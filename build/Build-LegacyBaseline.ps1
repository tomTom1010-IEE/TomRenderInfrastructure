param(
    [string]$GameRoot = $(if ($env:KKS_GAME_ROOT) { $env:KKS_GAME_ROOT } else { 'D:/Program Files/KoikatuSunshine' }),
    [string]$OutputDirectory
)
. "$PSScriptRoot/Common.ps1"
$null = Test-ImportedSources
if (-not $OutputDirectory) { $OutputDirectory = Join-Path $RepoRoot ('artifacts/baseline/' + (Get-Date -Format 'yyyyMMdd-HHmmss-fff')) }
$OutputDirectory = [IO.Path]::GetFullPath($OutputDirectory)
$artifactRoot = [IO.Path]::GetFullPath((Join-Path $RepoRoot 'artifacts')).TrimEnd('\') + '\'
if (-not $OutputDirectory.StartsWith($artifactRoot, [StringComparison]::OrdinalIgnoreCase)) { throw 'The baseline must stay inside this repository artifacts directory.' }
if (Test-Path -LiteralPath $OutputDirectory) { throw 'Baseline directory already exists. Never overwrite a control build.' }
$work = Join-Path $OutputDirectory 'workspace'
$null = New-Item -ItemType Directory -Path $work -Force
$manifest = Get-Content -LiteralPath (Join-Path $RepoRoot 'migration/source-manifest.json') -Raw | ConvertFrom-Json
foreach ($file in $manifest.files | Where-Object { $_.role -in 'runtime-source','legacy-project-template','legacy-build-reference' }) {
    $from = if ($file.role -eq 'legacy-project-template') { $file.baselinePath } else { $file.path }
    if ($file.role -eq 'runtime-source' -and (Test-Path -LiteralPath (Join-Path $RepoRoot 'migration/phase1/frozen-manifest.json'))) {
        $frozen = Get-Content (Join-Path $RepoRoot 'migration/phase1/frozen-manifest.json') -Raw | ConvertFrom-Json
        $from = ($frozen.runtimeSources | Where-Object path -eq $file.path).frozenPath
    }
    $target = Join-Path $work $file.origin
    $null = New-Item -ItemType Directory -Path (Split-Path $target -Parent) -Force
    Copy-Item -LiteralPath (Join-Path $RepoRoot $from) -Destination $target
}
$projects = @('Tools/TomProbeRuntime/KKS_TomProbe/KKS_TomProbe.csproj','Tools/TomBSDFRuntime/KKS_TomBSDF/KKS_TomBSDF.csproj','Tools/TomSceneEffects/KKS_TomSceneEffects/KKS_TomSceneEffects.csproj')
foreach ($project in $projects) {
    $name = [IO.Path]::GetFileNameWithoutExtension($project)
    Invoke-Logged dotnet @('build',(Join-Path $work $project),'-c','Release','-v','minimal','--disable-build-servers','-m:1','-p:ImportDirectoryBuildProps=false',"-p:GameRoot=$GameRoot") (Join-Path $OutputDirectory "$name.log")
}
$xRoot = Join-Path $work 'Assets/Mods/TomShadersX'
$xOutput = Join-Path $OutputDirectory 'tomx'
foreach ($script in 'Build-HairFeatherPlugin.ps1','Build-SkinSDFPlugin.ps1','Build-TessellationPlugin.ps1') {
    Invoke-Logged pwsh @('-NoProfile','-File',(Join-Path $xRoot "Tests/$script"),'-Root',$xRoot,'-GameRoot',$GameRoot,'-OutputDirectory',$xOutput) (Join-Path $OutputDirectory "$script.log")
}
$release = Get-ReleaseManifest
$outputs = @($release.assemblies | Where-Object module -ne 'Core' | ForEach-Object {
    $assembly = $_
    if ($assembly.compiler -eq 'legacyX') { $path = Join-Path $xOutput "$($assembly.name).dll" }
    else {
        $template = $manifest.files | Where-Object { $_.role -eq 'legacy-project-template' -and $_.path -eq $assembly.project }
        $path = Join-Path (Split-Path (Join-Path $work $template.origin) -Parent) "bin/Release/$($assembly.name).dll"
    }
    [ordered]@{name=$assembly.name; path=$path; sha256=(Get-Hash $path)}
})
$receipt = Join-Path $OutputDirectory 'receipt.json'
Write-Json $receipt ([ordered]@{
    schema=1; status='built'; completedUtc=[DateTime]::UtcNow.ToString('o'); configuration='Release'
    sourceFingerprint=(Get-SourceFingerprint); toolchain=(Get-Toolchain $GameRoot); outputs=$outputs
    purpose='Unmodified source and legacy project layout, isolated from the Unity project and installed game.'
})
Write-Output "Legacy control receipt: $receipt"
