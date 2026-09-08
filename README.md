# Quran Desktop — KSU Electronic Moshaf untuk Windows

Aplikasi Al-Qur'an desktop (Windows Forms / C# .NET 7) yang dibuat ulang berdasarkan situs resmi
**[Quran KSU Electronic Moshaf Project](https://quran.ksu.edu.sa/index.php?ui=1&l=en)** — Universitas King Saud, Arab Saudi.

> Dibuat ulang untuk **Windows 10 & 11** oleh **Lindu Cipta Pranayama**
> Dibangun menggunakan **GLM 5.3 Flash**
> Kontak / WhatsApp: **0812-9605-2010** (+62 812 9605 2010)

---

## Fitur

| Fitur | Keterangan |
|---|---|
| 3 Mode Tampilan | **Mushaf** (buka-bukaan 2 halaman, ganjil kanan–genap kiri), **Teks & Terjemahan**, **Tes Hafalan (Hifz)** |
| 3 Jenis Mushaf | Hafs, Rewayat Warsh, Hafs Tajweed (604 halaman, gambar asli server KSU) |
| Klik Ayat di Mushaf | Klik langsung ayat pada halaman → highlight + arti + tafsir |
| 47 Qari | Paritas penuh dengan situs KSU: Hafs + varian Warsh, Murattal/Mujawwad/Teacher/32kbps (Husary, Abdul Basit, Minshawi, Sudais, Maher, Afasy, **Al-Banna, Basfar 32k, Al-Akhdar, Ayyoub 32k**, dll.) |
| 31 Terjemahan | Indonesia, English (Saheeh International), Melayu, Arab (4 varian), Urdu, Rusia, **Bengali, Somali, Tamil, Hausa, Belanda (Keyzer), Swahili, Thai, Uzbek, Mandarin** |
| 9 Kitab Tafsir | **Tafsir Jalalain (Indonesia)**, Al-Muyassar, Ibn Kathir, As-Sa'dy, Al-Baghawy, Al-Qortoby, At-Tabary, I'rab, Tafhim (Rusia) |
| Talaqaa (Voice Translation) | Audio terjemahan: English, French, Urdu, Bosnian |
| Pemutar Audio | Per-ayat, auto-next antar ayat & surah, repeat 1×–10×/∞, basmalah & audhubillah otomatis (dengan aturan qari persis KSU), volume |
| **Jeda Antar Ayat** | Jeda 0,5 / 1 / 1,5 detik antar pengulangan & antar ayat (seperti `repeat_waiting` KSU) |
| **Auto-Stop Audio** | Berhenti otomatis per **halaman / surah / juz** saat ayat penutup selesai (seperti `sel_autoStop` KSU) |
| **Navigasi Hizb** | Lompat ke **hizb 1–60 + kuartal** (awal/¼/½/¾) — 240 pembagian presisi; indikator ikut posisi ayat |
| **Uji Hafalan Rentang** | Mode *Mushaf Test*: ayat acak dari rentang pilihan, tersamar → buka jawaban (Arab + arti) + putar audio |
| **Cetak Mushaf** | Preview cetak halaman mushaf aktif (gambar resolusi penuh, unduh otomatis) |
| Navigasi Lengkap | Surah, Ayat, Halaman, Juz, **Hizb** — seperti situs aslinya |
| Pencarian | Cari kata/frasa di seluruh Al-Qur'an → langsung lompat ke ayat |
| Mode Hifz | Soal hafalan acak dari rentang surah/ayat, sembunyi/tampil teks, putar audio |
| Indikator Sajdah | Ayat sajdah wajib / disunnahkan di status bar |
| Unduh Massal | Pusat Unduhan: mushaf, teks, tafsir, audio per qari, rentang ayat — resume otomatis |
| Shortcut Keyboard | ← → pindah ayat • Space play/pause • PgUp/PgDn halaman • Ctrl+F cari |
| Cache Offline | Audio, gambar mushaf, terjemahan & tafsir tersimpan otomatis |
| Simpan Posisi | Surah, ayat, qari, mode, zoom — tersimpan otomatis |

## Sumber Data — semua dari situs resmi KSU

Semua konten di aplikasi ini diambil langsung dari server **[quran.ksu.edu.sa](https://quran.ksu.edu.sa)** (Electronic Moshaf Project, King Saud University). Endpoint yang digunakan:

**Audio (Talaqah per-ayat):**

| Konten | Endpoint |
|---|---|
| Audio ayat per qari | `https://quran.ksu.edu.sa/ayat/mp3/{qari}/{SSS}{AAA}.mp3` — contoh: `ayat/mp3/Husary_64kbps/056018.mp3` |
| Audhubillah (intro) | `https://quran.ksu.edu.sa/ayat/mp3/all/audhubillah.mp3` |
| Basmalah per qari | `https://quran.ksu.edu.sa/ayat/mp3/{qari}/001001.mp3` |
| Voice translation | folder `English_Walk`, `fr.leclerc_128kbs`, `ur.khan_46kbs`, `Bosnian_Korkut_128kbps` |

**Gambar mushaf (604 halaman):**

| Mushaf | Endpoint |
|---|---|
| Hafs | `https://quran.ksu.edu.sa/ayat/safahat1/{halaman}.png` |
| Rewayat Warsh | `https://quran.ksu.edu.sa/warsh/{halaman}.png` |
| Hafs Tajweed | `https://quran.ksu.edu.sa/tajweed_png/{halaman}.png` |

**Teks, tafsir, terjemahan, pencarian** — via `https://quran.ksu.edu.sa/interface.php?ui=pc`:

| Konten | Endpoint |
|---|---|
| Tafsir per-ayat | `&do=tafsir&author={kitab}&sura={s}&aya={a}` |
| Terjemahan (rentang) | `&do=tarjama&tafsir={kode}&b_sura=…&b_aya=…&e_sura=…&e_aya=…` |
| Pencarian ayat | `&do=search` (POST `query`) |
| Koordinat highlight ayat di mushaf | `&do=hilites&page={halaman}` |

**Daftar kitab tafsir** (key `author`): `indonesian` (Jalalain — Indonesia), `muyassar`, `sa3dy`, `baghawy`, `katheer`, `qortoby`, `tabary`, `e3rab`, `russian` (Tafhim — Rusia)

**Kode terjemahan** (key `tafsir` pada tarjama): `id_indonesian` (Indonesia), `en_sh` (English — Saheeh International), `ms_basmeih` (Melayu), `ar_ayat`/`ar_ayat_safy`/`ar_mu`/`ar_ma3any` (Arab), `ur_gl` (Urdu), `ru_ku` (Rusia), `fr_ha`, `es_navio`, `de_bo`, `it_piccardo`, `pt_elhayek`, `nl_siregar`, `bs_korkut`, `sq_nahi`, `sv_bernstrom`, `tr_diyanet`, `ku_asan`, `pr_tagi`, `ml_abdulhameed`, `bn_bengali`, `so_abduh`, `ta_tamil`, `ha_gumi`, `nl_keyzer`, `sw_barwani`, `th_thai`, `uz_sodik`, `zh_jian`

**Metadata halaman, juz, hizb & sajdah:**
- `https://quran.ksu.edu.sa/js/quran-data.js` — pemetaan halaman (Page/Page_warsh/Page2), juz, **kuartal hizb (HizbQaurter, 240)**, dan daftar ayat sajdah; sumber aslinya metadata **[Tanzil.net](https://tanzil.net)** (lisensi GPL), digunakan oleh situs KSU

**Peta konfigurasi** (daftar 47 qari, jenis mushaf, kode terjemahan, aturan basmalah & batasan qari) diekstrak dari script situs: `https://quran.ksu.edu.sa/provider/index.php?g=scr`

**Tautan tafsir versi web** (tombol "Buka di browser"): `https://quran.ksu.edu.sa/tafseer/{kitab}/sura{s}-aya{a}.html`

## Menjalankan

**Cara termudah — exe portable (tidak perlu install apa pun):**

Unduh dari [Google Drive — QuranDesktop portable](https://drive.google.com/drive/folders/1A0AvGWNaHMU2bZtrvoES25RUh-pr6VMx?usp=sharing) atau [GitHub Releases](https://github.com/linducip2208/alquran/releases).

Cukup copy 1 file exe itu ke PC/laptop Windows 10 atau 11 mana saja.

**Build sendiri untuk distribusi:**

```powershell
.\scripts\publish.ps1 -Version 1.4.0
```

Hasilnya arsip self-contained `win-x64` + checksum SHA-256 di folder `artifacts/`
(lihat [docs/RELEASE.md](docs/RELEASE.md) untuk checklist rilis lengkap).

**Dari source code:**

1. Install [.NET 7 SDK](https://dotnet.microsoft.com/download/dotnet/7.0)
2. `dotnet build QuranDesktop -c Release`
3. `dotnet run --project QuranDesktop`

> Koneksi internet diperlukan saat pertama membuka konten; setelah tersimpan di cache, dapat diakses offline.

### Lokasi data pengguna

Seluruh konten download/offline disimpan di folder **`downloads` di samping
`QuranDesktop.exe`** — satu folder dengan aplikasi, tanpa cache tersembunyi:

```
D:\QuranDesktop\
├── QuranDesktop.exe
└── downloads\
    ├── audio\      # audio qari (47 qari + audhubillah)
    ├── voice\      # voice translation
    ├── mushaf\     # gambar halaman mushaf
    ├── teks\       # teks Arab & terjemahan
    ├── tafsir\     # tafsir per ayat
    ├── hilites\    # koordinat highlight
    ├── fonts\      # font hasil unduhan
    ├── recordings\ # rekaman tilawah
    └── temp\       # file sementara
```

Pengaturan (`settings.json`), progres (`progress.json`), dan log (`error.log`)
tetap di `%LOCALAPPDATA%\QuranDesktop` — itu bukan konten download.

**Migrasi otomatis:** bila ditemukan konten offline versi lama di
`%LOCALAPPDATA%\QuranDesktop\downloads`, aplikasi memindahkannya ke folder
downloads di samping EXE (dengan progress, resumable, tanpa unduhan ulang,
tanpa menimpa file yang sudah valid). Tombol **"Impor cache lama dari
AppData…"** tersedia di Pusat Unduhan → Penyimpanan untuk pemindaian ulang
manual. Gunakan fitur Backup & Restore untuk memindahkan pengaturan, progres,
dan rekaman ke komputer lain.

### Endpoint provider

Endpoint kompatibel dapat diganti dari **Pengaturan → Provider & Endpoint**.
Perubahan berlaku untuk request baru; tutup dan buka ulang aplikasi bila ingin
memuat ulang konfigurasi mushaf. URL harus berupa alamat `http` atau `https`.
Jika URL tidak valid, aplikasi menggunakan nilai bawaan yang kompatibel.

## Struktur Project

```
QuranDesktop/
├── Controls/          MushafView, TextModeControl, HifzControl, SearchDialog,
│                      DownloadCenterDialog, MTestDialog (uji hafalan rentang), dll.
├── Data/quran-data.js Metadata halaman, juz, hizb & sajdah (Tanzil, embedded resource)
├── MainForm.cs        Orkestrasi UI & pemutar audio (NAudio + SoundTouch)
├── AudioPlaybackService.cs  Cache lookup + unduh resumable audio per ayat
├── KsuApi.cs          Klien API tafsir/terjemahan/pencarian/koordinat KSU
├── QuranData.cs       Parser metadata halaman, juz, hizb, sajdah
├── ProviderEndpoints.cs  Endpoint provider (bisa diganti dari Pengaturan)
├── UpdateService.cs   Pemeriksa rilis baru via GitHub Releases
└── Reciters.cs, Translations.cs, Tafsirs.cs, MushafTypes.cs
```

## Kredit

- **Sumber & data:** [Quran KSU Electronic Moshaf Project](https://quran.ksu.edu.sa) — Electronic Moshaf Project, King Saud University
- **Metadata Quran:** [Tanzil.net](https://tanzil.net) (GPL)
- **Dibuat ulang untuk Windows 10 & 11:** Lindu Cipta Pranayama (WA 0812-9605-2010)
- **Dibangun dengan:** GLM 5.3 Flash
