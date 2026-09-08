namespace QuranDesktop;

static class Program
{
    [STAThread]
    static void Main(string[] args)
    {
        if (args.Contains("--selftest"))
        {
            Environment.ExitCode = OfflineSelfTest.RunAll();
            return;
        }

        // (C) uji izin tulis root downloads (samping EXE) — TANPA fallback ke AppData/TEMP
        if (!KsuAudio.EnsureWritableRoot(out string permError))
        {
            ApplicationConfiguration.Initialize();
            MessageBox.Show(
                "Folder aplikasi tidak dapat ditulis.\n\n" +
                "Quran Desktop menyimpan seluruh konten offline di folder\n" +
                "'downloads' yang berada di samping QuranDesktop.exe.\n" +
                "Pindahkan aplikasi ke folder yang memiliki izin tulis.\n\n" +
                $"Lokasi: {KsuAudio.DataRoot}\nPenyebab: {permError}",
                "Quran Desktop — Folder tidak dapat ditulis",
                MessageBoxButtons.OK, MessageBoxIcon.Error);
            Environment.ExitCode = 2;
            return;
        }

        if (args.Contains("--dlctest"))
        {
            // --dlctest [mushafKey] — smoke test dialog dengan mushaf apa pun (mis. tajweed).
            // Menunggu scan awal selesai, lalu menjalankan path "Scan Ulang" (RescanAsync)
            // dan memverifikasi tidak ada "Scan gagal".
            var rest = args.SkipWhile(a => a != "--dlctest").Skip(1).ToList();
            string mushaf = rest.Count > 0 ? rest[0] : "hafs";
            ApplicationConfiguration.Initialize();
            using var dlg = new Controls.DownloadCenterDialog(mushaf, "id_indonesian", "indonesian", "husary");
            dlg.Show();
            var sw = System.Diagnostics.Stopwatch.StartNew();

            // tunggu scan awal selesai (label berhenti berubah maksimal 120 s)
            string last = "";
            int stable = 0;
            while (sw.Elapsed.TotalSeconds < 120 && stable < 10)
            {
                Application.DoEvents();
                Thread.Sleep(200);
                if (dlg.ProgressText != last) { last = dlg.ProgressText; stable = 0; }
                else stable++;
            }
            Console.WriteLine($"DLCTEST initial ({mushaf}): \"{dlg.ProgressText}\" dalam {sw.Elapsed.TotalSeconds:0.0}s");

            // jalankan path tombol [Scan Ulang]
            sw.Restart();
            var rescan = dlg.RescanAsync();
            while (!rescan.IsCompleted && sw.Elapsed.TotalSeconds < 120)
            {
                Application.DoEvents();
                Thread.Sleep(100);
            }
            // tunggu label stabil (setelah task selesai, label di-update di UI thread)
            string last2 = "";
            int stable2 = 0;
            while (stable2 < 8 && sw.Elapsed.TotalSeconds < 150)
            {
                Application.DoEvents();
                Thread.Sleep(150);
                if (dlg.ProgressText != last2) { last2 = dlg.ProgressText; stable2 = 0; }
                else stable2++;
            }
            Console.WriteLine($"DLCTEST rescan  ({mushaf}): \"{dlg.ProgressText}\" dalam {sw.Elapsed.TotalSeconds:0.0}s");

            bool ok = !dlg.ProgressText.StartsWith("Scan gagal", StringComparison.Ordinal);
            dlg.Close();
            Console.WriteLine(ok ? $"DLCTEST-OK ({mushaf})" : $"DLCTEST-FAIL ({mushaf})");
            Environment.ExitCode = ok ? 0 : 1;
            return;
        }

        // (regression fix) konten download lama di %LOCALAPPDATA% dipindah ke samping EXE:
        // dialog progress tampil hanya bila cache legacy benar-benar ada & migrasi belum selesai
        if (OfflineMigrator.HasLegacyData)
        {
            ApplicationConfiguration.Initialize();
            using var dlg = new Controls.MigrationDialog();
            dlg.ShowDialog();
        }
        OfflineMigrator.EnsureStarted();

        Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
        Application.ThreadException += (s, e) => Log(e.Exception);
        AppDomain.CurrentDomain.UnhandledException += (s, e) => Log(e.ExceptionObject as Exception);

        ApplicationConfiguration.Initialize();
        Application.Run(new MainForm());
    }

    internal static void Log(Exception? ex)
    {
        try
        {
            var dir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "QuranDesktop");
            Directory.CreateDirectory(dir);
            var logPath = Path.Combine(dir, "error.log");
            if (File.Exists(logPath) && new FileInfo(logPath).Length > 5 * 1024 * 1024)
            {
                var oldPath = Path.Combine(dir, "error.previous.log");
                try { File.Delete(oldPath); } catch { }
                try { File.Move(logPath, oldPath); } catch { }
            }
            File.AppendAllText(logPath,
                DateTime.Now.ToString("O") + " [" + Environment.ProcessId + "]"
                + Environment.NewLine + ex + Environment.NewLine + "----" + Environment.NewLine,
                System.Text.Encoding.UTF8);
        }
        catch
        {
        }
    }
}
