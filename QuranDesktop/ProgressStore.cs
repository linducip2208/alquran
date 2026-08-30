using System.Text.Json;

namespace QuranDesktop;

public sealed class Bookmark
{
    public int S { get; set; }
    public int A { get; set; }
}

internal static class ProgressStore
{
    private sealed class Data
    {
        public HashSet<int> ReadPages { get; set; } = new();
        public Dictionary<string, int> ReadCounts { get; set; } = new();
        public Dictionary<int, int> HafalStatus { get; set; } = new();
        public List<Bookmark> Bookmarks { get; set; } = new();
    }

    private static Data _data = Load();
    private static readonly object _lock = new();

    private static string Dir => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "QuranDesktop");

    private static string FilePath => Path.Combine(Dir, "progress.json");

    private static Data Load()
    {
        try
        {
            if (File.Exists(FilePath))
            {
                using var reader = new StreamReader(File.OpenRead(FilePath));
                var d = JsonSerializer.Deserialize<Data>(reader.ReadToEnd());
                if (d != null) return d;
            }
        }
        catch
        {
        }
        return new Data();
    }

    public static void Save()
    {
        lock (_lock)
        {
            try
            {
                Directory.CreateDirectory(Dir);
                File.WriteAllText(FilePath, JsonSerializer.Serialize(_data, new JsonSerializerOptions { WriteIndented = true }));
            }
            catch
            {
            }
        }
    }

    public static bool IsPageRead(int page) => _data.ReadPages.Contains(page);

    public static int ReadPageCount => _data.ReadPages.Count;

    public static void MarkPageRead(int page)
    {
        bool added;
        lock (_lock)
        {
            added = _data.ReadPages.Add(page);
            if (added)
            {
                var key = DateTime.Now.ToString("yyyy-MM-dd");
                _data.ReadCounts[key] = _data.ReadCounts.TryGetValue(key, out int c) ? c + 1 : 1;
            }
        }
        if (added) Save();
    }

    public static int StreakDays()
    {
        int streak = 0;
        for (int i = 0; i < 3650; i++)
        {
            var key = DateTime.Now.AddDays(-i).ToString("yyyy-MM-dd");
            if (_data.ReadCounts.TryGetValue(key, out int c) && c > 0)
            {
                streak++;
            }
            else if (i > 0)
            {
                break;
            }
        }
        return streak;
    }

    public static int GetHafal(int page)
        => _data.HafalStatus.TryGetValue(page, out int v) ? v : 0;

    public static void SetHafal(int page, int status)
    {
        lock (_lock)
        {
            if (status <= 0) _data.HafalStatus.Remove(page);
            else _data.HafalStatus[page] = status;
        }
        Save();
    }

    public static List<Bookmark> Bookmarks
    {
        get
        {
            lock (_lock) return _data.Bookmarks.ToList();
        }
    }

    public static bool IsBookmarked(int surah, int ayah)
        => _data.Bookmarks.Any(b => b.S == surah && b.A == ayah);

    public static void ToggleBookmark(int surah, int ayah)
    {
        lock (_lock)
        {
            var existing = _data.Bookmarks.FirstOrDefault(b => b.S == surah && b.A == ayah);
            if (existing != null) _data.Bookmarks.Remove(existing);
            else _data.Bookmarks.Add(new Bookmark { S = surah, A = ayah });
        }
        Save();
    }
}
