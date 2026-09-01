using System.Text;

namespace QuranDesktop.Controls;

/// <summary>
/// Pusat Unduhan &amp; Konten Offline — status seluruh resource (mushaf, hilite, teks Arab,
/// terjemahan, tafsir, audio qari, voice translation) + unduh hanya-yang-kurang + storage manager.
/// Semua scan berjalan di background; semua unduhan memakai DownloadManager.
/// Catatan model cache: teks Arab/terjemahan disimpan per surah (JSON), tafsir &amp; hilite per ayat/halaman,
/// audio per ayat — UI menjelaskan hal ini agar unduhan "per ayat" tidak mengejutkan pengguna.
/// </summary>
internal sealed class DownloadCenterDialog : Form
{
    private readonly string _mushafKey;
    private readonly string _transKey;
    private readonly string _tafsirKey;
    private readonly string _qareeKey;
    private readonly int _gotoSurah;
    private readonly int _gotoAyah;

    private readonly TabControl _tabs = new() { Dock = DockStyle.Fill };
    private readonly DataGridView _gridSurah = CreateGrid();
    private readonly DataGridView _gridAyat = CreateGrid();
    private readonly DataGridView _gridQari = CreateGrid();
    private readonly DataGridView _gridQariSurah = CreateGrid();
    private readonly DataGridView _gridStorage = CreateGrid();

