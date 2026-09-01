using System.Diagnostics;

namespace QuranDesktop.Controls;

internal sealed class DownloadAllDialog : Form
{
    private readonly CheckBox _chkPages = new() { Text = "Halaman mushaf aktif (604 halaman)", AutoSize = true, Checked = true };
    private readonly CheckBox _chkText = new() { Text = "Teks Arab (Madinah) + terjemahan aktif (114 surah)", AutoSize = true, Checked = true };
    private readonly CheckBox _chkAudio = new() { Text = "Audio Al-Qur'an penuh untuk qari terpilih (6.236 ayat)", AutoSize = true };
    private readonly ComboBox _cmbQaree = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 250, DropDownWidth = 280 };
    private readonly Button _btnStart = new() { Text = "Mulai Unduh Semua", Width = 150 };
    private readonly Button _btnCancel = new() { Text = "Batal", Width = 90, Enabled = false };
    private readonly ProgressBar _bar = new() { Width = 460, Height = 22 };
    private readonly Label _lblStatus = new()
    {
        Text = "",
        AutoSize = true,
        MaximumSize = new Size(460, 0),
    };
    private readonly Label _lblEstimate = new() { AutoSize = true, ForeColor = Color.DimGray, Font = new Font("Segoe UI", 9f) };

    private CancellationTokenSource? _cts;
    private bool _running;

    public DownloadAllDialog(string currentQareeKey, string activeTranslationKey)
    {
        Text = "Unduh Semua — Quran Desktop";
        ClientSize = new Size(490, 250);
        FormBorderStyle = FormBorderStyle.FixedDialog;
        StartPosition = FormStartPosition.CenterParent;
        MaximizeBox = false;

        foreach (var r in Reciters.All)
        {
            _cmbQaree.Items.Add(new ComboItem(r.Display, r));
        }
        var cur = Reciters.Find(currentQareeKey) ?? Reciters.All[0];
        _cmbQaree.SelectedIndex = Reciters.All.IndexOf(cur);

        var flow = new FlowLayoutPanel
        {
            Location = new Point(12, 12),
            Size = new Size(465, 90),
            FlowDirection = FlowDirection.TopDown,
            WrapContents = false,
        };
        flow.Controls.Add(_chkPages);
        flow.Controls.Add(_chkText);
        flow.Controls.Add(_chkAudio);

        var flowQ = new FlowLayoutPanel
        {
            Location = new Point(30, 104),
            Size = new Size(445, 34),
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = false,
        };
        flowQ.Controls.Add(new Label { Text = "Qari:", AutoSize = true, Padding = new Padding(0, 8, 0, 0) });
        flowQ.Controls.Add(_cmbQaree);

        _lblEstimate.Location = new Point(32, 140);
        _bar.Location = new Point(12, 164);
        _btnStart.Location = new Point(12, 194);
        _btnCancel.Location = new Point(170, 194);
        _lblStatus.Location = new Point(12, 224);

        Controls.Add(flow);
        Controls.Add(flowQ);
        Controls.Add(_lblEstimate);
        Controls.Add(_bar);
        Controls.Add(_btnStart);
        Controls.Add(_btnCancel);
        Controls.Add(_lblStatus);

        AcceptButton = _btnStart;
        _chkAudio.CheckedChanged += (_, _) => UpdateEstimate();
        _chkPages.CheckedChanged += (_, _) => UpdateEstimate();
        _chkText.CheckedChanged += (_, _) => UpdateEstimate();
        _cmbQaree.SelectedIndexChanged += (_, _) => UpdateEstimate();
        UpdateEstimate();

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

    private void UpdateEstimate()
    {
        double mb = 0;
        int files = 0;
        if (_chkPages.Checked) { mb += 604 * 0.08; files += 604; }
        if (_chkText.Checked) { mb += 228 * 0.01; files += 228; }
        if (_chkAudio.Checked)
        {
            var r = (Reciter)((ComboItem)_cmbQaree.SelectedItem!).Value!;
            double perFile = r.Folder.Contains("128", StringComparison.OrdinalIgnoreCase) ? 0.28
                : r.Folder.Contains("192", StringComparison.OrdinalIgnoreCase) ? 0.42
                : r.Folder.Contains("64", StringComparison.OrdinalIgnoreCase) ? 0.14
                : 0.10;
            mb += 6236 * perFile;
            files += 6236;
        }
        _lblEstimate.Text = $"Estimasi: {files:N0} file • ±{mb:N0} MB. Yang sudah ada di cache dilewati.";
    }

    private sealed record Job(string Url, string Rel, string Label);

    private async Task StartAsync()
    {
        if (_running) return;
        var mt = MushafTypes.All[0];

        var jobs = new List<DownloadManager.DownloadItem>();
        if (_chkPages.Checked)
        {
            for (int p = 1; p <= 604; p++)
            {
                jobs.Add(new DownloadManager.DownloadItem
                {
                    Label = $"Halaman {p}",
                    Kind = DownloadManager.JobKind.File,
                    Rel = $"mushaf/{mt.Key}/{p}.png",
                    Url = mt.ImageBase + p + ".png",
                    MinBytes = 2048,
                });
            }
        }
        if (_chkText.Checked)
        {
            for (int s = 1; s <= 114; s++)
            {
                jobs.Add(new DownloadManager.DownloadItem { Label = $"Teks Arab surah {s}", Kind = DownloadManager.JobKind.Tarjama, TextKey = "ar_ayat", Surah = s });
                jobs.Add(new DownloadManager.DownloadItem { Label = $"Terjemahan surah {s}", Kind = DownloadManager.JobKind.Tarjama, TextKey = ProgramServices.ActiveTranslationKey ?? "id_indonesian", Surah = s });
            }
        }
        if (_chkAudio.Checked)
        {
            var r = (Reciter)((ComboItem)_cmbQaree.SelectedItem!).Value!;
            jobs.AddRange(DownloadManager.BuildJobs(new DownloadManager.DownloadScope
            {
                Mushaf = false, Hilites = false, Arab = false,
                AudioFolders = new[] { r.Folder },
            }));
        }
        if (jobs.Count == 0) return;

        _running = true;
        _btnStart.Enabled = false;
        _chkPages.Enabled = false;
        _chkText.Enabled = false;
        _chkAudio.Enabled = false;
        _cmbQaree.Enabled = false;
        _btnCancel.Enabled = true;
        _bar.Maximum = jobs.Count;
        _bar.Value = 0;

        _cts = new CancellationTokenSource();
        var ct = _cts.Token;
        var sw = Stopwatch.StartNew();

        var progress = new Progress<DownloadManager.DownloadProgress>(p =>
        {
            _bar.Maximum = Math.Max(1, p.Total);
            _bar.Value = Math.Min(_bar.Maximum, p.Done);
            _lblStatus.Text = $"{p.Done}/{p.Total} — baru {p.Downloaded}, ada {p.Skipped}, gagal {p.Failed}";
        });

        try
        {
            var res = await DownloadManager.Shared.RunAsync(jobs, progress, ct);
            OfflineContentService.Instance.InvalidateAll();
            sw.Stop();
            _lblStatus.Text = res.Cancelled
                ? $"Dibatalkan pada {res.Downloaded + res.Skipped + res.Failed}/{jobs.Count}. Jalankan lagi untuk melanjutkan."
                : res.Failed == 0
                    ? $"Selesai! {res.Downloaded} diunduh, {res.Skipped} sudah ada ({sw.Elapsed.TotalMinutes:0} menit)."
                    : $"Selesai dengan {res.Failed} gagal — ulangi (resume otomatis).";
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
            _chkPages.Enabled = true;
            _chkText.Enabled = true;
            _chkAudio.Enabled = true;
            _cmbQaree.Enabled = true;
            _btnCancel.Enabled = false;
        }
    }
}
