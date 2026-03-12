param(
    [datetime]$TargetDate = (Get-Date).Date,
    [int]$StartHour = 7,
    [int]$StartMinute = 40,
    [string]$StartTime = "",
    [int]$TopLongCount = 100,
    [string]$LogsDir = "",
    [string]$OutputDir = "",
    [string]$OutputFileName = "",
    [switch]$AppendTimestamp
)

$ErrorActionPreference = "Stop"

function Parse-LogDateTime {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Value
    )

    $styles = [System.Globalization.DateTimeStyles]::AssumeLocal
    $culture = [System.Globalization.CultureInfo]::InvariantCulture
    $formats = @(
        "yyyy-MM-dd HH:mm:ss.fff",
        "yyyy-MM-dd HH:mm:ss"
    )

    foreach ($format in $formats) {
        $parsed = [datetime]::MinValue
        if ([datetime]::TryParseExact($Value, $format, $culture, $styles, [ref]$parsed)) {
            return $parsed
        }
    }

    $fallback = [datetime]::MinValue
    if ([datetime]::TryParse($Value, [ref]$fallback)) {
        return $fallback
    }

    return $null
}

function Escape-MarkdownCell {
    param([object]$Value)

    if ($null -eq $Value) {
        return ""
    }

    $text = [string]$Value
    $text = $text -replace "\r?\n", "<br>"
    $text = $text -replace "\|", "\|"
    return $text
}

function New-MarkdownTable {
    param(
        [Parameter(Mandatory = $true)]
        [object[]]$Rows,
        [Parameter(Mandatory = $true)]
        [string[]]$Columns
    )

    if ($Rows.Count -lt 1) {
        return "_データなし_"
    }

    $header = "| " + (($Columns | ForEach-Object { Escape-MarkdownCell $_ }) -join " | ") + " |"
    $separator = "| " + (($Columns | ForEach-Object { "---" }) -join " | ") + " |"
    $lines = New-Object System.Collections.Generic.List[string]
    $lines.Add($header)
    $lines.Add($separator)

    foreach ($row in $Rows) {
        $cells = foreach ($column in $Columns) {
            Escape-MarkdownCell $row.$column
        }
        $lines.Add("| " + ($cells -join " | ") + " |")
    }

    return ($lines -join "`n")
}

function Write-Utf8NoBom {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Path,
        [Parameter(Mandatory = $true)]
        [string]$Content
    )

    $encoding = New-Object System.Text.UTF8Encoding($false)
    [System.IO.File]::WriteAllText($Path, $Content, $encoding)
}

if ([string]::IsNullOrWhiteSpace($LogsDir)) {
    $LogsDir = Join-Path $env:LOCALAPPDATA "IndigoMovieManager_fork_workthree\logs"
}

$repoRoot = Resolve-Path (Join-Path $PSScriptRoot "..")
if ([string]::IsNullOrWhiteSpace($OutputDir)) {
    $OutputDir = Join-Path $repoRoot ".local"
}

$processCsvPath = Join-Path $LogsDir "thumbnail-create-process.csv"
$runtimeLogPath = Join-Path $LogsDir "debug-runtime.log"

if (-not (Test-Path -LiteralPath $processCsvPath -PathType Leaf)) {
    throw "thumbnail-create-process.csv が見つかりません: $processCsvPath"
}
if (-not (Test-Path -LiteralPath $runtimeLogPath -PathType Leaf)) {
    throw "debug-runtime.log が見つかりません: $runtimeLogPath"
}

New-Item -ItemType Directory -Path $OutputDir -Force | Out-Null

# 開始時刻は StartTime が指定されたらそれを優先し、未指定なら StartHour/StartMinute を使う。
if (-not [string]::IsNullOrWhiteSpace($StartTime)) {
    if ($StartTime -notmatch "^(?<h>\d{1,2}):(?<m>\d{2})(:(?<s>\d{2}))?$") {
        throw "StartTime は HH:mm または HH:mm:ss 形式で指定してください。指定値: $StartTime"
    }

    $StartHour = [int]$matches["h"]
    $StartMinute = [int]$matches["m"]
    $startSecond = if ([string]::IsNullOrWhiteSpace($matches["s"])) { 0 } else { [int]$matches["s"] }
}
else {
    $startSecond = 0
}

if ($StartHour -lt 0 -or $StartHour -gt 23) {
    throw "StartHour は 0-23 の範囲で指定してください。指定値: $StartHour"
}
if ($StartMinute -lt 0 -or $StartMinute -gt 59) {
    throw "StartMinute は 0-59 の範囲で指定してください。指定値: $StartMinute"
}
if ($startSecond -lt 0 -or $startSecond -gt 59) {
    throw "StartSecond は 0-59 の範囲で指定してください。指定値: $startSecond"
}

