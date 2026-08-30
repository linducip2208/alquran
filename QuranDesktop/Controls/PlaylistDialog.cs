namespace QuranDesktop.Controls;

internal sealed record PlaylistEntry(int Surah, string QareeKey, string QareeName);

internal sealed class PlaylistDialog : Form
{
    private readonly ListBox _lst = new()
    {
        Left = 12,
        Top = 12,
        Width = 420,
        Height = 240,
        Font = new Font("Segoe UI", 10.5f),
    };
    private readonly Button _btnAdd = new() { Text = "+ Tambah Surah Aktif", Left = 12, Width = 180, Top = 260 };
    private readonly Button _btnRemove = new() { Text = "Hapus", Left = 200, Width = 80, Top = 260 };
    private readonly Button _btnClear = new() { Text = "Kosongkan", Left = 288, Width = 100, Top = 260 };
    private readonly Button _btnPlay = new() { Text = "▶ Putar Playlist", Left = 12, Width = 180, Top = 300 };
    private readonly Button _btnClose = new() { Text = "Tutup", Left = 352, Width = 100, Top = 300 };

    public List<PlaylistEntry> Entries { get; } = new();

    public event Action<List<PlaylistEntry>>? PlayRequested;

    public PlaylistDialog(int currentSurah, string currentQareeKey, string currentQareeName)
    {
        Text = "Playlist Surah";
        ClientSize = new Size(446, 344);
        FormBorderStyle = FormBorderStyle.FixedDialog;
        StartPosition = FormStartPosition.CenterParent;
        MaximizeBox = false;

        Controls.Add(_lst);
        Controls.Add(_btnAdd);
        Controls.Add(_btnRemove);
        Controls.Add(_btnClear);
        Controls.Add(_btnPlay);
        Controls.Add(_btnClose);
        AcceptButton = _btnPlay;

        _btnAdd.Click += (_, _) =>
        {
            var info = SurahList.Get(currentSurah);
            Entries.Add(new PlaylistEntry(currentSurah, currentQareeKey, currentQareeName));
            _lst.Items.Add($"QS {currentSurah}. {info.EnglishName} — {currentQareeName}");
        };
        _btnRemove.Click += (_, _) =>
        {
            int idx = _lst.SelectedIndex;
            if (idx >= 0 && idx < Entries.Count)
            {
                Entries.RemoveAt(idx);
                _lst.Items.RemoveAt(idx);
            }
        };
        _btnClear.Click += (_, _) =>
        {
            Entries.Clear();
            _lst.Items.Clear();
        };
        _btnPlay.Click += (_, _) =>
        {
            if (Entries.Count > 0)
            {
                PlayRequested?.Invoke(Entries.ToList());
                Close();
            }
        };
        _btnClose.Click += (_, _) => Close();
    }
}
