# Handoff: Game Tracker — FF VII Remake Intergrade

Status per **9 Oktober 2026**, dengan pembaruan **10 Oktober 2026** di bagian 0. Ditulis untuk AI berikutnya yang melanjutkan proyek ini. Baca seluruhnya sebelum mengubah apa pun.

---

## 0. Pembaruan 10 Oktober 2026 (sesi Claude, baca dulu)

**Aturan baru dari user**
- **Push otomatis**: setiap commit langsung di-push ke `backup/local-2026-10-08` tanpa bertanya. Tetap jangan sentuh `main`/PR. Tanya dulu hanya untuk force-push, file besar/sensitif, atau build gagal.
- Restart overlay untuk perbaikan boleh: `Stop-Process GameTracker`, `dotnet build`, jalankan `GameTracker.exe --game ff7r` (langsung overlay).
- Ini playthrough PERTAMA user, mode **Normal** (Hard belum terbuka).

**Fitur baru di overlay** (semua khusus Steam 1.0.0.7; alamat & cara menemukannya di `research/notes.md`)
- **Posisi pemain** (`Ff7rPosition.cs`): aktor = `[[modul+0x53DD150]+0x60]`, objek posisi (vtable modul+0x4DD7148) yang +0x20 == aktor, XYZ float di +0x160. Dicatat ke `data/logs/position.log` tiap 2 m.
- **Nama area dari database game** (`Ff7rMapArea.cs`): aktor `EndNaviMapVolume` (vtable modul+0x4C1C358) bernama `Navi070_Layer07_060_...` (peta, lantai, part/10) -> kunci `$navi070_name_part007_600` -> teks dari tabel bahasa; batas = kotak Bounds di BrushComponent (+0x160 aktor, +0x160 komponen). Volume terkecil yang memuat posisi = area sekarang. FNamePool blok di modul+0x5981310. Nomor peta TIDAK selalu = chapter x 10 (Ch10 navi090, Ch11 navi100, Ch14 pakai navi080/090, navi140 = Shinra Building).
- **Pop-up "DI AREA INI"** (`ToastWindow.cs`) di tengah atas layar + kotak & tag DI SINI di overlay saat area sekarang = area langkah yang belum selesai (dari awal teks "where": `Area (B5): ...`). Bertahan sampai item diambil/keluar area.
- **Auto-hide**: overlay & pop-up hanya tampil saat menjelajah. `modul+0x57E9ABB`: 1 menjelajah, 0 battle, 3 menu (termasuk menu taktis battle & pause cutscene), 5 cutscene/dialog. Perubahan dicatat ke `data/logs/state.log`.
- **Mode Hard**: langkah khusus Hard punya `"hard": true`; catatan "Hard: ..." di akhir teks disembunyikan di Normal. Saklar manual **Ctrl+Shift+H** (disimpan di progres). Deteksi difficulty dari memori BELUM ketemu.
- Tampilan: label chapter kecil + hitungan, nama quest terbesar, peringatan garis kuning (merah saat langkah gate aktif), ikon tipe pengganti checkbox di mode ringkas, Bahnschrift.
- Perbaikan audit: overlay yang ditutup berhenti total, gil tidak dicocokkan ke "Gil Up", error file tidak membuat crash (`data/logs/crash.log`), belajar flag/item hanya kalau buktinya tunggal, scan memori prioritas rendah, backup progres per game (50 terbaru), dashboard hemat refresh.

**Panduan** (`overlay/games/ff7r/guide.json`, 461 langkah)
- Semua kolom "where" item/quest/discovery ditulis ulang dengan **nama area resmi game** di depan (riset 5 agen dari PowerPyx/Fextralife/Game8 dll; bahan & hasil di `research/locations/`, termasuk `REVIEW.md` yang sudah disetujui user).
- Koreksi disetujui: item yang bisa dibeli jadi opsional, disc Ch14 missable, duplikat dihapus (Otherworldly Crystal Ch9, Moogle Medal Ch13, Bahamut Ch14), item baru (Crescent Moon Charm Ch3, Barrier Materia Ch4, Champion Belt, Enemy Skill, Whack-a-Box Hard, Pull-Up Challenge, Chakra (2) Ch21).
- Ch7+ tetap belum diverifikasi di game; koreksi saat user main.

**Progres user**: Ch7, sebelum Airbuster, 5/12. Masih terbuka: Discovery: Waste Recovery (Security Ops), Magic Up (Waste Storage), Bombing Mission (Gate Office), Titanium Bangle (otomatis). Crescent Moon Charm, Barrier Materia, Power Wristguards sudah dicentang (dicek dari inventory).

