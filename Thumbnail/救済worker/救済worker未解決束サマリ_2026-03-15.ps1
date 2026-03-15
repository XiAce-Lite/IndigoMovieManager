param(
    [string]$AppName = "IndigoMovieManager_fork_workthree",
    [int]$RecentHours = 12,
    [int]$LongProcessingMinutes = 15,
    [int]$TopGroups = 20
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

# p6 では「今まだ解けていない束」だけを route 単位で返す。
$localAppData = [Environment]::GetFolderPath("LocalApplicationData")
$appRoot = [IO.Path]::Combine($localAppData, $AppName)
$failureDbDir = [IO.Path]::Combine($appRoot, "FailureDb")

function Write-Section([string]$title)
{
    ""
    "=== $title ==="
}

function Get-UnresolvedSummary(
    [string]$dbPath,
    [int]$recentHours,
    [int]$longProcessingMinutes,
    [int]$topGroups
)
{
    $pythonScript = @'
import datetime
import json
import sqlite3
import sys

db_path = sys.argv[1]
recent_hours = int(sys.argv[2])
long_processing_minutes = int(sys.argv[3])
top_groups = int(sys.argv[4])

conn = sqlite3.connect(db_path)
conn.row_factory = sqlite3.Row

recent_cutoff = datetime.datetime.now(datetime.UTC) - datetime.timedelta(hours=recent_hours)
processing_cutoff = datetime.datetime.now(datetime.UTC) - datetime.timedelta(minutes=long_processing_minutes)

def parse_extra(extra_json):
    try:
        return json.loads(extra_json or "{}")
    except Exception:
        return {}

def parse_utc(value):
    if not value:
        return None
    try:
        return datetime.datetime.fromisoformat(value.replace("Z", "+00:00"))
    except Exception:
        return None

def fetch(sql, params=()):
    return [dict(row) for row in conn.execute(sql, params).fetchall()]

gave_up_rows = fetch(
    """
    SELECT
        FailureId,
        FailureKind,
        FailureReason,
        MoviePath,
        UpdatedAtUtc,
        COALESCE(ExtraJson, '') AS ExtraJson
    FROM ThumbnailFailure
    WHERE Status = 'gave_up'
    ORDER BY datetime(UpdatedAtUtc) DESC
    """
)

gave_up_groups = {}
legacy_gave_up_count = 0
for row in gave_up_rows:
    updated = parse_utc(row["UpdatedAtUtc"])
    if updated is None or updated < recent_cutoff:
        continue
    extra = parse_extra(row["ExtraJson"])
    route_id = extra.get("RouteId", "")
    symptom = extra.get("SymptomClass", "")
    phase = extra.get("CurrentPhase", "")
    engine = extra.get("CurrentEngine", "")
    if not route_id:
        legacy_gave_up_count += 1
    key = (
        route_id,
        symptom,
        phase,
        engine,
        row["FailureKind"] or "",
        (row["FailureReason"] or "").strip(),
    )
    if key not in gave_up_groups:
        gave_up_groups[key] = {
            "Count": 0,
            "LatestAtUtc": row["UpdatedAtUtc"],
            "MoviePath": row["MoviePath"] or "",
        }
    gave_up_groups[key]["Count"] += 1

gave_up_grouped = []
for key, value in gave_up_groups.items():
    route_id, symptom, phase, engine, failure_kind, failure_reason = key
    gave_up_grouped.append(
        {
            "RouteId": route_id,
            "SymptomClass": symptom,
            "CurrentPhase": phase,
            "CurrentEngine": engine,
            "FailureKind": failure_kind,
            "FailureReason": failure_reason,
            "Count": value["Count"],
            "LatestAtUtc": value["LatestAtUtc"],
            "MoviePath": value["MoviePath"],
        }
    )

gave_up_grouped.sort(key=lambda item: (-item["Count"], item["LatestAtUtc"]), reverse=False)
gave_up_grouped = gave_up_grouped[:top_groups]

long_processing = []
for row in fetch(
    """
    SELECT
        FailureId,
        FailureKind,
        FailureReason,
        MoviePath,
        UpdatedAtUtc,
        COALESCE(ExtraJson, '') AS ExtraJson
    FROM ThumbnailFailure
    WHERE Status = 'processing_rescue'
    ORDER BY datetime(UpdatedAtUtc) ASC
    """
):
    updated = parse_utc(row["UpdatedAtUtc"])
    if updated is None or updated > processing_cutoff:
        continue
    extra = parse_extra(row["ExtraJson"])
    long_processing.append(
        {
            "FailureId": row["FailureId"],
            "RouteId": extra.get("RouteId", ""),
            "SymptomClass": extra.get("SymptomClass", ""),
            "CurrentPhase": extra.get("CurrentPhase", ""),
            "CurrentEngine": extra.get("CurrentEngine", ""),
            "FailureKind": row["FailureKind"] or "",
            "FailureReason": row["FailureReason"] or "",
            "MoviePath": row["MoviePath"] or "",
            "UpdatedAtUtc": row["UpdatedAtUtc"],
        }
    )

print(
    json.dumps(
        {
            "db_path": db_path,
            "gave_up_grouped": gave_up_grouped,
            "legacy_gave_up_count": legacy_gave_up_count,
            "long_processing": long_processing[:top_groups],
        },
        ensure_ascii=False,
    )
)
'@

    $json = $pythonScript | python -X utf8 - $dbPath $RecentHours $LongProcessingMinutes $TopGroups
    return $json | ConvertFrom-Json
}

Write-Output "救済worker未解決束サマリ: $(Get-Date -Format 'yyyy-MM-dd HH:mm:ss')"
Write-Output "対象App: $AppName"
Write-Output "FailureDb: $failureDbDir"
Write-Output "RecentHours: $RecentHours / LongProcessingMinutes: $LongProcessingMinutes"

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
    try
    {
        $summary = Get-UnresolvedSummary -dbPath $dbFile.FullName -recentHours $RecentHours -longProcessingMinutes $LongProcessingMinutes -topGroups $TopGroups
        $gaveUpGroups = @($summary.gave_up_grouped)
        $longProcessing = @($summary.long_processing)

        if ($gaveUpGroups.Count -lt 1 -and $longProcessing.Count -lt 1 -and [int]$summary.legacy_gave_up_count -lt 1)
        {
            continue
        }

        Write-Section $dbFile.Name
        "最終更新: $($dbFile.LastWriteTime.ToString('yyyy-MM-dd HH:mm:ss'))"

        if ([int]$summary.legacy_gave_up_count -gt 0)
        {
            "legacy route 欠損 gave_up: $($summary.legacy_gave_up_count)"
        }

        if ($gaveUpGroups.Count -gt 0)
        {
            "recent gave_up 束:"
            foreach ($group in $gaveUpGroups)
            {
                "  count=$($group.Count) route=$($group.RouteId) symptom=$($group.SymptomClass) phase=$($group.CurrentPhase) engine=$($group.CurrentEngine) kind=$($group.FailureKind)"
                "    reason: $($group.FailureReason)"
                if (-not [string]::IsNullOrWhiteSpace($group.MoviePath))
                {
                    "    sample: $($group.MoviePath)"
                }
            }
        }

        if ($longProcessing.Count -gt 0)
        {
            "long processing_rescue:"
            foreach ($row in $longProcessing)
            {
                "  id=$($row.FailureId) route=$($row.RouteId) symptom=$($row.SymptomClass) phase=$($row.CurrentPhase) engine=$($row.CurrentEngine) kind=$($row.FailureKind) updated=$($row.UpdatedAtUtc)"
                "    reason: $($row.FailureReason)"
                if (-not [string]::IsNullOrWhiteSpace($row.MoviePath))
                {
                    "    movie: $($row.MoviePath)"
                }
            }
        }
    }
    catch
    {
        Write-Section $dbFile.Name
        "読み取り失敗: $($_.Exception.Message)"
    }
}

Write-Section "見方"
"- p6 では recent gave_up を優先して、route ごとの出口が弱い所を返す。"
"- legacy route 欠損 gave_up は、旧実装の残りなので新しい route 評価とは分けて扱う。"
"- long processing_rescue は hard timeout / stale lease / 旧親行残留の候補である。"
