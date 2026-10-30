@echo off
setlocal
cd /d E:\alikasi
set "TOOLS=D:\LaptopStatusBuildTools"
echo ===== PORTABLE SDK DIRECTORY =====
dir /a "%TOOLS%"
echo ===== INSTALLED SDK FILES =====
dir /a /s "%TOOLS%\dotnet\sdk"
echo ===== INSTALLED SDK VERSION =====
if exist "%TOOLS%\dotnet\dotnet.exe" "%TOOLS%\dotnet\dotnet.exe" --list-sdks
echo ===== SDK INSTALLER SCRIPT =====
if exist "%TOOLS%\dotnet-install.ps1" dir "%TOOLS%\dotnet-install.ps1"
echo ===== PUBLISH LOG =====
type hasil-build\LaptopStatus-Release-20261010-ScheduleFix\build-windows.log
pause
