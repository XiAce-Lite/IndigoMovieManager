param(
    [Parameter(Mandatory = $true)]
    [string]$InputMovie,
    [string[]]$Engines = @("autogen", "ffmediatoolkit", "ffmpeg1pass", "opencv"),
    [int]$Iteration = 3,
    [int]$Warmup = 1,
    [int]$TabIndex = 0,
    [string]$Configuration = "Debug",
    [string]$Platform = "Any CPU",
    [switch]$SkipBuild
)

$ErrorActionPreference = "Stop"

if (-not (Test-Path -LiteralPath $InputMovie -PathType Leaf)) {
    throw "入力動画が見つかりません: $InputMovie"
}
$resolvedInputMovie = (Resolve-Path -LiteralPath $InputMovie).Path

if ($Iteration -lt 1 -or $Iteration -gt 100) {
    throw "Iteration は 1 から 100 の範囲で指定してください。"
}
if ($Warmup -lt 0 -or $Warmup -gt 10) {
    throw "Warmup は 0 から 10 の範囲で指定してください。"
}
if ($TabIndex -notin @(0, 1, 2, 3, 4, 99)) {
    throw "TabIndex は 0,1,2,3,4,99 のいずれかを指定してください。"
}

# スクリプト配置場所からリポジトリルートへ移動する。
$repoRoot = Resolve-Path (Join-Path $PSScriptRoot "..\..")
Set-Location $repoRoot

$msbuildPath = "C:\Program Files\Microsoft Visual Studio\18\Community\MSBuild\Current\Bin\MSBuild.exe"
$testProject = "Tests/IndigoMovieManager_fork.Tests/IndigoMovieManager_fork.Tests.csproj"
$testFilter = "FullyQualifiedName~ThumbnailEngineBenchTests.Bench_同一入力でエンジン別比較を実行する"
$benchLogDir = Join-Path $env:LOCALAPPDATA "IndigoMovieManager_fork\logs"
$startedAt = Get-Date
$expectedInputFileName = [System.IO.Path]::GetFileName($resolvedInputMovie)
$expectedEngines = $Engines |
    ForEach-Object { [regex]::Split($_, "[\s,;]+") } |
    ForEach-Object { $_.Trim().ToLowerInvariant() } |
    Where-Object { -not [string]::IsNullOrWhiteSpace($_) } |
    Sort-Object -Unique
if ($expectedEngines.Count -lt 1) {
    throw "Engines が空です。"
}
$normalizedEnginesCsv = $expectedEngines -join ","
$expectedRowCount = $expectedEngines.Count * $Iteration

function Resolve-BenchCsvForCurrentRun {
    param(
        [Parameter(Mandatory = $true)]
        [string]$LogDir,
        [Parameter(Mandatory = $true)]
        [datetime]$Since,
        [Parameter(Mandatory = $true)]
        [string]$InputFileName,
        [Parameter(Mandatory = $true)]
        [string[]]$Engines,
        [Parameter(Mandatory = $true)]
        [int]$ExpectedRows
    )

    $candidates = Get-ChildItem -LiteralPath $LogDir -Filter "thumbnail-engine-bench-*.csv" -File |
        Where-Object { $_.LastWriteTime -ge $Since.AddSeconds(-1) } |
        Sort-Object LastWriteTime -Descending

    foreach ($candidate in $candidates) {
        try {
            $rows = Import-Csv -LiteralPath $candidate.FullName
        }
        catch {
            continue
        }

        if (-not $rows -or $rows.Count -lt 1) {
            continue
        }

        if ($rows.Count -ne $ExpectedRows) {
            continue
        }

        $hasDifferentInput = ($rows | Where-Object { $_.input_file_name -ne $InputFileName } | Measure-Object).Count -gt 0
        if ($hasDifferentInput) {
            continue
        }

        $actualEngines = $rows |
            Select-Object -ExpandProperty engine -Unique |
            ForEach-Object { $_.Trim().ToLowerInvariant() } |
            Sort-Object -Unique

        if (($actualEngines -join ",") -ne ($Engines -join ",")) {
            continue
        }

        return $candidate
    }

    return $null
}

