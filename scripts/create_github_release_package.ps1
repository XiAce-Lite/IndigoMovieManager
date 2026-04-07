[CmdletBinding()]
param(
    [string]$ProjectPath = "IndigoMovieManager.csproj",
    [string]$Configuration = "Release",
    [string]$Runtime = "win-x64",
    [string]$OutputRoot = "artifacts/github-release",
    [string]$VersionLabel = "",
    [string]$PreparedWorkerPublishDir = "artifacts/rescue-worker/publish/Release-win-x64",
    [string]$PreparedPrivateEnginePackageDir = "",
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

function Get-DefaultRescueWorkerAssetFileName {
    param(
        [Parameter(Mandatory = $true)]
        [string]$VersionLabel,
        [Parameter(Mandatory = $true)]
        [string]$Runtime,
        [Parameter(Mandatory = $true)]
        [string]$CompatibilityVersion
    )

    $compatibilityLabel = Get-NormalizedLabel -Label $CompatibilityVersion
    return "IndigoMovieManager.Thumbnail.RescueWorker-$VersionLabel-$Runtime-compat-$compatibilityLabel.zip"
}

function Get-PreparedWorkerSourceMetadata {
    param(
        [Parameter(Mandatory = $true)]
        [string]$RepoRoot,
        [Parameter(Mandatory = $true)]
        [string]$PreparedPublishDir,
        [Parameter(Mandatory = $true)]
        [string]$DefaultVersion,
        [Parameter(Mandatory = $true)]
        [string]$Runtime
    )

    if ([string]::IsNullOrWhiteSpace($PreparedPublishDir)) {
        throw "prepared worker publish directory が未指定です。create_github_release_package.ps1 は app package 専用であり、local worker source build は行いません。scripts/sync_private_engine_worker_artifact.ps1 で artifact を同期するか、PreparedWorkerPublishDir を明示してください。"
    }

    $publishDir = [System.IO.Path]::GetFullPath($PreparedPublishDir, $RepoRoot)
    if (-not (Test-Path -LiteralPath $publishDir)) {
        throw "prepared worker publish directory が見つかりません: $publishDir"
    }

    $markerPath = Join-Path $publishDir "rescue-worker-artifact.json"
    if (-not (Test-Path -LiteralPath $markerPath)) {
        throw "worker artifact marker が見つかりません: $markerPath"
    }

    $marker = Read-RescueWorkerArtifactMarker -MarkerPath $markerPath
    $markerCompatibilityVersion = "$($marker.compatibilityVersion)".Trim()
    if ([string]::IsNullOrWhiteSpace($markerCompatibilityVersion)) {
        throw "worker artifact marker の compatibilityVersion が空です: $markerPath"
    }

    $metadataPath = Join-Path $publishDir "rescue-worker-sync-source.json"
    $sourceType = ""
    $version = $DefaultVersion
    $assetFileName = ""
    $sourceArtifactName = ""
    $compatibilityVersion = ""

    if (Test-Path -LiteralPath $metadataPath) {
        $metadata = Get-Content -LiteralPath $metadataPath -Raw -Encoding utf8 | ConvertFrom-Json
        $sourceType = "$($metadata.sourceType)".Trim()
        if (-not [string]::IsNullOrWhiteSpace("$($metadata.version)")) {
            $version = "$($metadata.version)".Trim()
        }

        $assetFileName = "$($metadata.assetFileName)".Trim()
        $sourceArtifactName = "$($metadata.sourceArtifactName)".Trim()
        $compatibilityVersion = "$($metadata.compatibilityVersion)".Trim()
    }

    if (-not [string]::IsNullOrWhiteSpace($compatibilityVersion) -and $compatibilityVersion -ne $markerCompatibilityVersion) {
        throw "sync metadata と marker の compatibilityVersion が一致しません: metadata='$compatibilityVersion' marker='$markerCompatibilityVersion'"
    }

    if ([string]::IsNullOrWhiteSpace($assetFileName)) {
        $assetFileName = Get-DefaultRescueWorkerAssetFileName `
            -VersionLabel $version `
            -Runtime $Runtime `
            -CompatibilityVersion $markerCompatibilityVersion
    }

    return [pscustomobject][ordered]@{
        PublishDirectory = $publishDir
        SourceType = $(if ([string]::IsNullOrWhiteSpace($sourceType)) { "prepared-publish-dir" } else { $sourceType })
        Version = $(if ([string]::IsNullOrWhiteSpace($version)) { $DefaultVersion } else { $version })
        AssetFileName = $assetFileName
        SourceArtifactName = $sourceArtifactName
        CompatibilityVersion = $markerCompatibilityVersion
    }
}

function Get-PreparedPrivateEnginePackageMetadata {
    param(
        [string]$PreparedPackageDir
    )

    if ([string]::IsNullOrWhiteSpace($PreparedPackageDir)) {
        return $null
    }

    $metadataPath = Join-Path $PreparedPackageDir "private-engine-packages-source.json"
    if (-not (Test-Path -LiteralPath $metadataPath)) {
        return $null
    }

    $metadata = Get-Content -LiteralPath $metadataPath -Raw -Encoding utf8 | ConvertFrom-Json
    $sourceType = "$($metadata.sourceType)".Trim()
    $packageVersion = "$($metadata.packageVersion)".Trim()
    $manifestFileName = "$($metadata.manifestFileName)".Trim()
    $packages = @()
    if ($null -ne $metadata.packages) {
        $packages = @(
            $metadata.packages | ForEach-Object {
                [pscustomobject]@{
                    PackageId = "$($_.packageId)".Trim()
                    AssetFileName = "$($_.assetFileName)".Trim()
                    Sha256 = "$($_.sha256)".Trim().ToUpperInvariant()
                }
            }
        )
    }

    return [pscustomobject][ordered]@{
        SourceType = $(if ([string]::IsNullOrWhiteSpace($sourceType)) { "prepared-package-dir" } else { $sourceType })
        PackageVersion = $packageVersion
        ManifestFileName = $manifestFileName
        Packages = $packages
    }
}

function Resolve-PrivateEnginePackageVersionFromDirectory {
    param(
        [Parameter(Mandatory = $true)]
        [string]$PreparedPackageDir
    )

    $packageIds = @(
        "IndigoMovieEngine.Thumbnail.Contracts",
        "IndigoMovieEngine.Thumbnail.Engine",
        "IndigoMovieEngine.Thumbnail.FailureDb"
    )
    $resolvedVersions = @()

    foreach ($packageId in $packageIds) {
        $pattern = '^' + [regex]::Escape($packageId) + '\.(.+)\.nupkg$'
        $matchedVersions = @(
            Get-ChildItem -Path $PreparedPackageDir -File -Filter "$packageId.*.nupkg" -ErrorAction SilentlyContinue |
                Where-Object { $_.Name -match $pattern } |
                ForEach-Object { $Matches[1] }
        )

        if ($matchedVersions.Count -ne 1) {
            throw "private engine package version を一意に解決できません: $packageId -> $($matchedVersions -join ', ')"
        }

        $resolvedVersions += $matchedVersions[0]
    }

    $uniqueVersions = @($resolvedVersions | Sort-Object -Unique)
    if ($uniqueVersions.Count -ne 1) {
        throw "private engine package version が揃っていません: $($uniqueVersions -join ', ')"
    }

    return $uniqueVersions[0]
}

function Resolve-PrivateEnginePackagesFromDirectory {
    param(
        [Parameter(Mandatory = $true)]
        [string]$PreparedPackageDir
    )

    $packageVersion = Resolve-PrivateEnginePackageVersionFromDirectory -PreparedPackageDir $PreparedPackageDir
    $packageIds = @(
        "IndigoMovieEngine.Thumbnail.Contracts",
        "IndigoMovieEngine.Thumbnail.Engine",
        "IndigoMovieEngine.Thumbnail.FailureDb"
    )
    $manifestFileName = if (Test-Path -LiteralPath (Join-Path $PreparedPackageDir "private-engine-packages-manifest.json")) {
        "private-engine-packages-manifest.json"
    }
    else {
        ""
    }
    $packages = @()

    foreach ($packageId in $packageIds) {
        $assetPath = Join-Path $PreparedPackageDir "$packageId.$packageVersion.nupkg"
        if (-not (Test-Path -LiteralPath $assetPath)) {
            throw "private engine package asset が見つかりません: $assetPath"
        }

        $packages += [pscustomobject]@{
            PackageId = $packageId
            AssetFileName = [System.IO.Path]::GetFileName($assetPath)
            Sha256 = (Get-FileHash -LiteralPath $assetPath -Algorithm SHA256).Hash.ToUpperInvariant()
        }
    }

    return [pscustomobject][ordered]@{
        PackageVersion = $packageVersion
        ManifestFileName = $manifestFileName
        Packages = $packages
    }
}

function Resolve-PreparedPrivateEnginePackages {
    param(
        [Parameter(Mandatory = $true)]
        [string]$RepoRoot,
        [string]$PreparedPackageDir
    )

    if ([string]::IsNullOrWhiteSpace($PreparedPackageDir)) {
        return $null
    }

    $packageDir = [System.IO.Path]::GetFullPath($PreparedPackageDir, $RepoRoot)
    if (-not (Test-Path -LiteralPath $packageDir)) {
        throw "prepared private engine package directory が見つかりません: $packageDir"
    }

    $metadata = Get-PreparedPrivateEnginePackageMetadata -PreparedPackageDir $packageDir
    $actualMetadata = Resolve-PrivateEnginePackagesFromDirectory -PreparedPackageDir $packageDir
    if ($null -ne $metadata -and -not [string]::IsNullOrWhiteSpace($metadata.PackageVersion)) {
        if ($metadata.PackageVersion -ne $actualMetadata.PackageVersion) {
            throw "prepared private engine package metadata と実体の packageVersion が一致しません: metadata='$($metadata.PackageVersion)' actual='$($actualMetadata.PackageVersion)'"
        }

        if ($null -ne $metadata.Packages -and $metadata.Packages.Count -gt 0) {
            foreach ($package in $metadata.Packages) {
                $actualPackage = @($actualMetadata.Packages | Where-Object { $_.PackageId -eq $package.PackageId })
                if ($actualPackage.Count -ne 1) {
                    throw "prepared private engine package metadata の packageId を実体から一意に解決できません: $($package.PackageId)"
                }

                if ($actualPackage[0].AssetFileName -ne $package.AssetFileName) {
                    throw "prepared private engine package metadata と実体の assetFileName が一致しません: $($package.PackageId)"
                }
                if ($actualPackage[0].Sha256 -ne $package.Sha256) {
                    throw "prepared private engine package metadata と実体の sha256 が一致しません: $($package.PackageId)"
                }
            }
        }

        $resolvedMetadata = [pscustomobject][ordered]@{
            PackageVersion = $actualMetadata.PackageVersion
            ManifestFileName = $(if ([string]::IsNullOrWhiteSpace($metadata.ManifestFileName)) { $actualMetadata.ManifestFileName } else { $metadata.ManifestFileName })
            Packages = $(if ($null -ne $metadata.Packages -and $metadata.Packages.Count -gt 0) { $metadata.Packages } else { $actualMetadata.Packages })
            SourceType = $metadata.SourceType
        }
    }
    else {
        $resolvedMetadata = [pscustomobject][ordered]@{
            PackageVersion = $actualMetadata.PackageVersion
            ManifestFileName = $actualMetadata.ManifestFileName
            Packages = $actualMetadata.Packages
            SourceType = "prepared-package-dir"
        }
    }

    return [pscustomobject][ordered]@{
        DirectoryPath = $packageDir
        PackageVersion = $resolvedMetadata.PackageVersion
        SourceType = $resolvedMetadata.SourceType
        ManifestFileName = $resolvedMetadata.ManifestFileName
        Packages = $resolvedMetadata.Packages
    }
}

function Read-RescueWorkerArtifactMarker {
    param(
        [Parameter(Mandatory = $true)]
        [string]$MarkerPath
    )

    return Get-Content -LiteralPath $MarkerPath -Raw -Encoding utf8 | ConvertFrom-Json
}

function Publish-RescueWorkerArtifactIntoPackage {
    param(
        [Parameter(Mandatory = $true)]
        [string]$PackageDir,
        [Parameter(Mandatory = $true)]
        [psobject]$PreparedWorkerSourceMetadata
    )

    $workerPackageDir = Join-Path $PackageDir "rescue-worker"
    $sourcePublishDir = "$($PreparedWorkerSourceMetadata.PublishDirectory)".Trim()
    if ([string]::IsNullOrWhiteSpace($sourcePublishDir)) {
        throw "prepared worker source metadata の PublishDirectory が空です。"
    }

    if (Test-Path -LiteralPath $workerPackageDir) {
        Remove-Item -LiteralPath $workerPackageDir -Recurse -Force
    }

    if (-not (Test-Path -LiteralPath $sourcePublishDir)) {
        throw "prepared worker publish directory が見つかりません: $sourcePublishDir"
    }

    $markerPath = Join-Path $sourcePublishDir "rescue-worker-artifact.json"
    if (-not (Test-Path -LiteralPath $markerPath)) {
        throw "worker artifact marker が見つかりません: $markerPath"
    }

    $marker = Read-RescueWorkerArtifactMarker -MarkerPath $markerPath
    $markerCompatibilityVersion = "$($marker.compatibilityVersion)".Trim()
    if ([string]::IsNullOrWhiteSpace($markerCompatibilityVersion)) {
        throw "worker artifact marker の compatibilityVersion が空です: $markerPath"
    }

    if ($PreparedWorkerSourceMetadata.CompatibilityVersion -ne $markerCompatibilityVersion) {
        throw "prepared worker source metadata と marker の compatibilityVersion が一致しません: metadata='$($PreparedWorkerSourceMetadata.CompatibilityVersion)' marker='$markerCompatibilityVersion'"
    }

    New-Item -ItemType Directory -Path $workerPackageDir -Force | Out-Null
    Copy-Item -Path (Join-Path $sourcePublishDir "*") -Destination $workerPackageDir -Recurse -Force

    return [pscustomobject][ordered]@{
        SourceType = $PreparedWorkerSourceMetadata.SourceType
        Version = $PreparedWorkerSourceMetadata.Version
        AssetFileName = $PreparedWorkerSourceMetadata.AssetFileName
        SourceArtifactName = $PreparedWorkerSourceMetadata.SourceArtifactName
        CompatibilityVersion = $markerCompatibilityVersion
    }
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

    $sourceArtifactSummaryLine = ""
    if ($workerArtifact.Contains("sourceArtifactName")) {
        $sourceArtifactSummaryLine = "- sourceArtifactName: $($workerArtifact["sourceArtifactName"])
"
    }
    $privateEngineSummaryLines = ""
    if ($WorkerLock.Contains("privateEnginePackages")) {
        $privateEnginePackages = $WorkerLock["privateEnginePackages"]
        $packageLines = ""
        if ($privateEnginePackages.Contains("manifestFileName")) {
            $packageLines += "- privateEnginePackages.manifestFileName: $($privateEnginePackages["manifestFileName"])
"
        }
        if ($privateEnginePackages.Contains("packages")) {
            foreach ($package in $privateEnginePackages["packages"]) {
                $packageLines += "- package: $($package["packageId"]) / $($package["assetFileName"]) / $($package["sha256"])
"
            }
        }
        $privateEngineSummaryLines = @"
- privateEnginePackages.sourceType: $($privateEnginePackages["sourceType"])
- privateEnginePackages.version: $($privateEnginePackages["version"])
$packageLines
"@
    }

    return @"
Rescue Worker Lock Summary
==========================

- source: $($workerArtifact["sourceType"])
- version: $($workerArtifact["version"])
- asset: $($workerArtifact["assetFileName"])
- compatibilityVersion: $($workerArtifact["compatibilityVersion"])
$sourceArtifactSummaryLine- workerExecutableSha256: $($workerArtifact["workerExecutableSha256"])
- bundledWorker: $BundledRescueWorkerRelativePath
$privateEngineSummaryLines
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
    $sourceArtifactReleaseLine = ""
    if ($workerArtifact.Contains("sourceArtifactName")) {
        $sourceArtifactReleaseLine = "- SourceArtifactName: ``$($workerArtifact["sourceArtifactName"])``
"
    }
    $privateEngineReleaseLines = ""
    if ($WorkerLock.Contains("privateEnginePackages")) {
        $privateEnginePackages = $WorkerLock["privateEnginePackages"]
        $packageLines = ""
        if ($privateEnginePackages.Contains("manifestFileName")) {
            $packageLines += "- EnginePackageManifest: ``$($privateEnginePackages["manifestFileName"])``
"
        }
        if ($privateEnginePackages.Contains("packages")) {
            foreach ($package in $privateEnginePackages["packages"]) {
                $packageLines += "- EnginePackage: ``$($package["packageId"])`` / ``$($package["assetFileName"])`` / ``$($package["sha256"])``
"
            }
        }
        $privateEngineReleaseLines = @"
- EnginePackageSource: ``$($privateEnginePackages["sourceType"])``
- EnginePackageVersion: ``$($privateEnginePackages["version"])``
$packageLines
"@
    }
    $releaseBodySnippet = @"
### Bundled Rescue Worker

- Source: ``$($workerArtifact["sourceType"])``
- Version: ``$($workerArtifact["version"])``
- Artifact: ``$($workerArtifact["assetFileName"])``
$sourceArtifactReleaseLine- CompatibilityVersion: ``$($workerArtifact["compatibilityVersion"])``
- WorkerExe SHA256: ``$($workerArtifact["workerExecutableSha256"])``
$privateEngineReleaseLines
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
$preparedWorkerSource = Get-PreparedWorkerSourceMetadata `
    -RepoRoot $repoRoot `
    -PreparedPublishDir $PreparedWorkerPublishDir `
    -DefaultVersion $versionLabelNormalized `
    -Runtime $Runtime
$rescueWorkerCompatibilityVersion = $preparedWorkerSource.CompatibilityVersion

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
$preparedPrivateEnginePackages = Resolve-PreparedPrivateEnginePackages `
    -RepoRoot $repoRoot `
    -PreparedPackageDir $PreparedPrivateEnginePackageDir
if ($null -eq $preparedPrivateEnginePackages) {
    throw "prepared private engine package directory が未指定です。create_github_release_package.ps1 は shared core package consume 前提です。scripts/sync_private_engine_packages.ps1 で package を同期するか、PreparedPrivateEnginePackageDir を明示してください。"
}

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

if ($null -ne $preparedPrivateEnginePackages) {
    # release package では shared core も Private 正本 package を使って復元する。
    $publishArguments += @(
        "-p:ImmUsePrivateEnginePackages=true"
        "-p:ImmPrivateEnginePackageSource=$($preparedPrivateEnginePackages.DirectoryPath)"
        "-p:ImmPrivateEnginePackageVersion=$($preparedPrivateEnginePackages.PackageVersion)"
    )
}

# GitHub Actions でもローカルでも同じ publish 条件に揃える。
Write-Host "dotnet $($publishArguments -join ' ')"
& dotnet @publishArguments
if ($LASTEXITCODE -ne 0) {
    throw "dotnet publish に失敗しました。"
}

# publish 出力をそのまま配布フォルダへ移し、依存 DLL 取りこぼしを防ぐ。
Copy-Item -Path (Join-Path $publishDir "*") -Destination $packageDir -Recurse -Force

# rescue worker は完成済み artifact を rescue-worker 配下へ同梱し、利用者が別 asset を探さなくて済む形にする。
$workerArtifactSource = Publish-RescueWorkerArtifactIntoPackage `
    -PackageDir $packageDir `
    -PreparedWorkerSourceMetadata $preparedWorkerSource

# 配布 ZIP にはデバッグ用 pdb を含めず、利用者向けの同梱物だけへ絞る。
Get-ChildItem -Path $packageDir -Filter *.pdb -Recurse -File | Remove-Item -Force

$bundledRescueWorkerRelativePath = "rescue-worker\IndigoMovieManager.Thumbnail.RescueWorker.exe"
$bundledRescueWorkerPath = Join-Path $packageDir $bundledRescueWorkerRelativePath
if (-not (Test-Path -LiteralPath $bundledRescueWorkerPath)) {
    throw "同梱 rescue worker exe が見つかりません: $bundledRescueWorkerPath"
}

$bundledRescueWorkerHash = (Get-FileHash -LiteralPath $bundledRescueWorkerPath -Algorithm SHA256).Hash.ToUpperInvariant()
$workerArtifactLock = [ordered]@{
    artifactType = "IndigoMovieManager.Thumbnail.RescueWorker"
    sourceType = $workerArtifactSource.SourceType
    version = $workerArtifactSource.Version
    assetFileName = $workerArtifactSource.AssetFileName
    compatibilityVersion = $rescueWorkerCompatibilityVersion
    workerExecutableSha256 = $bundledRescueWorkerHash
}
if (-not [string]::IsNullOrWhiteSpace($workerArtifactSource.SourceArtifactName)) {
    $workerArtifactLock.sourceArtifactName = $workerArtifactSource.SourceArtifactName
}

$rescueWorkerLock = [ordered]@{
    schemaVersion = 1
    workerArtifact = $workerArtifactLock
}
if ($null -ne $preparedPrivateEnginePackages) {
    $privateEnginePackagesLock = [ordered]@{
        sourceType = $preparedPrivateEnginePackages.SourceType
        version = $preparedPrivateEnginePackages.PackageVersion
    }
    if (-not [string]::IsNullOrWhiteSpace($preparedPrivateEnginePackages.ManifestFileName)) {
        $privateEnginePackagesLock.manifestFileName = $preparedPrivateEnginePackages.ManifestFileName
    }
    if ($null -ne $preparedPrivateEnginePackages.Packages -and $preparedPrivateEnginePackages.Packages.Count -gt 0) {
        $privateEnginePackagesLock.packages = @(
            $preparedPrivateEnginePackages.Packages | ForEach-Object {
                [ordered]@{
                    packageId = $_.PackageId
                    assetFileName = $_.AssetFileName
                    sha256 = $_.Sha256
                }
            }
        )
    }
    $rescueWorkerLock.privateEnginePackages = $privateEnginePackagesLock
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
- engine package source: $($preparedPrivateEnginePackages.SourceType)
- engine package version: $($preparedPrivateEnginePackages.PackageVersion)
- worker source: $($workerArtifactSource.SourceType)
- worker source version: $($workerArtifactSource.Version)
- worker source artifact: $($workerArtifactSource.AssetFileName)
- worker source artifact name: $(if ([string]::IsNullOrWhiteSpace($workerArtifactSource.SourceArtifactName)) { "-" } else { $workerArtifactSource.SourceArtifactName })
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
