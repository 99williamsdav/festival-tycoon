[CmdletBinding()]
param([string] $EvidenceDirectory = 'briefs\results\M0.09-foundation')

$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path -Parent $PSScriptRoot
$executable = Join-Path $repoRoot 'artifacts\windows\FestivalTycoon.exe'
if (-not (Test-Path -LiteralPath $executable)) { throw "Windows export not found. Run .\tools\export-windows.ps1 first." }
$evidenceRoot = [IO.Path]::GetFullPath((Join-Path $repoRoot $EvidenceDirectory))
New-Item -ItemType Directory -Force -Path $evidenceRoot | Out-Null
$verification = Join-Path $evidenceRoot 'verification-1280x720.txt'
if (Test-Path -LiteralPath $verification) { Remove-Item -LiteralPath $verification -Force }
& $executable -- --capture-foundation $evidenceRoot --capture-size 1280x720
$deadline = (Get-Date).AddSeconds(150)
while (-not (Test-Path -LiteralPath $verification) -and (Get-Date) -lt $deadline) { Start-Sleep -Milliseconds 250 }
if (-not (Test-Path -LiteralPath $verification)) { throw "Timed out waiting for M0.09 foundation verification." }
$result = Get-Content -LiteralPath $verification -Raw
if ($result -notmatch 'passed=True') { throw "Foundation verification failed.`n$result" }
Write-Output $result.TrimEnd()
