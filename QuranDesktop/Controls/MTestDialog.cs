namespace QuranDesktop.Controls;

/// <summary>
/// (KSU mtest — "Mushaf Test") uji hafalan pada RENTANG surah-ayat pilihan:
/// ayat acak dari rentang ditampilkan dalam mode tersamar, pengguna mengingat,
/// lalu membuka jawaban (teks Arab + arti) dan/atau memutar audio ayat.
/// </summary>
internal sealed class MTestDialog : Form
{
    private readonly ComboBox _cmbFrom = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 210, DropDownWidth = 250 };
    private readonly ComboBox _cmbTo = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 210, DropDownWidth = 250 };
    private readonly NumericUpDown _numFromA = new() { Minimum = 1, Maximum = 286, Value = 1, Width = 60 };
    private readonly NumericUpDown _numToA = new() { Minimum = 1, Maximum = 286, Value = 5, Width = 60 };
    private readonly Button _btnAsk = new() { Text = "Soal Baru", Width = 92 };
    private readonly Button _btnReveal = new() { Text = "Jawaban", Width = 88, Enabled = false };
    private readonly Button _btnPlay = new() { Text = "Putar Audio", Width = 96, Enabled = false };
    private readonly Label _lblInfo = new()
    {
        Dock = DockStyle.Top,
        Height = 32,
        TextAlign = ContentAlignment.MiddleLeft,
        Padding = new Padding(12, 8, 0, 0),
        Font = new Font("Segoe UI", 10f),
        Text = "Ayat acak dari rentang disamarkan — ingat, lalu buka Jawaban.",
    };
    private readonly RichTextBox _txt = new()
    {
        Dock = DockStyle.Fill,
        ReadOnly = true,
        BorderStyle = BorderStyle.FixedSingle,
        BackColor = Color.White,
        Font = new Font("Segoe UI", 11f),
    };

    private readonly Random _rng = new();
    private readonly List<int> _pool = new();
    private (int Surah, int Ayah)? _current;
    private readonly string _transKey;

    public event Action<int, int>? PlayRequested;
    public event Action<int, int>? GotoRequested;

    public MTestDialog(int currentSurah, int currentAyah, string transKey)
    {
        Text = "Uji Hafalan Rentang (Mushaf Test)";
        ClientSize = new Size(700, 430);
        StartPosition = FormStartPosition.CenterParent;
        MinimumSize = new Size(600, 370);
        _transKey = string.IsNullOrEmpty(transKey) ? "id_indonesian" : transKey;

        for (int s = 1; s <= 114; s++)
        {
            var info = SurahList.Get(s);
            var item = new ComboItem($"{s}. {info.EnglishName}", s);
            _cmbFrom.Items.Add(item);
            _cmbTo.Items.Add(item);
        }
        _cmbFrom.SelectedIndex = Math.Clamp(currentSurah - 1, 0, 113);
        _cmbTo.SelectedIndex = Math.Clamp(currentSurah - 1, 0, 113);
        _numFromA.Value = Math.Clamp(currentAyah, 1, 286);
        _numToA.Value = Math.Clamp(currentAyah + 4, 1, 286);
        _cmbFrom.SelectedIndexChanged += (_, _) => ClampAyahNums();
        _cmbTo.SelectedIndexChanged += (_, _) => ClampAyahNums();
        _cmbFrom.SelectedIndexChanged += (_, _) => SyncAyahMax(_numFromA, _cmbFrom.SelectedIndex + 1);
        _cmbTo.SelectedIndexChanged += (_, _) => SyncAyahMax(_numToA, _cmbTo.SelectedIndex + 1);

        var top = new FlowLayoutPanel
        {
            Dock = DockStyle.Top,
            Height = 42,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = false,
            Padding = new Padding(8, 6, 0, 0),
        };
        top.Controls.Add(new Label { Text = "Dari:", AutoSize = true, Padding = new Padding(0, 8, 0, 0) });
        top.Controls.Add(_cmbFrom);
        top.Controls.Add(new Label { Text = "ayat", AutoSize = true, Padding = new Padding(0, 8, 0, 0) });
        top.Controls.Add(_numFromA);
        top.Controls.Add(new Label { Text = "Ke:", AutoSize = true, Padding = new Padding(8, 8, 0, 0) });
        top.Controls.Add(_cmbTo);
        top.Controls.Add(new Label { Text = "ayat", AutoSize = true, Padding = new Padding(0, 8, 0, 0) });
        top.Controls.Add(_numToA);
        top.Controls.Add(_btnAsk);

        var bottom = new FlowLayoutPanel
        {
            Dock = DockStyle.Bottom,
            Height = 44,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = false,
            Padding = new Padding(8, 8, 0, 0),
        };
        bottom.Controls.Add(_btnReveal);
        bottom.Controls.Add(_btnPlay);
        bottom.Controls.Add(new Label
        {
            Text = "Tampilkan jawaban sebelum mengingat = mengurangi manfaat tes.",
            AutoSize = true,
            Padding = new Padding(10, 10, 0, 0),
            ForeColor = Color.DimGray,
        });

        Controls.Add(_txt);
        Controls.Add(_lblInfo);
        Controls.Add(top);
        Controls.Add(bottom);
        _lblInfo.BringToFront();
        bottom.BringToFront();

        _btnAsk.Click += (_, _) => AskNext();
        _btnReveal.Click += async (_, _) => await RevealAsync();
        _btnPlay.Click += (_, _) =>
        {
            if (_current != null) PlayRequested?.Invoke(_current.Value.Surah, _current.Value.Ayah);
        };
    }

    private void ClampAyahNums()
    {
        if (_numFromA.Value > _numToA.Value) _numToA.Value = _numFromA.Value;
    }

    private static void SyncAyahMax(NumericUpDown num, int surah)
        => num.Maximum = Math.Max(1, QuranData.SurahAyahCount(surah));

    private void AskNext()
    {
        int fromS = Math.Max(1, _cmbFrom.SelectedIndex + 1);
        int toS = Math.Max(fromS, _cmbTo.SelectedIndex + 1);
        int fromA = (int)_numFromA.Value;
        int toA = (int)_numToA.Value;

        int fromId = QuranData.AyaToId(fromS, fromA);
        int toSMax = QuranData.SurahAyahCount(toS);
        if (toA > toSMax) toA = toSMax;
        if (toS == fromS && toA < fromA) toA = fromA;
        int toId = QuranData.AyaToId(toS, toA);

        if (_pool.Count == 0)
        {
            for (int id = fromId; id <= toId; id++) _pool.Add(id);
            if (_pool.Count > 1)
            {
                for (int i = _pool.Count - 1; i > 0; i--)
                {
                    int j = _rng.Next(i + 1);
                    (_pool[i], _pool[j]) = (_pool[i], _pool[j]);
                }
            }
        }
        if (_pool.Count == 0) return;

        int pick = _pool[^1];
        _pool.RemoveAt(_pool.Count - 1);
        var (s, a) = QuranData.IdToAya(pick);
        _current = (s, a);
        _btnReveal.Enabled = true;
        _btnPlay.Enabled = true;

        var info = SurahList.Get(s);
        _lblInfo.Text = $"Soal: tersamar. Petunjuk posisi — QS {s}. {info.EnglishName}, ayat {a}. (sisa {_pool.Count} soal)";
        _txt.Clear();
        _txt.RightToLeft = RightToLeft.No;
        _txt.Font = new Font("Segoe UI", 11f);
        _txt.ForeColor = Color.Gray;
        _txt.AppendText("[Ayat disembunyikan — ingat ayatnya, lalu klik Jawaban.\n" +
                        "Petunjuk: gunakan pilihan Putar Audio untuk mendengarkan ayat.]");

        GotoRequested?.Invoke(s, a);
    }

    private async Task RevealAsync()
    {
        if (_current == null) return;
        var (s, a) = _current.Value;
        try
        {
            string text = MadinahText.Get(s, a) ?? "";
            if (string.IsNullOrWhiteSpace(text))
            {
                var arabic = await ProgramServices.Api.GetSurahTarjamaAsync("ar_ayat", s, CancellationToken.None);
                text = arabic.TryGetValue(a, out var t) ? t : "(teks tidak tersedia)";
            }

            string arti = "";
            if (_transKey != "ar_ayat")
            {
                var map = await ProgramServices.Api.GetSurahTarjamaAsync(_transKey, s, CancellationToken.None);
                arti = map.TryGetValue(a, out var v) ? KsuApi.StripHtml(v) : "";
            }

            var info = SurahList.Get(s);
            _txt.Clear();
            _txt.ForeColor = Color.Black;
            _txt.AppendText($"QS {s}. {info.EnglishName} — Ayat {a}\n\n");
            _txt.RightToLeft = RightToLeft.Yes;
            _txt.Font = MadinahFont.Create(18f);
            _txt.AppendText(text + "\n");
            if (arti.Length > 0)
            {
                _txt.RightToLeft = RightToLeft.No;
                _txt.Font = new Font("Segoe UI", 11f);
                _txt.AppendText("\n" + arti);
            }
        }
        catch (Exception ex)
        {
            _lblInfo.Text = "Gagal memuat teks: " + ex.Message;
        }
    }
}
