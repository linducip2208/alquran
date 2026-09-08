namespace QuranDesktop.Controls;

/// <summary>
/// Dialog migrasi "Cache lama ditemukan" — memindahkan konten download dari
/// %LOCALAPPDATA%\QuranDesktop\downloads ke folder downloads di samping EXE,
/// dengan progress live (file + bytes + path aktif) dan tombol Batal.
/// Batal: file yang sudah pindah tetap aman, source tersisa, migrasi dapat dilanjutkan.
/// </summary>
internal sealed class MigrationDialog : Form
{
    private readonly Label _lblTitle = new()
    {
        Text = "Cache lama ditemukan",
        Font = new Font("Segoe UI", 13f, FontStyle.Bold),
        AutoSize = true,
        Padding = new Padding(6, 10, 0, 0),
    };
    private readonly Label _lblInfo = new()
    {
        AutoSize = true,
        Padding = new Padding(8, 4, 8, 0),
        ForeColor = Color.DimGray,
    };
    private readonly Label _lblFrom = new() { AutoSize = true, Padding = new Padding(8, 2, 0, 0) };
    private readonly Label _lblTo = new() { AutoSize = true, Padding = new Padding(8, 2, 0, 0) };
    private readonly ProgressBar _bar = new() { Dock = DockStyle.Top, Height = 20, Margin = new Padding(8, 8, 8, 0) };
    private readonly Label _lblStats = new()
    {
        Dock = DockStyle.Top,
        Height = 26,
        TextAlign = ContentAlignment.MiddleLeft,
        Padding = new Padding(8, 4, 0, 0),
    };
    private readonly Label _lblFile = new()
    {
        Dock = DockStyle.Top,
        Height = 24,
        TextAlign = ContentAlignment.MiddleLeft,
        Padding = new Padding(8, 0, 0, 0),
        ForeColor = Color.DimGray,
        AutoEllipsis = true,
    };
    private readonly Button _btnCancel = new() { Text = "Batal", Width = 90, DialogResult = DialogResult.None };
    private readonly Button _btnClose = new() { Text = "Tutup", Width = 90, Enabled = false };

    private readonly CancellationTokenSource _cts = new();
    private readonly bool _force;
    private (int Files, long Bytes, bool Cancelled)? _result;

    public MigrationDialog(bool force = false)
    {
        _force = force;
        Text = force ? "Impor Cache Lama dari AppData" : "Migrasi Cache Lama";
        ClientSize = new Size(620, 250);
        FormBorderStyle = FormBorderStyle.FixedDialog;
        StartPosition = FormStartPosition.CenterParent;
        MaximizeBox = false;
        MinimizeBox = false;
        ControlBox = false; // wajib lewat Batal/Tutup agar token cancel terkelola

        var info = OfflineMigrator.InspectLegacy();
        _lblInfo.Text = force
            ? "Pindai ulang cache lama di AppData dan impor berkas yang belum ada di folder offline aktif."
            : "Quran Desktop menemukan konten offline dari versi sebelumnya. Isi akan dipindahkan ke folder downloads di samping aplikasi — Anda TIDAK perlu mengunduh ulang.";
        _lblFrom.Text = "Lokasi lama: " + info.SourcePath;
        _lblTo.Text = "Memindahkan ke: " + KsuAudio.DataRoot;
        _lblStats.Text = "Menyiapkan…";

        var header = new FlowLayoutPanel
        {
            Dock = DockStyle.Top,
            FlowDirection = FlowDirection.TopDown,
            WrapContents = false,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            Height = 130,
        };
        header.Controls.AddRange(new Control[] { _lblTitle, _lblInfo, _lblFrom, _lblTo });

        var bottom = new FlowLayoutPanel
        {
            Dock = DockStyle.Bottom,
            FlowDirection = FlowDirection.LeftToRight,
            Height = 44,
            Padding = new Padding(8, 6, 0, 0),
        };
        bottom.Controls.Add(_btnCancel);
        bottom.Controls.Add(_btnClose);

        Controls.Add(_lblFile);
        Controls.Add(_lblStats);
        Controls.Add(_bar);
        Controls.Add(header);
        Controls.Add(bottom);
        _lblFile.BringToFront();
        _lblStats.BringToFront();

        _btnCancel.Click += (_, _) =>
        {
            _btnCancel.Enabled = false;
            _lblStats.Text = "Membatalkan… (file yang sudah dipindah tetap aman)";
            OfflineMigrator.Cancel();
        };
        _btnClose.Click += (_, _) => Close();
        Shown += async (_, _) => await RunAsync();
    }

    private async Task RunAsync()
    {
        var progress = new Progress<MigrationProgress>(p =>
        {
            _bar.Value = Math.Clamp(p.Percent, 0, 100);
            _lblStats.Text = $"{p.FilesDone:N0} / {p.FilesTotal:N0} file — {FormatSize(p.BytesDone)} / {FormatSize(p.BytesTotal)} — {p.Percent}%";
            _lblFile.Text = p.CurrentRelativePath;
        });

        try
        {
            var res = await Task.Run(() => OfflineMigrator.Run(progress: progress, force: _force, ct: _cts.Token));
            _result = (res.FilesMoved, res.BytesMoved, res.Cancelled);
            _lblFile.Text = "";
            _lblStats.Text = res.Cancelled
                ? $"Dibatalkan — {res.FilesMoved:N0} file sudah dipindah. Migrasi dapat dilanjutkan nanti."
                : $"Selesai — {res.FilesMoved:N0} file dipindah ({FormatSize(res.BytesMoved)}). Tidak perlu unduh ulang.";
            _btnClose.Enabled = true;
            if (!res.Cancelled)
            {
                // auto-tutup bila sukses (migrasi mulus tidak perlu diklik)
                await Task.Delay(900);
                DialogResult = DialogResult.OK;
            }
        }
        catch (OperationCanceledException)
        {
            _result = (0, 0, true);
            _btnClose.Enabled = true;
        }
    }

    private static string FormatSize(long bytes)
        => bytes >= 1L << 30 ? $"{bytes / (double)(1L << 30):0.00} GB"
            : bytes >= 1L << 20 ? $"{bytes / (double)(1L << 20):0.0} MB"
            : bytes >= 1L << 10 ? $"{bytes / (double)(1L << 10):0} KB"
            : $"{bytes} B";
}
