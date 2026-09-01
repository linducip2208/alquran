namespace QuranDesktop;

public sealed record MushafType(string Key, string Display, string ImageBase, string PageKey, int DisplayHeight);

public static class MushafTypes
{
    public static readonly List<MushafType> All = new()
    {
        new("hafs", "Hafs", "https://quran.ksu.edu.sa/ayat/safahat1/", "Page", 690),
        new("warsh", "Rewayat Warsh", "https://quran.ksu.edu.sa/warsh/", "Page_warsh", 760),
        new("tajweed", "Hafs Tajweed", "https://quran.ksu.edu.sa/tajweed_png/", "Page2", 720),
    };

    public static MushafType? Find(string key) => All.FirstOrDefault(m => m.Key == key);

    /// <summary>Resolve mushafKey -> MushafType (fallback mushaf pertama bila key tidak dikenal). Tidak pernah null.</summary>
    public static MushafType ResolveMushaf(string mushafKey)
        => Find(mushafKey) ?? All[0];

    /// <summary>Resolve mushafKey -> PAGE KEY (Page/Page_warsh/Page2).
    /// QuranData.FindPage/PageCount/PageStart/PageAyahs menerima PAGE KEY, BUKAN mushaf key —
    /// helper ini mencegah salah kirim mushaf key yang menyebabkan key error.</summary>
    public static string ResolvePageKey(string mushafKey)
        => ResolveMushaf(mushafKey).PageKey;

    /// <summary>Halaman untuk (surah, ayah) pada mushaf tertentu.</summary>
    public static int FindMushafPage(string mushafKey, int surah, int ayah)
        => QuranData.FindPage(ResolvePageKey(mushafKey), surah, ayah);

    /// <summary>Jumlah halaman mushaf tertentu (via mushaf key, bukan page key).</summary>
    public static int MushafPageCount(string mushafKey)
        => QuranData.PageCount(ResolvePageKey(mushafKey));

    /// <summary>Ayah awal sebuah halaman pada mushaf tertentu (via mushaf key).</summary>
    public static (int Surah, int Ayah) MushafPageStart(string mushafKey, int page)
        => QuranData.PageStart(ResolvePageKey(mushafKey), page);
}

public sealed record VoiceTranslation(string Key, string Folder, string Display);

public static class VoiceTranslations
{
    public static readonly List<VoiceTranslation> All = new()
    {
        new("En", "English_Walk", "English"),
        new("Fr", "fr.leclerc_128kbs", "French"),
        new("Ur", "ur.khan_46kbs", "Urdu"),
        new("Bs", "Bosnian_Korkut_128kbps", "Bosnian"),
    };

    public static VoiceTranslation? Find(string key) => All.FirstOrDefault(v => v.Key == key);
}
