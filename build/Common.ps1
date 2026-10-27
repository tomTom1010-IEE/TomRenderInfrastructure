Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$RepoRoot = Split-Path $PSScriptRoot -Parent

function Get-Hash([string]$Path) {
    (Get-FileHash -LiteralPath $Path -Algorithm SHA256).Hash.ToLowerInvariant()
}

function Write-Json([string]$Path, $Value) {
    $null = New-Item -ItemType Directory -Force -Path (Split-Path $Path -Parent)
    $Value | ConvertTo-Json -Depth 60 | Set-Content -LiteralPath $Path -Encoding utf8
}

function Get-ReleaseManifest {
    $manifest = Get-Content -LiteralPath (Join-Path $RepoRoot 'release.manifest.json') -Raw | ConvertFrom-Json
    $expected = if ($manifest.schema -eq 2) { 10 } else { 9 }
    if ($manifest.assemblies.Count -ne $expected) { throw 'Inventory must preserve nine legacy assemblies plus the explicit phase-2 Core.' }
    foreach ($key in 'name','install','project') {
        if (@($manifest.assemblies.$key | Sort-Object -Unique).Count -ne $expected) { throw "Duplicate assembly $key in manifest." }
    }
    $plugins = @($manifest.assemblies | Where-Object { $_.PSObject.Properties['plugin'] })
    if ($plugins.Count -ne 6 -or @($plugins.plugin.guid | Sort-Object -Unique).Count -ne 6) { throw 'Six unique preserved plugin GUIDs are required.' }
    foreach ($assembly in $manifest.assemblies) {
        if ($assembly.install -notmatch '^BepInEx/plugins/[A-Za-z0-9_-]+/[A-Za-z0-9_.-]+\.dll$' -or
            [IO.Path]::GetFileName($assembly.install) -cne "$($assembly.name).dll") { throw "Invalid explicit install destination: $($assembly.install)" }
        if (-not (Test-Path -LiteralPath (Join-Path $RepoRoot $assembly.project))) { throw "Missing project: $($assembly.project)" }
    }
    if ('SceneEffects' -in $manifest.profiles.current -or 'SceneEffects' -notin $manifest.profiles.'with-prototypes') { throw 'Prototype opt-in boundaries changed.' }
    return $manifest
}

