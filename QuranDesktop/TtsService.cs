using System.Speech.Synthesis;

namespace QuranDesktop;

internal static class TtsService
{
    private static SpeechSynthesizer? _synth;
    private static bool _initTried;

    public static bool Available
    {
        get
        {
            Ensure();
            return _synth != null;
        }
    }

    private static void Ensure()
    {
        if (_initTried) return;
        _initTried = true;
        try
        {
            _synth = new SpeechSynthesizer();
            _synth.SetOutputToDefaultAudioDevice();
        }
        catch
        {
            _synth = null;
        }
    }

    public static void Speak(string text)
    {
        Ensure();
        if (_synth == null) return;
        try
        {
            var voice = _synth.GetInstalledVoices()
                .FirstOrDefault(v => v.VoiceInfo.Culture.Name.StartsWith("id", StringComparison.OrdinalIgnoreCase)
                                     || v.VoiceInfo.Culture.Name.StartsWith("en", StringComparison.OrdinalIgnoreCase));
            if (voice != null) _synth.SelectVoice(voice.VoiceInfo.Name);
            _synth.SpeakAsyncCancelAll();
            _synth.SpeakAsync(text);
        }
        catch
        {
        }
    }

    public static void Stop()
    {
        try
        {
            _synth?.SpeakAsyncCancelAll();
        }
        catch
        {
        }
    }

    public static void Dispose()
    {
        try
        {
            _synth?.Dispose();
        }
        catch
        {
        }
        _synth = null;
    }
}
