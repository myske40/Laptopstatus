#!/usr/bin/env python3
"""Small Linux host for LaptopStatus Android, using only the Python standard library."""

from __future__ import annotations

import hashlib
import json
import os
import platform
import secrets
import signal
import ssl
import subprocess
import threading
import time
from http.server import BaseHTTPRequestHandler, ThreadingHTTPServer
from pathlib import Path
from urllib.parse import parse_qs, urlsplit

APP_DIR = Path.home() / ".config" / "laptopstatus"
CERT = APP_DIR / "host.crt"
KEYFILE = APP_DIR / "host.key"
TOKEN_FILE = APP_DIR / "pairing-token"
SCREEN_PASSWORD_FILE = APP_DIR / "screen-password"
DEVICE_ID_FILE = APP_DIR / "device-id"
PORT = int(os.environ.get("LAPTOPSTATUS_PORT", "8765"))
LOCK = threading.Lock()
SHELLS: dict[str, tuple[subprocess.Popen, list[str], float]] = {}


def run(*args: str) -> str:
    try:
        return subprocess.check_output(args, text=True, stderr=subprocess.DEVNULL, timeout=3).strip()
    except Exception:
        return ""


def initialize() -> None:
    APP_DIR.mkdir(parents=True, exist_ok=True)
    os.chmod(APP_DIR, 0o700)
    if not TOKEN_FILE.exists():
        TOKEN_FILE.write_text(secrets.token_urlsafe(32), encoding="utf-8")
        os.chmod(TOKEN_FILE, 0o600)
    if not SCREEN_PASSWORD_FILE.exists():
        SCREEN_PASSWORD_FILE.write_text(secrets.token_urlsafe(16), encoding="utf-8")
        os.chmod(SCREEN_PASSWORD_FILE, 0o600)
    if not DEVICE_ID_FILE.exists():
        DEVICE_ID_FILE.write_text(secrets.token_hex(16), encoding="utf-8")
        os.chmod(DEVICE_ID_FILE, 0o600)
    if not CERT.exists() or not KEYFILE.exists():
        subprocess.run([
            "openssl", "req", "-x509", "-newkey", "rsa:3072", "-sha256", "-nodes",
            "-keyout", str(KEYFILE), "-out", str(CERT), "-days", "3650",
            "-subj", "/CN=LaptopStatus Linux",
        ], check=True, stdout=subprocess.DEVNULL, stderr=subprocess.DEVNULL)
        os.chmod(KEYFILE, 0o600)
        os.chmod(CERT, 0o644)


def battery() -> tuple[int | None, bool | None]:
    for item in Path("/sys/class/power_supply").glob("*"):
        kind = (item / "type").read_text(errors="ignore").strip() if (item / "type").exists() else ""
        if kind == "Battery":
            try:
                percent = int((item / "capacity").read_text().strip())
                status = (item / "status").read_text(errors="ignore").strip().lower()
                return percent, status in ("charging", "full")
            except Exception:
                pass
    return None, None


def cpu_percent() -> float:
    def sample() -> tuple[int, int]:
        fields = Path("/proc/stat").read_text().splitlines()[0].split()[1:]
        values = [int(value) for value in fields]
        return sum(values), values[3] + (values[4] if len(values) > 4 else 0)
    first = sample()
    time.sleep(0.08)
    second = sample()
    total, idle = second[0] - first[0], second[1] - first[1]
    return round(100 * (total - idle) / total, 1) if total > 0 else 0.0


