$ErrorActionPreference='SilentlyContinue'
$out='E:\alikasi\hasil-build\large-files-report-20261010.txt'
Start-Transcript -LiteralPath $out -Force | Out-Null
function Show-TopFiles([string]$label,[string]$path,[int]$top=6){
  Write-Output "=== $label ==="
  if(-not (Test-Path -LiteralPath $path -PathType Container)){Write-Output 'Folder tidak ditemukan.'; return}
  $files=Get-ChildItem -LiteralPath $path -File -Recurse -Force -ErrorAction SilentlyContinue | Sort-Object Length -Descending | Select-Object -First $top
  if(-not $files){Write-Output 'Tidak ada file yang dapat dibaca.'; return}
  foreach($f in $files){Write-Output ('{0,7:N2} GiB | {1:yyyy-MM-dd HH:mm} | {2}' -f ($f.Length/1GB),$f.LastWriteTime,$f.FullName)}
}
Write-Output 'Audit read-only: file besar pada C:. Tidak ada file dihapus atau dipindahkan.'
Write-Output '=== FILE SISTEM LANGSUNG DI C:\ ==='
Get-ChildItem -LiteralPath 'C:\' -File -Force | Sort-Object Length -Descending | Select-Object -First 12 | ForEach-Object {Write-Output ('{0,7:N2} GiB | {1:yyyy-MM-dd HH:mm} | {2}' -f ($_.Length/1GB),$_.LastWriteTime,$_.FullName)}
Show-TopFiles 'Downloads' 'C:\Users\msi\Downloads' 10
Show-TopFiles 'OneDrive' 'C:\Users\msi\OneDrive' 10
Show-TopFiles 'Videos' 'C:\Users\msi\Videos' 8
Show-TopFiles 'Desktop' 'C:\Users\msi\Desktop' 6
Show-TopFiles 'Documents' 'C:\Users\msi\Documents' 6
Show-TopFiles 'Pictures' 'C:\Users\msi\Pictures' 6
Show-TopFiles 'Cache unduhan Windows Update (dipertahankan)' 'C:\Windows\SoftwareDistribution\Download' 8
Show-TopFiles 'Data offline Spotify (mungkin lagu unduhan)' 'C:\Users\msi\AppData\Local\Packages\SpotifyAB.SpotifyMusic_zpdnekdrzrea0' 8
Show-TopFiles 'Penyimpanan Roblox' 'C:\Users\msi\AppData\Local\RobloxPCGDK\rbx-storage' 8
Show-TopFiles 'Data Wondershare Filmora' 'C:\Users\msi\AppData\Local\Wondershare\Wondershare Filmora' 8
Stop-Transcript | Out-Null
