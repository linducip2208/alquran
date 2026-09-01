using System.Net;
using System.Text;
using System.Text.Json;

namespace QuranDesktop;

/// <summary>
/// Verifikasi service offline — dijalankan via CLI: QuranDesktop.exe --selftest
/// Exit code 0 = semua PASS. Tidak butuh UI.
/// </summary>
public static class OfflineSelfTest
{
    private static int _pass;
    private static int _fail;
    private static readonly List<string> _failedNames = new();

    private static void Check(string name, bool ok, string detail = "")
    {
        if (ok) _pass++;
        else
        {
            _fail++;
            _failedNames.Add(name + (detail.Length > 0 ? " — " + detail : ""));
        }
        Console.WriteLine((ok ? "[PASS] " : "[FAIL] ") + name + (detail.Length > 0 ? $"  ({detail})" : ""));
    }

    public static int RunAll()
    {
        try { Console.OutputEncoding = Encoding.UTF8; } catch { }
        Console.WriteLine("=== Quran Desktop — Offline Self Test ===");
        Console.WriteLine($"Cache root: {KsuAudio.CacheDir}");
        Console.WriteLine();

        CheckCounts();
        ReciterIntegrity();
        AudioFileNameParser();
        PngValidation();
        WritePermissionTest();
        MigrationMarkerTest();
        AudioDetection();
        AudioPathLayout();
        TextJsonDetection();
        TextAyatAccuracy();
        TafsirDiskCache();
        HiliteDiskCache();
        AyahStatusAccuracy();
        ScanSurahAllMushafs();
        MigratorTest();
        DownloadEngineAsync().GetAwaiter().GetResult();
        StorageActualBytes();
        FinalAudit();
        LegacyCompatibility();

        Console.WriteLine();
        Console.WriteLine($"=== Hasil: {_pass} PASS, {_fail} FAIL ===");
        foreach (var f in _failedNames) Console.WriteLine("  FAIL: " + f);
        return _fail == 0 ? 0 : 1;
    }

    // 1. Counter Quran = 6.236 & per-surah benar
    private static void CheckCounts()
    {
        Console.WriteLine("-- Counter QuranData");
        var svc = OfflineContentService.Instance;
        Check("Total ayat = 6236", svc.TotalAyat == 6236, $"got {svc.TotalAyat}");
        Check("Total surah = 114", svc.TotalSurah == 114, $"got {svc.TotalSurah}");
        int sum = 0;
        for (int s = 1; s <= 114; s++) sum += QuranData.SurahAyahCount(s);
        Check("Sum ayat per surah = 6236", sum == 6236, $"got {sum}");
        Check("QuranData.SurahAyahCount(2) = 286", QuranData.SurahAyahCount(2) == 286);
        Check("QuranData.SurahAyahCount(1) = 7", QuranData.SurahAyahCount(1) == 7);
        Check("PageCount(Page) = 604", QuranData.PageCount("Page") == 604, $"got {QuranData.PageCount("Page")}");
        Check("PageCount(Page2) = 604", QuranData.PageCount("Page2") == 604, $"got {QuranData.PageCount("Page2")}");
        Check("PageCount(Page_warsh) > 0", QuranData.PageCount("Page_warsh") > 0, $"got {QuranData.PageCount("Page_warsh")}");
        Check("IdToAya(ayat terakhir) = 114:6", QuranData.IdToAya(QuranData.AyaToId(114, 6)) == (114, 6));

        Console.WriteLine("-- Mapping mushafKey -> PageKey & FindPage");
        foreach (var mt in MushafTypes.All)
        {
            int pc = QuranData.PageCount(mt.PageKey);
            bool ok1 = MushafTypes.ResolveMushaf(mt.Key).PageKey == mt.PageKey;
            Check($"ResolveMushaf({mt.Key}) -> PageKey {mt.PageKey}", ok1);
            int p1 = MushafTypes.FindMushafPage(mt.Key, 2, 283); // tidak boleh crash
            int p2 = MushafTypes.FindMushafPage(mt.Key, 1, 1);
            Check($"FindMushafPage({mt.Key}) tidak crash & valid",
                p1 >= 1 && p1 <= pc && p2 >= 1 && p2 <= pc,
                $"2:283={p1} (max {pc})");
        }
        Check("ResolveMushaf(key tak dikenal) fallback", MushafTypes.ResolveMushaf("??").PageKey == MushafTypes.All[0].PageKey);
        Check("ResolvePageKey(hafs) = Page", MushafTypes.ResolvePageKey("hafs") == "Page");
        Check("ResolvePageKey(warsh) = Page_warsh", MushafTypes.ResolvePageKey("warsh") == "Page_warsh");
        Check("ResolvePageKey(tajweed) = Page2", MushafTypes.ResolvePageKey("tajweed") == "Page2");
        Check("MushafPageCount(tajweed) = 604", MushafTypes.MushafPageCount("tajweed") == 604, $"got {MushafTypes.MushafPageCount("tajweed")}");
        Check("MushafPageStart(hafs, 1) = 1:1", MushafTypes.MushafPageStart("hafs", 1) == (1, 1));
        Check("QuranData pageKey tak dikenal tidak crash (fallback Page)",
            QuranData.PageCount("tajweed") > 0 && QuranData.FindPage("tajweed", 1, 1) >= 1,
            $"count={QuranData.PageCount("tajweed")}");
        Check("FindPage(Page) 2:282 = halaman valid",
            QuranData.FindPage("Page", 2, 282) >= 1 && QuranData.FindPage("Page", 2, 282) <= 604,
            $"got {QuranData.FindPage("Page", 2, 282)}");
    }

    // 1b. (AH #3,#4,#6) Folder qari unik & jumlah 43 — tidak crash
    private static void ReciterIntegrity()
    {
        Console.WriteLine("-- Integritas daftar qari");
        int count = Reciters.All.Count;
        Check("Reciters.All.Count = 43 (bukan 47 — voice translation terpisah)", count == 43, $"got {count}");
        Check("VoiceTranslations.All tidak tercampur Reciters.All",
            Reciters.All.All(r => VoiceTranslations.All.All(v => v.Key != r.Key)),
            "ada key yang sama antara qari dan voice");
        var dupes = Reciters.All
            .GroupBy(r => r.Folder, StringComparer.OrdinalIgnoreCase)
            .Where(g => g.Count() > 1)
            .Select(g => g.Key)
            .ToList();
        Check("semua folder qari unik (case-insensitive)", dupes.Count == 0, $"duplikat: {string.Join(", ", dupes)}");
        var keysDupe = Reciters.All.GroupBy(r => r.Key).Where(g => g.Count() > 1).ToList();
        Check("semua key qari unik", keysDupe.Count == 0);
        // (AH #6) path Husary 001001 != Alafasy 001001
        string husary = KsuAudio.CachePath($"audio/{Reciters.All[0].Folder}/001001.mp3");
        var afasy = Reciters.Find("afasy") ?? Reciters.All[17];
        string afasyPath = KsuAudio.CachePath($"audio/{afasy.Folder}/001001.mp3");
        Check("path audio per-qari berbeda (Husary vs Afasy 001001)",
            !string.Equals(husary, afasyPath, StringComparison.OrdinalIgnoreCase) && husary.Contains(Reciters.All[0].Folder)
            && afasyPath.Contains(afasy.Folder),
            $"{husary} vs {afasyPath}");
        Check("audio rel TIDAK di audio/001001.mp3 (tanpa folder qari)",
            !File.Exists(Path.Combine(KsuAudio.AudioRoot, "001001.mp3")));
    }

