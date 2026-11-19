param([string]$GamePath='A:\Steam\steamapps\common\rat game')
$ErrorActionPreference='Stop'
Set-Location -LiteralPath (Split-Path -Parent $PSScriptRoot)
$output=Join-Path (Get-Location) 'test-output\network-markers'
New-Item -ItemType Directory -Path $output -Force | Out-Null
$managed=Join-Path $GamePath 'Rat Game_Data\Managed'
$compiler='C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe'
$compileArgs=@('/nologo','/noconfig','/nostdlib+','/target:exe','/codepage:65001',('/out:'+(Join-Path $output 'NetworkMarkerTests.exe')))
foreach($name in @('mscorlib','System','System.Core','netstandard','Newtonsoft.Json')) { $compileArgs+='/reference:'+(Join-Path $managed ($name+'.dll')) }
$compileArgs+=@('assist\TeamPolicy.cs','assist\PingProtocol.cs','assist\SteamLobbySharing.cs','assist\NetworkMarkers.cs','assist\TeamPings.cs','tests\MarkerSyncStubs.cs','tests\NetworkMarkerTests.cs')
& $compiler @compileArgs
if($LASTEXITCODE -ne 0) { throw 'Network marker test compilation failed.' }
Copy-Item -LiteralPath (Join-Path $managed 'Newtonsoft.Json.dll') -Destination $output -Force
[IO.File]::WriteAllText((Join-Path $output 'NetworkMarkerTests.runtimeconfig.json'),'{"runtimeOptions":{"tfm":"net8.0","framework":{"name":"Microsoft.NETCore.App","version":"8.0.0"}}}')
& dotnet (Join-Path $output 'NetworkMarkerTests.exe') 2>&1 | Tee-Object -FilePath (Join-Path $output 'regression.txt')
if($LASTEXITCODE -ne 0) { throw 'Network marker tests failed.' }
