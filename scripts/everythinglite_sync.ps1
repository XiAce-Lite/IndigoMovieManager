param(
    [ValidateSet("Import", "Export")]
    [string]$Mode = "Import",
    [string]$RepoRoot = "",
    [string]$MyLabRoot = ""
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

function Resolve-RepoRoot {
    $scriptDir = Split-Path -Parent $PSCommandPath
    return (Resolve-Path (Join-Path $scriptDir "..")).Path
}

function Convert-ToUsnMftNamespace {
    param([string]$Text)
    # 取り込み後は技術名ベースの名前空間へ統一する。
    return $Text -replace "namespace\s+EverythingLite", "namespace IndigoMovieManager.FileIndex.UsnMft"
}

function Convert-ToEverythingLiteNamespace {
    param([string]$Text)
    # 再分離時は外部プロジェクト側の既存名前空間へ戻す。
    return $Text -replace "namespace\s+IndigoMovieManager\.FileIndex\.UsnMft", "namespace EverythingLite"
}

if ([string]::IsNullOrWhiteSpace($RepoRoot)) {
    $RepoRoot = Resolve-RepoRoot
}

if ([string]::IsNullOrWhiteSpace($MyLabRoot)) {
    $MyLabRoot = Join-Path (Split-Path -Parent $RepoRoot) "MyLab"
}

$sourceRoot = Join-Path $MyLabRoot "EverythingLite"
$targetRoot = Join-Path $RepoRoot "src\IndigoMovieManager.FileIndex.UsnMft"

$syncFiles = @(
    "AdminUsnMftIndexBackend.cs",
    "AppStructuredLog.cs",
    "FileIndexService.cs",
    "FileIndexServiceOptions.cs",
    "IFileIndexService.cs",
    "IIndexBackend.cs",
    "IndexProgress.cs",
    "SearchResultItem.cs",
    "StandardFileSystemIndexBackend.cs"
)

if ($Mode -eq "Import") {
    if (-not (Test-Path $sourceRoot)) {
        throw "ソースが見つかりません: $sourceRoot"
    }

    New-Item -ItemType Directory -Force -Path $targetRoot | Out-Null
    foreach ($relative in $syncFiles) {
        $src = Join-Path $sourceRoot $relative
        if (-not (Test-Path $src)) {
            throw "取り込み元ファイルが見つかりません: $src"
        }

        $dst = Join-Path $targetRoot $relative
        $raw = Get-Content -Encoding UTF8 -Raw $src
        $converted = Convert-ToUsnMftNamespace -Text $raw
        $normalized = $converted -replace "`r`n", "`n"
        Set-Content -Encoding utf8 -NoNewline -Path $dst -Value $normalized
        Write-Host "[sync:import] $relative"
    }
}
else {
    if (-not (Test-Path $targetRoot)) {
        throw "エクスポート元が見つかりません: $targetRoot"
    }

    New-Item -ItemType Directory -Force -Path $sourceRoot | Out-Null
    foreach ($relative in $syncFiles) {
        $src = Join-Path $targetRoot $relative
        if (-not (Test-Path $src)) {
            throw "エクスポート元ファイルが見つかりません: $src"
        }

        $dst = Join-Path $sourceRoot $relative
        $raw = Get-Content -Encoding UTF8 -Raw $src
        $converted = Convert-ToEverythingLiteNamespace -Text $raw
        $normalized = $converted -replace "`r`n", "`n"
        Set-Content -Encoding utf8 -NoNewline -Path $dst -Value $normalized
        Write-Host "[sync:export] $relative"
    }
}

Write-Host "[sync] completed mode=$Mode repo=$RepoRoot mylab=$MyLabRoot"
