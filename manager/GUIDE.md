# Modosu Offline — Quick Start

Requires Windows, PowerShell, and osu!lazer. The manager keeps beatmaps, profiles, scores, and replays in lazer's normal data folder. Offline mode blocks network access for the entire lazer installation, even if you select another ruleset. Closing lazer does not restore network access.

## Start an offline practice session

1. Close lazer. 
2. Run **Launch Modosu Offline.cmd** as a normal user. 
2. Choose **1 — Offline practice** and approve the Windows prompts for firewall changes. The manager launches lazer without elevation.
3. Wait for **Modosu loaded**. If loading is not confirmed within 60 seconds, close lazer and check the manager's diagnostic. The firewall block remains in place.

The manager detects `%LOCALAPPDATA%\osulazer` and `%APPDATA%\osu`. If your paths differ, it prompts for them. You can also pass custom paths:

```powershell
.\ModosuOffline.ps1 -InstallDir 'D:\lazer' -DataDir 'D:\osu-data'
```

Windows Firewall must be enabled for Domain, Private, and Public profiles. The manager blocks all executable files it finds in the lazer installation, including the launcher and updater, and verifies those paths each time. Do not launch lazer directly while practicing offline, disable the firewall, or update the client during an offline session.

## Return to online play

Close lazer, run the manager, and choose **2 — Remove Modosu and return online**. It removes the DLL before removing only its own firewall rules. It refuses the transition if a game process is running, an additional Modosu copy is detected, or DLL ownership cannot be verified. Other rulesets and firewall rules are left alone. You can then start lazer normally and sign in manually.

Both modes use the same data folder. Modosu also clears the saved authentication when it loads; local profiles remain available. Import beatmaps from `.osz` files and collections or presets from JSON. Post-play statistics are local. osu!standard is used only in a temporary replay-encoding copy so `.osr` files can be written; saved scores retain Modosu's custom identity.

## Diagnostics and recovery

Choose **3 — Diagnostics** to check the DLL, firewall protection, owned rule count, and verified executables. If the state is incomplete, keep lazer closed and retry the transition or diagnostic. Declining a UAC prompt never restores network access automatically.

After a power loss or forced termination during an administrator operation, Windows may have applied only part of the changes. The state journal alone does not prove that the firewall is blocking traffic; run diagnostics before launching anything. Protection cannot be guaranteed if an administrator disables Windows Firewall or Windows rejects a rule.

The manager stores its journal and parked DLL files under `%LOCALAPPDATA%\ModosuOfflineManager`, outside `rulesets`. Keep the manager and this folder so you can return online. Do not delete or move the firewall rules manually.

## Migrate from the previous MOsu package

Close lazer, extract this package into a separate folder, run **Launch Modosu Offline.cmd**, and choose **1**. If the previous `%LOCALAPPDATA%\MOsuOfflineManager` journal exists, the new manager reuses its rules, saves the old DLL, and removes it before installing Modosu. Network access stays blocked throughout. Choosing **2** also removes a managed legacy installation and its firewall block.

The old storage location and firewall rule names are retained so the new manager can remove them. Use only the new manager after migration. An old DLL without a matching journal is never removed automatically.

When Modosu loads, it migrates old settings and profiles without overwriting existing Modosu settings. Local scores and presets retain their IDs, replay files, and users. JSON imports continue to accept the legacy `mosu` identifier for migration; historical project attribution is also preserved.

## Package validation

A compile-only package remains locked (`ControlsValidated: false`), and the manager refuses to install it. From the repository root, with .NET 10 installed:

```powershell
dotnet restore Modosu.sln --packages .cache/nuget
powershell -NoProfile -ExecutionPolicy RemoteSigned -File manager/Validate-Package.ps1
powershell -NoProfile -ExecutionPolicy RemoteSigned -File manager/Build-Bundle.ps1
```

Validation runs NUnit, 29 transition scenarios, 7 visual scenes with temporary data, and a TCP probe to `api.nuget.org:443`. It requests UAC approval for temporary firewall rules and checks initial connectivity, blocking, restoration, legacy migration, and preservation of a sentinel rule. No osu! account is used. Review screenshots in `screenshots`; reports are stored in `.cache/offline-validation`.

Package validation does not prove compatibility with every lazer version or approval by the osu! team. The protected first launch must confirm that lazer actually loaded the ruleset. If confirmation fails, close lazer and keep the network block in place.
