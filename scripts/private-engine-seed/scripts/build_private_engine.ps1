param(
    [string]$Configuration = "Debug"
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot "..")).Path
$solutionPath = Join-Path $repoRoot "IndigoMovieEngine.slnx"
$testsProjectPath = Join-Path $repoRoot "tests\IndigoMovieManager.Tests\IndigoMovieManager.Tests.csproj"

dotnet build $solutionPath -c $Configuration
dotnet test $testsProjectPath -c $Configuration -p:Platform=x64 --no-build
