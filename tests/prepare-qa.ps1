$ErrorActionPreference='Stop'
Set-Location -LiteralPath (Split-Path -Parent $PSScriptRoot)
$managed='A:\Steam\steamapps\common\rat game\Rat Game_Data\Managed'
$core=Join-Path (Get-Location) '.tools\bepinex\BepInEx\core'
$references=@('mscorlib','System','System.Core','netstandard','Assembly-CSharp','UnityEngine','UnityEngine.CoreModule','UnityEngine.PhysicsModule','UnityEngine.ScreenCaptureModule','UnityEngine.AIModule','Unity.Netcode.Runtime','Unity.Collections','Unity.Networking.Transport','Unity.Mathematics','Newtonsoft.Json') | ForEach-Object { '/reference:'+(Join-Path $managed ($_+'.dll')) }
$references+=('/reference:'+(Join-Path $core 'BepInEx.dll')),('/reference:'+(Join-Path $core '0Harmony.dll')),('/reference:'+(Join-Path (Get-Location) 'build\RatTrainer.dll'))
& 'C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe' '/nologo' '/noconfig' '/nostdlib+' '/target:library' '/out:build\RatTrainer.Qa.dll' @references 'tests\QaPlugin.cs'
if($LASTEXITCODE -ne 0) { throw 'QA compilation failed.' }
$qaRoot=Join-Path $env:TEMP ('rat-trainer-qa-'+[Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $qaRoot -Force | Out-Null
[IO.File]::WriteAllText((Join-Path (Get-Location) 'test-output\qa-location.txt'),$qaRoot)
foreach($name in @('qa-host','qa-client')) {
    $target=Join-Path $qaRoot $name
    New-Item -ItemType Directory -Path $target -Force | Out-Null
    foreach($file in @('Rat Game.exe','UnityPlayer.dll','UnityCrashHandler64.exe')) { Copy-Item -LiteralPath (Join-Path 'A:\Steam\steamapps\common\rat game' $file) -Destination (Join-Path $target $file) -Force }
    foreach($directory in @('Rat Game_Data','MonoBleedingEdge','D3D12')) {
        $link=Join-Path $target $directory
        if(-not (Test-Path -LiteralPath $link)) { New-Item -ItemType Junction -Path $link -Value (Join-Path 'A:\Steam\steamapps\common\rat game' $directory) | Out-Null }
    }
    & '.\dist\RatTrainer\Manage-RatTrainer.ps1' -GamePath $target -Action Install
    Copy-Item -LiteralPath 'build\RatTrainer.Qa.dll' -Destination (Join-Path $target 'BepInEx\plugins\RatTrainer\RatTrainer.Qa.dll') -Force
}
Write-Output 'Isolated QA host/client are ready. Game data directories are read-only test inputs.'