**Tools**: scanner `tools/ff7r-scan` (menulis ke `research/scan/`, di-ignore git) punya perintah baru: who, strs, vt, base, obj, module, navi, inventory, pair, vecnear, fdiff, bdiff, find dengan lebar 1/2/4, filter float (fsame, fchg, fdir, fnear, fmoved).

**Malam 10 Okt (user tidur, belum dites di game)**
- Struktur per game: semua file FF7R di `overlay/games/ff7r/` (game.json, guide.json, items.json, assets/, reader/). `GameRegistry` membaca `games/<id>/game.json`; game tanpa `reader` = overlay manual (hotkey), dites dengan game uji. Template di `games/_template`, cara pakai di `games/README.md`.
- Overlay: baris notifikasi (centang otomatis/hotkey/undo/dipelajari, hitung mundur hanya saat terlihat), rekap missable terlewat 90 dtk setelah chapter berganti normal (`data/logs/missed.log`), jumlah di peringatan point of no return, petunjuk "➜ langkah: ruang › ruang" dari ruang yang pernah dilalui (`data/area-links.json`, hanya kalau rute lengkap diketahui), banner area digambar ulang setelah menu.
- Performa: baca angka tanpa array, record inventory dari span, awal daftar item satu kali baca (fallback per record), set flag di-cache, scan entri objektif di thread pekerja, posisi dibaca sekali per poll. P/Invoke `lpNumberOfBytesRead` = `nint`.
- Perlu dicek saat main: notifikasi, rute (muncul setelah berjalan antar ruang), rekap di akhir Ch7.

**Bahasa (10 Okt sore)**
- Overlay default **English**; saklar **EN | IN** di header (Ctrl+Shift+L, atau Ctrl+Shift+Alt+L kalau L dipakai aplikasi lain), disimpan di `data/settings.json` (`Lang.cs`, `Lang.T(en, id)`). Dashboard & tray ikut.
- `guide.json`: `where`/`warning` tetap Indonesia (dibaca aturan: "otomatis", "akhir chapter", "Setelah"); `whereEn`/`warningEn` = versi Inggris yang ditampilkan. Langkah baru wajib diberi keduanya; kalimat "Setelah ..." di warning = "After ..." di warningEn, catatan `Hard:` tetap di akhir.

**Pop-up quest (10 Okt malam)**
- Quest + subquest dari game tampil di pop-up sendiri kiri atas (`QuestWindow.cs`): selalu tampil saat menjelajah, fade in saat berganti, ikut tersembunyi bersama overlay. Overlay kanan hanya checklist + petunjuk langkah cerita. Keterbacaan: latar panel ~90% gelap, TextFormattingMode=Display, teks keterangan 13 px #CBD5E1; outline blur (DropShadowEffect) dihapus karena membuat teks kabur. Coba dulu tanpa mengecil/redup (permintaan user); cek posisi kiri atas tidak menutupi HUD game.

**Masih terbuka**
- Deteksi difficulty otomatis (coba saat user main Hard).
- Overlay ikut tersembunyi saat dialog ringan (masih bisa jalan) dan TIDAK tersembunyi di layar shop: nilai state sama dengan adegan dialog / main. Riset 10 Okt gagal (lihat notes.md "Dialog ringan vs adegan dialog"); user memilih tidak melanjutkan dulu. Ide: tanda HUD tampil, siklus snapshot penuh dengan shop.
- Batas area masih kotak (bisa meleset di ruangan miring); bentuk persis ada di Model/BodySetup volume.
- Penanda di atas peti dalam game (butuh posisi peti + kamera): belum.
- Data panduan Ch11 & Ch15-18: sebagian area null (peta tak termuat saat ekspor nama).
- README masih usang.

---

## 1. Ringkasan proyek

