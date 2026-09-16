param(
    [ValidateSet(50,200,500,1200)][int]$Agents,
    [ValidateSet('narrow','wide')][string]$Passage = 'wide',
    [ValidateSet(1,4)][int]$Speed = 1
)
$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path -Parent $PSScriptRoot
$output = Join-Path $repoRoot "reports\evidence\M0.10\headless-$Agents-$Passage-${Speed}x.json"
dotnet run --project (Join-Path $repoRoot 'src\Festival.Runner\Festival.Runner.csproj') -c Debug --no-restore -- --benchmark $Agents $Passage $Speed $output
exit $LASTEXITCODE
