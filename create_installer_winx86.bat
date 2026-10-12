@echo off
setlocal enabledelayedexpansion

echo =========================================================
echo   FileSender LAN - Windows Installer Generator
echo   Platform: Windows x86 (Self-Contained Standalone)
echo =========================================================
echo.

set "PUBLISH_DIR=%~dp0publish"
if not exist "%PUBLISH_DIR%" mkdir "%PUBLISH_DIR%"

set "DESKTOP_DIR=%USERPROFILE%\Desktop"
if not exist "%DESKTOP_DIR%" (
    set "DESKTOP_DIR=%HOMEDRIVE%%HOMEPATH%\Desktop"
)

set "APP_VERSION=0.1.0"
if exist "%~dp0version.conf" (
    set /p APP_VERSION=<"%~dp0version.conf"
    set "APP_VERSION=!APP_VERSION: =!"
)

set "TARGET_DIR=%PUBLISH_DIR%\FileSender_Windows_Installer"
set "ZIP_FILE=%PUBLISH_DIR%\FileSender-Windows-x86-Installer.zip"
set "TEMP_PUBLISH=%~dp0bin\publish_win_temp"

echo Versi Aplikasi : v%APP_VERSION%
echo.

echo [1/4] Membersihkan folder installer sebelumnya di folder publish...
if exist "%TEMP_PUBLISH%" rmdir /s /q "%TEMP_PUBLISH%"
if exist "%TARGET_DIR%" rmdir /s /q "%TARGET_DIR%"
if exist "%ZIP_FILE%" del /f /q "%ZIP_FILE%"
mkdir "%TARGET_DIR%"

echo.
echo [2/4] Melakukan build dan publish standalone Windows x86 (.NET 10)...
dotnet publish "%~dp0FileSender.csproj" -c Release -r win-x86 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -o "%TEMP_PUBLISH%"

if %ERRORLEVEL% NEQ 0 (
    echo [ERROR] Gagal melakukan compile FileSender Windows.
    pause
    exit /b %ERRORLEVEL%
)

echo.
echo [3/4] Menyiapkan paket installer di folder publish...
copy "%TEMP_PUBLISH%\FileSender.exe" "%TARGET_DIR%\FileSender.exe" >nul
copy "%~dp0version.conf" "%TARGET_DIR%\version.conf" >nul 2>&1
copy "%~dp0app_icon.ico" "%TARGET_DIR%\app_icon.ico" >nul
copy "%~dp0app_icon.png" "%TARGET_DIR%\app_icon.png" >nul
copy "%~dp0installers\win\Install_Shortcut.bat" "%TARGET_DIR%\Install_Shortcut.bat" >nul
copy "%~dp0installers\win\README.txt" "%TARGET_DIR%\README.txt" >nul

echo.
echo [4/4] Mengompres installer ke format ZIP...
powershell -NoProfile -Command "Add-Type -AssemblyName System.IO.Compression.FileSystem; if (Test-Path '%ZIP_FILE%') { Remove-Item -Force '%ZIP_FILE%' }; [System.IO.Compression.ZipFile]::CreateFromDirectory('%TARGET_DIR%', '%ZIP_FILE%')"

rem Salin juga ke Desktop untuk kemudahan akses
if exist "%DESKTOP_DIR%" (
    copy "%ZIP_FILE%" "%DESKTOP_DIR%\FileSender-Windows-x86-Installer.zip" >nul 2>&1
)

if exist "%TEMP_PUBLISH%" rmdir /s /q "%TEMP_PUBLISH%"

echo.
echo =========================================================
echo   [BERHASIL] Installer Windows Telah Selesai Dibuat!
echo =========================================================
echo Lokasi Folder Proyek : %TARGET_DIR%
echo Lokasi File ZIP      : %ZIP_FILE%
if exist "%DESKTOP_DIR%" echo Salinan Desktop     : %DESKTOP_DIR%\FileSender-Windows-x86-Installer.zip
echo.

explorer.exe "%PUBLISH_DIR%"
pause