def status_data() -> dict:
    battery_percent, on_ac = battery()
    mem = {}
    for line in Path("/proc/meminfo").read_text().splitlines():
        if ":" in line:
            name, raw = line.split(":", 1)
            try:
                mem[name] = int(raw.strip().split()[0]) * 1024
            except Exception:
                pass
    total = mem.get("MemTotal", 0)
    available = mem.get("MemAvailable", mem.get("MemFree", 0))
    release = {}
    for line in Path("/etc/os-release").read_text(errors="ignore").splitlines():
        if "=" in line:
            name, value = line.split("=", 1)
            release[name] = value.strip().strip('"')
    return {
        "deviceId": DEVICE_ID_FILE.read_text().strip(),
        "deviceName": platform.node() or "Laptop Linux",
        "manufacturer": "Linux",
        "model": platform.machine(),
        "macAddress": "",
        "online": True,
        "cpuPercent": cpu_percent(),
        "ramPercent": round(100 * (total - available) / total) if total else 0,
        "batteryPercent": battery_percent,
        "onAcPower": on_ac,
        "logicalProcessors": os.cpu_count() or 1,
        "totalPhysicalMemoryBytes": total,
        "osArchitecture": platform.machine(),
        "osVersion": f"{release.get('PRETTY_NAME', 'Linux')} ({platform.release()})",
        "jokeActive": False,
        "androidJokeTriggerActive": False,
        "androidJokeNotificationEnabled": False,
        "screenControlActive": False,
        "screenControlPending": False,
        "screenPasswordAccessEnabled": True,
        "screenPort": 8767,
        "updatedAt": time.strftime("%Y-%m-%dT%H:%M:%S%z"),
    }


def process_list() -> list[dict]:
    rows = []
    for entry in Path("/proc").iterdir():
        if not entry.name.isdigit():
            continue
        try:
            stat = (entry / "stat").read_text()
            name = stat[stat.index("(") + 1:stat.rindex(")")]
            status = (entry / "status").read_text()
            rss_kb = next((int(line.split()[1]) for line in status.splitlines() if line.startswith("VmRSS:")), 0)
            pid = int(entry.name)
            rows.append({"pid": pid, "name": name, "workingSetBytes": rss_kb * 1024,
                         "responding": True, "canTerminate": pid != os.getpid(),
                         "terminationNote": "" if pid != os.getpid() else "LaptopStatus sedang berjalan"})
        except (OSError, ValueError, IndexError):
            continue
    return sorted(rows, key=lambda row: row["workingSetBytes"], reverse=True)[:300]


