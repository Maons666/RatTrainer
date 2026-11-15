param([string]$GamePath = 'A:\Steam\steamapps\common\rat game')
$ErrorActionPreference = 'Stop'
Set-Location -LiteralPath $PSScriptRoot
$managed = Join-Path $GamePath 'Rat Game_Data\Managed'
$core = Join-Path $PSScriptRoot '.tools\bepinex\BepInEx\core'
$compiler = 'C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe'
if (-not (Test-Path -LiteralPath (Join-Path $managed 'Assembly-CSharp.dll'))) { throw 'Rat Game installation not found.' }
if (-not (Test-Path -LiteralPath (Join-Path $core 'BepInEx.dll'))) { throw 'Run bootstrap.ps1 first.' }
New-Item -ItemType Directory -Path 'build','dist\RatTrainer\BepInEx\plugins\RatTrainer' -Force | Out-Null
$references = @('mscorlib','System','System.Core','netstandard','Assembly-CSharp','UnityEngine','UnityEngine.CoreModule','UnityEngine.IMGUIModule','UnityEngine.InputLegacyModule','UnityEngine.InputModule','UnityEngine.TextRenderingModule','UnityEngine.PhysicsModule','UnityEngine.AnimationModule','UnityEngine.AIModule','Unity.InputSystem','Unity.Netcode.Runtime','Unity.Collections','Unity.Mathematics','Newtonsoft.Json')
$argsList = @('/nologo','/noconfig','/target:library','/optimize+','/debug:pdbonly','/nostdlib+','/out:build\RatTrainer.dll')
foreach ($name in $references) { $argsList += ('/reference:' + (Join-Path $managed ($name + '.dll'))) }
foreach ($name in @('BepInEx','0Harmony')) { $argsList += ('/reference:' + (Join-Path $core ($name + '.dll'))) }
$argsList += (Get-ChildItem -LiteralPath 'src' -Filter '*.cs' | Select-Object -ExpandProperty FullName)
& $compiler @argsList
if ($LASTEXITCODE -ne 0) { throw 'Compilation failed.' }
Copy-Item -LiteralPath 'build\RatTrainer.dll' -Destination 'dist\RatTrainer\BepInEx\plugins\RatTrainer\RatTrainer.dll' -Force
Write-Output 'Built RatTrainer.dll'
