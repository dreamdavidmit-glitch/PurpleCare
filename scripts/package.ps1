$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$build = Join-Path $root ('artifacts\package-build-' + [Guid]::NewGuid().ToString('N'))
& (Join-Path $PSScriptRoot 'build.ps1') -OutputDirectory $build
$version = [Diagnostics.FileVersionInfo]::GetVersionInfo((Join-Path $build 'PurpleCare.exe')).FileVersion
$version = ([Version]$version).ToString(3)
$dist = Join-Path $root 'dist'
$zip = Join-Path $dist ('PurpleCare-' + $version + '-windows-x64.zip')
if (Test-Path -LiteralPath $zip) { throw 'Package already exists. Move the previous ZIP aside before packaging again.' }
$stage = Join-Path $root ('artifacts\package-stage-' + [Guid]::NewGuid().ToString('N') + '\PurpleCare')
New-Item -ItemType Directory -Path $stage,$dist,(Join-Path $stage 'docs') -Force | Out-Null
# Only known build outputs and public documentation enter the release.
Copy-Item -LiteralPath (Join-Path $build 'PurpleCare.exe') -Destination (Join-Path $stage '紫芯电脑助手.exe')
Copy-Item -LiteralPath (Join-Path $build 'LegacyLauncher.exe') -Destination (Join-Path $stage '紫芯电脑助手-v1.4.exe')
foreach ($name in @('README.md','LICENSE','THIRD_PARTY_NOTICES.md','CHANGELOG.md')) {
 Copy-Item -LiteralPath (Join-Path $root $name) -Destination $stage
}
foreach ($name in @('USAGE.md','VALIDATION.md')) {
 Copy-Item -LiteralPath (Join-Path $root ('docs\' + $name)) -Destination (Join-Path $stage 'docs')
}
Copy-Item -LiteralPath (Join-Path $root 'src\PurpleCare\Assets\图片来源.txt') -Destination $stage
Compress-Archive -LiteralPath $stage -DestinationPath $zip -CompressionLevel Optimal
$digest = (Get-FileHash -LiteralPath $zip -Algorithm SHA256).Hash.ToLowerInvariant()
[IO.File]::WriteAllText(($zip + '.sha256'),($digest + '  ' + [IO.Path]::GetFileName($zip) + [Environment]::NewLine),[Text.UTF8Encoding]::new($false))
Write-Output $zip
Write-Output ($zip + '.sha256')
