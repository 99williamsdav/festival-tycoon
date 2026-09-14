[CmdletBinding()]
param(
    [string] $EvidenceDirectory = 'briefs\results\M0.06-screenshots'
)

$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path -Parent $PSScriptRoot
$executable = Join-Path $repoRoot 'artifacts\windows\FestivalTycoon.exe'
if (-not (Test-Path -LiteralPath $executable)) {
    throw "Windows export not found at $executable. Run .\tools\export-windows.ps1 first."
}

$evidenceRoot = [IO.Path]::GetFullPath((Join-Path $repoRoot $EvidenceDirectory))
New-Item -ItemType Directory -Force -Path $evidenceRoot | Out-Null

foreach ($resolution in @('1280x720', '1920x1080')) {
    $verification = Join-Path $evidenceRoot "verification-$resolution.txt"
    if (Test-Path -LiteralPath $verification) {
        Remove-Item -LiteralPath $verification -Force
    }

    & $executable -- --capture-farm $evidenceRoot --capture-size $resolution
    $deadline = (Get-Date).AddSeconds(30)
    while (-not (Test-Path -LiteralPath $verification) -and (Get-Date) -lt $deadline) {
        Start-Sleep -Milliseconds 250
    }
    if (-not (Test-Path -LiteralPath $verification)) {
        throw "Timed out waiting for $resolution farm verification."
    }

    $result = Get-Content -LiteralPath $verification -Raw
    if ($result -notmatch 'passed=True') {
        throw "Farm camera verification failed at $resolution.`n$result"
    }
    Write-Output $result.TrimEnd()
}
