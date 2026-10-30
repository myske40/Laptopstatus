@echo off
title Laptop Status Android 1.18.1 build
cd /d E:\alikasi\LaptopStatus-AndroidApp
set "ANDROID_HOME=E:\alikasi\.build-tools\android-sdk"
set "ANDROID_SDK_ROOT=E:\alikasi\.build-tools\android-sdk"
echo Building Laptop Status Android 1.18.1 with SDK on E:...
call gradlew.bat assembleRelease --console=plain > "E:\alikasi\hasil-build\LaptopStatus-Android-1.18.1-build.log" 2>&1
set "BUILD_RC=%ERRORLEVEL%"
echo.
echo ===== Gradle output =====
type "E:\alikasi\hasil-build\LaptopStatus-Android-1.18.1-build.log"
echo.
echo Build exit code: %BUILD_RC%
pause
