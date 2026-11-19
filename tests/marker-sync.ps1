param([string]$GamePath='A:\Steam\steamapps\common\rat game')
$ErrorActionPreference='Stop'
Set-Location -LiteralPath (Split-Path -Parent $PSScriptRoot)
$output=Join-Path (Get-Location) 'test-output\marker-sync'
New-Item -ItemType Directory -Path $output -Force | Out-Null
$managed=Join-Path $GamePath 'Rat Game_Data\Managed'
$compiler='C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe'
$argsList=@('/nologo','/noconfig','/nostdlib+','/target:exe','/codepage:65001',('/out:'+(Join-Path $output 'MarkerSyncTests.exe')))
foreach($name in @('mscorlib','System','System.Core','netstandard','Newtonsoft.Json')) { $argsList+='/reference:'+(Join-Path $managed ($name+'.dll')) }
$argsList+=@('assist\TeamPolicy.cs','assist\PingProtocol.cs','assist\SteamLobbySharing.cs','assist\NetworkMarkers.cs','assist\TeamPings.cs','tests\MarkerSyncStubs.cs','tests\MarkerSyncTests.cs')
& $compiler @argsList
if($LASTEXITCODE -ne 0) { throw 'Marker synchronization test compilation failed.' }
Copy-Item -LiteralPath (Join-Path $managed 'Newtonsoft.Json.dll') -Destination $output -Force
[IO.File]::WriteAllText((Join-Path $output 'MarkerSyncTests.runtimeconfig.json'),'{"runtimeOptions":{"tfm":"net8.0","framework":{"name":"Microsoft.NETCore.App","version":"8.0.0"}}}')
& dotnet (Join-Path $output 'MarkerSyncTests.exe') 2>&1 | Tee-Object -FilePath (Join-Path $output 'regression.txt')
if($LASTEXITCODE -ne 0) { throw 'Marker synchronization tests failed.' }
