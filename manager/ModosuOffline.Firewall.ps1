param([Parameter(Mandatory = $true)] [string] $RequestFile)
$ErrorActionPreference = 'Stop'
Import-Module (Join-Path $PSScriptRoot 'ModosuOffline.Core.psm1') -Force
$request = Get-Content -LiteralPath $RequestFile -Raw | ConvertFrom-Json
$context = New-OfflineContext $request.InstallDir $request.DataDir $request.PackagePath $request.VaultDir
$resultFile = $RequestFile + '.result'
$mutex = [Threading.Mutex]::new($false, 'Local\' + $context.RuleGroup + '-Operation')
$acquired = $false
try {
    try { $acquired = $mutex.WaitOne(0) } catch [Threading.AbandonedMutexException] { $acquired = $true }
    if (!$acquired) { throw 'Another administrator operation is in progress. Wait for it to finish, then run diagnostics again.' }
    switch ($request.Action) {
        'Block' { Enable-NetworkBlock $context }
        'Verify' { Assert-NetworkBlocked $context }
        'Unblock' { Disable-NetworkBlock $context }
        'Offline' {
            Assert-PackageValidated $context
            Invoke-OfflineTransition $context Offline @{
                AssertClosed = { param($c) Assert-LazerClosed $c }
                Block = { param($c) Enable-NetworkBlock $c }
                Verify = { param($c) Assert-NetworkBlocked $c }
                Unblock = { param($c) Disable-NetworkBlock $c }
            }
        }
        'Online' {
            Invoke-OfflineTransition $context Online @{
                AssertClosed = { param($c) Assert-LazerClosed $c }
                Block = { param($c) Enable-NetworkBlock $c }
                Verify = { param($c) Assert-NetworkBlocked $c }
                Unblock = { param($c) Disable-NetworkBlock $c }
            }
        }
        'Status' {
            $owned = @(Get-OwnedFirewallRules $context)
            $blocked = $false
            try { Assert-NetworkBlocked $context; $blocked = $true } catch { }
            $result = @{ Success = $true; Blocked = $blocked; RuleCount = $owned.Count }
        }
        default { throw 'Unknown firewall action.' }
    }
    if (!$result) { $result = @{ Success = $true } }
} catch { $result = @{ Success = $false; Error = $_.Exception.Message } }
finally { if ($acquired) { $mutex.ReleaseMutex() }; $mutex.Dispose() }
[IO.File]::WriteAllText($resultFile, ($result | ConvertTo-Json), [Text.UTF8Encoding]::new($false))
if (!$result.Success) { exit 1 }
