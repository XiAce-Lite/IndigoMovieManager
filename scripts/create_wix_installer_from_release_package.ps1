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

function Get-ProjectAppVersion {
    param([string]$RepoRoot)

    $version = & dotnet msbuild (Join-Path $RepoRoot "IndigoMovieManager.csproj") -getProperty:Version -nologo
    if ($LASTEXITCODE -ne 0) {
        throw "app version の取得に失敗しました。"
    }

    return $version.Trim()
}

function Get-PackageAppVersion {
    param([string]$MainExePath)

    $versionInfo = (Get-Item -LiteralPath $MainExePath).VersionInfo
    $fileVersion = [string]$versionInfo.FileVersion
    if ([string]::IsNullOrWhiteSpace($fileVersion)) {
        throw "package exe の FileVersion を取得できません: $MainExePath"
    }

    return $fileVersion.Trim()
}

function Convert-ToThreePartVersion {
    param([string]$Version)

    $parts = $Version.Split(".")
    if ($parts.Length -lt 3) {
        throw "3 part へ変換できない version です: $Version"
    }

    return "{0}.{1}.{2}" -f $parts[0], $parts[1], $parts[2]
}

function Get-DotNetDesktopRuntimeMetadata {
    param(
        [int]$MajorVersion = 8,
        [string]$Platform = "x64"
    )

    # bundle が remote prerequisite を正しく持てるよう、
    # 公式 metadata と installer 実ファイルの version resource を合わせて使う。
    $channel = "{0}.0" -f $MajorVersion
    $releaseMetadataUrl = "https://dotnetcli.blob.core.windows.net/dotnet/release-metadata/$channel/releases.json"
    $metadata = Invoke-RestMethod -Uri $releaseMetadataUrl
    if ($null -eq $metadata -or $null -eq $metadata.releases -or $metadata.releases.Count -eq 0) {
        throw ".NET Desktop Runtime metadata の取得に失敗しました: $releaseMetadataUrl"
    }

    $release = $metadata.releases[0]
    $runtime = $release.windowsdesktop
    if ($null -eq $runtime -or $null -eq $runtime.files) {
        throw ".NET Desktop Runtime metadata に windowsdesktop 情報がありません: $releaseMetadataUrl"
    }

    $rid = "win-$Platform"
    $runtimeFile = $runtime.files |
        Where-Object { $_.rid -eq $rid -and $_.url -like "*windowsdesktop-runtime-*-win-$Platform.exe" } |
        Select-Object -First 1

    if ($null -eq $runtimeFile) {
        throw ".NET Desktop Runtime installer が見つかりません: rid=$rid"
    }

    $downloadUrl = [string]$runtimeFile.url
    $downloadFileName = Split-Path -Leaf ([System.Uri]$downloadUrl).AbsolutePath
    $tempRoot = Join-Path ([System.IO.Path]::GetTempPath()) "IndigoMovieManager-dotnet-runtime"
    $tempFilePath = Join-Path $tempRoot $downloadFileName

    New-Item -ItemType Directory -Path $tempRoot -Force | Out-Null

    # WiX v4 の ExePackagePayload で必要な Size / ProductName / Description / Version を取るため、
    # Microsoft 公式 installer を一度だけ取得して version resource を読む。
    Invoke-WebRequest -Uri $downloadUrl -OutFile $tempFilePath

    $downloadedFile = Get-Item -LiteralPath $tempFilePath
    $versionInfo = $downloadedFile.VersionInfo
    $productName = [string]$versionInfo.ProductName
    $description = [string]$versionInfo.FileDescription
    $fileVersion = [string]$versionInfo.FileVersion

    if ([string]::IsNullOrWhiteSpace($productName)) {
        $productName = "Microsoft Windows Desktop Runtime - {0} ({1})" -f $runtime.version, $Platform
    }

    if ([string]::IsNullOrWhiteSpace($description)) {
        $description = $productName
    }

    if ([string]::IsNullOrWhiteSpace($fileVersion)) {
        $fileVersion = "{0}.0" -f $runtime.version
    }

    return [pscustomobject]@{
        MajorVersion   = [string]$MajorVersion
        MinimumVersion = "{0}.0.0" -f $MajorVersion
        DownloadUrl    = $downloadUrl
        FileName       = $downloadFileName
        Hash           = ([string]$runtimeFile.hash).ToUpperInvariant()
        Size           = [string]$downloadedFile.Length
        ProductName    = $productName
        Description    = $description
        Version        = $fileVersion
    }
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
$dotNetDesktopRuntime = Get-DotNetDesktopRuntimeMetadata -MajorVersion 8 -Platform "x64"

if ([string]::IsNullOrWhiteSpace($VersionLabel)) {
    $VersionLabel = "v$(Get-ProjectAppVersion -RepoRoot $repoRoot)"
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

# installer の版数は、現在 checkout 中の csproj ではなく、
# 実際に包む package の exe 版数へ合わせる。
$appVersion = Get-PackageAppVersion -MainExePath $mainExePath
$productVersion = Convert-ToThreePartVersion -Version $appVersion

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

# WiX の既定 obj/bin に前回 build の manifest が残ると、
# 別 package の版数差し替えが反映されない事があるため毎回掃除する。
foreach ($path in @(
    (Join-Path $repoRoot "installer/wix/bin"),
    (Join-Path $repoRoot "installer/wix/obj")
)) {
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
    "-t:Rebuild"
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
    "-t:Rebuild"
    "-o"
    $bundleOutputDir
    "-p:SuppressValidation=true"
    "-p:ImmInstallerProductVersion=$productVersion"
    "-p:ImmInstallerBundleVersion=$appVersion"
    "-p:ImmInstallerMsiPath=$msiPath"
    "-p:ImmInstallerDotNetDesktopRuntimeMajorVersion=$($dotNetDesktopRuntime.MajorVersion)"
    "-p:ImmInstallerDotNetDesktopRuntimeMinimumVersion=$($dotNetDesktopRuntime.MinimumVersion)"
    "-p:ImmInstallerDotNetDesktopRuntimeDownloadUrl=$($dotNetDesktopRuntime.DownloadUrl)"
    "-p:ImmInstallerDotNetDesktopRuntimeFileName=$($dotNetDesktopRuntime.FileName)"
    "-p:ImmInstallerDotNetDesktopRuntimeHash=$($dotNetDesktopRuntime.Hash)"
    "-p:ImmInstallerDotNetDesktopRuntimeSize=$($dotNetDesktopRuntime.Size)"
    "-p:ImmInstallerDotNetDesktopRuntimeProductName=$($dotNetDesktopRuntime.ProductName)"
    "-p:ImmInstallerDotNetDesktopRuntimeDescription=$($dotNetDesktopRuntime.Description)"
    "-p:ImmInstallerDotNetDesktopRuntimeVersion=$($dotNetDesktopRuntime.Version)"
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
Write-Host "DotNetDesktopRuntime: $($dotNetDesktopRuntime.DownloadUrl)"
