param(
    [string]$AppName = "IndigoMovieManager_fork_workthree",
    [int]$TopGroups = 15,
    [int]$RecentProcessingLimit = 5
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

# p2 の失敗洗い出しを速くするため、FailureDb の rescue 試行失敗を束で読む。
$localAppData = [Environment]::GetFolderPath("LocalApplicationData")
$appRoot = [IO.Path]::Combine($localAppData, $AppName)
$failureDbDir = [IO.Path]::Combine($appRoot, "FailureDb")

function Write-Section([string]$title)
{
    ""
    "=== $title ==="
}

function Get-FailureBundleSummary(
    [string]$dbPath,
    [int]$topGroups,
    [int]$recentProcessingLimit
)
{
    $pythonScript = @'
import json
import sqlite3
import sys

db_path = sys.argv[1]
top_groups = int(sys.argv[2])
recent_processing_limit = int(sys.argv[3])

conn = sqlite3.connect(db_path)
conn.row_factory = sqlite3.Row

def fetch_all(sql, params=()):
    return [dict(row) for row in conn.execute(sql, params).fetchall()]

grouped_attempts = fetch_all(
    """
    SELECT
        COALESCE(ExtraJson, '') AS ExtraJson,
        COALESCE(Engine, '') AS Engine,
        COALESCE(FailureKind, '') AS FailureKind,
        SUBSTR(
            REPLACE(REPLACE(TRIM(COALESCE(FailureReason, '')), CHAR(13), ' '), CHAR(10), ' '),
            1,
            120
        ) AS ReasonKey,
        COUNT(*) AS Count,
        MAX(UpdatedAtUtc) AS LatestAtUtc
    FROM ThumbnailFailure
    WHERE Lane = 'rescue'
      AND Status = 'attempt_failed'
    GROUP BY Engine, FailureKind, ReasonKey
    ORDER BY Count DESC, LatestAtUtc DESC
    LIMIT ?
    """,
    (top_groups,),
)

for row in grouped_attempts:
    try:
        extra = json.loads(row.get("ExtraJson") or "{}")
    except Exception:
        extra = {}
    row["RouteId"] = extra.get("RouteId", "")
    row["SymptomClass"] = extra.get("SymptomClass", "")
    row.pop("ExtraJson", None)

recent_processing = fetch_all(
    """
    SELECT
        FailureId,
        MoviePath,
        FailureReason,
        ExtraJson,
        UpdatedAtUtc
    FROM ThumbnailFailure
    WHERE Lane IN ('normal', 'slow')
      AND Status = 'processing_rescue'
    ORDER BY UpdatedAtUtc DESC, FailureId DESC
    LIMIT ?
    """,
    (recent_processing_limit,),
)

for row in recent_processing:
    try:
        extra = json.loads(row.get("ExtraJson") or "{}")
    except Exception:
        extra = {}
    row["RouteId"] = extra.get("RouteId", "")
    row["SymptomClass"] = extra.get("SymptomClass", "")
    row["CurrentPhase"] = extra.get("CurrentPhase", "")
    row["CurrentEngine"] = extra.get("CurrentEngine", "")

print(
    json.dumps(
        {
            "db_path": db_path,
            "grouped_attempts": grouped_attempts,
            "recent_processing": recent_processing,
        },
        ensure_ascii=False,
    )
)
'@

    $json = $pythonScript | python - $dbPath $topGroups $recentProcessingLimit
    return $json | ConvertFrom-Json
}

Write-Output "救済worker失敗束サマリ: $(Get-Date -Format 'yyyy-MM-dd HH:mm:ss')"
Write-Output "対象App: $AppName"
Write-Output "FailureDb: $failureDbDir"

if (-not (Test-Path $failureDbDir))
{
    Write-Output "FailureDb ディレクトリが見つかりません。"
    exit 0
}

$dbFiles = @(Get-ChildItem -Path $failureDbDir -Filter "*.failure.imm" -File | Sort-Object LastWriteTime -Descending)
if ($dbFiles.Count -lt 1)
{
    Write-Output "FailureDb ファイルなし"
    exit 0
}

foreach ($dbFile in $dbFiles)
{
    Write-Section $dbFile.Name
    "最終更新: $($dbFile.LastWriteTime.ToString('yyyy-MM-dd HH:mm:ss'))"

    try
    {
        $summary = Get-FailureBundleSummary -dbPath $dbFile.FullName -topGroups $TopGroups -recentProcessingLimit $RecentProcessingLimit

        if (@($summary.grouped_attempts).Count -lt 1)
        {
            "attempt_failed はまだありません。"
        }
        else
        {
            "attempt_failed 上位束:"
            foreach ($group in @($summary.grouped_attempts))
            {
                "  count=$($group.Count) route=$($group.RouteId) symptom=$($group.SymptomClass) engine=$($group.Engine) kind=$($group.FailureKind) latest=$($group.LatestAtUtc)"
                if (-not [string]::IsNullOrWhiteSpace($group.ReasonKey))
                {
                    "    reason: $($group.ReasonKey)"
                }
            }
        }

        if (@($summary.recent_processing).Count -gt 0)
        {
            "processing_rescue 親行:"
            foreach ($row in @($summary.recent_processing))
            {
                "  id=$($row.FailureId) updated=$($row.UpdatedAtUtc) route=$($row.RouteId) symptom=$($row.SymptomClass) phase=$($row.CurrentPhase) engine=$($row.CurrentEngine) movie='$($row.MoviePath)'"
                if (-not [string]::IsNullOrWhiteSpace($row.FailureReason))
                {
                    "    failure: $($row.FailureReason)"
                }
                if (-not [string]::IsNullOrWhiteSpace($row.ExtraJson))
                {
                    "    extra: $($row.ExtraJson)"
                }
            }
        }
    }
    catch
    {
        "読み取り失敗: $($_.Exception.Message)"
    }
}

Write-Section "見方"
"- p2 では count の大きい束から先に見る。"
"- 同じ route / symptom / engine / kind / reason が固まっていれば、その route の順序や gate を疑う。"
"- processing_rescue 親行の route / symptom / phase / engine を見ると、今どこで止まっているかを追える。"
