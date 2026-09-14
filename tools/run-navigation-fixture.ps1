[CmdletBinding()]
param(
    [ValidateSet('Debug', 'Release')]
    [string] $Configuration = 'Debug'
)

$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path -Parent $PSScriptRoot
& dotnet run --no-build --project (Join-Path $repoRoot 'src\Festival.Runner\Festival.Runner.csproj') --configuration $Configuration -- --scenario single-agent-navigation
if ($LASTEXITCODE -ne 0) { throw "Navigation fixture failed with exit code $LASTEXITCODE." }
