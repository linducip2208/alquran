using NAudio.Wave;
using NAudio.Wave.SampleProviders;
using SoundTouch;

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


internal sealed class NAudioEngine : IAudioEngine
{
    private sealed class TempoSampleProvider : ISampleProvider
    {
        private readonly ISampleProvider _source;
        private readonly SoundTouchProcessor _st;
        private readonly int _channels;
        private readonly float[] _frame = new float[8192 * 8];
        private bool _ended;

        public TempoSampleProvider(ISampleProvider source)
        {
            _source = source;
            _channels = source.WaveFormat.Channels;
            _st = new SoundTouchProcessor
            {
                Channels = _channels,
                SampleRate = source.WaveFormat.SampleRate,
                Tempo = 1.0,
            };
        }

        public WaveFormat WaveFormat => _source.WaveFormat;

        public double Tempo
        {
            get => _st.Tempo;
            set => _st.Tempo = Math.Clamp(value, 0.5, 2.0);
        }

        public int Read(float[] buffer, int offset, int count)
        {
            if (_ended) return 0;
            int framesRequested = count / _channels;
            var outSpan = buffer.AsSpan(offset, count);

            int received = _st.ReceiveSamples(outSpan, framesRequested);
            if (received > 0) return received * _channels;

            int read = _source.Read(_frame, 0, _frame.Length);
            if (read == 0)
            {
                _st.Flush();
                received = _st.ReceiveSamples(outSpan, framesRequested);
                if (received == 0)
                {
                    _ended = true;
                    return 0;
                }
                return received * _channels;
            }

            _st.PutSamples(_frame.AsSpan(0, read), read / _channels);
            received = _st.ReceiveSamples(outSpan, framesRequested);
            return received * _channels;
        }
    }

    private WaveOutEvent? _output;
    private MediaFoundationReader? _reader;
    private TempoSampleProvider? _tempo;
    private bool _stopExpected;
    private float _speed = 1f;
    private int _volume = 80;

    public event Action? Finished;

    public bool IsOpen => _reader != null;
    public bool IsPlaying => _output != null && _output.PlaybackState == PlaybackState.Playing;
    public bool IsPaused => _output != null && _output.PlaybackState == PlaybackState.Paused;

    public int VolumePercent
    {
        get => _volume;
        set
        {
            _volume = Math.Clamp(value, 0, 100);
            if (_output != null) _output.Volume = _volume / 100f;
        }
    }

    public float Speed
    {
        get => _speed;
        set
        {
            _speed = Math.Clamp(value, 0.5f, 2f);
            if (_tempo != null) _tempo.Tempo = _speed;
        }
    }

    public bool Open(string file)
    {
        try
        {
            Stop();
            _reader = new MediaFoundationReader(file);
            _tempo = new TempoSampleProvider(_reader.ToSampleProvider()) { Tempo = _speed };
            _output = new WaveOutEvent();
            _output.Volume = _volume / 100f;
            _output.Init(_tempo);
            _stopExpected = false;
            _output.PlaybackStopped += (_, _) =>
            {
                if (!_stopExpected) Finished?.Invoke();
            };
            return true;
        }
        catch
        {
            Cleanup();
            return false;
        }
    }

    public bool Play()
    {
        if (_output == null) return false;
        _stopExpected = false;
        _output.Play();
        return true;
    }

    public void Pause()
    {
        if (IsPlaying) _output?.Pause();
    }

    public void Resume()
    {
        if (IsPaused) _output?.Play();
    }

    public void Stop()
    {
        _stopExpected = true;
        try
        {
            _output?.Stop();
        }
        catch
        {
        }
    }

    public void Close()
    {
        _stopExpected = true;
        Cleanup();
    }

    private void Cleanup()
    {
        try
        {
            _output?.Stop();
        }
        catch
        {
        }
        _output?.Dispose();
        _output = null;
        _reader?.Dispose();
        _reader = null;
        _tempo = null;
    }

    public void Dispose() => Close();
}