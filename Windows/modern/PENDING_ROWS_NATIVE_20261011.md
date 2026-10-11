# Completed-import rows: accessible name and readable wrapping · October 11, 2026

The production pending chooser now wraps long device names and completion explanations within its available width. Previously, the default string template clipped the generated row to a single line. `MainWindow.xaml` provides a binding-only TextBlock template and a stretched ListViewItem style; `PendingImportsDialog` applies them with horizontal scrolling disabled. The existing300-height vertical viewport, item strings, selection/index mapping, separate Review import action and safe Close focus remain unchanged. No backend/import/removal policy changes.

## Native evidence

Both sessions use the supported computer-use skill, an engineering build with isolated preferences, one copied generated UHD history record and an absent synthetic source with **no removal plan**. Inspection mode registers read-only storage notifications but starts no runtime pump, update checks or automatic import. The generated no-media pending entry has no batch and offers no sharing action. Normal review executables are never launched.

| Scope | Observed result |
| --- | --- |
| Before correction, PID23508, `ui-pending-accessibility-native-20261011` | Provider reports `Completed imports awaiting review` and the full generated row string. Screenshot inspection shows that the visible row clips to one line. Initial Return closes; pointer selection enables only the separate Review import action; selected Close/fresh selection reset/Escape preserve the entry and recover idle. |
| Corrected PID15212, `ui-pending-wrap-final-native-20261011` | Same generated row now displays its full explanation across two lines at the existing150% scaling/window size. Native provider retains the exact list name and full row text. No new theme/font/OS setting is applied. |
| Corrected chooser behavior | Review import initially disabled and Close highlighted; actual Return closes safely. Reopen retains the row; actual pointer selection enables Review import without opening completion. Actual Close preserves it; another fresh chooser resets selection/Review import to disabled; Escape closes safely. |
| Both sessions | Notifications-off route records pending=1, working=false, confirmationOpen=false and zero shell shown/clicked/closed callbacks. Final settled idle precedes the existing engineering finish gate. Actual production tray Quit callback exits0, native result PASS, no unhandled exception. Shell tray-icon input itself is not established. |

Before aggregate: `C:\Users\rsmit\.codex\EasyEjectBuildTools\Codex020-pending-accessibility-native-20261011\final.json`, SHA **ea9851d4c3463775b3b0e6f32c3d69c64173f164b3073a11148a492f7f77f0bd**. Its initial verifier falsely matched the parent's Quick Share tools button as a completion-sharing action; the assertion was narrowed to the actual Quick Share action label. No extra authority or app defect occurred.

Corrected aggregate: `C:\Users\rsmit\.codex\EasyEjectBuildTools\Codex020-pending-wrap-final-native-20261011\final.json`, SHA **0576e141cb0cff29c2443dbbc7ef0fd2f127b7c9481658c339eb49953a1aaf83**. Retained accessibility observations, loaded-module/process/exit records, notification/keyboard/native-result logs and verification pins bind these claims. Screenshots were inspected directly; their payloads were not saved or processed. This is a concrete one-row/window visual check, not general human screen-reader/monitor/DPI acceptance.

## Builds and preservation

- `Windows/modern/build-ui-tests.ps1 -BuildRoot C:\Users\rsmit\.codex\EasyEjectBuildTools\Codex020-pending-wrap-ui-20261011`: **0 warnings/0 errors**, native completion/pending focus, presenter/compact/footer/Stop/tray draft/editor/busy/idle regressions PASS; automatic scope `ui-35762b4b31544ad6baca712149bcd44f`.
- `Windows/modern/build.ps1 -Test -BuildRoot C:\Users\rsmit\.codex\EasyEjectBuildTools\Codex020-pending-wrap-normal-20261011`: **0 warnings/0 errors**, selected **246/0**, exit0; normal metadata contains KeepConnectedDialog and excludes engineering hosts/inspection/probes. No new full/default or actual-tool count is claimed for this source correction.
- The first wrapping launch wrapper failed **before starting an app** because a regex replacement expanded `$_` to the entire input script. Its script/partial empty evidence scope remain retained. The corrected wrapper uses literal substring boundaries and a fresh final scope; both actual native runs above finish successfully.

Independent verification preserves exact hashes/bytes/write times/counts of **2 live files**, **5 prior isolated profile/configuration files**, **21 prior generated destination files**, and the one copied history in each new scope. Mac baseline384cb767 and absent login registration remain exact. The ba5661f ZIP stays107460193bytes/SHAa49f4e12bb50b3dfbc1bd5e5a75073379bd46c977df7fc7ea65e87c0ecd15e60, but is **superseded pending rebuild** for this production correction. Its earlier exact full/tool replay remains historical, separate evidence.

No real import/export/media/device/delete/format/eject, install/autostart/OS/security/power/display setting, signing/push/publication action occurs. Full physical/genuine-media/official-LUT/travel/OS tray/notification/accessibility/normal-loader/trust/publishing gates remain open. Goal active;0.2 publication still requires Ryan's explicit OK.

Follow-up: [exact40b0a26 normal Release review/ZIP](REVIEW_020_20261011.md) now includes this correction; the rebuild is complete.107460521bytes/650 entries/SHAe64714952dc971df3b93badd45ba631877fd2ce0439dd49adb72fb9df1f5d4ca; every entry independently decompresses/hashes, normal engineering probes absent. The ba5661f ZIP above stays exact but is superseded. The normal app is unlaunched and new Release full/tool replay is not claimed. Post-build preservation recheck passes; report SHAdb211b211a26e6c3f51469d3c6a5d2580de295610b950c0436427d762194ef63.
