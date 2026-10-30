@echo off
setlocal
title Laptop Status Android 1.18.2 - build, sign, install
set "PROJECT=E:\alikasi\LaptopStatus-AndroidApp"
set "SDK=E:\alikasi\.build-tools\android-sdk"
set "ANDROID_HOME=%SDK%"
set "ANDROID_SDK_ROOT=%SDK%"
set "ADB=E:\platform-tools\adb.exe"
set "APKSIGNER=%SDK%\build-tools\37.0.0\apksigner.bat"
set "APKOUT=E:\alikasi\hasil-build\Android\LaptopStatus-Android-1.18.2.apk"
set "RELEASE=E:\alikasi\hasil-build\LaptopStatus-Release-20261010-ProcessIcons"
echo ===== Android SDK =====
echo ANDROID_HOME=%ANDROID_HOME%
if not exist "%ANDROID_HOME%\platform-tools\adb.exe" echo SDK path invalid&goto failed
cd /d "%PROJECT%"
echo ===== Android release build 1.18.2 =====
call gradlew.bat assembleRelease > "%RELEASE%\build-android.log" 2>&1
set "ANDROID_EXIT=%ERRORLEVEL%"
type "%RELEASE%\build-android.log"
if not "%ANDROID_EXIT%"=="0" goto failed
echo ===== Sign APK with same local certificate as previous builds =====
call "%APKSIGNER%" sign --ks "C:\Users\msi\.android\debug.keystore" --ks-pass pass:android --ks-key-alias androiddebugkey --key-pass pass:android --out "%APKOUT%" "E:\alikasi\LaptopStatus-AndroidApp\app\build\outputs\apk\release\app-release-unsigned.apk"
if errorlevel 1 goto failed
echo ===== Verify APK signature =====
call "%APKSIGNER%" verify --verbose --print-certs "%APKOUT%"
if errorlevel 1 goto failed
copy /y "%APKOUT%" "%RELEASE%\LaptopStatus-Android-1.18.2.apk"
if errorlevel 1 goto failed
echo ===== Device detection and install =====
"%ADB%" devices -l
"%ADB%" -d install -r "%APKOUT%"
if errorlevel 1 goto devicefailed
"%ADB%" -d shell input keyevent 224
"%ADB%" -d shell am force-stop com.example.laptopstatus
"%ADB%" -d shell monkey -p com.example.laptopstatus 1
if errorlevel 1 goto devicefailed
echo ===== Android 1.18.2 build, signature check, and install succeeded =====
echo APK: %APKOUT%
pause
exit /b 0
:devicefailed
echo APK is built and signed. Install/reopen failed; leave logs for device review.
pause
exit /b 2
:failed
echo Android build or signing failed. Review log: %RELEASE%\build-android.log
pause
exit /b 1
