param([string]$EvidenceRoot)
$ErrorActionPreference='Stop'
$items=@()
foreach($file in Get-ChildItem -LiteralPath $EvidenceRoot -Recurse -Filter '*.ftsave') {
 $f=[IO.File]::OpenRead($file.FullName)
 $z=[IO.Compression.GZipStream]::new($f,[IO.Compression.CompressionMode]::Decompress)
 $r=[IO.StreamReader]::new($z)
 try {$s=$r.ReadToEnd()|ConvertFrom-Json} finally {$r.Dispose()}
 $p=$s.payload
 $items += [pscustomobject]@{file=$file.FullName;sha256=(Get-FileHash -LiteralPath $file.FullName).Hash;geometry=$p.barnGeometryVersion;identity=$p.identity;tick=$p.currentTick;status=$p.preparation.status;people=@($p.preparation.people).Count;admitted=@($p.preparation.people|Where-Object admitted).Count;departed=@($p.preparation.people|Where-Object departed).Count;performanceRecordCount=if($p.performanceRecords){@($p.performanceRecords).Count}else{0};noRoute=@($p.navigationAgents|Where-Object action -eq 3).Count}
}
$items|ConvertTo-Json -Depth 6|Set-Content (Join-Path $EvidenceRoot 'saved-state-inspection.json')
$items|Where-Object {$_.file -match 'finished|live.ftsave|manual|tier-two'}|ConvertTo-Json -Depth 6
