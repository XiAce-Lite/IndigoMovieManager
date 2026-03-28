param(
    [Parameter(Mandatory = $true)]
    [string]$MoviePath,

    [Parameter(Mandatory = $true)]
    [string]$EngineId,

    [int]$TimeoutSec = 15,

    [string]$DbName = "rescue-child-live",

    [string]$ThumbFolder = "",

    [int]$TabIndex = 2,

    [string]$SourceMoviePath = "",

    [string]$ResultRoot = "",

    [string]$RescueWorkerExePath = ""
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

if (-not (Test-Path $MoviePath))
{
    throw "movie not found: $MoviePath"
}

$repoRoot = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
if ([string]::IsNullOrWhiteSpace($RescueWorkerExePath))
{
    $RescueWorkerExePath = Join-Path $repoRoot "src\IndigoMovieManager.Thumbnail.RescueWorker\bin\x64\Debug\net8.0-windows\IndigoMovieManager.Thumbnail.RescueWorker.exe"
}

if (-not (Test-Path $RescueWorkerExePath))
{
    throw "rescue worker exe not found: $RescueWorkerExePath"
}

if ([string]::IsNullOrWhiteSpace($SourceMoviePath))
{
    $SourceMoviePath = $MoviePath
}

if ([string]::IsNullOrWhiteSpace($ResultRoot))
{
    $ResultRoot = Join-Path $repoRoot ".codex_build\rescue-attempt-child-live"
}

if ([string]::IsNullOrWhiteSpace($ThumbFolder))
{
    $ThumbFolder = Join-Path $ResultRoot "thumb"
}

New-Item -ItemType Directory -Force -Path $ResultRoot | Out-Null
New-Item -ItemType Directory -Force -Path $ThumbFolder | Out-Null

$resultJsonPath = Join-Path $ResultRoot "result.json"
$stdoutPath = Join-Path $ResultRoot "stdout.txt"
$stderrPath = Join-Path $ResultRoot "stderr.txt"

foreach ($path in @($resultJsonPath, $stdoutPath, $stderrPath))
{
    if (Test-Path $path)
    {
        Remove-Item $path -Force
    }
}

$movieSizeBytes = (Get-Item $MoviePath).Length

# rescue worker child を 1 engine 試行だけ起動し、外側から timeout kill できるかを確認する。
$startInfo = New-Object System.Diagnostics.ProcessStartInfo
$startInfo.FileName = $RescueWorkerExePath
$startInfo.WorkingDirectory = Split-Path -Parent $RescueWorkerExePath
$startInfo.UseShellExecute = $false
$startInfo.CreateNoWindow = $true
$startInfo.RedirectStandardOutput = $true
$startInfo.RedirectStandardError = $true
$null = $startInfo.ArgumentList.Add("--attempt-child")
$null = $startInfo.ArgumentList.Add("--engine")
$null = $startInfo.ArgumentList.Add($EngineId)
$null = $startInfo.ArgumentList.Add("--movie")
$null = $startInfo.ArgumentList.Add($MoviePath)
$null = $startInfo.ArgumentList.Add("--source-movie")
$null = $startInfo.ArgumentList.Add($SourceMoviePath)
$null = $startInfo.ArgumentList.Add("--db-name")
$null = $startInfo.ArgumentList.Add($DbName)
$null = $startInfo.ArgumentList.Add("--thumb-folder")
$null = $startInfo.ArgumentList.Add($ThumbFolder)
$null = $startInfo.ArgumentList.Add("--tab-index")
$null = $startInfo.ArgumentList.Add($TabIndex.ToString())
$null = $startInfo.ArgumentList.Add("--movie-size-bytes")
$null = $startInfo.ArgumentList.Add($movieSizeBytes.ToString())
$null = $startInfo.ArgumentList.Add("--result-json")
$null = $startInfo.ArgumentList.Add($resultJsonPath)

$process = New-Object System.Diagnostics.Process
$process.StartInfo = $startInfo
$null = $process.Start()

$exitedWithinTimeout = $process.WaitForExit($TimeoutSec * 1000)
if (-not $exitedWithinTimeout)
{
    $process.Kill($true)
    $process.WaitForExit()
}

[IO.File]::WriteAllText($stdoutPath, $process.StandardOutput.ReadToEnd())
[IO.File]::WriteAllText($stderrPath, $process.StandardError.ReadToEnd())

$thumbFiles = @(
    Get-ChildItem -Path $ThumbFolder -Recurse -File -ErrorAction SilentlyContinue
)

[pscustomobject]@{
    MoviePath = $MoviePath
    EngineId = $EngineId
    TimeoutSec = $TimeoutSec
    ExitedWithinTimeout = $exitedWithinTimeout
    ExitCode = $process.ExitCode
    ResultJsonExists = (Test-Path $resultJsonPath)
    ResultJsonPath = $resultJsonPath
    ThumbFileCount = $thumbFiles.Count
    ThumbFolder = $ThumbFolder
    StdoutPath = $stdoutPath
    StderrPath = $stderrPath
    StdoutFirst = ((Get-Content $stdoutPath -ErrorAction SilentlyContinue | Select-Object -First 5) -join " | ")
    StderrFirst = ((Get-Content $stderrPath -ErrorAction SilentlyContinue | Select-Object -First 5) -join " | ")
} | ConvertTo-Json -Depth 3 -Compress
