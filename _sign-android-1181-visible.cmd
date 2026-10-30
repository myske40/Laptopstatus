@echo off
title Laptop Status Android 1.18.1 signing and verification
set "APKSIGNER=E:\alikasi\.build-tools\android-sdk\build-tools\37.0.0\apksigner.bat"
set "UNSIGNED=E:\alikasi\LaptopStatus-AndroidApp\app\build\outputs\apk\release\app-release-unsigned.apk"
set "SIGNED=E:\alikasi\hasil-build\Android\LaptopStatus-Android-1.18.1.apk"
if not exist "E:\alikasi\hasil-build\Android" mkdir "E:\alikasi\hasil-build\Android"
echo Signing release APK with the same local Android Debug certificate as prior releases...
call "%APKSIGNER%" sign --ks "C:\Users\msi\.android\debug.keystore" --ks-pass pass:android --ks-key-alias androiddebugkey --key-pass pass:android --out "%SIGNED%" "%UNSIGNED%"
if errorlevel 1 goto failed
echo.
echo ===== Signature verification =====
call "%APKSIGNER%" verify --verbose --print-certs "%SIGNED%"
if errorlevel 1 goto failed
echo.
echo ===== Release APK =====
dir "%SIGNED%"
echo Signing and verification succeeded.
pause
exit /b 0
:failed
echo Signing or verification failed. See output above.
pause
exit /b 1
