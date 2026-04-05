[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string]$Version,
    [string]$ProjectPath = "IndigoMovieManager.csproj",
    [string]$SolutionPath = "IndigoMovieManager.sln",
    [string]$Configuration = "Release",
    [string]$Runtime = "win-x64",
    [string]$Remote = "origin",
    [string]$CommitMessage = "",
    [string]$PreparedWorkerPublishDir = "artifacts/rescue-worker/publish/Release-win-x64",
    [string]$PreparedPrivateEnginePackageDir = "artifacts/private-engine-packages/Release",
    [string]$AuthorName = "T-Hamada0101",
    [string]$AuthorEmail = "T-Hamada0101@users.noreply.github.com",
    [switch]$SkipBranchPush,
    [switch]$SkipTagPush,
    [switch]$DryRun,
    [switch]$AllowDirty
)

$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest

function Write-Step {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Message
    )

    Write-Host "[release] $Message" -ForegroundColor Cyan
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

function Invoke-GitCapture {
    param(
        [Parameter(Mandatory = $true)]
        [string[]]$Arguments,
        [switch]$AllowFailure
    )

    $output = & git @Arguments 2>&1
    $exitCode = $LASTEXITCODE
    $text = ($output | Out-String).Trim()

    if (-not $AllowFailure -and $exitCode -ne 0) {
        throw "git $($Arguments -join ' ') に失敗しました。`n$text"
    }

    return @{
        ExitCode = $exitCode
        Output = $text
    }
}

function Invoke-Git {
    param(
        [Parameter(Mandatory = $true)]
        [string[]]$Arguments
    )

    if ($DryRun) {
        Write-Step "DryRun: git $($Arguments -join ' ')"
        return
    }

    $result = Invoke-GitCapture -Arguments $Arguments
    if (-not [string]::IsNullOrWhiteSpace($result.Output)) {
        Write-Host $result.Output
    }
}

function Invoke-Tool {
    param(
        [Parameter(Mandatory = $true)]
        [string]$FilePath,
        [Parameter(Mandatory = $true)]
        [string[]]$Arguments,
        [Parameter(Mandatory = $true)]
        [string]$Description
    )

    if ($DryRun) {
        Write-Step "DryRun: $Description"
        Write-Host "$FilePath $($Arguments -join ' ')"
        return
    }

    Write-Step $Description
    & $FilePath @Arguments
    if ($LASTEXITCODE -ne 0) {
        throw "$Description に失敗しました。"
    }
}

function Resolve-PreparedWorkerPublishDir {
    param(
        [Parameter(Mandatory = $true)]
        [string]$RepoRoot,
        [Parameter(Mandatory = $true)]
        [string]$PreparedWorkerPublishDir
    )

    $relativePath = $PreparedWorkerPublishDir.Trim()
    if ([string]::IsNullOrWhiteSpace($relativePath)) {
        throw "PreparedWorkerPublishDir が空です。Private repo の publish artifact を同期したパスを指定してください。"
    }

    $fullPath = [System.IO.Path]::GetFullPath($relativePath, $RepoRoot)
    if (-not (Test-Path -LiteralPath $fullPath)) {
        throw "prepared worker publish directory が見つかりません: $fullPath`nscripts/sync_private_engine_worker_artifact.ps1 で同期するか、PreparedWorkerPublishDir を明示してください。"
    }

    return [pscustomobject]@{
        RelativePath = $relativePath
        FullPath = $fullPath
    }
}

function Resolve-PreparedPrivateEnginePackageDir {
    param(
        [Parameter(Mandatory = $true)]
        [string]$RepoRoot,
        [Parameter(Mandatory = $true)]
        [string]$PreparedPrivateEnginePackageDir
    )

    $relativePath = $PreparedPrivateEnginePackageDir.Trim()
    if ([string]::IsNullOrWhiteSpace($relativePath)) {
        throw "PreparedPrivateEnginePackageDir が空です。Private repo の package artifact を同期したパスを指定してください。"
    }

    $fullPath = [System.IO.Path]::GetFullPath($relativePath, $RepoRoot)
    if (-not (Test-Path -LiteralPath $fullPath)) {
        throw "prepared private engine package directory が見つかりません: $fullPath`nscripts/sync_private_engine_packages.ps1 で同期するか、PreparedPrivateEnginePackageDir を明示してください。"
    }

    return [pscustomobject]@{
        RelativePath = $relativePath
        FullPath = $fullPath
    }
}

