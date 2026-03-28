param(
    [switch]$ForceStart
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

# 実動画確認前に「今起動しているのが本当に workthree か」を止血確認する。
$repoRoot = Split-Path -Parent $PSScriptRoot
$preferredExePath = Join-Path $repoRoot "bin\\x64\\Debug\\net8.0-windows\\IndigoMovieManager_fork_workthree.exe"

function Write-Section([string]$title)
{
    ""
    "=== $title ==="
}

function Get-ConflictingProcesses([string]$currentRepoRoot)
{
    return @(
        Get-Process |
            Where-Object {
                $_.ProcessName -like "IndigoMovieManager*"
            } |
            Select-Object ProcessName, Id, Path |
            Where-Object {
                -not [string]::IsNullOrWhiteSpace($_.Path) -and
                -not $_.Path.StartsWith($currentRepoRoot, [StringComparison]::OrdinalIgnoreCase)
            }
    )
}

function Resolve-WorkthreeExePath([string]$currentRepoRoot, [string]$preferredPath)
{
    if (Test-Path $preferredPath)
    {
        return (Get-Item $preferredPath).FullName
    }

    $candidates = @(
        Get-ChildItem -Path $currentRepoRoot -Recurse -Filter "IndigoMovieManager_fork_workthree.exe" -File |
            Sort-Object LastWriteTime -Descending
    )

    if ($candidates.Count -lt 1)
    {
        return ""
    }

    return $candidates[0].FullName
}

Write-Output "実動画確認起動ガード: $(Get-Date -Format 'yyyy-MM-dd HH:mm:ss')"
Write-Output "リポジトリ: $repoRoot"

$conflicts = @(Get-ConflictingProcesses -currentRepoRoot $repoRoot)
$exePath = Resolve-WorkthreeExePath -currentRepoRoot $repoRoot -preferredPath $preferredExePath

Write-Section "起動候補"
if ([string]::IsNullOrWhiteSpace($exePath))
{
    "workthree exe が見つかりません。先に build が必要です。"
    exit 2
}

"exe: $exePath"

Write-Section "競合プロセス"
if ($conflicts.Count -lt 1)
{
    "競合プロセスなし"
}
else
{
    $conflicts | Format-Table -AutoSize | Out-String
}

if ($conflicts.Count -gt 0 -and -not $ForceStart.IsPresent)
{
    ""
    "起動中止: 現在は別リポジトリの IndigoMovieManager 系プロセスが動作中です。"
    "この状態で workthree を重ねて起動すると、同じ DB / ログ / LocalAppData を触って実動画確認を汚す可能性があります。"
    "必要なら先に既存プロセスを止めてから、再度このスクリプトを実行してください。"
    "どうしても重ねて起動する場合だけ -ForceStart を付けます。"
    exit 1
}

Write-Section "起動"
$process = Start-Process -FilePath $exePath -WorkingDirectory (Split-Path -Parent $exePath) -PassThru
"started: pid=$($process.Id)"
"起動後は .\\Thumbnail\\実動画確認サマリ_2026-03-14.ps1 を再実行して、現在見ているログと FailureDb が workthree か確認してください。"
