[CmdletBinding()]
param(
    [ValidateSet('Debug', 'Release')]
    [string] $Configuration = 'Debug'
)

$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path -Parent $PSScriptRoot
$validPath = Join-Path $repoRoot 'content\scenarios\m0-valid.json'
$invalidPath = Join-Path $repoRoot 'tests\fixtures\content\m0-invalid.json'
Push-Location $repoRoot
try {
    dotnet run --project .\src\Festival.Runner\Festival.Runner.csproj --configuration $Configuration --no-build -- --content-catalogue $validPath $invalidPath
    if ($LASTEXITCODE -ne 0) { throw 'Content catalogue runner failed.' }
}
finally {
    Pop-Location
}
