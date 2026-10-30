@echo off
setlocal
cd /d E:\alikasi
set "DOTNET_ROOT=D:\LaptopStatusBuildTools\dotnet"
set "DOTNET_CLI_HOME=D:\LaptopStatusBuildTools\.dotnet-cli"
set "PATH=%DOTNET_ROOT%;%PATH%"
echo ===== ENVIRONMENT =====
echo DOTNET_ROOT=%DOTNET_ROOT%
echo PATH=%PATH%
echo ===== GLOBAL JSON =====
if exist global.json (type global.json) else (echo No global.json in repo root)
echo ===== DOTNET SDK LIST =====
dotnet --list-sdks
echo ===== DOTNET INFO =====
dotnet --info
echo ===== SDK RESOLUTION =====
dotnet --version
pause
