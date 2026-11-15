$ErrorActionPreference = 'Stop'
Set-Location -LiteralPath $PSScriptRoot
New-Item -ItemType Directory -Path '.tools' -Force | Out-Null
$url = 'https://github.com/BepInEx/BepInEx/releases/download/v5.4.23.6/BepInEx_win_x64_5.4.23.6.zip'
$archive = Join-Path $PSScriptRoot '.tools\BepInEx_win_x64_5.4.23.6.zip'
if (-not (Test-Path -LiteralPath $archive)) { Invoke-WebRequest -Uri $url -OutFile $archive -UseBasicParsing }
if ((Get-FileHash -LiteralPath $archive).Hash.ToLowerInvariant() -ne '6c57a714a892efb4e9e1fef63d608a3391f30ec984031c465404e4ee381a0755') { throw 'Official archive checksum mismatch.' }
Expand-Archive -LiteralPath $archive -DestinationPath '.tools\bepinex' -Force
Write-Output 'BepInEx 5.4.23.6 x64 is ready.'
