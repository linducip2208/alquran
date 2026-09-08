using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text.Json;

namespace QuranDesktop;

/// <summary>Progress live migrasi cache lama â†’ downloads/ di samping EXE.</summary>
public sealed record MigrationProgress(
    long FilesTotal, long FilesDone, long BytesTotal, long BytesDone, string CurrentRelativePath)
{
    public int Percent => FilesTotal <= 0 ? 0 : (int)Math.Min(100, FilesDone * 100 / FilesTotal);
    public bool CompletedStage { get; init; }
    public bool Cancelled { get; init; }
}

/// <summary>
/// Migrasi konten download lama ke root offline baru (<see cref="KsuAudio.DataRoot"/> = samping EXE).
/// Sumber: %LOCALAPPDATA%\QuranDesktop\downloads (regression 1.4.x) dan â€” pra-v1 â€”
/// folder qari langsung di %LOCALAPPDATA%\QuranDesktop.
/// ATURAN:
/// - per-file: coba File.Move; bila gagal (cross-volume) â†’ copy â†’ flush â†’ verify (size/SHA-256) â†’ baru hapus source.
/// - destination sudah ada & valid â†’ source boleh dihapus SETELAH destination diverifikasi; TIDAK pernah overwrite.
/// - marker v2 hanya ditulis SETELAH seluruh scan sukses (bukan saat cancel).
/// - resumable: startup berikutnya melanjutkan dari file yang tersisa.
/// - settings.json / progress.json / error.log BUKAN konten download â€” TIDAK pernah disentuh.
/// - dilarang Directory.Delete(source, true) selama migrasi; folder legacy dihapus bottom-up
///   hanya bila benar-benar kosong, dan hanya subfolder "downloads" â€” tidak pernah root QuranDesktop.
/// </summary>
public static class OfflineMigrator
{
    private static int _started;
    /// <summary>Task migrasi yang sedang berjalan (null bila belum dimulai / sudah selesai tanpa start).</summary>
    public static Task? Current { get; private set; }

    /// <summary>Marker regresi 1.4.x: konten sudah dipindah AppData â†’ samping EXE.</summary>
    public static string MarkerPath => Path.Combine(KsuAudio.DataRoot, ".migration-appdata-to-exe-v2-complete");

    public static bool MigrationComplete
    {
        get { try { return File.Exists(MarkerPath); } catch { return false; } }
    }

    private sealed record MarkerData(string Source, string Destination, string CompletedUtc, long FilesMoved, long BytesMoved);

