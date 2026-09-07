$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path -Parent $PSScriptRoot
$manifest = Get-Content -LiteralPath (Join-Path $repoRoot 'eng\toolchain.json') -Raw | ConvertFrom-Json
$editorRoot = Join-Path $repoRoot ".tools\godot\$($manifest.godot.version)\editor"
$godotExe = Get-ChildItem -LiteralPath $editorRoot -Recurse -File -Filter '*mono_win64_console.exe' -ErrorAction SilentlyContinue |
    Select-Object -First 1

if (-not $godotExe) {
    throw 'Pinned Godot toolchain not found. Run .\tools\bootstrap-toolchain.ps1 first.'
}
