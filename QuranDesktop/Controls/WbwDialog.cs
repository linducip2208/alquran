namespace QuranDesktop.Controls;

internal sealed class WbwDialog : Form
{
    private readonly Label _lblHeader = new()
    {
        Dock = DockStyle.Top,
        Height = 34,
        TextAlign = ContentAlignment.MiddleLeft,
        Padding = new Padding(12, 8, 0, 0),
        Font = new Font("Segoe UI", 11f, FontStyle.Bold),
    };
    private readonly FlowLayoutPanel _flow = new()
    {
        Dock = DockStyle.Fill,
        FlowDirection = FlowDirection.LeftToRight,
        WrapContents = true,
        AutoScroll = true,
        Padding = new Padding(10),
        BackColor = Color.FromArgb(250, 250, 246),
    };
    private readonly Button _btnPrev = new() { Text = "◀ Ayat", Width = 70 };
    private readonly Button _btnNext = new() { Text = "Ayat ▶", Width = 70 };
    private readonly Button _btnClose = new() { Text = "Tutup", Width = 70 };
    private readonly Label _lblStatus = new() { AutoSize = true, Padding = new Padding(8, 12, 0, 0), ForeColor = Color.DimGray };

    private int _surah;
    private int _ayah;
    private bool _busy;

    public WbwDialog(int surah, int ayah)
    {
        _surah = surah;
        _ayah = ayah;
        Text = "Terjemahan Kata per Kata — quran.com";
        ClientSize = new Size(860, 380);
        StartPosition = FormStartPosition.CenterParent;
        MinimumSize = new Size(700, 320);

        var bottom = new FlowLayoutPanel
        {
            Dock = DockStyle.Bottom,
            Height = 48,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = false,
            Padding = new Padding(8, 8, 0, 0),
        };
        bottom.Controls.Add(_btnPrev);
        bottom.Controls.Add(_btnNext);
        bottom.Controls.Add(_btnClose);
        bottom.Controls.Add(_lblStatus);

        Controls.Add(_flow);
        Controls.Add(_lblHeader);
        Controls.Add(bottom);
        _lblHeader.BringToFront();

        _btnPrev.Click += async (_, _) => { if (_ayah > 1) { _ayah--; await LoadAsync(); } };
        _btnNext.Click += async (_, _) => { if (_ayah < QuranData.SurahAyahCount(_surah)) { _ayah++; await LoadAsync(); } };
        _btnClose.Click += (_, _) => Close();

        Load += async (_, _) => await LoadAsync();
    }

    private async Task LoadAsync()
    {
        if (_busy) return;
        _busy = true;
        var info = SurahList.Get(_surah);
        _lblHeader.Text = $"QS {_surah}. {info.EnglishName} — Ayat {_ayah}";
        _lblStatus.Text = "Memuat…";
        _flow.Controls.Clear();

        try
        {
            var words = await QuranComApi.GetWordsAsync(_surah, _ayah, CancellationToken.None);
            foreach (var w in words)
            {
                var card = new Panel
                {
                    Size = new Size(128, 130),
                    Margin = new Padding(4),
                    BackColor = Color.White,
                    BorderStyle = BorderStyle.FixedSingle,
                };
                var arab = new Label
                {
                    Dock = DockStyle.Top,
                    Height = 52,
                    Text = w.Uthmani,
                    Font = new Font("Traditional Arabic", 17f),
                    TextAlign = ContentAlignment.MiddleCenter,
                    RightToLeft = RightToLeft.Yes,
                };
                var translit = new Label
                {
                    Dock = DockStyle.Top,
                    Height = 30,
                    Text = w.Transliteration,
                    Font = new Font("Segoe UI", 8.5f, FontStyle.Italic),
                    TextAlign = ContentAlignment.MiddleCenter,
                    ForeColor = Color.FromArgb(150, 110, 30),
                };
                var trans = new Label
                {
                    Dock = DockStyle.Fill,
                    Text = w.Translation,
                    Font = new Font("Segoe UI", 8.5f),
                    TextAlign = ContentAlignment.MiddleCenter,
                    ForeColor = Color.FromArgb(70, 70, 70),
                };
                card.Controls.Add(trans);
                card.Controls.Add(translit);
                card.Controls.Add(arab);
                trans.BringToFront();
                translit.BringToFront();
                _flow.Controls.Add(card);
            }
            _lblStatus.Text = words.Count == 0 ? "Tidak ada data" : $"{words.Count} kata — sumber: quran.com (arti Inggris)";
        }
        catch (Exception ex)
        {
            _lblStatus.Text = "Gagal memuat: " + ex.Message;
        }
        finally
        {
            _busy = false;
        }
    }
}
