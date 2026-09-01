using System.Collections.Concurrent;
using System.Text.Json;

namespace QuranDesktop;

public sealed record AudioFileStatus(bool Exists, bool IsValid, long SizeBytes, string LocalPath, DateTime LastModified);

public sealed record AyahOfflineStatus(int Surah, int Ayah, int Page)
{
    public bool MushafAvailable { get; set; }
    public bool HiliteAvailable { get; set; }
    public bool ArabicAvailable { get; set; }
    public Dictionary<string, bool> TranslationAvailable { get; init; } = new();
    public Dictionary<string, bool> TafsirAvailable { get; init; } = new();
    public Dictionary<string, AudioFileStatus> ReciterAudio { get; init; } = new();
    public Dictionary<string, AudioFileStatus> VoiceTranslationAudio { get; init; } = new();

    public bool IsFullyAvailable =>
        MushafAvailable && HiliteAvailable && ArabicAvailable
        && TranslationAvailable.Values.All(v => v)
        && TafsirAvailable.Values.All(v => v)
        && ReciterAudio.Values.All(a => a.IsValid)
        && VoiceTranslationAudio.Values.All(a => a.IsValid);
}

public sealed record SurahOfflineSummary(
    int Number, string Name, int AyahCount,
    int MushafPages, int MushafPagesTotal,
    int ArabicAyat,
    Dictionary<string, int> TranslationAyat,
    Dictionary<string, int> TafsirAyat,
    Dictionary<string, int> ReciterAyat,
    long AudioBytes)
{
    public bool Complete { get; init; }
    public bool Partial { get; init; }
}

public sealed record ReciterSummary(string Key, string Folder, string Display, int Valid, int Total, long Bytes);

public sealed record MushafPageSummary(string Key, string Display, int Pages, int PagesTotal, long Bytes);

public sealed record TextKeySummary(string Kind, string Key, string Display, int SurahsValid, int SurahsTotal, int AyatFound, int AyatTotal, long Bytes);

public sealed record StorageItem(string Label, long Bytes);
public sealed record StorageReport(List<StorageItem> Items, long TotalBytes);

public sealed class OfflineContentService
{
    public static OfflineContentService Instance { get; } = new();

    /// <summary>Dipicu setiap kali status resource berubah (download, hapus, verify) — UI dapat refresh.</summary>
    public event Action? InventoryChanged;

    public string CacheRoot => KsuAudio.CacheDir;
    public string MushafDir => Path.Combine(CacheRoot, "mushaf");
    public string TeksDir => Path.Combine(CacheRoot, "teks");
    public string TafsirDir => Path.Combine(CacheRoot, "tafsir");
    public string HilitesDir => Path.Combine(CacheRoot, "hilites");
    public string AudioDir => CacheRoot;

    public int TotalAyat => QuranData.TotalAyahCount;
    public int TotalSurah => QuranData.SurahCount;

    private readonly ConcurrentDictionary<string, AudioFileStatus> _audioCache = new();
    private readonly ConcurrentDictionary<string, AudioFileStatus> _mushafCache = new();
    private readonly ConcurrentDictionary<string, TextSurahStatus> _tarjamaCache = new();
    private readonly ConcurrentDictionary<string, TextSurahStatus> _tafsirCache = new();
    private readonly ConcurrentDictionary<string, bool> _hiliteCache = new();

    private void FireChanged()
    {
        try { InventoryChanged?.Invoke(); } catch { }
    }

    // ============ VALIDASI FILE ============

    private static AudioFileStatus StatFile(string path, long minBytes)
    {
        try
        {
            var fi = new FileInfo(path);
            if (!fi.Exists || fi.Length <= 0) return new AudioFileStatus(false, false, 0, path, DateTime.MinValue);
            bool valid = fi.Extension != ".part" && fi.Length >= minBytes;
            return new AudioFileStatus(true, valid, fi.Length, path, fi.LastWriteTimeUtc);
        }
        catch
        {
            return new AudioFileStatus(false, false, 0, path, DateTime.MinValue);
        }
    }

    public AudioFileStatus GetAudioStatus(string relativePath)
    {
        string rel = relativePath.Replace('\\', '/');
        return _audioCache.GetOrAdd(rel, _ => StatFile(KsuAudio.CachePath(rel), 4096));
    }

