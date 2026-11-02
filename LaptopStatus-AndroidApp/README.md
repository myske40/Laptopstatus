# Laptop Status — Android

Aplikasi Android mencari Laptop Status di jaringan lokal, menampilkan nama perangkat, merek/model, alamat dan MAC, lalu meminta Windows menyetujui kode pairing. Token dan fingerprint TLS disimpan setelah disetujui, sehingga tidak perlu mengetik IP, kunci pairing, atau fingerprint secara manual. Aplikasi memantau status, CPU, RAM, baterai dan riwayat; juga menyediakan daftar proses serta kendali Windows. Pratinjau kendali layar memiliki mode lanskap layar penuh.

## Sambungan dari jaringan berbeda

Untuk koneksi jarak jauh yang sederhana, pasang Tailscale di Windows dan Android lalu masuk ke akun yang sama. Installer Windows Laptop Status dapat memasang Tailscale resmi, memeriksa checksum serta tanda tangannya, dan membuat aturan firewall terbatas ke rentang alamat Tailscale. Setelah login Tailscale aktif di kedua perangkat, pindai QR baru dari Ringkasan Windows; Android akan memakai alamat Tailscale laptop. Cloudflare tetap tersedia sebagai metode alternatif.

Tombol **Panduan sambungan Tailscale** di aplikasi membuka panduan lengkap dari `app/src/main/assets/README-Tailscale.md`, termasuk tautan unduhan Android/Windows.

## Menghubungkan

1. Jalankan aplikasi Windows dan izinkan koneksi pada jaringan privat di Windows Firewall.
2. Hubungkan ponsel ke Wi-Fi yang sama dengan laptop.
3. Di Android, buka halaman Akun. Daftar laptop yang ditemukan tampil seperti daftar jaringan Wi-Fi; tekan **Hubungkan** pada laptop yang ingin dipilih. Untuk mencari lagi gunakan **Pindai ulang laptop**. Atau tekan **Pindai QR laptop** dan arahkan kamera ke QR di Ringkasan Windows. QR mendukung alamat Wi-Fi maupun URL Cloudflare HTTPS. Bandingkan kode di ponsel dan Windows, lalu setujui di Windows. Pengaturan koneksi manual tetap tersedia jika alamat laptop dapat dijangkau. Pemindai menggunakan Google Play services, tidak meminta izin kamera dari aplikasi ini, dan mungkin mengunduh komponennya saat pertama kali digunakan.
4. Gunakan tombol kontrol Windows. Shutdown dan restart meminta konfirmasi di Android dan memiliki jeda 10 detik. Sleep meminta konfirmasi sebelum dijalankan. Mode Joke hanya dapat diaktifkan atau dimatikan dari Android; layar merah menjelaskan bahwa ini simulasi dan tertutup otomatis setelah 30 detik.
5. Kendali layar secara default meminta persetujuan di laptop. Untuk memakai password khusus LaptopStatus, aktifkan pilihan tersebut dan buat password di halaman Pengaturan aplikasi Windows. Dari Android pilih **Pakai password**; password hanya dipakai untuk membuka satu sesi dan tidak disimpan. Sesi berhenti otomatis setelah 10 menit dan dapat dihentikan dari laptop atau Android.


Password kendali layar adalah password khusus LaptopStatus, bukan password akun Windows. Akses password mati secara default dan memerlukan pairing. Jika laptop offline ketika ponsel melakukan polling, aplikasi menandainya offline setelah permintaan gagal. QR pairing memakai hostname Cloudflare aktif bila tersedia dan memprioritaskannya saat dipindai, sehingga dapat dipakai lintas jaringan. Tunnel dengan hostname tetap perlu domain yang tersedia pada akun Cloudflare; Quick Tunnel memakai URL acak sementara. Tunnel akun yang disimpan di Windows menyambung otomatis saat aplikasi Windows dibuka.

Jadwal daya satu kali tetap tersimpan di Windows saat laptop dimatikan. Jika laptop dinyalakan kembali sebelum waktu target, jadwal tetap berlaku. Jika Windows baru dinyalakan setelah waktu target, jadwal dibatalkan dan tidak memulai hitung mundur. Windows menampilkan notifikasi ketika jadwal dibuat.
