using System.Text.Json;

namespace QuranDesktop;

internal sealed class AppSettings
{
    private static AppSettings? _current;
    public static AppSettings Current => _current ??= Load();

    public int Surah { get; set; } = 1;
    public int Ayah { get; set; } = 1;
    public string Qaree { get; set; } = "husary";
    public string Translation { get; set; } = "en_sh";
    public string Tafsir { get; set; } = "indonesian";
    public string PbTrans { get; set; } = "";
    public string Mosshaf { get; set; } = "hafs";
    public string Mode { get; set; } = "teks";
    public int Repeat { get; set; } = 1;
    public bool AutoNext { get; set; } = true;
    public bool PlayOnClick { get; set; } = true;
    public bool ShowTafsirPanel { get; set; } = true;
    public bool ShowTranslation { get; set; } = true;
    public bool ShowInlineTafsir { get; set; } = false;
    public bool ShowMushafOverlay { get; set; } = false;
    public bool TeacherMode { get; set; } = false;
    public bool DarkMode { get; set; } = false;
    public float Speed { get; set; } = 1f;
    public bool ReminderEnabled { get; set; } = false;
    public string ReminderTime { get; set; } = "20:00";
    public bool ShowDailyAyah { get; set; } = true;
    public float TranslationFontSize { get; set; } = 10.5f;
    public string PrayerCity { get; set; } = "Jakarta";
    public string PrayerCountry { get; set; } = "Indonesia";
    public int PrayerMethod { get; set; } = 20;
    public bool PrayerNotify { get; set; } = true;
    public bool FirstRunDone { get; set; } = false;
    public int Volume { get; set; } = 80;
    public float Zoom { get; set; } = 1f;
    public bool SinglePage { get; set; } = false;
    public string ZoomMode { get; set; } = "fit";
    public float ManualZoom { get; set; } = 1f;
    public int DetailPanelWidth { get; set; } = 320;
    public bool DetailPanelCollapsed { get; set; } = false;

    private static string Dir => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "QuranDesktop");

    private static string FilePath => Path.Combine(Dir, "settings.json");

    public static AppSettings Load()
    {
        try
        {
            if (File.Exists(FilePath))
            {
                using var reader = new StreamReader(File.OpenRead(FilePath));
                var json = reader.ReadToEnd();
                var s = JsonSerializer.Deserialize<AppSettings>(json);
                if (s != null)
                {
                    if (s.Translation == "en.sahih") s.Translation = "en_sh";
                    else if (s.Translation == "id.indonesian") s.Translation = "id_indonesian";
                    else if (s.Translation is "en.pickthall" or "en.yusufali") s.Translation = "en_sh";
                    if (s.Surah < 1 || s.Surah > 114) s.Surah = 1;
                    var max = QuranData.SurahAyahCount(s.Surah);
                    if (s.Ayah < 1 || s.Ayah > max) s.Ayah = 1;
                    if (s.Volume is < 0 or > 100) s.Volume = 80;
                    if (s.Zoom is < 0.4f or > 2.5f) s.Zoom = 1f;
                    return s;
                }
            }
        }
        catch
        {
        }
        return new AppSettings();
    }

    public void Save()
    {
        try
        {
            Directory.CreateDirectory(Dir);
            File.WriteAllText(FilePath, JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch
        {
        }
    }
}
