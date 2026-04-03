[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string]$PackageDir
)

$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest

function Read-JsonFile {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Path
    )

    if (-not (Test-Path -LiteralPath $Path)) {
        throw "JSON file が見つかりません: $Path"
    }

    return Get-Content -LiteralPath $Path -Raw -Encoding utf8 | ConvertFrom-Json
}

function Get-RequiredPropertyValue {
    param(
        [Parameter(Mandatory = $true)]
        [object]$Object,
        [Parameter(Mandatory = $true)]
        [string]$PropertyName,
        [Parameter(Mandatory = $true)]
        [string]$ContextLabel
    )

    $property = $Object.PSObject.Properties[$PropertyName]
    if ($null -eq $property) {
        throw "$ContextLabel の必須項目がありません: $PropertyName"
    }

    $value = "$($property.Value)"
    if ([string]::IsNullOrWhiteSpace($value)) {
        throw "$ContextLabel の必須項目が空です: $PropertyName"
    }

    return $value.Trim()
}

function Get-NormalizedSha256 {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Path
    )

    if (-not (Test-Path -LiteralPath $Path)) {
        throw "hash 対象ファイルが見つかりません: $Path"
    }

    return (Get-FileHash -LiteralPath $Path -Algorithm SHA256).Hash.ToUpperInvariant()
}

function Test-RequiredArtifactPaths {
    param(
        [Parameter(Mandatory = $true)]
        [string]$ArtifactDirectory
    )

    $requiredRelativePaths = @(
        "IndigoMovieManager.Thumbnail.RescueWorker.exe",
        "Images\noFileSmall.jpg",
        "tools\ffmpeg-shared",
        "SQLitePCLRaw.batteries_v2.dll",
        "SQLitePCLRaw.core.dll",
        "SQLitePCLRaw.provider.e_sqlite3.dll",
        "System.Data.SQLite.dll"
    )

    for ($i = 0; $i -lt $requiredRelativePaths.Count; $i++) {
        $fullPath = Join-Path $ArtifactDirectory $requiredRelativePaths[$i]
        if (-not (Test-Path -LiteralPath $fullPath)) {
            throw "bundled worker artifact の必須項目が不足しています: $fullPath"
        }
    }
}

function Test-NativeSqlitePresence {
    param(
        [Parameter(Mandatory = $true)]
        [string]$ArtifactDirectory
    )

    $nativeSqliteCandidates = @(
        (Join-Path $ArtifactDirectory "e_sqlite3.dll"),
        (Join-Path $ArtifactDirectory "runtimes\win-x64\native\e_sqlite3.dll")
    )

    for ($i = 0; $i -lt $nativeSqliteCandidates.Count; $i++) {
        if (Test-Path -LiteralPath $nativeSqliteCandidates[$i] -PathType Leaf) {
            return
        }
    }

    throw "bundled worker artifact の native sqlite が不足しています。"
}

$packageDirFullPath = [System.IO.Path]::GetFullPath($PackageDir)
if (-not (Test-Path -LiteralPath $packageDirFullPath -PathType Container)) {
    throw "package directory が見つかりません: $packageDirFullPath"
}

$lockFilePath = Join-Path $packageDirFullPath "rescue-worker.lock.json"
$expectedFilePath = Join-Path $packageDirFullPath "rescue-worker-expected.json"

$lock = Read-JsonFile -Path $lockFilePath
$expected = Read-JsonFile -Path $expectedFilePath

$schemaVersionProperty = $lock.PSObject.Properties["schemaVersion"]
if ($null -eq $schemaVersionProperty -or [int]$schemaVersionProperty.Value -lt 1) {
    throw "rescue-worker.lock.json の schemaVersion が不正です。"
}

$workerArtifactProperty = $lock.PSObject.Properties["workerArtifact"]
if ($null -eq $workerArtifactProperty -or $null -eq $workerArtifactProperty.Value) {
    throw "rescue-worker.lock.json に workerArtifact がありません。"
}

$workerArtifact = $workerArtifactProperty.Value
$artifactType = Get-RequiredPropertyValue -Object $workerArtifact -PropertyName "artifactType" -ContextLabel "workerArtifact"
$compatibilityVersion = Get-RequiredPropertyValue -Object $workerArtifact -PropertyName "compatibilityVersion" -ContextLabel "workerArtifact"
$workerExecutableSha256 = Get-RequiredPropertyValue -Object $workerArtifact -PropertyName "workerExecutableSha256" -ContextLabel "workerArtifact"
$assetFileName = Get-RequiredPropertyValue -Object $workerArtifact -PropertyName "assetFileName" -ContextLabel "workerArtifact"

if ($artifactType -ne "IndigoMovieManager.Thumbnail.RescueWorker") {
    throw "workerArtifact.artifactType が不正です: $artifactType"
}

$bundledRescueWorkerRelativePath = Get-RequiredPropertyValue -Object $expected -PropertyName "bundledRescueWorkerRelativePath" -ContextLabel "rescue-worker-expected.json"
$expectedCompatibilityVersion = Get-RequiredPropertyValue -Object $expected -PropertyName "expectedRescueWorkerCompatibilityVersion" -ContextLabel "rescue-worker-expected.json"

$bundledRescueWorkerFullPath = Join-Path $packageDirFullPath $bundledRescueWorkerRelativePath
if (-not (Test-Path -LiteralPath $bundledRescueWorkerFullPath -PathType Leaf)) {
    throw "同梱 rescue worker exe が見つかりません: $bundledRescueWorkerFullPath"
}

$bundledRescueWorkerDirectory = Split-Path -Parent $bundledRescueWorkerFullPath
$markerPath = Join-Path $bundledRescueWorkerDirectory "rescue-worker-artifact.json"
$marker = Read-JsonFile -Path $markerPath
$markerArtifactType = Get-RequiredPropertyValue -Object $marker -PropertyName "artifactType" -ContextLabel "rescue-worker-artifact.json"
$markerCompatibilityVersion = Get-RequiredPropertyValue -Object $marker -PropertyName "compatibilityVersion" -ContextLabel "rescue-worker-artifact.json"

if ($markerArtifactType -ne "IndigoMovieManager.Thumbnail.RescueWorker") {
    throw "rescue-worker-artifact.json の artifactType が不正です: $markerArtifactType"
}

if ($compatibilityVersion -ne $expectedCompatibilityVersion) {
    throw "lock と expected の compatibilityVersion が一致しません: lock='$compatibilityVersion' expected='$expectedCompatibilityVersion'"
}

if ($compatibilityVersion -ne $markerCompatibilityVersion) {
    throw "lock と marker の compatibilityVersion が一致しません: lock='$compatibilityVersion' marker='$markerCompatibilityVersion'"
}

Test-RequiredArtifactPaths -ArtifactDirectory $bundledRescueWorkerDirectory
Test-NativeSqlitePresence -ArtifactDirectory $bundledRescueWorkerDirectory

$actualWorkerExecutableSha256 = Get-NormalizedSha256 -Path $bundledRescueWorkerFullPath
if ($workerExecutableSha256.ToUpperInvariant() -ne $actualWorkerExecutableSha256) {
    throw "lock と worker exe の sha256 が一致しません: lock='$workerExecutableSha256' actual='$actualWorkerExecutableSha256'"
}

Write-Host "worker lock verification ok"
Write-Host "- packageDir: $packageDirFullPath"
Write-Host "- bundledWorker: $bundledRescueWorkerRelativePath"
Write-Host "- compatibilityVersion: $compatibilityVersion"
Write-Host "- assetFileName: $assetFileName"
