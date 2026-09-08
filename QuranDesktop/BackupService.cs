using System.IO.Compression;
using System.Text.Json;

namespace QuranDesktop;

internal static class BackupService
{
    private const int CurrentVersion = 2;

    private sealed record BackupManifest(
        int Version,
        DateTime CreatedUtc,
        string[] Files,
        Dictionary<string, string> Sha256);

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
            var files = new List<string>();
            var hashes = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var name in new[] { "settings.json", "progress.json" })
            {
                var p = Path.Combine(DataDir, name);
                if (File.Exists(p))
                {
                    zip.CreateEntryFromFile(p, name);
                    files.Add(name);
                    hashes[name] = HashFile(p);
                }
            }

            var recordings = Path.Combine(KsuAudio.CacheDir, "recordings");
            if (Directory.Exists(recordings))
            {
                foreach (var recording in Directory.EnumerateFiles(recordings, "*.wav", SearchOption.TopDirectoryOnly))
                {
                    var name = "recordings/" + Path.GetFileName(recording);
                    zip.CreateEntryFromFile(recording, name);
                    files.Add(name);
                    hashes[name] = HashFile(recording);
                }
            }

            var manifest = new BackupManifest(CurrentVersion, DateTime.UtcNow, files.ToArray(), hashes);
            var entry = zip.CreateEntry("manifest.json");
            using var writer = new StreamWriter(entry.Open());
            writer.Write(JsonSerializer.Serialize(manifest, new JsonSerializerOptions { WriteIndented = true }));
        });
    }

    public static async Task ImportAsync(string zipPath)
    {
        await Task.Run(() =>
        {
            using var fs = File.OpenRead(zipPath);
            using var zip = new ZipArchive(fs, ZipArchiveMode.Read);
            var manifestEntry = zip.GetEntry("manifest.json")
                ?? throw new InvalidDataException("Backup tidak memiliki manifest.");
            BackupManifest? manifest;
            using (var reader = new StreamReader(manifestEntry.Open()))
            {
                manifest = JsonSerializer.Deserialize<BackupManifest>(reader.ReadToEnd());
            }
            if (manifest is null || manifest.Version is < 1 or > CurrentVersion)
                throw new InvalidDataException("Versi backup tidak didukung.");

            var temp = Path.Combine(DataDir, ".restore-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(temp);
            try
            {
                foreach (var name in manifest.Files)
                {
                    if (!IsSafeName(name))
                        throw new InvalidDataException("Backup berisi file yang tidak diizinkan.");

                    var entry = zip.GetEntry(name)
                        ?? throw new InvalidDataException($"File backup hilang: {name}");
                    var target = Path.Combine(temp, name.Replace('/', Path.DirectorySeparatorChar));
                    Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                    entry.ExtractToFile(target, overwrite: true);
                    if (manifest.Sha256?.TryGetValue(name, out var expected) == true
                        && !string.Equals(HashFile(target), expected, StringComparison.OrdinalIgnoreCase))
                        throw new InvalidDataException($"Checksum backup tidak cocok: {name}");
                }

                foreach (var json in new[] { "settings.json", "progress.json" })
                {
                    var path = Path.Combine(temp, json);
                    if (!File.Exists(path)) continue;
                    using var document = JsonDocument.Parse(File.ReadAllText(path));
                }

                foreach (var name in manifest.Files)
                {
                    var source = Path.Combine(temp, name.Replace('/', Path.DirectorySeparatorChar));
                    var destination = Path.Combine(DataDir, name.Replace('/', Path.DirectorySeparatorChar));
                    Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
                    File.Move(source, destination, overwrite: true);
                }
            }
            finally
            {
                try { if (Directory.Exists(temp)) Directory.Delete(temp, true); } catch { }
            }
        });
    }

    private static bool IsSafeName(string name)
    {
        if (name is "settings.json" or "progress.json") return true;
        if (!name.StartsWith("recordings/", StringComparison.Ordinal)
            || name.Length <= "recordings/".Length) return false;
        var file = name["recordings/".Length..];
        return file.IndexOfAny(new[] { '/', '\\' }) < 0
            && file != "." && file != "..";
    }

    private static string HashFile(string path)
    {
        using var stream = File.OpenRead(path);
        return Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(stream));
    }
}
