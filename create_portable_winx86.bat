@echo off
setlocal enabledelayedexpansion

echo =========================================================
echo   FileSender LAN - Windows Portable Generator
echo   Platform: Windows x86 (Self-Contained Portable Folder)
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

set "TARGET_DIR=%PUBLISH_DIR%\FileSender_Windows_Portable"
set "ZIP_FILE=%PUBLISH_DIR%\FileSender-Windows-Portable-x86.zip"

echo Versi Aplikasi : v%APP_VERSION%
echo.

echo [1/3] Membersihkan folder portable sebelumnya di folder publish...
if exist "%TARGET_DIR%" rmdir /s /q "%TARGET_DIR%"
if exist "%ZIP_FILE%" del /f /q "%ZIP_FILE%"
mkdir "%TARGET_DIR%"

echo.
echo [2/3] Melakukan build dan publish portable Windows x86 (.NET 10)...
echo (Format: Loose Portable Folder - Tidak disatukan menjadi 1 file EXE)
dotnet publish "%~dp0FileSender.csproj" -c Release -r win-x86 --self-contained true -p:PublishSingleFile=false -o "%TARGET_DIR%"

if %ERRORLEVEL% NEQ 0 (
    echo [ERROR] Gagal melakukan compile FileSender Windows Portable.
    pause
    exit /b %ERRORLEVEL%
)

echo.
echo [3/3] Menyiapkan paket file dan mengompres ke format ZIP...
copy "%~dp0version.conf" "%TARGET_DIR%\version.conf" >nul 2>&1
copy "%~dp0app_icon.ico" "%TARGET_DIR%\app_icon.ico" >nul
copy "%~dp0app_icon.png" "%TARGET_DIR%\app_icon.png" >nul
copy "%~dp0installers\win\README_PORTABLE.txt" "%TARGET_DIR%\README.txt" >nul

powershell -NoProfile -Command "Add-Type -AssemblyName System.IO.Compression.FileSystem; if (Test-Path '%ZIP_FILE%') { Remove-Item -Force '%ZIP_FILE%' }; [System.IO.Compression.ZipFile]::CreateFromDirectory('%TARGET_DIR%', '%ZIP_FILE%')"

rem Salin juga ke Desktop untuk kemudahan akses
if exist "%DESKTOP_DIR%" (
    copy "%ZIP_FILE%" "%DESKTOP_DIR%\FileSender-Windows-Portable-x86.zip" >nul 2>&1
)

echo.
echo =========================================================
echo   [BERHASIL] FileSender Portable Windows Telah Dibuat!
echo =========================================================
echo Lokasi Folder Portable : %TARGET_DIR%
echo Lokasi File ZIP        : %ZIP_FILE%
if exist "%DESKTOP_DIR%" echo Salinan Desktop       : %DESKTOP_DIR%\FileSender-Windows-Portable-x86.zip
echo.
echo Struktur Portable:
echo - Folder '%TARGET_DIR%' berisi FileSender.exe lengkap dengan semua DLL runtime.
echo - Bukan 1 file exe gabungan/packer. Anda tinggal bawa folder ini ke mana saja.
echo.

explorer.exe "%PUBLISH_DIR%"
pause
