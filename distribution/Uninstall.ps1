# Keep all user task data. No administrator rights needed.
$ErrorActionPreference = 'Stop'
$destination = [IO.Path]::GetFullPath((Join-Path $env:LOCALAPPDATA 'Programs\ReMinder'))
$expected = [IO.Path]::GetFullPath((Join-Path $env:LOCALAPPDATA 'Programs\ReMinder'))
if ($destination -ne $expected -or [IO.Path]::GetPathRoot($destination) -eq $destination) { throw 'Unsafe uninstall path.' }
$executable = Join-Path $destination 'ReMinder.exe'
$running = Get-Process -Name ReMinder -ErrorAction SilentlyContinue | Where-Object Path -EQ $executable
if ($running) { throw 'Please exit Re:minder from its tray menu before uninstalling.' }
if (Test-Path -LiteralPath $executable) { $helper = Start-Process -FilePath $executable -ArgumentList '--unregister-notifications' -PassThru -WindowStyle Hidden; $helper.WaitForExit(); if ($helper.ExitCode -ne 0) { throw 'Notification cleanup failed; installation was kept.' } }
$key = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Run'
$value = (Get-ItemProperty -LiteralPath $key -Name ReMinder -ErrorAction SilentlyContinue).ReMinder
if ($value -eq ('"' + $executable + '" --silent')) { Remove-ItemProperty -LiteralPath $key -Name ReMinder }
$shortcut = Join-Path ([Environment]::GetFolderPath('Programs')) 'Re-minder.lnk'
if (Test-Path -LiteralPath $shortcut) { Remove-Item -LiteralPath $shortcut }
# If this script itself resides in the installation, move to a neutral directory first.
Set-Location -LiteralPath $env:TEMP
if (Test-Path -LiteralPath $destination) { Remove-Item -LiteralPath $destination -Recurse -Force }
Write-Host 'Uninstalled. Your task data is preserved in %LOCALAPPDATA%\ReMinder.'
