using System;
using System.Collections.Generic;

namespace LaptopStatus
{
    /// <summary>
    /// Membatasi percobaan pairing (/api/pair) supaya endpoint yang terbuka lewat Cloudflare Tunnel
    /// tidak bisa dipakai untuk membanjiri jendela persetujuan di laptop atau menebak kode.
    ///
    /// Aturan:
    ///  1. Hanya satu permintaan pairing yang boleh menunggu persetujuan pada satu waktu.
    ///  2. Setiap klien (alamat IP) dibatasi N percobaan per jendela waktu.
    ///  3. Ada batas global lintas klien (menahan penyerang yang berganti IP).
    ///  4. Beberapa kegagalan berturut-turut (ditolak pemilik / format salah) mengunci klien itu sementara.
    /// </summary>
    internal sealed class PairingRateLimiter
    {
        public enum Decision { Allowed, TooManyAttempts, LockedOut, Busy }

        private sealed class ClientState
        {
            public readonly Queue<DateTimeOffset> Attempts = new();
            public readonly Queue<DateTimeOffset> Failures = new();
            public DateTimeOffset LockedUntil = DateTimeOffset.MinValue;
        }

        private readonly object _gate = new();
        private readonly Func<DateTimeOffset> _now;
        private readonly int _maxAttemptsPerClient;
        private readonly int _maxAttemptsGlobal;
        private readonly int _failuresBeforeLockout;
        private readonly TimeSpan _window;
        private readonly TimeSpan _lockout;
        private readonly Dictionary<string, ClientState> _clients = new(StringComparer.OrdinalIgnoreCase);
        private readonly Queue<DateTimeOffset> _globalAttempts = new();
        private bool _approvalInFlight;

        public PairingRateLimiter(
            Func<DateTimeOffset>? clock = null,
            int maxAttemptsPerClient = 5,
            int maxAttemptsGlobal = 20,
            int failuresBeforeLockout = 3,
            TimeSpan? window = null,
            TimeSpan? lockout = null)
        {
            _now = clock ?? (() => DateTimeOffset.UtcNow);
            _maxAttemptsPerClient = maxAttemptsPerClient;
            _maxAttemptsGlobal = maxAttemptsGlobal;
            _failuresBeforeLockout = failuresBeforeLockout;
            _window = window ?? TimeSpan.FromMinutes(10);
            _lockout = lockout ?? TimeSpan.FromMinutes(15);
        }

        /// <summary>
        /// Panggil sebelum memproses permintaan pairing. Jika hasilnya Allowed, pemanggil WAJIB memanggil
        /// <see cref="End"/> (gunakan try/finally) agar slot persetujuan dilepas.
        /// </summary>
        public Decision TryBegin(string clientKey, out TimeSpan retryAfter)
        {
            lock (_gate)
            {
                var now = _now();
                PruneQueue(_globalAttempts, now);

                // Tolak lebih dulu agar permintaan IP baru yang sudah terkena batas global
                // tidak membuat entri ClientState tanpa batas di memori.
                if (_approvalInFlight)
                {
                    retryAfter = TimeSpan.FromSeconds(10);
                    return Decision.Busy;
                }

                var hasState = _clients.TryGetValue(clientKey, out var existingState);
                if (!hasState && _globalAttempts.Count >= _maxAttemptsGlobal)
                {
                    retryAfter = _globalAttempts.Peek() + _window - now;
                    return Decision.TooManyAttempts;
                }
                var state = hasState ? existingState! : GetState(clientKey, now);
                PruneQueue(state.Attempts, now);
                if (state.LockedUntil > now)
                {
                    retryAfter = state.LockedUntil - now;
                    return Decision.LockedOut;
                }


                if (state.Attempts.Count >= _maxAttemptsPerClient)
                {
                    retryAfter = state.Attempts.Peek() + _window - now;
                    return Decision.TooManyAttempts;
                }

                if (_globalAttempts.Count >= _maxAttemptsGlobal)
                {
                    retryAfter = _globalAttempts.Peek() + _window - now;
                    return Decision.TooManyAttempts;
                }

                state.Attempts.Enqueue(now);
                _globalAttempts.Enqueue(now);
                _approvalInFlight = true;
                retryAfter = TimeSpan.Zero;
                return Decision.Allowed;
            }
        }

        /// <summary>Lepas slot persetujuan dan catat hasilnya.</summary>
        public void End(string clientKey, bool succeeded)
        {
            lock (_gate)
            {
                _approvalInFlight = false;
                var now = _now();
                var state = GetState(clientKey, now);
                if (succeeded)
                {
                    state.Failures.Clear();
                    return;
                }

                PruneQueue(state.Failures, now);
                state.Failures.Enqueue(now);
                if (state.Failures.Count >= _failuresBeforeLockout)
                {
                    state.LockedUntil = now + _lockout;
                    state.Failures.Clear();
                }
            }
        }

        private ClientState GetState(string clientKey, DateTimeOffset now)
        {
            if (_clients.Count > 2000) PruneClients(now);
            if (!_clients.TryGetValue(clientKey, out var state))
            {
                state = new ClientState();
                _clients[clientKey] = state;
            }
            return state;
        }

        private void PruneQueue(Queue<DateTimeOffset> queue, DateTimeOffset now)
        {
            while (queue.Count > 0 && now - queue.Peek() >= _window) queue.Dequeue();
        }

        private void PruneClients(DateTimeOffset now)
        {
            var stale = new List<string>();
            foreach (var pair in _clients)
            {
                PruneQueue(pair.Value.Attempts, now);
                PruneQueue(pair.Value.Failures, now);
                if (pair.Value.Attempts.Count == 0 && pair.Value.Failures.Count == 0 && pair.Value.LockedUntil <= now)
                    stale.Add(pair.Key);
            }
            foreach (var key in stale) _clients.Remove(key);
        }
    }
}
