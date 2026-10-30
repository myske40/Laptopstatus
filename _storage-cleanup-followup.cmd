@echo off
title Laptop Status - Safe temporary cache cleanup
powershell.exe -NoProfile -ExecutionPolicy Bypass -File E:\alikasi\_storage-cleanup-followup.ps1
echo.
echo Follow-up cleanup complete.
pause
