param([switch]$SkipBuild)
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$exe = Join-Path $root 'artifacts\build\PurpleCare.exe'
if (-not $SkipBuild) { & (Join-Path $PSScriptRoot 'build.ps1') }
if (-not (Test-Path -LiteralPath $exe)) { throw 'Build the application first.' }
$testDir = Join-Path $root ('artifacts\tests\' + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $testDir -Force | Out-Null
$output = Join-Path $testDir 'self-tests.txt'
$process = Start-Process -FilePath $exe -ArgumentList ('--self-test "' + $output + '"') -PassThru -WindowStyle Hidden
try {
 if (-not $process.WaitForExit(120000)) { $process.Kill(); throw 'Synthetic self-tests exceeded 120 seconds.' }
 $process.Refresh()
 if (Test-Path -LiteralPath $output) { Get-Content -LiteralPath $output }
 if ($process.ExitCode -ne 0) { throw ('Synthetic self-tests failed: ' + $process.ExitCode) }
 if (-not (Test-Path -LiteralPath $output)) { throw 'Self-test output is missing.' }
} finally { $process.Dispose() }
