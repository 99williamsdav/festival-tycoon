[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path -Parent $PSScriptRoot
$manifest = Get-Content -LiteralPath (Join-Path $repoRoot 'eng\toolchain.json') -Raw | ConvertFrom-Json
$version = $manifest.godot.version
$toolRoot = Join-Path $repoRoot ".tools\godot\$version"
$downloadRoot = Join-Path $repoRoot '.tools\downloads'
$editorArchive = Join-Path $downloadRoot "Godot_v$version-stable_mono_win64.zip"
$templateArchive = Join-Path $downloadRoot "Godot_v$version-stable_mono_export_templates.tpz"
$editorRoot = Join-Path $toolRoot 'editor'
$templateStaging = Join-Path $toolRoot 'template-staging'

New-Item -ItemType Directory -Force -Path $downloadRoot,$toolRoot | Out-Null

function Get-VerifiedArchive {
    param(
        [Parameter(Mandatory)] [string] $Path,
        [Parameter(Mandatory)] [string] $Url,
        [Parameter(Mandatory)] [string] $ExpectedHash
    )

    if (-not (Test-Path -LiteralPath $Path)) {
        Write-Host "Downloading $(Split-Path -Leaf $Path)..."
        Invoke-WebRequest -UseBasicParsing -Uri $Url -OutFile $Path
    }

    $actualHash = (Get-FileHash -LiteralPath $Path -Algorithm SHA256).Hash
    if ($actualHash -ne $ExpectedHash) {
        throw "SHA-256 mismatch for $Path. Expected $ExpectedHash, got $actualHash."
    }
}

Get-VerifiedArchive -Path $editorArchive -Url $manifest.godot.editorUrl -ExpectedHash $manifest.godot.editorSha256
Get-VerifiedArchive -Path $templateArchive -Url $manifest.godot.templatesUrl -ExpectedHash $manifest.godot.templatesSha256

if (-not (Test-Path -LiteralPath $editorRoot)) {
    New-Item -ItemType Directory -Path $editorRoot | Out-Null
    Expand-Archive -LiteralPath $editorArchive -DestinationPath $editorRoot
}

$godotExe = Get-ChildItem -LiteralPath $editorRoot -Recurse -File -Filter '*mono_win64_console.exe' |
    Select-Object -First 1
if (-not $godotExe) {
    throw "The Godot .NET console executable was not found below $editorRoot."
}

$selfContainedMarker = Join-Path $godotExe.DirectoryName '_sc_'
if (-not (Test-Path -LiteralPath $selfContainedMarker)) {
    New-Item -ItemType File -Path $selfContainedMarker | Out-Null
}

$templateRoot = Join-Path $godotExe.DirectoryName "editor_data\export_templates\$version.stable.mono"
$releaseTemplate = Join-Path $templateRoot 'windows_release_x86_64.exe'
if (-not (Test-Path -LiteralPath $releaseTemplate)) {
    if (Test-Path -LiteralPath $templateStaging) {
        $resolvedStaging = [IO.Path]::GetFullPath($templateStaging)
        $resolvedToolRoot = [IO.Path]::GetFullPath($toolRoot) + [IO.Path]::DirectorySeparatorChar
        if (-not $resolvedStaging.StartsWith($resolvedToolRoot, [StringComparison]::OrdinalIgnoreCase)) {
            throw "Refusing to clean template staging outside $toolRoot."
        }
        Remove-Item -LiteralPath $resolvedStaging -Recurse -Force
    }

    New-Item -ItemType Directory -Path $templateStaging | Out-Null
    Add-Type -AssemblyName System.IO.Compression.FileSystem
    [IO.Compression.ZipFile]::ExtractToDirectory($templateArchive, $templateStaging)
    $extractedTemplates = Get-ChildItem -LiteralPath $templateStaging -Recurse -Directory |
        Where-Object Name -eq 'templates' |
        Select-Object -First 1
    if (-not $extractedTemplates) {
        throw "No templates directory was found in $templateArchive."
    }

    New-Item -ItemType Directory -Force -Path $templateRoot | Out-Null
    Copy-Item -Path (Join-Path $extractedTemplates.FullName '*') -Destination $templateRoot -Recurse -Force
    Remove-Item -LiteralPath $templateStaging -Recurse -Force
}

Write-Host "Godot .NET $version ready: $($godotExe.FullName)"
Write-Host "Export templates ready: $templateRoot"
