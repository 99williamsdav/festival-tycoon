[CmdletBinding()]
param(
    [ValidateSet('Debug', 'Release')]
    [string] $Configuration = 'Debug',
    # Also run the soak tests tagged [TestCategory("Slow")] (whole editions, benchmarks, feasibility sweeps).
    [switch] $Slow
)

$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path -Parent $PSScriptRoot
Push-Location $repoRoot
try {
    $arguments = @('test', '.\tests\Festival.Tests\Festival.Tests.csproj', '--configuration', $Configuration, '--no-restore')
    if (-not $Slow) { $arguments += @('--', '--filter', 'TestCategory!=Slow') }
    dotnet @arguments
    if ($LASTEXITCODE -ne 0) { throw 'Tests failed.' }
}
finally {
    Pop-Location
}
