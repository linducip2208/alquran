namespace QuranDesktop;

public sealed record TafsirOption(string Key, string Display, bool IsArabic);

public static class Tafsirs
{
    public static readonly List<TafsirOption> All = new()
    {
        new("indonesian", "Tafsir Jalalain (Indonesia)", false),
        new("muyassar", "Tafsir Al-Muyassar", true),
        new("sa3dy", "Tafsir As-Sa'dy", true),
        new("baghawy", "Tafsir Al-Baghawy", true),
        new("katheer", "Tafsir Ibn Katheer", true),
        new("qortoby", "Tafsir Al-Qortoby", true),
        new("tabary", "Tafsir At-Tabary", true),
        new("e3rab", "I'rab", true),
        new("russian", "Tafhim (Russian)", false),
    };

    public static TafsirOption? Find(string key) => All.FirstOrDefault(t => t.Key == key);
}
