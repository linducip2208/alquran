# Quran Desktop Architecture Notes

## Runtime data

`KsuAudio.DataRoot` is the single cache root — **always beside the executable**
so a portable copy stays self-contained:

```text
<AppContext.BaseDirectory>\downloads
├── audio       # reciter audio
├── voice       # voice translations
├── mushaf      # page images
├── teks        # translations and Arabic text
├── tafsir      # tafsir responses
├── hilites     # verse coordinates
├── fonts       # downloaded fonts
├── recordings  # user WAV recordings
└── temp        # transient files and backups
```

`settings.json`, `progress.json`, and `error.log` are user preferences /
diagnostics, not download content, and remain under
`%LOCALAPPDATA%\QuranDesktop`.

`KsuAudio.LegacyDownloadsRoot`
(`%LOCALAPPDATA%\QuranDesktop\downloads`) is a **migration source only** — it
is never used for new downloads, playback, scans, or storage reports.
`OfflineMigrator` moves legacy content per-file: move when possible,
cross-volume copy → flush → verify (size / SHA-256, PNG & JSON validation)
→ delete source only after the destination is verified. Existing valid
destinations are never overwritten. The `.migration-appdata-to-exe-v2-complete`
marker is written only after a successful run; cancelled migrations stay
resumable. Startup also requires a write-permission probe
(`downloads\.write-test`) and refuses to fall back to AppData or TEMP.

## Provider boundary

`ProviderEndpoints` is the only endpoint configuration boundary. Values are
stored in `AppSettings` and can be changed from **Provider & Endpoint** in the
settings dialog. Consumers use the boundary rather than embedding endpoint
URLs in their request methods.

## Audio boundary

`AudioPlaybackService` owns cache validation, resumable download, and opening a
validated local file on `IAudioEngine`. `MainForm` owns queue policy, repeat,
teacher mode, playlist behavior, and navigation.

`UpdateService` owns release-feed parsing, semantic version comparison, asset
discovery, and update errors. It never replaces the running executable.

## Backup boundary

`BackupService` writes a versioned ZIP manifest. Each included file has a
SHA-256 digest; restore validates allowed paths, JSON syntax, and checksums
before replacing user data. Restore is followed by an application restart so
in-memory settings cannot remain stale.
