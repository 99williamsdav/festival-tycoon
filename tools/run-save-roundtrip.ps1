[CmdletBinding()]
param(
    [ValidateSet('Debug', 'Release')]
    [string] $Configuration = 'Debug',
    [string] $Slot = 'm0-demo'
)

$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path -Parent $PSScriptRoot
$tempRoot = [IO.Path]::GetFullPath([IO.Path]::GetTempPath())
$demoDirectory = Join-Path $tempRoot ("festival-save-demo-" + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $demoDirectory | Out-Null
Push-Location $repoRoot
try {
    dotnet run --project .\src\Festival.Runner\Festival.Runner.csproj --configuration $Configuration --no-build -- --save-roundtrip $demoDirectory $Slot
    if ($LASTEXITCODE -ne 0) { throw 'Save roundtrip runner failed.' }
}
finally {
    Pop-Location
    $resolvedDemo = [IO.Path]::GetFullPath($demoDirectory)
    if ($resolvedDemo.StartsWith($tempRoot, [StringComparison]::OrdinalIgnoreCase) -and (Test-Path -LiteralPath $resolvedDemo)) {
        Remove-Item -LiteralPath $resolvedDemo -Recurse -Force
    }
}
