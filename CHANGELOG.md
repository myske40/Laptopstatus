# Changelog

## v1.18.22 — 2026-11-04

Rilis perbaikan crash dan pembaruan Statistik Windows 2.11.40 dengan installer Windows 2.13.28. Aplikasi Android 1.18.19 dan host Linux 0.1.0 tetap tersedia tanpa perubahan.

### Diperbaiki

- Crash pada halaman Statistik dan Rincian perangkat diperbaiki. Pembacaan informasi memori Windows kini memakai struktur native berurutan yang benar.
- Pembaruan telemetri dijaga agar kegagalan pembacaan sensor atau performa tidak menutup aplikasi Windows.
- Data pengecekan Statistik ditata dalam kisi agar bagian troubleshooting lebih mudah dibaca.

### Statistik Windows

- Tab Performa menampilkan grafik dan rincian pemakaian, kecepatan, core, prosesor logis, proses, thread, handle, cache, serta uptime CPU.
- Rincian RAM meliputi kapasitas terpakai/tersedia, modul, kecepatan, dan slot yang terisi bila dilaporkan firmware.
- Rincian GPU dan jaringan meliputi penggunaan, memori grafis, suhu/driver bila tersedia, serta laju unduh dan unggah.

### Berkas rilis

- Installer Windows 2.13.28 dan ZIP installer.
- Aplikasi Windows portable 2.11.40.
- APK Android 1.18.19 dan paket host Linux 0.1.0 tetap disertakan sebagai versi terbaru untuk platform tersebut.

### Versi

- Android: 1.18.19 (tidak berubah)
- Windows: 2.11.40
- Installer Windows: 2.13.28
- Linux host: 0.1.0 (tidak berubah)

## v1.18.21 — 2026-11-04

Rilis perbaikan tampilan Windows 2.11.38 dan installer Windows 2.13.26. Android tetap di 1.18.19.

### Diperbaiki

- Jendela utama Windows kini mengikuti area kerja layar agar tidak terpotong pada layar pendek atau skala DPI besar.
- Drag jendela utama dan installer memakai entry point Windows yang benar, memperbaiki error `SendWindowMessage` dan membuat bar judul dapat digeser.
- Daftar proses Windows kini selaras dengan Android: filter Semua/Bisa dihentikan/Dilindungi, ikon aplikasi, alasan perlindungan, pengurutan RAM/nama, dan hitungan kategori.
- Tombol Hentikan berubah menjadi Yakin? sebelum proses dihentikan; perlindungan proses tetap diperiksa lagi oleh Windows.

### UI baru dan animasi

- UI installer baru mengadaptasi HTML wizard referensi yang diberikan pengguna, termasuk ikon animasi, gerak pada indikator langkah, animasi tombol, dan transisi antar langkah.
- Alur wizard tetap menyediakan pilihan folder pemasangan, opsi koneksi dan pintasan, progres pemasangan, serta layar selesai.
- UI utama Windows memakai halaman penuh dengan bilah judul, navigasi, tema terang/gelap, animasi perpindahan halaman, dan rincian perangkat berbasis ikon.
- Tampilan Windows dan installer dimuat dari berkas lokal; keduanya tidak perlu mengambil HTML atau pustaka antarmuka dari internet saat dibuka.

### Versi

- Android: 1.18.19 (tidak berubah)
- Windows: 2.11.38
- Installer Windows: 2.13.26
- Linux host: 0.1.0 (tidak berubah)

## v1.18.20 — 2026-11-04

Rilis perbaikan Windows 2.11.31 dan installer Windows 2.13.12. Android tetap di 1.18.19.

### Diperbaiki

- Jadwal shutdown, restart, atau sleep yang terlewat saat laptop tidur atau tidak aktif kini dibatalkan; hitung mundur tidak dimulai setelah waktunya lewat.
- Pemulihan jadwal dari Task Scheduler memberi toleransi singkat untuk waktu mulai yang terlambat, lalu memberi notifikasi bahwa jadwal telah terlewat.

### Diperbarui

- Banner AI di README diperbarui dengan keterangan yang lebih jelas tentang alasan penggunaan AI dan peran umpan balik pengguna.

### Versi

- Android: 1.18.19 (tidak berubah)
- Windows: 2.11.31
- Installer Windows: 2.13.12
- Linux host: 0.1.0 (tidak berubah)

## v1.18.19 — 2026-11-03

Rilis Android 1.18.19, Windows 2.11.30, dan installer Windows 2.13.11.

### Ditambahkan

- Riwayat Windows kini memakai tampilan timeline dengan filter Semua/Koneksi/Baterai, penyimpanan lokal 20 aktivitas terbaru, catatan koneksi Android, baterai rendah, serta putus/sambung kembali.
- README menampilkan lisensi MIT, banner AI, alasan penggunaan AI, dan ringkasan transkrip proses pengembangan.

### Diperbaiki

