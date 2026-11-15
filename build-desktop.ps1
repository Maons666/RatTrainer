param([string]$GamePath = 'A:\Steam\steamapps\common\rat game', [switch]$DesktopOnly)
$ErrorActionPreference = 'Stop'
Set-Location -LiteralPath $PSScriptRoot
if(-not $DesktopOnly) { & (Join-Path $PSScriptRoot 'package.ps1') -GamePath $GamePath }

Add-Type -AssemblyName System.IO.Compression
Add-Type -AssemblyName System.IO.Compression.FileSystem
$packageRoot = Join-Path $PSScriptRoot 'dist\RatTrainer'
$payloadPath = Join-Path $PSScriptRoot 'build\desktop-payload.zip'
$manifestPath = Join-Path $packageRoot 'package-manifest.json'
if(-not $DesktopOnly) {
$manifest = Get-Content -LiteralPath $manifestPath -Raw -Encoding UTF8 | ConvertFrom-Json
$stream = [IO.File]::Open($payloadPath, [IO.FileMode]::Create)
try {
    $archive = New-Object IO.Compression.ZipArchive($stream, [IO.Compression.ZipArchiveMode]::Create, $true)
    try {
        foreach($relative in @($manifest.Files.Relative) + @('package-manifest.json')) {
            $source = Join-Path $packageRoot $relative
            $entryName = $relative.Replace('\', '/')
            [IO.Compression.ZipFileExtensions]::CreateEntryFromFile($archive, $source, $entryName, [IO.Compression.CompressionLevel]::Optimal) | Out-Null
        }
    } finally { $archive.Dispose() }
} finally { $stream.Dispose() }
} elseif(-not (Test-Path -LiteralPath $payloadPath -PathType Leaf)) { throw 'No existing payload. Run a full build first.' }

$framework = 'C:\Windows\Microsoft.NET\Framework64\v4.0.30319'
$compileArgs = @('/nologo', '/noconfig', '/target:winexe', '/platform:x64', '/optimize+', '/codepage:65001',
    ('/win32manifest:' + (Join-Path $PSScriptRoot 'desktop\app.manifest')),
    ('/resource:' + $payloadPath + ',RatTrainer.Payload.zip'))
foreach($reference in @('System','System.Core','Accessibility','System.Drawing','System.Windows.Forms','System.Web.Extensions','System.IO.Compression','System.IO.Compression.FileSystem')) {
    $compileArgs += '/reference:' + (Join-Path $framework ($reference + '.dll'))
}
$compileArgs += Join-Path $PSScriptRoot 'src\Options.cs'
$compileArgs += Join-Path $PSScriptRoot 'src\DesktopProtocol.cs'
$compileArgs += Get-ChildItem -LiteralPath (Join-Path $PSScriptRoot 'desktop') -Filter '*.cs' | Select-Object -ExpandProperty FullName
foreach($art in Get-ChildItem -LiteralPath (Join-Path $PSScriptRoot 'desktop\assets') -Filter '*.png') {
    $compileArgs += '/resource:' + $art.FullName + ',RatTrainer.Art.' + $art.Name
}
foreach($edition in @('All', 'Host')) {
    $outputPath = Join-Path $PSScriptRoot ('dist\RatTrainer-' + $edition + '-v1.3.3.exe')
    $editionArgs = @('/out:' + $outputPath) + $compileArgs
    if($edition -eq 'Host') { $editionArgs = @('/define:HOST_SANDBOX') + $editionArgs }
    & (Join-Path $framework 'csc.exe') @editionArgs
    if ($LASTEXITCODE -ne 0) { throw ($edition + ' desktop compilation failed.') }
    Write-Output ('Standalone EXE: ' + $outputPath)
    Write-Output ('Size: ' + (Get-Item -LiteralPath $outputPath).Length + ' bytes')
}
