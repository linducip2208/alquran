namespace QuranDesktop;

/// <summary>
/// Coordinates the I/O part of audio playback: cache lookup, resumable download,
/// validation, and handing a complete file to the audio engine. Queue policy and
/// UI navigation remain in MainForm.
/// </summary>
internal sealed class AudioPlaybackService
{
    private readonly IAudioEngine _engine;
    private readonly Action<string> _status;

    public AudioPlaybackService(IAudioEngine engine, Action<string> status)
    {
        _engine = engine;
        _status = status;
    }

    public async Task<string> EnsureAndPlayAsync(
        string url,
        string cacheRelativePath,
        CancellationToken cancellationToken)
    {
        string local = KsuAudio.CachePath(cacheRelativePath);
        var status = OfflineContentService.Instance.GetAudioStatus(cacheRelativePath);
        if (!status.IsValid)
        {
            _status("Mengunduh audio…");
            bool downloaded = await DownloadManager.Shared.EnsureFileAsync(
                ProgramServices.Http, url, cacheRelativePath, cancellationToken);
            if (!downloaded || !DownloadManager.FileValid(local, 4096))
                throw new IOException("Audio belum tersedia atau hasil unduhan tidak valid.");
        }

        if (!_engine.Open(local)) throw new IOException("Audio gagal dibuka.");
        if (!_engine.Play()) throw new IOException("Audio gagal diputar.");
        return local;
    }
}
