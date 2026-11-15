param([string]$GamePath='A:\Steam\steamapps\common\rat game',[switch]$ReusePlugins,[switch]$Test)
$ErrorActionPreference='Stop'
Set-Location -LiteralPath $PSScriptRoot
if(-not $ReusePlugins) { & (Join-Path $PSScriptRoot 'build-assist.ps1') -GamePath $GamePath }
Add-Type -AssemblyName System.IO.Compression
Add-Type -AssemblyName System.IO.Compression.FileSystem
New-Item -ItemType Directory -Path 'build','dist' -Force | Out-Null
$loader=Join-Path $PSScriptRoot '.tools\bepinex'
$entries=@()
foreach($file in Get-ChildItem -LiteralPath (Join-Path $loader 'BepInEx\core') -File) {
    $entries+=[pscustomobject]@{Source=$file.FullName;Relative=('BepInEx\core\'+$file.Name)}
}
foreach($file in @('winhttp.dll','doorstop_config.ini','.doorstop_version')) {
    $entries+=[pscustomobject]@{Source=(Join-Path $loader $file);Relative=$file}
}
foreach($file in @('RatAssist.Core.dll','RatAssist.Markers.dll','RatAssist.TeamMap.dll')) {
    $entries+=[pscustomobject]@{Source=(Join-Path (Join-Path $PSScriptRoot 'build\assist') $file);Relative=('BepInEx\plugins\RatAssist\'+$file)}
}
$manifest=@($entries | ForEach-Object { [pscustomobject]@{Relative=$_.Relative;Hash=(Get-FileHash -LiteralPath $_.Source).Hash} })
$manifestPath=Join-Path $PSScriptRoot 'build\assist-installer-manifest.json'
[IO.File]::WriteAllText($manifestPath,([pscustomobject]@{Version='1.0.0';Files=$manifest} | ConvertTo-Json -Depth 5),(New-Object Text.UTF8Encoding($false)))
$payload=Join-Path $PSScriptRoot 'build\assist-installer-payload.zip'
$stream=[IO.File]::Open($payload,[IO.FileMode]::Create)
try {
    $archive=New-Object IO.Compression.ZipArchive($stream,[IO.Compression.ZipArchiveMode]::Create,$true)
    try {
        foreach($entry in $entries) { [IO.Compression.ZipFileExtensions]::CreateEntryFromFile($archive,$entry.Source,$entry.Relative.Replace('\','/'),[IO.Compression.CompressionLevel]::Optimal) | Out-Null }
        [IO.Compression.ZipFileExtensions]::CreateEntryFromFile($archive,$manifestPath,'package-manifest.json',[IO.Compression.CompressionLevel]::Optimal) | Out-Null
    } finally { $archive.Dispose() }
} finally { $stream.Dispose() }
$framework='C:\Windows\Microsoft.NET\Framework64\v4.0.30319'
$common=@('/nologo','/noconfig','/platform:x64','/optimize+','/codepage:65001',('/win32manifest:'+(Join-Path $PSScriptRoot 'assist-installer\app.manifest')),('/resource:'+$payload+',RatTrainer.Payload.zip'),('/resource:'+(Join-Path $PSScriptRoot 'desktop\assets\rat.png')+',RatTrainer.Art.rat.png'))
foreach($reference in @('System','System.Core','Accessibility','System.Drawing','System.Windows.Forms','System.Web.Extensions','System.IO.Compression','System.IO.Compression.FileSystem')) { $common+='/reference:'+(Join-Path $framework ($reference+'.dll')) }
foreach($file in @('Controls.cs','InputControls.cs','WindowChrome.cs','Infrastructure.cs','Installer.cs','ComponentInstaller.cs')) { $common+=Join-Path (Join-Path $PSScriptRoot 'desktop') $file }
$common+=@((Join-Path $PSScriptRoot 'src\Options.cs'),(Join-Path $PSScriptRoot 'src\DesktopProtocol.cs'))
$common+=Get-ChildItem -LiteralPath (Join-Path $PSScriptRoot 'assist-installer') -Filter '*.cs' | Select-Object -ExpandProperty FullName
$output=Join-Path $PSScriptRoot 'dist\RatAssist-Installer-v1.0.exe'
& (Join-Path $framework 'csc.exe') /target:winexe ('/out:'+$output) @common
if($LASTEXITCODE -ne 0) { throw 'Assist installer compilation failed.' }
Write-Output ('Standalone installer: '+$output)
Write-Output ('Size: '+(Get-Item -LiteralPath $output).Length+' bytes; payload includes only loader and RatAssist plugins.')
if($Test) {
    $testOutput=Join-Path $PSScriptRoot 'test-output\assist-installer'
    New-Item -ItemType Directory -Path $testOutput -Force | Out-Null
    $testExe=Join-Path $testOutput 'AssistInstallerTests.exe'
    & (Join-Path $framework 'csc.exe') /target:exe /main:AssistInstallerTests ('/out:'+$testExe) @common (Join-Path $PSScriptRoot 'tests\AssistInstallerTests.cs')
    if($LASTEXITCODE -ne 0) { throw 'Installer test compilation failed.' }
    & $testExe $testOutput (Join-Path $GamePath 'Rat Game_Data\Managed\Assembly-CSharp.dll')
    if($LASTEXITCODE -ne 0) { throw 'Assist installer tests failed.' }
}
