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

# Godot reports progress on stderr. Under Windows PowerShell 5.1 with 'Stop', a redirected stderr
# line from a native program is a terminating error, so the export runs with 'Continue' and is
# judged by its exit code, its log and the executable it writes.
$previousPreference = $ErrorActionPreference
$ErrorActionPreference = 'Continue'
try {
    $output = & $godotExe.FullName --headless --path (Join-Path $repoRoot 'game') --export-release 'Windows Desktop' $executable 2>&1 |
        ForEach-Object { "$_" }
    $godotExitCode = $LASTEXITCODE
}
finally {
    $ErrorActionPreference = $previousPreference
}
$escape = [char]27
$plain = $output | ForEach-Object { $_ -replace "$escape\[[0-9;]*m", '' }
$plain | Set-Content -LiteralPath $log -Encoding utf8
# Re-importing textures extracted from models logs a harmless "get_multiple_md5" condition while the
# file does not exist yet; any other ERROR fails the export.
$text = ($plain -join "`n") -replace '(?m)^ERROR: Condition "f\.is_null\(\)" is true\. Continuing\.\n\s*at: get_multiple_md5 [^\n]*', ''
if ($godotExitCode -ne 0 -or $text -match '(?m)^ERROR:') {
    throw "Windows export failed (exit $godotExitCode). Inspect $log."
}
if (-not (Test-Path -LiteralPath $executable)) {
    throw "Windows export did not create $executable."
}
# The export re-imports assets, and Godot rewrites .import files with different line endings from the
# checkout. Restore only those whose content is unchanged, so real edits are never touched.
$flagged = @(git -C $repoRoot status --porcelain -- '*.import' | Where-Object { $_ -match '^ M ' } | ForEach-Object { $_.Substring(3) })
$edited = @(git -C $repoRoot diff --name-only -- '*.import')
$lineEndingsOnly = @($flagged | Where-Object { $edited -notcontains $_ })
if ($lineEndingsOnly.Count -gt 0) {
    git -C $repoRoot checkout -- $lineEndingsOnly
}
$built = Get-Item -LiteralPath $executable
Write-Host "Exported $($built.FullName) ($([math]::Round($built.Length / 1MB)) MB, $($built.LastWriteTime))"
