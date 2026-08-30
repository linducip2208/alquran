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
