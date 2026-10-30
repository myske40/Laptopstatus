$ErrorActionPreference='Stop'
$source='C:\Users\msi\.gradle\caches'
$target='E:\alikasi\.build-tools\gradle-caches'
$backup='C:\Users\msi\.gradle\caches-move-backup-20261010'
$transcript='E:\alikasi\hasil-build\gradle-cache-relocation-20261010.log'
Start-Transcript -LiteralPath $transcript -Force | Out-Null
try {
  $resolvedSource=[IO.Path]::GetFullPath($source).TrimEnd('\')
  $resolvedTarget=[IO.Path]::GetFullPath($target).TrimEnd('\')
  if($resolvedSource -ne 'C:\Users\msi\.gradle\caches'){throw 'Source path validation failed.'}
  if($resolvedTarget -ne 'E:\alikasi\.build-tools\gradle-caches'){throw 'Target path validation failed.'}
  if(-not (Test-Path -LiteralPath $source -PathType Container)){throw 'Gradle cache source is missing.'}
  if(Test-Path -LiteralPath $target){throw 'Target already exists; no files were changed.'}
  if(Test-Path -LiteralPath $backup){throw 'Backup path already exists; no files were changed.'}
  if(Get-CimInstance Win32_Process | Where-Object {$_.Name -eq 'java.exe' -and $_.CommandLine -match 'GradleDaemon'}){throw 'A Gradle daemon is active; no files were changed.'}
  if((Get-Item -LiteralPath $source -Force).Attributes -band [IO.FileAttributes]::ReparsePoint){throw 'Source is already a link; no files were changed.'}
  $before=Get-ChildItem -LiteralPath $source -File -Recurse -Force -ErrorAction Stop
  $beforeCount=$before.Count; [long]$beforeBytes=($before|Measure-Object Length -Sum).Sum
  Write-Output ("Before: {0} files, {1:N2} GiB" -f $beforeCount,($beforeBytes/1GB))
  New-Item -ItemType Directory -Path $target -Force | Out-Null
  & robocopy.exe $source $target /E /COPY:DAT /DCOPY:DAT /R:1 /W:1 /XJ /NP
  $copyExit=$LASTEXITCODE
  if($copyExit -ge 8){throw "Robocopy failed with exit code $copyExit."}
  $after=Get-ChildItem -LiteralPath $target -File -Recurse -Force -ErrorAction Stop
  $afterCount=$after.Count; [long]$afterBytes=($after|Measure-Object Length -Sum).Sum
  Write-Output ("Copied: {0} files, {1:N2} GiB; robocopy code {2}" -f $afterCount,($afterBytes/1GB),$copyExit)
  if($beforeCount -ne $afterCount -or $beforeBytes -ne $afterBytes){throw 'Copy verification did not match; original cache remains untouched.'}
  Rename-Item -LiteralPath $source -NewName 'caches-move-backup-20261010' -ErrorAction Stop
  try {
    New-Item -ItemType Junction -Path $source -Target $target -ErrorAction Stop | Out-Null
    $link=Get-Item -LiteralPath $source -Force
    if(-not ($link.Attributes -band [IO.FileAttributes]::ReparsePoint) -or $link.Target -ne $target){throw 'Junction verification failed.'}
    $throughLink=Get-ChildItem -LiteralPath $source -File -Recurse -Force -ErrorAction Stop
    if($throughLink.Count -ne $beforeCount){throw 'Cache access through junction did not match.'}
  } catch {
    if(Test-Path -LiteralPath $source){Remove-Item -LiteralPath $source -Force -ErrorAction SilentlyContinue}
    Rename-Item -LiteralPath $backup -NewName 'caches' -ErrorAction SilentlyContinue
    throw
  }
  $resolvedBackup=[IO.Path]::GetFullPath($backup).TrimEnd('\')
  if($resolvedBackup -ne 'C:\Users\msi\.gradle\caches-move-backup-20261010'){throw 'Backup cleanup path validation failed.'}
  Remove-Item -LiteralPath $backup -Recurse -Force -ErrorAction Stop
  Write-Output 'Gradle cache now resolves through the original C: path to the preserved E: copy.'
  Get-CimInstance Win32_LogicalDisk -Filter "DeviceID='C:'" | Select-Object @{n='C_FreeGiB';e={[math]::Round($_.FreeSpace/1GB,2)}},@{n='C_FreePercent';e={[math]::Round(100*$_.FreeSpace/$_.Size,1)}} | Format-Table -AutoSize
} catch { Write-Error $_; exit 1 } finally { Stop-Transcript | Out-Null }
