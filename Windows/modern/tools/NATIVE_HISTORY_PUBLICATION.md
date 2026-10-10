# Exact generated-video history gate

## Explicit reviewed empty issue baseline · October 10 UTC

The helper now takes an optional trailing `--reviewed-empty-issue-baseline` on every live
mode. It selects the reviewed empty issue state: exactly 49 bytes, SHA256
`4E998657AF8E8B4DEBCF79494D65A5B9C9811D8D1884A9551EDA8191F8C32651`. Without the flag,
the legacy 161B pin `2DA02745…85BC0` is still required, so the helper continues to
refuse the current state by default. Any other spelling, position or repetition is refused.

Publication records the selection as `IssueBaseline` in `admission.json`, and the fresh
config baseline must match that state's hash and length. Checkpoint and recovery modes
must pass the same selection the ledger recorded, or they refuse before writing.
Ledgers written before the selector existed read as legacy. Nothing is adopted
automatically, and the historical packets and the original `0C2ADA0F…` helper are kept.

`run-admission-verification.ps1 -BrokerGrant [-ReviewedEmptyIssueBaseline]`: the switch
also pins the live 49B issue state read-only before and after. Run from isolated copy
`PublicationBaselineSource-20261010T0402460818713Z`, it passed **17/0 core, 48/0
admission/integration, 60/0 issue-baseline and 12 CLI refusals**, each at its expected
reason. Receipt `PublicationAdmissionRun-20261010T0402464683487Z/result.json`
`E2C60935BEF18B47DED2FE6CA16553C144957F9288D3E76FE41B3C6A73669150`. See
[Windows progress](../../PROGRESS.md) for retained earlier attempts and limits. This is
still disposable/synthetic evidence; a live publication needs a new slot and fresh
native idle proof.

## Positive native live slice · October 9 EDT

Separately brokered live publication, explicitly reviewed native1080p/Standard
edit checkpoint and reviewed recovery each passed with a different actual
supported idle proof less than60s old. All helper processes exited0. Native history
verified two exact generated saved clips with source absent, hid sharing while
off, then offered separate manual export after opt-in:2 new, then2 reused.
All first-export bytes/hashes/write times remain. Both owned records are retained
in quarantine; history reopened empty and the original app returned idle.
[Full observations/commands/pins](../VISIBLE_REVIEW_WORKFLOW.md) preserve original
afterguard failure and separate UTC comparison correction. All649 payloads,
original process/window,15 pre-existing fixture files and tools config match.
Successful app export legitimately cleared its earlier scan issue; current49B
state and prior161B snapshot remain. Recovery restored no old config.

This slot is finished/handed back. The old helper still pins prior161B issue state
and must refuse a new publication against current49B state. A separately reviewed,
tested helper baseline is required; do not restore old issue or adopt unexpected
config. Future live/restart work also needs a new slot and fresh native idle proof.
No code/build/default-count change or complete-port claim. Native4K/full restart/
refusal/cancel, real camera/device/official Luna LUT/filesystem/travel/trust remain.

This optional engineering helper binds only the existing F810 review app and the
retained `NativeHistoryVideo-20261008` generated fixture. It is not an application
feature or a general configuration writer. Real camera media, devices and tools
are never imported, exported, cleaned or ejected by this helper.

The live modes require a separate brokered live-publication slot and a fresh
supported native idle observation. The self-test command authorizes no live write.
An inaccessible desktop is not permission to manufacture an idle observation.

## Disposable verification

Run the private `run-admission-verification.ps1 -BrokerGrant` only in the bounded
helper slot. It builds against the exact existing backend and SDK/cache outside
OneDrive, reruns the original 17 native publication/recovery tests, runs synthetic
admission and disposable integrated tests, and exercises negative CLI admissions.
Every fixture, rejected record and log is retained. Before/after guards bind the
current app/PID, all 649 payload files and both live configuration files.

The synthetic snapshots and stale CLI proof are rejection controls. They do not
prove successful native UI interaction, live idle admission, hardware safety,
Quick Share export, operating system sleep behavior or trusted distribution.

## Explicit live commands (not executed by disposable verification)

Each command takes `LEDGER IDLE_JSON IDLE_SHA PID START_UTC --broker-grant` after
the mode. The idle JSON is an owner-captured supported native observation with
`capturedUtc`, `window.app`, `window.id`, `window.title` and `tree` fields. It must
identify the exact current app and be at most 60 seconds old. A live ledger must
be a new direct child of the owned tools root beginning `NativeHistoryLive-`.

