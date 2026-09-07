[CmdletBinding()]
param(
    [ValidateSet('Debug', 'Release')]
    [string] $Configuration = 'Debug'
)

$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path -Parent $PSScriptRoot
Push-Location $repoRoot
try {
    dotnet restore .\FestivalTycoon.sln --locked-mode
    if ($LASTEXITCODE -ne 0) { throw 'dotnet restore failed.' }

    dotnet build .\FestivalTycoon.sln --configuration $Configuration --no-restore
    if ($LASTEXITCODE -ne 0) { throw 'dotnet build failed.' }
}
finally {
    Pop-Location
}
