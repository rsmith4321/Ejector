# App regression contract audit · October 6, 2026

Baseline: Mac `384cb767ffbbb87ec5fe67f45d0b4d0ce324ad7c`. This extends the separate [media mapping](REGRESSION_MAP.md). It inventories app harnesses and starts an assertion-based mapping of defaults, completion, notifications and grouped/bulk actions. It does not establish full-project parity or native foreground/hardware acceptance. Read the named assertions and implementation before relying on a row; related test names alone are insufficient.

**Fixture** means applicable policy/file/control assertions exist on Windows. **Partial** means related evidence is narrower than the Mac integration/property. **Platform** identifies an Apple-specific implementation whose applicable user behavior must still be preserved. **Pending** means the named harness has not received its complete assertion mapping here. Actual device and foreground integrations remain pending regardless of a Fixture row.

## Complete harness inventory

| Mac harness | Mapping/evidence status | Remaining evidence |
| --- | --- | --- |
| `ImportEngineTests.swift` | 44 named call sites mapped in REGRESSION_MAP: 42 applicable fixture properties and two Store-only cases | Real camera/filesystem proof; long-lived owner audit continues |
| `ImportDefaultsTests.swift` | Logical assertion groups mapped below; no individually named test helper in this harness | Combined exact roundtrip/malformed/removal inheritance assertions still need final line-by-line coverage audit |
| `ImportEjectPromptTests.swift` | Policy/hidden-control mapping below | Real foreground Enter/Escape/click response and grouped-camera prompt |
| `ImportCompletionNotificationTests.swift` | Process registry and content mapping below; Apple permission/category matrix is Platform | Real Windows notification delivery/suppression and pending-choice behavior |
| `ImportNotificationIntegrationTests.swift` | Import/default/removal/choice fixtures are related; actual UI/device sequence is Partial | Identified disposable hardware and foreground notification/default/import/reuse/removal/explicit-eject sequence; no real removal run here |
| `BulkEjectPolicyTests.swift` | Nine planning properties mapped below; empty planning is source evidence only | Explicit empty-plan/service fixture; physical grouping and foreground review |
| `GroupedUnmountTests.swift` | Four named sequential/result properties mapped below to Windows whole-device groups/serial confirmed targets | Native Windows group removal differs from per-volume Apple flush/unmount; actual multi-volume/LUN/veto proof |
| `GroupedUnmountIntegrationTests.swift` | Partial: fake verified disappearance and read-only topology | Two authorized mounted volumes/LUNs, identity checks and actual final absence before success |
| `ManualQuickShareIntegrationTests.swift` | Pending complete assertion inventory; manual workflow, profile/tool/batch/cancellation fixtures exist | Actual supplied tools/LUT, foreground flow and exact named semantic mapping |
| `EasyShareTests.swift` | Pending complete assertion inventory; recipe/runner/transaction and synthetic helper fixtures exist | Actual synthetic video probe/encode/full decode/framing/audio/LUT/reuse/retry/cancellation, then representative camera proof |
| `UpdateSafetyTests.swift` | Busy/dialog/cadence/revocable scope/manual source review evidence in REGRESSION_MAP; Sparkle relaunch is Platform under manual Windows installation | Configured real endpoint/foreground owner/modal/timer and trusted artifact/manual install/rollback acceptance |
| `StoreEjectPolicyTests.swift` | Platform: Store sandbox/bookmarks are outside Windows direct download | Preserve applicable plain-eject behavior and Windows native fresh identity/safety gates |

## Defaults logical assertion groups

| Exact Mac property | Inspected Windows evidence | Status |
| --- | --- | --- |
| New profiles: videos/previews/clean on; automatic import/eject prompt/removal/recovery off | `Tests.cs`: shared defaults preserve custom choices; ask-to-eject defaults start off. `AutomaticImportTests.cs`: factory/new/legacy off. `RemovalTests.cs`: removal starts off. `TrashRecoveryTests.cs`: recovery defaults and explicit owner | Fixture |
| Saved preferences seed new cards; older seeds stay unchanged; resolution follows current shared choices without mutating seeds | `Tests.cs`: shared defaults preserve custom choices and freeze in-flight selection; defaults persistence does not overwrite profiles. `AutomaticImportTests.cs`: inherited/custom automatic switches. `ImportDefaults.NewProfile`/`ImportProfile.Resolve` inspected | Fixture; combined exact Mac assertion sequence still requires final audit |
| Switching to custom copies current defaults; later defaults edits cannot change custom or running options | Same shared-defaults case; `Tests.cs`: editing defaults during a copy affects only next import. Automatic/default/removal/recovery snapshots inspected | Fixture |
| Switching back to defaults and resolving malformed inherited destructive seed flags uses current defaults | `ImportProfile.SetCustom`/`Resolve` inspected; shared-defaults, removal-defaults and recovery-defaults tests | Partial: full malformed combined DeleteOriginals+RecoverTrash+automatic sequence not explicitly mapped |
| Custom/default inheritance flags and saved seed values survive roundtrip; legacy absence remains custom and does not enable actions | `Tests.cs`: legacy profiles remain custom all-media after settings roundtrip; ask-to-eject migration. `AutomaticImportTests.cs`: saved automatic/destination identity. Removal/recovery profile roundtrips | Fixture; exact full combined legacy equality is not established by separate cases |
| Sharing choices remain independent of import-default resolution and survive profile roundtrip | `QuickShareDialogTests.cs`: new/truly legacy off; per-device connected/offline persistence without shared defaults; cancelled edits; fresh manual prompts isolated | Fixture |
| Shared removal/recovery reaches new and existing default-following cards, leaves custom/legacy cards isolated, remains frozen in flight | `RemovalTests.cs`: removal defaults inheritance freezes options. `TrashRecoveryTests.cs`: recovery defaults/owner/frozen inheritance; shared/custom broadening checks | Partial: exact simultaneous combinations and switch-to/from-custom sequence need explicit closure |
| Turning Videos only off resets both destructive defaults, including absent factory Videos only; already running selection stays frozen | `RemovalTests.cs`: factory-video broadening reset and acknowledged draft. `TrashRecoveryTests.cs`: both removal/recovery cleared by custom/shared broadening with frozen recovery. `ImportDefaults.SetVideosOnly` inspected | Fixture |

