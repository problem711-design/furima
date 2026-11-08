# Furima PC Sync PowerShell Script (store.json パス対応)
$ScriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$StoreJson = Join-Path $ScriptDir "store.json"
$SyncDir = Join-Path $ScriptDir "出品写真_PC保存用"

if (Test-Path $StoreJson) {
    try {
        $json = Get-Content $StoreJson -Raw -Encoding UTF8 | ConvertFrom-Json
        if ($json.path) {
            $SyncDir = $json.path
        }
    } catch {}
}

if (-not (Test-Path $SyncDir)) {
    New-Item -ItemType Directory -Path $SyncDir | Out-Null
}

Write-Host "同期先フォルダ: $SyncDir"
