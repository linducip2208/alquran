using System.Diagnostics;

namespace QuranDesktop.Controls;

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
        _bar.Value = 0;

        _cts = new CancellationTokenSource();
        var ct = _cts.Token;

        int total = to - from + 1;
        int done = 0, downloaded = 0, skipped = 0, failed = 0;
        var failedPages = new List<int>();
        var sw = Stopwatch.StartNew();

        try
        {
            var semaphore = new SemaphoreSlim(4);
            var tasks = new List<Task>();
            for (int p = from; p <= to; p++)
            {
                int page = p;
                await semaphore.WaitAsync(ct);
                tasks.Add(Task.Run(async () =>
                {
                    try
                    {
                        bool existed = File.Exists(Path.Combine(KsuAudio.CacheDir, "mushaf", mt.Key, page + ".png"));
                        await KsuAudio.EnsureMushafPageAsync(mt.Key, page, ProgramServices.Http, ct);
                        if (existed) Interlocked.Increment(ref skipped);
                        else Interlocked.Increment(ref downloaded);
                    }
                    catch (OperationCanceledException)
                    {
                    }
                    catch
                    {
                        Interlocked.Increment(ref failed);
                        lock (failedPages)
                        {
                            failedPages.Add(page);
                        }
                    }
                    finally
                    {
                        semaphore.Release();
                        int d = Interlocked.Increment(ref done);
                        BeginInvoke(new Action(() =>
                        {
                            _bar.Value = Math.Min(100, d * 100 / total);
                            _lblStatus.Text = $"Hal {page} — {d}/{total} (baru {downloaded}, ada {skipped}, gagal {failed})";
                        }));
                    }
                }, ct));
            }
            await Task.WhenAll(tasks);

            sw.Stop();
            if (ct.IsCancellationRequested)
            {
                _lblStatus.Text = $"Dibatalkan pada {done}/{total} (baru {downloaded}).";
            }
            else if (failed == 0)
            {
                _lblStatus.Text = $"Selesai! {downloaded} halaman baru diunduh, {skipped} sudah ada. ({sw.Elapsed.TotalSeconds:0} detik)";
            }
            else
            {
                var sample = string.Join(", ", failedPages.Take(8));
                _lblStatus.Text = $"Selesai dengan {failed} gagal: hal {sample}… Coba ulangi rentang itu.";
            }
        }
        catch (OperationCanceledException)
        {
            _lblStatus.Text = $"Dibatalkan pada {done}/{total}.";
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
        }
    }
}
