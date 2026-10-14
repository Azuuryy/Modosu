# Modosu Offline Windows Package Validation

Controls completed on October 14, 2026, using `manager/Validate-Package.ps1`, Windows PowerShell 5.1, and .NET 10. The user's game installation was not modified.

| Check | Result |
|---|---|
| Restore and Release build | Passed; three pre-existing nullable warnings in replay tests |
| NUnit | 13 passed: custom ruleset identity, mod flags, disconnect/reconnect behavior, local RandomV2/CircleGeneration transforms, object restoration, and SpacingAdjust |
| PowerShell transitions | 29 temporary-directory scenarios passed: repeated transitions, permission denial, disabled firewall, active game, process restart during removal, foreign/renamed DLLs, MOsu migration, interruptions after each mutation, and recovery |
| Visual tests | Seven scenes passed: JSON/beatmap and preset imports; migration of settings, profiles, scores, presets, and replay references; local profiles and scores; RandomV2; CircleGeneration; replay save and playback. Screenshots were generated. |
| Windows Firewall integration | Temporary probe verified initial TCP connectivity, blocking through the real helper, DLL installation/removal, connectivity restoration, legacy MOsu migration, preservation of a sentinel rule, and cleanup of test rules |
| Installed official client | **Not validated**: Modosu was not installed or loaded in the user's game |

The seven visual scenes were `TestSceneCollectionImport`, `TestSceneUserProfileOverlay`, `TestSceneReplayPersistence`, `TestSceneAutoplayRandomV2`, `TestSceneOsuModRandomV2`, `TestSceneLegacyDataMigration`, and `TestSceneModPresetImport`. They used a fake API and temporary data; launcher caches were isolated. The firewall probe connected only to `api.nuget.org:443`, without an osu! account or osu! endpoint.

Interruptions were injected as exceptions between operations. UAC denial and disabled firewall were simulated in these tests; the real firewall was not disabled. Power loss or forced termination of the administrator process during a system call is not covered. Run diagnostics after such an interruption before launching the game.

Replay encoding uses osu!standard only in a temporary copy to create the `.osr` file. The test confirms that the saved score keeps `modosu` and `OnlineID = -1`, and that the replay can be read back.

Local reports are in `.cache/offline-validation`; screenshots are in `screenshots`. `package/validation.json` binds the validation result to the DLL and PowerShell module using SHA-256. A new build invalidates the receipt; rerun validation before creating a new release package.

This report is not osu! team approval and does not guarantee compatibility with the installed client. Keep the first launch protected and require actual load confirmation. A load failure is an incompatibility and must leave the firewall block active.
