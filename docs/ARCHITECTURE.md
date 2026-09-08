# Quran Desktop Architecture Notes

## Runtime data

`KsuAudio.DataRoot` is the single cache root:

```text
%LOCALAPPDATA%\QuranDesktop\downloads
├── audio       # reciter audio
├── voice       # voice translations
├── mushaf      # page images
├── teks        # translations and Arabic text
├── tafsir      # tafsir responses
├── hilites     # verse coordinates
├── recordings  # user WAV recordings
└── temp        # transient files and backups
```

`OfflineMigrator` reads the previous `%LOCALAPPDATA%\QuranDesktop` layout once
and merges files without overwriting newer files.

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
