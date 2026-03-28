param(
    [Parameter(Mandatory = $true)]
    [string]$MoviePath,
    [string]$ProbeSecondsCsv = '30,300,900',
    [int]$TimeoutSec = 20,
    [string]$OutputRoot = ''
)

$ErrorActionPreference = 'Stop'

Add-Type -AssemblyName System.Drawing

function Get-AverageLuma {
    param(
        [Parameter(Mandatory = $true)]
        [string]$ImagePath
    )

    $bitmap = [System.Drawing.Bitmap]::new($ImagePath)
    try {
        $sum = 0.0
        $count = 0

        # ざっくりした黒判定用途なので 8px 間引きで十分とする。
        for ($y = 0; $y -lt $bitmap.Height; $y += 8) {
            for ($x = 0; $x -lt $bitmap.Width; $x += 8) {
                $pixel = $bitmap.GetPixel($x, $y)
                $sum += (0.2126 * $pixel.R) + (0.7152 * $pixel.G) + (0.0722 * $pixel.B)
                $count++
            }
        }

        return [Math]::Round($sum / [Math]::Max(1, $count), 2)
    }
    finally {
        $bitmap.Dispose()
    }
}

if ([string]::IsNullOrWhiteSpace($OutputRoot)) {
    $safeName = [IO.Path]::GetFileNameWithoutExtension($MoviePath)
    if ($safeName.Length -gt 24) {
        $safeName = $safeName.Substring(0, 24)
    }

    $OutputRoot = Join-Path `
        'C:\Users\na6ce\source\repos\IndigoMovieManager_fork_workthree\.codex_build' `
        ("manual-huge-av1-seek_" + $safeName + "_" + (Get-Date -Format 'yyyyMMdd_HHmmss'))
}

New-Item -ItemType Directory -Force -Path $OutputRoot | Out-Null

$rows = New-Object System.Collections.Generic.List[object]
$probeSeconds = $ProbeSecondsCsv.Split(',') | ForEach-Object { $_.Trim() } | Where-Object { $_ }

foreach ($probeSecText in $probeSeconds) {
    $jpgPath = Join-Path $OutputRoot ("sec-" + $probeSecText + ".jpg")
    $psi = New-Object System.Diagnostics.ProcessStartInfo
    $psi.FileName = 'ffmpeg'
    @('-y', '-ss', $probeSecText, '-i', $MoviePath, '-frames:v', '1', '-an', '-sn', '-q:v', '2', $jpgPath) |
        ForEach-Object {
            [void]$psi.ArgumentList.Add($_)
        }
    $psi.UseShellExecute = $false
    $psi.RedirectStandardOutput = $true
    $psi.RedirectStandardError = $true

    $sw = [System.Diagnostics.Stopwatch]::StartNew()
    $process = [System.Diagnostics.Process]::Start($psi)

    if (-not $process.WaitForExit($TimeoutSec * 1000)) {
        $process.Kill($true)
        $rows.Add([pscustomobject]@{
            CaptureSec = $probeSecText
            Exists = $false
            ExitCode = -1
            ElapsedMs = $sw.ElapsedMilliseconds
            AvgLuma = -1
            Note = "timeout>$TimeoutSec" 
            ImagePath = $jpgPath
        }) | Out-Null
        continue
    }

    $sw.Stop()
    $stderr = $process.StandardError.ReadToEnd()
    $exists = Test-Path $jpgPath
    $avgLuma = if ($exists) { Get-AverageLuma -ImagePath $jpgPath } else { -1 }
    $firstErrorLine = ''
    if (-not $exists) {
        $firstErrorLine = ($stderr -split "`r?`n" | Where-Object { $_ } | Select-Object -First 1)
    }

    $rows.Add([pscustomobject]@{
        CaptureSec = $probeSecText
        Exists = $exists
        ExitCode = $process.ExitCode
        ElapsedMs = $sw.ElapsedMilliseconds
        AvgLuma = $avgLuma
        Note = $firstErrorLine
        ImagePath = $jpgPath
    }) | Out-Null
}

$summaryJsonPath = Join-Path $OutputRoot 'summary.json'
$summaryCsvPath = Join-Path $OutputRoot 'summary.csv'

$rows | ConvertTo-Json -Depth 4 | Set-Content -Encoding utf8 -Path $summaryJsonPath
$rows | Export-Csv -NoTypeInformation -Encoding utf8 -Path $summaryCsvPath

Get-Content -Raw $summaryJsonPath
