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

1. Unduh dan jalankan installer, atau ekstrak ZIP portabel lalu jalankan `LaptopStatus.exe`.
2. Ikuti panduan awal untuk menyiapkan koneksi Cloudflare.
3. Buka jendela pairing untuk menampilkan kode QR.

**Di ponsel (Android)**

1. Unduh APK dari halaman Releases.
2. Aktifkan **Install dari sumber tidak dikenal** untuk aplikasi yang kamu pakai membuka APK (browser atau pengelola berkas).
3. Pasang aplikasi, lalu pindai kode QR dari laptop.

**Di laptop (Linux)**

1. Pasang dependensi yang disebutkan di [`LaptopStatus-LinuxApp/README.md`](LaptopStatus-LinuxApp/README.md), lalu ekstrak arsip Linux.
2. Jalankan `bash install.sh`, buka LaptopStatus dari menu aplikasi, dan pilih **Sambungkan Android…** pada ikon tray.
3. Tambahkan koneksi secara manual di Android memakai alamat IP, token, dan fingerprint TLS yang ditampilkan.

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

1. Download and run the installer, or extract the portable ZIP and run `LaptopStatus.exe`.
2. Follow the first-run guide to set up the Cloudflare connection.
3. Open the pairing window to show the QR code.

**On the phone (Android)**

1. Download the APK from the Releases page.
2. Turn on **Install unknown apps** for the app you use to open the APK (browser or file manager).
3. Install the app, then scan the QR code shown on the laptop.

**On Linux**

1. Install the dependencies listed in [`LaptopStatus-LinuxApp/README.md`](LaptopStatus-LinuxApp/README.md), then extract the Linux archive.
2. Run `bash install.sh`, open LaptopStatus from the application menu, and select **Sambungkan Android…** from the tray icon.
3. Add a manual connection in Android with the displayed IP address, token, and TLS fingerprint.

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

