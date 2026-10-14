param([string] $OutputPath)
$ErrorActionPreference = 'Stop'
Import-Module (Join-Path $PSScriptRoot '..\ModosuOffline.Core.psm1') -Force
function Assert-True($Condition, [string] $Message) { if (!$Condition) { throw $Message } }
function Assert-Throws([scriptblock] $Action) {
    $failed = $false
    try { & $Action } catch { $failed = $true }
    Assert-True $failed 'A dangerous operation should have been rejected.'
}
$script:passed = 0
function Test-Case([string] $Name, [scriptblock] $Body) {
    $script:state = @{ Blocked = $false; Running = $false; VerifyCount = 0; Failure = ''; Events = [Collections.Generic.List[string]]::new() }
    $caseDir = Join-Path $testRoot ([Guid]::NewGuid().ToString('N'))
    [IO.Directory]::CreateDirectory((Join-Path $caseDir 'lazer\current')) | Out-Null
    [IO.Directory]::CreateDirectory((Join-Path $caseDir 'package')) | Out-Null
    [IO.File]::WriteAllText((Join-Path $caseDir 'lazer\current\osu!.exe'), 'fake-executable')
    [IO.File]::WriteAllText((Join-Path $caseDir 'package\osu.Game.Rulesets.Modosu.dll'), 'test-dll')
    $script:context = New-OfflineContext (Join-Path $caseDir 'lazer') (Join-Path $caseDir 'data') (Join-Path $caseDir 'package\osu.Game.Rulesets.Modosu.dll') (Join-Path $caseDir 'vault')
    $script:ops = @{
        AssertClosed = { param($c) if ($script:state.Running) { throw 'A game process is running.' } }
        Block = { param($c) if ($script:state.Failure -in @('Permission', 'FirewallDisabled')) { throw 'Network protection was rejected.' }; $script:state.Blocked = $true; $script:state.Events.Add('Block') }
        Verify = { param($c) $script:state.VerifyCount++; if (!$script:state.Blocked -or $script:state.Failure -eq ('Verify' + $script:state.VerifyCount)) { throw 'Protection verification failed.' }; $script:state.Events.Add('Verify') }
        Unblock = { param($c) Assert-True (!(Test-Path -LiteralPath $c.DllPath) -and !(Test-Path -LiteralPath $c.LegacyDllPath)) 'Network cannot be restored before DLL removal'; if ($script:state.Failure -eq 'Unblock') { throw 'Could not remove owned firewall rules.' }; $script:state.Blocked = $false; $script:state.Events.Add('Unblock') }
    }
    & $Body
    $script:passed++
    Write-Host "PASS $Name"
}
$testRoot = Join-Path ([IO.Path]::GetTempPath()) ('ModosuOffline-tests-' + [Guid]::NewGuid().ToString('N'))
function Initialize-LegacyInstallation {
    [IO.Directory]::CreateDirectory($context.RulesetsDir) | Out-Null
    [IO.File]::WriteAllText($context.LegacyDllPath, 'legacy-owned-dll')
    $context.RuleGroup = $context.RuleGroup.Replace('ModosuOffline-', 'MOsuOffline-')
    Save-OfflineState $context 'Offline' (Get-FileHash -LiteralPath $context.LegacyDllPath).Hash
    $script:context = New-OfflineContext $context.InstallDir $context.DataDir $context.PackagePath $context.VaultDir
    Assert-True ($context.RuleGroup.StartsWith('MOsuOffline-')) 'Legacy firewall rules were orphaned'
    $state.Blocked = $true
}
[IO.Directory]::CreateDirectory($testRoot) | Out-Null
try {
    Test-Case 'Repeated transitions preserve user data' {
        [IO.Directory]::CreateDirectory($context.DataDir) | Out-Null
        $profile = Join-Path $context.DataDir 'client.realm'
        [IO.File]::WriteAllText($profile, 'keep-profile-and-scores')
        1..3 | ForEach-Object {
            Invoke-OfflineTransition $context Offline $ops
            Assert-True $state.Blocked 'Network is not blocked'
            Assert-True (Test-Path -LiteralPath $context.DllPath) 'DLL is missing'
            Assert-True ((Get-OfflineState $context).Mode -eq 'Offline') 'State journal is incorrect'
            Invoke-OfflineTransition $context Online $ops
            Assert-True (!$state.Blocked) 'Firewall rules were not removed'
            Assert-True (!(Test-Path -LiteralPath $context.DllPath)) 'DLL is still installed'
        }
        Assert-True ((Get-Content -LiteralPath $profile -Raw) -eq 'keep-profile-and-scores') 'User data was altered'
    }
    foreach ($reason in @('Permission', 'FirewallDisabled')) {
        Test-Case $reason {
            $state.Failure = $reason
            Assert-Throws { Invoke-OfflineTransition $context Offline $ops }
            Assert-True (!(Test-Path -LiteralPath $context.DllPath)) 'DLL was installed without network protection'
        }
    }
    Test-Case 'Active game process' {
        $state.Running = $true
        Assert-Throws { Invoke-OfflineTransition $context Offline $ops }
        Assert-True ($state.Events.Count -eq 0) 'A transition started while the game was active'
    }
    foreach ($checkpoint in @('Verify1', 'Verify2')) {
        Test-Case "Interruption at $checkpoint" {
            $state.Failure = $checkpoint
            Assert-Throws { Invoke-OfflineTransition $context Offline $ops }
            Assert-True $state.Blocked 'Network block was lost after interruption'
            $state.Failure = ''
            Invoke-OfflineTransition $context Online $ops
            Assert-True (!$state.Blocked -and !(Test-Path -LiteralPath $context.DllPath)) 'Recovery failed'
        }
    }
    Test-Case 'Existing DLL is not overwritten' {
        [IO.Directory]::CreateDirectory($context.RulesetsDir) | Out-Null
        [IO.File]::WriteAllText($context.DllPath, 'foreign-dll')
        Assert-Throws { Invoke-OfflineTransition $context Offline $ops }
        Assert-Throws { Invoke-OfflineTransition $context Online $ops }
        Assert-True ((Get-Content -LiteralPath $context.DllPath -Raw) -eq 'foreign-dll') 'Third-party DLL was altered'
    }
    Test-Case 'Failed online transition keeps network blocked' {
        Invoke-OfflineTransition $context Offline $ops
        $state.Failure = 'Unblock'
        Assert-Throws { Invoke-OfflineTransition $context Online $ops }
        Assert-True $state.Blocked 'Network block was lost'
        Assert-True (!(Test-Path -LiteralPath $context.DllPath)) 'DLL was not removed'
    }
    Test-Case 'Process starts during removal' {
        Invoke-OfflineTransition $context Offline $ops
        $script:closedCalls = 0
        $ops.AssertClosed = { param($c) $script:closedCalls++; if ($script:closedCalls -gt 1) { throw 'A new process started.' } }
        Assert-Throws { Invoke-OfflineTransition $context Online $ops }
        Assert-True $state.Blocked 'Network was unblocked while a process was active'
    }
    Test-Case 'Storage inside rulesets is rejected' {
        Assert-Throws { New-OfflineContext $context.InstallDir $context.DataDir $context.PackagePath $context.RulesetsDir }
    }
    foreach ($checkpoint in @('Blocked', 'VerifiedBeforeInstall', 'InstallJournaled', 'Staged', 'Installed', 'VerifiedAfterInstall', 'OfflineJournaled')) {
        Test-Case "Interruption during offline setup after $checkpoint" {
            $script:failAt = $checkpoint
            $ops.Checkpoint = { param($name) if ($name -eq $script:failAt) { throw 'Injected interruption.' } }
            Assert-Throws { Invoke-OfflineTransition $context Offline $ops }
            Assert-True $state.Blocked 'The interruption unblocked the network'
            $ops.Remove('Checkpoint')
            Invoke-OfflineTransition $context Online $ops
            Assert-True (!(Test-Path -LiteralPath $context.DllPath) -and !$state.Blocked) 'Recovery failed'
        }
    }
    foreach ($checkpoint in @('RemoveJournaled', 'Parked', 'Removed', 'ClosedBeforeUnblock', 'Unblocked', 'OnlineJournaled')) {
        Test-Case "Interruption during online transition after $checkpoint" {
            Invoke-OfflineTransition $context Offline $ops
            $script:failAt = $checkpoint
            $ops.Checkpoint = { param($name) if ($name -eq $script:failAt) { throw 'Injected interruption.' } }
            Assert-Throws { Invoke-OfflineTransition $context Online $ops }
            Assert-True $state.Blocked 'The interruption left the network unblocked'
            $ops.Remove('Checkpoint')
            Invoke-OfflineTransition $context Online $ops
            Assert-True (!(Test-Path -LiteralPath $context.DllPath) -and !$state.Blocked) 'Recovery failed'
        }
    }
    Test-Case 'Additional Modosu copy detected without execution' {
        $dll = Join-Path $PSScriptRoot '..\..\osu.Game.Rulesets.Modosu\bin\Release\net10.0\osu.Game.Rulesets.Modosu.dll'
        [IO.Directory]::CreateDirectory($context.RulesetsDir) | Out-Null
        $extra = Join-Path $context.RulesetsDir 'osu.Game.Rulesets.OtherName.dll'
        Copy-Item -LiteralPath $dll -Destination $extra
        Assert-Throws { Invoke-OfflineTransition $context Offline $ops }
        Assert-Throws { Invoke-OfflineTransition $context Online $ops }
        Assert-True (Test-Path -LiteralPath $extra) 'Unmanaged copy was altered'
    }
    Test-Case 'Migration from MOsu to Modosu keeps network blocked' {
        Initialize-LegacyInstallation
        $oldHash = (Get-OfflineState $context).DllHash
        Invoke-OfflineTransition $context Offline $ops
        Assert-True ($state.Blocked -and (Test-Path -LiteralPath $context.DllPath) -and !(Test-Path -LiteralPath $context.LegacyDllPath)) 'Migration is incomplete'
        Assert-True (Test-Path -LiteralPath (Join-Path $context.VaultDir ('parked-' + $oldHash + '.dll'))) 'Legacy DLL was not backed up'
        $script:context = New-OfflineContext $context.InstallDir $context.DataDir $context.PackagePath $context.VaultDir
        Assert-True ($context.RuleGroup.StartsWith('MOsuOffline-')) 'Legacy rule group was lost during migration'
        Invoke-OfflineTransition $context Online $ops
        Assert-True (!$state.Blocked) 'Legacy network block was not removed'
    }
    Test-Case 'Return online from a managed legacy installation' {
        Initialize-LegacyInstallation
        Invoke-OfflineTransition $context Online $ops
        Assert-True (!(Test-Path -LiteralPath $context.LegacyDllPath) -and !$state.Blocked) 'Legacy installation was not removed'
    }
    Test-Case 'Legacy DLL without a journal is rejected' {
        [IO.Directory]::CreateDirectory($context.RulesetsDir) | Out-Null
        [IO.File]::WriteAllText($context.LegacyDllPath, 'unknown-legacy-dll')
        Assert-Throws { Invoke-OfflineTransition $context Offline $ops }
        Assert-Throws { Invoke-OfflineTransition $context Online $ops }
        Assert-True ((Get-Content -LiteralPath $context.LegacyDllPath -Raw) -eq 'unknown-legacy-dll') 'Unmanaged legacy DLL was altered'
    }
    foreach ($checkpoint in @('LegacyParked', 'LegacyRemoved')) {
        Test-Case "Interruption during legacy migration after $checkpoint" {
            Initialize-LegacyInstallation
            $script:failAt = $checkpoint
            $ops.Checkpoint = { param($name) if ($name -eq $script:failAt) { throw 'Injected migration interruption.' } }
            Assert-Throws { Invoke-OfflineTransition $context Offline $ops }
            Assert-True $state.Blocked 'Interrupted migration unblocked the network'
            $ops.Remove('Checkpoint')
            Invoke-OfflineTransition $context Online $ops
            Assert-True (!(Test-Path -LiteralPath $context.LegacyDllPath) -and !(Test-Path -LiteralPath $context.DllPath) -and !$state.Blocked) 'Migration recovery failed'
        }
    }
    if ($OutputPath) {
        $report = @{ Passed = $passed; Success = $true; Utc = [DateTime]::UtcNow.ToString('o'); CoreSha256 = (Get-FileHash (Join-Path $PSScriptRoot '..\ModosuOffline.Core.psm1')).Hash }
        [IO.File]::WriteAllText([IO.Path]::GetFullPath($OutputPath), ($report | ConvertTo-Json), [Text.UTF8Encoding]::new($false))
    }
    Write-Host "$passed transition tests passed (network operations were simulated)."
} finally {
    $resolved = (Resolve-Path -LiteralPath $testRoot).ProviderPath
    if ($resolved -ne [IO.Path]::GetFullPath($testRoot) -or !(Split-Path $resolved -Leaf).StartsWith('ModosuOffline-tests-')) { throw 'Refusing unsafe temporary-directory cleanup.' }
    Remove-Item -LiteralPath $resolved -Recurse -Force
}
