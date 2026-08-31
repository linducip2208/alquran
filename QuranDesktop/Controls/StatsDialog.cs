namespace QuranDesktop.Controls;

internal sealed class StatsDialog : Form
{
    public StatsDialog()
    {
        Text = "Statistik Baca — 30 Hari";
        ClientSize = new Size(620, 360);
        StartPosition = FormStartPosition.CenterParent;
        MinimumSize = new Size(540, 320);

        var panel = new StatsPanel { Dock = DockStyle.Fill };
        Controls.Add(panel);
        Load += (_, _) => panel.Invalidate();
    }

    private sealed class StatsPanel : Panel
    {
        public StatsPanel()
        {
            DoubleBuffered = true;
            BackColor = Color.White;
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            var g = e.Graphics;
            g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;

            var counts = ProgressStore.ReadCountsSnapshot();
            var days = Enumerable.Range(0, 30)
                .Select(i => DateTime.Today.AddDays(-29 + i))
                .ToList();
            int max = Math.Max(4, days.Max(d => counts.TryGetValue(d.ToString("yyyy-MM-dd"), out int c) ? c : 0));

            int left = 46, bottom = Height - 56;
            int chartW = Width - left - 24, chartH = bottom - 40;

            using var axis = new Pen(Color.Gainsboro);
            g.DrawLine(axis, left, 40, left, bottom);
            g.DrawLine(axis, left, bottom, left + chartW, bottom);

            for (int i = 1; i <= 4; i++)
            {
                int y = bottom - chartH * i / 4;
                g.DrawLine(Pens.WhiteSmoke, left, y, left + chartW, y);
                string v = (max * i / 4).ToString();
                g.DrawString(v, Font, Brushes.Gray, left - 38, y - 7);
            }

            float slot = (float)chartW / days.Count;
            float barW = Math.Max(3f, slot * 0.62f);
            for (int i = 0; i < days.Count; i++)
            {
                int c = counts.TryGetValue(days[i].ToString("yyyy-MM-dd"), out int cc) ? cc : 0;
                float h = c * (float)chartH / max;
                float x = left + i * slot + (slot - barW) / 2;

                using var brush = new SolidBrush(c > 0 ? Color.FromArgb(46, 160, 67) : Color.FromArgb(215, 215, 215));
                g.FillRectangle(brush, x, bottom - h, barW, h);
                if (c > 0)
                {
                    g.DrawString(c.ToString(), new Font("Segoe UI", 7.5f), Brushes.DarkGreen,
                        new RectangleF(x - 8, bottom - h - 16, barW + 16, 14),
                        new StringFormat { Alignment = StringAlignment.Center });
                }

                if (days[i].Day % 5 == 0 || i == days.Count - 1)
                {
                    g.DrawString(days[i].ToString("d/M"), new Font("Segoe UI", 7.5f), Brushes.Gray,
                        new RectangleF(x - 12, bottom + 6, slot + 24, 16),
                        new StringFormat { Alignment = StringAlignment.Center });
                }
            }

            int total = days.Sum(d => counts.TryGetValue(d.ToString("yyyy-MM-dd"), out int c) ? c : 0);
            g.DrawString($"Halaman dibaca per hari — 30 hari terakhir (total {total} halaman)",
                new Font("Segoe UI", 10f, FontStyle.Bold), Brushes.Black, left, 14);

            int streak = ProgressStore.StreakDays();
            g.DrawString($"Streak: {streak} hari  •  Khatam selesai: {ProgressStore.KhatamCount}×",
                new Font("Segoe UI", 9f), Brushes.DimGray, left, bottom + 24);
        }
    }
}
