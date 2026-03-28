param(
    [Parameter(Mandatory = $true)]
    [string]$MoviePath,

    [string]$OutputRoot = "",

    [int]$TargetCount = 6,

    [int]$ProbeIntervalSec = 30,

    [double]$NearBlackLumaThreshold = 6.0,

    [int]$MinimumSpacingSec = 20,

    [string]$RefineOffsetsSecCsv = "-15,-7,-3,0,3,7,15"
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

function Resolve-ToolPath {
    param(
        [Parameter(Mandatory = $true)]
        [string]$CommandName,

        [Parameter(Mandatory = $true)]
        [string]$FallbackPath
    )

    $command = Get-Command $CommandName -ErrorAction SilentlyContinue
    if ($command)
    {
        return $command.Source
    }

    if (Test-Path $FallbackPath)
    {
        return $FallbackPath
    }

    throw "$CommandName が見つかりません: $FallbackPath"
}

function New-ShortOutputToken {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Text
    )

    $safe = [IO.Path]::GetFileNameWithoutExtension($Text)
    $safe = ($safe -replace '[\\/:*?""<>|]', '_')
    if ($safe.Length -gt 24)
    {
        $safe = $safe.Substring(0, 24)
    }

    $bytes = [System.Text.Encoding]::UTF8.GetBytes($Text)
    $hashBytes = [System.Security.Cryptography.SHA256]::HashData($bytes)
    $hash = [Convert]::ToHexString($hashBytes).Substring(0, 8).ToLowerInvariant()
    return "{0}_{1}" -f $safe, $hash
}

function Get-VideoDurationSec {
    param(
        [Parameter(Mandatory = $true)]
        [string]$FfprobePath,

        [Parameter(Mandatory = $true)]
        [string]$InputPath
    )

    $raw = & $FfprobePath -v error -show_entries format=duration -of default=noprint_wrappers=1:nokey=1 -- "$InputPath"
    $text = ($raw | Select-Object -First 1).Trim()
    $durationSec = 0.0
    if (-not [double]::TryParse($text, [System.Globalization.NumberStyles]::Float, [System.Globalization.CultureInfo]::InvariantCulture, [ref]$durationSec))
    {
        throw "duration の取得に失敗しました: $InputPath"
    }

    return [math]::Max($durationSec, 0.0)
}

function Get-FrameMetrics {
    param(
        [Parameter(Mandatory = $true)]
        [string]$ImagePath
    )

    Add-Type -AssemblyName System.Drawing
    $bitmap = [System.Drawing.Bitmap]::new($ImagePath)

    try
    {
        $sampleStep = 8
        $lumaSum = 0.0
        $satSum = 0.0
        $lumaValues = New-Object System.Collections.Generic.List[double]
        $count = 0

        for ($y = 0; $y -lt $bitmap.Height; $y += $sampleStep)
        {
            for ($x = 0; $x -lt $bitmap.Width; $x += $sampleStep)
            {
                $c = $bitmap.GetPixel($x, $y)
                $r = $c.R / 255.0
                $g = $c.G / 255.0
                $b = $c.B / 255.0
                $max = [Math]::Max($r, [Math]::Max($g, $b))
                $min = [Math]::Min($r, [Math]::Min($g, $b))
                $delta = $max - $min
                $sat = if ($max -le 0.0) { 0.0 } else { $delta / $max }
                $luma = (0.2126 * $c.R) + (0.7152 * $c.G) + (0.0722 * $c.B)

                $lumaSum += $luma
                $satSum += ($sat * 255.0)
                $lumaValues.Add($luma)
                $count++
            }
        }

        if ($count -le 0)
        {
            return [pscustomobject]@{
                AvgLuma = 0.0
                AvgSaturation = 0.0
                LumaStdDev = 0.0
            }
        }

        $avgLuma = $lumaSum / $count
        $avgSat = $satSum / $count
        $variance = 0.0
        foreach ($value in $lumaValues)
        {
            $diff = $value - $avgLuma
            $variance += ($diff * $diff)
        }

        $stdDev = [Math]::Sqrt($variance / $count)

        return [pscustomobject]@{
            AvgLuma = [math]::Round($avgLuma, 2)
            AvgSaturation = [math]::Round($avgSat, 2)
            LumaStdDev = [math]::Round($stdDev, 2)
        }
    }
    finally
    {
        $bitmap.Dispose()
    }
}

