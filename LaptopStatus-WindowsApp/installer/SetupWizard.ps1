Add-Type -AssemblyName System.Windows.Forms
Add-Type -AssemblyName System.Drawing
[System.Windows.Forms.Application]::EnableVisualStyles()

$identity = [Security.Principal.WindowsIdentity]::GetCurrent()
$principal = New-Object Security.Principal.WindowsPrincipal($identity)
if (-not $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
    Start-Process -FilePath 'powershell.exe' -Verb RunAs -ArgumentList "-NoProfile -ExecutionPolicy Bypass -STA -File `"$PSCommandPath`""
    exit
}

$form = New-Object System.Windows.Forms.Form
$form.Text = 'Pengaturan Laptop Status'
$form.Size = New-Object System.Drawing.Size(590, 390)
$form.MinimumSize = $form.Size
$form.MaximumSize = $form.Size
$form.StartPosition = 'CenterScreen'
$form.FormBorderStyle = 'FixedDialog'
$form.MaximizeBox = $false
$form.MinimizeBox = $false
$form.BackColor = [System.Drawing.Color]::FromArgb(18, 24, 33)
$form.ForeColor = [System.Drawing.Color]::White
$form.Font = New-Object System.Drawing.Font('Segoe UI', 10)

$title = New-Object System.Windows.Forms.Label
$title.Text = 'Instal Laptop Status'
$title.Font = New-Object System.Drawing.Font('Segoe UI Semibold', 19)
$title.Location = New-Object System.Drawing.Point(28, 22)
$title.Size = New-Object System.Drawing.Size(510, 40)
$form.Controls.Add($title)

$intro = New-Object System.Windows.Forms.Label
$intro.Text = 'Aplikasi memantau laptop dan menerima kontrol dari perangkat Android yang sudah dipasangkan.'
$intro.Location = New-Object System.Drawing.Point(30, 70)
$intro.Size = New-Object System.Drawing.Size(520, 42)
$intro.ForeColor = [System.Drawing.Color]::FromArgb(185, 197, 211)
$form.Controls.Add($intro)

$pathLabel = New-Object System.Windows.Forms.Label
$pathLabel.Text = 'Lokasi instalasi'
$pathLabel.Location = New-Object System.Drawing.Point(30, 127)
$pathLabel.Size = New-Object System.Drawing.Size(510, 22)
$form.Controls.Add($pathLabel)

$installPath = New-Object System.Windows.Forms.TextBox
$installPath.Text = Join-Path $env:LOCALAPPDATA 'Programs\LaptopStatus'
$installPath.Location = New-Object System.Drawing.Point(30, 154)
$installPath.Size = New-Object System.Drawing.Size(415, 28)
$installPath.BackColor = [System.Drawing.Color]::FromArgb(30, 39, 52)
$installPath.ForeColor = [System.Drawing.Color]::White
$form.Controls.Add($installPath)

$browse = New-Object System.Windows.Forms.Button
$browse.Text = 'Pilih...'
$browse.Location = New-Object System.Drawing.Point(455, 152)
$browse.Size = New-Object System.Drawing.Size(95, 31)
$browse.Add_Click({
    $dialog = New-Object System.Windows.Forms.FolderBrowserDialog
    $dialog.SelectedPath = $installPath.Text
    $dialog.Description = 'Pilih folder untuk memasang Laptop Status'
    if ($dialog.ShowDialog() -eq [System.Windows.Forms.DialogResult]::OK) { $installPath.Text = Join-Path $dialog.SelectedPath 'LaptopStatus' }
})
$form.Controls.Add($browse)

$autoStart = New-Object System.Windows.Forms.CheckBox
$autoStart.Text = 'Jalankan otomatis saat saya masuk ke Windows'
$autoStart.Checked = $true
$autoStart.Location = New-Object System.Drawing.Point(30, 204)
$autoStart.Size = New-Object System.Drawing.Size(510, 28)
$autoStart.ForeColor = [System.Drawing.Color]::FromArgb(225, 232, 240)
$form.Controls.Add($autoStart)

$launchNow = New-Object System.Windows.Forms.CheckBox
$launchNow.Text = 'Buka Laptop Status setelah instalasi selesai'
$launchNow.Checked = $true
$launchNow.Location = New-Object System.Drawing.Point(30, 238)
$launchNow.Size = New-Object System.Drawing.Size(510, 28)
$launchNow.ForeColor = [System.Drawing.Color]::FromArgb(225, 232, 240)
$form.Controls.Add($launchNow)

$status = New-Object System.Windows.Forms.Label
$status.Text = 'Izin administrator diminta sekali saat setup. Auto-start dan shortcut memakai izin tertinggi tanpa UAC per proses.'
$status.Location = New-Object System.Drawing.Point(30, 278)
$status.Size = New-Object System.Drawing.Size(520, 36)
$status.ForeColor = [System.Drawing.Color]::FromArgb(140, 154, 172)
$form.Controls.Add($status)

$install = New-Object System.Windows.Forms.Button
$install.Text = 'Instal'
$install.Location = New-Object System.Drawing.Point(342, 320)
$install.Size = New-Object System.Drawing.Size(100, 34)
$install.BackColor = [System.Drawing.Color]::FromArgb(53, 170, 135)
$install.FlatStyle = 'Flat'
$install.ForeColor = [System.Drawing.Color]::White
$install.Add_Click({
    try {
        $runningApps = @(Get-CimInstance Win32_Process -Filter "Name='LaptopStatus.exe'" -ErrorAction SilentlyContinue)
        if ($runningApps.Count -gt 0) {
            [System.Windows.Forms.MessageBox]::Show(
                'Status Perangkat masih berjalan. Tutup aplikasi dari ikon tray (klik kanan > Keluar), lalu klik Instal lagi. Koneksi Android akan terputus sebentar saat pembaruan.',
                'Tutup aplikasi sebelum memperbarui', 'OK', 'Warning'
            ) | Out-Null
            return
        }

        $destination = [System.IO.Path]::GetFullPath($installPath.Text.Trim())
        if ([string]::IsNullOrWhiteSpace($destination) -or $destination.Length -lt 8) { throw 'Lokasi instalasi tidak valid.' }
        New-Item -ItemType Directory -Force -Path $destination | Out-Null
        Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'LaptopStatus.exe') -Destination (Join-Path $destination 'LaptopStatus.exe') -Force
        Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'Uninstall-LaptopStatus.ps1') -Destination (Join-Path $destination 'Uninstall-LaptopStatus.ps1') -Force

        $runKey = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Run'
        $runName = 'LaptopStatus'
        $exePath = Join-Path $destination 'LaptopStatus.exe'
        Remove-ItemProperty -Path $runKey -Name $runName -ErrorAction SilentlyContinue
        & "$env:WINDIR\System32\schtasks.exe" /Delete /TN '\LaptopStatus' /F *> $null
        if ($autoStart.Checked) {
            & "$env:WINDIR\System32\schtasks.exe" /Create /TN '\LaptopStatus' /SC ONLOGON /IT /TR ('"' + $exePath + '"') /RL HIGHEST /F
            if ($LASTEXITCODE -ne 0) { throw 'Task Scheduler tidak dapat mengatur auto-start Laptop Status.' }
        }

        $programs = Join-Path $env:APPDATA 'Microsoft\Windows\Start Menu\Programs\Laptop Status'
        New-Item -ItemType Directory -Force -Path $programs | Out-Null
        $shell = New-Object -ComObject WScript.Shell
        $shortcut = $shell.CreateShortcut((Join-Path $programs 'Laptop Status.lnk'))
        $shortcut.TargetPath = if ($autoStart.Checked) { "$env:WINDIR\System32\schtasks.exe" } else { $exePath }
        if ($autoStart.Checked) { $shortcut.Arguments = '/Run /TN "\LaptopStatus"' }
        $shortcut.WorkingDirectory = $destination
        $shortcut.Save()
        $uninstallShortcut = $shell.CreateShortcut((Join-Path $programs 'Hapus Laptop Status.lnk'))
        $uninstallShortcut.TargetPath = 'powershell.exe'
        $uninstallShortcut.Arguments = '-NoProfile -ExecutionPolicy Bypass -File "' + (Join-Path $destination 'Uninstall-LaptopStatus.ps1') + '"'
        $uninstallShortcut.Save()

        $uninstallKey = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Uninstall\LaptopStatus'
        New-Item -Path $uninstallKey -Force | Out-Null
        New-ItemProperty -Path $uninstallKey -Name DisplayName -Value 'Laptop Status' -PropertyType String -Force | Out-Null
        New-ItemProperty -Path $uninstallKey -Name DisplayVersion -Value '2.2.0' -PropertyType String -Force | Out-Null
        New-ItemProperty -Path $uninstallKey -Name Publisher -Value 'Laptop Status' -PropertyType String -Force | Out-Null
        New-ItemProperty -Path $uninstallKey -Name InstallLocation -Value $destination -PropertyType String -Force | Out-Null
        New-ItemProperty -Path $uninstallKey -Name UninstallString -Value ('powershell.exe -NoProfile -ExecutionPolicy Bypass -File "' + (Join-Path $destination 'Uninstall-LaptopStatus.ps1') + '"') -PropertyType String -Force | Out-Null

        if ($launchNow.Checked) {
            if ($autoStart.Checked) { Start-Process -FilePath "$env:WINDIR\System32\schtasks.exe" -ArgumentList @('/Run', '/TN', '\LaptopStatus') -WindowStyle Hidden }
            else { Start-Process -FilePath $exePath -WorkingDirectory $destination }
        }
        [System.Windows.Forms.MessageBox]::Show('Laptop Status berhasil dipasang.' + [Environment]::NewLine + 'Di Android, cari laptop di Wi-Fi atau pindai QR dari Ringkasan Windows. Setujui kode pairing di Windows.', 'Instalasi selesai', 'OK', 'Information') | Out-Null
        $form.Close()
    }
    catch {
        [System.Windows.Forms.MessageBox]::Show($_.Exception.Message, 'Instalasi gagal', 'OK', 'Error') | Out-Null
    }
})
$form.Controls.Add($install)

$cancel = New-Object System.Windows.Forms.Button
$cancel.Text = 'Batal'
$cancel.Location = New-Object System.Drawing.Point(450, 320)
$cancel.Size = New-Object System.Drawing.Size(100, 34)
$cancel.Add_Click({ $form.Close() })
$form.Controls.Add($cancel)

[void]$form.ShowDialog()
