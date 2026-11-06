param(
    [string]$ServerUrl = ""
)

Write-Host "=== Furima PC Sync Tool (PowerShell) ===" -ForegroundColor Cyan

if ([string]::IsNullOrEmpty($ServerUrl)) {
    $ServerUrl = Read-Host "スマホアプリのWebサーバーURLを入力してください (例: http://192.168.1.10:8080)"
}

if ([string]::IsNullOrEmpty($ServerUrl)) {
    Write-Host "URLが入力されませんでした。終了します。" -ForegroundColor Red
    Read-Host "Enterキーを押して終了してください"
    exit
}

$ServerUrl = $ServerUrl.TrimEnd('/')
$SyncDir = Join-Path (Get-Location) "出品写真_PC保存用"
if (!(Test-Path $SyncDir)) {
    New-Item -ItemType Directory -Path $SyncDir | Out-Null
}

Write-Host "ブラウザで管理画面を開いています: $ServerUrl" -ForegroundColor Green
try {
    Start-Process $ServerUrl
} catch {
    Write-Host "ブラウザの自動起動に失敗しました: $_" -ForegroundColor Yellow
}

Write-Host "30秒ごとに自動同期を開始します。(Ctrl+Cで終了)" -ForegroundColor Yellow

while ($true) {
    try {
        $ApiUrl = "$ServerUrl/api/sync/full-data"
        $Response = Invoke-RestMethod -Uri $ApiUrl -Method Get -TimeoutSec 10
        $Items = $Response.items

        $Count = 0
        foreach ($item in $Items) {
            $id = $item.id
            $title = if ($item.title) { $item.title } else { "商品" }
            $description = if ($item.description) { $item.description } else { "" }
            $price = $item.price
            $profit = $item.profit
            $statusLabel = $item.statusLabel

            # 不正文字除去
            foreach ($char in [System.IO.Path]::GetInvalidFileNameChars()) {
                $title = $title -replace [Regex]::Escape($char), '_'
            }

            $ItemFolderName = "${id}_${title}"
            $ItemDirPath = Join-Path $SyncDir $ItemFolderName
            if (!(Test-Path $ItemDirPath)) {
                New-Item -ItemType Directory -Path $ItemDirPath | Out-Null
            }

            # 説明文.txt出力
            $TxtPath = Join-Path $ItemDirPath "商品情報_説明文.txt"
            $TxtContent = "【商品ID】 $id`n【タイトル】 $title`n【販売価格】 ¥$([String]::Format('{0:N0}', $price))`n【純利益】 ¥$([String]::Format('{0:N0}', $profit))`n【ステータス】 $statusLabel`n`n--- 商品説明 ---`n$description"
            [System.IO.File]::WriteAllText($TxtPath, $TxtContent)

            # 写真ダウンロード
            if ($item.photos) {
                $PhotoIdx = 1
                foreach ($photo in $item.photos) {
                    $PhotoUrl = $photo.url
                    if (![string]::IsNullOrEmpty($PhotoUrl)) {
                        try {
                            $PhotoFileName = "photo_{0:D2}.jpg" -f $PhotoIdx
                            $PhotoPath = Join-Path $ItemDirPath $PhotoFileName
                            Invoke-WebRequest -Uri $PhotoUrl -OutFile $PhotoPath -TimeoutSec 10 -ErrorAction Stop
                            $PhotoIdx++
                        } catch {
                            # 失敗時はスキップ
                        }
                    }
                }
            }
            $Count++
        }
        $TimeStr = Get-Date -Format "HH:mm:ss"
        Write-Host "[$TimeStr] 同期成功: $count件の商品と写真・説明文を同期しました。" -ForegroundColor Cyan
    } catch {
        $TimeStr = Get-Date -Format "HH:mm:ss"
        Write-Host "[$TimeStr] 接続エラー: $_ (スマホのWebサーバーとIPを確認してください)" -ForegroundColor Red
    }

    Start-Sleep -Seconds 30
}