function New-CandidateRecord {
    param(
        [Parameter(Mandatory = $true)]
        [double]$CaptureSec,

        [Parameter(Mandatory = $true)]
        [string]$ImagePath,

        [Parameter(Mandatory = $true)]
        [string]$Phase
    )

    try
    {
        $metrics = Get-FrameMetrics -ImagePath $ImagePath
    }
    catch
    {
        return $null
    }
    $score = ($metrics.AvgLuma * 1.0) + ($metrics.AvgSaturation * 0.8) + ($metrics.LumaStdDev * 0.5)

    return [pscustomobject]@{
        CaptureSec = [math]::Round($CaptureSec, 3)
        Phase = $Phase
        ImagePath = $ImagePath
        AvgLuma = $metrics.AvgLuma
        AvgSaturation = $metrics.AvgSaturation
        LumaStdDev = $metrics.LumaStdDev
        Score = [math]::Round($score, 2)
    }
}

function Invoke-FrameCapture {
    param(
        [Parameter(Mandatory = $true)]
        [string]$FfmpegPath,

        [Parameter(Mandatory = $true)]
        [string]$MoviePath,

        [Parameter(Mandatory = $true)]
        [double]$CaptureSec,

        [Parameter(Mandatory = $true)]
        [string]$OutputPath
    )

    & $FfmpegPath -y -ss ([string]::Format([System.Globalization.CultureInfo]::InvariantCulture, "{0:0.###}", $CaptureSec)) -i $MoviePath -frames:v 1 -update 1 -q:v 2 $OutputPath *> $null
    return (Test-Path $OutputPath)
}

function Select-UniqueCandidates {
    param(
        [Parameter(Mandatory = $true)]
        [AllowEmptyCollection()]
        [object[]]$Candidates,

        [Parameter(Mandatory = $true)]
        [int]$TargetCount,

        [Parameter(Mandatory = $true)]
        [int]$MinimumSpacingSec,

        [Parameter(Mandatory = $true)]
        [double]$NearBlackLumaThreshold
    )

    $selected = New-Object System.Collections.Generic.List[object]
    foreach (
        $candidate in (
            $Candidates
            | Sort-Object -Property `
                @{ Expression = "Score"; Descending = $true }, `
                @{ Expression = "AvgLuma"; Descending = $true }
        )
    )
    {
        if ($candidate.AvgLuma -lt $NearBlackLumaThreshold)
        {
            continue
        }

        $isFarEnough = $true
        foreach ($picked in $selected)
        {
            if ([math]::Abs($picked.CaptureSec - $candidate.CaptureSec) -lt $MinimumSpacingSec)
            {
                $isFarEnough = $false
                break
            }
        }

        if (-not $isFarEnough)
        {
            continue
        }

        $selected.Add($candidate)
        if ($selected.Count -ge $TargetCount)
        {
            break
        }
    }

    return $selected
}

if (-not (Test-Path $MoviePath))
{
    throw "movie not found: $MoviePath"
}

$repoRoot = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
$ffprobePath = Resolve-ToolPath -CommandName "ffprobe" -FallbackPath (Join-Path $repoRoot "tools\ffmpeg\ffprobe.exe")
$ffmpegPath = Resolve-ToolPath -CommandName "ffmpeg" -FallbackPath (Join-Path $repoRoot "tools\ffmpeg\ffmpeg.exe")
$durationSec = Get-VideoDurationSec -FfprobePath $ffprobePath -InputPath $MoviePath

if ($durationSec -le 0.0)
{
    throw "duration が 0 です: $MoviePath"
}

if ([string]::IsNullOrWhiteSpace($OutputRoot))
{
    $token = New-ShortOutputToken -Text $MoviePath
    $OutputRoot = Join-Path $repoRoot (".codex_build\manual-bright-frame-loop_{0}_{1:yyyyMMdd_HHmmss}" -f $token, [datetime]::Now)
}

$tempDir = Join-Path $OutputRoot "temp"
$selectedDir = Join-Path $OutputRoot "selected"
New-Item -ItemType Directory -Force -Path $tempDir | Out-Null
New-Item -ItemType Directory -Force -Path $selectedDir | Out-Null

$coarseCandidates = New-Object System.Collections.Generic.List[object]
$seenSeconds = New-Object System.Collections.Generic.HashSet[string]