# 実行前の環境変数を退避する。
$oldInput = [Environment]::GetEnvironmentVariable("IMM_BENCH_INPUT")
$oldEngines = [Environment]::GetEnvironmentVariable("IMM_BENCH_ENGINES")
$oldIter = [Environment]::GetEnvironmentVariable("IMM_BENCH_ITER")
$oldWarmup = [Environment]::GetEnvironmentVariable("IMM_BENCH_WARMUP")
$oldTabIndex = [Environment]::GetEnvironmentVariable("IMM_BENCH_TAB_INDEX")

try {
    [Environment]::SetEnvironmentVariable("IMM_BENCH_INPUT", $resolvedInputMovie)
    # エンジン指定は正規化済みCSVで環境変数へ渡す。
    [Environment]::SetEnvironmentVariable("IMM_BENCH_ENGINES", $normalizedEnginesCsv)
    [Environment]::SetEnvironmentVariable("IMM_BENCH_ITER", $Iteration.ToString())
    [Environment]::SetEnvironmentVariable("IMM_BENCH_WARMUP", $Warmup.ToString())
    [Environment]::SetEnvironmentVariable("IMM_BENCH_TAB_INDEX", $TabIndex.ToString())

    if (-not $SkipBuild) {
        if (-not (Test-Path $msbuildPath)) {
            throw "MSBuild が見つかりません: $msbuildPath"
        }

        # COM参照を含むため、先にMSBuildでビルドする。
        & $msbuildPath "IndigoMovieManager_fork.sln" "/p:Configuration=$Configuration" "/p:Platform=$Platform" "/m"
        if ($LASTEXITCODE -ne 0) {
            throw "MSBuild が失敗しました。exit code: $LASTEXITCODE"
        }
    }

    $args = @(
        "test",
        $testProject,
        "-c",
        $Configuration,
        "--no-build",
        "--filter",
        $testFilter
    )

    # 同一入力でエンジン比較ベンチを実行する。
    & dotnet @args
    if ($LASTEXITCODE -ne 0) {
        throw "dotnet test が失敗しました。exit code: $LASTEXITCODE"
    }

    if (-not (Test-Path -LiteralPath $benchLogDir -PathType Container)) {
        throw "ベンチCSVフォルダが見つかりません: $benchLogDir"
    }

    $csv = Resolve-BenchCsvForCurrentRun `
        -LogDir $benchLogDir `
        -Since $startedAt `
        -InputFileName $expectedInputFileName `
        -Engines $expectedEngines `
        -ExpectedRows $expectedRowCount

    if (-not $csv) {
        throw "ベンチCSVを特定できませんでした。条件: input_file_name='$expectedInputFileName', engines='$($expectedEngines -join ",")', rows=$expectedRowCount。$benchLogDir を確認してください。"
    }

    $rows = Import-Csv -LiteralPath $csv.FullName
    if (-not $rows -or $rows.Count -eq 0) {
        throw "ベンチCSVにデータ行がありません: $($csv.FullName)"
    }

    $summary = $rows |
        Group-Object engine |
        ForEach-Object {
            $elapsed = $_.Group | ForEach-Object { [double]::Parse($_.elapsed_ms, [System.Globalization.CultureInfo]::InvariantCulture) }
            $successCount = ($_.Group | Where-Object { $_.success -eq "success" }).Count
            [pscustomobject]@{
                Engine = $_.Name
                Runs = $_.Count
                Success = $successCount
                Failed = $_.Count - $successCount
                AvgMs = [math]::Round(($elapsed | Measure-Object -Average).Average, 2)
                MinMs = [math]::Round(($elapsed | Measure-Object -Minimum).Minimum, 2)
                MaxMs = [math]::Round(($elapsed | Measure-Object -Maximum).Maximum, 2)
            }
        } |
        Sort-Object Engine

    Write-Host ""
    Write-Host "ベンチ完了: $($csv.FullName)"
    $summary | Format-Table -AutoSize
}
finally {
    # 実行後は環境変数を元に戻す。
    [Environment]::SetEnvironmentVariable("IMM_BENCH_INPUT", $oldInput)
    [Environment]::SetEnvironmentVariable("IMM_BENCH_ENGINES", $oldEngines)
    [Environment]::SetEnvironmentVariable("IMM_BENCH_ITER", $oldIter)
    [Environment]::SetEnvironmentVariable("IMM_BENCH_WARMUP", $oldWarmup)
    [Environment]::SetEnvironmentVariable("IMM_BENCH_TAB_INDEX", $oldTabIndex)
}
