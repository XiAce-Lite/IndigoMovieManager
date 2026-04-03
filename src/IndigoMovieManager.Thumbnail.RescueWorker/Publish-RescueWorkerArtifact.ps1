[CmdletBinding()]
param(
    [string]$Configuration = "Release",
    [string]$Runtime = "win-x64",
    [string]$OutputRoot = "artifacts/rescue-worker",
    [switch]$SelfContained,
    [string]$PublishDirOverride = "",
    [switch]$MarkerOnly
)

$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest

function Write-Utf8NoBomFile {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Path,
        [Parameter(Mandatory = $true)]
        [string]$Content
    )

    $utf8NoBom = [System.Text.UTF8Encoding]::new($false)
    [System.IO.File]::WriteAllText($Path, $Content, $utf8NoBom)
}

function Get-ArtifactCompatibilityVersion {
    param(
        [Parameter(Mandatory = $true)]
        [string]$SourcePath
    )

    if (-not (Test-Path -LiteralPath $SourcePath)) {
        throw "artifact contract source が見つかりません: $SourcePath"
    }

    $content = Get-Content -LiteralPath $SourcePath -Raw -Encoding utf8
    $match = [regex]::Match($content, 'CompatibilityVersion\s*=\s*"([^"]+)"')
    if (-not $match.Success) {
        throw "CompatibilityVersion を読み取れませんでした: $SourcePath"
    }

    return $match.Groups[1].Value
}

function Copy-TreeIfExists {
    param(
        [Parameter(Mandatory = $true)]
        [string]$SourcePath,
        [Parameter(Mandatory = $true)]
        [string]$DestinationPath
    )

    if (-not (Test-Path -LiteralPath $SourcePath)) {
        return
    }

    New-Item -ItemType Directory -Path $DestinationPath -Force | Out-Null
    Copy-Item -Path (Join-Path $SourcePath "*") -Destination $DestinationPath -Recurse -Force
}

function Copy-FileIfExists {
    param(
        [Parameter(Mandatory = $true)]
        [string]$SourcePath,
        [Parameter(Mandatory = $true)]
        [string]$DestinationPath
    )

    if (-not (Test-Path -LiteralPath $SourcePath)) {
        return
    }

    $parentDirectory = Split-Path -Parent $DestinationPath
    if (-not [string]::IsNullOrWhiteSpace($parentDirectory)) {
        New-Item -ItemType Directory -Path $parentDirectory -Force | Out-Null
    }

    Copy-Item -LiteralPath $SourcePath -Destination $DestinationPath -Force
}

function Resolve-BuildOutputDirectory {
    param(
        [Parameter(Mandatory = $true)]
        [string]$ScriptRoot,
        [Parameter(Mandatory = $true)]
        [string]$Configuration,
        [Parameter(Mandatory = $true)]
        [string]$Runtime
    )

    $buildOutputCandidates = @(
        (Join-Path $ScriptRoot "bin\x64\$Configuration\net8.0-windows\$Runtime"),
        (Join-Path $ScriptRoot "bin\x64\$Configuration\net8.0-windows"),
        (Join-Path $ScriptRoot "bin\$Configuration\net8.0-windows\$Runtime"),
        (Join-Path $ScriptRoot "bin\$Configuration\net8.0-windows")
    )

    return $buildOutputCandidates |
        Where-Object { Test-Path -LiteralPath $_ } |
        Select-Object -First 1
}

