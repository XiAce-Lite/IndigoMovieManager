[CmdletBinding()]
param(
    [string]$ProjectPath = "IndigoMovieManager.csproj",
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

function Get-MsBuildPropertyValue {
    param(
        [Parameter(Mandatory = $true)]
        [string]$ProjectPath,
        [Parameter(Mandatory = $true)]
        [string]$PropertyName
    )

    # Directory.Build.props やコマンドライン上書きを含めた最終値を取得する。
    $output = & dotnet msbuild $ProjectPath -nologo "-getProperty:$PropertyName"
    if ($LASTEXITCODE -ne 0) {
        throw "MSBuild プロパティの取得に失敗しました: $PropertyName"
    }

    return ($output | Select-Object -Last 1).Trim()
}

function Get-RescueWorkerArtifactCompatibilityVersion {
    param(
        [Parameter(Mandatory = $true)]
        [string]$RepoRoot
    )

    $contractPath = Join-Path $RepoRoot "src\IndigoMovieManager.Thumbnail.Contracts\RescueWorkerArtifactContract.cs"
    if (-not (Test-Path -LiteralPath $contractPath)) {
        throw "worker artifact contract が見つかりません: $contractPath"
    }

    $content = Get-Content -LiteralPath $contractPath -Raw -Encoding utf8
    $match = [regex]::Match($content, 'CompatibilityVersion\s*=\s*"([^"]+)"')
    if (-not $match.Success) {
        throw "worker artifact の CompatibilityVersion を読み取れません: $contractPath"
    }

    return $match.Groups[1].Value
}

function Publish-RescueWorkerArtifactIntoPackage {
    param(
        [Parameter(Mandatory = $true)]
        [string]$RepoRoot,
        [Parameter(Mandatory = $true)]
        [string]$Configuration,
        [Parameter(Mandatory = $true)]
        [string]$Runtime,
        [Parameter(Mandatory = $true)]
        [string]$PackageDir,
        [Parameter(Mandatory = $true)]
        [string]$WorkerOutputRootRelativePath,
        [Parameter(Mandatory = $true)]
        [bool]$SelfContained
    )

    $publishScriptPath = Join-Path $RepoRoot "src\IndigoMovieManager.Thumbnail.RescueWorker\Publish-RescueWorkerArtifact.ps1"
    if (-not (Test-Path -LiteralPath $publishScriptPath)) {
        throw "worker publish script が見つかりません: $publishScriptPath"
    }

    $workerOutputRoot = Join-Path $RepoRoot $WorkerOutputRootRelativePath
    $workerPublishDir = Join-Path $workerOutputRoot "publish\$Configuration-$Runtime"
    $workerPackageDir = Join-Path $PackageDir "rescue-worker"

    if (Test-Path -LiteralPath $workerPublishDir) {
        Remove-Item -LiteralPath $workerPublishDir -Recurse -Force
    }
    if (Test-Path -LiteralPath $workerPackageDir) {
        Remove-Item -LiteralPath $workerPackageDir -Recurse -Force
    }

    & $publishScriptPath `
        -Configuration $Configuration `
        -Runtime $Runtime `
        -OutputRoot $WorkerOutputRootRelativePath `
        -SelfContained:$SelfContained
    if ($LASTEXITCODE -ne 0) {
        throw "worker publish script に失敗しました。"
    }

    if (-not (Test-Path -LiteralPath $workerPublishDir)) {
        throw "worker publish directory が見つかりません: $workerPublishDir"
    }

    New-Item -ItemType Directory -Path $workerPackageDir -Force | Out-Null
    Copy-Item -Path (Join-Path $workerPublishDir "*") -Destination $workerPackageDir -Recurse -Force
}

function Convert-RescueWorkerLockToSummaryText {
    param(
        [Parameter(Mandatory = $true)]
        [hashtable]$WorkerLock,
        [Parameter(Mandatory = $true)]
        [string]$BundledRescueWorkerRelativePath
    )

    $workerArtifact = $WorkerLock["workerArtifact"]
    if ($null -eq $workerArtifact) {
        throw "worker lock summary 用の workerArtifact がありません。"
    }

    return @"
Rescue Worker Lock Summary
==========================

- source: $($workerArtifact["sourceType"])
- version: $($workerArtifact["version"])
- asset: $($workerArtifact["assetFileName"])
- compatibilityVersion: $($workerArtifact["compatibilityVersion"])
- workerExecutableSha256: $($workerArtifact["workerExecutableSha256"])
- bundledWorker: $BundledRescueWorkerRelativePath
"@
}

function Convert-RescueWorkerLockToReleaseSummaryMarkdown {
    param(
        [Parameter(Mandatory = $true)]
        [hashtable]$WorkerLock,
        [Parameter(Mandatory = $true)]
        [string]$OutputRootFullPath,
        [Parameter(Mandatory = $true)]
        [string]$PackageDir,
        [Parameter(Mandatory = $true)]
        [string]$LockFilePath
    )

    $workerArtifact = $WorkerLock["workerArtifact"]
    if ($null -eq $workerArtifact) {
        throw "release summary 用の workerArtifact がありません。"
    }

    $packageRelativePath = [System.IO.Path]::GetRelativePath($OutputRootFullPath, $PackageDir).Replace("\", "/")
    $lockFileRelativePath = [System.IO.Path]::GetRelativePath($OutputRootFullPath, $LockFilePath).Replace("\", "/")
    $releaseBodySnippet = @"
### Bundled Rescue Worker

- Source: ``$($workerArtifact["sourceType"])``
- Version: ``$($workerArtifact["version"])``
- Artifact: ``$($workerArtifact["assetFileName"])``
- CompatibilityVersion: ``$($workerArtifact["compatibilityVersion"])``
- WorkerExe SHA256: ``$($workerArtifact["workerExecutableSha256"])``
"@

    return @"
# Rescue Worker Lock Summary

このファイルは、GitHub Release 本文へ bundled rescue worker の pin 情報を転記するための summary である。

## GitHub Release 本文へ貼るブロック

~~~md
$releaseBodySnippet
~~~

## ローカル確認用

- Package: ``$packageRelativePath``
- LockFile: ``$lockFileRelativePath``
"@
}

$repoRoot = Split-Path -Parent $PSScriptRoot
$projectFullPath = [System.IO.Path]::GetFullPath((Join-Path $repoRoot $ProjectPath))
$outputRootFullPath = [System.IO.Path]::GetFullPath((Join-Path $repoRoot $OutputRoot))
$versionLabelNormalized = Get-NormalizedLabel -Label $VersionLabel
$rescueWorkerCompatibilityVersion = Get-RescueWorkerArtifactCompatibilityVersion -RepoRoot $repoRoot
$rescueWorkerCompatibilityLabel = Get-NormalizedLabel -Label $rescueWorkerCompatibilityVersion
$expectedRescueWorkerAssetFileName =
    "IndigoMovieManager.Thumbnail.RescueWorker-$versionLabelNormalized-$Runtime-compat-$rescueWorkerCompatibilityLabel.zip"

if (-not (Test-Path -LiteralPath $projectFullPath)) {
    throw "プロジェクトが見つかりません: $projectFullPath"
}

$assemblyName = Get-MsBuildPropertyValue -ProjectPath $projectFullPath -PropertyName "AssemblyName"
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

# rescue worker は完成済み artifact を rescue-worker 配下へ同梱し、利用者が別 asset を探さなくて済む形にする。
Publish-RescueWorkerArtifactIntoPackage `
    -RepoRoot $repoRoot `
    -Configuration $Configuration `
    -Runtime $Runtime `
    -PackageDir $packageDir `
    -WorkerOutputRootRelativePath (Join-Path $OutputRoot "rescue-worker") `
    -SelfContained $SelfContained.IsPresent

# 配布 ZIP にはデバッグ用 pdb を含めず、利用者向けの同梱物だけへ絞る。
Get-ChildItem -Path $packageDir -Filter *.pdb -Recurse -File | Remove-Item -Force

$bundledRescueWorkerRelativePath = "rescue-worker\IndigoMovieManager.Thumbnail.RescueWorker.exe"
$bundledRescueWorkerPath = Join-Path $packageDir $bundledRescueWorkerRelativePath
if (-not (Test-Path -LiteralPath $bundledRescueWorkerPath)) {
    throw "同梱 rescue worker exe が見つかりません: $bundledRescueWorkerPath"
}

$bundledRescueWorkerHash = (Get-FileHash -LiteralPath $bundledRescueWorkerPath -Algorithm SHA256).Hash.ToUpperInvariant()
$rescueWorkerLock = [ordered]@{
    schemaVersion = 1
    workerArtifact = [ordered]@{
        artifactType = "IndigoMovieManager.Thumbnail.RescueWorker"
        sourceType = "bundled-app-package"
        version = $versionLabelNormalized
        assetFileName = $expectedRescueWorkerAssetFileName
        compatibilityVersion = $rescueWorkerCompatibilityVersion
        workerExecutableSha256 = $bundledRescueWorkerHash
    }
}
$rescueWorkerLockSummary = Convert-RescueWorkerLockToSummaryText `
    -WorkerLock $rescueWorkerLock `
    -BundledRescueWorkerRelativePath $bundledRescueWorkerRelativePath

$packageReadme = @"
IndigoMovieManager 配布パッケージ
===============================

- アセンブリ名: $assemblyName
- バージョンラベル: $versionLabelNormalized
- 構成: $Configuration
- ランタイム: $Runtime
- SelfContained: $($SelfContained.IsPresent)
- 同梱 rescue worker: rescue-worker\$([System.IO.Path]::GetFileName('IndigoMovieManager.Thumbnail.RescueWorker.exe'))
- rescue worker compatibilityVersion: $rescueWorkerCompatibilityVersion

$rescueWorkerLockSummary

使い方
------
1. この ZIP を展開する
2. 展開先の $assemblyName.exe を起動する

注意
----
- SelfContained が False の場合は、.NET 8 Desktop Runtime が必要です
- 同梱 DLL や画像を使うため、exe 単体ではなく展開したフォルダごと扱ってください
- rescue worker は rescue-worker フォルダへ同梱済みです
- rescue-worker.lock.json に、同梱 worker の pin 情報を持たせています
- rescue-worker-lock-summary.txt に、同梱 worker の pin 情報要約を書き出しています
- rescue worker を差し替える時は、rescue-worker-expected.json の compatibilityVersion と一致するものを使ってください
"@
New-Utf8NoBomFile -Path (Join-Path $packageDir "README-package.txt") -Content $packageReadme

$rescueWorkerExpected = [ordered]@{
    artifactType = "IndigoMovieManager.AppPackage"
    versionLabel = $versionLabelNormalized
    runtime = $Runtime
    bundledRescueWorkerRelativePath = $bundledRescueWorkerRelativePath
    expectedRescueWorkerCompatibilityVersion = $rescueWorkerCompatibilityVersion
}
New-Utf8NoBomFile `
    -Path (Join-Path $packageDir "rescue-worker-expected.json") `
    -Content ($rescueWorkerExpected | ConvertTo-Json -Depth 4)
New-Utf8NoBomFile `
    -Path (Join-Path $packageDir "rescue-worker.lock.json") `
    -Content ($rescueWorkerLock | ConvertTo-Json -Depth 4)
New-Utf8NoBomFile `
    -Path (Join-Path $packageDir "rescue-worker-lock-summary.txt") `
    -Content $rescueWorkerLockSummary

$verifyWorkerLockScriptPath = Join-Path $repoRoot "scripts\verify_app_package_worker_lock.ps1"
if (-not (Test-Path -LiteralPath $verifyWorkerLockScriptPath)) {
    throw "worker lock verify script が見つかりません: $verifyWorkerLockScriptPath"
}

& $verifyWorkerLockScriptPath -PackageDir $packageDir
if ($LASTEXITCODE -ne 0) {
    throw "worker lock verification に失敗しました。"
}

$mainExePath = Join-Path $packageDir "$assemblyName.exe"
if (-not (Test-Path -LiteralPath $mainExePath)) {
    throw "配布用 exe が見つかりません: $mainExePath"
}

$hash = Get-FileHash -LiteralPath $mainExePath -Algorithm SHA256
$hashContent = "$($hash.Algorithm)  $($hash.Hash)  $($hash.Path | Split-Path -Leaf)"
New-Utf8NoBomFile -Path (Join-Path $packageDir "SHA256SUMS.txt") -Content $hashContent

Compress-Archive -Path (Join-Path $packageDir "*") -DestinationPath $zipFilePath -CompressionLevel Optimal -Force

$rescueWorkerLockFilePath = Join-Path $packageDir "rescue-worker.lock.json"
$rescueWorkerReleaseSummaryMarkdown = Convert-RescueWorkerLockToReleaseSummaryMarkdown `
    -WorkerLock $rescueWorkerLock `
    -OutputRootFullPath $outputRootFullPath `
    -PackageDir $packageDir `
    -LockFilePath $rescueWorkerLockFilePath
New-Utf8NoBomFile `
    -Path (Join-Path $outputRootFullPath "release-worker-lock-summary-$versionLabelNormalized-$Runtime.md") `
    -Content $rescueWorkerReleaseSummaryMarkdown

Write-Host "Publish directory: $publishDir"
Write-Host "Package directory: $packageDir"
Write-Host "Zip file: $zipFilePath"
