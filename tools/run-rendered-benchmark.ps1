param(
    [ValidateSet(50,200,500,1200)][int]$Agents = 1200,
    [ValidateSet('narrow','wide')][string]$Passage = 'wide',
    [ValidateRange(1,900)][int]$TimeoutSeconds = 300,
    [string]$EvidenceDirectory = 'reports\evidence\M0.10'
)
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'common.ps1')
$repoRoot = Split-Path -Parent $PSScriptRoot
$evidence = [IO.Path]::GetFullPath((Join-Path $repoRoot $EvidenceDirectory))
New-Item -ItemType Directory -Force -Path $evidence | Out-Null
$output = Join-Path $evidence "rendered-$Agents-$Passage-1280x720.txt"
$exe = Join-Path $repoRoot 'artifacts\windows\FestivalTycoon.exe'
if (Test-Path -LiteralPath $output) { Remove-Item -LiteralPath $output -Force }
$process = $null
try {
    $startInfo = [Diagnostics.ProcessStartInfo]::new()
    $startInfo.FileName = $exe
    $startInfo.WorkingDirectory = $repoRoot
    $startInfo.UseShellExecute = $false
    $startInfo.CreateNoWindow = $true
    $startInfo.WindowStyle = [Diagnostics.ProcessWindowStyle]::Hidden
    foreach ($argument in @('--', '--benchmark-launch', [string]$Agents, $Passage, $output)) {
        $startInfo.ArgumentList.Add($argument)
    }
    $process = [Diagnostics.Process]::new()
    $process.StartInfo = $startInfo
    if (-not $process.Start()) { throw 'Rendered benchmark process did not start.' }
    $deadline = (Get-Date).AddSeconds($TimeoutSeconds)
    while (-not (Test-Path -LiteralPath $output) -and -not $process.HasExited -and (Get-Date) -lt $deadline) {
        Start-Sleep -Milliseconds 250
        $process.Refresh()
    }
    if (Test-Path -LiteralPath $output) {
        $process.WaitForExit(5000) | Out-Null
        if ($process.HasExited -and $process.ExitCode -ne 0) { throw "Rendered benchmark exited with code $($process.ExitCode)." }
        Get-Content -LiteralPath $output
    }
    elseif ($process.HasExited) { throw "Rendered benchmark exited early with code $($process.ExitCode) without evidence." }
    else { throw "Rendered benchmark timed out after $TimeoutSeconds seconds without evidence." }
}
finally {
    if ($null -ne $process -and -not $process.HasExited) {
        Stop-Process -Id $process.Id -Force
        $process.WaitForExit(5000) | Out-Null
    }
}
