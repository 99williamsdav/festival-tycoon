param(
    [Parameter(Mandatory=$true)][string]$Delivery,
    [Parameter(Mandatory=$true)][string]$InputSave,
    [Parameter(Mandatory=$true)][string]$OutputDirectory
)
$ErrorActionPreference='Stop'
# Evidence-only offline benchmark. Uses the copied release assemblies; never modifies inputs.
$runtime=Join-Path $Delivery 'data_Festival.Game_windows_x86_64'
[void][Reflection.Assembly]::LoadFrom((Join-Path $runtime 'Festival.Simulation.dll'))
[void][Reflection.Assembly]::LoadFrom((Join-Path $runtime 'Festival.Persistence.dll'))
$inputHash=(Get-FileHash -LiteralPath $InputSave -Algorithm SHA256).Hash
$stream=[IO.File]::OpenRead($InputSave)
$gzip=[IO.Compression.GZipStream]::new($stream,[IO.Compression.CompressionMode]::Decompress)
try {
    $document=[Text.Json.JsonDocument]::Parse($gzip)
    $header=$document.RootElement.GetProperty('header')
    $compat=[Festival.Persistence.SaveCompatibility]::new($header.GetProperty('buildId').GetString(),$header.GetProperty('contentHash').GetString(),$header.GetProperty('rulesetHash').GetString())
} finally { if($document){$document.Dispose()};$gzip.Dispose();$stream.Dispose() }
$loaded=[Festival.Persistence.SaveFileAdapter]::LoadFile($InputSave,$compat)
if(!$loaded.IsSuccess){throw $loaded.Error}
$snapshot=$loaded.Session.CapturePersistenceSnapshot()
$hash=$snapshot.AuthoritativeHash
New-Item -ItemType Directory -Path $OutputDirectory -Force|Out-Null
$writes=Join-Path $OutputDirectory 'rotation-saves'
New-Item -ItemType Directory -Path $writes -Force|Out-Null
$samples=[Collections.Generic.List[double]]::new()
for($i=0;$i -lt 13;$i++) {
    $watch=[Diagnostics.Stopwatch]::StartNew()
    $saved=[Festival.Persistence.AutosaveRotation]::SaveCaptured($writes,$snapshot,$compat,[DateTimeOffset]::UtcNow,[long]$i,$null)
    $watch.Stop()
    if(!$saved.IsSuccess){throw $saved.Error}
    if($i -ge 3){$samples.Add($watch.Elapsed.TotalMilliseconds)}
    $roundtrip=[Festival.Persistence.SaveFileAdapter]::LoadFile($saved.Path,$compat)
    if(!$roundtrip.IsSuccess -or $roundtrip.Session.CaptureSnapshot().AuthoritativeHash -ne $hash){throw 'Benchmark save failed exact hash roundtrip'}
}
$sorted=@($samples|Sort-Object)
$people=$loaded.Session.CapturePreparation().People
$result=[pscustomobject]@{
    diagnostic='Offline production AutosaveRotation.SaveCaptured from actual copied release assemblies'
    inputSave=$InputSave;inputSha256=$inputHash;authoritativeHash=$hash
    simulationDllSha256=(Get-FileHash -LiteralPath (Join-Path $runtime 'Festival.Simulation.dll') -Algorithm SHA256).Hash
    persistenceDllSha256=(Get-FileHash -LiteralPath (Join-Path $runtime 'Festival.Persistence.dll') -Algorithm SHA256).Hash
    people=$people.Length;attendees=@($people|Where-Object Role -eq Guest).Count;stages=$loaded.Session.Stages.Count;tick=$loaded.Session.CurrentTick
    warmupWrites=3;samples=$samples.ToArray();count=$samples.Count;meanMs=($samples|Measure-Object -Average).Average;p95Ms=$sorted[[int][Math]::Ceiling($sorted.Length*.95)-1];maximumMs=$sorted[-1]
    allThirteenSavedAndExactHashRestored=$true
    limitation='Offline unchanged captured state, after three warmup writes. Includes production envelope, gzip/write-through flush, temporary/prior validation and slot replacement. Excludes foreground snapshot capture, concurrent game/render work and worker scheduling. Ten samples; nearest-rank p95 equals maximum. Not a frame-blocking duration.'
}
$result|ConvertTo-Json -Depth 4|Set-Content -LiteralPath (Join-Path $OutputDirectory 'summary.json') -Encoding utf8
$result|Select-Object people,attendees,stages,count,p95Ms,maximumMs,allThirteenSavedAndExactHashRestored|ConvertTo-Json -Compress