    public AudioFileStatus GetAudioStatus(string reciterFolder, int surah, int ayah)
        => GetAudioStatus($"{reciterFolder}/{surah:D3}{ayah:D3}.mp3");

    public AudioFileStatus GetMushafPageStatus(string mushafKey, int page)
        => _mushafCache.GetOrAdd($"{mushafKey}|{page}", _ => StatFile(Path.Combine(MushafDir, mushafKey, page + ".png"), 2048));

    public static bool IsJsonReadable(string path)
    {
        try
        {
            if (!File.Exists(path) || new FileInfo(path).Length == 0) return false;
            using var fs = File.OpenRead(path);
            using var doc = JsonDocument.Parse(fs);
            return doc.RootElement.ValueKind == JsonValueKind.Object;
        }
        catch
        {
            return false;
        }
    }

    // ============ TEKS / TERJEMAHAN / TAFSIR (per-surah JSON) ============

    public sealed record TextSurahStatus(bool FileValid, int AyatFound, HashSet<int> MissingAyat, long Bytes)
    {
        public bool Complete => FileValid && MissingAyat.Count == 0;
    }

    private static TextSurahStatus ReadTextSurah(string dir, string subKey, int surah)
    {
        string path = Path.Combine(dir, subKey, surah + ".json");
        try
        {
            if (!File.Exists(path)) return new TextSurahStatus(false, 0, new HashSet<int>(), 0);
            long bytes = new FileInfo(path).Length;
            if (bytes == 0) return new TextSurahStatus(false, 0, new HashSet<int>(), 0);
            using var fs = File.OpenRead(path);
            using var doc = JsonDocument.Parse(fs);
            var found = new HashSet<int>();
            if (doc.RootElement.TryGetProperty("ayat", out var ayatEl) && ayatEl.ValueKind == JsonValueKind.Object)
            {
                foreach (var prop in ayatEl.EnumerateObject())
                {
                    if (int.TryParse(prop.Name, out int a)
                        && prop.Value.ValueKind == JsonValueKind.String
                        && !string.IsNullOrWhiteSpace(prop.Value.GetString()))
                    {
                        found.Add(a);
                    }
                }
            }
            int expected = QuranData.SurahAyahCount(surah);
            var missing = new HashSet<int>();
            for (int a = 1; a <= expected; a++)
            {
                if (!found.Contains(a)) missing.Add(a);
            }
            return new TextSurahStatus(true, found.Count, missing, bytes);
        }
        catch
        {
            return new TextSurahStatus(false, 0, new HashSet<int>(), 0);
        }
    }

    public TextSurahStatus GetTarjamaStatus(string transKey, int surah)
        => _tarjamaCache.GetOrAdd($"{transKey}|{surah}", _ => ReadTextSurah(TeksDir, transKey, surah));

    public TextSurahStatus GetTafsirStatus(string author, int surah)
        => _tafsirCache.GetOrAdd($"{author}|{surah}", _ => ReadTextSurah(TafsirDir, author, surah));

    public bool GetHiliteStatus(string mushafKey, int page)
        => _hiliteCache.GetOrAdd($"{mushafKey}|{page}", _ => IsJsonReadable(Path.Combine(HilitesDir, mushafKey, page + ".json")));

    public bool GetArabicStatus(int surah, int ayah)
    {
        if (MadinahText.HasAyah(surah, ayah)) return true;
        return GetTarjamaStatus("ar_ayat", surah).AyatFound >= ayah || !GetTarjamaStatus("ar_ayat", surah).MissingAyat.Contains(ayah);
    }

    public AyahOfflineStatus GetAyahStatus(
        int surah, int ayah, int page,
        string mushafKey,
        IEnumerable<string> translationKeys,
        IEnumerable<string> tafsirKeys,
        IEnumerable<Reciter> reciters,
        IEnumerable<VoiceTranslation> voices)
    {
        var st = new AyahOfflineStatus(surah, ayah, page);
        var mushaf = GetMushafPageStatus(mushafKey, page);
        st.MushafAvailable = mushaf.IsValid;
        st.HiliteAvailable = GetHiliteStatus(mushafKey, page);
        st.ArabicAvailable = GetArabicStatus(surah, ayah);
        foreach (var t in translationKeys)
        {
            st.TranslationAvailable[t] = !GetTarjamaStatus(t, surah).MissingAyat.Contains(ayah);
        }
        foreach (var tf in tafsirKeys)
        {
            st.TafsirAvailable[tf] = !GetTafsirStatus(tf, surah).MissingAyat.Contains(ayah);
        }
        foreach (var r in reciters)
        {
            st.ReciterAudio[r.Key] = GetAudioStatus(r.Folder, surah, ayah);
        }
        foreach (var v in voices)
        {
            st.VoiceTranslationAudio[v.Key] = GetAudioStatus(v.Folder, surah, ayah);
        }
        return st;
    }

