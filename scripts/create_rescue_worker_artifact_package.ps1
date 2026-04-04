[CmdletBinding()]
param(
    [string]$Configuration = "Release",
    [string]$Runtime = "win-x64",
    [string]$OutputRoot = "artifacts/rescue-worker",
    [string]$VersionLabel = "",
    [string]$PreparedWorkerPublishDir = "",
    [switch]$AllowLocalWorkerSourceBuild,
    [switch]$SelfContained
)

$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest

function Get-NormalizedLabel {
    param(
        [string]$Label
    )

    if ([string]::IsNullOrWhiteSpace($Label)) {
        return (Get-Date -Format "yyyyMMdd-HHmmss")
    }

    return ($Label -replace '[\\/:*?"<>|]', '-').Trim()
}

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

function Test-AllowLocalWorkerSourceBuild {
    param(
        [Parameter(Mandatory = $true)]
        [bool]$AllowLocalWorkerSourceBuild
    )

    if ($AllowLocalWorkerSourceBuild) {
        return $true
    }

    $rawValue = [Environment]::GetEnvironmentVariable("IMM_ALLOW_LOCAL_WORKER_SOURCE_BUILD")
    if ([string]::IsNullOrWhiteSpace($rawValue)) {
        return $false
    }

    $normalized = $rawValue.Trim()
    return
        $normalized -ieq "1" -or
        $normalized -ieq "true" -or
        $normalized -ieq "yes" -or
        $normalized -ieq "on"
}

$repoRoot = Split-Path -Parent $PSScriptRoot
$outputRootFullPath = [System.IO.Path]::GetFullPath((Join-Path $repoRoot $OutputRoot))
$versionLabelNormalized = Get-NormalizedLabel -Label $VersionLabel
$publishScriptPath = Join-Path $repoRoot "src\IndigoMovieManager.Thumbnail.RescueWorker\Publish-RescueWorkerArtifact.ps1"
$allowLocalWorkerSourceBuildEffective = Test-AllowLocalWorkerSourceBuild -AllowLocalWorkerSourceBuild $AllowLocalWorkerSourceBuild.IsPresent

if (-not (Test-Path -LiteralPath $publishScriptPath) -and [string]::IsNullOrWhiteSpace($PreparedWorkerPublishDir)) {
    throw "publish script が見つかりません: $publishScriptPath"
}

$publishDir = Join-Path $outputRootFullPath "publish\$Configuration-$Runtime"
$packageRoot = Join-Path $outputRootFullPath "package"
$sourcePublishDir = ""
if (-not [string]::IsNullOrWhiteSpace($PreparedWorkerPublishDir)) {
    $sourcePublishDir = [System.IO.Path]::GetFullPath($PreparedWorkerPublishDir, $repoRoot)
    if (-not (Test-Path -LiteralPath $sourcePublishDir)) {
        throw "prepared worker publish directory が見つかりません: $sourcePublishDir"
    }
}
else {
    if (-not $allowLocalWorkerSourceBuildEffective) {
        throw "prepared worker publish directory が未指定です。既定では local worker source build を行いません。scripts/sync_private_engine_worker_artifact.ps1 で artifact を同期するか、-AllowLocalWorkerSourceBuild または IMM_ALLOW_LOCAL_WORKER_SOURCE_BUILD=1 で明示 opt-in してください。"
    }

    & $publishScriptPath `
        -Configuration $Configuration `
        -Runtime $Runtime `
        -OutputRoot $OutputRoot `
        -SelfContained:$SelfContained.IsPresent

    if ($LASTEXITCODE -ne 0) {
        throw "worker publish script に失敗しました。"
    }

    if (-not (Test-Path -LiteralPath $publishDir)) {
        throw "publish directory が見つかりません: $publishDir"
    }

    $sourcePublishDir = $publishDir
}

$markerPath = Join-Path $sourcePublishDir "rescue-worker-artifact.json"
if (-not (Test-Path -LiteralPath $markerPath)) {
    throw "artifact marker が見つかりません: $markerPath"
}

