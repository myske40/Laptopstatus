@echo off
setlocal
cd /d E:\alikasi
title Laptop Status - Bersihkan Cache dan Tampilkan Log
set "USERPROFILE=C:\Users\msi"
set "LOCALAPPDATA=C:\Users\msi\AppData\Local"
set "APPDATA=C:\Users\msi\AppData\Roaming"
set "LOG=E:\alikasi\hasil-build\storage-cleanup-console.log"
echo ===== MULAI PEMBERSIHAN CACHE =====
echo Profil: %USERPROFILE%
echo Cache lokal: %LOCALAPPDATA%
echo Script: E:\alikasi\_storage-cleanup.ps1
echo powershell.exe -NoProfile -ExecutionPolicy Bypass -File E:\alikasi\_storage-cleanup.ps1
powershell.exe -NoProfile -ExecutionPolicy Bypass -File E:\alikasi\_storage-cleanup.ps1 > "%LOG%" 2>&1
set "CLEAN_EXIT=%ERRORLEVEL%"
echo ===== HASIL POWERSHELL (EXIT %CLEAN_EXIT%) =====
if exist "%LOG%" type "%LOG%"
echo ===== SELESAI =====
pause
