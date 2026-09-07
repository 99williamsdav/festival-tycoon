[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
$repoRoot = [IO.Path]::GetFullPath((Split-Path -Parent $PSScriptRoot))
$targets = @(
    (Join-Path $repoRoot 'artifacts'),
    (Join-Path $repoRoot 'game\.godot'),
    (Join-Path $repoRoot 'game\bin'),
    (Join-Path $repoRoot 'game\obj'),
    (Join-Path $repoRoot 'src\Festival.Runner\bin'),
    (Join-Path $repoRoot 'src\Festival.Runner\obj'),
    (Join-Path $repoRoot 'src\Festival.Simulation\bin'),
    (Join-Path $repoRoot 'src\Festival.Simulation\obj'),
    (Join-Path $repoRoot 'tests\Festival.Tests\bin'),
    (Join-Path $repoRoot 'tests\Festival.Tests\obj')
)

$rootPrefix = $repoRoot + [IO.Path]::DirectorySeparatorChar
foreach ($target in $targets) {
    $resolved = [IO.Path]::GetFullPath($target)
    if (-not $resolved.StartsWith($rootPrefix, [StringComparison]::OrdinalIgnoreCase)) {
        throw "Refusing to remove generated path outside repository: $resolved"
    }
    if (Test-Path -LiteralPath $resolved) {
        Write-Host "Removing generated path: $resolved"
        Remove-Item -LiteralPath $resolved -Recurse -Force
    }
}
