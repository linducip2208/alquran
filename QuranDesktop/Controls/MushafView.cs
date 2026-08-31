namespace QuranDesktop.Controls;

internal sealed class MushafView : Panel
{
    private sealed class PageBox
    {
        public PictureBox Pic = new() { SizeMode = PictureBoxSizeMode.StretchImage, Location = Point.Empty };
        public Image? Img;
        public int Page = -1;
        public Dictionary<string, int[]> Hilites = new();

        public float Scale => Img == null || Img.Width == 0 ? 1f : (float)Pic.Width / Img.Width;
    }

    private const int Gap = 10;
    private const int TopMargin = 38;
    private const float HitRadius = 75f;

    private readonly PageBox _left = new();
    private readonly PageBox _right = new();
    private (int Surah, int Ayah)? _selected;
    private float _zoom = 1f;
    private HttpClient _http = new();
    private string _mushafKey = "hafs";
    private readonly ToolTip _tip = new();
    private (int Page, int Surah, int Ayah)? _tipAya;
    private static readonly Color NormalColor = Color.White;
    private static readonly Color HoverColor = Color.FromArgb(238, 245, 251);
    private static readonly Color SelectedColor = Color.FromArgb(255, 242, 204);
    private bool _leftHover;
    private bool _rightHover;

    public event Action<int, int>? AyahClicked;
    public event Action? ImageChanged;
    public event Action? ZoomChanged;

    public Func<int, int, string>? TooltipProvider { get; set; }
    public Func<int, int, string>? OverlayProvider { get; set; }
    public bool ShowOverlay { get; set; }

    public HashSet<(int Surah, int Ayah)> SearchHits { get; } = new();

    public void SetSearchHits(IEnumerable<(int Surah, int Ayah)> hits)
    {
        SearchHits.Clear();
        foreach (var h in hits)
        {
            SearchHits.Add(h);
        }
        _left.Pic.Invalidate();
        _right.Pic.Invalidate();
    }

    public int CurrentPage { get; private set; } = -1;

    public bool SinglePage { get; set; }

    public (int Right, int Left) SpreadPages => SinglePage
        ? (CurrentPage, -1)
        : (_right.Page, _left.Page);

    public MushafView()
    {
        DoubleBuffered = true;
        AutoScroll = true;
        BackColor = Color.FromArgb(40, 40, 42);
        Controls.Add(_left.Pic);
        Controls.Add(_right.Pic);

        foreach (var box in new[] { _left, _right })
        {
            box.Pic.MouseClick += OnPicMouseClick;
            box.Pic.MouseMove += OnPicMouseMove;
            box.Pic.MouseLeave += OnPicMouseLeave;
            box.Pic.Paint += OnPicPaint;
            box.Pic.MouseEnter += (_, _) => { if (box == _left) _leftHover = true; else _rightHover = true; InvalidateBox(box); };
        }
        _left.Pic.MouseLeave += (_, _) => { _leftHover = false; InvalidateBox(_left); };
        _right.Pic.MouseLeave += (_, _) => { _rightHover = false; InvalidateBox(_right); };
    }

    private void InvalidateBox(PageBox box) => box.Pic.Invalidate();

    public async Task LoadAsync(int page, string mushafKey, HttpClient http, CancellationToken ct)
    {
        _http = http;
        _mushafKey = mushafKey;
        CurrentPage = page;

        int rightPage;
        int leftPage;
        if (SinglePage)
        {
            rightPage = page;
            leftPage = -1;
            await LoadPageBoxAsync(_right, rightPage, ct);
            if (_left.Page != -1)
            {
                var oldLeftImg = _left.Pic.Image;
                _left.Img = null;
                _left.Page = -1;
                _left.Hilites = new Dictionary<string, int[]>();
                _left.Pic.Image = null;
                oldLeftImg?.Dispose();
            }
        }
        else
        {
            rightPage = page % 2 == 1 ? page : page - 1;
            leftPage = rightPage + 1;
            var rightTask = LoadPageBoxAsync(_right, rightPage, ct);
            var leftTask = LoadPageBoxAsync(_left, leftPage, ct);
            await rightTask;
            await leftTask;
        }

        bool sameSpread = _right.Page == rightPage && (_left.Page == leftPage || SinglePage) && _right.Img != null;
        sameSpread = _right.Page == rightPage && _right.Img != null;

        LayoutPages();
        _left.Pic.Invalidate();
        _right.Pic.Invalidate();
        ImageChanged?.Invoke();
    }

