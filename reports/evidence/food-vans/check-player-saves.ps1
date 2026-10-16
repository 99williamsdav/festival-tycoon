param([Parameter(Mandatory=$true)][string]$Output)
$ErrorActionPreference='Stop'
$baseline=Get-Content -LiteralPath (Join-Path $PSScriptRoot 'player-save-pins.json') -Raw|ConvertFrom-Json
$files=@(Get-ChildItem -LiteralPath $baseline.root -Recurse -File|Select-Object -ExpandProperty FullName|Sort-Object)
$directories=@(Get-ChildItem -LiteralPath $baseline.root -Recurse -Directory|Select-Object -ExpandProperty FullName|Sort-Object)
$changed=@($baseline.files|Where-Object {!(Test-Path -LiteralPath $_.path) -or (Get-FileHash -LiteralPath $_.path -Algorithm SHA256).Hash -ne $_.sha256})
$names=@(Compare-Object @($baseline.files.path|Sort-Object) $files)
$dirs=@(Compare-Object @($baseline.directories) $directories)
$result=[ordered]@{passed=($changed.Count -eq 0 -and $names.Count -eq 0 -and $dirs.Count -eq 0);fileCount=$files.Count;directoryCount=$directories.Count;changedFiles=@($changed|Select-Object -ExpandProperty path);fileSetDifferences=$names;directoryDifferences=$dirs}
$result|ConvertTo-Json -Depth 5|Set-Content -LiteralPath $Output -Encoding utf8
if(!$result.passed){throw 'Player ordinary/development save profiles changed'}
$result|ConvertTo-Json -Depth 5 -Compress
