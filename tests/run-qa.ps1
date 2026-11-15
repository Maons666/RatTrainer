$ErrorActionPreference='Stop'
Set-Location -LiteralPath (Split-Path -Parent $PSScriptRoot)
$runs=@()
$qaRoot=[IO.File]::ReadAllText((Join-Path (Get-Location) 'test-output\qa-location.txt')).Trim()
if(-not ([IO.Path]::GetFullPath($qaRoot)).StartsWith([IO.Path]::GetFullPath($env:TEMP).TrimEnd('\')+'\rat-trainer-qa-', [StringComparison]::OrdinalIgnoreCase)) { throw 'Unexpected QA root' }
foreach($name in @('qa-host','qa-client')) {
    $target=[IO.Path]::GetFullPath((Join-Path $qaRoot $name))
    foreach($file in @('qa-result.json','host-done.txt','trainer-panel.png')) { $result=Join-Path $target $file; if(Test-Path -LiteralPath $result) { Remove-Item -LiteralPath $result } }
    $arguments=@(('--rat-'+$name),'-screen-fullscreen','0','-screen-width','1280','-screen-height','800','-logFile',('"'+(Join-Path $target 'qa-player.log')+'"'))
    if($name -eq 'qa-host') { $arguments+=@('-batchmode','-nographics') }
    $process=Start-Process -FilePath (Join-Path $target 'Rat Game.exe') -ArgumentList $arguments -WorkingDirectory $target -WindowStyle Hidden -PassThru
    $runs += [pscustomobject]@{ Role=$name; Pid=$process.Id; Root=$target }
}
$runs | ConvertTo-Json
