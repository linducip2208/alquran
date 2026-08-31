using QuranDesktop.Controls;

namespace QuranDesktop;

internal sealed class MainForm : Form
{
    private const int MM_MCINOTIFY = 0x3B9;

    private readonly AppSettings _settings = AppSettings.Current;
    private readonly IAudioEngine _audio;
    private readonly Queue<string> _playQueue = new();

    private CancellationTokenSource? _playCts;
    private int _playToken;
    private int _curSurah = 1;
    private int _curAyah = 1;
    private int _renderedSurah = -1;
    private bool _introPlayed;
    private int _basmalaSurah = -1;
    private int _repeatRemaining;
    private int _rangeLoopsRemaining;
    private bool _uiBusy;
    private List<AyahData> _curAyahs = new();
    private readonly Dictionary<string, Dictionary<int, string>> _tarjamaLocal = new();
    private int _lastTafsirS;
    private int _lastTafsirA;
    private Button _btnOpenTafsir = new();
    private readonly HashSet<(int Surah, int Ayah)> _searchHits = new();

    private readonly Font _arabicFont = MadinahFont.Create(24f);
    private readonly Font _tafsirFont = ResolveFont(
        new[] { "Traditional Arabic", "Scheherazade New", "Amiri", "Segoe UI" },
        14f);
    private Font _transFont = new("Segoe UI", 10.5f);

    private ComboBox _cmbMode = new();
    private ComboBox _cmbMosshaf = new();
    private ComboBox _cmbSurah = new();
    private ComboBox _cmbAyah = new();
    private ComboBox _cmbPage = new();
    private ComboBox _cmbJuz = new();
    private ComboBox _cmbQaree = new();
    private ComboBox _cmbTrans = new();
    private ComboBox _cmbPb = new();
    private ComboBox _cmbTafsir = new();
    private ComboBox _cmbRepeat = new();
    private Button _btnPlayPause = new();
    private Button _btnStop = new();
    private Button _btnPrevAya = new();
    private Button _btnNextAya = new();
    private Button _btnPagePrev = new();
    private Button _btnPageNext = new();
    private Button _btnZoomIn = new();
    private Button _btnZoomOut = new();
    private Button _btnDownload = new();
    private CheckBox _chkAutoNext = new();
    private CheckBox _chkPlayOnClick = new();
    private CheckBox _chkTafsirPanel = new();
    private CheckBox _chkShowTrans = new();
    private CheckBox _chkInlineTafsir = new();
    private CheckBox _chkOverlay = new();
    private CheckBox _chkTeacher = new();
    private CheckBox _chkRepeatRange = new();
    private NumericUpDown _numRangeFrom = new();
    private NumericUpDown _numRangeTo = new();
    private TrackBar _trackVolume = new();
    private TextBox _txtSearch = new();
    private Button _btnSearch = new();
    private Label _lblStatus = new();

    private Controls.TextModeControl _textMode = new();
    private Controls.MushafView _mushafView = new();
    private Panel _mushafRight = new();
    private FlowLayoutPanel _ayahStrip = new();
    private RichTextBox _mushafInfo = new();
    private Controls.HifzControl _hifz = new();
    private Panel _tafsirPanel = new();
    private RichTextBox _tafsirText = new();
    private Label _tafsirHeader = new();
    private Panel _center = new();
    private readonly ToolTip _stripTip = new();
    private Panel _topContainer = new();
    private Button _btnStar = new();
    private Button _btnCard = new();
    private Button _btnFeatures = new();
    private TrackBar _trackSpeed = new();
    private Button _btnInspirasi = new();
    private MiniPlayerForm? _mini;
    private NotifyIcon? _trayIcon;
    private System.Windows.Forms.Timer? _reminderTimer;
    private bool _reminderFiredToday;
    private readonly List<PlaylistEntry> _playlist = new();
    private int _playlistIndex = -1;
    private bool _playingPlaylist;
    private string? _playlistFolder;
    private bool _focusMode;
    private readonly List<(int Surah, int Ayah)> _history = new();
    private int _histPos = -1;
    private Dictionary<string, string>? _prayerTimes;
    private DateTime _prayerFetchedDate;
    private readonly HashSet<string> _notifiedPrayers = new();

    private const string AppVersion = "1.4.0";

    public MainForm()
    {
        WmpEngine wmp = new();
        _audio = wmp.Available ? wmp : new MciEngine();
        _audio.VolumePercent = Math.Clamp(_settings.Volume, 0, 100);
        _audio.Finished += () =>
        {
            try
            {
                BeginInvoke(new Action(() =>
                {
                    UpdatePlayButton();
                    _ = PlayNextInQueueAsync(_playToken);
                }));
            }
            catch (ObjectDisposedException)
            {
            }
        };

        Text = "Quran Desktop — KSU Electronic Moshaf (WinForms)";
        StartPosition = FormStartPosition.CenterScreen;
        Size = new Size(1250, 850);
        MinimumSize = new Size(980, 660);
        DoubleBuffered = true;

        BuildUi();
        _curSurah = Math.Clamp(_settings.Surah, 1, 114);
        _curAyah = Math.Clamp(_settings.Ayah, 1, QuranData.SurahAyahCount(_curSurah));
        ApplySettingsToUi();
        WireEvents();
        SwitchMode(_settings.Mode);

        _mushafView.ShowOverlay = _settings.ShowMushafOverlay;
        _mushafView.OverlayProvider = OverlayTextForAyah;
        _audio.Speed = Math.Clamp(_settings.Speed, 0.5f, 2f);
        ProgramServices.ActiveTranslationKey = _settings.Translation;
        _mushafView.ZoomChanged += () =>
        {
            _settings.Zoom = _mushafView.Zoom;
            _settings.Save();
        };

        InitTrayAndReminder();
        ApplyDarkMode();

        if (!_settings.FirstRunDone)
        {
            Shown += (_, _) =>
            {
                _settings.FirstRunDone = true;
                _settings.Save();
                using var w = new WelcomeDialog();
                w.ShowDialog(this);
            };
        }

        if (_settings.ShowDailyAyah)
        {
            Shown += (_, _) =>
            {
                var t = new System.Windows.Forms.Timer { Interval = 1500 };
                t.Tick += (_, _) =>
                {
                    t.Stop();
                    t.Dispose();
                    if (IsDisposed || !IsHandleCreated) return;
                    try
                    {
                        var dlg = new DailyAyahDialog();
                        dlg.GotoRequested += (s, a) => _ = GotoAyahAsync(s, a);
                        dlg.Show(this);
                    }
                    catch
                    {
                    }
                };
                t.Start();
            };
        }
    }

    private string OverlayTextForAyah(int surah, int ayah)
    {
        var t = CurrentTranslation;
        if (t == null || t.Key == "ar_ayat" || !_chkShowTrans.Checked) return "";
        if (_tarjamaLocal.TryGetValue(t.Key + "|" + surah, out var map) && map.TryGetValue(ayah, out var text))
        {
            return KsuApi.StripHtml(text).Replace('\n', ' ');
        }
        return "";
    }

    private static Font ResolveFont(string[] candidates, float size)
    {
        foreach (var name in candidates)
        {
            try
            {
                using var probe = new FontFamily(name);
                return new Font(probe, size);
            }
            catch
            {
            }
        }
        return new Font(FontFamily.GenericSerif, size);
    }

    private void BuildUi()
    {
        _topContainer = new Panel { Dock = DockStyle.Top, Height = 156 };

        var flow1 = MakeFlow();
        var flow2 = MakeFlow();
        var flow3 = MakeFlow();
        var flow4 = MakeFlow();
        _topContainer.Controls.Add(flow4);
        _topContainer.Controls.Add(flow3);
        _topContainer.Controls.Add(flow2);
        _topContainer.Controls.Add(flow1);
        flow1.BringToFront();
        flow2.BringToFront();
        flow3.BringToFront();

        _cmbMode = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 150, FlatStyle = FlatStyle.Flat };
        _cmbMode.Items.Add(new ComboItem("Teks & Terjemahan", "teks"));
        _cmbMode.Items.Add(new ComboItem("Mushaf (Halaman)", "mushaf"));
        _cmbMode.Items.Add(new ComboItem("Tes Hafalan (Hifz)", "hifz"));

