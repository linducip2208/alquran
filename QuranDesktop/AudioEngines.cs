using System.Runtime.InteropServices;

namespace QuranDesktop;

internal interface IAudioEngine : IDisposable
{
    bool IsOpen { get; }
    bool IsPlaying { get; }
    bool IsPaused { get; }
    int VolumePercent { get; set; }
    float Speed { get; set; }
    event Action? Finished;
    bool Open(string file);
    bool Play();
    void Pause();
    void Resume();
    void Stop();
    void Close();
}

internal sealed class MciEngine : NativeWindow, IAudioEngine
{
    private sealed class NotifyWindow : NativeWindow
    {
        public Action<int>? OnNotify;

        protected override void WndProc(ref Message m)
        {
            if (m.Msg == 0x3B9)
            {
                OnNotify?.Invoke(m.WParam.ToInt32());
            }
            base.WndProc(ref m);
        }
    }

    private readonly MciAudio _mci = new();
    private readonly NotifyWindow _wnd = new();

    public event Action? Finished;

    public MciEngine()
    {
        _wnd.CreateHandle(new CreateParams());
        _wnd.OnNotify = wParam =>
        {
            if (wParam == MciAudio.NotifySuccess) Finished?.Invoke();
        };
    }

    public bool IsOpen => _mci.IsOpen;
    public bool IsPlaying => _mci.IsPlaying;
    public bool IsPaused => _mci.IsPaused;

    public int VolumePercent
    {
        get => _mci.VolumePercent;
        set => _mci.VolumePercent = value;
    }

    public float Speed
    {
        get => 1f;
        set
        {
        }
    }

    public bool Open(string file) => _mci.Open(file);
    public bool Play() => _mci.Play(_wnd.Handle);
    public void Pause() => _mci.Pause();
    public void Resume() => _mci.Resume();
    public void Stop() => _mci.Stop();
    public void Close() => _mci.Close();
    public void Dispose() => _mci.Dispose();
}

internal sealed class WmpEngine : IAudioEngine
{
    private dynamic? _wmp;
    private readonly System.Windows.Forms.Timer _poll = new() { Interval = 200 };
    private bool _finishedFired;
    private bool _wasPlaying;

    public event Action? Finished;

    public bool Available { get; private set; }

    public WmpEngine()
    {
        try
        {
            var type = Type.GetTypeFromProgID("WMPlayer.OCX.7");
            if (type == null) return;
            _wmp = Activator.CreateInstance(type);
            if (_wmp == null) return;
            _wmp.settings.autoStart = false;
            _wmp.settings.volume = 80;
            _wmp.settings.rate = 1.0;
            _wmp.uiMode = "Invisible";
            Available = true;

            _poll.Tick += (_, _) => PollFinished();
            _poll.Start();
        }
        catch
        {
            _wmp = null;
            Available = false;
        }
    }

    private void PollFinished()
    {
        try
        {
            if (_wmp == null) return;
            bool playing = ((int)_wmp.playState) == 3;
            if (playing) _wasPlaying = true;

            if (_wasPlaying && !_finishedFired)
            {
                double dur = 0, pos = 0;
                try
                {
                    dur = Convert.ToDouble(_wmp.currentMedia.duration);
                    pos = Convert.ToDouble(_wmp.Ctlcontrols.currentPosition);
                }
                catch
                {
                }
                if (dur > 0 && pos >= dur - 0.3)
                {
                    _finishedFired = true;
                    Finished?.Invoke();
                }
            }
            if (!playing) _wasPlaying = false;
        }
        catch
        {
        }
    }

    public bool IsOpen => Available && _wmp != null && !string.IsNullOrEmpty((string)_wmp.URL);
    public bool IsPlaying => Available && _wmp != null && ((int)_wmp.playState) == 3;
    public bool IsPaused => Available && _wmp != null && ((int)_wmp.playState) == 2;

    public int VolumePercent
    {
        get => Available && _wmp != null ? (int)_wmp.settings.volume : 80;
        set
        {
            if (Available && _wmp != null) _wmp.settings.volume = Math.Clamp(value, 0, 100);
        }
    }

    public float Speed
    {
        get => Available && _wmp != null ? (float)Convert.ToDouble(_wmp.settings.rate) : 1f;
        set
        {
            if (Available && _wmp != null)
            {
                double v = Math.Clamp(value, 0.5, 2.0);
                _wmp.settings.rate = v;
            }
        }
    }

    public bool Open(string file)
    {
        if (!Available || _wmp == null) return false;
        _finishedFired = false;
        _wasPlaying = false;
        _wmp.URL = file;
        return true;
    }

    public bool Play()
    {
        if (!Available || _wmp == null) return false;
        _finishedFired = false;
        _wmp.Ctlcontrols.play();
        return true;
    }

    public void Pause()
    {
        if (Available && _wmp != null && IsPlaying) _wmp.Ctlcontrols.pause();
    }

    public void Resume()
    {
        if (Available && _wmp != null && IsPaused) _wmp.Ctlcontrols.play();
    }

    public void Stop()
    {
        if (Available && _wmp != null) _wmp.Ctlcontrols.stop();
    }

    public void Close()
    {
        if (Available && _wmp != null)
        {
            _wmp.Ctlcontrols.stop();
            _wmp.URL = "";
        }
        _finishedFired = false;
        _wasPlaying = false;
    }

    public void Dispose()
    {
        Close();
        _poll.Stop();
        _poll.Dispose();
        _wmp = null;
    }
}
