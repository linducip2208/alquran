namespace QuranDesktop.Controls;

internal sealed class TextDownloadDialog : Form
{
    private readonly CheckBox _chkArabic = new() { Text = "Teks Arab (Mushaf Madinah)", AutoSize = true, Checked = true };
    private readonly CheckBox _chkTrans = new() { Text = "Terjemahan aktif", AutoSize = true, Checked = true };
    private readonly Button _btnStart = new() { Text = "Mulai Unduh", Width = 110 };
    private readonly Button _btnCancel = new() { Text = "Batal", Width = 80, Enabled = false };
    private readonly ProgressBar _bar = new() { Width = 460, Height = 22 };
    private readonly Label _lblStatus = new()
    {
        Text = "Mengunduh teks & terjemahan 114 surah — setelah ini tampilan teks bekerja offline.",
        AutoSize = true,
        MaximumSize = new Size(460, 0),
    };

    private CancellationTokenSource? _cts;
    private bool _running;
    private readonly string _transKey;

    public TextDownloadDialog(string activeTranslationKey)
    {
        _transKey = string.IsNullOrEmpty(activeTranslationKey) ? "id_indonesian" : activeTranslationKey;

        Text = "Unduh Semua Teks & Terjemahan";
        ClientSize = new Size(484, 180);
        FormBorderStyle = FormBorderStyle.FixedDialog;
        StartPosition = FormStartPosition.CenterParent;
        MaximizeBox = false;

        var flow = new FlowLayoutPanel
        {
            Location = new Point(12, 12),
            Size = new Size(460, 34),
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = false,
        };
        flow.Controls.Add(_chkArabic);
        flow.Controls.Add(_chkTrans);

        _bar.Location = new Point(12, 54);
        _btnStart.Location = new Point(12, 84);
        _btnCancel.Location = new Point(130, 84);
        _lblStatus.Location = new Point(12, 120);

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

        var jobs = new List<(string Key, int Surah)>();
        if (_chkArabic.Checked)
        {
            for (int s = 1; s <= 114; s++) jobs.Add(("ar_ayat", s));
        }
        if (_chkTrans.Checked)
        {
            for (int s = 1; s <= 114; s++) jobs.Add((_transKey, s));
        }
        if (jobs.Count == 0) return;

        _running = true;
        _btnStart.Enabled = false;
        _chkArabic.Enabled = false;
        _chkTrans.Enabled = false;
        _btnCancel.Enabled = true;
        _bar.Value = 0;

        _cts = new CancellationTokenSource();
        var ct = _cts.Token;
        int done = 0, ok = 0, failed = 0;
        int total = jobs.Count;

        try
        {
            var semaphore = new SemaphoreSlim(4);
            var tasks = new List<Task>();
            foreach (var (key, surah) in jobs)
            {
                await semaphore.WaitAsync(ct);
                tasks.Add(Task.Run(async () =>
                {
                    try
                    {
                        var map = await ProgramServices.Api.GetSurahTarjamaAsync(key, surah, ct);
                        if (map.Count > 0) Interlocked.Increment(ref ok);
                        else Interlocked.Increment(ref failed);
                    }
                    catch (OperationCanceledException)
                    {
                    }
                    catch
                    {
                        Interlocked.Increment(ref failed);
                    }
                    finally
                    {
                        semaphore.Release();
                        int d = Interlocked.Increment(ref done);
                        BeginInvoke(new Action(() =>
                        {
                            _bar.Value = Math.Min(100, d * 100 / total);
                            _lblStatus.Text = $"Surah {surah} ({key}) — {d}/{total} (ok {ok}, gagal {failed})";
                        }));
                    }
                }, ct));
            }
            await Task.WhenAll(tasks);

            _lblStatus.Text = ct.IsCancellationRequested
                ? $"Dibatalkan pada {done}/{total}."
                : failed == 0
                    ? $"Selesai! {ok} dataset tersimpan — mode teks siap offline."
                    : $"Selesai dengan {failed} gagal — ulangi untuk melengkapi.";
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
            _chkArabic.Enabled = true;
            _chkTrans.Enabled = true;
            _btnCancel.Enabled = false;
        }
    }
}
