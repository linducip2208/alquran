namespace QuranDesktop;

public sealed record TranslationOption(string Key, string Display, bool Rtl);

public static class Translations
{
    public static readonly List<TranslationOption> All = new()
    {
        new("ar_ayat", "Arab — Teks Uthmani", true),
        new("ar_ayat_safy", "Arab — Teks Sederhana", true),
        new("ar_mu", "Arab — Mushaf", true),
        new("ar_ma3any", "Arab — Ma'any", true),
        new("en_sh", "English — Saheeh International", false),
        new("fr_ha", "French", false),
        new("es_navio", "Spanish", false),
        new("de_bo", "German", false),
        new("it_piccardo", "Italian", false),
        new("pt_elhayek", "Portuguese", false),
        new("nl_siregar", "Dutch", false),
        new("bs_korkut", "Bosnian", false),
        new("sq_nahi", "Albanian", false),
        new("sv_bernstrom", "Swedish", false),
        new("tr_diyanet", "Turkish", false),
        new("ru_ku", "Russian", false),
        new("id_indonesian", "Indonesia", false),
        new("ms_basmeih", "Melayu", false),
        new("ku_asan", "Kurdish", true),
        new("pr_tagi", "Persia", true),
        new("ur_gl", "Urdu", true),
        new("ml_abdulhameed", "Malayalam", false),
    };

    public static TranslationOption? Find(string key) => All.FirstOrDefault(t => t.Key == key);
}
