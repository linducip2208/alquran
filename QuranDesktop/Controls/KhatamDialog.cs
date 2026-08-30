namespace QuranDesktop.Controls;

internal sealed class KhatamDialog : Form
{
    private readonly ProgressBar _bar = new() { Width = 460, Height = 24 };
    private readonly Label _lblOverall = new() { AutoSize = true, Font = new Font("Segoe UI", 9.5f, FontStyle.Bold), MaximumSize = new Size(470, 0) };
    private readonly Label _lblStreak = new() { AutoSize = true, Font = new Font("Segoe UI", 10f) };
    private readonly FlowLayoutPanel _grid = new()
    {
        Size = new Size(470, 130),
        FlowDirection = FlowDirection.LeftToRight,
        WrapContents = true,
    };
    private readonly ToolTip _tip = new();

    public KhatamDialog()
    {
        Text = "Target Khatam";
        ClientSize = new Size(494, 268);
        FormBorderStyle = FormBorderStyle.FixedDialog;
        StartPosition = FormStartPosition.CenterParent;
        MaximizeBox = false;

        _bar.Location = new Point(12, 12);
        _lblOverall.Location = new Point(12, 44);
        _lblStreak.Location = new Point(12, 74);
        _grid.Location = new Point(12, 104);

        Controls.Add(_bar);
        Controls.Add(_lblOverall);
        Controls.Add(_lblStreak);
        Controls.Add(_grid);

        Load += (_, _) => RefreshAll();
    }

    private void RefreshAll()
    {
        _grid.SuspendLayout();
        _grid.Controls.Clear();
        for (int j = 1; j <= 30; j++)
        {
            var start = QuranData.JuzStart(j);
            int endPage = j < 30
                ? QuranData.FindPage("Page", QuranData.JuzStart(j + 1).Surah, QuranData.JuzStart(j + 1).Ayah) - 1
                : QuranData.PageCount("Page");
            int startPage = QuranData.FindPage("Page", start.Surah, start.Ayah);
            int total = endPage - startPage + 1;
            int read = 0;
            for (int p = startPage; p <= endPage; p++)
            {
                if (ProgressStore.IsPageRead(p)) read++;
            }

            var b = new Button
            {
                Text = $"Juz {j}\n{read * 100 / Math.Max(1, total)}%",
                Size = new Size(56, 44),
                Tag = (startPage, endPage),
            };
            Colorize(b, read, total);
            _tip.SetToolTip(b, $"Juz {j} — hal {startPage}–{endPage} ({read}/{total} terbaca). Klik untuk detail.");
            b.Click += (_, _) =>
            {
                if (b.Tag is (int f, int t))
                {
                    using var hb = new HeatmapDialog(f, t);
                    hb.ShowDialog(this);
                    RefreshAll();
                }
            };
            _grid.Controls.Add(b);
        }
        _grid.ResumeLayout();

        int readAll = ProgressStore.ReadPageCount;
        int totalAll = QuranData.PageCount("Page");
        _bar.Value = Math.Min(100, readAll * 100 / Math.Max(1, totalAll));
        _lblOverall.Text = $"Terbaca {readAll}/{totalAll} halaman ({readAll * 100 / Math.Max(1, totalAll)}%)";
        int streak = ProgressStore.StreakDays();
        _lblStreak.Text = streak > 0
            ? $"Streak: {streak} hari berturut-turut ada progres"
            : "Belum ada streak — buka halaman di Mode Mushaf hari ini";
    }

    private static void Colorize(Button b, int read, int total)
    {
        double pct = (double)read / Math.Max(1, total);
        if (pct >= 0.999) b.BackColor = Color.FromArgb(46, 160, 67);
        else if (pct >= 0.5) b.BackColor = Color.FromArgb(240, 180, 40);
        else if (pct > 0) b.BackColor = Color.FromArgb(230, 120, 40);
        else b.BackColor = Color.FromArgb(200, 90, 90);
        b.ForeColor = Color.White;
        b.FlatStyle = FlatStyle.Flat;
    }
}
