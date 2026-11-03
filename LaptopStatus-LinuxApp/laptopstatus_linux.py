#!/usr/bin/env python3
"""Small Linux host for LaptopStatus Android, using only the Python standard library."""

from __future__ import annotations

import hashlib
import ipaddress
import json
import os
import platform
import secrets
import signal
import ssl
import subprocess
import threading
import time
from collections import deque
from dataclasses import dataclass, field
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
LOCK = threading.RLock()
SHELLS: dict[str, "ShellSession"] = {}
MAX_SHELL_SESSIONS = 4
MAX_SHELL_OUTPUT = 65536
SHELL_IDLE_SECONDS = 900
MAX_JSON_BODY = 16 * 1024
MAX_AUTH_FAILURES_PER_MINUTE = 20
AUTH_FAILURES: dict[str, deque[float]] = {}
AUTH_LOCK = threading.Lock()
_cpu_sample: tuple[int, int] | None = None


def is_allowed_peer(client_ip: str) -> bool:
    try:
        address = ipaddress.ip_address(client_ip)
        tailscale_peer = address.version == 4 and address in ipaddress.ip_network("100.64.0.0/10")
        return address.is_loopback or address.is_private or tailscale_peer
    except (ValueError, TypeError):
        return False


@dataclass
class ShellSession:
    process: subprocess.Popen
    created_at: float = field(default_factory=time.monotonic)
    last_activity: float = field(default_factory=time.monotonic)
    output: deque[str] = field(default_factory=deque)
    output_size: int = 0
    output_start: int = 0
    output_lock: threading.Lock = field(default_factory=threading.Lock)

    def append(self, line: str) -> None:
        text = line
        with self.output_lock:
            self.output.append(text)
            self.output_size += len(text)
            while self.output_size > MAX_SHELL_OUTPUT and self.output:
                excess = self.output_size - MAX_SHELL_OUTPUT
                first = self.output[0]
                if len(first) <= excess:
                    self.output.popleft()
                    self.output_size -= len(first)
                    self.output_start += len(first)
                else:
                    self.output[0] = first[excess:]
                    self.output_size -= excess
                    self.output_start += excess

    def read_after(self, cursor: int) -> tuple[str, int, bool]:
        with self.output_lock:
            end = self.output_start + self.output_size
            actual_cursor = min(max(cursor, self.output_start), end)
            combined = "".join(self.output)
            text = combined[actual_cursor - self.output_start:]
            self.last_activity = time.monotonic()
            return text, actual_cursor + len(text), cursor < self.output_start


def stop_shell_process(session: ShellSession) -> None:
    try:
        os.killpg(session.process.pid, signal.SIGTERM)
        session.process.wait(timeout=2)
    except subprocess.TimeoutExpired:
        try:
            os.killpg(session.process.pid, signal.SIGKILL)
        except OSError:
            pass
        try:
            session.process.wait(timeout=2)
        except subprocess.TimeoutExpired:
            pass
    except OSError:
        if session.process.poll() is None:
            try:
                session.process.terminate()
                session.process.wait(timeout=2)
            except (OSError, subprocess.TimeoutExpired):
                pass


def cleanup_shells() -> None:
    now = time.monotonic()
    with LOCK:
        expired_ids = [session_id for session_id, session in SHELLS.items()
                       if now - session.last_activity > SHELL_IDLE_SECONDS]
        expired = [SHELLS.pop(session_id) for session_id in expired_ids]
    for session in expired:
        stop_shell_process(session)


def run(*args: str) -> str:
    try:
        return subprocess.check_output(args, text=True, stderr=subprocess.DEVNULL, timeout=3).strip()
    except Exception:
        return ""


def write_private_file(path: Path, value: str) -> None:
    previous_umask = os.umask(0o077)
    try:
        path.write_text(value, encoding="utf-8")
    finally:
        os.umask(previous_umask)
    os.chmod(path, 0o600)


