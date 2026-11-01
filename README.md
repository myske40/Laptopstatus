# LaptopStatus

![LaptopStatus — kendali laptop dari Android](assets/laptopstatus-banner.svg)

**Bahasa Indonesia** | [English](#english)

---

## Bahasa Indonesia

LaptopStatus membantu memantau dan mengendalikan laptop Windows atau Linux dari ponsel Android. Proyek ini menyediakan aplikasi Android, aplikasi Windows, dan host Linux tahap awal:

- **Aplikasi Windows** (WPF, .NET 8) yang berjalan di laptop.
- **Host Linux** (Python) untuk distro desktop berbasis systemd.
- **Aplikasi Android** (Kotlin) sebagai pendamping di ponsel.

Host Linux tahap awal tersedia di [`LaptopStatus-LinuxApp`](LaptopStatus-LinuxApp/README.md). Host ini memakai ikon tray dan sambungan TLS manual ke Android; fiturnya belum selengkap aplikasi Windows.

Ponsel terhubung ke laptop lewat **Cloudflare Tunnel (cloudflared)**, jadi laptop tetap bisa dijangkau dari jaringan lain tanpa perlu mengatur port forwarding. Pemasangan perangkat dilakukan dengan pairing melalui kode QR.

### Fitur

- Pairing ponsel dan laptop dengan kode QR, dilengkapi pembatasan percobaan (rate limiting).
- Pemantauan status laptop: CPU, GPU, RAM, baterai, dan rincian perangkat.
- Daftar proses yang berjalan, dengan ikon proses dan opsi menghentikan proses.
- Terminal jarak jauh (remote shell).
- Kontrol layar dan tampilan layar penuh.
- Kontrol daya (matikan, restart) dengan hitung mundur dan perlindungan password pembatalan.
- Peringatan baterai dan riwayat aktivitas.
- Jadwal tindakan otomatis.
- Dukungan koneksi lewat jaringan lokal (LAN), Tailscale, atau Cloudflare Tunnel.
- Host Linux awal dengan pemantauan CPU/RAM/baterai, daftar proses, kontrol daya, dan terminal.
- Tampilan antarmuka berbahasa Indonesia.

### Unduh

Unduh versi terbaru di halaman **Releases**:

**https://github.com/myske40/Laptopstatus/releases/latest**

| Berkas | Keterangan |
|---|---|
| `LaptopStatus-Android-<versi>.apk` | Aplikasi Android |
| `LaptopStatus-Windows-<versi>-Portable.zip` | Aplikasi Windows portabel (tanpa instalasi) |
| `LaptopStatus-Setup-<versi>.exe` | Installer Windows |
| `LaptopStatus-Linux-<versi>-source.zip` | Host Linux dan panduan pemasangan |
| `Source code (zip)` | Kode sumber, dibuat otomatis oleh GitHub |

### Cara pakai

**Di laptop (Windows)**

1. Unduh `LaptopStatus-Setup-<versi>.exe` dari Releases dan jalankan. Jika Windows SmartScreen muncul, pilih **More info → Run anyway**. Alternatifnya, ekstrak ZIP portabel lalu jalankan `LaptopStatus.exe`.
2. Izinkan LaptopStatus pada Windows Firewall untuk jaringan **Private** ketika diminta. Jangan izinkan jaringan publik.
3. Ikuti panduan pertama untuk memilih koneksi. Untuk koneksi lokal, hubungkan Android dan laptop ke Wi-Fi yang sama; Cloudflare/Tailscale dipakai bila perlu koneksi dari luar jaringan.
4. Buka halaman pairing di aplikasi Windows dan siapkan QR untuk dipindai dari Android.

**Di ponsel (Android)**

1. Unduh `LaptopStatus-Android-<versi>.apk` dari Releases.
2. Buka berkas APK. Jika diminta, izinkan browser atau pengelola berkas tersebut memasang aplikasi dari sumber ini, lalu selesaikan pemasangan.
3. Buka LaptopStatus dan pindai QR dari Windows. Cocokkan kode pairing di ponsel dan laptop, lalu setujui permintaan pada aplikasi Windows.
4. Setelah terhubung, gunakan halaman Perangkat untuk status, proses, terminal, dan kontrol yang tersedia.

**Di laptop (Linux)**

1. Unduh dan ekstrak `LaptopStatus-Linux-0.1.0-source.zip`. Ikuti perintah dependensi yang sesuai untuk Ubuntu/Debian, Fedora, atau Arch di [`LaptopStatus-LinuxApp/README.md`](LaptopStatus-LinuxApp/README.md).
2. Dari terminal, masuk ke folder hasil ekstrak dan jalankan `bash install.sh`. Buka **LaptopStatus** dari menu aplikasi.
3. Klik ikon tray **LaptopStatus → Sambungkan Android…**. Biarkan aplikasi Linux berjalan dan hubungkan ponsel ke jaringan lokal yang sama.
4. Di Android, tambahkan koneksi manual menggunakan alamat IPv4, token pairing, dan fingerprint TLS yang ditampilkan oleh ikon tray. Masukkan password terminal Linux saat membuka terminal.

Host Linux tahap awal ditujukan untuk distro desktop berbasis systemd seperti Ubuntu, Debian, Fedora, dan Arch. Kendali layar, QR pairing, tunnel jarak jauh, dan telemetry GPU belum didukung di Linux.

> **Catatan Windows:** karena aplikasi ini bukan dari Microsoft Store, Windows SmartScreen bisa menampilkan peringatan. Klik **More info**, lalu **Run anyway**.

### Membangun dari kode sumber

**Aplikasi Windows**

Prasyarat: Windows, [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0).

```bash
cd LaptopStatus-WindowsApp
dotnet build -c Release
```

**Aplikasi Android**

Prasyarat: JDK 17 dan Android SDK.

```bash
cd LaptopStatus-AndroidApp
./gradlew assembleRelease
```

APK hasil build tidak bertanda tangan. Tandatangani dengan keystore milikmu sendiri sebelum dipasang atau dibagikan.

### Struktur repositori

```
LaptopStatus-WindowsApp/   Aplikasi Windows (WPF, .NET 8)
LaptopStatus-AndroidApp/   Aplikasi Android (Kotlin)
LaptopStatus-LinuxApp/     Host Linux (Python, tahap awal)
LaptopStatus-Setup/        Proyek installer Windows
```

### Keamanan

- Jangan membagikan kode QR pairing kepada orang lain.
- Fitur seperti terminal jarak jauh dan kontrol daya memberi akses penuh ke laptop. Pasang hanya di perangkat milikmu sendiri.
- Jangan pernah memasukkan keystore, sertifikat `.pfx`, atau token ke repositori.

### Lisensi
nanti yaa

---

<a id="english"></a>

## English

LaptopStatus helps you monitor and control a Windows or Linux laptop from an Android phone. The project includes an Android app, a Windows app, and an early Linux host:

- A **Windows app** (WPF, .NET 8) that runs on the laptop.
- An **Android app** (Kotlin) that runs on the phone as a companion.
- An **early Linux host** (Python) for systemd-based desktop distributions.

The phone connects to the laptop through a **Cloudflare Tunnel (cloudflared)**, so the laptop stays reachable from other networks without any port forwarding. Devices are paired with a QR code.

The Linux host is in [`LaptopStatus-LinuxApp`](LaptopStatus-LinuxApp/README.md). It uses a tray icon and manual TLS pairing with Android; feature coverage is still limited compared with Windows.

### Features

- Pair your phone and laptop with a QR code, with rate limiting on pairing attempts.
- Laptop status monitoring: CPU, GPU, RAM, battery, and device details.
- Running process list with process icons and an option to end a process.
- Remote terminal (remote shell).
- Screen control and full-screen view.
- Power control (shut down, restart) with a countdown and a cancellation password.
- Battery alerts and activity history.
- Scheduled actions.
- Connect over a local network (LAN), Tailscale, or Cloudflare Tunnel.
- Early Linux host with CPU/RAM/battery monitoring, process list, power control, and terminal.
- Indonesian-language interface.

### Download

Get the latest version from the **Releases** page:

**https://github.com/myske40/Laptopstatus/releases/latest**

| File | Description |
|---|---|
| `LaptopStatus-Android-<version>.apk` | Android app |
| `LaptopStatus-Windows-<version>-Portable.zip` | Portable Windows app (no installation) |
| `LaptopStatus-Setup-<version>.exe` | Windows installer |
| `LaptopStatus-Linux-<version>-source.zip` | Linux host and installation guide |
| `Source code (zip)` | Source code, generated automatically by GitHub |

### Getting started

**On the laptop (Windows)**

1. Download `LaptopStatus-Setup-<version>.exe` from Releases and run it. If Windows SmartScreen appears, choose **More info → Run anyway**. Alternatively, extract the portable ZIP and run `LaptopStatus.exe`.
2. Allow LaptopStatus through Windows Firewall for **Private** networks only when prompted. Do not allow public networks.
3. Follow the first-run guide to choose a connection. For local access, connect Android and the laptop to the same Wi-Fi; use Cloudflare/Tailscale when you need access from another network.
4. Open pairing in the Windows app and display its QR code.

**On the phone (Android)**

1. Download `LaptopStatus-Android-<version>.apk` from Releases.
2. Open the APK. If prompted, allow the browser or file manager to install apps from this source, then finish installation.
3. Open LaptopStatus and scan the Windows QR code. Compare the pairing code on both devices, then approve the request in the Windows app.
4. After connecting, use the Device page for status, processes, terminal, and available controls.

**On Linux**

1. Download and extract `LaptopStatus-Linux-0.1.0-source.zip`. Follow the Ubuntu/Debian, Fedora, or Arch dependency commands in [`LaptopStatus-LinuxApp/README.md`](LaptopStatus-LinuxApp/README.md).
2. Open a terminal in the extracted folder and run `bash install.sh`. Launch **LaptopStatus** from the application menu.
3. Select **Sambungkan Android…** from the LaptopStatus tray icon. Keep the Linux host running and connect the phone to the same local network.
4. In Android, add a manual connection with the IPv4 address, pairing token, and TLS fingerprint shown by the tray icon. Enter the Linux terminal password when opening a terminal.

The early Linux host targets systemd desktop distributions such as Ubuntu, Debian, Fedora, and Arch. Screen control, QR pairing, remote tunneling, and GPU telemetry are not available on Linux yet.

> **Windows note:** because the app is not distributed through the Microsoft Store, Windows SmartScreen may show a warning. Click **More info**, then **Run anyway**.

### Build from source

**Windows app**

Requirements: Windows and the [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0).

```bash
cd LaptopStatus-WindowsApp
dotnet build -c Release
```

**Android app**

Requirements: JDK 17 and the Android SDK.

```bash
cd LaptopStatus-AndroidApp
./gradlew assembleRelease
```

The resulting APK is unsigned. Sign it with your own keystore before installing or sharing it.

### Repository layout

```
LaptopStatus-WindowsApp/   Windows app (WPF, .NET 8)
LaptopStatus-AndroidApp/   Android app (Kotlin)
LaptopStatus-LinuxApp/     Early Linux host (Python)
LaptopStatus-Setup/        Windows installer project
```

### Security

- Do not share the pairing QR code with anyone else.
- Features such as the remote terminal and power control give full access to the laptop. Install the app only on devices you own.
- Never commit keystores, `.pfx` certificates, or tokens to the repository.

### License

soon

