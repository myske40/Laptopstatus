@echo off
setlocal
cd /d E:\alikasi
title Laptop Status - Audit file sementara dan dump
powershell.exe -NoProfile -ExecutionPolicy Bypass -File E:\alikasi\_storage-candidates.ps1
pause