function Resolve-PrivateEnginePackageVersionFromPreparedDir {
    param(
        [Parameter(Mandatory = $true)]
        [string]$PreparedPackageDir
    )

    $metadataPath = Join-Path $PreparedPackageDir "private-engine-packages-source.json"
    if (Test-Path -LiteralPath $metadataPath) {
        $metadata = Get-Content -LiteralPath $metadataPath -Raw -Encoding utf8 | ConvertFrom-Json
        $metadataVersion = "$($metadata.packageVersion)".Trim()
        if (-not [string]::IsNullOrWhiteSpace($metadataVersion)) {
            return $metadataVersion
        }
    }

    $packageIds = @(
        "IndigoMovieEngine.Thumbnail.Contracts",
        "IndigoMovieEngine.Thumbnail.Engine",
        "IndigoMovieEngine.Thumbnail.FailureDb"
    )
    $resolvedVersions = @()

    foreach ($packageId in $packageIds) {
        $pattern = '^' + [regex]::Escape($packageId) + '\.(.+)\.nupkg$'
        $matches = @(
            Get-ChildItem -Path $PreparedPackageDir -File -Filter "$packageId.*.nupkg" -ErrorAction SilentlyContinue |
                Where-Object { $_.Name -match $pattern } |
                ForEach-Object { $Matches[1] }
        )

        if ($matches.Count -ne 1) {
            throw "private engine package version を一意に解決できません: $packageId -> $($matches -join ', ')"
        }

        $resolvedVersions += $matches[0]
    }

    $uniqueVersions = @($resolvedVersions | Sort-Object -Unique)
    if ($uniqueVersions.Count -ne 1) {
        throw "private engine package version が揃っていません: $($uniqueVersions -join ', ')"
    }

    return $uniqueVersions[0]
}

function Set-ProjectVersion {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Path,
        [Parameter(Mandatory = $true)]
        [string]$NewVersion
    )

    $content = Get-Content -LiteralPath $Path -Raw -Encoding utf8
    $updated = $content
    $updated = [regex]::new('<Version>[^<]+</Version>').Replace($updated, "<Version>$NewVersion</Version>", 1)
    $updated = [regex]::new('<FileVersion>[^<]+</FileVersion>').Replace($updated, "<FileVersion>$NewVersion</FileVersion>", 1)
    $updated = [regex]::new('<AssemblyVersion>[^<]+</AssemblyVersion>').Replace($updated, "<AssemblyVersion>$NewVersion</AssemblyVersion>", 1)

    if ($updated -eq $content) {
        throw "version 更新対象が見つかりませんでした: $Path"
    }

    Write-Utf8NoBomFile -Path $Path -Content $updated
}

function Get-CurrentProjectVersion {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Path
    )

    $content = Get-Content -LiteralPath $Path -Raw -Encoding utf8
    $match = [regex]::Match($content, '<Version>([^<]+)</Version>')
    if (-not $match.Success) {
        throw "現在 version を読み取れませんでした: $Path"
    }

    return $match.Groups[1].Value.Trim()
}

function Get-MsBuildPropertyValue {
    param(
        [Parameter(Mandatory = $true)]
        [string]$ProjectPath,
        [Parameter(Mandatory = $true)]
        [string]$PropertyName
    )

    $output = & dotnet msbuild $ProjectPath -nologo "-getProperty:$PropertyName"
    if ($LASTEXITCODE -ne 0) {
        throw "MSBuild プロパティの取得に失敗しました: $PropertyName"
    }

    return ($output | Select-Object -Last 1).Trim()
}

function Normalize-Version {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Value
    )

    $normalized = $Value.Trim()
    if ($normalized.StartsWith("v", [System.StringComparison]::OrdinalIgnoreCase)) {
        $normalized = $normalized.Substring(1)
    }

    if ($normalized -notmatch '^\d+\.\d+\.\d+\.\d+$') {
        throw "Version は 1.0.3.2 のような 4 要素で指定してください。入力値: $Value"
    }

    return $normalized
}

function Assert-CleanWorktree {
    $status = Invoke-GitCapture -Arguments @("status", "--porcelain", "--untracked-files=all")
    if (-not [string]::IsNullOrWhiteSpace($status.Output)) {
        throw "作業ツリーが dirty です。release helper は意図しない差分混入を避けるため clean worktree を要求します。`n$status.Output"
    }
}

function Assert-IndexClean {
    $status = Invoke-GitCapture -Arguments @("diff", "--cached", "--name-only")
    if (-not [string]::IsNullOrWhiteSpace($status.Output)) {
        throw "index に staged 変更があります。AllowDirty 時でも release commit 混入を避けるため staged 変更は空にしてください。`n$status.Output"
    }
}

