@echo off
chcp 65001 > nul
echo === Windows標準 C# コンパイラ (csc.exe) による furima-sync.exe のビルド ===

set CSC="C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe"
if not exist %CSC% (
    set CSC="C:\Windows\Microsoft.NET\Framework\v4.0.30319\csc.exe"
)

if not exist %CSC% (
    echo [エラー] Windows標準の .NET Framework コンパイラ (csc.exe) が見つかりませんでした。
    pause
    exit /b 1
)

echo コンパイル中...
%CSC% /target:exe /out:furima-sync.exe /r:System.Net.Http.dll /r:System.Web.Extensions.dll FurimaSyncNative.cs

if exist furima-sync.exe (
    echo.
    echo ========================================================
    echo 【ビルド成功】 tools\furima-sync.exe が正常に生成されました！
    echo ========================================================
) else (
    echo.
    echo [エラー] ビルドに失敗しました。
)

pause
