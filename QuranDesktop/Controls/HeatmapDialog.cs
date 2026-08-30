namespace QuranDesktop.Controls;

internal sealed class HeatmapDialog : Form
{
    private readonly FlowLayoutPanel _grid = new()
    {
        Dock = DockStyle.Fill,
        FlowDirection = FlowDirection.LeftToRight,
        WrapContents = true,
        AutoScroll = true,
        Padding = new Padding(8),
        BackColor = Color.FromArgb(244, 244, 240),
    };
    private readonly Label _legend = new()
    {
        Dock = DockStyle.Bottom,
        Height = 34,
        TextAlign = ContentAlignment.MiddleLeft,
        Padding = new Padding(10, 8, 0, 0),
        Text = "Hijau = hafal   Kuning = perlu ulang   Merah/abu = belum   (klik halaman untuk ganti status)",
    };
    private readonly int _from;
    private readonly int _to;
    private readonly List<Button> _buttons = new();

    public HeatmapDialog(int from, int to)
    {
        _from = from;
        _to = to;
        Text = $"Hafalan — halaman {from}–{to}";
        ClientSize = new Size(620, 420);
        StartPosition = FormStartPosition.CenterParent;
        Controls.Add(_grid);
        Controls.Add(_legend);
        _legend.BringToFront();

        Build();
    }

    private void Build()
    {
        _grid.SuspendLayout();
        _grid.Controls.Clear();
        _buttons.Clear();
        var tip = new ToolTip();
        for (int p = _from; p <= _to; p++)
        {
            var b = new Button
            {
                Text = p.ToString(),
                Size = new Size(36, 24),
                Tag = p,
                FlatStyle = FlatStyle.Flat,
            };
            Colorize(b);
            tip.SetToolTip(b, $"Hal {p}: {StatusName(ProgressStore.GetHafal(p))}");
            b.Click += (_, _) =>
            {
                if (b.Tag is int page)
                {
                    ProgressStore.SetHafal(page, (ProgressStore.GetHafal(page) + 1) % 3);
                    Colorize(b);
                    tip.SetToolTip(b, $"Hal {page}: {StatusName(ProgressStore.GetHafal(page))}");
                }
            };
            _buttons.Add(b);
            _grid.Controls.Add(b);
        }
        _grid.ResumeLayout();
    }

    private static string StatusName(int s) => s == 2 ? "Hafal" : s == 1 ? "Perlu ulang" : "Belum";

    private static void Colorize(Button b)
    {
        int s = ProgressStore.GetHafal((int)b.Tag!);
        b.BackColor = s == 2 ? Color.FromArgb(46, 160, 67) : s == 1 ? Color.FromArgb(240, 180, 40) : Color.FromArgb(210, 210, 210);
        b.ForeColor = s == 1 ? Color.Black : Color.White;
    }
}
