param(
    [string]$Configuration = "Debug",
    [string]$Platform = "Any CPU",
    [string]$Filter = "FullyQualifiedName~AutogenRegressionTests",
    [switch]$SkipBuild
)

$ErrorActionPreference = "Stop"

# スクリプト配置場所からリポジトリルートへ移動する。
$repoRoot = Resolve-Path (Join-Path $PSScriptRoot "..\..")
Set-Location $repoRoot

$msbuildPath = "C:\Program Files\Microsoft Visual Studio\18\Community\MSBuild\Current\Bin\MSBuild.exe"

if (-not $SkipBuild) {
    if (-not (Test-Path $msbuildPath)) {
        throw "MSBuild が見つかりません: $msbuildPath"
    }

    # COM参照を含むため、先にMSBuildでソリューションをビルドする。
    & $msbuildPath "IndigoMovieManager_fork.sln" "/p:Configuration=$Configuration" "/p:Platform=$Platform" "/m"
    if ($LASTEXITCODE -ne 0) {
        throw "MSBuild が失敗しました。exit code: $LASTEXITCODE"
    }
}

# ビルド済み成果物に対して回帰テストを実行する。
& dotnet test "Tests/IndigoMovieManager_fork.Tests/IndigoMovieManager_fork.Tests.csproj" -c $Configuration --no-build --filter $Filter
if ($LASTEXITCODE -ne 0) {
    throw "dotnet test が失敗しました。exit code: $LASTEXITCODE"
}

Write-Host "Autogen回帰テストが成功しました。"
