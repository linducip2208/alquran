namespace QuranDesktop.Controls;

internal sealed class AudioDownloadDialog : Form
{
    private readonly ComboBox _cmbQaree = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 240, DropDownWidth = 270 };
    private readonly ComboBox _cmbSurah = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 300, DropDownWidth = 340 };
    private readonly Button _btnStart = new() { Text = "Mulai Unduh", Width = 110 };
    private readonly Button _btnCancel = new() { Text = "Batal", Width = 80, Enabled = false };
    private readonly ProgressBar _bar = new() { Width = 460, Height = 22 };
    private readonly Label _lblStatus = new()
    {
        Text = "File yang sudah ada di cache akan dilewati.",
        AutoSize = true,
        MaximumSize = new Size(460, 0),
    };

    private CancellationTokenSource? _cts;
    private bool _running;

    public AudioDownloadDialog(string currentQareeKey, int currentSurah)
    {
        Text = "Unduh Audio Satu Surah";
        ClientSize = new Size(484, 170);
        FormBorderStyle = FormBorderStyle.FixedDialog;
        StartPosition = FormStartPosition.CenterParent;
        MaximizeBox = false;

        foreach (var r in Reciters.All)
        {
            _cmbQaree.Items.Add(new ComboItem(r.Display, r));
        }
        var cur = Reciters.Find(currentQareeKey) ?? Reciters.All[0];
        _cmbQaree.SelectedIndex = Reciters.All.IndexOf(cur);

        foreach (var s in SurahList.All)
        {
            _cmbSurah.Items.Add(new ComboItem($"{s.Number}. {s.EnglishName} ({s.AyahCount} ayat)", s.Number));
        }
        _cmbSurah.SelectedIndex = Math.Clamp(currentSurah - 1, 0, 113);

        var flow = new FlowLayoutPanel
        {
            Location = new Point(12, 12),
            Size = new Size(460, 40),
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = false,
        };
        flow.Controls.Add(new Label { Text = "Qari:", AutoSize = true, Padding = new Padding(0, 8, 0, 0) });
        flow.Controls.Add(_cmbQaree);
        flow.Controls.Add(new Label { Text = "Surah:", AutoSize = true, Padding = new Padding(6, 8, 0, 0) });
        flow.Controls.Add(_cmbSurah);

        _bar.Location = new Point(12, 60);
        _btnStart.Location = new Point(12, 90);
        _btnCancel.Location = new Point(130, 90);
        _lblStatus.Location = new Point(12, 126);

        Controls.Add(flow);
        Controls.Add(_bar);
        Controls.Add(_btnStart);
        Controls.Add(_btnCancel);
        Controls.Add(_lblStatus);

        AcceptButton = _btnStart;
        _btnStart.Click += async (_, _) => await StartAsync();
        _btnCancel.Click += (_, _) => _cts?.Cancel();
        FormClosing += (_, e) =>
        {
            if (_running)
            {
                _cts?.Cancel();
                e.Cancel = true;
            }
        };
    }

    private async Task StartAsync()
    {
        if (_running) return;
        var reciter = (Reciter)((ComboItem)_cmbQaree.SelectedItem!).Value!;
        int surah = (int)((ComboItem)_cmbSurah.SelectedItem!).Value!;
        int count = QuranData.SurahAyahCount(surah);

        _running = true;
        _btnStart.Enabled = false;
        _cmbQaree.Enabled = false;
        _cmbSurah.Enabled = false;
        _btnCancel.Enabled = true;
        _bar.Maximum = count;
        _bar.Value = 0;

        _cts = new CancellationTokenSource();
        var ct = _cts.Token;
        int downloaded = 0, skipped = 0, failed = 0;

        var jobs = DownloadManager.BuildJobs(new DownloadManager.DownloadScope
        {
            Mushaf = false, Hilites = false, Arab = false,
            AudioFolders = new[] { reciter.Folder },
            Surahs = new[] { surah },
        });
        var progress = new Progress<DownloadManager.DownloadProgress>(p =>
        {
            _bar.Maximum = Math.Max(1, p.Total);
            _bar.Value = Math.Min(_bar.Maximum, p.Done);
            downloaded = p.Downloaded; skipped = p.Skipped; failed = p.Failed;
            _lblStatus.Text = $"{p.Done}/{p.Total} — baru {p.Downloaded}, ada {p.Skipped}, gagal {p.Failed}";
        });

        try
        {
            var res = await DownloadManager.Shared.RunAsync(jobs, progress, ct);
            OfflineContentService.Instance.ClearReciterAudioCache();
            _lblStatus.Text = res.Cancelled
                ? $"Dibatalkan pada {res.Downloaded + res.Skipped + res.Failed}/{count}."
                : res.Failed == 0
                    ? $"Selesai! {res.Downloaded} file baru, {res.Skipped} sudah ada."
                    : $"Selesai dengan {res.Failed} gagal — ulangi untuk melengkapi (resume otomatis).";
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            _lblStatus.Text = "Gagal: " + ex.Message;
        }
        finally
        {
            _running = false;
            _btnStart.Enabled = true;
            _cmbQaree.Enabled = true;
            _cmbSurah.Enabled = true;
            _btnCancel.Enabled = false;
        }
    }
}
