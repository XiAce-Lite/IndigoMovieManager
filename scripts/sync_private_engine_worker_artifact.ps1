[CmdletBinding()]
param(
    [string]$PrivateRepoFullName = "T-Hamada0101/IndigoMovieEngine",
    [string]$WorkflowFileName = "private-engine-publish.yml",
    [string]$ArtifactName = "rescue-worker-publish",
    [string]$Branch = "main",
    [string]$ReleaseTag = "",
    [string]$DestinationPath = "artifacts/rescue-worker/publish/Release-win-x64",
    [string]$GitHubToken = "",
    [long]$RunId = 0
)

$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest

$script:EngineMirrorManifestFileName = "engine-manifest.json"

function Get-RepoRoot {
    return (Resolve-Path (Join-Path $PSScriptRoot "..")).Path
}

function Write-Utf8Line {
    param([string]$Message)
    Write-Host $Message
}

function New-GitHubHeaders {
    param([string]$Token)

    $headers = @{
        Accept = "application/vnd.github+json"
        "X-GitHub-Api-Version" = "2022-11-28"
    }

    if (-not [string]::IsNullOrWhiteSpace($Token)) {
        $headers.Authorization = "Bearer $Token"
    }

    return $headers
}

function Get-GitHubToken {
    param([string]$ExplicitToken)

    # CI では secret 注入を優先し、ローカルでは既存 git credential を最後の fallback にする。
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

    return ""
}

function Invoke-GitHubJson {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Uri,
        [Parameter(Mandatory = $true)]
        [string]$Token
    )

    $headers = New-GitHubHeaders -Token $Token
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

    $headers = New-GitHubHeaders -Token $Token
    Invoke-WebRequest -Uri $Uri -Headers $headers -OutFile $OutFilePath | Out-Null
}

function Get-EngineMirrorManifestAsset {
    param(
        [Parameter(Mandatory = $true)]
        [psobject]$Release
    )

    $matched = @($Release.assets | Where-Object { "$($_.name)" -eq $script:EngineMirrorManifestFileName })
    if ($matched.Count -gt 1) {
        throw "engine manifest asset が複数見つかりました: $($script:EngineMirrorManifestFileName)"
    }

    if ($matched.Count -eq 1) {
        return $matched[0]
    }

    return $null
}

function Read-EngineMirrorManifest {
    param(
        [Parameter(Mandatory = $true)]
        [string]$ManifestPath
    )

    if (-not (Test-Path -LiteralPath $ManifestPath)) {
        return $null
    }

    try {
        $manifest = Get-Content -LiteralPath $ManifestPath -Raw -Encoding utf8 | ConvertFrom-Json
    }
    catch {
        Write-Host "engine-manifest.json の JSON 解釈に失敗したため legacy 解決へ fallback します: $($_.Exception.Message)"
        return $null
    }

    if ("$($manifest.schemaVersion)" -ne "1") {
        Write-Host "engine-manifest.json の schemaVersion を解釈できないため legacy 解決へ fallback します: $ManifestPath"
        return $null
    }

    return $manifest
}

function Resolve-WorkerAssetNameFromEngineManifest {
    param(
        [Parameter(Mandatory = $true)]
        [psobject]$EngineManifest
    )

    if ($null -ne $EngineManifest.worker) {
        $workerAssetName = "$($EngineManifest.worker.assetFileName)".Trim()
        if (-not [string]::IsNullOrWhiteSpace($workerAssetName)) {
            return $workerAssetName
        }
    }

    $legacyWorkerAssetName = "$($EngineManifest.workerAssetName)".Trim()
    if (-not [string]::IsNullOrWhiteSpace($legacyWorkerAssetName)) {
        return $legacyWorkerAssetName
    }

    return ""
}

