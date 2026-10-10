# Rencana: Game Tracker Overlay (panduan 100% di dalam game)

_Draft 1 · 7 Oktober 2026_

> **Usang.** Digantikan `docs/RENCANA.md` (7 Okt 2026), lalu oleh kondisi di `CLAUDE.md` dan `docs/HANDOFF-AI.md`. Tauri/SQLite/Steam
> achievement tidak dipakai; aplikasi sekarang C# WPF yang membaca memori game. Disimpan hanya sebagai catatan sejarah.

## 1. Tujuan

Overlay yang muncul di atas game (tekan hotkey) dan langsung memberi tahu:

- apa saja yang tersisa untuk 100% (achievement + collectible + side quest),
- **di mana / bagaimana** mendapatkannya, sesuai posisi kamu sekarang di game,
- peringatan **missable** sebelum kamu melewati titik tanpa kembali,

tanpa harus alt-tab ke browser atau membuka HP.

## 2. Asumsi awal (bisa diubah)

| Hal | Default yang dipilih | Alasan |
|---|---|---|
| Platform | Windows PC | Sesuai PC kamu (DESKTOP-GL5BJC3) |
| Launcher pertama | Steam | Satu-satunya yang punya API achievement publik yang lengkap |
| Mode tampilan game | Borderless / windowed fullscreen | Overlay jendela biasa bisa tampil di atasnya tanpa menyuntik kode ke game |
| Bahasa UI | Indonesia (dengan opsi Inggris) | |

## 3. Cara kerja (gambaran besar)

```
 ┌──────────────┐   proses aktif    ┌─────────────────┐
 │ Game berjalan│ ───────────────▶ │ Detektor game    │  (registry Steam RunningAppID / daftar proses)
 └──────────────┘                   └────────┬────────┘
                                             │ appid
             ┌───────────────────────────────┼───────────────────────────┐
             ▼                               ▼                           ▼
   ┌───────────────────┐        ┌────────────────────────┐   ┌──────────────────────┐
   │ Steam Web API     │        │ Database panduan lokal │   │ Pembaca progres lokal │
   │ achievement status│        │ (checklist per game)   │   │ (opsional: save file) │
   └─────────┬─────────┘        └───────────┬────────────┘   └──────────┬───────────┘
             └──────────────┬───────────────┴──────────────────────────┘
                            ▼
                  ┌───────────────────┐   hotkey (mis. Ctrl+Shift+G)
                  │  Overlay (UI)     │ ◀─────────────────────────────
                  │  transparan,      │
                  │  always-on-top    │
                  └───────────────────┘
```

### 3.1 Overlay
- Jendela transparan, selalu di atas, **click-through** saat mode "HUD kecil", dan bisa diklik saat mode "panel penuh".
- Hotkey global untuk buka/tutup (hindari Shift+Tab karena dipakai Steam).
- Tidak menyuntik DLL ke game → aman terhadap anti-cheat. Konsekuensinya: tidak tampil di **exclusive fullscreen**; game harus di borderless.

### 3.2 Deteksi game & achievement
- **Game yang sedang dimainkan**: baca `HKCU\Software\Valve\Steam\RunningAppID`, cadangan: cocokkan nama proses.
- **Daftar achievement + ikon + deskripsi**: Steam Web API `GetSchemaForGame`.
- **Status unlock**: `GetPlayerAchievements` (butuh Steam API key milik kamu sendiri dan profil "Game details" publik). Dicek berkala (mis. tiap 30–60 detik) dan langsung saat overlay dibuka.
- **Kelangkaan**: `GetGlobalAchievementPercentagesForApp` untuk mengurutkan dari yang termudah.
- Notifikasi kecil saat achievement baru terbuka + "berikutnya yang terdekat".

### 3.3 Panduan 100% (bagian tersulit)
Steam hanya tahu achievement, bukan collectible/side quest atau lokasinya. Jadi perlu **data panduan per game** dalam format checklist terstruktur:

