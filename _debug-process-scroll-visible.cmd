@echo off
setlocal
set ADB=E:\platform-tools\adb.exe
set DEVICE_FILE=/sdcard/laptopstatus-process-check.png
set OUT=E:\alikasi\hasil-build\Android\android-process-scroll-check.png
echo === Device check ===
"%ADB%" devices -l
echo.
echo === Scroll the Proses page ===
"%ADB%" shell input swipe 1200 680 1200 250 500
timeout /t 2 /nobreak >nul
echo.
echo === Capture current Android screen ===
"%ADB%" shell screencap -p %DEVICE_FILE%
"%ADB%" pull %DEVICE_FILE% "%OUT%"
echo Screenshot path: %OUT%
echo.
echo === Complete. This window stays open for review. ===
pause
