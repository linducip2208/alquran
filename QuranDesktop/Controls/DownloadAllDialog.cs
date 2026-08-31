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

        var jobs = new List<Job>();
        if (_chkPages.Checked)
        {
            for (int p = 1; p <= 604; p++)
            {
                jobs.Add(new Job(mt.ImageBase + p + ".png", Path.Combine("mushaf", mt.Key, p + ".png"), $"Halaman {p}"));
            }
        }
        if (_chkText.Checked)
        {
            for (int s = 1; s <= 114; s++)
            {
                jobs.Add(new Job($"tarjama:ar_ayat:{s}", $"tarjama:ar_ayat:{s}", $"Teks Arab surah {s}"));
                jobs.Add(new Job($"tarjama:{ProgramServices.ActiveTranslationKey ?? "id_indonesian"}:{s}", $"tarjama:{ProgramServices.ActiveTranslationKey ?? "id_indonesian"}:{s}", $"Terjemahan surah {s}"));
            }
        }
        if (_chkAudio.Checked)
        {
            var r = (Reciter)((ComboItem)_cmbQaree.SelectedItem!).Value!;
            for (int s = 1; s <= 114; s++)
            {
                int n = QuranData.SurahAyahCount(s);
                for (int a = 1; a <= n; a++)
                {
                    jobs.Add(new Job(KsuAudio.AyahUrl(r.Folder, s, a), Path.Combine(r.Folder, $"{s:D3}{a:D3}.mp3"), $"Audio {s}:{a}"));
                }
            }
        }
        if (jobs.Count == 0) return;

        _running = true;
        _btnStart.Enabled = false;
        _chkPages.Enabled = false;
        _chkText.Enabled = false;
        _chkAudio.Enabled = false;
        _cmbQaree.Enabled = false;
        _btnCancel.Enabled = true;
        _bar.Value = 0;

        _cts = new CancellationTokenSource();
        var ct = _cts.Token;
        var sw = Stopwatch.StartNew();
        int done = 0, downloaded = 0, skipped = 0, failed = 0;
        int total = jobs.Count;

        try
        {
            var semaphore = new SemaphoreSlim(4);
            var tasks = new List<Task>();
            foreach (var job in jobs)
            {
                await semaphore.WaitAsync(ct);
                tasks.Add(Task.Run(async () =>
                {
                    try
                    {
                        if (job.Url.StartsWith("tarjama:"))
                        {
                            var parts = job.Url.Split(':');
                            string key = parts[1];
                            int surah = int.Parse(parts[2]);
                            var map = await ProgramServices.Api.GetSurahTarjamaAsync(key, surah, ct);
                            if (map.Count > 0) Interlocked.Increment(ref downloaded);
                            else Interlocked.Increment(ref failed);
                        }
                        else
                        {
                            bool existed = File.Exists(KsuAudio.CachePath(job.Rel));
                            if (existed)
                            {
                                Interlocked.Increment(ref skipped);
                            }
                            else
                            {
                                using var resp = await ProgramServices.Http.GetAsync(job.Url, ct);
                                resp.EnsureSuccessStatusCode();
                                Directory.CreateDirectory(Path.GetDirectoryName(KsuAudio.CachePath(job.Rel))!);
                                await using var src = await resp.Content.ReadAsStreamAsync(ct);
                                await using var dst = File.Create(KsuAudio.CachePath(job.Rel));
                                await src.CopyToAsync(dst, ct);
                                Interlocked.Increment(ref downloaded);
                            }
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
                            _bar.Value = Math.Min(100, d * 100 / total);
                            _lblStatus.Text = $"{d}/{total} — baru {downloaded}, ada {skipped}, gagal {failed}";
                        }));
                    }
                }, ct));
            }
            await Task.WhenAll(tasks);

            sw.Stop();
            _lblStatus.Text = ct.IsCancellationRequested
                ? $"Dibatalkan pada {done}/{total}. Jalankan lagi untuk melanjutkan."
                : $"Selesai! {downloaded} diunduh, {skipped} sudah ada, {failed} gagal ({sw.Elapsed.TotalMinutes:0} menit).";
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
