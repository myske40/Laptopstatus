@echo off
title Laptop Status - Move Gradle cache to E
cd /d E:\alikasi\LaptopStatus-AndroidApp
set "ANDROID_HOME=E:\alikasi\.build-tools\android-sdk"
set "ANDROID_SDK_ROOT=E:\alikasi\.build-tools\android-sdk"
echo Stopping only Gradle background daemons before moving the cache...
call gradlew.bat --stop --console=plain > E:\alikasi\hasil-build\gradle-stop-20261010.log 2>&1
type E:\alikasi\hasil-build\gradle-stop-20261010.log
pause
