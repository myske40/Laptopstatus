# Sambungan jarak jauh dengan Tailscale

Tailscale membuat jaringan pribadi antara HP dan laptop, tanpa domain, VPS, atau pengaturan port router.

## Unduh

Android: https://play.google.com/store/apps/details?id=com.tailscale.ipn

Windows: https://tailscale.com/download/windows

Panduan resmi Tailscale: https://tailscale.com/kb/1017/install/

## Pengaturan satu kali

1. Pasang Tailscale di Windows lewat installer Laptop Status, atau unduh dari tautan Windows di atas.
2. Buka Tailscale di laptop dan masuk dengan akun Tailscale.
3. Pasang Tailscale dari Play Store di HP, lalu masuk ke akun Tailscale yang sama.
4. Pastikan status Tailscale aktif di kedua perangkat. Biarkan Tailscale tersambung saat ingin mengontrol laptop dari jaringan lain.
5. Buka Laptop Status di Windows, pilih **Tampilkan QR untuk Android**. QR akan memakai alamat Tailscale jika tersedia.
6. Di aplikasi Android Laptop Status, buka **Akun** → **Pindai QR laptop**. Setujui permintaan pairing di Windows.

Pairing Laptop Status hanya perlu dilakukan sekali. Setelah tersimpan, Android dapat menggunakan koneksi privat Tailscale setiap kali kedua perangkat online dan masuk ke tailnet yang sama. Koneksi langsung dicoba terlebih dahulu; relay Tailscale dapat dipakai bila jaringan tidak mengizinkan koneksi langsung.

## Jika belum tersambung

- Pastikan Tailscale menunjukkan status aktif di Windows dan Android serta memakai akun yang sama.
- Buka ulang QR setelah Tailscale laptop aktif agar alamat terbaru ikut disertakan.
- Izinkan koneksi Laptop Status pada jaringan privat di Windows Firewall.
- Jika Tailscale belum dipasang lewat installer, gunakan tautan unduhan di atas.

Cloudflare tetap tersedia sebagai jalur alternatif di Laptop Status Windows. Pengaturan Tailscale tidak menghapus atau mengubah konfigurasi Cloudflare.
