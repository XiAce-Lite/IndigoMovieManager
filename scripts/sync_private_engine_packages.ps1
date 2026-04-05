[CmdletBinding()]
param(
    [string]$PrivateRepoFullName = "T-Hamada0101/IndigoMovieEngine",
    [string]$WorkflowFileName = "private-engine-publish.yml",
    [string]$ArtifactName = "private-engine-packages",
    [string]$Branch = "main",
    [string]$ReleaseTag = "",
    [string]$DestinationPath = "artifacts/private-engine-packages/Release",
    [string]$GitHubToken = "",
    [long]$RunId = 0
)

$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest

function Get-RepoRoot {
    return (Resolve-Path (Join-Path $PSScriptRoot "..")).Path
}

function Get-GitHubToken {
    param([string]$ExplicitToken)

    if (-not [string]::IsNullOrWhiteSpace($ExplicitToken)) {
        return $ExplicitToken.Trim()
    }

    if (-not [string]::IsNullOrWhiteSpace($env:IMM_PRIVATE_ENGINE_TOKEN)) {
        return $env:IMM_PRIVATE_ENGINE_TOKEN.Trim()
    }

    if (-not [string]::IsNullOrWhiteSpace($env:GH_TOKEN)) {
        return $env:GH_TOKEN.Trim()
    }

    if (-not [string]::IsNullOrWhiteSpace($env:GITHUB_TOKEN)) {
        return $env:GITHUB_TOKEN.Trim()
    }

    $credentialInput = "protocol=https`nhost=github.com`n`n"
    $credentialOutput = $credentialInput | git credential fill 2>$null
    foreach ($line in ($credentialOutput -split "`r?`n")) {
        if ($line -like "password=*") {
            return $line.Substring("password=".Length)
        }
    }

    throw "GitHub token を取得できませんでした。"
}

function Invoke-GitHubJson {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Uri,
        [Parameter(Mandatory = $true)]
        [string]$Token
    )

    $headers = @{
        Authorization = "Bearer $Token"
        Accept = "application/vnd.github+json"
        "X-GitHub-Api-Version" = "2022-11-28"
    }
    return Invoke-RestMethod -Method Get -Uri $Uri -Headers $headers
}

function Download-GitHubArtifactZip {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Uri,
        [Parameter(Mandatory = $true)]
        [string]$Token,
        [Parameter(Mandatory = $true)]
        [string]$OutFilePath
    )

    $headers = @{
        Authorization = "Bearer $Token"
        Accept = "application/vnd.github+json"
        "X-GitHub-Api-Version" = "2022-11-28"
    }
    Invoke-WebRequest -Uri $Uri -Headers $headers -OutFile $OutFilePath | Out-Null
}

function Download-GitHubReleaseAsset {
    param(
        [Parameter(Mandatory = $true)]
        [string]$PrivateRepo,
        [Parameter(Mandatory = $true)]
        [long]$AssetId,
        [Parameter(Mandatory = $true)]
        [string]$Token,
        [Parameter(Mandatory = $true)]
        [string]$OutFilePath
    )

    $headers = @{
        Authorization = "Bearer $Token"
        Accept = "application/octet-stream"
        "X-GitHub-Api-Version" = "2022-11-28"
    }
    $uri = "https://api.github.com/repos/$PrivateRepo/releases/assets/$AssetId"
    Invoke-WebRequest -Uri $uri -Headers $headers -OutFile $OutFilePath | Out-Null
}

function Get-WorkflowRun {
    param(
        [Parameter(Mandatory = $true)]
        [string]$PrivateRepo,
        [Parameter(Mandatory = $true)]
        [long]$WorkflowRunId,
        [Parameter(Mandatory = $true)]
        [string]$Token
    )

    $uri = "https://api.github.com/repos/$PrivateRepo/actions/runs/$WorkflowRunId"
    return Invoke-GitHubJson -Uri $uri -Token $Token
}

function Find-LatestSuccessfulWorkflowRun {
    param(
        [Parameter(Mandatory = $true)]
        [string]$PrivateRepo,
        [Parameter(Mandatory = $true)]
        [string]$WorkflowName,
        [Parameter(Mandatory = $true)]
        [string]$BranchName,
        [Parameter(Mandatory = $true)]
        [string]$Token
    )

    $uri = "https://api.github.com/repos/$PrivateRepo/actions/workflows/$WorkflowName/runs?branch=$BranchName&per_page=20"
    $response = Invoke-GitHubJson -Uri $uri -Token $Token
    foreach ($run in $response.workflow_runs) {
        if ($run.status -eq "completed" -and $run.conclusion -eq "success") {
            return $run
        }
    }

    throw "成功済み workflow run が見つかりません: repo=$PrivateRepo workflow=$WorkflowName branch=$BranchName"
}

