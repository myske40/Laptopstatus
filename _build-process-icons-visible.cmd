@echo off
setlocal
 title Laptop Status - build 2.11.14 / Android 1.18.2
cd /d E:\alikasi
set "RELEASE=hasil-build\LaptopStatus-Release-20261010-ProcessIcons"
set "TOOLS=D:\LaptopStatusBuildTools"
set "DOTNET_ROOT=%TOOLS%\dotnet"
set "DOTNET_CLI_HOME=%TOOLS%\.dotnet-cli"
set "NUGET_PACKAGES=%TOOLS%\nuget"
set "TEMP=%TOOLS%\tmp"
set "TMP=%TEMP%"
set "PATH=%DOTNET_ROOT%;%PATH%"
where dotnet
dotnet --version
set "SDK=E:\alikasi\.build-tools\android-sdk"
set "ADB=E:\platform-tools\adb.exe"
set "APKSIGNER=%SDK%\build-tools\37.0.0\apksigner.bat"
set "APKOUT=E:\alikasi\hasil-build\Android\LaptopStatus-Android-1.18.2.apk"
if not exist "%TEMP%" mkdir "%TEMP%"
if not exist "%RELEASE%\Windows" mkdir "%RELEASE%\Windows"
if not exist "%RELEASE%\Installer" mkdir "%RELEASE%\Installer"
if not exist "hasil-build\Android" mkdir "hasil-build\Android"
echo ===== Build Windows app 2.11.14 =====
dotnet publish LaptopStatus-WindowsApp\LaptopStatus.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:DebugType=None -p:DebugSymbols=false -o "%RELEASE%\Windows" > "%RELEASE%\build-windows.log" 2>&1
set "WINDOWS_EXIT=%ERRORLEVEL%"
type "%RELEASE%\build-windows.log"
if not "%WINDOWS_EXIT%"=="0" goto failed
copy /y LaptopStatus-WindowsApp\installer\Uninstall-LaptopStatus.ps1 "%RELEASE%\Windows\Uninstall-LaptopStatus.ps1"
if errorlevel 1 goto failed
if exist LaptopStatus-Setup\payload\LaptopStatusPayload.zip copy /y LaptopStatus-Setup\payload\LaptopStatusPayload.zip "%RELEASE%\LaptopStatusPayload-previous.zip"
echo ===== Package updated Windows app for installer =====
powershell.exe -NoProfile -ExecutionPolicy Bypass -Command "$ErrorActionPreference='Stop'; Compress-Archive -Path 'E:\alikasi\hasil-build\LaptopStatus-Release-20261010-ProcessIcons\Windows\*' -DestinationPath 'E:\alikasi\hasil-build\LaptopStatus-Release-20261010-ProcessIcons\LaptopStatusPayload.zip' -CompressionLevel Optimal -Force"
if errorlevel 1 goto failed
copy /y "%RELEASE%\LaptopStatusPayload.zip" LaptopStatus-Setup\payload\LaptopStatusPayload.zip
if errorlevel 1 goto failed
echo ===== Build installer 2.12.5 =====
dotnet publish LaptopStatus-Setup\LaptopStatus-Setup.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:DebugType=None -p:DebugSymbols=false -o "%RELEASE%\Installer" > "%RELEASE%\build-installer.log" 2>&1
set "SETUP_EXIT=%ERRORLEVEL%"
type "%RELEASE%\build-installer.log"
if not "%SETUP_EXIT%"=="0" goto failed
copy /y "%RELEASE%\Installer\LaptopStatus-Setup.exe" "E:\alikasi\hasil-build\LaptopStatus-Setup-2.12.5.exe"
if errorlevel 1 goto failed
copy /y "%RELEASE%\Windows\LaptopStatus.exe" "E:\alikasi\hasil-build\Windows\LaptopStatus.exe"
if errorlevel 1 goto failed
echo ===== Build Android release 1.18.2 =====
cd /d E:\alikasi\LaptopStatus-AndroidApp
call gradlew.bat assembleRelease > "E:\alikasi\hasil-build\LaptopStatus-Release-20261010-ProcessIcons\build-android.log" 2>&1
set "ANDROID_EXIT=%ERRORLEVEL%"
type "E:\alikasi\hasil-build\LaptopStatus-Release-20261010-ProcessIcons\build-android.log"
if not "%ANDROID_EXIT%"=="0" goto failed
call "%APKSIGNER%" sign --ks "C:\Users\msi\.android\debug.keystore" --ks-pass pass:android --ks-key-alias androiddebugkey --key-pass pass:android --out "%APKOUT%" "E:\alikasi\LaptopStatus-AndroidApp\app\build\outputs\apk\release\app-release-unsigned.apk"
if errorlevel 1 goto failed
call "%APKSIGNER%" verify --verbose --print-certs "%APKOUT%"
if errorlevel 1 goto failed
copy /y "%APKOUT%" "E:\alikasi\hasil-build\LaptopStatus-Release-20261010-ProcessIcons\LaptopStatus-Android-1.18.2.apk"
if errorlevel 1 goto failed
echo ===== Connected Android devices =====
"%ADB%" devices -l
"%ADB%" -d install -r "%APKOUT%"
if errorlevel 1 goto devicefailed
"%ADB%" -d shell input keyevent 224
"%ADB%" -d shell am force-stop com.example.laptopstatus
"%ADB%" -d shell monkey -p com.example.laptopstatus 1
if errorlevel 1 goto devicefailed
echo ===== BUILD AND APK INSTALL SUCCEEDED =====
echo Android APK: %APKOUT%
echo Windows installer: E:\alikasi\hasil-build\LaptopStatus-Setup-2.12.5.exe
echo Windows app EXE: E:\alikasi\hasil-build\Windows\LaptopStatus.exe
echo Logs: E:\alikasi\hasil-build\LaptopStatus-Release-20261010-ProcessIcons\
pause
exit /b 0
:devicefailed
echo Build succeeded, but the phone install/restart step failed. APK and logs remain available above.
echo Check that ADB lists one authorized device.
pause
exit /b 2
:failed
echo A build step failed. Review the corresponding log in %RELEASE%.
pause
exit /b 1
