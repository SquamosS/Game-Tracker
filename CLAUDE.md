# GameTracker

Overlay 100%-completion (C# .NET 8 WPF, Windows), game pertama FINAL FANTASY VII REMAKE INTERGRADE. Membaca memori game (read-only) untuk chapter, objektif, inventory, posisi, area, dan state menu/cutscene.

**Overlay = canvas bersama untuk banyak game.** Semua yang khusus satu game (kode pembaca, data, aturan, jenis langkah) tinggal di `overlay/games/<id>/`; game baru tidak boleh mengubah perilaku game lain. Lihat "Arsitektur" di bawah.

**Baca dulu:** `docs/HANDOFF-AI.md` (bagian 0 = kondisi terbaru) dan `research/notes.md` (semua alamat memori dan riset, termasuk yang gagal; tidak masuk git).

## Aturan user
- Komunikasi dalam bahasa Indonesia, langkah demi langkah, ringkas.
- Memori game **hanya dibaca**. Jangan pernah menulis ke proses game.
- Commit lalu **push otomatis** ke `backup/local-2026-10-08`. Jangan sentuh `main`, jangan buat PR. Tanya dulu untuk force-push, file besar/sensitif, atau build gagal. Identitas: `git -c user.name=SquamosS -c user.email=gpdhimas1@gmail.com`.
- Jangan ubah setting sistem. Data kerja di D: (C: hampir penuh).
- Kebenaran lebih penting dari cakupan: lebih baik tidak menampilkan/mempelajari daripada salah.
- Panduan: nama quest/item resmi bahasa Inggris, deskripsi Indonesia, "where" diawali nama area resmi game; item yang bisa dibeli = opsional, tidak dicentang dari kepemilikan; music disc tetap missable.

## Peta proyek
- `overlay/` aplikasi bersama (dashboard + overlay), dipakai semua game. `MainWindow` (+ `.Guidance.cs`) = tampilan, notifikasi, hotkey; `GameReader.cs` = antarmuka ke game; `Models.cs` = guide.json/game.json.
- `overlay/Modules/<fitur>/` fitur bersama tanpa WPF: `Guide/` (`GuideRules` aturan panduan, `StepStatus` langkah terbuka/aktif, `Checklist` langkah yang tampil + tag), `Progress/` (`ProgressTracker` + `.Items/.Flags/.Story/.Load/.Recap/.Manual`: centang dari game dan manual, bicara ke jendela lewat `IProgressHost`), `Area/` (`AreaTracker` area/posisi/rute, `World` jarak), `Chests/ChestGuide`, `Trail/TrailRecorder` (jejak, titik, items.log).
- `overlay/games/<id>/` semua file satu game: `game.json` (nama, `shortName`, proses, Steam id, `reader`, `stepTypes`), `guide.json` (panduan), `points.json` (titik tujuan per langkah), `assets/` (gambar), data lain. FF7R: `overlay/games/ff7r/` dengan pembaca memori di `reader/` (`Ff7rChapterReader.cs` + partial `Ff7r*.cs`, `ItemMap.cs`). Cara menambah game: `overlay/games/README.md`.
- `tools/tracker-tests/` tes modul dan data panduan tanpa game. `tools/ff7r-scan/` scanner riset memori (output ke `research/scan/`). `tools/guide-columns.py` pengisi kolom aturan (sekali pakai, 10 Okt).
- `data/` (di-ignore git): bersama = `settings.json`, `playtime.json`, `logs/` (crash, games, steam-online). Per game `data/<id>/`: progres `<nama game>.json` + `backups/`, `trail.json` (jejak), `area-links.json`, `chests/` (peti termuat + `opened.json`), `points-recorded.tsv` (Ctrl+Shift+Alt+P), `logs/*.log` (state, position, area, items, trail, chest-flag, field-actors, quest-choice, missed; FF7R juga objective-search, objective-titles), FF7R `item-map.json`. File tata letak lama dipindah otomatis sekali (`DataPaths.MoveOld`, tidak menimpa).
- `research/` riset & hasil walkthrough (di-ignore git).

## Arsitektur: overlay sebagai canvas (aturan, berlaku untuk semua perubahan)
- Kode bersama (`overlay/*.cs` dan `overlay/Modules/`) tidak boleh berisi logika khusus satu game: alamat memori, id item (mis. Gil = 20), nama tabel/kunci game (`$str080`, `obt080`), nomor chapter khusus, satuan engine, kata kunci bahasa di teks panduan. Hal itu masuk ke `overlay/games/<id>/` (reader, guide.json, game.json).
- Kode bersama bicara ke game hanya lewat `IGameReader` (`overlay/GameReader.cs`): chapter, objektif & halaman quest, inventory, flag, posisi & area (+ `UnitsPerMetre`), state (menu/battle/cutscene), peti, objek di peta, nama item/flag (`IGameNames`). Pembaca boleh mengisi sebagian; fitur tanpa data mati diam-diam (null = tidak tahu, tidak menebak).
- Pertanyaan baru ke pembaca: tambah di `IGameReader` **dengan isi bawaan "tidak tahu"** (dan versi `virtual` di `GameReaderBase`), supaya pembaca yang sudah ada (FF7R) tidak perlu diubah. Pembaca game baru diturunkan dari `GameReaderBase` dan diberi `[GameReader("<id>")]`. FF7R mengimplementasikan antarmuka langsung: anggota yang ia isi harus `public` dengan tipe persis (kalau tidak, isi bawaan diam-diam dipakai; tambah cek di tes).
- Aturan panduan lewat kolom eksplisit di `guide.json` (langkah: `after`, `revisit`, `optional`, `missable`, `hard`, `rewardOf`, `auto`, `gameTitle`, `closes`; chapter: `story`), bukan mencari kata di teks "where" (hanya nama area di awalnya).
- Jenis langkah, ikon, warna: per game di `game.json` `stepTypes` (role story/quest/event/item/trophy, `neverLost`, `unique`); kode bersama hanya bertanya role/tanda.
- Data runtime per game di `data/<id>/` (`DataPaths.Game/GameLogs`); hanya pengaturan, waktu main dan log aplikasi yang bersama.
- Game tanpa reader tetap jalan sebagai checklist manual (hotkey), dengan titik manual dan jejak dari posisi kalau ada.
- Fitur bersama baru = modul di `overlay/Modules/<fitur>/`, tanpa WPF bila bisa (proyek tes mengompilasi semua `Modules/**`), dengan cek di `tools/tracker-tests`.
- **Status 11 Okt 2026:** canvas selesai (4 tahap) dan `MainWindow` dipecah jadi modul; kode bersama netral (tidak ada logika FF7R). Riwayat dan detail di `docs/HANDOFF-AI.md` bagian 0.

## Aturan kode & performa (game harus tetap lancar)
- Poll overlay 1x/detik di thread UI: hanya baca kecil. Angka tunggal lewat overload `ReadProcessMemory(..., out long/int/byte, ..., out nint)` (tanpa array); `lpNumberOfBytesRead` selalu `nint`.
- Blok besar: satu kali baca + parse dari `Span` (lihat `ReadRecords`, `ItemsStart`), buffer dari `ArrayPool`; jangan alokasi per record.
- Scan besar (>1 MB) jangan di thread UI: lewat `Scan(...)` (thread pekerja, prioritas rendah, bisa dibatalkan `Detach`), hasil digabung di thread UI; buffer per scan, bukan dibagi. Scan penuh memori dibatasi (>= 20-30 dtk) atau dipicu perubahan.
- Cache hasil yang jarang berubah (mis. set flag dipakai ulang kalau byte sama); baca posisi sekali per poll; Regex `static readonly ... Compiled`.
- `game.json` yang rusak membuat game hilang dari daftar; alasannya di `data/logs/games.log`.
- Hotkey overlay: tabel di `docs/HANDOFF-AI.md` (bagian Hotkey overlay) dan `README.md`; Ctrl+Shift+L dipakai aplikasi lain di PC user.
- Teks UI selalu dua bahasa lewat `Lang.T(en, id)`; langkah panduan baru isi `where` (Indonesia, diawali nama area) dan `whereEn`, plus kolom aturan yang berlaku (`rewardOf`, `auto`, `warning` + `warningEn` + `closes`/`closesEn`).
- Centang tidak boleh salah, juga setelah load/restart: setelah load, item dipertahankan hanya kalau save yang di-load memilikinya, side quest dari halaman quest game; data yang dibaca sebelum load dibuang (`ForgetChestCopy`, `ForgetSideQuests`). Uji restart overlay setelah mengubah aturan centang (progres harus utuh; backup progres dulu).
- Tes: `dotnet run --project tools/tracker-tests` (tanpa NuGet, boleh saat overlay jalan, folder data sementara sendiri; juga memeriksa guide.json/game.json) harus 0 failed setelah mengubah modul, aturan atau panduan. Aturan baru = cek baru; pastikan cek itu bisa gagal (rusak aturannya sebentar).
- Render ulang hanya saat ada perubahan; tampilan tidak boleh menebak (lebih baik kosong). Pakai `Notify(...)` untuk umpan balik ke user.
- Setelah perubahan besar: minta satu agent review commit-nya (bug, thread, P/Invoke), lalu perbaiki.

## Skill proyek
- `/restart-overlay`: build dan jalankan ulang overlay dengan aman.
- `/memory-research`: alur riset memori FF7R dengan scanner, bersama user di dalam game.
