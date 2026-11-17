# The setup installs quietly, tafs starts from where it put it and saves its sign-in screen, and it uninstalls, or this
# fails saying where. The zip job runs it on the unsigned setup, and the sign job on the signed one.
param([Parameter(Mandatory = $true)] [string] $Setup)
$ErrorActionPreference = 'Stop'
$Setup = (Resolve-Path $Setup).Path
$p = Start-Process $Setup -ArgumentList '/quiet', '/norestart', '/log', 'install.log' -Wait -PassThru
if ($p.ExitCode -ne 0) { Get-Content install.log -Tail 60; throw "install: $($p.ExitCode)" }
$app = Join-Path $env:ProgramFiles 'tafs\Tafs.App.exe'
if (-not (Test-Path $app)) { throw "not installed at $app" }
$run = Start-Process $app -ArgumentList '--page', 'sign-in', '--screenshot', "$PWD\installed-start.png" -PassThru
if (-not $run.WaitForExit(120000)) { $run.Kill(); throw 'the installed app did not finish its screenshot' }
if (-not (Test-Path installed-start.png) -or (Get-Item installed-start.png).Length -eq 0) { throw 'the installed app did not start' }
$u = Start-Process $Setup -ArgumentList '/uninstall', '/quiet', '/norestart', '/log', 'uninstall.log' -Wait -PassThru
if ($u.ExitCode -ne 0) { Get-Content uninstall.log -Tail 60; throw "uninstall: $($u.ExitCode)" }
if (Test-Path $app) { throw 'still there after uninstalling' }
Write-Host 'installed, started and uninstalled'
