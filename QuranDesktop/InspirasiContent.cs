using System.Reflection;
using System.Text.Json;

namespace QuranDesktop;

public sealed record InspirasiAyah(int S, int A, string Label);

public sealed record InspirasiKategori(string Key, string Judul, List<InspirasiAyah> Ayat);

public sealed record QuickItem(string Judul, int S, int A);

public static class InspirasiContent
{
    private sealed class Root
    {
        public List<InspirasiKategori> Kategori { get; set; } = new();
        public List<InspirasiAyah> Rabbana { get; set; } = new();
        public List<QuickItem> Quick { get; set; } = new();
    }

    private static Root? _root;

    private static Root Data
    {
        get
        {
            if (_root == null)
            {
                try
                {
                    var asm = Assembly.GetExecutingAssembly();
                    using var stream = asm.GetManifestResourceStream("inspirasi.json")
                        ?? throw new InvalidOperationException("inspirasi.json missing");
                    using var reader = new StreamReader(stream);
                    _root = JsonSerializer.Deserialize<Root>(reader.ReadToEnd()) ?? new Root();
                }
                catch
                {
                    _root = new Root();
                }
            }
            return _root;
        }
    }

    public static List<InspirasiKategori> Kategori => Data.Kategori;

    public static List<InspirasiAyah> Rabbana => Data.Rabbana;

    public static List<QuickItem> Quick => Data.Quick;

    public static List<InspirasiAyah> SemuaAyat()
    {
        var all = new List<InspirasiAyah>();
        foreach (var k in Kategori)
        {
            foreach (var a in k.Ayat)
            {
                all.Add(a);
            }
        }
        return all;
    }

    public static InspirasiAyah AyatHariIni()
    {
        var all = SemuaAyat();
        if (all.Count == 0) return new InspirasiAyah(2, 286, "Ayat Kursi Hari Ini");
        int seed = DateTime.Now.Year * 1000 + DateTime.Now.DayOfYear;
        return all[seed % all.Count];
    }
}
