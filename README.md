# Quran Desktop — KSU Electronic Moshaf untuk Windows

**Bahasa / Languages:** [Indonesia](README.md) • [English](README.en.md) • [العربية](README.ar.md)

Aplikasi Al-Qur'an desktop (Windows Forms / C# .NET 7) yang dibuat ulang berdasarkan situs resmi
**[Quran KSU Electronic Moshaf Project](https://quran.ksu.edu.sa/index.php?ui=1&l=en)** — Universitas King Saud, Arab Saudi.

> Dibuat ulang untuk **Windows 10 & 11** oleh **Lindu Cipta Pranayama**
> Dibangun menggunakan **GLM 5.3 Flash**
> Kontak / WhatsApp: **+62 812-9605-2010**

---

## Download

**Portable (tidak perlu install):** [Google Drive — QuranDesktop portable](https://drive.google.com/drive/folders/1A0AvGWNaHMU2bZtrvoES25RUh-pr6VMx?usp=sharing)

Alternatif:
- [GitHub Releases](https://github.com/linducip2208/alquran/releases) — unduh `QuranDesktop-v1.3.0-win-x64.exe`
- [Langsung dari repo (Git LFS)](https://github.com/linducip2208/alquran/raw/main/QuranDesktop/bin/Release/net7.0-windows/win-x64/publish/QuranDesktop.exe)

---

## Fitur Lengkap

| Kategori | Fitur |
|---|---|
| Mode Tampilan | **Mushaf** (buka-bukaan 2 halaman: ganjil kanan–genap kiri), **Teks & Terjemahan**, **Tes Hafalan (Hifz)** |
| Jenis Mushaf | Hafs, Rewayat Warsh, Hafs Tajweed (604 halaman, gambar asli server KSU) |
| Interaksi Mushaf | Klik ayat langsung di halaman → highlight bubble emas + cincin biru hasil pencarian |
| Overlay | Teks arti melayang di atas halaman pada posisi tiap ayat |
| Qari | **43 qari** + varian Warsh, Murattal/Mujawwad/Teacher (Husary, Abdul Basit, Minshawi, Sudais, Maher, Afasy, dll.) |
| Terjemahan | **22 bahasa** — Indonesia, English (Saheeh International), Melayu, Arab (4 varian), Urdu, Rusia, dll. |
| Tafsir | **9 kitab** — Tafsir Jalalain (Indonesia), Al-Muyassar, Ibn Kathir, As-Sa'dy, Al-Baghawy, Al-Qortoby, At-Tabary, I'rab, Tafhim (Rusia) |
| Tafsir Inline | Tafsir tampil di bawah ayat terpilih pada mode Teks |
| Talaqaa (Voice) | Audio terjemahan: English, French, Urdu, Bosnian |
| Pemutar Audio | Per-ayat, auto-next antar ayat & surah, repeat 1×–10×/∞, **ulang rentang ayat X–Y**, **mode guru** (ayat diulang berkala), basmalah & audhubillah otomatis, volume, **kecepatan 0,5×–2×** |
| Tampilkan/Sembunyikan | Toggle arti, tafsir inline, panel tafsir, overlay mushaf |
| Navigasi | Surah, Ayat, Halaman (spread), Juz — seperti situs aslinya |
| Pencarian | Cari kata/frasa seluruh Al-Qur'an → lompat ke ayat + **hasil ditandai cincin biru di mushaf** |
| Mode Hifz | Soal hafalan acak dari rentang surah/ayat, sembunyi/tampil teks, putar audio |
| Target Khatam | Progres 30 juz + streak harian — halaman otomatis tercatat saat dibuka |
| Peta Hafalan | Heatmap 604 halaman: hafal / perlu ulang / belum (klik untuk ubah status) |
| Bookmark | Tandai ayat favorit, panel daftar untuk lompat cepat |
| Kuis Hafalan | "Lanjutannya ayat mana?" — kuis berantai per surah dengan skor |
| Playlist Surah | Antrian beberapa surah, tiap surah bisa qari berbeda |
| Mini Player | Jendela kecil selalu di atas (always on top) |
| Kartu Ayat | Export ayat + arti ke gambar PNG & salin teks ke clipboard |
| Pengingat Harian | Notifikasi tray di jam yang diatur |
| Mode Fokus | Toolbar & panel hilang, Esc untuk keluar |
| Dark Mode | Tema gelap untuk seluruh aplikasi |
| Konten Inspirasi | **Ayat Hari Ini** (tampil saat buka app), 12 kategori motivasi (cemas, rezeki, jodoh, ikhtiar, dll.), Doa Rabbana, quick access Ayat Kursi & 3 Qul |
| Indikator Sajdah | Ayat sajdah wajib / disunnahkan di status bar |
| Unduh Massal | Download semua/rentang halaman mushaf & audio satu surah penuh — offline |
| Shortcut Keyboard | ← → pindah ayat • Space play/pause • PgUp/PgDn halaman • Ctrl+F cari • Esc keluar fokus |
| Cache Offline | Audio, gambar mushaf, terjemahan & tafsir tersimpan otomatis |
| Simpan Posisi | Surah, ayat, qari, mode, zoom, tema — tersimpan otomatis |
| Ikon | Ikon aplikasi kustom |

---

## Download & Menjalankan

**Cara termudah:** unduh exe portable dari [Google Drive](https://drive.google.com/drive/folders/1A0AvGWNaHMU2bZtrvoES25RUh-pr6VMx?usp=sharing) atau [GitHub Releases](https://github.com/linducip2208/alquran/releases), lalu jalankan — tidak perlu install apa pun.

**Dari source code:**
1. Install [.NET 7 SDK](https://dotnet.microsoft.com/download/dotnet/7.0)
2. `dotnet build QuranDesktop -c Release`
3. `dotnet run --project QuranDesktop`

Koneksi internet diperlukan saat pertama membuka konten; setelah tersimpan di cache, dapat diakses offline.

---

## Sumber Data

Semua konten diambil langsung dari server **[quran.ksu.edu.sa](https://quran.ksu.edu.sa)** (Electronic Moshaf Project, King Saud University):

- **Audio:** `https://quran.ksu.edu.sa/ayat/mp3/{qari}/{SSS}{AAA}.mp3` (+ audhubillah & basmalah, voice translation)
- **Gambar mushaf:** `https://quran.ksu.edu.sa/ayat/safahat1/{hal}.png` (Hafs), `/warsh/{hal}.png`, `/tajweed_png/{hal}.png`
- **Tafsir / terjemahan / pencarian / koordinat highlight:** `https://quran.ksu.edu.sa/interface.php?ui=pc&do=tafsir|tarjama|search|hilites`
- **Metadata halaman & juz:** `https://quran.ksu.edu.sa/js/quran-data.js` (sumber: [Tanzil.net](https://tanzil.net), GPL)
- **Tautan tafsir web:** `https://quran.ksu.edu.sa/tafseer/{kitab}/sura{s}-aya{a}.html`

## Kredit

- **Sumber & data:** [Quran KSU Electronic Moshaf Project](https://quran.ksu.edu.sa) — King Saud University
- **Metadata Quran:** [Tanzil.net](https://tanzil.net) (GPL)
- **Dibuat ulang untuk Windows 10 & 11:** Lindu Cipta Pranayama (WA +62 812-9605-2010)
- **Dibangun dengan:** GLM 5.3 Flash
