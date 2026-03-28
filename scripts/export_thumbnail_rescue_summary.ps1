param(
    [datetime]$TargetDate = (Get-Date).Date,
    [string]$StartTime = "21:30:00",
    [int]$TailCsvCount = 80,
    [string]$LogsDir = "",
    [string]$OutputDir = "",
    [string]$OutputFileName = ""
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

$runtimeLogPath = Join-Path $LogsDir "debug-runtime.log"
$processCsvPath = Join-Path $LogsDir "thumbnail-create-process.csv"

if (-not (Test-Path -LiteralPath $runtimeLogPath -PathType Leaf)) {
    throw "debug-runtime.log が見つかりません: $runtimeLogPath"
}

if (-not (Test-Path -LiteralPath $processCsvPath -PathType Leaf)) {
    throw "thumbnail-create-process.csv が見つかりません: $processCsvPath"
}

if ($StartTime -notmatch "^(?<h>\d{1,2}):(?<m>\d{2})(:(?<s>\d{2}))?$") {
    throw "StartTime は HH:mm または HH:mm:ss 形式で指定してください。指定値: $StartTime"
}

$startHour = [int]$matches["h"]
$startMinute = [int]$matches["m"]
$startSecond = if ([string]::IsNullOrWhiteSpace($matches["s"])) { 0 } else { [int]$matches["s"] }
$startAt = $TargetDate.Date.AddHours($startHour).AddMinutes($startMinute).AddSeconds($startSecond)

New-Item -ItemType Directory -Path $OutputDir -Force | Out-Null

$logPattern = "^(?<ts>\d{4}-\d{2}-\d{2} \d{2}:\d{2}:\d{2}\.\d{3}) \[(?<cat>[^\]]+)\] (?<msg>.*)$"
$runtimeRows = New-Object System.Collections.Generic.List[object]

# 救済系の観測に必要なカテゴリだけを拾う。
Get-Content -Path $runtimeLogPath -Encoding UTF8 | ForEach-Object {
    if ($_ -notmatch $logPattern) {
        return
    }

    $ts = Parse-LogDateTime -Value $matches["ts"]
    if ($null -eq $ts -or $ts -lt $startAt) {
        return
    }

    $cat = $matches["cat"]
    $msg = $matches["msg"]
    $isRescueRelated =
        $msg.Contains("thumbnail-rescue") `
        -or $msg.Contains("thumbnail-repair") `
        -or $msg.Contains("thumbnail-timeout") `
        -or $msg.Contains("thumbnail-recovery") `
        -or $msg.Contains("missing-thumb rescue")
    if ($isRescueRelated) {
        $runtimeRows.Add(
            [pscustomobject]@{
                Datetime = $ts.ToString("yyyy-MM-dd HH:mm:ss.fff")
                Category = $cat
                Message = $msg
            }
        )
    }
}

$csvRows =
    Import-Csv -Path $processCsvPath |
    ForEach-Object {
        $dt = Parse-LogDateTime -Value $_.datetime
        if ($null -eq $dt -or $dt -lt $startAt) {
            return
        }

        [pscustomobject]@{
            Dt = $dt
            Datetime = $dt.ToString("yyyy-MM-dd HH:mm:ss.fff")
            Engine = $_.engine
            Movie = $_.movie_file_name
            Status = $_.status
            Error = $_.error_message
        }
    } |
    Sort-Object Dt

$csvTailRows =
    $csvRows |
    Select-Object -Last ([Math]::Max(1, $TailCsvCount)) |
    ForEach-Object {
        [pscustomobject]@{
            Datetime = $_.Datetime
            Engine = $_.Engine
            Movie = $_.Movie
            Status = $_.Status
            Error = $_.Error
        }
    }

$runtimeSummaryRows =
    $runtimeRows |
    Group-Object Category |
    Sort-Object Count -Descending |
    ForEach-Object {
        [pscustomobject]@{
            Category = $_.Name
            Count = $_.Count
        }
    }

$csvSummaryRows =
    $csvRows |
    Group-Object Engine, Status, Error |
    Sort-Object Count -Descending |
    ForEach-Object {
        [pscustomobject]@{
            Engine = $_.Group[0].Engine
            Status = $_.Group[0].Status
            Error = $_.Group[0].Error
            Count = $_.Count
        }
    }

$doc = New-Object System.Collections.Generic.List[string]
$doc.Add("# 救済レーンログ要約")
$doc.Add("")
$doc.Add("- 集計開始: $($startAt.ToString("yyyy-MM-dd HH:mm:ss"))")
$doc.Add("- runtime log: $runtimeLogPath")
$doc.Add("- process csv: $processCsvPath")
$doc.Add("- runtime hit: $($runtimeRows.Count)")
$doc.Add("- csv hit: $($csvRows.Count)")
$doc.Add("")
$doc.Add("## runtimeカテゴリ件数")
$doc.Add("")
$doc.Add((New-MarkdownTable -Rows $runtimeSummaryRows -Columns @("Category", "Count")))
$doc.Add("")
$doc.Add("## runtime該当行")
$doc.Add("")
$doc.Add((New-MarkdownTable -Rows $runtimeRows.ToArray() -Columns @("Datetime", "Category", "Message")))
$doc.Add("")
$doc.Add("## CSV要約")
$doc.Add("")
$doc.Add((New-MarkdownTable -Rows $csvSummaryRows -Columns @("Engine", "Status", "Error", "Count")))
$doc.Add("")
$doc.Add("## CSV末尾")
$doc.Add("")
$doc.Add((New-MarkdownTable -Rows $csvTailRows -Columns @("Datetime", "Engine", "Movie", "Status", "Error")))
$doc.Add("")

$resolvedOutputFileName = $OutputFileName
if ([string]::IsNullOrWhiteSpace($resolvedOutputFileName)) {
    $resolvedOutputFileName = "thumbnail_rescue_summary_{0}_{1}.md" -f $TargetDate.ToString("yyyyMMdd"), ($StartTime -replace ":", "")
}
elseif ([System.IO.Path]::GetExtension($resolvedOutputFileName) -eq "") {
    $resolvedOutputFileName = "$resolvedOutputFileName.md"
}

$outputPath = Join-Path $OutputDir $resolvedOutputFileName
Write-Utf8NoBom -Path $outputPath -Content (($doc -join "`n") + "`n")
Write-Output "出力完了: $outputPath"