    private static void WriteMarker(string source, long filesMoved, long bytesMoved)
    {
        try
        {
            Directory.CreateDirectory(KsuAudio.DataRoot);
            var data = new MarkerData(source, KsuAudio.DataRoot, DateTime.UtcNow.ToString("O"), filesMoved, bytesMoved);
            File.WriteAllText(MarkerPath, JsonSerializer.Serialize(data, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch
        {
        }
    }

    /// <summary>Hasil scan legacy (untuk dialog konfirmasi "Cache lama ditemukan").</summary>
    public sealed record LegacyInfo(bool Found, long Files, long Bytes, string SourcePath);

    /// <summary>true bila cache legacy (AppData) berisi file dan migrasi v2 belum selesai —
    /// dipakai untuk memutuskan apakah dialog migrasi perlu tampil saat startup.</summary>
    public static bool HasLegacyData
    {
        get
        {
            try
            {
                if (MigrationComplete || KsuAudio.SamePathAsLegacy) return false;
                var info = InspectLegacy();
                if (info.Found) return true;
                // pra-v1: qari langsung di %LOCALAPPDATA%\QuranDesktop (bukan settings/log)
                string v1 = KsuAudio.LegacyCacheDir;
                if (!Directory.Exists(v1)) return false;
                return Directory
                    .EnumerateFileSystemEntries(v1)
                    .Any(e => Path.GetFileName(e) is not
                        ("settings.json" or "progress.json" or "error.log" or "error.previous.log"
                            or "downloads" or "audio" or "voice" or "temp"));
            }
            catch { return false; }
        }
    }

    /// <summary>Hitung isi legacy root (file + bytes) tanpa memindahkan apa pun. Cepat: hanya enumerasi.</summary>
    public static LegacyInfo InspectLegacy(string? legacyRoot = null)
    {
        legacyRoot ??= KsuAudio.LegacyDownloadsRoot;
        try
        {
            if (KsuAudio.SamePathAsLegacy || !Directory.Exists(legacyRoot)) return new LegacyInfo(false, 0, 0, legacyRoot);
            long files = 0, bytes = 0;
            foreach (var f in Directory.EnumerateFiles(legacyRoot, "*", SearchOption.AllDirectories))
            {
                files++;
                try { bytes += new FileInfo(f).Length; } catch { }
            }
            return new LegacyInfo(files > 0, files, bytes, legacyRoot);
        }
        catch { return new LegacyInfo(false, 0, 0, legacyRoot); }
    }

    /// <summary>Mulai migrasi sekali di background. Aman dipanggil berkali-kali.
    /// Bila marker v2 sudah ada: TIDAK memindai AppData sama sekali.</summary>
    public static void EnsureStarted(IProgress<MigrationProgress>? progress = null)
    {
        if (Interlocked.Exchange(ref _started, 1) != 0) return;
        if (MigrationComplete) return;
        Current = Task.Run(() =>
        {
            try { Run(progress: progress); }
            catch { }
            OfflineContentService.Instance.InvalidateAll();
        });
    }

    /// <summary>Token cancel migrasi aktif (dipakai UI). Cancel: file sukses tetap aman, source tersisa, tanpa marker.</summary>
    private static CancellationTokenSource? _cts;

    public static void Cancel()
    {
        try { _cts?.Cancel(); } catch { }
    }

    /// <summary>
    /// Jalankan migrasi (sinkron). Parameter dapat dioverride untuk unit test / sandbox.
    /// <paramref name="force"/> = true â†’ scan ulang meski marker sudah ada (impor manual).
    /// Return: jumlah file yang dipindahkan. Progress dilaporkan lewat <paramref name="progress"/>.
    /// </summary>
    public static (int FilesMoved, long BytesMoved, bool Cancelled) Run(
        string? oldRoot = null, string? newRoot = null,
        IProgress<MigrationProgress>? progress = null,
        bool force = false,
        CancellationToken ct = default,
        bool remapLegacy = false)
    {
        bool useDefaults = oldRoot == null && newRoot == null;
        oldRoot ??= KsuAudio.LegacyDownloadsRoot;
        newRoot ??= KsuAudio.DataRoot;

        // guard self-copy: source == destination â†’ tidak ada yang perlu dilakukan
        if (SameDir(oldRoot!, newRoot!)) return (0, 0, false);
        if (useDefaults)
        {
            if (MigrationComplete && !force) return (0, 0, false);
            if (KsuAudio.SamePathAsLegacy) return (0, 0, false);
            _cts = new CancellationTokenSource();
            ct = _cts.Token;
        }

        int filesMoved = 0;
        long bytesMoved = 0;
        try
        {
            // default: dua fase — (1) regression 1.4.x %LOCALAPPDATA%\QuranDesktop\downloads 1:1,
            // (2) pra-v1 folder qari langsung di %LOCALAPPDATA%\QuranDesktop (remapLegacy)
            bool doV2 = Directory.Exists(oldRoot!) && !SameDir(oldRoot!, newRoot!);
            bool doV1 = useDefaults && Directory.Exists(KsuAudio.LegacyCacheDir)
                && !SameDir(KsuAudio.LegacyCacheDir, newRoot!);

            if (!doV2 && !doV1)
            {
                if (useDefaults) WriteMarker(oldRoot!, 0, 0);
                return (0, 0, false);
            }

            Directory.CreateDirectory(newRoot!);

            long filesTotal = 0, bytesTotal = 0;
            var jobs = new List<(string Src, string Dst, string Rel, long Bytes)>();

            void CollectDir(string srcDir, string dstDir, string relPrefix)
            {
                try
                {
                    foreach (var f in Directory.EnumerateFiles(srcDir))
                    {
                        ct.ThrowIfCancellationRequested();
                        string name = Path.GetFileName(f);
                        long len = 0;
                        try { len = new FileInfo(f).Length; } catch { }
                        string rel = relPrefix.Length == 0 ? name : relPrefix + "/" + name;
                        jobs.Add((f, Path.Combine(dstDir, name), rel, len));
                        filesTotal++;
                        bytesTotal += len;
                    }
                    foreach (var d in Directory.EnumerateDirectories(srcDir))
                    {
                        string dn = Path.GetFileName(d);
                        if (dn.StartsWith("test_", StringComparison.OrdinalIgnoreCase)
                            || dn.StartsWith("selftest", StringComparison.OrdinalIgnoreCase))
                        {
                            continue; // buang folder sisa selftest — bukan data user
                        }
                        CollectDir(d, Path.Combine(dstDir, dn), relPrefix.Length == 0 ? dn : relPrefix + "/" + dn);
                    }
                }
                catch (OperationCanceledException) { throw; }
                catch { }
            }

            // root relatif konten download yang sah — folder lain di luar ini di-skip
            static bool IsKnownContentRoot(string n) => n is "mushaf" or "hilites" or "teks" or "tafsir"
                or "fonts" or "recordings" or "audio" or "voice" or "temp";

            var voiceFolders = VoiceTranslations.All.Select(v => v.Folder).ToHashSet(StringComparer.OrdinalIgnoreCase);

            if (doV2)
            {
                // struktur sudah "downloads": salin seluruh subtree 1:1
                CollectDir(oldRoot!, newRoot!, "");
            }
            if (doV1)
            {
                // pra-v1: folder struktural langsung di root + folder qari/voice per nama
                foreach (var name in new[] { "tafsir", "teks", "hilites", "fonts", "recordings", "mushaf" })
                {
                    string src = Path.Combine(KsuAudio.LegacyCacheDir, name);
                    if (Directory.Exists(src)) CollectDir(src, Path.Combine(newRoot!, name), name);
                }
                foreach (var dir in Directory.EnumerateDirectories(KsuAudio.LegacyCacheDir))
                {
                    string name = Path.GetFileName(dir);
                    if (IsKnownContentRoot(name)) continue;
                    if (name.StartsWith("test_", StringComparison.OrdinalIgnoreCase)
                        || name.StartsWith("selftest", StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }
                    string sub = voiceFolders.Contains(name) ? "voice" : "audio";
                    CollectDir(dir, Path.Combine(newRoot!, sub, name), sub + "/" + name);
                }
            }

            if (filesTotal == 0)
            {
                if (useDefaults) WriteMarker(oldRoot!, 0, 0);
                return (0, 0, false);
            }

            var (moved, bytes) = MoveAll(jobs, filesTotal, bytesTotal, progress, ct);
            filesMoved = moved;
            bytesMoved = bytes;

            if (!ct.IsCancellationRequested)
            {
                // hapus folder legacy kosong bottom-up — hanya legacy tree, TIDAK pernah root QuranDesktop
                TryPruneEmptyDirs(oldRoot!, stopAt: Path.GetDirectoryName(Path.TrimEndingDirectorySeparator(oldRoot!)));
                if (doV1)
                {
                    // pra-v1: hapus folder struktural kosong di %LOCALAPPDATA%\QuranDesktop — root tidak disentuh
                    foreach (var name in new[] { "tafsir", "teks", "hilites", "fonts", "recordings", "mushaf" })
                    {
                        TryPruneEmptyDirs(Path.Combine(KsuAudio.LegacyCacheDir, name),
                            stopAt: Path.GetDirectoryName(Path.TrimEndingDirectorySeparator(KsuAudio.LegacyCacheDir)));
                    }
                }
                if (useDefaults)
                {
                    WriteMarker(oldRoot!, filesMoved, bytesMoved);
                }
            }
        }
        catch (OperationCanceledException)
        {
            // cancel: file sukses tetap aman, source tersisa, TIDAK ada marker
            return (filesMoved, bytesMoved, true);
        }
        catch
        {
            // kegagalan tak terduga: biarkan resumable â€” jangan tulis marker
            return (filesMoved, bytesMoved, false);
        }
        return (filesMoved, bytesMoved, false);
    }

    private static (int, long) MoveAll(
        List<(string Src, string Dst, string Rel, long Bytes)> jobs,
        long filesTotal, long bytesTotal,
        IProgress<MigrationProgress>? progress, CancellationToken ct)
    {
        int moved = 0;
        long bytesDone = 0;

        foreach (var (src, dst, rel, bytes) in jobs)
        {
            ct.ThrowIfCancellationRequested();
            progress?.Report(new MigrationProgress(filesTotal, moved, bytesTotal, bytesDone, rel));
            if (MoveOneFile(src, dst, rel))
            {
                moved++;
                bytesDone += bytes;
            }
            else
            {
                // file gagal (terkunci dsb.) â€” biarkan: migrasi resumable, file tetap di source
            }
        }
        progress?.Report(new MigrationProgress(filesTotal, moved, bytesTotal, bytesDone, "")
        {
            CompletedStage = true,
        });
        return (moved, bytesDone);
    }

    /// <summary>Pindah satu file dengan verifikasi. Return true bila file destination valid
    /// dan source sudah dibersihkan. TIDAK PERNAH overwrite destination valid.</summary>
    private static bool MoveOneFile(string src, string dst, string rel)
    {
        try
        {
            long srcLen;
            try { srcLen = new FileInfo(src).Length; }
            catch { return false; }

            if (File.Exists(dst))
            {
                long dstLen;
                try { dstLen = new FileInfo(dst).Length; }
                catch { return false; }

                if (dstLen == srcLen && dstLen > 0)
                {
                    // ukuran sama â†’ asumsi sama; source boleh dihapus (destination valid)
                    TryDelete(src);
                    return true;
                }
                // ukuran beda â†’ validasi destination dengan aturan file; valid â†’ jangan timpa;
                // destination corrupt â†’ replace SETELAH source dipastikan ada
                if (IsFileValid(dst, rel))
                {
                    TryDelete(src);
                    return true;
                }
                if (srcLen <= 0) return false; // source kosong & destination rusak â†’ jangan rusakkan dua-duanya
                File.Delete(dst);
            }

            Directory.CreateDirectory(Path.GetDirectoryName(dst)!);

            if (srcLen <= 0)
            {
                // file sumber 0 byte â€” salin apa adanya agar tidak dianggap hilang
                File.WriteAllBytes(dst, Array.Empty<byte>());
                TryDelete(src);
                return true;
            }

            try
            {
                File.Move(src, dst);
                return true;
            }
            catch (IOException)
            {
                // lintas volume / terkunci â†’ copy + flush + verify + delete
                CopyVerified(src, dst, rel);
                TryDelete(src);
                return true;
            }
            catch (UnauthorizedAccessException)
            {
                CopyVerified(src, dst, rel);
                TryDelete(src);
                return true;
            }
        }
        catch (OperationCanceledException) { throw; }
        catch
        {
            return false;
        }
    }

    /// <summary>Copy dengan flush ke disk lalu verify SHA-256 (untuk lintas volume).
    /// Throw bila hasil copy tidak identik â€” source TIDAK dihapus.</summary>
    private static void CopyVerified(string src, string dst, string rel)
    {
        string tmp = dst + ".migtmp";
        try
        {
            using (var input = new FileStream(src, FileMode.Open, FileAccess.Read, FileShare.Read))
            using (var output = new FileStream(tmp, FileMode.Create, FileAccess.Write, FileShare.None))
            {
                input.CopyTo(output);
                output.Flush(flushToDisk: true);
            }
            string h1 = Sha256(src), h2 = Sha256(tmp);
            if (h1.Length == 0 || h1 != h2)
            {
                throw new IOException("copy cross-volume gagal verifikasi SHA-256: " + rel);
            }
            File.Move(tmp, dst, overwrite: true);
        }
        finally
        {
            try { if (File.Exists(tmp)) File.Delete(tmp); } catch { }
        }
    }

    private static string Sha256(string path)
    {
        try
        {
            using var fs = File.OpenRead(path);
            using var sha = SHA256.Create();
            return Convert.ToHexString(sha.ComputeHash(fs));
        }
        catch { return ""; }
    }

    /// <summary>Validasi konten per jenis file (aturan #5) untuk keputusan delete source / replace.</summary>
    private static bool IsFileValid(string path, string rel)
    {
        try
        {
            var fi = new FileInfo(path);
            if (!fi.Exists || fi.Length <= 0) return false;
            if (rel.EndsWith(".png", StringComparison.OrdinalIgnoreCase))
            {
                // PNG signature check (lightweight — tanpa min bytes ketat untuk file legacy kecil)
                var head = new byte[8];
                using var fs = File.OpenRead(path);
                if (fs.Read(head, 0, 8) < 8) return false;
                return head[0] == 0x89 && head[1] == 0x50 && head[2] == 0x4E && head[3] == 0x47;
            }
            if (rel.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
            {
                using var fs = File.OpenRead(path);
                using var doc = JsonDocument.Parse(fs);
                return doc.RootElement.ValueKind is JsonValueKind.Object or JsonValueKind.Array;
            }
            if (rel.EndsWith(".mp3", StringComparison.OrdinalIgnoreCase)) return fi.Length >= 4096;
            return true;
        }
        catch { return false; }
    }

    private static void TryDelete(string path)
    {
        try { File.Delete(path); } catch { }
    }

    private static bool SameDir(string a, string b)
    {
        try
        {
            return string.Equals(
                Path.TrimEndingDirectorySeparator(Path.GetFullPath(a)),
                Path.TrimEndingDirectorySeparator(Path.GetFullPath(b)),
                StringComparison.OrdinalIgnoreCase);
        }
        catch { return false; }
    }

    /// <summary>Hapus folder kosong bottom-up (terdalam dulu) di dalam <paramref name="dir"/>,
    /// lalu folder itu sendiri bila kosong. Tidak pernah menembus di atas legacy tree.</summary>
    private static void TryPruneEmptyDirs(string dir, string? stopAt)
    {
        try
        {
            string full = Path.TrimEndingDirectorySeparator(Path.GetFullPath(dir));
            if (!Directory.Exists(full)) return;
            foreach (var sub in Directory.EnumerateDirectories(full, "*", SearchOption.AllDirectories)
                         .OrderByDescending(d => d.Length))
            {
                try
                {
                    if (!Directory.EnumerateFileSystemEntries(sub).Any()) Directory.Delete(sub);
                }
                catch { }
            }
            try
            {
                if (!Directory.EnumerateFileSystemEntries(full).Any()) Directory.Delete(full);
            }
            catch { }
        }
        catch { }
    }
}
