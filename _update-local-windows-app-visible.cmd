@echo off
setlocal
set "RELEASE=E:\alikasi\hasil-build\LaptopStatus-Release-20261011-ProcessUi"
set "SOURCE=E:\alikasi\hasil-build\Windows\LaptopStatus.exe"
set "TARGET=C:\LaptopStatus\LaptopStatus.exe"
echo ===== Back up current PC app v2.11.13 =====
if not exist "%TARGET%" goto missing
copy /y "%TARGET%" "%RELEASE%\LaptopStatus-2.11.13-installed-backup.exe"
if errorlevel 1 goto failed
echo ===== Stop app so the executable can be updated =====
taskkill /im LaptopStatus.exe /t /f
 timeout /t 2 /nobreak >nul
echo ===== Install PC backend v2.11.15 =====
copy /y "%SOURCE%" "%TARGET%"
if errorlevel 1 goto failed
echo ===== Restart PC app =====
start "" "%TARGET%"
timeout /t 3 /nobreak >nul
powershell.exe -NoProfile -Command "$p=Get-Process -Name LaptopStatus -ErrorAction SilentlyContinue | Select-Object -First 1; if($p){$v=(Get-Item -LiteralPath $p.Path).VersionInfo; 'Running PC version: '+$v.FileVersion} else {'App did not report as running'}"
echo Previous executable backup: %RELEASE%\LaptopStatus-2.11.13-installed-backup.exe
echo Update complete.
pause
exit /b 0
:missing
echo Installed app path was not found. No update was applied.
pause
exit /b 2
:failed
echo Update failed. Restore the saved executable if needed.
pause
exit /b 1
