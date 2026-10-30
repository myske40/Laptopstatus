$candidates = @(
  'C:\Windows\MEMORY.DMP',
  'C:\Windows\Minidump',
  'C:\Windows\LiveKernelReports',
  'C:\ProgramData\Microsoft\Windows\WER',
  'C:\Users\msi\AppData\Local\CrashDumps',
  'C:\Windows\Temp',
  'C:\Users\msi\AppData\Local\Temp'
)
foreach ($path in $candidates) {
  if (Test-Path -LiteralPath $path -PathType Leaf) {
    $item = Get-Item -LiteralPath $path -Force
    '{0} | {1:N2} GiB | file' -f $item.FullName, ($item.Length / 1GB)
  } elseif (Test-Path -LiteralPath $path -PathType Container) {
    [long]$sum = 0
    foreach ($file in (Get-ChildItem -LiteralPath $path -File -Recurse -Force -ErrorAction SilentlyContinue)) { $sum += $file.Length }
    '{0} | {1:N2} GiB | folder' -f $path, ($sum / 1GB)
  } else {
    '{0} | not found' -f $path
  }
}
