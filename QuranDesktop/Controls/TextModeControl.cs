namespace QuranDesktop.Controls;

internal sealed class TextModeControl : Panel
{
    private readonly FlowLayoutPanel _flp = new()
    {
        Dock = DockStyle.Fill,
        FlowDirection = FlowDirection.TopDown,
        WrapContents = false,
        AutoScroll = true,
        Padding = new Padding(8),
        BackColor = Color.FromArgb(244, 244, 240),
    };

    private readonly List<AyahView> _views = new();
    private int _selectedIndex = -1;

    public event Action<int>? AyahClicked;

    public TextModeControl()
    {
        DoubleBuffered = true;
        Controls.Add(_flp);
        _flp.Resize += (_, _) => UpdateWidths();
    }

    public int SelectedIndex => _selectedIndex;

    public void Render(List<AyahData> ayahs, Font arabicFont, Font transFont, bool transRtl)
    {
        foreach (var v in _views)
        {
            _flp.Controls.Remove(v);
            v.Dispose();
        }
        _views.Clear();
        _selectedIndex = -1;

        int w = AyahWidth();
        foreach (var a in ayahs)
        {
            var view = new AyahView(a, arabicFont, transFont, transRtl) { Width = w };
            view.AyahClicked += (s, e) =>
            {
                if (s is AyahView av) AyahClicked?.Invoke(av.NumberInSurah);
            };
            _views.Add(view);
            _flp.Controls.Add(view);
        }
    }

    public void SetSelected(int ayahNumber)
    {
        int idx = ayahNumber - 1;
        if (idx < 0 || idx >= _views.Count) return;

        if (_selectedIndex >= 0 && _selectedIndex < _views.Count)
        {
            _views[_selectedIndex].Selected = false;
        }
        _selectedIndex = idx;
        _views[idx].Selected = true;

        BeginInvoke(new Action(() =>
        {
            try
            {
                if (_selectedIndex >= 0 && _selectedIndex < _views.Count)
                {
                    _flp.ScrollControlIntoView(_views[_selectedIndex]);
                }
            }
            catch
            {
            }
        }));
    }

    private int AyahWidth() => Math.Max(200, _flp.ClientSize.Width - _flp.Padding.Horizontal - 4);

    private void UpdateWidths()
    {
        int w = AyahWidth();
        foreach (var v in _views)
        {
            v.Width = w;
        }
    }
}
