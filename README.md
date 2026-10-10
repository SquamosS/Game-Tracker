# Game Tracker

Overlay di atas game yang memandu ke 100% completion per chapter: semua senjata, materia, music disc, side quest, dan item missable di chapter yang sedang kamu mainkan, dicentang saat sudah didapat. Tujuannya supaya tidak ada yang terlewat dan kamu tidak perlu replay.

Rencana: [docs/RENCANA.md](docs/RENCANA.md).

## Menjalankan (Windows)

Pasang sekali: [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0) dan Git.

```
cd "D:\.... Claude Suno Project\Game tracker"
git clone https://github.com/SquamosS/Game-Tracker.git
cd Game-Tracker\overlay
dotnet run
```

Build dan jalan dalam beberapa detik. Untuk versi terbaru: `git pull` lalu `dotnet run` lagi.

Main game dalam mode **Borderless** atau **Windowed** agar overlay terlihat di atasnya.

## Hotkey

| Tombol | Fungsi |
|---|---|
| Ctrl+Shift+G | Tampilkan / sembunyikan overlay |
| Ctrl+Shift+Space | Centang objektif berikutnya |
| Ctrl+Shift+Backspace | Batalkan centang terakhir |
| Ctrl+Shift+PageDown / PageUp | Chapter berikutnya / sebelumnya |
| Ctrl+Shift+T | Mode mouse: tembus ke game atau bisa klik overlay |

Geser overlay dengan menarik judulnya. Progres disimpan di `%APPDATA%\GameTracker`.

## Data panduan

Performa: overlay hanya membaca memori game (read-only) sekali per detik dengan bacaan kecil; pencarian besar berjalan di thread terpisah berprioritas rendah supaya FPS game tidak terganggu.

Satu folder per game di `overlay/games/<id>/` (`game.json`, `guide.json`, `assets/`); lihat `overlay/games/README.md` untuk menambah game. Edit `guide.json` di `bin\Debug\net8.0-windows\games\<id>` saat aplikasi jalan untuk melihat perubahan langsung, lalu salin ke `overlay/games/<id>`. Panduan FF7R mencakup Chapter 1–18 dan INTERmission (Ch 7 ke atas belum diverifikasi di game).
