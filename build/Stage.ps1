param(
    [ValidateSet('current','with-prototypes')][string]$Profile = 'current',
    [string]$OutputDirectory
)
. "$PSScriptRoot/Common.ps1"
if ((Get-ReleaseManifest).schema -eq 2) {
    $acceptancePath=Join-Path $RepoRoot 'artifacts/integration/acceptance.json'
    if (-not (Test-Path -LiteralPath $acceptancePath)) { throw 'Runtime integration is in progress. Four-step behavior/Unity acceptance is required before staging migrated binaries.' }
    $acceptance=Get-Content $acceptancePath -Raw|ConvertFrom-Json
    if (-not $acceptance.passed -or $acceptance.sourceFingerprint -ne (Get-SourceFingerprint) -or @($acceptance.steps).Count -ne 4) { throw 'Full runtime acceptance is missing or stale.' }
    foreach($step in 1..4) {
        $entry=@($acceptance.steps|Where-Object step -eq $step)
        if($entry.Count -ne 1 -or -not $entry[0].passed){throw "Runtime integration step $step has not passed."}
        $proof=[IO.Path]::GetFullPath((Join-Path $RepoRoot $entry[0].receipt))
        $allowed=[IO.Path]::GetFullPath((Join-Path $RepoRoot 'artifacts/integration')).TrimEnd('\')+'\'
        if(-not $proof.StartsWith($allowed,[StringComparison]::OrdinalIgnoreCase) -or (Get-Hash $proof) -ne $entry[0].sha256){throw "Invalid step $step receipt."}
    }
}
$null = Test-ImportedSources
$auditPath = Join-Path $RepoRoot 'artifacts/audit/report.json'
$audit = Get-Content -LiteralPath $auditPath -Raw | ConvertFrom-Json
$receiptPath = Join-Path $RepoRoot 'artifacts/build-receipt.json'
$receipt = Get-Content -LiteralPath $receiptPath -Raw | ConvertFrom-Json
$fingerprint = Get-SourceFingerprint
if (-not $audit.passed -or $audit.sourceFingerprint -ne $fingerprint -or $receipt.sourceFingerprint -ne $fingerprint -or $receipt.status -ne 'built') { throw 'A current passing Release build and identity/IL audit are required.' }
if ($audit.buildReceiptSha256 -ne (Get-Hash $receiptPath)) { throw 'The audited build receipt was replaced.' }
$release = Get-ReleaseManifest
$modules = @($release.profiles.$Profile)
$assemblies = @($release.assemblies | Where-Object { $_.module -in $modules })
if (-not $OutputDirectory) { $OutputDirectory = Join-Path $RepoRoot ('artifacts/staging/' + $Profile + '-' + (Get-Date -Format 'yyyyMMdd-HHmmss-fff')) }
$OutputDirectory = [IO.Path]::GetFullPath($OutputDirectory)
$allowed = [IO.Path]::GetFullPath((Join-Path $RepoRoot 'artifacts/staging')).TrimEnd('\') + '\'
if (-not $OutputDirectory.StartsWith($allowed, [StringComparison]::OrdinalIgnoreCase)) { throw 'Staging must stay inside artifacts/staging.' }
if (Test-Path -LiteralPath $OutputDirectory) { throw 'Staging output exists. No overwrite is permitted.' }
$null = New-Item -ItemType Directory -Path $OutputDirectory -Force
foreach ($assembly in $assemblies) {
    $entry = @($receipt.outputs | Where-Object name -eq $assembly.name)
    if ($entry.Count -ne 1) { throw "Missing / duplicate output: $($assembly.name)" }
    $source = Join-Path $RepoRoot $entry[0].path
    if ((Get-Hash $source) -ne $entry[0].sha256) { throw "Unaudited DLL: $($assembly.name)" }
    $target = Join-Path $OutputDirectory $assembly.install
    $null = New-Item -ItemType Directory -Path (Split-Path $target -Parent) -Force
    Copy-Item -LiteralPath $source -Destination $target
}
foreach ($name in 'release.manifest.json','README.md') { Copy-Item -LiteralPath (Join-Path $RepoRoot $name) -Destination $OutputDirectory }
Copy-Item -LiteralPath (Join-Path $RepoRoot 'LICENSES') -Destination (Join-Path $OutputDirectory 'LICENSES') -Recurse
Copy-Item -LiteralPath (Join-Path $RepoRoot 'docs') -Destination (Join-Path $OutputDirectory 'docs') -Recurse
Copy-Item -LiteralPath (Join-Path $RepoRoot 'docs/Build-and-Staging.md') -Destination (Join-Path $OutputDirectory 'STAGING-NOT-INSTALLER.md')
$dlls = @(Get-ChildItem -LiteralPath $OutputDirectory -Recurse -File -Filter '*.dll')
if ($dlls.Count -ne $assemblies.Count) { throw 'Unexpected binary copied into staging.' }
$inventory = @($assemblies | ForEach-Object {
    $assembly = $_
    $hash = Get-Hash (Join-Path $OutputDirectory $assembly.install)
    $built = @($receipt.outputs | Where-Object name -eq $assembly.name)[0]
    if ($hash -ne $built.sha256) { throw "Staged DLL hash mismatch: $($assembly.name)" }
    [ordered]@{ name=$assembly.name; path=$assembly.install; sha256=$hash }
})
Write-Json (Join-Path $OutputDirectory 'staging-manifest.json') ([ordered]@{
    schema=1; profile=$Profile; status='LOCAL DLL-ONLY STAGING - NOT A COMPLETE INSTALLER'
    sourceFingerprint=$fingerprint; auditSha256=(Get-Hash $auditPath); dlls=$inventory
    includedModules=$modules; dependencies=@($release.dependencies | Where-Object { $_.module -in $modules })
    resourcesIncluded=$false; thirdPartyDllsIncluded=$false; deployed=$false; published=$false
    warning='Existing shader zipmods and the Feather helper Bundle must be supplied separately. Prototype opt-in does not authorize public release.'
})
$hashes = @(Get-ChildItem -LiteralPath $OutputDirectory -Recurse -File | Sort-Object FullName | ForEach-Object {
    (Get-Hash $_.FullName) + '  ' + $_.FullName.Substring($OutputDirectory.Length+1).Replace('\','/')
})
$hashes | Set-Content -LiteralPath (Join-Path $OutputDirectory 'SHA256SUMS') -Encoding utf8
Write-Output "Staged $($inventory.Count) explicit DLLs ($Profile). No shader resources, deployment or publication: $OutputDirectory"
