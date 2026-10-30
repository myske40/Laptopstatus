@echo off
title Laptop Status - Relocate Gradle cache while preserving it
powershell.exe -NoProfile -ExecutionPolicy Bypass -File E:\alikasi\_move-gradle-cache.ps1
echo.
echo Cache relocation process finished.
pause
