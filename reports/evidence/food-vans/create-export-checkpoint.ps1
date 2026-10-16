# Historical export checkpoint recipe. Do not rerun on later source: original export was commit 7b467c6d; see exported-source-overrides.json.
$ErrorActionPreference='Stop'
Set-Location 'C:/Projects/festival-tycoon'
$repo='C:/Projects/festival-tycoon'
$workspace='C:/Users/99wil/Documents/ChatGPT/Festival Tycoon'
$delivery=Join-Path $workspace 'playtest-food-vans-2026-10-16'
$prior=Get-Content reports/evidence/tractor/export-checkpoint.json -Raw|ConvertFrom-Json
$inherited=Get-Content reports/evidence/food-vans/inherited-reference-changes.json -Raw|ConvertFrom-Json
$preserve=@($prior.preservationPins|Where-Object {$inherited.path -notcontains $_.path})
foreach($p in $inherited){$preserve += [pscustomobject]@{path=$p.path;group='inherited-pre-food-reference-revision';sha256=$p.inheritedCurrentSha256}}
foreach($p in $prior.payload){$preserve += [pscustomobject]@{path=Join-Path $prior.delivery $p.path;group='previous-tractor-payload';sha256=$p.sha256}}
foreach($p in (Get-Content reports/evidence/food-vans/original-input-pins.json -Raw|ConvertFrom-Json)){$preserve += [pscustomobject]@{path=Join-Path $repo $p.path;group='food-vans-original-input';sha256=$p.sha256}}
foreach($p in (Get-Content reports/evidence/food-vans/player-save-pins.json -Raw|ConvertFrom-Json).files){$preserve += [pscustomobject]@{path=$p.path;group='all-player-save-files';sha256=$p.sha256}}
$package=Join-Path $workspace 'food-van-production-2026-10-16'
foreach($f in Get-ChildItem -LiteralPath $package -Recurse -File){$preserve += [pscustomobject]@{path=$f.FullName;group='approved-original-food-van-package';sha256=(Get-FileHash -LiteralPath $f.FullName -Algorithm SHA256).Hash}}
foreach($p in $preserve){if((Get-FileHash -LiteralPath $p.path -Algorithm SHA256).Hash -ne $p.sha256){throw "Preservation mismatch: $($p.path)"}}
$paths=@($prior.sourceFiles.path)+@(git ls-files -- game src content eng tools)
$source=@($paths|Sort-Object -Unique|ForEach-Object{[pscustomobject]@{path=$_;sha256=(Get-FileHash -LiteralPath $_ -Algorithm SHA256).Hash}})
$payload=@(Get-ChildItem -LiteralPath $delivery -Recurse -File|Where-Object {$_.Name -ne 'README.txt'}|ForEach-Object{[pscustomobject]@{path=$_.FullName.Substring($delivery.Length+1).Replace('\','/');bytes=$_.Length;sha256=(Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash}})
$checkpoint=[ordered]@{date=(Get-Date).ToString('o');delivery=$delivery;sourceCommit=(git rev-parse HEAD);assetCommit='6d361ea6';exportExit=0;exportLogSha256=(Get-FileHash artifacts/windows/export.log -Algorithm SHA256).Hash;payloadCount=$payload.Count;payloadBytes=($payload|Measure-Object bytes -Sum).Sum;payload=$payload;sourceFiles=$source;preservationPins=$preserve;caveat='Working-tree export with95preserved original inputs. No simulation/economy/footprint change. All233player files and52directories separately guarded against diagnostic writes. No performance claim.'}
$checkpoint|ConvertTo-Json -Depth 10|Set-Content reports/evidence/food-vans/export-checkpoint.json -Encoding utf8
[pscustomobject]@{payloadCount=$payload.Count;payloadBytes=$checkpoint.payloadBytes;sourcePins=$source.Count;preservationPins=$preserve.Count;exe=($payload|Where-Object path -eq 'FestivalTycoon.exe').sha256}|ConvertTo-Json -Compress


