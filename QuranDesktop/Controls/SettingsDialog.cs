namespace QuranDesktop.Controls;

internal sealed class SettingsDialog : Form
{
    public SettingsDialog(params (string Group, (string? Label, Control C)[] Items)[] groups)
    {
        Text = "Pengaturan — Quran Desktop";
        ClientSize = new Size(520, 620);
        StartPosition = FormStartPosition.CenterParent;
        MinimumSize = new Size(460, 420);

        var scroll = new Panel
        {
            Dock = DockStyle.Fill,
            AutoScroll = true,
            Padding = new Padding(10),
        };

        foreach (var (group, items) in groups)
        {
            var box = new GroupBox
            {
                Text = group,
                Width = 470,
                Padding = new Padding(10, 6, 10, 8),
                Margin = new Padding(0, 0, 0, 10),
                AutoSize = true,
            };
            var flow = new FlowLayoutPanel
            {
                Dock = DockStyle.Fill,
                FlowDirection = FlowDirection.TopDown,
                WrapContents = false,
                AutoSize = true,
            };
            foreach (var (label, control) in items)
            {
                if (!string.IsNullOrEmpty(label))
                {
                    var lbl = new Label
                    {
                        Text = label,
                        AutoSize = true,
                        Margin = new Padding(0, 6, 0, 2),
                        ForeColor = Color.DimGray,
                    };
                    flow.Controls.Add(lbl);
                }
                control.Margin = new Padding(0, 2, 0, 4);
                flow.Controls.Add(control);
            }
            box.Controls.Add(flow);
            scroll.Controls.Add(box);
        }

        var note = new Label
        {
            Dock = DockStyle.Bottom,
            Height = 30,
            TextAlign = ContentAlignment.MiddleLeft,
            Padding = new Padding(12, 6, 0, 0),
            ForeColor = Color.DimGray,
            Text = "Semua perubahan langsung tersimpan. Tutup jendela ini untuk kembali.",
        };

        Controls.Add(scroll);
        Controls.Add(note);
        scroll.BringToFront();

        FormClosing += (_, e) =>
        {
            if (e.CloseReason == CloseReason.UserClosing)
            {
                e.Cancel = true;
                Hide();
            }
        };
    }

    public void ApplyDark(bool dark)
    {
        BackColor = dark ? Color.FromArgb(30, 30, 34) : SystemColors.Control;
        foreach (Control c in Controls)
        {
            if (c is Panel p) p.BackColor = BackColor;
        }
        ForeColor = dark ? Color.Gainsboro : SystemColors.ControlText;
    }
}
