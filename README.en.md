# Quran Desktop — KSU Electronic Moshaf for Windows

**Bahasa / Languages:** [Indonesia](README.md) • [English](README.en.md) • [العربية](README.ar.md)

A desktop Qur'an application (Windows Forms / C# .NET 7) rebuilt from the official
**[Quran KSU Electronic Moshaf Project](https://quran.ksu.edu.sa/index.php?ui=1&l=en)** — King Saud University, Saudi Arabia.

> Rebuilt for **Windows 10 & 11** by **Lindu Cipta Pranayama**
> Built with **GLM 5.3 Flash**
> Contact / WhatsApp: **+62 812-9605-2010**

---

## Download

**Portable (no installation required):** [Google Drive — QuranDesktop portable](https://drive.google.com/drive/folders/1A0AvGWNaHMU2bZtrvoES25RUh-pr6VMx?usp=sharing)

Alternatives:
- [GitHub Releases](https://github.com/linducip2208/alquran/releases) — download the latest win-x64 archive
- Build it yourself: `.\scripts\publish.ps1` (see below)

---

## Full Feature List

| Category | Features |
|---|---|
| Display Modes | **Mushaf** (two-page spread: odd right / even left), **Text & Translation**, **Memorization Test (Hifz)** |
| Mushaf Types | Hafs, Rewayat Warsh, Hafs Tajweed (604 pages, original KSU server images) |
| Mushaf Interaction | Click any verse directly on the page → golden highlight bubble + blue rings for search results |
| Overlay | Floating translation text at each verse position on the page |
| Reciters | **47 reciters** — full parity with the KSU site — plus Warsh, Murattal/Mujawwad/Teacher/32kbps variants (Husary, Abdul Basit, Minshawi, Sudais, Maher, Afasy, **Al-Banna, Basfar 32k, Al-Akhdar, Ayyoub 32k**, etc.) |
| Translations | **31 languages** — English (Saheeh International), Indonesian, Malay, Arabic (4 variants), Urdu, Russian, **Bengali, Somali, Tamil, Hausa, Dutch (Keyzer), Swahili, Thai, Uzbek, Mandarin** |
| Tafsir | **9 books** — Tafsir Jalalain (Indonesian), Al-Muyassar, Ibn Kathir, As-Sa'dy, Al-Baghawy, Al-Qortoby, At-Tabary, I'rab, Tafhim (Russian) |
| Inline Tafsir | Tafsir displayed under the selected verse in Text mode |
| Talaqaa (Voice) | Voice translations: English, French, Urdu, Bosnian |
| Audio Player | Per-verse, auto-advance across verses & surahs, repeat 1×–10×/∞, **verse-range repeat**, **teacher mode** (periodic replay), automatic basmalah & audhubillah (exact KSU reciter rules), volume, **playback speed 0.5×–2×** |
| **Inter-Verse Delay** | 0.5 / 1 / 1.5 second gap between repeats & verses (KSU `repeat_waiting`) |
| **Auto-Stop Audio** | Stop automatically after finishing a **page / surah / juz** (KSU `sel_autoStop`) |
| **Hizb Navigation** | Jump to **hizb 1–60 + quarter** (start/¼/½/¾) — 240 precise divisions; indicator follows the current verse |
| **Range Memorization Test** | *Mushaf Test* mode: random verse from a selected range, masked → reveal answer (Arabic + translation) + play audio |
| **Print Mushaf Page** | Print preview of the current mushaf page (full-resolution image, auto-download) |
| Show/Hide | Toggle translation, inline tafsir, tafsir panel, mushaf overlay |
| Navigation | Surah, Verse, Page (spread), Juz, **Hizb** — just like the original site |
| Search | Search the entire Qur'an → jump to verse + **results marked with blue rings in the mushaf** |
| Hifz Mode | Random memorization quiz from a surah/verse range, hide/show text, play audio |
| Khatam Target | 30-juz progress + daily streak — pages auto-recorded as you open them |
| Memorization Map | Heatmap of all 604 pages: memorized / needs review / not yet (click to change) |
| Bookmarks | Star favorite verses, jump to them quickly from a list |
| Memorization Quiz | "What comes next?" — chained quiz per surah with score |
| Surah Playlist | Queue multiple surahs, each with a different reciter |
| Mini Player | Small always-on-top window |
| Verse Card | Export verse + translation as a PNG image & copy text to clipboard |
| Daily Reminder | Tray notification at a scheduled time |
| Focus Mode | Toolbar & panels hidden, Esc to exit |
| Dark Mode | Dark theme across the whole app |
| Inspiring Content | **Verse of the Day** (shown on launch), 12 motivation categories (anxiety, provision, marriage, effort, etc.), Rabbana supplications, quick access to Ayat al-Kursi & the 3 Quls |
| Word-by-Word | Per-word Arabic meaning & transliteration (from quran.com) |
| TTS | Read the verse translation aloud with Windows voices |
| Dictation Practice | Random audio plays — guess the surah & verse |
| Recitation Recorder | Record your voice with the microphone, play back & delete |
| Prayer Times | AlAdhan API per city + notification 10 minutes before prayer |
| Reading Stats | Bar chart of pages read per day (last 30 days) |
| Backup & Restore | Save all data (settings + progress) into a single file |
| Update Checker | Check for the latest release automatically from GitHub Releases |
| Extras | Adjustable translation font size • First-run welcome |
| Sajdah Indicator | Obligatory / recommended prostration verses in the status bar |
| Bulk Download | Download all/selected mushaf pages & a full surah of audio — offline |
| Keyboard Shortcuts | ← → change verse • Space play/pause • PgUp/PgDn page • Ctrl+F search • Esc exit focus • Ctrl+scroll mushaf zoom • Alt+←/→ back/forward navigation |
| Offline Cache | Audio, mushaf images, translations & tafsir cached automatically |
| Remember Position | Surah, verse, reciter, mode, zoom, theme — saved automatically |
| Icon | Custom application icon |

---

## Download & Running

**Easiest way:** download the portable exe from [Google Drive](https://drive.google.com/drive/folders/1A0AvGWNaHMU2bZtrvoES25RUh-pr6VMx?usp=sharing) or [GitHub Releases](https://github.com/linducip2208/alquran/releases) and run it — nothing to install.

**From source:**
1. Install the [.NET 7 SDK](https://dotnet.microsoft.com/download/dotnet/7.0)
2. `dotnet build QuranDesktop -c Release`
3. `dotnet run --project QuranDesktop`

**Build for distribution:**

```powershell
.\scripts\publish.ps1 -Version 1.4.0
```

Produces a self-contained `win-x64` archive + SHA-256 checksum under `artifacts/`
(see [docs/RELEASE.md](docs/RELEASE.md) for the full release checklist).

Internet is required the first time content is opened; once cached it works offline.

### User data location

All download/offline content is stored in a **`downloads` folder beside
`QuranDesktop.exe`** — one folder with the application, no hidden cache:

```
D:\QuranDesktop\
├── QuranDesktop.exe
└── downloads\
    ├── audio\      # reciter audio (47 reciters + audhubillah)
    ├── voice\      # voice translations
    ├── mushaf\     # mushaf page images
    ├── teks\       # Arabic text & translations
    ├── tafsir\     # per-verse tafsir
    ├── hilites\    # highlight coordinates
    ├── fonts\      # downloaded fonts
    ├── recordings\ # recitation recordings
    └── temp\       # transient files
```

Settings (`settings.json`), progress (`progress.json`), and the log
(`error.log`) remain under `%LOCALAPPDATA%\QuranDesktop` — those are not
download content.

**Automatic migration:** if old-version offline content is found under
`%LOCALAPPDATA%\QuranDesktop\downloads`, the app moves it into the downloads
folder beside the EXE (with progress, resumable, no re-downloading, never
overwriting valid files). A **"Import legacy cache from AppData…"** button is
available in Download Center → Storage for a manual re-scan. Use Backup &
Restore to move settings, progress, and recordings to another computer.

### Provider endpoints

Compatible endpoints can be changed under **Settings → Provider & Endpoint**.
The values apply to new requests; restart the application to reload mushaf
configuration. URLs must use `http` or `https`. Invalid values fall back to
the compatible defaults.

---

## Data Sources

All content is fetched directly from the **[quran.ksu.edu.sa](https://quran.ksu.edu.sa)** server (Electronic Moshaf Project, King Saud University):

- **Audio:** `https://quran.ksu.edu.sa/ayat/mp3/{reciter}/{SSS}{AAA}.mp3` (+ audhubillah & basmalah, voice translations)
- **Mushaf images:** `https://quran.ksu.edu.sa/ayat/safahat1/{page}.png` (Hafs), `/warsh/{page}.png`, `/tajweed_png/{page}.png`
- **Tafsir / translations / search / highlight coordinates:** `https://quran.ksu.edu.sa/interface.php?ui=pc&do=tafsir|tarjama|search|hilites`
- **Page, juz & hizb metadata:** `https://quran.ksu.edu.sa/js/quran-data.js` (source: [Tanzil.net](https://tanzil.net), GPL)
- **Web tafsir links:** `https://quran.ksu.edu.sa/tafseer/{book}/sura{s}-aya{a}.html`
- **Reciter map & reciter rules** (47 reciters, basmalah exceptions, Khaleefa restriction): extracted from the site script `provider/index.php?g=scr`

## Credits

- **Source & data:** [Quran KSU Electronic Moshaf Project](https://quran.ksu.edu.sa) — King Saud University
- **Qur'an metadata:** [Tanzil.net](https://tanzil.net) (GPL)
- **Rebuilt for Windows 10 & 11:** Lindu Cipta Pranayama (WA +62 812-9605-2010)
- **Built with:** GLM 5.3 Flash
