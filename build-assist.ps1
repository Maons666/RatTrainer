param([string]$GamePath='A:\Steam\steamapps\common\rat game')
$ErrorActionPreference='Stop'
Set-Location -LiteralPath $PSScriptRoot
$managed=Join-Path $GamePath 'Rat Game_Data\Managed'
$core=Join-Path $PSScriptRoot '.tools\bepinex\BepInEx\core'
$compiler='C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe'
$destination=Join-Path $PSScriptRoot 'dist\RatTrainer\BepInEx\plugins\RatAssist'
New-Item -ItemType Directory -Path $destination,'build\assist' -Force | Out-Null
$references=@('mscorlib','System','System.Core','netstandard','Assembly-CSharp','UnityEngine','UnityEngine.CoreModule','UnityEngine.PhysicsModule','UnityEngine.TextRenderingModule','UnityEngine.ImageConversionModule','UnityEngine.UI','UnityEngine.UIModule','Unity.InputSystem','Unity.Netcode.Runtime','Unity.Collections','Newtonsoft.Json','Facepunch.Steamworks.Win64')
$common=@('/nologo','/noconfig','/nostdlib+','/target:library','/optimize+','/codepage:65001')
foreach($name in $references) { $common += '/reference:'+(Join-Path $managed ($name+'.dll')) }
$common += '/reference:'+(Join-Path $core 'BepInEx.dll')
$common += '/reference:'+(Join-Path $core '0Harmony.dll')
$coreOutput=Join-Path $PSScriptRoot 'build\assist\RatAssist.Core.dll'
$coreArgs=$common+@(('/out:'+$coreOutput),('/resource:'+(Join-Path $PSScriptRoot 'desktop\assets\rat.png')+',RatAssist.rat.png'))
$coreArgs+=Get-ChildItem -LiteralPath 'assist' -Filter '*.cs' | Where-Object { $_.Name -notin @('MarkersEntry.cs','MapEntry.cs') } | Select-Object -ExpandProperty FullName
& $compiler @coreArgs
if($LASTEXITCODE -ne 0) { throw 'Assist core compilation failed.' }
Copy-Item -LiteralPath $coreOutput -Destination $destination -Force
foreach($entry in @(@{Source='MarkersEntry.cs';Output='RatAssist.Markers.dll'},@{Source='MapEntry.cs';Output='RatAssist.TeamMap.dll'})) {
    $output=Join-Path (Join-Path $PSScriptRoot 'build\assist') $entry.Output
    $argsList=$common+@(('/out:'+$output),('/reference:'+$coreOutput),(Join-Path (Join-Path $PSScriptRoot 'assist') $entry.Source))
    & $compiler @argsList
    if($LASTEXITCODE -ne 0) { throw ('Assist entry compilation failed: '+$entry.Source) }
    Copy-Item -LiteralPath $output -Destination $destination -Force
}
Write-Output 'Built independent map markers and team map plugins.'
