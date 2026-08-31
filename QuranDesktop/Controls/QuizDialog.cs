namespace QuranDesktop.Controls;

internal sealed class QuizDialog : Form
{
    private readonly Label _lblHeader = new()
    {
        Dock = DockStyle.Top,
        Height = 34,
        TextAlign = ContentAlignment.MiddleLeft,
        Padding = new Padding(12, 8, 0, 0),
        Font = new Font("Segoe UI", 10.5f, FontStyle.Bold),
        Text = "Kuis: lanjutannya ayat mana?",
    };
    private readonly Label _lblAya = new()
    {
        Dock = DockStyle.Fill,
        TextAlign = ContentAlignment.MiddleCenter,
        RightToLeft = RightToLeft.Yes,
        Font = new Font("Traditional Arabic", 20f),
        Padding = new Padding(12),
        Text = "Memuat…",
    };
    private readonly FlowLayoutPanel _options = new()
    {
        Dock = DockStyle.Bottom,
        Height = 150,
        FlowDirection = FlowDirection.TopDown,
        WrapContents = false,
        Padding = new Padding(10),
    };

    private Dictionary<int, string> _ayat = new();
    private int _curAya;
    private int _score;
    private int _asked;
    private int _surah;
    private readonly Random _rng = new();
    private bool _busy;

    public QuizDialog(int surah)
    {
        _surah = surah;
        Text = "Kuis Hafalan — Lanjutannya?";
        ClientSize = new Size(620, 470);
        StartPosition = FormStartPosition.CenterParent;
        MinimumSize = new Size(560, 420);

        Controls.Add(_lblAya);
        Controls.Add(_options);
        Controls.Add(_lblHeader);
        _lblHeader.BringToFront();

        var info = SurahList.Get(surah);
        _lblHeader.Text = $"Kuis — Surah {surah}. {info.EnglishName} — Skor: 0/0";
        _ = LoadRoundAsync(surah, 1);
    }

    private async Task LoadRoundAsync(int surah, int fromAya)
    {
        if (_busy) return;
        _busy = true;
        _lblAya.Text = "Memuat…";
        try
        {
            _ayat = MadinahText.GetSurah(surah);
            if (!MadinahText.Available || _ayat.Values.All(string.IsNullOrWhiteSpace))
            {
                _ayat = await ProgramServices.Api.GetSurahTarjamaAsync("ar_ayat", surah, CancellationToken.None);
            }
            _surah = surah;
            SetRound(fromAya);
        }
        catch (Exception ex)
        {
            _lblAya.Text = "Gagal memuat: " + ex.Message;
        }
        finally
        {
            _busy = false;
        }
    }

    private void SetRound(int aya)
    {
        int count = _ayat.Count;
        if (aya >= count)
        {
            _lblAya.Text = "Selesai — surah ini habis. Tutup dan pilih surah lain.";
            return;
        }
        _curAya = aya;
        _ayat.TryGetValue(aya, out var text);
        _lblAya.Text = text ?? "";

        int answer = aya + 1;
        var wrongs = new List<int>();
        while (wrongs.Count < 2)
        {
            int w = _rng.Next(1, count + 1);
            if (w != answer && w != aya && !wrongs.Contains(w)) wrongs.Add(w);
        }

        var opts = new List<(int aya, string text)> { (answer, _ayat.TryGetValue(answer, out var t1) ? t1 : "") };
        foreach (var w in wrongs)
        {
            opts.Add((w, _ayat.TryGetValue(w, out var t2) ? t2 : ""));
        }
        opts = opts.OrderBy(_ => _rng.Next()).ToList();

        _options.Controls.Clear();
        foreach (var (a, t) in opts)
        {
            int ayaOpt = a;
            var b = new Button
            {
                Text = (t.Length > 90 ? t[..90] + "…" : t),
                Width = 580,
                Height = 40,
                RightToLeft = RightToLeft.Yes,
                TextAlign = ContentAlignment.MiddleRight,
                Font = MadinahFont.Create(15f),
            };
            b.Click += (_, _) =>
            {
                _asked++;
                bool correct = ayaOpt == _curAya + 1;
                if (correct) _score++;
                var info = SurahList.Get(_surah);
                _lblHeader.Text = $"Kuis — Surah {_surah}. {info.EnglishName} — Skor: {_score}/{_asked}" + (correct ? "  ✓" : "  ✗ benar: ayat " + (_curAya + 1));
                SetRound(_curAya + 1);
            };
            _options.Controls.Add(b);
        }
    }
}
