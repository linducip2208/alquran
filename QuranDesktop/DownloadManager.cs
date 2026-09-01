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
        /// <summary>Untuk job Tafsir per ayat: 0 = semua ayat yang kurang pada surah.</summary>
        public int Ayah { get; init; }
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
        int downloaded = 0, skipped = 0, failed = 0;
        long bytes = 0;
        var errors = new List<string>();
        var sw = Stopwatch.StartNew();
        var gate = new SemaphoreSlim(Math.Max(1, Concurrency));
        var tasks = new List<Task>(total);
        using var cancel = CancellationTokenSource.CreateLinkedTokenSource(ct);

        // Semua counter (Done/Downloaded/Skipped/Failed) di-update ATOMIK dalam satu lock
        // agar progress Report selalu konsisten: Done == Downloaded + Skipped + Failed.
        object countLock = new();
        int totalDone = 0;
        void Count(int dl, int sk, int fl)
        {
            lock (countLock)
            {
                downloaded += dl; skipped += sk; failed += fl;
                totalDone = downloaded + skipped + failed;
            }
        }

        void Report(string current)
        {
            if (progress == null) return;
            double speed = sw.Elapsed.TotalSeconds > 0.5 ? bytes / sw.Elapsed.TotalSeconds : 0;
            var eta = speed > 1 && totalDone > 0
                ? TimeSpan.FromSeconds(Math.Max(0, total - totalDone) / Math.Max(1.0, totalDone / sw.Elapsed.TotalSeconds))
                : TimeSpan.Zero;
            progress.Report(new DownloadProgress(total, totalDone, downloaded, skipped, failed, bytes, speed, eta, current));
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
                            case 0:
                                Count(0, 1, 0);
                                break;
                            case 1:
                                Count(1, 0, 0);
                                if (item.Kind == JobKind.File && item.Rel != null)
                                {
                                    var fi = new FileInfo(KsuAudio.CachePath(item.Rel));
                                    if (fi.Exists) Interlocked.Add(ref bytes, fi.Length);
                                }
                                break;
                            default:
                                Count(0, 0, 1);
                                lock (errors) { errors.Add(item.Label); }
                                break;
                        }
                        Report(current);
                    }
                    catch (OperationCanceledException)
                    {
                        cancel.Cancel();
                    }
                    catch (Exception ex)
                    {
                        Count(0, 0, 1);
                        lock (errors) { errors.Add(item.Label + " — " + ex.Message); }
                        Report(item.Label);
                    }
                    finally
                    {
                        gate.Release();
                    }
                }, cancel.Token));
            }
            await Task.WhenAll(tasks);
        }
        catch (OperationCanceledException)
        {
        }

        // lapor status final agar Done == Downloaded + Skipped + Failed == jumlah job (bila tak dibatalkan)
        Report("");
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
                int before = OfflineContentService.Instance.GetTarjamaStatus(key, surah).AyatFound;
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
                // gagal: buang hasil parsial agar "yang kurang" tidak salah hitung
                OfflineContentService.Instance.InvalidateTarjama(key, surah);
                return OfflineContentService.Instance.GetTarjamaStatus(key, surah).AyatFound > before ? 1 : -1;
            }
            case JobKind.Tafsir:
            {
                string key = item.TextKey!;
                int surah = item.Surah;
                var st = OfflineContentService.Instance.GetTafsirStatus(key, surah);
                if (st.Complete) return 0;
                // scope per ayat (unduh 1 ayat) atau seluruh ayat yang kurang pada surah
                List<int> missing = item.Ayah > 0
                    ? (st.MissingAyat.Contains(item.Ayah) ? new List<int> { item.Ayah } : new List<int>())
                    : st.MissingAyat.ToList();
                if (missing.Count == 0) return 0;
                var gate2 = new SemaphoreSlim(Math.Max(1, Concurrency));
                var tasks2 = new List<Task>();
                int fetched = 0, failAyat = 0;
                foreach (var ayah in missing)
                {
                    await gate2.WaitAsync(ct);
                    tasks2.Add(Task.Run(async () =>
                    {
                        try
                        {
                            string text = await ProgramServices.Api.GetTafsirAsync(key, surah, ayah, ct);
                            if (string.IsNullOrWhiteSpace(text)) Interlocked.Increment(ref failAyat);
                            else Interlocked.Increment(ref fetched);
                        }
                        catch (OperationCanceledException)
                        {
                            Interlocked.Increment(ref failAyat);
                            throw;
                        }
                        catch
                        {
                            Interlocked.Increment(ref failAyat);
                        }
                        finally
                        {
                            gate2.Release();
                        }
                    }, ct));
                }
                try { await Task.WhenAll(tasks2); }
                catch (OperationCanceledException) { throw; }
                OfflineContentService.Instance.InvalidateTafsir(key, surah);
                return fetched > 0 ? 1 : (failAyat > 0 ? -1 : 0);
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
        var mushaf = MushafTypes.ResolveMushaf(scope.MushafKey);
        int pageCount = QuranData.PageCount(mushaf.PageKey);

        IEnumerable<int> Surahs() => scope.Surahs.Count > 0 ? scope.Surahs : Enumerable.Range(1, QuranData.SurahCount);

        // Halaman mushaf/hilite yang relevan: scope.Pages bila diisi;
        // bila scope per surah → hanya halaman tempat ayat surah itu berada (bukan 604 halaman);
        // bila full → seluruh halaman.
        HashSet<int> ResolvePages()
        {
            if (scope.Pages.Count > 0)
            {
                return scope.Pages.Where(p => p >= 1 && p <= pageCount).ToHashSet();
            }
            if (scope.Surahs.Count > 0)
            {
                var set = new HashSet<int>();
                foreach (var s in scope.Surahs)
                {
                    int n = QuranData.SurahAyahCount(s);
                    for (int a = 1; a <= n; a++) set.Add(MushafTypes.FindMushafPage(mushaf.Key, s, a));
                }
                return set;
            }
            return Enumerable.Range(1, pageCount).ToHashSet();
        }

        if (scope.Mushaf)
        {
            foreach (var mt in scope.AllMushafs ? (IEnumerable<MushafType>)MushafTypes.All : new[] { mushaf })
            {
                int pc = QuranData.PageCount(mt.PageKey);
                IEnumerable<int> pages;
                if (scope.Pages.Count > 0) pages = scope.Pages.Where(p => p <= pc);
                else if (scope.Surahs.Count > 0)
                {
                    var set = new HashSet<int>();
                    foreach (var s in scope.Surahs)
                    {
                        int n = QuranData.SurahAyahCount(s);
                        for (int a = 1; a <= n; a++) set.Add(Math.Min(QuranData.FindPage(mt.PageKey, s, a), pc));
                    }
                    pages = set;
                }
                else pages = Enumerable.Range(1, pc);
                foreach (var p in pages)
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
            foreach (var p in ResolvePages())
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
                        // audio qari tersimpan di downloads/audio/{folder}/…
                        Rel = $"audio/{folder}/{s:D3}{a:D3}.mp3",
                        Url = KsuAudio.AyahUrl(folder, s, a),
                    });
                }
            }
        }
        return items;
    }
}
