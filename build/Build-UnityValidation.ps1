param([string]$UnityEditorRoot='D:/Document/Unity/Editor')
. "$PSScriptRoot/Common.ps1"
$logRoot=Join-Path $RepoRoot 'artifacts/unity-validation'
$null=New-Item -ItemType Directory -Path $logRoot -Force
Push-Location $RepoRoot
try { Invoke-Logged dotnet @('build','tests/UnityValidation/TomRender.UnityValidation.csproj','-c','Release','-v','minimal','--disable-build-servers','-m:1',"-p:UnityEditorRoot=$UnityEditorRoot") (Join-Path $logRoot 'build.log') }
finally { Pop-Location }
Write-Output 'Diagnostic assembly built outside Assets. Run only through the whitelisted Codex Bridge fixture.'
