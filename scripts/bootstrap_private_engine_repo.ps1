param(
    [ValidateSet("Bootstrap", "SyncDocs", "SyncSource", "SyncSources")]
    [string]$Mode = "Bootstrap",
    [string]$RepoRoot = "",
    [string]$PrivateRepoRoot = "",
    [switch]$DryRun
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

function Resolve-RepoRoot {
    $scriptDir = Split-Path -Parent $PSCommandPath
    return (Resolve-Path (Join-Path $scriptDir "..")).Path
}

function Resolve-PrivateRepoRoot {
    param([string]$RepoRootValue, [string]$PrivateRepoRootValue)

    if (-not [string]::IsNullOrWhiteSpace($PrivateRepoRootValue)) {
        return [System.IO.Path]::GetFullPath($PrivateRepoRootValue)
    }

    $repoParent = Split-Path -Parent $RepoRootValue
    return Join-Path $repoParent "IndigoMovieEngine"
}

function Ensure-Directory {
    param([string]$PathValue)

    if ($DryRun) {
        Write-Host "[dryrun] mkdir $PathValue"
        return
    }

    New-Item -ItemType Directory -Force -Path $PathValue | Out-Null
}

function Write-TextFileNoBom {
    param(
        [string]$PathValue,
        [string]$Text
    )

    if ($DryRun) {
        Write-Host "[dryrun] write $PathValue"
        return
    }

    $parent = Split-Path -Parent $PathValue
    if (-not [string]::IsNullOrWhiteSpace($parent)) {
        New-Item -ItemType Directory -Force -Path $parent | Out-Null
    }

    $normalized = ($Text ?? "") -replace "`r`n", "`n"
    Set-Content -Encoding utf8NoBOM -NoNewline -Path $PathValue -Value $normalized
}

function Copy-TextFileNoBom {
    param(
        [string]$SourcePath,
        [string]$TargetPath
    )

    if (-not (Test-Path $SourcePath)) {
        throw "コピー元が見つかりません: $SourcePath"
    }

    if ($DryRun) {
        Write-Host "[dryrun] copy $SourcePath -> $TargetPath"
        return
    }

    $parent = Split-Path -Parent $TargetPath
    if (-not [string]::IsNullOrWhiteSpace($parent)) {
        New-Item -ItemType Directory -Force -Path $parent | Out-Null
    }

    $content = Get-Content -Encoding UTF8 -Raw -Path $SourcePath
    $normalized = $content -replace "`r`n", "`n"
    Set-Content -Encoding utf8NoBOM -NoNewline -Path $TargetPath -Value $normalized
}

function Should-TreatAsTextFile {
    param([string]$PathValue)

    $textExtensions = @(
        ".cs",
        ".csproj",
        ".props",
        ".targets",
        ".resx",
        ".xaml",
        ".config",
        ".json",
        ".md",
        ".txt",
        ".xml",
        ".sln",
        ".yml",
        ".yaml",
        ".ps1",
        ".bat",
        ".cmd",
        ".settings",
        ".runsettings",
        ".editorconfig",
        ".gitattributes",
        ".gitignore"
    )

    $extension = [System.IO.Path]::GetExtension($PathValue).ToLowerInvariant()
    return $textExtensions -contains $extension
}

function Copy-FilePreservingKind {
    param(
        [string]$SourcePath,
        [string]$TargetPath
    )

    if (Should-TreatAsTextFile -PathValue $SourcePath) {
        Copy-TextFileNoBom -SourcePath $SourcePath -TargetPath $TargetPath
        return
    }

    if ($DryRun) {
        Write-Host "[dryrun] copy $SourcePath -> $TargetPath"
        return
    }

    $parent = Split-Path -Parent $TargetPath
    if (-not [string]::IsNullOrWhiteSpace($parent)) {
        New-Item -ItemType Directory -Force -Path $parent | Out-Null
    }

    Copy-Item -Force -Path $SourcePath -Destination $TargetPath
}

function Copy-TreeFiltered {
    param(
        [string]$SourceRoot,
        [string]$TargetRoot
    )

    if (-not (Test-Path $SourceRoot)) {
        throw "コピー元が見つかりません: $SourceRoot"
    }

    $excludePattern = '\\(bin|obj|publish|TestResults|\.vs|\.codex_build|\.tmp)(\\|$)'
    $excludeFileNames = @(
        "g.editorconfig"
    )
    $files = Get-ChildItem -Path $SourceRoot -Recurse -File -Force | Where-Object {
        $_.FullName -notmatch $excludePattern -and $excludeFileNames -notcontains $_.Name
    }

    foreach ($file in $files) {
        $relative = [System.IO.Path]::GetRelativePath($SourceRoot, $file.FullName)
        $targetPath = Join-Path $TargetRoot $relative
        Copy-FilePreservingKind -SourcePath $file.FullName -TargetPath $targetPath
    }
}

function Initialize-PrivateRepoLayout {
    param([string]$TargetRoot)

    $dirs = @(
        "src\IndigoMovieManager.Thumbnail.Contracts",
        "src\IndigoMovieManager.Thumbnail.Engine",
        "src\IndigoMovieManager.Thumbnail.FailureDb",
        "src\IndigoMovieManager.Thumbnail.RescueWorker",
        "tests",
        "tests\IndigoMovieManager.Tests",
        "scripts",
        "docs",
        ".github\workflows"
    )

    foreach ($relative in $dirs) {
        Ensure-Directory (Join-Path $TargetRoot $relative)
    }

    Write-TextFileNoBom (
        Join-Path $TargetRoot "README.md"
    ) @'
# IndigoMovieEngine

Private repo for engine / worker.

- Contracts
- Engine
- FailureDb
- RescueWorker

Public repo から app に機能を追加し、配る責務を守るための外だし先である。
'@

    Write-TextFileNoBom (
        Join-Path $TargetRoot "docs\README.md"
    ) @'
# docs

このフォルダには、Public repo から持ち出す契約・構成・運用の正本を置く。
'@

    Write-TextFileNoBom (
        Join-Path $TargetRoot "tests\README.md"
    ) @'
# tests

この repo では engine / worker 側の単体 test と contract test を集約する。
最小構成として `tests/IndigoMovieManager.Tests` を置き、build / test の入口から回す。
'@

    Write-TextFileNoBom (
        Join-Path $TargetRoot "tests\IndigoMovieManager.Tests\IndigoMovieManager.Tests.csproj"
    ) @'
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net8.0-windows</TargetFramework>
    <Nullable>disable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
    <RootNamespace>IndigoMovieManager.Tests</RootNamespace>
    <Platforms>x64</Platforms>
    <PlatformTarget>x64</PlatformTarget>
    <IsPackable>false</IsPackable>
    <IsTestProject>true</IsTestProject>
  </PropertyGroup>

  <ItemGroup>
    <PackageReference Include="Microsoft.NET.Test.Sdk" Version="18.0.1" />
    <PackageReference Include="NUnit" Version="4.5.0" />
    <PackageReference Include="NUnit3TestAdapter" Version="6.1.0" />
    <PackageReference Include="NUnit.Analyzers" Version="4.11.1">
      <PrivateAssets>all</PrivateAssets>
      <IncludeAssets>runtime; build; native; contentfiles; analyzers; buildtransitive</IncludeAssets>
    </PackageReference>
  </ItemGroup>

  <ItemGroup>
    <ProjectReference Include="..\..\src\IndigoMovieManager.Thumbnail.Contracts\IndigoMovieManager.Thumbnail.Contracts.csproj" />
    <ProjectReference Include="..\..\src\IndigoMovieManager.Thumbnail.FailureDb\IndigoMovieManager.Thumbnail.FailureDb.csproj" />
    <ProjectReference Include="..\..\src\IndigoMovieManager.Thumbnail.RescueWorker\IndigoMovieManager.Thumbnail.RescueWorker.csproj" />
  </ItemGroup>
</Project>
'@

    Write-TextFileNoBom (
        Join-Path $TargetRoot "tests\IndigoMovieManager.Tests\SmokeTests.cs"
    ) @'
using IndigoMovieManager.Thumbnail;
using IndigoMovieManager.Thumbnail.FailureDb;
using IndigoMovieManager.Thumbnail.RescueWorker;
using NUnit.Framework;

namespace IndigoMovieManager.Tests;

[TestFixture]
public sealed class SmokeTests
{
    private string failureDbRoot = "";

    [SetUp]
    public void SetUp()
    {
        // 共有の静的状態は、各 test の前に明示的に整えておく。
        failureDbRoot = Path.Combine(Path.GetTempPath(), "IndigoMovieEngine.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(failureDbRoot);
        ThumbnailQueueHostPathPolicy.Configure(failureDbDirectoryPath: failureDbRoot);
    }

    [TearDown]
    public void TearDown()
    {
        ThumbnailQueueHostPathPolicy.Configure(failureDbDirectoryPath: "");

        if (Directory.Exists(failureDbRoot))
        {
            Directory.Delete(failureDbRoot, recursive: true);
        }
    }

    [Test]
    public void Contracts_ThumbnailRequest_RoundTripsLegacyQueueObj()
    {
        QueueObj legacy = new()
        {
            Tabindex = 3,
            MovieId = 42,
            MovieFullPath = @"C:\Media\sample.mp4",
            Hash = "abc123",
            MovieSizeBytes = 123456,
            ThumbPanelPos = 7,
            ThumbTimePos = 9,
            Priority = ThumbnailQueuePriority.Preferred,
        };

        ThumbnailRequest request = ThumbnailRequest.FromLegacyQueueObj(legacy);

        Assert.That(request.TabIndex, Is.EqualTo(3));
        Assert.That(request.MovieId, Is.EqualTo(42));
        Assert.That(request.MovieFullPath, Is.EqualTo(@"C:\Media\sample.mp4"));
        Assert.That(request.Priority, Is.EqualTo(ThumbnailQueuePriority.Preferred));
        Assert.That(request.ToLegacyQueueObj().Hash, Is.EqualTo("abc123"));
    }

    [Test]
    public void Contracts_ThumbnailPathKeyHelper_NormalizesPathForCompare()
    {
        string actual = ThumbnailPathKeyHelper.NormalizePathForCompare(@"""C:/Media/Clip.mp4""");

        Assert.That(actual, Is.EqualTo(@"c:\media\clip.mp4"));
        Assert.That(ThumbnailPathKeyHelper.GetMainDbPathHash8(@"C:\Media\main.db"), Has.Length.EqualTo(8));
    }

    [Test]
    public void FailureDb_ResolveFailureDbPath_UsesConfiguredDirectoryAndSanitizesName()
    {
        string failureDbPath = ThumbnailFailureDbPathResolver.ResolveFailureDbPath(@"C:\Video\main:db?.wb");

        Assert.That(failureDbPath, Does.StartWith(failureDbRoot));
        Assert.That(Path.GetFileName(failureDbPath), Does.EndWith(".failure.imm"));
        Assert.That(Path.GetFileName(failureDbPath), Does.Not.Contain(":"));
        Assert.That(Path.GetFileName(failureDbPath), Does.Not.Contain("?"));
    }

    [Test]
    public void RescueWorker_ResolveEffectiveEngineOrderAfterPromotion_ReplacesOnlyWhenNeeded()
    {
        IReadOnlyList<string> currentOrder = ["ffmpeg1pass", "autogen"];
        RescueWorkerApplication.RescueExecutionPlan promotedPlan = new(
            RouteId: "route",
            SymptomClass: "symptom",
            DirectEngineOrder: ["opencv", "autogen"],
            UseRepairAfterDirect: true,
            RepairEngineOrder: ["ffmpeg1pass"]
        );

        IReadOnlyList<string> preservedOrder = RescueWorkerApplication.ResolveEffectiveEngineOrderAfterPromotion(
            currentOrder,
            promotedPlan,
            preserveProvidedEngineOrder: true
        );
        IReadOnlyList<string> replacedOrder = RescueWorkerApplication.ResolveEffectiveEngineOrderAfterPromotion(
            currentOrder,
            promotedPlan,
            preserveProvidedEngineOrder: false
        );

        Assert.That(preservedOrder, Is.EqualTo(currentOrder));
        Assert.That(replacedOrder, Is.EqualTo(promotedPlan.DirectEngineOrder));
    }
}
'@

    Write-TextFileNoBom (
        Join-Path $TargetRoot ".gitignore"
    ) @'
bin/
obj/
artifacts/
logs/
g.editorconfig
*.user
*.suo
'@

    Write-TextFileNoBom (
        Join-Path $TargetRoot "scripts\build_private_engine.ps1"
    ) @'
param(
    [string]$Configuration = "Debug"
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot "..")).Path
$solutionPath = Join-Path $repoRoot "IndigoMovieEngine.slnx"
$testsProjectPath = Join-Path $repoRoot "tests\IndigoMovieManager.Tests\IndigoMovieManager.Tests.csproj"

dotnet build $solutionPath -c $Configuration
dotnet test $testsProjectPath -c $Configuration -p:Platform=x64 --no-build
'@

    Write-TextFileNoBom (
        Join-Path $TargetRoot "scripts\publish_private_engine.ps1"
    ) @'
param(
    [string]$Configuration = "Release",
    [string]$Runtime = "win-x64",
    [string]$VersionLabel = "",
    [switch]$SelfContained
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot "..")).Path
$packageScriptPath = Join-Path $repoRoot "scripts\create_rescue_worker_artifact_package.ps1"

if (-not (Test-Path -LiteralPath $packageScriptPath)) {
    throw "package script が見つかりません: $packageScriptPath"
}

# local publish でも CI と同じ完成物を作り、release asset と成果物の形を揃える。
& $packageScriptPath `
    -Configuration $Configuration `
    -Runtime $Runtime `
    -VersionLabel $VersionLabel `
    -SelfContained:$SelfContained.IsPresent

if ($LASTEXITCODE -ne 0) {
    throw "private engine publish package 作成に失敗しました。"
}
'@

    Write-TextFileNoBom (
        Join-Path $TargetRoot "scripts\create_rescue_worker_artifact_package.ps1"
    ) @'
[CmdletBinding()]
param(
    [string]$Configuration = "Release",
    [string]$Runtime = "win-x64",
    [string]$OutputRoot = "artifacts/rescue-worker",
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

function Write-Utf8NoBomFile {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Path,
        [Parameter(Mandatory = $true)]
        [string]$Content
    )

    $utf8NoBom = [System.Text.UTF8Encoding]::new($false)
    [System.IO.File]::WriteAllText($Path, $Content, $utf8NoBom)
}

$repoRoot = Split-Path -Parent $PSScriptRoot
$outputRootFullPath = [System.IO.Path]::GetFullPath((Join-Path $repoRoot $OutputRoot))
$versionLabelNormalized = Get-NormalizedLabel -Label $VersionLabel
$publishScriptPath = Join-Path $repoRoot "src\IndigoMovieManager.Thumbnail.RescueWorker\Publish-RescueWorkerArtifact.ps1"

if (-not (Test-Path -LiteralPath $publishScriptPath)) {
    throw "publish script が見つかりません: $publishScriptPath"
}

$publishDir = Join-Path $outputRootFullPath "publish\$Configuration-$Runtime"
$packageRoot = Join-Path $outputRootFullPath "package"

& $publishScriptPath `
    -Configuration $Configuration `
    -Runtime $Runtime `
    -OutputRoot $OutputRoot `
    -SelfContained:$SelfContained.IsPresent

if ($LASTEXITCODE -ne 0) {
    throw "worker publish script に失敗しました。"
}

if (-not (Test-Path -LiteralPath $publishDir)) {
    throw "publish directory が見つかりません: $publishDir"
}

$markerPath = Join-Path $publishDir "rescue-worker-artifact.json"
if (-not (Test-Path -LiteralPath $markerPath)) {
    throw "artifact marker が見つかりません: $markerPath"
}

$marker = Get-Content -LiteralPath $markerPath -Raw -Encoding utf8 | ConvertFrom-Json
$compatibilityVersion = "$($marker.compatibilityVersion)"
$compatibilityLabel = Get-NormalizedLabel -Label $compatibilityVersion
$packageIdentity =
    "IndigoMovieManager.Thumbnail.RescueWorker-$versionLabelNormalized-$Runtime-compat-$compatibilityLabel"
$packageDir = Join-Path $packageRoot $packageIdentity
$zipFilePath = Join-Path $outputRootFullPath "$packageIdentity.zip"

# 毎回同じ名前の package を作るため、同一 label の残骸は先に掃除する。
Get-ChildItem -Path $packageRoot -Directory -Filter "IndigoMovieManager.Thumbnail.RescueWorker-$versionLabelNormalized-$Runtime-compat-*" -ErrorAction SilentlyContinue |
    Remove-Item -Recurse -Force
Get-ChildItem -Path $outputRootFullPath -File -Filter "IndigoMovieManager.Thumbnail.RescueWorker-$versionLabelNormalized-$Runtime-compat-*.zip" -ErrorAction SilentlyContinue |
    Remove-Item -Force

if (Test-Path -LiteralPath $packageDir) {
    Remove-Item -LiteralPath $packageDir -Recurse -Force
}
if (Test-Path -LiteralPath $zipFilePath) {
    Remove-Item -LiteralPath $zipFilePath -Force
}

New-Item -ItemType Directory -Path $packageDir -Force | Out-Null
Copy-Item -Path (Join-Path $publishDir "*") -Destination $packageDir -Recurse -Force

$readme = @"
IndigoMovieEngine Rescue Worker Artifact
=======================================

- VersionLabel: $versionLabelNormalized
- Configuration: $Configuration
- Runtime: $Runtime
- SelfContained: $($SelfContained.IsPresent)
- CompatibilityVersion: $compatibilityVersion
- AssetFileName: $packageIdentity.zip

使い方
------
1. ZIP を展開する
2. 展開先の IndigoMovieManager.Thumbnail.RescueWorker.exe を起動元として使う
3. rescue-worker-artifact.json の compatibilityVersion が host 側期待値と一致することを確認する

注意
----
- SelfContained が False の場合は .NET 8 Desktop Runtime が必要
- GitHub Release asset と Actions artifact で同じ中身を追えるよう、marker 付き publish 出力をそのまま封入している
"@
Write-Utf8NoBomFile -Path (Join-Path $packageDir "README-artifact.txt") -Content $readme

$hashEntries = Get-ChildItem -LiteralPath $packageDir -File -Recurse |
    Where-Object { $_.Name -ne "SHA256SUMS.txt" } |
    Sort-Object FullName |
    ForEach-Object {
        $relativePath = [System.IO.Path]::GetRelativePath($packageDir, $_.FullName) -replace "\\", "/"
        $hash = Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256
        "$($hash.Hash)  $relativePath"
    }
Write-Utf8NoBomFile -Path (Join-Path $packageDir "SHA256SUMS.txt") -Content ($hashEntries -join [Environment]::NewLine)

Compress-Archive -Path (Join-Path $packageDir "*") -DestinationPath $zipFilePath -CompressionLevel Optimal -Force

Write-Host "Publish directory: $publishDir"
Write-Host "Package directory: $packageDir"
Write-Host "Zip file: $zipFilePath"
'@

    Write-TextFileNoBom (
        Join-Path $TargetRoot ".github\workflows\private-engine-build.yml"
    ) @'
name: private-engine-build

on:
  pull_request:
  push:
    branches:
      - main
      - master
  workflow_dispatch:

jobs:
  build:
    runs-on: windows-latest
    steps:
      - uses: actions/checkout@v4

      - uses: actions/setup-dotnet@v4
        with:
          dotnet-version: 10.0.x

      - name: Build and test private engine
        shell: pwsh
        run: ./scripts/build_private_engine.ps1 -Configuration Debug
'@

    Write-TextFileNoBom (
        Join-Path $TargetRoot ".github\workflows\private-engine-publish.yml"
    ) @'
name: private-engine-publish

on:
  workflow_dispatch:
  push:
    tags:
      - "v*"

jobs:
  publish-worker:
    runs-on: windows-latest
    permissions:
      contents: write
    steps:
      - uses: actions/checkout@v4

      - uses: actions/setup-dotnet@v4
        with:
          dotnet-version: 8.0.x

      - name: Build and test private engine
        shell: pwsh
        run: ./scripts/build_private_engine.ps1 -Configuration Release

      - name: Publish rescue worker artifact
        shell: pwsh
        run: ./src/IndigoMovieManager.Thumbnail.RescueWorker/Publish-RescueWorkerArtifact.ps1 -Configuration Release -Runtime win-x64

      - name: Create rescue worker artifact package
        shell: pwsh
        run: |
          $versionLabel = if ("${{ github.ref_type }}" -eq "tag") { "${{ github.ref_name }}" } else { "manual-${{ github.run_number }}" }
          ./scripts/create_rescue_worker_artifact_package.ps1 -Configuration Release -Runtime win-x64 -VersionLabel $versionLabel

      - name: Upload rescue worker publish output
        uses: actions/upload-artifact@v4
        with:
          name: rescue-worker-publish
          path: artifacts/rescue-worker/publish/Release-win-x64/**
          if-no-files-found: error

      - name: Upload rescue worker artifact package
        uses: actions/upload-artifact@v4
        with:
          name: rescue-worker-package
          path: artifacts/rescue-worker/*.zip
          if-no-files-found: error

      - name: Attach rescue worker package to GitHub Release
        if: startsWith(github.ref, 'refs/tags/')
        uses: softprops/action-gh-release@v2
        with:
          files: artifacts/rescue-worker/*.zip
          fail_on_unmatched_files: true
'@
}

function Sync-PrivateRepoSource {
    param(
        [string]$RepoRootValue,
        [string]$TargetRoot
    )

    $projectDirs = @(
        "src\IndigoMovieManager.Thumbnail.Contracts",
        "src\IndigoMovieManager.Thumbnail.Engine",
        "src\IndigoMovieManager.Thumbnail.FailureDb",
        "src\IndigoMovieManager.Thumbnail.RescueWorker"
    )

    foreach ($relative in $projectDirs) {
        $sourcePath = Join-Path $RepoRootValue $relative
        $targetPath = Join-Path $TargetRoot $relative
        Copy-TreeFiltered -SourceRoot $sourcePath -TargetRoot $targetPath
    }

    Copy-TextFileNoBom `
        -SourcePath (Join-Path $RepoRootValue "Directory.Build.props") `
        -TargetPath (Join-Path $TargetRoot "Directory.Build.props")

    $assetFiles = @(
        @{ Source = "Images\noFileBig.jpg"; Target = "Images\noFileBig.jpg" },
        @{ Source = "Images\noFileGrid.jpg"; Target = "Images\noFileGrid.jpg" },
        @{ Source = "Images\nofileList.jpg"; Target = "Images\nofileList.jpg" },
        @{ Source = "Images\noFileSmall.jpg"; Target = "Images\noFileSmall.jpg" },
        @{ Source = "tools\ffmpeg\LICENSE-ffmpeg-lgpl.txt"; Target = "tools\ffmpeg\LICENSE-ffmpeg-lgpl.txt" }
    )

    foreach ($item in $assetFiles) {
        $sourcePath = Join-Path $RepoRootValue $item.Source
        $targetPath = Join-Path $TargetRoot $item.Target
        Copy-FilePreservingKind -SourcePath $sourcePath -TargetPath $targetPath
    }

    $sharedToolSource = Join-Path $RepoRootValue "tools\ffmpeg-shared"
    $sharedToolTarget = Join-Path $TargetRoot "tools\ffmpeg-shared"
    if (Test-Path $sharedToolSource) {
        Copy-TreeFiltered -SourceRoot $sharedToolSource -TargetRoot $sharedToolTarget
    }
}

function Ensure-PrivateRepoSolution {
    param([string]$TargetRoot)

    $solutionPath = Join-Path $TargetRoot "IndigoMovieEngine.slnx"
    $projectPaths = @(
        "src\IndigoMovieManager.Thumbnail.Contracts\IndigoMovieManager.Thumbnail.Contracts.csproj",
        "src\IndigoMovieManager.Thumbnail.Engine\IndigoMovieManager.Thumbnail.Engine.csproj",
        "src\IndigoMovieManager.Thumbnail.FailureDb\IndigoMovieManager.Thumbnail.FailureDb.csproj",
        "src\IndigoMovieManager.Thumbnail.RescueWorker\IndigoMovieManager.Thumbnail.RescueWorker.csproj",
        "tests\IndigoMovieManager.Tests\IndigoMovieManager.Tests.csproj"
    )

    if ($DryRun) {
        if (-not (Test-Path $solutionPath)) {
            Write-Host "[dryrun] dotnet new sln -n IndigoMovieEngine"
        }

        foreach ($projectPath in $projectPaths) {
            Write-Host "[dryrun] dotnet sln IndigoMovieEngine.slnx add $projectPath"
        }

        return
    }

    Push-Location $TargetRoot
    try {
        if (-not (Test-Path $solutionPath)) {
            & dotnet new sln -n IndigoMovieEngine | Out-Null
            if ($LASTEXITCODE -ne 0) {
                throw "Private repo solution 作成に失敗しました: $solutionPath"
            }
        }

        $existingLines = & dotnet sln $solutionPath list 2>$null
        foreach ($projectPath in $projectPaths) {
            if ($existingLines -notcontains $projectPath) {
                & dotnet sln $solutionPath add $projectPath | Out-Null
                if ($LASTEXITCODE -ne 0) {
                    throw "Private repo solution への project 追加に失敗しました: $projectPath"
                }
            }
        }
    }
    finally {
        Pop-Location
    }
}

function Sync-PrivateRepoDocs {
    param(
        [string]$RepoRootValue,
        [string]$TargetRoot
    )

    $docMap = @(
        @{
            Source = "src\IndigoMovieManager.Thumbnail.RescueWorker\Docs\設計メモ_repo構成表_Public本体_PrivateEngine_2026-04-04.md"
            Target = "docs\設計メモ_repo構成表_Public本体_PrivateEngine_2026-04-04.md"
        },
        @{
            Source = "src\IndigoMovieManager.Thumbnail.RescueWorker\Docs\Implementation Plan_RescueWorker_v1契約_PrivateRepo前提_2026-04-04.md"
            Target = "docs\Implementation Plan_RescueWorker_v1契約_PrivateRepo前提_2026-04-04.md"
        },
        @{
            Source = "Thumbnail\Docs\Implementation Plan_workerとサムネイル作成エンジン外だし_2026-04-01.md"
            Target = "docs\Implementation Plan_workerとサムネイル作成エンジン外だし_2026-04-01.md"
        }
    )

    foreach ($item in $docMap) {
        $sourcePath = Join-Path $RepoRootValue $item.Source
        $targetPath = Join-Path $TargetRoot $item.Target
        Copy-TextFileNoBom -SourcePath $sourcePath -TargetPath $targetPath
    }
}

if ([string]::IsNullOrWhiteSpace($RepoRoot)) {
    $RepoRoot = Resolve-RepoRoot
}

$RepoRoot = [System.IO.Path]::GetFullPath($RepoRoot)
$PrivateRepoRoot = Resolve-PrivateRepoRoot -RepoRootValue $RepoRoot -PrivateRepoRootValue $PrivateRepoRoot

if (-not (Test-Path $RepoRoot)) {
    throw "RepoRoot が見つかりません: $RepoRoot"
}

if ($Mode -eq "Bootstrap") {
    Initialize-PrivateRepoLayout -TargetRoot $PrivateRepoRoot
    Sync-PrivateRepoDocs -RepoRootValue $RepoRoot -TargetRoot $PrivateRepoRoot
}
elseif ($Mode -eq "SyncDocs") {
    Ensure-Directory (Join-Path $PrivateRepoRoot "docs")
    Sync-PrivateRepoDocs -RepoRootValue $RepoRoot -TargetRoot $PrivateRepoRoot
}
else {
    Initialize-PrivateRepoLayout -TargetRoot $PrivateRepoRoot
    Sync-PrivateRepoDocs -RepoRootValue $RepoRoot -TargetRoot $PrivateRepoRoot
    Sync-PrivateRepoSource -RepoRootValue $RepoRoot -TargetRoot $PrivateRepoRoot
    Ensure-PrivateRepoSolution -TargetRoot $PrivateRepoRoot
}

Write-Host "[bootstrap] completed mode=$Mode repo=$RepoRoot privateRepo=$PrivateRepoRoot"
