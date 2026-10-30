@echo off
setlocal
set ADB=E:\platform-tools\adb.exe
echo === Discover wireless ADB services ===
"%ADB%" mdns services > E:\alikasi\hasil-build\Android\adb-wireless-discovery.txt 2>&1
type E:\alikasi\hasil-build\Android\adb-wireless-discovery.txt
echo.
echo === Check connected and discovered devices ===
"%ADB%" devices -l > E:\alikasi\hasil-build\Android\adb-wireless-devices.txt 2>&1
type E:\alikasi\hasil-build\Android\adb-wireless-devices.txt
echo.
echo Discovery finished.
pause
