param(
    [string]$MainDbFullPath = "",
    [int]$AutoSmokeSeconds = 0
)

$ErrorActionPreference = "Stop"

# スクリプト配置場所からリポジトリルートへ移動する。
$repoRoot = Resolve-Path (Join-Path $PSScriptRoot "..\..")
Set-Location $repoRoot

$logsDir = Join-Path $repoRoot "logs"
$timestamp = Get-Date -Format "yyyyMMdd_HHmmss"
$archiveDir = Join-Path $logsDir ("queue-e2e-manual\" + $timestamp)
New-Item -ItemType Directory -Path $archiveDir -Force | Out-Null
$appExe = Join-Path $repoRoot "bin\x64\Debug\net8.0-windows\IndigoMovieManager_fork.exe"

$queueDbDir = Join-Path $env:LOCALAPPDATA "IndigoMovieManager_fork\QueueDb"
$logTargets = @(
    "debug-runtime.log",
    "thumbnail-create-process.csv"
)

function Copy-LogSnapshot {
    param(
        [string]$Prefix
    )

    foreach ($name in $logTargets) {
        $source = Join-Path $logsDir $name
        if (-not (Test-Path $source)) {
            continue
        }

        $dest = Join-Path $archiveDir ($Prefix + "_" + $name)
        Copy-Item $source $dest -Force
    }
}

function Copy-QueueDbSnapshot {
    param(
        [string]$Prefix
    )

    if (-not (Test-Path $queueDbDir)) {
        return
    }

    $destDir = Join-Path $archiveDir ($Prefix + "_queue_db")
    New-Item -ItemType Directory -Path $destDir -Force | Out-Null

    Get-ChildItem -Path $queueDbDir -Filter "*.queue.imm" -File -ErrorAction SilentlyContinue |
        ForEach-Object {
            $dest = Join-Path $destDir $_.Name
            try {
                Copy-Item $_.FullName $dest -Force
            }
            catch {
                Write-Warning "QueueDBのコピーをスキップしました: $($_.Name) / $($_.Exception.Message)"
            }
        }
}

Copy-LogSnapshot -Prefix "before"
Copy-QueueDbSnapshot -Prefix "before"

Write-Host "Queue E2E手動回帰を開始します。"
if (-not [string]::IsNullOrWhiteSpace($MainDbFullPath)) {
    Write-Host "対象MainDB: $MainDbFullPath"
}
if ($AutoSmokeSeconds -gt 0) {
    $safeSeconds = [Math]::Max(1, $AutoSmokeSeconds)
    Write-Host "自動スモークモードで実行します。秒数: $safeSeconds"
    if (-not (Test-Path $appExe)) {
        Write-Warning "アプリ実行ファイルが見つかりません: $appExe"
    }
    else {
        # 指定秒だけ起動し、終了しなければ停止する。
        $process = Start-Process -FilePath $appExe -PassThru
        Start-Sleep -Seconds $safeSeconds
        if (-not $process.HasExited) {
            Stop-Process -Id $process.Id -Force
            Write-Host "自動スモークでプロセスを停止しました。Pid=$($process.Id)"
        }
        else {
            Write-Host "自動スモーク中にプロセスが自然終了しました。ExitCode=$($process.ExitCode)"
        }
    }
}
else {
    Write-Host "1) アプリを起動し、通常キュー投入（等間隔または全ファイル再作成）を実行してください。"
    Write-Host "2) サムネイル進捗タブで Queue/Thread/Worker パネル更新を確認してください。"
    Write-Host "3) 必要なら再投入（全ファイルサムネイル再作成）を実行し、処理継続を確認してください。"
    Write-Host "4) 確認後に Enter を押すとログとQueueDBを退避します。"
    Read-Host "操作完了後に Enter"
}

Copy-LogSnapshot -Prefix "after"
Copy-QueueDbSnapshot -Prefix "after"

Write-Host "Queue E2E手動ログを保存しました: $archiveDir"