function Write-ArtifactMarker {
    param(
        [Parameter(Mandatory = $true)]
        [string]$ArtifactDirectory,
        [Parameter(Mandatory = $true)]
        [string]$CompatibilityVersion,
        [Parameter(Mandatory = $true)]
        [string]$Configuration,
        [Parameter(Mandatory = $true)]
        [string]$Runtime,
        [Parameter(Mandatory = $true)]
        [bool]$SelfContained
    )

    New-Item -ItemType Directory -Path $ArtifactDirectory -Force | Out-Null

    $artifactMetadata = [ordered]@{
        artifactType = "IndigoMovieManager.Thumbnail.RescueWorker"
        markerVersion = 1
        compatibilityVersion = $CompatibilityVersion
        supportedEntryModes = @(
            "legacy-main-cli",
            "rescue-job-json",
            "attempt-child",
            "direct-index-repair"
        )
        createdAt = (Get-Date).ToString("o")
        configuration = $Configuration
        runtime = $Runtime
        selfContained = $SelfContained
        overlayRequired = $false
        workerExecutable = "IndigoMovieManager.Thumbnail.RescueWorker.exe"
    }

    $artifactMarkerPath = Join-Path $ArtifactDirectory "rescue-worker-artifact.json"
    Write-Utf8NoBomFile `
        -Path $artifactMarkerPath `
        -Content ($artifactMetadata | ConvertTo-Json -Depth 4)

    return $artifactMarkerPath
}

$scriptRoot = Split-Path -Parent $PSCommandPath
$repoRoot = [System.IO.Path]::GetFullPath((Join-Path $scriptRoot "..\.."))
$projectPath = Join-Path $scriptRoot "IndigoMovieManager.Thumbnail.RescueWorker.csproj"
$outputRootFullPath = [System.IO.Path]::GetFullPath((Join-Path $repoRoot $OutputRoot))
$publishDir = if ([string]::IsNullOrWhiteSpace($PublishDirOverride)) {
    Join-Path $outputRootFullPath "publish\$Configuration-$Runtime"
} else {
    [System.IO.Path]::GetFullPath($PublishDirOverride)
}
$artifactMarkerPath = Join-Path $publishDir "rescue-worker-artifact.json"
$artifactContractSourcePath = Join-Path $repoRoot "src\IndigoMovieManager.Thumbnail.Contracts\RescueWorkerArtifactContract.cs"
$compatibilityVersion = Get-ArtifactCompatibilityVersion -SourcePath $artifactContractSourcePath

if (-not (Test-Path -LiteralPath $projectPath)) {
    throw "worker project が見つかりません: $projectPath"
}