function Find-RunArtifact {
    param(
        [Parameter(Mandatory = $true)]
        [string]$PrivateRepo,
        [Parameter(Mandatory = $true)]
        [long]$WorkflowRunId,
        [Parameter(Mandatory = $true)]
        [string]$ExpectedArtifactName,
        [Parameter(Mandatory = $true)]
        [string]$Token
    )

    $uri = "https://api.github.com/repos/$PrivateRepo/actions/runs/$WorkflowRunId/artifacts"
    $response = Invoke-GitHubJson -Uri $uri -Token $Token
    foreach ($artifact in $response.artifacts) {
        if ($artifact.name -eq $ExpectedArtifactName) {
            return $artifact
        }
    }

    throw "artifact が見つかりません: runId=$WorkflowRunId name=$ExpectedArtifactName"
}

function Get-ReleaseByTag {
    param(
        [Parameter(Mandatory = $true)]
        [string]$PrivateRepo,
        [Parameter(Mandatory = $true)]
        [string]$TagName,
        [Parameter(Mandatory = $true)]
        [string]$Token
    )

    $escapedTagName = [uri]::EscapeDataString($TagName)
    $uri = "https://api.github.com/repos/$PrivateRepo/releases/tags/$escapedTagName"
    return Invoke-GitHubJson -Uri $uri -Token $Token
}

function Get-NormalizedReleaseVersion {
    param([string]$ReleaseTag)

    $trimmed = $ReleaseTag.Trim()
    if ($trimmed.Length -ge 2 -and ($trimmed[0] -eq 'v' -or $trimmed[0] -eq 'V') -and [char]::IsDigit($trimmed[1])) {
        return $trimmed.Substring(1)
    }

    return $trimmed
}

function Find-ReleaseAssets {
    param(
        [Parameter(Mandatory = $true)]
        [psobject]$Release,
        [Parameter(Mandatory = $true)]
        [string]$ReleaseTag
    )

    $normalizedVersion = Get-NormalizedReleaseVersion -ReleaseTag $ReleaseTag
    $packageIds = @(
        "IndigoMovieEngine.Thumbnail.Contracts",
        "IndigoMovieEngine.Thumbnail.Engine",
        "IndigoMovieEngine.Thumbnail.FailureDb"
    )

    $assets = @()
    foreach ($packageId in $packageIds) {
        $expectedName = "$packageId.$normalizedVersion.nupkg"
        $matchedAsset = @($Release.assets | Where-Object { "$($_.name)" -eq $expectedName })
        if ($matchedAsset.Count -ne 1) {
            throw "release asset が一意に見つかりません: $expectedName"
        }

        $assets += $matchedAsset[0]
    }

    return [pscustomobject]@{
        Version = $normalizedVersion
        Assets = $assets
    }
}

function Resolve-PackageVersionFromFiles {
    param(
        [Parameter(Mandatory = $true)]
        [string]$DirectoryPath
    )

    $packageIds = @(
        "IndigoMovieEngine.Thumbnail.Contracts",
        "IndigoMovieEngine.Thumbnail.Engine",
        "IndigoMovieEngine.Thumbnail.FailureDb"
    )
    $versions = @()

    foreach ($packageId in $packageIds) {
        $pattern = '^' + [regex]::Escape($packageId) + '\.(.+)\.nupkg$'
        $matches = @(
            Get-ChildItem -Path $DirectoryPath -File -Filter "$packageId.*.nupkg" -ErrorAction SilentlyContinue |
                Where-Object { $_.Name -match $pattern } |
                ForEach-Object { $Matches[1] }
        )

        if ($matches.Count -ne 1) {
            throw "package version を一意に判定できません: $packageId -> $($matches -join ', ')"
        }

        $versions += $matches[0]
    }

    $common = @($versions | Sort-Object -Unique)
    if ($common.Count -ne 1) {
        throw "package version が揃っていません: $($common -join ', ')"
    }

    return $common[0]
}

function Write-SyncMetadata {
    param(
        [Parameter(Mandatory = $true)]
        [string]$DestinationDirectory,
        [Parameter(Mandatory = $true)]
        [string]$PrivateRepo,
        [Parameter(Mandatory = $true)]
        [string]$WorkflowName,
        [Parameter(Mandatory = $true)]
        [string]$SourceType,
        [Parameter(Mandatory = $true)]
        [string]$PackageVersion,
        [string]$ReleaseTag = "",
        [long]$RunId = 0,
        [string]$RunUrl = "",
        [string]$ReleaseUrl = ""
    )

    $metadata = [ordered]@{
        schemaVersion = 1
        sourceType = $SourceType
        version = $PackageVersion
        packageVersion = $PackageVersion
        privateRepoFullName = $PrivateRepo
        workflowFileName = $WorkflowName
        syncedAtUtc = [DateTime]::UtcNow.ToString("o")
    }

    if (-not [string]::IsNullOrWhiteSpace($ReleaseTag)) {
        $metadata.releaseTag = $ReleaseTag
    }
    if ($RunId -gt 0) {
        $metadata.runId = $RunId
    }
    if (-not [string]::IsNullOrWhiteSpace($RunUrl)) {
        $metadata.runUrl = $RunUrl
    }
    if (-not [string]::IsNullOrWhiteSpace($ReleaseUrl)) {
        $metadata.releaseUrl = $ReleaseUrl
    }

    $path = Join-Path $DestinationDirectory "private-engine-packages-source.json"
    $utf8NoBom = [System.Text.UTF8Encoding]::new($false)
    [System.IO.File]::WriteAllText($path, ($metadata | ConvertTo-Json -Depth 5), $utf8NoBom)
}

