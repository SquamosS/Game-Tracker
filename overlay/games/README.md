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

Folder yang namanya diawali `_` (mis. `_template`) dilewati, juga `logs` dan `backups` (nama folder bersama di `data/`). Salin `_template` untuk memulai game baru.

## game.json

```json
{
  "displayName": "NAMA GAME SEPERTI DI STEAM",
  "shortName": "SINGKATAN",
  "processName": "nama_exe_tanpa_.exe",
  "steamAppId": 123456,
  "screenshotGlob": "Folder\\Relatif\\Ke\\Instalasi\\*.png",
  "reader": null,
  "stepTypes": {
    "cerita":     { "role": "story",  "en": "story", "color": "#38BDF8" },
    "side quest": { "role": "quest",  "icon": "◎", "color": "#22D3EE" },
    "item":       { "role": "item",   "icon": "◆", "color": "#4ADE80", "neverLost": true },
    "trofi":      { "role": "trophy", "en": "trophy", "icon": "★", "color": "#FCD34D" }
  }
}
```

- `processName`: nama proses saat game jalan (Task Manager > Details, tanpa `.exe`). Dipakai untuk status "sedang jalan" dan waktu main.
- `steamAppId`: untuk tombol START (lewat Steam), info Steam, dan artwork cadangan. `null` kalau bukan game Steam.
- `screenshotGlob`: opsional, folder screenshot milik game itu sendiri.
- `shortName`: opsional, nama pendek di baris status overlay (mis. `FF7R`); tanpa ini dipakai `displayName`.
- `reader`: id pembaca memori. `"ff7r"` = pembaca memori FF7R (centang otomatis, overlay ikut menu/battle/cutscene). `null` = overlay manual:
  checklist yang sama, dicentang dengan hotkey (Ctrl+Shift+Space centang, Ctrl+Shift+Backspace batal,
  Ctrl+Shift+PageUp/PageDown ganti chapter) atau klik di tampilan lengkap (Ctrl+Shift+A).
- `stepTypes`: jenis langkah yang dipakai `type` di guide.json (kunci = nama jenis, ditampilkan apa adanya dalam bahasa Indonesia).
  `role` menentukan aturan overlay: `story` (alur utama; membagi chapter jadi fase), `quest` (side quest, dicocokkan dengan halaman
  quest game), `event` (discovery/kejadian), `item` (diberikan game: dicentang dari inventory), `trophy` (tidak dihitung). Opsional:
  `en` (nama Inggris), `icon`, `color` (`#RRGGBB`), `neverLost: true` (tidak bisa dijual/habis: setelah load, tidak ada = belum
  punya), `unique: true` (hanya satu per playthrough: dimiliki = selesai). Jenis yang tidak terdaftar tampil abu-abu tanpa aturan.

## guide.json

```json
{
  "game": "NAMA GAME (dipakai juga untuk nama file progres di data/<id>/)",
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
- `type`: salah satu kunci `stepTypes` di game.json. FF7R: `cerita`, `side quest`, `discovery`, `kejadian`, `materia`, `senjata`,
  `armor`, `aksesori`, `summon`, `music disc`, `manuskrip`, `item kunci`, `trofi`.
- `where` diawali **nama area resmi** lalu titik dua (`Area (B5): ...`): overlay memakainya untuk pop-up "DI AREA INI"
  dan petunjuk ruang (hanya untuk game dengan reader yang tahu area pemain).
- Opsional: `optional: true` (item yang juga dijual: hanya pengingat), `warning` (point of no return; kalimat "Setelah ..." juga diisi di `closes`, menjelaskan
  apa yang tertutup), `needs` (id langkah yang ditunggu warning), `hard: true` (hanya mode Hard).
- `after`: id langkah yang membuka langkah ini (side quest Ch8 FF7R terbuka saat "Requests for the Mercenary" dimulai). Sebelum
  tercapai (langkah itu aktif atau selesai) langkah tidak tampil, di mana pun ia tercantum di panduan. Data game (halaman quest,
  objek di peta) tetap didahulukan kalau pembaca bisa membacanya.
- `revisit`: id langkah yang membuat area langkah ini bisa didatangi lagi; sebelum itu langkah yang terlewat tidak ditandai tertinggal.
- Tanpa `after`, urutan panduan menentukan: langkah baru tampil saat langkah cerita sebelumnya sedang berjalan.
- `rewardOf`: id side quest/discovery yang hadiahnya langkah ini; langkah tidak tampil di pop-up area sampai quest itu selesai.
- `auto`: item diberikan sendiri, tidak perlu dicari: `"chapter"` (di akhir chapter; ikut dicentang saat chapter berganti; untuk
  trofi = trofi tamat chapter), `"boss"` (setelah boss) atau `"yes"`. Tampil sebagai tag REWARD CHAPTER / REWARD BOSS / REWARD.
- `gameTitle`: nama quest seperti di game kalau nama panduan berbeda (`"Discovery: X"` -> `"X"`), untuk mencocokkan dengan halaman quest.
- `closes` / `closesEn`: apa yang tertutup setelah point of no return (kalimat "Setelah ..." / "After ..." dari warning), tampil di peringatan.
- Aturan overlay hanya membaca kolom-kolom ini dan awal `where` (nama area), bukan kata di teks. Mengisi kolom FF7R dari teks lama:
  `python -I tools/guide-columns.py overlay/games/ff7r/guide.json` (sekali, sudah dijalankan 10 Okt 2026).

## points.json (opsional)

Titik tujuan per langkah, untuk jarak dari pemain (hanya game dengan reader yang tahu posisi dan area):

```json
{ "c8-13-disc-costa": { "x": -6670, "y": 8590, "z": 349, "area": "Center District", "note": "depan Materia Shop" } }
```

Jarak hanya tampil kalau area titik itu terbaca di peta yang dimuat. Rekam sambil main dengan Ctrl+Shift+Alt+P
(`data/<id>/points-recorded.tsv`). Jejak otomatis (`data/<id>/trail.json`: tempat quest diambil, tahap selesai, item diambil) dipakai lebih dulu.

## reader/ (opsional)

Pembaca memori = satu kelas C# di `games/<id>/reader/` dengan atribut `[GameReader("<id>")]`; overlay memilihnya dari `reader`
di game.json, tanpa mengubah kode bersama. Mulai dari `GameReaderBase` (`overlay/GameReader.cs`): semua jawaban bawaannya "tidak
tahu", cukup `override` yang bisa dibaca (posisi, chapter, inventory...); compiler memeriksa nama dan tipenya. Mesin game bebas
(Unreal, Unity, ...): hanya pembaca game itu yang dibuat saat game itu dimainkan, pembaca lain tidak tersentuh. Nama item/flag:
turunan `GameNamesBase`. Yang tidak diketahui tetap `null` (atau daftar kosong), fitur yang memerlukannya mati diam-diam. Hanya baca
memori game, jangan menulis. Contoh lengkap: `games/ff7r/reader/Ff7rChapterReader.cs`.

Setelah menambah folder: build ulang (`dotnet build` di `overlay/`) supaya file JSON tersalin ke folder exe.
