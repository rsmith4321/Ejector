# Native Settings and unconfigured update review · October 11, 2026

The isolated engineering app now has foreground evidence for the unconfigured update controls, installation-guide navigation, exact-folder buttons and saved automatic-check preference across true Quit/restart. A post-Explorer Escape observation remains unresolved; it is not a complete keyboard/installation acceptance pass.

## Exact scope

Fresh isolated scope: `C:\Users\rsmit\.codex\EasyEjectFixtures\ui-settings-updates-native-20261011`. Evidence packet: `C:\Users\rsmit\.codex\EasyEjectBuildTools\Codex020-native-settings-updates-20261011`.

Unchanged engineering executable: `C:\Users\rsmit\.codex\EasyEjectBuildTools\Codex020-ui-layout-20261011\bin\EasyEject.Modern\Debug\net10.0-windows10.0.26100.0\win-x64\EasyEject.Modern.exe`. Fresh `review` PID **4988** and exact marked `review-restart` PID **25964** both exit normally through the real tray callback. Loaded UI/backend hashes remain **46F2E9E5C1A522F8100F481CCFB6956E34D82882B8A315D3929FF1A689DF387B** / **6F38BE334769E31A918D23CACB1E8E5CDA04D40AEE087FC6AB27C7ED9181359F**.

Relevant current contracts were inspected before input: `MainWindow.Releases.cs`, `ReleaseView.cs`, `WindowsUpdateCoordinator.cs`, `AppSettingsView.xaml` / `.xaml.cs`, `AppSettings.cs`, `AppSettingsEdit.cs`, `StartupEdit.cs` and `StartupRegistration.cs`. `ModernProductionChannel` remains null. `StartupEdit.SaveReviewed(null)` inspects registration without applying a change. This check configures no publisher or signing policy and exercises no configured release worker.

## Native results

- **Release review:** displays the missing approved channel/signing publisher explanation. Check release metadata, Verify selected ZIP, Verify extracted folder and Stop review are disabled.
- **Update history:** Review history backup, Restore reviewed history and Cancel backup review are disabled. No backup picker or restore is attempted.
- **Installation guide:** shows `0.2.0-dev`, the exact engineering app folder and the separate isolated Profiles folder. Instructions describe manual extraction, true tray Quit, separate settings backup, retained previous version, rollback and explicit login retargeting. The fixed Close footer stays visible while the long guide scrolls.
- **Exact-folder buttons:** Open settings folder opens the intended `ui-settings-updates-native-20261011\EasyEject\Profiles` in Explorer, initially empty. Open app folder opens the exact `Codex020-ui-layout-20261011\bin\EasyEject.Modern\Debug\net10.0-windows10.0.26100.0\win-x64` folder. Supported accessibility readback confirms the breadcrumbs. Only the two newly opened Explorer windows are closed; the pre-existing Downloads window is retained. No file is opened, moved or deleted there.
- **Keyboard qualification:** after Explorer navigation/closure and supported activation, Escape repeatedly leaves the guide open; explicit Close succeeds. Reopening the guide directly, clicking its selectable instructions and pressing Escape closes it. Thus the initial interpretation that selectable text universally swallows Escape is contradicted by direct reproduction. Focus restoration/input delivery after Explorer remains unresolved and needs a scoped diagnostic before a production fix or full keyboard pass. The first app-folder click also reports unavailable input geometry; one fresh selection/activation/screenshot recovery succeeds. Neither observation is silently counted as passing.
- **Settings Save/restart:** fresh controls show login unregistered/off, notifications/warn-other on, cleanup/shortcut/debug off, shortcut E and automatic release checks on. Only the automatic-check preference is turned off and saved. Status reports no import/cleanup/removal and login still unregistered. True idle tray callback Quit exits PID4988. A separate restart PID25964 reopens an unchecked automatic-check control. An opposite draft is cancelled; the saved file retains exact hash, bytes and write time. Idle tray Quit exits PID25964.

Saved `app-settings.json` is **169 bytes**, SHA-256 **6F1508500C79EBBC7D051B16EEA33C465EC30450FF474EB2F86414DF309596E8**, write time **2026-10-11T16:25:31.6990775Z**. Raw JSON contains only `AutomaticUpdateChecks=false` with six untouched fields null. Effective getters retain the initial notification/warning/shortcut/cleanup/debug defaults. The first independent assertion wrongly required effective defaults as explicit raw values and failed; it is corrected against `AppSettingsEdit.Copy`'s nullable-preservation contract. `after-save.json` retains that correction. No application failure is inferred from the assertion mistake.

Before/after independent readback proves the owned HKCU Run value `EasyEject.Windows` remains **absent**, and both pre-existing live Profiles files retain their exact hashes, bytes and write times with no new file. Neither Windows settings nor login registration is changed. No download, installation, autostart, device action, source write, signing account, power/display/security change or publication occurs.

## Evidence packet

| File | SHA-256 |
| --- | --- |
| `before.json` | `900686827A7E9A2DD74793E1B5B13A34B4866F9391E19FB51BB294960CB06297` |
| `after-save.json` | `5A8A6EE49BD3B6D9924AC5E0928C5CCF8C341CF7A028CD77BB1C2FAD5D697F28` |
| `module-pins.json` | `A8D5CCB603816DAB7132CB1D0051CB51C1F5BEDE623D76807D62CD1D832237A0` |
| `final.json` | `C8B433D64D05D06694C7D84CE42FEC5E505255EC83DB65F6134E8A04AE3883B9` |

No production code/payload or default-suite count changes. Previous **1454/0** each runtime and **246/0** selected modern remain separate software evidence. A later [passive keyboard diagnostic](KEYBOARD_PROBE_20261011.md) passes both Explorer-return paths without a production change; the failure recorded here remains unexplained. Configured signed update/backup/restore/worker delivery, broad keyboard acceptance and all OS tray/notification/display/physical/media/LUT/trust gates remain open. The full goal remains active; 0.2 publishing OK is still pending.
