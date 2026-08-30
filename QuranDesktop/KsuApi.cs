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
    private readonly ConcurrentDictionary<int, Dictionary<string, int[]>> _hilitesCache = new();

    private static HttpClient CreateClient()
    {
        var c = new HttpClient();
        c.Timeout = TimeSpan.FromSeconds(45);
        c.DefaultRequestHeaders.UserAgent.ParseAdd("Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 QuranDesktop/2.0");
        c.DefaultRequestHeaders.Referrer = new Uri("https://quran.ksu.edu.sa/index.php?ui=1&l=en");
        return c;
    }

    public async Task<string> GetTafsirAsync(string author, int surah, int ayah, CancellationToken ct)
    {
        string key = $"{author}|{surah}|{ayah}";
        if (_tafsirCache.TryGetValue(key, out var cached)) return cached;

        string url = $"{InterfaceUrl}&do=tafsir&author={Uri.EscapeDataString(author)}&sura={surah}&aya={ayah}";
        string raw = await _http.GetStringAsync(url, ct);
        string text = raw;
        int sep = raw.IndexOf("|||", StringComparison.Ordinal);
        if (sep >= 0) text = raw[(sep + 3)..];
        _tafsirCache[key] = text;
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

        int eSura = surah < QuranData.SurahCount ? surah + 1 : surah;
        int eAya = surah < QuranData.SurahCount ? 1 : QuranData.SurahAyahCount(surah);
        string url = $"{InterfaceUrl}&do=tarjama&tafsir={Uri.EscapeDataString(transKey)}"
            + $"&b_sura={surah}&b_aya=1&e_sura={eSura}&e_aya={eAya}";

        using var stream = await _http.GetStreamAsync(url, ct);
        using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: ct);

        var map = new Dictionary<int, string>();
        if (doc.RootElement.TryGetProperty("tafsir", out var taf) && taf.ValueKind == JsonValueKind.Object)
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

    public async Task<Dictionary<string, int[]>> GetHilitesAsync(int page, CancellationToken ct)
    {
        if (_hilitesCache.TryGetValue(page, out var cached)) return cached;

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
        _hilitesCache[page] = map;
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
