param(
    [ValidateSet('Menu', 'Offline', 'Online', 'Status')] [string] $Action = 'Menu',
    [string] $InstallDir = (Join-Path $env:LOCALAPPDATA 'osulazer'),
    [string] $DataDir = (Join-Path $env:APPDATA 'osu'),
    [string] $PackagePath = (Join-Path $PSScriptRoot 'package\osu.Game.Rulesets.Modosu.dll'),
    [string] $VaultDir = (Join-Path $env:LOCALAPPDATA 'ModosuOfflineManager'),
    [switch] $NoLaunch
)
$ErrorActionPreference = 'Stop'
Import-Module (Join-Path $PSScriptRoot 'ModosuOffline.Core.psm1') -Force

function Invoke-FirewallHelper($Context, [string] $Operation) {
    [IO.Directory]::CreateDirectory($Context.VaultDir) | Out-Null
    $requestFile = Join-Path $Context.VaultDir ([Guid]::NewGuid().ToString('N') + '.request.json')
    $request = @{ Action = $Operation; InstallDir = $Context.InstallDir; DataDir = $Context.DataDir; PackagePath = $Context.PackagePath; VaultDir = $Context.VaultDir }
    [IO.File]::WriteAllText($requestFile, ($request | ConvertTo-Json), [Text.UTF8Encoding]::new($false))
    $helper = Join-Path $PSScriptRoot 'ModosuOffline.Firewall.ps1'
    # Single-quoted literals are escaped before encoding; no string-built shell paths.
    $command = "& '" + $helper.Replace("'", "''") + "' -RequestFile '" + $requestFile.Replace("'", "''") + "'"
    $encoded = [Convert]::ToBase64String([Text.Encoding]::Unicode.GetBytes($command))
    try {
        $powershell = Join-Path $env:SystemRoot 'System32\WindowsPowerShell\v1.0\powershell.exe'
        $helperProcess = Start-Process -FilePath $powershell -ArgumentList @('-NoProfile', '-ExecutionPolicy', 'RemoteSigned', '-EncodedCommand', $encoded) -Verb RunAs -WindowStyle Hidden -Wait -PassThru
        if (!(Test-Path -LiteralPath ($requestFile + '.result'))) { throw 'Administrator result is missing. No transition was validated.' }
        $result = Get-Content -LiteralPath ($requestFile + '.result') -Raw | ConvertFrom-Json
        if ($helperProcess.ExitCode -ne 0 -or !$result.Success) { throw $result.Error }
        return $result
    } finally {
        Remove-Item -LiteralPath $requestFile, ($requestFile + '.result') -Force -ErrorAction SilentlyContinue
    }
}

function Start-OfflineLazer($Context) {
    $identity = [Security.Principal.WindowsIdentity]::GetCurrent()
    $principal = [Security.Principal.WindowsPrincipal]::new($identity)
    if ($principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
        throw 'Run the manager as a standard user. The game must not be launched with administrator privileges.'
    }
    Assert-LazerClosed $Context
    Invoke-FirewallHelper $Context 'Verify' | Out-Null
    Write-Host 'First launch: compatibility is confirmed only if the client actually loads the DLL. If loading fails, network access remains blocked.'
    $session = [Guid]::NewGuid().ToString('N')
    $ready = Join-Path $Context.VaultDir ($session + '.ready')
    $oldReady = $env:MODOSU_OFFLINE_READY_FILE
    $oldSession = $env:MODOSU_OFFLINE_SESSION
    try {
        $env:MODOSU_OFFLINE_READY_FILE = $ready
        $env:MODOSU_OFFLINE_SESSION = $session
        $game = Start-Process -FilePath $Context.GamePath -WorkingDirectory (Split-Path $Context.GamePath) -PassThru
    } finally {
        $env:MODOSU_OFFLINE_READY_FILE = $oldReady
        $env:MODOSU_OFFLINE_SESSION = $oldSession
    }
    $deadline = [DateTime]::UtcNow.AddSeconds(60)
    while ([DateTime]::UtcNow -lt $deadline) {
        if (Test-Path -LiteralPath $ready) {
            $message = @(Get-Content -LiteralPath $ready)
            if ($message.Count -eq 2 -and $message[0] -eq $session -and $message[1] -eq $game.Id.ToString()) {
                Write-Host 'Modosu loaded. Offline practice is active. Return online only through the manager.' -ForegroundColor Green
                Remove-Item -LiteralPath $ready -Force
                return
            }
        }
        if ($game.HasExited) { break }
        Start-Sleep -Milliseconds 250
    }
    throw 'Modosu load was not confirmed: the client version may be incompatible, the ruleset may be blocked, or startup may be incomplete. Close lazer. Network access remains blocked; no client checks will be bypassed.'
}

