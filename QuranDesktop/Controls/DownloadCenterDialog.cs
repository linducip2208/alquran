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

    // Tab Qari: filter status + panel qari aktif
    private readonly ComboBox _cmbFilterQari = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 150 };
    private readonly CardPanel _cardActiveQari = new() { Margin = new Padding(0) };
    private readonly Label _lblQariStats = new()
    {
        AutoSize = true,
        ForeColor = Color.FromArgb(110, 110, 115),
        Margin = new Padding(8, 2, 8, 4),
    };

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
    /// <summary>(R) Progress bar kedua: byte file yang sedang diunduh.</summary>
    private readonly ProgressBar _barFile = new() { Dock = DockStyle.Fill, Height = 12 };
    private readonly Label _lblProgress = new() { AutoSize = true, Text = "Siap." };
    private readonly Button _btnCancelJobs = new() { Text = "Batal", Width = 80, Enabled = false };

    private CancellationTokenSource? _scanCts;
    private CancellationTokenSource? _jobCts;
    private bool _running;
    private bool _scanning;

    private SurahOfflineSummary[] _surahRows = Array.Empty<SurahOfflineSummary>();
    private List<AyahRow> _filteredAyat = new();
    /// <summary>(F) HANYA qari (Reciters.All — 43). Voice translation TIDAK masuk daftar ini.</summary>
    private List<ReciterSummary> _qariRows = new();
    /// <summary>(J) Qari yang sudah discan — kolom "Scan" menampilkan ✓.</summary>
    private readonly HashSet<string> _scanDone = new(StringComparer.Ordinal);
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
        Shown += (_, _) => OfflineMigrator.EnsureStarted();
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

        var cards = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 5, AutoSize = true };
        for (int i = 0; i < 5; i++) cards.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 20f));
        foreach (var key in new[] { "card.mushaf", "card.hilite", "card.arab", "card.trans", "card.tafsir", "card.audio", "card.qari", "card.storage", "card.status", "card.hint" })
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

        // (E) profil unduhan WAJIB masuk layout — bug lama: panel dibuat tapi tidak pernah ditambahkan
        layout.Controls.Add(profile, 0, 1);
        layout.SetColumnSpan(profile, 2);

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
        layout.Controls.Add(actions, 0, 2);
        layout.SetColumnSpan(actions, 2);
        var hint = new Label
        {
            Text = "Semua unduhan hanya mengambil file yang belum ada/rusak. File diunduh ke .part lalu dipindah otomatis setelah valid.",
            AutoSize = true,
            MaximumSize = new Size(860, 0),
            ForeColor = Color.FromArgb(110, 110, 115),
            Padding = new Padding(10, 6, 10, 4),
        };
        layout.Controls.Add(hint, 0, 3);
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

        _cmbFilterQari.Items.AddRange(new object[] { "Semua", "Sudah ada", "Lengkap", "Sebagian", "Belum diunduh" });
        _cmbFilterQari.SelectedIndex = 0;
        _cmbFilterQari.SelectedIndexChanged += (_, _) => FillQariGrid();

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
        foreach (var r in Reciters.All) _cmbAyatQari.Items.Add(new ComboItem(r.Display, r));
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
        _cmbAyatQari.SelectedIndexChanged += async (_, _) => { await RebuildAyatRowsAsync(); HighlightActiveQariRow(); UpdateQariActivePanel(); };
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
            Col("qari", "Qari", 22),
            Col("folder", "Folder", 15),
            Col("downloaded", "Downloaded", 9),
            Col("total", "Total", 8),
            Col("missing", "Kurang", 8),
            Col("progress", "Progress", 8),
            Col("size", "Ukuran", 9),
            Col("status", "Status", 12),
            Col("scan", "Scan", 9),
        });
        _gridQari.SelectionChanged += (_, _) => { HighlightActiveQariRow(); FillQariSurahGrid(); };
        // (L) klik baris qari = qari tersebut menjadi qari AKTIF (sinkron combo ayat + profil)
        _gridQari.CellClick += (_, e) => { if (e.RowIndex >= 0) SelectQariAsActive(e.RowIndex); };
        _gridQari.CellDoubleClick += (_, e) => { if (e.RowIndex >= 0) SelectQariAsActive(e.RowIndex); };

        _gridQariSurah.Columns.AddRange(new DataGridViewColumn[]
        {
            Col("qsurah", "Surah", 42),
            Col("qayat", "Ayat", 10),
            Col("qok", "Downloaded", 14),
            Col("qmiss", "Kurang", 10),
            Col("qstatus", "Status", 20),
        });

        // Panel qari aktif: nama + progress + aksi — download utama SELALU mengikuti qari terpilih
        var activePanel = new Panel { Dock = DockStyle.Top, AutoSize = true, Padding = new Padding(8, 6, 8, 2) };
        var activeTable = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, AutoSize = true };
        activeTable.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        activeTable.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        var activeLeft = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, WrapContents = true };
        activeLeft.Controls.Add(_cardActiveQari);
        var activeStats = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, WrapContents = true };
        activeStats.Controls.Add(_lblQariStats);
        var tbl = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 2, AutoSize = true };
        tbl.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        tbl.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        tbl.Controls.Add(activeLeft, 0, 0);
        tbl.Controls.Add(activeStats, 0, 1);
        activeTable.Controls.Add(tbl, 0, 0);

        var activeButtons = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, WrapContents = true };
        var btnActiveMissing = ActionButton("Unduh Yang Kurang (qari aktif)", 220);
        var btnActiveAll = ActionButton("Unduh Semua Qari Ini", 180);
        var btnActiveVerify = ActionButton("Verifikasi", 100);
        var btnActiveFolder = ActionButton("Buka Folder Qari", 140);
        var btnAllReciters = ActionButton("Unduh Semua Qari…", 150);
        // (K) scan qari terpilih saja — bukan 42 qari lain
        var btnScanThis = ActionButton("Scan Qari Ini", 130);
        // (I) scan semua qari dengan progress live per qari
        var btnScanAll = ActionButton("Scan Semua Qari", 150);
        btnActiveMissing.Click += async (_, _) => await DownloadReciterAsync(activeOnly: true, all: false);
        btnActiveAll.Click += async (_, _) => await DownloadReciterAsync(activeOnly: true, all: true);
        btnActiveVerify.Click += async (_, _) => { OfflineContentService.Instance.ClearReciterAudioCache(); await RefreshAllAsync(); };
        btnActiveFolder.Click += (_, _) => OpenFolder(Path.Combine(OfflineContentService.Instance.AudioDir, ActiveReciter().Folder));
        btnAllReciters.Click += async (_, _) => await DownloadAllRecitersAsync();
        btnScanThis.Click += async (_, _) => await ScanOneQariAsync();
        btnScanAll.Click += async (_, _) => await ScanAllQarisLiveAsync();
        activeButtons.Controls.AddRange(new Control[] { btnActiveMissing, btnActiveAll, btnActiveVerify, btnActiveFolder, btnAllReciters, btnScanThis, btnScanAll });
        activeTable.Controls.Add(activeButtons, 1, 0);
        activePanel.Controls.Add(activeTable);

        // tombol pada baris breakdown per surah
        var detailButtons = new FlowLayoutPanel { Dock = DockStyle.Top, AutoSize = true, WrapContents = true, Padding = new Padding(6, 2, 6, 6) };
        var btnDlMissing = ActionButton("Unduh Yang Kurang untuk Qari Ini", 240);
        var btnDlAll = ActionButton("Unduh Semua", 130);
        var btnDel = ActionButton("Hapus Audio Qari", 150);
        btnDlMissing.Click += async (_, _) => await DownloadReciterAsync(activeOnly: false, all: false);
        btnDlAll.Click += async (_, _) => await DownloadReciterAsync(activeOnly: false, all: true);
        btnDel.Click += async (_, _) => await DeleteReciterAsync();
        detailButtons.Controls.AddRange(new Control[] { btnDlMissing, btnDlAll, btnDel });

        var split = new SplitContainer { Dock = DockStyle.Fill, Orientation = Orientation.Horizontal };
        split.Panel1.Controls.Add(_gridQari);
        split.Panel2.Controls.Add(_gridQariSurah);
        split.SplitterDistance = 340;
        split.Panel2MinSize = 120;

        page.Controls.Add(split);
        page.Controls.Add(detailButtons);
        page.Controls.Add(activePanel);
        page.Controls.Add(Toolbar(
            new Label { Text = "Filter:", AutoSize = true, Margin = new Padding(2, 8, 2, 0) },
            _cmbFilterQari,
            new Label
            {
                Text = "Klik qari untuk memilih & melihat rincian per surah. Unduhan utama mengikuti qari aktif — bukan semua qari.",
                AutoSize = true,
                ForeColor = Color.DimGray,
                Margin = new Padding(8, 8, 4, 0),
            }));
        return page;
    }

    /// <summary>Qari aktif = qari terpilih di tab Ayat (fallback qari dari MainForm).</summary>
    private Reciter ActiveReciter()
    {
        // (D) value combo qari adalah Reciter — JANGAN cast ke string
        if (_cmbAyatQari.SelectedItem is ComboItem ci && ci.Value is Reciter r)
        {
            return Reciters.Find(r.Key) ?? r;
        }
        return Reciters.Find(_qareeKey) ?? Reciters.All[0];
    }

    /// <summary>(D) Qari dari combo profil — value adalah Reciter, bukan string. Tidak pernah InvalidCastException.</summary>
    private Reciter ProfileReciter()
        => ResolveProfileReciter(_cmbProfileQari.SelectedItem as ComboItem, _qareeKey);

    /// <summary>(D) Inti resolver qari profil — static agar bisa di-regression-test via selftest:
    /// value combo WAJIB diperlakukan sebagai Reciter (bukan cast ke string).</summary>
    internal static Reciter ResolveProfileReciter(ComboItem? selected, string fallbackKey)
    {
        if (selected != null && selected.Value is Reciter r)
        {
            return Reciters.Find(r.Key) ?? r;
        }
        return Reciters.Find(fallbackKey) ?? Reciters.All[0];
    }

    /// <summary>(L) Klik baris qari → qari itu menjadi AKTIF: sinkron combo tab Ayat & combo profil,
    /// lalu refresh panel & grid per surah. Download berikutnya mengikuti qari ini.</summary>
    private void SelectQariAsActive(int rowIndex)
    {
        if (rowIndex < 0 || rowIndex >= _qariView.Count) return;
        var row = _qariView[rowIndex];
        var rec = Reciters.Find(row.Key);
        if (rec == null) return; // voice translation tidak bisa jadi qari aktif
        for (int i = 0; i < _cmbAyatQari.Items.Count; i++)
        {
            if (_cmbAyatQari.Items[i] is ComboItem ci && ci.Value is Reciter r && r.Key == rec.Key)
            {
                if (_cmbAyatQari.SelectedIndex != i) _cmbAyatQari.SelectedIndex = i; // memicu UpdateQariActivePanel via event
                else UpdateQariActivePanel();
                break;
        }
        }
        for (int i = 0; i < _cmbProfileQari.Items.Count; i++)
        {
            if (_cmbProfileQari.Items[i] is ComboItem ci && ci.Value is Reciter r && r.Key == rec.Key)
            {
                _cmbProfileQari.SelectedIndex = i;
                break;
            }
        }
        HighlightActiveQariRow();
        FillQariSurahGrid();
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
        var btnCleanTemp = new Button { Text = "Bersihkan folder temp", Width = 160 };
        var btnDel = new Button { Text = "Hapus Resource Terpilih…", Width = 200 };
        var btnVerify = new Button { Text = "Verifikasi Semua", Width = 130 };
        btnOpen.Click += (_, _) => OpenFolder(OfflineContentService.Instance.CacheRoot);
        btnClean.Click += async (_, _) =>
        {
            if (MessageBox.Show(this, "Hapus semua file .part (unduhan belum selesai)?",
                    "Bersihkan .part", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;
            int n = OfflineContentService.Instance.CleanPartFiles();
            MessageBox.Show(this, $"{n} file .part dibersihkan.", "Bersihkan .part");
            await RefreshStorageAsync();
        };
        // (AB) temp juga tetap di dalam aplikasi: downloads/temp/ — bukan %TEMP% Windows
        btnCleanTemp.Click += async (_, _) =>
        {
            if (MessageBox.Show(this, "Hapus seluruh isi downloads/temp/?",
                    "Bersihkan temp", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;
            int n = OfflineContentService.Instance.CleanTempDir();
            MessageBox.Show(this, $"{n} file temp dibersihkan.", "Bersihkan temp");
            await RefreshStorageAsync();
        };
        btnDel.Click += async (_, _) => await DeleteResourceAsync();
        btnVerify.Click += async (_, _) =>
        {
            OfflineContentService.Instance.InvalidateAll();
            await RefreshAllAsync(deep: true);
            MessageBox.Show(this, "Verifikasi selesai — inventory dimuat ulang dari file aktual.", "Verifikasi");
        };
        buttons.Controls.AddRange(new Control[] { btnOpen, btnClean, btnCleanTemp, btnDel, btnVerify });

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
        var panel = new Panel { Dock = DockStyle.Fill, Height = 78, Padding = new Padding(10, 6, 10, 8), BackColor = Color.FromArgb(250, 250, 249) };
        panel.Paint += (_, e) =>
        {
            using var pen = new Pen(Color.FromArgb(228, 228, 230));
            e.Graphics.DrawLine(pen, 0, 0, panel.Width, 0);
        };
        _bar.Height = 18;
        _bar.Margin = new Padding(0, 4, 8, 2);
        _barFile.Height = 10;
        _barFile.Margin = new Padding(0, 0, 8, 2);
        _btnCancelJobs.Height = 28;
        _btnCancelJobs.Margin = new Padding(4, 0, 0, 0);
        _lblProgress.ForeColor = Color.FromArgb(70, 70, 75);
        _lblProgress.Margin = new Padding(1, 3, 0, 0);
        _lblProgress.AutoEllipsis = true;
        var table = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 3 };
        table.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        table.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        table.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        table.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        table.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        table.Controls.Add(_bar, 0, 0);
        table.Controls.Add(_btnCancelJobs, 1, 0);
        // (R) progress bar file aktif — di bawah bar overall
        table.Controls.Add(_barFile, 0, 1);
        table.SetColumnSpan(_barFile, 2);
        table.Controls.Add(_lblProgress, 0, 2);
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
        // (D) value combo qari = Reciter
        var qari = _cmbAyatQari.SelectedItem is ComboItem q && q.Value is Reciter r
            ? Reciters.Find(r.Key) ?? r
            : Reciters.Find(_qareeKey) ?? Reciters.All[0];
        return (trans, tafsir, qari);
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
            // (F) HANYA Reciters.All (43 qari) — VoiceTranslations BUKAN qari dan punya inventory sendiri
            var reciterTask = Task.Run(() =>
            {
                var list = new List<ReciterSummary>();
                foreach (var r in Reciters.All)
                {
                    ct.ThrowIfCancellationRequested();
                    list.Add(svc.ScanReciter(r));
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
            var activeQari = Reciters.Find(_qareeKey) ?? Reciters.All[0];
            var surahTask = Task.Run(() =>
            {
                var list = new SurahOfflineSummary[QuranData.SurahCount];
                for (int s = 1; s <= QuranData.SurahCount; s++)
                {
                    ct.ThrowIfCancellationRequested();
                    list[s - 1] = svc.ScanSurah(s, _mushafKey,
                        new[] { _transKey }, new[] { _tafsirKey }, new[] { activeQari });
                }
                // deteksi file rusak di background (sekalian menghangatkan cache tarjama)
                bool corrupt = false;
                for (int s = 1; s <= QuranData.SurahCount && !corrupt; s++)
                {
                    var st = svc.GetTarjamaStatus(_transKey, s);
                    corrupt = st.FileValid
                              && st.AyatFound < QuranData.SurahAyahCount(s)
                              && st.AyatFound > 0;
                }
                return (List: list, Corrupt: corrupt);
            }, ct);
            var mk0 = MushafTypes.ResolveMushaf(_mushafKey);
            var hiliteTask = Task.Run(() =>
            {
                int total = QuranData.PageCount(mk0.PageKey);
                int ok = 0;
                for (int p = 1; p <= total; p++)
                {
                    ct.ThrowIfCancellationRequested();
                    if (svc.GetHiliteStatus(mk0.Key, p)) ok++;
                }
                return (Ok: ok, Total: total);
            }, ct);

            // SEMUA scan di background — UI thread tidak pernah memblokir IO disk
            await Task.WhenAll(mushafTask, reciterTask, storageTask, textTask, surahTask, hiliteTask);

            var mushafs = mushafTask.Result;
            var storage = storageTask.Result;
            var texts = textTask.Result;
            var surahs = surahTask.Result.List;
            var reciters = reciterTask.Result;

            // (V) card mushaf mengikuti mushaf AKTIF — bukan selalu mushafs[0]
            var activeMushaf = mushafs.FirstOrDefault(x => x.Key == MushafTypes.ResolveMushaf(_mushafKey).Key) ?? mushafs[0];
            SetCard("card.mushaf", "Mushaf", $"{activeMushaf.Display} — {activeMushaf.Pages}/{activeMushaf.PagesTotal} halaman",
                activeMushaf.Pages == activeMushaf.PagesTotal);
            SetCard("card.hilite", "Hilite ayat", $"{hiliteTask.Result.Ok}/{hiliteTask.Result.Total} halaman", hiliteTask.Result.Ok == hiliteTask.Result.Total);
            SetCard("card.arab", "Teks Arab", $"{texts[0].AyatFound}/{texts[0].AyatTotal} ayat", texts[0].AyatFound == texts[0].AyatTotal);
            SetCard("card.trans", "Terjemahan aktif", $"{texts[1].AyatFound}/{texts[1].AyatTotal} ayat", texts[1].AyatFound == texts[1].AyatTotal);
            SetCard("card.tafsir", "Tafsir aktif", $"{texts[2].AyatFound}/{texts[2].AyatTotal} ayat", texts[2].AyatFound == texts[2].AyatTotal);

            _surahRows = surahs;
            FillSurahGrid();

            _qariRows = reciters;
            foreach (var r in reciters) _scanDone.Add(r.Key);
            FillQariGrid();
            UpdateQariCards();

            SetCard("card.storage", "Total storage", FormatSize(storage.TotalBytes), null);
            SetCard("card.status", "Status", StatusSummaryText(surahs, activeMushaf, texts, surahTask.Result.Corrupt), null);
            SetCard("card.hint", "Tips", "Buka tab Ayat untuk status per ayat (6.236 ayat). Klik baris untuk detail & tombol unduh per ayat.", null);

            FillStorageGrid(storage);
            _lblProgress.Text = deep ? "Scan & verifikasi selesai." : "Scan selesai.";
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            // scan gagal → jangan biarkan UI kosong/membingungkan; isi placeholder jelas + catat ke log
            Program.Log(ex);
            _lblProgress.Text = "Scan gagal: " + ex.Message;
            foreach (var key in new[] { "card.mushaf", "card.hilite", "card.arab", "card.trans", "card.tafsir", "card.audio", "card.qari", "card.storage", "card.status" })
            {
                SetCard(key, key.Split('.')[1], "Data belum tersedia karena scan gagal.", null);
            }
            SetCard("card.hint", "Tips", "Coba [Scan Ulang] atau [Verifikasi]. Pastikan disk cache dapat diakses.", null);
        }
        finally
        {
            _scanning = false;
        }
    }

    /// <summary>Rescan penuh (dipakai tombol Scan Ulang & harness test).</summary>
    public async Task RescanAsync()
    {
        OfflineContentService.Instance.InvalidateAll();
        await RefreshAllAsync();
    }

    internal string ProgressText => _lblProgress.Text;

    private string StatusSummaryText(SurahOfflineSummary[] surahs, MushafPageSummary mushaf, List<TextKeySummary> texts, bool corrupt = false)
    {
        if (surahs.All(s => s.Complete) && mushaf.Pages == mushaf.PagesTotal
            && texts.All(t => t.AyatFound == t.AyatTotal)) return "✓ Lengkap";
        bool any = surahs.Any(s => s.Partial || s.Complete) || mushaf.Pages > 0 || texts.Any(t => t.AyatFound > 0);
        if (!any) return "Belum diunduh";
        return corrupt ? "! Ada file rusak/tidak lengkap" : "Sebagian";
    }

    /// <summary>(AF) Card audio qari aktif + card qari tersimpan — SELALU basis 43 qari (tanpa voice).</summary>
    private void UpdateQariCards()
    {
        var active = _qariRows.FirstOrDefault(r => r.Key == ActiveReciter().Key);
        if (active != null)
        {
            double pctA = active.Total <= 0 ? 0 : active.Valid * 100.0 / active.Total;
            SetCard("card.audio", "Audio qari aktif",
                $"{Reciters.Find(active.Key)?.Display ?? active.Key}\n{Num(active.Valid)} / {Num(active.Total)} ayat — {pctA:0.00}% • {FormatSize(active.Bytes)}",
                active.Valid == active.Total);
        }
        int withDownloads = _qariRows.Count(x => x.Valid > 0);
        int completeQ = _qariRows.Count(x => x.Total > 0 && x.Valid == x.Total);
        int partialQ = _qariRows.Count(x => x.Valid > 0 && x.Total > 0 && x.Valid < x.Total);
        int noneQ = _qariRows.Count - withDownloads;
        SetCard("card.qari", "Qari tersimpan",
            $"{withDownloads} / {_qariRows.Count} qari\nLengkap {completeQ} • Sebagian {partialQ} • Belum {noneQ}",
            withDownloads == _qariRows.Count && _qariRows.Count > 0 ? true : withDownloads > 0 ? null : false);
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
        // (D) value combo qari = Reciter (bukan string) — jangan cast
        var reciter = ProfileReciter();
        var audio = _chkAudio.Checked ? new[] { reciter.Folder } : Array.Empty<string>();
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
        // (O) BuildJobs di background — daftar 6.236+ job tidak memblokir UI
        await RunJobsAsync(async ct => await Task.Run(() => DownloadManager.BuildJobs(scope), ct));
    }

    private async Task DownloadMissingActiveAsync()
    {
        var scope = BuildActiveScope();
        // (O) BuildJobs di background
        await RunJobsAsync(async ct => await Task.Run(() => DownloadManager.BuildJobs(scope), ct));
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
            await RunJobsAsync(_ => Task.FromResult(items), new[] { qari });
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
                    Rel = $"audio/{r.Folder}/{row.S:D3}{row.A:D3}.mp3",
                    Url = KsuAudio.AyahUrl(r.Folder, row.S, row.A),
                });
            }
        }
        if (items.Count == 0)
        {
            MessageBox.Show(this, "Semua resource ayat ini sudah tersedia offline ✓", "Unduh ayat");
            return;
        }
        await RunJobsAsync(_ => Task.FromResult(items));
    }

    /// <summary>
    /// Unduh audio untuk SATU qari.
    /// activeOnly=true → selalu qari AKTIF (tombol panel atas), abaikan baris terpilih.
    /// activeOnly=false → qari yang diklik di daftar (tombol detail).
    /// all=false → hanya ayat yang kurang (progress akurat); all=true → seluruh 6236 (skip otomatis yang valid).
    /// </summary>
    private async Task DownloadReciterAsync(bool activeOnly, bool all)
    {
        ReciterSummary row;
        if (activeOnly)
        {
            var rec = ActiveReciter();
            row = _qariRows.FirstOrDefault(r => r.Key == rec.Key)
                  ?? OfflineContentService.Instance.ScanReciter(rec);
        }
        else
        {
            if (_gridQari.CurrentRow?.Index is not int idx || idx >= _qariView.Count) return;
            row = _qariView[idx];
        }
        var rec2 = Reciters.Find(row.Key);
        if (rec2 == null) return; // voice translation tidak diunduh via tombol qari
        List<DownloadManager.DownloadItem> jobs;
        if (!all)
        {
            // hanya file yang kurang — progress bar akurat (cek status di background)
            var svc = OfflineContentService.Instance;
            jobs = await Task.Run(() =>
            {
                var list = new List<DownloadManager.DownloadItem>();
                for (int s = 1; s <= QuranData.SurahCount; s++)
                {
                    int n = QuranData.SurahAyahCount(s);
                    for (int a = 1; a <= n; a++)
                    {
                        if (!svc.GetAudioStatus(rec2.Folder, s, a).IsValid)
                        {
                            list.Add(new DownloadManager.DownloadItem
                            {
                                Label = $"Audio {s}:{a} ({rec2.Display})",
                                Kind = DownloadManager.JobKind.File,
                                Rel = $"audio/{rec2.Folder}/{s:D3}{a:D3}.mp3",
                                Url = KsuAudio.AyahUrl(rec2.Folder, s, a),
                            });
                        }
                    }
                }
                return list;
            }, CancellationToken.None);
        }
        else
        {
            // scope SATU qari — bukan Reciters.All; (O) BuildJobs di background
            jobs = await Task.Run(() => DownloadManager.BuildJobs(new DownloadManager.DownloadScope
            {
                Mushaf = false, Hilites = false, Arab = false,
                AudioFolders = new[] { rec2.Folder },
            }), CancellationToken.None);
        }
        if (jobs.Count == 0 && all)
        {
            MessageBox.Show(this, $"Audio {rec2.Display} sudah lengkap ✓", "Unduh audio");
            return;
        }
        // (T) progress overall = ayat qari INI (6236), bukan gabungan semua qari
        await RunJobsAsync(ct => Task.FromResult(jobs), new[] { rec2 });
    }

    /// <summary>Unduh audio SEMUA qari — aksi besar, wajib konfirmasi eksplisit (U).</summary>
    private async Task DownloadAllRecitersAsync()
    {
        int totalReciters = Reciters.All.Count;
        long totalJobs = (long)totalReciters * QuranData.TotalAyahCount;
        long estBytes = _qariRows.Sum(r => r.Bytes);
        if (MessageBox.Show(this,
                $"Unduh audio SEMUA {totalReciters} qari?\n\n" +
                $"• {totalReciters} qari × {Num(QuranData.TotalAyahCount)} ayat per qari\n" +
                $"• Maksimum {Num((int)Math.Min(totalJobs, int.MaxValue))} file MP3\n" +
                $"Perkiraan storage tambahan BESAR (puluhan GB, tergantung kualitas).\n" +
                $"Sudah tersimpan: {FormatSize(estBytes)}.\n\nLanjutkan?",
                "Konfirmasi Unduh Semua Qari", MessageBoxButtons.YesNo, MessageBoxIcon.Warning,
                MessageBoxDefaultButton.Button2) != DialogResult.Yes)
        {
            return;
        }
        // (O) daftar ±268 ribu job dibangun di background; (U) label diawali [i/43] per qari
        await RunJobsAsync(async ct => await Task.Run(() =>
        {
            var jobs = new List<DownloadManager.DownloadItem>(totalJobs > int.MaxValue ? int.MaxValue : (int)totalJobs);
            for (int i = 0; i < totalReciters; i++)
            {
                ct.ThrowIfCancellationRequested();
                var rec = Reciters.All[i];
                var scopeJobs = DownloadManager.BuildJobs(new DownloadManager.DownloadScope
                {
                    Mushaf = false, Hilites = false, Arab = false,
                    AudioFolders = new[] { rec.Folder },
                });
                string prefix = $"[{i + 1}/{totalReciters}] ";
                foreach (var j in scopeJobs)
                {
                    jobs.Add(new DownloadManager.DownloadItem
                    {
                        Label = prefix + j.Label,
                        Kind = j.Kind,
                        Url = j.Url,
                        Rel = j.Rel,
                        TextKey = j.TextKey,
                        Surah = j.Surah,
                        Ayah = j.Ayah,
                        MinBytes = j.MinBytes,
                    });
                }
            }
            return jobs;
        }, ct), Reciters.All.ToList());
    }

    private async Task DeleteReciterAsync()
    {
        if (_gridQari.CurrentRow?.Index is not int idx || idx >= _qariView.Count) return;
        var row = _qariView[idx];
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
            "voice" => OfflineContentService.Instance.DeleteVoiceAudio(folder),
            _ => OfflineContentService.Instance.DeleteReciterAudio(folder),
        };
        _lblProgress.Text = $"{n} file dihapus ({label}).";
        await RefreshStorageAsync();
    }

    /// <summary>Daftar qari setelah filter + urutan tampil (baris grid ↔ list ini).</summary>
    private List<ReciterSummary> _qariView = new();

    private static string Num(int n) => n.ToString("N0", _idCulture);
    private static readonly System.Globalization.CultureInfo _idCulture =
        System.Globalization.CultureInfo.GetCultureInfo("id-ID");

    /// <summary>Urutan tampil: qari aktif dulu, lalu Lengkap → Sebagian → Belum diunduh.</summary>
    private int QariOrder(ReciterSummary r)
    {
        if (r.Key == ActiveReciter().Key) return 0;
        if (r.Total > 0 && r.Valid >= r.Total) return 1;
        if (r.Valid > 0) return 2;
        return 3;
    }

    private string QariStatusText(ReciterSummary r)
    {
        if (r.Total <= 0) return "—";
        if (r.Valid >= r.Total) return "✓ Lengkap";
        if (r.Valid > 0) return "Sebagian";
        return "Belum diunduh";
    }

    private void FillQariGrid()
    {
        string filter = _cmbFilterQari.SelectedItem?.ToString() ?? "Semua";
        _qariView = _qariRows
            .Where(r => filter switch
            {
                "Sudah ada" => r.Valid > 0,
                "Lengkap" => r.Total > 0 && r.Valid >= r.Total,
                "Sebagian" => r.Valid > 0 && r.Valid < r.Total,
                "Belum diunduh" => r.Valid == 0,
                _ => true,
            })
            .OrderBy(QariOrder)
            .ThenByDescending(r => r.Valid)
            .ThenBy(r => r.Display)
            .ToList();

        _gridQari.SuspendLayout();
        _gridQari.Rows.Clear();
        foreach (var r in _qariView)
        {
            string progress = r.Total <= 0 ? "—" : $"{r.Valid * 100.0 / r.Total:0.00}%";
            // (J) kolom Folder + Scan: folder unik qari & status apakah sudah discan
            _gridQari.Rows.Add(r.Display, r.Folder, Num(r.Valid), Num(r.Total), Num(Math.Max(0, r.Total - r.Valid)),
                progress, FormatSize(r.Bytes), QariStatusText(r), _scanDone.Contains(r.Key) ? "✓" : "—");
        }
        _gridQari.ResumeLayout();
        HighlightActiveQariRow();
        UpdateQariActivePanel();
    }

    /// <summary>Tandai baris qari aktif + panel ringkasan qari aktif.</summary>
    private void HighlightActiveQariRow()
    {
        string activeKey = ActiveReciter().Key;
        foreach (DataGridViewRow row in _gridQari.Rows)
        {
            if (row.Index < 0 || row.Index >= _qariView.Count) continue;
            bool isActive = _qariView[row.Index].Key == activeKey;
            row.DefaultCellStyle.Font = new Font(_gridQari.Font, isActive ? FontStyle.Bold : FontStyle.Regular);
            row.DefaultCellStyle.ForeColor = isActive ? Color.FromArgb(20, 90, 170) : Color.Black;
        }
    }

    /// <summary>Panel ringkasan qari aktif: nama, downloaded/total, kurang, %, ukuran, status.</summary>
    private void UpdateQariActivePanel()
    {
        var rec = ActiveReciter();
        var row = _qariRows.FirstOrDefault(r => r.Key == rec.Key);
        if (row == null) return;
        int missing = Math.Max(0, row.Total - row.Valid);
        double pct = row.Total <= 0 ? 0 : row.Valid * 100.0 / row.Total;
        _cardActiveQari.Title = "Qari aktif";
        _cardActiveQari.Value = $"{rec.Display} — {Num(row.Valid)} / {Num(row.Total)} ayat • Kurang {Num(missing)} • {pct:0.00}% • {FormatSize(row.Bytes)} • {QariStatusText(row)}";
        _cardActiveQari.SetState(row.Total > 0 && row.Valid >= row.Total ? true : row.Valid > 0 ? null : false);

        int totalReciters = _qariRows.Count;
        int withDownloads = _qariRows.Count(x => x.Valid > 0);
        int complete = _qariRows.Count(x => x.Total > 0 && x.Valid == x.Total);
        int partial = _qariRows.Count(x => x.Valid > 0 && x.Total > 0 && x.Valid < x.Total);
        int none = totalReciters - withDownloads;
        // (F) panel stats selalu basis Reciters.All (43) — tanpa voice translation
        _lblQariStats.Text = $"Qari dengan file tersimpan: {withDownloads} / {totalReciters} (dari {Reciters.All.Count} qari)   •   Lengkap: {complete}   •   Sebagian: {partial}   •   Belum diunduh: {none}";
        UpdateQariCards();
    }

    private void FillQariSurahGrid()
    {
        if (_gridQari.CurrentRow?.Index is not int idx || idx >= _qariView.Count)
        {
            _gridQariSurah.Rows.Clear();
            return;
        }
        var row = _qariView[idx];
        var svc = OfflineContentService.Instance;
        _gridQariSurah.SuspendLayout();
        _gridQariSurah.Rows.Clear();
        for (int s = 1; s <= QuranData.SurahCount; s++)
        {
            int n = QuranData.SurahAyahCount(s);
            // pakai hasil breakdown dari scan (tanpa IO disk di UI thread); fallback: hitung cepat
            int ok = row.PerSurah != null ? row.PerSurah[s] : CountValidAyatFast(svc, row.Folder, s, n);
            _gridQariSurah.Rows.Add($"{s}. {SurahList.Get(s).EnglishName}", Num(n),
                $"{Num(ok)} / {Num(n)}", Num(Math.Max(0, n - ok)),
                ok == n ? "✓ Lengkap" : ok > 0 ? "Sebagian" : "Belum diunduh");
        }
        _gridQariSurah.ResumeLayout();
    }

    private static int CountValidAyatFast(OfflineContentService svc, string folder, int surah, int ayatCount)
    {
        int ok = 0;
        for (int a = 1; a <= ayatCount; a++)
        {
            if (svc.GetAudioStatus(folder, surah, a).IsValid) ok++;
        }
        return ok;
    }

    /// <summary>(J) Update sel satu baris grid qari dari hasil scan.</summary>
    private void UpdateQariRowCells(int rowIndex, ReciterSummary r, string scanStatus)
    {
        if (rowIndex < 0 || rowIndex >= _gridQari.Rows.Count) return;
        var row = _gridQari.Rows[rowIndex];
        row.Cells["qari"].Value = r.Display;
        row.Cells["folder"].Value = r.Folder;
        row.Cells["downloaded"].Value = Num(r.Valid);
        row.Cells["total"].Value = Num(r.Total);
        row.Cells["missing"].Value = Num(Math.Max(0, r.Total - r.Valid));
        row.Cells["progress"].Value = r.Total <= 0 ? "—" : $"{r.Valid * 100.0 / r.Total:0.00}%";
        row.Cells["size"].Value = FormatSize(r.Bytes);
        row.Cells["status"].Value = QariStatusText(r);
        row.Cells["scan"].Value = scanStatus;
    }

    /// <summary>(I) Scan SEMUA qari dengan progress live: grid berisi 43 baris sejak awal (status Menunggu),
    /// lalu satu per satu dipindai — baris menampilkan "Memindai…", label menampilkan Qari i/43, file ditemukan,
    /// valid, ukuran. Satu folder = satu enumeration; PerSurah ikut terisi (M).</summary>
    private async Task ScanAllQarisLiveAsync()
    {
        if (_scanning || _running) return;
        _scanning = true;
        _scanCts = new CancellationTokenSource();
        var ct = _scanCts.Token;
        try
        {
            // prefill: 43 baris sesuai urutan Reciters.All — status awal "Menunggu"
            _qariView = Reciters.All.Select(r =>
                _qariRows.FirstOrDefault(x => x.Key == r.Key)
                ?? new ReciterSummary(r.Key, r.Folder, r.Display, 0, QuranData.TotalAyahCount, 0, null)).ToList();
            _gridQari.SuspendLayout();
            _gridQari.Rows.Clear();
            foreach (var r in _qariView)
            {
                string pct = r.Total <= 0 ? "—" : $"{r.Valid * 100.0 / r.Total:0.00}%";
                _gridQari.Rows.Add(r.Display, r.Folder, Num(r.Valid), Num(r.Total), Num(Math.Max(0, r.Total - r.Valid)),
                    pct, FormatSize(r.Bytes), "Menunggu", "…");
            }
            _gridQari.ResumeLayout();

            var progress = new Progress<AudioFolderScanProgress>(p =>
            {
                int idx = p.Index - 1;
                double pctAll = p.Total <= 0 ? 0 : Math.Max(0, p.Index - 1) * 100.0 / p.Total;
                _lblProgress.Text = $"Memindai qari {Math.Min(p.Index, p.Total)} / {p.Total} ({pctAll:0.0}%) — {p.Display}"
                    + $"  •  ditemukan {Num(p.FilesFound)}  •  valid {Num(p.ValidFiles)}  •  {FormatSize(p.Bytes)}";
                if (idx >= 0 && idx < _gridQari.Rows.Count)
                {
                    var row = _gridQari.Rows[idx];
                    row.Cells["size"].Value = FormatSize(p.Bytes);
                    row.Cells["status"].Value = "Memindai…";
                    row.Cells["scan"].Value = "…";
                }
            });

            var svc = OfflineContentService.Instance;
            int total = Reciters.All.Count;
            for (int i = 0; i < total; i++)
            {
                ct.ThrowIfCancellationRequested();
                var rec = Reciters.All[i];
                var sum = await Task.Run(() => svc.ScanReciter(rec, i + 1, total, progress, ct), ct);
                _qariView[i] = sum;
                int ri = _qariRows.FindIndex(r => r.Key == sum.Key);
                if (ri >= 0) _qariRows[ri] = sum; else _qariRows.Add(sum);
                _scanDone.Add(sum.Key);
                UpdateQariRowCells(i, sum, "✓ Selesai");
            }
            HighlightActiveQariRow();
            UpdateQariActivePanel();
            _lblProgress.Text = $"Scan semua qari selesai — {total} qari.";
        }
        catch (OperationCanceledException)
        {
            _lblProgress.Text = "Scan qari dibatalkan.";
        }
        catch (Exception ex)
        {
            Program.Log(ex);
            _lblProgress.Text = "Scan qari gagal: " + ex.Message;
        }
        finally
        {
            _scanning = false;
        }
    }

    /// <summary>(K) Scan Qari Ini: hanya folder qari terpilih di grid — bukan 42 qari lain, mushaf, atau teks.</summary>
    private async Task ScanOneQariAsync()
    {
        if (_scanning) return;
        if (_gridQari.CurrentRow?.Index is not int idx || idx >= _qariView.Count)
        {
            MessageBox.Show(this, "Pilih qari terlebih dahulu.", "Scan Qari Ini");
            return;
        }
        var target = _qariView[idx];
        var rec = Reciters.Find(target.Key);
        if (rec == null) return; // voice translation tidak discan di tab qari
        _scanning = true;
        _scanCts = new CancellationTokenSource();
        var ct = _scanCts.Token;
        try
        {
            _gridQari.Rows[idx].Cells["status"].Value = "Memindai…";
            _gridQari.Rows[idx].Cells["scan"].Value = "…";
            var progress = new Progress<AudioFolderScanProgress>(p =>
            {
                _lblProgress.Text = $"Memindai {p.Display}  •  ditemukan {Num(p.FilesFound)}  •  valid {Num(p.ValidFiles)}  •  {FormatSize(p.Bytes)}";
                if (idx < _gridQari.Rows.Count)
                {
                    _gridQari.Rows[idx].Cells["size"].Value = FormatSize(p.Bytes);
                }
            });
            var sum = await Task.Run(() => OfflineContentService.Instance.ScanReciter(rec, 1, 1, progress, ct), ct);
            int ri = _qariRows.FindIndex(r => r.Key == sum.Key);
            if (ri >= 0) _qariRows[ri] = sum; else _qariRows.Add(sum);
            _qariView[idx] = sum;
            _scanDone.Add(sum.Key);
            UpdateQariRowCells(idx, sum, "✓");
            HighlightActiveQariRow();
            UpdateQariActivePanel();
            _lblProgress.Text = $"Scan {rec.Display} selesai — {Num(sum.Valid)}/{Num(sum.Total)} ayat ({FormatSize(sum.Bytes)}).";
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            Program.Log(ex);
            _lblProgress.Text = "Scan gagal: " + ex.Message;
        }
        finally
        {
            _scanning = false;
        }
    }

    /// <summary>(AG) Refresh TER-target setelah unduhan qari selesai — hanya qari yang diunduh
    /// yang discan ulang. Bukan 43 qari + semua mushaf + teks (itu hanya untuk Scan Ulang/Verifikasi/startup).</summary>
    private async Task TargetedReciterRefreshAsync(IReadOnlyList<Reciter> reciters)
    {
        var svc = OfflineContentService.Instance;
        svc.ClearReciterAudioCache();
        foreach (var rec in reciters)
        {
            var sum = await Task.Run(() => svc.ScanReciter(rec));
            int ri = _qariRows.FindIndex(r => r.Key == sum.Key);
            if (ri >= 0) _qariRows[ri] = sum; else _qariRows.Add(sum);
            _scanDone.Add(sum.Key);
        }
        FillQariGrid();
        UpdateQariActivePanel();
        svc.InvalidateStorage();
        var report = await Task.Run(() => svc.GetStorageAsync().GetAwaiter().GetResult());
        FillStorageGrid(report);
        _lblProgress.Text = "Inventory qari diperbarui (refresh ter-target).";
    }

    private async Task RefreshStorageAsync()
    {
        var svc = OfflineContentService.Instance;
        svc.InvalidateStorage();
        var report = await Task.Run(() => svc.GetStorageAsync().GetAwaiter().GetResult());
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
        foreach (var v in VoiceTranslations.All) list.Add(("voice", v.Key, "Voice " + v.Display, v.Folder));
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

    /// <summary>
    /// (N/O) Jalankan unduhan: UI berubah &lt;300 ms ("Menyiapkan daftar unduhan…" + marquee + Batal enabled),
    /// pembangunan daftar job dijalankan di background (Task.Run), progress live: overall 0/N sejak awal (P),
    /// byte file aktif (Q/R), speed dari byte transfer aktual (S).
    /// recitersTouched diisi → refresh ter-target hanya qari terkait (AG); null → refresh penuh.
    /// </summary>
    private async Task RunJobsAsync(
        Func<CancellationToken, Task<List<DownloadManager.DownloadItem>>> jobsFactory,
        IReadOnlyList<Reciter>? recitersTouched = null)
    {
        if (_running)
        {
            MessageBox.Show(this, "Masih ada unduhan yang berjalan.", "Unduhan");
            return;
        }

        _running = true;
        _btnCancelJobs.Enabled = true;
        _jobCts = new CancellationTokenSource();
        var ct = _jobCts.Token;
        _bar.Style = ProgressBarStyle.Marquee; // respons < 300 ms selama daftar disiapkan
        _barFile.Value = 0;
        _lblProgress.Text = "Menyiapkan daftar unduhan…";

        List<DownloadManager.DownloadItem> list;
        try
        {
            list = await jobsFactory(ct);
        }
        catch (OperationCanceledException)
        {
            _lblProgress.Text = "Dibatalkan sebelum unduhan dimulai.";
            _running = false;
            _btnCancelJobs.Enabled = false;
            _bar.Style = ProgressBarStyle.Continuous;
            return;
        }
        catch (Exception ex)
        {
            _lblProgress.Text = "Gagal menyiapkan unduhan: " + ex.Message;
            _running = false;
            _btnCancelJobs.Enabled = false;
            _bar.Style = ProgressBarStyle.Continuous;
            return;
        }
        if (list.Count == 0)
        {
            MessageBox.Show(this, "Tidak ada item untuk diunduh — semuanya sudah tersedia ✓", "Unduhan");
            _lblProgress.Text = "Tidak ada yang perlu diunduh — semuanya sudah tersedia ✓";
            _running = false;
            _btnCancelJobs.Enabled = false;
            _bar.Style = ProgressBarStyle.Continuous;
            return;
        }

        _bar.Style = ProgressBarStyle.Continuous;
        _bar.Value = 0;

        var progress = new Progress<DownloadManager.DownloadProgress>(p =>
        {
            // (R) Overall: done/total + % — plus info file aktif (CurrentFileRel/Bytes/Total)
            if (p.Total > 0)
            {
                _bar.Maximum = Math.Max(1, p.Total);
                _bar.Value = Math.Min(_bar.Maximum, p.Done);
            }
            double opct = p.Total <= 0 ? 0 : p.Done * 100.0 / p.Total;
            double mbps = p.BytesPerSec / (1024.0 * 1024.0);
            string filePart = "";
            if (p.CurrentFileTotal > 0)
            {
                double fpct = Math.Min(100.0, p.CurrentFileBytes * 100.0 / p.CurrentFileTotal);
                _barFile.Maximum = 100;
                _barFile.Value = Math.Clamp((int)fpct, 0, 100);
                string fname = string.IsNullOrEmpty(p.CurrentFileRel)
                    ? "-"
                    : Path.GetFileName(p.CurrentFileRel.Replace('\\', '/'));
                filePart = $"  •  File {fname}: {FormatSize(p.CurrentFileBytes)} / {FormatSize(p.CurrentFileTotal)} ({fpct:0}%)";
            }
            else
            {
                _barFile.Value = 0;
            }
            _lblProgress.Text =
                $"Overall {p.Done}/{p.Total} ({opct:0.00}%) — baru {p.Downloaded}, ada {p.Skipped}, gagal {p.Failed}"
                + filePart
                + $"  •  {mbps:0.00} MB/s"
                + (p.Eta > TimeSpan.Zero ? $"  •  ETA {p.Eta:hh\\:mm\\:ss}" : "")
                + (p.Current.Length > 0 ? $"  •  {p.Current}" : "");
        });

        try
        {
            var res = await DownloadManager.Shared.RunAsync(list, progress, ct);
            // (AG) targeted refresh untuk unduhan qari; refresh penuh untuk resource lain
            if (recitersTouched is { Count: > 0 })
            {
                await TargetedReciterRefreshAsync(recitersTouched);
            }
            else
            {
                OfflineContentService.Instance.InvalidateAll();
                await RefreshAllAsync();
            }
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
            _bar.Style = ProgressBarStyle.Continuous;
            _barFile.Value = 0;
        }
    }
}
