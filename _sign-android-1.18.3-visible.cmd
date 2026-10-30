@echo off
setlocal
set "SDK=E:\alikasi\.build-tools\android-sdk"
set "APKSIGNER=%SDK%\build-tools\37.0.0\apksigner.bat"
set "UNSIGNED=E:\alikasi\LaptopStatus-AndroidApp\app\build\outputs\apk\release\app-release-unsigned.apk"
set "APKOUT=E:\alikasi\hasil-build\Android\LaptopStatus-Android-1.18.3.apk"
set "LOG=E:\alikasi\hasil-build\LaptopStatus-Release-20261011-ProcessUi\sign-android.log"
echo ===== Sign APK 1.18.3 =====
call "%APKSIGNER%" sign --ks "C:\Users\msi\.android\debug.keystore" --ks-pass pass:android --ks-key-alias androiddebugkey --key-pass pass:android --out "%APKOUT%" "%UNSIGNED%" > "%LOG%" 2>&1
set "SIGN_EXIT=%ERRORLEVEL%"
type "%LOG%"
if not "%SIGN_EXIT%"=="0" goto failed
echo ===== Verify APK signature =====
call "%APKSIGNER%" verify --verbose --print-certs "%APKOUT%" >> "%LOG%" 2>&1
set "VERIFY_EXIT=%ERRORLEVEL%"
type "%LOG%"
if not "%VERIFY_EXIT%"=="0" goto failed
copy /y "%APKOUT%" "E:\alikasi\hasil-build\LaptopStatus-Release-20261011-ProcessUi\LaptopStatus-Android-1.18.3.apk"
if errorlevel 1 goto failed
echo APK signing and verification succeeded.
pause
exit /b 0
:failed
echo APK signing/verification failed; see %LOG%.
pause
exit /b 1

