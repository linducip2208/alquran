namespace QuranDesktop.Controls;

internal sealed class ReminderDialog : Form
{
    private readonly CheckBox _chkEnabled = new() { Text = "Aktifkan pengingat baca harian", Left = 16, Top = 16, AutoSize = true };
    private readonly DateTimePicker _time = new() { Format = DateTimePickerFormat.Time, ShowUpDown = true, Left = 16, Top = 46, Width = 120 };
    private readonly Button _btnSave = new() { Text = "Simpan", Left = 150, Top = 44, Width = 90 };
    private readonly Label _lblNote = new()
    {
        Left = 16,
        Top = 84,
        AutoSize = true,
        MaximumSize = new Size(400, 0),
        Text = "Jika aplikasi terbuka (atau di tray) pada jam tersebut, muncul notifikasi \"waktunya baca\".",
        Font = new Font("Segoe UI", 9f),
        ForeColor = Color.DimGray,
    };

    public bool EnabledReminder => _chkEnabled.Checked;
    public TimeSpan Time => _time.Value.TimeOfDay;

    public ReminderDialog(bool enabled, TimeSpan time)
    {
        Text = "Pengingat Harian";
        ClientSize = new Size(430, 130);
        FormBorderStyle = FormBorderStyle.FixedDialog;
        StartPosition = FormStartPosition.CenterParent;
        MaximizeBox = false;

        _chkEnabled.Checked = enabled;
        _time.Value = DateTime.Today + time;

        Controls.Add(_chkEnabled);
        Controls.Add(_time);
        Controls.Add(_btnSave);
        Controls.Add(_lblNote);
        AcceptButton = _btnSave;
        _btnSave.Click += (_, _) => { DialogResult = DialogResult.OK; Close(); };
    }
}
