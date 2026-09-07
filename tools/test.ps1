[CmdletBinding()]
param(
    [ValidateSet('Debug', 'Release')]
    [string] $Configuration = 'Debug'
)

$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path -Parent $PSScriptRoot
Push-Location $repoRoot
try {
    dotnet test .\tests\Festival.Tests\Festival.Tests.csproj --configuration $Configuration --no-restore
    if ($LASTEXITCODE -ne 0) { throw 'Tests failed.' }
}
finally {
    Pop-Location
}
