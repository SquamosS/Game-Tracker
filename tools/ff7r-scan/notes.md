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

- Log event side quest (persist di save): materia+0xE9A4, int32 per langkah, -1 = kosong. Rat selesai 100910228, Prowl selesai 167986966, kucing 2 (Lost Friends) 83873628. Penyelamatan Johnny (cerita) TIDAK menulis apa pun di blok ini.
- music disc 258 = Barret's Theme
- Lost Friends selesai 03:23:20: flag C4:1 (m+0x412C4 bit1), event 171907506; kucing 3 = 199427524
- Graveyard selesai 03:29:52: flag C4:0. Urutan bit (dari m+0x412C0): Rat 29, Nuisance 30, Prowl 31, Graveyard 32, Lost Friends 33, Chadley 34. Key item 123 = card key Graveyard.
- Katie 50 kill 03:35:22: materia 13002 = MP Up; flag watch+0x431E0 (m+0x41140) bit31 (bit29/30 = Katie 10/20?)
- Alone at Last 03:44:58: flag m+0x411E4 bit11, hadiah aksesori 9033 (Crescent Moon Charm?). Kunci flag overlay sekarang relatif m+0x40E00 (Rat = 4C0:29 dst).
- 9033 = Crescent Moon Charm (pasti, dari user); 9017 = Power Wristguards (aksesori didapat 10-06, Ch2)
- Masuk Seventh Heaven (Shinra Reacts selesai) 03:54: flag m+0x4134C bit28 (= kunci 54C:28). Penghitung m-0x1EA0 ternyata reset per load (hitung kejadian sejak load) -> tidak dipakai. Daftar riwayat bergeser di m+0x509E4, m+0x54870, m+0x548F0 (nilai ~140000, mungkin ID area/objektif).
- Darts Seventh Heaven selesai 04:01:30: flag m+0x411E0 bit27 (3E0:27), tidak ada item (Luck Up diberi Wedge di Ch4)
- Ifrit (summon materia) = 14003, didapat 04:11:53 dari Jessie
- Wind Materia = 10007 (dibeli 3x 02:39-02:40)
- Ch4 04:30-04:36: penghitung m-0x1EA0 naik tepat saat objektif cerita berganti (14->15 To Sector 7, 15->16, 16->17 Homecoming) -> bisa jadi sinyal 'objektif berganti' real-time (nilai absolut reset per load). Flag cerita Ch4 belum muncul sampai 04:36.
- Ch4: key item 111 = Shinra ID Card (masuk 04:45:31, keluar lagi 04:46:19 saat objektif -> Sector 7-6 Annex Infiltration). Barrier Materia TIDAK diberikan di rumah Jessie.
- Ch4 Motor Chase selesai 04:28:22: flag m+0x4175C bit8 (kunci 95C:8) -> di luar jangkauan lama, FlagBytes diperbesar ke 0x1000

Sesi 2026-10-08 04:50 (game ditutup):
- TEKS OBJEKTIF LIVE: teks objektif aktif ("Head for the Sector 7-6 Annex with Biggs and Wedge.") ada di 3 tempat: 2 salinan tabel teks (semua objektif berurutan) + 1 buffer UI/HUD (0x2B77A7EE630) yang isinya hanya objektif aktif -> sumber 'quest aktif' yang live. Belum ketemu kunci teks objektif (/ tidak ada).
- Penghitung m-0x1EA0 naik tiap objektif berganti (live), reset per load.
- Rencana: (1) cari struct yang menunjuk ke buffer HUD / objek quest manager dengan nilai ID objektif; (2) alternatif: overlay mencocokkan teks objektif live dengan teks objektif yang disimpan di guide.

Riset offline 2026-10-08 (setelah game ditutup):
- Kingdom Save Editor (Xeeynamo, GPL-3.0) punya struktur save FF7R + daftar 433 ID item lengkap (resources/ff7r-meta-items.yml di KH3SaveEditor). Cocok 100% dengan ID yang kita amati (111 Shinra ID Card, 258/260 disc, 9002/9033/9040, 10011, 13012, 14003). Disalin ke overlay/data/ff7r-items.json sebagai fallback nama.
- Save terdiri dari chunk: ChunkCommon + 21 ChunkChapter (0x660E8 byte, per chapter: posisi karakter, NPC/objek/musuh). Belum ada field 'objektif aktif' yang terdokumentasi di sana.
- RENCANA quest live (sesi berikut, game harus jalan):
  1. Cari lagi buffer teks objektif HUD (teks objektif aktif saja), lalu pointer-scan + tes restart -> overlay bisa membaca teks objektif live.
  2. Ekstrak semua teks objektif dari tabel teks game (pool string di memori) dan cocokkan ke langkah guide; overlay pindah langkah saat teks live cocok. Belajar: teks yang terlihat saat langkah X aktif = milik X.
  3. Tampilkan teks objektif live di header overlay (selalu benar walau mapping belum ada).
  4. Cadangan: penghitung m-0x1EA0 sebagai pemicu 'objektif berganti'.