try {
    if ($Action -eq 'Menu') {
        Write-Host 'Modosu Offline — mode manager'
        Write-Host '1. Enable offline practice and launch lazer'
        Write-Host '2. Remove Modosu and return online'
        Write-Host '3. Check mode and protections'
        Write-Host '0. Exit'
        switch (Read-Host 'Choose an option') { '1' { $Action = 'Offline' }; '2' { $Action = 'Online' }; '3' { $Action = 'Status' }; '0' { return }; default { throw 'Invalid option.' } }
        if (!(Test-Path -LiteralPath (Join-Path $InstallDir 'current\osu!.exe'))) { $InstallDir = Read-Host 'lazer installation folder (containing current)' }
        if (!(Test-Path -LiteralPath $DataDir)) { $DataDir = Read-Host 'lazer data folder (open the osu! folder from settings)' }
    }
    # Existing installations keep their journal and rule ownership through the rename.
    $legacyVault = Join-Path $env:LOCALAPPDATA 'MOsuOfflineManager'
    if ($VaultDir -eq (Join-Path $env:LOCALAPPDATA 'ModosuOfflineManager') -and
        !(Test-Path -LiteralPath (Join-Path $VaultDir 'state.json')) -and
        (Test-Path -LiteralPath (Join-Path $legacyVault 'state.json'))) { $VaultDir = $legacyVault }
    $context = New-OfflineContext $InstallDir $DataDir $PackagePath $VaultDir
    $mutex = [Threading.Mutex]::new($false, 'Local\' + $context.RuleGroup + '-UI')
    if (!$mutex.WaitOne(0)) { throw 'Another mode transition is already in progress.' }
    try {
        if ($Action -eq 'Status') {
            $status = Invoke-FirewallHelper $context 'Status'
            $legacyInstalled = Test-Path -LiteralPath $context.LegacyDllPath
            $installed = (Test-Path -LiteralPath $context.DllPath) -or $legacyInstalled
            Assert-NoAdditionalModosuCopies $context
            if ($installed) { Assert-OwnedDll $context }
            $mode = if ($installed -and $status.Blocked) { 'Protected offline' } elseif (!$installed -and $status.RuleCount -eq 0) { 'Modosu removed / online mode' } else { 'Incomplete state: do not launch the game' }
            Write-Host "Mode: $mode"
            Write-Host "DLL installed: $installed; full network block: $($status.Blocked); managed rules: $($status.RuleCount)"
            if ($legacyInstalled) { Write-Host 'Legacy MOsu DLL detected; it will be backed up and removed during the transition.' }
            Write-Host "Journal and storage: $($context.VaultDir)"
            foreach ($exe in $context.Executables) { Write-Host "Verified executable: $exe" }
            return
        }
        if ($Action -eq 'Offline') {
            Assert-PackageValidated $context
        }
        Invoke-FirewallHelper $context $Action | Out-Null
        if ($Action -eq 'Offline' -and !$NoLaunch) { Start-OfflineLazer $context }
        elseif ($Action -eq 'Online') { Write-Host 'Modosu and the manager-owned firewall rules were removed. You can launch lazer normally and sign in manually.' }
    } finally { $mutex.ReleaseMutex(); $mutex.Dispose() }
} catch {
    Write-Error $_.Exception.Message
    exit 1
}