- Klik kiri dan kanan ikon tray kini sama-sama membuka tray baru. Menu tray lama hanya aktif sebagai fallback saat tray baru gagal atau dipilih dari dalam flyout.
- Polling status Android setiap 5 detik tidak lagi memunculkan toast koneksi berulang; notifikasi hanya muncul saat koneksi baru atau tersambung kembali setelah terputus.
- Toast tray bisa diklik untuk membuka Riwayat Perangkat, tombol tutup dibuat lebih besar dan kontras, dan riwayat aktivitas Windows tersimpan setelah aplikasi dibuka ulang.
- Tombol “Keluar dari LaptopStatus” dan opsi Keluar di tray kini menutup aplikasi utama beserta layanan dan koneksi Android, sehingga proses LaptopStatus tidak tertinggal di Task Manager. Aplikasi lain tidak ikut ditutup.
- Menu tray Windows lama tersedia sebagai fallback otomatis saat tray baru gagal dibuka, serta dapat dipilih manual dari menu klik kanan.
- Log Windows menunjukkan tray diperbarui sebelum ikon siap dan tray popup gagal saat transform diterapkan pada Window; popup kini menunggu tray siap, mengikuti DPI monitor yang dipilih, dan notifikasi dibatasi agar tetap utuh di layar.
- Notifikasi tetap Android kini menunjukkan status koneksi, CPU, RAM, dan baterai. Aktivasi pemantauan meminta izin notifikasi dan memberi jalan ke pengaturan sistem bila notifikasi dimatikan.
- Nama PT HAHA NASIONAL kini ditampilkan pada halaman Tentang Android dan jendela installer. Installer ditandatangani agar UAC menampilkan penerbit pada komputer yang mempercayai sertifikat tersebut.
- Installer Windows uji menyertakan .NET runtime, sehingga bisa dijalankan pada laptop tanpa .NET 8 terpasang.

- Crash tray Windows akibat transform pada objek Window diperbaiki; pembacaan sensor dan pemulihan jadwal kini berjalan di latar agar jendela utama lebih cepat tampil.
- Animasi ikon Windows berhenti saat status idle dan sertifikat TLS lokal dilindungi DPAPI.
- WebView Android kini dibuat hanya saat dipakai, memakai WebViewAssetLoader, memblokir navigasi ke luar asset aplikasi, dan tidak ikut mencadangkan kredensial koneksi.
- Host Linux membatasi koneksi serentak, percobaan autentikasi, ukuran input terminal, jumlah dan umur sesi shell, serta membersihkan proses shell saat tray keluar.
- Statistik CPU Linux tidak lagi menunggu tambahan 80 ms per permintaan; pemindaian proses membaca data memori dengan lebih ringan.
- Installer Linux mengutip lokasi executable, mendaftarkan aplikasi di autostart, dan host tidak lagi mencetak token pairing atau password terminal ke output.

### Dioptimalkan

- Penyegaran terminal Android ditunda sampai UI awal tampil; WebView tidak dimuat pada mode native.
- Sertifikat Linux memakai ECDSA P-256 untuk mempercepat pembuatan sertifikat pertama.
- Aturan firewall LAN Windows kini hanya berlaku pada profil Private/Domain; jalur Tailscale tetap dibatasi ke rentang alamat Tailscale.
- Build uji sebelumnya: Android 1.18.18 (version code 66), Windows 2.11.26, installer Windows 2.13.7.
- Versi rilis: Android 1.18.19 (version code 67), Windows 2.11.30, installer Windows 2.13.11, Linux 0.1.0.

### Diperbaiki

- Startup Windows tidak lagi memperbarui ikon tray sebelum ikon tersebut dibuat.
- Ukuran installer kini mengikuti ruang layar yang tersedia; halaman wizard dapat discroll pada layar pendek.
- Tombol navigasi installer otomatis berpindah ke baris tambahan ketika ruang footer sempit.

### Diperbarui

- Desain tray Windows mengikuti referensi baru: flyout status, kartu koneksi Android, jadwal daya, metrik CPU/RAM/baterai, dan tombol aplikasi.
- Ikon tray kini beranimasi sesuai status layanan, koneksi, jadwal, atau koneksi yang ditolak.
- Notifikasi tray memakai kartu animasi yang dapat ditutup, bertumpuk, dan menampilkan waktu tampil.
- Animasi pada ikon, flyout, dan notifikasi mengikuti pengaturan animasi gerak aplikasi.

## v1.18.17 — 2026-11-03

### Diperbaiki

- Notifikasi jadwal daya kini muncul sebagai pemberitahuan di layar Windows, selain notifikasi ikon tray yang dapat disembunyikan Windows.

### Versi

- Android: 1.18.15 (version code 65; tidak berubah)
- Windows: 2.11.23
- Installer Windows: 2.13.3
- Linux host: 0.1.0

## v1.18.16 — 2026-11-03

### Diperbaiki

- Windows memberi notifikasi ketika jadwal shutdown, restart, atau sleep dibuat.
- Jadwal dibatalkan jika Windows baru dinyalakan setelah waktu target, sehingga hitung mundur tidak berjalan tiba-tiba.
- Dokumentasi jadwal Windows dan Android disesuaikan dengan perilaku pemulihan terbaru.

### Versi

- Android: 1.18.15 (version code 65; tidak berubah)
- Windows: 2.11.22
- Installer Windows: 2.13.2
- Linux host: 0.1.0

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