    private async Task LoadPageBoxAsync(PageBox box, int page, CancellationToken ct)
    {
        await KsuAudio.EnsureMushafPageAsync(_mushafKey, page, _http, ct);

        string local = Path.Combine(KsuAudio.CacheDir, "mushaf", _mushafKey, page + ".png");
        byte[] bytes = await File.ReadAllBytesAsync(local, ct);
        using var ms = new MemoryStream(bytes);
        using var raw = Image.FromStream(ms);
        var img = new Bitmap(raw);

        var old = box.Pic.Image;
        box.Img = img;
        box.Page = page;
        box.Pic.Image = img;
        old?.Dispose();

        box.Pic.Invalidate();
    }

    public void SetHilites(int page, Dictionary<string, int[]> hilites)
    {
        var box = _left.Page == page ? _left : _right.Page == page ? _right : null;
        if (box == null) return;
        box.Hilites = hilites ?? new Dictionary<string, int[]>();
        box.Pic.Invalidate();
    }

    public void SetSelected((int Surah, int Ayah) sel)
    {
        _selected = sel;
        _left.Pic.Invalidate();
        _right.Pic.Invalidate();
        ScrollToAyah(sel.Surah, sel.Ayah);
    }

    public void SetZoom(float zoom)
    {
        _zoom = Math.Clamp(zoom, 0.15f, 2.5f);
        LayoutPages();
        _left.Pic.Invalidate();
        _right.Pic.Invalidate();
    }

    public void FitToScreen()
    {
        var img = _right.Img ?? _left.Img;
        if (img == null || img.Width == 0) return;

        int availW = Math.Max(100, ClientSize.Width - Padding.Horizontal - 4);
        int availH = Math.Max(100, ClientSize.Height - TopMargin * 2 - 20);

        float fitW, fitH;
        if (SinglePage)
        {
            fitW = (float)availW / img.Width;
            fitH = (float)availH / img.Height;
        }
        else
        {
            int slotW = (availW - Gap) / 2;
            fitW = (float)slotW / img.Width;
            fitH = (float)availH / img.Height;
        }

        SetZoom(Math.Min(fitW, fitH));
        AutoScrollPosition = new Point(0, 0);
        _left.Pic.Invalidate();
        _right.Pic.Invalidate();
    }

    public float Zoom => _zoom;

    private void LayoutPages()
    {
        int availW = Math.Max(200, ClientSize.Width - Padding.Horizontal - 4);
        int bottomPad = TopMargin;

        if (SinglePage)
        {
            _left.Pic.Visible = false;
            _right.Pic.Visible = true;
            if (_right.Img != null)
            {
                int w = Math.Max(80, (int)(availW * _zoom));
                int h = Math.Max(100, w * _right.Img.Height / _right.Img.Width);
                _right.Pic.SetBounds(Padding.Left, TopMargin, w, h);
                _picBounds = new Rectangle(Padding.Left, TopMargin, w, h);
            }
            AutoScrollMargin = new Size(0, bottomPad);
            return;
        }

        _left.Pic.Visible = true;
        _right.Pic.Visible = true;
        int slotW = (availW - Gap) / 2;

        if (_left.Img != null)
        {
            int w = Math.Max(80, (int)(slotW * _zoom));
            int h = Math.Max(100, w * _left.Img.Height / _left.Img.Width);
            _left.Pic.SetBounds(0, TopMargin, w, h);
            _picBounds = new Rectangle(0, TopMargin, w, h);
        }

        if (_right.Img != null)
        {
            int w = Math.Max(80, (int)(slotW * _zoom));
            int h = Math.Max(100, w * _right.Img.Height / _right.Img.Width);
            int x = _left.Img != null && _left.Pic.Visible ? _left.Pic.Right + Gap : Padding.Left;
            _right.Pic.SetBounds(x, TopMargin, w, h);
            _picBounds = new Rectangle(x, TopMargin, w, h);
        }

        AutoScrollMargin = new Size(0, bottomPad);
        ClampScroll();
    }

    private Rectangle _picBounds = Rectangle.Empty;