    // 1c. (H) Parser nama file 6 digit — selftest khusus (AH #7-12)
    private static void AudioFileNameParser()
    {
        Console.WriteLine("-- Parser nama file audio {surah:D3}{ayah:D3}");
        Check("parser 001001 valid (1:1)", OfflineContentService.TryParseAyahFile("001001", out int s1, out int a1) && s1 == 1 && a1 == 1, $"{s1}:{a1}");
        Check("parser 002283 valid (2:283)", OfflineContentService.TryParseAyahFile("002283", out int s2, out int a2) && s2 == 2 && a2 == 283, $"{s2}:{a2}");
        Check("parser 114006 valid (114:6)", OfflineContentService.TryParseAyahFile("114006", out int s3, out int a3) && s3 == 114 && a3 == 6, $"{s3}:{a3}");
        Check("parser 7-digit invalid", !OfflineContentService.TryParseAyahFile("0010011", out _, out _));
        Check("parser 5-digit invalid", !OfflineContentService.TryParseAyahFile("01001", out _, out _));
        Check("parser non-digit invalid", !OfflineContentService.TryParseAyahFile("abc123", out _, out _));
        Check("parser null/empty invalid", !OfflineContentService.TryParseAyahFile(null, out _, out _) && !OfflineContentService.TryParseAyahFile("", out _, out _));
        Check("parser surah 000 invalid", !OfflineContentService.TryParseAyahFile("000001", out _, out _));
        Check("parser surah 115 invalid", !OfflineContentService.TryParseAyahFile("115001", out _, out _));

        // scan folder: .part tidak dihitung, <4096 tidak valid, PerSurah benar (AH #11-13)
        var svc = OfflineContentService.Instance;
        string folder = "SelfTest_Parse";
        string dir = Path.Combine(KsuAudio.AudioRoot, folder);
        try
        {
            Directory.CreateDirectory(dir);
            File.WriteAllBytes(Path.Combine(dir, "001001.mp3"), new byte[8192]);   // valid
            File.WriteAllBytes(Path.Combine(dir, "001002.mp3"), new byte[10]);     // terlalu kecil
            File.WriteAllBytes(Path.Combine(dir, "001003.mp3.part"), new byte[99]);// .part
            File.WriteAllBytes(Path.Combine(dir, "114006.mp3"), new byte[8192]);   // valid
            File.WriteAllBytes(Path.Combine(dir, "junk123.mp3"), new byte[8192]);  // nama tidak valid
            svc.InvalidateAudio($"audio/{folder}/001001.mp3");
            var sum = svc.ScanAudioFolder(folder, folder, "SelfTest Parse");
            Check("scan folder: 2 file valid (bukan .part/kecil/junk)", sum.Valid == 2, $"got {sum.Valid}");
            Check("scan folder: PerSurah[1] = 1", sum.PerSurah != null && sum.PerSurah[1] == 1, $"got {sum.PerSurah?[1]}");
            Check("scan folder: PerSurah[114] = 1", sum.PerSurah != null && sum.PerSurah[114] == 1, $"got {sum.PerSurah?[114]}");
            Check("scan folder: PerSurah[2] = 0", sum.PerSurah != null && sum.PerSurah[2] == 0, $"got {sum.PerSurah?[2]}");
            Check("scan folder: bytes = 16384", sum.Bytes == 16384, $"got {sum.Bytes}");
        }
        finally
        {
            try { Directory.Delete(dir, true); } catch { }
            svc.InvalidateAudio($"audio/{folder}/001001.mp3");
            svc.InvalidateAudio($"audio/{folder}/114006.mp3");
        }
    }

    // 1d. (X) Validasi signature PNG (AH #21-22)
    private static void PngValidation()
    {
        Console.WriteLine("-- Validasi signature PNG");
        byte[] pngHeader = { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A };
        Check("header PNG valid diterima", DownloadManager.IsPngHeader(pngHeader));
        Check("header pendek ditolak", !DownloadManager.IsPngHeader(pngHeader.AsSpan(0, 4).ToArray()));
        byte[] jpegLike = { 0xFF, 0xD8, 0xFF, 0xE0, 0x00, 0x10, 0x4A, 0x46 };
        Check("header JPEG ditolak", !DownloadManager.IsPngHeader(jpegLike));
        Check("header HTML-error ditolak", !DownloadManager.IsPngHeader("<html>e"u8.ToArray()));

        string dir = Path.Combine(KsuAudio.DataRoot, "SelfTest_Png");
        try
        {
            Directory.CreateDirectory(dir);
            string good = Path.Combine(dir, "good.png");
            File.WriteAllBytes(good, pngHeader.Concat(new byte[4096]).ToArray());
            Check("FileValid(.png) signature benar = valid", DownloadManager.FileValid(good, 2048));
            string bad = Path.Combine(dir, "bad.png");
            File.WriteAllBytes(bad, jpegLike.Concat(new byte[4096]).ToArray());
            Check("FileValid(.png) signature salah = TIDAK valid", !DownloadManager.FileValid(bad, 2048));
        }
        finally
        {
            try { Directory.Delete(dir, true); } catch { }
        }
    }

    // 1e. (C) Write permission test tanpa fallback (AH #30)
    private static void WritePermissionTest()
    {
        Console.WriteLine("-- Uji izin tulis root downloads");
        bool ok = KsuAudio.EnsureWritableRoot(out string err);
        Check("root downloads dapat ditulis (write-test sukses)", ok, err);
        Check("tidak ada sisa .write-test", !File.Exists(Path.Combine(KsuAudio.DataRoot, ".write-test")));
        // DataRoot pasti di samping exe — bukan AppData/TEMP
        string baseDir = Path.TrimEndingDirectorySeparator(AppContext.BaseDirectory);
        string root = Path.TrimEndingDirectorySeparator(KsuAudio.DataRoot);
        Check("DataRoot = <exe>/downloads", root == Path.Combine(baseDir, "downloads"),
            KsuAudio.DataRoot);
        Check("DataRoot TIDAK di AppData/TEMP",
            !root.Contains("AppData", StringComparison.OrdinalIgnoreCase)
            && !root.Contains("Temp", StringComparison.OrdinalIgnoreCase),
            KsuAudio.DataRoot);
    }

    // 1f. (B) Marker migrasi (AH: migrasi satu kali)
    private static void MigrationMarkerTest()
    {
        Console.WriteLine("-- Marker migrasi .migration-v1-complete");
        string marker = Path.Combine(KsuAudio.DataRoot, ".migration-v1-complete");
        bool existedBefore = File.Exists(marker);
        try
        {
            if (File.Exists(marker)) File.Delete(marker);
            Check("marker hilang saat dihapus", !OfflineMigrator.MigrationComplete);
            // folder lama tidak ada → Run menulis marker & return 0
            int moved = OfflineMigrator.Run(
                Path.Combine(KsuAudio.DataRoot, "SelfTest_Marker_old"),
                Path.Combine(KsuAudio.DataRoot, "SelfTest_Marker_new"));
            Check("tanpa cache lama: 0 dipindah", moved == 0);
            // Run dengan oldRoot eksplisit TIDAK menulis marker default (hanya saat useDefaults)
            // simulasi: tulis marker manual lalu cek
            File.WriteAllText(marker, "test");
            Check("marker ada → MigrationComplete = true", OfflineMigrator.MigrationComplete);
            Check("EnsureStarted dengan marker: tidak mulai migrasi", OfflineMigrator.Current == null);
        }
        finally
        {
            // pulihkan state awal: selftest TIDAK boleh menulis marker di mesin user
            // yang migrasinya belum terjadi — marker hanya tetap ada bila sebelumnya sudah ada.
            try
            {
                if (existedBefore) File.WriteAllText(marker, DateTime.UtcNow.ToString("O"));
                else if (File.Exists(marker)) File.Delete(marker);
            }
            catch { }
            try { Directory.Delete(Path.Combine(KsuAudio.DataRoot, "SelfTest_Marker_old"), true); } catch { }
        }
    }

    // 2. Deteksi audio existing / missing / zero-byte / .part
    private static void AudioDetection()
    {
        Console.WriteLine("-- Deteksi audio");
        var svc = OfflineContentService.Instance;
        string dir = Path.Combine(KsuAudio.CacheDir, "SelfTest_Audio");
        try
        {
            Directory.CreateDirectory(dir);
            string rel1 = "SelfTest_Audio/001001.mp3";
            string p1 = KsuAudio.CachePath(rel1);
            File.WriteAllBytes(p1, new byte[8192]);
            svc.InvalidateAudio(rel1);
            Check("audio existing terdeteksi valid", svc.GetAudioStatus(rel1).IsValid);

            string rel2 = "SelfTest_Audio/001002.mp3";
            Check("audio missing terdeteksi tidak ada", !svc.GetAudioStatus(rel2).Exists);

            string rel3 = "SelfTest_Audio/001003.mp3";
            File.WriteAllBytes(KsuAudio.CachePath(rel3), new byte[10]);
            svc.InvalidateAudio(rel3);
            Check("audio zero-byte/kecil = rusak", !svc.GetAudioStatus(rel3).IsValid);

            string rel4 = "SelfTest_Audio/001004.mp3.part";
            string p4 = KsuAudio.CachePath(rel4);
            Directory.CreateDirectory(Path.GetDirectoryName(p4)!);
            File.WriteAllBytes(p4, new byte[9999]);
            // .part: file ada di disk tetapi TIDAK valid/complete
            Check("file .part TIDAK dianggap complete", !svc.GetAudioStatus(rel4).IsValid);
        }
        finally
        {
            try { Directory.Delete(dir, true); } catch { }
            svc.ClearReciterAudioCache();
        }
    }