def initialize() -> None:
    APP_DIR.mkdir(parents=True, exist_ok=True)
    os.chmod(APP_DIR, 0o700)
    if not TOKEN_FILE.exists():
        write_private_file(TOKEN_FILE, secrets.token_urlsafe(32))
    os.chmod(TOKEN_FILE, 0o600)
    if not SCREEN_PASSWORD_FILE.exists():
        write_private_file(SCREEN_PASSWORD_FILE, secrets.token_urlsafe(16))
    os.chmod(SCREEN_PASSWORD_FILE, 0o600)
    if not DEVICE_ID_FILE.exists():
        write_private_file(DEVICE_ID_FILE, secrets.token_hex(16))
    os.chmod(DEVICE_ID_FILE, 0o600)
    if not CERT.exists() or not KEYFILE.exists():
        previous_umask = os.umask(0o077)
        try:
            subprocess.run([
                "openssl", "req", "-x509", "-newkey", "ec", "-pkeyopt", "ec_paramgen_curve:prime256v1", "-sha256", "-nodes",
                "-keyout", str(KEYFILE), "-out", str(CERT), "-days", "3650",
                "-subj", "/CN=LaptopStatus Linux",
            ], check=True, stdout=subprocess.DEVNULL, stderr=subprocess.DEVNULL)
        finally:
            os.umask(previous_umask)
    if KEYFILE.exists():
        os.chmod(KEYFILE, 0o600)
    if CERT.exists():
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
    global _cpu_sample
    def sample() -> tuple[int, int]:
        fields = Path("/proc/stat").read_text().splitlines()[0].split()[1:]
        values = [int(value) for value in fields]
        return sum(values), values[3] + (values[4] if len(values) > 4 else 0)
    current = sample()
    previous = _cpu_sample
    _cpu_sample = current
    if previous is None:
        return 0.0
    total, idle = current[0] - previous[0], current[1] - previous[1]
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
            statm = (entry / "statm").read_text().split()
            rss_bytes = int(statm[1]) * os.sysconf("SC_PAGE_SIZE") if len(statm) > 1 else 0
            pid = int(entry.name)
            rows.append({"pid": pid, "name": name, "workingSetBytes": rss_bytes,
                         "responding": True, "canTerminate": pid != os.getpid(),
                         "terminationNote": "" if pid != os.getpid() else "LaptopStatus sedang berjalan"})
        except (OSError, ValueError, IndexError):
            continue
    return sorted(rows, key=lambda row: row["workingSetBytes"], reverse=True)[:300]


class Handler(BaseHTTPRequestHandler):
    server_version = "LaptopStatusLinux"
    sys_version = ""

    def setup(self) -> None:
        super().setup()
        self.connection.settimeout(10)

    def reply(self, code: int, value: dict) -> None:
        body = json.dumps(value, separators=(",", ":")).encode()
        self.send_response(code)
        self.send_header("Content-Type", "application/json; charset=utf-8")
        self.send_header("Content-Length", str(len(body)))
        self.send_header("Cache-Control", "no-store")
        self.end_headers()
        self.wfile.write(body)

    def authorized(self) -> bool:
        client_ip = self.client_address[0]
        if not is_allowed_peer(client_ip):
            return False
        now = time.monotonic()
        with AUTH_LOCK:
            attempts = AUTH_FAILURES.get(client_ip)
            if attempts is None:
                if len(AUTH_FAILURES) >= 4096:
                    AUTH_FAILURES.pop(next(iter(AUTH_FAILURES)))
                attempts = AUTH_FAILURES.setdefault(client_ip, deque())
            while attempts and now - attempts[0] >= 60:
                attempts.popleft()
            if len(attempts) >= MAX_AUTH_FAILURES_PER_MINUTE:
                return False
        supplied = self.headers.get("Authorization", "")
        expected = "Bearer " + TOKEN_FILE.read_text().strip()
        valid = secrets.compare_digest(supplied, expected)
        with AUTH_LOCK:
            if valid:
                AUTH_FAILURES.pop(client_ip, None)
            else:
                attempts = AUTH_FAILURES.get(client_ip)
                if attempts is None:
                    if len(AUTH_FAILURES) >= 4096:
                        AUTH_FAILURES.pop(next(iter(AUTH_FAILURES)))
                    attempts = AUTH_FAILURES.setdefault(client_ip, deque())
                attempts.append(now)
        return valid

    def read_json_body(self, maximum: int = MAX_JSON_BODY) -> dict | None:
        try:
            length = int(self.headers.get("Content-Length", "0"))
        except ValueError:
            self.reply(400, {"error": "invalid_content_length"})
            return None
        if length <= 0:
            self.reply(400, {"error": "empty_request_body"})
            return None
        if length > maximum:
            self.reply(413, {"error": "request_body_too_large"})
            return None
        try:
            raw = self.rfile.read(length)
            if len(raw) != length:
                self.reply(400, {"error": "incomplete_request_body"})
                return None
            value = json.loads(raw)
            if not isinstance(value, dict):
                raise ValueError("JSON object required")
            return value
        except (json.JSONDecodeError, UnicodeDecodeError, ValueError):
            self.reply(400, {"error": "invalid_json"})
            return None

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
            cleanup_shells()
            with LOCK:
                session = SHELLS.get(session_id)
                if not session:
                    self.reply(404, {"error": "shell_session_not_found"})
                    return
                after = max(0, int(args.get("after", ["0"])[0]))
                output, next_cursor, truncated = session.read_after(after)
                self.reply(200, {"sessionId": session_id, "output": output,
                                 "exited": session.process.poll() is not None, "nextCursor": next_cursor, "truncated": truncated})
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
            body = self.read_json_body(4096)
            if body is None:
                return
            if not secrets.compare_digest(str(body.get("screenPassword", "")), SCREEN_PASSWORD_FILE.read_text().strip()):
                self.reply(403, {"error": "screen_password_required", "message": "Password terminal Linux tidak cocok."})
                return
            shell = os.environ.get("SHELL", "/bin/bash") if body.get("type") != "cmd" else "/bin/sh"
            cleanup_shells()
            with LOCK:
                if len(SHELLS) >= MAX_SHELL_SESSIONS:
                    self.reply(429, {"error": "too_many_shell_sessions"})
                    return
                proc = subprocess.Popen([shell, "-i"], stdin=subprocess.PIPE, stdout=subprocess.PIPE,
                                        stderr=subprocess.STDOUT, text=True, bufsize=1, start_new_session=True)
                session = ShellSession(proc)
                session_id = secrets.token_urlsafe(18)
                SHELLS[session_id] = session
            def reader() -> None:
                for line in proc.stdout:
                    session.append(line)
            threading.Thread(target=reader, daemon=True).start()
            self.reply(200, {"sessionId": session_id, "workingDirectory": str(Path.home())})
        elif path.path == "/api/shell/input":
            body = self.read_json_body(4096)
            if body is None:
                return
            command_text = str(body.get("text", ""))
            session_id = args.get("sessionId", [""])[0]
            with LOCK:
                session = SHELLS.get(session_id)
                if not session:
                    self.reply(404, {"error": "shell_session_not_found"})
                elif len(command_text) > 4096:
                    self.reply(413, {"error": "shell_input_too_large"})
                else:
                    try:
                        session.process.stdin.write(command_text)
                        session.process.stdin.flush()
                        session.last_activity = time.monotonic()
                        self.reply(200, {"ok": True})
                    except (OSError, AttributeError):
                        self.reply(409, {"error": "shell_session_ended"})
        elif path.path == "/api/shell/stop":
            session_id = args.get("sessionId", [""])[0]
            with LOCK:
                session = SHELLS.pop(session_id, None)
            if session:
                stop_shell_process(session)
            self.reply(200, {"ok": True})
        else:
            self.reply(404, {"error": "not_found"})

    def log_message(self, fmt: str, *args: object) -> None:
        print(f"{self.client_address[0]} {fmt % args}")


