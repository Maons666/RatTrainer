$ErrorActionPreference='Stop'
Set-Location -LiteralPath (Split-Path -Parent $PSScriptRoot)
New-Item -ItemType Directory -Path 'test-output\core' -Force | Out-Null
$compiler='C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe'
$json='A:\Steam\steamapps\common\rat game\Rat Game_Data\Managed\Newtonsoft.Json.dll'
$managed=Split-Path -Parent $json
$references=@('mscorlib','System','System.Core','netstandard','Newtonsoft.Json') | ForEach-Object { '/reference:'+(Join-Path $managed ($_+'.dll')) }
& $compiler '/nologo' '/noconfig' '/nostdlib+' '/target:exe' '/out:test-output\core\CoreTests.exe' @references 'src\Options.cs' 'src\SafeFiles.cs' 'tests\CoreTests.cs'
if($LASTEXITCODE -ne 0) { throw 'Test compilation failed.' }
Copy-Item -LiteralPath $json -Destination 'test-output\core\Newtonsoft.Json.dll' -Force
[IO.File]::WriteAllText((Join-Path (Get-Location) 'test-output\core\CoreTests.runtimeconfig.json'),'{"runtimeOptions":{"tfm":"net8.0","framework":{"name":"Microsoft.NETCore.App","version":"8.0.0"}}}')
dotnet '.\test-output\core\CoreTests.exe' (Join-Path (Get-Location) ('test-output\core\run-'+[Guid]::NewGuid().ToString('N')))
if($LASTEXITCODE -ne 0) { throw 'Core tests failed.' }
$fixture=Join-Path (Get-Location) ('test-output\install-'+[Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path (Join-Path $fixture 'Rat Game_Data\Managed') -Force | Out-Null
Copy-Item -LiteralPath 'A:\Steam\steamapps\common\rat game\Rat Game_Data\Managed\Assembly-CSharp.dll' -Destination (Join-Path $fixture 'Rat Game_Data\Managed\Assembly-CSharp.dll')
[IO.File]::WriteAllText((Join-Path $fixture 'Rat Game.exe'),'fixture')
[IO.File]::WriteAllText((Join-Path $fixture 'preserve.txt'),'user file')
$installer=Join-Path (Get-Location) 'dist\RatTrainer\Manage-RatTrainer.ps1'
& $installer -GamePath $fixture -Action Install
if(-not (Test-Path -LiteralPath (Join-Path $fixture 'BepInEx\plugins\RatTrainer\RatTrainer.dll'))) { throw 'Installer did not deploy plugin.' }
& $installer -GamePath $fixture -Action Install
& $installer -GamePath $fixture -Action Uninstall
if(Test-Path -LiteralPath (Join-Path $fixture 'BepInEx\plugins\RatTrainer\RatTrainer.dll')) { throw 'Uninstaller left plugin active.' }
if([IO.File]::ReadAllText((Join-Path $fixture 'preserve.txt')) -ne 'user file') { throw 'Uninstaller changed unrelated files.' }
if((Get-FileHash -LiteralPath (Join-Path $fixture 'Rat Game_Data\Managed\Assembly-CSharp.dll')).Hash -ne '517DD87905AC3E2DCF845384C440293AEAAA82773F6977964AD459070A21E1C1') { throw 'Game assembly was altered.' }
Write-Output 'PASS installer, idempotent reinstall, uninstall, original assembly, unrelated files'
