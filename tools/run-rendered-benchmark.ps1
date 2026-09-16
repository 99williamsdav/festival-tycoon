param(
    [ValidateSet(50,200,500,1200)][int]$Agents = 1200,
    [ValidateSet('narrow','wide')][string]$Passage = 'wide'
)
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'common.ps1')
$repoRoot = Split-Path -Parent $PSScriptRoot
$evidence = Join-Path $repoRoot 'reports\evidence\M0.10'
New-Item -ItemType Directory -Force -Path $evidence | Out-Null
$output = Join-Path $evidence "rendered-$Agents-$Passage-1280x720.txt"
$exe = Join-Path $repoRoot 'artifacts\windows\FestivalTycoon.exe'
if (Test-Path -LiteralPath $output) { Remove-Item -LiteralPath $output -Force }
& $exe -- --benchmark-launch $Agents $Passage $output
$deadline = (Get-Date).AddMinutes(15)
while (-not (Test-Path -LiteralPath $output) -and (Get-Date) -lt $deadline) { Start-Sleep -Seconds 2 }
if (-not (Test-Path -LiteralPath $output)) { throw 'Rendered benchmark did not write evidence.' }
Get-Content -LiteralPath $output
