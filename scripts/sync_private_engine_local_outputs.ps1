[CmdletBinding()]
param(
    [string]$PrivateRepoPath = "",
    [string]$PrivateWorkerPublishDir = "",
    [string]$PrivatePackageDir = "",
    [string]$WorkerDestinationPath = "artifacts/rescue-worker/publish/Release-win-x64",
    [string]$PackageDestinationPath = "artifacts/private-engine-packages/Release"
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

function Get-RepoRoot {
    return (Resolve-Path (Join-Path $PSScriptRoot "..")).Path
}

function Write-Utf8NoBomJson {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Path,
        [Parameter(Mandatory = $true)]
        [object]$Object
    )

    $utf8NoBom = [System.Text.UTF8Encoding]::new($false)
    [System.IO.File]::WriteAllText($Path, ($Object | ConvertTo-Json -Depth 8), $utf8NoBom)
}

function Copy-DirectoryContent {
    param(
        [Parameter(Mandatory = $true)]
        [string]$SourceDirectory,
        [Parameter(Mandatory = $true)]
        [string]$DestinationDirectory
    )

    if (Test-Path -LiteralPath $DestinationDirectory) {
        Remove-Item -LiteralPath $DestinationDirectory -Recurse -Force
    }

    New-Item -ItemType Directory -Path $DestinationDirectory -Force | Out-Null
    Copy-Item -Path (Join-Path $SourceDirectory "*") -Destination $DestinationDirectory -Recurse -Force
}

function Resolve-PackageVersionFromDirectory {
    param(
        [Parameter(Mandatory = $true)]
        [string]$PackageDirectory
    )

    $manifestPath = Join-Path $PackageDirectory "private-engine-packages-manifest.json"
    if (-not (Test-Path -LiteralPath $manifestPath)) {
        throw "private-engine-packages-manifest.json が見つかりません: $manifestPath"
    }

    $manifest = Get-Content -LiteralPath $manifestPath -Raw -Encoding utf8 | ConvertFrom-Json
    return [pscustomobject]@{
        PackageVersion = "$($manifest.packageVersion)".Trim()
        Packages = @($manifest.packages)
    }
}

$repoRoot = Get-RepoRoot
if ([string]::IsNullOrWhiteSpace($PrivateRepoPath)) {
    $PrivateRepoPath = Join-Path $env:USERPROFILE "source\repos\IndigoMovieEngine"
}

if ([string]::IsNullOrWhiteSpace($PrivateWorkerPublishDir)) {
    $PrivateWorkerPublishDir = Join-Path $PrivateRepoPath "artifacts\rescue-worker\publish\Release-win-x64"
}

if ([string]::IsNullOrWhiteSpace($PrivatePackageDir)) {
    $PrivatePackageDir = Join-Path $PrivateRepoPath "artifacts\private-engine-packages\Release"
}

$workerDestinationFullPath = [System.IO.Path]::GetFullPath((Join-Path $repoRoot $WorkerDestinationPath))
$packageDestinationFullPath = [System.IO.Path]::GetFullPath((Join-Path $repoRoot $PackageDestinationPath))

if (-not (Test-Path -LiteralPath $PrivateWorkerPublishDir)) {
    throw "Private worker publish directory が見つかりません: $PrivateWorkerPublishDir"
}

if (-not (Test-Path -LiteralPath $PrivatePackageDir)) {
    throw "Private package directory が見つかりません: $PrivatePackageDir"
}

$workerMarkerPath = Join-Path $PrivateWorkerPublishDir "rescue-worker-artifact.json"
if (-not (Test-Path -LiteralPath $workerMarkerPath)) {
    throw "rescue-worker-artifact.json が見つかりません: $workerMarkerPath"
}

$workerMarker = Get-Content -LiteralPath $workerMarkerPath -Raw -Encoding utf8 | ConvertFrom-Json
$packageMetadata = Resolve-PackageVersionFromDirectory -PackageDirectory $PrivatePackageDir
$workerSourceVersion =
    if ($workerMarker.PSObject.Properties.Name -contains "version" -and -not [string]::IsNullOrWhiteSpace("$($workerMarker.version)")) {
        "$($workerMarker.version)"
    }
    else {
        "local-private-repo"
    }

# Public 側の prepared dir へ寄せることで、local private prepare の直後に app release をそのまま実行できるようにする。
Copy-DirectoryContent -SourceDirectory $PrivateWorkerPublishDir -DestinationDirectory $workerDestinationFullPath
Copy-DirectoryContent -SourceDirectory $PrivatePackageDir -DestinationDirectory $packageDestinationFullPath

$workerSyncMetadata = [ordered]@{
    schemaVersion = 1
    sourceType = "local-private-repo"
    version = $workerSourceVersion
    compatibilityVersion = "$($workerMarker.compatibilityVersion)"
    privateRepoPath = $PrivateRepoPath
    syncedAtUtc = [DateTime]::UtcNow.ToString("o")
}
Write-Utf8NoBomJson -Path (Join-Path $workerDestinationFullPath "rescue-worker-sync-source.json") -Object $workerSyncMetadata

$packageSyncMetadata = [ordered]@{
    schemaVersion = 1
    sourceType = "local-private-repo"
    version = $packageMetadata.PackageVersion
    packageVersion = $packageMetadata.PackageVersion
    privateRepoPath = $PrivateRepoPath
    syncedAtUtc = [DateTime]::UtcNow.ToString("o")
    packages = @(
        $packageMetadata.Packages | ForEach-Object {
            [ordered]@{
                packageId = "$($_.packageId)"
                assetFileName = "$($_.assetFileName)"
                sha256 = "$($_.sha256)"
            }
        }
    )
}
Write-Utf8NoBomJson -Path (Join-Path $packageDestinationFullPath "private-engine-packages-source.json") -Object $packageSyncMetadata

Write-Host "Private local outputs synced."
Write-Host "privateRepoPath: $PrivateRepoPath"
Write-Host "workerSource: $PrivateWorkerPublishDir"
Write-Host "workerDestination: $workerDestinationFullPath"
Write-Host "packageSource: $PrivatePackageDir"
Write-Host "packageDestination: $packageDestinationFullPath"
Write-Host "packageVersion: $($packageMetadata.PackageVersion)"
Write-Host "compatibilityVersion: $($workerMarker.compatibilityVersion)"
