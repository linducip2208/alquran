using System.Text.Json;

namespace QuranDesktop;

public sealed record WbwWord(string Uthmani, string Translation, string Transliteration);

public static class QuranComApi
{
    public static async Task<List<WbwWord>> GetWordsAsync(int surah, int ayah, CancellationToken ct)
    {
        string url = $"https://api.quran.com/api/v4/verses/by_key/{surah}:{ayah}"
            + "?words=true&word_fields=text_uthmani,translation&translations=134";

        using var resp = await ProgramServices.Http.GetAsync(url, ct);
        resp.EnsureSuccessStatusCode();
        using var stream = await resp.Content.ReadAsStreamAsync(ct);
        using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: ct);

        var result = new List<WbwWord>();
        if (!doc.RootElement.TryGetProperty("verse", out var verse)) return result;
        if (!verse.TryGetProperty("words", out var words)) return result;

        foreach (var w in words.EnumerateArray())
        {
            if (w.TryGetProperty("char_type_name", out var ctName) && ctName.GetString() != "word") continue;

            string uthmani = w.TryGetProperty("text_uthmani", out var u) ? u.GetString() ?? "" : "";
            if (string.IsNullOrEmpty(uthmani) && w.TryGetProperty("text", out var t)) uthmani = t.GetString() ?? "";

            string trans = "", translit = "";
            if (w.TryGetProperty("translation", out var tr) && tr.ValueKind == JsonValueKind.Object)
                trans = tr.TryGetProperty("text", out var tt) ? tt.GetString() ?? "" : "";
            if (w.TryGetProperty("transliteration", out var tl) && tl.ValueKind == JsonValueKind.Object)
                translit = tl.TryGetProperty("text", out var tt2) ? tt2.GetString() ?? "" : "";

            if (!string.IsNullOrWhiteSpace(uthmani))
            {
                result.Add(new WbwWord(uthmani, trans, translit));
            }
        }
        return result;
    }
}
