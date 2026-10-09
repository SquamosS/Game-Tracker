# Game per folder

Setiap game punya satu folder di sini, dengan nama = id game (huruf kecil, tanpa spasi, mis. `ff7r`).
Dashboard dan overlay dipakai bersama; game baru **tidak perlu kode** kecuali kalau mau centang otomatis dari memori.

```
games/<id>/
  game.json      wajib   nama, proses, Steam id, reader
  guide.json     wajib   panduan per chapter (checklist overlay)
  assets/        opsional gambar dashboard
    background.jpg   1920x1080 (16:9)  latar dashboard; tokoh utama di separuh kanan
    cover.jpg        600x900   (2:3)   sampul di daftar game
    icon.png         256x256           ikon tray / taskbar
    source/                            file asli gambar-gambar di atas
  reader/        opsional kode C# pembaca memori (hanya game dengan centang otomatis)
  *.json         opsional data lain milik game itu (mis. ff7r: items.json)
```

Folder yang namanya diawali `_` (mis. `_template`) dilewati. Salin `_template` untuk memulai game baru.

## game.json

```json
{
  "displayName": "NAMA GAME SEPERTI DI STEAM",
  "processName": "nama_exe_tanpa_.exe",
  "steamAppId": 123456,
  "screenshotGlob": "Folder\\Relatif\\Ke\\Instalasi\\*.png",
  "reader": null
}
```

- `processName`: nama proses saat game jalan (Task Manager > Details, tanpa `.exe`). Dipakai untuk status "sedang jalan" dan waktu main.
- `steamAppId`: untuk tombol START (lewat Steam), info Steam, dan artwork cadangan. `null` kalau bukan game Steam.
- `screenshotGlob`: opsional, folder screenshot milik game itu sendiri.
- `reader`: `"ff7r"` = pembaca memori FF7R (centang otomatis, overlay ikut menu/battle/cutscene). `null` = overlay manual:
  checklist yang sama, dicentang dengan hotkey (Ctrl+Shift+Space centang, Ctrl+Shift+Backspace batal,
  Ctrl+Shift+PageUp/PageDown ganti chapter) atau klik di tampilan lengkap (Ctrl+Shift+A).

## guide.json

```json
{
  "game": "NAMA GAME (dipakai juga untuk nama file progres di data/)",
  "status": "draft",
  "notes": [],
  "chapters": [
    {
      "number": 1,
      "title": "Judul Chapter",
      "pointOfNoReturn": "Kalimat singkat: apa yang tertutup setelah chapter ini",
      "objectives": [
        { "id": "c1-01-start", "type": "cerita", "name": "Nama Quest Resmi", "where": "Nama Area: keterangan", "missable": false },
        { "id": "c1-02-sword", "type": "senjata", "name": "Nama Item Resmi", "where": "Nama Area: peti di pojok", "missable": true },
        { "id": "c1-03-gate", "type": "cerita", "name": "Quest Berikutnya", "where": "Nama Area: ...", "missable": false,
          "warning": "SEBELUM LANJUT: ambil X. Setelah ini area tertutup." }
      ]
    }
  ]
}
```

- `id` unik di seluruh panduan; progres disimpan dengan id ini, jadi jangan diganti setelah dipakai.
- `type`: `cerita`, `side quest`, `kejadian` (nama diawali `Discovery:` tampil sebagai discovery), `materia`, `senjata`,
  `armor`, `aksesori`, `summon`, `music disc`, `manuskrip`, `trofi` (trofi tidak dihitung). Tipe lain tampil abu-abu.
- `where` diawali **nama area resmi** lalu titik dua (`Area (B5): ...`): overlay memakainya untuk pop-up "DI AREA INI"
  dan petunjuk ruang (hanya untuk game dengan reader yang tahu area pemain).
- Opsional: `optional: true` (item yang juga dijual: hanya pengingat), `warning` (point of no return; teks "Setelah ..." menjelaskan
  apa yang tertutup), `needs` (id langkah yang ditunggu warning), `hard: true` (hanya mode Hard).

Setelah menambah folder: build ulang (`dotnet build` di `overlay/`) supaya file JSON tersalin ke folder exe.
