namespace QuranDesktop;

internal sealed class AyahView : Panel
{
    private static readonly Color NormalColor = Color.White;
    private static readonly Color HoverColor = Color.FromArgb(238, 245, 251);
    private static readonly Color SelectedColor = Color.FromArgb(255, 242, 204);

    private readonly Label _arabic = new();
    private readonly Label _trans = new();
    private readonly Label _tafsir = new();
    private bool _hover;
    private bool _selected;

    public event EventHandler? AyahClicked;

    public int NumberInSurah { get; }

    public AyahView(AyahData ayah, Font arabicFont, Font transFont, bool transRtl, Font tafsirFont)
    {
        DoubleBuffered = true;
        NumberInSurah = ayah.NumberInSurah;
        Padding = new Padding(12, 8, 12, 8);
        Margin = new Padding(0, 0, 0, 8);
        BackColor = NormalColor;
        Cursor = Cursors.Hand;

        _arabic.BackColor = Color.Transparent;
        _arabic.TextAlign = ContentAlignment.TopRight;
        _arabic.RightToLeft = RightToLeft.Yes;
        _arabic.Font = arabicFont;
        _arabic.Text = ayah.Arabic + " \uFD3F" + Utils.ToArabicDigits(ayah.NumberInSurah) + "\uFD3E";
        _arabic.Padding = new Padding(0, 0, 0, 4);
        _arabic.Cursor = Cursors.Hand;
        _arabic.Visible = !string.IsNullOrWhiteSpace(ayah.Arabic);

        _trans.BackColor = Color.Transparent;
        _trans.TextAlign = transRtl ? ContentAlignment.TopRight : ContentAlignment.TopLeft;
        _trans.RightToLeft = transRtl ? RightToLeft.Yes : RightToLeft.No;
        _trans.Font = transFont;
        _trans.ForeColor = Color.FromArgb(72, 72, 72);
        _trans.Text = ayah.Translation;
        _trans.Padding = new Padding(0, 2, 0, 6);
        _trans.Cursor = Cursors.Hand;
        _trans.Visible = !string.IsNullOrWhiteSpace(ayah.Translation);

        _tafsir.BackColor = Color.Transparent;
        _tafsir.TextAlign = ContentAlignment.TopRight;
        _tafsir.RightToLeft = RightToLeft.Yes;
        _tafsir.ForeColor = Color.FromArgb(146, 96, 6);
        _tafsir.Padding = new Padding(0, 0, 0, 2);
        _tafsir.Cursor = Cursors.Hand;
        _tafsir.Font = tafsirFont;
        _tafsir.Visible = false;

        Controls.Add(_arabic);
        Controls.Add(_trans);
        Controls.Add(_tafsir);

        foreach (Control c in new Control[] { this, _arabic, _trans, _tafsir })
        {
            c.Click += (s, e) => AyahClicked?.Invoke(this, EventArgs.Empty);
            c.MouseEnter += (s, e) => { _hover = true; UpdateBack(); };
            c.MouseLeave += (s, e) => { _hover = false; UpdateBack(); };
        }

        UpdateBack();
        LayoutChildren();
    }

    public bool Selected
    {
        get => _selected;
        set
        {
            _selected = value;
            UpdateBack();
        }
    }

    public void SetTransVisible(bool visible)
    {
        _trans.Visible = visible && !string.IsNullOrWhiteSpace(_trans.Text);
        LayoutChildren();
    }

    public void SetTafsir(string? text, Font? font, bool rtl)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            _tafsir.Visible = false;
        }
        else
        {
            _tafsir.Text = text;
            if (font != null) _tafsir.Font = font;
            _tafsir.TextAlign = rtl ? ContentAlignment.TopRight : ContentAlignment.TopLeft;
            _tafsir.RightToLeft = rtl ? RightToLeft.Yes : RightToLeft.No;
            _tafsir.Visible = true;
        }
        LayoutChildren();
    }

    private void UpdateBack() => BackColor = _selected ? SelectedColor : _hover ? HoverColor : NormalColor;

    private void LayoutChildren()
    {
        int w = Math.Max(50, Width - Padding.Horizontal);
        int y = Padding.Top;

        if (_arabic.Visible)
        {
            _arabic.SetBounds(Padding.Left, y, w, MeasureHeight(_arabic, w));
            y += _arabic.Height;
        }

        if (_trans.Visible)
        {
            _trans.SetBounds(Padding.Left, y, w, MeasureHeight(_trans, w));
            y += _trans.Height;
        }

        if (_tafsir.Visible)
        {
            _tafsir.SetBounds(Padding.Left, y, w, MeasureHeight(_tafsir, w));
            y += _tafsir.Height;
        }

        Height = y + Padding.Bottom;
    }

    private static int MeasureHeight(Label label, int width)
    {
        var flags = TextFormatFlags.WordBreak;
        if (label.RightToLeft == RightToLeft.Yes) flags |= TextFormatFlags.RightToLeft;
        var size = TextRenderer.MeasureText(
            label.Text,
            label.Font,
            new Size(width - label.Padding.Horizontal, int.MaxValue),
            flags);
        return size.Height + label.Padding.Vertical;
    }

    protected override void OnResize(EventArgs eventargs)
    {
        base.OnResize(eventargs);
        LayoutChildren();
    }
}
