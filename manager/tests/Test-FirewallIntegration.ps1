param([string] $OutputPath = (Join-Path $PSScriptRoot 'firewall-result.json'))
$ErrorActionPreference = 'Stop'
Import-Module (Join-Path $PSScriptRoot '..\ModosuOffline.Core.psm1') -Force
$probeBuild = Join-Path $PSScriptRoot 'FirewallProbe\bin\Release\net10.0'
if (!(Test-Path -LiteralPath (Join-Path $probeBuild 'FirewallProbe.exe'))) { throw 'Build FirewallProbe in Release before running this test.' }
$fixture = Join-Path ([IO.Path]::GetTempPath()) ('ModosuOffline-firewall-' + [Guid]::NewGuid().ToString('N'))
$gameDir = Join-Path $fixture 'lazer\current'
[IO.Directory]::CreateDirectory($gameDir) | Out-Null
Get-ChildItem -LiteralPath $probeBuild -File | Copy-Item -Destination $gameDir
Rename-Item -LiteralPath (Join-Path $gameDir 'FirewallProbe.exe') -NewName 'osu!.exe'
$packageDir = Join-Path $fixture 'package'
[IO.Directory]::CreateDirectory($packageDir) | Out-Null
$dll = Join-Path $packageDir 'osu.Game.Rulesets.Modosu.dll'
Copy-Item -LiteralPath (Join-Path $PSScriptRoot '..\..\osu.Game.Rulesets.Modosu\bin\Release\net10.0\osu.Game.Rulesets.Modosu.dll') -Destination $dll
$core = Join-Path $PSScriptRoot '..\ModosuOffline.Core.psm1'
$receipt = @{ ControlsValidated = $true; DllSha256 = (Get-FileHash $dll).Hash; CoreSha256 = (Get-FileHash $core).Hash }
[IO.File]::WriteAllText((Join-Path $packageDir 'validation.json'), ($receipt | ConvertTo-Json))
$ctx = New-OfflineContext (Join-Path $fixture 'lazer') (Join-Path $fixture 'data') $dll (Join-Path $fixture 'vault')
function Invoke-TestHelper([string] $Mode) {
    $requestFile = Join-Path $fixture 'request.json'
    $request = @{ Action = $Mode; InstallDir = $ctx.InstallDir; DataDir = $ctx.DataDir; PackagePath = $ctx.PackagePath; VaultDir = $ctx.VaultDir }
    [IO.File]::WriteAllText($requestFile, ($request | ConvertTo-Json))
    & "$env:SystemRoot\System32\WindowsPowerShell\v1.0\powershell.exe" -NoProfile -ExecutionPolicy RemoteSigned -File (Join-Path $PSScriptRoot '..\ModosuOffline.Firewall.ps1') -RequestFile $requestFile
    if ($LASTEXITCODE -ne 0) { throw "Helper operation failed: $Mode." }
    $helperResult = Get-Content -LiteralPath ($requestFile + '.result') -Raw | ConvertFrom-Json
    if ($helperResult.Success -ne $true) { throw $helperResult.Error }
}
$result = @{ Validated = $false; Utc = [DateTime]::UtcNow.ToString('o'); Probe = 'TCP api.nuget.org:443'; Error = '' }
$sentinel = $ctx.RuleGroup + '-foreign-sentinel'
$initialGroup = $ctx.RuleGroup
$legacyGroup = $initialGroup.Replace('ModosuOffline-', 'MOsuOffline-')
try {
    Assert-FirewallEnabled
    & $ctx.GamePath
    if ($LASTEXITCODE -ne 0) { throw 'Initial connection failed; the test is inconclusive and validation is denied.' }
    New-NetFirewallRule -Name $sentinel -DisplayName 'Modosu Offline integration sentinel' -Group ($ctx.RuleGroup + '-foreign') -Program $ctx.GamePath -Direction Outbound -Action Allow -Profile Any -ErrorAction Stop | Out-Null
    Invoke-TestHelper 'Offline'
    if (!(Test-Path -LiteralPath $ctx.DllPath)) { throw 'DLL was not installed in the test fixture.' }
    Assert-NetworkBlocked $ctx
    & $ctx.GamePath
    if ($LASTEXITCODE -ne 3) { throw 'The probe connected despite the firewall block.' }
    Invoke-TestHelper 'Online'
    if (Test-Path -LiteralPath $ctx.DllPath) { throw 'DLL is still present in the test fixture.' }
    if (@(Get-OwnedFirewallRules $ctx).Count -ne 0) { throw 'Test firewall rules remain.' }
    if (!(Get-NetFirewallRule -Name $sentinel -ErrorAction Stop)) { throw 'A third-party firewall rule was removed.' }
    & $ctx.GamePath
    if ($LASTEXITCODE -ne 0) { throw 'Connectivity was not restored after removing the block.' }
    # Recreate a managed legacy installation only inside the temporary fixture.
    [IO.Directory]::CreateDirectory($ctx.RulesetsDir) | Out-Null
    Copy-Item -LiteralPath $dll -Destination $ctx.LegacyDllPath
    $ctx.RuleGroup = $legacyGroup
    Save-OfflineState $ctx 'Offline' (Get-FileHash -LiteralPath $ctx.LegacyDllPath).Hash
    Enable-NetworkBlock $ctx
    $ctx = New-OfflineContext $ctx.InstallDir $ctx.DataDir $ctx.PackagePath $ctx.VaultDir
    if ($ctx.RuleGroup -ne $legacyGroup) { throw 'Legacy firewall rule group was not preserved.' }
    Invoke-TestHelper 'Offline'
    if ((Test-Path -LiteralPath $ctx.LegacyDllPath) -or !(Test-Path -LiteralPath $ctx.DllPath)) { throw 'DLL migration is incomplete.' }
    Assert-NetworkBlocked $ctx
    & $ctx.GamePath
    if ($LASTEXITCODE -ne 3) { throw 'Network blocking was lost during migration.' }
    Invoke-TestHelper 'Online'
    if ((Test-Path -LiteralPath $ctx.LegacyDllPath) -or (Test-Path -LiteralPath $ctx.DllPath) -or @(Get-OwnedFirewallRules $ctx).Count -ne 0) { throw 'Legacy installation was not removed.' }
    if (!(Get-NetFirewallRule -Name $sentinel -ErrorAction Stop)) { throw 'A third-party firewall rule was removed during migration.' }
    & $ctx.GamePath
    if ($LASTEXITCODE -ne 0) { throw 'Connectivity was not restored after migration.' }
    $result.LegacyMigrationValidated = $true
    $result.Validated = $true
} catch {
    $result.Error = $_.Exception.Message
} finally {
    # Only this fixture's rules are removed, even if a test assertion failed.
    foreach ($rule in @(Get-NetFirewallRule -ErrorAction Stop | Where-Object { $_.Group -in @($initialGroup, $legacyGroup) -or ($_.Name -eq $sentinel -and $_.Group -eq ($initialGroup + '-foreign')) })) {
        Remove-NetFirewallRule -Name $rule.Name -ErrorAction Stop
    }
    [IO.File]::WriteAllText([IO.Path]::GetFullPath($OutputPath), ($result | ConvertTo-Json), [Text.UTF8Encoding]::new($false))
    $resolved = (Resolve-Path -LiteralPath $fixture).ProviderPath
    if ($resolved -ne [IO.Path]::GetFullPath($fixture) -or !(Split-Path $resolved -Leaf).StartsWith('ModosuOffline-firewall-')) { throw 'Refusing unsafe temporary-directory cleanup.' }
    Remove-Item -LiteralPath $resolved -Recurse -Force
}
if (!$result.Validated) { throw $result.Error }
Write-Host 'PASS Windows Firewall: connectivity, blocking, restoration, MOsu migration, and third-party rule preservation.'
