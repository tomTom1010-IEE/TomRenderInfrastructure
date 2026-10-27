param(
    [string]$GameRoot = $(if ($env:KKS_GAME_ROOT) { $env:KKS_GAME_ROOT } else { 'D:/Program Files/KoikatuSunshine' }),
    [ValidateSet('Release','Debug')][string]$Configuration = 'Release'
)
. "$PSScriptRoot/Common.ps1"
$null = Test-ImportedSources
$fingerprint = Get-SourceFingerprint
$logRoot = Join-Path $RepoRoot ('artifacts/logs/' + (Get-Date -Format 'yyyyMMdd-HHmmss-fff'))
$null = New-Item -ItemType Directory -Path $logRoot -Force
$receiptPath = Join-Path $RepoRoot 'artifacts/build-receipt.json'
Write-Json $receiptPath ([ordered]@{ status='building'; sourceFingerprint=$fingerprint; startedUtc=[DateTime]::UtcNow.ToString('o') })
Push-Location $RepoRoot
try {
    Invoke-Logged dotnet @('build','TomRenderInfrastructure.sln','--configuration',$Configuration,'--verbosity','minimal','--disable-build-servers','-m:1',"-p:GameRoot=$GameRoot") (Join-Path $logRoot 'build.log')
    $release = Get-ReleaseManifest
    $outputs = @($release.assemblies | ForEach-Object {
        $path = Get-OutputPath $_ $Configuration
        [ordered]@{ name=$_.name; path=$path.Substring($RepoRoot.Length+1).Replace('\','/'); sha256=(Get-Hash $path) }
    })
    if ((Get-SourceFingerprint) -ne $fingerprint) { throw 'Build inputs changed during compilation.' }
    Write-Json $receiptPath ([ordered]@{
        schema=1; status='built'; completedUtc=[DateTime]::UtcNow.ToString('o'); configuration=$Configuration
        sourceFingerprint=$fingerprint; toolchain=(Get-Toolchain $GameRoot); outputs=$outputs
        logDirectory=$logRoot.Substring($RepoRoot.Length+1).Replace('\','/')
        deployed=$false; bundleBuilt=$false
    })
} finally { Pop-Location }
Write-Output "Built $($outputs.Count) assemblies. No deployment or Bundle build. Receipt: $receiptPath"
