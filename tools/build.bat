@echo off
chcp 65001 > nul
echo === FurimaSync ビルドスクリプト ===

where dotnet >nul 2>&1
if %errorlevel% neq 0 (
    echo.
    echo [エラー] .NET SDK (dotnet コマンド) が見つかりません。
    echo PCに .NET 6.0 以降の SDK がインストールされていない可能性があります。
    echo 以下の公式サイトから .NET SDK をインストールしてください:
    echo https://dotnet.microsoft.com/download
    echo.
    pause
    exit /b %errorlevel%
)

echo テンポラリプロジェクトを作成中...
dotnet new console -n FurimaSyncTemp --force
if %errorlevel% neq 0 pause

echo Program.cs をコピー中...
copy /y FurimaSync.cs FurimaSyncTemp\Program.cs
cd FurimaSyncTemp

echo 独立実行型の単一ファイル (.exe) をビルド中...
dotnet publish -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -o bin_publish

if exist bin_publish\FurimaSync.exe (
    copy /y bin_publish\FurimaSync.exe ..\furima-sync.exe
    echo.
    echo ========================================================
    echo 【ビルド成功】 tools\furima-sync.exe が正常に生成されました！
    echo ========================================================
) else (
    echo.
    echo [エラー] ビルド成果物の FurimaSync.exe が見つかりませんでした。
    echo 上記のビルドエラーメッセージをご確認ください。
)

cd ..
rmdir /s /q FurimaSyncTemp
pause
