# Native accessible control names · October 11, 2026

Storage/profile list rows previously exposed `EasyEject.Modern.StorageCard` / `ProfileCard` as their accessible names. The real update-page ComboBox had no accessible name. Production rows now return their displayed title, detail and state from `ToString()`, and key lists/selectors/diagnostic text have explicit accessible names. Visual text, layout, selection objects and action authority are unchanged.

Native PID **30912** uses `C:\Users\rsmit\.codex\EasyEjectBuildTools\Codex020-accessibility-ui-final-20261011\bin\EasyEject.Modern\Debug\net10.0-windows10.0.26100.0\win-x64\EasyEject.Modern.exe`, fixture `C:\Users\rsmit\.codex\EasyEjectFixtures\ui-accessibility-native-20261011`. Only prior generated settings/debug log and generated UHD profile/history/tool references are copied. The source remains absent. No genuine media is used, no export is requested, no device action occurs.

| Real native provider observation | Result |
| --- | --- |
| Connected storage volumes | List has this name. All five rows expose visible title/detail/state, including `OS · Windows system storage: protected · C:\ · Protected`, rather than a class name. |
| Saved device profiles | List has this name; generated row exposes device name, `Custom · Manual` and destination. Actual selection still fills exact saved details and enables Edit. No edit/save/import/forget action is invoked. |
| Displayed debug log | Read-only native edit exposes name and retained416-character log value. Close succeeds; no Copy/Clear/Refresh invoked. |
| Updates and installation section | Real ordinary dialog's ComboBox exposes name; production channel remains unconfigured and remote actions disabled. Close succeeds. |
| Verified import batches | History list exposes name and exact two-file generated batch. Selecting it enables existing manual actions. |
| Sharing copy size for this export | Actual Quick Share prompt exposes named ComboBox, saved4K/LUT-off/Cancel choices. Actual Cancel reports no export started, retains originals and recovers idle controls. |
| Final actual tray Quit callback | Exit0; no unhandled exception. Supported shell tray-icon input itself remains unproved. |

The pending-import list also receives `Completed imports awaiting review` in its production dialog factory. Existing automatic native pending-focus tests exercise that factory after the change; this milestone does not claim a separate native accessibility-provider observation of its name. No additional test-only inspection hooks were introduced.

Builds outside OneDrive:

```powershell
Windows/modern/build-ui-tests.ps1 -BuildRoot C:\Users\rsmit\.codex\EasyEjectBuildTools\Codex020-accessibility-ui-final-20261011
Windows/modern/build.ps1 -Test -BuildRoot C:\Users\rsmit\.codex\EasyEjectBuildTools\Codex020-accessibility-normal-final-20261011
```

Both final logs: **0 warnings/0 errors**, automatic native regression PASS (`ui-e1f997a1531e4bbb99666ee64ce76441`), normal selected **246/0**. Earlier accessibility builds also pass but are superseded because the pending-list label was added during their run. No new full-suite/tool count or human screen-reader acceptance claim. Normal PE retains production view and excludes engineering host/probes.

Evidence root `C:\Users\rsmit\.codex\EasyEjectBuildTools\Codex020-accessibility-native-20261011`: launcher/finalizer, actual observations, loaded Modern/Runtime/coreclr, process/exit and pins. Aggregate `final.json` SHA **b38710eae522a99e2c08f9193b1893566da20b9dcf58564026832ced96855122**. Finalizer initially miscompared a JSON timestamp parsed as DateTime against a string; independent readback showed unchanged values. Corrected string-preserving parsing verifies exact hash/bytes/write-time/count for **2 live records, 5 isolated records and all21 generated destination files**. No preservation failure is inferred from that verifier issue.

Mac baseline384cb767 and absent login registration remain exact. No OS setting/tool/LUT installation, real media/device/deletion, push or publication. The older0040e58 unsigned ZIP is preserved (107459831 bytes, SHAe941e2c49a9bb88d65d6d24234e6cd2fbcd711350ef2c7cf43105a7e56198bb4), but **superseded pending rebuild** to include these production accessibility changes. Full human accessibility/display/physical/genuine-media/official-LUT/travel/trust/normal-loader/signing/publishing gates remain open; goal active.
