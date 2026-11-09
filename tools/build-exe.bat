@echo off
chcp 65001 >nul
cd /d "%~dp0"
echo ===================================================
echo  FurimaServerNative / FurimaSyncNative ビルドスクリプト
echo ===================================================

set CSC="%SystemRoot%\Microsoft.NET\Framework\v4.0.30319\csc.exe"

if not exist %CSC% (
    echo [エラー] .NET Framework 4.0 の csc.exe が見つかりません。
    pause
    exit /b 1
)

echo.
echo [1/2] FurimaServerNative.exe をビルド中...
%CSC% /nologo /out:furima-server.exe /target:exe FurimaServerNative.cs SyncStore.cs DashboardHtml.cs /reference:lib\Newtonsoft.Json.dll /reference:System.Windows.Forms.dll /reference:System.Drawing.dll

if %ERRORLEVEL% neq 0 (
    echo [失敗] furima-server.exe のビルドエラー。
    pause
    exit /b 1
)
echo [成功] furima-server.exe のビルド完了。

echo.
echo [2/2] FurimaSyncNative.exe をビルド中...
%CSC% /nologo /out:furima-sync.exe /target:exe FurimaSyncNative.cs /reference:lib\Newtonsoft.Json.dll /reference:System.Net.Http.dll

if %ERRORLEVEL% neq 0 (
    echo [失敗] furima-sync.exe のビルドエラー。
    pause
    exit /b 1
)
echo [成功] furima-sync.exe のビルド完了。

echo.
echo [成功] すべてのビルドが正常に完了しました。
pause
