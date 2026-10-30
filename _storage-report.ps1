$ErrorActionPreference = 'SilentlyContinue'
$volumes = Get-CimInstance Win32_LogicalDisk -Filter 'DriveType=3' |
    Select-Object DeviceID,
        @{Name='FreeGiB'; Expression={[math]::Round($_.FreeSpace / 1GB, 2)}},
        @{Name='SizeGiB'; Expression={[math]::Round($_.Size / 1GB, 2)}},
        @{Name='FreePercent'; Expression={[math]::Round(100 * $_.FreeSpace / $_.Size, 1)}}
Write-Output 'DRIVE SPACE'
$volumes | Format-Table -AutoSize | Out-String | Write-Output

function Get-DirectorySize([string]$Path) {
    if (-not (Test-Path -LiteralPath $Path)) { return }
    $sum = (Get-ChildItem -LiteralPath $Path -File -Recurse -Force -ErrorAction SilentlyContinue |
        Measure-Object -Property Length -Sum).Sum
    [pscustomobject]@{Path=$Path; GiB=[math]::Round(([double]$sum / 1GB), 3)}
}

$targets = @(
    (Join-Path $env:LOCALAPPDATA 'Temp'),
    (Join-Path $env:LOCALAPPDATA 'Microsoft\Edge\User Data\Default\Cache'),
    (Join-Path $env:LOCALAPPDATA 'Microsoft\Edge\User Data\Default\Code Cache'),
    (Join-Path $env:LOCALAPPDATA 'Google\Chrome\User Data\Default\Cache'),
    (Join-Path $env:LOCALAPPDATA 'Google\Chrome\User Data\Default\Code Cache'),
    (Join-Path $env:USERPROFILE '.gradle\caches'),
    'E:\alikasi\hasil-build',
    'E:\alikasi\LaptopStatus-WindowsApp\bin',
    'E:\alikasi\LaptopStatus-WindowsApp\obj',
    'E:\alikasi\LaptopStatus-AndroidApp\build'
)
Write-Output 'CACHE / BUILD / RELEASE SIZES'
foreach ($path in $targets) { Get-DirectorySize $path }
Write-Output 'TEMP LAPTOPSTATUS EXTRACTION CACHES'
Get-ChildItem -LiteralPath (Join-Path $env:LOCALAPPDATA 'Temp') -Directory -Force -ErrorAction SilentlyContinue |
    Where-Object Name -Match 'LaptopStatus|Setup|Extract' |
    ForEach-Object { Get-DirectorySize $_.FullName }
