[CmdletBinding()]
param(
    [ValidateSet('Debug', 'Release')]
    [string] $Configuration = 'Debug'
)

$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path -Parent $PSScriptRoot
Push-Location $repoRoot
try {
    dotnet run --project .\src\Festival.Runner\Festival.Runner.csproj --configuration $Configuration --no-build -- --scenario atomic-purchase
    if ($LASTEXITCODE -ne 0) { throw 'Atomic purchase runner failed.' }
}
finally {
    Pop-Location
}