## Completion, notification and prompt properties

| Exact Mac property | Inspected Windows evidence | Status |
| --- | --- | --- |
| Same connected volume/root/physical disk/generation accepted; replacement, missing/unmounted root, reconnect and unknown token refused | `Tests.cs`: completion identity matrix; changed source path/topology; immutable removal plan. `CompletionConnectionTests.cs`: reconnect, missing source, unreadable root, mount/serial/hidden sibling, tracking loss | Fixture |
| New import supersedes old source action; different devices await independent choices; disconnect invalidates only its source; one-use consume; restart cannot recover eject authority | `Tests.cs`: completion registry supersedes source independently and restart; explicit consume once. `CompletionConnectionTests.cs`: selective source/sibling expiry and reconciliation. Persisted history carries no connection permission | Fixture |
| Original physical identity absent cannot authorize eject | `ImportCompletionOrigin` refuses missing stable source identity or retains a blocked plan; `Tests.cs`: completion with no removable node keeps folder available without eject authority | Fixture with a stricter missing-identity constructor refusal |
| Apple authorized/enabled/banner-or-alert notification action route, registered category/actions, no authority in untrusted payload | Windows uses NotifyIcon balloons opening the app, with persistent manual choices rather than native eject payloads. `Tests.cs`: notification route remains manual; `Program.cs` balloon callback reveals app only | Platform + Partial native acceptance: Apple UserNotifications matrix is not a Windows permission/delivery test |
| Media/no-match outcome titles/content; whole removal group explained; photos/skipped previews can remain | `Tests.cs`: media/no-match completion defaults and notification content; `ImportCompletionDialog` shows all affected sources and skipped-file caveat | Fixture |
| Keep Connected is Enter/Escape/default; eject requires explicit second action; grouped source names explained | `Tests.cs`: completion Keep/default/expiry. `BulkTests.cs`: immutable grouped names/Cancel default. Native hidden renders inspected | Fixture controls; Partial real foreground runModal/click equivalent |
| Actual importer resolves shared video/layout/eject flags, ignores inherited destructive seed fields, retains photos and originals; repeated verified import produces fresh choice | Existing defaults/copy/reuse/completion fixtures cover components | Partial exact end-to-end integration sequence, including foreground notification-off/on and actual identified disposable device |
| Dismissal/legacy notification cannot eject; empty/filtered import still yields deliberate choice; duplicate explicit eject cannot repeat | Notification only opens app; one-use service/registry tests and no-match dialog fixtures | Partial real foreground/native notification and physical explicit-eject integration |
| Shared removal+Trash inheritance copies/verifies only selected generated videos before removing them, retains ordinary/Trash photos | Removal/Trash file fixtures and frozen default inheritance | Partial combined importer/foreground/device integration; never performed on real media here |

## Bulk/grouped planning and result properties

| Exact Mac property | Inspected Windows evidence | Status |
| --- | --- | --- |
| Empty planning makes no request | `BulkEjectPolicy.Plan` returns zero targets; `BulkEjectResult.Message` describes no cards | Partial: source inspected; named empty service fixture still missing |
| Distinct card disks are separate targets; same disk partitions deduplicate; unrelated non-card SSD excluded | `BulkTests.cs`: plans only camera/emulator cards and deduplicates partitions | Fixture |
| Missing disk, blocked card, protected destination sibling, mixed non-card partition, or one blocked later disk refuses entire confirmed plan | `BulkTests.cs`: protected/unknown/spanning/untracked refusal; mixed partitions/unclassified LUNs; reservation of every confirmed disk before API | Fixture |
| Serial actions, success only after all confirmed sources absent | `BulkTests.cs`: survives own events and verifies each absent; accepted request without disappearance refuses; transient/incomplete inventory cases | Fixture; Windows device/LUN grouping rather than Apple per-volume flush |
| First failure halts before another source; no retry | `BulkTests.cs`: first veto stops; later veto retains only earlier verified safe names | Fixture |
| Topology/reconnect/extra source in confirmed removal group halts between sources | `BulkTests.cs`: identical reconnect, changed identity/classification/sibling, native-boundary gap, tracking loss. Newly unrelated card is excluded from the captured confirmation | Fixture |
| Final verification failure never claims safe to unplug | `BulkTests.cs`: accepted request without disappearance and incomplete disappearance refuse; EjectService fresh confirmed-group checks | Fixture; actual hardware remains required |

The path ownership audit discovered real gaps while checking these contracts. `PathOwnershipTests.cs` now covers callbacks replacing an empty destination, nested copy/reuse parent, saved-recording parent, completion folder and cleanup child/root. Holding directory attributes alone was insufficient. The corresponding source now checks held and freshly opened native identities before/after callbacks; history captures all exact recording parents before initial device validation. This is scope continuity during an operation, not persistent file-ID authentication across restart or an OS-wide atomic path lock.