function Get-SourceFingerprint {
    $files = @(Get-ChildItem -LiteralPath (Join-Path $RepoRoot 'src'), (Join-Path $RepoRoot 'build'), (Join-Path $RepoRoot 'tests'), (Join-Path $RepoRoot 'integration') -File -Recurse |
        Where-Object { $_.Extension -in '.cs', '.csproj', '.props', '.targets', '.ps1' })
    $files += @(Get-Item -LiteralPath (Join-Path $RepoRoot 'Directory.Build.props'), (Join-Path $RepoRoot 'release.manifest.json'), (Join-Path $RepoRoot 'migration/source-manifest.json'), (Join-Path $RepoRoot 'TomRenderInfrastructure.sln'), (Join-Path $RepoRoot 'global.json'))
    if (Test-Path -LiteralPath (Join-Path $RepoRoot 'Local.props')) { $files += Get-Item -LiteralPath (Join-Path $RepoRoot 'Local.props') }
    $lines = @($files | Sort-Object FullName | ForEach-Object {
        $_.FullName.Substring($RepoRoot.Length + 1).Replace('\','/') + '=' + (Get-Hash $_.FullName)
    })
    $bytes = [Text.Encoding]::UTF8.GetBytes(($lines -join "`n"))
    $sha = [Security.Cryptography.SHA256]::Create()
    try { ([BitConverter]::ToString($sha.ComputeHash($bytes))).Replace('-','').ToLowerInvariant() }
    finally { $sha.Dispose() }
}

function Test-ImportedSources([string]$LegacyRoot, [switch]$StrictCurrent) {
    $manifest = Get-Content -LiteralPath (Join-Path $RepoRoot 'migration/source-manifest.json') -Raw | ConvertFrom-Json
    $frozenPath = Join-Path $RepoRoot 'migration/phase1/frozen-manifest.json'
    $frozen = if (Test-Path -LiteralPath $frozenPath) { Get-Content $frozenPath -Raw | ConvertFrom-Json } else { $null }
    if ($frozen -and (Get-Hash (Join-Path $RepoRoot 'migration/phase1/contracts.json')) -ne $frozen.abiContractSha256) { throw 'Frozen ABI contract changed.' }
    if ($frozen) {
        $contract = Get-Content (Join-Path $RepoRoot 'migration/phase1/contracts.json') -Raw | ConvertFrom-Json
        if ((Get-Hash (Join-Path $RepoRoot 'migration/source-manifest.json')) -ne $contract.sourceManifestSha256) { throw 'Original source manifest changed.' }
    }
    $runtimeCount = 0
    foreach ($file in $manifest.files) {
        $path = if ($file.role -eq 'legacy-project-template') { $file.baselinePath } else { $file.path }
        if ($file.role -eq 'runtime-source' -and $frozen) {
            $entry = @($frozen.runtimeSources | Where-Object path -eq $file.path)
            if ($entry.Count -ne 1 -or $entry[0].sha256 -ne $file.sha256) { throw "Missing frozen source provenance: $($file.path)" }
            $path = $entry[0].frozenPath
            if (-not (Test-Path -LiteralPath (Join-Path $RepoRoot $file.path))) { throw "Legacy runtime source removed: $($file.path)" }
            if ($StrictCurrent -and (Get-Hash (Join-Path $RepoRoot $file.path)) -ne $file.sha256) { throw "Current runtime source changed: $($file.path)" }
        }
        if ((Get-Hash (Join-Path $RepoRoot $path)) -ne $file.sha256) { throw "Frozen import changed: $path" }
        if ($LegacyRoot -and $file.role -eq 'runtime-source' -and (Get-Hash (Join-Path $LegacyRoot $file.origin)) -ne $file.sha256) { throw "Legacy source drift: $($file.origin)" }
        if ($file.role -eq 'runtime-source') { $runtimeCount++ }
    }
    return $runtimeCount
}

function Get-Toolchain([string]$GameRoot) {
    $compiler = Join-Path $env:WINDIR 'Microsoft.NET/Framework64/v4.0.30319/csc.exe'
    $sdk = (& dotnet --version | Out-String).Trim()
    if ($LASTEXITCODE) { throw 'dotnet SDK unavailable.' }
    $sdkCompiler = Join-Path (Split-Path (Get-Command dotnet).Source -Parent) "sdk/$sdk/Roslyn/bincore/csc.dll"
    $dlls = @(
        Get-ChildItem -LiteralPath (Join-Path $GameRoot 'KoikatsuSunshine_Data/Managed'), (Join-Path $GameRoot 'CharaStudio_Data/Managed') -File -Filter '*.dll'
        Get-ChildItem -LiteralPath (Join-Path $GameRoot 'BepInEx/core') -File -Filter '*.dll'
        Get-Item -LiteralPath (Join-Path $GameRoot 'BepInEx/plugins/KKSAPI.dll'), (Join-Path $GameRoot 'BepInEx/plugins/KKS_BepisPlugins/KKS_ExtensibleSaveFormat.dll')
    )
    [ordered]@{
        sdk = $sdk
        sdkCompiler = [ordered]@{ path=$sdkCompiler; sha256=(Get-Hash $sdkCompiler) }
        gameRoot = [IO.Path]::GetFullPath($GameRoot)
        legacyCompiler = [ordered]@{ path=$compiler; sha256=(Get-Hash $compiler); version=(Get-Item $compiler).VersionInfo.FileVersion }
        references = @($dlls | Sort-Object FullName -Unique | ForEach-Object {
            [ordered]@{ path=$_.FullName.Substring([IO.Path]::GetFullPath($GameRoot).TrimEnd('\','/').Length+1).Replace('\','/'); sha256=(Get-Hash $_.FullName) }
        })
    }
}

function Invoke-Logged([string]$Command, [string[]]$Arguments, [string]$LogPath) {
    & $Command @Arguments 2>&1 | Tee-Object -FilePath $LogPath | Out-Host
    if ($LASTEXITCODE -ne 0) { throw "Command failed ($LASTEXITCODE): $Command; see $LogPath" }
}

function Get-OutputPath($Assembly, [string]$Configuration = 'Release') {
    Join-Path $RepoRoot "artifacts/bin/$($Assembly.name)/$Configuration/$($Assembly.name).dll"
}
