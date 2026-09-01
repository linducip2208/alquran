using System.Collections.Concurrent;
using System.Text;
using System.Text.Json;

namespace QuranDesktop;

public sealed record SearchResult(int Surah, int Ayah, string Text);

public sealed class KsuApi
{
    public const string InterfaceUrl = "https://quran.ksu.edu.sa/interface.php?ui=pc";

    private readonly HttpClient _http = CreateClient();
    private readonly ConcurrentDictionary<string, string> _tafsirCache = new();
    private readonly ConcurrentDictionary<string, Dictionary<int, string>> _tarjamaCache = new();
    private readonly ConcurrentDictionary<string, Dictionary<string, int[]>> _hilitesCache = new();

    // Lock per file tafsir/hilites agar penulisan JSON aman terhadap fetch paralel
    private static readonly ConcurrentDictionary<string, SemaphoreSlim> _fileLocks = new();

    private static SemaphoreSlim FileLock(string path) => _fileLocks.GetOrAdd(path, _ => new SemaphoreSlim(1, 1));

    private static HttpClient CreateClient()
    {
        var c = new HttpClient();
        c.Timeout = TimeSpan.FromSeconds(45);
        c.DefaultRequestHeaders.UserAgent.ParseAdd("Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 QuranDesktop/2.0");
        c.DefaultRequestHeaders.Referrer = new Uri("https://quran.ksu.edu.sa/index.php?ui=1&l=en");
        return c;
    }

    // ---------- TAFSIR (disk cache: cache/tafsir/{author}/{surah}.json) ----------

    public static string TafsirPath(string author, int surah)
        => Path.Combine(KsuAudio.CacheDir, "tafsir", author, surah + ".json");

    private static async Task<Dictionary<int, string>> ReadTafsirDiskCoreAsync(string author, int surah, CancellationToken ct)
    {
        var result = new Dictionary<int, string>();
        string path = TafsirPath(author, surah);
        try
        {
            if (!File.Exists(path)) return result;
            using var fs = File.OpenRead(path);
            using var doc = await JsonDocument.ParseAsync(fs, cancellationToken: ct);
            if (doc.RootElement.TryGetProperty("ayat", out var ayatEl) && ayatEl.ValueKind == JsonValueKind.Object)
            {
                foreach (var prop in ayatEl.EnumerateObject())
                {
                    if (int.TryParse(prop.Name, out int a) && prop.Value.ValueKind == JsonValueKind.String)
                    {
                        result[a] = prop.Value.GetString() ?? "";
                    }
                }
            }
        }
        catch
        {
        }
        return result;
    }

    /// <summary>Baca disk tafsir dengan lock per file — aman terhadap penulis paralel (tidak ada partial-read).</summary>
    private static async Task<Dictionary<int, string>> ReadTafsirDiskAsync(string author, int surah, CancellationToken ct)
    {
        string path = TafsirPath(author, surah);
        if (!File.Exists(path)) return new Dictionary<int, string>();
        var lockObj = FileLock(path);
        await lockObj.WaitAsync(ct);
        try
        {
            return await ReadTafsirDiskCoreAsync(author, surah, ct);
        }
        finally
        {
            lockObj.Release();
        }
    }

    private static async Task WriteTafsirDiskAsync(string author, int surah, Dictionary<int, string> ayat, CancellationToken ct)
    {
        string path = TafsirPath(author, surah);
        try
        {
            var lockObj = FileLock(path);
            await lockObj.WaitAsync(ct);
            try
            {
                // merge ke file existing (core read TANPA lock — kita sudah memegang lock)
                var existing = await ReadTafsirDiskCoreAsync(author, surah, ct);
                foreach (var (a, text) in ayat)
                {
                    if (string.IsNullOrWhiteSpace(text) && existing.TryGetValue(a, out var old)) existing[a] = old;
                    else if (!string.IsNullOrWhiteSpace(text)) existing[a] = text;
                }
                Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                using var ms = new MemoryStream();
                using (var writer = new Utf8JsonWriter(ms))
                {
                    writer.WriteStartObject();
                    writer.WriteStartObject("ayat");
                    foreach (var (a, text) in existing.OrderBy(k => k.Key))
                    {
                        writer.WriteString(a.ToString(), text);
                    }
                    writer.WriteEndObject();
                    writer.WriteEndObject();
                }
                // tulis ke file temp lalu move atomik — file existing tidak pernah setengah-tertulis
                string tmp = path + ".tmp";
                File.WriteAllBytes(tmp, ms.ToArray());
                File.Move(tmp, path, overwrite: true);
            }
            finally
            {
                lockObj.Release();
            }
        }
        catch
        {
        }
    }