```yaml
game: Hollow Knight
appid: 367520
sections:
  - name: Forgotten Crossroads
    items:
      - id: mask-shard-1
        type: collectible
        title: Mask Shard #1
        hint: Beli dari Sly setelah menyelamatkannya di Crossroads.
        missable: false
        achievement: null
```

Sumber data, bertahap:
1. **Checklist manual** yang kamu (atau komunitas) tulis untuk game favorit.
2. **Dibantu AI**: Claude merangkum petunjuk tiap achievement / collectible jadi checklist singkat dalam format di atas (ditulis ulang, bukan menyalin isi walkthrough berhak cipta), lalu kamu verifikasi.
3. **Impor** dari sumber terbuka bila ada (mis. data RetroAchievements untuk game retro).

Fitur panduan:
- Checklist per area/chapter, centang manual atau otomatis (dari achievement).
- **Missable warning** ditaruh paling atas, dengan "titik tanpa kembali".
- **Mode tanpa spoiler**: petunjuk disembunyikan sampai diklik.
- Pencarian cepat ("kunci merah", "chapter 4").
- Progress bar: achievement % dan checklist %.

### 3.4 Penyimpanan
- Database lokal SQLite: progres checklist, cache data Steam, pengaturan.
- File panduan dalam YAML/JSON di folder `guides/`, mudah diedit dan dibagikan.

## 4. Teknologi yang diusulkan

| Bagian | Pilihan | Catatan |
|---|---|---|
| Aplikasi + overlay | **Tauri v2** (Rust) + React/TypeScript | Ringan saat game jalan (jauh lebih hemat RAM dari Electron), mendukung jendela transparan & click-through |
| Hotkey global | plugin `global-shortcut` Tauri | |
| Data lokal | SQLite | |
| Data Steam | Steam Web API | API key gratis dari steamcommunity.com/dev/apikey |
| Generator panduan AI | Claude API (opsional, fase 3) | |

Alternatif kalau mau lebih sederhana: Electron (lebih berat) atau C#/WPF (Windows saja).

## 5. Tahapan

**Fase 0 · Persiapan**
- Struktur repo, CI build Windows, buat Steam API key.

**Fase 1 · MVP: overlay achievement Steam**
- Deteksi game Steam yang aktif.
- Overlay dengan hotkey: daftar achievement terkunci/terbuka, ikon, deskripsi, % global, progress bar.
- Toast saat achievement baru terbuka.
- ✅ Selesai bila: di 1 game nyata, overlay menunjukkan status yang benar tanpa alt-tab.

**Fase 2 · Checklist panduan 100%**
- Format file panduan + penampil checklist per area.
- Missable warning, mode tanpa spoiler, pencarian.
- Tulis panduan lengkap untuk 1–2 game pilihan kamu sebagai contoh.

**Fase 3 · Panduan dibantu AI**
- Tombol "buat draft panduan" untuk game baru: dari daftar achievement, Claude menulis petunjuk singkat per item; kamu cek dan simpan.

**Fase 4 · Deteksi otomatis lanjutan (per game)**
- Membaca save file untuk mencentang collectible otomatis (khusus game yang format save-nya diketahui).
- Opsional: OCR layar untuk mendeteksi area/chapter saat ini.

**Fase 5 · Platform lain**
- Xbox / Game Pass, GOG, Epic, RetroAchievements (tergantung API yang tersedia).

## 6. Risiko & batasan

- **Exclusive fullscreen**: overlay tidak terlihat; solusi: borderless. (Hook DirectX seperti overlay Steam mungkin di masa depan, tapi berisiko dengan anti-cheat.)
- **"100%" in-game tidak tersedia lewat API**: kualitas panduan tergantung data checklist per game.
- **Hak cipta walkthrough**: jangan menyalin isi situs panduan; tulis ulang/rangkum.
- **Profil Steam privat** membuat status achievement tidak terbaca → aplikasi memberi tahu cara mengaturnya.
- Batas Steam API (±100.000 request/hari) cukup untuk pemakaian pribadi.

## 7. Yang perlu diputuskan kamu

1. Launcher utama yang dipakai (default: Steam).
2. 1–2 game pertama untuk dijadikan contoh panduan lengkap di Fase 2.