$repoRoot = Get-RepoRoot
$destinationFullPath = [System.IO.Path]::GetFullPath((Join-Path $repoRoot $DestinationPath))
$token = Get-GitHubToken -ExplicitToken $GitHubToken
$tempRoot = Join-Path $env:TEMP ("imm-private-package-sync-" + [guid]::NewGuid().ToString("N"))
$extractRoot = Join-Path $tempRoot "extract"

try {
    New-Item -ItemType Directory -Path $extractRoot -Force | Out-Null

    if (-not [string]::IsNullOrWhiteSpace($ReleaseTag)) {
        $release = Get-ReleaseByTag -PrivateRepo $PrivateRepoFullName -TagName $ReleaseTag -Token $token
        $releaseAssets = Find-ReleaseAssets -Release $release -ReleaseTag $ReleaseTag

        if (Test-Path -LiteralPath $destinationFullPath) {
            Remove-Item -LiteralPath $destinationFullPath -Recurse -Force
        }
        New-Item -ItemType Directory -Path $destinationFullPath -Force | Out-Null

        foreach ($asset in $releaseAssets.Assets) {
            $outFilePath = Join-Path $destinationFullPath $asset.name
            Download-GitHubReleaseAsset `
                -PrivateRepo $PrivateRepoFullName `
                -AssetId ([long]$asset.id) `
                -Token $token `
                -OutFilePath $outFilePath
        }

        Write-SyncMetadata `
            -DestinationDirectory $destinationFullPath `
            -PrivateRepo $PrivateRepoFullName `
            -WorkflowName $WorkflowFileName `
            -SourceType "github-release-asset" `
            -PackageVersion $releaseAssets.Version `
            -ReleaseTag $ReleaseTag `
            -ReleaseUrl "$($release.html_url)"

        Write-Host "Private engine packages synced."
        Write-Host "repo: $PrivateRepoFullName"
        Write-Host "releaseTag: $ReleaseTag"
        Write-Host "packageVersion: $($releaseAssets.Version)"
        Write-Host "destination: $destinationFullPath"
        Write-Host "sourceType: github-release-asset"
        exit 0
    }

    $run = if ($RunId -gt 0) {
        Get-WorkflowRun -PrivateRepo $PrivateRepoFullName -WorkflowRunId $RunId -Token $token
    }
    else {
        Find-LatestSuccessfulWorkflowRun -PrivateRepo $PrivateRepoFullName -WorkflowName $WorkflowFileName -BranchName $Branch -Token $token
    }

    if ("$($run.status)" -ne "completed" -or "$($run.conclusion)" -ne "success") {
        throw "workflow run が completed/success ではありません: runId=$($run.id)"
    }

    $artifact = Find-RunArtifact `
        -PrivateRepo $PrivateRepoFullName `
        -WorkflowRunId ([long]$run.id) `
        -ExpectedArtifactName $ArtifactName `
        -Token $token

    $zipPath = Join-Path $tempRoot "packages.zip"
    Download-GitHubArtifactZip -Uri $artifact.archive_download_url -Token $token -OutFilePath $zipPath
    Expand-Archive -LiteralPath $zipPath -DestinationPath $extractRoot -Force

    if (Test-Path -LiteralPath $destinationFullPath) {
        Remove-Item -LiteralPath $destinationFullPath -Recurse -Force
    }
    New-Item -ItemType Directory -Path $destinationFullPath -Force | Out-Null

    Get-ChildItem -Path $extractRoot -Recurse -File -Filter "*.nupkg" |
        ForEach-Object {
            Copy-Item -LiteralPath $_.FullName -Destination (Join-Path $destinationFullPath $_.Name) -Force
        }

    $packageVersion = Resolve-PackageVersionFromFiles -DirectoryPath $destinationFullPath
    Write-SyncMetadata `
        -DestinationDirectory $destinationFullPath `
        -PrivateRepo $PrivateRepoFullName `
        -WorkflowName $WorkflowFileName `
        -SourceType "github-actions-artifact" `
        -PackageVersion $packageVersion `
        -RunId ([long]$run.id) `
        -RunUrl "$($run.html_url)"

    Write-Host "Private engine packages synced."
    Write-Host "repo: $PrivateRepoFullName"
    Write-Host "runId: $($run.id)"
    Write-Host "artifact: $($artifact.name)"
    Write-Host "packageVersion: $packageVersion"
    Write-Host "destination: $destinationFullPath"
    Write-Host "sourceType: github-actions-artifact"
}
finally {
    if (Test-Path -LiteralPath $tempRoot) {
        Remove-Item -LiteralPath $tempRoot -Recurse -Force
    }
}
