param(
    [string]$AppName = "IndigoMovieManager_fork_workthree",
    [int]$TailLines = 400,
    [int]$RecentFailureLimit = 5
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

# 実動画確認の入口を1本にまとめ、今見ているログとFailureDbが現行実装かを即判定できるようにする。
$repoRoot = Split-Path -Parent $PSScriptRoot
$localAppData = [Environment]::GetFolderPath("LocalApplicationData")
$appRoot = [IO.Path]::Combine($localAppData, $AppName)
$logDir = [IO.Path]::Combine($appRoot, "logs")
$debugRuntimeLogPath = [IO.Path]::Combine($logDir, "debug-runtime.log")
$failureDbDir = [IO.Path]::Combine($appRoot, "FailureDb")

function Write-Section([string]$title)
{
    ""
    "=== $title ==="
}

function Try-GetRecentLogLines([string]$logPath, [int]$tailLines)
{
    if (-not (Test-Path $logPath))
    {
        return @()
    }

    return @(Get-Content -Path $logPath -Tail $tailLines)
}

function Get-LogMarkerSummary([string[]]$lines, [string]$marker)
{
    $matched = @(
        $lines | Where-Object {
            $_ -and $_.IndexOf($marker, [StringComparison]::OrdinalIgnoreCase) -ge 0
        }
    )
    [pscustomobject]@{
        Marker = $marker
        Count = $matched.Count
        Latest = if ($matched.Count -gt 0) { $matched[-1] } else { "" }
    }
}

function Get-FailureDbSummary([string]$dbPath, [int]$limit)
{
    $pythonScript = @'
import json
import sqlite3
import sys

db_path = sys.argv[1]
limit = int(sys.argv[2])
conn = sqlite3.connect(db_path)
conn.row_factory = sqlite3.Row

def fetch_all(sql, params=()):
    return [dict(row) for row in conn.execute(sql, params).fetchall()]

status_counts = {
    row["Status"]: row["Cnt"]
    for row in conn.execute(
        "SELECT Status, COUNT(*) AS Cnt FROM ThumbnailFailure GROUP BY Status ORDER BY Status"
    )
}

latest_main = fetch_all(
    """
    SELECT FailureId, Status, Lane, TabIndex, MoviePath, UpdatedAtUtc, FailureReason
    FROM ThumbnailFailure
    WHERE Lane IN ('normal', 'slow')
    ORDER BY UpdatedAtUtc DESC, FailureId DESC
    LIMIT ?
    """,
    (limit,),
)

latest_rescue = fetch_all(
    """
    SELECT FailureId, Status, Lane, Engine, RepairApplied, UpdatedAtUtc, FailureReason
    FROM ThumbnailFailure
    WHERE Lane = 'rescue'
    ORDER BY UpdatedAtUtc DESC, FailureId DESC
    LIMIT ?
    """,
    (limit,),
)

print(
    json.dumps(
        {
            "db_path": db_path,
            "status_counts": status_counts,
            "latest_main": latest_main,
            "latest_rescue": latest_rescue,
        },
        ensure_ascii=False,
    )
)
'@

    $json = $pythonScript | python - $dbPath $limit
    return $json | ConvertFrom-Json
}

Write-Output "実動画確認サマリ: $(Get-Date -Format 'yyyy-MM-dd HH:mm:ss')"
Write-Output "対象App: $AppName"
Write-Output "リポジトリ: $repoRoot"
Write-Output "LocalAppData: $appRoot"

Write-Section "起動中プロセス"
$processes = @(
    Get-Process |
        Where-Object { $_.ProcessName -like "IndigoMovieManager*" -or $_.ProcessName -like "*RescueWorker*" } |
        Select-Object ProcessName, Id, Path
)

if ($processes.Count -eq 0)
{
    "起動中プロセスなし"
}
else
{
    $processes | Format-Table -AutoSize | Out-String

    $currentRepoProcess = @(
        $processes | Where-Object {
            -not [string]::IsNullOrWhiteSpace($_.Path) -and $_.Path.StartsWith($repoRoot, [StringComparison]::OrdinalIgnoreCase)
        }
    )
    if ($currentRepoProcess.Count -lt 1)
    {
        "注意: 現在起動中のプロセスはこの workthree リポジトリ配下ではありません。"
    }
}

Write-Section "ログ概要"
if (-not (Test-Path $debugRuntimeLogPath))
{
    "debug-runtime.log が見つかりません。"
}
else
{
    $logFile = Get-Item $debugRuntimeLogPath
    "debug-runtime.log: $($logFile.FullName)"
    "最終更新: $($logFile.LastWriteTime.ToString('yyyy-MM-dd HH:mm:ss'))"
    $recentLines = Try-GetRecentLogLines -logPath $debugRuntimeLogPath -tailLines $TailLines
    "対象行数: $($recentLines.Count)"

    $markers = @(
        "queue-consumer",
        "thumbnail-rescue-request",
        "thumbnail-rescue-worker",
        "thumbnail-sync",
        "thumbnail-rescue",
        "thumbnail-repair",
        "watch-check"
    )

    foreach ($marker in $markers)
    {
        $summary = Get-LogMarkerSummary -lines $recentLines -marker $marker
        "{0,-26} count={1}" -f $summary.Marker, $summary.Count
        if (-not [string]::IsNullOrWhiteSpace($summary.Latest))
        {
            "  latest: $($summary.Latest)"
        }
    }

    if (
        ($recentLines | Where-Object {
            $_ -and $_.IndexOf("thumbnail-rescue", [StringComparison]::OrdinalIgnoreCase) -ge 0
        }).Count -gt 0
    )
    {
        "補足: 直近ログに旧 in-proc rescue 系の [thumbnail-rescue] が残っています。古い実行結果を見ている可能性があります。"
    }
}

Write-Section "FailureDb 概要"
if (-not (Test-Path $failureDbDir))
{
    "FailureDb ディレクトリが見つかりません。"
}
else
{
    $dbFiles = @(Get-ChildItem -Path $failureDbDir -Filter "*.failure.imm" -File | Sort-Object LastWriteTime -Descending)
    if ($dbFiles.Count -lt 1)
    {
        "FailureDb ファイルなし"
    }
    else
    {
        foreach ($dbFile in $dbFiles)
        {
            "DB: $($dbFile.FullName)"
            "  最終更新: $($dbFile.LastWriteTime.ToString('yyyy-MM-dd HH:mm:ss'))"
            try
            {
                $summary = Get-FailureDbSummary -dbPath $dbFile.FullName -limit $RecentFailureLimit
                $statusPairs = @($summary.status_counts.PSObject.Properties | Sort-Object Name | ForEach-Object { "$($_.Name)=$($_.Value)" })
                if ($statusPairs.Count -gt 0)
                {
                    "  status: $($statusPairs -join ', ')"
                }
                else
                {
                    "  status: 0件"
                }

                foreach ($row in @($summary.latest_main))
                {
                    "  main: id=$($row.FailureId) status=$($row.Status) lane=$($row.Lane) tab=$($row.TabIndex) updated=$($row.UpdatedAtUtc) movie='$($row.MoviePath)'"
                    if (-not [string]::IsNullOrWhiteSpace($row.FailureReason))
                    {
                        "    reason: $($row.FailureReason)"
                    }
                }

                foreach ($row in @($summary.latest_rescue))
                {
                    "  rescue: id=$($row.FailureId) status=$($row.Status) engine=$($row.Engine) repair=$($row.RepairApplied) updated=$($row.UpdatedAtUtc)"
                    if (-not [string]::IsNullOrWhiteSpace($row.FailureReason))
                    {
                        "    reason: $($row.FailureReason)"
                    }
                }
            }
            catch
            {
                "  読み取り失敗: $($_.Exception.Message)"
            }
        }
    }
}

Write-Section "判定メモ"
"- 現行実装の実動画確認では、[thumbnail-rescue-request] / [thumbnail-rescue-worker] / [thumbnail-sync] を優先して見る。"
"- [thumbnail-rescue] / [thumbnail-repair] だけしか出ていない場合は、古いバイナリか古いログを見ている可能性が高い。"
"- pending_rescue が増える一方で current repo 配下のプロセスが無ければ、現行 workthree をまだ起動していない可能性が高い。"
