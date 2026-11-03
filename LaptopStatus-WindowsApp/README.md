# Laptop Status — Windows

Aplikasi WPF .NET 8 membaca penggunaan CPU, RAM, dan baterai, menyediakan daftar proses yang dapat dihentikan serta ringkasan memori mirip RAMMap, dan membuka API lokal TLS pada port 8765 dan 8767. Aplikasi menyiarkan identitas laptop di jaringan lokal untuk penemuan otomatis dari Android. Pairing baru meminta persetujuan di Windows, menampilkan kode cocok, lalu menyimpan token khusus perangkat dan fingerprint TLS di Android.

Setup meminta UAC saat pemasangan untuk mendaftarkan tugas Windows. Saat aplikasi dibuka langsung, UAC diminta satu kali untuk sesi aplikasi; daftar proses dan perintah kill kemudian memakai sesi administrator yang sama, tanpa UAC per proses. Proses inti Windows yang dilindungi tidak dapat dihentikan. Jika auto-start diaktifkan, Task Scheduler menjalankan aplikasi saat pengguna masuk dan shortcut Start Menu menjalankan tugas yang sama. Tutup jendela menyembunyikan aplikasi ke system tray.

## Menjalankan

1. Buka `LaptopStatus.csproj` di Visual Studio 2022 atau jalankan aplikasi yang sudah dipasang.
2. Izinkan aplikasi menerima koneksi pada jaringan privat ketika Windows Firewall bertanya. Installer membatasi aturan LAN ke profil Private/Domain dan Tailscale ke rentang alamat Tailscale.
3. Pastikan ponsel dan laptop terhubung ke Wi-Fi yang sama. Pilih laptop yang ditemukan di Android atau pindai QR dari Ringkasan Windows, lalu setujui kode pairing pada Windows.

API hanya menerima koneksi TLS dengan fingerprint sertifikat yang dipasangkan di Android dan header `Authorization: Bearer <kunci>`. Kunci pairing dan telemetri tidak dikirim lewat HTTP tanpa enkripsi. Aplikasi tidak memasang service tersembunyi dan melayani Android selama proses aplikasi berjalan, termasuk saat jendelanya disembunyikan ke tray.

API juga menerima kontrol Android yang terautentikasi untuk shutdown (jeda 10 detik), restart (jeda 10 detik), sleep, dan Mode Joke. Mode Joke menampilkan layar merah bertanda jelas sebagai simulasi dan tertutup otomatis setelah 30 detik. Navigasi Ringkasan, Riwayat, dan Pengaturan di sidebar membuka panel terkait.

Jadwal shutdown, restart, dan sleep satu kali disimpan di Windows bersama waktu targetnya. Windows menampilkan notifikasi saat jadwal dibuat. Jika Windows baru dinyalakan setelah waktu target, jadwal dibatalkan dan tidak memulai hitung mundur saat laptop dibuka kembali. Jika Windows tetap berjalan atau kembali aktif sebelum waktu target, jadwal tetap berlaku. Membatalkan jadwal atau menghapus aplikasi juga menghapus pemicu jadwal tersebut. Rutinitas berulang tetap memerlukan aplikasi berjalan ketika waktu rutinitas tiba.

Untuk koneksi dari jaringan lain, Ringkasan Windows menyediakan Cloudflare Tunnel. Untuk alamat yang tetap bagi laptop ini, buat satu tunnel khusus laptop tersebut pada akun Cloudflare dan arahkan hostname publiknya ke `http://127.0.0.1:8768`. Masukkan hostname dan token tunnel sekali di Pengaturan. Token disimpan terenkripsi dengan DPAPI; hostname tetap dimasukkan dalam QR pairing, termasuk sebagai alamat pilihan agar pemindaian dari jaringan berbeda tidak tertukar dengan alamat Wi-Fi lama. Aplikasi menyambung otomatis saat dijalankan dan mencoba ulang jika koneksi tunnel putus. `cloudflared` diunduh dari rilis resmi Cloudflare saat tunnel pertama kali dinyalakan. Endpoint origin HTTP hanya terikat ke loopback; perintah API tetap memerlukan token pairing Android. Quick Tunnel tidak memerlukan akun, tetapi membuat hostname acak dan hanya cocok untuk uji coba. Domain tetap harus berada pada domain yang tersedia di akun Cloudflare pengguna.


Kendali layar Windows memakai TLS pada port 8767. Secara default, sesi harus disetujui langsung di laptop. Pengguna dapat memilih akses password LaptopStatus di Pengaturan Windows: aktifkan secara lokal dan buat password khusus minimal 3 karakter. Aplikasi menyimpan hash PBKDF2 dengan salt serta salinan terenkripsi DPAPI untuk pemulihan lokal. Android tetap memerlukan kunci pairing dan fingerprint TLS serta password untuk tiap sesi. Percobaan dibatasi, tiap sesi berakhir otomatis setelah 10 menit, dan indikator selalu tampil di Windows dengan tombol untuk menghentikan sesi. Password akun Windows tidak diminta maupun digunakan.

Countdown shutdown/restart/sleep Windows berjalan 10 detik. Tekan Esc untuk membuka verifikasi password LaptopStatus; timer berhenti selama verifikasi, pembatalan hanya terjadi jika password benar, dan timer berlanjut bila password salah atau prompt ditutup. Countdown konfirmasi Android juga berhenti saat pesan khusus sedang diketik dan lanjut setelah pengguna berhenti mengetik atau menutup keyboard.

Setelah password baru disimpan, tombol **Tampilkan password** di bawah fingerprint TLS dapat mengungkapkannya selama aplikasi Windows masih terbuka. Nilai itu hanya berada di memori sesi; setelah aplikasi ditutup, buat ulang password jika perlu melihat atau menggantinya.

Cloudflare Tunnel membuat laptop menjadi host dan membuka koneksi keluar ke Cloudflare, sehingga router tidak perlu membuka port masuk. Tunnel berjalan selama aplikasi Windows hidup; untuk mode tunnel akun yang tersimpan, aplikasi menyambung lagi saat mulai otomatis setelah pengguna masuk ke Windows. Mode cepat membuat alamat baru setiap kali tunnel dimulai ulang.
