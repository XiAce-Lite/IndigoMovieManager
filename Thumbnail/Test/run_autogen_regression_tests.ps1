param(
    [string]$Configuration = "Debug",
    [string]$Platform = "Any CPU",
    [string]$Filter = "FullyQualifiedName~AutogenRegressionTests",
    [switch]$SkipBuild,
    [switch]$CaptureFailureLogs = $true
)

$ErrorActionPreference = "Stop"

# スクリプト配置場所からリポジトリルートへ移動する。
$repoRoot = Resolve-Path (Join-Path $PSScriptRoot "..\..")
Set-Location $repoRoot

$msbuildPath = "C:\Program Files\Microsoft Visual Studio\18\Community\MSBuild\Current\Bin\MSBuild.exe"

function Save-FailureLogs {
    param(
        [string]$Reason
    )

    # 失敗時の調査用に主要ログを退避する。
    $timestamp = Get-Date -Format "yyyyMMdd_HHmmss"
    $baseDir = Join-Path $repoRoot "logs"
    $destDir = Join-Path $baseDir ("test-failures\" + $timestamp)
    New-Item -ItemType Directory -Path $destDir -Force | Out-Null

    $targets = @(
        "debug-runtime.log",
        "thumbnail-create-process.csv"
    )

    foreach ($name in $targets) {
        $source = Join-Path $baseDir $name
        if (Test-Path $source) {
            Copy-Item $source (Join-Path $destDir $name) -Force
        }
    }

    $metaPath = Join-Path $destDir "failure-meta.txt"
    $meta = @(
        "timestamp=$timestamp",
        "reason=$Reason",
        "configuration=$Configuration",
        "platform=$Platform",
        "filter=$Filter"
    )
    Set-Content -Path $metaPath -Value $meta -Encoding UTF8

    Write-Host "失敗ログを保存しました: $destDir"
}

try {
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
}
catch {
    if ($CaptureFailureLogs) {
        Save-FailureLogs -Reason $_.Exception.Message
    }
    throw
}
