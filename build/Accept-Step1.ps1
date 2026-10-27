param(
    [Parameter(Mandatory)][string]$ObserverReport,
    [Parameter(Mandatory)][string]$BridgeStatus,
    [Parameter(Mandatory)][string]$DofBaselineReport
)
. "$PSScriptRoot/Common.ps1"
$null=Test-ImportedSources -StrictCurrent
$fingerprint=Get-SourceFingerprint
$observer=Get-Content $ObserverReport -Raw|ConvertFrom-Json
$status=Get-Content $BridgeStatus -Raw|ConvertFrom-Json
$dof=Get-Content $DofBaselineReport -Raw|ConvertFrom-Json
$auditPath=Join-Path $RepoRoot 'artifacts/audit/report.json'
$audit=Get-Content $auditPath -Raw|ConvertFrom-Json
$modelPath=Join-Path $RepoRoot 'artifacts/core-model-tests/receipt.json'
$model=Get-Content $modelPath -Raw|ConvertFrom-Json
$modelReportPath=Join-Path $RepoRoot 'artifacts/core-model-tests/report.json'
$modelReport=Get-Content $modelReportPath -Raw|ConvertFrom-Json
$toolingPath=Join-Path $RepoRoot 'artifacts/tooling-tests/report.json'
$tooling=Get-Content $toolingPath -Raw|ConvertFrom-Json
if(-not $audit.passed -or $audit.sourceFingerprint -ne $fingerprint -or -not $model.passed -or $model.sourceFingerprint -ne $fingerprint -or -not $tooling.passed -or $tooling.sourceFingerprint -ne $fingerprint){throw 'Fresh ABI, model and tooling receipts are required.'}
if(-not $modelReport.passed -or $modelReport.checks.Count -lt 27 -or $model.reportSha256 -ne (Get-Hash $modelReportPath)){throw 'Managed behavior evidence missing or replaced.'}
if(-not $observer.passed -or @($observer.checks).Count -lt 29 -or @($observer.checks|Where-Object {-not $_.passed}).Count -or $observer.maximumPixelDifference -ne 0 -or $observer.coreGpuResources -ne 0){throw 'Observer lifecycle/rendering/resource gates failed or evidence is incomplete.'}
if($observer.authoringIdentities.Count -ne 14){throw 'All fourteen original authoring script identities must be frozen.'}
if($dof.status -ne 'passed' -or @($dof.checks).Count -lt 24 -or @($dof.checks|Where-Object {-not $_.passed}).Count){throw 'Original DOF scheduling baseline is incomplete.'}
if(-not $status.success -or $status.message -notmatch 'loaded=True; hash=([A-F0-9]{128})'){throw 'A current Bridge loaded-assembly proof is required.'}
$loadedHash=$Matches[1]
$core=Join-Path $RepoRoot 'artifacts/bin/TomRender.Core/Release/TomRender.Core.dll'
$validator=Join-Path $RepoRoot 'artifacts/bin/TomRender.UnityValidation/Release/TomRender.UnityValidation.dll'
if($loadedHash -cne ((Get-Hash $core)+(Get-Hash $validator)).ToUpperInvariant()){throw 'Unity rendered a different diagnostic binary pair.'}
$archive=Join-Path $RepoRoot 'artifacts/integration/accepted-step1'
$identityPath=Join-Path $RepoRoot 'migration/phase1/authoring-identities.json'
if((Test-Path $archive) -or (Test-Path $identityPath)){throw 'Step-1 accepted evidence is immutable; do not overwrite.'}
$null=New-Item -ItemType Directory -Path $archive -Force
Copy-Item -LiteralPath $core,$validator -Destination $archive
Copy-Item -LiteralPath $auditPath -Destination (Join-Path $archive 'abi-report.json')
Copy-Item -LiteralPath $modelPath -Destination (Join-Path $archive 'core-model-receipt.json')
Copy-Item -LiteralPath $modelReportPath -Destination (Join-Path $archive 'core-model-report.json')
Copy-Item -LiteralPath $toolingPath -Destination (Join-Path $archive 'tooling-report.json')
Copy-Item -LiteralPath (Split-Path ([IO.Path]::GetFullPath($ObserverReport)) -Parent) -Destination (Join-Path $archive 'observer') -Recurse
Copy-Item -LiteralPath (Split-Path ([IO.Path]::GetFullPath($DofBaselineReport)) -Parent) -Destination (Join-Path $archive 'original-dof-baseline') -Recurse
Copy-Item -LiteralPath $BridgeStatus -Destination (Join-Path $archive 'bridge-loaded-proof.json')
Write-Json $identityPath ([ordered]@{schema=1; source='Codex Bridge AssetDatabase GUID/localFileId and MonoScript.GetClass, before runtime authoring integration'; identities=$observer.authoringIdentities})
Write-Json (Join-Path $RepoRoot 'migration/phase1/stage-contract.json') ([ordered]@{
    schema=1; scope='Original scheduling baseline. DOF fixture is a marked blur surrogate, not native Studio DOF.'
    modules=@(
        [ordered]@{module='BSDF';callbacks=@('Camera.onPreCull','Camera.onPreRender','Camera.onPostRender');bufferStage='BeforeForwardAlpha';shadowStages=@('AfterShadowMap','BeforeScreenspaceMask')}
        [ordered]@{module='Feather';callbacks=@('component.OnPreCull');gpuStages=@('BeforeForwardOpaque','AfterEverything');partialRect='Immediate private field before native setup, then publish only'}
        [ordered]@{module='ProbeSky';callbacks=@('Camera.onPreCull','Camera.onPreRender','Camera.onPostRender');ownership='Explicit capture camera or guarded native reflection job; reject coincident owners'}
        [ordered]@{module='DOF';callbacks=@('Camera.onPreCull');early='ImageEffectOpaque relay';late='One native passthrough only after successful early execution';default='Native / scene Inherit'}
    )
    nativeDofOrder=$dof.nativeOrder;earlyDofOrder=$dof.earlyOrder
})
Write-Json (Join-Path $archive 'acceptance.json') ([ordered]@{
    schema=1; step=1; passed=$true; acceptedUtc=[DateTime]::UtcNow.ToString('o'); sourceFingerprint=$fingerprint
    abiPreserved=9; pluginIdentities=6; modelChecks=$modelReport.checks.Count; toolingChecks=$tooling.tests.Count; unityChecks=$observer.checks.Count
    originalDofChecks=$dof.checks.Count; originalSourcesUnchanged=35; frozenAuthoringIdentities=$observer.authoringIdentities.Count
    maximumPixelDifference=$observer.maximumPixelDifference; ownedGpuResources=$observer.coreGpuResources
    pairedMedianCpuDeltaMs=$observer.pairedMedianDeltaMs; performanceScope=$observer.scope
    coreSha256=(Get-Hash $core); validatorSha256=(Get-Hash $validator); authoringContractSha256=(Get-Hash $identityPath)
    limitation='Observer phase accepted, not the four-step runtime integration. BSDF/DOF, Feather/shared ownership and Probe migration remain outstanding.'
})
Write-Output "Accepted step 1 only. Full goal remains active. Evidence: $archive"
