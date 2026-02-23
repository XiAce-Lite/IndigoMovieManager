param(
    [string]$Root = "."
)

$ErrorActionPreference = "Stop"

$targetExtensions = @("*.cs", "*.xaml", "*.md", "*.csproj", "*.ps1", "*.config")
$excludePathRegex = "\\(bin|obj|\\.git|packages)\\"

# Detect common mojibake signals:
# 1) Unicode replacement character (U+FFFD)
# 2) Mixed CJK and halfwidth-katakana in one line (high-probability mojibake pattern)
$suspiciousRegex = "�|[\u4E00-\u9FFF].*[\uFF61-\uFF9F]|[\uFF61-\uFF9F].*[\u4E00-\u9FFF]"

$files = foreach ($ext in $targetExtensions) {
    Get-ChildItem -Path $Root -Recurse -File -Filter $ext
}

$files = $files |
    Where-Object { $_.FullName -notmatch $excludePathRegex } |
    Sort-Object FullName -Unique

$hits = New-Object System.Collections.Generic.List[object]

foreach ($file in $files) {
    $text = [System.IO.File]::ReadAllText($file.FullName)
    $lineNo = 1
    foreach ($line in ($text -split "`n")) {
        if ($line -match $suspiciousRegex) {
            $hits.Add([pscustomobject]@{
                    File = $file.FullName
                    Line = $lineNo
                    Text = $line.Trim()
                })
            break
        }
        $lineNo++
    }
}

if ($hits.Count -gt 0) {
    Write-Host "NG: Mojibake-like text detected." -ForegroundColor Red
    foreach ($hit in $hits) {
        Write-Host ("- {0}:{1}" -f $hit.File, $hit.Line)
        Write-Host ("  {0}" -f $hit.Text)
    }
    exit 1
}

Write-Host "OK: No mojibake-like text detected."
