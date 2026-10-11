[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
$root = (Resolve-Path "$PSScriptRoot\..").Path
$preview = Join-Path $root 'artifacts\XenMotion'
$app = Join-Path $preview 'App'

& "$PSScriptRoot\Build-Release.ps1" -OutputDirectory $app -SkipInstaller

$exe = Join-Path $app 'XenMotion.exe'
if (-not (Test-Path -LiteralPath $exe)) { throw 'Published XenMotion.exe is missing.' }
$shell = New-Object -ComObject WScript.Shell
$shortcut = $shell.CreateShortcut((Join-Path $preview 'XenMotion.lnk'))
$shortcut.TargetPath = $exe
$shortcut.Arguments = '--show'
$shortcut.WorkingDirectory = $app
$shortcut.IconLocation = "$exe,0"
$shortcut.Description = 'Open XenMotion'
$shortcut.Save()

$check = $shell.CreateShortcut((Join-Path $preview 'XenMotion.lnk'))
if ($check.TargetPath -ne $exe -or $check.Arguments -ne '--show') { throw 'XenMotion shortcut verification failed.' }
Write-Host "Open: $preview\XenMotion.lnk"
