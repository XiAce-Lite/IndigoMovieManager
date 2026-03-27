param(
    [string]$Configuration = "Debug",
    [string]$Platform = "x64",
    [string]$MyLabRoot = "",
    [switch]$UseExternalEverythingLite
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

function Resolve-MsBuildPath {
    # まず開発環境固定パスを優先し、見つからない場合はvswhereで探索する。
    $preferred = "C:\Program Files\Microsoft Visual Studio\18\Community\MSBuild\Current\Bin\MSBuild.exe"
    if (Test-Path $preferred) {
        return $preferred
    }

    $vsWhere = Join-Path ${env:ProgramFiles(x86)} "Microsoft Visual Studio\Installer\vswhere.exe"
    if (Test-Path $vsWhere) {
        $found = & $vsWhere -latest -products * -requires Microsoft.Component.MSBuild -find "MSBuild\**\Bin\MSBuild.exe" | Select-Object -First 1
        if (-not [string]::IsNullOrWhiteSpace($found) -and (Test-Path $found)) {
            return $found
        }
    }

    throw "MSBuild が見つかりません。Visual Studio Build Tools を確認してください。"
}

function Resolve-RepoRoot {
    $scriptDir = Split-Path -Parent $PSCommandPath
    return (Resolve-Path (Join-Path $scriptDir "..")).Path
}

$repoRoot = Resolve-RepoRoot

$msbuildArgs = @(
    ".\IndigoMovieManager.sln",
    "/restore",
    "/t:Build",
    "/p:Configuration=$Configuration",
    "/p:Platform=$Platform",
    "/m"
)

if ($UseExternalEverythingLite) {
    if ([string]::IsNullOrWhiteSpace($MyLabRoot)) {
        $MyLabRoot = Join-Path (Split-Path -Parent $repoRoot) "MyLab"
    }

    $everythingLiteCsproj = Join-Path $MyLabRoot "EverythingLite\EverythingLite.csproj"
    if (-not (Test-Path $everythingLiteCsproj)) {
        throw "UseExternalEverythingLite=true ですが EverythingLite.csproj が見つかりません: $everythingLiteCsproj"
    }

    # 外部版を使うときだけプロジェクト参照先を上書きする。
    $msbuildArgs += "/p:UseExternalEverythingLite=true"
    $msbuildArgs += "/p:ExternalEverythingLiteProjectPath=$everythingLiteCsproj"
}

$msbuildPath = Resolve-MsBuildPath
Write-Host "[AB-CI] RepoRoot=$repoRoot"
Write-Host "[AB-CI] UseExternalEverythingLite=$UseExternalEverythingLite"
if ($UseExternalEverythingLite) {
    Write-Host "[AB-CI] MyLabRoot=$MyLabRoot"
}
Write-Host "[AB-CI] MSBuild=$msbuildPath"

Push-Location $repoRoot
try {
    # COM参照やWPFを含むため、先にMSBuildでソリューションをビルドする。
    & $msbuildPath @msbuildArgs
    if ($LASTEXITCODE -ne 0) {
        throw "MSBuild が失敗しました。exit code: $LASTEXITCODE"
    }

    # Provider差分の回帰対象だけを抽出して実行する。
    $filter = "FullyQualifiedName~EverythingLiteProviderTests|FullyQualifiedName~FileIndexProviderAbDiffTests|FullyQualifiedName~FileIndexReasonTableTests"
    & dotnet test ".\Tests\IndigoMovieManager.Tests\IndigoMovieManager.Tests.csproj" -c $Configuration --no-build --filter $filter
    if ($LASTEXITCODE -ne 0) {
        throw "dotnet test が失敗しました。exit code: $LASTEXITCODE"
    }

    Write-Host "[AB-CI] Completed successfully."
}
finally {
    Pop-Location
}