$startAt = $TargetDate.Date.AddHours($StartHour).AddMinutes($StartMinute)
$startAt = $startAt.AddSeconds($startSecond)
$pairScanStartAt = $startAt.AddHours(-2)

# CSVを読み取り、対象期間の作成結果を抽出する。
$csvRows =
    Import-Csv -Path $processCsvPath |
    ForEach-Object {
        $dt = Parse-LogDateTime -Value $_.datetime
        if ($null -eq $dt) {
            return
        }

        [pscustomobject]@{
            Dt = $dt
            Engine = $_.engine
            Movie = $_.movie_file_name
            Ext = ([System.IO.Path]::GetExtension($_.movie_file_name) ?? "").ToLowerInvariant()
            Codec = $_.codec
            Status = $_.status
            Error = $_.error_message
        }
    } |
    Where-Object { $_.Dt -ge $startAt } |
    Sort-Object Dt

if ($csvRows.Count -lt 1) {
    if ([string]::IsNullOrWhiteSpace($OutputFileName)) {
        $baseNoDataName = "thumbnail-log-summary_{0}_h{1:00}m{2:00}" -f $TargetDate.ToString("yyyyMMdd"), $StartHour, $StartMinute
        if ($AppendTimestamp) {
            $baseNoDataName = "{0}_{1}" -f $baseNoDataName, (Get-Date).ToString("yyyyMMdd_HHmmss")
        }
        $OutputFileName = "$baseNoDataName.md"
    }
    elseif ([System.IO.Path]::GetExtension($OutputFileName) -eq "") {
        $OutputFileName = "$OutputFileName.md"
    }
    if ($AppendTimestamp) {
        $nameWithoutExt = [System.IO.Path]::GetFileNameWithoutExtension($OutputFileName)
        $ext = [System.IO.Path]::GetExtension($OutputFileName)
        $OutputFileName = "{0}_{1}{2}" -f $nameWithoutExt, (Get-Date).ToString("yyyyMMdd_HHmmss"), $ext
    }

    $noDataPath = Join-Path $OutputDir $OutputFileName
    $noDataDoc = @"
# サムネ作成ログ集計

- 集計開始: $($startAt.ToString("yyyy-MM-dd HH:mm:ss"))
- 結果: 対象データなし
- 参照: $processCsvPath
"@
    Write-Utf8NoBom -Path $noDataPath -Content ($noDataDoc.Trim() + "`n")
    Write-Output "出力完了: $noDataPath"
    exit 0
}

# debug-runtime から CreateThumbAsync の開始/終了をペア化して実時間(ms)を作る。
$logLinePattern = "^(?<ts>\d{4}-\d{2}-\d{2} \d{2}:\d{2}:\d{2}\.\d{3}) \[(?<cat>[^\]]+)\] (?<msg>.*)$"
$startQueues = @{}
$durations = New-Object System.Collections.Generic.List[object]
$engineFailedEvents = New-Object System.Collections.Generic.List[object]
$placeholderEvents = New-Object System.Collections.Generic.List[object]
$consumerFailedEvents = New-Object System.Collections.Generic.List[object]
$errorMarkerEvents = New-Object System.Collections.Generic.List[object]

