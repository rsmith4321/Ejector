# Native persisted issue recovery · October 11, 2026

Unchanged production issue recovery now passes native initially empty-state rereading, acknowledgement/true restart, changed-record refusal and explicit recovery. All warnings are labelled **Generated native acceptance issue A/B/C** and state that no import, export or removal failed or was retried. They are disposable configuration fixtures, not genuine operation failures or hardware acceptance.

## Scope and preparation

Fresh isolated root: `C:\Users\rsmit\.codex\EasyEjectFixtures\ui-issue-native-20261011`.
Evidence packet: `C:\Users\rsmit\.codex\EasyEjectBuildTools\Codex020-issue-native-20261011`.
Unchanged engineering host: `C:\Users\rsmit\.codex\EasyEjectBuildTools\Codex020-reveal-final-20261011\bin\EasyEject.Modern\Debug\net10.0-windows10.0.26100.0\win-x64\EasyEject.Modern.exe`.
Source checkpoint **f0355a90fa4f688599760c35a1611030ca85b78d**; no new build/source change.

Inspected `MainWindow.Diagnostics.cs`, `AppSettings.cs` and `IssuePersistence.cs` before preparing data. View issue rereads the record; IssueRecordReview pins its folder and compares the exact previous file during exclusive publication. Failed actions are consumed; fresh review retains distinct live/saved explanations; Save current explanation preserves acknowledgement status and retries no operation.

Fixture A is exclusively created only in the fresh app-owned root after the empty view is closed. Preparation checks the exact review marker, owned process/path and ordinary ancestors. The retained `write-owned-issue-fixture.ps1` changes only that exact fixture file, checks its expected SHA-256 under an exclusive bounded stream, verifies ordinary ancestry and retains before/after payloads. It contains no device, media, live-configuration or UI automation code. All records use the actual `Dismissed`, `Message`, `ObservedUtc` schema and production readers/writers. B/C are deliberate external fixture changes, not app-created warnings.

## Native sequence

1. PID **31592** starts empty. View issue displays **No saved issue needs attention**, Close default and no acknowledgement action. Escape closes without a write.
2. Exclusively add A. A new View issue rereads it and displays **Needs attention**, correct explanation/date, **Dismiss issue**, highlighted Cancel. Return cancels; file hash remains exact.
3. Separate explicit Dismiss saves only acknowledgement. JSON checks preserve message/date and require `Dismissed:true`. Native Summary confirms no operation retried. Actual tray callback Quit exits normally.
4. True `review-restart` PID **30568** displays **Last issue (acknowledged)** for A with Close and no Dismiss. Record bytes/hash remain exact after restart/read. Escape closes.
5. Prepare undismissed B and open its native dialog. Replace only the saved fixture with C while B remains displayed. Stale Dismiss refuses with **Saved issue state changed. Open View issue to reread and review it before saving.** C remains byte-identical to its prepared copy.
6. Fresh View issue shows live B and saved C, explains the unconfirmed save and offers **Save current explanation**, Cancel default. Return preserves C exactly. A later fresh explicit Save current explanation writes exact B with `Dismissed:false` and original date. Native Summary confirms acknowledgement status unchanged and no operation retried.
7. A separate new View issue offers Dismiss rather than Save current explanation. Explicit Dismiss acknowledges B and preserves its message/date. Actual tray callback Quit exits PID30568.
8. True second restart PID **28040** displays **Last issue (acknowledged)** for B with only Close. The recovered acknowledged record remains exact. Escape closes; idle actual tray callback Quit exits normally.

All three processes are absent at the final check. Only `issue-state.json` exists under the isolated Profiles folder. Both live configuration files retain exact count, hashes, lengths and write times. Read-only RegistryKey inspection confirms login Run value `EasyEject.Windows` absent. Inputs use supported Computer Use; no alternate input/termination. Settled observations resolve immediate accessibility animation lag before choosing controls.

## Exact evidence

Initial/restarted UI DLL: **505856 bytes**, SHA-256 `340B8589A27935A2C0CC3AEA03AED5831F30BCC6A2A7CE8E49AD27BECD828E0D`.
Initial/restarted runtime DLL: **1008128 bytes**, SHA-256 `9BDC84290BAD34C4ACAEB7AB8674320FABFD3413C09B81925AF59D0001601010`.
Independent module-receipt comparison passes.

| Packet record | SHA-256 |
| --- | --- |
| `seed-a.json` | `DE047D9BEFA56D90F8201207C47C6F35034BF2DE4633FC2B3078F07998D6A9DF` |
| `ack-a.json` (exact through first restart) | `3B28409227A4108FFE715994EE9C689597240CC0C2C5CFE4B3A7F2A623DCE1E7` |
| `fixture-B.json` / `recovered-B.json` | `57A574D4CDBC2A9257537E9B6533BA38FB458416C37610D8FC25801B8AA7D2EE` |
| `fixture-C.json` / `after-stale-C.json` | `B2AAE7AAE6AC1CBEEF3D3C9D7276E7D596A9EF161C39BFAFB769D5D6F3BA376B` |
| `ack-B.json` / final issue (exact through second restart) | `F7AC0601A947BEF5BC04D76DC2B0997C0C3BB178855B6DE9672CDD95BE87EA66` |
| `final.json` | `AF52E4D03A71579AE2BCB64118D4C51C363CA206D80F4A58CA8FC494AE05DAD4` |

`final.json` retains record pins, live before/after comparison, both module receipts, restart evidence and three passive trace hashes. Screenshots remain in the tool transcript; none were extracted/saved. The unchanged host's prior warning-free native presenter/tray Quit/Stop regression is in `Codex020-reveal-final-20261011.log`; no rebuild/rerun or new full-suite count is claimed.

This proves the exercised native issue paths, not every uncertain-write/presentation-failure/filesystem boundary. No real operation, OS notification/tray-icon click, clipboard/log clear, camera/media, eject/format/cleanup, autostart/OS setting, tool installation, push or publication occurred. Physical/media/LUT/trust and explicit publishing gates remain open; full Windows port goal remains active.
