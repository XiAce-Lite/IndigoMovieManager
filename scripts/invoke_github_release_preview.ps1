[CmdletBinding()]
param(
    [string]$Owner = "T-Hamada0101",
    [string]$Repository = "IndigoMovieManager_fork",
    [string]$WorkflowFileName = "github-release-package.yml",
    [string]$Ref = "workthree",
    [string]$PrivateEngineRunId = "",
    [switch]$Wait,
    [int]$PollIntervalSeconds = 10,
    [int]$TimeoutMinutes = 10
)

$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest

function Write-Step {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Message
    )

    Write-Host "[github-preview] $Message" -ForegroundColor Cyan
}

function Get-GitHubToken {
    # gh ログインに依存せず、環境変数の token だけで GitHub API を叩けるようにする。
    if (-not [string]::IsNullOrWhiteSpace($env:GH_TOKEN)) {
        return $env:GH_TOKEN
    }

    if (-not [string]::IsNullOrWhiteSpace($env:GITHUB_TOKEN)) {
        return $env:GITHUB_TOKEN
    }

    $credentialInput = "protocol=https`nhost=github.com`n`n"
    $credentialOutput = $credentialInput | git credential fill 2>$null
    foreach ($line in ($credentialOutput -split "`r?`n")) {
        if ($line -like "password=*") {
            return $line.Substring("password=".Length)
        }
    }

    throw "GH_TOKEN か GITHUB_TOKEN を設定してください。workflow_dispatch には Actions: write 権限が必要です。"
}

function Get-GitHubApiHeaders {
    $token = Get-GitHubToken
    return @{
        Authorization         = "Bearer $token"
        Accept                = "application/vnd.github+json"
        "X-GitHub-Api-Version" = "2022-11-28"
    }
}

function Invoke-GitHubApi {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Method,
        [Parameter(Mandatory = $true)]
        [string]$Uri,
        [object]$Body
    )

    $invokeParams = @{
        Method      = $Method
        Uri         = $Uri
        Headers     = Get-GitHubApiHeaders
        ErrorAction = "Stop"
    }

    if ($PSBoundParameters.ContainsKey("Body")) {
        $invokeParams.ContentType = "application/json; charset=utf-8"
        $invokeParams.Body = ($Body | ConvertTo-Json -Depth 10)
    }

    return Invoke-RestMethod @invokeParams
}

function Start-WorkflowDispatch {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Owner,
        [Parameter(Mandatory = $true)]
        [string]$Repository,
        [Parameter(Mandatory = $true)]
        [string]$WorkflowFileName,
        [Parameter(Mandatory = $true)]
        [string]$Ref,
        [string]$PrivateEngineRunId = ""
    )

    $dispatchUri = "https://api.github.com/repos/$Owner/$Repository/actions/workflows/$WorkflowFileName/dispatches"
    $dispatchBody = [ordered]@{
        ref = $Ref
    }
    if (-not [string]::IsNullOrWhiteSpace($PrivateEngineRunId)) {
        $dispatchBody.inputs = @{
            private_engine_run_id = $PrivateEngineRunId.Trim()
        }
    }

    # 送信ログを出す前に token を確認し、未設定時の誤解を避ける。
    [void](Get-GitHubToken)
    $runIdSuffix =
        if ([string]::IsNullOrWhiteSpace($PrivateEngineRunId)) {
            ""
        }
        else
        {
            " private_engine_run_id=$($PrivateEngineRunId.Trim())"
        }
    Write-Step "workflow_dispatch を送信します: $Owner/$Repository $WorkflowFileName ref=$Ref$runIdSuffix"
    Invoke-GitHubApi -Method Post -Uri $dispatchUri -Body $dispatchBody | Out-Null
}

function Get-LatestWorkflowDispatchRun {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Owner,
        [Parameter(Mandatory = $true)]
        [string]$Repository,
        [Parameter(Mandatory = $true)]
        [string]$WorkflowFileName,
        [Parameter(Mandatory = $true)]
        [string]$Ref,
        [Parameter(Mandatory = $true)]
        [datetimeoffset]$DispatchStartedAt
    )

    $runsUri = "https://api.github.com/repos/$Owner/$Repository/actions/workflows/$WorkflowFileName/runs?event=workflow_dispatch&branch=$Ref&per_page=10"
    $response = Invoke-GitHubApi -Method Get -Uri $runsUri
    if ($null -eq $response.workflow_runs) {
        return $null
    }

    foreach ($run in $response.workflow_runs) {
        $createdAt = [datetimeoffset]::Parse($run.created_at)
        if ($createdAt -ge $DispatchStartedAt.AddSeconds(-30)) {
            return $run
        }
    }

    return $null
}

$dispatchStartedAt = [datetimeoffset]::Now
Start-WorkflowDispatch `
    -Owner $Owner `
    -Repository $Repository `
    -WorkflowFileName $WorkflowFileName `
    -Ref $Ref `
    -PrivateEngineRunId $PrivateEngineRunId

if (-not $Wait) {
    Write-Step "dispatch 済みです。Actions 画面で github-release-package を確認してください。"
    exit 0
}

# dispatch 直後は run 一覧に出るまで少しラグがあるので、一定時間だけ待つ。
$deadline = [datetimeoffset]::Now.AddMinutes($TimeoutMinutes)
$lastRunUrl = ""

while ([datetimeoffset]::Now -lt $deadline) {
    $run = Get-LatestWorkflowDispatchRun `
        -Owner $Owner `
        -Repository $Repository `
        -WorkflowFileName $WorkflowFileName `
        -Ref $Ref `
        -DispatchStartedAt $dispatchStartedAt

    if ($null -eq $run) {
        Start-Sleep -Seconds $PollIntervalSeconds
        continue
    }

    $runUrl = "$($run.html_url)"
    if ($runUrl -ne $lastRunUrl) {
        Write-Step "workflow run: $runUrl"
        $lastRunUrl = $runUrl
    }

    $status = "$($run.status)"
    $conclusion = "$($run.conclusion)"
    Write-Step "status=$status conclusion=$conclusion"

    if ($status -eq "completed") {
        if ($conclusion -ne "success") {
            throw "workflow_dispatch は完了しましたが conclusion=$conclusion でした。run: $runUrl"
        }

        Write-Step "preview run が成功しました。run summary と github-release-body-preview artifact を確認してください。"
        exit 0
    }

    Start-Sleep -Seconds $PollIntervalSeconds
}

throw "workflow_dispatch の run を $TimeoutMinutes 分待ちましたが完了を確認できませんでした。"
