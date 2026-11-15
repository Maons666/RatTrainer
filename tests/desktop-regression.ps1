param([string]$GamePath='A:\Steam\steamapps\common\rat game', [switch]$UiOnly)
$ErrorActionPreference='Stop'
Set-Location -LiteralPath (Split-Path -Parent $PSScriptRoot)
$output=Join-Path (Get-Location) 'test-output\v1.3.3'
New-Item -ItemType Directory -Path $output -Force | Out-Null
$framework='C:\Windows\Microsoft.NET\Framework64\v4.0.30319'
$compileArgs=@('/nologo','/noconfig','/target:exe','/platform:x64','/codepage:65001','/main:DesktopRegression',('/win32manifest:'+(Join-Path (Get-Location) 'desktop\app.manifest')),('/resource:'+(Join-Path (Get-Location) 'build\desktop-payload.zip')+',RatTrainer.Payload.zip'))
foreach($reference in @('System','System.Core','Accessibility','System.Drawing','System.Windows.Forms','System.Web.Extensions','System.IO.Compression','System.IO.Compression.FileSystem')) { $compileArgs+='/reference:'+(Join-Path $framework ($reference+'.dll')) }
$compileArgs+=@('src\Options.cs','src\DesktopProtocol.cs','assist\TeamPolicy.cs','tests\DesktopRegression.cs')
$compileArgs+=Get-ChildItem -LiteralPath 'desktop' -Filter '*.cs' | Select-Object -ExpandProperty FullName
foreach($art in Get-ChildItem -LiteralPath 'desktop\assets' -Filter '*.png') { $compileArgs+='/resource:'+$art.FullName+',RatTrainer.Art.'+$art.Name }
foreach($edition in @('All','Host')) {
    $exe=Join-Path $output ('Regression-'+$edition+'.exe')
    $editionArgs=@('/out:'+$exe)+$compileArgs
    if($edition -eq 'Host') { $editionArgs=@('/define:HOST_SANDBOX')+$editionArgs }
    & (Join-Path $framework 'csc.exe') @editionArgs
    if($LASTEXITCODE -ne 0) { throw 'Regression compilation failed.' }
    if($UiOnly) { & $exe $output '--ui-only' }
    else { & $exe $output (Join-Path $GamePath 'Rat Game_Data\Managed\Assembly-CSharp.dll') }
    $result=Get-Content -LiteralPath (Join-Path $output ($edition.ToLowerInvariant()+'-regression.txt')) -Encoding UTF8
    $result | Select-Object -Last 3
    if($LASTEXITCODE -ne 0) { throw 'Offline regression failed.' }
}
