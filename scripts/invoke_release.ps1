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
    [string]$AuthorName = "T-Hamada0101",
    [string]$AuthorEmail = "T-Hamada0101@users.noreply.github.com",
    [switch]$IncludeWorkerArtifactPackage,
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

    $assemblyName = Get-MsBuildPropertyValue -ProjectPath $ProjectPath -PropertyName "AssemblyName"
    if ([string]::IsNullOrWhiteSpace($assemblyName)) {
        $assemblyName = [System.IO.Path]::GetFileNameWithoutExtension($ProjectPath)
    }

    $packageDir = Join-Path $RepoRoot (Join-Path $OutputRoot "package\$assemblyName-$VersionLabel-$Runtime")
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

    Write-Step "worker lock source: $sourceType"
    Write-Step "worker lock version: $version"
    Write-Step "worker lock asset: $assetFileName"
    Write-Step "worker lock compatibilityVersion: $compatibilityVersion"
    Write-Step "worker lock sha256: $workerExecutableSha256"
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

    if ($DryRun) {
        Write-Step "DryRun: version を更新します: $projectFullPath"
    } else {
        $originalProjectContent = Get-Content -LiteralPath $projectFullPath -Raw -Encoding utf8
        Set-ProjectVersion -Path $projectFullPath -NewVersion $versionNormalized
        $projectVersionWritten = $true
    }

    Invoke-Tool `
        -FilePath "dotnet" `
        -Arguments @("msbuild", $solutionFullPath, "/p:Configuration=$Configuration", "/p:Platform=x64") `
        -Description "Release build"

    $createReleasePackageScript = Join-Path $repoRoot "scripts\create_github_release_package.ps1"
    Invoke-Tool `
        -FilePath "pwsh" `
        -Arguments @(
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
            $tagName
        ) `
        -Description "app release package 作成"

    if (-not $DryRun) {
        Show-WorkerLockSummary `
            -RepoRoot $repoRoot `
            -ProjectPath $projectFullPath `
            -OutputRoot "artifacts/github-release" `
            -VersionLabel $tagName `
            -Runtime $Runtime
    }

    if ($IncludeWorkerArtifactPackage) {
        $createWorkerPackageScript = Join-Path $repoRoot "scripts\create_rescue_worker_artifact_package.ps1"
        Invoke-Tool `
            -FilePath "pwsh" `
            -Arguments @(
                "-NoLogo",
                "-NoProfile",
                "-File",
                $createWorkerPackageScript,
                "-Configuration",
                $Configuration,
                "-Runtime",
                $Runtime,
                "-OutputRoot",
                "artifacts/rescue-worker",
                "-VersionLabel",
                $tagName
            ) `
            -Description "worker artifact package 作成"
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
    if ($IncludeWorkerArtifactPackage) {
        Write-Host "- ローカル生成した worker artifact ZIP"
    } else {
        Write-Host "- 必要なら rescue-worker-artifact を手動実行して worker 単体確認"
    }
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
