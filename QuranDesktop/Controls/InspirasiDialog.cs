namespace QuranDesktop.Controls;

internal sealed class InspirasiDialog : Form
{
    private readonly ListBox _lstKategori = new() { Dock = DockStyle.Left, Width = 210, Font = new Font("Segoe UI", 10.5f) };
    private readonly ListBox _lstAyat = new() { Dock = DockStyle.Fill, Font = new Font("Segoe UI", 10f), HorizontalScrollbar = true };
    private readonly Button _btnBuka = new() { Text = "Buka Ayat", Width = 96 };
    private readonly Button _btnCard = new() { Text = "Kartu PNG", Width = 92 };
    private readonly Button _btnCopy = new() { Text = "Salin", Width = 68 };
    private readonly Button _btnStar = new() { Text = "★", Width = 40 };
    private readonly Button _btnClose = new() { Text = "Tutup", Width = 68 };
    private readonly Label _lblStatus = new() { AutoSize = true, Padding = new Padding(8, 10, 0, 0), ForeColor = Color.DimGray };

    private const string RabbanaKey = "__rabbana";
    private const string QuickKey = "__quick";

    public event Action<int, int>? GotoRequested;

    public InspirasiDialog()
    {
        Text = "Inspirasi — Ayat Motivasi & Doa";
        ClientSize = new Size(860, 480);
        StartPosition = FormStartPosition.CenterParent;
        MinimumSize = new Size(760, 420);

        var bottom = new FlowLayoutPanel
        {
            Dock = DockStyle.Bottom,
            Height = 48,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = false,
            Padding = new Padding(8, 8, 0, 0),
        };
        bottom.Controls.Add(_btnBuka);
        bottom.Controls.Add(_btnCard);
        bottom.Controls.Add(_btnCopy);
        bottom.Controls.Add(_btnStar);
        bottom.Controls.Add(_btnClose);
        bottom.Controls.Add(_lblStatus);

        var right = new Panel { Dock = DockStyle.Fill };
        right.Controls.Add(_lstAyat);

        Controls.Add(right);
        Controls.Add(_lstKategori);
        Controls.Add(bottom);
        _lstKategori.BringToFront();
        _lstAyat.BringToFront();
        bottom.BringToFront();

        _lstKategori.Items.Add(new ComboItem("✨ Ayat Hari Ini", "__daily"));
        _lstKategori.Items.Add(new ComboItem("📌 Ayat & Doa Pilihan", QuickKey));
        _lstKategori.Items.Add(new ComboItem("🤲 Doa Rabbana (dalam Al-Qur'an)", RabbanaKey));
        foreach (var k in InspirasiContent.Kategori)
        {
            _lstKategori.Items.Add(new ComboItem(k.Judul, k.Key));
        }
        _lstKategori.SelectedIndex = 0;

        _lstKategori.SelectedIndexChanged += (_, _) => LoadSelectedCategory();
        _btnBuka.Click += (_, _) => BukaSelected();
        _lstAyat.DoubleClick += (_, _) => BukaSelected();
        _btnCard.Click += async (_, _) => await KartuSelected();
        _btnCopy.Click += async (_, _) => await CopySelected();
        _btnStar.Click += (_, _) =>
        {
            var sel = CurrentAyah();
            if (sel != null)
            {
                ProgressStore.ToggleBookmark(sel.Value.S, sel.Value.A);
                _lblStatus.Text = ProgressStore.IsBookmarked(sel.Value.S, sel.Value.A)
                    ? $"★ QS {sel.Value.S}:{sel.Value.A} ditandai"
                    : $"Bookmark QS {sel.Value.S}:{sel.Value.A} dihapus";
            }
        };
        _btnClose.Click += (_, _) => Close();
    }

    private void LoadSelectedCategory()
    {
        if (_lstKategori.SelectedItem is not ComboItem item) return;
        string key = (string)item.Value;
        _lstAyat.Items.Clear();

        if (key == "__daily")
        {
            var a = InspirasiContent.AyatHariIni();
            var info = SurahList.Get(a.S);
            _lstAyat.Items.Add(new ComboItem($"\"{a.Label}\" — QS {a.S}. {info.EnglishName}:{a.A}", (a.S, a.A, a.Label)));
            return;
        }

        if (key == QuickKey)
        {
            foreach (var q in InspirasiContent.Quick)
            {
                var info = SurahList.Get(q.S);
                _lstAyat.Items.Add(new ComboItem($"{q.Judul} — QS {q.S}. {info.EnglishName}:{q.A}", (q.S, q.A, q.Judul)));
            }
            return;
        }

        if (key == RabbanaKey)
        {
            foreach (var r in InspirasiContent.Rabbana)
            {
                var info = SurahList.Get(r.S);
                _lstAyat.Items.Add(new ComboItem($"Rabbana — QS {r.S}. {info.EnglishName}:{r.A}", (r.S, r.A, "Rabbana")));
            }
            return;
        }

        var kat = InspirasiContent.Kategori.FirstOrDefault(k => k.Key == key);
        if (kat != null)
        {
            foreach (var a in kat.Ayat)
            {
                var info = SurahList.Get(a.S);
                _lstAyat.Items.Add(new ComboItem($"{a.Label} — QS {a.S}. {info.EnglishName}:{a.A}", (a.S, a.A, a.Label)));
            }
        }
    }

    private (int S, int A, string Label)? CurrentAyah()
        => _lstAyat.SelectedItem is ComboItem item && item.Value is (int s, int a, string l) ? (s, a, l) : null;

    private void BukaSelected()
    {
        var sel = CurrentAyah();
        if (sel != null)
        {
            GotoRequested?.Invoke(sel.Value.S, sel.Value.A);
            Close();
        }
    }

    private async Task<(string Arab, string Arti)> FetchTextAsync(int s, int a)
    {
        var arabic = await ProgramServices.Api.GetSurahTarjamaAsync("ar_ayat", s, CancellationToken.None);
        string arab = arabic.TryGetValue(a, out var av) ? av : "";
        string arti = "";
        var t = ProgramServices.ActiveTranslationKey;
        if (!string.IsNullOrEmpty(t))
        {
            var map = await ProgramServices.Api.GetSurahTarjamaAsync(t, s, CancellationToken.None);
            arti = map.TryGetValue(a, out var tv) ? KsuApi.StripHtml(tv) : "";
        }
        return (arab, arti);
    }

    private async Task KartuSelected()
    {
        var sel = CurrentAyah();
        if (sel == null) return;
        _lblStatus.Text = "Menyiapkan kartu…";
        try
        {
            var (arab, arti) = await FetchTextAsync(sel.Value.S, sel.Value.A);
            using var dlg = new AyahImageDialog(sel.Value.S, sel.Value.A, arab, arti);
            dlg.ShowDialog(this);
            _lblStatus.Text = "";
        }
        catch (Exception ex)
        {
            _lblStatus.Text = "Gagal: " + ex.Message;
        }
    }

    private async Task CopySelected()
    {
        var sel = CurrentAyah();
        if (sel == null) return;
        try
        {
            var (arab, arti) = await FetchTextAsync(sel.Value.S, sel.Value.A);
            var info = SurahList.Get(sel.Value.S);
            Clipboard.SetText($"{arab}\n\n\"{arti}\"\n— QS {sel.Value.S}. {info.EnglishName} [{sel.Value.S}:{sel.Value.A}]");
            _lblStatus.Text = "Tersalin ke clipboard ✓";
        }
        catch (Exception ex)
        {
            _lblStatus.Text = "Gagal: " + ex.Message;
        }
    }
}
