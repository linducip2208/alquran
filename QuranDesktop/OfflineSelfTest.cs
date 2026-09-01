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