Get-Content -Path $runtimeLogPath -Encoding UTF8 | ForEach-Object {
    if ($_ -notmatch $logLinePattern) {
        return
    }

    $ts = Parse-LogDateTime -Value $matches["ts"]
    if ($null -eq $ts) {
        return
    }

    $cat = $matches["cat"]
    $msg = $matches["msg"]

    if ($ts -ge $pairScanStartAt) {
        if ($cat -eq "task-start" -and $msg.StartsWith("CreateThumbAsync ")) {
            $jobKey = $msg.Substring("CreateThumbAsync ".Length)
            if (-not $startQueues.ContainsKey($jobKey)) {
                $startQueues[$jobKey] = New-Object System.Collections.Generic.Queue[datetime]
            }
            $startQueues[$jobKey].Enqueue($ts)
            return
        }

        if ($cat -eq "task-end" -and $msg.StartsWith("CreateThumbAsync ")) {
            $jobKey = $msg.Substring("CreateThumbAsync ".Length)
            if ($startQueues.ContainsKey($jobKey) -and $startQueues[$jobKey].Count -gt 0) {
                $st = $startQueues[$jobKey].Dequeue()
                if ($ts -ge $startAt) {
                    $durations.Add(
                        [pscustomobject]@{
                            EndTs = $ts
                            DurationMs = [math]::Round(($ts - $st).TotalMilliseconds, 3)
                            JobKey = $jobKey
                        }
                    )
                }
            }
            return
        }
    }

    if ($ts -lt $startAt) {
        return
    }

    if ($cat -eq "thumbnail" -and $msg.StartsWith("engine failed:")) {
        $engine = ""
        $reason = $msg
        if ($msg -match "engine failed: id=(?<id>[^,]+),") {
            $engine = $matches["id"]
        }

        # reasonは内部にクォートを含むケースがあるため、末尾条件を緩めて安全に抽出する。
        if ($msg -match "reason='(?<reason>.*)', try_next=") {
            $reason = $matches["reason"]
        }
        elseif ($msg -match "reason='(?<reason>.*)$") {
            $reason = $matches["reason"].TrimEnd("'")
        }
        $engineFailedEvents.Add(
            [pscustomobject]@{
                Dt = $ts
                Engine = $engine
                Reason = $reason
            }
        )
        return
    }

    if ($cat -eq "thumbnail" -and $msg.StartsWith("failure placeholder created:")) {
        $kind = ""
        $movie = ""
        $detail = ""
        if ($msg -match "kind=(?<kind>[^,]+), movie='(?<movie>[^']*)', path='(?<path>[^']*)', detail='(?<detail>[^']*)'") {
            $kind = $matches["kind"]
            $movie = $matches["movie"]
            $detail = $matches["detail"]
        }
        $placeholderEvents.Add(
            [pscustomobject]@{
                Dt = $ts
                Kind = $kind
                Movie = $movie
                Detail = $detail
            }
        )
        return
    }

    if ($cat -eq "queue-consumer" -and $msg.StartsWith("consumer failed:")) {
        $consumerFailedEvents.Add(
            [pscustomobject]@{
                Dt = $ts
                Message = $msg
            }
        )
        return
    }

    if ($cat -eq "thumbnail" -and $msg.StartsWith("error marker created:")) {
        $errorMarkerEvents.Add(
            [pscustomobject]@{
                Dt = $ts
                Message = $msg
            }
        )
    }
}

$durations = $durations | Sort-Object EndTs

# CSV行と実時間を時系列で突合する。
$pairCount = [Math]::Min($csvRows.Count, $durations.Count)
$rowsWithDuration = New-Object System.Collections.Generic.List[object]

for ($i = 0; $i -lt $pairCount; $i++) {
    $row = $csvRows[$i]
    $dur = $durations[$i]
    $rowsWithDuration.Add(
        [pscustomobject]@{
            Dt = $row.Dt
            Engine = $row.Engine
            Movie = $row.Movie
            Ext = $row.Ext
            Codec = $row.Codec
            Status = $row.Status
            Error = $row.Error
            DurationMs = $dur.DurationMs
            PairDeltaMs = [math]::Round(($dur.EndTs - $row.Dt).TotalMilliseconds, 3)
        }
    )
}

for ($i = $pairCount; $i -lt $csvRows.Count; $i++) {
    $row = $csvRows[$i]
    $rowsWithDuration.Add(
        [pscustomobject]@{
            Dt = $row.Dt
            Engine = $row.Engine
            Movie = $row.Movie
            Ext = $row.Ext
            Codec = $row.Codec
            Status = $row.Status
            Error = $row.Error
            DurationMs = $null
            PairDeltaMs = $null
        }
    )
}

$successRows = $rowsWithDuration | Where-Object { $_.Status -eq "success" }
$failedRows = $rowsWithDuration | Where-Object { $_.Status -ne "success" }
$rowsWithDurationMs = $rowsWithDuration | Where-Object { $null -ne $_.DurationMs }
$successRowsWithDuration = $successRows | Where-Object { $null -ne $_.DurationMs }

$avgMsAll = if ($rowsWithDurationMs.Count -gt 0) {
    [math]::Round((($rowsWithDurationMs | Measure-Object DurationMs -Average).Average), 2)
}
else {
    0
}
$avgMsSuccess = if ($successRowsWithDuration.Count -gt 0) {
    [math]::Round((($successRowsWithDuration | Measure-Object DurationMs -Average).Average), 2)
}
else {
    0
}

