@echo off
setlocal
cd /d "%~dp0"
echo ===== BUILD SCRIPT =====
type _build-schedule-fix.cmd
echo ===== STORAGE CLEANUP SCRIPT =====
type _storage-cleanup.ps1
echo ===== GIT STATUS =====
git status --short
echo ===== DOTNET SDK =====
where dotnet
dotnet --list-sdks
pause
