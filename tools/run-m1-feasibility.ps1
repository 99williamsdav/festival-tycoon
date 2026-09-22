param(
    [ValidateRange(10,300)][int]$TimeoutSeconds = 120,
    [string]$EvidenceDirectory = 'reports\evidence\M1.00'
)
$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path -Parent $PSScriptRoot
$evidence = [IO.Path]::GetFullPath((Join-Path $repoRoot $EvidenceDirectory))
New-Item -ItemType Directory -Force -Path $evidence | Out-Null
$output = Join-Path $evidence 'shared-world-rendered.txt'
$exe = Join-Path $repoRoot 'artifacts\windows\FestivalTycoon.exe'
foreach ($path in @($output, (Join-Path $evidence 'shared-world-1x.json'), (Join-Path $evidence 'shared-world-4x.json'))) {
    if (Test-Path -LiteralPath $path) { Remove-Item -LiteralPath $path -Force }
}
$process = $null
try {
    $startInfo = [Diagnostics.ProcessStartInfo]::new()
    $startInfo.FileName = $exe
    $startInfo.WorkingDirectory = $repoRoot
    $startInfo.UseShellExecute = $false
    # Keep the exported render window visible: minimized/hidden Windows applications may be
    # presentation-throttled, which would make the 60 FPS gate measurement non-representative.
    $startInfo.CreateNoWindow = $false
    $startInfo.WindowStyle = [Diagnostics.ProcessWindowStyle]::Normal
    foreach ($argument in @('--', '--m1-feasibility-launch', $output, '--capture-size', '1280x720')) { $startInfo.ArgumentList.Add($argument) }
    $process = [Diagnostics.Process]::new()
    $process.StartInfo = $startInfo
    if (-not $process.Start()) { throw 'M1.00 rendered process did not start.' }
    $deadline = (Get-Date).AddSeconds($TimeoutSeconds)
    while (-not (Test-Path -LiteralPath $output) -and -not $process.HasExited -and (Get-Date) -lt $deadline) {
        Start-Sleep -Milliseconds 250
        $process.Refresh()
    }
    if (Test-Path -LiteralPath $output) {
        $process.WaitForExit(5000) | Out-Null
        if ($process.HasExited -and $process.ExitCode -ne 0) { throw "M1.00 rendered process exited with code $($process.ExitCode)." }
        Get-Content -LiteralPath $output
    }
    elseif ($process.HasExited) { throw "M1.00 rendered process exited early with code $($process.ExitCode) without evidence." }
    else { throw "M1.00 rendered process timed out after $TimeoutSeconds seconds without evidence." }
}
finally {
    if ($null -ne $process -and -not $process.HasExited) {
        Stop-Process -Id $process.Id -Force
        $process.WaitForExit(5000) | Out-Null
    }
}
