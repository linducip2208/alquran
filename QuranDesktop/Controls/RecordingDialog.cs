using NAudio.Wave;

namespace QuranDesktop.Controls;

internal sealed class RecordingDialog : Form
{
    private readonly ListBox _lst = new() { Dock = DockStyle.Fill, Font = new Font("Segoe UI", 10f) };
    private readonly Button _btnRecord = new() { Text = "● Rekam", Width = 100 };
    private readonly Button _btnStopRec = new() { Text = "■ Stop", Width = 80, Enabled = false };
    private readonly Button _btnPlay = new() { Text = "▶ Putar", Width = 80 };
    private readonly Button _btnStopPlay = new() { Text = "⏹", Width = 50 };
    private readonly Button _btnDelete = new() { Text = "Hapus", Width = 80 };
    private readonly Label _lblStatus = new() { AutoSize = true, Padding = new Padding(8, 10, 0, 0), ForeColor = Color.DimGray };

    private WaveInEvent? _waveIn;
    private WaveFileWriter? _writer;
    private WaveOutEvent? _waveOut;
    private WaveFileReader? _reader;
    private string? _currentFile;

    public RecordingDialog()
    {
        Text = "Rekam Tilawah";
        ClientSize = new Size(520, 400);
        StartPosition = FormStartPosition.CenterParent;
        MinimumSize = new Size(460, 340);

        var bottom = new FlowLayoutPanel
        {
            Dock = DockStyle.Bottom,
            Height = 70,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = false,
            Padding = new Padding(8, 8, 0, 0),
        };
        bottom.Controls.Add(_btnRecord);
        bottom.Controls.Add(_btnStopRec);
        bottom.Controls.Add(_btnPlay);
        bottom.Controls.Add(_btnStopPlay);
        bottom.Controls.Add(_btnDelete);
        var statusPanel = new Panel { Dock = DockStyle.Bottom, Height = 30 };
        statusPanel.Controls.Add(_lblStatus);

        Controls.Add(_lst);
        Controls.Add(bottom);
        Controls.Add(statusPanel);
        _lst.BringToFront();

        Load += (_, _) => Reload();
        _btnRecord.Click += StartRecord;
        _btnStopRec.Click += StopRecord;
        _btnPlay.Click += PlaySelected;
        _btnStopPlay.Click += (_, _) => { try { _waveOut?.Stop(); } catch { } };
        _btnDelete.Click += DeleteSelected;
        FormClosing += (_, _) =>
        {
            try { _waveIn?.StopRecording(); _writer?.Dispose(); _waveOut?.Dispose(); _reader?.Dispose(); } catch { }
        };
    }

    private static string RecDir => Path.Combine(KsuAudio.CacheDir, "recordings");

    private void Reload()
    {
        _lst.Items.Clear();
        try
        {
            Directory.CreateDirectory(RecDir);
            foreach (var f in Directory.GetFiles(RecDir, "*.wav").OrderByDescending(f => f))
            {
                _lst.Items.Add(Path.GetFileName(f));
            }
        }
        catch
        {
        }
    }

    private void StartRecord(object? sender, EventArgs e)
    {
        try
        {
            Directory.CreateDirectory(RecDir);
            _currentFile = Path.Combine(RecDir, $"tilawah-{DateTime.Now:yyyyMMdd-HHmmss}.wav");
            _waveIn = new WaveInEvent { WaveFormat = new WaveFormat(44100, 1) };
            _writer = new WaveFileWriter(_currentFile, _waveIn.WaveFormat);
            _waveIn.DataAvailable += (_, args) => _writer?.Write(args.Buffer, 0, args.BytesRecorded);
            _waveIn.RecordingStopped += (_, _) =>
            {
                _writer?.Dispose();
                _writer = null;
                _waveIn?.Dispose();
                _waveIn = null;
            };
            _waveIn.StartRecording();
            _btnRecord.Enabled = false;
            _btnStopRec.Enabled = true;
            _lblStatus.Text = "Merekam… (pakai mikrofon, baca tilawah, lalu klik Stop)";
        }
        catch (Exception ex)
        {
            _lblStatus.Text = "Gagal merekam: " + ex.Message + " (cek mikrofon)";
            _btnRecord.Enabled = true;
            _btnStopRec.Enabled = false;
        }
    }

    private void StopRecord(object? sender, EventArgs e)
    {
        try
        {
            _waveIn?.StopRecording();
            _lblStatus.Text = "Rekaman tersimpan";
        }
        catch
        {
        }
        _btnRecord.Enabled = true;
        _btnStopRec.Enabled = false;
        Reload();
    }

    private void PlaySelected(object? sender, EventArgs e)
    {
        if (_lst.SelectedItem is not string name) return;
        try
        {
            _waveOut?.Stop();
            _reader?.Dispose();
            _reader = new WaveFileReader(Path.Combine(RecDir, name));
            _waveOut = new WaveOutEvent();
            _waveOut.Init(_reader);
            _waveOut.Play();
            _lblStatus.Text = "Memutar: " + name;
        }
        catch (Exception ex)
        {
            _lblStatus.Text = "Gagal memutar: " + ex.Message;
        }
    }

    private void DeleteSelected(object? sender, EventArgs e)
    {
        if (_lst.SelectedItem is not string name) return;
        try
        {
            File.Delete(Path.Combine(RecDir, name));
            Reload();
        }
        catch (Exception ex)
        {
            _lblStatus.Text = "Gagal hapus: " + ex.Message;
        }
    }
}
