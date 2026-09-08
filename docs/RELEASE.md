# Quran Desktop Release Checklist

## Build

Run from the repository root:

```powershell
.\scripts\publish.ps1 -Version 1.4.0
```

The script creates a self-contained `win-x64` archive under `artifacts/` and a
`.sha256` checksum file. Do not include the user's `downloads` folder in a
release archive; it is created per-user under `%LOCALAPPDATA%` on first run.

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
executable under Program Files while runtime data remains in `%LOCALAPPDATA%`.