    public async Task<string> GetTafsirAsync(string author, int surah, int ayah, CancellationToken ct)
    {
        string key = $"{author}|{surah}|{ayah}";
        if (_tafsirCache.TryGetValue(key, out var cached)) return cached;

        // L2: disk cache persisten — bekerja offline
        var disk = await ReadTafsirDiskAsync(author, surah, ct);
        if (disk.TryGetValue(ayah, out var diskText) && !string.IsNullOrWhiteSpace(diskText))
        {
            _tafsirCache[key] = diskText;
            return diskText;
        }

        string url = $"{InterfaceUrl}&do=tafsir&author={Uri.EscapeDataString(author)}&sura={surah}&aya={ayah}";
        string raw = await _http.GetStringAsync(url, ct);
        string text = raw;
        int sep = raw.IndexOf("|||", StringComparison.Ordinal);
        if (sep >= 0) text = raw[(sep + 3)..];
        _tafsirCache[key] = text;

        if (!string.IsNullOrWhiteSpace(text))
        {
            await WriteTafsirDiskAsync(author, surah, new Dictionary<int, string> { [ayah] = text }, ct);
        }
        return text;
    }

    public static string AyahTextFromTafsirRaw(string raw)
    {
        int sep = raw.IndexOf("|||", StringComparison.Ordinal);
        return sep >= 0 ? raw[..sep] : "";
    }

