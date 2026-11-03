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
mkdir -p "$install_dir" "$HOME/.local/share/applications" "$HOME/.config/autostart"
install -m 755 "$source_dir/laptopstatus_linux.py" "$install_dir/"
install -m 755 "$source_dir/laptopstatus_tray.py" "$install_dir/"
desktop_exec_path="$(python3 -c 'import sys; p=sys.argv[1]; print("\"" + p.replace("\\", "\\\\").replace("\"", "\\\"").replace("$", "\\$").replace("`", "\\`").replace("%", "%%") + "\"")' "$install_dir/laptopstatus_tray.py")"
desktop_file="$HOME/.local/share/applications/laptopstatus.desktop"
cat > "$desktop_file" <<EOF
[Desktop Entry]
Type=Application
Name=LaptopStatus
Comment=Pantau dan kendalikan Linux dari Android
Exec=python3 $desktop_exec_path
Icon=computer
Terminal=false
Categories=Utility;System;
EOF
chmod 644 "$desktop_file"
install -m 644 "$desktop_file" "$HOME/.config/autostart/laptopstatus.desktop"
echo "LaptopStatus Linux dipasang. Ikon tray akan berjalan otomatis saat login dan tersedia di menu aplikasi."