    private void ClampScroll()
    {
        try
        {
            int curX = Math.Max(0, -AutoScrollPosition.X);
            int curY = Math.Max(0, -AutoScrollPosition.Y);
            int maxX = Math.Max(0, DisplayRectangle.Width - ClientSize.Width);
            int maxY = Math.Max(0, DisplayRectangle.Height - ClientSize.Height);
            AutoScrollPosition = new Point(Math.Min(curX, maxX), Math.Min(curY, maxY));
        }
        catch
        {
        }
    }

    public void ScrollToTop()
    {
        AutoScrollPosition = new Point(0, 0);
        _left.Pic.Invalidate();
        _right.Pic.Invalidate();
    }

    public int ScrollTopPixel
    {
        get => Math.Max(0, -AutoScrollPosition.Y);
        set => AutoScrollPosition = new Point(Math.Max(0, -AutoScrollPosition.X), Math.Max(0, value));
    }

    public int TopMarginPx => TopMargin;

    protected override void OnMouseWheel(MouseEventArgs e)
    {
        if (ModifierKeys.HasFlag(Keys.Control))
        {
            SetZoom(_zoom * (e.Delta > 0 ? 1.15f : 1f / 1.15f));
            ZoomChanged?.Invoke();
            return;
        }
        base.OnMouseWheel(e);
    }

    private void ScrollToAyah(int surah, int ayah)
    {
        string key = $"{surah}_{ayah}";
        foreach (var box in new[] { _right, _left })
        {
            if (box.Page > 0 && box.Hilites.TryGetValue(key, out var pt))
            {
                float sc = box.Scale;
                int yInBox = (int)(pt[1] * sc);
                int yAbs = box.Pic.Top + yInBox;
                AutoScrollPosition = new Point(0, Math.Max(0, yAbs - Height / 3));
                return;
            }
        }
        ScrollToTop();
    }

    private PageBox? HitBox(PictureBox pic) => pic == _left.Pic ? _left : pic == _right.Pic ? _right : null;

    private (int Surah, int Ayah)? FindNearest(PageBox box, Point picPoint)
    {
        if (box.Hilites.Count == 0) return null;
        float sc = box.Scale;
        float ix = picPoint.X / sc, iy = picPoint.Y / sc;

        string? bestKey = null;
        double bestDist = HitRadius * HitRadius;
        foreach (var (key, pt) in box.Hilites)
        {
            double dx = pt[0] - ix, dy = pt[1] - iy;
            double d = dx * dx + dy * dy;
            if (d < bestDist)
            {
                bestDist = d;
                bestKey = key;
            }
        }

        if (bestKey == null) return null;
        var parts = bestKey.Split('_');
        if (parts.Length == 2 && int.TryParse(parts[0], out int s) && int.TryParse(parts[1], out int a)) return (s, a);
        return null;
    }

    private void OnPicMouseMove(object? sender, MouseEventArgs e)
    {
        if (sender is not PictureBox pic || HitBox(pic) is not PageBox box) return;
        var hit = FindNearest(box, e.Location);
        var tipKey = hit.HasValue ? (box.Page, hit.Value.Surah, hit.Value.Ayah) : ((int Page, int Surah, int Ayah)?)null;

        if (tipKey == null)
        {
            if (_tipAya != null)
            {
                _tipAya = null;
                _tip.SetToolTip(pic, "");
            }
            return;
        }
        if (tipKey != _tipAya)
        {
            _tipAya = tipKey;
            string txt = $"{hit!.Value.Surah}:{hit.Value.Ayah}";
            if (TooltipProvider != null) txt += " — " + TooltipProvider(hit.Value.Surah, hit.Value.Ayah);
            _tip.SetToolTip(pic, txt);
        }
    }

    private void OnPicMouseLeave(object? sender, EventArgs e)
    {
        _tipAya = null;
        if (sender is PictureBox pic) _tip.SetToolTip(pic, "");
    }

    private void OnPicMouseClick(object? sender, MouseEventArgs e)
    {
        if (sender is not PictureBox pic || HitBox(pic) is not PageBox box) return;
        var hit = FindNearest(box, e.Location);
        if (hit != null)
        {
            AyahClicked?.Invoke(hit.Value.Surah, hit.Value.Ayah);
        }
    }

