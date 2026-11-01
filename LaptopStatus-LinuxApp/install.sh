#!/usr/bin/env bash
set -euo pipefail

source_dir="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
python3 -c 'import tkinter, pystray, PIL' 2>/dev/null || {
  echo "Dependensi belum lengkap: perlu Python 3, Tkinter, pystray, Pillow, dan OpenSSL." >&2
  echo "Pasang paket Python tersebut dari repositori distro, lalu jalankan installer lagi." >&2
  exit 1
}
command -v openssl >/dev/null || { echo "OpenSSL belum tersedia." >&2; exit 1; }

install_dir="$HOME/.local/opt/laptopstatus"
mkdir -p "$install_dir" "$HOME/.local/share/applications"
install -m 755 "$source_dir/laptopstatus_linux.py" "$install_dir/"
install -m 755 "$source_dir/laptopstatus_tray.py" "$install_dir/"
cat > "$HOME/.local/share/applications/laptopstatus.desktop" <<EOF
[Desktop Entry]
Type=Application
Name=LaptopStatus
Comment=Pantau dan kendalikan Linux dari Android
Exec=python3 $install_dir/laptopstatus_tray.py
Icon=computer
Terminal=false
Categories=Utility;System;
X-GNOME-Autostart-enabled=true
EOF
chmod 644 "$HOME/.local/share/applications/laptopstatus.desktop"
echo "LaptopStatus Linux dipasang. Buka LaptopStatus dari menu aplikasi untuk menjalankan ikon tray."
