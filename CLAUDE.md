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
- `overlay/` aplikasi bersama (dashboard + overlay), dipakai semua game.
- `overlay/games/<id>/` semua file satu game: `game.json` (nama, proses, Steam id, reader), `guide.json` (panduan), `points.json` (titik tujuan per langkah), `assets/` (gambar), data lain. FF7R: `overlay/games/ff7r/` dengan pembaca memori di `reader/` (`Ff7rChapterReader.cs` + partial `Ff7r*.cs`, `ItemMap.cs`). Cara menambah game: `overlay/games/README.md`.
- `tools/ff7r-scan/` scanner riset memori (output ke `research/scan/`).
- `data/` progres & runtime (di-ignore git): progres, `trail.json` (jejak), `chests/` (peti termuat + `opened.json`), `points-recorded.tsv` (Ctrl+Shift+Alt+P), `logs/*.log` (crash, state, position, area, items, trail, chest-flag, field-actors, quest-choice, objective-search). Target: per game `data/<id>/` (belum, lihat Arsitektur).
- `research/` riset & hasil walkthrough (di-ignore git).

## Arsitektur: overlay sebagai canvas (aturan, berlaku untuk semua perubahan)
- Kode bersama (`overlay/*.cs`) tidak boleh berisi logika khusus satu game: alamat memori, id item (mis. Gil = 20), nama tabel/kunci game (`$str080`, `obt080`), kata kunci bahasa di teks panduan. Hal itu masuk ke `overlay/games/<id>/` (reader atau data).
- Kode bersama bicara ke pembaca game lewat antarmuka umum (target `IGameReader`): chapter, objektif & halaman quest (judul, tahap, selesai), inventory, posisi & area, state (menu/battle/cutscene), titik penting (peti: posisi, isi, dibuka?, ada di peta?). Pembaca boleh mengisi sebagian; fitur tanpa data mati diam-diam (null = tidak tahu, tidak menebak).
- Aturan panduan lewat kolom eksplisit di `guide.json` (`after`, `revisit`, `optional`, `missable`, `hard`, target: `rewardOf`, `auto`), bukan mencari kata di teks "where".
- Jenis langkah, ikon, warna: target didefinisikan per game di `game.json` (sekarang masih di kode, daftar FF7R).
- Data runtime per game (target `data/<id>/`), supaya progres/jejak/log game lain tidak tercampur.
- Game tanpa reader tetap jalan sebagai checklist manual (hotkey), dengan titik manual dan jejak dari posisi kalau ada.
- **Status 10 Okt 2026: belum dirombak.** `MainWindow` masih memakai `Ff7rChapterReader` langsung (±50 pemanggilan) dan aturan "hadiah"/"otomatis" masih membaca teks. Rencana 4 tahap (antarmuka pembaca; aturan teks -> kolom; jenis langkah di game.json; data per game) ada di `docs/HANDOFF-AI.md` bagian 0. Fitur baru jangan menambah ketergantungan langsung ke FF7R di kode bersama.

## Aturan kode & performa (game harus tetap lancar)
- Poll overlay 1x/detik di thread UI: hanya baca kecil. Angka tunggal lewat overload `ReadProcessMemory(..., out long/int/byte, ..., out nint)` (tanpa array); `lpNumberOfBytesRead` selalu `nint`.
- Blok besar: satu kali baca + parse dari `Span` (lihat `ReadRecords`, `ItemsStart`), buffer dari `ArrayPool`; jangan alokasi per record.
- Scan besar (>1 MB) jangan di thread UI: lewat `Scan(...)` (thread pekerja, prioritas rendah, bisa dibatalkan `Detach`), hasil digabung di thread UI; buffer per scan, bukan dibagi. Scan penuh memori dibatasi (>= 20-30 dtk) atau dipicu perubahan.
- Cache hasil yang jarang berubah (mis. set flag dipakai ulang kalau byte sama); baca posisi sekali per poll; Regex `static readonly ... Compiled`.
- Hotkey overlay: tabel di `docs/HANDOFF-AI.md` (bagian Hotkey overlay); Ctrl+Shift+L dipakai aplikasi lain di PC user.
- Teks UI selalu dua bahasa lewat `Lang.T(en, id)`; langkah panduan baru isi `where` (Indonesia, dibaca aturan) dan `whereEn`.
- Centang tidak boleh salah, juga setelah load/restart: setelah load, item dipertahankan hanya kalau save yang di-load memilikinya, side quest dari halaman quest game; data yang dibaca sebelum load dibuang (`ForgetChestCopy`, `ForgetSideQuests`). Uji restart overlay setelah mengubah aturan centang (progres harus utuh).
- Render ulang hanya saat ada perubahan; tampilan tidak boleh menebak (lebih baik kosong). Pakai `Notify(...)` untuk umpan balik ke user.
- Setelah perubahan besar: minta satu agent review commit-nya (bug, thread, P/Invoke), lalu perbaiki.

## Skill proyek
- `/restart-overlay`: build dan jalankan ulang overlay dengan aman.
- `/memory-research`: alur riset memori FF7R dengan scanner, bersama user di dalam game.
