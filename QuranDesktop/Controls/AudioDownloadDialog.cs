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
        _bar.Value = 0;

        _cts = new CancellationTokenSource();
        var ct = _cts.Token;
        int done = 0, downloaded = 0, skipped = 0, failed = 0;

        try
        {
            var semaphore = new SemaphoreSlim(4);
            var tasks = new List<Task>();
            for (int a = 1; a <= count; a++)
            {
                int ayah = a;
                await semaphore.WaitAsync(ct);
                tasks.Add(Task.Run(async () =>
                {
                    try
                    {
                        string rel = Path.Combine(reciter.Folder, $"{surah:D3}{ayah:D3}.mp3");
                        bool existed = File.Exists(KsuAudio.CachePath(rel));
                        string url = KsuAudio.AyahUrl(reciter.Folder, surah, ayah);
                        using var resp = await ProgramServices.Http.GetAsync(url, ct);
                        resp.EnsureSuccessStatusCode();
                        if (!existed)
                        {
                            Directory.CreateDirectory(Path.GetDirectoryName(KsuAudio.CachePath(rel))!);
                            await using var src = await resp.Content.ReadAsStreamAsync(ct);
                            await using var dst = File.Create(KsuAudio.CachePath(rel));
                            await src.CopyToAsync(dst, ct);
                            Interlocked.Increment(ref downloaded);
                        }
                        else
                        {
                            Interlocked.Increment(ref skipped);
                        }
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
                            _bar.Value = Math.Min(100, d * 100 / count);
                            _lblStatus.Text = $"Ayat {ayah} — {d}/{count} (baru {downloaded}, ada {skipped}, gagal {failed})";
                        }));
                    }
                }, ct));
            }
            await Task.WhenAll(tasks);

            _lblStatus.Text = ct.IsCancellationRequested
                ? $"Dibatalkan pada {done}/{count}."
                : failed == 0
                    ? $"Selesai! {downloaded} file baru, {skipped} sudah ada."
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
            _cmbQaree.Enabled = true;
            _cmbSurah.Enabled = true;
            _btnCancel.Enabled = false;
        }
    }
}
