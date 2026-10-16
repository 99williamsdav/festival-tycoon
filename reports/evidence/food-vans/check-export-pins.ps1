param([Parameter(Mandatory=$true)][string]$Output)
$ErrorActionPreference='Stop'
$repo='C:/Projects/festival-tycoon'
$checkpoint=Get-Content (Join-Path $PSScriptRoot 'export-checkpoint.json') -Raw|ConvertFrom-Json
$pins=@()
foreach($p in $checkpoint.payload){$pins += [pscustomobject]@{group='payload';path=Join-Path $checkpoint.delivery $p.path;sha256=$p.sha256}}
$overrides=@(Get-Content (Join-Path $PSScriptRoot 'exported-source-overrides.json') -Raw|ConvertFrom-Json)
foreach($p in $checkpoint.sourceFiles){
    $override=@($overrides|Where-Object {$_.path -eq $p.path})
    if($override.Count){
        if($override.Count -ne 1 -or $override[0].sha256 -ne $p.sha256){throw 'Invalid exported source override'}
        $pins += [pscustomobject]@{group='exported-source-snapshot';path=Join-Path $repo $override[0].snapshot;sha256=$p.sha256}
    }else{$pins += [pscustomobject]@{group='source';path=Join-Path $repo $p.path;sha256=$p.sha256}}
}
foreach($p in $checkpoint.preservationPins){$pins += [pscustomobject]@{group='preservation';path=$p.path;sha256=$p.sha256}}
$failures=@($pins|Where-Object {!(Test-Path -LiteralPath $_.path) -or (Get-FileHash -LiteralPath $_.path -Algorithm SHA256).Hash -ne $_.sha256})
$liveDifferences=@($checkpoint.sourceFiles|Where-Object {(Get-FileHash -LiteralPath (Join-Path $repo $_.path) -Algorithm SHA256).Hash -ne $_.sha256}|Select-Object -ExpandProperty path)
$unexpected=@($liveDifferences|Where-Object {$overrides.path -notcontains $_})
if($unexpected.Count){throw ('Unexpected live source differences: '+($unexpected -join ', '))}
$result=[ordered]@{passed=($failures.Count -eq 0);payloadCount=@($checkpoint.payload).Count;sourceCount=@($checkpoint.sourceFiles).Count;exportedSourceSnapshotCount=$overrides.Count;liveSourceDifferences=$liveDifferences;preservationCount=@($checkpoint.preservationPins).Count;failures=$failures}
$result|ConvertTo-Json -Depth 6|Set-Content -LiteralPath $Output -Encoding utf8
if($failures.Count){throw 'Export/source/preservation pin mismatch'}
$result|ConvertTo-Json -Depth 6 -Compress
