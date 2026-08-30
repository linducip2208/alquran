namespace QuranDesktop.Controls;

internal sealed class BookmarksDialog : Form
{
    private readonly ListBox _lst = new()
    {
        Left = 12,
        Top = 12,
        Width = 420,
        Height = 300,
        Font = new Font("Segoe UI", 10.5f),
        Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right,
    };
    private readonly Button _btnGoto = new() { Text = "Buka Ayat", Left = 12, Width = 100, Top = 320, Anchor = AnchorStyles.Bottom | AnchorStyles.Left };
    private readonly Button _btnDelete = new() { Text = "Hapus", Left = 120, Width = 80, Top = 320, Anchor = AnchorStyles.Bottom | AnchorStyles.Left };
    private readonly Button _btnClose = new() { Text = "Tutup", Left = 352, Width = 80, Top = 320, Anchor = AnchorStyles.Bottom | AnchorStyles.Left };

    public event Action<int, int>? GotoRequested;

    public BookmarksDialog()
    {
        Text = "Bookmark Ayat";
        ClientSize = new Size(446, 360);
        FormBorderStyle = FormBorderStyle.FixedDialog;
        StartPosition = FormStartPosition.CenterParent;
        MaximizeBox = false;

        Controls.Add(_lst);
        Controls.Add(_btnGoto);
        Controls.Add(_btnDelete);
        Controls.Add(_btnClose);
        AcceptButton = _btnGoto;

        Load += (_, _) => Reload();
        _btnGoto.Click += (_, _) => GotoSelected();
        _lst.DoubleClick += (_, _) => GotoSelected();
        _btnDelete.Click += (_, _) => DeleteSelected();
        _btnClose.Click += (_, _) => Close();
    }

    private void Reload()
    {
        _lst.Items.Clear();
        foreach (var bm in ProgressStore.Bookmarks)
        {
            var info = SurahList.Get(bm.S);
            _lst.Items.Add(new ComboItem($"QS {bm.S}. {info.EnglishName} — ayat {bm.A}", (bm.S, bm.A)));
        }
        if (_lst.Items.Count == 0) _lst.Items.Add(new ComboItem("(Belum ada bookmark — klik tombol ★ di aplikasi)", null));
    }

    private void GotoSelected()
    {
        if (_lst.SelectedItem is ComboItem item && item.Value is (int s, int a))
        {
            GotoRequested?.Invoke(s, a);
            Close();
        }
    }

    private void DeleteSelected()
    {
        if (_lst.SelectedItem is ComboItem item && item.Value is (int s, int a))
        {
            ProgressStore.ToggleBookmark(s, a);
            Reload();
        }
    }
}