if ($MarkerOnly) {
    $artifactMarkerPath = Write-ArtifactMarker `
        -ArtifactDirectory $publishDir `
        -CompatibilityVersion $compatibilityVersion `
        -Configuration $Configuration `
        -Runtime $Runtime `
        -SelfContained $SelfContained.IsPresent
    Write-Host "Artifact marker: $artifactMarkerPath"
    return
}

# 毎回同じ完成形を検証できるよう、前回成果物は消してから publish する。
if (Test-Path -LiteralPath $publishDir) {
    Remove-Item -LiteralPath $publishDir -Recurse -Force
}

New-Item -ItemType Directory -Path $publishDir -Force | Out-Null

$publishArguments = @(
    "publish"
    $projectPath
    "-c", $Configuration
    "-r", $Runtime
    "-p:Platform=x64"
    "--self-contained", $SelfContained.IsPresent.ToString().ToLowerInvariant()
    "-o", $publishDir
)

Write-Host "dotnet $($publishArguments -join ' ')"
& dotnet @publishArguments
if ($LASTEXITCODE -ne 0) {
    throw "dotnet publish に失敗しました。"
}

# clean runner では build 出力が publish 実行後に初めて作られるため、補完元はここで確定する。
$buildOutputDir = Resolve-BuildOutputDirectory `
    -ScriptRoot $scriptRoot `
    -Configuration $Configuration `
    -Runtime $Runtime
$buildRuntimeDir = if ([string]::IsNullOrWhiteSpace($buildOutputDir)) {
    ""
} else {
    Join-Path $buildOutputDir "runtimes\$Runtime"
}

# worker が placeholder を自前で解決できるよう、必要画像だけ同梱する。
$imageFileNames = @(
    "noFileBig.jpg",
    "noFileGrid.jpg",
    "nofileList.jpg",
    "noFileSmall.jpg"
)
for ($i = 0; $i -lt $imageFileNames.Count; $i++) {
    $fileName = $imageFileNames[$i]
    Copy-FileIfExists `
        -SourcePath (Join-Path $repoRoot "Images\$fileName") `
        -DestinationPath (Join-Path $publishDir "Images\$fileName")
}

# FFmpeg shared DLL と LGPL 表記は publish 成果物の中で完結させる。
Copy-TreeIfExists `
    -SourcePath (Join-Path $repoRoot "tools\ffmpeg-shared") `
    -DestinationPath (Join-Path $publishDir "tools\ffmpeg-shared")
Copy-FileIfExists `
    -SourcePath (Join-Path $repoRoot "tools\ffmpeg\ffmpeg.exe") `
    -DestinationPath (Join-Path $publishDir "tools\ffmpeg\ffmpeg.exe")
Copy-FileIfExists `
    -SourcePath (Join-Path $repoRoot "tools\ffmpeg\LICENSE-ffmpeg-lgpl.txt") `
    -DestinationPath (Join-Path $publishDir "tools\ffmpeg\LICENSE-ffmpeg-lgpl.txt")

# publish 形が SDK や package 構成で揺れる native DLL は、build 出力にある時だけ補完する。
Copy-TreeIfExists `
    -SourcePath $buildRuntimeDir `
    -DestinationPath (Join-Path $publishDir "runtimes\$Runtime")

# publish 成果物から落ちることがある SQLite 関連DLLは build 出力から補完して完成形を固定する。
$supplementalFileNames = @(
    "SQLitePCLRaw.batteries_v2.dll",
    "SQLitePCLRaw.core.dll",
    "SQLitePCLRaw.provider.e_sqlite3.dll",
    "System.Data.SQLite.dll"
)
for ($i = 0; $i -lt $supplementalFileNames.Count; $i++) {
    $fileName = $supplementalFileNames[$i]
    if (-not [string]::IsNullOrWhiteSpace($buildOutputDir)) {
        Copy-FileIfExists `
            -SourcePath (Join-Path $buildOutputDir $fileName) `
            -DestinationPath (Join-Path $publishDir $fileName)
    }
}

$requiredPaths = @(
    (Join-Path $publishDir "IndigoMovieManager.Thumbnail.RescueWorker.exe"),
    (Join-Path $publishDir "Images\noFileSmall.jpg"),
    (Join-Path $publishDir "tools\ffmpeg-shared"),
    (Join-Path $publishDir "SQLitePCLRaw.batteries_v2.dll"),
    (Join-Path $publishDir "SQLitePCLRaw.core.dll"),
    (Join-Path $publishDir "SQLitePCLRaw.provider.e_sqlite3.dll"),
    (Join-Path $publishDir "System.Data.SQLite.dll")
)

for ($i = 0; $i -lt $requiredPaths.Count; $i++) {
    $requiredPath = $requiredPaths[$i]
    if (-not (Test-Path -LiteralPath $requiredPath)) {
        throw "publish artifact が不完全です: $requiredPath"
    }
}

$nativeSqliteCandidates = @(
    (Join-Path $publishDir "e_sqlite3.dll"),
    (Join-Path $publishDir "runtimes\$Runtime\native\e_sqlite3.dll")
)
if (-not ($nativeSqliteCandidates | Where-Object { Test-Path -LiteralPath $_ } | Select-Object -First 1)) {
    throw "publish artifact が不完全です: e_sqlite3.dll"
}

$artifactMarkerPath = Write-ArtifactMarker `
    -ArtifactDirectory $publishDir `
    -CompatibilityVersion $compatibilityVersion `
    -Configuration $Configuration `
    -Runtime $Runtime `
    -SelfContained $SelfContained.IsPresent

Write-Host "Rescue worker artifact directory: $publishDir"
Write-Host "Artifact marker: $artifactMarkerPath"
