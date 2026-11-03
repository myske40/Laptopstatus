# LaptopStatus Linux

Host LaptopStatus untuk laptop Linux. Aplikasi berjalan sebagai ikon tray dan menyediakan API TLS yang dapat dipasangkan secara manual dari aplikasi Android.

## Sistem yang didukung

Target awal mencakup Ubuntu, Debian, Fedora, Arch Linux, dan distro desktop Linux lain yang menyediakan Python 3, OpenSSL, systemd, Tkinter, pystray, serta Pillow. Pengukuran CPU dan RAM menggunakan `/proc`; daftar proses menggunakan `/proc`; kontrol daya memakai `systemctl`.

## Fitur awal

- Status CPU, RAM, baterai, informasi sistem operasi, dan nama perangkat.
- Daftar proses dan permintaan penghentian proses.
- Perintah shutdown, restart, dan sleep melalui systemd.
- Sesi terminal lokal yang diautentikasi untuk Android.
- Sambungan TLS dengan sertifikat dan token khusus yang disimpan pada akun pengguna.
- Ikon tray untuk menampilkan detail pairing atau keluar.

Kontrol layar jarak jauh, pairing QR dengan persetujuan desktop, tunnel jarak jauh, telemetry GPU, dan integrasi native distro belum tersedia pada implementasi awal ini.

## Dependensi

- Python 3.10 atau lebih baru
- OpenSSL
- Tkinter
- pystray
- Pillow
- systemd untuk perintah daya

Pasang dependensi Python dari repositori distro agar cocok dengan versi Python sistem.

### Ubuntu dan Debian

```bash
sudo apt update
sudo apt install python3 python3-tk python3-pil python3-pystray openssl
```

### Fedora

```bash
sudo dnf install python3 python3-tkinter python3-pillow python3-pystray openssl
```

### Arch Linux

```bash
sudo pacman -S python tk python-pillow python-pystray openssl
```

## Pasang

Jalankan `bash install.sh`. Installer menambahkan LaptopStatus ke menu aplikasi dan autostart sesi desktop. Saat pertama kali dijalankan, aplikasi membuat token pairing, password terminal, dan sertifikat TLS pada `~/.config/laptopstatus/`.

Pilih **Sambungkan Android…** pada menu ikon tray. Di Android, masukkan alamat IP laptop pada jaringan lokal, token pairing, dan fingerprint TLS yang ditampilkan. Terminal di Android meminta password terminal yang juga ditampilkan pada panel koneksi.

Server hanya menerima perangkat dengan alamat loopback, LAN privat, atau Tailscale. Jika firewall aktif, izinkan koneksi masuk TCP port `8765` hanya dari jaringan privat/lokal. Server memakai TLS, membatasi percobaan autentikasi, permintaan serentak, ukuran body, jumlah sesi terminal, dan usia sesi.

Token pairing dan password terminal memberi akses ke API laptop. Simpan dan gunakan hanya pada ponsel milikmu. Berkas kredensial dibatasi hanya untuk akun Linux yang memasangnya; kredensial tidak dicetak ke log.

Secara bawaan host menerima koneksi dari jaringan agar Android dapat tersambung. Untuk membatasi host ke laptop itu sendiri, jalankan dengan `LAPTOPSTATUS_BIND=127.0.0.1`; mode tersebut tidak menerima koneksi Android dari perangkat lain.

## Jalankan dari sumber

```bash
python3 laptopstatus_tray.py
```

Untuk menjalankan host tanpa ikon tray:

```bash
python3 laptopstatus_linux.py
```
