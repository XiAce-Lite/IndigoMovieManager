param(
    [Parameter(Mandatory = $true)]
    [string]$InputFolder,
    [string[]]$Engines = @("autogen", "ffmediatoolkit", "ffmpeg1pass"),
    [int]$Iteration = 1,
    [int]$Warmup = 1,
    [int]$TabIndex = 4,
    [string]$GpuMode = "cuda",
    [int]$FileTimeoutSec = 300,
    [string]$Configuration = "Debug",
    [string]$Platform = "x64",
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
if ($FileTimeoutSec -lt 30 -or $FileTimeoutSec -gt 7200) {
    throw "FileTimeoutSec は 30 から 7200 の範囲で指定してください。"
}

# スクリプト配置場所からリポジトリルートへ移動する。
$repoRoot = Resolve-Path (Join-Path $PSScriptRoot "..\..")
Set-Location $repoRoot

$msbuildPath = "C:\Program Files\Microsoft Visual Studio\18\Community\MSBuild\Current\Bin\MSBuild.exe"
$singleBenchScript = Join-Path $repoRoot "Thumbnail\Test\run_thumbnail_engine_bench.ps1"
$benchLogDir = Join-Path $env:LOCALAPPDATA "IndigoMovieManager_fork_workthree\logs"

$engines = $Engines |
    ForEach-Object { [regex]::Split($_, "[\s,;]+") } |
    ForEach-Object { $_.Trim().ToLowerInvariant() } |
    Where-Object { -not [string]::IsNullOrWhiteSpace($_) } |
    Sort-Object -Unique
if ($engines.Count -lt 1) {
    throw "Engines が空です。"
}
$enginesCsv = $engines -join ","
$expectedRowCount = $engines.Count * $Iteration

function Resolve-PanelCount {
    param(
        [Parameter(Mandatory = $true)]
        [int]$TargetTabIndex
    )

    switch ($TargetTabIndex) {
        0 { return 3 }
        1 { return 3 }
        2 { return 1 }
        3 { return 5 }
        4 { return 10 }
        99 { return 1 }
        default { return 1 }
    }
}

function Quote-Arg {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Value
    )

    if ($Value -match '[\s"]') {
        return '"' + ($Value -replace '"', '\"') + '"'
    }
    return $Value
}

function Read-FirstLogText {
    param(
        [string]$PrimaryPath,
        [string]$FallbackPath,
        [int]$MaxLines = 20
    )

    $text = ""
    try {
        if (Test-Path -LiteralPath $PrimaryPath -PathType Leaf) {
            $text = (Get-Content -Path $PrimaryPath -Encoding UTF8 | Select-Object -First $MaxLines) -join " | "
        }
        if ([string]::IsNullOrWhiteSpace($text) -and (Test-Path -LiteralPath $FallbackPath -PathType Leaf)) {
            $text = (Get-Content -Path $FallbackPath -Encoding UTF8 | Select-Object -First $MaxLines) -join " | "
        }
    }
    catch {
        $text = ""
    }

    return $text
}

function Stop-ProcessTree {
    param(
        [Parameter(Mandatory = $true)]
        [int]$RootProcessId
    )

    $all = Get-CimInstance Win32_Process
    $children = @{}
    foreach ($p in $all) {
        if (-not $children.ContainsKey($p.ParentProcessId)) {
            $children[$p.ParentProcessId] = [System.Collections.Generic.List[int]]::new()
        }
        $children[$p.ParentProcessId].Add([int]$p.ProcessId)
    }

    $stack = [System.Collections.Generic.Stack[int]]::new()
    $stack.Push($RootProcessId)
    $killList = [System.Collections.Generic.List[int]]::new()
    while ($stack.Count -gt 0) {
        $id = $stack.Pop()
        $killList.Add($id)
        if ($children.ContainsKey($id)) {
            foreach ($childId in $children[$id]) {
                $stack.Push($childId)
            }
        }
    }

    foreach ($id in ($killList | Sort-Object -Descending -Unique)) {
        try {
            Stop-Process -Id $id -Force -ErrorAction Stop
            Write-Host "killed PID=$id"
        }
        catch {
            # 既に終了済みは無視する。
        }
    }
}

