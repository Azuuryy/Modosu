# Windows package

`ModosuOffline-Windows.zip` contains the ruleset DLL, the offline manager, its validation receipt, and a quick-start guide. Extract the package outside lazer's `rulesets` folder and follow `GUIDE.md`. Never install the DLL manually.

Automated package controls and the firewall probe passed for this build. Loading by the installed official client remains unverified. See the [validation report](../manager/VALIDATION.md).

To rebuild the archive, run `manager/Validate-Package.ps1` followed by `manager/Build-Bundle.ps1`. The generated archive is written to `artifacts`.
