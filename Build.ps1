param([switch]$SkipChecks)
$ErrorActionPreference = 'Stop'
Set-Location -LiteralPath $PSScriptRoot
$sdk = Join-Path $PSScriptRoot '.tools\dotnet\dotnet.exe'
if (-not (Test-Path -LiteralPath $sdk)) { $sdk = (Get-Command dotnet -ErrorAction Stop).Source }
$env:DOTNET_CLI_HOME = Join-Path $PSScriptRoot '.tools\dotnet-home'
$env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
$env:MSBuildEnableWorkloadResolver = 'false'
& $sdk restore 'src/ReMinder.App' --locked-mode
if ($LASTEXITCODE -ne 0) { throw 'Restore failed.' }
if (-not $SkipChecks) {
    & $sdk run --project 'tests/ReMinder.Checks' -c Release
    if ($LASTEXITCODE -ne 0) { throw 'Checks failed.' }
    & $sdk restore 'tests/ReMinder.MotionChecks' --locked-mode
    if ($LASTEXITCODE -ne 0) { throw 'Motion checks restore failed.' }
    & $sdk build 'tests/ReMinder.MotionChecks' -c Release --no-restore -m:1 -nodeReuse:false
    if ($LASTEXITCODE -ne 0) { throw 'Motion checks build failed.' }
    & './tests/ReMinder.MotionChecks/bin/Release/net10.0-windows10.0.19041.0/win-x64/ReMinder.MotionChecks.exe'
    if ($LASTEXITCODE -ne 0) { throw 'Motion checks failed.' }
}
& $sdk publish 'src/ReMinder.App' -c Release --no-restore -o 'artifacts/ReMinder-win-x64'
if ($LASTEXITCODE -ne 0) { throw 'Publish failed.' }
Copy-Item -LiteralPath 'distribution/Install.ps1','distribution/Uninstall.ps1','distribution/使用说明.md' -Destination 'artifacts/ReMinder-win-x64'
Write-Host 'Ready: artifacts/ReMinder-win-x64/ReMinder.exe'