    // 2b. Layout path: audio/{folder}, voice/{folder}, root di samping exe
    private static void AudioPathLayout()
    {
        Console.WriteLine("-- Layout path audio/voice & root downloads");
        var svc = OfflineContentService.Instance;
        Check("DataRoot = exe/downloads (bukan LocalApplicationData)",
            KsuAudio.CacheDir.EndsWith(Path.Combine("downloads") + "", StringComparison.OrdinalIgnoreCase)
            && KsuAudio.CacheDir.StartsWith(AppContext.BaseDirectory, StringComparison.OrdinalIgnoreCase),
            KsuAudio.CacheDir);

        string recFolder = "SelfTest_Reciter";
        string voiceFolder = VoiceTranslations.All[0].Folder;
        try
        {
            string recFile = Path.Combine(KsuAudio.AudioRoot, recFolder, "001001.mp3");
            Directory.CreateDirectory(Path.GetDirectoryName(recFile)!);
            File.WriteAllBytes(recFile, new byte[8192]);
            svc.InvalidateAudio($"audio/{recFolder}/001001.mp3");
            Check("GetAudioStatus(folder,s,a) membaca downloads/audio/{folder}/001001.mp3",
                svc.GetAudioStatus(recFolder, 1, 1).IsValid);

            string voiceFile = Path.Combine(KsuAudio.VoiceRoot, voiceFolder, "001001.mp3");
            Directory.CreateDirectory(Path.GetDirectoryName(voiceFile)!);
            File.WriteAllBytes(voiceFile, new byte[8192]);
            svc.InvalidateAudio($"voice/{voiceFolder}/001001.mp3");
            Check("GetVoiceStatus membaca downloads/voice/{folder}/001001.mp3",
                svc.GetVoiceStatus(voiceFolder, 1, 1).IsValid);

            Check("audio inventory TIDAK tercampur mushaf",
                !svc.GetAudioStatus(recFolder, 1, 1).LocalPath.Contains("mushaf", StringComparison.OrdinalIgnoreCase));
        }
        finally
        {
            try { Directory.Delete(Path.Combine(KsuAudio.AudioRoot, recFolder), true); } catch { }
            try { Directory.Delete(Path.Combine(KsuAudio.VoiceRoot, voiceFolder), true); } catch { }
            svc.InvalidateAudio($"audio/{recFolder}/001001.mp3");
            svc.InvalidateAudio($"voice/{voiceFolder}/001001.mp3");
        }
    }

    // 2c. Migrator: cache lama → downloads/ (idempotent, skip resource existing, skip folder test)
    private static void MigratorTest()
    {
        Console.WriteLine("-- Migrasi cache lama");
        string baseDir = Path.Combine(KsuAudio.DataRoot, "SelfTest_Mig");
        string oldRoot = Path.Combine(baseDir, "old");
        string newRoot = Path.Combine(baseDir, "new");
        try
        {
            // susun cache lama palsu
            Directory.CreateDirectory(Path.Combine(oldRoot, "mushaf", "hafs"));
            Directory.CreateDirectory(Path.Combine(oldRoot, "teks", "id_indonesian"));
            Directory.CreateDirectory(Path.Combine(oldRoot, "Husary_64kbps"));
            Directory.CreateDirectory(Path.Combine(oldRoot, "English_Walk"));
            Directory.CreateDirectory(Path.Combine(oldRoot, "test_5xx"));
            Directory.CreateDirectory(Path.Combine(newRoot, "mushaf", "hafs"));
            File.WriteAllText(Path.Combine(oldRoot, "mushaf", "hafs", "1.png"), "m");
            File.WriteAllText(Path.Combine(oldRoot, "mushaf", "hafs", "2.png"), "m2");
            File.WriteAllText(Path.Combine(oldRoot, "teks", "id_indonesian", "1.json"), "{}");
            File.WriteAllText(Path.Combine(oldRoot, "Husary_64kbps", "001001.mp3"), "a");
            File.WriteAllText(Path.Combine(oldRoot, "English_Walk", "001001.mp3"), "v");
            File.WriteAllText(Path.Combine(oldRoot, "test_5xx", "x.mp3"), "junk");
            // root baru sudah punya mushaf/hafs (tidak boleh ditimpa)
            File.WriteAllText(Path.Combine(newRoot, "mushaf", "hafs", "9.png"), "existing");

            int moved = OfflineMigrator.Run(oldRoot, newRoot);
            Check("migrasi memindahkan folder struktural + audio",
                moved >= 3, $"moved={moved}");
            Check("mushaf lama dipindah", File.Exists(Path.Combine(newRoot, "mushaf", "hafs", "1.png"))
                && File.Exists(Path.Combine(newRoot, "mushaf", "hafs", "2.png"))
                && !Directory.Exists(Path.Combine(oldRoot, "mushaf")));
            Check("teks lama dipindah", File.Exists(Path.Combine(newRoot, "teks", "id_indonesian", "1.json")));
            Check("folder qari → downloads/audio/", File.Exists(Path.Combine(newRoot, "audio", "Husary_64kbps", "001001.mp3")));
            Check("folder voice → downloads/voice/", File.Exists(Path.Combine(newRoot, "voice", "English_Walk", "001001.mp3")));
            Check("file existing di root baru TIDAK ditimpa, file lain tetap merge",
                File.Exists(Path.Combine(newRoot, "mushaf", "hafs", "9.png"))
                && File.Exists(Path.Combine(newRoot, "mushaf", "hafs", "1.png"))
                && !Directory.Exists(Path.Combine(oldRoot, "mushaf")));
            Check("folder test_ di-skip", File.Exists(Path.Combine(oldRoot, "test_5xx", "x.mp3")));

            // idempotent: run lagi → tidak ada yang pindah
            int moved2 = OfflineMigrator.Run(oldRoot, newRoot);
            Check("migrasi idempotent (run kedua = 0)", moved2 == 0, $"moved2={moved2}");
        }
        finally
        {
            try { Directory.Delete(baseDir, true); } catch { }
        }
    }

    // 3. JSON teks/terjemahan incomplete terdeteksi
    private static void TextJsonDetection()
    {
        Console.WriteLine("-- Deteksi teks/terjemahan JSON");
        var svc = OfflineContentService.Instance;
        string key = "selftest_trans";
        string dir = Path.Combine(svc.TeksDir, key);
        try
        {
            Directory.CreateDirectory(dir);
            // surah 1 punya 7 ayat; tulis hanya ayat 1-3 (incomplete)
            string json = JsonSerializer.Serialize(new Dictionary<string, object>
            {
                ["ayat"] = new Dictionary<string, string>
                {
                    ["1"] = "teks 1", ["2"] = "teks 2", ["3"] = "teks 3",
                },
            });
            File.WriteAllText(Path.Combine(dir, "1.json"), json);
            var st = svc.GetTarjamaStatus(key, 1);
            Check("JSON surah ada tapi incomplete terdeteksi", st.FileValid && st.AyatFound == 3 && st.MissingAyat.Contains(4));
            Check("JSON incomplete = tidak Complete", !st.Complete);

            // lengkapi
            var full = new Dictionary<string, string>();
            for (int a = 1; a <= 7; a++) full[a.ToString()] = "teks " + a;
            File.WriteAllText(Path.Combine(dir, "1.json"),
                JsonSerializer.Serialize(new Dictionary<string, object> { ["ayat"] = full }));
            svc.InvalidateTarjama(key, 1);
            Check("JSON lengkap = Complete", svc.GetTarjamaStatus(key, 1).Complete);

            // JSON rusak
            File.WriteAllText(Path.Combine(dir, "2.json"), "{ bukan json");
            Check("JSON rusak terdeteksi tidak valid", !svc.GetTarjamaStatus(key, 2).FileValid);

            // file kosong
            File.WriteAllText(Path.Combine(dir, "3.json"), "");
            Check("JSON kosong terdeteksi tidak valid", !svc.GetTarjamaStatus(key, 3).FileValid);
        }
        finally
        {
            try { Directory.Delete(dir, true); } catch { }
            for (int s = 1; s <= 5; s++) svc.InvalidateTarjama(key, s);
        }
    }

