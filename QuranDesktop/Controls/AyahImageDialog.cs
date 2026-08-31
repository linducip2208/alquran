using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Drawing.Text;

namespace QuranDesktop.Controls;

internal sealed class AyahImageDialog : Form
{
    private readonly int _surah;
    private readonly int _ayah;
    private readonly PictureBox _pic = new()
    {
        Dock = DockStyle.Fill,
        SizeMode = PictureBoxSizeMode.Zoom,
        BackColor = Color.White,
    };
    private readonly Button _btnCopy = new() { Text = "Salin Teks", Width = 110 };
    private readonly Button _btnSave = new() { Text = "Simpan PNG…", Width = 110 };
    private readonly Button _btnClose = new() { Text = "Tutup", Width = 80 };
    private readonly Label _lblInfo = new() { AutoSize = true, Padding = new Padding(8, 10, 0, 0) };

    private string _arab = "";
    private string _arti = "";
    private Size _cardSize = new(600, 400);

    public AyahImageDialog(int surah, int ayah, string arab, string arti)
    {
        _surah = surah;
        _ayah = ayah;
        _arab = arab;
        _arti = arti;

        Text = $"Kartu Ayat — {surah}:{ayah}";
        StartPosition = FormStartPosition.CenterParent;
        MinimumSize = new Size(420, 300);

        var bottom = new FlowLayoutPanel
        {
            Dock = DockStyle.Bottom,
            Height = 46,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = false,
            Padding = new Padding(8, 6, 0, 0),
        };
        bottom.Controls.Add(_btnCopy);
        bottom.Controls.Add(_btnSave);
        bottom.Controls.Add(_btnClose);
        bottom.Controls.Add(_lblInfo);

        var scroll = new Panel
        {
            Dock = DockStyle.Fill,
            AutoScroll = true,
            BackColor = Color.FromArgb(244, 242, 236),
        };
        _pic.SizeMode = PictureBoxSizeMode.Normal;
        _pic.Location = Point.Empty;
        scroll.Controls.Add(_pic);
        Controls.Add(scroll);
        Controls.Add(bottom);
        _pic.BringToFront();

        var resizeTimer = new System.Windows.Forms.Timer { Interval = 250 };
        resizeTimer.Tick += (_, _) =>
        {
            resizeTimer.Stop();
            resizeTimer.Dispose();
            if (IsDisposed) return;
            RenderCard();
        };
        scroll.Resize += (_, _) =>
        {
            if (scroll.Width > 50 && Math.Abs(_cardSize.Width - (scroll.ClientSize.Width)) > 30)
            {
                resizeTimer.Stop();
                resizeTimer.Start();
            }
        };

        var info = SurahList.Get(surah);
        _lblInfo.Text = $"QS {surah}. {info.EnglishName} : {ayah}";

        _btnCopy.Click += (_, _) =>
        {
            try
            {
                Clipboard.SetText($"{_arab}\n\n\"{_arti}\"\n— QS {surah}. {info.EnglishName} [{surah}:{ayah}]");
                _lblInfo.Text = "Tersalin ke clipboard ✓";
            }
            catch
            {
            }
        };
        _btnSave.Click += (_, _) => SavePng();
        _btnClose.Click += (_, _) => Close();

        Load += (_, _) => { RenderCard(); FitWindow(); };
    }

    private void RenderCard()
    {
        int w = Math.Max(400, _pic.ClientSize.Width > 10 ? _pic.ClientSize.Width : 600);
        using var measure = Graphics.FromHwnd(IntPtr.Zero);
        measure.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;

        var info = SurahList.Get(_surah);
        using var headFont = new Font("Segoe UI", 11f, FontStyle.Bold);
        using var arabFont = MadinahFont.Create(28f);
        using var artiFont = new Font("Segoe UI", 11f);
        using var dark = new SolidBrush(Color.FromArgb(40, 60, 40));
        using var gold = new SolidBrush(Color.FromArgb(150, 110, 30));
        using var gray = new SolidBrush(Color.FromArgb(70, 70, 70));
        using var footBrush = new SolidBrush(Color.FromArgb(150, 150, 150));

        float inner = w - 90;
        var arabFlags = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Near, FormatFlags = StringFormatFlags.DirectionRightToLeft };
        var artiFlags = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Near };

        float headH = measure.MeasureString($"﴿ QS {_surah}. {info.EnglishName} : {_ayah} ﴾", headFont, (int)inner).Height + 16;
        SizeF arabSize = TextRenderer.MeasureText(_arab, arabFont, new Size((int)inner, int.MaxValue), TextFormatFlags.WordBreak);
        float arabH = arabSize.Height + 24;
        SizeF artiSize = string.IsNullOrWhiteSpace(_arti)
            ? new SizeF(0, 0)
            : TextRenderer.MeasureText("\"" + _arti + "\"", artiFont, new Size((int)inner, int.MaxValue), TextFormatFlags.WordBreak);
        float artiH = (artiSize.Height > 0 ? artiSize.Height + 20 : 0);
        float footH = 40;
        int totalH = (int)Math.Ceiling(48 + headH + arabH + artiH + footH) + 24;

        var bmp = new Bitmap(w, totalH);
        using var g = Graphics.FromImage(bmp);
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
        g.Clear(Color.FromArgb(252, 250, 243));

        using var frame = new Pen(Color.FromArgb(180, 170, 130, 50), 2f);
        g.DrawRectangle(frame, 10, 10, w - 21, totalH - 21);

        float y = 26;
        var sh = measure.MeasureString($"﴿ QS {_surah}. {info.EnglishName} : {_ayah} ﴾", headFont, (int)inner);
        g.DrawString($"﴿ QS {_surah}. {info.EnglishName} : {_ayah} ﴾", headFont, gold, (w - sh.Width) / 2, y);
        y += headH;

        var arabRect = new RectangleF(45, y, inner, arabH);
        g.DrawString(_arab, arabFont, dark, arabRect, arabFlags);
        y += arabH;

        if (artiH > 0)
        {
            var artiRect = new RectangleF(45, y, inner, artiH);
            g.DrawString("\"" + _arti + "\"", artiFont, gray, artiRect, artiFlags);
            y += artiH;
        }

        string foot = "Quran Desktop — berbasis quran.ksu.edu.sa";
        var sfFoot = new StringFormat { Alignment = StringAlignment.Center };
        g.DrawString(foot, new Font("Segoe UI", 8f), footBrush, new RectangleF(0, totalH - 44, w, 24), sfFoot);

        _cardSize = new Size(w, totalH);
        var old = _pic.Image;
        _pic.Image = bmp;
        _pic.Size = bmp.Size;
        old?.Dispose();
    }

    private void FitWindow()
    {
        var wa = Screen.FromControl(this).WorkingArea;
        int targetW = Math.Max(MinimumSize.Width, Math.Min(_cardSize.Width + 16, wa.Width - 40));
        int targetH = Math.Min(_cardSize.Height + 46 + 12, wa.Height - 60);
        ClientSize = new Size(targetW, targetH);
    }

    private void SavePng()
    {
        using var dlg = new SaveFileDialog
        {
            Filter = "PNG Image|*.png",
            FileName = $"ayat-{_surah}-{_ayah}.png",
        };
        if (dlg.ShowDialog(this) != DialogResult.OK) return;
        if (_pic.Image == null) return;
        _pic.Image.Save(dlg.FileName, ImageFormat.Png);
        _lblInfo.Text = "Tersimpan: " + Path.GetFileName(dlg.FileName);
    }
}