Aplikasi Windows (C# WPF, .NET 8) yang terdiri dari:

1. **Dashboard** (jendela awal): library game (sekarang hanya FF7R), info Steam, tombol START (jalankan game lewat Steam + overlay) dan OVERLAY (overlay saja). Minimize = sembunyi ke tray.
2. **Overlay** di atas game: checklist per chapter (cerita, side quest, senjata, materia, music disc, aksesori, armor, summon, manuskrip, kejadian/discovery) yang **dicentang otomatis** dengan membaca memori game (read-only).

**Tujuan user:** satu kali main tanpa ada yang terlewat (*flawless one-run*), dengan **Chapter Select seminim mungkin**. Trofi/achievement **tidak penting** (disembunyikan dari tracker).

---

## 2. Lokasi & repo

| Apa | Di mana |
|---|---|
| Proyek | `D:\Claude Project\GameTracker` |
| Kode | `overlay\` (proyek `GameTracker.csproj`) |
| Folder per game | `overlay\games\<id>\` (game.json, guide.json, assets\, data); cara menambah game di `overlay\games\README.md` |
| Guide FF7R | `overlay\games\ff7r\guide.json` |
| Peta item | `overlay\games\ff7r\items.json`; pembaca memori `overlay\games\ff7r\reader\` |
| Gambar | `overlay\assets\games\ff7r\` (background 1920×1080, cover 600×900, icon 256), `overlay\assets\app\` (logo GP: `logo.png`, `app.ico`) |
| Data runtime (gitignored) | `data\` — progres `FINAL FANTASY VII REMAKE INTERGRADE.json`, `backups\`, `playtime.json`, `logs\` |
| Riset memori (gitignored) | `research\notes.md` + dump scan (besar, jangan di-commit) |
| Tool scanner | `tools\ff7r-scan\` |
| Repo GitHub | `SquamosS/Game-Tracker`, branch kerja **`backup/local-2026-10-08`** |
| Game | Steam app 1462040, versi 1.0.0.7, di `D:\SteamLibrary\steamapps\common\FINAL FANTASY VII REMAKE` |

Build & jalan:

```
Stop-Process -Name GameTracker -Force -ErrorAction SilentlyContinue
cd "D:\Claude Project\GameTracker\overlay"
dotnet build
.\bin\Debug\net8.0-windows\GameTracker.exe            # dashboard
.\bin\Debug\net8.0-windows\GameTracker.exe --game ff7r  # langsung overlay
```

Game harus mode **Borderless/Windowed** agar overlay terlihat.

---

## 3. ATURAN WAJIB dari user

1. **Setiap commit langsung di-push otomatis** ke `backup/local-2026-10-08` tanpa bertanya. **Jangan sentuh `main`, jangan buat PR.** Tanya dulu hanya untuk force-push, file besar/sensitif, atau build gagal.
2. **Memori game hanya dibaca (read-only).** Jangan pernah menulis ke memori game (user pernah minta speed hack; ditolak, disarankan Cheat Engine).
3. **Jangan ubah setting sistem** (power plan, registry, dll.). Kalau perlu, beri user perintahnya.
4. **Drive C: hampir penuh** — semua data/file kerja di D:.
5. **Jangan commit dump scan besar** (`research\`, `*.cand`, `*.raw`).
6. Guide: **jangan cantumkan item yang bisa dibeli di toko**. Item opsional yang ada di jalur diberi label **OPSIONAL**, dan **tidak pernah dicentang otomatis dari kepemilikan** (user bisa sudah punya hasil beli).
7. Nama langkah pakai **nama quest resmi bahasa Inggris** (sesuai teks game); deskripsi/lokasi dalam **bahasa Indonesia**.
8. Restart overlay boleh kalau memang perlu — cukup beri tahu user.
9. Komunikasi dengan user dalam **bahasa Indonesia**, jelaskan langkah demi langkah (apa yang selesai / sedang ditunggu / berikutnya).
10. Kebenaran lebih penting dari kecepatan (user menolak cache alamat memori antar restart karena bisa basi setelah load save lain).
11. Hapus folder lama (`D:\GameTrackerScan`, sisa di C:) **hanya setelah user setuju**.

---

## 4. Progres main user (FF7R)

- Tracker sekarang di **Chapter 7 (A Trap Is Sprung)**.
- **96 item tercentang**, semua Chapter 1–6 (terakhir: `c6-10-mp-up`, `c6-11-cargo`, `c6-12-lmg`). Chapter 7 belum ada yang dicentang.
- Play time Steam: ±17 jam 17 menit. Achievement 17/63 (tidak dipedulikan).
- User sempat load save Ch3 ↔ Ch7 untuk tes; reconcile mengembalikan centang dengan benar.

## 5. Status guide (`games/ff7r/guide.json`, status: draft)

Semua chapter sudah terisi: **Ch 1–18 + INTERmission (nomor 21 & 22)**.

| Ch | Judul | Objektif | Missable |
|---|---|---|---|
| 1 | The Destruction of Mako Reactor 1 | 12 | 0 |
| 2 | Fateful Encounters | 13 | 0 |
| 3 | Home Sweet Slum | 30 | 14 |
| 4 | Mad Dash | 16 | 2 |
| 5 | Dogged Pursuit | 13 | 1 |
| 6 | Light the Way | 13 | 5 |
| 7 | A Trap Is Sprung | 13 | 3 |
| 8 | Budding Bodyguard | 43 | 16 |
| 9 | The Town That Never Sleeps | 45 | 15 |
| 10 | Rough Waters | 11 | 2 |
| 11 | Haunted | 13 | 6 |
| 12 | Fight for Survival | 9 | 0 |
| 13 | A Broken World | 20 | 7 |
| 14 | In Search of Hope | 47 | 29 |
| 15 | The Day Midgar Stood Still | 14 | 4 |
| 16 | The Belly of the Beast | 25 | 13 |
| 17 | Deliverance from Chaos | 29 | 5 |
| 18 | Destiny's Crossroads | 7 | 0 |
| 21 | INTERmission 1: Wutai's Finest | 53 | 31 |
| 22 | INTERmission 2: Covert Ops | 30 | 13 |

- **Ch 1–6 sudah terverifikasi saat dimainkan.** Ch 7 ke atas disusun dari beberapa walkthrough (PowerPyx, Game8, Fextralife, GamerGuides) dan dicek ulang missable-nya, **tapi belum diverifikasi di game** — koreksi saat user sampai di sana.
- Format satu objektif: `{ "id": "c3-05-...", "type": "cerita|side quest|materia|senjata|music disc|aksesori|armor|summon|manuskrip|kejadian|trofi", "name": "...", "where": "...", "missable": bool, "optional"?: bool, ... }`, plus `pointOfNoReturn` per chapter.
- Koreksi yang sudah dibuat user: Bulletproof Vest = **aksesori** (bukan armor); disc tetap missable; Lightning cukup opsional, bukan missable; MP Up opsional.

---

## 6. Cara kerja overlay (penting sebelum mengubah kode)

File utama: `MainWindow.xaml.cs`, `Ff7rChapterReader.cs`, `Ff7rObjective.cs`, `ItemMap.cs`, `ProgressStore.cs`, `Native.cs`.

- **Chapter**: dibaca dari memori seperti autosplitter LiveSplit (offset dari Mysterion06/FF7RSplitter). 255 = menu/loading.
- **Quest aktif**: scan signature (ReadProcessMemory, paralel di setengah core, cache blok 64 KB; scan awal ±13 detik). Baris tabel objektif: title key +0x58, desc key +0x68, sprite +0x78. Entry dibuat berjarak 0x188; **quest sekarang = entry terakhir dari deretan terpanjang**. Sub-objektif: key `_(s|S|sub)\d+`. Selesai = desc `_990_d`, `_Done`, atau key `_Mate_` yang desc-nya ≠ title+"_d". Nomor chapter di key **bergeser mulai Ch8** (`_Chapter09_` = Ch8; `_Chapter14_` = Ch13+14) → lihat `ChapterOf`.
- **Inventory**: list di memori adalah salinan save; salinan "live" dipilih dengan `LiveCopy()` (yang berubah antara dua baca berjarak 1 detik). Peralatan: record 0x10 byte `{kind 1/2, id}` di materia − 0x2000. Tumpukan yang bertambah dihitung sebagai pickup.
- **Reconcile saat load save** (dipicu saat kembali ke game, lompat chapter, atau >3 slot berubah): chapter setelahnya di-uncentang; chapter sebelumnya mempertahankan langkah cerita + set `Ever`; item unik non-opsional dicentang dari kepemilikan; setelah `_storyMayGoBack`, semua setelah langkah cerita sekarang di-uncentang.
- **ProgressStore**: simpan atomik (file temp + move), backup di `data\backups`, fallback ke backup terbaru kalau file rusak (`Recovered` → muncul di footer).
- Tampilan: quest utama biru, sub-quest kuning; warna per tipe; tag REWARD; background 50%, outline tipis; footer hanya untuk error.
- Log diagnosa: `data\logs\objective-search.log`, `quest-choice.log` (tanda [RAGU]/[BEDA]), `steam-online.log`.

### Hotkey overlay

| Tombol | Fungsi |
|---|---|
| Ctrl+Shift+G | Tampilkan/sembunyikan overlay |
| Ctrl+Shift+Space | Centang objektif berikutnya |
| Ctrl+Shift+Backspace | Batalkan centang terakhir |
| Ctrl+Shift+PageDown / PageUp | Chapter berikut / sebelumnya |
| Ctrl+Shift+T | Mode klik-tembus |
| Ctrl+Shift+A | Ganti tampilan (Ctrl+Shift+L sudah dipakai aplikasi lain) |
| Ctrl+Shift+H | Mode Normal / Hard (tampilkan langkah khusus Hard) |
| Ctrl+Shift+L, atau Ctrl+Shift+Alt+L | Bahasa EN / IN (sama dengan saklar di header). Di PC user Ctrl+Shift+L dipakai aplikasi lain, jadi yang aktif Ctrl+Shift+Alt+L; tooltip saklar menyebut yang aktif |

Klik kotak peringatan kuning: buka/tutup daftar missable dan alasannya. Hotkey baru: cek dulu tabel ini, karena Ctrl+Shift+L sudah dipakai aplikasi lain.

## 7. Dashboard (dibuat 9 Okt 2026)

File: `App.xaml.cs` (tray, single instance `Local\GameTracker.Single`, argumen `--game`), `DashboardWindow.xaml(.cs)`, `GameModule.cs` (`GameRegistry` membaca semua `games/<id>/game.json`; tambah game = satu folder, tanpa kode), `PlayTime.cs`, `SteamStats.cs`, `SteamInfo.cs`.

- Gaya HUD futuristik: font Bahnschrift, warna Mako `#5EEAD4` + cyan `#38BDF8`, tile bersudut, jendela tanpa border + maximize; bring-to-front saat maximize/restore.
- Dashboard **tidak disembunyikan** setelah START; hanya sembunyi saat user minimize.
- Info Steam (semua gratis, tanpa API key):
  - Lokal: waktu main, terakhir main, 2 minggu terakhir, achievement (dari `appcache\stats`), cloud save, update/build, ukuran + lokasi library, screenshot terbaru (folder screenshot game + F12 Steam), artwork fallback dari `appcache\librarycache`.
  - Online (cache 15 menit): pemain online, harga (`cc=id`), 3 berita Steam.
  - **Data akun hanya dari akun Steam yang sedang login** (`HKCU\Software\Valve\Steam\ActiveProcess\ActiveUser` + pid Steam hidup). Kalau belum login → tile akun disembunyikan, tampil "Steam belum login". Tile AKUN STEAM menampilkan nama profil.
- User bilang: "masukkan semuanya dulu, nanti kita hapus yang kira-kira tidak berguna" → **tunggu user memilih tile mana yang dihapus**.

---

## 8. Yang perlu dilakukan AI berikutnya

**Prioritas (saat user main):**
1. Dampingi user di **Chapter 7 dan seterusnya**: pastikan centang otomatis benar, perbaiki guide (urutan, nama, missable/opsional, lokasi) berdasarkan apa yang user lihat di game. Ch 7+ belum terverifikasi.
2. Kalau ada salah centang: cek `quest-choice.log` / `objective-search.log` dulu sebelum mengubah logika reconcile.
3. Perhatikan pergeseran key chapter mulai Ch8 (`ChapterOf`) — paling rawan salah di Ch8–14.

**Menunggu keputusan user:**
4. Tile dashboard mana yang dihapus.
5. Tes kondisi "Steam belum login" di dashboard (belum dites; jangan logout akun user sendiri).
6. Hapus `D:\GameTrackerScan` dan sisa file di C: — hanya kalau user setuju.

**Tertunda / opsional:**
7. Riset penanda cutscene (lihat `research\notes.md`, bagian "Cutscene marker", dijeda).
8. `README.md` sudah usang (masih menyebut guide Ch 1–3, path lama, `%APPDATA%`) — perbarui kalau user mau.
9. Log suhu GPU (`data\logs\gpu-temp.csv`): PC user pernah mati mendadak 2× (Kernel-Power 41, tanpa BSOD; puncak GPU 79 °C ±165 W). Belum ada kesimpulan; bisa dinyalakan lagi saat user main.

## 9. Jebakan yang sudah pernah terjadi

- Escape string di skrip Python/bash merusak path (`\b`, `\f` → karakter kontrol). Untuk edit file C#/JSON pakai editor/tool edit langsung, bukan here-string dengan backslash.
- Sebelum build, matikan proses `GameTracker` (exe terkunci → build gagal / instance ganda).
- `MaxBy` pada sequence kosong → crash; pakai `OrderByDescending().FirstOrDefault()`.
- Jangan enumerasi koleksi yang diubah timer lain — `ToList()` dulu.
- Item yang bisa dibeli (mis. Bulletproof Vest) pernah tercentang karena user punya hasil beli → item opsional tidak boleh dicentang dari kepemilikan.
- Commit butuh identitas: pakai nama/email dari commit sebelumnya (`git log -1 --format="%an|%ae"`) dengan `git -c user.name=... -c user.email=...`.
