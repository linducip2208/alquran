using System.Runtime.InteropServices;
using System.Text;

namespace QuranDesktop;

internal sealed class MciAudio : IDisposable
{
    public const int NotifySuccess = 0x1;

    [DllImport("winmm.dll", CharSet = CharSet.Unicode)]
    private static extern int mciSendString(string command, StringBuilder? buffer, int bufferSize, IntPtr hwndCallback);

    private readonly string _alias = "quran_" + Guid.NewGuid().ToString("N").Substring(0, 8);
    private int _volumePercent = 80;

    public bool IsOpen { get; private set; }
    public bool IsPlaying { get; private set; }
    public bool IsPaused { get; private set; }

    public int VolumePercent
    {
        get => _volumePercent;
        set
        {
            _volumePercent = Math.Clamp(value, 0, 100);
            if (IsOpen)
            {
                mciSendString($"setaudio {_alias} volume to {_volumePercent * 10}", null, 0, IntPtr.Zero);
            }
        }
    }

    public bool Open(string file)
    {
        Close();
        var sb = new StringBuilder(256);
        int r = mciSendString($"open \"{file}\" type mpegvideo alias {_alias}", sb, 256, IntPtr.Zero);
        IsOpen = r == 0;
        if (IsOpen)
        {
            mciSendString($"setaudio {_alias} volume to {_volumePercent * 10}", null, 0, IntPtr.Zero);
        }
        return IsOpen;
    }

    public bool Play(IntPtr notifyWindow)
    {
        if (!IsOpen) return false;
        int r = mciSendString($"play {_alias} notify", null, 0, notifyWindow);
        IsPlaying = r == 0;
        IsPaused = false;
        return IsPlaying;
    }

    public void Pause()
    {
        if (IsOpen && IsPlaying)
        {
            mciSendString($"pause {_alias}", null, 0, IntPtr.Zero);
            IsPlaying = false;
            IsPaused = true;
        }
    }

    public void Resume()
    {
        if (IsOpen && IsPaused)
        {
            mciSendString($"resume {_alias}", null, 0, IntPtr.Zero);
            IsPlaying = true;
            IsPaused = false;
        }
    }

    public void Stop()
    {
        if (IsOpen)
        {
            mciSendString($"stop {_alias}", null, 0, IntPtr.Zero);
            IsPlaying = false;
            IsPaused = false;
        }
    }

    public void Close()
    {
        if (IsOpen)
        {
            mciSendString($"close {_alias}", null, 0, IntPtr.Zero);
            IsOpen = false;
            IsPlaying = false;
            IsPaused = false;
        }
    }

    public void Dispose() => Close();
}
