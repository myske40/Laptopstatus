@echo off
setlocal
set ADB=E:\platform-tools\adb.exe
echo ===== Try the known phone codename/serial as a wireless host =====
"%ADB%" connect 24094RAD4G > E:\alikasi\hasil-build\Android\adb-codename-connect.txt 2>&1
type E:\alikasi\hasil-build\Android\adb-codename-connect.txt
echo.
echo ===== Device list after the attempt =====
"%ADB%" devices -l > E:\alikasi\hasil-build\Android\adb-codename-devices.txt 2>&1
type E:\alikasi\hasil-build\Android\adb-codename-devices.txt
echo.
echo Wireless codename attempt finished.
pause
