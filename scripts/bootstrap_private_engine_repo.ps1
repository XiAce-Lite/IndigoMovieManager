param(
    [ValidateSet("Bootstrap", "SyncDocs", "SyncSource")]
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

function Should-TreatAsTextFile {
    param([string]$PathValue)

    $textExtensions = @(
        ".cs",
        ".csproj",
        ".props",
        ".targets",
        ".resx",
        ".xaml",
        ".config",
        ".json",
        ".md",
        ".txt",
        ".xml",
        ".sln",
        ".yml",
        ".yaml",
        ".ps1",
        ".bat",
        ".cmd",
        ".settings",
        ".runsettings",
        ".editorconfig",
        ".gitattributes",
        ".gitignore"
    )

    $extension = [System.IO.Path]::GetExtension($PathValue).ToLowerInvariant()
    return $textExtensions -contains $extension
}

function Copy-FilePreservingKind {
    param(
        [string]$SourcePath,
        [string]$TargetPath
    )

    if (Should-TreatAsTextFile -PathValue $SourcePath) {
        Copy-TextFileNoBom -SourcePath $SourcePath -TargetPath $TargetPath
        return
    }

    if ($DryRun) {
        Write-Host "[dryrun] copy $SourcePath -> $TargetPath"
        return
    }

    $parent = Split-Path -Parent $TargetPath
    if (-not [string]::IsNullOrWhiteSpace($parent)) {
        New-Item -ItemType Directory -Force -Path $parent | Out-Null
    }

    Copy-Item -Force -Path $SourcePath -Destination $TargetPath
}

function Copy-TreeFiltered {
    param(
        [string]$SourceRoot,
        [string]$TargetRoot
    )

    if (-not (Test-Path $SourceRoot)) {
        throw "コピー元が見つかりません: $SourceRoot"
    }

    $excludePattern = '\\(bin|obj|publish|TestResults|\.vs|\.codex_build|\.tmp)(\\|$)'
    $files = Get-ChildItem -Path $SourceRoot -Recurse -File -Force | Where-Object {
        $_.FullName -notmatch $excludePattern
    }

    foreach ($file in $files) {
        $relative = [System.IO.Path]::GetRelativePath($SourceRoot, $file.FullName)
        $targetPath = Join-Path $TargetRoot $relative
        Copy-FilePreservingKind -SourcePath $file.FullName -TargetPath $targetPath
    }
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

    Write-TextFileNoBom (
        Join-Path $TargetRoot "scripts\build_private_engine.ps1"
    ) @'
param(
    [string]$Configuration = "Debug",
    [string]$Platform = "x64"
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot "..")).Path
$solutionPath = Join-Path $repoRoot "IndigoMovieEngine.sln"

dotnet build $solutionPath -c $Configuration -p:Platform=$Platform
'@

    Write-TextFileNoBom (
        Join-Path $TargetRoot "scripts\publish_private_engine.ps1"
    ) @'
param(
    [string]$Configuration = "Release",
    [string]$Platform = "x64",
    [string]$Runtime = "win-x64"
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot "..")).Path
$workerProjectPath = Join-Path $repoRoot "src\IndigoMovieManager.Thumbnail.RescueWorker\IndigoMovieManager.Thumbnail.RescueWorker.csproj"

dotnet publish $workerProjectPath -c $Configuration -r $Runtime -p:Platform=$Platform --self-contained false
'@
}

function Sync-PrivateRepoSource {
    param(
        [string]$RepoRootValue,
        [string]$TargetRoot
    )

    $projectDirs = @(
        "src\IndigoMovieManager.Thumbnail.Contracts",
        "src\IndigoMovieManager.Thumbnail.Engine",
        "src\IndigoMovieManager.Thumbnail.FailureDb",
        "src\IndigoMovieManager.Thumbnail.RescueWorker"
    )

    foreach ($relative in $projectDirs) {
        $sourcePath = Join-Path $RepoRootValue $relative
        $targetPath = Join-Path $TargetRoot $relative
        Copy-TreeFiltered -SourceRoot $sourcePath -TargetRoot $targetPath
    }

    $assetFiles = @(
        @{ Source = "Images\noFileBig.jpg"; Target = "Images\noFileBig.jpg" },
        @{ Source = "Images\noFileGrid.jpg"; Target = "Images\noFileGrid.jpg" },
        @{ Source = "Images\nofileList.jpg"; Target = "Images\nofileList.jpg" },
        @{ Source = "Images\noFileSmall.jpg"; Target = "Images\noFileSmall.jpg" },
        @{ Source = "tools\ffmpeg\ffmpeg.exe"; Target = "tools\ffmpeg\ffmpeg.exe" },
        @{ Source = "tools\ffmpeg\LICENSE-ffmpeg-lgpl.txt"; Target = "tools\ffmpeg\LICENSE-ffmpeg-lgpl.txt" }
    )

    foreach ($item in $assetFiles) {
        $sourcePath = Join-Path $RepoRootValue $item.Source
        $targetPath = Join-Path $TargetRoot $item.Target
        Copy-FilePreservingKind -SourcePath $sourcePath -TargetPath $targetPath
    }

    $sharedToolSource = Join-Path $RepoRootValue "tools\ffmpeg-shared"
    $sharedToolTarget = Join-Path $TargetRoot "tools\ffmpeg-shared"
    if (Test-Path $sharedToolSource) {
        Copy-TreeFiltered -SourceRoot $sharedToolSource -TargetRoot $sharedToolTarget
    }

    Copy-TextFileNoBom (
        Join-Path $RepoRootValue "IndigoMovieManager.sln"
    ) (Join-Path $TargetRoot "IndigoMovieEngine.sln")
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
elseif ($Mode -eq "SyncDocs") {
    Ensure-Directory (Join-Path $PrivateRepoRoot "docs")
    Sync-PrivateRepoDocs -RepoRootValue $RepoRoot -TargetRoot $PrivateRepoRoot
}
else {
    Initialize-PrivateRepoLayout -TargetRoot $PrivateRepoRoot
    Sync-PrivateRepoDocs -RepoRootValue $RepoRoot -TargetRoot $PrivateRepoRoot
    Sync-PrivateRepoSource -RepoRootValue $RepoRoot -TargetRoot $PrivateRepoRoot
}

Write-Host "[bootstrap] completed mode=$Mode repo=$RepoRoot privateRepo=$PrivateRepoRoot"
