param([ValidateSet(2,3,4)][int]$Step=2)
. "$PSScriptRoot/Common.ps1"
$auditPath=Join-Path $RepoRoot 'artifacts/audit/report.json'
$buildPath=Join-Path $RepoRoot 'artifacts/build-receipt.json'
$audit=Get-Content $auditPath -Raw|ConvertFrom-Json
$build=Get-Content $buildPath -Raw|ConvertFrom-Json
$fingerprint=Get-SourceFingerprint
if(-not $audit.passed -or $audit.sourceFingerprint -ne $fingerprint -or $audit.buildReceiptSha256 -ne (Get-Hash $buildPath)){throw 'Current ABI acceptance is required before preparing authoring integration.'}
$files=@(
    [ordered]@{source='artifacts/bin/TomRender.Core/Release/TomRender.Core.dll';destination='Assets/TomRenderInfrastructure/Runtime/TomRender.Core.dll';kind='dll'}
    [ordered]@{source='artifacts/bin/TomBSDF.Runtime/Release/TomBSDF.Runtime.dll';destination='Assets/Mods/TomShadersBSDF/Runtime/TomBSDF.Runtime.dll';kind='dll'}
    [ordered]@{source='src/SceneEffects/TomSceneEffects.Runtime/DofCompatibility.cs';destination='Assets/TomSceneEffects/Runtime/DofCompatibility.cs';kind='source'}
    [ordered]@{source='src/SceneEffects/TomSceneEffects.Runtime/TomDofOpaqueRelay.cs';destination='Assets/TomSceneEffects/Runtime/TomDofOpaqueRelay.cs';kind='source'}
)
if($Step -ge 3){
    foreach($component in 'TomHairFeatherCamera','TomSkinSDFHead','TomTessellationBounds'){
        $module=@{TomHairFeatherCamera='TomHairFeather';TomSkinSDFHead='TomSkinSDF';TomTessellationBounds='TomTessellation'}[$component]
        $files += [ordered]@{source="src/TomX/$module/$component.cs";destination="Assets/Mods/TomShadersX/Runtime/$component.cs";kind='source'}
    }
}
if($Step -eq 4){$files += [ordered]@{source='artifacts/bin/TomProbe.Runtime/Release/TomProbe.Runtime.dll';destination='Assets/Mods/TomLightingProbes/Runtime/TomProbe.Runtime.dll';kind='dll'}}
foreach($file in $files){$file.sha256=Get-Hash (Join-Path $RepoRoot $file.source)}
$runtimeNames=@('TomRender.Core','TomBSDF.Runtime');if($Step -eq 4){$runtimeNames+='TomProbe.Runtime'}
foreach($name in $runtimeNames){
    $entry=@($build.outputs|Where-Object name -eq $name)
    $file=@($files|Where-Object {$_.source.EndsWith("/$name.dll")})
    if($entry.Count -ne 1 -or $file.Count -ne 1 -or $entry[0].sha256 -ne $file[0].sha256){throw "Unaudited authoring DLL: $name"}
}
$path=Join-Path $RepoRoot "artifacts/authoring/step$Step-candidate.json"
Write-Json $path ([ordered]@{schema=1;status='ready';sourceFingerprint=$fingerprint;auditSha256=(Get-Hash $auditPath);buildReceiptSha256=(Get-Hash $buildPath);files=$files})
Write-Output "Candidate prepared, not installed. Use Codex Bridge integratetomrendercore: $path"
