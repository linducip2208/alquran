using System.Diagnostics;
using System.Text.Json;

namespace QuranDesktop;

/// <summary>
/// Download engine terpadu untuk seluruh aplikasi (audio, mushaf, teks, tafsir, hilite).
/// - Skip file valid TANPA request jaringan
/// - Download ke "*.part" lalu rename atomik setelah valid
/// - Resume via HTTP Range bila server mendukung
/// - Retry otomatis (default 3x), concurrency (default 4), cancellation
/// </summary>
public sealed class DownloadManager
{
    public static DownloadManager Shared { get; } = new();

    public int Concurrency { get; set; } = 4;
    public int MaxRetries { get; set; } = 3;
    public int TimeoutSeconds { get; set; } = 45;

    public sealed record DownloadProgress(
        int Total, int Done, int Downloaded, int Skipped, int Failed,
        long Bytes, double BytesPerSec, TimeSpan Eta, string Current);

    public sealed record DownloadResult(
        int Downloaded, int Skipped, int Failed, long Bytes, bool Cancelled, IReadOnlyList<string> Errors)
    {
        public bool Success => Failed == 0;
    }

    public enum JobKind { File, Tarjama, Tafsir, Hilites }

    public sealed class DownloadItem
    {
        public string Label { get; init; } = "";
        public JobKind Kind { get; init; } = JobKind.File;
        public string? Url { get; init; }
        public string? Rel { get; init; }
        public string? TextKey { get; init; }
        public int Surah { get; init; }
        public long MinBytes { get; init; } = 4096;
        public override string ToString() => Label;
    }

    public sealed record DownloadScope
    {
        public bool Mushaf { get; init; } = true;
        public bool AllMushafs { get; init; }
        public bool Hilites { get; init; } = true;
        public bool Arab { get; init; } = true;
        public IReadOnlyList<string> Translations { get; init; } = Array.Empty<string>();
        public IReadOnlyList<string> Tafsirs { get; init; } = Array.Empty<string>();
        public IReadOnlyList<string> AudioFolders { get; init; } = Array.Empty<string>();
        public string MushafKey { get; init; } = "hafs";
        public IReadOnlyList<int> Surahs { get; init; } = Array.Empty<int>();
        public IReadOnlyList<int> Pages { get; init; } = Array.Empty<int>();
        public (int Lo, int Hi)? AyahRange { get; init; }
    }