class Handler(BaseHTTPRequestHandler):
    server_version = "LaptopStatusLinux/0.1"

    def reply(self, code: int, value: dict) -> None:
        body = json.dumps(value, separators=(",", ":")).encode()
        self.send_response(code)
        self.send_header("Content-Type", "application/json; charset=utf-8")
        self.send_header("Content-Length", str(len(body)))
        self.send_header("Cache-Control", "no-store")
        self.end_headers()
        self.wfile.write(body)

    def authorized(self) -> bool:
        supplied = self.headers.get("Authorization", "")
        expected = "Bearer " + TOKEN_FILE.read_text().strip()
        return secrets.compare_digest(supplied, expected)

    def do_GET(self) -> None:
        path = urlsplit(self.path)
        if not self.authorized():
            self.reply(401, {"error": "unauthorized"})
        elif path.path == "/api/status":
            self.reply(200, status_data())
        elif path.path == "/api/processes":
            self.reply(200, {"processes": process_list()})
        elif path.path == "/api/shell/read":
            args = parse_qs(path.query)
            session_id = args.get("sessionId", [""])[0]
            with LOCK:
                session = SHELLS.get(session_id)
                if not session:
                    self.reply(404, {"error": "shell_session_not_found"})
                    return
                proc, output, _ = session
                all_output = "".join(output)
                after = max(0, int(args.get("after", ["0"])[0]))
                self.reply(200, {"sessionId": session_id, "output": all_output[after:],
                                 "exited": proc.poll() is not None, "nextCursor": len(all_output), "truncated": after > len(all_output)})
        else:
            self.reply(404, {"error": "not_found"})

    def do_POST(self) -> None:
        path = urlsplit(self.path)
        if not self.authorized():
            self.reply(401, {"error": "unauthorized"})
            return
        args = parse_qs(path.query)
        if path.path == "/api/process/kill":
            try:
                pid = int(args.get("pid", [""])[0])
                if pid <= 1 or pid == os.getpid():
                    raise ValueError()
                os.kill(pid, signal.SIGTERM)
                self.reply(202, {"ok": True, "status": "terminated"})
            except (ValueError, ProcessLookupError):
                self.reply(400, {"error": "invalid_or_missing_process"})
            except PermissionError:
                self.reply(403, {"error": "permission_denied"})
        elif path.path == "/api/control":
            command = args.get("command", [""])[0]
            actions = {"shutdown": ["systemctl", "poweroff"], "restart": ["systemctl", "reboot"], "sleep": ["systemctl", "suspend"]}
            if command not in actions:
                self.reply(400, {"error": "unsupported_command"})
                return
            subprocess.Popen(actions[command], stdout=subprocess.DEVNULL, stderr=subprocess.DEVNULL)
            self.reply(200, {"ok": True})
        elif path.path == "/api/shell/start":
            body = json.loads(self.rfile.read(int(self.headers.get("Content-Length", "0"))))
            if not secrets.compare_digest(str(body.get("screenPassword", "")), SCREEN_PASSWORD_FILE.read_text().strip()):
                self.reply(403, {"error": "screen_password_required", "message": "Password terminal Linux tidak cocok."})
                return
            shell = os.environ.get("SHELL", "/bin/bash") if body.get("type") != "cmd" else "/bin/sh"
            session_id = secrets.token_urlsafe(18)
            proc = subprocess.Popen([shell, "-i"], stdin=subprocess.PIPE, stdout=subprocess.PIPE,
                                    stderr=subprocess.STDOUT, text=True, bufsize=1)
            output: list[str] = []
            def reader() -> None:
                for line in proc.stdout:
                    output.append(line)
                    if sum(map(len, output)) > 65536:
                        del output[:max(1, len(output) // 2)]
            threading.Thread(target=reader, daemon=True).start()
            with LOCK:
                SHELLS[session_id] = (proc, output, time.time())
            self.reply(200, {"sessionId": session_id, "workingDirectory": str(Path.home())})
        elif path.path == "/api/shell/input":
            body = json.loads(self.rfile.read(int(self.headers.get("Content-Length", "0"))))
            command_text = str(body.get("text", ""))
            session_id = args.get("sessionId", [""])[0]
            with LOCK:
                session = SHELLS.get(session_id)
                if not session:
                    self.reply(404, {"error": "shell_session_not_found"})
                else:
                    session[0].stdin.write(command_text)
                    session[0].stdin.flush()
                    self.reply(200, {"ok": True})
        elif path.path == "/api/shell/stop":
            session_id = args.get("sessionId", [""])[0]
            with LOCK:
                session = SHELLS.pop(session_id, None)
            if session:
                session[0].terminate()
            self.reply(200, {"ok": True})
        else:
            self.reply(404, {"error": "not_found"})

    def log_message(self, fmt: str, *args: object) -> None:
        print(f"{self.client_address[0]} {fmt % args}")


def main() -> None:
    initialize()
    der = subprocess.check_output(["openssl", "x509", "-in", str(CERT), "-outform", "DER"])
    fingerprint = hashlib.sha256(der).hexdigest().upper()
    print("LaptopStatus Linux berjalan; panel Android bisa disambungkan secara manual.")
    print(f"Host: {platform.node()}   Port: {PORT}")
    print(f"Token pairing: {TOKEN_FILE.read_text().strip()}")
    print(f"Fingerprint TLS: {fingerprint}")
    print(f"Password terminal: {SCREEN_PASSWORD_FILE.read_text().strip()}")
    print("Jangan bagikan token atau fingerprint ini kepada orang lain.")
    server = ThreadingHTTPServer(("0.0.0.0", PORT), Handler)
    context = ssl.SSLContext(ssl.PROTOCOL_TLS_SERVER)
    context.minimum_version = ssl.TLSVersion.TLSv1_2
    context.load_cert_chain(CERT, KEYFILE)
    server.socket = context.wrap_socket(server.socket, server_side=True)
    server.serve_forever()


if __name__ == "__main__":
    main()
