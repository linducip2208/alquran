using System.Text.Json;

namespace QuranDesktop;

internal static class UpdateService
{
    public sealed record UpdateInfo(string Tag, string Url, string? AssetUrl, string? ChecksumUrl);

    public static bool IsNewerVersion(string latest, string current)
    {
        static Version Parse(string value)
        {
            value = value.Trim().TrimStart('v', 'V');
            var core = value.Split('-', '+')[0];
            return Version.TryParse(core, out var version) ? version : new Version(0, 0);
        }

        return Parse(latest) > Parse(current);
    }

    public static async Task<UpdateInfo?> CheckAsync(CancellationToken ct)
    {
        try
        {
            using var resp = await ProgramServices.Http.GetAsync(ProviderEndpoints.UpdateFeedUrl, ct);
            resp.EnsureSuccessStatusCode();
            using var stream = await resp.Content.ReadAsStreamAsync(ct);
            using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: ct);
            string tag = doc.RootElement.TryGetProperty("tag_name", out var t) ? t.GetString() ?? "" : "";
            string url = doc.RootElement.TryGetProperty("html_url", out var u) ? u.GetString() ?? "" : "";
            string? assetUrl = null;
            string? checksumUrl = null;
            if (doc.RootElement.TryGetProperty("assets", out var assets)
                && assets.ValueKind == JsonValueKind.Array)
            {
                foreach (var asset in assets.EnumerateArray())
                {
                    string name = asset.TryGetProperty("name", out var n) ? n.GetString() ?? "" : "";
                    string download = asset.TryGetProperty("browser_download_url", out var d)
                        ? d.GetString() ?? "" : "";
                    if (name.EndsWith(".sha256", StringComparison.OrdinalIgnoreCase)) checksumUrl = download;
                    else if (name.EndsWith(".zip", StringComparison.OrdinalIgnoreCase)
                        && name.Contains("win-x64", StringComparison.OrdinalIgnoreCase)) assetUrl = download;
                }
            }
            return tag.Length > 0 ? new UpdateInfo(tag, url, assetUrl, checksumUrl) : null;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            Program.Log(ex);
            return null;
        }
    }
}
