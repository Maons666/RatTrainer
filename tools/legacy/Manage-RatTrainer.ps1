param(
    [ValidateSet('Install','Uninstall','Check')][string]$Action = 'Install',
    [string]$GamePath = 'A:\Steam\steamapps\common\rat game',
    [switch]$DryRun
)
$ErrorActionPreference = 'Stop'
$root = [IO.Path]::GetFullPath($GamePath).TrimEnd('\')
$prefix = $root + '\'
function GameFile([string]$relative) {
    if ([IO.Path]::IsPathRooted($relative)) { throw 'Absolute paths are not allowed in the install manifest.' }
    $full = [IO.Path]::GetFullPath((Join-Path $root $relative))
    if (-not $full.StartsWith($prefix, [StringComparison]::OrdinalIgnoreCase)) { throw 'Path escaped the selected game directory.' }
    return $full
}
$exe = GameFile 'Rat Game.exe'
if (-not (Test-Path -LiteralPath $exe -PathType Leaf)) { throw "Rat Game.exe was not found in $root" }
$recordPath = GameFile '.rat-trainer-install.json'
$record = if (Test-Path -LiteralPath $recordPath) { Get-Content -LiteralPath $recordPath -Raw -Encoding UTF8 | ConvertFrom-Json } else { $null }
if ($Action -ne 'Check' -and $record -and $record.Version -ge 2) { throw 'Use the desktop Trainer EXE to manage this shared component installation.' }
if ($Action -ne 'Check') {
    $running = @(Get-Process -Name 'Rat Game' -ErrorAction SilentlyContinue | Where-Object { -not $_.Path -or $_.Path -eq $exe })
    if ($running.Count -gt 0) { throw 'Close Rat Game before installing or uninstalling. Your game will not be terminated automatically.' }
}
if ($Action -eq 'Uninstall') {
    if (-not $record) { Write-Output 'Rat Trainer is not installed by this package.'; return }
    $others = @(Get-ChildItem -LiteralPath (GameFile 'BepInEx\plugins') -Filter '*.dll' -Recurse -File -ErrorAction SilentlyContinue | Where-Object { $_.FullName -ne (GameFile 'BepInEx\plugins\RatTrainer\RatTrainer.dll') })
    $skipped = 0
    foreach ($entry in $record.Files) {
        $target = GameFile $entry.Relative
        $runtime = $entry.Relative -notlike 'BepInEx\plugins\RatTrainer\*'
        if ($runtime -and $others.Count -gt 0) { Write-Output "Keeping shared runtime: $($entry.Relative)"; continue }
        if (-not (Test-Path -LiteralPath $target)) { continue }
        if ((Get-FileHash -LiteralPath $target).Hash -ne $entry.InstalledHash) { Write-Output "Keeping modified file: $($entry.Relative)"; $skipped++; continue }
        if ($DryRun) { Write-Output "Would restore/remove: $($entry.Relative)"; continue }
        if ($entry.Backup) {
            $backup = GameFile $entry.Backup
            if (-not (Test-Path -LiteralPath $backup)) { throw "Missing original-file backup: $backup" }
            Copy-Item -LiteralPath $backup -Destination $target -Force
        } else { Remove-Item -LiteralPath $target -Force }
    }
    if (-not $DryRun -and $skipped -eq 0) { Remove-Item -LiteralPath $recordPath -Force }
    Write-Output 'Uninstall completed. Configuration and save backups were preserved.'
    return
}
$assembly = GameFile 'Rat Game_Data\Managed\Assembly-CSharp.dll'
$expected = '517DD87905AC3E2DCF845384C440293AEAAA82773F6977964AD459070A21E1C1'
if ((Get-FileHash -LiteralPath $assembly).Hash -ne $expected) { throw 'This game build differs from the analyzed Build 26403247. Rebuild and review compatibility before installing.' }
if ($Action -eq 'Check') {
    Write-Output ('Game version: compatible; installed: ' + [bool]$record)
    return
}
$packagePath = Join-Path $PSScriptRoot 'package-manifest.json'
if (-not (Test-Path -LiteralPath $packagePath)) { throw 'Run scripts\package.ps1 first, then use the installer inside dist\RatTrainer.' }
$package = Get-Content -LiteralPath $packagePath -Raw -Encoding UTF8 | ConvertFrom-Json
$existingCore = GameFile 'BepInEx\core\BepInEx.dll'
if (Test-Path -LiteralPath $existingCore) {
    $ourCore = Join-Path $PSScriptRoot 'BepInEx\core\BepInEx.dll'
    if ((Get-FileHash -LiteralPath $existingCore).Hash -ne (Get-FileHash -LiteralPath $ourCore).Hash) { throw 'A different BepInEx version is installed. The installer will not overwrite another mod loader.' }
} elseif (Test-Path -LiteralPath (GameFile 'winhttp.dll')) { throw 'An existing winhttp.dll belongs to another loader. It will not be overwritten.' }
$entries = New-Object System.Collections.Generic.List[object]
if ($record) { foreach ($item in $record.Files) { $entries.Add($item) } }
$planned = New-Object System.Collections.Generic.List[object]
foreach ($file in $package.Files) {
    if ($file.Relative -like 'BepInEx\plugins\RatAssist\*') { continue }
    $target = GameFile $file.Relative
    $source = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot $file.Relative))
    if (-not $source.StartsWith($PSScriptRoot.TrimEnd('\') + '\', [StringComparison]::OrdinalIgnoreCase)) { throw 'Package path escaped its directory.' }
    if ((Get-FileHash -LiteralPath $source).Hash -ne $file.Hash) { throw "Package checksum mismatch: $($file.Relative)" }
    if (Test-Path -LiteralPath $target) {
        if ((Get-FileHash -LiteralPath $target).Hash -eq $file.Hash) { continue }
        if ($file.Relative -ne 'BepInEx\plugins\RatTrainer\RatTrainer.dll') { throw "Existing loader file differs: $($file.Relative)" }
    }
    $planned.Add([pscustomobject]@{ Relative=$file.Relative; Source=$source; Target=$target; Hash=$file.Hash })
}
if ($DryRun) { $planned | Select-Object Relative,Hash; Write-Output "Dry run: $($planned.Count) files would be installed."; return }
$rollbackDirectory = 'BepInEx\config\RatTrainer\install-backups\' + [DateTime]::UtcNow.ToString('yyyyMMdd-HHmmss-fff')
$changed = New-Object System.Collections.Generic.List[object]
try {
    foreach ($item in $planned) {
        $previous = @($entries | Where-Object { $_.Relative -eq $item.Relative }) | Select-Object -First 1
        $backup = $null
        if (Test-Path -LiteralPath $item.Target) {
            $backup = $rollbackDirectory + '\' + [Guid]::NewGuid().ToString('N') + '.bak'
            $backupPath = GameFile $backup
            New-Item -ItemType Directory -Path (Split-Path -Parent $backupPath) -Force | Out-Null
            Copy-Item -LiteralPath $item.Target -Destination $backupPath
        }
        $changed.Add([pscustomobject]@{ Target=$item.Target; Backup=$backup })
        New-Item -ItemType Directory -Path (Split-Path -Parent $item.Target) -Force | Out-Null
        Copy-Item -LiteralPath $item.Source -Destination $item.Target -Force
        if ($previous) { $previous.InstalledHash = $item.Hash }
        else { $entries.Add([pscustomobject]@{ Relative=$item.Relative; InstalledHash=$item.Hash; Backup=$backup }) }
    }
    $data = [pscustomobject]@{ Version=1; GamePath=$root; GameAssemblyHash=$expected; Files=@($entries.ToArray()) }
    $json = $data | ConvertTo-Json -Depth 6
    [IO.File]::WriteAllText($recordPath, $json, (New-Object Text.UTF8Encoding($false)))
} catch {
    for ($i=$changed.Count-1; $i -ge 0; $i--) {
        $change=$changed[$i]
        if ($change.Backup) { Copy-Item -LiteralPath (GameFile $change.Backup) -Destination $change.Target -Force }
        elseif (Test-Path -LiteralPath $change.Target) { Remove-Item -LiteralPath $change.Target -Force }
    }
    throw
}
Write-Output "Rat Trainer installed in $root. Start the game and press F6."