    public async Task<bool> EnsureFileAsync(HttpClient http, string url, string rel, CancellationToken ct)
    {
        var item = new DownloadItem { Label = rel, Kind = JobKind.File, Url = url, Rel = rel };
        try
        {
            int res = await RunItemAsync(item, ct, _ => { });
            return res >= 0 && FileValid(KsuAudio.CachePath(rel), item.MinBytes);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch
        {
            return false;
        }
    }

    public async Task<DownloadResult> RunAsync(
        IEnumerable<DownloadItem> items, IProgress<DownloadProgress>? progress, CancellationToken ct)
    {
        var list = items.ToList();
        int total = list.Count;
        int done = 0, downloaded = 0, skipped = 0, failed = 0;
        long bytes = 0;
        var errors = new List<string>();
        var sw = Stopwatch.StartNew();
        var gate = new SemaphoreSlim(Math.Max(1, Concurrency));
        var tasks = new List<Task>(total);
        using var cancel = CancellationTokenSource.CreateLinkedTokenSource(ct);

        void Report(string current)
        {
            if (progress == null) return;
            double speed = sw.Elapsed.TotalSeconds > 0.5 ? bytes / sw.Elapsed.TotalSeconds : 0;
            var eta = speed > 1 && done > 0
                ? TimeSpan.FromSeconds(Math.Max(0, total - done) / Math.Max(1.0, done / sw.Elapsed.TotalSeconds))
                : TimeSpan.Zero;
            progress.Report(new DownloadProgress(total, done, downloaded, skipped, failed, bytes, speed, eta, current));
        }

        try
        {
            foreach (var item in list)
            {
                await gate.WaitAsync(cancel.Token);
                if (cancel.Token.IsCancellationRequested) break;
                tasks.Add(Task.Run(async () =>
                {
                    try
                    {
                        string current = item.Label;
                        int outcome = await RunItemAsync(item, cancel.Token, _ => { });
                        switch (outcome)
                        {
                            case 0: Interlocked.Increment(ref skipped); break;
                            case 1:
                                Interlocked.Increment(ref downloaded);
                                if (item.Kind == JobKind.File && item.Rel != null)
                                {
                                    var fi = new FileInfo(KsuAudio.CachePath(item.Rel));
                                    if (fi.Exists) Interlocked.Add(ref bytes, fi.Length);
                                }
                                break;
                            default: Interlocked.Increment(ref failed); lock (errors) { errors.Add(item.Label); } break;
                        }
                        Report(current);
                    }
                    catch (OperationCanceledException)
                    {
                        cancel.Cancel();
                    }
                    catch (Exception ex)
                    {
                        Interlocked.Increment(ref failed);
                        lock (errors) { errors.Add(item.Label + " — " + ex.Message); }
                        Report(item.Label);
                    }
                    finally
                    {
                        Interlocked.Increment(ref done);
                        gate.Release();
                    }
                }, cancel.Token));
            }
            await Task.WhenAll(tasks);
        }
        catch (OperationCanceledException)
        {
        }

        bool wasCancelled = ct.IsCancellationRequested || cancel.Token.IsCancellationRequested;
        return new DownloadResult(downloaded, skipped, failed, bytes, wasCancelled, errors);
    }

    /// <returns>0 = skipped (sudah valid), 1 = downloaded, -1 = gagal</returns>
    private async Task<int> RunItemAsync(DownloadItem item, CancellationToken ct, Action<string> status)
    {
        switch (item.Kind)
        {
            case JobKind.Tarjama:
            {
                string key = item.TextKey!;
                int surah = item.Surah;
                if (OfflineContentService.Instance.GetTarjamaStatus(key, surah).Complete) return 0;
                for (int attempt = 1; attempt <= MaxRetries; attempt++)
                {
                    try
                    {
                        var map = await ProgramServices.Api.GetSurahTarjamaAsync(key, surah, ct);
                        if (map.Count > 0)
                        {
                            OfflineContentService.Instance.InvalidateTarjama(key, surah);
                            return 1;
                        }
                    }
                    catch (OperationCanceledException) { throw; }
                    catch when (attempt < MaxRetries)
                    {
                        await Task.Delay(500 * attempt, ct);
                    }
                }
                return -1;
            }
            case JobKind.Tafsir:
            {
                string key = item.TextKey!;
                int surah = item.Surah;
                var st = OfflineContentService.Instance.GetTafsirStatus(key, surah);
                if (st.Complete) return 0;
                var missing = st.MissingAyat.ToList();
                var gate = new SemaphoreSlim(Concurrency);
                var tasks = new List<Task>();
                int fetched = 0;
                foreach (var ayah in missing)
                {
                    await gate.WaitAsync(ct);
                    tasks.Add(Task.Run(async () =>
                    {
                        try
                        {
                            await ProgramServices.Api.GetTafsirAsync(key, surah, ayah, ct);
                            Interlocked.Increment(ref fetched);
                        }
                        finally
                        {
                            gate.Release();
                        }
                    }, ct));
                }
                await Task.WhenAll(tasks);
                OfflineContentService.Instance.InvalidateTafsir(key, surah);
                return fetched > 0 ? 1 : 0;
            }
            case JobKind.Hilites:
            {
                string key = item.TextKey!;
                int page = item.Surah;
                if (OfflineContentService.Instance.GetHiliteStatus(key, page)) return 0;
                for (int attempt = 1; attempt <= MaxRetries; attempt++)
                {
                    try
                    {
                        var map = await ProgramServices.Api.GetHilitesAsync(key, page, ct);
                        if (map.Count > 0)
                        {
                            OfflineContentService.Instance.InvalidateHilite(key, page);
                            return 1;
                        }
                    }
                    catch (OperationCanceledException) { throw; }
                    catch when (attempt < MaxRetries)
                    {
                        await Task.Delay(500 * attempt, ct);
                    }
                }
                return -1;
            }
            default:
            {
                string rel = item.Rel!;
                string dest = KsuAudio.CachePath(rel);
                if (FileValid(dest, item.MinBytes)) return 0;
                string url = item.Url!;
                for (int attempt = 1; attempt <= MaxRetries; attempt++)
                {
                    ct.ThrowIfCancellationRequested();
                    try
                    {
                        await DownloadToFileAsync(ProgramServices.Http, url, dest, item.MinBytes, ct);
                        return 1;
                    }
                    catch (OperationCanceledException) { throw; }
                    catch when (attempt < MaxRetries)
                    {
                        await Task.Delay(500 * attempt, ct);
                    }
                }
                TryDeletePart(dest);
                return -1;
            }
        }
    }

    private static string PartPath(string dest) => dest + ".part";

    public static bool FileValid(string path, long minBytes)
    {
        try
        {
            var fi = new FileInfo(path);
            if (!fi.Exists || fi.Length < Math.Max(1, minBytes)) return false;
            if (fi.Extension == ".json")
            {
                using var fs = File.OpenRead(path);
                using var doc = JsonDocument.Parse(fs);
                return doc.RootElement.ValueKind == JsonValueKind.Object;
            }
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static void TryDeletePart(string dest)
    {
        try { if (File.Exists(PartPath(dest))) File.Delete(PartPath(dest)); } catch { }
    }

    private async Task<long> DownloadToFileAsync(
        HttpClient http, string url, string dest, long minBytes, CancellationToken ct)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(dest)!);
        string part = PartPath(dest);
        long resumeFrom = File.Exists(part) ? new FileInfo(part).Length : 0;

        using var req = new HttpRequestMessage(HttpMethod.Get, url);
        if (resumeFrom > 0) req.Headers.Range = new System.Net.Http.Headers.RangeHeaderValue(resumeFrom, null);

        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(TimeSpan.FromSeconds(TimeoutSeconds));
        using var resp = await http.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, cts.Token);
        resp.EnsureSuccessStatusCode();

        bool appending = resumeFrom > 0 && resp.StatusCode == System.Net.HttpStatusCode.PartialContent;
        long written = 0;
        await using (var src = await resp.Content.ReadAsStreamAsync(cts.Token))
        await using (var dst = new FileStream(part, appending ? FileMode.Append : FileMode.Create, FileAccess.Write, FileShare.None, 81920, useAsync: true))
        {
            if (!appending) resumeFrom = 0;
            var buffer = new byte[81920];
            int read;
            while ((read = await src.ReadAsync(buffer, cts.Token)) > 0)
            {
                await dst.WriteAsync(buffer.AsMemory(0, read), cts.Token);
                written += read;
            }
        }

        long totalSize = resumeFrom + written;
        if (totalSize < Math.Max(1, minBytes))
        {
            TryDeletePart(dest);
            throw new IOException($"File terlalu kecil ({totalSize} byte)");
        }

        if (Path.GetExtension(dest).Equals(".json", StringComparison.OrdinalIgnoreCase))
        {
            try
            {
                using var fs = File.OpenRead(part);
                using var doc = JsonDocument.Parse(fs);
            }
            catch
            {
                TryDeletePart(dest);
                throw new IOException("JSON tidak valid");
            }
        }

        File.Move(part, dest, overwrite: true);
        OfflineContentService.Instance.InvalidateAllSilent(dest);
        return totalSize;
    }

