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
  "shortName": "SINGKATAN",
  "processName": "nama_exe_tanpa_.exe",
  "steamAppId": 123456,
  "screenshotGlob": "Folder\\Relatif\\Ke\\Instalasi\\*.png",
  "reader": null
}
```

- `processName`: nama proses saat game jalan (Task Manager > Details, tanpa `.exe`). Dipakai untuk status "sedang jalan" dan waktu main.
- `steamAppId`: untuk tombol START (lewat Steam), info Steam, dan artwork cadangan. `null` kalau bukan game Steam.
- `screenshotGlob`: opsional, folder screenshot milik game itu sendiri.
- `shortName`: opsional, nama pendek di baris status overlay (mis. `FF7R`); tanpa ini dipakai `displayName`.
- `reader`: id pembaca memori. `"ff7r"` = pembaca memori FF7R (centang otomatis, overlay ikut menu/battle/cutscene). `null` = overlay manual:
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
  `armor`, `aksesori`, `summon`, `music disc`, `manuskrip`, `item kunci`, `trofi` (trofi tidak dihitung). Tipe lain tampil abu-abu.
  (Target: daftar jenis, ikon dan warna didefinisikan per game di game.json.)
- `where` diawali **nama area resmi** lalu titik dua (`Area (B5): ...`): overlay memakainya untuk pop-up "DI AREA INI"
  dan petunjuk ruang (hanya untuk game dengan reader yang tahu area pemain).
- Opsional: `optional: true` (item yang juga dijual: hanya pengingat), `warning` (point of no return; teks "Setelah ..." menjelaskan
  apa yang tertutup), `needs` (id langkah yang ditunggu warning), `hard: true` (hanya mode Hard).
- `after`: id langkah yang membuka langkah ini (side quest Ch8 FF7R terbuka saat "Requests for the Mercenary" dimulai). Sebelum
  tercapai (langkah itu aktif atau selesai) langkah tidak tampil, di mana pun ia tercantum di panduan. Data game (halaman quest,
  objek di peta) tetap didahulukan kalau pembaca bisa membacanya.
- `revisit`: id langkah yang membuat area langkah ini bisa didatangi lagi; sebelum itu langkah yang terlewat tidak ditandai tertinggal.
- Tanpa `after`, urutan panduan menentukan: langkah baru tampil saat langkah cerita sebelumnya sedang berjalan.
- Hadiah quest: sekarang dikenali dari kata "hadiah" + nama quest di `where` (FF7R); target kolom `rewardOf`.

## points.json (opsional)

Titik tujuan per langkah, untuk jarak dari pemain (hanya game dengan reader yang tahu posisi dan area):

```json
{ "c8-13-disc-costa": { "x": -6670, "y": 8590, "z": 349, "area": "Center District", "note": "depan Materia Shop" } }
```

Jarak hanya tampil kalau area titik itu terbaca di peta yang dimuat. Rekam sambil main dengan Ctrl+Shift+Alt+P
(`data/points-recorded.tsv`). Jejak otomatis (`data/trail.json`: tempat quest diambil, tahap selesai, item diambil) dipakai lebih dulu.

## reader/ (opsional)

Pembaca memori = satu kelas C# di `games/<id>/reader/` yang mengimplementasikan `IGameReader` (`overlay/GameReader.cs`) dan
diberi atribut `[GameReader("<id>")]`; overlay memilihnya dari `reader` di game.json, tanpa mengubah kode bersama. Isi sebagian
saja boleh: yang tidak diketahui kembalikan `null` (atau daftar kosong), fitur yang memerlukannya mati diam-diam. Hanya baca
memori game, jangan menulis. Contoh lengkap: `games/ff7r/reader/Ff7rChapterReader.cs`.

Setelah menambah folder: build ulang (`dotnet build` di `overlay/`) supaya file JSON tersalin ke folder exe.
