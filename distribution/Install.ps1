# Optional per-user installation. Run only after closing Re:minder.
$ErrorActionPreference = 'Stop'
$destination = Join-Path $env:LOCALAPPDATA 'Programs\ReMinder'
$executable = Join-Path $destination 'ReMinder.exe'
if (Test-Path -LiteralPath $executable) {
    $running = Get-Process -Name ReMinder -ErrorAction SilentlyContinue | Where-Object Path -EQ $executable
    if ($running) { throw 'Please exit the installed Re:minder from its tray menu before updating.' }
}
New-Item -ItemType Directory -Path $destination -Force | Out-Null
if ([IO.Path]::GetFullPath($PSScriptRoot) -ne [IO.Path]::GetFullPath($destination)) { Get-ChildItem -LiteralPath $PSScriptRoot | ForEach-Object { Copy-Item -LiteralPath $_.FullName -Destination $destination -Recurse -Force } }
$shell = New-Object -ComObject WScript.Shell
$link = $shell.CreateShortcut((Join-Path ([Environment]::GetFolderPath('Programs')) 'Re-minder.lnk'))
$link.TargetPath = $executable
$link.WorkingDirectory = $destination
$link.IconLocation = $executable
$link.Save()
Write-Host "Installed for this user: $destination"
Write-Host 'Launch ReMinder.exe or Re-minder from Start. Startup remains off until enabled in Settings.'
