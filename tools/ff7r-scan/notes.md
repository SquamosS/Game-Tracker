# FF7R story-progress scan (Steam 1.0.0.7)

Kandidat angka progres cerita (int32), ditemukan 2026-10-08:
- Chapter 3, save slot 3 = "bangun di kamar" (BUKAN awal Chapter 3) -> nilai 12
- setelah bicara Marle + ketemu Tifa (mulai ikuti Tifa) -> 14
- sampai di toko item bersama Tifa -> 15
- slot 4 (setelah belanja + dialog Tifa, kembali ke Marle bersama Tifa) -> 16

KOREKSI 2026-10-08 01:50: nilai 597..600 muncul di ribuan alamat (juga berubah saat ganti senjata) -> itu BUKAN game moment, cuma noise. Progres cerita belum ketemu.
Senjata: ganti Buster Sword <-> Iron Blade hanya mengubah stat Cloud (record karakter di 0x255E82C08B0: "2D 01 02 <level>", HP, MP, stat). ID senjata terpasang belum ketemu di area save.

KOREKSI 2026-10-08 01:00: angka 12..17 di atas ternyata nomor AREA/zona (berubah saat keluar-masuk toko), BUKAN progres cerita.
Rantai modul+0x57E99C0 0x58 0x598 0x48 = zona. Overlay sekarang memakainya sebagai "progres cerita" -> perlu diganti.

Kandidat "game moment" (progres cerita global), Ch3 Life in the Slums:
- sebelum bicara Tifa di toko senjata = 597, tutorial upgrade senjata = 598, gerbang Scrap Boulevard dibuka = 599/600
- beberapa salinan heap dengan nilai berbeda 597..600 (mungkin current/previous); belum dipastikan, belum ada jalur pointer
- kalau dicari ulang: find nilai sekitar 600 lalu filter inc saat cerita maju

Kandidat langkah dalam quest "Life in the Slums": 0 (bangun) -> 2 (ketemu Tifa) -> 3 (toko).

Inventory (ditemukan 2026-10-08, heap, alamat sesi itu 0x255E82F5640):
- array record 0x18 byte: +0 uint32 waktu dapat (unix detik), +4 0, +8 int32 item id, +0xC int32 jumlah, +0x10 0
- slot kosong: id -1
- id: 1 Potion, 2 Hi-Potion, 3 Ether, 5 Elixir?, 6 Phoenix Down, 9 Adrenaline, 10 Sedative, 13 Grenade, 20 = gil
- 90xx = key items; 257 = The Prelude (dibeli sesi ini); 300 kemungkinan Tifa's Theme; 118/144 belum diketahui
- belum: jalur pointer, materia/senjata/aksesori (array lain?)

Jalur pointer story counter (lolos tes restart): modul+0x57E99C0 0x58 0x598 0x48 (juga 0x5A06750 0x8.., 0x57E9AB0 0x528..)

Jalur pointer lama (sebelum restart):
- modul+0x59CD510 0x720 0x48  (dekat alamat chapter modul+0x59CD160)
- modul+0x5830750 0x38 0xEA8
- daftar lengkap: p1.txt (1042 jalur yang valid sebelum restart)

- Item list +0x10 = kategori: 0 consumable, 1 gil, 2 key item, 8 armor (9002 Iron Bangle, 9040 Star Bracelet), 9 aksesori, 10 music disc. Senjata TIDAK di list ini (Iron Blade tidak muncul).

Sesi 2026-10-08 ~02:00 (setelah restart; base save = materia list start 0x1E2EB9320A0, watch base = materia-0x2000):
- penghitung kejadian quest: materia-0x1EA0 (int). 6->7 Job NW -> Problem Solving; 7->9 Nuisance in the Factory selesai; 9->10 Rat Problem selesai (02:22:33)
- flag bitfield sekitar materia+0x40F00..+0x41400 (Rat Problem: bit di watch+0x432C0 = materia+0x412C0, 0x40000000 -> 0x60000000)
- kill counter Katie kemungkinan watch+0x352C4 (= materia+0x332C4): 22->25
- materia ID: 10002 Cleansing, 12002 Assess; key item 122 Combat Analyzer
- rekaman: save-watch.log
- 9024 Revival Earrings (aksesori); Katie 20-kill reward 02:23:45 flag watch+0x431E0 (0 -> 0x60000000)
