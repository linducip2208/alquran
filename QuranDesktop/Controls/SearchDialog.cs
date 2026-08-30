namespace QuranDesktop.Controls;

internal sealed class SearchDialog : Form
{
    private readonly TextBox _txtQuery = new() { Width = 320, Left = 12, Top = 12 };
    private readonly Button _btnSearch = new() { Text = "Cari", Left = 342, Top = 10, Width = 80 };
    private readonly ListBox _lst = new()
    {
        Left = 12,
        Top = 48,
        Width = 760,
        Height = 420,
        Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right,
        Font = new Font("Segoe UI", 10f),
        HorizontalScrollbar = true,
    };
    private readonly Label _lblStatus = new()
    {
        Left = 12,
        Top = 472,
        AutoSize = true,
        Anchor = AnchorStyles.Bottom | AnchorStyles.Left,
        Text = "Ketik kata (min. 2 huruf), lalu Enter.",
    };

    public (int Surah, int Ayah)? Selected { get; private set; }

    public SearchDialog(string? initialQuery)
    {
        Text = "Pencarian Al-Qur'an — KSU";
        ClientSize = new Size(784, 500);
        StartPosition = FormStartPosition.CenterParent;
        FormBorderStyle = FormBorderStyle.Sizable;
        MaximizeBox = true;

        Controls.Add(_txtQuery);
        Controls.Add(_btnSearch);
        Controls.Add(_lst);
        Controls.Add(_lblStatus);
        AcceptButton = _btnSearch;

        if (!string.IsNullOrWhiteSpace(initialQuery)) _txtQuery.Text = initialQuery;

        _btnSearch.Click += async (_, _) => await DoSearchAsync();
        _lst.DoubleClick += (_, _) => Confirm();
        _txtQuery.KeyDown += async (_, e) =>
        {
            if (e.KeyCode == Keys.Enter)
            {
                e.SuppressKeyPress = true;
                await DoSearchAsync();
            }
        };
    }

    private async Task DoSearchAsync()
    {
        string q = _txtQuery.Text.Trim();
        if (q.Length < 2)
        {
            _lblStatus.Text = "Minimal 2 huruf.";
            return;
        }

        _btnSearch.Enabled = false;
        _lst.Items.Clear();
        _lblStatus.Text = "Mencari…";
        try
        {
            var results = await ProgramServices.Api.SearchAsync(q, CancellationToken.None);
            foreach (var r in results)
            {
                var info = SurahList.Get(r.Surah);
                string text = KsuApi.StripHtml(r.Text).Replace('\n', ' ');
                _lst.Items.Add(new ComboItem(
                    $"[{r.Surah}:{r.Ayah}] {info.EnglishName} — {text}",
                    (r.Surah, r.Ayah)));
            }
            _lblStatus.Text = results.Count == 0 ? "Tidak ada hasil." : $"Ditemukan {results.Count} ayat — klik ganda untuk membuka.";
        }
        catch (Exception ex)
        {
            _lblStatus.Text = "Gagal: " + ex.Message;
        }
        finally
        {
            _btnSearch.Enabled = true;
        }
    }

    private void Confirm()
    {
        if (_lst.SelectedItem is ComboItem item && item.Value is (int s, int a))
        {
            Selected = (s, a);
            DialogResult = DialogResult.OK;
            Close();
        }
    }
}
