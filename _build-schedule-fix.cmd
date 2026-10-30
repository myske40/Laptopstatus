@echo off
setlocal
title Laptop Status - Build Windows 2.11.13
cd /d E:\alikasi
set "RELEASE=hasil-build\LaptopStatus-Release-20261010-ScheduleFix"
set "RESULT=0"
set "TOOLS=D:\LaptopStatusBuildTools"
set "DOTNET_ROOT=%TOOLS%\dotnet"
set "DOTNET_CLI_HOME=%TOOLS%\.dotnet-cli"
set "NUGET_PACKAGES=%TOOLS%\nuget"
set "TEMP=%TOOLS%\tmp"
set "TMP=%TEMP%"
if not exist "%TEMP%" mkdir "%TEMP%"
if not exist "%TOOLS%" mkdir "%TOOLS%"
if not exist "%DOTNET_ROOT%\dotnet.exe" (
  echo ===== MENYIAPKAN .NET SDK 8 DI D:\LaptopStatusBuildTools =====
  powershell.exe -NoProfile -ExecutionPolicy Bypass -Command "Invoke-WebRequest -UseBasicParsing -Uri 'https://dot.net/v1/dotnet-install.ps1' -OutFile '%TOOLS%\dotnet-install.ps1'"
  if errorlevel 1 goto sdkfailed
  powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%TOOLS%\dotnet-install.ps1" -Channel 8.0 -InstallDir "%DOTNET_ROOT%" -NoPath
  if errorlevel 1 goto sdkfailed
)
set "PATH=%DOTNET_ROOT%;%PATH%"
echo ===== SDK YANG DIGUNAKAN =====
where dotnet
dotnet --list-sdks
dotnet --version
if errorlevel 1 goto sdkfailed
echo ===== BUILD APLIKASI WINDOWS 2.11.13 =====
if not exist "%RELEASE%\Windows" mkdir "%RELEASE%\Windows"
echo dotnet publish LaptopStatus-WindowsApp\LaptopStatus.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:DebugType=None -p:DebugSymbols=false -o hasil-build\LaptopStatus-Release-20261010-ScheduleFix\Windows
dotnet publish LaptopStatus-WindowsApp\LaptopStatus.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:DebugType=None -p:DebugSymbols=false -o "%RELEASE%\Windows" > "%RELEASE%\build-windows.log" 2>&1
set "BUILD_EXIT=%ERRORLEVEL%"
type "%RELEASE%\build-windows.log"
echo.
echo Exit code build aplikasi: %BUILD_EXIT%
if not "%BUILD_EXIT%"=="0" goto buildfailed

echo ===== MENYIAPKAN PAYLOAD INSTALLER =====
copy /y LaptopStatus-WindowsApp\installer\Uninstall-LaptopStatus.ps1 "%RELEASE%\Windows\Uninstall-LaptopStatus.ps1"
if errorlevel 1 goto packagefailed
if exist LaptopStatus-Setup\payload\LaptopStatusPayload.zip (
  copy /y LaptopStatus-Setup\payload\LaptopStatusPayload.zip "%RELEASE%\LaptopStatusPayload-previous.zip"
  if errorlevel 1 goto packagefailed
)
powershell.exe -NoProfile -ExecutionPolicy Bypass -Command "$ErrorActionPreference='Stop'; Compress-Archive -Path 'E:\alikasi\hasil-build\LaptopStatus-Release-20261010-ScheduleFix\Windows\*' -DestinationPath 'E:\alikasi\hasil-build\LaptopStatus-Release-20261010-ScheduleFix\LaptopStatusPayload-new.zip' -CompressionLevel Optimal -Force"
if errorlevel 1 goto packagefailed
copy /y "%RELEASE%\LaptopStatusPayload-new.zip" LaptopStatus-Setup\payload\LaptopStatusPayload.zip
if errorlevel 1 goto packagefailed

echo ===== BUILD INSTALLER WINDOWS 2.12.4 =====
if not exist "%RELEASE%\Installer" mkdir "%RELEASE%\Installer"
dotnet publish LaptopStatus-Setup\LaptopStatus-Setup.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:DebugType=None -p:DebugSymbols=false -o "%RELEASE%\Installer" > "%RELEASE%\build-installer.log" 2>&1
set "SETUP_EXIT=%ERRORLEVEL%"
type "%RELEASE%\build-installer.log"
echo Exit code build installer: %SETUP_EXIT%
if not "%SETUP_EXIT%"=="0" goto installerfailed

echo ===== MENYALIN INSTALLER KE HASIL-BUILD =====
copy /y "%RELEASE%\Installer\LaptopStatus-Setup.exe" hasil-build\LaptopStatus-Setup-2.12.4.exe
if errorlevel 1 goto packagefailed
copy /y "%RELEASE%\Installer\LaptopStatus-Setup.exe" hasil-build\LaptopStatus-Setup.exe
if errorlevel 1 goto packagefailed
echo BUILD BERHASIL. Installer: hasil-build\LaptopStatus-Setup-2.12.4.exe
echo Versi aplikasi Windows 2.11.13; versi installer 2.12.4.
goto done

:buildfailed
set "RESULT=1"
echo Build aplikasi gagal. Log: %RELEASE%\build-windows.log
goto done
:installerfailed
set "RESULT=1"
echo Build installer gagal. Log: %RELEASE%\build-installer.log
goto done
:packagefailed
set "RESULT=1"
echo Penyiapan payload atau penyalinan installer gagal. Periksa log CMD di atas.
:done
echo Jendela ini dibiarkan terbuka agar log bisa diperiksa.
pause
exit /b %RESULT%

:sdkfailed
echo Pemasangan .NET SDK gagal. Jadwal shutdown sudah diperbaiki di sumber, tetapi aplikasi belum dapat dibangun.
pause
exit /b 1
