param([string]$OutputDirectory = (Join-Path $PSScriptRoot '..\Build'))
$ErrorActionPreference = 'Stop'
$frameworkDir = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319'
$compilerPath = Join-Path $frameworkDir 'csc.exe'
New-Item -ItemType Directory -Path $OutputDirectory -Force | Out-Null
$targetPath = Join-Path ([System.IO.Path]::GetFullPath($OutputDirectory)) 'PurpleCare.exe'
$referenceNames = @('System.dll','System.Core.dll','System.Management.dll','System.ServiceProcess.dll','System.Web.Extensions.dll','System.Xaml.dll','WPF\WindowsBase.dll','WPF\PresentationCore.dll','WPF\PresentationFramework.dll')
$compilerArgs = @('/nologo','/target:winexe','/platform:x64','/optimize+','/utf8output',('/out:' + $targetPath),('/win32manifest:' + (Join-Path $PSScriptRoot 'app.manifest')),('/resource:' + (Join-Path $PSScriptRoot 'MainWindow.xaml') + ',MainWindow.xaml'))
$compilerArgs += '/resource:' + (Join-Path $PSScriptRoot 'Assets\verstappen-austria.jpg') + ',verstappen-background.jpg'
foreach ($referenceName in $referenceNames) { $compilerArgs += '/reference:' + (Join-Path $frameworkDir $referenceName) }
$compilerArgs += @(Get-ChildItem -LiteralPath $PSScriptRoot -Filter '*.cs' | ForEach-Object { $_.FullName })
& $compilerPath @compilerArgs
if ($LASTEXITCODE -ne 0) { throw 'Compilation failed' }
$launcherArgs=@('/nologo','/target:winexe','/platform:x64','/optimize+','/utf8output',('/out:'+(Join-Path ([IO.Path]::GetFullPath($OutputDirectory)) 'LegacyLauncher.exe')),('/win32manifest:'+(Join-Path $PSScriptRoot 'app.manifest')),('/reference:'+(Join-Path $frameworkDir 'WPF\PresentationFramework.dll')),('/reference:'+(Join-Path $frameworkDir 'WPF\WindowsBase.dll')),(Join-Path $PSScriptRoot 'Launcher\Launcher.cs'))
foreach($referenceName in @('WPF\PresentationCore.dll','System.Xaml.dll')){$launcherArgs+='/reference:'+(Join-Path $frameworkDir $referenceName)}
& $compilerPath @launcherArgs
if ($LASTEXITCODE -ne 0) { throw 'Launcher compilation failed' }
Write-Output $targetPath
