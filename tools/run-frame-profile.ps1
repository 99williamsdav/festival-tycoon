[CmdletBinding()]
param(
    # Guests in the opening roster; 20 is the ordinary Tier 1 campaign.
    [int] $Guests = 20,
    # Seconds of live play to sample after a short warm-up.
    [int] $Seconds = 60,
    [string] $Output = 'artifacts\development\frame-profile.json'
)

# Opens the default Build edition in the editor build and samples per-frame cost:
# simulation, presentation/HUD, render CPU/GPU and draw calls, with p50/p95/p99.
# The project caps frames at run/max_fps, so frame time reflects pacing as well as work.
. (Join-Path $PSScriptRoot 'common.ps1')
$outputPath = [IO.Path]::GetFullPath((Join-Path $repoRoot $Output))
New-Item -ItemType Directory -Force -Path (Split-Path -Parent $outputPath) | Out-Null
if (Test-Path -LiteralPath $outputPath) { Remove-Item -LiteralPath $outputPath -Force }
& $godotExe.FullName --path (Join-Path $repoRoot 'game') -- --profile-build $outputPath $Guests $Seconds --capture-size 1280x720
if ($LASTEXITCODE -ne 0 -or -not (Test-Path -LiteralPath $outputPath)) { throw "Frame profile failed; no result at $outputPath." }
$result = Get-Content -LiteralPath $outputPath -Raw | ConvertFrom-Json
"people={0} frames={1} frame mean={2:N2}ms p95={3:N2}ms p99={4:N2}ms | simulation mean={5:N2}ms | presentation mean={6:N2}ms p99={7:N2}ms | render cpu={8:N2}ms gpu={9:N2}ms" -f `
    $result.people, $result.frameMs.count, $result.frameMs.mean, $result.frameMs.p95, $result.frameMs.p99,
    $result.simulationObservationMs.mean, $result.presentationHudMs.mean, $result.presentationHudMs.p99,
    $result.renderCpuMs.mean, $result.renderGpuMs.mean