function Stop-ResidualBenchProcesses {
    param(
        [Parameter(Mandatory = $true)]
        [int]$CurrentProcessId
    )

    $all = Get-CimInstance Win32_Process
    $procById = @{}
    foreach ($p in $all) {
        $procById[[int]$p.ProcessId] = $p
    }

    # 自プロセスの親系統は誤爆防止のため除外する。
    $exclude = [System.Collections.Generic.HashSet[int]]::new()
    $pidCursor = $CurrentProcessId
    while ($pidCursor -gt 0 -and $procById.ContainsKey($pidCursor)) {
        [void]$exclude.Add($pidCursor)
        $parent = [int]$procById[$pidCursor].ParentProcessId
        if ($parent -le 0 -or $exclude.Contains($parent)) {
            break
        }
        $pidCursor = $parent
    }

    $targets = $all | Where-Object {
        -not $exclude.Contains([int]$_.ProcessId) -and
        $_.CommandLine -and (
            $_.CommandLine -like '*run_thumbnail_engine_bench.ps1*' -or
            $_.CommandLine -like '*run_thumbnail_engine_bench_folder.ps1*' -or
            $_.CommandLine -like '*vstest.console.dll*ThumbnailEngineBenchTests*'
        )
    }

    if (-not $targets) {
        Write-Host "残存ベンチプロセス: なし"
        return
    }

    Write-Host "残存ベンチプロセスを停止: $($targets.Count)件"
    foreach ($p in $targets) {
        Stop-ProcessTree -RootProcessId ([int]$p.ProcessId)
    }
}

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
$panelCount = Resolve-PanelCount -TargetTabIndex $TabIndex

try {
    # 前回中断で残ったベンチ関連プロセスを起動前に掃除する。
    Stop-ResidualBenchProcesses -CurrentProcessId $PID
    [Environment]::SetEnvironmentVariable("IMM_THUMB_GPU_DECODE", $GpuMode)

    foreach ($movie in $movieFiles) {
        Write-Host "ベンチ実行: $($movie.FullName)"
        $startedAtSingle = Get-Date

        $singleArgs = @(
            '-File', (Quote-Arg $singleBenchScript),
            '-InputMovie', (Quote-Arg $movie.FullName),
            '-Engines', (Quote-Arg $enginesCsv),
            '-Iteration', $Iteration.ToString(),
            '-Warmup', $Warmup.ToString(),
            '-TabIndex', $TabIndex.ToString(),
            '-Configuration', (Quote-Arg $Configuration),
            '-Platform', (Quote-Arg $Platform),
            '-SkipBuild'
        )

        $singleArgLine = $singleArgs -join ' '
        $stdoutPath = Join-Path $env:TEMP ("imm_bench_single_stdout_{0}.log" -f ([guid]::NewGuid().ToString("N")))
        $stderrPath = Join-Path $env:TEMP ("imm_bench_single_stderr_{0}.log" -f ([guid]::NewGuid().ToString("N")))
        $child = Start-Process -FilePath 'pwsh' -ArgumentList $singleArgLine -PassThru -RedirectStandardOutput $stdoutPath -RedirectStandardError $stderrPath
        $finished = Wait-Process -Id $child.Id -Timeout $FileTimeoutSec -PassThru -ErrorAction SilentlyContinue
        if (-not $finished) {
            Write-Warning "タイムアウトでスキップ: movie=$($movie.FullName), timeout=${FileTimeoutSec}s"
            Stop-ProcessTree -RootProcessId $child.Id

            # タイムアウト時も集計しやすいよう、エンジン×反復回数ぶん失敗行を補完する。
            for ($iter = 1; $iter -le $Iteration; $iter++) {
                foreach ($engine in $engines) {
                    $allRows.Add([pscustomobject]@{
                            gpu_mode = $GpuMode
                            input_file_name = $movie.Name
                            input_full_path = $movie.FullName
                            engine = $engine
                            iteration = $iter
                            tab_index = $TabIndex
                            panel_count = $panelCount
                            elapsed_ms = [double]($FileTimeoutSec * 1000)
                            success = "failed"
                            duration_sec = ""
                            output_bytes = 0
                            output_path = ""
                            error_message = "timeout skip (${FileTimeoutSec}s)"
                            source_csv = ""
                        })
                }
            }
            Remove-Item -LiteralPath $stdoutPath, $stderrPath -ErrorAction SilentlyContinue
            continue
        }

        if ($child.ExitCode -ne 0) {
            $stderr = Read-FirstLogText -PrimaryPath $stderrPath -FallbackPath $stdoutPath -MaxLines 20

            Write-Warning "単体ベンチ失敗でスキップ: movie=$($movie.FullName), exit=$($child.ExitCode)"
            for ($iter = 1; $iter -le $Iteration; $iter++) {
                foreach ($engine in $engines) {
                    $allRows.Add([pscustomobject]@{
                            gpu_mode = $GpuMode
                            input_file_name = $movie.Name
                            input_full_path = $movie.FullName
                            engine = $engine
                            iteration = $iter
                            tab_index = $TabIndex
                            panel_count = $panelCount
                            elapsed_ms = 0
                            success = "failed"
                            duration_sec = ""
                            output_bytes = 0
                            output_path = ""
                            error_message = if ([string]::IsNullOrWhiteSpace($stderr)) { "single bench exit=$($child.ExitCode)" } else { "single bench exit=$($child.ExitCode): $stderr" }
                            source_csv = ""
                        })
                }
            }
            Remove-Item -LiteralPath $stdoutPath, $stderrPath -ErrorAction SilentlyContinue
            continue
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
        Remove-Item -LiteralPath $stdoutPath, $stderrPath -ErrorAction SilentlyContinue
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
