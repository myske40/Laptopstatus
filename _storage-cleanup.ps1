$ErrorActionPreference = 'Continue'
$userRoot = 'C:\Users\msi'
$localRoot = 'C:\Users\msi\AppData\Local'
$buildRoot = 'E:\alikasi\hasil-build'
$logPath = 'E:\alikasi\hasil-build\storage-cleanup-20261010.log'
New-Item -ItemType Directory -Path (Split-Path -Parent $logPath) -Force | Out-Null
Set-Content -LiteralPath $logPath -Value 'Laptop Status cleanup log - 2026-10-10'

function Write-Log {
    param([string]$Message)
    Write-Output $Message
    Add-Content -LiteralPath $logPath -Value $Message
}

function Get-BytesInFolder {
    param([string]$Path)
    [long]$total = 0
    if (-not (Test-Path -LiteralPath $Path -PathType Container)) { return $total }
    foreach ($file in (Get-ChildItem -LiteralPath $Path -File -Recurse -Force -ErrorAction SilentlyContinue)) {
        $total += $file.Length
    }
    return $total
}

function Clear-CachePath {
    param([string]$Path, [string]$AllowedRoot, [string]$Label)
    if ([string]::IsNullOrWhiteSpace($Path) -or [string]::IsNullOrWhiteSpace($AllowedRoot)) {
        Write-Log ('SKIP ' + $Label + ': jalur kosong')
        return
    }
    $fullPath = [IO.Path]::GetFullPath($Path).TrimEnd('\')
    $fullRoot = [IO.Path]::GetFullPath($AllowedRoot).TrimEnd('\')
    if (-not $fullPath.StartsWith($fullRoot + '\', [StringComparison]::OrdinalIgnoreCase)) {
        Write-Log ('SKIP ' + $Label + ': di luar folder yang diizinkan')
        return
    }
    if (-not (Test-Path -LiteralPath $fullPath -PathType Container)) { return }

    $before = Get-BytesInFolder -Path $fullPath
    [int]$failed = 0
    foreach ($child in (Get-ChildItem -LiteralPath $fullPath -Force -ErrorAction SilentlyContinue)) {
        try {
            Remove-Item -LiteralPath $child.FullName -Recurse -Force -ErrorAction Stop
        }
        catch {
            $failed++
            Write-Log ('TERKUNCI ' + $child.FullName)
        }
    }
    $after = Get-BytesInFolder -Path $fullPath
    $freedGiB = [math]::Round(($before - $after) / 1GB, 2)
    $remainGiB = [math]::Round($after / 1GB, 2)
    Write-Log ('BERSIH ' + $Label + ': dilepas ' + $freedGiB + ' GiB; tertahan ' + $remainGiB + ' GiB; file terkunci ' + $failed)
}

function Clear-BrowserCache {
    param([string]$UserDataRoot, [string]$ProcessName, [string]$Label)
    if (Get-Process -Name $ProcessName -ErrorAction SilentlyContinue) {
        Write-Log ('LEWATI ' + $Label + ': browser sedang berjalan')
        return
    }
    if (-not (Test-Path -LiteralPath $UserDataRoot -PathType Container)) { return }
    $cacheNames = @('Cache', 'Code Cache', 'GPUCache')
    foreach ($profile in (Get-ChildItem -LiteralPath $UserDataRoot -Directory -Force -ErrorAction SilentlyContinue)) {
        foreach ($cacheName in $cacheNames) {
            $cachePath = Join-Path $profile.FullName $cacheName
            Clear-CachePath -Path $cachePath -AllowedRoot $UserDataRoot -Label ($Label + ' ' + $profile.Name + ' ' + $cacheName)
        }
        $workerPath = Join-Path $profile.FullName 'Service Worker\CacheStorage'
        Clear-CachePath -Path $workerPath -AllowedRoot $UserDataRoot -Label ($Label + ' ' + $profile.Name + ' worker cache')
    }
    $componentCache = Join-Path $UserDataRoot 'component_crx_cache'
    Clear-CachePath -Path $componentCache -AllowedRoot $UserDataRoot -Label ($Label + ' component cache')
}

function Get-FreeGiB {
    param([string]$Drive)
    $disk = Get-CimInstance Win32_LogicalDisk -Filter ("DeviceID='" + $Drive + "'") -ErrorAction SilentlyContinue
    if ($disk) { return [math]::Round($disk.FreeSpace / 1GB, 2) }
    return 0
}

$freeCBefore = Get-FreeGiB -Drive 'C:'
$freeEBefore = Get-FreeGiB -Drive 'E:'
Write-Log ('RUANG SEBELUM: C ' + $freeCBefore + ' GiB; E ' + $freeEBefore + ' GiB')
Write-Log 'Hanya cache yang dapat dibuat ulang dan staging build lama yang dibersihkan.'

$tempRoot = Join-Path $localRoot 'Temp\.net'
if (-not (Get-Process -Name LaptopStatus-Setup -ErrorAction SilentlyContinue)) {
    foreach ($oldSetupCache in (Get-ChildItem -LiteralPath $tempRoot -Directory -Force -ErrorAction SilentlyContinue | Where-Object { $_.Name -like 'LaptopStatus-Setup*' })) {
        Clear-CachePath -Path $oldSetupCache.FullName -AllowedRoot $tempRoot -Label ('cache ekstraksi installer ' + $oldSetupCache.Name)
        try { Remove-Item -LiteralPath $oldSetupCache.FullName -Recurse -Force -ErrorAction Stop } catch { }
    }
} else { Write-Log 'LEWATI cache ekstraksi installer: installer sedang berjalan' }

$nvidiaRoot = Join-Path $localRoot 'NVIDIA'
Clear-CachePath -Path (Join-Path $nvidiaRoot 'DXCache') -AllowedRoot $nvidiaRoot -Label 'cache shader NVIDIA'
if (-not (Get-Process -Name NVIDIAApp,NVIDIAOverlay -ErrorAction SilentlyContinue)) {
    $nvidiaUiRoot = Join-Path $localRoot 'NVIDIA Corporation'
    Clear-CachePath -Path (Join-Path $nvidiaUiRoot 'NVIDIA App\CefCache') -AllowedRoot $nvidiaUiRoot -Label 'cache NVIDIA App'
    Clear-CachePath -Path (Join-Path $nvidiaUiRoot 'NVIDIA Overlay\CefCache') -AllowedRoot $nvidiaUiRoot -Label 'cache NVIDIA Overlay'
} else { Write-Log 'LEWATI cache antarmuka NVIDIA: aplikasi sedang berjalan' }

$chromeData = Join-Path $localRoot 'Google\Chrome\User Data'
Clear-BrowserCache -UserDataRoot $chromeData -ProcessName 'chrome' -Label 'cache Chrome'
$braveData = Join-Path $localRoot 'BraveSoftware\Brave-Browser\User Data'
Clear-BrowserCache -UserDataRoot $braveData -ProcessName 'brave' -Label 'cache Brave'

$steamRoot = Join-Path $localRoot 'Steam'
if (-not (Get-Process -Name steam -ErrorAction SilentlyContinue)) {
    Clear-CachePath -Path (Join-Path $steamRoot 'htmlcache') -AllowedRoot $steamRoot -Label 'cache Steam'
} else { Write-Log 'LEWATI cache Steam: Steam sedang berjalan' }

$fortniteRoot = Join-Path $localRoot 'FortniteGame'
if (-not (Get-Process -Name FortniteClient-Win64-Shipping,FORTNITE -ErrorAction SilentlyContinue)) {
    Clear-CachePath -Path (Join-Path $fortniteRoot 'Saved\PersistentDownloadDir\IoStore\InstallCache') -AllowedRoot $fortniteRoot -Label 'cache pemasangan Fortnite'
} else { Write-Log 'LEWATI cache Fortnite: game sedang berjalan' }

$androidStudioRoot = Join-Path $localRoot 'Google\AndroidStudio2025.3.4'
if (-not (Get-Process -Name studio64,studio -ErrorAction SilentlyContinue)) {
    Clear-CachePath -Path (Join-Path $androidStudioRoot 'index') -AllowedRoot $androidStudioRoot -Label 'indeks Android Studio'
    Clear-CachePath -Path (Join-Path $androidStudioRoot 'caches') -AllowedRoot $androidStudioRoot -Label 'cache Android Studio'
} else { Write-Log 'LEWATI cache Android Studio: IDE sedang berjalan' }

if (-not (Get-Process -Name java,gradle -ErrorAction SilentlyContinue)) {
    $gradleRoot = Join-Path $userRoot '.gradle\caches'
    foreach ($oldGradleCache in @('8.7', '8.13', 'transforms-4')) {
        Clear-CachePath -Path (Join-Path $gradleRoot $oldGradleCache) -AllowedRoot $gradleRoot -Label ('cache Gradle ' + $oldGradleCache)
    }
} else { Write-Log 'LEWATI cache Gradle: proses build Android sedang berjalan' }

$npmRoot = Join-Path $localRoot 'npm-cache'
Clear-CachePath -Path (Join-Path $npmRoot '_cacache') -AllowedRoot $npmRoot -Label 'cache npm'

if (-not (Get-Process -Name olk,OUTLOOK -ErrorAction SilentlyContinue)) {
    $outlookRoot = Join-Path $localRoot 'Microsoft\Olk\EBWebView\Default'
    Clear-CachePath -Path (Join-Path $outlookRoot 'Cache') -AllowedRoot $outlookRoot -Label 'cache Outlook'
    Clear-CachePath -Path (Join-Path $outlookRoot 'Code Cache') -AllowedRoot $outlookRoot -Label 'cache kode Outlook'
    Clear-CachePath -Path (Join-Path $outlookRoot 'Service Worker\CacheStorage') -AllowedRoot $outlookRoot -Label 'cache worker Outlook'
} else { Write-Log 'LEWATI cache Outlook: Outlook sedang berjalan' }

foreach ($oldBuild in @('_build-20260930', '_build-20260929', '_build-20260929-ui')) {
    $oldBuildPath = Join-Path $buildRoot $oldBuild
    Clear-CachePath -Path $oldBuildPath -AllowedRoot $buildRoot -Label ('staging build ' + $oldBuild)
    if (Test-Path -LiteralPath $oldBuildPath) {
        try { Remove-Item -LiteralPath $oldBuildPath -Recurse -Force -ErrorAction Stop } catch { }
    }
}

$freeCAfter = Get-FreeGiB -Drive 'C:'
$freeEAfter = Get-FreeGiB -Drive 'E:'
Write-Log ('RUANG SESUDAH: C ' + $freeCAfter + ' GiB; E ' + $freeEAfter + ' GiB')
Write-Log 'Firefox dan Edge yang sedang aktif, runtime Laptop Status, Windows Update, unduhan, proyek, serta OneDrive dipertahankan.'
