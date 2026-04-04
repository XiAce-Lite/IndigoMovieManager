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

function Sync-PrivateRepoSeedFiles {
    param(
        [string]$RepoRootValue,
        [string]$TargetRoot
    )

    $seedRoot = Join-Path $RepoRootValue "scripts\private-engine-seed"
    $seedFiles = @(
        @{ Source = "scripts\build_private_engine.ps1"; Target = "scripts\build_private_engine.ps1" },
        @{ Source = "scripts\publish_private_engine.ps1"; Target = "scripts\publish_private_engine.ps1" },
        @{ Source = "scripts\create_rescue_worker_artifact_package.ps1"; Target = "scripts\create_rescue_worker_artifact_package.ps1" },
        @{ Source = ".github\workflows\private-engine-build.yml"; Target = ".github\workflows\private-engine-build.yml" },
        @{ Source = ".github\workflows\private-engine-publish.yml"; Target = ".github\workflows\private-engine-publish.yml" }
    )

    foreach ($item in $seedFiles) {
        $sourcePath = Join-Path $seedRoot $item.Source
        $targetPath = Join-Path $TargetRoot $item.Target

        # Private repo に seed する正本は、Public 側 root script ではなく専用 seed フォルダから取る。
        Copy-FilePreservingKind -SourcePath $sourcePath -TargetPath $targetPath
    }
}

function Initialize-PrivateRepoLayout {
    param(
        [string]$RepoRootValue,
        [string]$TargetRoot
    )

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
2026-04-05 時点で `RescueWorkerApplicationTests` もこちらへ寄せ、worker 実装直結 test の正本を Private repo 側へ移した。
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

    # seed 正本は別ファイルに置き、bootstrap はコピー役だけを担う。
    Sync-PrivateRepoSeedFiles -RepoRootValue $RepoRootValue -TargetRoot $TargetRoot
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

# この script は Private repo 初期化と再同期の橋渡し専用であり、通常の release 導線では使わない。
Write-Warning "bootstrap_private_engine_repo.ps1 は移行ブリッジです。通常運用の release / worker 配布には使わず、Private repo の初期化・再同期時だけ使ってください。"

if ($Mode -eq "Bootstrap") {
    Initialize-PrivateRepoLayout -RepoRootValue $RepoRoot -TargetRoot $PrivateRepoRoot
    Sync-PrivateRepoDocs -RepoRootValue $RepoRoot -TargetRoot $PrivateRepoRoot
}
elseif ($Mode -eq "SyncDocs") {
    Ensure-Directory (Join-Path $PrivateRepoRoot "docs")
    Sync-PrivateRepoDocs -RepoRootValue $RepoRoot -TargetRoot $PrivateRepoRoot
}
else {
    Initialize-PrivateRepoLayout -RepoRootValue $RepoRoot -TargetRoot $PrivateRepoRoot
    Sync-PrivateRepoDocs -RepoRootValue $RepoRoot -TargetRoot $PrivateRepoRoot
    Sync-PrivateRepoSource -RepoRootValue $RepoRoot -TargetRoot $PrivateRepoRoot
    Ensure-PrivateRepoSolution -TargetRoot $PrivateRepoRoot
}

Write-Host "[bootstrap] completed mode=$Mode repo=$RepoRoot privateRepo=$PrivateRepoRoot"
