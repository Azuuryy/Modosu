param()
$ErrorActionPreference = 'Stop'
$repo = Split-Path $PSScriptRoot
$results = Join-Path $repo '.cache\offline-validation'
[IO.Directory]::CreateDirectory($results) | Out-Null
Push-Location $repo
try {
    & dotnet build Modosu.sln --no-restore -c Release
    if ($LASTEXITCODE -ne 0) { throw 'Build failed.' }
    & (Join-Path $PSScriptRoot 'Build-Package.ps1')
    & dotnet test osu.Game.Rulesets.Modosu.Tests/osu.Game.Rulesets.Modosu.Tests.csproj --no-build --no-restore -c Release --filter 'FullyQualifiedName~OfflinePolicyTest|FullyQualifiedName~OriginalStateStoreTest|FullyQualifiedName~SpacingAdjustTest' --logger 'trx;LogFileName=unit.trx' --results-directory $results
    if ($LASTEXITCODE -ne 0) { throw 'NUnit tests failed.' }
    & (Join-Path $PSScriptRoot 'tests\Test-Transitions.ps1') -OutputPath (Join-Path $results 'transitions.json')
    foreach ($scene in @('TestSceneCollectionImport', 'TestSceneUserProfileOverlay', 'TestSceneReplayPersistence', 'TestSceneAutoplayRandomV2', 'TestSceneOsuModRandomV2', 'TestSceneLegacyDataMigration', 'TestSceneModPresetImport')) {
        $log = Join-Path $results ($scene + '.log')
        & dotnet run --project osu.Game.Rulesets.Modosu.Tests/osu.Game.Rulesets.Modosu.Tests.csproj --no-restore -- --auto --filter $scene *> $log
        if ($LASTEXITCODE -ne 0 -or !(Select-String -LiteralPath $log -SimpleMatch "[ScreenshotTestRunner] Completed: $scene" -Quiet)) { throw "Visual test failed: $scene. See $log" }
        if (!(Test-Path -LiteralPath (Join-Path $repo "screenshots\$scene.png"))) { throw "Screenshot is missing: $scene" }
    }
    & dotnet build manager/tests/FirewallProbe/FirewallProbe.csproj -c Release
    if ($LASTEXITCODE -ne 0) { throw 'Firewall probe build failed.' }
    & (Join-Path $PSScriptRoot 'tests\Run-FirewallValidation.ps1') -OutputPath (Join-Path $results 'firewall.json')
    if ($LASTEXITCODE -ne 0) { throw 'Firewall validation failed.' }
    $firewall = Get-Content -LiteralPath (Join-Path $results 'firewall.json') -Raw | ConvertFrom-Json
    if ($firewall.Validated -ne $true) { throw 'Firewall validation report is inconclusive.' }
    $package = Join-Path $PSScriptRoot 'package'
    $receipt = Get-Content -LiteralPath (Join-Path $package 'validation.json') -Raw | ConvertFrom-Json
    $receipt.ControlsValidated = $true
    $receipt.Note = 'NUnit, interrupted transitions, local scenes, and Windows probe passed. Official client compatibility is untested; protected launch confirmation is required.'
    $receipt | Add-Member -NotePropertyName ValidatedUtc -NotePropertyValue ([DateTime]::UtcNow.ToString('o')) -Force
    $receipt | Add-Member -NotePropertyName CoreSha256 -NotePropertyValue ((Get-FileHash (Join-Path $PSScriptRoot 'ModosuOffline.Core.psm1')).Hash) -Force
    [IO.File]::WriteAllText((Join-Path $package 'validation.json'), ($receipt | ConvertTo-Json), [Text.UTF8Encoding]::new($false))
    Write-Host 'Automated checks passed. Review screenshots. Nothing was installed in the game.'
} finally { Pop-Location }