# 長尺全体を一定間隔で粗く走査し、黒ではない候補帯を拾う。
for ($sec = $ProbeIntervalSec; $sec -lt $durationSec; $sec += $ProbeIntervalSec)
{
    $captureSec = [math]::Round($sec, 3)
    $key = [string]::Format([System.Globalization.CultureInfo]::InvariantCulture, "{0:0.###}", $captureSec)
    if (-not $seenSeconds.Add($key))
    {
        continue
    }

    $fileName = "coarse_{0:00000000}.jpg" -f [int]($captureSec * 1000)
    $outputPath = Join-Path $tempDir $fileName
    if (Invoke-FrameCapture -FfmpegPath $ffmpegPath -MoviePath $MoviePath -CaptureSec $captureSec -OutputPath $outputPath)
    {
        $candidate = New-CandidateRecord -CaptureSec $captureSec -ImagePath $outputPath -Phase "coarse"
        if ($null -ne $candidate)
        {
            $coarseCandidates.Add($candidate)
        }
    }
}

$refineOffsets = $RefineOffsetsSecCsv.Split(',', [System.StringSplitOptions]::RemoveEmptyEntries) |
    ForEach-Object { [double]::Parse($_.Trim(), [System.Globalization.CultureInfo]::InvariantCulture) }

$seedCandidates = $coarseCandidates |
    Sort-Object -Property `
        @{ Expression = "Score"; Descending = $true }, `
        @{ Expression = "AvgLuma"; Descending = $true } |
    Select-Object -First ([math]::Max($TargetCount * 3, 12))

$allCandidates = New-Object System.Collections.Generic.List[object]
foreach ($candidate in $coarseCandidates)
{
    $allCandidates.Add($candidate)
}

# 上位候補の前後を細かく掘り、短い明所を取りこぼしにくくする。
foreach ($seed in $seedCandidates)
{
    foreach ($offset in $refineOffsets)
    {
        $captureSec = [math]::Round([math]::Min([math]::Max($seed.CaptureSec + $offset, 0.0), $durationSec - 0.05), 3)
        $key = [string]::Format([System.Globalization.CultureInfo]::InvariantCulture, "{0:0.###}", $captureSec)
        if (-not $seenSeconds.Add($key))
        {
            continue
        }

        $fileName = "refine_{0:00000000}.jpg" -f [int]($captureSec * 1000)
        $outputPath = Join-Path $tempDir $fileName
        if (Invoke-FrameCapture -FfmpegPath $ffmpegPath -MoviePath $MoviePath -CaptureSec $captureSec -OutputPath $outputPath)
        {
            $candidate = New-CandidateRecord -CaptureSec $captureSec -ImagePath $outputPath -Phase "refine"
            if ($null -ne $candidate)
            {
                $allCandidates.Add($candidate)
            }
        }
    }
}

$selectedCandidates = @(Select-UniqueCandidates -Candidates $allCandidates.ToArray() -TargetCount $TargetCount -MinimumSpacingSec $MinimumSpacingSec -NearBlackLumaThreshold $NearBlackLumaThreshold)

$index = 1
foreach ($candidate in $selectedCandidates)
{
    $targetPath = Join-Path $selectedDir ("selected_{0:00}_{1:00000000}.jpg" -f $index, [int]($candidate.CaptureSec * 1000))
    Copy-Item -Force -Path $candidate.ImagePath -Destination $targetPath
    $candidate | Add-Member -NotePropertyName SelectedPath -NotePropertyValue $targetPath
    $index++
}

$reportPath = Join-Path $OutputRoot "report.csv"
$allCandidates |
    Sort-Object CaptureSec |
    Select-Object CaptureSec, Phase, AvgLuma, AvgSaturation, LumaStdDev, Score, ImagePath |
    Export-Csv -NoTypeInformation -Encoding utf8 -Path $reportPath

$selectedSummary = @(
    foreach ($candidate in $selectedCandidates)
    {
        [pscustomobject]@{
            CaptureSec = $candidate.CaptureSec
            AvgLuma = $candidate.AvgLuma
            AvgSaturation = $candidate.AvgSaturation
            LumaStdDev = $candidate.LumaStdDev
            Score = $candidate.Score
            SelectedPath = $candidate.SelectedPath
        }
    }
)

$candidateCount = [int]$allCandidates.Count
$selectedCount = [int]@($selectedCandidates).Count
$durationRounded = [math]::Round($durationSec, 3)

$summary = [ordered]@{
    MoviePath = $MoviePath
    DurationSec = $durationRounded
    ProbeIntervalSec = $ProbeIntervalSec
    NearBlackLumaThreshold = $NearBlackLumaThreshold
    CandidateCount = $candidateCount
    SelectedCount = $selectedCount
    OutputRoot = $OutputRoot
    SelectedDir = $selectedDir
    ReportPath = $reportPath
    Selected = $selectedSummary
}

[pscustomobject]$summary | ConvertTo-Json -Depth 5
