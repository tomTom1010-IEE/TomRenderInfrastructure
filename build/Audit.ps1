param(
    [string]$BaselineReceipt,
    [string]$LegacyRoot,
    [string]$GameRoot = $(if ($env:KKS_GAME_ROOT) { $env:KKS_GAME_ROOT } else { 'D:/Program Files/KoikatuSunshine' })
)
. "$PSScriptRoot/Common.ps1"
if ((Get-ReleaseManifest).schema -eq 2) {
    & "$PSScriptRoot/Audit-Runtime.ps1" -GameRoot $GameRoot -LegacyRoot $LegacyRoot
    exit 0
}
$runtimeCount = Test-ImportedSources $LegacyRoot
$receipt = Get-Content -LiteralPath (Join-Path $RepoRoot 'artifacts/build-receipt.json') -Raw | ConvertFrom-Json
$baseline = Get-Content -LiteralPath $BaselineReceipt -Raw | ConvertFrom-Json
$fingerprint = Get-SourceFingerprint
if ($receipt.status -ne 'built' -or $baseline.status -ne 'built' -or $receipt.configuration -ne 'Release' -or $baseline.configuration -ne 'Release') { throw 'Passing Release builds are required.' }
if ($receipt.sourceFingerprint -ne $fingerprint -or $baseline.sourceFingerprint -ne $fingerprint) { throw 'Build inputs changed; rebuild both controls and migrated outputs before auditing.' }
if (($receipt.toolchain | ConvertTo-Json -Depth 20 -Compress) -cne ($baseline.toolchain | ConvertTo-Json -Depth 20 -Compress)) { throw 'Control and migrated toolchains/reference hashes differ.' }
$null = [Reflection.Assembly]::LoadFrom((Join-Path $GameRoot 'BepInEx/core/Mono.Cecil.dll'))
. "$PSScriptRoot/AssemblySnapshot.ps1"
$release = Get-ReleaseManifest
if ($release.assemblies.Count -ne 9) { throw 'Phase 1 requires nine preserved assemblies.' }
$results = @()
$allPassed = $true
$pluginCount = 0
$snapshotRoot = Join-Path $RepoRoot 'artifacts/audit/snapshots'
$null = New-Item -ItemType Directory -Path $snapshotRoot -Force
foreach ($assembly in $release.assemblies) {
    $currentEntry = @($receipt.outputs | Where-Object name -eq $assembly.name)
    $controlEntry = @($baseline.outputs | Where-Object name -eq $assembly.name)
    if ($currentEntry.Count -ne 1 -or $controlEntry.Count -ne 1) { throw "Missing/duplicate assembly receipt: $($assembly.name)" }
    $currentPath = Join-Path $RepoRoot $currentEntry[0].path
    $controlPath = $controlEntry[0].path
    if ((Get-Hash $currentPath) -ne $currentEntry[0].sha256 -or (Get-Hash $controlPath) -ne $controlEntry[0].sha256) { throw "DLL hash drift: $($assembly.name)" }
    $current = Get-AssemblySnapshot $currentPath
    $control = Get-AssemblySnapshot $controlPath
    $currentJson = $current | ConvertTo-Json -Depth 60
    $controlJson = $control | ConvertTo-Json -Depth 60
    $pass = $currentJson -ceq $controlJson
    if (-not $pass) { $allPassed = $false }
    $expectedPlugin = $assembly.PSObject.Properties['plugin']
    if ($expectedPlugin) {
        $pluginCount++
        if ($current.plugins.Count -ne 1) { throw "Expected one plugin in $($assembly.name)" }
        foreach ($key in 'guid','name','version') {
            if ($current.plugins[0].$key -cne $assembly.plugin.$key) { throw "Plugin $key mismatch: $($assembly.name)" }
        }
    } elseif ($current.plugins.Count -ne 0) { throw "Unexpected plugin in runtime library $($assembly.name)" }
    $currentJson | Set-Content -LiteralPath (Join-Path $snapshotRoot "$($assembly.name).current.json") -Encoding utf8
    $controlJson | Set-Content -LiteralPath (Join-Path $snapshotRoot "$($assembly.name).legacy.json") -Encoding utf8
    $results += [ordered]@{
        name=$assembly.name; identity=$current.identity; metadataAndIlEqual=$pass
        binaryEqual=($currentEntry[0].sha256 -eq $controlEntry[0].sha256)
        sha256=$currentEntry[0].sha256; legacySha256=$controlEntry[0].sha256
        plugins=$current.plugins
    }
}
if ($pluginCount -ne 6) { throw 'Phase 1 requires six preserved entry points.' }
$report = [ordered]@{
    schema=1; passed=$allPassed; completedUtc=[DateTime]::UtcNow.ToString('o'); sourceFingerprint=$fingerprint
    buildReceiptSha256=(Get-Hash (Join-Path $RepoRoot 'artifacts/build-receipt.json')); baselineReceiptSha256=(Get-Hash $BaselineReceipt)
    runtimeSources=$runtimeCount; assemblyCount=$results.Count; pluginCount=$pluginCount
    baselineReceipt=[IO.Path]::GetFullPath($BaselineReceipt); legacyMirrorCompared=[bool]$LegacyRoot
    comparison='Assembly identity, attributes, references, all types/nested types, fields/constants/layout, methods and IL, exception regions, properties/events and embedded resources. PE timestamp, module MVID and debug documents are excluded. Legacy csc private array-storage GUID names are normalized; their layout, payload and IL references are still compared. User strings are not normalized.'
    assemblies=$results; deployed=$false; unityAssetsTouched=$false; shadersTouched=$false; bundleBuilt=$false
    limitation='Build equivalence is not a new Unity or live-game acceptance. Legacy source checks are read-only.'
}
$reportPath = Join-Path $RepoRoot 'artifacts/audit/report.json'
Write-Json $reportPath $report
if (-not $allPassed) { throw "Metadata / IL mismatch. Inspect snapshots: $snapshotRoot" }
Write-Output "PASS: $runtimeCount unchanged runtime sources, $($results.Count) preserved assemblies, $pluginCount preserved plugin identities. Report: $reportPath"