    // ============ INVALIDATION ============

    public void InvalidateAudio(string relativePath) { _audioCache.TryRemove(relativePath.Replace('\\', '/'), out _); FireChanged(); }
    public void InvalidateMushafPage(string mushafKey, int page) { _mushafCache.TryRemove($"{mushafKey}|{page}", out _); FireChanged(); }
    public void InvalidateTarjama(string transKey, int surah) { _tarjamaCache.TryRemove($"{transKey}|{surah}", out _); FireChanged(); }
    public void InvalidateTafsir(string author, int surah) { _tafsirCache.TryRemove($"{author}|{surah}", out _); FireChanged(); }
    public void InvalidateHilite(string mushafKey, int page) { _hiliteCache.TryRemove($"{mushafKey}|{page}", out _); FireChanged(); }
    public void InvalidateAll()
    {
        _audioCache.Clear(); _mushafCache.Clear(); _tarjamaCache.Clear();
        _tafsirCache.Clear(); _hiliteCache.Clear(); _storageCache = null;
        FireChanged();
    }

    /// <summary>Invalidate cache status untuk satu file hasil download (berdasarkan path tujuan). Debounced FireChanged.</summary>
    public void InvalidateAllSilent(string destPath)
    {
        try
        {
            string root = Path.GetFullPath(CacheRoot);
            string rel = Path.GetFullPath(destPath);
            if (!rel.StartsWith(root, StringComparison.OrdinalIgnoreCase)) return;
            string relNorm = rel[(root.Length)..].TrimStart('\\', '/').Replace('\\', '/');
            if (relNorm.StartsWith("mushaf/", StringComparison.Ordinal))
            {
                var parts = relNorm.Split('/');
                if (parts.Length >= 3 && int.TryParse(Path.GetFileNameWithoutExtension(parts[2]), out int pg))
                {
                    _mushafCache.TryRemove($"{parts[1]}|{pg}", out _);
                }
            }
            else if (relNorm.StartsWith("hilites/", StringComparison.Ordinal))
            {
                var parts = relNorm.Split('/');
                if (parts.Length >= 3 && int.TryParse(Path.GetFileNameWithoutExtension(parts[2]), out int pg2))
                {
                    _hiliteCache.TryRemove($"{parts[1]}|{pg2}", out _);
                }
            }
            else
            {
                _audioCache.TryRemove(relNorm, out _);
            }
            FireChanged();
        }
        catch
        {
        }
    }

    public void ClearReciterAudioCache()
    {
        foreach (var k in _audioCache.Keys.Where(k => !k.StartsWith("mushaf/", StringComparison.Ordinal)).ToList())
        {
            _audioCache.TryRemove(k, out _);
        }
        FireChanged();
    }

    // ============ SCAN: SURAH SUMMARY ============

