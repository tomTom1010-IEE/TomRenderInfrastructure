param([string]$GameRoot='D:/Program Files/KoikatuSunshine',[string]$Root=(Split-Path $PSScriptRoot -Parent),[string]$OutputDirectory)
$ErrorActionPreference='Stop'
if(-not $OutputDirectory){$OutputDirectory=[IO.Path]::GetFullPath((Join-Path $Root '../../../CodexBridge/Builds/TomTessellation'))}
$managed=Join-Path $GameRoot 'KoikatsuSunshine_Data/Managed'
$references=@((Join-Path $GameRoot 'BepInEx/core/BepInEx.dll'))+@(Get-ChildItem -LiteralPath $managed -Filter 'UnityEngine*.dll' | ForEach-Object FullName)+(Join-Path $managed 'netstandard.dll')
foreach($reference in $references){if(-not(Test-Path -LiteralPath $reference)){throw "Missing reference: $reference"}}
New-Item -ItemType Directory -Path $OutputDirectory -Force | Out-Null
$arguments=@('/nologo','/target:library','/optimize+','/define:TOM_X_BEPINEX',('/out:'+(Join-Path $OutputDirectory 'TomTessellation.dll')))+@($references | ForEach-Object {'/reference:'+$_})
$arguments+=@((Join-Path $Root 'Runtime/TomTessellationBounds.cs'),(Join-Path $Root 'Runtime/TomTessellationPlugin.cs'))
& (Join-Path $env:WINDIR 'Microsoft.NET/Framework64/v4.0.30319/csc.exe') @arguments
if($LASTEXITCODE -ne 0){throw 'Tessellation bounds plugin compilation failed'}
Write-Output "Built $OutputDirectory/TomTessellation.dll; no game files installed."
