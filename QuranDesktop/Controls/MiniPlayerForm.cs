namespace QuranDesktop.Controls;

internal sealed class MiniPlayerForm : Form
{
    private readonly Label _lblInfo = new()
    {
        Dock = DockStyle.Fill,
        TextAlign = ContentAlignment.MiddleCenter,
        Font = new Font("Segoe UI", 9.5f, FontStyle.Bold),
        Text = "—",
    };
    private readonly FlowLayoutPanel _bar = new()
    {
        Dock = DockStyle.Bottom,
        Height = 42,
        FlowDirection = FlowDirection.LeftToRight,
        WrapContents = false,
        Padding = new Padding(4, 2, 4, 2),
    };
    private readonly Button _btnPrev = new() { Text = "◀", Width = 36 };
    private readonly Button _btnPlay = new() { Text = "▶", Width = 44 };
    private readonly Button _btnNext = new() { Text = "▶", Width = 36 };
    private readonly Button _btnRestore = new() { Text = "⬆", Width = 40 };

    public event Action? PlayPause;
    public event Action? Next;
    public event Action? Prev;
    public event Action? Restore;

    public MiniPlayerForm()
    {
        Text = "Quran Mini";
        ClientSize = new Size(300, 108);
        FormBorderStyle = FormBorderStyle.FixedToolWindow;
        StartPosition = FormStartPosition.Manual;
        ShowInTaskbar = false;
        TopMost = true;
        MinimizeBox = false;
        MaximizeBox = false;

        var screen = Screen.PrimaryScreen!.WorkingArea;
        Location = new Point(screen.Right - Width - 16, screen.Bottom - Height - 16);

        _btnNext.Text = "▶|";
        _bar.Controls.Add(_btnPrev);
        _bar.Controls.Add(_btnPlay);
        _bar.Controls.Add(_btnNext);
        _bar.Controls.Add(_btnRestore);

        Controls.Add(_bar);
        Controls.Add(_lblInfo);
        _lblInfo.BringToFront();

        _btnPlay.Click += (_, _) => PlayPause?.Invoke();
        _btnNext.Click += (_, _) => Next?.Invoke();
        _btnPrev.Click += (_, _) => Prev?.Invoke();
        _btnRestore.Click += (_, _) => Restore?.Invoke();
    }

    public void SetInfo(string text, bool playing)
    {
        _lblInfo.Text = text;
        _btnPlay.Text = playing ? "⏸" : "▶";
    }
}
