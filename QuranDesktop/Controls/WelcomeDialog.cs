namespace QuranDesktop.Controls;

internal sealed class WelcomeDialog : Form
{
    public WelcomeDialog()
    {
        Text = "Selamat Datang di Quran Desktop";
        ClientSize = new Size(560, 420);
        StartPosition = FormStartPosition.CenterParent;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;

        var lblTitle = new Label
        {
            Dock = DockStyle.Top,
            Height = 60,
            Text = "🌙  Quran Desktop\nReplika quran.ksu.edu.sa untuk Windows 10/11",
            TextAlign = ContentAlignment.MiddleCenter,
            Font = new Font("Segoe UI", 13f, FontStyle.Bold),
        };

        var tips = new Label
        {
            Dock = DockStyle.Fill,
            Text = "Cara cepat mulai:\n\n" +
                   "1.  Pilih Mode: Mushaf (buka-bukaan 2 halaman), Teks & Terjemahan, atau Hifz\n" +
                   "2.  Klik ayat di halaman/teks untuk membaca arti & tafsirnya\n" +
                   "3.  Tekan ▶ Play untuk talaqah — ayat berlanjut otomatis\n" +
                   "4.  ✨ Inspirasi → Ayat Hari Ini, motivasi per situasi, doa Rabbana\n" +
                   "5.  Fitur Lainnya → Khatam, Peta Hafalan, Kuis, Playlist, Mini Player, dll.\n\n" +
                   "Shortcut: ← → ayat  •  Space play/pause  •  Ctrl+F cari  •  Ctrl+scroll zoom mushaf\n\n" +
                   "Selamat membaca — semoga menjadi amal yang berkilau!",
            TextAlign = ContentAlignment.MiddleLeft,
            Padding = new Padding(24, 8, 16, 0),
            Font = new Font("Segoe UI", 10.5f),
        };

        var bottom = new FlowLayoutPanel
        {
            Dock = DockStyle.Bottom,
            Height = 54,
            FlowDirection = FlowDirection.RightToLeft,
            Padding = new Padding(0, 8, 12, 0),
        };
        var btnStart = new Button { Text = "Mulai Membaca", Width = 140 };
        var btnWa = new Button { Text = "Kontak (WA)", Width = 110 };
        bottom.Controls.Add(btnStart);
        bottom.Controls.Add(btnWa);

        Controls.Add(tips);
        Controls.Add(lblTitle);
        Controls.Add(bottom);
        tips.BringToFront();

        AcceptButton = btnStart;
        btnStart.Click += (_, _) => Close();
        btnWa.Click += (_, _) =>
        {
            try
            {
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                {
                    FileName = "https://wa.me/6281296052010",
                    UseShellExecute = true,
                });
            }
            catch
            {
            }
        };
    }
}
