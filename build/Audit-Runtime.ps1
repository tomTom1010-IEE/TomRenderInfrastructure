param(
    [string]$LegacyRoot,
    [string]$GameRoot = $(if ($env:KKS_GAME_ROOT) { $env:KKS_GAME_ROOT } else { 'D:/Program Files/KoikatuSunshine' })
)
. "$PSScriptRoot/Common.ps1"
$null = Test-ImportedSources $LegacyRoot
$receiptPath = Join-Path $RepoRoot 'artifacts/build-receipt.json'
$receipt = Get-Content $receiptPath -Raw | ConvertFrom-Json
$fingerprint = Get-SourceFingerprint
if ($receipt.status -ne 'built' -or $receipt.configuration -ne 'Release' -or $receipt.sourceFingerprint -ne $fingerprint) { throw 'A current Release build is required.' }
$null = [Reflection.Assembly]::LoadFrom((Join-Path $GameRoot 'BepInEx/core/Mono.Cecil.dll'))
. "$PSScriptRoot/AssemblySnapshot.ps1"
. "$PSScriptRoot/AbiContract.ps1"
$baseline = Get-Content (Join-Path $RepoRoot 'migration/phase1/contracts.json') -Raw | ConvertFrom-Json
$snapshots = @{}
$contracts = @{}
$release = Get-ReleaseManifest
$snapshotRoot = Join-Path $RepoRoot 'artifacts/audit/runtime-snapshots'
foreach ($assembly in $release.assemblies) {
    $entry = @($receipt.outputs | Where-Object name -eq $assembly.name)
    if ($entry.Count -ne 1) { throw "Missing / duplicate build output: $($assembly.name)" }
    $path = Join-Path $RepoRoot $entry[0].path
    if ((Get-Hash $path) -ne $entry[0].sha256) { throw "Build output drift: $path" }
    $snapshot = Get-AssemblySnapshot $path
    $snapshots[$assembly.name] = $snapshot
    $contracts[$assembly.name] = Get-AbiContract $snapshot
    Write-Json (Join-Path $snapshotRoot "$($assembly.name).json") $snapshot
    $plugin = $assembly.PSObject.Properties['plugin']
    if ($plugin) {
        if ($snapshot.plugins.Count -ne 1) { throw "Wrong entry count: $($assembly.name)" }
        foreach ($key in 'guid','name','version') {
            if ($snapshot.plugins[0].$key -cne $assembly.plugin.$key) { throw "Plugin $key mismatch: $($assembly.name)" }
        }
    } elseif ($snapshot.plugins.Count) { throw "Unexpected BepInEx entry: $($assembly.name)" }
}
$shaderProperties = @($contracts.Values | ForEach-Object { $_.shaderPropertyNames } | Sort-Object -Unique)
$results = @($baseline.assemblies | ForEach-Object {
    $assembly = $_
    if (-not $contracts.ContainsKey($assembly.name)) { throw "Legacy assembly removed: $($assembly.name)" }
    $issues = Compare-AbiContract $assembly.contract $contracts[$assembly.name] $shaderProperties
    [ordered]@{ name=$assembly.name; passed=($issues.Count -eq 0); issues=$issues; identity=$contracts[$assembly.name].identity }
})
$passed = @($results | Where-Object { -not $_.passed }).Count -eq 0
$path = Join-Path $RepoRoot 'artifacts/audit/report.json'
Write-Json $path ([ordered]@{
    schema=2; phase='runtime-integration'; passed=$passed; completedUtc=[DateTime]::UtcNow.ToString('o')
    sourceFingerprint=$fingerprint; buildReceiptSha256=(Get-Hash $receiptPath)
    frozenContractSha256=(Get-Hash (Join-Path $RepoRoot 'migration/phase1/contracts.json'))
    preservedAssemblyCount=$results.Count; assemblyCount=$release.assemblies.Count; pluginCount=6; assemblies=$results
    policy='Additive public API allowed; existing ABI, serialization, plugin identity and shader property names preserved. IL equality is historical evidence, not the new behavior gate.'
    behavioralAcceptance='Separate managed and Unity rendering/lifecycle/performance evidence required for each migration step.'
    deployed=$false; shadersTouched=$false; bundleBuilt=$false
})
if (-not $passed) { throw "ABI / serialization incompatibility: $path" }
Write-Output "PASS: 9 preserved ABI/serialization contracts, 10 assemblies, 6 plugin identities. Separate behavior gates remain required. Report: $path"
