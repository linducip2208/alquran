namespace QuranDesktop.Controls;

/// <summary>
/// (Z) Unduh halaman mushaf — memakai DownloadManager engine yang sama dengan seluruh aplikasi:
/// HTTP → downloads/mushaf/{key}/{page}.png.part → validasi ukuran + signature PNG → atomic move.
/// Halaman yang sudah valid dilewati tanpa request jaringan; resume otomatis dari .part.
/// </summary>
internal sealed class DownloadDialog : Form
{
    private readonly ComboBox _cmbMushaf = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 220, DropDownWidth = 240 };
    private readonly NumericUpDown _numFrom = new() { Minimum = 1, Maximum = 999, Value = 1, Width = 70 };
    private readonly NumericUpDown _numTo = new() { Minimum = 1, Maximum = 999, Value = 604, Width = 70 };
    private readonly Button _btnStart = new() { Text = "Mulai Unduh", Width = 110 };
    private readonly Button _btnCancel = new() { Text = "Batal", Width = 80, Enabled = false };
    private readonly ProgressBar _bar = new() { Width = 440, Height = 22 };
    private readonly Label _lblStatus = new()
    {
        Text = "Halaman yang sudah ada di cache akan dilewati.",
        AutoSize = true,
    };

    private CancellationTokenSource? _cts;
    private bool _running;

    public DownloadDialog(string currentMushafKey)
    {
        Text = "Unduh Halaman Mushaf";
        ClientSize = new Size(480, 190);
        FormBorderStyle = FormBorderStyle.FixedDialog;
        StartPosition = FormStartPosition.CenterParent;
        MaximizeBox = false;
        MinimizeBox = false;

        foreach (var m in MushafTypes.All)
        {
            _cmbMushaf.Items.Add(new ComboItem(m.Display, m));
        }
        var cur = MushafTypes.Find(currentMushafKey) ?? MushafTypes.All[0];
        _cmbMushaf.SelectedIndex = MushafTypes.All.IndexOf(cur);

        var flowTop = new FlowLayoutPanel
        {
            Location = new Point(12, 12),
            Size = new Size(456, 40),
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = false,
        };
        flowTop.Controls.Add(new Label { Text = "Mushaf:", AutoSize = true, Padding = new Padding(0, 8, 0, 0) });
        flowTop.Controls.Add(_cmbMushaf);
        flowTop.Controls.Add(new Label { Text = "Dari:", AutoSize = true, Padding = new Padding(8, 8, 0, 0) });
        flowTop.Controls.Add(_numFrom);
        flowTop.Controls.Add(new Label { Text = "Ke:", AutoSize = true, Padding = new Padding(4, 8, 0, 0) });
        flowTop.Controls.Add(_numTo);

        _bar.Location = new Point(12, 60);
        _btnStart.Location = new Point(12, 92);
        _btnCancel.Location = new Point(130, 92);
        _lblStatus.Location = new Point(12, 130);
        _lblStatus.MaximumSize = new Size(456, 0);

        Controls.Add(flowTop);
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
        var mt = (MushafType)((ComboItem)_cmbMushaf.SelectedItem!).Value!;
        int from = (int)_numFrom.Value;
        int to = (int)_numTo.Value;
        if (to < from) (from, to) = (to, from);
        to = Math.Min(to, QuranData.PageCount(mt.PageKey));

        _running = true;
        _btnStart.Enabled = false;
        _cmbMushaf.Enabled = false;
        _numFrom.Enabled = false;
        _numTo.Enabled = false;
        _btnCancel.Enabled = true;
        _bar.Style = ProgressBarStyle.Marquee;
        _lblStatus.Text = "Menyiapkan daftar unduhan…";

        _cts = new CancellationTokenSource();
        var ct = _cts.Token;

        try
        {
            // (Z) job list dibangun di background; unduhan via DownloadManager (satu engine untuk seluruh aplikasi)
            var jobs = await Task.Run(() => Enumerable.Range(from, to - from + 1).Select(p => new DownloadManager.DownloadItem
            {
                Label = $"Mushaf {mt.Display} hal {p}",
                Kind = DownloadManager.JobKind.File,
                Rel = $"mushaf/{mt.Key}/{p}.png",
                Url = mt.ImageBase + p + ".png",
                MinBytes = 2048,
            }).ToList(), ct);

            _bar.Style = ProgressBarStyle.Continuous;
            var progress = new Progress<DownloadManager.DownloadProgress>(p =>
            {
                _bar.Maximum = Math.Max(1, p.Total);
                _bar.Value = Math.Min(_bar.Maximum, p.Done);
                string file = p.CurrentFileTotal > 0
                    ? $"  •  {Path.GetFileName(p.CurrentFileRel.Replace('\\', '/'))} {p.CurrentFileBytes * 100 / Math.Max(1, p.CurrentFileTotal)}%"
                    : "";
                _lblStatus.Text = $"{p.Done}/{p.Total} — baru {p.Downloaded}, ada {p.Skipped}, gagal {p.Failed}{file}  •  {p.Current}";
            });

            var res = await DownloadManager.Shared.RunAsync(jobs, progress, ct);
            OfflineContentService.Instance.InvalidateAll();
            _lblStatus.Text = res.Cancelled
                ? $"Dibatalkan pada {res.Downloaded + res.Skipped + res.Failed}/{jobs.Count}. Jalankan lagi untuk melanjutkan (resume otomatis)."
                : res.Failed == 0
                    ? $"Selesai! {res.Downloaded} halaman baru diunduh, {res.Skipped} sudah ada."
                    : $"Selesai dengan {res.Failed} gagal — ulangi rentang itu (resume otomatis).";
        }
        catch (OperationCanceledException)
        {
            _lblStatus.Text = "Dibatalkan.";
        }
        catch (Exception ex)
        {
            _lblStatus.Text = "Gagal: " + ex.Message;
        }
        finally
        {
            _running = false;
            _btnStart.Enabled = true;
            _cmbMushaf.Enabled = true;
            _numFrom.Enabled = true;
            _numTo.Enabled = true;
            _btnCancel.Enabled = false;
            _bar.Style = ProgressBarStyle.Continuous;
        }
    }
}
