[CmdletBinding()]
param(
    [switch] $Headless,
    [ValidateRange(0, 3600)]
    [int] $QuitAfter = 0
)

. (Join-Path $PSScriptRoot 'common.ps1')
$artifactRoot = Join-Path $repoRoot 'artifacts\development'
$log = Join-Path $artifactRoot 'run-game.log'
New-Item -ItemType Directory -Force -Path $artifactRoot | Out-Null

$godotArguments = @('--path', (Join-Path $repoRoot 'game'))
if ($Headless) {
    $godotArguments += '--headless'
}
if ($QuitAfter -gt 0) {
    $godotArguments += @('--quit-after', $QuitAfter)
}

$output = & $godotExe.FullName @godotArguments 2>&1 | Tee-Object -FilePath $log
$godotExitCode = $LASTEXITCODE
$outputText = $output -join [Environment]::NewLine
if ($godotExitCode -ne 0 -or $outputText -match '(?m)^ERROR:') {
    throw "Godot game failed. Inspect $log."
}
if ($outputText -notmatch 'FESTIVAL_TYCOON_LAUNCHED build=') {
    throw "Godot exited without the application startup marker. Inspect $log."
}