    public async Task<Dictionary<int, string>> GetSurahTarjamaAsync(string transKey, int surah, CancellationToken ct)
    {
        string key = $"{transKey}|{surah}";
        if (_tarjamaCache.TryGetValue(key, out var cached)) return cached;

        string diskPath = Path.Combine(KsuAudio.CacheDir, "teks", transKey, surah + ".json");
        if (File.Exists(diskPath))
        {
            try
            {
                using var fs = File.OpenRead(diskPath);
                using var doc = await JsonDocument.ParseAsync(fs, cancellationToken: ct);
                var diskMap = new Dictionary<int, string>();
                if (doc.RootElement.TryGetProperty("ayat", out var ayatEl) && ayatEl.ValueKind == JsonValueKind.Object)
                {
                    foreach (var prop in ayatEl.EnumerateObject())
                    {
                        if (int.TryParse(prop.Name, out int a)) diskMap[a] = prop.Value.GetString() ?? "";
                    }
                }
                if (diskMap.Count > 0)
                {
                    _tarjamaCache[key] = diskMap;
                    return diskMap;
                }
            }
            catch
            {
            }
        }

        int eSura = surah < QuranData.SurahCount ? surah + 1 : surah;
        int eAya = surah < QuranData.SurahCount ? 1 : QuranData.SurahAyahCount(surah);
        string url = $"{InterfaceUrl}&do=tarjama&tafsir={Uri.EscapeDataString(transKey)}"
            + $"&b_sura={surah}&b_aya=1&e_sura={eSura}&e_aya={eAya}";

        using var stream = await _http.GetStreamAsync(url, ct);
        using var doc2 = await JsonDocument.ParseAsync(stream, cancellationToken: ct);

        var map = new Dictionary<int, string>();
        if (doc2.RootElement.TryGetProperty("tafsir", out var taf) && taf.ValueKind == JsonValueKind.Object)
        {
            foreach (var prop in taf.EnumerateObject())
            {
                var parts = prop.Name.Split('_');
                if (parts.Length == 2
                    && int.TryParse(parts[0], out int s) && s == surah
                    && int.TryParse(parts[1], out int a)
                    && prop.Value.ValueKind == JsonValueKind.Object
                    && prop.Value.TryGetProperty("text", out var txtEl))
                {
                    map[a] = txtEl.GetString() ?? "";
                }
            }
        }

        if (map.Count > 0)
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(diskPath)!);
                using var ms = new MemoryStream();
                using (var writer = new Utf8JsonWriter(ms))
                {
                    writer.WriteStartObject();
                    writer.WriteStartObject("ayat");
                    foreach (var (a, text) in map.OrderBy(k => k.Key))
                    {
                        writer.WriteString(a.ToString(), text);
                    }
                    writer.WriteEndObject();
                    writer.WriteEndObject();
                }
                // merge: jangan buang ayat existing di disk (race dengan fetch per ayat lain)
                var merged = new Dictionary<int, string>();
                if (File.Exists(diskPath))
                {
                    try
                    {
                        using var fsOld = File.OpenRead(diskPath);
                        using var docOld = await JsonDocument.ParseAsync(fsOld, cancellationToken: ct);
                        if (docOld.RootElement.TryGetProperty("ayat", out var oldEl) && oldEl.ValueKind == JsonValueKind.Object)
                        {
                            foreach (var p in oldEl.EnumerateObject())
                            {
                                if (int.TryParse(p.Name, out int oa) && p.Value.ValueKind == JsonValueKind.String)
                                {
                                    merged[oa] = p.Value.GetString() ?? "";
                                }
                            }
                        }
                    }
                    catch
                    {
                    }
                }
                foreach (var (a, text) in map)
                {
                    if (!string.IsNullOrWhiteSpace(text)) merged[a] = text;
                }
                using var msM = new MemoryStream();
                using (var writer = new Utf8JsonWriter(msM))
                {
                    writer.WriteStartObject();
                    writer.WriteStartObject("ayat");
                    foreach (var (a, text) in merged.OrderBy(k => k.Key))
                    {
                        writer.WriteString(a.ToString(), text);
                    }
                    writer.WriteEndObject();
                    writer.WriteEndObject();
                }
                string tmp = diskPath + ".tmp";
                File.WriteAllBytes(tmp, msM.ToArray());
                File.Move(tmp, diskPath, overwrite: true);
            }
            catch
            {
            }
        }

        _tarjamaCache[key] = map;
        return map;
    }

    public async Task<List<SearchResult>> SearchAsync(string query, CancellationToken ct)
    {
        string url = $"{InterfaceUrl}&do=search";
        using var content = new FormUrlEncodedContent(new Dictionary<string, string> { ["query"] = query });
        using var resp = await _http.PostAsync(url, content, ct);
        resp.EnsureSuccessStatusCode();
        using var stream = await resp.Content.ReadAsStreamAsync(ct);
        using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: ct);

        var results = new List<SearchResult>();
        if (doc.RootElement.TryGetProperty("results", out var arr) && arr.ValueKind == JsonValueKind.Object)
        {
            foreach (var prop in arr.EnumerateObject())
            {
                var v = prop.Value;
                if (v.ValueKind == JsonValueKind.Object
                    && v.TryGetProperty("sura", out var sEl)
                    && v.TryGetProperty("aya", out var aEl)
                    && v.TryGetProperty("text", out var tEl)
                    && int.TryParse(sEl.GetString(), out int s)
                    && int.TryParse(aEl.GetString(), out int a))
                {
                    results.Add(new SearchResult(s, a, tEl.GetString() ?? ""));
                }
            }
        }
        return results;
    }

    // ---------- HILITES (disk cache: cache/hilites/{mushafKey}/{page}.json) ----------

    public static string HilitesPath(string mushafKey, int page)
        => Path.Combine(KsuAudio.CacheDir, "hilites", mushafKey, page + ".json");

    private static async Task<Dictionary<string, int[]>?> ReadHilitesDiskAsync(string mushafKey, int page, CancellationToken ct)
    {
        string path = HilitesPath(mushafKey, page);
        try
        {
            if (!File.Exists(path)) return null;
            using var fs = File.OpenRead(path);
            using var doc = await JsonDocument.ParseAsync(fs, cancellationToken: ct);
            var map = new Dictionary<string, int[]>();
            if (doc.RootElement.ValueKind == JsonValueKind.Object)
            {
                foreach (var prop in doc.RootElement.EnumerateObject())
                {
                    if (prop.Value.ValueKind == JsonValueKind.Array && prop.Value.GetArrayLength() >= 2)
                    {
                        map[prop.Name] = new[] { prop.Value[0].GetInt32(), prop.Value[1].GetInt32() };
                    }
                }
            }
            return map.Count > 0 ? map : null;
        }
        catch
        {
            return null;
        }
    }

    private static async Task WriteHilitesDiskAsync(string mushafKey, int page, Dictionary<string, int[]> map, CancellationToken ct)
    {
        string path = HilitesPath(mushafKey, page);
        try
        {
            var lockObj = FileLock(path);
            await lockObj.WaitAsync(ct);
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                using var ms = new MemoryStream();
                using (var writer = new Utf8JsonWriter(ms))
                {
                    writer.WriteStartObject();
                    foreach (var (k, v) in map.OrderBy(kv => kv.Key, StringComparer.Ordinal))
                    {
                        writer.WriteStartArray(k);
                        writer.WriteNumberValue(v[0]);
                        writer.WriteNumberValue(v[1]);
                        writer.WriteEndArray();
                    }
                    writer.WriteEndObject();
                }
                string tmp = path + ".tmp";
                File.WriteAllBytes(tmp, ms.ToArray());
                File.Move(tmp, path, overwrite: true);
            }
            finally
            {
                lockObj.Release();
            }
        }
        catch
        {
        }
    }

    public async Task<Dictionary<string, int[]>> GetHilitesAsync(int page, CancellationToken ct)
        => await GetHilitesAsync("hafs", page, ct);

    public async Task<Dictionary<string, int[]>> GetHilitesAsync(string mushafKey, int page, CancellationToken ct)
    {
        string key = $"{mushafKey}|{page}";
        if (_hilitesCache.TryGetValue(key, out var cached)) return cached;

        // L2: disk cache persisten — reader tetap bisa highlight/klik ayat saat offline
        var disk = await ReadHilitesDiskAsync(mushafKey, page, ct);
        if (disk != null)
        {
            _hilitesCache[key] = disk;
            return disk;
        }

        string url = $"{InterfaceUrl}&do=hilites&page={page}";
        using var stream = await _http.GetStreamAsync(url, ct);
        using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: ct);

        var map = new Dictionary<string, int[]>();
        foreach (var pageProp in doc.RootElement.EnumerateObject())
        {
            if (pageProp.Value.ValueKind == JsonValueKind.Object)
            {
                foreach (var ayaProp in pageProp.Value.EnumerateObject())
                {
                    if (ayaProp.Value.ValueKind == JsonValueKind.Array && ayaProp.Value.GetArrayLength() >= 2)
                    {
                        map[ayaProp.Name] = new[]
                        {
                            ayaProp.Value[0].GetInt32(),
                            ayaProp.Value[1].GetInt32(),
                        };
                    }
                }
            }
        }
        _hilitesCache[key] = map;
        if (map.Count > 0)
        {
            await WriteHilitesDiskAsync(mushafKey, page, map, ct);
        }
        return map;
    }

    public static string StripHtml(string html)
    {
        if (string.IsNullOrEmpty(html)) return "";
        var sb = new StringBuilder(html);
        sb.Replace("</p>", "\n\n").Replace("<br>", "\n").Replace("<br/>", "\n").Replace("<br />", "\n");
        var text = System.Text.RegularExpressions.Regex.Replace(sb.ToString(), "<[^>]*>", "");
        text = text
            .Replace("&quot;", "\"").Replace("&#39;", "'").Replace("&amp;", "&")
            .Replace("&lt;", "<").Replace("&gt;", ">").Replace("&nbsp;", " ")
            .Replace("\r", "");
        return System.Text.RegularExpressions.Regex.Replace(text, "[ ]{2,}", " ").Trim();
    }
}
