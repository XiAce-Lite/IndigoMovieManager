param(
    [ValidateSet("Bootstrap", "SyncDocs")]
    [string]$Mode = "Bootstrap",
    [string]$RepoRoot = "",
    [string]$PrivateRepoRoot = "",
    [switch]$DryRun
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

function Resolve-RepoRoot {
    $scriptDir = Split-Path -Parent $PSCommandPath
    return (Resolve-Path (Join-Path $scriptDir "..")).Path
}

function Resolve-PrivateRepoRoot {
    param([string]$RepoRootValue, [string]$PrivateRepoRootValue)

    if (-not [string]::IsNullOrWhiteSpace($PrivateRepoRootValue)) {
        return [System.IO.Path]::GetFullPath($PrivateRepoRootValue)
    }

    $repoParent = Split-Path -Parent $RepoRootValue
    return Join-Path $repoParent "IndigoMovieEngine"
}

function Ensure-Directory {
    param([string]$PathValue)

    if ($DryRun) {
        Write-Host "[dryrun] mkdir $PathValue"
        return
    }

    New-Item -ItemType Directory -Force -Path $PathValue | Out-Null
}

function Write-TextFileNoBom {
    param(
        [string]$PathValue,
        [string]$Text
    )

    if ($DryRun) {
        Write-Host "[dryrun] write $PathValue"
        return
    }

    $parent = Split-Path -Parent $PathValue
    if (-not [string]::IsNullOrWhiteSpace($parent)) {
        New-Item -ItemType Directory -Force -Path $parent | Out-Null
    }

    $normalized = ($Text ?? "") -replace "`r`n", "`n"
    Set-Content -Encoding utf8NoBOM -NoNewline -Path $PathValue -Value $normalized
}

function Copy-TextFileNoBom {
    param(
        [string]$SourcePath,
        [string]$TargetPath
    )

    if (-not (Test-Path $SourcePath)) {
        throw "コピー元が見つかりません: $SourcePath"
    }

    if ($DryRun) {
        Write-Host "[dryrun] copy $SourcePath -> $TargetPath"
        return
    }

    $parent = Split-Path -Parent $TargetPath
    if (-not [string]::IsNullOrWhiteSpace($parent)) {
        New-Item -ItemType Directory -Force -Path $parent | Out-Null
    }

    $content = Get-Content -Encoding UTF8 -Raw -Path $SourcePath
    $normalized = $content -replace "`r`n", "`n"
    Set-Content -Encoding utf8NoBOM -NoNewline -Path $TargetPath -Value $normalized
}

function Initialize-PrivateRepoLayout {
    param([string]$TargetRoot)

    $dirs = @(
        "src\IndigoMovieManager.Thumbnail.Contracts",
        "src\IndigoMovieManager.Thumbnail.Engine",
        "src\IndigoMovieManager.Thumbnail.FailureDb",
        "src\IndigoMovieManager.Thumbnail.RescueWorker",
        "tests",
        "scripts",
        "docs",
        ".github\workflows"
    )

    foreach ($relative in $dirs) {
        Ensure-Directory (Join-Path $TargetRoot $relative)
    }

    Write-TextFileNoBom (
        Join-Path $TargetRoot "README.md"
    ) @'
# IndigoMovieEngine

Private repo for engine / worker.

- Contracts
- Engine
- FailureDb
- RescueWorker

Public repo から app に機能を追加し、配る責務を守るための外だし先である。
'@

    Write-TextFileNoBom (
        Join-Path $TargetRoot "docs\README.md"
    ) @'
# docs

このフォルダには、Public repo から持ち出す契約・構成・運用の正本を置く。
'@

    Write-TextFileNoBom (
        Join-Path $TargetRoot ".gitignore"
    ) @'
bin/
obj/
artifacts/
logs/
*.user
*.suo
'@
}

function Sync-PrivateRepoDocs {
    param(
        [string]$RepoRootValue,
        [string]$TargetRoot
    )

    $docMap = @(
        @{
            Source = "src\IndigoMovieManager.Thumbnail.RescueWorker\Docs\設計メモ_repo構成表_Public本体_PrivateEngine_2026-04-04.md"
            Target = "docs\設計メモ_repo構成表_Public本体_PrivateEngine_2026-04-04.md"
        },
        @{
            Source = "src\IndigoMovieManager.Thumbnail.RescueWorker\Docs\Implementation Plan_RescueWorker_v1契約_PrivateRepo前提_2026-04-04.md"
            Target = "docs\Implementation Plan_RescueWorker_v1契約_PrivateRepo前提_2026-04-04.md"
        },
        @{
            Source = "Thumbnail\Docs\設計メモ_engine-client責務表_Public本体責務集中_2026-04-04.md"
            Target = "docs\設計メモ_engine-client責務表_Public本体責務集中_2026-04-04.md"
        },
        @{
            Source = "Thumbnail\Docs\Implementation Plan_workerとサムネイル作成エンジン外だし_2026-04-01.md"
            Target = "docs\Implementation Plan_workerとサムネイル作成エンジン外だし_2026-04-01.md"
        }
    )

    foreach ($item in $docMap) {
        $sourcePath = Join-Path $RepoRootValue $item.Source
        $targetPath = Join-Path $TargetRoot $item.Target
        Copy-TextFileNoBom -SourcePath $sourcePath -TargetPath $targetPath
    }
}

if ([string]::IsNullOrWhiteSpace($RepoRoot)) {
    $RepoRoot = Resolve-RepoRoot
}

$RepoRoot = [System.IO.Path]::GetFullPath($RepoRoot)
$PrivateRepoRoot = Resolve-PrivateRepoRoot -RepoRootValue $RepoRoot -PrivateRepoRootValue $PrivateRepoRoot

if (-not (Test-Path $RepoRoot)) {
    throw "RepoRoot が見つかりません: $RepoRoot"
}

if ($Mode -eq "Bootstrap") {
    Initialize-PrivateRepoLayout -TargetRoot $PrivateRepoRoot
    Sync-PrivateRepoDocs -RepoRootValue $RepoRoot -TargetRoot $PrivateRepoRoot
}
else {
    Ensure-Directory (Join-Path $PrivateRepoRoot "docs")
    Sync-PrivateRepoDocs -RepoRootValue $RepoRoot -TargetRoot $PrivateRepoRoot
}

Write-Host "[bootstrap] completed mode=$Mode repo=$RepoRoot privateRepo=$PrivateRepoRoot"
