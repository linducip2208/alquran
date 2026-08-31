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

    public AyahImageDialog(int surah, int ayah, string arab, string arti)
    {
        _surah = surah;
        _ayah = ayah;
        _arab = arab;
        _arti = arti;

        Text = $"Kartu Ayat — {surah}:{ayah}";
        ClientSize = new Size(640, 420);
        StartPosition = FormStartPosition.CenterParent;
        MinimumSize = new Size(520, 360);

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

        Controls.Add(_pic);
        Controls.Add(bottom);
        _pic.BringToFront();

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

        RenderCard();
    }

    private void RenderCard()
    {
        int w = _pic.ClientSize.Width > 10 ? _pic.ClientSize.Width : 600;
        int h = _pic.ClientSize.Height > 10 ? _pic.ClientSize.Height : 360;
        var bmp = new Bitmap(w, h);
        using var g = Graphics.FromImage(bmp);
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
        g.Clear(Color.FromArgb(252, 250, 243));

        using var frame = new Pen(Color.FromArgb(180, 170, 130, 50), 2f);
        g.DrawRectangle(frame, 10, 10, w - 21, h - 21);

        var info = SurahList.Get(_surah);
        using var headFont = new Font("Segoe UI", 11f, FontStyle.Bold);
        using var arabFont = MadinahFont.Create(28f);
        using var artiFont = new Font("Segoe UI", 11f);
        using var dark = new SolidBrush(Color.FromArgb(40, 60, 40));
        using var gold = new SolidBrush(Color.FromArgb(150, 110, 30));
        using var gray = new SolidBrush(Color.FromArgb(70, 70, 70));
        using var footBrush = new SolidBrush(Color.FromArgb(150, 150, 150));

        string header = $"﴿ QS {_surah}. {info.EnglishName} : {_ayah} ﴾";
        var sh = g.MeasureString(header, headFont);
        g.DrawString(header, headFont, gold, (w - sh.Width) / 2, 26);

        var rect = new RectangleF(40, 70, w - 80, h * 0.42f);
        var sf = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Near };
        g.DrawString(_arab, arabFont, dark, rect, sf);

        var artiRect = new RectangleF(40, h * 0.55f, w - 80, h * 0.28f);
        using var artiFormat = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Near };
        g.DrawString("\"" + _arti + "\"", artiFont, gray, artiRect, artiFormat);

        string foot = "Quran Desktop — berbasis quran.ksu.edu.sa";
        var sfFoot = new StringFormat { Alignment = StringAlignment.Center };
        g.DrawString(foot, new Font("Segoe UI", 8f), footBrush, new RectangleF(0, h - 44, w, 24), sfFoot);

        var old = _pic.Image;
        _pic.Image = bmp;
        old?.Dispose();
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