$marker = Get-Content -LiteralPath $markerPath -Raw -Encoding utf8 | ConvertFrom-Json
$compatibilityVersion = "$($marker.compatibilityVersion)"
$compatibilityLabel = Get-NormalizedLabel -Label $compatibilityVersion
$packageIdentity =
    "IndigoMovieManager.Thumbnail.RescueWorker-$versionLabelNormalized-$Runtime-compat-$compatibilityLabel"
$packageDir = Join-Path $packageRoot $packageIdentity
$zipFilePath = Join-Path $outputRootFullPath "$packageIdentity.zip"

Get-ChildItem -Path $packageRoot -Directory -Filter "IndigoMovieManager.Thumbnail.RescueWorker-$versionLabelNormalized-$Runtime-compat-*" -ErrorAction SilentlyContinue |
    Remove-Item -Recurse -Force
Get-ChildItem -Path $outputRootFullPath -File -Filter "IndigoMovieManager.Thumbnail.RescueWorker-$versionLabelNormalized-$Runtime-compat-*.zip" -ErrorAction SilentlyContinue |
    Remove-Item -Force
Get-ChildItem -Path $packageRoot -Directory -Filter "IndigoMovieManager.Thumbnail.RescueWorker-$versionLabelNormalized-$Runtime" -ErrorAction SilentlyContinue |
    Remove-Item -Recurse -Force
Get-ChildItem -Path $outputRootFullPath -File -Filter "IndigoMovieManager.Thumbnail.RescueWorker-$versionLabelNormalized-$Runtime.zip" -ErrorAction SilentlyContinue |
    Remove-Item -Force

if (Test-Path -LiteralPath $packageDir) {
    Remove-Item -LiteralPath $packageDir -Recurse -Force
}
if (Test-Path -LiteralPath $zipFilePath) {
    Remove-Item -LiteralPath $zipFilePath -Force
}

New-Item -ItemType Directory -Path $packageDir -Force | Out-Null
Copy-Item -Path (Join-Path $sourcePublishDir "*") -Destination $packageDir -Recurse -Force

$readme = @"
IndigoMovieManager Rescue Worker Artifact
========================================

- VersionLabel: $versionLabelNormalized
- Configuration: $Configuration
- Runtime: $Runtime
- SelfContained: $($SelfContained.IsPresent)
- CompatibilityVersion: $compatibilityVersion
- AssetFileName: $packageIdentity.zip
- Source: $(if (-not [string]::IsNullOrWhiteSpace($PreparedWorkerPublishDir)) { "prepared-worker-publish-dir" } else { "local-worker-source-build" })

使い方
------
1. ZIP を展開する
2. 展開先の IndigoMovieManager.Thumbnail.RescueWorker.exe を launcher の入力元として使う
3. marker の compatibilityVersion が host 側期待値と一致することを確認する

注意
----
- この artifact は overlay 不要前提で同梱物を揃えている
- SelfContained が False の場合は .NET 8 Desktop Runtime が必要
"@
Write-Utf8NoBomFile -Path (Join-Path $packageDir "README-artifact.txt") -Content $readme

$mainExePath = Join-Path $packageDir "IndigoMovieManager.Thumbnail.RescueWorker.exe"
if (-not (Test-Path -LiteralPath $mainExePath)) {
    throw "worker exe が見つかりません: $mainExePath"
}

$hash = Get-FileHash -LiteralPath $mainExePath -Algorithm SHA256
$hashContent = "$($hash.Algorithm)  $($hash.Hash)  $($hash.Path | Split-Path -Leaf)"
Write-Utf8NoBomFile -Path (Join-Path $packageDir "SHA256SUMS.txt") -Content $hashContent

Compress-Archive -Path (Join-Path $packageDir "*") -DestinationPath $zipFilePath -CompressionLevel Optimal -Force

Write-Host "Publish directory: $sourcePublishDir"
Write-Host "Package directory: $packageDir"
Write-Host "Zip file: $zipFilePath"
