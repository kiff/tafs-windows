# The setup installs quietly, every file the publish holds is where it put it, tafs starts from there and saves its
# sign-in screen, and it uninstalls, or this fails saying where. The zip job runs it on the unsigned setup, and the sign
# job on the signed one. With -From, the setup goes over that earlier setup's install first, as people update.
param(
  [Parameter(Mandatory = $true)] [string] $Setup,
  # An earlier release's setup, installed first: the check is then the update over it.
  [string] $From,
  # The publish the setup was built from: every file in it must be installed.
  [string] $AppDir
)
$ErrorActionPreference = 'Stop'
$Setup = (Resolve-Path $Setup).Path
if ($From) {
  $From = (Resolve-Path $From).Path
  $f = Start-Process $From -ArgumentList '/quiet', '/norestart', '/log', 'previous.log' -Wait -PassThru
  if ($f.ExitCode -ne 0) { Get-Content previous.log -Tail 60; throw "the earlier release's install: $($f.ExitCode)" }
  Write-Host "installed the earlier release first: $(Split-Path $From -Leaf)"
}
$p = Start-Process $Setup -ArgumentList '/quiet', '/norestart', '/log', 'install.log' -Wait -PassThru
if ($p.ExitCode -ne 0) { Get-Content install.log -Tail 60; throw "install: $($p.ExitCode)" }
$dir = Join-Path $env:ProgramFiles 'tafs'
$app = Join-Path $dir 'Tafs.App.exe'
if (-not (Test-Path $app)) { throw "not installed at $app" }
if ($AppDir) {
  $root = (Resolve-Path $AppDir).Path
  $missing = Get-ChildItem $root -Recurse -File | ForEach-Object { $_.FullName.Substring($root.Length).TrimStart('\') } |
    Where-Object { -not (Test-Path (Join-Path $dir $_)) }
  if ($missing) { $missing | Select-Object -First 40 | ForEach-Object { Write-Host "missing: $_" }; throw "$(@($missing).Count) files of the publish aren't installed" }
  Write-Host 'every file of the publish is installed'
}
$run = Start-Process $app -ArgumentList '--page', 'sign-in', '--screenshot', "$PWD\installed-start.png" -PassThru
if (-not $run.WaitForExit(120000)) { $run.Kill(); throw 'the installed app did not finish its screenshot' }
if (-not (Test-Path installed-start.png) -or (Get-Item installed-start.png).Length -eq 0) { throw 'the installed app did not start' }
$u = Start-Process $Setup -ArgumentList '/uninstall', '/quiet', '/norestart', '/log', 'uninstall.log' -Wait -PassThru
if ($u.ExitCode -ne 0) { Get-Content uninstall.log -Tail 60; throw "uninstall: $($u.ExitCode)" }
if (Test-Path $app) { throw 'still there after uninstalling' }
Remove-Item installed-start.png -ErrorAction SilentlyContinue
Write-Host 'installed, started and uninstalled'
