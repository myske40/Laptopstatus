$ErrorActionPreference = 'Stop'
$paths = @(
    'LaptopStatus-AndroidApp\app\src\main\assets\demo-android-motion.html',
    'LaptopStatus-AndroidApp\app\src\main\assets\demo-android-motion-lama.html'
)
$newProcess = @'
<div class="c" data-t="2" style="--i:2"><section class="proc-summary" aria-label="Ringkasan RAM proses"><div class="proc-sum-head"><div><small class="proc-eyebrow">RAM dipakai proses</small><div class="proc-total-line"><strong class="proc-total" id="processTotal">0</strong><small class="proc-unit" id="processUnit">MB</small></div></div><button class="proc-refresh" id="processRefresh" type="button" aria-label="Segarkan daftar proses" aria-busy="false"><svg width="19" height="19" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2.2" stroke-linecap="round" stroke-linejoin="round"><path d="M21 12a9 9 0 1 1-3-6.7"/><path d="M21 4v5h-5"/></svg></button></div><div class="proc-stack" id="processStack" aria-hidden="true"></div><div class="proc-legend" id="processLegend"></div><small class="proc-updated" id="processUpdated">Belum diperbarui</small></section><section class="proc-list-card"><div class="proc-list-title"><strong>Proses berjalan</strong><span id="processCount">0 / 0</span></div><div class="proc-search"><svg class="proc-search-icon" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2.1" stroke-linecap="round"><circle cx="11" cy="11" r="7"/><path d="m20 20-3.5-3.5"/></svg><input class="inp" id="pq" type="search" inputmode="search" autocomplete="off" placeholder="Cari nama atau PID" aria-label="Cari proses"><button class="proc-clear" id="processClear" type="button" aria-label="Hapus pencarian" hidden>×</button></div><div class="proc-tools"><div class="proc-chips" role="group" aria-label="Filter proses"><button class="proc-chip" data-process-filter="all" aria-pressed="true">Semua <i>0</i></button><button class="proc-chip" data-process-filter="kill" aria-pressed="false">Bisa dihentikan <i>0</i></button><button class="proc-chip" data-process-filter="safe" aria-pressed="false">Dilindungi <i>0</i></button></div><button class="proc-sort" id="processSort" type="button">Urutkan · RAM</button></div><div class="proc-status" id="processStatus" role="status">Hubungkan laptop untuk memuat proses.</div><div id="pl" aria-live="polite"></div></section></div>
'@
$newProcess = $newProcess.Trim()
$newProcess = $newProcess.Replace('data-t="2"', 'data-t="2"')
$newProcessClassic = $newProcess.Replace('data-t="2" ', '')
$newLogHeader = '<div class="session-log-head"><b>Log aktivitas aplikasi</b><span class="session-log-live"><i></i>LANGSUNG</span></div><small id="sessionLogStatus" class="dim" style="display:block;margin-bottom:8px">Menunggu aktivitas aplikasi…</small>'
$oldSearchHandler = "document.addEventListener('input',function(e){if(e.target&&e.target.id==='pq'){e.stopImmediatePropagation();call('loadProcesses',e.target.value||'')}},true);"
$newSearchHandler = "document.addEventListener('input',function(e){if(e.target&&e.target.id==='pq'){e.stopImmediatePropagation();window.LaptopStatus&&window.LaptopStatus.filterProcesses(e.target.value||'')}},true);"
$procFunctionPattern = 'function procs\(\)\{[^\r\n]*\}'
$procFunctionReplacement = 'function procs(){window.LaptopStatus&&window.LaptopStatus.renderProcesses&&window.LaptopStatus.renderProcesses()}'
foreach ($path in $paths) {
    $full = Join-Path (Get-Location) $path
    $html = [IO.File]::ReadAllText($full)
    if ($path.EndsWith('-lama.html')) {
        $oldProcess = '<div class="c" style="--i:2"><b>Proses</b><input class="inp" id="pq" placeholder="Cari proses"><div id="pl"></div></div>'
        $oldLogPrefix = '<div class="c" style="--i:3"><b>Pemecahan masalah</b><div id="problemLog"'
        $newProcessBlock = $newProcessClassic
    } else {
        $oldProcess = '<div class="c" data-t="2" style="--i:2"><b>Proses</b><input class="inp" id="pq" placeholder="Cari proses"><div id="pl"></div></div>'
        $oldLogPrefix = '<div class="c" data-t="3" style="--i:3"><b>Log masalah yang terjadi</b><div id="problemLog"'
        $newProcessBlock = $newProcess
    }
    if (-not $html.Contains($oldProcess)) { throw "Blok proses tidak ditemukan: $path" }
    $html = $html.Replace($oldProcess, $newProcessBlock)
    if (-not $html.Contains($oldLogPrefix)) { throw "Blok log tidak ditemukan: $path" }
    $html = $html.Replace($oldLogPrefix, $oldLogPrefix.Replace('<div id="problemLog"', "$newLogHeader<div id=`"problemLog`""))
    $html = $html.Replace('Belum ada pemeriksaan. Tekan tombol untuk mulai.', 'Menunggu aktivitas aplikasi…')
    $rx = [regex]::new($procFunctionPattern)
    if (-not $rx.IsMatch($html)) { throw "Fungsi proses contoh tidak ditemukan: $path" }
    $html = $rx.Replace($html, $procFunctionReplacement, 1)
    if (-not $html.Contains($oldSearchHandler)) { throw "Handler pencarian proses tidak ditemukan: $path" }
    $html = $html.Replace($oldSearchHandler, $newSearchHandler)
    $styleEnd = $html.LastIndexOf('</style>')
    if ($styleEnd -lt 0) { throw "CSS aplikasi tidak ditemukan: $path" }
    if (-not $html.Contains('process-live-ui.css')) {
        $styleEnd += '</style>'.Length
        $html = $html.Insert($styleEnd, '<link rel="stylesheet" href="process-live-ui.css">')
    }
    if (-not $html.Contains('process-live-ui.js')) { $html += '<script src="process-live-ui.js"></script>' }
    [IO.File]::WriteAllText($full, $html, [Text.UTF8Encoding]::new($false))
    Write-Output "Updated $path"
}
