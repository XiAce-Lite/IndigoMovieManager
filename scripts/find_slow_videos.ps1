$logPath = Join-Path $env:LOCALAPPDATA "IndigoMovieManager_fork\logs\debug-runtime.log"
$repoRoot = Resolve-Path (Join-Path $PSScriptRoot "..")
$outPath = Join-Path $repoRoot "Logs\ProblematicVideos\problematic_list.txt"
New-Item -ItemType Directory -Path (Split-Path -Path $outPath -Parent) -Force | Out-Null

$lines = Get-Content $logPath -Encoding UTF8
$currentStartTime = $null
$problematicThresholdMs = 1500 # Adjust this threshold (1.5s) to define "took a long time"
$results = @()

$currentMovie = $null

foreach ($line in $lines) {
    if ($line -match "^(?<time>[\d-]+\s+[\d:.]+)\s+\[task-start\]\s+CreateThumbAsync") {
        $currentStartTime = [datetime]::ParseExact($matches['time'], "yyyy-MM-dd HH:mm:ss.fff", $null)
        $currentMovie = $null
    }
    elseif ($line -match "\[thumbnail-path\]\s+Created thumbnail saved to:\s+(?<path>.*\\(?<name>[^\\]+)\.#.*?\.jpg)") {
        $currentMovie = $matches['name']
    }
    elseif ($line -match "\[thumbnail\]\s+thumb open failed:.*?movie='(?<path>.*?)'") {
        $currentMovie = [System.IO.Path]::GetFileNameWithoutExtension($matches['path']) + " (FAILED)"
    }
    elseif ($line -match "^(?<time>[\d-]+\s+[\d:.]+)\s+\[task-end\]\s+CreateThumbAsync" -and $currentStartTime -ne $null) {
        $endTime = [datetime]::ParseExact($matches['time'], "yyyy-MM-dd HH:mm:ss.fff", $null)
        $elapsed = ($endTime - $currentStartTime).TotalMilliseconds
        
        if ($elapsed -gt $problematicThresholdMs -or ($currentMovie -and $currentMovie -match "FAILED")) {
            if (-not $currentMovie) { $currentMovie = "Unknown_Video" }
            $results += [PSCustomObject]@{
                Movie  = $currentMovie
                TimeMs = $elapsed
                Status = if ($currentMovie -match "FAILED") { "Failed" } else { "Success" }
            }
        }
        $currentStartTime = $null
    }
}

$results | Sort-Object TimeMs -Descending | Format-Table -AutoSize | Out-File $outPath -Encoding UTF8
Write-Output "Extracted problematic videos to $outPath"