* `--publish-exact-video-history` publishes only the exact prepared manual/off
  profile and completed-import history. Original source/proof handles and their
  directory ownership remain held through publication. Records consume copies of
  the once-read, checked bytes; the helper never rereads source names at handoff.
* `--recover-exact-video-history` moves only the exact originally published file
  identities/bytes into retained quarantine. It refuses an editor replacement.
* `--checkpoint-exact-profile` additionally requires the explicitly reviewed
  edited-profile SHA256. Only SharingPreset may change to 1080p or 4K. Standard
  color, manual import, removal/trash/eject/defaults choices, destination and all
  identity fields must remain semantically identical. The helper holds the
  writer, directories and every published record while checking and journaling.
  It prints the hash of the exact checkpoint bytes it flushed.
* `--recover-reviewed-exact-video-history` additionally requires that separately
  recorded checkpoint SHA256. It binds the original publication pin, the reviewed
  replacement pin and its semantics under the held recovery handle. It does not
  discover or adopt a checkpoint automatically. Same-byte replacement, subsequent
  edits, checkpoint changes and changed history refuse before any record moves.

All records are preflighted together before recovery moves the first. Unrelated
configuration, including a newer app issue, stays in place. Recovery is explicit
and retryable; it never silently restores old user configuration.

## Verified milestone

Exact command: `C:/Users/rsmit/.codex/EasyEjectBuildTools/PublicationGuardSource-20261009/run-admission-verification.ps1 -BrokerGrant`.
Parent **2148**, **2026-10-09T04:46:14.6199111Z–04:46:21.2641879Z**, terminal exit0.
The existing SDK10.0.401/cache and cleared feeds compiled with warnings as errors
and no warning/error diagnostics. All seven selected backend/runtime pairs match
the review app. Actually loaded helper backend is hash-checked F810; the pairs
are file evidence, not a complete OS-loaded-module census.

**17/0 core**, **48/0 admission/integration**, **eight expected CLI refusals**.
Core and admission stderr are empty. Integration exercises production
`ReadSaved → ImportProfileEdit.SetSharing/Save → Profiles.SaveOffline`, including
native identity replacement, for both sharing sizes. Tests retain all generated
records, replacements and quarantine files. The source-change controls cover a
changed snapshot before construction, changed captured payload before handoff,
released source ownership and an actual write blocked by the retained exclusive
handle. Missing/wrong mode/grant/proof/hash/PID/start and stale-proof CLI controls
return1 with no live ledger and unchanged live config. The stale control reaches
the freshness refusal; wrong-start reaches the PID/start refusal. Snapshots are
synthetic and no positive native idle/publication claim follows from these tests.

Packet: `C:/Users/rsmit/.codex/EasyEjectBuildTools/PublicationAdmissionRun-20261009T0446145891869Z`.
Core fixture: `C:/Users/rsmit/.codex/EasyEjectBuildTools/PublicationGuardTest-20261009T0446146195853Z`.
Admission fixture: `C:/Users/rsmit/.codex/EasyEjectBuildTools/PublicationAdmissionTest-20261009T0446191380556Z`.

| Evidence | SHA256 |
| --- | --- |
| `result.json` | `113CD783CF0DF5C8481E69D52B10F8018BA7F830F2BFFAA2857A43556B90514C` |
| Helper executable | `0C2ADA0FCA4FDC59F4F856755AECEDD402C2715067ED610B6F403EF16D12A02C` |
| Helper assembly | `0C101A05ABA52F612EAFBB95CB4AAE031CAA4FCDDA68F067BF36B80F5CBF6FAF` |
| `OwnedRecordPublication.cs` | `315F253CF4997C3EFBA2E92241CF5E6098D749C48B98458D64F09F86E6D40316` |
| `NativeHistoryPublication.cs` | `8B25473EF4915F3047F613BDE3A86F92A4C4EB78B1D8F4E202D72E1749230BBB` |
| New project | `FF8C7BFD95E77C46F6BE3ABFCB3498A3220E761846029B0EDE31BB978C764A2C` |
| Verification wrapper | `6FEB33F069A18E538C928D7C68723864F5E995964EC6475C16070A6DEF0B7C13` |

All649 payload files, exact foreground PID21224/start/path and both live issue/tool
records remain unchanged. Before/after HEAD1f4aa6e is clean; no owned helper remains
running. Tested source/project/wrapper/feed inputs were copied byte-identically
into this repository after the execution boundary; milestone documentation is
separate. Default tests/app build, native UI/manual export/cold restart, official
Luna LUT, real camera/disposable-device/filesystem/travel and trust gates remain
open. At that historical checkpoint supported input was denied. The positive
slice above followed Ryan's Codex restart and separate live admission. Future
execution still requires current supported input and fresh admission.
