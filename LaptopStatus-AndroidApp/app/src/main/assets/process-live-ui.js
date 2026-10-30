(function () {
  'use strict';
  const $ = (selector, root = document) => root.querySelector(selector);
  const COLORS = ['#5ee6b0', '#4dc3d9', '#7b9cff', '#f2c56b', '#ff8f8f'];
  const state = { processes: [], memory: {}, query: '', filter: 'all', sort: 'ram', status: 'Belum dimuat', drawerPid: null, drawerType: '' };
  const safe = value => String(value == null ? '' : value);
  const call = event => {
    try { if (window.Android && typeof window.Android.logSessionActivity === 'function') window.Android.logSessionActivity(event); } catch (_) { }
  };
  const mb = bytes => Math.max(0, Number(bytes) || 0) / 1048576;
  const formatMb = value => value >= 1024
    ? (value / 1024).toLocaleString('id-ID', { minimumFractionDigits: 1, maximumFractionDigits: 1 })
    : Math.round(value).toLocaleString('id-ID');
  const hue = name => { let hash = 0; for (let i = 0; i < name.length; i++) hash = (hash * 31 + name.charCodeAt(i)) % 360; return hash; };
  const processes = () => state.processes;

  function updateSummary() {
    const processTotalBytes = processes().reduce((sum, item) => sum + Math.max(0, Number(item.memoryBytes) || 0), 0);
    const processTotal = processTotalBytes / 1048576;
    const physicalTotal = Math.max(0, Number(state.memory.totalBytes) || 0);
    const physicalAvailable = Math.min(physicalTotal, Math.max(0, Number(state.memory.availableBytes) || 0));
    const hasPhysicalMemory = physicalTotal > 0;
    const physicalUsed = hasPhysicalMemory ? Math.min(physicalTotal, Math.max(0, physicalTotal - physicalAvailable)) : 0;
    const processScale = hasPhysicalMemory && processTotalBytes > physicalUsed && processTotalBytes > 0
      ? physicalUsed / processTotalBytes : 1;
    const totalNode = $('#processTotal'), unitNode = $('#processUnit'), stack = $('#processStack'), legend = $('#processLegend');
    let physicalStatus = $('#processPhysicalStatus');
    if (!physicalStatus) {
      physicalStatus = document.createElement('small');
      physicalStatus.id = 'processPhysicalStatus';
      physicalStatus.className = 'proc-updated';
      const updated = $('#processUpdated');
      if (updated) updated.insertAdjacentElement('beforebegin', physicalStatus);
    }
    if (totalNode) totalNode.textContent = formatMb(processTotal);
    if (unitNode) unitNode.textContent = processTotal >= 1024 ? 'GB' : 'MB';
    if (physicalStatus) {
      if (hasPhysicalMemory) {
        const gib = value => (value / 1073741824).toLocaleString('id-ID', { minimumFractionDigits: 1, maximumFractionDigits: 1 });
        const percent = Math.round(physicalUsed * 100 / physicalTotal);
        physicalStatus.textContent = `RAM fisik ${percent}% dipakai · ${gib(physicalUsed)} dari ${gib(physicalTotal)} GB · tersedia ${gib(physicalAvailable)} GB`;
      } else physicalStatus.textContent = 'Menunggu data RAM fisik dari Windows…';
    }
    if (!stack || !legend) return;
    const sorted = processes().slice().sort((a, b) => mb(b.memoryBytes) - mb(a.memoryBytes));
    const top = sorted.slice(0, 5).map((item, index) => ({ key: `pid:${item.pid}`, name: item.name, bytes: Math.max(0, Number(item.memoryBytes) || 0), color: COLORS[index] }));
    const topBytes = top.reduce((sum, item) => sum + item.bytes, 0);
    const restBytes = Math.max(0, processTotalBytes - topBytes);
    const segments = top.map(item => ({ ...item, value: item.bytes * processScale }));
    if (restBytes > 0) segments.push({ key: 'other-processes', name: 'Proses lain', bytes: restBytes, value: restBytes * processScale, color: '#526476' });
    if (hasPhysicalMemory) {
      const attributedProcessBytes = Math.min(physicalUsed, processTotalBytes * processScale);
      const windowsBytes = Math.max(0, physicalUsed - attributedProcessBytes);
      if (windowsBytes > 0) segments.push({ key: 'windows-shared', name: 'Windows / memori bersama', bytes: windowsBytes, value: windowsBytes, color: '#344752' });
      if (physicalAvailable > 0) segments.push({ key: 'available', name: 'Tersedia', bytes: physicalAvailable, value: physicalAvailable, color: '#1d2a36' });
    }
    legend.replaceChildren();
    stack.title = hasPhysicalMemory
      ? 'Skala bar = kapasitas RAM fisik Windows: proses terlapor, Windows/memori bersama, lalu RAM tersedia.'
      : 'Menunggu data kapasitas RAM fisik dari Windows.';
    if (!segments.length) { stack.replaceChildren(); return; }
    const chartTotal = hasPhysicalMemory ? physicalTotal : processTotalBytes;
    const previousNodes = new Map(Array.from(stack.children).map(node => [node.dataset.segmentKey, node]));
    const retained = new Set();
    segments.forEach(segment => {
      let bar = previousNodes.get(segment.key);
      const isNew = !bar;
      if (!bar) {
        bar = document.createElement('i');
        bar.dataset.segmentKey = segment.key;
        bar.style.flex = '0 0 0%';
      }
      bar.style.background = segment.color;
      const percentOfChart = chartTotal > 0 ? Math.max(0, Math.min(100, segment.value * 100 / chartTotal)) : 0;
      bar.title = `${segment.name}: ${formatMb(segment.bytes / 1048576)} ${segment.bytes >= 1073741824 ? 'GB' : 'MB'}${segment.bytes !== segment.value ? ' (diskalakan agar sesuai RAM fisik)' : ''}`;
      stack.append(bar);
      retained.add(segment.key);
      if (isNew) {
        void bar.offsetWidth;
        requestAnimationFrame(() => { bar.style.flex = `0 0 ${percentOfChart}%`; });
      } else bar.style.flex = `0 0 ${percentOfChart}%`;
      const label = document.createElement('span'), swatch = document.createElement('b'), name = document.createElement('span'), percent = document.createElement('em');
      swatch.style.background = segment.color;
      name.textContent = segment.name;
      percent.textContent = `${Math.round(percentOfChart)}%`;
      percent.title = `${formatMb(segment.bytes / 1048576)} ${segment.bytes >= 1073741824 ? 'GB' : 'MB'} dilaporkan`;
      label.append(swatch, name, percent); legend.append(label);
    });
    previousNodes.forEach((node, key) => { if (!retained.has(key)) node.remove(); });
  }

  function setProcessMemory(raw) {
    try { state.memory = typeof raw === 'string' ? JSON.parse(raw) : (raw || {}); }
    catch (_) { state.memory = {}; }
    updateSummary();
  }

  function visibleProcesses() {
    const query = state.query.trim().toLocaleLowerCase();
    return processes().filter(item => {
      if (state.filter === 'kill' && !item.canTerminate) return false;
      if (state.filter === 'safe' && item.canTerminate) return false;
      return !query || item.name.toLocaleLowerCase().includes(query) || String(item.pid).includes(query);
    }).sort((a, b) => state.sort === 'ram'
      ? mb(b.memoryBytes) - mb(a.memoryBytes) || a.name.localeCompare(b.name)
      : a.name.localeCompare(b.name) || mb(b.memoryBytes) - mb(a.memoryBytes));
  }

  function openDrawer(row, type) {
    const pid = Number(row.dataset.pid), same = state.drawerPid === pid && state.drawerType === type;
    closeDrawer();
    if (same) return;
    const item = processes().find(process => process.pid === pid);
    const slot = $('.proc-panel-slot', row);
    if (!item || !slot) return;
    const panel = document.createElement('div');
    if (type === 'confirm') {
      panel.className = 'proc-panel confirm';
      const question = document.createElement('div');
      question.textContent = `Hentikan ${item.name} (PID ${item.pid})? Sekitar ${formatMb(mb(item.memoryBytes))} ${mb(item.memoryBytes) >= 1024 ? 'GB' : 'MB'} RAM dapat dibebaskan. Pekerjaan yang belum disimpan bisa hilang.`;
      const actions = document.createElement('div'); actions.className = 'proc-panel-actions';
      const cancel = document.createElement('button'); cancel.type = 'button'; cancel.dataset.procAction = 'cancel'; cancel.textContent = 'Batal';
      const yes = document.createElement('button'); yes.type = 'button'; yes.className = 'confirm-yes'; yes.dataset.procAction = 'confirm'; yes.textContent = 'Ya, hentikan';
      actions.append(cancel, yes); panel.append(question, actions);
    } else {
      panel.className = 'proc-panel reason';
      panel.textContent = item.note || 'Windows melindungi proses inti ini.';
    }
    slot.replaceChildren(panel); row.classList.add('open');
    state.drawerPid = pid; state.drawerType = type;
  }

  function closeDrawer() {
    document.querySelectorAll('.proc-row.open').forEach(row => row.classList.remove('open'));
    document.querySelectorAll('.proc-panel-slot').forEach(slot => slot.replaceChildren());
    state.drawerPid = null; state.drawerType = '';
  }

  function renderProcesses(animate = false) {
    const root = $('#pl'), count = $('#processCount'), status = $('#processStatus');
    if (!root) return;
    const list = visibleProcesses();
    if (count) count.textContent = `${list.length} / ${processes().length}`;
    if (status) {
      const suffix = state.status.startsWith('Memuat') ? state.status : processes().length
        ? `${list.length} dari ${processes().length} proses${state.status.startsWith('Diperbarui') ? ` · ${state.status}` : ''}`
        : state.status;
      status.textContent = suffix;
    }
    const filterCounts = {
      all: processes().length,
      kill: processes().filter(item => item.canTerminate).length,
      safe: processes().filter(item => !item.canTerminate).length
    };
    document.querySelectorAll('[data-process-filter]').forEach(button => {
      const key = button.dataset.processFilter;
      button.setAttribute('aria-pressed', String(key === state.filter));
      const countNode = $('i', button); if (countNode) countNode.textContent = String(filterCounts[key] || 0);
    });
    closeDrawer(); root.replaceChildren();
    if (!list.length) {
      const empty = document.createElement('div'); empty.className = 'proc-empty';
      empty.textContent = processes().length ? 'Tidak ada proses yang cocok. Ubah pencarian atau filter.' : (state.status || 'Belum ada proses. Segarkan daftar setelah laptop terhubung.');
      root.append(empty); updateSummary(); return;
    }
    const maxRam = Math.max(1, ...processes().map(item => mb(item.memoryBytes)));
    list.forEach((item, index) => {
      const row = document.createElement('article'); row.className = `proc-row${animate ? ' enter' : ''}`; row.dataset.pid = String(item.pid); row.style.setProperty('--proc-i', String(index));
      const main = document.createElement('div'); main.className = 'proc-row-main';
      const avatar = document.createElement('div'); avatar.className = 'proc-avatar'; avatar.setAttribute('aria-hidden', 'true');
      avatar.style.background = `hsl(${hue(item.name)} 38% 20%)`; avatar.style.color = `hsl(${hue(item.name)} 78% 77%)`;
      const iconData = safe(item.iconBase64).replace(/\s/g, '');
      if (iconData.length > 0 && iconData.length <= 131072 && /^[A-Za-z0-9+/]+={0,2}$/.test(iconData)) {
        const icon = document.createElement('img'); icon.alt = ''; icon.loading = 'lazy'; icon.decoding = 'async';
        icon.src = `data:image/png;base64,${iconData}`;
        icon.addEventListener('error', () => { icon.remove(); avatar.textContent = item.name.trim().charAt(0).toLocaleUpperCase() || '?'; }, { once: true });
        avatar.append(icon);
      } else avatar.textContent = item.name.trim().charAt(0).toLocaleUpperCase() || '?';
      const info = document.createElement('div'); info.className = 'proc-info';
      const name = document.createElement('span'); name.className = 'proc-name'; name.textContent = item.name;
      const pid = document.createElement('small'); pid.className = 'proc-pid'; pid.textContent = `PID ${item.pid}`;
      const bar = document.createElement('div'); bar.className = 'proc-bar';
      const fill = document.createElement('i'); fill.classList.toggle('hot', mb(item.memoryBytes) > 300); fill.style.setProperty('--proc-width', `${Math.max(2, mb(item.memoryBytes) / maxRam * 100)}%`); bar.append(fill);
      info.append(name, pid, bar);
      const side = document.createElement('div'); side.className = 'proc-side';
      const memory = document.createElement('span'); memory.className = 'proc-ram';
      const mem = mb(item.memoryBytes); memory.textContent = formatMb(mem); const unit = document.createElement('small'); unit.textContent = mem >= 1024 ? ' GB' : ' MB'; memory.append(unit);
      const action = document.createElement('button'); action.type = 'button'; action.className = `proc-action ${item.canTerminate ? 'kill' : 'lock'}`;
      action.dataset.procAction = item.canTerminate ? 'ask' : 'reason'; action.textContent = item.canTerminate ? 'Hentikan' : 'Dilindungi';
      side.append(memory, action); main.append(avatar, info, side); row.append(main);
      const drawer = document.createElement('div'); drawer.className = 'proc-drawer'; const inner = document.createElement('div');
      const slot = document.createElement('div'); slot.className = 'proc-panel-slot'; inner.append(slot); drawer.append(inner); row.append(drawer); root.append(row);
    });
    updateSummary();
  }

  function setProcesses(raw, status) {
    try {
      const parsed = typeof raw === 'string' ? JSON.parse(raw) : raw;
      state.processes = Array.isArray(parsed) ? parsed.map(item => ({
        pid: Number(item.pid) || 0,
        name: safe(item.name || 'Proses'),
        memoryBytes: Math.max(0, Number(item.memoryBytes) || 0),
        canTerminate: !!item.canTerminate,
        note: safe(item.note || ''),
        iconBase64: safe(item.iconBase64 || '')
      })) : [];
    } catch (_) { state.processes = []; }
    if (status !== undefined) state.status = safe(status);
    const refresh = $('#processRefresh');
    if (refresh) refresh.setAttribute('aria-busy', String(state.status.startsWith('Memuat') || state.status.startsWith('Mencari')));
    const updated = $('#processUpdated');
    if (updated && !state.status.startsWith('Memuat') && !state.status.startsWith('Mencari') && !state.status.startsWith('Hubungkan') && !state.status.startsWith('Belum ada') && state.processes.length) {
      updated.textContent = `Diperbarui ${new Date().toLocaleTimeString('id-ID', { hour: '2-digit', minute: '2-digit', second: '2-digit' })}`;
    }
    renderProcesses(true);
  }

  function filterProcesses(query) {
    state.query = safe(query);
    const clear = $('#processClear'); if (clear) clear.hidden = !state.query;
    renderProcesses(false);
  }
  function appendTroubleshootLog(line) {
    const root = $('#problemLog'); if (!root) return;
    root.querySelector('.log-empty')?.remove();
    const row = document.createElement('div'); const text = safe(line);
    const level = (text.match(/(?:^|\s)([EWID])\//) || [])[1] || 'I';
    row.className = `log-line ${level.toLowerCase()}`; row.textContent = text; root.append(row);
    while (root.children.length > 800) root.firstElementChild.remove();
    root.scrollTop = root.scrollHeight;
    syncTroubleshootActions();
    const live = $('#sessionLogStatus'); if (live) live.textContent = `${root.querySelectorAll('.log-line').length} baris · sesi aktif`;
    const summary = $('#trs'); if (summary) summary.textContent = 'Log mentah bertambah otomatis selama aplikasi berjalan.';
  }
  function setTroubleshootLog(raw) {
    const root = $('#problemLog'); if (!root) return;
    root.replaceChildren();
    const lines = safe(raw).split(/\r?\n/).filter(Boolean);
    if (!lines.length) { const empty = document.createElement('span'); empty.className = 'log-empty'; empty.textContent = 'Menunggu aktivitas aplikasi…'; root.append(empty); }
    lines.slice(-800).forEach(appendTroubleshootLog);
  }
  function syncTroubleshootActions() {
    const enabled = !!$('#problemLog .log-line');
    ['logCopy', 'logSave', 'logDeveloper', 'logPc'].forEach(id => { const button = $(`#${id}`); if (button) button.disabled = !enabled; });
  }
  function beginTroubleshoot() {
    const button = $('#trb'), status = $('#trs'), progress = $('#tri');
    if (button) { button.disabled = true; button.textContent = 'Pemeriksaan berjalan…'; }
    if (status) status.textContent = 'Pemeriksaan koneksi berjalan. Catatan sesi tetap disimpan.';
    if (progress) { progress.style.transition = 'width .25s ease'; progress.style.width = '8%'; }
    syncTroubleshootActions();
  }

  if (window.LaptopStatus) {
    window.LaptopStatus.setProcesses = setProcesses;
    window.LaptopStatus.setProcessMemory = setProcessMemory;
    window.LaptopStatus.filterProcesses = filterProcesses;
    window.LaptopStatus.renderProcesses = renderProcesses;
    window.LaptopStatus.appendTroubleshootLog = appendTroubleshootLog;
    window.LaptopStatus.setTroubleshootLog = setTroubleshootLog;
    window.LaptopStatus.syncTroubleshootActions = syncTroubleshootActions;
    window.LaptopStatus.beginTroubleshoot = beginTroubleshoot;
  }

  try { if (window.Android && typeof window.Android.getMemory === 'function') setProcessMemory(window.Android.getMemory()); } catch (_) { }

  document.addEventListener('click', event => {
    const target = event.target.closest && event.target.closest('button'); if (!target) return;
    if (target.id === 'processRefresh') {
      event.preventDefault();
      if (target.getAttribute('aria-busy') === 'true') return;
      target.setAttribute('aria-busy', 'true'); call('process:refresh');
      if (window.Android && typeof window.Android.loadProcesses === 'function') window.Android.loadProcesses('');
      else { target.setAttribute('aria-busy', 'false'); state.status = 'Penyegaran tersedia saat memakai aplikasi Android.'; renderProcesses(); }
      return;
    }
    const filter = target.dataset.processFilter;
    if (filter) { state.filter = filter; call(`process:filter:${filter}`); renderProcesses(true); return; }
    if (target.id === 'processSort') { state.sort = state.sort === 'ram' ? 'name' : 'ram'; target.textContent = state.sort === 'ram' ? 'Urutkan · RAM' : 'Urutkan · Nama'; call(`process:sort:${state.sort}`); renderProcesses(true); return; }
    if (target.id === 'processClear') { const input = $('#pq'); if (input) { input.value = ''; state.query = ''; target.hidden = true; input.focus(); renderProcesses(true); } return; }
    const action = target.dataset.procAction; if (!action) return;
    const row = target.closest('.proc-row'); if (!row) return;
    if (action === 'ask') { event.preventDefault(); openDrawer(row, 'confirm'); call('process:confirm_open'); }
    else if (action === 'reason') { event.preventDefault(); openDrawer(row, 'reason'); call('process:protection_detail'); }
    else if (action === 'cancel') { closeDrawer(); call('process:confirm_cancel'); }
    else if (action === 'confirm') {
      const pid = Number(row.dataset.pid); target.disabled = true; target.textContent = 'Mengirim…';
      call('process:terminate_confirmed');
      if (window.Android && typeof window.Android.confirmTerminateProcess === 'function') window.Android.confirmTerminateProcess(pid);
      else { target.disabled = false; target.textContent = 'Ya, hentikan'; }
    }
  });
  document.addEventListener('input', event => {
    if (event.target && event.target.id === 'pq') {
      state.query = event.target.value || '';
      const clear = $('#processClear'); if (clear) clear.hidden = !state.query;
      renderProcesses(false);
    }
  });
  document.addEventListener('click', event => {
    const button = event.target.closest && event.target.closest('#sg2 button');
    if (button) {
      const index = Array.from(button.parentElement.querySelectorAll('button')).indexOf(button);
      const tabs = ['memory', 'device', 'process', 'log']; call(`detail:${tabs[index] || 'unknown'}`);
      if (index === 2 && window.Android && typeof window.Android.loadProcesses === 'function') window.Android.loadProcesses($('#pq')?.value || '');
    }
    const nav = event.target.closest && event.target.closest('nav button');
    if (nav) {
      const index = Array.from(nav.parentElement.querySelectorAll('button')).indexOf(nav);
      const pages = ['device', 'history', 'detail', 'schedule', 'account']; call(`nav:${pages[index] || 'unknown'}`);
    }
  });

  setProcesses([], 'Menunggu data dari laptop…');
})();
