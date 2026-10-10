# GameTracker

Overlay 100%-completion untuk FINAL FANTASY VII REMAKE INTERGRADE (C# .NET 8 WPF, Windows). Membaca memori game (read-only) untuk chapter, objektif, inventory, posisi, area, dan state menu/cutscene.

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
- `overlay/games/<id>/` semua file satu game: `game.json` (nama, proses, Steam id, reader), `guide.json` (panduan), `assets/` (gambar), data lain. FF7R: `overlay/games/ff7r/` dengan pembaca memori di `reader/` (`Ff7rChapterReader.cs` + partial `Ff7r*.cs`, `ItemMap.cs`). Cara menambah game: `overlay/games/README.md`.
- `tools/ff7r-scan/` scanner riset memori (output ke `research/scan/`).
- `data/` progres & log runtime (di-ignore git): `data/logs/*.log` (crash, state, position, quest-choice, objective-search).
- `research/` riset & hasil walkthrough (di-ignore git).

## Aturan kode & performa (game harus tetap lancar)
- Poll overlay 1x/detik di thread UI: hanya baca kecil. Angka tunggal lewat overload `ReadProcessMemory(..., out long/int/byte, ..., out nint)` (tanpa array); `lpNumberOfBytesRead` selalu `nint`.
- Blok besar: satu kali baca + parse dari `Span` (lihat `ReadRecords`, `ItemsStart`), buffer dari `ArrayPool`; jangan alokasi per record.
- Scan besar (>1 MB) jangan di thread UI: lewat `Scan(...)` (thread pekerja, prioritas rendah, bisa dibatalkan `Detach`), hasil digabung di thread UI; buffer per scan, bukan dibagi. Scan penuh memori dibatasi (>= 20-30 dtk) atau dipicu perubahan.
- Cache hasil yang jarang berubah (mis. set flag dipakai ulang kalau byte sama); baca posisi sekali per poll; Regex `static readonly ... Compiled`.
- Hotkey overlay: tabel di `docs/HANDOFF-AI.md` (bagian Hotkey overlay); Ctrl+Shift+L dipakai aplikasi lain di PC user.
- Teks UI selalu dua bahasa lewat `Lang.T(en, id)`; langkah panduan baru isi `where` (Indonesia, dibaca aturan) dan `whereEn`.
- Render ulang hanya saat ada perubahan; tampilan tidak boleh menebak (lebih baik kosong). Pakai `Notify(...)` untuk umpan balik ke user.
- Setelah perubahan besar: minta satu agent review commit-nya (bug, thread, P/Invoke), lalu perbaiki.

## Skill proyek
- `/restart-overlay`: build dan jalankan ulang overlay dengan aman.
- `/memory-research`: alur riset memori FF7R dengan scanner, bersama user di dalam game.
