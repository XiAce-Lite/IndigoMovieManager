[CmdletBinding()]
param(
    [string]$Configuration = "Release",
    [string]$Runtime = "win-x64",
    [string]$OutputRoot = "artifacts/rescue-worker",
    [string]$VersionLabel = "",
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

$repoRoot = Split-Path -Parent $PSScriptRoot
$outputRootFullPath = [System.IO.Path]::GetFullPath((Join-Path $repoRoot $OutputRoot))
$versionLabelNormalized = Get-NormalizedLabel -Label $VersionLabel
$publishScriptPath = Join-Path $repoRoot "src\IndigoMovieManager.Thumbnail.RescueWorker\Publish-RescueWorkerArtifact.ps1"

if (-not (Test-Path -LiteralPath $publishScriptPath)) {
    throw "publish script が見つかりません: $publishScriptPath"
}

$publishDir = Join-Path $outputRootFullPath "publish\$Configuration-$Runtime"
$packageRoot = Join-Path $outputRootFullPath "package"

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

$markerPath = Join-Path $publishDir "rescue-worker-artifact.json"
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

# 毎回同じ名前の package を作るため、同一 label の残骸は先に掃除する。
Get-ChildItem -Path $packageRoot -Directory -Filter "IndigoMovieManager.Thumbnail.RescueWorker-$versionLabelNormalized-$Runtime-compat-*" -ErrorAction SilentlyContinue |
    Remove-Item -Recurse -Force
Get-ChildItem -Path $outputRootFullPath -File -Filter "IndigoMovieManager.Thumbnail.RescueWorker-$versionLabelNormalized-$Runtime-compat-*.zip" -ErrorAction SilentlyContinue |
    Remove-Item -Force

if (Test-Path -LiteralPath $packageDir) {
    Remove-Item -LiteralPath $packageDir -Recurse -Force
}
if (Test-Path -LiteralPath $zipFilePath) {
    Remove-Item -LiteralPath $zipFilePath -Force
}

New-Item -ItemType Directory -Path $packageDir -Force | Out-Null
Copy-Item -Path (Join-Path $publishDir "*") -Destination $packageDir -Recurse -Force

$readme = @"
IndigoMovieEngine Rescue Worker Artifact
=======================================

- VersionLabel: $versionLabelNormalized
- Configuration: $Configuration
- Runtime: $Runtime
- SelfContained: $($SelfContained.IsPresent)
- CompatibilityVersion: $compatibilityVersion
- AssetFileName: $packageIdentity.zip

使い方
------
1. ZIP を展開する
2. 展開先の IndigoMovieManager.Thumbnail.RescueWorker.exe を起動元として使う
3. rescue-worker-artifact.json の compatibilityVersion が host 側期待値と一致することを確認する

注意
----
- SelfContained が False の場合は .NET 8 Desktop Runtime が必要
- GitHub Release asset と Actions artifact で同じ中身を追えるよう、marker 付き publish 出力をそのまま封入している
"@
Write-Utf8NoBomFile -Path (Join-Path $packageDir "README-artifact.txt") -Content $readme

$hashEntries = Get-ChildItem -LiteralPath $packageDir -File -Recurse |
    Where-Object { $_.Name -ne "SHA256SUMS.txt" } |
    Sort-Object FullName |
    ForEach-Object {
        $relativePath = [System.IO.Path]::GetRelativePath($packageDir, $_.FullName) -replace "\\", "/"
        $hash = Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256
        "$($hash.Hash)  $relativePath"
    }
Write-Utf8NoBomFile -Path (Join-Path $packageDir "SHA256SUMS.txt") -Content ($hashEntries -join [Environment]::NewLine)

Compress-Archive -Path (Join-Path $packageDir "*") -DestinationPath $zipFilePath -CompressionLevel Optimal -Force

Write-Host "Publish directory: $publishDir"
Write-Host "Package directory: $packageDir"
Write-Host "Zip file: $zipFilePath"