    public SurahOfflineSummary ScanSurah(
        int surah,
        string mushafKey,
        IReadOnlyList<string> translationKeys,
        IReadOnlyList<string> tafsirKeys,
        IReadOnlyList<Reciter> reciters)
    {
        int count = QuranData.SurahAyahCount(surah);
        var pages = new HashSet<int>();
        for (int a = 1; a <= count; a++) pages.Add(QuranData.FindPage(mushafKey, surah, a));
        int mushafTotal = pages.Count;
        int mushafOk = pages.Count(p => GetMushafPageStatus(mushafKey, p).IsValid);

        int arabOk = 0;
        var trans = translationKeys.ToDictionary(k => k, _ => 0);
        var tafs = tafsirKeys.ToDictionary(k => k, _ => 0);
        var rec = reciters.ToDictionary(k => k.Key, _ => 0);
        long bytes = 0;

        for (int a = 1; a <= count; a++)
        {
            if (GetArabicStatus(surah, a)) arabOk++;
            foreach (var k in trans.Keys.ToList())
            {
                if (!GetTarjamaStatus(k, surah).MissingAyat.Contains(a)) trans[k]++;
            }
            foreach (var k in tafs.Keys.ToList())
            {
                if (!GetTafsirStatus(k, surah).MissingAyat.Contains(a)) tafs[k]++;
            }
            foreach (var r in reciters)
            {
                var st = GetAudioStatus(r.Folder, surah, a);
                if (st.IsValid) { rec[r.Key]++; bytes += st.SizeBytes; }
            }
        }

        bool complete = mushafOk == mushafTotal
            && arabOk == count
            && trans.Values.All(v => v == count)
            && tafs.Values.All(v => v == count)
            && rec.Values.All(v => v == count);
        bool partial = !complete && (mushafOk > 0 || arabOk > 0 || trans.Values.Any(v => v > 0) || tafs.Values.Any(v => v > 0) || rec.Values.Any(v => v > 0));

        return new SurahOfflineSummary(surah, SurahList.Get(surah).EnglishName, count,
            mushafOk, mushafTotal, arabOk, trans, tafs, rec, bytes)
        { Complete = complete, Partial = partial };
    }

    // ============ SCAN: RECITER ============

    public ReciterSummary ScanReciter(Reciter reciter)
    {
        int valid = 0;
        long bytes = 0;
        int total = TotalAyat;
        for (int s = 1; s <= TotalSurah; s++)
        {
            int n = QuranData.SurahAyahCount(s);
            for (int a = 1; a <= n; a++)
            {
                var fi = new FileInfo(KsuAudio.CachePath(Path.Combine(reciter.Folder, $"{s:D3}{a:D3}.mp3")));
                if (fi.Exists && fi.Length >= 4096) { valid++; bytes += fi.Length; }
            }
        }
        return new ReciterSummary(reciter.Key, reciter.Folder, reciter.Display, valid, total, bytes);
    }

    public MushafPageSummary ScanMushaf(MushafType mt)
    {
        int total = QuranData.PageCount(mt.PageKey);
        int ok = 0;
        long bytes = 0;
        for (int p = 1; p <= total; p++)
        {
            var st = StatFile(Path.Combine(MushafDir, mt.Key, p + ".png"), 2048);
            if (st.IsValid) { ok++; bytes += st.SizeBytes; }
        }
        return new MushafPageSummary(mt.Key, mt.Display, ok, total, bytes);
    }

    public TextKeySummary ScanTextKey(string kind, string key, string display)
    {
        string dir = kind == "tafsir" ? TafsirDir : TeksDir;
        int surahsValid = 0;
        int ayat = 0;
        long bytes = 0;
        for (int s = 1; s <= TotalSurah; s++)
        {
            var st = ReadTextSurah(dir, key, s);
            if (st.FileValid) { surahsValid++; bytes += st.Bytes; }
            ayat += st.AyatFound;
        }
        return new TextKeySummary(kind, key, display, surahsValid, TotalSurah, ayat, TotalAyat, bytes);
    }

    // ============ STORAGE ============

    private StorageReport? _storageCache;

    public async Task<StorageReport> GetStorageAsync()
    {
        if (_storageCache != null) return _storageCache;
        var report = await Task.Run(() =>
        {
            var items = new List<StorageItem>();
            long total = 0;
            foreach (var mt in MushafTypes.All)
            {
                long b = DirSize(Path.Combine(MushafDir, mt.Key));
                if (b > 0) items.Add(new StorageItem($"Mushaf {mt.Display}", b));
                total += b;
            }
            foreach (var t in Translations.All)
            {
                long b = DirSize(Path.Combine(TeksDir, t.Key));
                if (b > 0) items.Add(new StorageItem($"Terjemahan {t.Display}", b));
                total += b;
            }
            foreach (var t in Tafsirs.All)
            {
                long b = DirSize(Path.Combine(TafsirDir, t.Key));
                if (b > 0) items.Add(new StorageItem($"Tafsir {t.Display}", b));
                total += b;
            }
            long hb = DirSize(HilitesDir);
            if (hb > 0) items.Add(new StorageItem("Hilite ayat", hb));
            total += hb;
            long fb = DirSize(Path.Combine(CacheRoot, "fonts"));
            if (fb > 0) items.Add(new StorageItem("Font", fb));
            total += fb;
            foreach (var r in Reciters.All)
            {
                long b = DirSize(Path.Combine(AudioDir, r.Folder));
                if (b > 0) items.Add(new StorageItem($"Audio {r.Display}", b));
                total += b;
            }
            foreach (var v in VoiceTranslations.All)
            {
                long b = DirSize(Path.Combine(AudioDir, v.Folder));
                if (b > 0) items.Add(new StorageItem($"Voice {v.Display}", b));
                total += b;
            }
            items.Add(new StorageItem("TOTAL", total));
            return new StorageReport(items, total);
        });
        _storageCache = report;
        return report;
    }