function Assert-PathUnmodified {
    param(
        [Parameter(Mandatory = $true)]
        [string]$PathSpec
    )

    $status = Invoke-GitCapture -Arguments @("status", "--porcelain", "--", $PathSpec)
    if (-not [string]::IsNullOrWhiteSpace($status.Output)) {
        throw "release helper が触る対象ファイルに既存差分があります。先に整理してください。`n$status.Output"
    }
}

function Get-WorkerLockSummaryData {
    param(
        [Parameter(Mandatory = $true)]
        [string]$RepoRoot,
        [Parameter(Mandatory = $true)]
        [string]$ProjectPath,
        [Parameter(Mandatory = $true)]
        [string]$OutputRoot,
        [Parameter(Mandatory = $true)]
        [string]$VersionLabel,
        [Parameter(Mandatory = $true)]
        [string]$Runtime
    )

    $assemblyName = Get-MsBuildPropertyValue -ProjectPath $ProjectPath -PropertyName "AssemblyName"
    if ([string]::IsNullOrWhiteSpace($assemblyName)) {
        $assemblyName = [System.IO.Path]::GetFileNameWithoutExtension($ProjectPath)
    }

    $outputRootFullPath = Join-Path $RepoRoot $OutputRoot
    $packageDir = Join-Path $outputRootFullPath "package\$assemblyName-$VersionLabel-$Runtime"
    $lockFilePath = Join-Path $packageDir "rescue-worker.lock.json"
    if (-not (Test-Path -LiteralPath $lockFilePath)) {
        throw "worker lock file が見つかりません: $lockFilePath"
    }

    $lock = Get-Content -LiteralPath $lockFilePath -Raw -Encoding utf8 | ConvertFrom-Json
    if ($null -eq $lock.workerArtifact) {
        throw "worker lock file に workerArtifact がありません: $lockFilePath"
    }

    $workerArtifact = $lock.workerArtifact
    $sourceType = "$($workerArtifact.sourceType)".Trim()
    $version = "$($workerArtifact.version)".Trim()
    $assetFileName = "$($workerArtifact.assetFileName)".Trim()
    $compatibilityVersion = "$($workerArtifact.compatibilityVersion)".Trim()
    $workerExecutableSha256 = "$($workerArtifact.workerExecutableSha256)".Trim()

    return [ordered]@{
        SourceType = $sourceType
        Version = $version
        AssetFileName = $assetFileName
        CompatibilityVersion = $compatibilityVersion
        WorkerExecutableSha256 = $workerExecutableSha256
        PackageDir = $packageDir
        LockFilePath = $lockFilePath
        OutputRootFullPath = $outputRootFullPath
        PackageRelativePath = [System.IO.Path]::GetRelativePath($outputRootFullPath, $packageDir).Replace("\", "/")
        LockFileRelativePath = [System.IO.Path]::GetRelativePath($outputRootFullPath, $lockFilePath).Replace("\", "/")
    }
}

function Show-WorkerLockSummary {
    param(
        [Parameter(Mandatory = $true)]
        [string]$RepoRoot,
        [Parameter(Mandatory = $true)]
        [string]$ProjectPath,
        [Parameter(Mandatory = $true)]
        [string]$OutputRoot,
        [Parameter(Mandatory = $true)]
        [string]$VersionLabel,
        [Parameter(Mandatory = $true)]
        [string]$Runtime
    )

    $summary = Get-WorkerLockSummaryData `
        -RepoRoot $RepoRoot `
        -ProjectPath $ProjectPath `
        -OutputRoot $OutputRoot `
        -VersionLabel $VersionLabel `
        -Runtime $Runtime

    Write-Step "worker lock source: $($summary.SourceType)"
    Write-Step "worker lock version: $($summary.Version)"
    Write-Step "worker lock asset: $($summary.AssetFileName)"
    Write-Step "worker lock compatibilityVersion: $($summary.CompatibilityVersion)"
    Write-Step "worker lock sha256: $($summary.WorkerExecutableSha256)"

    return $summary
}

function Write-WorkerLockReleaseSummary {
    param(
        [Parameter(Mandatory = $true)]
        [System.Collections.IDictionary]$Summary,
        [Parameter(Mandatory = $true)]
        [string]$VersionLabel,
        [Parameter(Mandatory = $true)]
        [string]$Runtime
    )

    $summaryFileName = "release-worker-lock-summary-$VersionLabel-$Runtime.md"
    $summaryFilePath = Join-Path $Summary.OutputRootFullPath $summaryFileName
    $releaseBodySnippet = @"
### Bundled Rescue Worker

- Source: ``$($Summary.SourceType)``
- Version: ``$($Summary.Version)``
- Artifact: ``$($Summary.AssetFileName)``
- CompatibilityVersion: ``$($Summary.CompatibilityVersion)``
- WorkerExe SHA256: ``$($Summary.WorkerExecutableSha256)``
"@
    $summaryContent = @"
# Rescue Worker Lock Summary

このファイルは、GitHub Release 本文へ bundled rescue worker の pin 情報を転記するための summary である。

## GitHub Release 本文へ貼るブロック

~~~md
$releaseBodySnippet
~~~

## ローカル確認用

- Package: ``$($Summary.PackageRelativePath)``
- LockFile: ``$($Summary.LockFileRelativePath)``
"@

    # GitHub Release 本文へそのまま貼りやすい最小形で、release 出力直下にも要約を残す。
    Write-Utf8NoBomFile -Path $summaryFilePath -Content $summaryContent
    Write-Step "worker lock summary file: $summaryFilePath"
}

$repoRoot = Split-Path -Parent $PSScriptRoot
$projectFullPath = [System.IO.Path]::GetFullPath((Join-Path $repoRoot $ProjectPath))
$solutionFullPath = [System.IO.Path]::GetFullPath((Join-Path $repoRoot $SolutionPath))
$projectGitPath = [System.IO.Path]::GetRelativePath($repoRoot, $projectFullPath).Replace("\", "/")
$versionNormalized = Normalize-Version -Value $Version
$tagName = "v$versionNormalized"
$effectiveCommitMessage = if ([string]::IsNullOrWhiteSpace($CommitMessage)) { "リリース $tagName" } else { $CommitMessage }
$originalProjectContent = ""
$projectVersionWritten = $false
$releaseCommitCreated = $false

if ($PSVersionTable.PSVersion.Major -lt 7) {
    throw "PowerShell 7 以上で実行してください。現在: $($PSVersionTable.PSVersion)"
}

if (-not (Test-Path -LiteralPath $projectFullPath)) {
    throw "プロジェクトが見つかりません: $projectFullPath"
}

if (-not (Test-Path -LiteralPath $solutionFullPath)) {
    throw "ソリューションが見つかりません: $solutionFullPath"
}

Push-Location $repoRoot
try {
    $preparedWorkerPublish = Resolve-PreparedWorkerPublishDir `
        -RepoRoot $repoRoot `
        -PreparedWorkerPublishDir $PreparedWorkerPublishDir
    $preparedPrivateEnginePackages = Resolve-PreparedPrivateEnginePackageDir `
        -RepoRoot $repoRoot `
        -PreparedPrivateEnginePackageDir $PreparedPrivateEnginePackageDir
    $privateEnginePackageVersion = Resolve-PrivateEnginePackageVersionFromPreparedDir `
        -PreparedPackageDir $preparedPrivateEnginePackages.FullPath

    $branchName = (Invoke-GitCapture -Arguments @("branch", "--show-current")).Output
    if ([string]::IsNullOrWhiteSpace($branchName)) {
        throw "detached HEAD では実行できません。branch 上で実行してください。"
    }

    if (-not $AllowDirty) {
        Assert-CleanWorktree
    } else {
        Write-Step "AllowDirty 指定のため clean worktree チェックを省略します。"
        Assert-IndexClean
        Assert-PathUnmodified -PathSpec $projectGitPath
    }

    $currentVersion = Get-CurrentProjectVersion -Path $projectFullPath
    if ($currentVersion -eq $versionNormalized) {
        throw "現在 version と同じ値です。新しい version を指定してください: $currentVersion"
    }

    $localTag = Invoke-GitCapture -Arguments @("rev-parse", "-q", "--verify", "refs/tags/$tagName") -AllowFailure
    if ($localTag.ExitCode -eq 0) {
        throw "同じ tag が既にローカルに存在します: $tagName"
    }

    if ($DryRun) {
        Write-Step "DryRun: remote tag 重複確認は省略します。"
    } else {
        $remoteTag = Invoke-GitCapture -Arguments @("ls-remote", "--tags", $Remote, $tagName) -AllowFailure
        if (-not [string]::IsNullOrWhiteSpace($remoteTag.Output)) {
            throw "同じ tag が既に remote に存在します: $tagName"
        }
    }

    Write-Step "release 対象 branch: $branchName"
    Write-Step "version: $currentVersion -> $versionNormalized"
    Write-Step "tag: $tagName"
    Write-Step "private engine package source: $($preparedPrivateEnginePackages.FullPath)"
    Write-Step "private engine package version: $privateEnginePackageVersion"

    if ($DryRun) {
        Write-Step "DryRun: version を更新します: $projectFullPath"
    } else {
        $originalProjectContent = Get-Content -LiteralPath $projectFullPath -Raw -Encoding utf8
        Set-ProjectVersion -Path $projectFullPath -NewVersion $versionNormalized
        $projectVersionWritten = $true
    }

    # Public 側の正式入口は app 配布専用とし、worker は同期済み artifact を消費する。
    $releaseBuildTargetPath = $projectFullPath
    $releaseBuildDescription = "Release build (app project only / private packages + prepared worker artifact mode)"

    Invoke-Tool `
        -FilePath "dotnet" `
        -Arguments @(
            "msbuild",
            $releaseBuildTargetPath,
            "/p:Configuration=$Configuration",
            "/p:Platform=x64",
            "/p:ImmUsePrivateEnginePackages=true",
            "/p:ImmPrivateEnginePackageVersion=$privateEnginePackageVersion",
            "/p:ImmPrivateEnginePackageSource=$($preparedPrivateEnginePackages.FullPath)"
        ) `
        -Description $releaseBuildDescription

    $createReleasePackageScript = Join-Path $repoRoot "scripts\create_github_release_package.ps1"
    $createReleasePackageArguments = @(
        "-NoLogo",
        "-NoProfile",
        "-File",
        $createReleasePackageScript,
        "-Configuration",
        $Configuration,
        "-Runtime",
        $Runtime,
        "-OutputRoot",
        "artifacts/github-release",
        "-VersionLabel",
        $tagName,
        "-PreparedWorkerPublishDir",
        $preparedWorkerPublish.RelativePath,
        "-PreparedPrivateEnginePackageDir",
        $preparedPrivateEnginePackages.RelativePath
    )

    Invoke-Tool `
        -FilePath "pwsh" `
        -Arguments $createReleasePackageArguments `
        -Description "app release package 作成"

    if (-not $DryRun) {
        $workerLockSummary = Show-WorkerLockSummary `
            -RepoRoot $repoRoot `
            -ProjectPath $projectFullPath `
            -OutputRoot "artifacts/github-release" `
            -VersionLabel $tagName `
            -Runtime $Runtime
        Write-WorkerLockReleaseSummary `
            -Summary $workerLockSummary `
            -VersionLabel $tagName `
            -Runtime $Runtime
    }

    Invoke-GitCapture -Arguments @("diff", "--check", "--", $projectGitPath) | Out-Null

    if ($DryRun) {
        Write-Step "DryRun: git add -- $projectGitPath"
        Write-Step "DryRun: git commit -m $effectiveCommitMessage"
    } else {
        Invoke-Git -Arguments @("add", "--", $projectGitPath)
        $commitArguments = @(
            "-c", "user.name=$AuthorName",
            "-c", "user.email=$AuthorEmail",
            "commit",
            "-m", $effectiveCommitMessage
        )
        Invoke-Git -Arguments $commitArguments
        $releaseCommitCreated = $true
    }

    Invoke-Git -Arguments @("tag", "-a", $tagName, "-m", $tagName)

    if (-not $SkipBranchPush -and -not $SkipTagPush) {
        Invoke-Git -Arguments @("push", "--atomic", $Remote, "HEAD", $tagName)
    } elseif (-not $SkipBranchPush) {
        Invoke-Git -Arguments @("push", $Remote, "HEAD")
        Write-Step "tag push は SkipTagPush 指定で省略します。"
    } elseif (-not $SkipTagPush) {
        Invoke-Git -Arguments @("push", $Remote, $tagName)
    } else {
        Write-Step "branch push は SkipBranchPush 指定で省略します。"
        Write-Step "tag push は SkipTagPush 指定で省略します。"
    }

    Write-Step "release helper が完了しました。"
    Write-Host ""
    Write-Host "次の確認:" -ForegroundColor Green
    Write-Host "- GitHub Actions の github-release-package"
    Write-Host "- GitHub Release の app ZIP"
    Write-Host "- 必要なら Private repo の private-engine-publish を手動実行して worker 単体確認"
}
catch {
    if (-not $DryRun -and $projectVersionWritten -and -not $releaseCommitCreated) {
        Write-Step "途中失敗のため version 更新を巻き戻します。"
        try {
            Invoke-GitCapture -Arguments @("restore", "--source=HEAD", "--staged", "--worktree", "--", $projectGitPath) | Out-Null
        } catch {
            if (-not [string]::IsNullOrEmpty($originalProjectContent)) {
                Write-Utf8NoBomFile -Path $projectFullPath -Content $originalProjectContent
            }
        }
    }

    throw
}
finally {
    Pop-Location
}
