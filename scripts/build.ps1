param([string]$OutputDirectory = (Join-Path $PSScriptRoot '..\artifacts\build'))
$ErrorActionPreference = 'Stop'
if ($env:OS -ne 'Windows_NT') { throw 'PurpleCare requires Windows to build.' }
& (Join-Path $PSScriptRoot '..\src\PurpleCare\build.ps1') -OutputDirectory ([IO.Path]::GetFullPath($OutputDirectory))
