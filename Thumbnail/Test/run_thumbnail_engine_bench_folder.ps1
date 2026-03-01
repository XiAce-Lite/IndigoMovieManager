param(
    [Parameter(Mandatory = $true)]
    [string]$InputFolder,
    [string]$Engines = "autogen,ffmediatoolkit,ffmpeg1pass",
    [int]$Iteration = 1,
    [int]$Warmup = 1,
    [int]$TabIndex = 4,
    [string]$GpuMode = "cuda",
    [string]$Configuration = "Debug",
    [string]$Platform = "Any CPU",
    [switch]$SkipBuild
)

$ErrorActionPreference = "Stop"

if (-not (Test-Path -LiteralPath $InputFolder -PathType Container)) {
    throw "入力フォルダが見つかりません: $InputFolder"
}
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
$singleBenchScript = Join-Path $repoRoot "Thumbnail\Test\run_thumbnail_engine_bench.ps1"
$benchLogDir = Join-Path $env:LOCALAPPDATA "IndigoMovieManager_fork\logs"

$engines = $Engines.Split(",", [System.StringSplitOptions]::RemoveEmptyEntries) |
    ForEach-Object { $_.Trim().ToLowerInvariant() } |
    Where-Object { -not [string]::IsNullOrWhiteSpace($_) } |
    Sort-Object -Unique
if ($engines.Count -lt 1) {
    throw "Engines が空です。"
}
$expectedRowCount = $engines.Count * $Iteration

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
        [int]$ExpectedRows,
        [Parameter(Mandatory = $true)]
        [int]$TabIndex
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

        if (-not $rows -or $rows.Count -ne $ExpectedRows) {
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

        $hasDifferentTab = ($rows | Where-Object { [int]$_.tab_index -ne $TabIndex } | Measure-Object).Count -gt 0
        if ($hasDifferentTab) {
            continue
        }

        return $candidate
    }

    return $null
}

$movieFiles = Get-ChildItem -LiteralPath $InputFolder -File |
    Where-Object { $_.Extension -match '^\.(mp4|mkv|mov|wmv|avi|flv|ts|m4v)$' } |
    Sort-Object Name
if ($movieFiles.Count -lt 1) {
    throw "対象フォルダに動画ファイルが見つかりませんでした: $InputFolder"
}

if (-not $SkipBuild) {
    if (-not (Test-Path -LiteralPath $msbuildPath -PathType Leaf)) {
        throw "MSBuild が見つかりません: $msbuildPath"
    }

    # COM参照を含むため、先にMSBuildでビルドする。
    & $msbuildPath "IndigoMovieManager_fork.sln" "/p:Configuration=$Configuration" "/p:Platform=$Platform" "/m"
    if ($LASTEXITCODE -ne 0) {
        throw "MSBuild が失敗しました。exit code: $LASTEXITCODE"
    }
}

$oldGpuMode = [Environment]::GetEnvironmentVariable("IMM_THUMB_GPU_DECODE")
$allRows = [System.Collections.Generic.List[object]]::new()
$startedAtFolder = Get-Date

try {
    [Environment]::SetEnvironmentVariable("IMM_THUMB_GPU_DECODE", $GpuMode)

    foreach ($movie in $movieFiles) {
        Write-Host "ベンチ実行: $($movie.FullName)"
        $startedAtSingle = Get-Date

        & pwsh -File $singleBenchScript `
            -InputMovie $movie.FullName `
            -Engines ($engines -join ",") `
            -Iteration $Iteration `
            -Warmup $Warmup `
            -TabIndex $TabIndex `
            -Configuration $Configuration `
            -Platform $Platform `
            -SkipBuild
        if ($LASTEXITCODE -ne 0) {
            throw "単体ベンチ実行に失敗しました。movie=$($movie.FullName) exit=$LASTEXITCODE"
        }

        $csv = Resolve-BenchCsvForCurrentRun `
            -LogDir $benchLogDir `
            -Since $startedAtSingle `
            -InputFileName $movie.Name `
            -Engines $engines `
            -ExpectedRows $expectedRowCount `
            -TabIndex $TabIndex
        if (-not $csv) {
            throw "単体ベンチCSVを特定できませんでした: $($movie.FullName)"
        }

        $rows = Import-Csv -LiteralPath $csv.FullName
        foreach ($row in $rows) {
            $allRows.Add([pscustomobject]@{
                    gpu_mode = $GpuMode
                    input_file_name = $row.input_file_name
                    input_full_path = $movie.FullName
                    engine = $row.engine
                    iteration = [int]$row.iteration
                    tab_index = [int]$row.tab_index
                    panel_count = [int]$row.panel_count
                    elapsed_ms = [double]$row.elapsed_ms
                    success = $row.success
                    duration_sec = $row.duration_sec
                    output_bytes = $row.output_bytes
                    output_path = $row.output_path
                    error_message = $row.error_message
                    source_csv = $csv.FullName
                })
        }
    }
}
finally {
    [Environment]::SetEnvironmentVariable("IMM_THUMB_GPU_DECODE", $oldGpuMode)
}

$ts = $startedAtFolder.ToString("yyyyMMdd_HHmmss")
$outCombined = Join-Path $repoRoot ("logs\thumbnail-engine-bench-folder-tab{0}-combined_{1}.csv" -f $TabIndex, $ts)
$outSummary = Join-Path $repoRoot ("logs\thumbnail-engine-bench-folder-tab{0}-summary_{1}.csv" -f $TabIndex, $ts)

$allRows | Export-Csv -LiteralPath $outCombined -Encoding UTF8 -NoTypeInformation

$summary = $allRows |
    Group-Object gpu_mode, engine |
    ForEach-Object {
        $successRows = $_.Group | Where-Object { $_.success -eq "success" }
        $successCount = $successRows.Count
        $failedCount = $_.Count - $successCount
        $avg = 0
        $min = 0
        $max = 0
        if ($successCount -gt 0) {
            $elapsed = $successRows | ForEach-Object { [double]$_.elapsed_ms }
            $avg = [math]::Round(($elapsed | Measure-Object -Average).Average, 2)
            $min = [math]::Round(($elapsed | Measure-Object -Minimum).Minimum, 2)
            $max = [math]::Round(($elapsed | Measure-Object -Maximum).Maximum, 2)
        }
        [pscustomobject]@{
            gpu_mode = $_.Group[0].gpu_mode
            engine = $_.Group[0].engine
            runs = $_.Count
            success = $successCount
            failed = $failedCount
            avg_ms_success = $avg
            min_ms_success = $min
            max_ms_success = $max
        }
    } |
    Sort-Object gpu_mode, engine

$summary | Export-Csv -LiteralPath $outSummary -Encoding UTF8 -NoTypeInformation

Write-Host ""
Write-Host "フォルダベンチ完了"
Write-Host "combined: $outCombined"
Write-Host "summary : $outSummary"
$summary | Format-Table -AutoSize
