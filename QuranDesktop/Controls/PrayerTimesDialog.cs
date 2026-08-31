using System.Text.Json;

namespace QuranDesktop.Controls;

internal sealed class PrayerTimesDialog : Form
{
    private readonly TextBox _txtCity = new() { Width = 160 };
    private readonly TextBox _txtCountry = new() { Width = 140 };
    private readonly ComboBox _cmbMethod = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 200, DropDownWidth = 240 };
    private readonly CheckBox _chkNotify = new() { Text = "Notifikasi 10 menit sebelum waktu sholat", AutoSize = true };
    private readonly Button _btnSave = new() { Text = "Simpan & Muat", Width = 130 };
    private readonly Label _lblHijri = new() { AutoSize = true, Padding = new Padding(12, 4, 0, 0), ForeColor = Color.DimGray, Font = new Font("Segoe UI", 9f) };
    private readonly ListView _list = new()
    {
        View = View.Details,
        FullRowSelect = true,
        Dock = DockStyle.Fill,
        Font = new Font("Segoe UI", 10.5f),
    };

    public string City => _txtCity.Text.Trim();
    public string Country => _txtCountry.Text.Trim();
    public int Method => (int)((ComboItem)_cmbMethod.SelectedItem!).Value!;
    public bool NotifyBefore => _chkNotify.Checked;

    public PrayerTimesDialog(string city, string country, int method, bool notify)
    {
        Text = "Jadwal Sholat — AlAdhan";
        ClientSize = new Size(480, 470);
        StartPosition = FormStartPosition.CenterParent;
        MinimumSize = new Size(420, 400);

        _txtCity.Text = city;
        _txtCountry.Text = country;
        _chkNotify.Checked = notify;

        var methods = new (int Id, string Name)[]
        {
            (20, "KEMENAG — Indonesia"),
            (3, "Muslim World League"),
            (4, "Umm al-Qura, Makkah"),
            (5, "Persatuan Mesir"),
            (13, "Diyanet — Turki"),
            (17, "JAKIM — Malaysia"),
            (15, "Moonsighting Committee"),
        };
        foreach (var (id, name) in methods)
        {
            _cmbMethod.Items.Add(new ComboItem(name, id));
        }
        _cmbMethod.SelectedIndex = Math.Max(0, methods.ToList().FindIndex(m => m.Id == method));

        _list.Columns.Add("Waktu", 110);
        _list.Columns.Add("Sholat", 150);

        var top = new FlowLayoutPanel
        {
            Dock = DockStyle.Top,
            Height = 108,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = true,
            Padding = new Padding(10, 8, 0, 0),
        };
        top.Controls.Add(new Label { Text = "Kota:", AutoSize = true, Padding = new Padding(0, 8, 0, 0) });
        top.Controls.Add(_txtCity);
        top.Controls.Add(new Label { Text = "Negara:", AutoSize = true, Padding = new Padding(6, 8, 0, 0) });
        top.Controls.Add(_txtCountry);
        top.Controls.Add(_cmbMethod);
        top.Controls.Add(_chkNotify);
        top.Controls.Add(_btnSave);

        _lblHijri.Dock = DockStyle.Bottom;
        _lblHijri.Height = 28;

        Controls.Add(_list);
        Controls.Add(_lblHijri);
        Controls.Add(top);
        _lblHijri.BringToFront();
        _list.BringToFront();
        AcceptButton = _btnSave;

        _btnSave.Click += (_, _) => LoadTimes();
        Load += async (_, _) => await LoadTimesAsync();
    }

    private void LoadTimes()
    {
        _ = LoadTimesAsync();
    }

    private async Task LoadTimesAsync()
    {
        try
        {
            string url = $"https://api.aladhan.com/v1/timingsByCity?city={Uri.EscapeDataString(City)}"
                + $"&country={Uri.EscapeDataString(Country)}&method={Method}";
            using var resp = await ProgramServices.Http.GetAsync(url, CancellationToken.None);
            resp.EnsureSuccessStatusCode();
            using var doc = await JsonDocument.ParseAsync(await resp.Content.ReadAsStreamAsync(CancellationToken.None));

            var data = doc.RootElement.GetProperty("data");
            var timings = data.GetProperty("timings");

            _list.Items.Clear();
            AddRow(timings, "Fajr", "Subuh");
            AddRow(timings, "Sunrise", "Terbit");
            AddRow(timings, "Dhuhr", "Zuhur");
            AddRow(timings, "Asr", "Asar");
            AddRow(timings, "Maghrib", "Magrib");
            AddRow(timings, "Isha", "Isya");

            if (data.TryGetProperty("date", out var date) && date.TryGetProperty("hijri", out var hijri))
            {
                string day = hijri.TryGetProperty("day", out var dEl) ? dEl.GetString() ?? "" : "";
                string month = hijri.TryGetProperty("month", out var mEl) && mEl.TryGetProperty("en", out var mn) ? mn.GetString() ?? "" : "";
                string year = hijri.TryGetProperty("year", out var yEl) ? yEl.GetString() ?? "" : "";
                _lblHijri.Text = $"Tanggal Hijriah: {day} {month} {year} H";
            }
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, "Gagal memuat jadwal: " + ex.Message, "Jadwal Sholat", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
    }

    private void AddRow(JsonElement timings, string key, string label)
    {
        if (timings.TryGetProperty(key, out var v))
        {
            string time = v.GetString() ?? "";
            if (time.Length >= 5) time = time[..5];
            _list.Items.Add(new ListViewItem(new[] { time, label }));
        }
    }
}
