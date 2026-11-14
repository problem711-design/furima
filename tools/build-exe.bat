@echo off
cd /d "%~dp0"
echo ===================================================
echo  FurimaServerNative / FurimaSyncNative Build Script
echo  Requirements: .NET Framework 4.6+ (Win10/11)
echo ===================================================

set CSC="%SystemRoot%\Microsoft.NET\Framework\v4.0.30319\csc.exe"
if not exist %CSC% (
    set CSC="%SystemRoot%\Microsoft.NET\Framework64\v4.0.30319\csc.exe"
)
if not exist %CSC% (
    echo [ERROR] .NET Framework 4.0+ csc.exe not found.
    pause
    exit /b 1
)

echo.
echo [0/2] Stopping running furima-server.exe if active...
taskkill /F /IM furima-server.exe >nul 2>&1
timeout /t 1 /nobreak >nul

echo.
echo [1/2] Building furima-server.exe (/target:winexe)...
%CSC% /nologo /codepage:65001 /out:furima-server.exe /target:winexe FurimaServerNative.cs SyncStore.cs DashboardHtml.cs TunnelQrForm.cs /reference:Newtonsoft.Json.dll /reference:System.Windows.Forms.dll /reference:System.Drawing.dll
if %ERRORLEVEL% neq 0 (
    echo [FAILED] furima-server.exe build error.
    pause
    exit /b 1
)
echo [OK] furima-server.exe built successfully.

echo.
echo [2/2] Building furima-sync.exe...
%CSC% /nologo /codepage:65001 /out:furima-sync.exe /target:exe FurimaSyncNative.cs /reference:Newtonsoft.Json.dll /reference:System.Net.Http.dll
if %ERRORLEVEL% neq 0 (
    echo [FAILED] furima-sync.exe build error.
    pause
    exit /b 1
)
echo [OK] furima-sync.exe built successfully.

echo.
echo [SUCCESS] All builds completed!
pause
