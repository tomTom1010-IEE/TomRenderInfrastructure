param()
. "$PSScriptRoot/Common.ps1"
$fingerprint = Get-SourceFingerprint
$log = Join-Path $RepoRoot 'artifacts/core-model-tests/run.log'
$null = New-Item -ItemType Directory -Path (Split-Path $log -Parent) -Force
Push-Location $RepoRoot
try { Invoke-Logged dotnet @('run','--project','tests/CoreModel.Tests/CoreModel.Tests.csproj','--configuration','Release','--','artifacts/core-model-tests/report.json') $log }
finally { Pop-Location }
$report = Get-Content (Join-Path $RepoRoot 'artifacts/core-model-tests/report.json') -Raw | ConvertFrom-Json
if (-not $report.passed -or @($report.checks | Where-Object { -not $_.passed }).Count) { throw 'Core model tests failed.' }
if ((Get-SourceFingerprint) -ne $fingerprint) { throw 'Test sources changed during execution.' }
Write-Json (Join-Path $RepoRoot 'artifacts/core-model-tests/receipt.json') ([ordered]@{passed=$true; sourceFingerprint=$fingerprint; reportSha256=(Get-Hash (Join-Path $RepoRoot 'artifacts/core-model-tests/report.json')); scope='Managed production coordinator source, not Unity/Mono or GPU validation.'})
