param(
    [Parameter(Mandatory = $true)][ValidateSet(50, 100, 200)][int]$Population,
    [Parameter(Mandatory = $true)][ValidateSet('representative', 'congested')][string]$Layout,
    [Parameter(Mandatory = $true)][uint64]$Seed,
    [Parameter(Mandatory = $true)][string]$Output,
    [int]$TimeoutSeconds = 120
)

$ErrorActionPreference = 'Stop'
$repo = Split-Path -Parent $PSScriptRoot
$revision = (git -C $repo rev-parse HEAD).Trim()
$sourceFiles = @(
    'src/Festival.Runner/Program.cs',
    'src/Festival.Simulation/GameSession.cs',
    'src/Festival.Simulation/GameSession.Navigation.cs',
    'src/Festival.Simulation/Navigation.cs',
    'src/Festival.Simulation/ServiceQueue.cs',
    'src/Festival.Simulation/Fixtures/ScaleDiagnosticFixture.cs',
    'src/Festival.Simulation/ScaleDiagnosticProbe.cs'
)
$sourceHashes = $sourceFiles | ForEach-Object { git -C $repo hash-object $_ }
$fingerprint = ($sourceHashes | git hash-object --stdin).Trim()
$project = Join-Path $repo 'src/Festival.Runner/Festival.Runner.csproj'
$absoluteOutput = [System.IO.Path]::GetFullPath((Join-Path $repo $Output))
$dotnetPath = (Get-Command dotnet -ErrorAction Stop).Source
$arguments = @('run', '--project', $project, '-c', 'Release', '--no-build', '--',
    '--scale-diagnostic', $Population, $Layout, $Seed, $absoluteOutput, $revision, $fingerprint)
$child = Start-Process -FilePath $dotnetPath -ArgumentList $arguments -PassThru -WindowStyle Hidden
try {
    if (-not $child.WaitForExit($TimeoutSeconds * 1000)) {
        $child.Kill($true)
        $child.WaitForExit()
        throw "Scale diagnostic timed out after $TimeoutSeconds seconds; exact child $($child.Id) ($dotnetPath) was terminated. Partial checkpoint: $absoluteOutput"
    }
    if ($child.ExitCode -ne 0) { throw "Scale diagnostic child exited with code $($child.ExitCode)." }
}
finally {
    if (-not $child.HasExited) { $child.Kill($true); $child.WaitForExit() }
}
