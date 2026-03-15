[CmdletBinding()]
param(
    [string]$ProjectPath = "IndigoMovieManager_fork.csproj",
    [string]$Configuration = "Release",
    [string]$Runtime = "win-x64",
    [string]$OutputRoot = "artifacts/github-release",
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

function New-Utf8NoBomFile {
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
$projectFullPath = [System.IO.Path]::GetFullPath((Join-Path $repoRoot $ProjectPath))
$outputRootFullPath = [System.IO.Path]::GetFullPath((Join-Path $repoRoot $OutputRoot))
$versionLabelNormalized = Get-NormalizedLabel -Label $VersionLabel

if (-not (Test-Path -LiteralPath $projectFullPath)) {
    throw "プロジェクトが見つかりません: $projectFullPath"
}

[xml]$projectXml = Get-Content -LiteralPath $projectFullPath -Raw -Encoding utf8
$assemblyName = $projectXml.SelectNodes("//PropertyGroup/AssemblyName") |
    ForEach-Object { $_.InnerText } |
    Where-Object { -not [string]::IsNullOrWhiteSpace($_) } |
    Select-Object -First 1
if ([string]::IsNullOrWhiteSpace($assemblyName)) {
    $assemblyName = [System.IO.Path]::GetFileNameWithoutExtension($projectFullPath)
}

$publishRoot = Join-Path $outputRootFullPath "publish"
$packageRoot = Join-Path $outputRootFullPath "package"
$zipFileName = "$assemblyName-$versionLabelNormalized-$Runtime.zip"
$zipFilePath = Join-Path $outputRootFullPath $zipFileName
$publishDir = Join-Path $publishRoot "$versionLabelNormalized-$Runtime"
$packageDir = Join-Path $packageRoot "$assemblyName-$versionLabelNormalized-$Runtime"

# 毎回同じ形の配布物を作るため、前回の残骸を消してから publish する。
if (Test-Path -LiteralPath $publishDir) {
    Remove-Item -LiteralPath $publishDir -Recurse -Force
}
if (Test-Path -LiteralPath $packageDir) {
    Remove-Item -LiteralPath $packageDir -Recurse -Force
}
if (Test-Path -LiteralPath $zipFilePath) {
    Remove-Item -LiteralPath $zipFilePath -Force
}

New-Item -ItemType Directory -Path $publishDir -Force | Out-Null
New-Item -ItemType Directory -Path $packageDir -Force | Out-Null

$publishArguments = @(
    "publish"
    $projectFullPath
    "-c", $Configuration
    "-r", $Runtime
    "-p:Platform=x64"
    "--self-contained", $SelfContained.IsPresent.ToString().ToLowerInvariant()
    "-o", $publishDir
)

# GitHub Actions でもローカルでも同じ publish 条件に揃える。
Write-Host "dotnet $($publishArguments -join ' ')"
& dotnet @publishArguments
if ($LASTEXITCODE -ne 0) {
    throw "dotnet publish に失敗しました。"
}

# publish 出力をそのまま配布フォルダへ移し、依存 DLL 取りこぼしを防ぐ。
Copy-Item -Path (Join-Path $publishDir "*") -Destination $packageDir -Recurse -Force

$packageReadme = @"
IndigoMovieManager 配布パッケージ
===============================

- アセンブリ名: $assemblyName
- バージョンラベル: $versionLabelNormalized
- 構成: $Configuration
- ランタイム: $Runtime
- SelfContained: $($SelfContained.IsPresent)

使い方
------
1. この ZIP を展開する
2. 展開先の $assemblyName.exe を起動する

注意
----
- SelfContained が False の場合は、.NET 8 Desktop Runtime が必要です
- 同梱 DLL や画像を使うため、exe 単体ではなく展開したフォルダごと扱ってください
"@
New-Utf8NoBomFile -Path (Join-Path $packageDir "README-package.txt") -Content $packageReadme

$mainExePath = Join-Path $packageDir "$assemblyName.exe"
if (-not (Test-Path -LiteralPath $mainExePath)) {
    throw "配布用 exe が見つかりません: $mainExePath"
}

$hash = Get-FileHash -LiteralPath $mainExePath -Algorithm SHA256
$hashContent = "$($hash.Algorithm)  $($hash.Hash)  $($hash.Path | Split-Path -Leaf)"
New-Utf8NoBomFile -Path (Join-Path $packageDir "SHA256SUMS.txt") -Content $hashContent

Compress-Archive -Path (Join-Path $packageDir "*") -DestinationPath $zipFilePath -CompressionLevel Optimal -Force

Write-Host "Publish directory: $publishDir"
Write-Host "Package directory: $packageDir"
Write-Host "Zip file: $zipFilePath"
