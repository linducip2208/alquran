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

    /// <summary>Folder kerja sementara — bukan konten offline permanen.</summary>
    public static string TempDir => Path.Combine(DataRoot, "temp");

    /// <summary>Lokasi cache lama (pra-migrasi): %LOCALAPPDATA%\QuranDesktop\audio.
    /// Dibaca oleh migrator; aplikasi tidak lagi menulis ke sini.</summary>
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

    public static async Task EnsureMushafPageAsync(string mushafKey, int page, HttpClient http, CancellationToken ct)
    {
        string rel = Path.Combine("mushaf", mushafKey, page + ".png");
        string local = CachePath(rel);
        if (File.Exists(local)) return;

        Directory.CreateDirectory(Path.GetDirectoryName(local)!);
        string url = MushafTypes.Find(mushafKey)!.ImageBase + page + ".png";
        using var resp = await http.GetAsync(url, ct);
        resp.EnsureSuccessStatusCode();
        await using var src = await resp.Content.ReadAsStreamAsync(ct);
        await using var dst = File.Create(local);
        await src.CopyToAsync(dst, ct);
    }
}