        _cmbMosshaf = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 160, FlatStyle = FlatStyle.Flat };
        foreach (var m in MushafTypes.All) _cmbMosshaf.Items.Add(new ComboItem(m.Display, m));

        _cmbSurah = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 280, DropDownWidth = 320, FlatStyle = FlatStyle.Flat };
        _cmbAyah = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 72, FlatStyle = FlatStyle.Flat };
        _cmbPage = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 90, FlatStyle = FlatStyle.Flat };
        _cmbJuz = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 100, FlatStyle = FlatStyle.Flat };
        _btnPagePrev = new Button { Text = "◀ Hal", Width = 56 };
        _btnPageNext = new Button { Text = "Hal ▶", Width = 56 };
        _btnZoomIn = new Button { Text = "Zoom +", Width = 64 };
        _btnZoomOut = new Button { Text = "Zoom −", Width = 64 };
        _btnDownload = new Button { Text = "⬇ Unduh", Width = 80 };

        flow1.Controls.Add(new Label { Text = "Mode:", AutoSize = true, Padding = new Padding(0, 8, 0, 0) });
        flow1.Controls.Add(_cmbMode);
        flow1.Controls.Add(_cmbMosshaf);
        flow1.Controls.Add(new Label { Text = "Surah:", AutoSize = true, Padding = new Padding(6, 8, 0, 0) });
        flow1.Controls.Add(_cmbSurah);
        flow1.Controls.Add(new Label { Text = "Ayat:", AutoSize = true, Padding = new Padding(4, 8, 0, 0) });
        flow1.Controls.Add(_cmbAyah);
        flow1.Controls.Add(_btnPagePrev);
        flow1.Controls.Add(_cmbPage);
        flow1.Controls.Add(_btnPageNext);
        flow1.Controls.Add(new Label { Text = "Juz:", AutoSize = true, Padding = new Padding(4, 8, 0, 0) });
        flow1.Controls.Add(_cmbJuz);
        _chkOverlay = new CheckBox { Text = "Arti di mushaf", AutoSize = true, Padding = new Padding(6, 6, 0, 0) };
        flow1.Controls.Add(_btnZoomIn);
        flow1.Controls.Add(_btnZoomOut);
        flow1.Controls.Add(_btnDownload);
        flow1.Controls.Add(_chkOverlay);

        _cmbQaree = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 240, DropDownWidth = 270, FlatStyle = FlatStyle.Flat };
        _cmbTrans = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 220, DropDownWidth = 250, FlatStyle = FlatStyle.Flat };
        _cmbPb = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 130, FlatStyle = FlatStyle.Flat };
        _cmbTafsir = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 190, DropDownWidth = 220, FlatStyle = FlatStyle.Flat };
        _cmbRepeat = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 90, FlatStyle = FlatStyle.Flat };

        flow2.Controls.Add(new Label { Text = "Qari:", AutoSize = true, Padding = new Padding(0, 8, 0, 0) });
        flow2.Controls.Add(_cmbQaree);
        flow2.Controls.Add(new Label { Text = "Terjemahan:", AutoSize = true, Padding = new Padding(6, 8, 0, 0) });
        flow2.Controls.Add(_cmbTrans);
        flow2.Controls.Add(new Label { Text = "Talaqaa (voice):", AutoSize = true, Padding = new Padding(6, 8, 0, 0) });
        flow2.Controls.Add(_cmbPb);
        flow2.Controls.Add(new Label { Text = "Tafsir:", AutoSize = true, Padding = new Padding(6, 8, 0, 0) });
        flow2.Controls.Add(_cmbTafsir);
        flow2.Controls.Add(new Label { Text = "Ulangi:", AutoSize = true, Padding = new Padding(6, 8, 0, 0) });
        flow2.Controls.Add(_cmbRepeat);

        _btnPlayPause = new Button { Text = "▶ Play", Width = 90 };
        _btnStop = new Button { Text = "■ Stop", Width = 70 };
        _btnPrevAya = new Button { Text = "◀ Ayat", Width = 64 };
        _btnNextAya = new Button { Text = "Ayat ▶", Width = 64 };
        _chkAutoNext = new CheckBox { Text = "Lanjut otomatis", AutoSize = true, Padding = new Padding(4, 6, 0, 0) };
        _chkPlayOnClick = new CheckBox { Text = "Klik ayat = putar", AutoSize = true, Padding = new Padding(4, 6, 0, 0) };
        _chkTafsirPanel = new CheckBox { Text = "Panel tafsir", AutoSize = true, Padding = new Padding(4, 6, 0, 0) };
        _chkShowTrans = new CheckBox { Text = "Arti", AutoSize = true, Padding = new Padding(4, 6, 0, 0) };
        _chkInlineTafsir = new CheckBox { Text = "Tafsir inline", AutoSize = true, Padding = new Padding(4, 6, 0, 0) };
        _chkTeacher = new CheckBox { Text = "Mode guru", AutoSize = true, Padding = new Padding(4, 6, 0, 0) };
        _chkRepeatRange = new CheckBox { Text = "Ulang rentang", AutoSize = true, Padding = new Padding(4, 6, 0, 0) };
        _numRangeFrom = new NumericUpDown { Minimum = 1, Maximum = 286, Value = 1, Width = 58 };
        _numRangeTo = new NumericUpDown { Minimum = 1, Maximum = 286, Value = 5, Width = 58 };
        _trackVolume = new TrackBar { Minimum = 0, Maximum = 100, Width = 110, TickStyle = TickStyle.None };
        _txtSearch = new TextBox { Width = 170 };
        _btnSearch = new Button { Text = "🔍 Cari", Width = 76 };

        flow3.Controls.Add(_btnPlayPause);
        flow3.Controls.Add(_btnStop);
        flow3.Controls.Add(_btnPrevAya);
        flow3.Controls.Add(_btnNextAya);
        flow3.Controls.Add(_chkAutoNext);
        flow3.Controls.Add(_chkPlayOnClick);
        flow3.Controls.Add(_chkShowTrans);
        flow3.Controls.Add(_chkInlineTafsir);
        flow3.Controls.Add(_chkTeacher);
        flow3.Controls.Add(_chkRepeatRange);
        flow3.Controls.Add(_numRangeFrom);
        flow3.Controls.Add(new Label { Text = "–", AutoSize = true, Padding = new Padding(0, 8, 0, 0) });
        flow3.Controls.Add(_numRangeTo);
        flow3.Controls.Add(new Label { Text = "Vol:", AutoSize = true, Padding = new Padding(4, 10, 0, 0) });
        flow3.Controls.Add(_trackVolume);
        flow3.Controls.Add(new Label { Text = "Cari:", AutoSize = true, Padding = new Padding(8, 8, 0, 0) });
        flow3.Controls.Add(_txtSearch);
        flow3.Controls.Add(_btnSearch);

        _center = new Panel { Dock = DockStyle.Fill, BackColor = Color.FromArgb(244, 244, 240) };

        _textMode = new Controls.TextModeControl { Dock = DockStyle.Fill };

        _mushafView = new Controls.MushafView { Dock = DockStyle.Fill };
        _mushafRight = new Panel { Dock = DockStyle.Right, Width = 330, BackColor = Color.FromArgb(250, 250, 247), Padding = new Padding(6) };
        _ayahStrip = new FlowLayoutPanel
        {
            Dock = DockStyle.Top,
            Height = 110,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = true,
            AutoScroll = true,
            BackColor = Color.FromArgb(240, 240, 235),
        };
        _mushafInfo = new RichTextBox
        {
            Dock = DockStyle.Fill,
            ReadOnly = true,
            BorderStyle = BorderStyle.FixedSingle,
            BackColor = Color.White,
            Font = new Font("Segoe UI", 10.5f),
        };
        _mushafRight.Controls.Add(_mushafInfo);
        _mushafRight.Controls.Add(_ayahStrip);

        _hifz = new Controls.HifzControl { Dock = DockStyle.Fill };

        _center.Controls.Add(_textMode);
        _center.Controls.Add(_mushafView);
        _center.Controls.Add(_mushafRight);
        _center.Controls.Add(_hifz);
        _mushafRight.BringToFront();
        _mushafView.BringToFront();

        _tafsirPanel = new Panel { Dock = DockStyle.Bottom, Height = 190, BackColor = Color.White, Padding = new Padding(4) };
        var tafsirTop = new FlowLayoutPanel
        {
            Dock = DockStyle.Top,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = false,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            BackColor = Color.FromArgb(240, 240, 235),
        };
        _tafsirHeader = new Label { Text = "Tafsir ayat terpilih:", AutoSize = true, Padding = new Padding(4, 8, 0, 0) };
        tafsirTop.Controls.Add(_tafsirHeader);
        _btnOpenTafsir = new Button { Text = "Buka di browser ↗", Width = 130 };
        tafsirTop.Controls.Add(_btnOpenTafsir);
        _tafsirText = new RichTextBox
        {
            Dock = DockStyle.Fill,
            ReadOnly = true,
            BorderStyle = BorderStyle.None,
            BackColor = Color.White,
            Font = new Font("Segoe UI", 11f),
        };
        _tafsirPanel.Controls.Add(_tafsirText);
        _tafsirPanel.Controls.Add(tafsirTop);
        _tafsirText.BringToFront();

        _lblStatus = new Label
        {
            Dock = DockStyle.Bottom,
            AutoSize = true,
            Padding = new Padding(10, 5, 10, 5),
            Text = "Siap",
            BackColor = Color.FromArgb(45, 45, 48),
            ForeColor = Color.Gainsboro,
            Font = new Font("Segoe UI", 9.5f),
        };

        _btnStar = new Button { Text = "★ Bookmark", Width = 96 };
        _btnCard = new Button { Text = "Kartu Ayat", Width = 86 };
        _btnInspirasi = new Button { Text = "✨ Inspirasi", Width = 92 };
        _btnFeatures = new Button { Text = "Fitur Lainnya", Width = 104 };
        _trackSpeed = new TrackBar { Minimum = 5, Maximum = 20, TickFrequency = 5, Width = 110, TickStyle = TickStyle.None };

        var lblSpeedVal = new Label { Text = "1.0×", AutoSize = true, Padding = new Padding(0, 10, 0, 0) };
        flow4.Controls.Add(_btnStar);
        flow4.Controls.Add(_btnCard);
        flow4.Controls.Add(_btnInspirasi);
        flow4.Controls.Add(_btnFeatures);
        flow4.Controls.Add(new Label { Text = "Speed:", AutoSize = true, Padding = new Padding(4, 10, 0, 0) });
        flow4.Controls.Add(_trackSpeed);
        flow4.Controls.Add(lblSpeedVal);
        _trackSpeed.ValueChanged += (_, _) => lblSpeedVal.Text = (_trackSpeed.Value / 10.0).ToString("0.0") + "×";

        Controls.Add(_center);
        Controls.Add(_tafsirPanel);
        Controls.Add(_lblStatus);
        Controls.Add(_topContainer);
        _topContainer.BringToFront();
        _lblStatus.BringToFront();
    }

    private static FlowLayoutPanel MakeFlow() => new()
    {
        Dock = DockStyle.Top,
        FlowDirection = FlowDirection.LeftToRight,
        WrapContents = false,
        AutoSize = true,
        AutoSizeMode = AutoSizeMode.GrowAndShrink,
        Padding = new Padding(8, 4, 8, 0),
    };

    private void ApplySettingsToUi()
    {
        _uiBusy = true;

        foreach (var s in SurahList.All)
        {
            _cmbSurah.Items.Add(new ComboItem($"{s.Number}. {s.EnglishName} — {s.ArabicName}", s.Number));
        }

        _cmbTrans.Items.Clear();
        foreach (var t in Translations.All) _cmbTrans.Items.Add(new ComboItem(t.Display, t));

        _cmbPb.Items.Add(new ComboItem("(Nonaktif)", null));
        foreach (var v in VoiceTranslations.All) _cmbPb.Items.Add(new ComboItem(v.Display, v));

        foreach (var r in Reciters.All) _cmbQaree.Items.Add(new ComboItem(r.Display, r));

        foreach (var t in Tafsirs.All) _cmbTafsir.Items.Add(new ComboItem(t.Display, t));

        _cmbRepeat.Items.Add(new ComboItem("1× (off)", 1));
        _cmbRepeat.Items.Add(new ComboItem("2×", 2));
        _cmbRepeat.Items.Add(new ComboItem("3×", 3));
        _cmbRepeat.Items.Add(new ComboItem("5×", 5));
        _cmbRepeat.Items.Add(new ComboItem("10×", 10));
        _cmbRepeat.Items.Add(new ComboItem("∞", -1));

        for (int j = 1; j <= 30; j++)
        {
            var start = QuranData.JuzStart(j);
            _cmbJuz.Items.Add(new ComboItem($"Juz {j} ({start.Surah}:{start.Ayah})", j));
        }

        SelectCombo(_cmbMode, _settings.Mode);
        SelectCombo(_cmbMosshaf, MushafTypes.Find(_settings.Mosshaf) ?? MushafTypes.All[0]);
        _cmbSurah.SelectedIndex = _settings.Surah - 1;
        FillAyatCombo(_settings.Surah);
        _cmbAyah.SelectedIndex = _settings.Ayah - 1;
        SelectCombo(_cmbTrans, Translations.Find(_settings.Translation) ?? Translations.All[4]);
        SelectCombo(_cmbPb, string.IsNullOrEmpty(_settings.PbTrans) ? null : VoiceTranslations.Find(_settings.PbTrans));
        SelectCombo(_cmbTafsir, Tafsirs.Find(_settings.Tafsir) ?? Tafsirs.All[0]);
        _cmbRepeat.SelectedIndex = 0;
        for (int i = 0; i < _cmbRepeat.Items.Count; i++)
        {
            if ((int)((ComboItem)_cmbRepeat.Items[i]).Value! == _settings.Repeat) _cmbRepeat.SelectedIndex = i;
        }

        int qareeIdx = 0;
        for (int i = 0; i < _cmbQaree.Items.Count; i++)
        {
            if (((Reciter)((ComboItem)_cmbQaree.Items[i]).Value!).Key == _settings.Qaree) qareeIdx = i;
        }
        _cmbQaree.SelectedIndex = qareeIdx;

        _chkAutoNext.Checked = _settings.AutoNext;
        _chkPlayOnClick.Checked = _settings.PlayOnClick;
        _chkTafsirPanel.Checked = _settings.ShowTafsirPanel;
        _chkShowTrans.Checked = _settings.ShowTranslation;
        _chkInlineTafsir.Checked = _settings.ShowInlineTafsir;
        _chkOverlay.Checked = _settings.ShowMushafOverlay;
        _chkTeacher.Checked = _settings.TeacherMode;
        _numRangeFrom.Maximum = QuranData.SurahAyahCount(_curSurah);
        _numRangeTo.Maximum = _numRangeFrom.Maximum;
        _numRangeTo.Value = Math.Min(_settings.Ayah + 4, (int)_numRangeTo.Maximum);
        _trackVolume.Value = Math.Clamp(_settings.Volume, 0, 100);

        _uiBusy = false;
        RebuildPageCombo();
    }

    private static void SelectCombo(ComboBox cmb, object? value)
    {
        for (int i = 0; i < cmb.Items.Count; i++)
        {
            var item = (ComboItem)cmb.Items[i];
            bool match = (value == null && item.Value == null)
                || (value != null && value.Equals(item.Value));
            if (match)
            {
                cmb.SelectedIndex = i;
                return;
            }
        }
        if (cmb.Items.Count > 0 && cmb.SelectedIndex < 0) cmb.SelectedIndex = 0;
    }

    private void RebuildPageCombo()
    {
        var mt = CurrentMushafType;
        if (mt == null) return;
        _uiBusy = true;
        _cmbPage.Items.Clear();
        int count = QuranData.PageCount(mt.PageKey);
        for (int p = 1; p <= count; p++) _cmbPage.Items.Add(p.ToString());
        if (_cmbPage.Items.Count > 0 && _cmbPage.SelectedIndex < 0)
        {
            _cmbPage.SelectedIndex = Math.Clamp(QuranData.FindPage(mt.PageKey, _curSurah, _curAyah) - 1, 0, count - 1);
        }
        _uiBusy = false;
    }

    private void FillAyatCombo(int surah)
    {
        _cmbAyah.Items.Clear();
        int n = QuranData.SurahAyahCount(surah);
        for (int a = 1; a <= n; a++) _cmbAyah.Items.Add(a.ToString());
    }

    private Reciter? CurrentReciter => (Reciter?)((ComboItem?)_cmbQaree.SelectedItem)?.Value;

    private async Task<Dictionary<int, string>> TarjamaAsync(string transKey, int surah)
    {
        string k = transKey + "|" + surah;
        if (_tarjamaLocal.TryGetValue(k, out var cached)) return cached;
        var map = await ProgramServices.Api.GetSurahTarjamaAsync(transKey, surah, CancellationToken.None);
        _tarjamaLocal[k] = map;
        return map;
    }

    private TranslationOption? CurrentTranslation => (TranslationOption?)((ComboItem?)_cmbTrans.SelectedItem)?.Value;

    private string? CurrentTafsirKey => ((TafsirOption?)((ComboItem?)_cmbTafsir.SelectedItem)?.Value)?.Key;

    private VoiceTranslation? CurrentPb => (VoiceTranslation?)((ComboItem?)_cmbPb.SelectedItem)?.Value;

    private MushafType? CurrentMushafType => (MushafType?)((ComboItem?)_cmbMosshaf.SelectedItem)?.Value;

    private string CurrentMode => (string)((ComboItem)_cmbMode.SelectedItem!).Value!;

    private int CurrentRepeat => (int)((ComboItem)_cmbRepeat.SelectedItem!).Value!;

    private void WireEvents()
    {
        _cmbMode.SelectedIndexChanged += (_, _) =>
        {
            if (_uiBusy) return;
            _settings.Mode = CurrentMode;
            _settings.Save();
            SwitchMode(CurrentMode);
        };

        _cmbMosshaf.SelectedIndexChanged += (_, _) =>
        {
            if (_uiBusy) return;
            var mt = CurrentMushafType;
            if (mt != null)
            {
                _settings.Mosshaf = mt.Key;
                _settings.Save();
                RebuildPageCombo();
                if (CurrentMode == "mushaf")
                {
                    int page = QuranData.FindPage(mt.PageKey, _curSurah, _curAyah);
                    _ = LoadMushafPageAsync(page, _curSurah, _curAyah);
                }
            }
        };

        _cmbSurah.SelectedIndexChanged += (_, _) =>
        {
            if (!_uiBusy)
            {
                int s = (int)((ComboItem)_cmbSurah.SelectedItem!).Value!;
                _ = GotoAyahAsync(s, 1);
            }
        };

        _cmbAyah.SelectedIndexChanged += (_, _) =>
        {
            if (!_uiBusy)
            {
                _ = GotoAyahAsync(_curSurah, _cmbAyah.SelectedIndex + 1);
            }
        };

        _cmbPage.SelectedIndexChanged += (_, _) =>
        {
            if (!_uiBusy && CurrentMode == "mushaf" && _cmbPage.SelectedIndex >= 0)
            {
                var mt = CurrentMushafType;
                if (mt != null)
                {
                    var (s, a) = QuranData.PageStart(mt.PageKey, _cmbPage.SelectedIndex + 1);
                    _ = GotoAyahAsync(s, a);
                }
            }
        };

        _cmbJuz.SelectedIndexChanged += (_, _) =>
        {
            if (!_uiBusy && _cmbJuz.SelectedIndex >= 0)
            {
                int juz = _cmbJuz.SelectedIndex + 1;
                var (s, a) = QuranData.JuzStart(juz);
                _ = GotoAyahAsync(s, a);
            }
        };

        _btnPagePrev.Click += (_, _) => StepPage(-1);
        _btnPageNext.Click += (_, _) => StepPage(1);
        _btnZoomIn.Click += (_, _) => { _mushafView.SetZoom(_mushafView.Zoom * 1.2f); _settings.Zoom = _mushafView.Zoom; _settings.Save(); };
        _btnZoomOut.Click += (_, _) => { _mushafView.SetZoom(_mushafView.Zoom / 1.2f); _settings.Zoom = _mushafView.Zoom; _settings.Save(); };
        _btnDownload.Click += (_, _) =>
        {
            var mt = CurrentMushafType;
            if (mt != null)
            {
                using var dlg = new Controls.DownloadDialog(mt.Key);
                dlg.ShowDialog(this);
            }
        };

        _cmbQaree.SelectedIndexChanged += (_, _) =>
        {
            var r = CurrentReciter;
            if (r != null)
            {
                _settings.Qaree = r.Key;
                _settings.Save();
            }
        };

        _cmbTrans.SelectedIndexChanged += async (_, _) =>
        {
            if (_uiBusy) return;
            var t = CurrentTranslation;
            if (t != null)
            {
                _settings.Translation = t.Key;
                ProgramServices.ActiveTranslationKey = t.Key;
                _settings.Save();
                _renderedSurah = -1;
                if (CurrentMode == "teks")
                {
                    await RenderSurahAsync(_curSurah);
                    _textMode.SetSelected(_curAyah);
                }
                if (CurrentMode == "mushaf") UpdateMushafInfo(_curSurah, _curAyah);
            }
        };

        _cmbPb.SelectedIndexChanged += (_, _) =>
        {
            if (_uiBusy) return;
            var v = CurrentPb;
            _settings.PbTrans = v?.Key ?? "";
            _settings.Save();
        };

        _cmbTafsir.SelectedIndexChanged += (_, _) =>
        {
            if (_uiBusy) return;
            var t = CurrentTafsirKey;
            if (t != null)
            {
                _settings.Tafsir = t;
                _settings.Save();
                UpdateTafsirPanel(_curSurah, _curAyah);
            }
        };

        _cmbRepeat.SelectedIndexChanged += (_, _) =>
        {
            if (_uiBusy) return;
            _settings.Repeat = CurrentRepeat;
            _settings.Save();
        };

        _btnPlayPause.Click += (_, _) =>
        {
            if (_audio.IsOpen && _audio.IsPlaying)
            {
                _audio.Pause();
                UpdatePlayButton();
            }
            else if (_audio.IsOpen && _audio.IsPaused)
            {
                _audio.Resume();
                UpdatePlayButton();
            }
            else
            {
                PlayAyah(_curSurah, _curAyah, withIntro: true);
            }
        };

        _btnStop.Click += (_, _) => StopPlayback("Stop");

        _btnPrevAya.Click += (_, _) =>
        {
            if (_curAyah > 1) _ = GotoAyahAsync(_curSurah, _curAyah - 1);
            else if (_curSurah > 1) _ = GotoAyahAsync(_curSurah - 1, QuranData.SurahAyahCount(_curSurah - 1));
        };

        _btnNextAya.Click += (_, _) =>
        {
            if (_curAyah < QuranData.SurahAyahCount(_curSurah)) _ = GotoAyahAsync(_curSurah, _curAyah + 1);
            else if (_curSurah < 114) _ = GotoAyahAsync(_curSurah + 1, 1);
        };

        _chkAutoNext.CheckedChanged += (_, _) => { _settings.AutoNext = _chkAutoNext.Checked; _settings.Save(); };
        _chkPlayOnClick.CheckedChanged += (_, _) => { _settings.PlayOnClick = _chkPlayOnClick.Checked; _settings.Save(); };
        _chkTafsirPanel.CheckedChanged += (_, _) =>
        {
            _settings.ShowTafsirPanel = _chkTafsirPanel.Checked;
            _settings.Save();
            _tafsirPanel.Visible = _chkTafsirPanel.Checked;
        };

        _chkShowTrans.CheckedChanged += (_, _) =>
        {
            _settings.ShowTranslation = _chkShowTrans.Checked;
            _settings.Save();
            _textMode.SetTranslationVisible(_chkShowTrans.Checked);
            if (CurrentMode == "mushaf" && _mushafView.CurrentPage > 0)
            {
                UpdateMushafInfo(_curSurah, _curAyah);
            }
        };

        _chkInlineTafsir.CheckedChanged += async (_, _) =>
        {
            _settings.ShowInlineTafsir = _chkInlineTafsir.Checked;
            _settings.Save();
            if (CurrentMode == "teks" && _chkInlineTafsir.Checked && _curAyahs.Count > 0)
            {
                await LoadInlineTafsirAsync(_curSurah, _curAyah);
            }
        };

        _chkOverlay.CheckedChanged += (_, _) =>
        {
            _settings.ShowMushafOverlay = _chkOverlay.Checked;
            _settings.Save();
            _mushafView.ShowOverlay = _chkOverlay.Checked;
        };

        _chkTeacher.CheckedChanged += (_, _) =>
        {
            _settings.TeacherMode = _chkTeacher.Checked;
            _settings.Save();
            ShowStatus(_chkTeacher.Checked
                ? "Mode guru AKTIF — tiap ayat diulang 3× dengan jeda"
                : "Mode guru nonaktif");
        };

        _chkRepeatRange.CheckedChanged += (_, _) =>
        {
            if (_chkRepeatRange.Checked)
            {
                int from = (int)_numRangeFrom.Value;
                int to = (int)_numRangeTo.Value;
                ShowStatus($"Ulang rentang AKTIF — ayat {from}–{to} akan diulang {CurrentRepeat}× per putaran");
            }
            else
            {
                _rangeLoopsRemaining = 0;
                ShowStatus("Ulang rentang nonaktif");
            }
        };

        _numRangeFrom.ValueChanged += (_, _) =>
        {
            if (_numRangeFrom.Value > _numRangeTo.Value) _numRangeTo.Value = _numRangeFrom.Value;
        };

        _btnStar.Click += (_, _) =>
        {
            ProgressStore.ToggleBookmark(_curSurah, _curAyah);
            bool now = ProgressStore.IsBookmarked(_curSurah, _curAyah);
            ShowStatus(now ? $"★ Bookmark ditambahkan: QS {_curSurah}:{_curAyah}" : $"Bookmark dihapus: QS {_curSurah}:{_curAyah}");
        };

        _btnCard.Click += async (_, _) =>
        {
            try
            {
                ShowStatus("Menyiapkan kartu ayat…");
                string arab = MadinahText.Get(_curSurah, _curAyah) ?? "";
                string arti = "";
                var t = CurrentTranslation;
                if (t != null)
                {
                    var m = await TarjamaAsync(t.Key, _curSurah);
                    arti = m.TryGetValue(_curAyah, out var v) ? KsuApi.StripHtml(v) : "";
                }
                using var dlg = new AyahImageDialog(_curSurah, _curAyah, arab, arti);
                dlg.ShowDialog(this);
            }
            catch (Exception ex)
            {
                ShowStatus("Gagal: " + ex.Message, error: true);
            }
        };

        _btnInspirasi.Click += (_, _) =>
        {
            using var dlg = new InspirasiDialog();
            dlg.GotoRequested += (s, a) => _ = GotoAyahAsync(s, a);
            dlg.ShowDialog(this);
        };

        var featuresMenu = new ContextMenuStrip();
        featuresMenu.Items.Add("Ayat Hari Ini", null, (_, _) =>
        {
            using var d = new DailyAyahDialog();
            d.GotoRequested += (s, a) => _ = GotoAyahAsync(s, a);
            d.ShowDialog(this);
        });
        featuresMenu.Items.Add("Target Khatam", null, (_, _) =>
        {
            using var d = new KhatamDialog();
            d.ShowDialog(this);
        });
        featuresMenu.Items.Add("Peta Hafalan (604 halaman)", null, (_, _) =>
        {
            using var d = new HeatmapDialog(1, QuranData.PageCount("Page"));
            d.ShowDialog(this);
        });
        featuresMenu.Items.Add("Kuis: Lanjutannya?", null, (_, _) =>
        {
            using var d = new QuizDialog(_curSurah);
            d.ShowDialog(this);
        });
        featuresMenu.Items.Add("Playlist Surah", null, (_, _) =>
        {
            var r = CurrentReciter;
            using var d = new PlaylistDialog(_curSurah, r?.Key ?? "husary", r?.Display ?? "Husary");
            d.PlayRequested += list => StartPlaylist(list);
            d.ShowDialog(this);
        });
        featuresMenu.Items.Add("Unduh Audio Surah", null, (_, _) =>
        {
            var r = CurrentReciter;
            using var d = new AudioDownloadDialog(r?.Key ?? "husary", _curSurah);
            d.ShowDialog(this);
        });
        featuresMenu.Items.Add("Mini Player", null, (_, _) => ToggleMiniPlayer());
        featuresMenu.Items.Add("Pengingat Harian", null, (_, _) =>
        {
            TimeSpan cur = TimeSpan.TryParse(_settings.ReminderTime, out var tt) ? tt : new TimeSpan(20, 0, 0);
            using var d = new ReminderDialog(_settings.ReminderEnabled, cur);
            if (d.ShowDialog(this) == DialogResult.OK)
            {
                _settings.ReminderEnabled = d.EnabledReminder;
                _settings.ReminderTime = d.Time.ToString(@"hh\:mm");
                _settings.Save();
                _reminderFiredToday = false;
                ShowStatus(d.EnabledReminder ? $"Pengingat aktif jam {d.Time:hh\\:mm}" : "Pengingat nonaktif");
            }
        });
        featuresMenu.Items.Add(new ToolStripSeparator());
        featuresMenu.Items.Add("Kata per Kata (WBW)", null, (_, _) =>
        {
            using var d = new WbwDialog(_curSurah, _curAyah);
            d.ShowDialog(this);
        });
        featuresMenu.Items.Add("Bacakan Arti (TTS)", null, async (_, _) =>
        {
            if (!TtsService.Available)
            {
                ShowStatus("TTS tidak tersedia di sistem ini", error: true);
                return;
            }
            try
            {
                var arabic = await TarjamaAsync("ar_ayat", _curSurah);
                arabic.TryGetValue(_curAyah, out var arab);
                var t = CurrentTranslation;
                string arti = "";
                if (t != null)
                {
                    var m = await TarjamaAsync(t.Key, _curSurah);
                    arti = m.TryGetValue(_curAyah, out var v) ? KsuApi.StripHtml(v) : "";
                }
                var info = SurahList.Get(_curSurah);
                TtsService.Speak($"{info.EnglishName}, ayat {_curAyah}. {arti}");
                ShowStatus("Membacakan arti…");
            }
            catch (Exception ex)
            {
                ShowStatus("TTS gagal: " + ex.Message, error: true);
            }
        });
        featuresMenu.Items.Add("Latihan Dikte (Imla')", null, (_, _) =>
        {
            using var d = new DictationDialog();
            d.PlayRequested += (s, a) =>
            {
                _playingPlaylist = false;
                _playlistFolder = null;
                PlayAyah(s, a, withIntro: false);
            };
            d.ShowDialog(this);
        });
        featuresMenu.Items.Add("Rekam Tilawah", null, (_, _) =>
        {
            using var d = new RecordingDialog();
            d.ShowDialog(this);
        });
        featuresMenu.Items.Add("Jadwal Sholat", null, (_, _) =>
        {
            using var d = new PrayerTimesDialog(_settings.PrayerCity, _settings.PrayerCountry, _settings.PrayerMethod, _settings.PrayerNotify);
            if (d.ShowDialog(this) == DialogResult.OK)
            {
                _settings.PrayerCity = d.City;
                _settings.PrayerCountry = d.Country;
                _settings.PrayerMethod = d.Method;
                _settings.PrayerNotify = d.NotifyBefore;
                _settings.Save();
                _prayerFetchedDate = default;
                _notifiedPrayers.Clear();
                ShowStatus($"Jadwal sholat: {d.City}, {d.Country}");
            }
        });
        featuresMenu.Items.Add("Statistik Baca (30 hari)", null, (_, _) =>
        {
            using var d = new StatsDialog();
            d.ShowDialog(this);
        });
        featuresMenu.Items.Add(new ToolStripSeparator());
        featuresMenu.Items.Add("Backup Data…", null, async (_, _) =>
        {
            using var dlg = new SaveFileDialog
            {
                Filter = "Backup Quran Desktop|*.quranbak",
                FileName = $"quran-backup-{DateTime.Now:yyyyMMdd}.quranbak",
            };
            if (dlg.ShowDialog(this) == DialogResult.OK)
            {
                try
                {
                    await BackupService.ExportAsync(dlg.FileName);
                    ShowStatus("Backup tersimpan: " + Path.GetFileName(dlg.FileName));
                }
                catch (Exception ex)
                {
                    ShowStatus("Backup gagal: " + ex.Message, error: true);
                }
            }
        });
        featuresMenu.Items.Add("Restore Data…", null, async (_, _) =>
        {
            using var dlg = new OpenFileDialog { Filter = "Backup Quran Desktop|*.quranbak" };
            if (dlg.ShowDialog(this) == DialogResult.OK)
            {
                try
                {
                    await BackupService.ImportAsync(dlg.FileName);
                    MessageBox.Show(this, "Data dipulihkan. Aplikasi akan dimulai ulang.", "Restore",
                        MessageBoxButtons.OK, MessageBoxIcon.Information);
                    Application.Restart();
                }
                catch (Exception ex)
                {
                    ShowStatus("Restore gagal: " + ex.Message, error: true);
                }
            }
        });
        featuresMenu.Items.Add("Cek Pembaruan", null, async (_, _) =>
        {
            ShowStatus("Memeriksa pembaruan…");
            var upd = await BackupService.CheckUpdateAsync(CancellationToken.None);
            if (upd == null)
            {
                ShowStatus("Gagal memeriksa pembaruan", error: true);
                return;
            }
            string latest = upd.Value.Tag.TrimStart('v');
            if (string.CompareOrdinal(latest, AppVersion) > 0)
            {
                var ask = MessageBox.Show(this,
                    $"Versi baru tersedia: {upd.Value.Tag} (kamu pakai v{AppVersion}).\nBuka halaman unduhan?",
                    "Pembaruan Tersedia", MessageBoxButtons.YesNo, MessageBoxIcon.Information);
                if (ask == DialogResult.Yes)
                {
                    try
                    {
                        System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                        {
                            FileName = upd.Value.Url,
                            UseShellExecute = true,
                        });
                    }
                    catch
                    {
                    }
                }
            }
            else
            {
                ShowStatus($"Aplikasi sudah versi terbaru (v{AppVersion})");
            }
        });
        featuresMenu.Items.Add("Ukuran Font Terjemahan…", null, (_, _) =>
        {
            using var dlg = new Form
            {
                Text = "Ukuran Font Terjemahan",
                ClientSize = new Size(300, 120),
                FormBorderStyle = FormBorderStyle.FixedDialog,
                StartPosition = FormStartPosition.CenterParent,
                MaximizeBox = false,
            };
            var num = new NumericUpDown
            {
                Left = 20,
                Top = 20,
                Width = 100,
                Minimum = 9m,
                Maximum = 16m,
                Increment = 0.5m,
                DecimalPlaces = 1,
                Value = (decimal)_settings.TranslationFontSize,
            };
            var ok = new Button { Text = "OK", Left = 140, Top = 18, Width = 80 };
            dlg.Controls.Add(num);
            dlg.Controls.Add(ok);
            dlg.AcceptButton = ok;
            ok.Click += (_, _) => { dlg.DialogResult = DialogResult.OK; dlg.Close(); };
            if (dlg.ShowDialog(this) == DialogResult.OK)
            {
                _settings.TranslationFontSize = (float)num.Value;
                _settings.Save();
                _transFont.Dispose();
                _transFont = new Font("Segoe UI", _settings.TranslationFontSize);
                _renderedSurah = -1;
                if (CurrentMode == "teks")
                {
                    _ = RenderSurahAsync(_curSurah);
                    _textMode.SetSelected(_curAyah);
                }
            }
        });
        featuresMenu.Items.Add(new ToolStripSeparator());
        featuresMenu.Items.Add("Mode Fokus (Esc = keluar)", null, (_, _) => SetFocusMode(true));
        _btnFeatures.Click += (_, _) => featuresMenu.Show(_btnFeatures, new Point(0, _btnFeatures.Height));

        _trackSpeed.Value = Math.Clamp((int)Math.Round(_settings.Speed * 10), 5, 20);
        _trackSpeed.ValueChanged += (_, _) =>
        {
            float sp = _trackSpeed.Value / 10f;
            _audio.Speed = sp;
            _settings.Speed = sp;
            _settings.Save();
        };

        _trackVolume.ValueChanged += (_, _) =>
        {
            _audio.VolumePercent = _trackVolume.Value;
            _settings.Volume = _trackVolume.Value;
            _settings.Save();
        };

        _btnSearch.Click += (_, _) => OpenSearch();
        _btnOpenTafsir.Click += (_, _) => OpenTafsirInBrowser();
        _txtSearch.KeyDown += (_, e) =>
        {
            if (e.KeyCode == Keys.Enter)
            {
                e.SuppressKeyPress = true;
                OpenSearch();
            }
        };

        _textMode.AyahClicked += aya =>
        {
            _ = GotoAyahAsync(_curSurah, aya);
            if (_chkPlayOnClick.Checked) PlayAyah(_curSurah, aya, withIntro: true);
        };

        _mushafView.AyahClicked += (s, a) =>
        {
            _ = GotoAyahAsync(s, a);
            if (_chkPlayOnClick.Checked) PlayAyah(s, a, withIntro: true);
        };

        _hifz.PlayRequested += (s, a) =>
        {
            _ = GotoAyahAsync(s, a);
            PlayAyah(s, a, withIntro: true);
        };

        _mushafView.TooltipProvider = TooltipForAyah;

        Shown += async (_, _) => await GotoAyahAsync(_settings.Surah, _settings.Ayah);

        FormClosing += (_, _) =>
        {
            _playToken++;
            _playCts?.Cancel();
            _audio.Close();
            _mini?.Close();
            _trayIcon?.Dispose();
            TtsService.Dispose();
            _settings.Save();
        };
    }

    private void SwitchMode(string mode)
    {
        _textMode.Visible = mode == "teks";
        _mushafView.Visible = mode == "mushaf";
        _mushafRight.Visible = mode == "mushaf" && _chkShowTrans.Checked;
        _hifz.Visible = mode == "hifz";
        _btnPagePrev.Visible = _cmbPage.Visible = _btnPageNext.Visible = mode == "mushaf";
        _cmbJuz.Visible = mode == "mushaf";
        _btnZoomIn.Visible = _btnZoomOut.Visible = mode == "mushaf";
        _btnDownload.Visible = mode == "mushaf";

        if (mode == "teks")
        {
            _ = GotoAyahAsync(_curSurah, _curAyah);
        }
        else if (mode == "mushaf")
        {
            var mt = CurrentMushafType;
            if (mt != null)
            {
                int page = QuranData.FindPage(mt.PageKey, _curSurah, _curAyah);
                _ = LoadMushafPageAsync(page, _curSurah, _curAyah);
            }
        }
    }

    private void StepPage(int delta)
    {
        var mt = CurrentMushafType;
        if (mt == null || _mushafView.CurrentPage < 0) return;
        int target = Math.Clamp(_mushafView.CurrentPage + delta * 2, 1, QuranData.PageCount(mt.PageKey));
        var (s, a) = QuranData.PageStart(mt.PageKey, target);
        _ = GotoAyahAsync(s, a);
    }

    private async Task EnsureSurahRenderedAsync(int surah)
    {
        if (_renderedSurah == surah && _curAyahs.Count > 0) return;

        ShowStatus("Memuat surah…");
        var arabic = MadinahText.GetSurah(surah);
        if (!MadinahText.Available || arabic.Values.All(string.IsNullOrWhiteSpace))
        {
            arabic = await TarjamaAsync("ar_ayat", surah);
        }
        var trans = CurrentTranslation;
        Dictionary<int, string>? transMap = null;
        if (trans != null && trans.Key != "ar_ayat")
        {
            transMap = await TarjamaAsync(trans.Key, surah);
        }

        int count = QuranData.SurahAyahCount(surah);
        var list = new List<AyahData>(count);
        for (int a = 1; a <= count; a++)
        {
            string ar = arabic.TryGetValue(a, out var av) ? av : "";
            string tr = "";
            if (trans != null)
            {
                if (trans.Key == "ar_ayat") tr = "";
                else if (transMap != null && transMap.TryGetValue(a, out var tv)) tr = tv;
            }
            list.Add(new AyahData(a, ar, tr));
        }

        _curAyahs = list;
        _textMode.Render(list, _arabicFont, _transFont, trans?.Rtl ?? false, _tafsirFont);
        _textMode.SetTranslationVisible(_chkShowTrans.Checked);
        _renderedSurah = surah;
    }

    private async Task RenderSurahAsync(int surah)
    {
        try
        {
            await EnsureSurahRenderedAsync(surah);
        }
        catch (Exception ex)
        {
            ShowStatus("Gagal memuat teks: " + ex.Message, error: true);
        }
    }

    private async Task GotoAyahAsync(int surah, int ayah, bool pushHistory = true)
    {
        ayah = Math.Clamp(ayah, 1, QuranData.SurahAyahCount(surah));
        _curSurah = surah;
        _curAyah = ayah;
        _settings.Surah = surah;
        _settings.Ayah = ayah;
        _settings.Save();
        if (pushHistory) PushHistory(surah, ayah);

        _uiBusy = true;
        _cmbSurah.SelectedIndex = surah - 1;
        FillAyatCombo(surah);
        _cmbAyah.SelectedIndex = ayah - 1;
        _numRangeFrom.Maximum = QuranData.SurahAyahCount(surah);
        _numRangeTo.Maximum = _numRangeFrom.Maximum;
        var mt = CurrentMushafType;
        if (mt != null)
        {
            int page = QuranData.FindPage(mt.PageKey, surah, ayah);
            if (_cmbPage.Items.Count > 0) _cmbPage.SelectedIndex = Math.Clamp(page - 1, 0, _cmbPage.Items.Count - 1);
        }
        _uiBusy = false;

        if (CurrentMode == "teks")
        {
            await RenderSurahAsync(surah);
            _textMode.SetSelected(ayah);
        }
        else if (CurrentMode == "mushaf" && mt != null)
        {
            int page = QuranData.FindPage(mt.PageKey, surah, ayah);
            if (page != _mushafView.CurrentPage)
            {
                await LoadMushafPageAsync(page, surah, ayah);
            }
            else
            {
                _mushafView.SetSelected((surah, ayah));
                UpdateMushafInfo(surah, ayah);
            }
        }

        UpdateTafsirPanel(surah, ayah);
        if (CurrentMode == "teks" && _chkInlineTafsir.Checked)
        {
            _ = LoadInlineTafsirAsync(surah, ayah);
        }

        var info = SurahList.Get(surah);
        string status = $"Surah {surah}. {info.EnglishName} — ayat {ayah}/{QuranData.SurahAyahCount(surah)}";

        var mtNow = CurrentMushafType;
        if (CurrentMode == "mushaf" && mtNow != null)
        {
            int page = QuranData.FindPage(mtNow.PageKey, surah, ayah);
            status += $" • Hal {page}/{QuranData.PageCount(mtNow.PageKey)}";
        }

        byte saj = QuranData.SajdaType(surah, ayah);
        if (saj == 2) status += " • ⚠ Ayat sajdah (wajib)";
        else if (saj == 1) status += " • ⚠ Ayat sajdah (disunnahkan)";

        ShowStatus(status);
        UpdateMini();
    }

    private async Task LoadMushafPageAsync(int page, int? selectSurah, int? selectAyah)
    {
        var mt = CurrentMushafType;
        if (mt == null) return;

        ShowStatus($"Memuat halaman {page}…");
        try
        {
            await _mushafView.LoadAsync(page, mt.Key, ProgramServices.Http, CancellationToken.None);
            _mushafView.SetZoom(_settings.Zoom);

            var (rightPage, leftPage) = _mushafView.SpreadPages;
            int pageCount = QuranData.PageCount(mt.PageKey);
            foreach (var p in new[] { rightPage, leftPage })
            {
                if (p < 1 || p > pageCount) continue;
                for (int attempt = 1; attempt <= 3; attempt++)
                {
                    try
                    {
                        var hilites = await ProgramServices.Api.GetHilitesAsync(p, CancellationToken.None);
                        _mushafView.SetHilites(p, hilites);
                        break;
                    }
                    catch (Exception ex) when (attempt < 3)
                    {
                        await Task.Delay(600 * attempt);
                        if (attempt == 2) Program.Log(ex);
                    }
                }
            }
            BuildAyahStrip(new[] { rightPage, leftPage });

            if (selectSurah.HasValue && selectAyah.HasValue)
            {
                _mushafView.SetSelected((selectSurah.Value, selectAyah.Value));
                UpdateMushafInfo(selectSurah.Value, selectAyah.Value);
            }

            ShowStatus($"Hal {page} — layar {rightPage}–{leftPage} dari {pageCount} • {mt.Display}");
        }
        catch (Exception ex)
        {
            Program.Log(ex);
            ShowStatus("Gagal memuat mushaf: " + ex.Message, error: true);
        }
    }

    private void BuildAyahStrip(IEnumerable<int> pages)
    {
        var mt = CurrentMushafType;
        if (mt == null) return;
        _ayahStrip.SuspendLayout();
        _ayahStrip.Controls.Clear();
        foreach (var page in pages)
        {
            if (page < 1 || page > QuranData.PageCount(mt.PageKey)) continue;
            foreach (var (s, a) in QuranData.PageAyahs(mt.PageKey, page))
            {
                var b = new Button
                {
                    Text = Utils.ToArabicDigits(a),
                    Width = 34,
                    Height = 30,
                    Tag = (s, a),
                };
                _stripTip.SetToolTip(b, $"Surah {s} ayat {a}");
                b.Click += (_, _) =>
                {
                    if (b.Tag is (int ss, int aa)) _ = GotoAyahAsync(ss, aa);
                };
                _ayahStrip.Controls.Add(b);
            }
        }
        _ayahStrip.ResumeLayout();
    }

    private async void UpdateMushafInfo(int surah, int ayah)
    {
        var info = SurahList.Get(surah);
        _mushafInfo.Clear();

        _mushafInfo.SelectionFont = new Font("Segoe UI", 11f, FontStyle.Bold);
        _mushafInfo.AppendText($"Surah {surah}. {info.EnglishName} ({info.ArabicName}) — Ayat {ayah}\n\n");

        try
        {
            var t = CurrentTranslation;
            if (t != null && _chkShowTrans.Checked)
            {
                var map = await TarjamaAsync(t.Key, surah);
                if (map.TryGetValue(ayah, out var text))
                {
                    _mushafInfo.RightToLeft = t.Rtl ? RightToLeft.Yes : RightToLeft.No;
                    _mushafInfo.SelectionFont = new Font("Segoe UI", 11f);
                    _mushafInfo.SelectionColor = Color.FromArgb(70, 70, 70);
                    _mushafInfo.AppendText(text + "\n");
                }
            }
        }
        catch
        {
        }
    }

    private async void UpdateTafsirPanel(int surah, int ayah)
    {
        var author = CurrentTafsirKey;
        if (author == null) return;

        _tafsirHeader.Text = $"Tafsir — {Tafsirs.Find(author)?.Display ?? author} — {surah}:{ayah} (memuat…)";
        _tafsirText.Clear();
        try
        {
            var raw = await ProgramServices.Api.GetTafsirAsync(author, surah, ayah, CancellationToken.None);
            if (_curSurah != surah || _curAyah != ayah) return;

            string html = raw;
            int sep = raw.IndexOf("|||", StringComparison.Ordinal);
            if (sep >= 0) html = raw[(sep + 3)..];

            var opt = Tafsirs.Find(author);
            bool arabic = opt?.IsArabic ?? true;
            _tafsirText.RightToLeft = arabic ? RightToLeft.Yes : RightToLeft.No;
            _tafsirText.Font = arabic ? new Font("Traditional Arabic", 15f) : new Font("Segoe UI", 11.5f);
            _tafsirText.Text = KsuApi.StripHtml(html);
            _tafsirText.SelectionStart = 0;
            _lastTafsirS = surah;
            _lastTafsirA = ayah;
            _tafsirHeader.Text = $"Tafsir — {Tafsirs.Find(author)?.Display ?? author} — {surah}:{ayah}";
        }
        catch (Exception ex)
        {
            _tafsirText.Text = "Gagal memuat tafsir: " + ex.Message;
            _tafsirHeader.Text = $"Tafsir — {surah}:{ayah}";
        }
    }

    private void OpenSearch()
    {
        using var dlg = new Controls.SearchDialog(_txtSearch.Text);
        if (dlg.ShowDialog(this) == DialogResult.OK && dlg.Selected.HasValue)
        {
            _searchHits.Clear();
            foreach (var r in dlg.Results)
            {
                _searchHits.Add((r.Surah, r.Ayah));
            }
            _mushafView.SetSearchHits(_searchHits);

            var (s, a) = dlg.Selected.Value;
            _ = GotoAyahAsync(s, a);
        }
    }

    private async Task LoadInlineTafsirAsync(int surah, int ayah)
    {
        var author = CurrentTafsirKey;
        if (author == null) return;
        try
        {
            var raw = await ProgramServices.Api.GetTafsirAsync(author, surah, ayah, CancellationToken.None);
            if (_curSurah != surah || _curAyah != ayah || CurrentMode != "teks" || !_chkInlineTafsir.Checked) return;

            string html = raw;
            int sep = raw.IndexOf("|||", StringComparison.Ordinal);
            if (sep >= 0) html = raw[(sep + 3)..];

            var opt = Tafsirs.Find(author);
            bool arabic = opt?.IsArabic ?? true;
            string text = KsuApi.StripHtml(html);
            if (text.Length > 1200) text = text[..1200] + "…";
            _textMode.SetTafsir(ayah, text, arabic ? _tafsirFont : _transFont, arabic);
        }
        catch (Exception ex)
        {
            if (_curSurah == surah && _curAyah == ayah)
            {
                _textMode.SetTafsir(ayah, "(Gagal memuat tafsir: " + ex.Message + ")", _transFont, false);
            }
        }
    }

    private void PlayAyah(int surah, int ayah, bool withIntro)
    {
        var reciter = CurrentReciter;
        var pb = CurrentPb;
        bool hasOverride = _playingPlaylist && _playlistFolder != null;
        if (reciter == null && pb == null && !hasOverride)
        {
            ShowStatus("Pilih qari terlebih dahulu", error: true);
            return;
        }

        _playToken++;
        int token = _playToken;
        _playCts?.Cancel();
        _playCts?.Dispose();
        _playCts = new CancellationTokenSource();

        _playQueue.Clear();
        _audio.Stop();
        _audio.Close();

        string folder = pb?.Folder ?? (hasOverride ? _playlistFolder : reciter?.Folder) ?? reciter!.Folder;
        if (withIntro && pb == null)
        {
            if (!_introPlayed)
            {
                _playQueue.Enqueue(KsuAudio.AudhubillahUrl());
                _introPlayed = true;
            }
            if (ayah == 1 && surah != 1 && surah != 9 && _basmalaSurah != surah)
            {
                _playQueue.Enqueue(KsuAudio.BasmalaUrl(reciter!.Folder));
                _basmalaSurah = surah;
            }
        }

        _playQueue.Enqueue(KsuAudio.AyahUrl(folder, surah, ayah));
        _repeatRemaining = CurrentRepeat;
        if (_chkRepeatRange.Checked)
        {
            _rangeLoopsRemaining = CurrentRepeat;
        }

        UpdatePlayButton();
        _ = PlayNextInQueueAsync(token);
    }

    private async Task PlayNextInQueueAsync(int token)
    {
        if (token != _playToken) return;

        if (_playQueue.Count == 0)
        {
            OnQueueFinished(token);
            return;
        }

        string url = _playQueue.Dequeue();
        string rel = url.Substring(url.IndexOf("/ayat/mp3/", StringComparison.Ordinal) + "/ayat/mp3/".Length);

        try
        {
            ShowStatus("Mengunduh audio…");
            var local = await EnsureAudioAsync(url, rel, _playCts?.Token ?? CancellationToken.None);
            if (token != _playToken) return;

            if (!_audio.Open(local)) throw new Exception("MCI gagal membuka file audio");
            if (!_audio.Play()) throw new Exception("Gagal memutar audio");
            UpdatePlayButton();
            ShowStatus($"Memutar: {rel}");
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            if (token == _playToken)
            {
                UpdatePlayButton();
                ShowStatus("Audio gagal: " + ex.Message, error: true);
            }
        }
    }

    private async Task<string> EnsureAudioAsync(string url, string rel, CancellationToken ct)
    {
        var local = KsuAudio.CachePath(rel);
        if (File.Exists(local)) return local;

        Directory.CreateDirectory(Path.GetDirectoryName(local)!);
        using var resp = await ProgramServices.Http.GetAsync(url, ct);
        resp.EnsureSuccessStatusCode();
        await using var src = await resp.Content.ReadAsStreamAsync(ct);
        await using var dst = File.Create(local);
        await src.CopyToAsync(dst, ct);
        return local;
    }

    private void OnQueueFinished(int token)
    {
        if (token != _playToken) return;
        UpdatePlayButton();

        if (_chkTeacher.Checked)
        {
            _ = TeacherReplayAsync(token);
            return;
        }

        if (_repeatRemaining == -1 || _repeatRemaining > 1)
        {
            if (_repeatRemaining > 0) _repeatRemaining--;

            var reciter = CurrentReciter;
            var pb = CurrentPb;
            if (reciter == null && pb == null)
            {
                ShowStatus("Pilih qari terlebih dahulu", error: true);
                return;
            }
            string folder = pb?.Folder ?? reciter!.Folder;
            _playQueue.Enqueue(KsuAudio.AyahUrl(folder, _curSurah, _curAyah));
            _ = PlayNextInQueueAsync(token);
            return;
        }

        if (!_chkAutoNext.Checked)
        {
            ShowStatus("Selesai");
            return;
        }

        if (_playingPlaylist && _curAyah >= QuranData.SurahAyahCount(_curSurah))
        {
            AdvancePlaylist();
            return;
        }

        int nextS = _curSurah;
        int nextA = _curAyah + 1;

        if (_chkRepeatRange.Checked)
        {
            int from = (int)_numRangeFrom.Value;
            int to = (int)_numRangeTo.Value;
            if (nextA > to)
            {
                if (_rangeLoopsRemaining == -1 || _rangeLoopsRemaining > 1)
                {
                    if (_rangeLoopsRemaining > 0) _rangeLoopsRemaining--;
                    nextA = from;
                }
                else
                {
                    ShowStatus($"Rentang {from}–{to} selesai");
                    return;
                }
            }
            nextA = Math.Clamp(nextA, 1, QuranData.SurahAyahCount(nextS));
            _ = GotoAyahAsync(nextS, nextA).ContinueWith(_ =>
                PlayAyah(_curSurah, _curAyah, withIntro: false), TaskScheduler.FromCurrentSynchronizationContext());
            return;
        }

        if (nextA <= QuranData.SurahAyahCount(_curSurah))
        {
            _ = GotoAyahAsync(nextS, nextA).ContinueWith(_ =>
                PlayAyah(_curSurah, _curAyah, withIntro: false), TaskScheduler.FromCurrentSynchronizationContext());
        }
        else if (_curSurah < 114)
        {
            _ = GotoAyahAsync(_curSurah + 1, 1).ContinueWith(_ =>
                PlayAyah(_curSurah, _curAyah, withIntro: false), TaskScheduler.FromCurrentSynchronizationContext());
        }
        else
        {
            ShowStatus("Selesai — 114 surah tamat");
        }
    }

    private async Task TeacherReplayAsync(int token)
    {
        try
        {
            await Task.Delay(2600);
        }
        catch
        {
        }
        if (token != _playToken || !_chkTeacher.Checked || !IsHandleCreated) return;

        var reciter = CurrentReciter;
        var pb = CurrentPb;
        if (reciter == null && pb == null) return;
        string folder = pb?.Folder ?? reciter!.Folder;

        _playQueue.Clear();
        _playQueue.Enqueue(KsuAudio.AyahUrl(folder, _curSurah, _curAyah));
        await PlayNextInQueueAsync(token);
    }

    private void PushHistory(int surah, int ayah)
    {
        if (_histPos >= 0 && _histPos < _history.Count
            && _history[_histPos].Surah == surah && _history[_histPos].Ayah == ayah) return;

        while (_history.Count > _histPos + 1) _history.RemoveAt(_history.Count - 1);
        _history.Add((surah, ayah));
        if (_history.Count > 200) _history.RemoveAt(0);
        _histPos = _history.Count - 1;
    }

    private void NavBack()
    {
        if (_histPos > 0)
        {
            _histPos--;
            var (s, a) = _history[_histPos];
            _ = GotoAyahAsync(s, a, pushHistory: false);
        }
    }

    private void NavForward()
    {
        if (_histPos < _history.Count - 1)
        {
            _histPos++;
            var (s, a) = _history[_histPos];
            _ = GotoAyahAsync(s, a, pushHistory: false);
        }
    }

    private void StopPlayback(string message)
    {
        _playToken++;
        _playCts?.Cancel();
        _audio.Stop();
        _audio.Close();
        _playQueue.Clear();
        UpdatePlayButton();
        ShowStatus(message);
    }

    private void UpdatePlayButton()
    {
        _btnPlayPause.Text = _audio.IsOpen && _audio.IsPlaying ? "⏸ Pause" : "▶ Play";
        UpdateMini();
    }

    private void InitTrayAndReminder()
    {
        try
        {
            _trayIcon = new NotifyIcon
            {
                Icon = Icon,
                Text = "Quran Desktop",
                Visible = false,
            };
            _trayIcon.DoubleClick += (_, _) =>
            {
                Show();
                WindowState = FormWindowState.Normal;
            };
        }
        catch
        {
        }

        _reminderTimer = new System.Windows.Forms.Timer { Interval = 30000 };
        _reminderTimer.Tick += (_, _) => { CheckReminder(); CheckPrayerTimes(); };
        _reminderTimer.Start();
    }

    private void CheckReminder()
    {
        if (!_settings.ReminderEnabled)
        {
            if (_trayIcon != null) _trayIcon.Visible = false;
            return;
        }
        if (_reminderFiredToday) return;

        var now = DateTime.Now;
        if (TimeSpan.TryParse(_settings.ReminderTime, out var t)
            && now.TimeOfDay >= t
            && now.TimeOfDay < t.Add(TimeSpan.FromMinutes(5)))
        {
            _reminderFiredToday = true;
            if (_trayIcon != null)
            {
                _trayIcon.Visible = true;
                _trayIcon.ShowBalloonTip(8000, "Waktunya baca Al-Qur'an",
                    "Sedikit demi sedikit, lama-lama jadi bukit. Buka Quran Desktop sekarang.",
                    ToolTipIcon.Info);
            }
            ShowStatus("Pengingat: waktunya baca Al-Qur'an!");
        }
    }

    private async void CheckPrayerTimes()
    {
        try
        {
            if (_prayerFetchedDate != DateTime.Today)
            {
                _prayerFetchedDate = DateTime.Today;
                _notifiedPrayers.Clear();
                string url = $"https://api.aladhan.com/v1/timingsByCity?city={Uri.EscapeDataString(_settings.PrayerCity)}"
                    + $"&country={Uri.EscapeDataString(_settings.PrayerCountry)}&method={_settings.PrayerMethod}";
                using var resp = await ProgramServices.Http.GetAsync(url, CancellationToken.None);
                resp.EnsureSuccessStatusCode();
                var json = await resp.Content.ReadAsStringAsync();
                _prayerTimes = System.Text.Json.JsonDocument.Parse(json).RootElement
                    .GetProperty("data").GetProperty("timings").EnumerateObject()
                    .ToDictionary(p => p.Name, p => (p.Value.GetString() ?? "")[..5]);
            }

            if (_prayerTimes == null || !_settings.PrayerNotify) return;
            var now = DateTime.Now.TimeOfDay;
            foreach (var name in new[] { "Fajr", "Dhuhr", "Asr", "Maghrib", "Isha" })
            {
                if (_notifiedPrayers.Contains(name)) continue;
                if (_prayerTimes.TryGetValue(name, out var t) && TimeSpan.TryParse(t, out var time))
                {
                    var diff = time - now;
                    if (diff > TimeSpan.Zero && diff <= TimeSpan.FromMinutes(10))
                    {
                        _notifiedPrayers.Add(name);
                        string label = name switch
                        {
                            "Fajr" => "Subuh",
                            "Dhuhr" => "Zuhur",
                            "Asr" => "Asar",
                            "Maghrib" => "Magrib",
                            "Isha" => "Isya",
                            _ => name,
                        };
                        if (_trayIcon != null)
                        {
                            _trayIcon.Visible = true;
                            _trayIcon.ShowBalloonTip(6000, $"Sholat {label} {t}",
                                $"Waktu sholat {label} pukul {t} — bersiaplah.", ToolTipIcon.Info);
                        }
                        ShowStatus($"Sholat {label} pukul {t} — 10 menit lagi");
                    }
                }
            }
        }
        catch
        {
        }
    }

    private void ApplyDarkMode()
    {
        bool d = _settings.DarkMode;
        BackColor = d ? Color.FromArgb(30, 30, 34) : SystemColors.Control;
        _topContainer.BackColor = d ? Color.FromArgb(30, 30, 34) : SystemColors.Control;
        _center.BackColor = d ? Color.FromArgb(38, 38, 42) : Color.FromArgb(244, 244, 240);
        _textMode.ApplyDark(d);
        _tafsirPanel.BackColor = d ? Color.FromArgb(36, 36, 40) : Color.White;
        _tafsirText.BackColor = d ? Color.FromArgb(36, 36, 40) : Color.White;
        _tafsirText.ForeColor = d ? Color.Gainsboro : Color.Black;
        _mushafRight.BackColor = d ? Color.FromArgb(42, 42, 46) : Color.FromArgb(250, 250, 247);
        _mushafInfo.BackColor = d ? Color.FromArgb(42, 42, 46) : Color.White;
        _mushafInfo.ForeColor = d ? Color.Gainsboro : Color.Black;
    }

    private void ToggleMiniPlayer()
    {
        if (_mini == null)
        {
            _mini = new MiniPlayerForm();
            _mini.PlayPause += () => _btnPlayPause.PerformClick();
            _mini.Next += () => _btnNextAya.PerformClick();
            _mini.Prev += () => _btnPrevAya.PerformClick();
            _mini.Restore += () =>
            {
                Show();
                WindowState = FormWindowState.Normal;
                _mini?.Close();
            };
            _mini.FormClosed += (_, _) => _mini = null;
            _mini.Show(this);
            UpdateMini();
        }
        else
        {
            _mini.Close();
            _mini = null;
        }
    }

    private void UpdateMini()
    {
        var info = SurahList.Get(_curSurah);
        _mini?.SetInfo($"QS {_curSurah}:{_curAyah} — {info.EnglishName}", _audio.IsPlaying);
    }

    private void SetFocusMode(bool on)
    {
        _focusMode = on;
        _topContainer.Visible = !on;
        _tafsirPanel.Visible = !on && _chkTafsirPanel.Checked;
        _lblStatus.Visible = !on;
        Text = on
            ? "Quran Desktop — Mode Fokus (tekan Esc untuk keluar)"
            : "Quran Desktop — KSU Electronic Moshaf (WinForms)";
        if (on) ShowStatus("Mode fokus aktif");
    }

    private void StartPlaylist(List<PlaylistEntry> entries)
    {
        _playlist.Clear();
        _playlist.AddRange(entries);
        _playlistIndex = 0;
        _playingPlaylist = true;
        PlayPlaylistEntry();
    }

    private void PlayPlaylistEntry()
    {
        if (_playlistIndex < 0 || _playlistIndex >= _playlist.Count)
        {
            _playingPlaylist = false;
            _playlistFolder = null;
            ShowStatus("Playlist selesai");
            return;
        }
        var e = _playlist[_playlistIndex];
        _playlistFolder = Reciters.Find(e.QareeKey)?.Folder;
        ShowStatus($"Playlist {_playlistIndex + 1}/{_playlist.Count}: QS {e.Surah} — {e.QareeName}");
        _ = GotoAyahAsync(e.Surah, 1).ContinueWith(_ =>
            PlayAyah(_curSurah, _curAyah, withIntro: true), TaskScheduler.FromCurrentSynchronizationContext());
    }

    private void AdvancePlaylist()
    {
        _playlistIndex++;
        PlayPlaylistEntry();
    }

    private void ShowStatus(string message, bool error = false)
    {
        _lblStatus.Text = message;
        _lblStatus.ForeColor = error ? Color.Salmon : Color.Gainsboro;
    }

    private string TooltipForAyah(int surah, int ayah)
    {
        var t = CurrentTranslation;
        if (t == null || t.Key == "ar_ayat") return "";
        if (_tarjamaLocal.TryGetValue(t.Key + "|" + surah, out var map) && map.TryGetValue(ayah, out var text))
        {
            string clean = KsuApi.StripHtml(text).Replace('\n', ' ');
            return clean.Length > 120 ? clean[..120] + "…" : clean;
        }
        return "";
    }

    private static string TafsirWebUrl(string author, int surah, int ayah)
    {
        var web = author switch
        {
            "sa3dy" => "saadi",
            "qortoby" => "qortobi",
            "e3rab" => "eerab",
            _ => author,
        };
        return $"https://quran.ksu.edu.sa/tafseer/{web}/sura{surah}-aya{ayah}.html";
    }

    private void OpenTafsirInBrowser()
    {
        var author = CurrentTafsirKey;
        if (author == null || _lastTafsirS == 0) return;
        try
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = TafsirWebUrl(author, _lastTafsirS, _lastTafsirA),
                UseShellExecute = true,
            });
        }
        catch (Exception ex)
        {
            ShowStatus("Gagal membuka browser: " + ex.Message, error: true);
        }
    }

    protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
    {
        var ctl = ActiveControl;
        bool inText = ctl is TextBoxBase || ctl is ComboBox;
        if (!inText)
        {
            switch (keyData)
            {
                case Keys.Right:
                    _btnNextAya.PerformClick();
                    return true;
                case Keys.Left:
                    _btnPrevAya.PerformClick();
                    return true;
                case Keys.Space:
                    _btnPlayPause.PerformClick();
                    return true;
                case Keys.PageDown when CurrentMode == "mushaf":
                    StepPage(1);
                    return true;
                case Keys.PageUp when CurrentMode == "mushaf":
                    StepPage(-1);
                    return true;
                case Keys.Control | Keys.F:
                    _txtSearch.Focus();
                    return true;
                case Keys.Escape when _focusMode:
                    SetFocusMode(false);
                    return true;
                case Keys.Alt | Keys.Left:
                    NavBack();
                    return true;
                case Keys.Alt | Keys.Right:
                    NavForward();
                    return true;
            }
        }
        return base.ProcessCmdKey(ref msg, keyData);
    }

    protected override void WndProc(ref Message m)
    {
        base.WndProc(ref m);
    }
}
