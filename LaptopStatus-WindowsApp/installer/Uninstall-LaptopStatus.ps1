Add-Type -AssemblyName System.Windows.Forms
$identity = [Security.Principal.WindowsIdentity]::GetCurrent()
$principal = New-Object Security.Principal.WindowsPrincipal($identity)
if (-not $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
    Start-Process -FilePath 'powershell.exe' -Verb RunAs -ArgumentList "-NoProfile -ExecutionPolicy Bypass -File `"$PSCommandPath`""
    exit
}
$choice = [System.Windows.Forms.MessageBox]::Show('Hapus Laptop Status dan nonaktifkan auto-start?', 'Hapus Laptop Status', 'YesNo', 'Question')
if ($choice -ne [System.Windows.Forms.DialogResult]::Yes) { exit }

$installDir = $PSScriptRoot
$exePath = Join-Path $installDir 'LaptopStatus.exe'
$runKey = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Run'
$uninstallKey = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Uninstall\LaptopStatus'
$shortcutDir = Join-Path $env:APPDATA 'Microsoft\Windows\Start Menu\Programs\Laptop Status'
$scheduledPowerPath = Join-Path $env:LOCALAPPDATA 'LaptopStatus\scheduled-power.json'

Get-CimInstance Win32_Process -Filter "Name='LaptopStatus.exe'" -ErrorAction SilentlyContinue |
    Where-Object { $_.ExecutablePath -eq $exePath } |
    ForEach-Object { Stop-Process -Id $_.ProcessId -Force -ErrorAction SilentlyContinue }
Remove-ItemProperty -Path $runKey -Name 'LaptopStatus' -ErrorAction SilentlyContinue
& "$env:WINDIR\System32\schtasks.exe" /Delete /TN '\LaptopStatus' /F *> $null
& "$env:WINDIR\System32\schtasks.exe" /Delete /TN '\LaptopStatus-ScheduledPower' /F *> $null
Remove-Item -LiteralPath $scheduledPowerPath -Force -ErrorAction SilentlyContinue
Get-NetFirewallRule -DisplayName 'LaptopStatus Tailscale remote' -ErrorAction SilentlyContinue | Remove-NetFirewallRule -ErrorAction SilentlyContinue
Remove-Item -LiteralPath $uninstallKey -Recurse -Force -ErrorAction SilentlyContinue
Remove-Item -LiteralPath $shortcutDir -Recurse -Force -ErrorAction SilentlyContinue

$cleanupPath = Join-Path $env:TEMP ('LaptopStatus-cleanup-' + [guid]::NewGuid().ToString('N') + '.cmd')
$quotedInstall = '"' + $installDir + '"'
$cleanup = "@echo off`r`ntimeout /t 2 /nobreak >nul`r`nrmdir /s /q $quotedInstall`r`ndel `"%~f0`"`r`n"
Set-Content -LiteralPath $cleanupPath -Value $cleanup -Encoding ASCII
[System.Windows.Forms.MessageBox]::Show('Auto-start dan shortcut telah dihapus. Folder aplikasi akan dibersihkan sebentar lagi.', 'Laptop Status dihapus', 'OK', 'Information') | Out-Null
Start-Process -FilePath "$env:WINDIR\System32\cmd.exe" -ArgumentList @('/c', "`"$cleanupPath`"") -WindowStyle Hidden