    private readonly ComboBox _cmbFilterSurahStatus = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 130 };
    private readonly TextBox _txtSearchSurah = new() { Width = 180, PlaceholderText = "Cari surah…" };
    private readonly ComboBox _cmbAyatSurah = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 200, DropDownWidth = 320 };
    private readonly ComboBox _cmbAyatQari = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 190, DropDownWidth = 260 };
    private readonly ComboBox _cmbAyatTrans = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 190, DropDownWidth = 260 };
    private readonly ComboBox _cmbAyatTafsir = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 190, DropDownWidth = 260 };

    private readonly ComboBox _cmbProfile = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 200 };
    private readonly CheckBox _chkMushaf = new() { Text = "Mushaf aktif", AutoSize = true, Checked = true };
    private readonly CheckBox _chkHilite = new() { Text = "Hilite ayat", AutoSize = true, Checked = true };
    private readonly CheckBox _chkArab = new() { Text = "Teks Arab", AutoSize = true, Checked = true };
    private readonly CheckBox _chkTrans = new() { Text = "Terjemahan aktif", AutoSize = true, Checked = true };
    private readonly CheckBox _chkTafsir = new() { Text = "Tafsir aktif", AutoSize = true };
    private readonly CheckBox _chkAudio = new() { Text = "Audio qari aktif", AutoSize = true };
    private readonly ComboBox _cmbProfileQari = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 210, DropDownWidth = 280 };

    private readonly ComboBox _cmbDelResource = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 260, DropDownWidth = 300 };

    private readonly RichTextBox _detail = new()
    {
        ReadOnly = true,
        BorderStyle = BorderStyle.None,
        Font = new Font("Consolas", 9.5f),
        Dock = DockStyle.Fill,
        BackColor = Color.White,
    };

    private readonly ProgressBar _bar = new() { Dock = DockStyle.Fill, Height = 20 };
    private readonly Label _lblProgress = new() { AutoSize = true, Text = "Siap." };
    private readonly Button _btnCancelJobs = new() { Text = "Batal", Width = 80, Enabled = false };

    private CancellationTokenSource? _scanCts;
    private CancellationTokenSource? _jobCts;
    private bool _running;
    private bool _scanning;

    private SurahOfflineSummary[] _surahRows = Array.Empty<SurahOfflineSummary>();
    private List<AyahRow> _filteredAyat = new();
    private List<ReciterSummary> _qariRows = new();
    private List<StorageItem> _storageRows = new();
    private (string Kind, string Key, string Display, string Folder)[] _delResources = Array.Empty<(string, string, string, string)>();

    public event Action<int, int>? GotoRequested;

    private sealed record AyahRow(int S, int A, int Page, bool Mushaf, bool Hilite, bool Arab, bool Trans, bool Tafsir, bool Audio)
    {
        public static string Sym(bool b) => b ? "✓" : "—";

        public bool Complete => Mushaf && Hilite && Arab && Trans && Tafsir && Audio;
    }

    /// <summary>Card ringkasan: border tipis, judul kecil di atas, nilai besar di bawah, tint status lembut.</summary>
    private sealed class CardPanel : Panel
    {
        private readonly Label _title = new()
        {
            Dock = DockStyle.Top,
            Height = 20,
            Font = new Font("Segoe UI", 8.5f),
            ForeColor = Color.FromArgb(120, 120, 125),
            Padding = new Padding(1, 2, 0, 0),
            BackColor = Color.Transparent,
        };
        private readonly Label _value = new()
        {
            Dock = DockStyle.Fill,
            Font = new Font("Segoe UI", 10.5f, FontStyle.Bold),
            ForeColor = Color.FromArgb(45, 45, 50),
            TextAlign = ContentAlignment.MiddleLeft,
            BackColor = Color.Transparent,
        };

        public CardPanel()
        {
            DoubleBuffered = true;
            BackColor = Color.White;
            Padding = new Padding(11, 9, 11, 9);
            MinimumSize = new Size(150, 66);
            Height = 66;
            Controls.Add(_value);
            Controls.Add(_title);
        }

        public string Title { get => _title.Text; set => _title.Text = value; }
        public string Value { get => _value.Text; set => _value.Text = value; }

        /// <summary>true = hijau lembut, false = merah lembut, null = netral.</summary>
        public void SetState(bool? ok)
        {
            BackColor = ok == true ? Color.FromArgb(236, 247, 237)
                : ok == false ? Color.FromArgb(251, 238, 238)
                : Color.White;
            _value.ForeColor = ok == true ? Color.FromArgb(28, 110, 52)
                : ok == false ? Color.FromArgb(160, 52, 52)
                : Color.FromArgb(45, 45, 50);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            using var pen = new Pen(Color.FromArgb(225, 225, 228));
            e.Graphics.DrawRectangle(pen, 0, 0, Width - 1, Height - 1);
        }
    }

    public DownloadCenterDialog(
        string mushafKey, string transKey, string tafsirKey, string qareeKey,
        int gotoSurah = 0, int gotoAyah = 0)
    {
        _mushafKey = mushafKey;
        _transKey = transKey;
        _tafsirKey = tafsirKey;
        _qareeKey = qareeKey;
        _gotoSurah = gotoSurah;
        _gotoAyah = gotoAyah;

        Text = "Unduhan & Konten Offline — Quran Desktop";
        ClientSize = new Size(1080, 680);
        MinimumSize = new Size(940, 600);
        StartPosition = FormStartPosition.CenterParent;
        Font = new Font("Segoe UI", 9f);

        BuildTabs();

        var root = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 2 };
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.Controls.Add(_tabs);
        root.Controls.Add(BottomPanel());
        Controls.Add(root);

        OfflineContentService.Instance.InventoryChanged += OnInventoryChanged;
        FormClosed += (_, _) => OfflineContentService.Instance.InventoryChanged -= OnInventoryChanged;
        Load += async (_, _) =>
        {
            await RefreshAllAsync();
            FocusGotoAyah();
        };
        FormClosing += (_, e) =>
        {
            if (_running)
            {
                if (MessageBox.Show(this, "Masih ada unduhan berjalan. Batalkan dan tutup?",
                        "Unduhan berjalan", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes)
                {
                    e.Cancel = true;
                    return;
                }
                _jobCts?.Cancel();
            }
            _scanCts?.Cancel();
        };
    }

    private void OnInventoryChanged()
    {
        if (_running || _scanning || !IsHandleCreated) return;
        BeginInvoke(new Action(async () => await RefreshAllAsync()));
    }

    // ================= UI BUILD =================

    private static DataGridView CreateGrid()
    {
        var g = new DataGridView
        {
            Dock = DockStyle.Fill,
            ReadOnly = true,
            AllowUserToAddRows = false,
            AllowUserToDeleteRows = false,
            AllowUserToResizeRows = false,
            RowHeadersVisible = false,
            SelectionMode = DataGridViewSelectionMode.FullRowSelect,
            MultiSelect = false,
            AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill,
            BackgroundColor = Color.White,
            BorderStyle = BorderStyle.None,
            EnableHeadersVisualStyles = true,
            AllowUserToOrderColumns = false,
            RowTemplate = { Height = 26 },
        };
        // header lebih jelas + alternating row lembut
        g.ColumnHeadersDefaultCellStyle.Font = new Font("Segoe UI", 9f, FontStyle.Bold);
        g.ColumnHeadersDefaultCellStyle.BackColor = Color.FromArgb(244, 244, 242);
        g.ColumnHeadersDefaultCellStyle.ForeColor = Color.FromArgb(60, 60, 65);
        g.ColumnHeadersDefaultCellStyle.Padding = new Padding(6, 4, 6, 4);
        g.ColumnHeadersHeight = 32;
        g.ColumnHeadersHeightSizeMode = DataGridViewColumnHeadersHeightSizeMode.DisableResizing;
        g.EnableHeadersVisualStyles = false;
        g.DefaultCellStyle.SelectionBackColor = Color.FromArgb(214, 230, 246);
        g.DefaultCellStyle.SelectionForeColor = Color.Black;
        g.AlternatingRowsDefaultCellStyle.BackColor = Color.FromArgb(249, 249, 247);
        return g;
    }

    private static DataGridViewTextBoxColumn Col(string name, string header, float weight)
        => new()
        {
            Name = name,
            HeaderText = header,
            FillWeight = weight,
            SortMode = DataGridViewColumnSortMode.NotSortable,
        };

    private void BuildTabs()
    {
        _tabs.TabPages.Add(BuildTabRingkasan());
        _tabs.TabPages.Add(BuildTabSurah());
        _tabs.TabPages.Add(BuildTabAyat());
        _tabs.TabPages.Add(BuildTabQari());
        _tabs.TabPages.Add(BuildTabStorage());
    }

    private static FlowLayoutPanel Toolbar(params Control[] controls)
    {
        var flow = new FlowLayoutPanel
        {
            Dock = DockStyle.Top,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = true,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            Padding = new Padding(6),
        };
        foreach (var c in controls) flow.Controls.Add(c);
        return flow;
    }

    private static Label Heading(string text) => new()
    {
        Text = text,
        AutoSize = true,
        Font = new Font("Segoe UI", 11f, FontStyle.Bold),
        Padding = new Padding(4, 6, 4, 2),
    };

    private TabPage BuildTabRingkasan()
    {
        var page = new TabPage("Ringkasan");
        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 4, Padding = new Padding(10) };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        for (int i = 0; i < 4; i++) layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));

        var cards = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 3, AutoSize = true };
        for (int i = 0; i < 3; i++) cards.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 33.3f));
        foreach (var key in new[] { "card.mushaf", "card.hilite", "card.arab", "card.trans", "card.tafsir", "card.audio", "card.storage", "card.status", "card.hint" })
        {
            var card = new CardPanel { Name = key, Title = "…", Value = "Memuat…", Margin = new Padding(4) };
            cards.Controls.Add(card);
        }
        layout.Controls.Add(cards, 0, 0);
        layout.SetColumnSpan(cards, 2);

        var profile = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, AutoSize = true };
        profile.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        profile.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        profile.Controls.Add(Heading("Profil Unduhan"), 0, 0);
        profile.SetColumnSpan(profile.Controls[0], 2);
        var profileRow = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, WrapContents = true, Margin = new Padding(0) };
        _cmbProfile.Items.AddRange(new object[] { "BASIC OFFLINE", "READING OFFLINE", "FULL OFFLINE", "CUSTOM" });
        _cmbProfile.SelectedIndex = 0;
        foreach (var r in Reciters.All) _cmbProfileQari.Items.Add(new ComboItem(r.Display, r));
        _cmbProfileQari.SelectedIndex = Math.Max(0, Reciters.All.FindIndex(r => r.Key == _qareeKey));
        profileRow.Controls.Add(_cmbProfile);
        profileRow.Controls.Add(new Label { Text = "Qari:", AutoSize = true, Margin = new Padding(12, 7, 4, 0) });
        profileRow.Controls.Add(_cmbProfileQari);
        profileRow.Controls.Add(_chkMushaf);
        profileRow.Controls.Add(_chkHilite);
        profileRow.Controls.Add(_chkArab);
        profileRow.Controls.Add(_chkTrans);
        profileRow.Controls.Add(_chkTafsir);
        profileRow.Controls.Add(_chkAudio);
        profile.Controls.Add(profileRow, 0, 1);
        profile.SetColumnSpan(profileRow, 2);

        var actions = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            AutoSize = true,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = true,
            Margin = new Padding(0),
        };
        var btnStart = ActionButton("▶ Mulai Unduhan (hanya yang kurang)", 250);
        var btnScan = ActionButton("Scan Ulang", 110);
        var btnVerify = ActionButton("Verifikasi", 100);
        var btnMissing = ActionButton("Unduh Yang Kurang (paket aktif)", 230);
        var btnAll = ActionButton("Unduh Semua (FULL OFFLINE)", 200);
        var btnFolder = ActionButton("Buka Folder", 100);
        btnStart.Click += async (_, _) => await StartProfileAsync();
        btnScan.Click += async (_, _) => { OfflineContentService.Instance.InvalidateAll(); await RefreshAllAsync(); };
        btnVerify.Click += async (_, _) => { OfflineContentService.Instance.InvalidateAll(); await RefreshAllAsync(deep: true); };
        btnMissing.Click += async (_, _) => await DownloadMissingActiveAsync();
        btnAll.Click += async (_, _) => await StartProfileAsync(full: true);
        btnFolder.Click += (_, _) => OpenFolder(OfflineContentService.Instance.CacheRoot);
        actions.Controls.AddRange(new Control[] { btnStart, btnScan, btnVerify, btnMissing, btnAll, btnFolder });
        layout.Controls.Add(actions, 0, 1);
        layout.SetColumnSpan(actions, 2);
        var hint = new Label
        {
            Text = "Semua unduhan hanya mengambil file yang belum ada/rusak. File diunduh ke .part lalu dipindah otomatis setelah valid.",
            AutoSize = true,
            MaximumSize = new Size(860, 0),
            ForeColor = Color.FromArgb(110, 110, 115),
            Padding = new Padding(10, 6, 10, 4),
        };
        layout.Controls.Add(hint, 0, 2);
        layout.SetColumnSpan(hint, 2);

        page.Controls.Add(layout);
        return page;
    }

    /// <summary>Tombol aksi seragam: tinggi 32, margin 6, wrap rapi.</summary>
    private static Button ActionButton(string text, int width)
        => new()
        {
            Text = text,
            Width = width,
            Height = 32,
            UseVisualStyleBackColor = true,
            Margin = new Padding(3, 4, 3, 4),
        };

    private TabPage BuildTabSurah()
    {
        var page = new TabPage("Surah");
        _gridSurah.Columns.AddRange(new DataGridViewColumn[]
        {
            Col("no", "No", 5),
            Col("surah", "Surah", 26),
            Col("ayat", "Ayat", 7),
            Col("mushaf", "Mushaf", 10),
            Col("arab", "Arab", 10),
            Col("trans", "Terjemahan", 12),
            Col("tafsir", "Tafsir", 10),
            Col("audio", "Audio", 10),
            Col("progress", "Progress", 10),
            Col("status", "Status", 12),
        });
        _gridSurah.CellDoubleClick += (_, e) =>
        {
            if (e.RowIndex >= 0 && e.RowIndex < _surahRows.Length)
            {
                int s = _surahRows[e.RowIndex].Number;
                _cmbAyatSurah.SelectedIndex = s - 1;
                _tabs.SelectedIndex = 2;
            }
        };
        _gridSurah.SelectionChanged += (_, _) => UpdateSurahGridStyles();

        _cmbFilterSurahStatus.Items.AddRange(new object[] { "Semua", "Lengkap", "Sebagian", "Belum Ada" });
        _cmbFilterSurahStatus.SelectedIndex = 0;
        _cmbFilterSurahStatus.SelectedIndexChanged += (_, _) => FillSurahGrid();
        _txtSearchSurah.TextChanged += (_, _) => FillSurahGrid();

        page.Controls.Add(_gridSurah);
        page.Controls.Add(Toolbar(
            new Label { Text = "Filter:", AutoSize = true, Margin = new Padding(2, 8, 2, 0) },
            _cmbFilterSurahStatus,
            _txtSearchSurah));
        return page;
    }

    private TabPage BuildTabAyat()
    {
        var page = new TabPage("Ayat");
        _gridAyat.VirtualMode = true;
        _gridAyat.Columns.AddRange(new DataGridViewColumn[]
        {
            Col("ayat", "Ayat", 9),
            Col("page", "Halaman", 9),
            Col("mushaf", "Mushaf", 10),
            Col("hilite", "Hilite", 10),
            Col("arab", "Arab", 10),
            Col("trans", "Terjemahan", 13),
            Col("tafsir", "Tafsir", 13),
            Col("audio", "Audio", 13),
            Col("status", "Status", 13),
        });
        _gridAyat.CellValueNeeded += (_, e) =>
        {
            if (e.RowIndex < 0 || e.RowIndex >= _filteredAyat.Count) return;
            var r = _filteredAyat[e.RowIndex];
            e.Value = e.ColumnIndex switch
            {
                0 => $"{r.S}:{r.A}",
                1 => r.Page.ToString(),
                2 => AyahRow.Sym(r.Mushaf),
                3 => AyahRow.Sym(r.Hilite),
                4 => AyahRow.Sym(r.Arab),
                5 => AyahRow.Sym(r.Trans),
                6 => AyahRow.Sym(r.Tafsir),
                7 => AyahRow.Sym(r.Audio),
                8 => r.Complete ? "✓ Lengkap" : "— Kurang",
                _ => "",
            };
        };
        _gridAyat.SelectionChanged += (_, _) => ShowAyahDetail();
        _gridAyat.RowPostPaint += (_, e) =>
        {
            if (e.RowIndex >= 0 && e.RowIndex < _filteredAyat.Count)
            {
                var r = _filteredAyat[e.RowIndex];
                if (r.S == _gotoSurah && r.A == _gotoAyah)
                {
                    using var pen = new Pen(Color.OrangeRed, 2f);
                    var rect = e.RowBounds;
                    rect.Width -= 1; rect.Height -= 1;
                    e.Graphics.DrawRectangle(pen, rect);
                }
            }
        };

        foreach (var s in SurahList.All)
        {
            _cmbAyatSurah.Items.Add(new ComboItem($"{s.Number}. {s.EnglishName}", s.Number));
        }
        foreach (var r in Reciters.All) _cmbAyatQari.Items.Add(new ComboItem(r.Display, r.Key));
        foreach (var t in Translations.All.Where(t => t.Key != "ar_ayat" && t.Key != "ar_ayat_safy" && t.Key != "ar_mu" && t.Key != "ar_ma3any"))
        {
            _cmbAyatTrans.Items.Add(new ComboItem(t.Display, t.Key));
        }
        foreach (var t in Tafsirs.All) _cmbAyatTafsir.Items.Add(new ComboItem(t.Display, t.Key));
        _cmbAyatSurah.SelectedIndex = 0;
        _cmbAyatQari.SelectedIndex = Math.Max(0, Reciters.All.FindIndex(r => r.Key == _qareeKey));
        _cmbAyatTrans.SelectedIndex = Math.Max(0, _cmbAyatTrans.Items.Count - 1);
        SelectComboByKey(_cmbAyatTrans, _transKey);
        SelectComboByKey(_cmbAyatTafsir, _tafsirKey);
        _cmbAyatSurah.SelectedIndexChanged += async (_, _) => await RebuildAyatRowsAsync();
        _cmbAyatQari.SelectedIndexChanged += async (_, _) => await RebuildAyatRowsAsync();
        _cmbAyatTrans.SelectedIndexChanged += async (_, _) => await RebuildAyatRowsAsync();
        _cmbAyatTafsir.SelectedIndexChanged += async (_, _) => await RebuildAyatRowsAsync();

        var buttons = new FlowLayoutPanel { Dock = DockStyle.Top, AutoSize = true, WrapContents = true, Padding = new Padding(6) };
        var btnAyat = new Button { Text = "Unduh kebutuhan ayat ini", Width = 200 };
        var btnAudioAll = new Button { Text = "Unduh audio semua qari (ayat ini)", Width = 250 };
        var btnSurahMissing = new Button { Text = "Unduh resource kurang (surah ini)", Width = 240 };
        var btnDelAyat = new Button { Text = "Hapus cache ayat", Width = 140 };
        var btnOpen = new Button { Text = "Buka Ayat", Width = 100 };
        btnAyat.Click += async (_, _) => await DownloadAyatMissingAsync(CurrentAyat(), allReciters: false, surahScope: false);
        btnAudioAll.Click += async (_, _) => await DownloadAyatMissingAsync(CurrentAyat(), allReciters: true, surahScope: false);
        btnSurahMissing.Click += async (_, _) => await DownloadAyatMissingAsync(CurrentAyat(), allReciters: false, surahScope: true);
        btnDelAyat.Click += async (_, _) => await DeleteAyatCacheAsync();
        btnOpen.Click += (_, _) =>
        {
            var cur = CurrentAyat();
            if (cur != null)
            {
                GotoRequested?.Invoke(cur.S, cur.A);
                Close();
            }
        };
        buttons.Controls.AddRange(new Control[] { btnAyat, btnAudioAll, btnSurahMissing, btnDelAyat, btnOpen });

        var splitter = new SplitContainer
        {
            Dock = DockStyle.Fill,
            Orientation = Orientation.Horizontal,
            SplitterDistance = 300,
        };
        splitter.Panel1.Controls.Add(_gridAyat);
        splitter.Panel2.Controls.Add(_detail);
        splitter.Panel2.BackColor = Color.White;
        splitter.Panel2MinSize = 120;

        page.Controls.Add(splitter);
        page.Controls.Add(buttons);
        page.Controls.Add(Toolbar(
            new Label { Text = "Surah:", AutoSize = true, Margin = new Padding(2, 8, 2, 0) },
            _cmbAyatSurah,
            new Label { Text = "Qari:", AutoSize = true, Margin = new Padding(8, 8, 2, 0) },
            _cmbAyatQari,
            new Label { Text = "Terjemahan:", AutoSize = true, Margin = new Padding(8, 8, 2, 0) },
            _cmbAyatTrans,
            new Label { Text = "Tafsir:", AutoSize = true, Margin = new Padding(8, 8, 2, 0) },
            _cmbAyatTafsir));
        return page;
    }

    private TabPage BuildTabQari()
    {
        var page = new TabPage("Qari");
        _gridQari.Columns.AddRange(new DataGridViewColumn[]
        {
            Col("qari", "Qari", 34),
            Col("downloaded", "Downloaded", 14),
            Col("total", "Total", 9),
            Col("progress", "Progress", 11),
            Col("size", "Ukuran", 14),
            Col("status", "Status", 18),
        });
        _gridQari.SelectionChanged += (_, _) => FillQariSurahGrid();

        _gridQariSurah.Columns.AddRange(new DataGridViewColumn[]
        {
            Col("qsurah", "Surah", 44),
            Col("qayat", "Ayat", 12),
            Col("qok", "Downloaded", 18),
            Col("qstatus", "Status", 26),
        });

        var buttons = new FlowLayoutPanel { Dock = DockStyle.Top, AutoSize = true, WrapContents = true, Padding = new Padding(6) };
        var btnDlAll = new Button { Text = "Unduh Semua (6236 ayat)", Width = 190 };
        var btnDlMissing = new Button { Text = "Unduh Yang Kurang", Width = 150 };
        var btnVerify = new Button { Text = "Verifikasi", Width = 100 };
        var btnDel = new Button { Text = "Hapus Audio Qari", Width = 140 };
        btnDlAll.Click += async (_, _) => await DownloadReciterAsync(all: true);
        btnDlMissing.Click += async (_, _) => await DownloadReciterAsync(all: false);
        btnVerify.Click += async (_, _) => { OfflineContentService.Instance.ClearReciterAudioCache(); await RefreshAllAsync(); };
        btnDel.Click += async (_, _) => await DeleteReciterAsync();
        buttons.Controls.AddRange(new Control[] { btnDlAll, btnDlMissing, btnVerify, btnDel });

        var split = new SplitContainer { Dock = DockStyle.Fill, Orientation = Orientation.Horizontal };
        split.Panel1.Controls.Add(_gridQari);
        split.Panel2.Controls.Add(_gridQariSurah);
        split.SplitterDistance = 380;
        split.Panel2MinSize = 120;

        page.Controls.Add(split);
        page.Controls.Add(buttons);
        page.Controls.Add(Toolbar(new Label
        {
            Text = "Klik qari untuk rincian per surah (114 surah). Termasuk 4 voice translation — unduh sesuai kebutuhan storage.",
            AutoSize = true,
            ForeColor = Color.DimGray,
            Margin = new Padding(4, 8, 4, 0),
        }));
        return page;
    }

    private TabPage BuildTabStorage()
    {
        var page = new TabPage("Penyimpanan");
        _gridStorage.Columns.AddRange(new DataGridViewColumn[]
        {
            Col("slabel", "Resource", 62),
            Col("ssize", "Ukuran (aktual)", 38),
        });

        var buttons = new FlowLayoutPanel { Dock = DockStyle.Top, AutoSize = true, WrapContents = true, Padding = new Padding(6) };
        var btnOpen = new Button { Text = "Buka Folder", Width = 110 };
        var btnClean = new Button { Text = "Bersihkan file .part", Width = 150 };
        var btnDel = new Button { Text = "Hapus Resource Terpilih…", Width = 200 };
        var btnVerify = new Button { Text = "Verifikasi Semua", Width = 130 };
        btnOpen.Click += (_, _) => OpenFolder(OfflineContentService.Instance.CacheRoot);
        btnClean.Click += async (_, _) =>
        {
            int n = OfflineContentService.Instance.CleanPartFiles();
            MessageBox.Show(this, $"{n} file .part dibersihkan.", "Bersihkan .part");
            await RefreshStorageAsync();
        };
        btnDel.Click += async (_, _) => await DeleteResourceAsync();
        btnVerify.Click += async (_, _) =>
        {
            OfflineContentService.Instance.InvalidateAll();
            await RefreshAllAsync(deep: true);
            MessageBox.Show(this, "Verifikasi selesai — inventory dimuat ulang dari file aktual.", "Verifikasi");
        };
        buttons.Controls.AddRange(new Control[] { btnOpen, btnClean, btnDel, btnVerify });

        page.Controls.Add(_gridStorage);
        page.Controls.Add(buttons);
        page.Controls.Add(Toolbar(
            new Label { Text = "Hapus resource:", AutoSize = true, Margin = new Padding(2, 8, 2, 0) },
            _cmbDelResource,
            new Label { Text = "Selalu ada konfirmasi sebelum hapus.", AutoSize = true, ForeColor = Color.DimGray, Margin = new Padding(8, 8, 4, 0) }));
        return page;
    }

    private Control BottomPanel()
    {
        var panel = new Panel { Dock = DockStyle.Fill, Height = 62, Padding = new Padding(10, 6, 10, 8), BackColor = Color.FromArgb(250, 250, 249) };
        panel.Paint += (_, e) =>
        {
            using var pen = new Pen(Color.FromArgb(228, 228, 230));
            e.Graphics.DrawLine(pen, 0, 0, panel.Width, 0);
        };
        _bar.Height = 18;
        _bar.Margin = new Padding(0, 4, 8, 2);
        _btnCancelJobs.Height = 28;
        _btnCancelJobs.Margin = new Padding(4, 0, 0, 0);
        _lblProgress.ForeColor = Color.FromArgb(70, 70, 75);
        _lblProgress.Margin = new Padding(1, 3, 0, 0);
        _lblProgress.AutoEllipsis = true;
        var table = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 2 };
        table.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        table.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        table.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        table.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        table.Controls.Add(_bar, 0, 0);
        table.Controls.Add(_btnCancelJobs, 1, 0);
        table.Controls.Add(_lblProgress, 0, 1);
        table.SetColumnSpan(_lblProgress, 2);
        _btnCancelJobs.Click += (_, _) => _jobCts?.Cancel();
        panel.Controls.Add(table);
        return panel;
    }

    private static void SelectComboByKey(ComboBox cmb, string key)
    {
        for (int i = 0; i < cmb.Items.Count; i++)
        {
            if (cmb.Items[i] is ComboItem ci && (ci.Value as string) == key)
            {
                cmb.SelectedIndex = i;
                return;
            }
        }
    }

    private static void OpenFolder(string path)
    {
        try
        {
            Directory.CreateDirectory(path);
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = path,
                UseShellExecute = true,
            });
        }
        catch (Exception ex)
        {
            MessageBox.Show("Gagal membuka folder: " + ex.Message);
        }
    }

    private static string FormatSize(long bytes)
        => bytes >= 1L << 30 ? $"{bytes / (double)(1L << 30):0.00} GB"
            : bytes >= 1L << 20 ? $"{bytes / (double)(1L << 20):0.0} MB"
            : bytes >= 1L << 10 ? $"{bytes / (double)(1L << 10):0} KB"
            : $"{bytes} B";

    private (string TransKey, string TafsirKey, Reciter Qari) ActiveSelections()
    {
        string trans = _cmbAyatTrans.SelectedItem is ComboItem t ? (string)t.Value! : _transKey;
        string tafsir = _cmbAyatTafsir.SelectedItem is ComboItem tf ? (string)tf.Value! : _tafsirKey;
        string qari = _cmbAyatQari.SelectedItem is ComboItem q ? (string)q.Value! : _qareeKey;
        var rec = Reciters.Find(qari) ?? Reciters.All[0];
        return (trans, tafsir, rec);
    }

    private AyahRow? CurrentAyat()
        => _gridAyat.CurrentRow != null && _gridAyat.CurrentRow.Index < _filteredAyat.Count
            ? _filteredAyat[_gridAyat.CurrentRow.Index]
            : null;

    // ================= REFRESH / SCAN =================

    private async Task RefreshAllAsync(bool deep = false)
    {
        if (_scanning) return;
        _scanning = true;
        _scanCts = new CancellationTokenSource();
        var ct = _scanCts.Token;
        var svc = OfflineContentService.Instance;
        try
        {
            _lblProgress.Text = "Memindai konten offline…";

            var mushafTask = Task.Run(() => MushafTypes.All.Select(svc.ScanMushaf).ToList(), ct);
            var reciterTask = Task.Run(() =>
            {
                var list = new List<ReciterSummary>();
                foreach (var r in Reciters.All)
                {
                    ct.ThrowIfCancellationRequested();
                    list.Add(svc.ScanReciter(r));
                }
                foreach (var v in VoiceTranslations.All)
                {
                    ct.ThrowIfCancellationRequested();
                    list.Add(svc.ScanAudioFolder(v.Key, v.Folder, "Voice — " + v.Display));
                }
                return list;
            }, ct);
            var storageTask = svc.GetStorageAsync();
            var textTask = Task.Run(() =>
            {
                var list = new List<TextKeySummary>();
                list.Add(svc.ScanTextKey("teks", "ar_ayat", "Teks Arab"));
                list.Add(svc.ScanTextKey("teks", _transKey, "Terjemahan " + (Translations.Find(_transKey)?.Display ?? _transKey)));
                list.Add(svc.ScanTextKey("tafsir", _tafsirKey, "Tafsir " + (Tafsirs.Find(_tafsirKey)?.Display ?? _tafsirKey)));
                return list;
            }, ct);

            await Task.WhenAll(mushafTask, storageTask, textTask);

            var mushafs = mushafTask.Result;
            var storage = storageTask.Result;
            var texts = textTask.Result;

            SetCard("card.mushaf", "Mushaf", $"{mushafs[0].Pages}/{mushafs[0].PagesTotal} halaman", mushafs[0].Pages == mushafs[0].PagesTotal);
            SetCard("card.hilite", "Hilite ayat", hiliteText(), null);
            SetCard("card.arab", "Teks Arab", $"{texts[0].AyatFound}/{texts[0].AyatTotal} ayat", texts[0].AyatFound == texts[0].AyatTotal);
            SetCard("card.trans", "Terjemahan aktif", $"{texts[1].AyatFound}/{texts[1].AyatTotal} ayat", texts[1].AyatFound == texts[1].AyatTotal);
            SetCard("card.tafsir", "Tafsir aktif", $"{texts[2].AyatFound}/{texts[2].AyatTotal} ayat", texts[2].AyatFound == texts[2].AyatTotal);

            // Surah summaries — pakai qari aktif untuk kolom audio
            var activeQari = Reciters.Find(_qareeKey) ?? Reciters.All[0];
            var surahs = await Task.Run(() =>
            {
                var list = new SurahOfflineSummary[QuranData.SurahCount];
                for (int s = 1; s <= QuranData.SurahCount; s++)
                {
                    ct.ThrowIfCancellationRequested();
                    list[s - 1] = svc.ScanSurah(s, _mushafKey,
                        new[] { _transKey }, new[] { _tafsirKey }, new[] { activeQari });
                }
                return list;
            }, ct);
            _surahRows = surahs;
            FillSurahGrid();

            // Audio card dari reciter scan (qari + voice)
            var reciters = reciterTask.Result;
            _qariRows = reciters;
            FillQariGrid();
            var active = reciters.FirstOrDefault(r => r.Key == _qareeKey);
            if (active != null)
            {
                SetCard("card.audio", "Audio qari aktif", $"{active.Valid}/{active.Total} ayat", active.Valid == active.Total);
            }

            SetCard("card.storage", "Total storage", FormatSize(storage.TotalBytes), null);
            SetCard("card.status", "Status", StatusSummaryText(surahs, mushafs[0], texts), null);
            SetCard("card.hint", "Tips", "Buka tab Ayat untuk status per ayat (6.236 ayat). Klik baris untuk detail & tombol unduh per ayat.", null);

            FillStorageGrid(storage);
            _lblProgress.Text = deep ? "Scan & verifikasi selesai." : "Scan selesai.";
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            // scan gagal → jangan biarkan UI kosong/membingungkan; isi placeholder jelas
            _lblProgress.Text = "Scan gagal: " + ex.Message;
            foreach (var key in new[] { "card.mushaf", "card.hilite", "card.arab", "card.trans", "card.tafsir", "card.audio", "card.storage", "card.status" })
            {
                SetCard(key, key.Split('.')[1], "Data belum tersedia karena scan gagal.", null);
            }
            SetCard("card.hint", "Tips", "Coba [Scan Ulang] atau [Verifikasi]. Pastikan disk cache dapat diakses.", null);
        }
        finally
        {
            _scanning = false;
        }

        string hiliteText()
        {
            var mk = MushafTypes.ResolveMushaf(_mushafKey);
            int total = QuranData.PageCount(mk.PageKey);
            int ok = 0;
            for (int p = 1; p <= total; p++) if (svc.GetHiliteStatus(mk.Key, p)) ok++;
            return $"{ok}/{total} halaman";
        }
    }

    private string StatusSummaryText(SurahOfflineSummary[] surahs, MushafPageSummary mushaf, List<TextKeySummary> texts)
    {
        if (surahs.All(s => s.Complete) && mushaf.Pages == mushaf.PagesTotal
            && texts.All(t => t.AyatFound == t.AyatTotal)) return "✓ Lengkap";
        bool any = surahs.Any(s => s.Partial || s.Complete) || mushaf.Pages > 0 || texts.Any(t => t.AyatFound > 0);
        if (!any) return "Belum diunduh";
        bool corrupt = false;
        var svc = OfflineContentService.Instance;
        for (int s = 1; s <= QuranData.SurahCount && !corrupt; s++)
        {
            var st = svc.GetTarjamaStatus(_transKey, s);
            corrupt = st.FileValid
                      && st.AyatFound < QuranData.SurahAyahCount(s)
                      && st.AyatFound > 0;
        }
        return corrupt ? "! Ada file rusak/tidak lengkap" : "Sebagian";
    }

    private void SetCard(string name, string title, string value, bool? ok)
    {
        if (_tabs.TabPages.Count == 0 || _tabs.TabPages[0].Controls.Count == 0) return;
        var lbl = _tabs.TabPages[0].Controls.Find(name, true).FirstOrDefault() as CardPanel;
        if (lbl == null) return;
        lbl.Title = title;
        lbl.Value = value;
        lbl.SetState(ok);
    }

    private void FillSurahGrid()
    {
        string filter = _cmbFilterSurahStatus.SelectedItem?.ToString() ?? "Semua";
        string search = _txtSearchSurah.Text.Trim();
        _gridSurah.SuspendLayout();
        _gridSurah.Rows.Clear();
        foreach (var s in _surahRows)
        {
            bool match = filter switch
            {
                "Lengkap" => s.Complete,
                "Sebagian" => s.Partial,
                "Belum Ada" => !s.Complete && !s.Partial,
                _ => true,
            };
            if (!match) continue;
            if (search.Length > 0 && !s.Name.Contains(search, StringComparison.OrdinalIgnoreCase)
                && s.Number.ToString() != search) continue;

            string transPct = s.TranslationAyat.Count > 0
                ? $"{s.TranslationAyat.Values.First()}/{s.AyahCount}" : "—";
            string tafsPct = s.TafsirAyat.Count > 0
                ? $"{s.TafsirAyat.Values.First()}/{s.AyahCount}" : "—";
            string audioTxt = s.ReciterAyat.Count > 0
                ? $"{s.ReciterAyat.Values.First()}/{s.AyahCount}" : "—";
            double overall = AvgPct(s);
            string status = s.Complete ? "✓ Lengkap" : s.Partial ? "Sebagian" : "Belum Ada";
            _gridSurah.Rows.Add(s.Number, s.Name, s.AyahCount, Pct(s.MushafPages, s.MushafPagesTotal),
                Pct(s.ArabicAyat, s.AyahCount), transPct, tafsPct, audioTxt, $"{overall:0}%", status);
        }
        _gridSurah.ResumeLayout();
    }

    private static string Pct(int part, int total) => total <= 0 ? "—" : $"{part * 100 / total}%";

    private static double AvgPct(SurahOfflineSummary s)
    {
        double vals = PctVal(s.MushafPages, s.MushafPagesTotal) + PctVal(s.ArabicAyat, s.AyahCount)
            + PctVal(s.TranslationAyat.Values.FirstOrDefault(), s.AyahCount)
            + PctVal(s.TafsirAyat.Values.FirstOrDefault(), s.AyahCount)
            + PctVal(s.ReciterAyat.Values.FirstOrDefault(), s.AyahCount);
        return vals / 5;
    }

    private static double PctVal(int part, int total) => total <= 0 ? 0 : part * 100.0 / total;

    private void UpdateSurahGridStyles()
    {
        foreach (DataGridViewRow row in _gridSurah.Rows)
        {
            if (row.Index < 0 || row.Index >= _surahRows.Length) continue;
            var s = _surahRows[row.Index];
            row.DefaultCellStyle.BackColor = s.Complete ? Color.FromArgb(236, 248, 238)
                : s.Partial ? Color.Empty // biarkan alternating style
                : Color.FromArgb(250, 244, 244);
        }
    }

    private async Task RebuildAyatRowsAsync()
    {
        if (_scanning) return;
        _scanning = true;
        var ct = (_scanCts = new CancellationTokenSource()).Token;
        try
        {
            _lblProgress.Text = "Memuat status ayat…";
            var (trans, tafsir, qari) = ActiveSelections();
            int surah = _cmbAyatSurah.SelectedItem is ComboItem sc ? (int)sc.Value! : 0;
            var svc = OfflineContentService.Instance;
            var mk = MushafTypes.ResolveMushaf(_mushafKey);

            var rows = await Task.Run(() =>
            {
                var list = new List<AyahRow>(QuranData.TotalAyahCount);
                IEnumerable<int> surahs = surah > 0 ? new[] { surah } : Enumerable.Range(1, QuranData.SurahCount);
                foreach (var s in surahs)
                {
                    ct.ThrowIfCancellationRequested();
                    int n = QuranData.SurahAyahCount(s);
                    for (int a = 1; a <= n; a++)
                    {
                        int page = MushafTypes.FindMushafPage(mk.Key, s, a);
                        list.Add(new AyahRow(
                            s, a, page,
                            svc.GetMushafPageStatus(mk.Key, page).IsValid,
                            svc.GetHiliteStatus(mk.Key, page),
                            svc.GetArabicStatus(s, a),
                            svc.HasTarjamaAyah(trans, s, a),
                            svc.HasTafsirAyah(tafsir, s, a),
                            svc.GetAudioStatus(qari.Folder, s, a).IsValid));
                    }
                }
                return list;
            }, ct);

            _filteredAyat = rows;
            _gridAyat.RowCount = rows.Count;
            _gridAyat.Invalidate();
            _lblProgress.Text = $"{rows.Count} ayat dimuat.";
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            _lblProgress.Text = "Gagal memuat ayat: " + ex.Message;
        }
        finally
        {
            _scanning = false;
        }
    }

    private void ShowAyahDetail()
    {
        var cur = CurrentAyat();
        if (cur == null) return;
        var svc = OfflineContentService.Instance;
        var mk = MushafTypes.ResolveMushaf(_mushafKey);
        var (trans, tafsir, qari) = ActiveSelections();

        var st = svc.GetAyahStatus(cur.S, cur.A, cur.Page, mk.Key,
            new[] { trans }, new[] { tafsir },
            Reciters.All, VoiceTranslations.All);

        var sb = new StringBuilder();
        sb.AppendLine($"QS {cur.S}:{cur.A} — {SurahList.Get(cur.S).EnglishName}");
        sb.AppendLine($"Halaman ({mk.Display}): {cur.Page}");
        sb.AppendLine();
        sb.AppendLine($"Mushaf      : {(st.MushafAvailable ? "✓" : "—")} {mk.Display} — halaman {cur.Page} {(st.MushafAvailable ? "tersedia" : "belum ada")}");
        sb.AppendLine($"Hilite      : {(st.HiliteAvailable ? "✓" : "—")} {(st.HiliteAvailable ? "tersedia" : "belum ada")}");
        sb.AppendLine($"Teks Arab   : {(st.ArabicAvailable ? "✓" : "—")} {(st.ArabicAvailable ? "tersedia" : "belum ada")}");
        sb.AppendLine($"Terjemahan  : {(st.TranslationAvailable.TryGetValue(trans, out var tok) && tok ? "✓" : "—")} {Translations.Find(trans)?.Display ?? trans} — cache per surah");
        sb.AppendLine($"Tafsir      : {(st.TafsirAvailable.TryGetValue(tafsir, out var tfs) && tfs ? "✓" : "—")} {Tafsirs.Find(tafsir)?.Display ?? tafsir}");
        sb.AppendLine();
        sb.AppendLine($"AUDIO — status semua qari ({Reciters.All.Count}):");
        foreach (var (k, a) in st.ReciterAudio)
        {
            var r = Reciters.Find(k);
            sb.AppendLine(a.IsValid
                ? $"   ✓ {r?.Display ?? k} — {FormatSize(a.SizeBytes)}"
                : a.Exists ? $"   ! {r?.Display ?? k} — file rusak ({a.SizeBytes} B)"
                : $"   — {r?.Display ?? k}");
        }
        sb.AppendLine();
        sb.AppendLine("VOICE TRANSLATION:");
        foreach (var (k, a) in st.VoiceTranslationAudio)
        {
            var v = VoiceTranslations.Find(k);
            sb.AppendLine(a.IsValid
                ? $"   ✓ {v?.Display ?? k} — {FormatSize(a.SizeBytes)}"
                : $"   — {v?.Display ?? k}");
        }
        sb.AppendLine();
        sb.AppendLine("Catatan: teks Arab & terjemahan di-cache per surah; tafsir, audio, mushaf & hilite per ayat/halaman.");
        _detail.Text = sb.ToString();
    }

    // ================= ACTIONS =================

    private DownloadManager.DownloadScope BuildActiveScope()
    {
        var trans = _chkTrans.Checked ? new[] { _transKey } : Array.Empty<string>();
        var tafs = _chkTafsir.Checked ? new[] { _tafsirKey } : Array.Empty<string>();
        string qari = _cmbProfileQari.SelectedItem is ComboItem q ? (string)q.Value! : _qareeKey;
        var audio = _chkAudio.Checked ? new[] { (Reciters.Find(qari) ?? Reciters.All[0]).Folder } : Array.Empty<string>();
        return new DownloadManager.DownloadScope
        {
            Mushaf = _chkMushaf.Checked,
            Hilites = _chkHilite.Checked,
            Arab = _chkArab.Checked,
            Translations = trans,
            Tafsirs = tafs,
            AudioFolders = audio,
            MushafKey = _mushafKey,
        };
    }

    private async Task StartProfileAsync(bool full = false)
    {
        // Preset mengubah checkbox lalu menjalankan scope sesuai pilihan
        if (full)
        {
            _chkMushaf.Checked = true; _chkHilite.Checked = true; _chkArab.Checked = true;
            _chkTrans.Checked = true; _chkTafsir.Checked = true; _chkAudio.Checked = true;
        }
        else
        {
            switch (_cmbProfile.SelectedIndex)
            {
                case 0: // BASIC
                    _chkMushaf.Checked = true; _chkHilite.Checked = true; _chkArab.Checked = true;
                    _chkTrans.Checked = true; _chkTafsir.Checked = false; _chkAudio.Checked = false;
                    break;
                case 1: // READING
                    _chkMushaf.Checked = true; _chkHilite.Checked = true; _chkArab.Checked = true;
                    _chkTrans.Checked = true; _chkTafsir.Checked = true; _chkAudio.Checked = false;
                    break;
                case 2: // FULL
                    _chkMushaf.Checked = true; _chkHilite.Checked = true; _chkArab.Checked = true;
                    _chkTrans.Checked = true; _chkTafsir.Checked = true; _chkAudio.Checked = true;
                    break;
            }
        }
        var scope = BuildActiveScope();
        var jobs = DownloadManager.BuildJobs(scope);
        await RunJobsAsync(jobs);
    }

    private async Task DownloadMissingActiveAsync()
    {
        var scope = BuildActiveScope();
        await RunJobsAsync(DownloadManager.BuildJobs(scope));
    }

    /// <summary>
    /// Unduh kebutuhan AYAT terpilih: mushaf hanya halaman ayat, hilite hanya halaman ayat,
    /// audio hanya ayat itu (qari aktif atau semua qari). Teks Arab/terjemahan per surah (model cache),
    /// tafsir hanya ayat terpilih.
    /// </summary>
    private async Task DownloadAyatMissingAsync(AyahRow? row, bool allReciters, bool surahScope)
    {
        if (row == null)
        {
            MessageBox.Show(this, "Pilih ayat terlebih dahulu.", "Unduh ayat");
            return;
        }
        var svc = OfflineContentService.Instance;
        var mk = MushafTypes.ResolveMushaf(_mushafKey);
        var (trans, tafsir, qari) = ActiveSelections();
        var reciters = allReciters ? (IReadOnlyList<Reciter>)Reciters.All : (IReadOnlyList<Reciter>)new[] { qari };

        var items = new List<DownloadManager.DownloadItem>();

        if (surahScope)
        {
            // scope surah penuh: mushaf/hilite hanya halaman surah ini, audio hanya qari aktif
            items.AddRange(DownloadManager.BuildJobs(new DownloadManager.DownloadScope
            {
                Mushaf = true,
                Hilites = true,
                Arab = true,
                Translations = new[] { trans },
                Tafsirs = new[] { tafsir },
                AudioFolders = new[] { qari.Folder },
                MushafKey = mk.Key,
                Surahs = new[] { row.S },
            }));
            await RunJobsAsync(items);
            return;
        }

        if (!svc.GetMushafPageStatus(mk.Key, row.Page).IsValid)
        {
            items.Add(new DownloadManager.DownloadItem
            {
                Label = $"Mushaf {mk.Display} hal {row.Page}",
                Kind = DownloadManager.JobKind.File,
                Rel = $"mushaf/{mk.Key}/{row.Page}.png",
                Url = mk.ImageBase + row.Page + ".png",
                MinBytes = 2048,
            });
        }
        if (!svc.GetHiliteStatus(mk.Key, row.Page))
        {
            items.Add(new DownloadManager.DownloadItem
            {
                Label = $"Hilite hal {row.Page}", Kind = DownloadManager.JobKind.Hilites,
                TextKey = mk.Key, Surah = row.Page,
            });
        }
        if (!svc.GetArabicStatus(row.S, row.A))
        {
            items.Add(new DownloadManager.DownloadItem { Label = $"Teks Arab surah {row.S}", Kind = DownloadManager.JobKind.Tarjama, TextKey = "ar_ayat", Surah = row.S });
        }
        if (!svc.HasTarjamaAyah(trans, row.S, row.A))
        {
            items.Add(new DownloadManager.DownloadItem { Label = $"Terjemahan surah {row.S} ({trans})", Kind = DownloadManager.JobKind.Tarjama, TextKey = trans, Surah = row.S });
        }
        if (!svc.HasTafsirAyah(tafsir, row.S, row.A))
        {
            items.Add(new DownloadManager.DownloadItem { Label = $"Tafsir {row.S}:{row.A} ({tafsir})", Kind = DownloadManager.JobKind.Tafsir, TextKey = tafsir, Surah = row.S, Ayah = row.A });
        }
        foreach (var r in reciters)
        {
            if (!svc.GetAudioStatus(r.Folder, row.S, row.A).IsValid)
            {
                items.Add(new DownloadManager.DownloadItem
                {
                    Label = $"Audio {row.S}:{row.A} ({r.Display})",
                    Kind = DownloadManager.JobKind.File,
                    Rel = $"{r.Folder}/{row.S:D3}{row.A:D3}.mp3",
                    Url = KsuAudio.AyahUrl(r.Folder, row.S, row.A),
                });
            }
        }
        if (items.Count == 0)
        {
            MessageBox.Show(this, "Semua resource ayat ini sudah tersedia offline ✓", "Unduh ayat");
            return;
        }
        await RunJobsAsync(items);
    }

    private async Task DownloadReciterAsync(bool all)
    {
        if (_gridQari.CurrentRow?.Index is not int idx || idx >= _qariRows.Count) return;
        var row = _qariRows[idx];
        var rec = Reciters.Find(row.Key);
        List<DownloadManager.DownloadItem> jobs;
        if (rec != null && !all)
        {
            // hanya file yang kurang — progress bar akurat
            var svc = OfflineContentService.Instance;
            jobs = new List<DownloadManager.DownloadItem>();
            for (int s = 1; s <= QuranData.SurahCount; s++)
            {
                int n = QuranData.SurahAyahCount(s);
                for (int a = 1; a <= n; a++)
                {
                    if (!svc.GetAudioStatus(rec.Folder, s, a).IsValid)
                    {
                        jobs.Add(new DownloadManager.DownloadItem
                        {
                            Label = $"Audio {s}:{a} ({rec.Display})",
                            Kind = DownloadManager.JobKind.File,
                            Rel = $"{rec.Folder}/{s:D3}{a:D3}.mp3",
                            Url = KsuAudio.AyahUrl(rec.Folder, s, a),
                        });
                    }
                }
            }
        }
        else
        {
            jobs = DownloadManager.BuildJobs(new DownloadManager.DownloadScope
            {
                Mushaf = false, Hilites = false, Arab = false,
                AudioFolders = new[] { row.Folder },
            });
        }
        if (jobs.Count == 0)
        {
            MessageBox.Show(this, $"Audio {row.Display} sudah lengkap ✓", "Unduh audio");
            return;
        }
        await RunJobsAsync(jobs);
    }

    private async Task DeleteReciterAsync()
    {
        if (_gridQari.CurrentRow?.Index is not int idx || idx >= _qariRows.Count) return;
        var row = _qariRows[idx];
        if (MessageBox.Show(this,
                $"Hapus seluruh audio {row.Display} ({FormatSize(row.Bytes)})?\nFile yang sudah diunduh tidak bisa dikembalikan.",
                "Konfirmasi hapus", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes)
        {
            return;
        }
        int n = OfflineContentService.Instance.DeleteReciterAudio(row.Folder);
        _lblProgress.Text = $"{n} file audio {row.Display} dihapus.";
        await RefreshAllAsync();
    }

    private async Task DeleteAyatCacheAsync()
    {
        var cur = CurrentAyat();
        if (cur == null) return;
        if (MessageBox.Show(this,
                $"Hapus cache offline untuk QS {cur.S}:{cur.A} (audio semua qari & voice translation)?",
                "Konfirmasi hapus", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes)
        {
            return;
        }
        int n = OfflineContentService.Instance.DeleteAyahCache(cur.S, cur.A);
        _lblProgress.Text = $"{n} file dihapus untuk QS {cur.S}:{cur.A}.";
        await RebuildAyatRowsAsync();
        ShowAyahDetail();
    }

    private async Task DeleteResourceAsync()
    {
        if (_cmbDelResource.SelectedItem is not ComboItem ci || ci.Value is not (string kind, string key, string _, string folder))
        {
            MessageBox.Show(this, "Pilih resource terlebih dahulu.", "Hapus resource");
            return;
        }
        string label = kind switch
        {
            "mushaf" => $"Mushaf {MushafTypes.Find(key)?.Display ?? key}",
            "teks" => $"Terjemahan {Translations.Find(key)?.Display ?? key}",
            "tafsir" => $"Tafsir {Tafsirs.Find(key)?.Display ?? key}",
            "hilites" => "Hilite ayat",
            _ => $"Audio {Reciters.Find(key)?.Display ?? VoiceTranslations.Find(key)?.Display ?? key}",
        };
        if (MessageBox.Show(this, $"Hapus {label}?\nFile yang sudah diunduh tidak bisa dikembalikan.",
                "Konfirmasi hapus", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes)
        {
            return;
        }
        int n = kind switch
        {
            "mushaf" => OfflineContentService.Instance.DeleteMushaf(key),
            "teks" => OfflineContentService.Instance.DeleteTarjama(key),
            "tafsir" => OfflineContentService.Instance.DeleteTafsir(key),
            "hilites" => OfflineContentService.Instance.DeleteHilites(),
            _ => OfflineContentService.Instance.DeleteReciterAudio(folder),
        };
        _lblProgress.Text = $"{n} file dihapus ({label}).";
        await RefreshStorageAsync();
    }

    private void FillQariGrid()
    {
        _gridQari.SuspendLayout();
        _gridQari.Rows.Clear();
        foreach (var r in _qariRows)
        {
            string status = r.Total <= 0 ? "—"
                : r.Valid >= r.Total ? "✓ Lengkap"
                : r.Valid > 0 ? "Sebagian" : "Belum Ada";
            string progress = r.Total <= 0 ? "—" : $"{r.Valid * 100.0 / r.Total:0.00}%";
            _gridQari.Rows.Add(r.Display, $"{r.Valid}/{r.Total}", r.Total, progress, FormatSize(r.Bytes), status);
        }
        _gridQari.ResumeLayout();
    }

    private void FillQariSurahGrid()
    {
        if (_gridQari.CurrentRow?.Index is not int idx || idx >= _qariRows.Count)
        {
            _gridQariSurah.Rows.Clear();
            return;
        }
        var row = _qariRows[idx];
        string folder = row.Folder;
        var svc = OfflineContentService.Instance;
        _gridQariSurah.SuspendLayout();
        _gridQariSurah.Rows.Clear();
        for (int s = 1; s <= QuranData.SurahCount; s++)
        {
            int n = QuranData.SurahAyahCount(s);
            int ok = 0;
            for (int a = 1; a <= n; a++)
            {
                if (svc.GetAudioStatus(folder, s, a).IsValid) ok++;
            }
            _gridQariSurah.Rows.Add($"{s}. {SurahList.Get(s).EnglishName}", n,
                $"{ok}/{n}", ok == n ? "✓ Lengkap" : ok > 0 ? "Sebagian" : "Belum Ada");
        }
        _gridQariSurah.ResumeLayout();
    }

    private async Task RefreshStorageAsync()
    {
        var svc = OfflineContentService.Instance;
        var report = await Task.Run(() =>
        {
            svc.InvalidateAll();
            return svc.GetStorageAsync().GetAwaiter().GetResult();
        });
        FillStorageGrid(report);
        await RefreshAllAsync();
    }

    private void FillStorageGrid(StorageReport report)
    {
        _storageRows = report.Items;
        _gridStorage.SuspendLayout();
        _gridStorage.Rows.Clear();
        foreach (var it in report.Items)
        {
            _gridStorage.Rows.Add(it.Label, FormatSize(it.Bytes));
        }
        _gridStorage.ResumeLayout();

        // combo hapus resource
        _cmbDelResource.Items.Clear();
        var list = new List<(string Kind, string Key, string Display, string Folder)>();
        foreach (var mt in MushafTypes.All) list.Add(("mushaf", mt.Key, "Mushaf " + mt.Display, ""));
        foreach (var t in Translations.All) list.Add(("teks", t.Key, "Terjemahan " + t.Display, ""));
        foreach (var t in Tafsirs.All) list.Add(("tafsir", t.Key, "Tafsir " + t.Display, ""));
        list.Add(("hilites", "hilites", "Hilite ayat", ""));
        foreach (var r in Reciters.All) list.Add(("audio", r.Key, "Audio " + r.Display, r.Folder));
        foreach (var v in VoiceTranslations.All) list.Add(("audio", v.Key, "Voice " + v.Display, v.Folder));
        _delResources = list.ToArray();
        foreach (var it in list) _cmbDelResource.Items.Add(new ComboItem(it.Display, (it.Kind, it.Key, it.Display, it.Folder)));
        if (_cmbDelResource.Items.Count > 0 && _cmbDelResource.SelectedIndex < 0) _cmbDelResource.SelectedIndex = 0;
    }

    private void FocusGotoAyah()
    {
        if (_gotoSurah > 0)
        {
            _cmbAyatSurah.SelectedIndex = Math.Clamp(_gotoSurah - 1, 0, _cmbAyatSurah.Items.Count - 1);
            _tabs.SelectedIndex = 2;
        }
    }

    // ================= JOB RUNNER =================

    private async Task RunJobsAsync(IEnumerable<DownloadManager.DownloadItem> jobs)
    {
        if (_running)
        {
            MessageBox.Show(this, "Masih ada unduhan yang berjalan.", "Unduhan");
            return;
        }
        var list = jobs.ToList();
        if (list.Count == 0)
        {
            MessageBox.Show(this, "Tidak ada item untuk diunduh — semuanya sudah tersedia ✓", "Unduhan");
            return;
        }

        _running = true;
        _btnCancelJobs.Enabled = true;
        _jobCts = new CancellationTokenSource();
        _bar.Value = 0;

        var progress = new Progress<DownloadManager.DownloadProgress>(p =>
        {
            _bar.Maximum = Math.Max(1, p.Total);
            _bar.Value = Math.Min(_bar.Maximum, p.Done);
            double mbps = p.BytesPerSec / (1024.0 * 1024);
            _lblProgress.Text =
                $"{p.Done}/{p.Total} — baru {p.Downloaded}, ada {p.Skipped}, gagal {p.Failed}  •  {p.Bytes / (1024.0 * 1024):0.0} MB  •  {mbps:0.00} MB/s"
                + (p.Eta > TimeSpan.Zero ? $"  •  ETA {p.Eta:hh\\:mm\\:ss}" : "")
                + (p.Current.Length > 0 ? $"  •  {p.Current}" : "");
        });

        try
        {
            var res = await DownloadManager.Shared.RunAsync(list, progress, _jobCts.Token);
            OfflineContentService.Instance.InvalidateAll();
            await RefreshAllAsync();
            string msg = res.Cancelled
                ? $"Dibatalkan pada {res.Downloaded + res.Skipped + res.Failed}/{list.Count}. Jalankan lagi untuk melanjutkan (resume otomatis)."
                : res.Failed == 0
                    ? $"Selesai ✓ — {res.Downloaded} diunduh, {res.Skipped} sudah ada."
                    : $"Selesai dengan {res.Failed} gagal — coba ulangi (resume otomatis).";
            _lblProgress.Text = msg;
            if (res.Failed > 0 && !res.Cancelled)
            {
                var sample = string.Join("\n", res.Errors.Take(5));
                MessageBox.Show(this, "Sebagian gagal:\n" + sample, "Unduhan", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }
        catch (Exception ex)
        {
            _lblProgress.Text = "Unduhan gagal: " + ex.Message;
        }
        finally
        {
            _running = false;
            _btnCancelJobs.Enabled = false;
        }
    }
}