$extAvgRows =
    $successRowsWithDuration |
    Group-Object Ext |
    Sort-Object Count -Descending |
    ForEach-Object {
        [pscustomobject]@{
            Ext = if ([string]::IsNullOrWhiteSpace($_.Name)) { "(no ext)" } else { $_.Name }
            Count = $_.Count
            AvgMs = [math]::Round((($_.Group | Measure-Object DurationMs -Average).Average), 2)
            MinMs = [math]::Round((($_.Group | Measure-Object DurationMs -Minimum).Minimum), 2)
            MaxMs = [math]::Round((($_.Group | Measure-Object DurationMs -Maximum).Maximum), 2)
        }
    }

$allErrorSummaryRows =
    $engineFailedEvents |
    Group-Object Engine, Reason |
    Sort-Object Count -Descending |
    ForEach-Object {
        [pscustomobject]@{
            Engine = $_.Group[0].Engine
            Reason = $_.Group[0].Reason
            Count = $_.Count
        }
    }

$allErrorListRows =
    $engineFailedEvents |
    Sort-Object Dt |
    ForEach-Object {
        [pscustomobject]@{
            Datetime = $_.Dt.ToString("yyyy-MM-dd HH:mm:ss.fff")
            Engine = $_.Engine
            Reason = $_.Reason
        }
    }

$finalErrorRows =
    $failedRows |
    Sort-Object Dt |
    ForEach-Object {
        [pscustomobject]@{
            Datetime = $_.Dt.ToString("yyyy-MM-dd HH:mm:ss.fff")
            Engine = $_.Engine
            Movie = $_.Movie
            Ext = $_.Ext
            DurationMs = if ($null -eq $_.DurationMs) { "" } else { [math]::Round($_.DurationMs, 2) }
            Error = $_.Error
        }
    }

$managedOutsideRows =
    $rowsWithDuration |
    Where-Object {
        $_.Engine -like "placeholder-*"
    } |
    Sort-Object Dt |
    ForEach-Object {
        [pscustomobject]@{
            Datetime = $_.Dt.ToString("yyyy-MM-dd HH:mm:ss.fff")
            Engine = $_.Engine
            Movie = $_.Movie
            Ext = $_.Ext
            Error = $_.Error
        }
    }

$placeholderKindSummaryRows =
    $placeholderEvents |
    Group-Object Kind |
    Sort-Object Count -Descending |
    ForEach-Object {
        [pscustomobject]@{
            Kind = $_.Name
            Count = $_.Count
        }
    }

$longTopRows =
    $rowsWithDurationMs |
    Sort-Object DurationMs -Descending |
    Select-Object -First ([Math]::Max(1, $TopLongCount)) |
    ForEach-Object -Begin { $rank = 0 } -Process {
        $rank++
        [pscustomobject]@{
            Rank = $rank
            DurationMs = [math]::Round($_.DurationMs, 2)
            Datetime = $_.Dt.ToString("yyyy-MM-dd HH:mm:ss.fff")
            Status = $_.Status
            Engine = $_.Engine
            Ext = $_.Ext
            Movie = $_.Movie
            Error = $_.Error
        }
    }

$firstDt = ($csvRows | Select-Object -First 1).Dt
$lastDt = ($csvRows | Select-Object -Last 1).Dt
$spanSec = [math]::Round(($lastDt - $firstDt).TotalSeconds, 3)
$throughputPerSec = if ($spanSec -gt 0) { [math]::Round(($successRows.Count / $spanSec), 2) } else { 0 }
$throughputPerMin = [math]::Round(($throughputPerSec * 60), 1)
$pairDeltaAvg = if ($rowsWithDurationMs.Count -gt 0) {
    [math]::Round((($rowsWithDurationMs | Measure-Object PairDeltaMs -Average).Average), 2)
}
else {
    0
}
$pairDeltaMax = if ($rowsWithDurationMs.Count -gt 0) {
    [math]::Round((($rowsWithDurationMs | Measure-Object PairDeltaMs -Maximum).Maximum), 2)
}
else {
    0
}

$startTimeNote = if ([string]::IsNullOrWhiteSpace($StartTime)) {
    "(StartHour/StartMinute)"
}
else {
    $StartTime
}

