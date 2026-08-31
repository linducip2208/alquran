namespace QuranDesktop.Controls;

internal sealed class DictationDialog : Form
{
    private readonly ComboBox _cmbFrom = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 240, DropDownWidth = 280 };
    private readonly ComboBox _cmbTo = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 240, DropDownWidth = 280 };
    private readonly Button _btnAsk = new() { Text = "Soal Baru", Width = 100 };
    private readonly Button _btnCheck = new() { Text = "Periksa", Width = 90, Enabled = false };
    private readonly Button _btnReplay = new() { Text = "Putar Ulang", Width = 100, Enabled = false };
    private readonly Label _lblInfo = new()
    {
        Dock = DockStyle.Top,
        Height = 32,
        TextAlign = ContentAlignment.MiddleLeft,
        Padding = new Padding(12, 8, 0, 0),
        Font = new Font("Segoe UI", 10f),
        Text = "Audio diputar acak — tebak surah & ayatnya, lalu klik Periksa.",
    };
    private readonly RichTextBox _txt = new()
    {
        Dock = DockStyle.Fill,
        ReadOnly = true,
        BorderStyle = BorderStyle.FixedSingle,
        BackColor = Color.White,
        Font = new Font("Segoe UI", 11f),
    };

    private (int Surah, int Ayah)? _current;
    private readonly Random _rng = new();
    private bool _revealed;

    public event Action<int, int>? PlayRequested;

    public DictationDialog()
    {
        Text = "Latihan Dikte (Imla')";
        ClientSize = new Size(640, 420);
        StartPosition = FormStartPosition.CenterParent;
        MinimumSize = new Size(560, 360);

        var top = new FlowLayoutPanel
        {
            Dock = DockStyle.Top,
            Height = 42,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = false,
            Padding = new Padding(8, 6, 0, 0),
        };
        for (int s = 1; s <= 114; s++)
        {
            var info = SurahList.Get(s);
            var item = new ComboItem($"{s}. {info.EnglishName}", s);
            _cmbFrom.Items.Add(item);
            _cmbTo.Items.Add(item);
        }
        _cmbFrom.SelectedIndex = 55;
        _cmbTo.SelectedIndex = 56;
        top.Controls.Add(new Label { Text = "Dari:", AutoSize = true, Padding = new Padding(0, 8, 0, 0) });
        top.Controls.Add(_cmbFrom);
        top.Controls.Add(new Label { Text = "Ke:", AutoSize = true, Padding = new Padding(6, 8, 0, 0) });
        top.Controls.Add(_cmbTo);
        top.Controls.Add(_btnAsk);
        top.Controls.Add(_btnReplay);
        top.Controls.Add(_btnCheck);

        Controls.Add(_txt);
        Controls.Add(_lblInfo);
        Controls.Add(top);
        _lblInfo.BringToFront();

        _btnAsk.Click += async (_, _) => await AskAsync();
        _btnReplay.Click += (_, _) =>
        {
            if (_current != null) PlayRequested?.Invoke(_current.Value.Surah, _current.Value.Ayah);
        };
        _btnCheck.Click += async (_, _) => await RevealAsync();
    }

    private async Task AskAsync()
    {
        int from = Math.Max(1, _cmbFrom.SelectedIndex + 1);
        int to = Math.Max(from, _cmbTo.SelectedIndex + 1);
        int s = _rng.Next(from, to + 1);
        int a = _rng.Next(1, QuranData.SurahAyahCount(s) + 1);
        _current = (s, a);
        _revealed = false;
        _btnCheck.Enabled = true;
        _btnReplay.Enabled = true;

        var info = SurahList.Get(s);
        _lblInfo.Text = $"Dengarkan… lalu tebak: surah & ayat berapa ini?";
        _txt.Clear();
        _txt.ForeColor = Color.Gray;
        _txt.AppendText("[Teks disembunyikan — dengarkan audio, tebak, lalu klik Periksa]");

        PlayRequested?.Invoke(s, a);
        await Task.CompletedTask;
    }

    private async Task RevealAsync()
    {
        if (_current == null) return;
        if (_revealed) return;
        _revealed = true;
        var (s, a) = _current.Value;
        try
        {
            var arabic = await ProgramServices.Api.GetSurahTarjamaAsync("ar_ayat", s, CancellationToken.None);
            var info = SurahList.Get(s);
            _txt.Clear();
            _txt.RightToLeft = RightToLeft.Yes;
            _txt.Font = new Font("Traditional Arabic", 18f);
            string text = arabic.TryGetValue(a, out var t) ? t : "(teks tidak tersedia)";
            _txt.AppendText($"QS {s}. {info.EnglishName} — Ayat {a}\n\n{text}");
        }
        catch (Exception ex)
        {
            _lblInfo.Text = "Gagal memuat teks: " + ex.Message;
        }
    }
}
