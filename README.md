# Game Tracker

Overlay di atas game (Windows) yang menunjukkan progres achievement Steam dan checklist panduan 100%, tanpa perlu membuka walkthrough di browser atau HP.

Rencana lengkap: [docs/RENCANA-OVERLAY.md](docs/RENCANA-OVERLAY.md).

## Memakai

1. Unduh installer dari tab **Actions** → run terbaru → artifact `game-tracker-windows`, lalu jalankan.
2. Buat Steam Web API key di https://steamcommunity.com/dev/apikey (domain boleh `localhost`).
3. Di Steam, set privasi profil **Game details** ke **Public**.
4. Buka Game Tracker, isi API key di tab **Pengaturan** (SteamID64 terisi otomatis kalau Steam sedang login).
5. Main game dalam mode **borderless / windowed fullscreen**. Tekan **Ctrl+Shift+G** untuk membuka atau menutup overlay.

## Panduan

Panduan per game ada di `guides/*.json`. Item yang punya `achievement` (nama achievement di Steam, bahasa Inggris) tercentang otomatis saat achievement terbuka; sisanya dicentang manual.

Panduan yang tersedia:
- FINAL FANTASY VII REMAKE INTERGRADE (draft)

## Pengembangan

Butuh Node 22 dan Rust stable.

```
npm install
npm run tauri dev
```
