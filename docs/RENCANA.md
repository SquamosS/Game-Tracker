# Rencana v2: Overlay pelacak 100% per chapter

_7 Oktober 2026 · menggantikan rencana Steam achievement_

## Tujuan

Overlay seperti tracker objective di WoW/Overwolf atau tracker progres di LiveSplit, tapi untuk 100% completion:

- Overlay tahu kamu sedang di **chapter berapa** dan menampilkan semua yang harus dilakukan di chapter itu: senjata, materia, music disc, side quest, item missable.
- Item **tercentang sendiri** saat kamu mengambilnya.
- Tujuannya agar tidak ada yang terlewat, jadi kamu tidak perlu replay.
- Steam tidak dipakai sama sekali.

## Bagian 1: data panduan per chapter (inti aplikasi)

Untuk setiap chapter: daftar objektif berurutan sesuai jalur main, dengan tipe (senjata, materia, disc, quest, kartu/kejadian), lokasi singkat, tanda missable (titik tanpa kembali sebelum chapter selesai), dan "petunjuk deteksi" untuk pengenalan otomatis (misal teks notifikasi item).

Disimpan sebagai file JSON di folder lokal, mudah diedit.

## Bagian 2: mendeteksi progres otomatis

Ada beberapa cara. Tidak ada yang gratis, jadi dibuat bertingkat:

| Cara | Cara kerja | Kelebihan | Kekurangan |
|---|---|---|---|
| **A. Membaca layar (OCR)** | Mengambil cuplikan layar beberapa kali per detik, membaca teks notifikasi seperti nama item yang baru didapat dan judul chapter, lalu mencocokkan ke checklist | Tidak menyentuh game, aman dari anti-cheat, cara yang sama bisa dipakai untuk game lain | Bisa meleset kalau notifikasi cepat hilang atau tertutup efek. Butuh kalibrasi area layar |
| **B. Membaca memori game** (cara LiveSplit autosplitter) | Membaca angka di memori proses game (read-only), misal nomor chapter, inventory, flag quest | Paling akurat dan instan | Alamat memori harus dicari dulu (reverse engineering) dan bisa berubah saat game di-update. Perlu dicek apakah komunitas speedrun FF7R sudah punya alamatnya |
| **C. Membaca save file** | Membaca file save setiap kali game menyimpan | Lengkap: tahu semua item dan flag | Format save FF7R belum tentu terdokumentasi; hanya ter-update saat menyimpan |
| **D. Manual** | Hotkey untuk ganti chapter dan centang item berikutnya | Selalu berfungsi | Tidak otomatis |

Usulan urutan:
1. **Versi pertama**: checklist per chapter + pilih chapter lewat hotkey + centang lewat hotkey (cara D). Langsung bisa dipakai main.
2. **Deteksi chapter dan item lewat OCR** (cara A) memakai OCR bawaan Windows.
3. **Riset memori** (cara B): cari alamat chapter dan inventory FF7R; kalau ketemu, deteksi jadi akurat.

## Bagian 3: teknologi dan build cepat di PC kamu

Usulan: **C# .NET 8 (WPF)**, dikerjakan langsung di folder `D:\.... Claude Suno Project\Game tracker`.

- `dotnet run` membangun dan menjalankan dalam beberapa detik, tidak perlu menunggu build di GitHub.
- Jendela overlay transparan, always-on-top, dan click-through mudah dibuat.
- OCR bawaan Windows (`Windows.Media.Ocr`) gratis dan cepat, tanpa library tambahan.
- Membaca memori proses (`ReadProcessMemory`) adalah cara yang sama dipakai LiveSplit, yang juga ditulis dengan C#.
- Yang perlu dipasang sekali: .NET 8 SDK.

Kode Tauri/Steam yang sudah ada disimpan di branch lama, tidak dipakai lagi.

## Batasan yang perlu diketahui

- Overlay tetap butuh game di mode **Borderless/Windowed**.
- Akurasi otomatis bergantung pada data panduan yang lengkap. Data FF7R per chapter harus disusun dan diverifikasi bertahap.
- OCR dan pembacaan memori sangat spesifik per game. Game kedua butuh penyesuaian sendiri.
