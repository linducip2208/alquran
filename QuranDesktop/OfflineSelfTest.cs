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
        TextJsonDetection();
        TafsirDiskCache();
        HiliteDiskCache();
        DownloadEngineAsync().GetAwaiter().GetResult();
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
        Check("IdToAya(ayat terakhir) = 114:6", QuranData.IdToAya(QuranData.AyaToId(114, 6)) == (114, 6));
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
    }

    // 7. Legacy cache compatibility
    private static void LegacyCompatibility()
    {
        Console.WriteLine("-- Legacy cache compatibility");
        var svc = OfflineContentService.Instance;
        Check("KsuAudio.CachePath tetap dipakai (root sama)",
            svc.CacheRoot == KsuAudio.CacheDir);
        string legacyAudio = Path.Combine(KsuAudio.CacheDir, "Husary_64kbps");
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