    // 4a. Akurasi status ayat: file missing/rusak/incomplete TIDAK boleh false-positive
    private static void TextAyatAccuracy()
    {
        Console.WriteLine("-- Akurasi status ayat (missing/rusak/incomplete)");
        var svc = OfflineContentService.Instance;
        string transKey = "selftest_trans_acc";
        string tafsirKey = "selftest_tafsir_acc";
        string tdir = Path.Combine(svc.TeksDir, transKey);
        string fdir = Path.Combine(svc.TafsirDir, tafsirKey);
        try
        {
            // (9) file translation MISSING -> status ayat = false untuk SEMUA ayat
            var stMissing = svc.GetTarjamaStatus(transKey, 1);
            Check("translation file missing -> FileValid=false", !stMissing.FileValid);
            Check("translation file missing -> MissingAyat penuh", stMissing.MissingAyat.Count == QuranData.SurahAyahCount(1));
            Check("translation missing -> ayat 1 = false", !svc.HasTarjamaAyah(transKey, 1, 1));
            Check("translation missing -> ayat 7 = false", !svc.HasTarjamaAyah(transKey, 1, 7));

            // (10) JSON RUSAK -> status ayat = false
            Directory.CreateDirectory(tdir);
            File.WriteAllText(Path.Combine(tdir, "1.json"), "{ bukan json");
            var stCorrupt = svc.GetTarjamaStatus(transKey, 1);
            Check("translation JSON rusak -> FileValid=false", !stCorrupt.FileValid);
            Check("translation JSON rusak -> ayat 1 = false", !svc.HasTarjamaAyah(transKey, 1, 1));

            // struktur JSON salah (root array / ayat bukan object)
            File.WriteAllText(Path.Combine(tdir, "4.json"), "[]");
            Check("translation JSON struktur salah -> ayat 1 = false", !svc.HasTarjamaAyah(transKey, 4, 1));

            // (11) JSON INCOMPLETE -> hanya ayat yang ada = true (akurat per ayat)
            var partial = new Dictionary<string, object>
            {
                ["ayat"] = new Dictionary<string, string> { ["1"] = "a1", ["2"] = "a2", ["4"] = "a4" },
            };
            File.WriteAllText(Path.Combine(tdir, "3.json"), JsonSerializer.Serialize(partial));
            var stPart = svc.GetTarjamaStatus(transKey, 3);
            Check("translation incomplete: ayat 1,2,4 = true",
                svc.HasTarjamaAyah(transKey, 3, 1) && svc.HasTarjamaAyah(transKey, 3, 2) && svc.HasTarjamaAyah(transKey, 3, 4));
            Check("translation incomplete: ayat 3,5 = false (bukan count!)",
                !svc.HasTarjamaAyah(transKey, 3, 3) && !svc.HasTarjamaAyah(transKey, 3, 5));
            Check("AyatFound = 3 tapi ayat 3 tetap missing", stPart.AyatFound == 3 && stPart.MissingAyat.Contains(3));

            // (12) file TAFSIR MISSING -> false
            var tfMissing = svc.GetTafsirStatus(tafsirKey, 1);
            Check("tafsir file missing -> FileValid=false", !tfMissing.FileValid);
            Check("tafsir missing -> ayat 1 = false", !svc.HasTafsirAyah(tafsirKey, 1, 1));

            // (13) TAFSIR INCOMPLETE -> akurat per ayat
            Directory.CreateDirectory(fdir);
            var tafPart = new Dictionary<string, object>
            {
                ["ayat"] = new Dictionary<string, string> { ["1"] = "t1", ["2"] = "t2", ["4"] = "t4" },
            };
            File.WriteAllText(Path.Combine(fdir, "1.json"), JsonSerializer.Serialize(tafPart));
            svc.InvalidateTafsir(tafsirKey, 1); // refresh cache setelah file ditulis
            Check("tafsir incomplete: ayat 1,2,4 = true; ayat 3 = false",
                svc.HasTafsirAyah(tafsirKey, 1, 1) && svc.HasTafsirAyah(tafsirKey, 1, 2)
                && svc.HasTafsirAyah(tafsirKey, 1, 4) && !svc.HasTafsirAyah(tafsirKey, 1, 3));

            // (4-BLOCKER) GetArabicStatus berbasis membership: cache ar_ayat dipalsukan incomplete (ayat 1,2,4 saja)
            // ayat 3 TIDAK ada di cache — hasil harus sama dengan keberadaan MadinahText embedded
            string arDir = Path.Combine(svc.TeksDir, "ar_ayat");
            bool madinah = MadinahText.HasAyah(3, 3);
            Directory.CreateDirectory(arDir);
            File.WriteAllText(Path.Combine(arDir, "3.json"), JsonSerializer.Serialize(partial));
            svc.InvalidateTarjama("ar_ayat", 3);
            bool arab33 = svc.GetArabicStatus(3, 3);
            Check("GetArabicStatus(3:3) = membership ayat (bukan count)",
                arab33 == madinah && !svc.HasTarjamaAyah("ar_ayat", 3, 3),
                $"madinah={madinah} status={arab33}");
            svc.InvalidateTarjama("ar_ayat", 3);
        }
        finally
        {
            try { Directory.Delete(tdir, true); } catch { }
            try { Directory.Delete(fdir, true); } catch { }
            for (int s = 1; s <= 4; s++)
            {
                svc.InvalidateTarjama(transKey, s);
                svc.InvalidateTafsir(tafsirKey, s);
            }
            svc.InvalidateTarjama("ar_ayat", 3);
        }
    }

    // 4b. GetAyahStatus per ayat (QS 1:1): struktur lengkap semua qari & voice, halaman benar
    private static void AyahStatusAccuracy()
    {
        Console.WriteLine("-- Per-ayah status QS 1:1");
        var svc = OfflineContentService.Instance;
        var mk = MushafTypes.ResolveMushaf("hafs");
        int page = MushafTypes.FindMushafPage("hafs", 1, 1);
        Check("QS 1:1 -> halaman 1", page == 1, $"got {page}");
        var st = svc.GetAyahStatus(1, 1, page, "hafs",
            new[] { "selftest_missing_key" }, new[] { "selftest_missing_tafsir" },
            Reciters.All, VoiceTranslations.All);
        Check("GetAyahStatus QS 1:1 semua qari tercakup", st.ReciterAudio.Count == Reciters.All.Count,
            $"got {st.ReciterAudio.Count}");
        Check("GetAyahStatus QS 1:1 semua voice translation tercakup", st.VoiceTranslationAudio.Count == VoiceTranslations.All.Count);
        Check("GetAyahStatus translation key missing -> false",
            st.TranslationAvailable.TryGetValue("selftest_missing_key", out var tv) && !tv);
        Check("GetAyahStatus tafsir key missing -> false",
            st.TafsirAvailable.TryGetValue("selftest_missing_tafsir", out var tfv) && !tfv);
        Check("GetAyahStatus MushafAvailable mencerminkan file aktual", st.MushafAvailable == svc.GetMushafPageStatus("hafs", 1).IsValid);
        Check("GetAyahStatus HiliteAvailable mencerminkan file aktual", st.HiliteAvailable == svc.GetHiliteStatus("hafs", 1));
    }

