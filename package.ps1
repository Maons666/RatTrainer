param([string]$GamePath = 'A:\Steam\steamapps\common\rat game')
$ErrorActionPreference='Stop'
Set-Location -LiteralPath $PSScriptRoot
& (Join-Path $PSScriptRoot 'build.ps1') -GamePath $GamePath
& (Join-Path $PSScriptRoot 'build-assist.ps1') -GamePath $GamePath
$destination=Join-Path $PSScriptRoot 'dist\RatTrainer'
$coreDestination=Join-Path $destination 'BepInEx\core'
New-Item -ItemType Directory -Path $coreDestination -Force | Out-Null
$coreFiles=@(Get-ChildItem -LiteralPath '.tools\bepinex\BepInEx\core' -File)
foreach($file in $coreFiles) { Copy-Item -LiteralPath $file.FullName -Destination (Join-Path $coreDestination $file.Name) -Force }
foreach($file in @('winhttp.dll','doorstop_config.ini','.doorstop_version')) { Copy-Item -LiteralPath (Join-Path '.tools\bepinex' $file) -Destination (Join-Path $destination $file) -Force }
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'tools\legacy\Manage-RatTrainer.ps1') -Destination (Join-Path $destination 'Manage-RatTrainer.ps1') -Force
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'README.md') -Destination (Join-Path $destination 'README.md') -Force
$files=@($coreFiles | ForEach-Object { Get-Item -LiteralPath (Join-Path $coreDestination $_.Name) })
$files+=Get-Item -LiteralPath (Join-Path $destination 'BepInEx\plugins\RatTrainer\RatTrainer.dll')
$files+=Get-Item -LiteralPath (Join-Path $destination 'winhttp.dll'),(Join-Path $destination 'doorstop_config.ini'),(Join-Path $destination '.doorstop_version')
$files+=Get-ChildItem -LiteralPath (Join-Path $destination 'BepInEx\plugins\RatAssist') -Filter '*.dll' -File
$manifest=@($files | ForEach-Object { [pscustomobject]@{ Relative=$_.FullName.Substring($destination.Length+1); Hash=(Get-FileHash -LiteralPath $_.FullName).Hash } })
[IO.File]::WriteAllText((Join-Path $destination 'package-manifest.json'),([pscustomobject]@{ Version='1.3.0'; Files=$manifest } | ConvertTo-Json -Depth 5),(New-Object Text.UTF8Encoding($false)))
Write-Output "Package ready: $destination"
