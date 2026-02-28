param(
    [string]$Engine = "autogen"
)

$ErrorActionPreference = "Stop"

# スクリプト配置場所からリポジトリルートへ移動する。
$repoRoot = Resolve-Path (Join-Path $PSScriptRoot "..\..")
Set-Location $repoRoot

$logsDir = Join-Path $repoRoot "logs"
$timestamp = Get-Date -Format "yyyyMMdd_HHmmss"
$archiveDir = Join-Path $logsDir ("e2e-manual\" + $timestamp)
New-Item -ItemType Directory -Path $archiveDir -Force | Out-Null

$targets = @(
    "debug-runtime.log",
    "thumbnail-create-process.csv"
)

foreach ($name in $targets) {
    $source = Join-Path $logsDir $name
    if (Test-Path $source) {
        Copy-Item $source (Join-Path $archiveDir ("before_" + $name)) -Force
    }
}

# 重いE2Eは手動実行を前提にし、常時テストから分離する。
$env:IMM_THUMB_ENGINE = $Engine

Write-Host "E2E手動検証を開始します。"
Write-Host "1) アプリを起動してサムネイル生成を実施してください。"
Write-Host "2) 完了後に Enter を押すとログを退避します。"
Read-Host "準備ができたら Enter"

foreach ($name in $targets) {
    $source = Join-Path $logsDir $name
    if (Test-Path $source) {
        Copy-Item $source (Join-Path $archiveDir ("after_" + $name)) -Force
    }
}

# 実行後は環境変数を必ず戻す。
Remove-Item Env:IMM_THUMB_ENGINE -ErrorAction SilentlyContinue

Write-Host "E2E手動ログを保存しました: $archiveDir"