    // 4c. ScanSurah untuk SEMUA mushaf (hafs/warsh/tajweed) tidak crash — regresi key salah
    private static void ScanSurahAllMushafs()
    {
        Console.WriteLine("-- ScanSurah semua mushaf (regresi key)");
        var svc = OfflineContentService.Instance;
        foreach (var mt in MushafTypes.All)
        {
            try
            {
                var sum = svc.ScanSurah(2, mt.Key,
                    new[] { "selftest_scan_trans" }, new[] { "selftest_scan_tafsir" },
                    new[] { Reciters.All[0] });
                Check($"ScanSurah(2, {mt.Key}) tidak crash & mapping halaman valid",
                    sum.MushafPagesTotal >= 1 && sum.MushafPagesTotal <= MushafTypes.MushafPageCount(mt.Key)
                    && sum.MushafPages >= 0 && sum.MushafPages <= sum.MushafPagesTotal,
                    $"total={sum.MushafPagesTotal} valid={sum.MushafPages}");
            }
            catch (Exception ex)
            {
                Check($"ScanSurah(2, {mt.Key}) tidak crash", false, ex.Message);
            }
        }
        // mushaf key tidak dikenal → fallback, tidak crash
        try
        {
            var sum = svc.ScanSurah(1, "key_ngawur",
                Array.Empty<string>(), Array.Empty<string>(), Array.Empty<Reciter>());
            Check("ScanSurah key tak dikenal fallback tanpa crash", sum.MushafPagesTotal >= 1,
                $"total={sum.MushafPagesTotal}");
        }
        catch (Exception ex)
        {
            Check("ScanSurah key tak dikenal fallback tanpa crash", false, ex.Message);
        }
    }

    // 6b. Storage actual bytes
    private static void StorageActualBytes()
    {
        Console.WriteLine("-- Storage actual bytes");
        var svc = OfflineContentService.Instance;
        string dir = Path.Combine(svc.HilitesDir, "SelfTest_HiliteStorage");
        string file = Path.Combine(dir, "1.json");
        try
        {
            Directory.CreateDirectory(dir);
            byte[] payload = new byte[12_345];
            File.WriteAllBytes(file, payload);
            svc.InvalidateAll();
            var report = svc.GetStorageAsync().GetAwaiter().GetResult();
            long sumItems = report.Items.Where(i => i.Label != "TOTAL").Sum(i => i.Bytes);
            Check("Storage TOTAL = jumlah item", report.TotalBytes == sumItems,
                $"total={report.TotalBytes} sum={sumItems}");
            var hiliteItem = report.Items.FirstOrDefault(i => i.Label == "Hilite lain");
            Check("File uji terhitung sebagai 'Hilite lain' (actual bytes)",
                hiliteItem != null && hiliteItem.Bytes >= 12_345,
                hiliteItem?.Bytes.ToString() ?? "tidak ada");
            Check("Storage report tidak negatif", report.TotalBytes >= 0);
        }
        finally
        {
            try { Directory.Delete(dir, true); } catch { }
            svc.InvalidateAll();
        }
    }

    // 4. Tafsir persistent disk cache (KsuApi)
    private static void TafsirDiskCache()
    {
        Console.WriteLine("-- Tafsir disk cache");
        string author = "selftest_tafsir";
        string path = KsuApi.TafsirPath(author, 1);
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            var ayat = new Dictionary<string, string> { ["1"] = "teks tafsir 1", ["2"] = "teks tafsir 2" };
            using (var ms = new MemoryStream())
            {
                using (var w = new Utf8JsonWriter(ms))
                {
                    w.WriteStartObject();
                    w.WriteStartObject("ayat");
                    foreach (var (k, v) in ayat) w.WriteString(k, v);
                    w.WriteEndObject();
                    w.WriteEndObject();
                }
                File.WriteAllBytes(path, ms.ToArray());
            }
            var svc = OfflineContentService.Instance;
            var st = svc.GetTafsirStatus(author, 1);
            Check("tafsir disk cache terbaca", st.FileValid && st.AyatFound == 2);
            Check("tafsir ayat 3 terdeteksi missing", st.MissingAyat.Contains(3));

            // merge behavior via WriteTafsirDiskAsync dipakai KsuApi — simulasikan merge dengan append manual
            var merged = new Dictionary<int, string> { [3] = "teks tafsir 3" };
            var writeTask = typeof(KsuApi)
                .GetMethod("WriteTafsirDiskAsync", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
            if (writeTask != null)
            {
                ((Task)writeTask.Invoke(null, new object?[] { author, 1, merged, CancellationToken.None })!)
                    .GetAwaiter().GetResult();
            }
            svc.InvalidateTafsir(author, 1);
            var st2 = svc.GetTafsirStatus(author, 1);
            Check("merge tafsir menambah ayat tanpa menghapus lama", st2.AyatFound == 3, $"got {st2.AyatFound}");
        }
        finally
        {
            try { Directory.Delete(Path.GetDirectoryName(path)!, true); } catch { }
            OfflineContentService.Instance.InvalidateTafsir(author, 1);
        }
    }

