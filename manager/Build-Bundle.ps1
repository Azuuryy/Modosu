param()
$ErrorActionPreference = 'Stop'
$repo = Split-Path $PSScriptRoot
$package = Join-Path $PSScriptRoot 'package'
if (!(Test-Path -LiteralPath (Join-Path $package 'validation.json'))) { throw 'Prepare the package with Build-Package or Validate-Package first.' }
$artifacts = Join-Path $repo 'artifacts'
[IO.Directory]::CreateDirectory($artifacts) | Out-Null
$zip = Join-Path $artifacts 'ModosuOffline-Windows.zip'
$files = @((Join-Path $PSScriptRoot 'ModosuOffline.ps1'), (Join-Path $PSScriptRoot 'ModosuOffline.Core.psm1'), (Join-Path $PSScriptRoot 'ModosuOffline.Firewall.ps1'), (Join-Path $PSScriptRoot 'Launch Modosu Offline.cmd'), (Join-Path $PSScriptRoot 'GUIDE.md'), $package)
Compress-Archive -LiteralPath $files -DestinationPath $zip -Force
Write-Host "Package archive created: $zip"
