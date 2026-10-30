@echo off
title Laptop Status - Storage Check
cd /d E:\alikasi
echo ===== PEMERIKSAAN PENYIMPANAN (READ-ONLY) =====
powershell.exe -NoProfile -ExecutionPolicy Bypass -File E:\alikasi\_storage-report.ps1
echo.
echo Selesai. Tidak ada file yang dihapus oleh pemeriksaan ini.
pause
