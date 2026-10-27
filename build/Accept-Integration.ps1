param(
    [Parameter(Mandatory)][string]$ProbeReport,
    [Parameter(Mandatory)][string]$NativeProbeReport,
    [Parameter(Mandatory)][string]$FeatherReport,
    [Parameter(Mandatory)][string]$BsdfReport,
    [Parameter(Mandatory)][string]$BridgeStatus,
    [string]$UnityProjectRoot='D:/Document/download/md/KoikatsuSunshineModdingTools-main'
)
. "$PSScriptRoot/Common.ps1"
$fingerprint=Get-SourceFingerprint
foreach($path in 'artifacts/audit/report.json','artifacts/core-model-tests/receipt.json','artifacts/tooling-tests/report.json') {
    $gate=Get-Content (Join-Path $RepoRoot $path) -Raw|ConvertFrom-Json
    if(-not $gate.passed -or $gate.sourceFingerprint -ne $fingerprint){throw "Stale final gate: $path"}
}
$probe=Get-Content $ProbeReport -Raw|ConvertFrom-Json
$native=Get-Content $NativeProbeReport -Raw|ConvertFrom-Json
$feather=Get-Content $FeatherReport -Raw|ConvertFrom-Json
$bsdf=Get-Content $BsdfReport -Raw|ConvertFrom-Json
$bsdfRoot=Split-Path ([IO.Path]::GetFullPath($BsdfReport)) -Parent
$post=Get-Content (Join-Path $bsdfRoot 'post-import-report.json') -Raw|ConvertFrom-Json
$presets=Get-Content (Join-Path $bsdfRoot 'presets/report.json') -Raw|ConvertFrom-Json
if(-not $probe.passed -or $probe.baseline -or $probe.checks.Count -lt 44 -or $probe.maximumRadianceDifference -ne 0 -or $probe.maximumShDifference -ne 0){throw 'Probe gates failed/incomplete'}
if(-not $feather.passed -or $feather.checks.Count -lt 53 -or $feather.maximumPixelDifference -ne 0){throw 'Final Feather regression failed/incomplete'}
if(-not $bsdf.passed -or $bsdf.checks.Count -lt 40 -or $bsdf.nativePixelDifference -ne 0 -or $bsdf.earlyPixelDifference -ne 0 -or -not $presets.passed -or -not $post.passed -or $post.identities.Count -ne 14){throw 'Final BSDF/DOF/preset/identity gates failed/incomplete'}
$nativeBaseline=Join-Path $RepoRoot 'artifacts/integration/step4-native-original-play'
$oldNative=Get-Content (Join-Path $nativeBaseline 'report.json') -Raw|ConvertFrom-Json
if(-not $native.complete -or -not $native.passed -or -not $oldNative.complete -or -not $oldNative.passed -or $native.sky -notmatch 'faces=6'){throw 'Actual native RenderProbe evidence missing'}
$nativeRoot=Split-Path ([IO.Path]::GetFullPath($NativeProbeReport)) -Parent
foreach($face in 0..5){if((Get-Hash (Join-Path $nativeRoot "face-$face.png")) -ne (Get-Hash (Join-Path $nativeBaseline "face-$face.png"))){throw "Native probe appearance changed: face $face"}}
$status=Get-Content $BridgeStatus -Raw|ConvertFrom-Json
if(-not $status.success -or $status.message -notmatch 'loaded=True; hash=([A-F0-9]{128})'){throw 'No final loaded diagnostic proof'}
$loaded=$Matches[1]
$core=Join-Path $RepoRoot 'artifacts/bin/TomRender.Core/Release/TomRender.Core.dll'
$validator=Join-Path $RepoRoot 'artifacts/bin/TomRender.UnityValidation/Release/TomRender.UnityValidation.dll'
if($loaded -cne ((Get-Hash $core)+(Get-Hash $validator)).ToUpperInvariant()){throw 'Different final Core/diagnostic binaries rendered'}
$probeDll=Join-Path $RepoRoot 'artifacts/bin/TomProbe.Runtime/Release/TomProbe.Runtime.dll'
if($native.runtimeSha256.ToLowerInvariant() -ne (Get-Hash $probeDll)){throw 'Native probe fixture ran a different Runtime DLL'}
$candidate=Get-Content (Join-Path $RepoRoot 'artifacts/authoring/step4-candidate.json') -Raw|ConvertFrom-Json
if($candidate.sourceFingerprint -ne $fingerprint -or $candidate.files.Count -ne 8){throw 'Stale final authoring candidate'}
foreach($file in $candidate.files){if((Get-Hash (Join-Path $RepoRoot $file.source)) -ne $file.sha256 -or (Get-Hash (Join-Path $UnityProjectRoot $file.destination)) -ne $file.sha256){throw "Final authoring mismatch: $($file.destination)"}}
$archive=Join-Path $RepoRoot 'artifacts/integration/accepted-step4'
$overall=Join-Path $RepoRoot 'artifacts/integration/acceptance.json'
if((Test-Path $archive) -or (Test-Path $overall)){throw 'Accepted final evidence is immutable'}
$null=New-Item -ItemType Directory -Path $archive
$sources=@{
    probe=(Split-Path ([IO.Path]::GetFullPath($ProbeReport)) -Parent);native=$nativeRoot
    feather=(Split-Path ([IO.Path]::GetFullPath($FeatherReport)) -Parent);bsdf=$bsdfRoot
    originalProbe=(Join-Path $RepoRoot 'artifacts/integration/step4-frozen-probe-full-v2');originalNative=$nativeBaseline
}
foreach($entry in $sources.GetEnumerator()){Copy-Item -LiteralPath $entry.Value -Destination (Join-Path $archive $entry.Key) -Recurse}
Copy-Item -LiteralPath $BridgeStatus -Destination (Join-Path $archive 'bridge-loaded-proof.json')
Copy-Item -LiteralPath $validator -Destination $archive
$binaries=@();foreach($assembly in (Get-ReleaseManifest).assemblies){$path=Get-OutputPath $assembly;Copy-Item -LiteralPath $path -Destination $archive;$binaries+=@{name=$assembly.name;sha256=(Get-Hash $path)}}
foreach($path in 'artifacts/audit/report.json','artifacts/core-model-tests/receipt.json','artifacts/tooling-tests/report.json','artifacts/authoring/step4-candidate.json','artifacts/build-receipt.json'){
    $parent=Split-Path (Split-Path $path -Parent) -Leaf;Copy-Item -LiteralPath (Join-Path $RepoRoot $path) -Destination (Join-Path $archive "$parent-$(Split-Path $path -Leaf)")
}
$receipt=Join-Path $archive 'acceptance.json'
Write-Json $receipt ([ordered]@{
    schema=1;step=4;passed=$true;acceptedUtc=[DateTime]::UtcNow.ToString('o');sourceFingerprint=$fingerprint
    probeChecks=$probe.checks.Count;featherChecks=$feather.checks.Count;bsdfChecks=$bsdf.checks.Count;presetChecks=$presets.checks.Count;nativeFaces=6
    originalAuthoringIdentities=$post.identities.Count;maximumRadianceDifference=$probe.maximumRadianceDifference;maximumShDifference=$probe.maximumShDifference
    nativeRuntimeSha256=(Get-Hash $probeDll);binaries=$binaries;validatorSha256=(Get-Hash $validator)
    performanceScope='Raw Editor CPU Camera.Render/SH intervals and native completion intervals retained. No isolated GPU timing or live Studio performance claim.'
    limitation='Local integration acceptance only. Native Studio DOF is still represented by a marked Editor surrogate. No game deployment, shader/Bundle change, publication or KK support.'
})
$steps=@();foreach($step in 1..4){$path=Join-Path $RepoRoot "artifacts/integration/accepted-step$step/acceptance.json";$proof=Get-Content $path -Raw|ConvertFrom-Json;if(-not $proof.passed){throw "Missing accepted historical step $step"};$steps+=@{step=$step;passed=$true;receipt="artifacts/integration/accepted-step$step/acceptance.json";sha256=(Get-Hash $path)}}
Write-Json $overall ([ordered]@{schema=1;passed=$true;acceptedUtc=[DateTime]::UtcNow.ToString('o');sourceFingerprint=$fingerprint;steps=$steps;binaries=$binaries;scope='Four-step local authoring/runtime integration and current cross-module regression, not a game release';deployed=$false;published=$false})
Write-Output "Four-step local integration accepted: $overall"
