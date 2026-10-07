# Windows foreground acceptance

This records actual foreground observations separately from hidden renders and fixture tests. The full scope remains [PARITY.md](PARITY.md), against Mac baseline `384cb767ffbbb87ec5fe67f45d0b4d0ce324ad7c`. The Windows port is not complete.

## October 7 observation

Checkout was clean at `94a0047`; latest source is `e1598951d657749b310080190a9c1dcfe01f8bfb`. The existing build was launched without rebuilding or changing source. No source/test file was newer than the executable. The retained full-test log still ends **1201 passed; 0 failed**, `manual-import-review-worker-final-test.log`; that suite was not rerun for this observation.

| Artifact | SHA-256 |
| --- | --- |
| `EasyEject.Windows.exe` | `c0026e94df8bedd9f864d0abdb962de6b50d3911d43c66825d766f9f7635a9eb` |
| `EasyEject.Tests.exe` | `c85697f64895ef6e47cbf3de1bb41e99cb5f2e49c25adb9b9d13bd773d71f2f3` |

Read-only native inventory returned one internal disk/five volumes, with OS C: protected and MYASUS, RESTORE, SYSTEM and an unnamed internal volume. No disposable external storage was identified. The inventory command lacks connection notifications and blocks operations; its result does not prove the running application's notification delivery or hardware compatibility.

The pre-existing executable at `%LOCALAPPDATA%\EasyEject\PrototypeBuild\EasyEject.Windows.exe` was launched through the computer-use skill. Its launch helper initially reported no targetable window; a fresh window enumeration then returned the running app under the packaged host's canonical `LocalCache\Local\EasyEject\PrototypeBuild` path. The application did not need another launch. Native process inspection found PID 16400, started at 06:50:41 local time. A unique returned Easy Eject window was selected and brought forward; no other application's controls were operated.

| Actual observation | Evidence scope |
| --- | --- |
| Ordinary application launch | `Easy Eject · Windows prototype` appeared and remained running. No Easy Eject approval dialog was observed in this launch. This is under Ryan's current Windows policy, not proof of trusted signing or compatibility with default security policy. |
| Main device window | Foreground screenshot and accessibility tree showed all five internal volumes, separate Internal storage/Windows storage groups, protected OS C:, and the inventory/status text. Safe eject, Clean & eject, Eject cards, Verified import, Resume and Import profile were disabled with no eligible selection. |
| Settings entry | Clicking the actual Settings button opened `Easy Eject settings` and disabled the owner window while the modal was open. |
| Initial Settings presentation | Foreground screenshots showed launch-at-login unchecked and not registered, notifications checked, card cleanup unchecked, other-storage warnings checked, and visible Save settings/Cancel buttons outside the scrolling content. |
| Remaining Settings contents | The modal accessibility tree contained debug, shortcut/letter, Quick Share tools and automatic-release controls plus their explanatory text. Their presence in the tree does not prove scrolling, keyboard/dropdown operation or visibility at other sizes. |

No settings were saved, startup entry installed, media tool run, import started, cleanup performed or device ejected. Foreground input stopped when the access task requested focus for Ryan's Mac password interaction. Settings was left open; no Cancel, further scrolling, keyboard operation or app shutdown/restart was performed in this observation. This focus sequencing is not a paused goal or a verified background test job.

## Remaining foreground checks

Use fresh returned window/state objects before each input. Do not automate authentication/security prompts or compete with another task's password/installer interaction. Check the actual Mac `SettingsView.swift`, `AppLifecycle.swift`, completion and manual-sharing contracts before treating an outcome as parity evidence.

| Requirement | Next evidence | Current status |
| --- | --- | --- |
| Settings accessibility and cancellation | Scroll all content; inspect the letter dropdown, keyboard focus and Escape/Cancel; confirm temporary edits do not save or register a shortcut/login entry. | Not run |
| Defaults and saved profiles | Exercise defaults/custom selection, broadening confirmations, cancel, migration and stale edits through actual dialogs with owned fixtures. | Not run |
| History and completion | Inspect exact retained batches, explicit verification, Keep Connected default, return from manual sharing and loss of eject authority after restart. | Not run |
| Tray and lifecycle | Close hides; tray reopens; Quit exits; fresh launch and active/modal quit protection are observed. | Not run |
| Notifications and hotkey | Actual delivery/suppression, fresh review on balloon click, selected-key registration/collision and no repeated/stale removal. Do not install login registration during development. | Not run |
| Scaling and controls | Minimum-size scrolling, fixed action visibility, actual dropdowns/pickers, larger layout and applicable monitor/DPI behavior. | Initial main/Settings render only |
| Quick Share | Manual opt-in/size/LUT/tool choice, actual validation, cancellation/retry and unchanged exact imported originals/history across restart. | [Actual-tool rehearsal](QUICK_SHARE_ACCEPTANCE.md) prepared, not run; tool/LUT paths still needed |
| Hardware and optional removal | Explicitly identified disposable camera/card/reader/destination and authorized operations; physical import, long transfer, reconnect, grouping/veto/eject and any separately permitted cleanup/removal/recovery. | Identification/authorization still needed |
| Direct distribution | Trusted publisher/artifact, manual installation/update/rollback and current-policy proof after runtime/hardware acceptance. | Signing/runtime proof still needed; no public release authorized |

Do not turn these observations into a whole foreground, hardware, codec/LUT or full-port pass. Record further actions and their actual outcomes as they occur.
