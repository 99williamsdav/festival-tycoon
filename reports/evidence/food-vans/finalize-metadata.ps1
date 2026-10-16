$ErrorActionPreference='Stop'
Set-Location 'C:/Projects/festival-tycoon'
$workspace='C:/Users/99wil/Documents/ChatGPT/Festival Tycoon'
$paths=@('briefs/results/food-vans-integration-handoff.md','briefs/results/food-vans-integration-independent-review.md','briefs/results/food-vans-exe-independent-review.md','assets/source/DESIGN_INDEX.md','assets/source/environment/food-van-production-2026-10-16/INTEGRATION.md')
$paths+=@(Get-ChildItem reports/evidence/food-vans -Recurse -File|Where-Object {$_.Extension -notin @('.ftsave','.bak') -and $_.Name -notlike 'clip-*.png' -and $_.Name -ne 'final-metadata-pins.json'}|Select-Object -ExpandProperty FullName)
$paths+=@(Join-Path $workspace 'CURRENT-TASK.txt')
$paths+=@(Join-Path $workspace 'playtest-food-vans-2026-10-16/README.txt')
foreach($name in @('food-vans-integration-handoff.md','food-vans-integration-independent-review.md','food-vans-exe-independent-review.md')){$paths+=@(Join-Path $workspace $name)}
foreach($frame in @('0000','0140','0280','0419')){$paths+=@('reports/evidence/food-vans/independent-exe/stress/clip-'+$frame+'.png')}
$rows=@($paths|Sort-Object -Unique|ForEach-Object {$p=(Resolve-Path -LiteralPath $_).Path; [pscustomobject]@{path=$p;sha256=(Get-FileHash -LiteralPath $p -Algorithm SHA256).Hash}})
$rows|ConvertTo-Json -Depth 4|Set-Content reports/evidence/food-vans/final-metadata-pins.json -Encoding utf8
'Pinned metadata files: '+$rows.Count
