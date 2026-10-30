@echo off
setlocal
cd /d C:\LaptopStatus
echo ===== Start updated PC app from its install folder =====
start "" /D "C:\LaptopStatus" "C:\LaptopStatus\LaptopStatus.exe"
timeout /t 5 /nobreak >nul
echo ===== Check process =====
tasklist /fi "IMAGENAME eq LaptopStatus.exe"
powershell.exe -NoProfile -Command "$p=Get-Process -Name LaptopStatus -ErrorAction SilentlyContinue | Select-Object -First 1; if($p){$v=(Get-Item -LiteralPath $p.Path).VersionInfo; 'Running: '+$p.Path+' v'+$v.FileVersion} else {'Not running'}"
echo.
echo If it did not start, the old executable backup is in the release folder.
pause
