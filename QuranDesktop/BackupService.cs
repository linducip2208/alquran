using System.IO.Compression;
using System.Text.Json;

namespace QuranDesktop;

internal static class BackupService
{
    public static string DataDir => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "QuranDesktop");

    public static async Task ExportAsync(string zipPath)
    {
        await Task.Run(() =>
        {
            Directory.CreateDirectory(Path.GetDirectoryName(zipPath)!);
            using var fs = File.Create(zipPath);
            using var zip = new ZipArchive(fs, ZipArchiveMode.Create);
            foreach (var name in new[] { "settings.json", "progress.json" })
            {
                var p = Path.Combine(DataDir, name);
                if (File.Exists(p))
                {
                    zip.CreateEntryFromFile(p, name);
                }
            }
        });
    }

    public static async Task ImportAsync(string zipPath)
    {
        await Task.Run(() =>
        {
            using var fs = File.OpenRead(zipPath);
            using var zip = new ZipArchive(fs, ZipArchiveMode.Read);
            foreach (var entry in zip.Entries)
            {
                if (entry.Name is "settings.json" or "progress.json")
                {
                    Directory.CreateDirectory(DataDir);
                    entry.ExtractToFile(Path.Combine(DataDir, entry.Name), overwrite: true);
                }
            }
        });
    }

    public static async Task<(string Tag, string Url)?> CheckUpdateAsync(CancellationToken ct)
    {
        try
        {
            using var resp = await ProgramServices.Http.GetAsync(
                "https://api.github.com/repos/linducip2208/alquran/releases/latest", ct);
            resp.EnsureSuccessStatusCode();
            using var stream = await resp.Content.ReadAsStreamAsync(ct);
            using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: ct);
            string tag = doc.RootElement.TryGetProperty("tag_name", out var t) ? t.GetString() ?? "" : "";
            string url = doc.RootElement.TryGetProperty("html_url", out var u) ? u.GetString() ?? "" : "";
            if (tag.Length > 0) return (tag, url);
        }
        catch
        {
        }
        return null;
    }
}
