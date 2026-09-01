using System.Drawing.Text;
using System.Reflection;
using System.Text.Json;

namespace QuranDesktop;

public static class MadinahFont
{
    private static FontFamily? _family;
    private static bool _tried;

    public static FontFamily? Family
    {
        get
        {
            Ensure();
            return _family;
        }
    }

    private static void Ensure()
    {
        if (_tried) return;
        _tried = true;
        try
        {
            var dir = Path.Combine(KsuAudio.CacheDir, "fonts");
            Directory.CreateDirectory(dir);
            var fontPath = Path.Combine(dir, "uthmanic_hafs_v22.ttf");

            if (!File.Exists(fontPath))
            {
                var asm = Assembly.GetExecutingAssembly();
                using var rs = asm.GetManifestResourceStream("UthmanicHafs.ttf");
                if (rs == null) return;
                using var fs = File.Create(fontPath);
                rs.CopyTo(fs);
            }

            var pfc = new PrivateFontCollection();
            pfc.AddFontFile(fontPath);
            _family = pfc.Families.FirstOrDefault();
        }
        catch
        {
            _family = null;
        }
    }

    public static Font Create(float size, FontStyle style = FontStyle.Regular)
    {
        Ensure();
        return _family != null
            ? new Font(_family, size, style)
            : new Font("Traditional Arabic", size, style);
    }
}

public static class MadinahText
{
    private static Dictionary<int, string>? _byId;
    private static bool _tried;

    public static bool Available
    {
        get
        {
            Ensure();
            return _byId != null && _byId.Count > 0;
        }
    }

    private static void Ensure()
    {
        if (_tried) return;
        _tried = true;
        try
        {
            var asm = Assembly.GetExecutingAssembly();
            using var stream = asm.GetManifestResourceStream("madinah-text.json");
            if (stream == null) return;
            using var doc = JsonDocument.Parse(stream);
            var entries = doc.RootElement;

            var flat = new List<string>();
            foreach (var item in entries.EnumerateArray())
            {
                string text = "";
                foreach (var el in item.EnumerateArray())
                {
                    if (el.ValueKind == JsonValueKind.String) text = el.GetString() ?? "";
                }
                flat.Add(text);
            }

            var byId = new Dictionary<int, string>();
            int idx = 0;
            for (int s = 1; s <= QuranData.SurahCount; s++)
            {
                int count = QuranData.SurahAyahCount(s);
                for (int a = 1; a <= count && idx < flat.Count; a++, idx++)
                {
                    byId[QuranData.AyaToId(s, a)] = flat[idx];
                }
            }

            if (byId.Count >= QuranData.AyaToId(QuranData.SurahCount, QuranData.SurahAyahCount(QuranData.SurahCount)) - 5)
            {
                _byId = byId;
            }
        }
        catch
        {
            _byId = null;
        }
    }

    public static string? Get(int surah, int ayah)
    {
        Ensure();
        return _byId != null && _byId.TryGetValue(QuranData.AyaToId(surah, ayah), out var t) ? t : null;
    }

    public static bool HasAyah(int surah, int ayah) => !string.IsNullOrWhiteSpace(Get(surah, ayah));

    public static Dictionary<int, string> GetSurah(int surah)
    {
        Ensure();
        var result = new Dictionary<int, string>();
        int count = QuranData.SurahAyahCount(surah);
        for (int a = 1; a <= count; a++)
        {
            result[a] = Get(surah, a) ?? "";
        }
        return result;
    }
}
