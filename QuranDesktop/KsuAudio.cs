namespace QuranDesktop;

public static class KsuAudio
{
    private const string BaseUrl = "https://quran.ksu.edu.sa/ayat/mp3";

    /// <summary>Root data offline: satu lokasi dengan executable, di subfolder "downloads".
    /// Semua konten offline permanen WAJIB di sini — bukan %TEMP%, bukan LocalApplicationData.</summary>
    public static string DataRoot => Path.Combine(AppContext.BaseDirectory, "downloads");

    /// <summary>Root cache offline (alias DataRoot). Semua service offline membaca lewat sini.</summary>
    public static string CacheDir => DataRoot;

    /// <summary>Audio qari & voice translation: downloads/audio/… (rel path: "audio/{folder}/001001.mp3").</summary>
    public static string AudioRoot => Path.Combine(DataRoot, "audio");

    /// <summary>Voice translation: downloads/voice/… (rel path: "voice/{folder}/001001.mp3").</summary>
    public static string VoiceRoot => Path.Combine(DataRoot, "voice");

    /// <summary>Folder kerja sementara — bukan konten offline permanen.
    /// DILARANG menulis file sementara di luar folder ini (bukan %TEMP%).</summary>
    public static string TempDir => Path.Combine(DataRoot, "temp");

    /// <summary>Lokasi cache lama (pra-migrasi): %LOCALAPPDATA%\QuranDesktop\audio.
    /// Hanya dibaca SEKALI oleh migrator; aplikasi tidak pernah menulis ke sini.</summary>
    public static string LegacyCacheDir => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "QuranDesktop", "audio");

    public static string AyahUrl(string reciterFolder, int surah, int ayah)
        => $"{BaseUrl}/{reciterFolder}/{surah:D3}{ayah:D3}.mp3";

    public static string BasmalaUrl(string reciterFolder)
        => $"{BaseUrl}/{reciterFolder}/001001.mp3";

    public static string AudhubillahUrl()
        => $"{BaseUrl}/all/audhubillah.mp3";

    public static string CachePath(string relative)
        => Path.Combine(CacheDir, relative.Replace('/', Path.DirectorySeparatorChar));

    /// <summary>
    /// (C) Uji izin tulis root downloads (di samping EXE) — TANPA fallback ke AppData/TEMP.
    /// Membuat folder, menulis & flush beberapa byte ke ".write-test", lalu menghapusnya.
    /// Return false bila folder tidak dapat ditulis — caller WAJIB menampilkan pesan
    /// dan TIDAK boleh diam-diam memindahkan data ke lokasi lain.
    /// </summary>
    public static bool EnsureWritableRoot(out string error)
    {
        try
        {
            Directory.CreateDirectory(DataRoot);
            string probe = Path.Combine(DataRoot, ".write-test");
            File.WriteAllBytes(probe, new byte[] { 0x51, 0x44 });
            using (var fs = new FileStream(probe, FileMode.Append, FileAccess.Write, FileShare.None))
            {
                fs.WriteByte(0x44);
                fs.Flush(flushToDisk: false);
            }
            File.Delete(probe);
            error = "";
            return true;
        }
        catch (Exception ex)
        {
            error = ex.Message;
            return false;
        }
    }

    /// <summary>
    /// (W/X) Pastikan halaman mushaf tersedia: unduh via DownloadManager engine —
    /// HTTP → downloads/mushaf/{key}/{page}.png.part → validasi ukuran + signature PNG → atomic move.
    /// File final tidak pernah setengah-tertulis / korup.
    /// </summary>
    public static async Task EnsureMushafPageAsync(string mushafKey, int page, HttpClient http, CancellationToken ct)
    {
        string rel = Path.Combine("mushaf", mushafKey, page + ".png").Replace('\\', '/');
        string local = CachePath(rel);
        if (DownloadManager.FileValid(local, 2048)) return;

        string url = (MushafTypes.Find(mushafKey) ?? MushafTypes.All[0]).ImageBase + page + ".png";
        if (!await DownloadManager.Shared.EnsureFileAsync(http, url, rel, ct))
        {
            throw new HttpRequestException($"Gagal mengunduh halaman mushaf {mushafKey} hal {page}");
        }
    }
}