    private void OnPicPaint(object? sender, PaintEventArgs e)
    {
        if (sender is not PictureBox pic || HitBox(pic) is not PageBox box) return;
        if (box.Img == null) return;
        float sc = box.Scale;
        e.Graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;

        bool isSelBox = _selected.HasValue && box.Hilites.ContainsKey($"{_selected.Value.Surah}_{_selected.Value.Ayah}");

        Color boxTint = isSelBox ? SelectedColor : _leftHover && box == _left ? HoverColor : _rightHover && box == _right ? HoverColor : NormalColor;
        if (boxTint != NormalColor)
        {
            using var tint = new SolidBrush(Color.FromArgb(28, boxTint));
            e.Graphics.FillRectangle(tint, 0, 0, pic.Width, pic.Height);
        }

        foreach (var (key, pt) in box.Hilites)
        {
            var parts = key.Split('_');
            if (parts.Length != 2 || !int.TryParse(parts[0], out int s) || !int.TryParse(parts[1], out int a)) continue;
            bool isSel = _selected.HasValue && _selected.Value.Surah == s && _selected.Value.Ayah == a;
            float x = pt[0] * sc, y = pt[1] * sc;
            float r = isSel ? 30f : 15f;

            using (var fill = new SolidBrush(Color.FromArgb(isSel ? 110 : 42, 255, 214, 64)))
            using (var pen = new Pen(isSel ? Color.FromArgb(210, 210, 40, 40) : Color.FromArgb(60, 180, 140, 0), isSel ? 3f : 1.4f))
            {
                e.Graphics.FillEllipse(fill, x - r, y - r, r * 2, r * 2);
                e.Graphics.DrawEllipse(pen, x - r, y - r, r * 2, r * 2);
            }

            if (!isSel && SearchHits.Contains((s, a)))
            {
                using var ring = new Pen(Color.FromArgb(200, 40, 110, 220), 3f);
                e.Graphics.DrawEllipse(ring, x - 22, y - 22, 44, 44);
            }

            if (ShowOverlay && OverlayProvider != null)
            {
                var text = OverlayProvider(s, a);
                if (!string.IsNullOrWhiteSpace(text))
                {
                    DrawOverlay(e.Graphics, pic, box, x, y, text);
                }
            }
        }
    }

    private void DrawOverlay(Graphics g, PictureBox pic, PageBox box, float x, float y, string text)
    {
        float sc = box.Scale;
        float fontSize = Math.Clamp(9f * sc, 6.5f, 15f);
        using var font = new Font("Segoe UI", fontSize);
        float maxW = Math.Clamp(260f * sc, 130f, pic.Width - 20f);
        var lines = WrapText(g, text, font, maxW);
        if (lines.Count == 0) return;

        float lineH = font.GetHeight(g) + 2f;
        float pad = 5f;
        float boxW = 0f;
        foreach (var line in lines)
        {
            float lw = g.MeasureString(line, font).Width;
            if (lw > boxW) boxW = lw;
        }
        boxW += pad * 2;
        float boxH = lines.Count * lineH + pad * 2;

        float bx = x + 14f;
        if (bx + boxW > pic.Width - 4f) bx = x - 14f - boxW;
        float by = y + 10f;
        if (by + boxH > pic.Height - 4f) by = Math.Max(4f, pic.Height - boxH - 4f);

        using var bg = new SolidBrush(Color.FromArgb(215, 255, 253, 238));
        using var border = new Pen(Color.FromArgb(190, 160, 120, 40), 1.2f);
        using var textBrush = new SolidBrush(Color.FromArgb(60, 45, 20));

        g.FillRectangle(bg, bx, by, boxW, boxH);
        g.DrawRectangle(border, bx, by, boxW, boxH);

        float ty = by + pad;
        foreach (var line in lines)
        {
            g.DrawString(line, font, textBrush, bx + pad, ty);
            ty += lineH;
        }
    }

    private static List<string> WrapText(Graphics g, string text, Font font, float maxW)
    {
        var result = new List<string>();
        var current = new System.Text.StringBuilder();
        foreach (var word in text.Replace('\n', ' ').Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
            string candidate = current.Length == 0 ? word : current + " " + word;
            if (g.MeasureString(candidate, font).Width <= maxW)
            {
                current.Append(current.Length == 0 ? word : " " + word);
            }
            else
            {
                if (current.Length > 0) result.Add(current.ToString());
                current.Clear();
                current.Append(word);
            }
        }
        if (current.Length > 0) result.Add(current.ToString());
        return result;
    }

    protected override void OnResize(EventArgs eventargs)
    {
        base.OnResize(eventargs);
        LayoutPages();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _left.Img?.Dispose();
            _right.Img?.Dispose();
            _tip.Dispose();
        }
        base.Dispose(disposing);
    }
}