    /// <summary>Bangun daftar job unduhan sesuai scope (hanya yang kurang — file valid otomatis di-skip oleh engine).</summary>
    public static List<DownloadItem> BuildJobs(DownloadScope scope)
    {
        var items = new List<DownloadItem>();
        var mushaf = MushafTypes.Find(scope.MushafKey) ?? MushafTypes.All[0];
        int pageCount = QuranData.PageCount(mushaf.PageKey);

        IEnumerable<int> Surahs() => scope.Surahs.Count > 0 ? scope.Surahs : Enumerable.Range(1, QuranData.SurahCount);
        IEnumerable<int> Pages() => scope.Pages.Count > 0 ? scope.Pages.Where(p => p <= pageCount) : Enumerable.Range(1, pageCount);

        if (scope.Mushaf)
        {
            foreach (var mt in scope.AllMushafs ? (IEnumerable<MushafType>)MushafTypes.All : new[] { mushaf })
            {
                int pc = QuranData.PageCount(mt.PageKey);
                foreach (var p in scope.Pages.Count > 0 ? scope.Pages.Where(p => p <= pc) : Enumerable.Range(1, pc))
                {
                    items.Add(new DownloadItem
                    {
                        Label = $"Mushaf {mt.Display} hal {p}",
                        Kind = JobKind.File,
                        Rel = $"mushaf/{mt.Key}/{p}.png",
                        Url = mt.ImageBase + p + ".png",
                        MinBytes = 2048,
                    });
                }
            }
        }
        if (scope.Hilites)
        {
            foreach (var p in Pages())
            {
                items.Add(new DownloadItem
                {
                    Label = $"Hilite hal {p}",
                    Kind = JobKind.Hilites,
                    TextKey = mushaf.Key,
                    Surah = p,
                });
            }
        }
        if (scope.Arab)
        {
            foreach (var s in Surahs())
            {
                items.Add(new DownloadItem { Label = $"Teks Arab surah {s}", Kind = JobKind.Tarjama, TextKey = "ar_ayat", Surah = s });
            }
        }
        foreach (var tk in scope.Translations)
        {
            foreach (var s in Surahs())
            {
                items.Add(new DownloadItem { Label = $"Terjemahan surah {s} ({tk})", Kind = JobKind.Tarjama, TextKey = tk, Surah = s });
            }
        }
        foreach (var tk in scope.Tafsirs)
        {
            foreach (var s in Surahs())
            {
                items.Add(new DownloadItem { Label = $"Tafsir surah {s} ({tk})", Kind = JobKind.Tafsir, TextKey = tk, Surah = s });
            }
        }
        foreach (var folder in scope.AudioFolders)
        {
            foreach (var s in Surahs())
            {
                int n = QuranData.SurahAyahCount(s);
                int from = 1, to = n;
                if (scope.AyahRange is { } ar && scope.Surahs.Count == 1) { from = ar.Lo; to = Math.Min(ar.Hi, n); }
                for (int a = from; a <= to; a++)
                {
                    items.Add(new DownloadItem
                    {
                        Label = $"Audio {s}:{a} ({folder})",
                        Kind = JobKind.File,
                        Rel = $"{folder}/{s:D3}{a:D3}.mp3",
                        Url = KsuAudio.AyahUrl(folder, s, a),
                    });
                }
            }
        }
        return items;
    }
}
