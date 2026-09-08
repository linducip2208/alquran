# Quran Desktop Release Checklist

## Build

Run from the repository root:

```powershell
.\scripts\publish.ps1 -Version 1.4.0
```

The script creates a self-contained `win-x64` archive under `artifacts/` and a
`.sha256` checksum file. Do not include the user's `downloads` folder in a
release archive; it is created beside the executable on first run.

All download/offline content MUST live beside the executable
(`<AppContext.BaseDirectory>\downloads`). Never ship or document a layout that
stores downloads under `%LOCALAPPDATA%`, `%APPDATA%`, `%TEMP%`, Documents, or
the Windows Downloads folder. `settings.json`, `progress.json`, and
`error.log` are the only files allowed under `%LOCALAPPDATA%\QuranDesktop`.
The Inno Setup installer therefore installs per-user into
`{localappdata}\Programs\Quran Desktop` (writable) instead of Program Files.

## Before publishing

1. Run `dotnet build QuranDesktop/QuranDesktop.csproj --configuration Release`.
2. Run `QuranDesktop.exe --selftest` and confirm zero failures.
3. Test on a standard user account whose executable folder is read-only.
4. Test first launch without network, then test partial downloads and resume.
5. Verify the SHA-256 checksum after uploading the archive.
6. Publish release notes with data-source attribution and supported Windows versions.

## Update behavior

The application checks the configured update feed and opens the release page.
It does not silently replace the executable. This keeps updates reversible and
allows the user to verify the checksum before installing a new archive.

## Optional installer

If Inno Setup is installed, open `installer/QuranDesktop.iss` after running the
publish script. The installer uses per-user privileges and installs the
executable into `%LOCALAPPDATA%\Programs\Quran Desktop` — a writable location
so the `downloads` folder can live beside the EXE. It never installs into
Program Files, because download content must stay beside the executable.
