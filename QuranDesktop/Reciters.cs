namespace QuranDesktop;

public static class Reciters
{
    public static readonly List<Reciter> All = new()
    {
        new("husary", "Husary_64kbps", "Mahmoud Khalil Al-Husary"),
        new("husary.m", "Husary_Mujawwad_64kbps", "Al-Husary (Mujawwad)"),
        new("husary.t", "Hussary.teacher_64kbps", "Al-Husary (Teacher)"),
        new("husary.tq", "Hussary.teacher_32kbps", "Al-Husary (Teacher, slow)"),
        new("husary.w", "warsh_husary_64kbps", "Al-Husary (Warsh)"),
        new("husary.e", "husary_qasr_64kbps", "Al-Husary (Qasr)"),
        new("husary.q", "Husary_40kbps", "Al-Husary (40 kbps)"),
        new("huzaify", "Hudhaify_64kbps", "Ali Al-Hudhaify"),
        new("huzaify.q", "Hudhaify_32kbps", "Al-Hudhaify (32 kbps)"),
        new("sudais", "Abdurrahmaan_As-Sudais_64kbps", "Abdurrahman As-Sudais"),
        new("shuraym", "Saood_ash-Shuraym_64kbps", "Saud Ash-Shuraim"),
        new("maher", "Maher_AlMuaiqly_64kbps", "Maher Al-Muaiqly"),
        new("ghamidi", "Ghamadi_40kbps", "Saad Al-Ghamdi"),
        new("qatami", "Nasser_Alqatami_128kbps", "Nasser Al-Qatami"),
        new("jibreel", "Muhammad_Jibreel_64kbps", "Muhammad Jibreel"),
        new("shatree", "Abu_Bakr_Ash-Shaatree_64kbps", "Abu Bakr Ash-Shatri"),
        new("ajamy", "Ahmed_ibn_Ali_al-Ajamy_64kbps", "Ahmed Al-Ajamy"),
        new("afasy", "Alafasy_64kbps", "Mishary Al-Afasy"),
        new("basfar", "Abdullah_Basfar_64kbps", "Abdullah Basfar"),
        new("absulbasit", "Abdul_Basit_Murattal_64kbps", "Abdul Basit (Murattal)"),
        new("absulbasit.m", "AbdulSamad_64kbps", "Abdul Basit (Mujawwad)"),
        new("absulbasit.q", "Abdul_Basit_Murattal_40kbps", "Abdul Basit (40 kbps)"),
        new("minshawy", "Minshawy_Murattal_128kbps", "Al-Minshawi (Murattal)"),
        new("minshawy.m", "Minshawy_Mujawwad_64kbps", "Al-Minshawi (Mujawwad)"),
        new("minshawy.t", "Minshawy_Teacher_128kbps", "Al-Minshawi (Teacher)"),
        new("minshawy.q", "Minshawy_Murattal_48kbps", "Al-Minshawi (48 kbps)"),
        new("ayyoub", "Muhammad_Ayyoub_64kbps", "Muhammad Ayyoub"),
        new("rifai", "Hani_Rifai_192kbps", "Hani Ar-Rifai"),
        new("awwad", "Abdullaah_3awwaad_Al-Juhaynee_128kbps", "Abdullah Awad Al-Juhany"),
        new("qasim", "Muhsin_Al_Qasim_192kbps", "Muhsin Al-Qasim"),
        new("tablawy", "Mohammad_al_Tablaway_64kbps", "Mohamed Al-Tablawi"),
        new("tunaiji", "tunaiji_64kbps", "Ibrahim Al-Dossari (Tunaiji)"),
        new("khaleefa", "khaleefa_96kbps", "Khalifa Al-Tunaiji"),
        new("yaser", "Yasser_Ad-Dussary_128kbps", "Yasser Ad-Dussary"),
        new("abdulkareem", "Muhammad_AbdulKareem_128kbps", "Muhammad AbdulKareem"),
        new("dosary", "warsh_dossary_128kbps", "Warsh — Dossary"),
        new("dosary.q", "warsh_dossary_32kbps", "Warsh — Dossary (slow)"),
        new("yasin", "warsh_yassin_64kbps", "Warsh — Yassin"),
        new("fares", "Fares_Abbad_64kbps", "Fares Abbad"),
        new("salamah", "Yaser_Salamah_128kbps", "Yaser Salamah"),
        new("mostafa", "Mostafa_Ismail_128kbps", "Mostafa Ismail"),
        new("jaber", "Ali_Jaber_64kbps", "Ali Jaber"),
        new("ayman", "Ayman_Sowaid_64kbps", "Ayman Sowaid"),
    };

    public static Reciter? Find(string key) => All.FirstOrDefault(r => r.Key == key);
}
