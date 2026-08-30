namespace QuranDesktop.Controls;

internal sealed class DailyAyahDialog : Form
{
    private readonly Label _lblJudul = new()
    {
        Dock = DockStyle.Top,
        Height = 34,
        TextAlign = ContentAlignment.MiddleCenter,
        Font = new Font("Segoe UI", 11f, FontStyle.Bold),
        Text = "✨ Ayat Hari Ini",
    };
    private readonly Label _lblArab = new()
    {
        Dock = DockStyle.Fill,
        TextAlign = ContentAlignment.MiddleCenter,
        RightToLeft = RightToLeft.Yes,
        Font = new Font("Traditional Arabic", 20f),
        Padding = new Padding(14),
        Text = "Memuat…",
    };
    private readonly Label _lblArti = new()
    {
        Dock = DockStyle.Bottom,
        Height = 96,
        TextAlign = ContentAlignment.MiddleCenter,
        Font = new Font("Segoe UI", 10.5f),
        ForeColor = Color.FromArgb(70, 70, 70),
        Padding = new Padding(12, 0, 12, 4),
        Text = "",
    };
    private readonly FlowLayoutPanel _bottom = new()
    {
        Dock = DockStyle.Bottom,
        Height = 50,
        FlowDirection = FlowDirection.LeftToRight,
        WrapContents = false,
        Padding = new Padding(8, 8, 0, 0),
    };
    private readonly Button _btnBuka = new() { Text = "Buka di App", Width = 100 };
    private readonly Button _btnCard = new() { Text = "Kartu PNG", Width = 96 };
    private readonly Button _btnCopy = new() { Text = "Salin", Width = 70 };
    private readonly Button _btnJangan = new() { Text = "Jangan tampilkan lagi", Width = 150 };
    private readonly Button _btnClose = new() { Text = "Tutup", Width = 70 };

    private readonly InspirasiAyah _ayah;

    public event Action<int, int>? GotoRequested;

    public DailyAyahDialog()
    {
        _ayah = InspirasiContent.AyatHariIni();
        var info = SurahList.Get(_ayah.S);
        Text = "Ayat Hari Ini — Quran Desktop";
        ClientSize = new Size(620, 430);
        StartPosition = FormStartPosition.CenterParent;
        MinimumSize = new Size(540, 380);

        _bottom.Controls.Add(_btnBuka);
        _bottom.Controls.Add(_btnCard);
        _bottom.Controls.Add(_btnCopy);
        _bottom.Controls.Add(_btnJangan);
        _bottom.Controls.Add(_btnClose);

        _lblArti.Text = _ayah.Label;

        Controls.Add(_lblArab);
        Controls.Add(_lblArti);
        Controls.Add(_lblJudul);
        Controls.Add(_bottom);
        _lblJudul.BringToFront();
        _lblArti.BringToFront();

        _btnBuka.Click += (_, _) => { GotoRequested?.Invoke(_ayah.S, _ayah.A); Close(); };
        _btnCard.Click += async (_, _) => await ShowCardAsync();
        _btnCopy.Click += async (_, _) => await CopyAsync();
        _btnJangan.Click += (_, _) =>
        {
            AppSettings.Current.ShowDailyAyah = false;
            AppSettings.Current.Save();
            Close();
        };
        _btnClose.Click += (_, _) => Close();

        Load += async (_, _) => await LoadTextAsync();
    }

    private async Task LoadTextAsync()
    {
        try
        {
            var arabic = await ProgramServices.Api.GetSurahTarjamaAsync("ar_ayat", _ayah.S, CancellationToken.None);
            if (arabic.TryGetValue(_ayah.A, out var av)) _lblArab.Text = av;

            var t = ProgramServices.ActiveTranslationKey;
            if (!string.IsNullOrEmpty(t))
            {
                var map = await ProgramServices.Api.GetSurahTarjamaAsync(t, _ayah.S, CancellationToken.None);
                if (map.TryGetValue(_ayah.A, out var tv))
                {
                    var info = SurahList.Get(_ayah.S);
                    _lblArti.Text = KsuApi.StripHtml(tv) + $"\n— QS {_ayah.S}. {info.EnglishName} [{_ayah.S}:{_ayah.A}]";
                }
            }
        }
        catch (Exception ex)
        {
            _lblArab.Text = "Gagal memuat: " + ex.Message;
        }
    }

    private async Task ShowCardAsync()
    {
        try
        {
            var arabic = await ProgramServices.Api.GetSurahTarjamaAsync("ar_ayat", _ayah.S, CancellationToken.None);
            string arab = arabic.TryGetValue(_ayah.A, out var av) ? av : "";
            var t = ProgramServices.ActiveTranslationKey;
            string arti = "";
            if (!string.IsNullOrEmpty(t))
            {
                var map = await ProgramServices.Api.GetSurahTarjamaAsync(t, _ayah.S, CancellationToken.None);
                arti = map.TryGetValue(_ayah.A, out var tv) ? KsuApi.StripHtml(tv) : "";
            }
            using var dlg = new AyahImageDialog(_ayah.S, _ayah.A, arab, arti);
            dlg.ShowDialog(this);
        }
        catch
        {
        }
    }

    private async Task CopyAsync()
    {
        try
        {
            var arabic = await ProgramServices.Api.GetSurahTarjamaAsync("ar_ayat", _ayah.S, CancellationToken.None);
            string arab = arabic.TryGetValue(_ayah.A, out var av) ? av : "";
            var t = ProgramServices.ActiveTranslationKey;
            string arti = "";
            if (!string.IsNullOrEmpty(t))
            {
                var map = await ProgramServices.Api.GetSurahTarjamaAsync(t, _ayah.S, CancellationToken.None);
                arti = map.TryGetValue(_ayah.A, out var tv) ? KsuApi.StripHtml(tv) : "";
            }
            var info = SurahList.Get(_ayah.S);
            Clipboard.SetText($"{arab}\n\n\"{arti}\"\n— QS {_ayah.S}. {info.EnglishName} [{_ayah.S}:{_ayah.A}]");
            _lblArti.Text += "   ✓ tersalin";
        }
        catch
        {
        }
    }
}
