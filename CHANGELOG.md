# Changelog

## v1.18.15 — 2026-11-01

### Diperbaiki

- Judul output terminal dipaksa satu baris agar tidak terjepit oleh pesan status panjang.
- Status terminal dibatasi menjadi label pendek; rincian kegagalan tetap disampaikan melalui notifikasi.

### Versi

- Android: 1.18.15 (version code 65)
- Windows: 2.11.21
- Installer Windows: 2.13.1
- Host Linux: 0.1.0

## v1.18.14 — 2026-11-01

### Diperbaiki

- Keterangan hak shell dihapus dari layout terminal agar tidak menyisakan teks di atas output.
- Informasi tersebut kini muncul sebagai notifikasi singkat saat sesi terminal dimulai.
- Area output terminal diperbesar untuk memanfaatkan ruang yang tersedia.

### Versi

- Android: 1.18.14 (version code 64)
- Windows: 2.11.21
- Installer Windows: 2.13.1
- Host Linux: 0.1.0

## v1.18.13 — 2026-11-01

### Diperbaiki

- Area output terminal kini lebih besar dan menyesuaikan tinggi layar.
- Pesan status dan kegagalan singkat ditampilkan sebagai status/toast, bukan ditambahkan ke log output.
- Rincian error PowerShell seperti `CategoryInfo` dan `FullyQualifiedErrorId` ditampilkan lebih kecil dan redup agar hasil perintah utama lebih mudah dibaca.
- README menambahkan tutorial pemasangan Android, Windows, dan Linux.

### Versi

- Android: 1.18.13 (version code 63)
- Windows: 2.11.21
- Installer Windows: 2.13.1
- Host Linux: 0.1.0

## v1.18.12 — 2026-11-01

### Ditambahkan

- Host Linux awal dengan ikon tray, sambungan TLS manual dari Android, pemantauan CPU/RAM/baterai, daftar proses, kontrol daya, dan terminal.
- Dokumentasi dan pemasang awal untuk Ubuntu, Debian, Fedora, Arch Linux, dan distro desktop berbasis systemd.

### Perbaikan UI

- Tombol keyboard terminal Android diperbesar menjadi 40 dp dan teksnya diperbesar; tinggi panel output dikurangi agar keyboard tetap muat dan bisa digulir utuh.

### Perbaikan

- Android mengenali host Linux, menampilkan pilihan terminal Bash/POSIX, dan menyembunyikan kontrol layar serta Mode Joke yang belum tersedia di Linux.

## v1.18.11 — 2026-11-01

### Diperbaiki

- Mode Joke Windows kini hanya mengaktifkan simulasi di laptop. Pemicu Android dipisahkan agar ponsel tidak ikut menampilkan overlay.
- Panel terminal Android dapat digulir pada layar pendek agar seluruh tombol keyboard tetap bisa dijangkau.

### Versi

- Android: 1.18.12 (version code 62)
- Windows: 2.11.21
- Installer Windows: 2.13.1
- Host Linux: 0.1.0