function Find-ReleaseAssetFromEngineManifest {
    param(
        [Parameter(Mandatory = $true)]
        [string]$PrivateRepo,
        [Parameter(Mandatory = $true)]
        [psobject]$Release,
        [Parameter(Mandatory = $true)]
        [psobject]$EngineManifest
    )

    $assetName = Resolve-WorkerAssetNameFromEngineManifest -EngineManifest $EngineManifest
    if ([string]::IsNullOrWhiteSpace($assetName)) {
        return $null
    }

    $matched = @($Release.assets | Where-Object { "$($_.name)" -eq $assetName })
    if ($matched.Count -eq 0) {
        throw "engine manifest に書かれた worker asset が release に見つかりません: repo=$PrivateRepo asset=$assetName"
    }
    if ($matched.Count -gt 1) {
        throw "engine manifest に書かれた worker asset が一意に決まりません: repo=$PrivateRepo asset=$assetName"
    }

    return $matched[0]
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

    $uri =
        "https://api.github.com/repos/$PrivateRepo/actions/workflows/$WorkflowName/runs?branch=$BranchName&per_page=20"
    $response = Invoke-GitHubJson -Uri $uri -Token $Token
    foreach ($run in $response.workflow_runs) {
        # latest 成功 run だけを拾い、途中失敗や古い pending を混ぜない。
        if ($run.status -eq "completed" -and $run.conclusion -eq "success") {
            return $run
        }
    }

    throw "成功済み workflow run が見つかりません: repo=$PrivateRepo workflow=$WorkflowName branch=$BranchName"
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

function Get-NormalizedReleaseTag {
    param([string]$ReleaseTag)

    $tag = $ReleaseTag.Trim()
    if ($tag -like "v*") {
        $tag = $tag.Substring(1)
    }

    if ([string]::IsNullOrWhiteSpace($tag)) {
        throw "release tag が空です。"
    }

    return $tag
}

function Get-WorkerAssetPatterns {
    param(
        [Parameter(Mandatory = $true)]
        [string]$ReleaseTag,
        [Parameter(Mandatory = $true)]
        [string]$Runtime
    )

    $normalizedTag = Get-NormalizedReleaseTag -ReleaseTag $ReleaseTag
    $normalizedRuntime = [regex]::Escape($Runtime)
    $escapedTag = [regex]::Escape($normalizedTag)

    return @(
        @{
            Label = "new"
            Pattern = '^IndigoMovieManager\.Thumbnail\.RescueWorker-v?' + $escapedTag + '-' + $normalizedRuntime + '-compat-.*\.zip$'
            Priority = 0
        },
        @{
            Label = "legacyAppZip"
            Pattern = '^IndigoMovieManager-v?' + $escapedTag + '-' + $normalizedRuntime + '\.zip$'
            Priority = 1
        }
    )
}

function Test-WorkflowRunMatchesSelection {
    param(
        [Parameter(Mandatory = $true)]
        [psobject]$WorkflowRun,
        [Parameter(Mandatory = $true)]
        [string]$WorkflowName,
        [Parameter(Mandatory = $true)]
        [string]$BranchName
    )

    if ($null -eq $WorkflowRun) {
        throw "workflow run 情報が取得できませんでした。"
    }

    if ("$($WorkflowRun.status)" -ne "completed" -or "$($WorkflowRun.conclusion)" -ne "success") {
        throw "workflow run が completed/success ではありません: runId=$($WorkflowRun.id) status=$($WorkflowRun.status) conclusion=$($WorkflowRun.conclusion)"
    }

    $branchSpecified = -not [string]::IsNullOrWhiteSpace($BranchName)
    $branchMatches = [string]::Equals(
        "$($WorkflowRun.head_branch)",
        $BranchName,
        [System.StringComparison]::OrdinalIgnoreCase
    )
    if ($branchSpecified -and -not $branchMatches) {
        throw "workflow run の branch が一致しません: runId=$($WorkflowRun.id) expected=$BranchName actual=$($WorkflowRun.head_branch)"
    }

    $workflowPath = "$($WorkflowRun.path)".Trim()
    $hasWorkflowPath = -not [string]::IsNullOrWhiteSpace($workflowPath)
    $workflowPathContainsName =
        $workflowPath.IndexOf(
            "$WorkflowName@",
            [System.StringComparison]::OrdinalIgnoreCase
        ) -ge 0
    $workflowFileNameMatches = [string]::Equals(
        [System.IO.Path]::GetFileName($workflowPath),
        $WorkflowName,
        [System.StringComparison]::OrdinalIgnoreCase
    )
    if ($hasWorkflowPath -and -not $workflowPathContainsName -and -not $workflowFileNameMatches) {
        throw "workflow run が想定 workflow ではありません: runId=$($WorkflowRun.id) workflowPath=$workflowPath expected=$WorkflowName"
    }
}

function Find-ReleaseAsset {
    param(
        [Parameter(Mandatory = $true)]
        [string]$PrivateRepo,
        [Parameter(Mandatory = $true)]
        [psobject]$Release,
        [Parameter(Mandatory = $true)]
        [string]$ReleaseTag,
        [Parameter(Mandatory = $true)]
        [string]$Runtime
    )

    $assets = @($Release.assets)
    $assetPatterns = Get-WorkerAssetPatterns -ReleaseTag $ReleaseTag -Runtime $Runtime

    $primaryPattern = $assetPatterns | Where-Object { $_.Priority -eq 0 } | Select-Object -First 1
    $fallbackPattern = $assetPatterns | Where-Object { $_.Priority -gt 0 } | Select-Object -First 1

    $matchingAssets = @()
    if ($null -ne $primaryPattern) {
        $matchingAssets = @($assets | Where-Object { "$($_.name)" -match $primaryPattern.Pattern })
    }
    if ($matchingAssets.Count -gt 1) {
        $matchingAssetNames = @($matchingAssets | ForEach-Object { "$($_.name)" })
        throw "release asset が複数見つかりました: repo=$PrivateRepo releaseTag=$ReleaseTag expectedPattern=$($primaryPattern.Pattern) assets=[$($matchingAssetNames -join ', ')]"
    }
    if ($matchingAssets.Count -eq 1) {
        return $matchingAssets[0]
    }

    $fallbackAssets = @()
    $fallbackPatternText = ""
    if ($null -ne $fallbackPattern) {
        $fallbackAssets = @($assets | Where-Object { "$($_.name)" -match $fallbackPattern.Pattern })
        $fallbackPatternText = $fallbackPattern.Pattern
    }

    if ($fallbackAssets.Count -eq 0) {
        $patternDescriptions = @($assetPatterns | ForEach-Object { "$($_.Label):$($_.Pattern)" })
        $assetNames = @($assets | ForEach-Object { "$($_.name)" })
        throw "release asset が見つかりません: repo=$PrivateRepo releaseTag=$ReleaseTag patterns=[$($patternDescriptions -join ', ')] assets=[$($assetNames -join ', ')]"
    }

    if ($fallbackAssets.Count -gt 1) {
        $fallbackAssetNames = @($fallbackAssets | ForEach-Object { "$($_.name)" })
        throw "release asset が複数見つかりました: repo=$PrivateRepo releaseTag=$ReleaseTag pattern=$fallbackPatternText assets=[$($fallbackAssetNames -join ', ')]"
    }

    Write-Host "legacy worker asset 名を fallback で利用します: $($fallbackAssets[0].name)"
    return $fallbackAssets[0]
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

function Download-GitHubReleaseAssetZip {
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

    $headers = New-GitHubHeaders -Token $Token
    $headers.Accept = "application/octet-stream"
    $uri = "https://api.github.com/repos/$PrivateRepo/releases/assets/$AssetId"
    Invoke-WebRequest -Uri $uri -Headers $headers -OutFile $OutFilePath | Out-Null
}

function Resolve-ArtifactContentDirectory {
    param(
        [Parameter(Mandatory = $true)]
        [string]$ExtractRoot
    )

    $workerExe = Get-ChildItem -Path $ExtractRoot -Recurse -File -Filter "IndigoMovieManager.Thumbnail.RescueWorker.exe" |
        Select-Object -First 1
    if ($null -eq $workerExe) {
        throw "展開 artifact に worker exe が見つかりません。"
    }

    $markerPath = Join-Path $workerExe.Directory.FullName "rescue-worker-artifact.json"
    if (-not (Test-Path -LiteralPath $markerPath)) {
        throw "展開 artifact に marker が見つかりません: $markerPath"
    }

    return $workerExe.Directory.FullName
}

function Read-CompatibilityVersion {
    param(
        [Parameter(Mandatory = $true)]
        [string]$MarkerPath
    )

    $marker = Get-Content -LiteralPath $MarkerPath -Raw -Encoding utf8 | ConvertFrom-Json
    return "$($marker.compatibilityVersion)"
}

function Write-SyncSourceMetadata {
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
        [string]$SourceVersion,
        [Parameter(Mandatory = $true)]
        [string]$SourceAssetName,
        [Parameter(Mandatory = $true)]
        [string]$CompatibilityVersion,
        [long]$WorkflowRunId = 0,
        [string]$WorkflowRunUrl = "",
        [string]$ReleaseTag = "",
        [string]$ReleaseUrl = "",
        [long]$ReleaseAssetId = 0
    )

    $metadata = [ordered]@{
        schemaVersion = 1
        sourceType = $SourceType
        version = $SourceVersion
        assetFileName = $SourceAssetName
        sourceArtifactName = $SourceAssetName
        privateRepoFullName = $PrivateRepo
        workflowFileName = $WorkflowName
        compatibilityVersion = $CompatibilityVersion
        syncedAtUtc = [DateTime]::UtcNow.ToString("o")
    }

    if ($WorkflowRunId -gt 0) {
        $metadata.runId = $WorkflowRunId
    }
    if (-not [string]::IsNullOrWhiteSpace($WorkflowRunUrl)) {
        $metadata.runUrl = $WorkflowRunUrl
    }
    if (-not [string]::IsNullOrWhiteSpace($ReleaseTag)) {
        $metadata.releaseTag = $ReleaseTag
    }
    if (-not [string]::IsNullOrWhiteSpace($ReleaseUrl)) {
        $metadata.releaseUrl = $ReleaseUrl
    }
    if ($ReleaseAssetId -gt 0) {
        $metadata.releaseAssetId = $ReleaseAssetId
    }

    $metadataPath = Join-Path $DestinationDirectory "rescue-worker-sync-source.json"
    $utf8NoBom = [System.Text.UTF8Encoding]::new($false)
    [System.IO.File]::WriteAllText(
        $metadataPath,
        ($metadata | ConvertTo-Json -Depth 5),
        $utf8NoBom
    )
}

function Get-PrivateEngineSyncSource {
    param(
        [Parameter(Mandatory = $true)]
        [string]$PrivateRepo,
        [Parameter(Mandatory = $true)]
        [string]$WorkflowName,
        [Parameter(Mandatory = $true)]
        [string]$ArtifactNameValue,
        [Parameter(Mandatory = $true)]
        [string]$BranchName,
        [string]$ReleaseTagValue,
        [Parameter(Mandatory = $true)]
        [long]$WorkflowRunId,
        [Parameter(Mandatory = $true)]
        [string]$Token
    )

    if (-not [string]::IsNullOrWhiteSpace($ReleaseTagValue)) {
        $release = Get-ReleaseByTag -PrivateRepo $PrivateRepo -TagName $ReleaseTagValue -Token $Token
        $releaseAsset = $null
        $engineManifestAsset = Get-EngineMirrorManifestAsset -Release $release
        if ($null -ne $engineManifestAsset) {
            $manifestPath = Join-Path ([System.IO.Path]::GetTempPath()) ("imm-engine-manifest-" + [guid]::NewGuid().ToString("N") + ".json")
            try {
                Download-GitHubReleaseAssetZip `
                    -PrivateRepo $PrivateRepo `
                    -AssetId ([long]$engineManifestAsset.id) `
                    -Token $Token `
                    -OutFilePath $manifestPath
                $engineManifest = Read-EngineMirrorManifest -ManifestPath $manifestPath
                if ($null -ne $engineManifest) {
                    $releaseAsset = Find-ReleaseAssetFromEngineManifest `
                        -PrivateRepo $PrivateRepo `
                        -Release $release `
                        -EngineManifest $engineManifest
                    if ($null -ne $releaseAsset) {
                        Write-Host "engine-manifest.json から worker asset を解決します: $($releaseAsset.name)"
                    }
                }
            }
            finally {
                if (Test-Path -LiteralPath $manifestPath) {
                    Remove-Item -LiteralPath $manifestPath -Force
                }
            }
        }

        if ($null -eq $releaseAsset) {
            $releaseAsset = Find-ReleaseAsset -PrivateRepo $PrivateRepo -Release $release -ReleaseTag $ReleaseTagValue -Runtime "win-x64"
        }
        return [pscustomobject]@{
            SourceKind = "release"
            Release = $release
            ReleaseAsset = $releaseAsset
            SourceVersion = $ReleaseTagValue
            SourceType = "github-release-asset"
        }
    }

    if ($WorkflowRunId -gt 0) {
        $workflowRun = Get-WorkflowRun `
            -PrivateRepo $PrivateRepo `
            -WorkflowRunId $WorkflowRunId `
            -Token $Token
        Test-WorkflowRunMatchesSelection `
            -WorkflowRun $workflowRun `
            -WorkflowName $WorkflowName `
            -BranchName $BranchName

        $artifact = Find-RunArtifact `
            -PrivateRepo $PrivateRepo `
            -WorkflowRunId ([long]$workflowRun.id) `
            -ExpectedArtifactName $ArtifactNameValue `
            -Token $Token

        return [pscustomobject]@{
            SourceKind = "run"
            WorkflowRun = $workflowRun
            Artifact = $artifact
            SourceVersion = "run-$([long]$workflowRun.id)"
            SourceType = "github-actions-artifact"
        }
    }

    $workflowRun = Find-LatestSuccessfulWorkflowRun `
        -PrivateRepo $PrivateRepo `
        -WorkflowName $WorkflowName `
        -BranchName $BranchName `
        -Token $Token
    $artifact = Find-RunArtifact `
        -PrivateRepo $PrivateRepo `
        -WorkflowRunId ([long]$workflowRun.id) `
        -ExpectedArtifactName $ArtifactNameValue `
        -Token $Token

    return [pscustomobject]@{
        SourceKind = "run"
        WorkflowRun = $workflowRun
        Artifact = $artifact
        SourceVersion = "run-$([long]$workflowRun.id)"
        SourceType = "github-actions-artifact"
    }
}

$repoRoot = Get-RepoRoot
$destinationFullPath = [System.IO.Path]::GetFullPath((Join-Path $repoRoot $DestinationPath))
$token = Get-GitHubToken -ExplicitToken $GitHubToken

$syncSource = Get-PrivateEngineSyncSource `
    -PrivateRepo $PrivateRepoFullName `
    -WorkflowName $WorkflowFileName `
    -ArtifactNameValue $ArtifactName `
    -BranchName $Branch `
    -ReleaseTagValue $ReleaseTag `
    -WorkflowRunId $RunId `
    -Token $token

$tempRoot = Join-Path $env:TEMP ("imm-private-worker-sync-" + [guid]::NewGuid().ToString("N"))
$null = New-Item -ItemType Directory -Path $tempRoot -Force
$zipPath = Join-Path $tempRoot "artifact.zip"
$extractRoot = Join-Path $tempRoot "extract"

try {
    # まず zip を temp 展開し、内容を検証してから public repo の publish 置き場を置き換える。
    New-Item -ItemType Directory -Path $extractRoot -Force | Out-Null
    if ($syncSource.SourceKind -eq "release") {
        Download-GitHubReleaseAssetZip `
            -PrivateRepo $PrivateRepoFullName `
            -AssetId ([long]$syncSource.ReleaseAsset.id) `
            -Token $token `
            -OutFilePath $zipPath
    }
    else {
        Download-GitHubArtifactZip -Uri $syncSource.Artifact.archive_download_url -Token $token -OutFilePath $zipPath
    }
    Expand-Archive -LiteralPath $zipPath -DestinationPath $extractRoot -Force

    $contentDirectory = Resolve-ArtifactContentDirectory -ExtractRoot $extractRoot
    $markerPath = Join-Path $contentDirectory "rescue-worker-artifact.json"
    $compatibilityVersion = Read-CompatibilityVersion -MarkerPath $markerPath

    if (Test-Path -LiteralPath $destinationFullPath) {
        Remove-Item -LiteralPath $destinationFullPath -Recurse -Force
    }

    New-Item -ItemType Directory -Path $destinationFullPath -Force | Out-Null
    Copy-Item -Path (Join-Path $contentDirectory "*") -Destination $destinationFullPath -Recurse -Force
    Write-SyncSourceMetadata `
        -DestinationDirectory $destinationFullPath `
        -PrivateRepo $PrivateRepoFullName `
        -WorkflowName $WorkflowFileName `
        -SourceType $syncSource.SourceType `
        -SourceVersion $syncSource.SourceVersion `
        -SourceAssetName $(if ($syncSource.SourceKind -eq "release") { $syncSource.ReleaseAsset.name } else { $syncSource.Artifact.name }) `
        -CompatibilityVersion $compatibilityVersion `
        -WorkflowRunId $(if ($syncSource.SourceKind -eq "run") { [long]$syncSource.WorkflowRun.id } else { 0 }) `
        -WorkflowRunUrl $(if ($syncSource.SourceKind -eq "run") { $syncSource.WorkflowRun.html_url } else { "" }) `
        -ReleaseTag $(if ($syncSource.SourceKind -eq "release") { $ReleaseTag } else { "" }) `
        -ReleaseUrl $(if ($syncSource.SourceKind -eq "release") { $syncSource.Release.html_url } else { "" }) `
        -ReleaseAssetId $(if ($syncSource.SourceKind -eq "release") { [long]$syncSource.ReleaseAsset.id } else { 0 })

    Write-Utf8Line "Private worker artifact synced."
    Write-Utf8Line "repo: $PrivateRepoFullName"
    if ($syncSource.SourceKind -eq "release") {
        Write-Utf8Line "releaseTag: $ReleaseTag"
        Write-Utf8Line "releaseAsset: $($syncSource.ReleaseAsset.name)"
        Write-Utf8Line "releaseUrl: $($syncSource.Release.html_url)"
    }
    else {
        Write-Utf8Line "runId: $($syncSource.WorkflowRun.id)"
        Write-Utf8Line "artifact: $($syncSource.Artifact.name)"
        Write-Utf8Line "runUrl: $($syncSource.WorkflowRun.html_url)"
    }
    Write-Utf8Line "compatibilityVersion: $compatibilityVersion"
    Write-Utf8Line "destination: $destinationFullPath"
    Write-Utf8Line "sourceType: $($syncSource.SourceType)"
    Write-Utf8Line "sourceVersion: $($syncSource.SourceVersion)"
}
finally {
    if (Test-Path -LiteralPath $tempRoot) {
        Remove-Item -LiteralPath $tempRoot -Recurse -Force
    }
}
