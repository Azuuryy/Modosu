# Modosu Offline

Modosu is an osu!lazer practice ruleset for local play. This Windows fork includes a manager that blocks lazer's network access before installing the ruleset DLL and removes the DLL before restoring network access.

## Use

Extract the Windows package outside lazer's `rulesets` folder and follow [`manager/GUIDE.md`](manager/GUIDE.md). Do not install the DLL manually. The manager requires Windows Firewall and administrator approval for firewall changes; lazer itself runs as your normal user.

The project keeps local practice mods, profiles, scores, statistics, and replays. The network block applies to the whole lazer client, including when another ruleset is selected. Online mode is restored manually through the manager.

## Build

Requirements: Windows, .NET 10, and the `osu` submodule.

```powershell
git clone --recurse-submodules https://github.com/Azuuryy/mosu-offline.git
cd mosu-offline
dotnet restore Modosu.sln --packages .cache/nuget
powershell -NoProfile -ExecutionPolicy RemoteSigned -File manager/Validate-Package.ps1
powershell -NoProfile -ExecutionPolicy RemoteSigned -File manager/Build-Bundle.ps1
```

The ZIP is written to `artifacts/ModosuOffline-Windows.zip`. Validation does not install anything into lazer. A successful package validation does not confirm compatibility with every lazer version or imply osu! team approval; the protected first launch must confirm that lazer actually loaded the ruleset.

## Project history

This project derives from [p-720/mosu](https://github.com/p-720/mosu). Historical documentation and attribution are preserved in [`README.upstream.md`](README.upstream.md).
