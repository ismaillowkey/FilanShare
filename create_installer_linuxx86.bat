@echo off
setlocal enabledelayedexpansion

echo =========================================================
echo   FileSender LAN - Linux Installer Generator
echo   Platform: Linux x86_64 (Self-Contained Standalone)
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

set "TARGET_DIR=%PUBLISH_DIR%\FileSender_Linux_Installer"
set "TAR_FILE=%PUBLISH_DIR%\FileSender-Linux-x86-Installer.tar.gz"
set "TEMP_PUBLISH=%~dp0bin\publish_linux_temp"

echo Versi Aplikasi : v%APP_VERSION%
echo.

echo [1/4] Membersihkan folder build sebelumnya di folder publish...
if exist "%TEMP_PUBLISH%" rmdir /s /q "%TEMP_PUBLISH%"
if exist "%TARGET_DIR%" rmdir /s /q "%TARGET_DIR%"
if exist "%TAR_FILE%" del /f /q "%TAR_FILE%"
mkdir "%TARGET_DIR%"

echo.
echo [2/4] Melakukan build dan publish standalone Linux x86_64 (.NET 8)...
dotnet publish "%~dp0FileSender.csproj" -c Release -r linux-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -o "%TEMP_PUBLISH%"

if %ERRORLEVEL% NEQ 0 (
    echo [ERROR] Gagal melakukan compile FileSender Linux.
    pause
    exit /b %ERRORLEVEL%
)

echo.
echo [3/4] Menyiapkan paket installer Linux di folder publish...
copy "%TEMP_PUBLISH%\FileSender" "%TARGET_DIR%\FileSender" >nul
copy "%~dp0version.conf" "%TARGET_DIR%\version.conf" >nul 2>&1
copy "%~dp0app_icon.png" "%TARGET_DIR%\app_icon.png" >nul
copy "%~dp0installers\linux\install.sh" "%TARGET_DIR%\install.sh" >nul
copy "%~dp0installers\linux\README.txt" "%TARGET_DIR%\README.txt" >nul

echo.
echo [4/4] Mengompres installer ke format .tar.gz...
tar -czf "%TAR_FILE%" -C "%TARGET_DIR%" .

rem Salin juga ke Desktop untuk kemudahan akses
if exist "%DESKTOP_DIR%" (
    copy "%TAR_FILE%" "%DESKTOP_DIR%\FileSender-Linux-x86-Installer.tar.gz" >nul 2>&1
)

if exist "%TEMP_PUBLISH%" rmdir /s /q "%TEMP_PUBLISH%"

echo.
echo =========================================================
echo   [BERHASIL] Installer Linux Telah Selesai Dibuat!
echo =========================================================
echo Lokasi Folder Proyek : %TARGET_DIR%
echo Lokasi File TarGz    : %TAR_FILE%
if exist "%DESKTOP_DIR%" echo Salinan Desktop     : %DESKTOP_DIR%\FileSender-Linux-x86-Installer.tar.gz
echo.

explorer.exe "%PUBLISH_DIR%"
pause
