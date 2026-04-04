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
