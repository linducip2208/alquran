using System.Reflection;
using System.Text;

namespace QuranDesktop;

public static class QuranData
{
    private static readonly Lazy<Dictionary<string, int[][]>> _data = new(Load);

    private static Dictionary<string, int[][]> Load()
    {
        var asm = Assembly.GetExecutingAssembly();
        using var stream = asm.GetManifestResourceStream("quran-data.js")
            ?? throw new InvalidOperationException("quran-data.js embedded resource missing");
        using var reader = new StreamReader(stream, Encoding.UTF8);
        string js = reader.ReadToEnd();

        var result = new Dictionary<string, int[][]>();
        foreach (var name in new[] { "Sura", "Page", "Page_warsh", "Page2", "Juz" })
        {
            result[name] = ParseArray(js, name);
        }
        _sajda = ParseSajda(js);
        return result;
    }

    private static List<(int Surah, int Ayah, bool Obligatory)> ParseSajda(string js)
    {
        var list = new List<(int, int, bool)>();
        var marker = "QuranData.Sajda = [";
        int start = js.IndexOf(marker, StringComparison.Ordinal);
        if (start < 0) return list;

        int end = js.IndexOf("];", start, StringComparison.Ordinal);
        if (end < 0) return list;

        var segment = js.Substring(start, end - start);
        var rx = new System.Text.RegularExpressions.Regex(
            @"\[\s*(\d+)\s*,\s*(\d+)\s*,\s*'(recommended|obligatory)'\s*\]");
        foreach (System.Text.RegularExpressions.Match m in rx.Matches(segment))
        {
            list.Add((int.Parse(m.Groups[1].Value), int.Parse(m.Groups[2].Value), m.Groups[3].Value == "obligatory"));
        }
        return list;
    }

    private static List<(int Surah, int Ayah, bool Obligatory)> _sajda = new();

    private static int[][] ParseArray(string js, string name)
    {
        var marker = "QuranData." + name + " = [";
        int start = js.IndexOf(marker, StringComparison.Ordinal);
        if (start < 0) throw new InvalidOperationException(name + " not found in quran-data.js");
        start += marker.Length;

        var rows = new List<int[]>();
        var nums = new List<int>();
        var sb = new StringBuilder();
        int depth = 1;

        for (int i = start; i < js.Length && depth > 0; i++)
        {
            char c = js[i];

            if (c == '/' && i + 1 < js.Length && js[i + 1] == '/')
            {
                while (i < js.Length && js[i] != '\n') i++;
                continue;
            }

            if (c == '[')
            {
                depth++;
                nums.Clear();
            }
            else if (c == ']')
            {
                depth--;
                if (depth == 0) break;
                if (sb.Length > 0)
                {
                    nums.Add(int.Parse(sb.ToString()));
                    sb.Clear();
                }
                rows.Add(nums.ToArray());
                nums = new List<int>();
            }
            else if (c == ',')
            {
                if (sb.Length > 0)
                {
                    nums.Add(int.Parse(sb.ToString()));
                    sb.Clear();
                }
            }
            else if (char.IsDigit(c))
            {
                sb.Append(c);
            }
            else if (c == ';')
            {
                break;
            }
        }

        return rows.ToArray();
    }

    /// <summary>Ambil tabel berdasarkan key. Key tidak dikenal (mis. mushaf key "tajweed"
    /// terkirim sebagai pageKey) TIDAK melempar exception — fallback ke tabel "Page"
    /// agar scan/UI tidak pernah crash karena salah key.</summary>
    private static int[][] Table(string name)
        => _data.Value.TryGetValue(name, out var t) ? t : _data.Value["Page"];

    public static int SurahCount { get; } = ComputeSurahCount();

    private static int ComputeSurahCount()
    {
        var t = Table("Sura");
        int n = t.Length - 1;
        // Buang baris sentinel di akhir tabel (mis. [6236,1] / baris kosong)
        while (n >= 1 && (t[n].Length == 0 || t[n].Length < 4 || t[n][0] > TotalAyahCountRaw))
        {
            n--;
        }
        return n;
    }

    private const int TotalAyahCountRaw = 6236;

    /// <summary>Total ayat seluruh Al-Qur'an (6.236) — dihitung dari tabel, bukan hard-code per surah.</summary>
    public static int TotalAyahCount => AyaToId(SurahCount, SurahAyahCount(SurahCount)) + 1;

    public static int SurahStartId(int surah) => Table("Sura")[surah][0];

    public static int SurahAyahCount(int surah) => Table("Sura")[surah][1];

    public static int AyaToId(int surah, int ayah)
        => (surah > SurahCount ? int.MaxValue / 2 : SurahStartId(surah)) + ayah - 1;

    public static (int Surah, int Ayah) IdToAya(int id)
    {
        for (int s = SurahCount; s >= 1; s--)
        {
            if (SurahStartId(s) <= id) return (s, id - SurahStartId(s) + 1);
        }
        return (1, 1);
    }

    public static int PageCount(string pageKey)
    {
        var t = Table(pageKey);
        int count = t.Length - 1;
        while (count >= 1 && t[count].Length > 0 && t[count][0] > SurahCount)
        {
            count--;
        }
        return count;
    }

    public static (int Surah, int Ayah) PageStart(string pageKey, int page)
    {
        var row = Table(pageKey)[page];
        return (row[0], row[1]);
    }

    public static int FindPage(string pageKey, int surah, int ayah)
    {
        int target = AyaToId(surah, ayah);
        var t = Table(pageKey);
        int hi = PageCount(pageKey);
        int lo = 1, best = 1;
        while (lo <= hi)
        {
            int mid = (lo + hi) / 2;
            var start = PageStart(pageKey, mid);
            if (AyaToId(start.Surah, start.Ayah) <= target)
            {
                best = mid;
                lo = mid + 1;
            }
            else
            {
                hi = mid - 1;
            }
        }
        return best;
    }

    public static (int Surah, int Ayah) JuzStart(int juz)
    {
        var row = Table("Juz")[juz];
        return (row[0], row[1]);
    }

    public static byte SajdaType(int surah, int ayah)
    {
        foreach (var (s, a, obligatory) in _sajda)
        {
            if (s == surah && a == ayah) return obligatory ? (byte)2 : (byte)1;
        }
        return 0;
    }

    public static List<(int Surah, int Ayah)> PageAyahs(string pageKey, int page)
    {
        var list = new List<(int, int)>();
        var start = PageStart(pageKey, page);
        int fromId = AyaToId(start.Surah, start.Ayah);
        int toId = AyaToId(start.Surah, start.Ayah);
        if (page < PageCount(pageKey))
        {
            var next = PageStart(pageKey, page + 1);
            toId = AyaToId(next.Surah, next.Ayah) - 1;
        }
        else
        {
            toId = AyaToId(SurahCount, SurahAyahCount(SurahCount));
        }
        for (int id = fromId; id <= toId; id++)
        {
            list.Add(IdToAya(id));
        }
        return list;
    }
}
