param([Parameter(Mandatory)][string]$InputCache, [Parameter(Mandatory)][string]$OutputCache)
$ErrorActionPreference='Stop'
$rlManifest=Get-Content -LiteralPath ($InputCache+'.manifest.json') -Raw|ConvertFrom-Json
if($rlManifest.badRows -or $rlManifest.outOfOrder -or $rlManifest.dropped){throw 'Reject corrupt/source-order-invalid cache'}
if((Get-FileHash -LiteralPath $InputCache -Algorithm SHA256).Hash -ne $rlManifest.cacheSha256){throw 'Cache SHA mismatch'}
if(Test-Path -LiteralPath $OutputCache){throw 'Immutable output exists'}
function Read-RlCache([string]$rlPath) {
    $rlFile=[IO.File]::OpenRead($rlPath)
    return [IO.StreamReader]::new([IO.Compression.BrotliStream]::new($rlFile,[IO.Compression.CompressionMode]::Decompress))
}
$rlReader=Read-RlCache $InputCache
$rlLastGap=$null
try {while($null -ne ($rlLine=$rlReader.ReadLine())) {
    $rlDoc=[Text.Json.JsonDocument]::Parse($rlLine)
    if($rlDoc.RootElement.GetProperty('dataGap').GetBoolean()){$rlLastGap=$rlDoc.RootElement.GetProperty('at').GetDateTimeOffset()}
    $rlDoc.Dispose()
}} finally {$rlReader.Dispose()}
if($null -eq $rlLastGap){throw 'No gap; use the original cache'}
# 60-second features use the sample at the left boundary; add one full 5-second interval.
$rlCut=$rlLastGap.AddSeconds(65)
$rlReader=Read-RlCache $InputCache
$rlWriter=[IO.StreamWriter]::new([IO.Compression.BrotliStream]::new([IO.File]::Create($OutputCache),[IO.Compression.CompressionLevel]::Fastest))
$rlCount=0; $rlValid=0
try {while($null -ne ($rlLine=$rlReader.ReadLine())) {
    $rlDoc=[Text.Json.JsonDocument]::Parse($rlLine); $rlRoot=$rlDoc.RootElement
    if($rlRoot.GetProperty('at').GetDateTimeOffset() -ge $rlCut) {
        if($rlRoot.GetProperty('dataGap').GetBoolean()){throw 'Gap in retained range'}
        $rlWriter.WriteLine($rlLine); $rlCount++
        if($rlRoot.GetProperty('valid').GetBoolean()){$rlValid++}
    }
    $rlDoc.Dispose()
}} finally {$rlReader.Dispose();$rlWriter.Dispose()}
if($rlCount -lt 2 -or $rlValid -eq 0){throw 'No usable retained episode'}
$rlParent=$rlManifest.cacheSha256
$rlManifest.cacheSha256=(Get-FileHash -LiteralPath $OutputCache -Algorithm SHA256).Hash
$rlManifest.schema='nikkei-rl-cache-v2-derived'
$rlManifest.frames=$rlCount; $rlManifest.validFrames=$rlValid; $rlManifest.dataGapFrames=0
$rlManifest|Add-Member parentCacheSha256 $rlParent
$rlManifest|Add-Member retainedFrom $rlCut.ToString('o')
$rlManifest|Add-Member excludedReason 'Exclude prefix through last raw gap plus 65-second causal feature warmup'
$rlManifest|ConvertTo-Json -Depth 10|Set-Content -LiteralPath ($OutputCache+'.manifest.json') -Encoding utf8
$rlManifest|ConvertTo-Json -Compress
