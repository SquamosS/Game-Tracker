# Game Tracker

Overlay di atas game yang memandu ke 100% completion per chapter: semua senjata, materia, music disc, side quest, dan item missable di chapter yang sedang kamu mainkan, dicentang saat sudah didapat. Tujuannya satu kali main tanpa ada yang terlewat.

Overlay dipakai bersama untuk banyak game: satu folder per game di `overlay/games/<id>/`. Game pertama: FINAL FANTASY VII REMAKE INTERGRADE (Steam), yang memorinya dibaca (hanya baca) untuk centang otomatis. Game tanpa pembaca memori tetap bisa dipakai sebagai checklist manual.

## Menjalankan (Windows)

Pasang sekali: [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0) dan Git.

```
git clone https://github.com/SquamosS/Game-Tracker.git
cd Game-Tracker\overlay
dotnet build
.\bin\Debug\net8.0-windows\GameTracker.exe              # dashboard (library game, START, OVERLAY)
.\bin\Debug\net8.0-windows\GameTracker.exe --game ff7r  # langsung overlay satu game
```

Main game dalam mode **Borderless** atau **Windowed** agar overlay terlihat di atasnya. Geser overlay dengan menarik judulnya.

## Hotkey

| Tombol | Fungsi |
|---|---|
| Ctrl+Shift+G | Tampilkan / sembunyikan overlay |
| Ctrl+Shift+Space | Centang langkah berikutnya |
| Ctrl+Shift+Backspace | Batalkan centang terakhir |
| Ctrl+Shift+PageDown / PageUp | Chapter berikutnya / sebelumnya |
| Ctrl+Shift+T | Mode mouse: tembus ke game atau bisa klik overlay |
| Ctrl+Shift+A | Tampilan ringkas / checklist lengkap / dengan langkah selesai |
| Ctrl+Shift+H | Mode Normal / Hard |
| Ctrl+Shift+Alt+P | Simpan titik posisi sekarang (untuk `points.json`) |
| Ctrl+Shift+L (atau Ctrl+Shift+Alt+L kalau L dipakai aplikasi lain) | Bahasa English / Indonesia (juga saklar EN \| IN di overlay) |

Klik peringatan kuning untuk membuka/menutup daftar item yang belum diambil. Double-click sebuah langkah: "aku sudah di langkah ini".

## Data

- Progres, jejak, peti, dan log tiap game ada di `data/<id>/` (folder `data` di samping `overlay`; di luar proyek: `%APPDATA%\GameTracker`). Bersama: `settings.json`, `playtime.json`, `logs/` (crash, games, steam-online). Progres disimpan atomik dengan backup otomatis (`data/<id>/backups/`).
- Panduan dan definisi game: `overlay/games/<id>/` (`game.json`, `guide.json`, `points.json`, `assets/`). Cara menambah game: [overlay/games/README.md](overlay/games/README.md). Edit `guide.json` di `bin\Debug\net8.0-windows\games\<id>` saat aplikasi jalan untuk melihat perubahan langsung, lalu salin ke `overlay/games/<id>`.
- Panduan FF7R mencakup Chapter 1–18 dan INTERmission (Ch 8 ke atas belum semua diverifikasi di game).

## Struktur kode

- `overlay/` aplikasi (dashboard + overlay, WPF). `MainWindow` hanya tampilan, notifikasi, hotkey.
- `overlay/Modules/<fitur>/` fitur bersama tanpa WPF: aturan panduan, pencentang dari game, area dan rute, peti, jejak.
- `overlay/GameReader.cs` antarmuka `IGameReader`: satu-satunya jalan kode bersama ke game. Pembaca per game di `overlay/games/<id>/reader/`.
- `tools/tracker-tests/` tes aturan dan modul tanpa game: `dotnet run --project tools/tracker-tests` (boleh saat overlay jalan).
- `tools/ff7r-scan/` scanner riset memori FF7R.

Performa: overlay membaca memori game sekali per detik dengan bacaan kecil; pencarian besar berjalan di thread terpisah berprioritas rendah supaya FPS game tidak terganggu.

Untuk pengembang/AI: baca `CLAUDE.md` lalu `docs/HANDOFF-AI.md` (bagian 0 = kondisi terbaru).
