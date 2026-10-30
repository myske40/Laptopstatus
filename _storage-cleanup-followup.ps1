$ErrorActionPreference='Continue'
$log='E:\alikasi\hasil-build\storage-cleanup-followup-20261010.log'
Set-Content -LiteralPath $log -Value 'Follow-up cleanup: approved temporary caches only.'
function Note($s){Write-Output $s; Add-Content -LiteralPath $log -Value $s}
function Clear-CacheUnder($path,$root,$label){
  $full=[IO.Path]::GetFullPath($path).TrimEnd('\'); $base=[IO.Path]::GetFullPath($root).TrimEnd('\')
  if(-not $full.StartsWith($base+'\',[StringComparison]::OrdinalIgnoreCase)){Note "SKIP ${label}: path outside allowed root"; return}
  if(-not (Test-Path -LiteralPath $full -PathType Container)){Note "SKIP ${label}: not found"; return}
  try { Remove-Item -LiteralPath $full -Recurse -Force -ErrorAction Stop; Note "REMOVED $label" } catch { Note "SKIP ${label}: $($_.Exception.Message)" }
}
if(-not (Get-Process -Name LaptopStatus-Setup -ErrorAction SilentlyContinue)){Clear-CacheUnder 'C:\Users\msi\AppData\Local\Temp\.net\LaptopStatus-Setup-2.12.4' 'C:\Users\msi\AppData\Local\Temp\.net' 'old Laptop Status setup extraction cache'} else {Note 'SKIP setup cache: installer is running'}
if(-not (Get-Process -Name WinRAR -ErrorAction SilentlyContinue)){Clear-CacheUnder 'C:\Users\msi\AppData\Local\Temp\WinRAR.exe.WebView2' 'C:\Users\msi\AppData\Local\Temp' 'WinRAR WebView2 cache'} else {Note 'SKIP WinRAR cache: WinRAR is running'}
$p='C:\Windows\Temp\wctFC28.tmp'
if(Test-Path -LiteralPath $p -PathType Leaf){
  try { $s=[IO.File]::Open($p,[IO.FileMode]::Open,[IO.FileAccess]::ReadWrite,[IO.FileShare]::None); $s.Dispose(); Remove-Item -LiteralPath $p -Force -ErrorAction Stop; Note 'REMOVED unlocked Windows temporary .tmp file (147 MB)' }
  catch { Note "SKIP Windows temporary .tmp: $($_.Exception.Message)" }
} else { Note 'SKIP Windows temporary .tmp: not found' }
