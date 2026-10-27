param(
    [Parameter(Mandatory)][string]$UnityReport,
    [Parameter(Mandatory)][string]$BridgeStatus,
    [string]$UnityProjectRoot='D:/Document/download/md/KoikatsuSunshineModdingTools-main'
)
. "$PSScriptRoot/Common.ps1"
$fingerprint=Get-SourceFingerprint
$paths=@{
    audit='artifacts/audit/report.json'; model='artifacts/core-model-tests/receipt.json'
    tooling='artifacts/tooling-tests/report.json'
}
foreach($entry in $paths.GetEnumerator()) {
    $receipt=Get-Content (Join-Path $RepoRoot $entry.Value) -Raw|ConvertFrom-Json
    if(-not $receipt.passed -or $receipt.sourceFingerprint -ne $fingerprint){throw "Stale $($entry.Key) gate"}
}
$report=Get-Content $UnityReport -Raw|ConvertFrom-Json
$evidence=Split-Path ([IO.Path]::GetFullPath($UnityReport)) -Parent
$presets=Get-Content (Join-Path $evidence 'presets/report.json') -Raw|ConvertFrom-Json
$regression=Get-Content (Join-Path $evidence 'post-import-report.json') -Raw|ConvertFrom-Json
if(-not $report.passed -or $report.checks.Count -lt 40 -or @($report.checks|Where-Object {-not $_.passed}).Count -or $report.nativePixelDifference -ne 0 -or $report.earlyPixelDifference -ne 0 -or $report.opaqueSnapshotDifference -gt 1){throw 'Step-2 behavior/appearance gates incomplete'}
if(-not $presets.passed -or $presets.checks.Count -lt 29 -or @($presets.checks|Where-Object {-not $_.passed}).Count -or -not $regression.passed -or $regression.identities.Count -ne 14){throw 'Post-import preset/identity gate incomplete'}
$frozen=Get-Content (Join-Path $RepoRoot 'migration/phase1/authoring-identities.json') -Raw|ConvertFrom-Json
foreach($expected in $frozen.identities) {
    $match=@($regression.identities|Where-Object {$_.type -ceq $expected.type -and $_.path -ceq $expected.path -and $_.assembly -ceq $expected.assembly -and $_.guid -ceq $expected.guid -and $_.localFileId -eq $expected.localFileId})
    if($match.Count -ne 1){throw "Original authoring identity drift: $($expected.type)"}
}
$old=Join-Path $RepoRoot 'artifacts/integration/accepted-step1/observer/baseline-presets.png'
if((Get-Hash (Join-Path $evidence 'presets/baseline-presets.png')) -ne (Get-Hash $old)){throw 'Original preset pixels changed'}
$status=Get-Content $BridgeStatus -Raw|ConvertFrom-Json
if(-not $status.success -or $status.message -notmatch 'loaded=True; hash=([A-F0-9]{128})'){throw 'Missing loaded-binary proof'}
$loaded=$Matches[1]
$core=Join-Path $RepoRoot 'artifacts/bin/TomRender.Core/Release/TomRender.Core.dll'
$validator=Join-Path $RepoRoot 'artifacts/bin/TomRender.UnityValidation/Release/TomRender.UnityValidation.dll'
$runtime=Join-Path $RepoRoot 'artifacts/bin/TomBSDF.Runtime/Release/TomBSDF.Runtime.dll'
if($loaded -cne ((Get-Hash $core)+(Get-Hash $validator)).ToUpperInvariant()){throw 'Different Core/validation pair rendered'}
$imports=@(
    @($core,'Assets/TomRenderInfrastructure/Runtime/TomRender.Core.dll'),
    @($runtime,'Assets/Mods/TomShadersBSDF/Runtime/TomBSDF.Runtime.dll'),
    @((Join-Path $RepoRoot 'src/SceneEffects/TomSceneEffects.Runtime/DofCompatibility.cs'),'Assets/TomSceneEffects/Runtime/DofCompatibility.cs'),
    @((Join-Path $RepoRoot 'src/SceneEffects/TomSceneEffects.Runtime/TomDofOpaqueRelay.cs'),'Assets/TomSceneEffects/Runtime/TomDofOpaqueRelay.cs'),
    @((Join-Path $RepoRoot 'integration/UnityEditor/TomRenderInfrastructureBridge.cs'),'Assets/Editor/TomRenderInfrastructureBridge.cs')
)
foreach($pair in $imports){if((Get-Hash $pair[0]) -ne (Get-Hash (Join-Path $UnityProjectRoot $pair[1]))){throw "Stale authoring import: $($pair[1])"}}
$archive=Join-Path $RepoRoot 'artifacts/integration/accepted-step2'
if(Test-Path $archive){throw 'Accepted step-2 evidence is immutable'}
$null=New-Item -ItemType Directory -Path $archive
Copy-Item -LiteralPath $evidence -Destination (Join-Path $archive 'unity') -Recurse
Copy-Item -LiteralPath $core,$validator,$runtime -Destination $archive
Copy-Item -LiteralPath $BridgeStatus -Destination (Join-Path $archive 'bridge-loaded-proof.json')
foreach($entry in $paths.GetEnumerator()){Copy-Item -LiteralPath (Join-Path $RepoRoot $entry.Value) -Destination (Join-Path $archive "$($entry.Key)-receipt.json")}
$pairs=@();for($i=0;$i -lt $report.nativeCpuMs.Count;$i++){$pairs+=($report.earlyCpuMs[$i]-$report.nativeCpuMs[$i])}
$sorted=@($pairs|Sort-Object)
Write-Json (Join-Path $archive 'acceptance.json') ([ordered]@{
    schema=1;step=2;passed=$true;acceptedUtc=[DateTime]::UtcNow.ToString('o');sourceFingerprint=$fingerprint
    unityChecks=$report.checks.Count;presetObserverChecks=$presets.checks.Count;authoringIdentities=$regression.identities.Count
    nativePixelDifference=$report.nativePixelDifference;earlyPixelDifference=$report.earlyPixelDifference;opaqueSnapshotDifference=$report.opaqueSnapshotDifference
    pairedMedianCpuRoutingDeltaMs=$sorted[[int]($sorted.Count/2)];performanceScope=$report.scope
    coreSha256=(Get-Hash $core);runtimeSha256=(Get-Hash $runtime);validatorSha256=(Get-Hash $validator)
    limitation='Authoring/blur-surrogate behavior accepted. Native Studio DOF/game not deployed or tested. Steps 3 and 4 remain outstanding; this does not authorize staging.'
})
Write-Output "Accepted step 2 only: $archive"
