@echo off
setlocal
cd /d "%~dp0"
echo ===== APP PROJECT SETTINGS =====
type LaptopStatus-WindowsApp\LaptopStatus.csproj
echo ===== INSTALLER PROJECT SETTINGS =====
type LaptopStatus-Setup\LaptopStatus-Setup.csproj
echo ===== PAYLOAD CONTENTS =====
dir LaptopStatus-Setup\payload /s
echo ===== UNINSTALL SCRIPT =====
dir LaptopStatus-WindowsApp\installer
echo ===== TARGET SOURCE METHODS =====
findstr /n /c:"SaveScheduledPower" /c:"EnsureScheduledPowerTask" /c:"EnsureScheduledPowerResumeStartup" /c:"DeleteScheduledPowerResumeStartup" LaptopStatus-WindowsApp\MainWindow.xaml.cs
pause
