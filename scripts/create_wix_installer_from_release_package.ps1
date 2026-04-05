param(
    [string]$Configuration = "Release",
    [string]$Runtime = "win-x64",
    [string]$VersionLabel = "",
    [string]$PackageDir = "",
    [string]$OutputRoot = "artifacts/github-release/installer"
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

function Get-RepoRoot {
    param([string]$ScriptPath)
    return (Resolve-Path (Join-Path (Split-Path -Parent $ScriptPath) "..")).Path
}

function Get-AppVersion {
    param([string]$RepoRoot)

    $version = & dotnet msbuild (Join-Path $RepoRoot "IndigoMovieManager.csproj") -getProperty:Version -nologo
    if ($LASTEXITCODE -ne 0) {
        throw "app version の取得に失敗しました。"
    }

    return $version.Trim()
}

function Convert-ToThreePartVersion {
    param([string]$Version)

    $parts = $Version.Split(".")
    if ($parts.Length -lt 3) {
        throw "3 part へ変換できない version です: $Version"
    }

    return "{0}.{1}.{2}" -f $parts[0], $parts[1], $parts[2]
}

function Resolve-PackageDir {
    param(
        [string]$RepoRoot,
        [string]$PackageDir,
        [string]$VersionLabel,
        [string]$Runtime
    )

    if (-not [string]::IsNullOrWhiteSpace($PackageDir)) {
        $resolved = [System.IO.Path]::GetFullPath($PackageDir, $RepoRoot)
        if (-not (Test-Path -LiteralPath $resolved)) {
            throw "package dir が見つかりません: $resolved"
        }

        return $resolved
    }

    if ([string]::IsNullOrWhiteSpace($VersionLabel)) {
        throw "PackageDir を省略する時は VersionLabel が必要です。"
    }

    $defaultDir = Join-Path $RepoRoot ("artifacts/github-release/package/IndigoMovieManager-{0}-{1}" -f $VersionLabel, $Runtime)
    if (-not (Test-Path -LiteralPath $defaultDir)) {
        throw "既定 package dir が見つかりません: $defaultDir"
    }

    return $defaultDir
}

$scriptPath = $MyInvocation.MyCommand.Path
$repoRoot = Get-RepoRoot -ScriptPath $scriptPath
$appVersion = Get-AppVersion -RepoRoot $repoRoot
$productVersion = Convert-ToThreePartVersion -Version $appVersion

if ([string]::IsNullOrWhiteSpace($VersionLabel)) {
    $VersionLabel = "v$appVersion"
}

$packageDirFullPath = Resolve-PackageDir `
    -RepoRoot $repoRoot `
    -PackageDir $PackageDir `
    -VersionLabel $VersionLabel `
    -Runtime $Runtime

$mainExePath = Join-Path $packageDirFullPath "IndigoMovieManager.exe"
if (-not (Test-Path -LiteralPath $mainExePath)) {
    throw "package dir に IndigoMovieManager.exe がありません: $mainExePath"
}

$workerLockPath = Join-Path $packageDirFullPath "rescue-worker.lock.json"
if (-not (Test-Path -LiteralPath $workerLockPath)) {
    throw "package dir に rescue-worker.lock.json がありません: $workerLockPath"
}

$outputRootFullPath = [System.IO.Path]::GetFullPath($OutputRoot, $repoRoot)
$versionOutputDir = Join-Path $outputRootFullPath "$VersionLabel-$Runtime"
$msiOutputDir = Join-Path $versionOutputDir "msi"
$bundleOutputDir = Join-Path $versionOutputDir "bundle"
$msiPath = Join-Path $msiOutputDir "IndigoMovieManager.msi"
$bundleExePath = Join-Path $bundleOutputDir "IndigoMovieManager.Bundle.exe"
$finalBundlePath = Join-Path $outputRootFullPath ("IndigoMovieManager-Setup-{0}-{1}.exe" -f $VersionLabel, $Runtime)

# 毎回同じ形で出せるよう、前回の残骸を先に消す。
foreach ($path in @($versionOutputDir, $finalBundlePath)) {
    if (Test-Path -LiteralPath $path) {
        Remove-Item -LiteralPath $path -Recurse -Force
    }
}

New-Item -ItemType Directory -Path $msiOutputDir -Force | Out-Null
New-Item -ItemType Directory -Path $bundleOutputDir -Force | Out-Null

$commonProperties = @(
    "-p:Configuration=$Configuration"
    "-p:ProductName=IndigoMovieManager"
    "-p:Manufacturer=IndigoMovieManager"
    "-p:ProductVersion=$productVersion"
    "-p:BundleVersion=$appVersion"
    "-p:MainExeName=IndigoMovieManager.exe"
)

$packageProjectPath = Join-Path $repoRoot "installer/wix/IndigoMovieManager.Product.wixproj"
$bundleProjectPath = Join-Path $repoRoot "installer/wix/IndigoMovieManager.Bundle.wixproj"

# 先に MSI を作り、その後に bundle へ包む。
# per-user harvest は ICE38 / ICE64 と衝突するため、v1 は validation を抑止して
# verify 済み package をそのまま包む downstream proof を優先する。
$packageArguments = @(
    "build"
    $packageProjectPath
    "-o"
    $msiOutputDir
    "-p:SuppressValidation=true"
    "-p:ImmInstallerProductVersion=$productVersion"
    "-p:ImmInstallerVerifiedPackageDir=$packageDirFullPath"
) + $commonProperties

& dotnet @packageArguments
if ($LASTEXITCODE -ne 0) {
    throw "WiX package build に失敗しました。"
}

if (-not (Test-Path -LiteralPath $msiPath)) {
    throw "MSI が生成されていません: $msiPath"
}

$bundleArguments = @(
    "build"
    $bundleProjectPath
    "-o"
    $bundleOutputDir
    "-p:SuppressValidation=true"
    "-p:ImmInstallerProductVersion=$productVersion"
    "-p:ImmInstallerBundleVersion=$appVersion"
    "-p:ImmInstallerMsiPath=$msiPath"
) + $commonProperties

& dotnet @bundleArguments
if ($LASTEXITCODE -ne 0) {
    throw "WiX bundle build に失敗しました。"
}

if (-not (Test-Path -LiteralPath $bundleExePath)) {
    throw "bundle exe が生成されていません: $bundleExePath"
}

Copy-Item -LiteralPath $bundleExePath -Destination $finalBundlePath -Force

Write-Host "PackageDir: $packageDirFullPath"
Write-Host "WorkerLock: $workerLockPath"
Write-Host "LockSource: package-local"
Write-Host "MsiPath: $msiPath"
Write-Host "BundleExe: $bundleExePath"
Write-Host "FinalSetupExe: $finalBundlePath"
