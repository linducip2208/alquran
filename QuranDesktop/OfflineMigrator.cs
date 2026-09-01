using System.Collections.Concurrent;

namespace QuranDesktop;

/// <summary>
/// Migrasi cache lama (%LOCALAPPDATA%\QuranDesktop\audio) ke root baru di samping exe
/// (downloads/). Idempotent: resource yang sudah ada di root baru TIDAK ditimpa.
/// Best-effort & tidak pernah melempar exception ke caller.
/// </summary>
public static class OfflineMigrator
{
    private static int _started;
    /// <summary>Task migrasi yang sedang berjalan (null bila belum dimulai / sudah selesai tanpa start).</summary>
    public static Task? Current { get; private set; }

    /// <summary>(B) Marker satu kali: setelah ada, %LOCALAPPDATA% TIDAK dipindai lagi di setiap startup.</summary>
    public static string MarkerPath => Path.Combine(KsuAudio.DataRoot, ".migration-v1-complete");

    public static bool MigrationComplete
    {
        get { try { return File.Exists(MarkerPath); } catch { return false; } }
    }

    private static void WriteMarker()
    {
        try
        {
            Directory.CreateDirectory(KsuAudio.DataRoot);
            File.WriteAllText(MarkerPath, DateTime.UtcNow.ToString("O"));
        }
        catch
        {
        }
    }

    /// <summary>Mulai migrasi sekali di background. Aman dipanggil berkali-kali.
    /// Bila marker sudah ada: TIDAK memindai %LOCALAPPDATA% sama sekali.</summary>
    public static void EnsureStarted()
    {
        if (Interlocked.Exchange(ref _started, 1) != 0)
        {
            return;
        }
        if (MigrationComplete)
        {
            return;
        }
        Current = Task.Run(() =>
        {
            try { Run(); }
            catch { }
            OfflineContentService.Instance.InvalidateAll();
        });
    }

    /// <summary>
    /// Jalankan migrasi (sinkron). Parameter dapat dioverride untuk unit test.
    /// Aturan: folder lama ada DAN root baru belum memiliki resource tersebut → pindahkan.
    /// Hanya boleh MEMBACA cache lama — tidak pernah menulis ke %LOCALAPPDATA%.
    /// Setelah selesai (sukses atau tidak ada data), marker dibuat agar startup berikutnya skip.
    /// </summary>
    public static int Run(string? oldRoot = null, string? newRoot = null)
    {
        bool useDefaults = oldRoot == null && newRoot == null;
        oldRoot ??= KsuAudio.LegacyCacheDir;
        newRoot ??= KsuAudio.DataRoot;
        if (useDefaults && MigrationComplete) return 0;
        int moved = 0;

        try
        {
            if (!Directory.Exists(oldRoot))
            {
                // tidak ada cache lama — langsung tandai selesai agar startup berikutnya tidak memindai lagi
                if (useDefaults) WriteMarker();
                return 0;
            }
            Directory.CreateDirectory(newRoot);

            var known = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            { "mushaf", "hilites", "teks", "tafsir", "fonts", "recordings" };
            var voiceFolders = VoiceTranslations.All.Select(v => v.Folder).ToHashSet(StringComparer.OrdinalIgnoreCase);
            var reciterFolders = Reciters.All.Select(r => r.Folder).ToHashSet(StringComparer.OrdinalIgnoreCase);

            // 1) folder struktural → pindah utuh (mushaf/hilites/teks/tafsir dulu — kecil & dipakai reader)
            foreach (var name in new[] { "tafsir", "teks", "hilites", "fonts", "recordings", "mushaf" })
            {
                if (MergeDir(Path.Combine(oldRoot, name), Path.Combine(newRoot, name))) moved++;
            }

            // 2) folder audio: qari → audio/, voice translation → voice/, lainnya → audio/
            foreach (var dir in Directory.EnumerateDirectories(oldRoot))
            {
                string name = Path.GetFileName(dir);
                if (known.Contains(name)) continue;
                // buang folder sisa selftest — bukan data user
                if (name.StartsWith("test_", StringComparison.OrdinalIgnoreCase)
                    || name.StartsWith("selftest", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }
                string target = voiceFolders.Contains(name)
                    ? Path.Combine(newRoot, "voice", name)
                    : Path.Combine(newRoot, "audio", name);
                if (MergeDir(dir, target)) moved++;
            }

            // 3) hapus folder lama bila sudah kosong
            try
            {
                if (!Directory.EnumerateFileSystemEntries(oldRoot).Any()) Directory.Delete(oldRoot);
            }
            catch { }

            // (B) tandai migrasi selesai — startup berikutnya tidak memindai %LOCALAPPDATA% lagi
            WriteMarker();
        }
        catch
        {
        }
        return moved;
    }

    /// <summary>
    /// Pindahkan folder src → dst. Bila dst sudah ada: MERGE per file — file yang sudah ada
    /// di root baru TIDAK ditimpa, file lain tetap dipindah (tidak ada data user hilang).
    /// Aman lintas volume (copy+delete fallback).
    /// </summary>
    private static bool MergeDir(string src, string dst)
    {
        try
        {
            if (!Directory.Exists(src)) return false;
            bool movedAny = false;

            if (!Directory.Exists(dst))
            {
                try
                {
                    Directory.Move(src, dst); // sama volume & dst kosong → rename cepat
                    return true;
                }
                catch (IOException)
                {
                    // lintas volume → lanjut merge per file di bawah
                    Directory.CreateDirectory(dst);
                }
                catch
                {
                    return false;
                }
            }

            foreach (var file in Directory.EnumerateFiles(src))
            {
                string name = Path.GetFileName(file);
                string target = Path.Combine(dst, name);
                if (File.Exists(target)) continue; // resource di root baru menang
                try
                {
                    File.Move(file, target);
                    movedAny = true;
                }
                catch (IOException)
                {
                    try { File.Copy(file, target, overwrite: false); File.Delete(file); movedAny = true; }
                    catch { }
                }
                catch { }
            }
            foreach (var sub in Directory.EnumerateDirectories(src))
            {
                if (MergeDir(sub, Path.Combine(dst, Path.GetFileName(sub)))) movedAny = true;
            }
            try
            {
                if (!Directory.EnumerateFileSystemEntries(src).Any()) Directory.Delete(src);
            }
            catch { }
            return movedAny;
        }
        catch
        {
            return false;
        }
    }
}
