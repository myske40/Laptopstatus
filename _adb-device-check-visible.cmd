@echo off
setlocal
echo Running adb devices -l ...
E:\platform-tools\adb.exe devices -l > E:\alikasi\hasil-build\Android\adb-device-check.txt 2>&1
type E:\alikasi\hasil-build\Android\adb-device-check.txt
echo.
echo Device check complete.
pause
