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
#
# The console Godot is a wrapper that returns only once its output pipe closes. The export runs
# 'dotnet publish', whose reused MSBuild nodes and shared Roslyn compiler server outlive it and inherit
# that pipe, so the wrapper would wait until they idle out (up to ~15 minutes). Keep both in-process.
$previousPreference = $ErrorActionPreference
$previousNodeReuse = $env:MSBUILDDISABLENODEREUSE
$previousSharedCompilation = $env:UseSharedCompilation
$ErrorActionPreference = 'Continue'
$env:MSBUILDDISABLENODEREUSE = '1'
$env:UseSharedCompilation = 'false'
try {
    $output = & $godotExe.FullName --headless --path (Join-Path $repoRoot 'game') --export-release 'Windows Desktop' $executable 2>&1 |
        ForEach-Object { "$_" }
    $godotExitCode = $LASTEXITCODE
}
finally {
    $ErrorActionPreference = $previousPreference
    $env:MSBUILDDISABLENODEREUSE = $previousNodeReuse
    $env:UseSharedCompilation = $previousSharedCompilation
}
$escape = [char]27
$plain = $output | ForEach-Object { $_ -replace "$escape\[[0-9;]*m", '' }
$plain | Set-Content -LiteralPath $log -Encoding utf8

# The export re-imports assets, and Godot rewrites .import files with different line endings from the
# checkout. Restore only those whose content is unchanged, so real edits are never touched. This runs
# before judging the export so a failed export leaves the tree clean too.
$flagged = @(git -C $repoRoot status --porcelain -- '*.import' | Where-Object { $_ -match '^ M ' } | ForEach-Object { $_.Substring(3) })
$edited = @(git -C $repoRoot diff --name-only -- '*.import')
$lineEndingsOnly = @($flagged | Where-Object { $edited -notcontains $_ })
if ($lineEndingsOnly.Count -gt 0) {
    git -C $repoRoot checkout -- $lineEndingsOnly
}

# Re-importing textures extracted from models logs a harmless condition in get_multiple_md5 while the
# file does not exist yet; its lines can arrive interleaved, so each is dropped on its own. Any other
# ERROR fails the export.
$errors = $plain | Where-Object {
    $_ -match '^ERROR:' -and $_ -notmatch '^ERROR: Condition "f\.is_null\(\)" is true\. Continuing\.$'
}
if ($godotExitCode -ne 0 -or $errors) {
    throw "Windows export failed (exit $godotExitCode). Inspect $log."
}
if (-not (Test-Path -LiteralPath $executable)) {
    throw "Windows export did not create $executable."
}
$built = Get-Item -LiteralPath $executable
Write-Host "Exported $($built.FullName) ($([math]::Round($built.Length / 1MB)) MB, $($built.LastWriteTime))"
