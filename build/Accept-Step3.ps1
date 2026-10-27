param(
    [Parameter(Mandatory)][string]$UnityReport,
    [Parameter(Mandatory)][string]$BsdfRegression,
    [Parameter(Mandatory)][string]$BridgeStatus,
    [string]$UnityProjectRoot='D:/Document/download/md/KoikatsuSunshineModdingTools-main'
)
. "$PSScriptRoot/Common.ps1"
$fingerprint=Get-SourceFingerprint
foreach($path in 'artifacts/audit/report.json','artifacts/core-model-tests/receipt.json','artifacts/tooling-tests/report.json') {
    $value=Get-Content (Join-Path $RepoRoot $path) -Raw|ConvertFrom-Json
    if(-not $value.passed -or $value.sourceFingerprint -ne $fingerprint){throw "Stale gate: $path"}
}
$report=Get-Content $UnityReport -Raw|ConvertFrom-Json
$regression=Get-Content $BsdfRegression -Raw|ConvertFrom-Json
$dofRoot=Split-Path ([IO.Path]::GetFullPath($BsdfRegression)) -Parent
$preset=Get-Content (Join-Path $dofRoot 'presets/report.json') -Raw|ConvertFrom-Json
$post=Get-Content (Join-Path $dofRoot 'post-import-report.json') -Raw|ConvertFrom-Json
$baselinePath=Join-Path $RepoRoot 'artifacts/integration/step3-frozen-feather-v2'
$baseline=Get-Content (Join-Path $baselinePath 'report.json') -Raw|ConvertFrom-Json
if(-not $report.passed -or $report.baseline -or $report.checks.Count -lt 53 -or @($report.checks|Where-Object {-not $_.passed}).Count -or $report.maximumPixelDifference -ne 0){throw 'Feather/shared-ownership evidence incomplete'}
if(-not $baseline.passed -or -not $baseline.baseline -or $baseline.checks.Count -lt 13){throw 'Frozen original Feather baseline incomplete'}
if(-not $regression.passed -or $regression.checks.Count -lt 40 -or -not $preset.passed -or -not $post.passed -or $post.identities.Count -ne 14){throw 'Post-migration BSDF/DOF/preset/identity regression incomplete'}
$status=Get-Content $BridgeStatus -Raw|ConvertFrom-Json
if(-not $status.success -or $status.message -notmatch 'loaded=True; hash=([A-F0-9]{128})'){throw 'No loaded diagnostic proof'}
$loaded=$Matches[1]
$core=Join-Path $RepoRoot 'artifacts/bin/TomRender.Core/Release/TomRender.Core.dll'
$validator=Join-Path $RepoRoot 'artifacts/bin/TomRender.UnityValidation/Release/TomRender.UnityValidation.dll'
if($loaded -cne ((Get-Hash $core)+(Get-Hash $validator)).ToUpperInvariant()){throw 'Different diagnostic binaries rendered'}
$candidate=Get-Content (Join-Path $RepoRoot 'artifacts/authoring/step3-candidate.json') -Raw|ConvertFrom-Json
if($candidate.sourceFingerprint -ne $fingerprint){throw 'Stale authoring provenance'}
foreach($file in $candidate.files){if((Get-Hash (Join-Path $RepoRoot $file.source)) -ne $file.sha256 -or (Get-Hash (Join-Path $UnityProjectRoot $file.destination)) -ne $file.sha256){throw "Authoring import mismatch: $($file.destination)"}}
$archive=Join-Path $RepoRoot 'artifacts/integration/accepted-step3'
if(Test-Path $archive){throw 'Accepted step-3 evidence is immutable'}
$null=New-Item -ItemType Directory -Path $archive
Copy-Item -LiteralPath (Split-Path ([IO.Path]::GetFullPath($UnityReport)) -Parent) -Destination (Join-Path $archive 'unity') -Recurse
Copy-Item -LiteralPath $baselinePath -Destination (Join-Path $archive 'original-feather') -Recurse
Copy-Item -LiteralPath $dofRoot -Destination (Join-Path $archive 'bsdf-regression') -Recurse
Copy-Item -LiteralPath $BridgeStatus -Destination (Join-Path $archive 'bridge-loaded-proof.json')
Copy-Item -LiteralPath $validator -Destination $archive
$binaries=@();foreach($assembly in (Get-ReleaseManifest).assemblies){$path=Get-OutputPath $assembly;Copy-Item -LiteralPath $path -Destination $archive;$binaries+=@{name=$assembly.name;sha256=(Get-Hash $path)}}
foreach($path in 'artifacts/audit/report.json','artifacts/core-model-tests/receipt.json','artifacts/tooling-tests/report.json','artifacts/authoring/step3-candidate.json'){
    $parent=Split-Path (Split-Path $path -Parent) -Leaf
    Copy-Item -LiteralPath (Join-Path $RepoRoot $path) -Destination (Join-Path $archive "$parent-$(Split-Path $path -Leaf)")
}
$before=@($baseline.cpuMs|Sort-Object);$after=@($report.cpuMs|Sort-Object)
Write-Json (Join-Path $archive 'acceptance.json') ([ordered]@{
    schema=1;step=3;passed=$true;acceptedUtc=[DateTime]::UtcNow.ToString('o');sourceFingerprint=$fingerprint
    unityChecks=$report.checks.Count;originalFeatherChecks=$baseline.checks.Count;bsdfChecks=$regression.checks.Count;presetChecks=$preset.checks.Count
    originalAuthoringIdentities=$post.identities.Count;maximumPixelDifference=$report.maximumPixelDifference
    oldMedianCpuMs=$before[[int]($before.Count/2)];newMedianCpuMs=$after[[int]($after.Count/2)];performanceScope=$report.scope
    binaries=$binaries;validatorSha256=(Get-Hash $validator)
    limitation='Probe integration is still outstanding. Bounds facade math is tested directly in a preview scene because Unity global discovery omits preview objects. Native-shadow command-recorder/policy IL is unchanged; no new live-game/GPU timing certification.'
})
Write-Output "Accepted step 3 only: $archive"
