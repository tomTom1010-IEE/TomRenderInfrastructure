param([string]$LegacyRoot='D:/Document/download/md/KoikatsuSunshineModdingTools-main')
$ErrorActionPreference='Stop'
$root=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$manifest=Join-Path $root 'migration/source-manifest.json'
if(Test-Path -LiteralPath $manifest){throw 'An import baseline already exists. Never overwrite it automatically.'}
$files=[Collections.Generic.List[object]]::new()
function ImportFile([string]$source,[string]$target,[string]$role='runtime-source') {
    $from=Join-Path $LegacyRoot $source; $to=Join-Path $root $target
    if(Test-Path -LiteralPath $to){throw "Refusing to replace $target"}
    [IO.Directory]::CreateDirectory([IO.Path]::GetDirectoryName($to))|Out-Null
    Copy-Item -LiteralPath $from -Destination $to
    $hash=(Get-FileHash -LiteralPath $from -Algorithm SHA256).Hash.ToLowerInvariant()
    if($hash -ne (Get-FileHash -LiteralPath $to -Algorithm SHA256).Hash.ToLowerInvariant()){throw 'Copy mismatch'}
    $record=[ordered]@{origin=$source;path=$target;role=$role;sha256=$hash}
    if($role -eq 'legacy-project-template') {
        $baseline='migration/legacy-projects/'+$target.Substring(4)
        $copy=Join-Path $root $baseline
        [IO.Directory]::CreateDirectory([IO.Path]::GetDirectoryName($copy))|Out-Null
        Copy-Item -LiteralPath $from -Destination $copy
        $record.baselinePath=$baseline
    }
    $files.Add($record)
}
foreach($family in @(@{old='Tools/TomProbeRuntime';new='src/Probe';projects=@('TomProbe.Runtime','KKS_TomProbe')},@{old='Tools/TomBSDFRuntime';new='src/BSDF';projects=@('TomBSDF.Runtime','KKS_TomBSDF')},@{old='Tools/TomSceneEffects';new='src/SceneEffects';projects=@('TomSceneEffects.Runtime','KKS_TomSceneEffects')})) {
    foreach($project in $family.projects) {
        foreach($file in Get-ChildItem -LiteralPath (Join-Path $LegacyRoot "$($family.old)/$project") -File | Where-Object Extension -in '.cs','.csproj') {
            ImportFile "$($family.old)/$project/$($file.Name)" "$($family.new)/$project/$($file.Name)" $(if($file.Extension -eq '.csproj'){'legacy-project-template'}else{'runtime-source'})
        }
    }
}
foreach($name in @('DofCompatibility.cs','TomDofOpaqueRelay.cs')) {ImportFile "Assets/TomSceneEffects/Runtime/$name" "src/SceneEffects/TomSceneEffects.Runtime/$name"}
foreach($entry in @(@{name='TomHairFeather';files=@('TomHairFeatherCamera.cs','TomHairFeatherPlugin.cs')},@{name='TomSkinSDF';files=@('TomSkinSDFHead.cs','TomSkinSDFPlugin.cs')},@{name='TomTessellation';files=@('TomTessellationBounds.cs','TomTessellationPlugin.cs')})) {
    foreach($name in $entry.files){ImportFile "Assets/Mods/TomShadersX/Runtime/$name" "src/TomX/$($entry.name)/$name"}
}
foreach($name in @('Build-HairFeatherPlugin.ps1','Build-SkinSDFPlugin.ps1','Build-TessellationPlugin.ps1')) {ImportFile "Assets/Mods/TomShadersX/Tests/$name" "migration/legacy-build/$name" 'legacy-build-reference'}
foreach($entry in @(@{source='Assets/Mods/TomShadersBSDF/LICENSE';target='LICENSES/TomBSDF-module.txt'},@{source='Assets/Mods/TomShadersX/LICENSE';target='LICENSES/TomShadersX-root.txt'},@{source='Assets/Mods/TomShadersX/LICENSES/MIT-Upstream.txt';target='LICENSES/TomShadersX-MIT-Upstream.txt'})) {
    ImportFile $entry.source $entry.target 'license-notice'
}
[IO.Directory]::CreateDirectory((Split-Path $manifest))|Out-Null
$result=[ordered]@{schema=1;date='2026-10-27';originRootAlias='KKS_UNITY_ROOT';phase='Source/build consolidation; no runtime edits';files=$files}
[IO.File]::WriteAllText($manifest,($result|ConvertTo-Json -Depth 7)+"`n",[Text.UTF8Encoding]::new($false))
[pscustomobject]@{status='imported';files=$files.Count;runtimeSources=@($files|Where-Object role -eq 'runtime-source').Count}|ConvertTo-Json
