@echo off
setlocal
 title Laptop Status - Windows 2.11.15 / Android 1.18.3
cd /d E:\alikasi
set "RELEASE=E:\alikasi\hasil-build\LaptopStatus-Release-20261011-ProcessUi"
set "TOOLS=D:\LaptopStatusBuildTools"
set "DOTNET_ROOT=%TOOLS%\dotnet"
set "DOTNET_CLI_HOME=%TOOLS%\.dotnet-cli"
set "NUGET_PACKAGES=%TOOLS%\nuget"
set "TEMP=%TOOLS%\tmp"
set "TMP=%TEMP%"
set "PATH=%DOTNET_ROOT%;%PATH%"
set "SDK=E:\alikasi\.build-tools\android-sdk"
set "APKSIGNER=%SDK%\build-tools\37.0.0\apksigner.bat"
set "APKOUT=E:\alikasi\hasil-build\Android\LaptopStatus-Android-1.18.3.apk"
if not exist "%TEMP%" mkdir "%TEMP%"
if not exist "%RELEASE%\Windows" mkdir "%RELEASE%\Windows"
if not exist "%RELEASE%\Installer" mkdir "%RELEASE%\Installer"
if not exist "E:\alikasi\hasil-build\Android" mkdir "E:\alikasi\hasil-build\Android"
if not exist "E:\alikasi\hasil-build\Windows" mkdir "E:\alikasi\hasil-build\Windows"
echo ===== Runtime versions =====
where dotnet
dotnet --version
echo ===== Build Windows app 2.11.15 =====
dotnet publish LaptopStatus-WindowsApp\LaptopStatus.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:DebugType=None -p:DebugSymbols=false -o "%RELEASE%\Windows" > "%RELEASE%\build-windows.log" 2>&1
set "WINDOWS_EXIT=%ERRORLEVEL%"
type "%RELEASE%\build-windows.log"
if not "%WINDOWS_EXIT%"=="0" goto failed
copy /y LaptopStatus-WindowsApp\installer\Uninstall-LaptopStatus.ps1 "%RELEASE%\Windows\Uninstall-LaptopStatus.ps1"
if errorlevel 1 goto failed
copy /y "%RELEASE%\Windows\LaptopStatus.exe" "E:\alikasi\hasil-build\Windows\LaptopStatus.exe"
if errorlevel 1 goto failed
echo ===== Package Windows app for installer =====
powershell.exe -NoProfile -ExecutionPolicy Bypass -Command "$ErrorActionPreference='Stop'; Compress-Archive -Path 'E:\alikasi\hasil-build\LaptopStatus-Release-20261011-ProcessUi\Windows\*' -DestinationPath 'E:\alikasi\hasil-build\LaptopStatus-Release-20261011-ProcessUi\LaptopStatusPayload.zip' -CompressionLevel Optimal -Force"
if errorlevel 1 goto failed
copy /y "%RELEASE%\LaptopStatusPayload.zip" LaptopStatus-Setup\payload\LaptopStatusPayload.zip
if errorlevel 1 goto failed
echo ===== Build installer 2.12.6 =====
dotnet publish LaptopStatus-Setup\LaptopStatus-Setup.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:DebugType=None -p:DebugSymbols=false -o "%RELEASE%\Installer" > "%RELEASE%\build-installer.log" 2>&1
set "INSTALLER_EXIT=%ERRORLEVEL%"
type "%RELEASE%\build-installer.log"
if not "%INSTALLER_EXIT%"=="0" goto failed
copy /y "%RELEASE%\Installer\LaptopStatus-Setup.exe" "E:\alikasi\hasil-build\LaptopStatus-Setup-2.12.6.exe"
if errorlevel 1 goto failed
echo ===== Build Android app 1.18.3 =====
cd /d E:\alikasi\LaptopStatus-AndroidApp
set "ANDROID_HOME=%SDK%"
set "ANDROID_SDK_ROOT=%SDK%"
call gradlew.bat assembleRelease > "%RELEASE%\build-android.log" 2>&1
set "ANDROID_EXIT=%ERRORLEVEL%"
type "%RELEASE%\build-android.log"
if not "%ANDROID_EXIT%"=="0" goto failed
echo ===== Sign Android APK with the existing local key =====
call "%APKSIGNER%" sign --ks "C:\Users\msi\.android\debug.keystore" --ks-pass pass:android --ks-key-alias androiddebugkey --key-pass pass:android --out "%APKOUT%" "E:\alikasi\LaptopStatus-AndroidApp\app\build\outputs\apk\release\app-release-unsigned.apk"
if errorlevel 1 goto failed
echo ===== Verify Android APK signature =====
call "%APKSIGNER%" verify --verbose --print-certs "%APKOUT%"
if errorlevel 1 goto failed
copy /y "%APKOUT%" "%RELEASE%\LaptopStatus-Android-1.18.3.apk"
if errorlevel 1 goto failed
echo.
echo ===== BUILD COMPLETE =====
echo Windows app: E:\alikasi\hasil-build\Windows\LaptopStatus.exe
echo Windows installer: E:\alikasi\hasil-build\LaptopStatus-Setup-2.12.6.exe
echo Android APK: %APKOUT%
echo.
echo No ADB device was detected over USB or Wireless debugging; APK is ready, but phone install was skipped.
pause
exit /b 0
:failed
echo.
echo BUILD FAILED. Review the build log under %RELEASE%.
pause
exit /b 1
