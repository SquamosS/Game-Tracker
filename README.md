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

Ada di `overlay/guides/*.json`, satu file per game, dibagi per chapter. Edit file di `bin\Debug\net8.0-windows\guides` saat aplikasi jalan untuk melihat perubahan langsung, lalu salin ke `overlay/guides`. Panduan FF7R saat ini mencakup Chapter 1–3 dan masih draft.
