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

        if (args.Contains("--dlctest"))
        {
            ApplicationConfiguration.Initialize();
            using var dlg = new Controls.DownloadCenterDialog("hafs", "id_indonesian", "indonesian", "husary");
            dlg.Show();
            int waited = 0;
            while (!dlg.IsHandleCreated || waited < 15000)
            {
                Application.DoEvents();
                Thread.Sleep(100);
                waited += 100;
            }
            Thread.Sleep(6000);
            Application.DoEvents();
            dlg.Close();
            Console.WriteLine("DLCTEST-OK");
            return;
        }

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
            File.AppendAllText(Path.Combine(dir, "error.log"),
                DateTime.Now + Environment.NewLine + ex + Environment.NewLine + "----" + Environment.NewLine);
        }
        catch
        {
        }
    }
}
