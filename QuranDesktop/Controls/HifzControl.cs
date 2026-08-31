namespace QuranDesktop.Controls;

internal sealed class HifzControl : Panel
{
    private readonly ComboBox _cmbFromSura = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 230, DropDownWidth = 280 };
    private readonly ComboBox _cmbFromAya = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 70 };
    private readonly ComboBox _cmbToSura = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 230, DropDownWidth = 280 };
    private readonly ComboBox _cmbToAya = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 70 };
    private readonly Button _btnQuestion = new() { Text = "Soal (Acak)", Width = 110 };
    private readonly Button _btnReveal = new() { Text = "Lihat / Sembunyikan", Width = 150 };
    private readonly Button _btnPlay = new() { Text = "Putar Ayat", Width = 100 };
    private readonly Label _lblInfo = new()
    {
        Text = "Pilih rentang, lalu klik Soal untuk tes hafalan acak.",
        Dock = DockStyle.Top,
        AutoSize = true,
        Padding = new Padding(10, 8, 10, 4),
        Font = new Font("Segoe UI", 11f, FontStyle.Bold),
    };
    private readonly RichTextBox _txt = new()
    {
        Dock = DockStyle.Fill,
        ReadOnly = true,
        BackColor = Color.White,
        BorderStyle = BorderStyle.FixedSingle,
        Font = new Font("Segoe UI", 12f),
    };

    private bool _busy;
    private (int Surah, int Ayah)? _current;
    private bool _revealed;

    public event Action<int, int>? PlayRequested;

    public HifzControl()
    {
        DoubleBuffered = true;

        var top = new FlowLayoutPanel
        {
            Dock = DockStyle.Top,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = false,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            Padding = new Padding(8),
        };

        top.Controls.Add(new Label { Text = "Dari:", AutoSize = true, Padding = new Padding(0, 8, 0, 0) });
        top.Controls.Add(_cmbFromSura);
        top.Controls.Add(_cmbFromAya);
        top.Controls.Add(new Label { Text = "Ke:", AutoSize = true, Padding = new Padding(8, 8, 0, 0) });
        top.Controls.Add(_cmbToSura);
        top.Controls.Add(_cmbToAya);
        top.Controls.Add(_btnQuestion);
        top.Controls.Add(_btnReveal);
        top.Controls.Add(_btnPlay);

        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            RowCount = 3,
            ColumnCount = 1,
            BackColor = Color.White,
        };
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));

        top.Dock = DockStyle.Fill;
        layout.Controls.Add(top, 0, 0);
        layout.Controls.Add(_lblInfo, 0, 1);
        layout.Controls.Add(_txt, 0, 2);

        Controls.Add(layout);

        _txt.ForeColor = Color.Gray;
        _txt.Text = "Klik 'Soal (Acak)' untuk memulai — audio diputar & teks disembunyikan, tebak dulu lalu klik Lihat.";

        for (int s = 1; s <= 114; s++)
        {
            var info = SurahList.Get(s);
            var item = new ComboItem($"{s}. {info.EnglishName} — {info.ArabicName}", s);
            _cmbFromSura.Items.Add(item);
            _cmbToSura.Items.Add(item);
        }

        _cmbFromSura.SelectedIndexChanged += (_, _) => FillAyaCombo(_cmbFromAya, CurrentFromSura);
        _cmbToSura.SelectedIndexChanged += (_, _) => FillAyaCombo(_cmbToAya, CurrentToSura);
        _cmbFromSura.SelectedIndex = 0;
        _cmbToSura.SelectedIndex = 1;

        _btnQuestion.Click += async (_, _) => await AskQuestionAsync();
        _btnReveal.Click += (_, _) => ToggleReveal();
        _btnPlay.Click += (_, _) =>
        {
            if (_current != null) PlayRequested?.Invoke(_current.Value.Surah, _current.Value.Ayah);
        };
    }

    private int CurrentFromSura => (int)((ComboItem)_cmbFromSura.SelectedItem!).Value!;

    private int CurrentToSura => (int)((ComboItem)_cmbToSura.SelectedItem!).Value!;

    private static void FillAyaCombo(ComboBox cmb, int surah)
    {
        int prev = cmb.SelectedIndex;
        cmb.Items.Clear();
        int n = QuranData.SurahAyahCount(surah);
        for (int a = 1; a <= n; a++) cmb.Items.Add(a.ToString());
        cmb.SelectedIndex = prev >= 0 && prev < n ? prev : 0;
    }

    private (int Surah, int Ayah) FromPoint => (CurrentFromSura, _cmbFromAya.SelectedIndex + 1);

    private (int Surah, int Ayah) ToPoint => (CurrentToSura, _cmbToAya.SelectedIndex + 1);

    private async Task AskQuestionAsync()
    {
        if (_busy) return;
        int fromId = QuranData.AyaToId(FromPoint.Surah, FromPoint.Ayah);
        int toId = QuranData.AyaToId(ToPoint.Surah, ToPoint.Ayah);
        if (toId < fromId) (fromId, toId) = (toId, fromId);

        int id = Random.Shared.Next(fromId, toId + 1);
        var (s, a) = QuranData.IdToAya(id);
        _current = (s, a);
        _revealed = false;

        var info = SurahList.Get(s);
        _lblInfo.Text = $"Surah {s}. {info.EnglishName} — Ayat {a}";

        _busy = true;
        _txt.Clear();
        try
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            string ayahText = MadinahText.Get(s, a) ?? "";
            if (string.IsNullOrWhiteSpace(ayahText))
            {
                var ar = await ProgramServices.Api.GetSurahTarjamaAsync("ar_ayat", s, cts.Token);
                ayahText = ar.TryGetValue(a, out var at) ? at : "";
            }
            if (string.IsNullOrWhiteSpace(ayahText))
            {
                var raw = await ProgramServices.Api.GetTafsirAsync("muyassar", s, a, cts.Token);
                ayahText = KsuApi.AyahTextFromTafsirRaw(raw);
            }
            _txt.Tag = ayahText;
        }
        catch (Exception ex)
        {
            _txt.Tag = "";
            _lblInfo.Text += "  (gagal memuat teks: " + ex.Message + ")";
        }
        finally
        {
            _busy = false;
            RenderText();
            if (_current != null && _txt.Tag is string loaded && string.IsNullOrWhiteSpace(loaded))
            {
                _lblInfo.Text += "  (teks tidak tersedia — cek koneksi lalu coba lagi)";
            }
            else if (_current != null && _txt.Tag is string ok && !string.IsNullOrWhiteSpace(ok))
            {
                PlayRequested?.Invoke(_current.Value.Surah, _current.Value.Ayah);
            }
        }
    }

    private void ToggleReveal()
    {
        _revealed = !_revealed;
        RenderText();
    }

    private void RenderText()
    {
        _txt.Clear();
        string? text = _txt.Tag as string;
        if (string.IsNullOrEmpty(text)) return;

        if (_revealed)
        {
            _txt.RightToLeft = RightToLeft.Yes;
            _txt.Font = MadinahFont.Create(18f);
            _txt.AppendText(text);
        }
        else
        {
            _txt.RightToLeft = RightToLeft.No;
            _txt.Font = new Font("Segoe UI", 12f);
            _txt.ForeColor = Color.Gray;
            _txt.AppendText("[ Teks disembunyikan — jawab dari hafalan, lalu klik Lihat untuk cek ]");
            _txt.ForeColor = Color.Black;
        }
    }
}
