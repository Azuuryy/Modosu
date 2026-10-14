param([string] $Configuration = 'Release')
$ErrorActionPreference = 'Stop'
$repo = Split-Path $PSScriptRoot
$dll = Join-Path $repo "osu.Game.Rulesets.Modosu\bin\$Configuration\net10.0\osu.Game.Rulesets.Modosu.dll"
if (!(Test-Path -LiteralPath $dll)) { throw 'Build the ruleset before preparing the package.' }
$package = Join-Path $PSScriptRoot 'package'
[IO.Directory]::CreateDirectory($package) | Out-Null
Copy-Item -LiteralPath $dll -Destination (Join-Path $package 'osu.Game.Rulesets.Modosu.dll') -Force
$validation = @{
    ControlsValidated = $false
    DllSha256 = (Get-FileHash -LiteralPath $dll).Hash
    CoreSha256 = (Get-FileHash -LiteralPath (Join-Path $PSScriptRoot 'ModosuOffline.Core.psm1')).Hash
    ClientCompatibilityValidated = $false
    Note = 'Package delivery is locked by default. Transition, NUnit, visual, and firewall checks are required before validation.'
}
[IO.File]::WriteAllText((Join-Path $package 'validation.json'), ($validation | ConvertTo-Json), [Text.UTF8Encoding]::new($false))
Write-Host 'Package prepared without installation. The validation lock remains active.'
