@echo off
title Laptop Status - Pembersihan Cache Aman
cd /d E:\alikasi
echo ===== TARGET PEMBERSIHAN =====
echo Cache shader NVIDIA, cache browser tertutup, cache build dan installer sementara lama.
echo Proyek, unduhan, OneDrive, Windows Update, profil browser, dan build release dijaga.
powershell.exe -NoProfile -ExecutionPolicy Bypass -File E:\alikasi\_storage-cleanup.ps1
echo.
echo Selesai. Log tersimpan di E:\alikasi\hasil-build\storage-cleanup-20261010.log
pause
