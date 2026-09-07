[CmdletBinding()]
param()

. (Join-Path $PSScriptRoot 'common.ps1')
$artifactRoot = Join-Path $repoRoot 'artifacts\windows'
New-Item -ItemType Directory -Force -Path $artifactRoot | Out-Null
$executable = Join-Path $artifactRoot 'FestivalTycoon.exe'
$log = Join-Path $artifactRoot 'export.log'
if (Test-Path -LiteralPath $executable) {
    Remove-Item -LiteralPath $executable -Force
}

$output = & $godotExe.FullName --headless --path (Join-Path $repoRoot 'game') --export-release 'Windows Desktop' $executable 2>&1 |
    Tee-Object -FilePath $log
$godotExitCode = $LASTEXITCODE
$outputText = $output -join [Environment]::NewLine
if ($godotExitCode -ne 0 -or $outputText -match '(?m)^ERROR:') {
    throw "Windows export failed. Inspect $log."
}
if (-not (Test-Path -LiteralPath $executable)) {
    throw "Windows export did not create $executable."
}
