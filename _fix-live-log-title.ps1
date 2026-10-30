$ErrorActionPreference = 'Stop'
foreach ($relative in @(
    'LaptopStatus-AndroidApp\app\src\main\assets\demo-android-motion.html',
    'LaptopStatus-AndroidApp\app\src\main\assets\demo-android-motion-lama.html'
)) {
    $path = Join-Path (Get-Location) $relative
    $html = [IO.File]::ReadAllText($path)
    $before = $html
    $html = $html.Replace('<b>Log masalah yang terjadi</b><div class="session-log-head">', '<div class="session-log-head">')
    $html = $html.Replace('<b>Pemecahan masalah</b><div class="session-log-head">', '<div class="session-log-head">')
    if ($html -eq $before) { throw "Judul log lama tidak ditemukan: $relative" }
    [IO.File]::WriteAllText($path, $html, [Text.UTF8Encoding]::new($false))
    Write-Output "Cleaned log heading: $relative"
}
