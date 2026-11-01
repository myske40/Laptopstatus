(() => {
  const segment = document.querySelector('#sg2');
  const page = document.querySelector('#p2');
  if (!segment || !page || page.querySelector('[data-t="4"]')) return;

  const style = document.createElement('style');
  style.textContent = `
    .remote-shell-card{display:flex;flex-direction:column;gap:12px}
    .remote-shell-toolbar{display:flex;gap:8px;align-items:center}
    .remote-shell-toolbar select,.remote-shell-input{min-width:0;flex:1;background:var(--s);color:var(--tx);border:1px solid var(--ln);border-radius:13px;padding:12px;font:inherit}
    .remote-shell-output{box-sizing:border-box;min-height:34vh;max-height:62vh;overflow:auto;margin:0;padding:16px;border:1px solid var(--ln);border-radius:15px;background:#070d15;color:#d8e4ee;font:14px/1.55 ui-monospace,Consolas,monospace;white-space:pre-wrap;overflow-wrap:anywhere;user-select:text;-webkit-user-select:text}
    .remote-shell-entry{display:flex;gap:8px;align-items:center}
    .remote-shell-stop{background:#38232b!important;color:#ff9eaa!important}
  `;
  document.head.append(style);

  const button = document.createElement('button');
  button.type = 'button';
  button.innerHTML = '<svg viewBox="0 0 24 24" aria-hidden="true"><path d="m7 8 4 4-4 4m6 0h5"/></svg>Terminal';
  segment.append(button);
  segment.style.setProperty('--n', '5');

  const panel = document.createElement('section');
  panel.className = 'c remote-shell-card';
  panel.dataset.t = '4';
  panel.style.setProperty('--i', '4');
  panel.style.display = 'none';
  panel.innerHTML = `
    <div class="session-log-head"><b>Terminal Windows</b><span class="session-log-live"><i></i>SIAP</span></div>
    <div class="remote-shell-toolbar">
      <select id="remoteShellType" aria-label="Pilih jenis shell"><option value="powershell">PowerShell</option><option value="cmd">Command Prompt</option></select>
      <button class="act" id="remoteShellStart" type="button">Mulai</button>
      <button class="act remote-shell-stop" id="remoteShellStop" type="button" disabled>Hentikan</button>
    </div>
    <pre class="remote-shell-output" id="remoteShellOutput" role="log" aria-live="polite">Terminal belum dimulai. Pilih PowerShell atau Command Prompt lalu tekan Mulai.</pre>
    <div class="remote-shell-entry"><input class="remote-shell-input" id="remoteShellInput" type="text" autocomplete="off" autocapitalize="off" spellcheck="false" placeholder="Ketik perintah Windows" disabled><button class="act" id="remoteShellSend" type="button" disabled>Jalankan</button></div>
    <small id="remoteShellStatus" hidden></small>
  `;
  page.append(panel);

  const $ = (selector) => panel.querySelector(selector);
  const output = $('#remoteShellOutput');
  const status = $('#remoteShellStatus');
  const start = $('#remoteShellStart');
  const stop = $('#remoteShellStop');
  const type = $('#remoteShellType');
  const input = $('#remoteShellInput');
  const send = $('#remoteShellSend');
  let sessionId = '';

  function setRunning(running) {
    start.disabled = running;
    type.disabled = running;
    stop.disabled = !running;
    input.disabled = !running;
    send.disabled = !running;
    const badge = panel.querySelector('.session-log-live');
    if (badge) badge.lastChild.textContent = running ? 'TERHUBUNG' : 'SIAP';
  }

  function appendText(text) {
    if (!text) return;
    output.textContent += text;
    if (output.textContent.length > 96000) output.textContent = output.textContent.slice(-96000);
    output.scrollTop = output.scrollHeight;
  }

  start.addEventListener('click', () => {
    if (!window.Android?.startRemoteShell) {
      status.textContent = 'Terminal Windows hanya tersedia di aplikasi Android yang terpasang.';
      return;
    }
    output.textContent = '';
    status.textContent = 'Menghubungkan terminal dengan laptop…';
    start.disabled = true;
    window.Android.startRemoteShell(type.value);
  });

  function sendLine() {
    const line = input.value;
    if (!sessionId || !line.trim()) return;
    if (!window.Android?.writeRemoteShell) return;
    window.Android.writeRemoteShell(line + '\n');
    input.value = '';
  }
  send.addEventListener('click', sendLine);
  input.addEventListener('keydown', (event) => {
    if (event.key === 'Enter') {
      event.preventDefault();
      sendLine();
    }
  });
  stop.addEventListener('click', () => {
    if (sessionId && window.Android?.stopRemoteShell) window.Android.stopRemoteShell();
  });

  window.LaptopStatus = window.LaptopStatus || {};
  window.LaptopStatus.remoteShellStarted = (id, shellType, workingDirectory) => {
    sessionId = String(id || '');
    if (!sessionId) return window.LaptopStatus.remoteShellError('ID sesi terminal tidak diterima.');
    setRunning(true);
    status.textContent = `${shellType === 'cmd' ? 'Command Prompt' : 'PowerShell'} berjalan di ${workingDirectory} dengan hak akun Windows biasa.`;
    input.focus();
  };
  window.LaptopStatus.remoteShellStarting = () => {
    start.disabled = true;
    status.textContent = 'Membuka shell Windows dengan hak pengguna biasa…';
  };
  window.LaptopStatus.remoteShellOutput = (text, truncated) => {
    if (truncated) output.textContent = '[Keluaran lama dipangkas agar memori tetap ringan.]\n' + output.textContent;
    appendText(String(text || ''));
  };
  window.LaptopStatus.remoteShellError = (message) => {
    start.disabled = false;
    status.textContent = String(message || 'Terminal gagal dijalankan.');
  };
  window.LaptopStatus.remoteShellNotice = (message) => {
    status.textContent = String(message || '');
  };
  window.LaptopStatus.remoteShellEnded = (message) => {
    sessionId = '';
    setRunning(false);
    status.textContent = String(message || 'Sesi terminal selesai.');
    appendText('\n[Sesi terminal berakhir]\n');
  };
  window.LaptopStatus.remoteShellStopped = () => {
    sessionId = '';
    setRunning(false);
    status.textContent = 'Terminal dihentikan.';
    appendText('\n[Terminal dihentikan]\n');
  };
})();