$docLines = New-Object System.Collections.Generic.List[string]
$docLines.Add("# サムネ作成ログ集計")
$docLines.Add("")
$docLines.Add("## 集計条件")
$docLines.Add("")
$docLines.Add("- 集計開始: $($startAt.ToString("yyyy-MM-dd HH:mm:ss"))")
$docLines.Add("- 集計日: $($TargetDate.ToString("yyyy-MM-dd"))")
$docLines.Add("- 開始時刻指定: $startTimeNote")
$docLines.Add("- 対象CSV: $processCsvPath")
$docLines.Add("- 対象ランタイムログ: $runtimeLogPath")
$docLines.Add("- 対象期間先頭: $($firstDt.ToString("yyyy-MM-dd HH:mm:ss.fff"))")
$docLines.Add("- 対象期間末尾: $($lastDt.ToString("yyyy-MM-dd HH:mm:ss.fff"))")
$docLines.Add("- 期間秒数: $spanSec")
$docLines.Add("")
$docLines.Add("## サマリ")
$docLines.Add("")
$docLines.Add("- 総処理件数: $($csvRows.Count)")
$docLines.Add("- 作成成功数: $($successRows.Count)")
$docLines.Add("- 最終失敗数: $($failedRows.Count)")
$docLines.Add("- 平均ms(全件): $avgMsAll")
$docLines.Add("- 平均ms(成功のみ): $avgMsSuccess")
$docLines.Add("- スループット(成功): ${throughputPerSec}/sec (${throughputPerMin}/min)")
$docLines.Add("- 実時間突合: paired=$pairCount/$($csvRows.Count), delta_avg_ms=$pairDeltaAvg, delta_max_ms=$pairDeltaMax")
$docLines.Add("")
$docLines.Add("## 拡張子ごとの平均ms (成功のみ)")
$docLines.Add("")
$docLines.Add((New-MarkdownTable -Rows $extAvgRows -Columns @("Ext", "Count", "AvgMs", "MinMs", "MaxMs")))
$docLines.Add("")
$docLines.Add("## 各エラー分析")
$docLines.Add("")
$docLines.Add("- 中間エラーイベント数(エンジン失敗): $($engineFailedEvents.Count)")
$docLines.Add("- 最終失敗数(status=failed): $($failedRows.Count)")
$docLines.Add("- placeholder作成数: $($managedOutsideRows.Count)")
$docLines.Add("- queue-consumer failed: $($consumerFailedEvents.Count)")
$docLines.Add("- error marker created: $($errorMarkerEvents.Count)")
$docLines.Add("")
$docLines.Add("### 全エラーサマリ (中間エラー)")
$docLines.Add("")
$docLines.Add((New-MarkdownTable -Rows $allErrorSummaryRows -Columns @("Engine", "Reason", "Count")))
$docLines.Add("")
$docLines.Add("### 全エラーリスト (中間エラー全件)")
$docLines.Add("")
$docLines.Add((New-MarkdownTable -Rows $allErrorListRows -Columns @("Datetime", "Engine", "Reason")))
$docLines.Add("")
$docLines.Add("### 最終エラーリスト")
$docLines.Add("")
$docLines.Add((New-MarkdownTable -Rows $finalErrorRows -Columns @("Datetime", "Engine", "Movie", "Ext", "DurationMs", "Error")))
$docLines.Add("")
$docLines.Add("### 管理外DRMなどリスト (placeholder)")
$docLines.Add("")
$docLines.Add((New-MarkdownTable -Rows $placeholderKindSummaryRows -Columns @("Kind", "Count")))
$docLines.Add("")
$docLines.Add((New-MarkdownTable -Rows $managedOutsideRows -Columns @("Datetime", "Engine", "Movie", "Ext", "Error")))
$docLines.Add("")
$docLines.Add("## 長時間処理動画 best$TopLongCount")
$docLines.Add("")
$docLines.Add((New-MarkdownTable -Rows $longTopRows -Columns @("Rank", "DurationMs", "Datetime", "Status", "Engine", "Ext", "Movie", "Error")))
$docLines.Add("")

$outFileName = $OutputFileName
if ([string]::IsNullOrWhiteSpace($outFileName)) {
    $baseFileName = "thumbnail-log-summary_{0}_h{1:00}m{2:00}" -f $TargetDate.ToString("yyyyMMdd"), $StartHour, $StartMinute
    if ($AppendTimestamp) {
        $baseFileName = "{0}_{1}" -f $baseFileName, (Get-Date).ToString("yyyyMMdd_HHmmss")
    }
    $outFileName = "$baseFileName.md"
}
elseif ([System.IO.Path]::GetExtension($outFileName) -eq "") {
    $outFileName = "$outFileName.md"
}
if ($AppendTimestamp) {
    $nameWithoutExt = [System.IO.Path]::GetFileNameWithoutExtension($outFileName)
    $ext = [System.IO.Path]::GetExtension($outFileName)
    $outFileName = "{0}_{1}{2}" -f $nameWithoutExt, (Get-Date).ToString("yyyyMMdd_HHmmss"), $ext
}

$outPath = Join-Path $OutputDir $outFileName
$content = ($docLines -join "`n")
Write-Utf8NoBom -Path $outPath -Content $content

Write-Output "出力完了: $outPath"