class LimitedThreadingHTTPServer(ThreadingHTTPServer):
    daemon_threads = True
    request_queue_size = 32

    def __init__(self, server_address, request_handler):
        self._request_slots = threading.BoundedSemaphore(32)
        self.tls_context: ssl.SSLContext | None = None
        super().__init__(server_address, request_handler)

    def get_request(self):
        request, client_address = super().get_request()
        if self.tls_context is not None:
            request = self.tls_context.wrap_socket(request, server_side=True, do_handshake_on_connect=False)
        return request, client_address

    def verify_request(self, request, client_address):
        return is_allowed_peer(client_address[0])

    def process_request(self, request, client_address):
        if not self._request_slots.acquire(blocking=False):
            request.close()
            return
        try:
            super().process_request(request, client_address)
        except Exception:
            self._request_slots.release()
            raise

    def process_request_thread(self, request, client_address):
        try:
            super().process_request_thread(request, client_address)
        finally:
            self._request_slots.release()


def cleanup_shells_loop() -> None:
    while True:
        time.sleep(60)
        cleanup_shells()


def stop_all_shells() -> None:
    with LOCK:
        sessions = list(SHELLS.values())
        SHELLS.clear()
    for session in sessions:
        stop_shell_process(session)


def main() -> None:
    initialize()
    der = subprocess.check_output(["openssl", "x509", "-in", str(CERT), "-outform", "DER"])
    fingerprint = hashlib.sha256(der).hexdigest().upper()
    bind_address = os.environ.get("LAPTOPSTATUS_BIND", "0.0.0.0")
    print("LaptopStatus Linux berjalan; buka menu tray untuk melihat informasi pairing.")
    print(f"Host: {platform.node()}   Bind: {bind_address}   Port: {PORT}")
    print(f"Fingerprint TLS: {fingerprint}")
    threading.Thread(target=cleanup_shells_loop, daemon=True).start()
    server = LimitedThreadingHTTPServer((bind_address, PORT), Handler)
    context = ssl.SSLContext(ssl.PROTOCOL_TLS_SERVER)
    context.minimum_version = ssl.TLSVersion.TLSv1_2
    context.load_cert_chain(CERT, KEYFILE)
    server.tls_context = context
    signal.signal(signal.SIGTERM, lambda _signum, _frame: threading.Thread(target=server.shutdown, daemon=True).start())
    try:
        server.serve_forever()
    finally:
        server.server_close()
        stop_all_shells()


if __name__ == "__main__":
    main()
