[CmdletBinding()]
param(
    # Release by default: the tests run 3x faster with the JIT optimiser on, and the shipped game (ExportRelease) is
    # optimised too. The game's own build and Godot's launcher stay on Debug; this only picks the test build.
    [ValidateSet('Debug', 'Release')]
    [string] $Configuration = 'Release',
    # The release run: every test, including the [TestCategory("Slow")] soaks, probes and Survival parity guards.
    # Run it before release zips. (-Slow is the old name and still works.)
    [Alias('Slow')]
    [switch] $Full,
    # Only the untagged fast tests, without the everyday set's few slow guards.
    [switch] $Fast,
    # Any MSTest filter instead of a set, e.g. "FullyQualifiedName~RainTests".
    [string] $Filter,
    # Print every test's duration at the end and keep the output in reports/test-timings/.
    [switch] $Timings
)

# Sets (tests run in parallel across all cores; see tests/Festival.Tests/AssemblyInfo.cs):
#   .\tools\test.ps1          everyday: the fast suite plus the [TestCategory("Everyday")] guards (a Tier 1 and a
#                             Tier 2 full day, mid-day and walk-out save round trips). A couple of minutes.
#   .\tools\test.ps1 -Fast    the untagged tests only.
#   .\tools\test.ps1 -Full    everything. About 8-10 minutes on a 20-thread laptop with nothing else running.

$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path -Parent $PSScriptRoot
Push-Location $repoRoot
try {
    $project = '.\tests\Festival.Tests\Festival.Tests.csproj'
    dotnet build $project --configuration $Configuration --no-restore --nologo -v quiet
    if ($LASTEXITCODE -ne 0) { throw 'Test build failed.' }

    $set = if ($Filter) { 'filtered' } elseif ($Full) { 'full' } elseif ($Fast) { 'fast' } else { 'everyday' }
    $arguments = @()
    switch ($set) {
        'filtered' { $arguments += @('--filter', $Filter) }
        'fast' { $arguments += @('--filter', 'TestCategory!=Slow') }
        'everyday' { $arguments += @('--filter', 'TestCategory!=Slow|TestCategory=Everyday') }
    }
    $exe = Join-Path $repoRoot "tests\Festival.Tests\bin\$Configuration\net8.0\Festival.Tests.exe"
    $started = Get-Date
    if ($Timings) {
        $folder = Join-Path $repoRoot 'reports\test-timings'
        New-Item -ItemType Directory -Force $folder | Out-Null
        $log = Join-Path $folder ("{0:yyyy-MM-dd-HHmm}-{1}-{2}.txt" -f $started, $set, $Configuration.ToLowerInvariant())
        & $exe @arguments --show-slowest-tests 5000 --no-ansi --progress off | Tee-Object -FilePath $log
    }
    else {
        & $exe @arguments
    }
    $exitCode = $LASTEXITCODE
    Write-Host ("{0} set ({1}): {2:mm\:ss} wall clock." -f $set, $Configuration, ((Get-Date) - $started))
    if ($Timings) { Write-Host "Timings kept in $log" }
    if ($exitCode -ne 0) { throw 'Tests failed.' }
}
finally {
    Pop-Location
}
