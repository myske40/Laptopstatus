#!/usr/bin/env python3
"""Desktop tray launcher for the LaptopStatus Linux host."""

import hashlib
import os
import platform
import ssl
import subprocess
import sys
import socket
import threading
import tkinter as tk
from pathlib import Path
from tkinter import messagebox

import pystray
from PIL import Image, ImageDraw

HERE = Path(__file__).resolve().parent
APP_DIR = Path.home() / ".config" / "laptopstatus"
CERT = APP_DIR / "host.crt"
TOKEN = APP_DIR / "pairing-token"
PASSWORD = APP_DIR / "screen-password"


def info_dialog() -> None:
    der = subprocess.check_output(["openssl", "x509", "-in", str(CERT), "-outform", "DER"])
    fingerprint = hashlib.sha256(der).hexdigest().upper()
    addresses = sorted({item[4][0] for item in socket.getaddrinfo(platform.node(), None, type=socket.SOCK_STREAM)
                        if ":" not in item[4][0] and not item[4][0].startswith("127.")})
    text = (
        "LaptopStatus untuk Linux sedang berjalan.\n\n"
        f"Laptop: {platform.node()}\nAlamat IPv4: {', '.join(addresses) or 'lihat alamat IP di pengaturan jaringan'}\nPort: 8765\n\n"
        f"Token pairing:\n{TOKEN.read_text().strip()}\n\n"
        f"Fingerprint TLS:\n{fingerprint}\n\n"
        f"Password terminal:\n{PASSWORD.read_text().strip()}\n\n"
        "Di Android, pilih sambungan manual lalu masukkan alamat IP laptop, token, dan fingerprint TLS."
    )
    root = tk.Tk()
    root.withdraw()
    messagebox.showinfo("LaptopStatus Linux • Sambungan Android", text, parent=root)
    root.destroy()


def make_icon() -> Image.Image:
    image = Image.new("RGBA", (64, 64), (0, 0, 0, 0))
    draw = ImageDraw.Draw(image)
    draw.rounded_rectangle((7, 9, 57, 44), radius=6, fill=(99, 225, 194, 255))
    draw.rectangle((12, 14, 52, 39), fill=(12, 22, 35, 255))
    draw.polygon(((23, 48), (41, 48), (46, 54), (18, 54)), fill=(99, 225, 194, 255))
    return image


def main() -> None:
    server = subprocess.Popen([sys.executable, str(HERE / "laptopstatus_linux.py")], cwd=HERE)
    def stop(_icon, _item):
        server.terminate()
        server.wait(timeout=5)
        _icon.stop()

    icon = pystray.Icon("laptopstatus", make_icon(), "LaptopStatus Linux", menu=pystray.Menu(
        pystray.MenuItem("Sambungkan Android…", lambda _icon, _item: threading.Thread(target=info_dialog, daemon=True).start()),
        pystray.MenuItem("Keluar", stop),
    ))
    try:
        icon.run()
    finally:
        if server.poll() is None:
            server.terminate()


if __name__ == "__main__":
    main()