    private static long DirSize(string dir)
    {
        try
        {
            if (!Directory.Exists(dir)) return 0;
            long sum = 0;
            var di = new DirectoryInfo(dir);
            foreach (var fi in di.EnumerateFiles("*", SearchOption.AllDirectories)) sum += fi.Length;
            return sum;
        }
        catch
        {
            return 0;
        }
    }

    // ============ DELETE ============

    private static int DeleteDirIfExists(string dir)
    {
        try
        {
            if (!Directory.Exists(dir)) return 0;
            int n = Directory.GetFiles(dir, "*", SearchOption.AllDirectories).Length;
            Directory.Delete(dir, true);
            return n;
        }
        catch
        {
            return -1;
        }
    }

    public int DeleteReciterAudio(string folder)
    {
        int n = DeleteDirIfExists(Path.Combine(AudioDir, folder));
        ClearReciterAudioCache();
        _storageCache = null;
        FireChanged();
        return n;
    }

    public int DeleteTarjama(string transKey)
    {
        int n = DeleteDirIfExists(Path.Combine(TeksDir, transKey));
        foreach (var k in _tarjamaCache.Keys.Where(k => k.StartsWith(transKey + "|", StringComparison.Ordinal)).ToList())
        {
            _tarjamaCache.TryRemove(k, out _);
        }
        _storageCache = null;
        FireChanged();
        return n;
    }

    public int DeleteTafsir(string author)
    {
        int n = DeleteDirIfExists(Path.Combine(TafsirDir, author));
        foreach (var k in _tafsirCache.Keys.Where(k => k.StartsWith(author + "|", StringComparison.Ordinal)).ToList())
        {
            _tafsirCache.TryRemove(k, out _);
        }
        _storageCache = null;
        FireChanged();
        return n;
    }

    public int DeleteMushaf(string mushafKey)
    {
        int n = DeleteDirIfExists(Path.Combine(MushafDir, mushafKey));
        foreach (var k in _mushafCache.Keys.Where(k => k.StartsWith(mushafKey + "|", StringComparison.Ordinal)).ToList())
        {
            _mushafCache.TryRemove(k, out _);
        }
        _storageCache = null;
        FireChanged();
        return n;
    }

    public int DeleteAyahCache(int surah, int ayah)
    {
        int n = 0;
        foreach (var r in Reciters.All)
        {
            string p = KsuAudio.CachePath(Path.Combine(r.Folder, $"{surah:D3}{ayah:D3}.mp3"));
            if (TryDelete(p)) n++;
        }
        foreach (var v in VoiceTranslations.All)
        {
            string p = KsuAudio.CachePath(Path.Combine(v.Folder, $"{surah:D3}{ayah:D3}.mp3"));
            if (TryDelete(p)) n++;
        }
        foreach (var t in Translations.All)
        {
            // per-surah JSON — tandai surah invalid (ayat lain mungkin masih ada)
            InvalidateTarjama(t.Key, surah);
        }
        foreach (var tf in Tafsirs.All)
        {
            InvalidateTafsir(tf.Key, surah);
        }
        _storageCache = null;
        FireChanged();
        return n;
    }

    public int CleanPartFiles()
    {
        int n = 0;
        try
        {
            var root = new DirectoryInfo(CacheRoot);
            if (!root.Exists) return 0;
            foreach (var fi in root.EnumerateFiles("*.part", SearchOption.AllDirectories))
            {
                if (TryDelete(fi.FullName)) n++;
            }
        }
        catch
        {
        }
        _storageCache = null;
        FireChanged();
        return n;
    }

    private static bool TryDelete(string path)
    {
        try
        {
            if (File.Exists(path)) { File.Delete(path); return true; }
        }
        catch
        {
        }
        return false;
    }
}
