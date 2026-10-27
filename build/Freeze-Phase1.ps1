param([string]$GameRoot = 'D:/Program Files/KoikatuSunshine')
. "$PSScriptRoot/Common.ps1"
$null = Test-ImportedSources
$contractPath = Join-Path $RepoRoot 'migration/phase1/contracts.json'
if (Test-Path -LiteralPath $contractPath) { throw 'Phase 1 contract already frozen. Never overwrite it.' }
$reportPath = Join-Path $RepoRoot 'artifacts/audit/report.json'
$receiptPath = Join-Path $RepoRoot 'artifacts/build-receipt.json'
$audit = Get-Content $reportPath -Raw | ConvertFrom-Json
$receipt = Get-Content $receiptPath -Raw | ConvertFrom-Json
if (-not $audit.passed -or $audit.assemblyCount -ne 9 -or $audit.buildReceiptSha256 -ne (Get-Hash $receiptPath)) { throw 'The accepted phase-1 receipt is required.' }
$archive = Join-Path $RepoRoot 'artifacts/phase1/accepted'
if (Test-Path -LiteralPath $archive) { throw 'Archive already exists.' }
$null = New-Item -ItemType Directory -Path $archive -Force
Copy-Item -LiteralPath $reportPath,$receiptPath -Destination $archive
Copy-Item -LiteralPath (Join-Path $RepoRoot 'artifacts/audit/snapshots') -Destination (Join-Path $archive 'snapshots') -Recurse
Copy-Item -LiteralPath (Join-Path $RepoRoot 'build') -Destination (Join-Path $archive 'build') -Recurse
$manifest = Get-Content (Join-Path $RepoRoot 'migration/source-manifest.json') -Raw | ConvertFrom-Json
$sources = @($manifest.files | Where-Object role -eq 'runtime-source' | ForEach-Object {
    $target = 'migration/phase1/frozen/' + $_.path
    $destination = Join-Path $RepoRoot $target
    $null = New-Item -ItemType Directory -Force -Path (Split-Path $destination -Parent)
    Copy-Item -LiteralPath (Join-Path $RepoRoot $_.path) -Destination $destination
    if ((Get-Hash $destination) -ne $_.sha256) { throw "Frozen source copy mismatch: $target" }
    [ordered]@{ path=$_.path; frozenPath=$target; sha256=$_.sha256; origin=$_.origin }
})
$null = [Reflection.Assembly]::LoadFrom((Join-Path $GameRoot 'BepInEx/core/Mono.Cecil.dll'))
. "$PSScriptRoot/AssemblySnapshot.ps1"
. "$PSScriptRoot/AbiContract.ps1"
$contracts = @($receipt.outputs | ForEach-Object {
    $path = Join-Path $RepoRoot $_.path
    if ((Get-Hash $path) -ne $_.sha256) { throw "Accepted output changed: $($_.name)" }
    Copy-Item -LiteralPath $path -Destination $archive
    [ordered]@{ name=$_.name; sha256=$_.sha256; contract=(Get-AbiContract (Get-AssemblySnapshot $path)) }
})
Write-Json $contractPath ([ordered]@{schema=1; phase='phase1-source-consolidation'; sourceManifestSha256=(Get-Hash (Join-Path $RepoRoot 'migration/source-manifest.json')); assemblies=$contracts})
Write-Json (Join-Path $RepoRoot 'migration/phase1/frozen-manifest.json') ([ordered]@{schema=1; runtimeSources=$sources; acceptedAuditSha256=(Get-Hash $reportPath); acceptedReceiptSha256=(Get-Hash $receiptPath); abiContractSha256=(Get-Hash $contractPath)})
Write-Output "Frozen 35 original sources and 9 ABI contracts. Full historical IL evidence remains in $archive."