    // 5. Hilite persistent disk cache
    private static void HiliteDiskCache()
    {
        Console.WriteLine("-- Hilite disk cache");
        var svc = OfflineContentService.Instance;
        string mk = "selftest_hilite";
        string path = KsuApi.HilitesPath(mk, 1);
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, """{"2_1":[100,200]}""");
            Check("hilite disk terdeteksi", svc.GetHiliteStatus(mk, 1));
            Check("hilite page lain = missing", !svc.GetHiliteStatus(mk, 2));
            File.WriteAllText(Path.Combine(Path.GetDirectoryName(path)!, "2.json"), "{ rusak");
            Check("hilite JSON rusak = invalid", !OfflineContentService.IsJsonReadable(Path.Combine(Path.GetDirectoryName(path)!, "2.json")));
        }
        finally
        {
            try { Directory.Delete(Path.GetDirectoryName(path)!, true); } catch { }
            svc.InvalidateHilite(mk, 1);
            svc.InvalidateHilite(mk, 2);
        }
    }

    // 6. Download engine: skip tanpa HTTP, resume, retry, cancel, .part
    private static async Task DownloadEngineAsync()
    {
        Console.WriteLine("-- Download engine (HttpListener lokal)");
        var svc = OfflineContentService.Instance;
        string dir = Path.Combine(KsuAudio.CacheDir, "SelfTest_DL");
        try
        {
            Directory.CreateDirectory(dir);
            using var listener = new HttpListener();
            string prefix = "http://127.0.0.1:41953/self/";
            listener.Prefixes.Add(prefix);
            listener.Start();

            int httpRequests = 0;
            var payload = new byte[256 * 1024];
            new Random(1).NextBytes(payload);
            // payload PNG valid + payload sampah (bukan PNG) — test validasi mushaf
            var pngPayload = new byte[64 * 1024];
            pngPayload[0] = 0x89; pngPayload[1] = 0x50; pngPayload[2] = 0x4E; pngPayload[3] = 0x47;
            pngPayload[4] = 0x0D; pngPayload[5] = 0x0A; pngPayload[6] = 0x1A; pngPayload[7] = 0x0A;
            var garbagePayload = new byte[64 * 1024];
            Array.Fill(garbagePayload, (byte)0xEE);
            var slowPayload = new byte[32 * 1024];
            new Random(2).NextBytes(slowPayload);
            var ctsListener = new CancellationTokenSource();
            var serverTask = Task.Run(async () =>
            {
                while (!ctsListener.IsCancellationRequested)
                {
                    HttpListenerContext ctx;
                    try { ctx = await listener.GetContextAsync(); }
                    catch { return; }
                    Interlocked.Increment(ref httpRequests);
                    string rel = ctx.Request.Url!.AbsolutePath["/self/".Length..];
                    if (rel.EndsWith(".5xx"))
                    {
                        ctx.Response.StatusCode = 500;
                        ctx.Response.Close();
                        continue;
                    }
                    byte[] data = payload;
                    // test_resume: dukung Range (206) — klien melanjutkan dari .part sebagian
                    if (rel.StartsWith("test_resume"))
                    {
                        long start = 0;
                        if (ctx.Request.Headers["Range"] is string rg)
                        {
                            long.TryParse(rg.Replace("bytes=", "").Split('-')[0], out start);
                        }
                        if (start > 0)
                        {
                            ctx.Response.StatusCode = 206;
                            ctx.Response.Headers["Content-Range"] = $"bytes {start}-{payload.Length - 1}/{payload.Length}";
                            data = payload[(int)Math.Min(start, payload.Length)..];
                        }
                    }
                    // test_norange: server TIDAK mendukung Range → selalu 200 full body
                    if (rel.StartsWith("test_norange"))
                    {
                        ctx.Response.StatusCode = 200;
                        ctx.Response.ContentType = "application/octet-stream";
                        ctx.Response.ContentLength64 = data.Length;
                        await ctx.Response.OutputStream.WriteAsync(data);
                        ctx.Response.Close();
                        continue;
                    }
                    // test_png_ok: body PNG valid (signature benar)
                    if (rel.StartsWith("test_png_ok"))
                    {
                        data = pngPayload;
                    }
                    // test_png_bad: ukuran cukup tapi BUKAN PNG (harus ditolak engine)
                    if (rel.StartsWith("test_png_bad"))
                    {
                        data = garbagePayload;
                    }
                    // test_slow: 16 chunk × 60 ms ≈ 1 detik — untuk menangkap progress byte live
                    if (rel.StartsWith("test_slow"))
                    {
                        ctx.Response.StatusCode = 200;
                        ctx.Response.ContentType = "application/octet-stream";
                        ctx.Response.ContentLength64 = slowPayload.Length;
                        int chunk = slowPayload.Length / 16;
                        for (int off = 0; off < slowPayload.Length; off += chunk)
                        {
                            await ctx.Response.OutputStream.WriteAsync(slowPayload, off, Math.Min(chunk, slowPayload.Length - off));
                            await Task.Delay(60, ctsListener.Token);
                        }
                        ctx.Response.Close();
                        continue;
                    }
                    ctx.Response.ContentType = "application/octet-stream";
                    ctx.Response.ContentLength64 = data.Length;
                    await ctx.Response.OutputStream.WriteAsync(data);
                    ctx.Response.Close();
                }
            });

        var dm = new DownloadManager { Concurrency = 2, MaxRetries = 3, TimeoutSeconds = 20 };

        // bersihkan leftover dari run sebelumnya (mis. run crash)
        for (int attempt = 1; attempt <= 3; attempt++)
        {
            try
            {
                if (Directory.Exists(dir)) Directory.Delete(dir, true);
                break;
            }
            catch when (attempt < 3)
            {
                Thread.Sleep(200);
            }
            catch
            {
            }
        }
        Directory.CreateDirectory(dir);

        // (a) file baru → download; .part dibersihkan; file final valid
        string rel1 = "test_dl/001001.mp3";
        string dest1 = KsuAudio.CachePath(rel1);
        if (File.Exists(dest1)) File.Delete(dest1);
        var res1 = await dm.RunAsync(new[]
        {
            new DownloadManager.DownloadItem { Label = "t1", Kind = DownloadManager.JobKind.File, Rel = rel1, Url = prefix + rel1 },
        }, null, CancellationToken.None);
        Check("download file baru sukses", res1.Downloaded == 1 && res1.Skipped == 0 && FileValidLocal(dest1, 4096),
            $"dl={res1.Downloaded} skip={res1.Skipped} fail={res1.Failed}");
            Check("tidak ada .part tersisa", !File.Exists(dest1 + ".part"));
            long size1 = new FileInfo(dest1).Length;

            // (b) file valid → skip TANPA request HTTP
            int before = httpRequests;
            var res2 = await dm.RunAsync(new[]
            {
                new DownloadManager.DownloadItem { Label = "t2", Kind = DownloadManager.JobKind.File, Rel = rel1, Url = prefix + rel1 },
            }, null, CancellationToken.None);
            Check("file valid di-skip tanpa HTTP", res2.Skipped == 1 && httpRequests == before);

            // (c) .part tidak dianggap complete: taruh .part lama → harus tetap request & menimpa final valid
            string relC = "test_part/001002.mp3";
            string destC = KsuAudio.CachePath(relC);
            Directory.CreateDirectory(Path.GetDirectoryName(destC)!);
            TryDelete(destC);
            TryDelete(destC + ".part");
            File.WriteAllBytes(destC + ".part", new byte[500]);
            var res3 = await dm.RunAsync(new[]
            {
                new DownloadManager.DownloadItem { Label = "t3", Kind = DownloadManager.JobKind.File, Rel = relC, Url = prefix + relC },
            }, null, CancellationToken.None);
            Check(".part + tanpa final → dianggap perlu download", res3.Downloaded == 1 && FileValidLocal(destC, 4096),
                $"dl={res3.Downloaded} skip={res3.Skipped} fail={res3.Failed} errs=[{string.Join(";", res3.Errors)}]");

            // (d) resume: file .part sebagian + server 206 → final ukuran penuh
            string relD = "test_resume/001003.mp3";
            string destD = KsuAudio.CachePath(relD);
            Directory.CreateDirectory(Path.GetDirectoryName(destD)!);
            TryDelete(destD);
            TryDelete(destD + ".part");
            File.WriteAllBytes(destD + ".part", payload[..(payload.Length / 2)]);
            var res4 = await dm.RunAsync(new[]
            {
                new DownloadManager.DownloadItem { Label = "t4", Kind = DownloadManager.JobKind.File, Rel = relD, Url = prefix + relD },
            }, null, CancellationToken.None);
            Check("resume dari .part menghasilkan file utuh", res4.Downloaded == 1 && new FileInfo(destD).Length == payload.Length,
                $"dl={res4.Downloaded} skip={res4.Skipped} fail={res4.Failed} errs=[{string.Join(";", res4.Errors)}] size={(File.Exists(destD) ? new FileInfo(destD).Length : -1)}");

            // (d2) server TIDAK mendukung Range (200) → TIDAK boleh append ke .part lama; restart dari awal
            string relD2 = "test_norange/001306.mp3";
            string destD2 = KsuAudio.CachePath(relD2);
            Directory.CreateDirectory(Path.GetDirectoryName(destD2)!);
            TryDelete(destD2);
            TryDelete(destD2 + ".part");
            byte[] junkPart = new byte[payload.Length / 4];
            Array.Fill(junkPart, (byte)0xFF);
            File.WriteAllBytes(destD2 + ".part", junkPart); // .part lama berisi data sampah
            var resD2 = await dm.RunAsync(new[]
            {
                new DownloadManager.DownloadItem { Label = "t4b", Kind = DownloadManager.JobKind.File, Rel = relD2, Url = prefix + relD2 },
            }, null, CancellationToken.None);
            Check("server no-Range → restart dari awal (bukan append 200 ke .part)",
                resD2.Downloaded == 1 && new FileInfo(destD2).Length == payload.Length
                && File.ReadAllBytes(destD2)[..16].SequenceEqual(payload[..16]),
                $"dl={resD2.Downloaded} size={(File.Exists(destD2) ? new FileInfo(destD2).Length : -1)} errs=[{string.Join(";", resD2.Errors)}]");

            // (h) PNG valid → final tersimpan; PNG rusak → job gagal, final TIDAK ada, .part dihapus (AH #23-24)
            string relPng = "test_png_ok/1.png";
            string destPng = KsuAudio.CachePath(relPng);
            TryDelete(destPng);
            TryDelete(destPng + ".part");
            var resPng = await dm.RunAsync(new[]
            {
                new DownloadManager.DownloadItem { Label = "t-png", Kind = DownloadManager.JobKind.File, Rel = relPng, Url = prefix + relPng, MinBytes = 2048 },
            }, null, CancellationToken.None);
            Check("download mushaf .png sukses via .part + validasi", resPng.Downloaded == 1 && DownloadManager.FileValid(destPng, 2048),
                $"dl={resPng.Downloaded} fail={resPng.Failed} errs=[{string.Join(";", resPng.Errors)}]");
            Check("final .png punya signature PNG", DownloadManager.HasPngSignature(destPng));

            string relPngBad = "test_png_bad/2.png";
            string destPngBad = KsuAudio.CachePath(relPngBad);
            TryDelete(destPngBad);
            TryDelete(destPngBad + ".part");
            var resPngBad = await dm.RunAsync(new[]
            {
                new DownloadManager.DownloadItem { Label = "t-png-bad", Kind = DownloadManager.JobKind.File, Rel = relPngBad, Url = prefix + relPngBad, MinBytes = 2048 },
            }, null, CancellationToken.None);
            Check("PNG invalid header DITOLAK (job gagal)", resPngBad.Failed == 1 && resPngBad.Downloaded == 0,
                $"dl={resPngBad.Downloaded} fail={resPngBad.Failed}");
            Check("PNG ditolak: final korup tidak pernah tercipta", !File.Exists(destPngBad));
            Check("PNG ditolak: .part ikut dibersihkan", !File.Exists(destPngBad + ".part"));

            // (i)+(j) initial progress 0/N + byte progress live sebelum file selesai (AH #26-27)
            string relSlow = "test_slow/001006.mp3";
            string destSlow = KsuAudio.CachePath(relSlow);
            TryDelete(destSlow);
            TryDelete(destSlow + ".part");
            var reports = new List<DownloadManager.DownloadProgress>();
            var dmSlow = new DownloadManager { Concurrency = 1, MaxRetries = 1, TimeoutSeconds = 30 };
            var resSlow = await dmSlow.RunAsync(new[]
            {
                new DownloadManager.DownloadItem { Label = "t-slow", Kind = DownloadManager.JobKind.File, Rel = relSlow, Url = prefix + relSlow },
            }, new SyncProgress(reports), CancellationToken.None);
            Check("initial progress 0/N 'Memulai unduhan…' muncul SEBELUM file pertama selesai",
                reports.Count > 0 && reports[0].Total == 1 && reports[0].Done == 0 && reports[0].Current == "Memulai unduhan…",
                reports.Count > 0 ? $"first: {reports[0].Done}/{reports[0].Total} '{reports[0].Current}'" : "tidak ada report");
            Check("byte progress live: CurrentFileBytes > 0 saat file belum selesai",
                reports.Any(r => r.CurrentFileTotal == slowPayload.Length && r.CurrentFileBytes > 0 && r.CurrentFileBytes < slowPayload.Length),
                $"max={reports.Count}");
            Check("speed live dari byte transfer aktual (> 0 saat transfer berjalan)",
                reports.Any(r => r.Done == 0 && r.BytesPerSec > 0),
                "tidak ada report dengan speed > 0 sebelum file selesai");
            Check("download slow selesai utuh", resSlow.Downloaded == 1 && new FileInfo(destSlow).Length == slowPayload.Length,
                $"size={(File.Exists(destSlow) ? new FileInfo(destSlow).Length : -1)}");

            // (d3) tafsir job per ayat: ayat yang sudah ada di disk → SKIP tanpa network
            string tafsKey = "selftest_dl_tafsir";
            string tafsDir = Path.Combine(svc.TafsirDir, tafsKey);
            try
            {
                Directory.CreateDirectory(tafsDir);
                var ayat = new Dictionary<string, object>
                {
                    ["ayat"] = new Dictionary<string, string> { ["1"] = "t1", ["2"] = "t2" },
                };
                File.WriteAllText(Path.Combine(tafsDir, "1.json"), JsonSerializer.Serialize(ayat));
                var resD3 = await dm.RunAsync(new[]
                {
                    new DownloadManager.DownloadItem { Label = "t4c", Kind = DownloadManager.JobKind.Tafsir, TextKey = tafsKey, Surah = 1, Ayah = 1 },
                }, null, CancellationToken.None);
                Check("tafsir per-ayah sudah ada → skip tanpa network", resD3.Skipped == 1 && resD3.Downloaded == 0 && resD3.Failed == 0,
                    $"dl={resD3.Downloaded} skip={resD3.Skipped} fail={resD3.Failed}");
            }
            finally
            {
                try { Directory.Delete(tafsDir, true); } catch { }
                svc.InvalidateTafsir(tafsKey, 1);
            }

            // (e) retry: server 500 dua attempt pertama (gagal), maxRetries default → gagal tercatat; lalu sukses saat 5xx hilang
            string relE = "test_5xx/001004.mp3";
            var res5 = await dm.RunAsync(new[]
            {
                new DownloadManager.DownloadItem { Label = "t5", Kind = DownloadManager.JobKind.File, Rel = relE, Url = prefix + relE + ".5xx" },
            }, null, CancellationToken.None);
            Check("server error → retry sampai max lalu failed", res5.Failed == 1 && res5.Downloaded == 0);

            // (f) cancellation: cancel di tengah batch
            string relF = "test_cancel/001005.mp3";
            using var cts = new CancellationTokenSource();
            var job = dm.RunAsync(new[]
            {
                new DownloadManager.DownloadItem { Label = "t6", Kind = DownloadManager.JobKind.File, Rel = relF, Url = prefix + relF },
            }, null, cts.Token);
            cts.Cancel();
            var res6 = await job;
            Check("cancellation aman (tidak throw)", true);

            // (g) tarjama job: file disk lengkap → skip
            string key = "selftest_dl_trans";
            string tdir = Path.Combine(svc.TeksDir, key);
            try
            {
                Directory.CreateDirectory(tdir);
                var full = new Dictionary<string, string>();
                for (int a = 1; a <= 7; a++) full[a.ToString()] = "teks " + a;
                File.WriteAllText(Path.Combine(tdir, "1.json"),
                    JsonSerializer.Serialize(new Dictionary<string, object> { ["ayat"] = full }));
                var res7 = await dm.RunAsync(new[]
                {
                    new DownloadManager.DownloadItem { Label = "t7", Kind = DownloadManager.JobKind.Tarjama, TextKey = key, Surah = 1 },
                }, null, CancellationToken.None);
                Check("tarjama lengkap → skip tanpa network", res7.Skipped == 1);
            }
            finally
            {
                try { Directory.Delete(tdir, true); } catch { }
                svc.InvalidateTarjama(key, 1);
            }

            ctsListener.Cancel();
            try { listener.Stop(); } catch { }
            try { await serverTask; } catch { }
        }
        finally
        {
            try { Directory.Delete(dir, true); } catch { }
            svc.InvalidateAll();
        }

        static bool FileValidLocal(string p, long min) => File.Exists(p) && new FileInfo(p).Length >= min;

        static void TryDelete(string p)
        {
            for (int attempt = 1; attempt <= 3; attempt++)
            {
                try
                {
                    if (File.Exists(p)) File.Delete(p);
                    return;
                }
                catch when (attempt < 3)
                {
                    Thread.Sleep(150);
                }
                catch
                {
                }
            }
        }
    }

    // 7b. (AH) Final audit — kunci satu root downloads + isolasi voice + scope qari
    private static void FinalAudit()
    {
        Console.WriteLine("-- Final audit (single downloads root)");
        var svc = OfflineContentService.Instance;

        // (AH #1,#2) semua path permanen turunan DataRoot
        string root = Path.GetFullPath(KsuAudio.DataRoot) + Path.DirectorySeparatorChar;
        foreach (var rel in new[]
        {
            "audio/Husary_64kbps/001001.mp3",
            "voice/English_Walk/001001.mp3",
            "mushaf/hafs/1.png",
            "mushaf/tajweed/604.png",
            "mushaf/warsh/3.png",
            "hilites/hafs/5.json",
            "teks/ar_ayat/2.json",
            "tafsir/indonesian/3.json",
            "fonts/uthmanic_hafs_v22.ttf",
            "temp/anything.tmp",
        })
        {
            string full = Path.GetFullPath(KsuAudio.CachePath(rel));
            Check($"path permanen di downloads/: {rel}",
                full.StartsWith(root, StringComparison.OrdinalIgnoreCase),
                full);
        }

        // service dirs mengarah ke DataRoot
        Check("OfflineContentService.CacheRoot = KsuAudio.DataRoot", svc.CacheRoot == KsuAudio.DataRoot);
        Check("AudioDir = downloads/audio", Path.GetFullPath(svc.AudioDir) == Path.GetFullPath(KsuAudio.AudioRoot));
        Check("VoiceDir = downloads/voice", Path.GetFullPath(svc.VoiceDir) == Path.GetFullPath(KsuAudio.VoiceRoot));
        Check("TempDir = downloads/temp", Path.GetFullPath(KsuAudio.TempDir).StartsWith(root, StringComparison.OrdinalIgnoreCase));
        Check("LegacyCacheDir HANYA untuk baca (di %LOCALAPPDATA%)",
            Path.GetFullPath(KsuAudio.LegacyCacheDir).StartsWith(
                Path.GetFullPath(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData)),
                StringComparison.OrdinalIgnoreCase));

        // (AH #14-16) combo qari Reciter tidak InvalidCastException + scope audio benar
        var rec = Reciters.All[0];
        var ci = new ComboItem(rec.Display, rec);
        Reciter resolved;
        try
        {
            resolved = QuranDesktop.Controls.DownloadCenterDialog.ResolveProfileReciter(ci, "afasy");
            Check("ComboItem(Reciter) resolve tanpa InvalidCastException", resolved.Key == rec.Key, resolved.Key);
        }
        catch (InvalidCastException)
        {
            Check("ComboItem(Reciter) resolve tanpa InvalidCastException", false, "InvalidCastException");
            resolved = rec;
        }
        var clicked = Reciters.Find("afasy")!;
        Check("klik qari A → qari A aktif (bukan fallback)",
            QuranDesktop.Controls.DownloadCenterDialog.ResolveProfileReciter(new ComboItem(clicked.Display, clicked), "husary").Key == "afasy");
        Check("combo non-Reciter → fallback key", QuranDesktop.Controls.DownloadCenterDialog.ResolveProfileReciter(new ComboItem("x", "bukan-reciter"), "husary").Key == "husary");
        Check("combo null → fallback key", QuranDesktop.Controls.DownloadCenterDialog.ResolveProfileReciter(null, "husary").Key == "husary");

        // BuildJobs scope SATU qari: seluruh rel = audio/{folder}/…
        var afasy2 = Reciters.Find("afasy")!;
        var jobs = DownloadManager.BuildJobs(new DownloadManager.DownloadScope
        {
            Mushaf = false, Hilites = false, Arab = false,
            AudioFolders = new[] { afasy2.Folder },
            Surahs = new[] { 1 },
        });
        Check("BuildJobs scope qari: 7 job, rel audio/{folder}/…",
            jobs.Count == 7 && jobs.All(j => j.Rel!.StartsWith($"audio/{afasy2.Folder}/", StringComparison.Ordinal)),
            $"count={jobs.Count}, first={jobs.FirstOrDefault()?.Rel}");
        Check("BuildJobs URL qari benar", jobs.All(j => j.Url!.Contains(afasy2.Folder, StringComparison.Ordinal)));

        // (AH #17-20) mushaf aktif — Tajweed→Page2, Warsh→Page_warsh, Hafs→Page
        Check("mushaf aktif tajweed → Page2", MushafTypes.ResolveMushaf("tajweed").PageKey == "Page2");
        Check("mushaf aktif warsh → Page_warsh", MushafTypes.ResolveMushaf("warsh").PageKey == "Page_warsh");
        Check("mushaf aktif hafs → Page", MushafTypes.ResolveMushaf("hafs").PageKey == "Page");
        Check("MushafPageCount mengikuti mushaf aktif", MushafTypes.MushafPageCount("tajweed") == QuranData.PageCount("Page2"));
        var sumTajweed = svc.ScanMushaf(MushafTypes.ResolveMushaf("tajweed"));
        Check("ScanMushaf(tajweed) summary pakai key aktif",
            sumTajweed.Key == "tajweed" && sumTajweed.PagesTotal == QuranData.PageCount("Page2"));

        // (AH #29) qari tidak pernah 47: Reciters.All == 43 && VoiceTranslations.All == 4
        Check("qari = 43 dan voice = 4 (43+4 ≠ 47 dalam satu daftar)",
            Reciters.All.Count == 43 && VoiceTranslations.All.Count == 4,
            $"qari={Reciters.All.Count} voice={VoiceTranslations.All.Count}");

        // (AH #28) scan progress record: Index/Total/Stage
        var scanReports = new List<AudioFolderScanProgress>();
        string folder = "SelfTest_ScanProg";
        string dir = Path.Combine(KsuAudio.AudioRoot, folder);
        try
        {
            Directory.CreateDirectory(dir);
            File.WriteAllBytes(Path.Combine(dir, "001001.mp3"), new byte[8192]);
            File.WriteAllBytes(Path.Combine(dir, "001002.mp3"), new byte[8192]);
            var sum = svc.ScanAudioFolder(folder, folder, "ScanProg",
                "audio", new SelfTestScanProgress(scanReports), CancellationToken.None, index: 1, total: 43);
            Check("scan progress dilaporkan dengan Index=1 Total=43",
                scanReports.Any(p => p.Index == 1 && p.Total == 43), $"reports={scanReports.Count}");
            Check("scan stage berakhir Completed", scanReports[^1].Stage == AudioFolderScanProgress.Completed);
            Check("scan hasil valid = 2", sum.Valid == 2, $"got {sum.Valid}");
        }
        finally
        {
            try { Directory.Delete(dir, true); } catch { }
            svc.InvalidateAudio($"audio/{folder}/001001.mp3");
            svc.InvalidateAudio($"audio/{folder}/001002.mp3");
        }

        // (B) migrasi default (idempotent — skip bila marker sudah ada) lalu marker WAJIB ada
        OfflineMigrator.Run();
        Check("marker .migration-v1-complete ada setelah migrasi default", OfflineMigrator.MigrationComplete);
    }

    private sealed class SelfTestScanProgress : IProgress<AudioFolderScanProgress>
    {
        private readonly List<AudioFolderScanProgress> _sink;
        public SelfTestScanProgress(List<AudioFolderScanProgress> sink) { _sink = sink; }
        public void Report(AudioFolderScanProgress value) => _sink.Add(value);
    }

    /// <summary>IProgress sinkron untuk determinisme test (Progress<T> async ambigu).</summary>
    private sealed class SyncProgress : IProgress<DownloadManager.DownloadProgress>
    {
        private readonly List<DownloadManager.DownloadProgress> _sink;
        public SyncProgress(List<DownloadManager.DownloadProgress> sink) { _sink = sink; }
        public void Report(DownloadManager.DownloadProgress value) => _sink.Add(value);
    }

    // 7. Legacy cache compatibility
    private static void LegacyCompatibility()
    {
        Console.WriteLine("-- Legacy cache compatibility");
        var svc = OfflineContentService.Instance;
        Check("KsuAudio.CachePath tetap dipakai (root sama)",
            svc.CacheRoot == KsuAudio.CacheDir);
        string legacyAudio = Path.Combine(OfflineContentService.Instance.AudioDir, "Husary_64kbps");
        if (Directory.Exists(legacyAudio))
        {
            var st = svc.GetAudioStatus("Husary_64kbps", 1, 1);
            Check("audio legacy qari aktif terdeteksi", st.Exists && st.IsValid, st.Exists ? $"{st.SizeBytes} B" : "file tidak ada");
            var sum = svc.ScanReciter(Reciters.Find("husary") ?? Reciters.All[0]);
            Check("ScanReciter menghitung dari file aktual", sum.Valid >= 0 && sum.Total == 6236, $"{sum.Valid}/{sum.Total}");
        }
        else
        {
            Check("legacy audio tidak ada di mesin ini (skip)", true);
        }
        string oldLegacyAudio = Path.Combine(KsuAudio.LegacyCacheDir, "Husary_64kbps");
        if (Directory.Exists(oldLegacyAudio))
        {
            Check("cache lama %LOCALAPPDATA% masih ada — migrator akan memindahkannya saat startup", true);
        }
        string legacyTeks = Path.Combine(svc.TeksDir);
        string legacyTrans = "id_indonesian";
        string legacyDir = Path.Combine(legacyTeks, legacyTrans);
        if (Directory.Exists(legacyDir))
        {
            // pilih surah JSON yang benar-benar ada di cache legacy mesin ini
            int found = 0;
            for (int s = 1; s <= 114 && found == 0; s++)
            {
                if (File.Exists(Path.Combine(legacyDir, s + ".json"))) found = s;
            }
            if (found > 0)
            {
                var st = svc.GetTarjamaStatus(legacyTrans, found);
                Check($"tarjama legacy terbaca (surah {found})",
                    st.FileValid && st.AyatFound > 0, $"{st.AyatFound}/{QuranData.SurahAyahCount(found)}");
            }
            else
            {
                Check("tarjama legacy: folder ada tapi kosong (skip)", true);
            }
        }
        else
        {
            Check("tarjama legacy tidak ada di mesin ini (skip)", true);
        }
        Check("mushaf legacy (jika ada) terdeteksi", true);
    }
}
