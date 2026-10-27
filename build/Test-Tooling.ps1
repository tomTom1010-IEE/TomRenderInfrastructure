param(
    [string]$GameRoot = $(if ($env:KKS_GAME_ROOT) { $env:KKS_GAME_ROOT } else { 'D:/Program Files/KoikatuSunshine' })
)
. "$PSScriptRoot/Common.ps1"
$tests = [Collections.Generic.List[object]]::new()
function Assert-Check([string]$Name, [bool]$Pass) {
    $tests.Add([ordered]@{name=$Name; passed=$Pass})
    if (-not $Pass) { throw "Tooling check failed: $Name" }
}
$release = Get-ReleaseManifest
Assert-Check 'Frozen runtime source hashes (35)' ((Test-ImportedSources) -eq 35)
foreach ($script in Get-ChildItem -LiteralPath $PSScriptRoot -Filter '*.ps1') {
    $tokens = $null; $parseErrors = $null
    $null = [Management.Automation.Language.Parser]::ParseFile($script.FullName, [ref]$tokens, [ref]$parseErrors)
    Assert-Check "PowerShell syntax: $($script.Name)" (@($parseErrors).Count -eq 0)
}
foreach ($project in $release.assemblies.project) {
    $xml = [xml](Get-Content -LiteralPath (Join-Path $RepoRoot $project) -Raw)
    Assert-Check "Project XML: $project" ($xml.DocumentElement.LocalName -eq 'Project')
}
Assert-Check 'Default inventory selects eight DLLs including Core' (@($release.assemblies | Where-Object { $_.module -in $release.profiles.current }).Count -eq 8)
Assert-Check 'Prototype inventory selects ten DLLs including Core' (@($release.assemblies | Where-Object { $_.module -in $release.profiles.'with-prototypes' }).Count -eq 10)
$solution = Get-Content -LiteralPath (Join-Path $RepoRoot 'TomRenderInfrastructure.sln') -Raw
Assert-Check 'Solution includes all ten projects' ((@($release.assemblies | Where-Object { -not $solution.Contains($_.project.Replace('/','\')) })).Count -eq 0)
$bsdfSource = Get-Content (Join-Path $RepoRoot 'src/BSDF/TomBSDF.Runtime/TomBSDFSceneService.cs') -Raw
$dofSource = Get-Content (Join-Path $RepoRoot 'src/SceneEffects/KKS_TomSceneEffects/Plugin.cs') -Raw
$adapter = Get-Content (Join-Path $RepoRoot 'src/SceneEffects/KKS_TomSceneEffects/BsdfCaptureAdapter.cs') -Raw
Assert-Check 'BSDF has one Hub registration and no direct camera subscription' ($bsdfSource.Contains('UnityCameraHub.Register(') -and $bsdfSource -notmatch 'Camera\.on(PreCull|PreRender|PostRender)\s*[+-]=')
Assert-Check 'DOF has one Hub registration and no direct camera subscription' ($dofSource.Contains('UnityCameraHub.Register(') -and $dofSource -notmatch 'Camera\.on(PreCull|PreRender|PostRender)\s*[+-]=')
Assert-Check 'Explicit color adapter has no private IL dependency' ($adapter.Contains('SceneColorProtocol') -and $adapter -notmatch 'Transpiler|Reflection\.Emit|CodeInstruction|GetMethod\(')
Assert-Check 'Native DOF prefix remains guarded' ($dofSource.Contains('HarmonyMethod') -and $dofSource.Contains('BeforeNative'))
foreach ($module in 'BSDF','SceneEffects','Probe','TomHairFeather','TomSkinSDF','TomTessellation') {
    Assert-Check "Required Core dependency recorded: $module" ('TomRender.Core.dll' -in ($release.dependencies | Where-Object module -eq $module).required)
}

$null = [Reflection.Assembly]::LoadFrom((Join-Path $GameRoot 'BepInEx/core/Mono.Cecil.dll'))
. "$PSScriptRoot/AssemblySnapshot.ps1"
. "$PSScriptRoot/AbiContract.ps1"
$run = Join-Path $RepoRoot ('artifacts/tooling-tests/' + (Get-Date -Format 'yyyyMMdd-HHmmss-fff'))
$null = New-Item -ItemType Directory -Path $run -Force
$pluginPath = Get-OutputPath ($release.assemblies | Where-Object name -eq 'KKS_TomProbe')
$runtimePath = Get-OutputPath ($release.assemblies | Where-Object name -eq 'TomProbe.Runtime')
$pluginHash = Get-Hash $pluginPath
$runtimeHash = Get-Hash $runtimePath
$pluginSnapshot = Get-AssemblySnapshot $pluginPath | ConvertTo-Json -Depth 60 -Compress
$runtimeSnapshot = Get-AssemblySnapshot $runtimePath | ConvertTo-Json -Depth 60 -Compress

$a = [Mono.Cecil.AssemblyDefinition]::ReadAssembly($pluginPath)
try {
    $type = $a.MainModule.GetType('TomProbePlugin.Plugin')
    $attr = @($type.CustomAttributes | Where-Object { $_.AttributeType.FullName -eq 'BepInEx.BepInPlugin' })[0]
    $attr.ConstructorArguments[0] = [Mono.Cecil.CustomAttributeArgument]::new($a.MainModule.TypeSystem.String, 'invalid.test.guid')
    $path = Join-Path $run 'changed-guid.dll'; $a.Write($path)
} finally { $a.Dispose() }
Assert-Check 'Audit detects changed BepInEx GUID' (($pluginSnapshot) -cne (Get-AssemblySnapshot $path | ConvertTo-Json -Depth 60 -Compress))
$pluginContract = Get-AbiContract (Get-AssemblySnapshot $pluginPath)
Assert-Check 'ABI gate rejects changed BepInEx GUID' ((Compare-AbiContract $pluginContract (Get-AbiContract (Get-AssemblySnapshot $path))).Count -gt 0)

$a = [Mono.Cecil.AssemblyDefinition]::ReadAssembly($runtimePath)
try {
    $field = @($a.MainModule.GetType('TomProbeRuntime.TomReflectionProbeController').Fields | Where-Object Name -eq 'resolution')[0]
    $field.Name = 'invalidSerializedFieldName'
    $path = Join-Path $run 'changed-field.dll'; $a.Write($path)
} finally { $a.Dispose() }
Assert-Check 'Audit detects changed Unity component field' (($runtimeSnapshot) -cne (Get-AssemblySnapshot $path | ConvertTo-Json -Depth 60 -Compress))
$runtimeContract = Get-AbiContract (Get-AssemblySnapshot $runtimePath)
Assert-Check 'ABI gate rejects changed serialized Unity field' ((Compare-AbiContract $runtimeContract (Get-AbiContract (Get-AssemblySnapshot $path))).Count -gt 0)

$a = [Mono.Cecil.AssemblyDefinition]::ReadAssembly($pluginPath)
try {
    $awake = @($a.MainModule.GetType('TomProbePlugin.Plugin').Methods | Where-Object Name -eq 'Awake')[0]
    $literal = @($awake.Body.Instructions | Where-Object { $_.OpCode.Code -eq [Mono.Cecil.Cil.Code]::Ldstr })[0]
    $literal.Operand = 'invalid runtime literal for audit sensitivity'
    $path = Join-Path $run 'changed-il.dll'; $a.Write($path)
} finally { $a.Dispose() }
Assert-Check 'Audit detects changed method IL string' (($pluginSnapshot) -cne (Get-AssemblySnapshot $path | ConvertTo-Json -Depth 60 -Compress))
Assert-Check 'ABI gate permits body rewrites, requiring separate behavior gates' ((Compare-AbiContract $pluginContract (Get-AbiContract (Get-AssemblySnapshot $path))).Count -eq 0)

$a = [Mono.Cecil.AssemblyDefinition]::ReadAssembly($runtimePath)
try {
    $type = $a.MainModule.GetType('TomProbeRuntime.TomReflectionProbeController')
    $field = [Mono.Cecil.FieldDefinition]::new('_newImplementationField',[Mono.Cecil.FieldAttributes]::Private,$a.MainModule.TypeSystem.Int32)
    $type.Fields.Add($field)
    $path = Join-Path $run 'private-field.dll'; $a.Write($path)
    Assert-Check 'ABI gate permits new nonserialized implementation state' ((Compare-AbiContract $runtimeContract (Get-AbiContract (Get-AssemblySnapshot $path))).Count -eq 0)
    $scope = @($a.MainModule.AssemblyReferences | Where-Object Name -eq 'UnityEngine.CoreModule')[0]
    $serialize = [Mono.Cecil.TypeReference]::new('UnityEngine','SerializeField',$a.MainModule,$scope)
    $constructor = [Mono.Cecil.MethodReference]::new('.ctor',$a.MainModule.TypeSystem.Void,$serialize); $constructor.HasThis=$true
    $field.CustomAttributes.Add([Mono.Cecil.CustomAttribute]::new($constructor))
    $path = Join-Path $run 'serialized-field.dll'; $a.Write($path)
    Assert-Check 'ABI gate rejects unexpected serialized implementation state' ((Compare-AbiContract $runtimeContract (Get-AbiContract (Get-AssemblySnapshot $path))).Count -gt 0)
} finally { $a.Dispose() }
Assert-Check 'Sensitivity tests preserve original plugin output' ((Get-Hash $pluginPath) -eq $pluginHash)
Assert-Check 'Sensitivity tests preserve original runtime output' ((Get-Hash $runtimePath) -eq $runtimeHash)
$feather=Get-OutputPath ($release.assemblies|Where-Object name -eq 'TomHairFeather')
$a=[Mono.Cecil.AssemblyDefinition]::ReadAssembly($feather)
try {
    $type=$a.MainModule.GetType('TomShadersX.TomHairFeatherCamera')
    $before=Get-AbiContract (Get-AssemblySnapshot $feather)
    $attribute=$type.CustomAttributes[0];$type.CustomAttributes.RemoveAt(0);$type.CustomAttributes.Add($attribute)
    $path=Join-Path $run 'attribute-order.dll';$a.Write($path)
    Assert-Check 'ABI ignores only class-attribute metadata order' ((Compare-AbiContract $before (Get-AbiContract (Get-AssemblySnapshot $path))).Count -eq 0)
    $type.CustomAttributes.Remove($attribute)
    $path=Join-Path $run 'missing-attribute.dll';$a.Write($path)
    Assert-Check 'ABI rejects removal of a Unity class attribute' ((Compare-AbiContract $before (Get-AbiContract (Get-AssemblySnapshot $path))).Count -gt 0)
} finally {$a.Dispose()}

$reportPath = Join-Path $RepoRoot 'artifacts/tooling-tests/report.json'
$oldShadow=Get-Content (Join-Path $RepoRoot 'artifacts/phase1/accepted/snapshots/TomBSDF.Runtime.current.json') -Raw|ConvertFrom-Json
$newShadow=Get-AssemblySnapshot (Get-OutputPath ($release.assemblies|Where-Object name -eq 'TomBSDF.Runtime'))
$oldMethods=($oldShadow.modules.types|Where-Object name -eq 'TomBSDFRuntime.TomBSDFShadowReceivers').methods
$newMethods=($newShadow.modules.types|Where-Object name -eq 'TomBSDFRuntime.TomBSDFShadowReceivers').methods
foreach($method in 'IsReceiver','Resolution','RebuildHooks','HasActiveResources','Publish') {
    $before=@($oldMethods|Where-Object {$_.name -match "::$method\("})
    $after=@($newMethods|Where-Object {$_.name -match "::$method\("})
    Assert-Check "Native-shadow GPU command/policy body preserved: $method" ($before.Count -eq 1 -and $after.Count -eq 1 -and ($before[0].body|ConvertTo-Json -Depth 60 -Compress) -ceq ($after[0].body|ConvertTo-Json -Depth 60 -Compress))
}
$oldProbe=Get-Content (Join-Path $RepoRoot 'artifacts/phase1/accepted/snapshots/TomProbe.Runtime.current.json') -Raw|ConvertFrom-Json
$newProbe=Get-AssemblySnapshot (Get-OutputPath ($release.assemblies|Where-Object name -eq 'TomProbe.Runtime'))
foreach($type in 'TomProbeRuntime.TomSHProbeVolume','TomProbeRuntime.TomReflectionProbeController') {
    $before=($oldProbe.modules.types|Where-Object name -eq $type).methods
    $after=($newProbe.modules.types|Where-Object name -eq $type).methods
    foreach($method in 'CaptureSphericalHarmonics','AddFaceToSphericalHarmonics','CubemapDirection','PackUnitySH','UploadAtlas','BeginCapture','StepCapture') {
        $b=@($before|Where-Object {$_.name -match "::$method\("});if($b.Count -ne 1){continue}
        $a=@($after|Where-Object name -eq $b[0].name)
        Assert-Check "Probe capture/integration/publication algorithm preserved: $type.$method" ($a.Count -eq 1 -and ($b[0].body|ConvertTo-Json -Depth 60 -Compress) -ceq ($a[0].body|ConvertTo-Json -Depth 60 -Compress))
    }
}
$probeSky=Get-Content (Join-Path $RepoRoot 'src/Probe/TomProbe.Runtime/TomProbeCaptureSky.cs') -Raw
Assert-Check 'Probe sky uses Hub without a second camera subscription set' ($probeSky.Contains('UnityCameraHub.Register(') -and $probeSky -notmatch 'Camera\.on(PreCull|PreRender|PostRender)\s*[+-]=')
Write-Json $reportPath ([ordered]@{schema=2; passed=$true; sourceFingerprint=(Get-SourceFingerprint); completedUtc=[DateTime]::UtcNow.ToString('o'); tests=@($tests.ToArray()); mutatedCopiesOnly=$run; deployed=$false})
Write-Output "PASS: $($tests.Count) tooling checks. Mutations affected ignored test copies only. Report: $reportPath"
