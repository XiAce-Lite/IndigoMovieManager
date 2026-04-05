[CmdletBinding()]
param(
    [string]$Configuration = "Debug",
    [string]$Platform = "x64",
    [string]$PackageSource = "",
    [string]$PackageVersion = ""
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

$script:PrivateEnginePackageManifestFileName = "private-engine-packages-manifest.json"

function Resolve-PackageSourcePath {
    param(
        [string]$PackageSource,
        [string]$Configuration
    )

    if (-not [string]::IsNullOrWhiteSpace($PackageSource)) {
        return [System.IO.Path]::GetFullPath($PackageSource)
    }

    $defaultSource = Join-Path $env:USERPROFILE "source\repos\IndigoMovieEngine\artifacts\private-engine-packages\$Configuration"
    return [System.IO.Path]::GetFullPath($defaultSource)
}

function Resolve-PackageVersionFromFeed {
    param(
        [string]$PackageSourcePath
    )

    $manifestPath = Join-Path $PackageSourcePath $script:PrivateEnginePackageManifestFileName
    if (Test-Path -LiteralPath $manifestPath) {
        $manifest = Get-Content -LiteralPath $manifestPath -Raw -Encoding utf8 | ConvertFrom-Json
        $manifestVersion = "$($manifest.packageVersion)".Trim()
        if (-not [string]::IsNullOrWhiteSpace($manifestVersion)) {
            return $manifestVersion
        }
    }

    $packageIds = @(
        "IndigoMovieEngine.Thumbnail.Contracts",
        "IndigoMovieEngine.Thumbnail.Engine",
        "IndigoMovieEngine.Thumbnail.FailureDb"
    )

    $resolvedVersions = @()

    foreach ($packageId in $packageIds) {
        $pattern = [regex]::Escape($packageId) + '\.(.+)\.nupkg$'
        $matches = @(
            Get-ChildItem -Path $PackageSourcePath -File -Filter "$packageId.*.nupkg" -ErrorAction SilentlyContinue |
                Where-Object { $_.Name -match $pattern } |
                ForEach-Object { [pscustomobject]@{ Name = $_.Name; Version = $Matches[1] } }
        )

        if ($null -eq $matches -or $matches.Count -eq 0) {
            throw "package が見つかりません: $packageId"
        }

        $uniqueVersions = @($matches.Version | Sort-Object -Unique)
        if ($uniqueVersions.Count -ne 1) {
            throw "package version が一意に決まりません: $packageId -> $($uniqueVersions -join ', ')"
        }

        $resolvedVersions += $uniqueVersions[0]
    }

    $commonVersions = @($resolvedVersions | Sort-Object -Unique)
    if ($commonVersions.Count -ne 1) {
        throw "Contracts / Engine / FailureDb で version が揃っていません: $($commonVersions -join ', ')"
    }

    return $commonVersions[0]
}

function Invoke-CheckedCommand {
    param(
        [Parameter(Mandatory = $true)]
        [string]$FilePath,
        [Parameter(Mandatory = $true)]
        [string[]]$ArgumentList
    )

    Write-Host ">> $FilePath $($ArgumentList -join ' ')"
    & $FilePath @ArgumentList
    if ($LASTEXITCODE -ne 0) {
        throw "コマンドに失敗しました: $FilePath"
    }
}

$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot "..")).Path
$packageSourcePath = Resolve-PackageSourcePath -PackageSource $PackageSource -Configuration $Configuration

if (-not (Test-Path -LiteralPath $packageSourcePath)) {
    throw "package source が見つかりません: $packageSourcePath"
}

$resolvedPackageVersion = if (-not [string]::IsNullOrWhiteSpace($PackageVersion)) {
    $PackageVersion.Trim()
}
else {
    Resolve-PackageVersionFromFeed -PackageSourcePath $packageSourcePath
}

Write-Host "Package source: $packageSourcePath"
Write-Host "Package version: $resolvedPackageVersion"

# app 側が shared core package を飲めるかを、最短経路で先に確認する。
$buildArguments = @(
    "msbuild",
    "IndigoMovieManager.csproj",
    "/restore",
    "/p:Configuration=$Configuration",
    "/p:Platform=$Platform",
    "/p:ImmUsePrivateEnginePackages=true",
    "/p:ImmPrivateEnginePackageVersion=$resolvedPackageVersion",
    "/p:ImmPrivateEnginePackageSource=$packageSourcePath"
)
Invoke-CheckedCommand -FilePath "dotnet" -ArgumentList $buildArguments

# launcher / FailureDb / create entry の consumer 観点だけを targeted test で確認する。
$testArguments = @(
    "test",
    "Tests\IndigoMovieManager.Tests\IndigoMovieManager.Tests.csproj",
    "-c", $Configuration,
    "-p:Platform=$Platform",
    "-p:ImmUsePrivateEnginePackages=true",
    "-p:ImmPrivateEnginePackageVersion=$resolvedPackageVersion",
    "-p:ImmPrivateEnginePackageSource=$packageSourcePath",
    "--filter",
    "FullyQualifiedName~ThumbnailFailureDbTests|FullyQualifiedName~ThumbnailRescueWorkerLauncherTests|FullyQualifiedName~ThumbnailCreateEntryCoordinatorTests"
)
Invoke-CheckedCommand -FilePath "dotnet" -ArgumentList $testArguments

Write-Host "Private Engine package consume validation completed."
