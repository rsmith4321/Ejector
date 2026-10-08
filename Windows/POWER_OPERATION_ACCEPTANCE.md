# Sleep/wake during active operations · October 8, 2026

This extends the [connection-boundary milestone](POWER_CONNECTION_ACCEPTANCE.md) with named operation coverage. Production code is unchanged. Every new case uses retained generated files, fake inventory/power/eject APIs and a synthetic media process; no physical sleep, device removal, genuine media or FFmpeg execution occurs.

For each suspend `4`, automatic resume `18` and standalone user resume `7`, the 27 cases verify:

- Manual Quick Share stops before encode, after encode, or during full-decode validation, preserves source and saved originals/history/import receipts, publishes nothing, releases its destination reservation and allows a fresh explicit size choice. Merely resuming/reading the offer starts no export.
- A completed first export retains its exact output and receipt when the next export is interrupted. A fresh manual retry reuses it and creates only the remaining export.
- Retirement between exclusive output and receipt publication preserves the unreceipted file, reports the exact `1/0` publication counts and requires attention. Retry neither overwrites nor trusts that file; it publishes a separate validated copy/receipt. Private encoder work is removed.
- Metadata cleanup stops after one accepted deletion, reports its precise count, retains two pending metadata files plus recordings/Trash, releases reservations and never reaches the fake eject API. The old eject confirmation stays invalid; a fresh reviewed retry removes only the two remaining metadata files before fake eject.
- Open and already-confirmed interrupted-removal reviews expire without removing pending originals. Fresh explicit confirmation resumes against the same exact journal/copies, keeping prior/current removal counts separate.
- A boundary after one accepted resumed removal stops before the next original. The audit/journal, pending original and all verified copies remain. Fresh review counts two prior absent sources and only one new removal; no completion/eject authority is issued for the interrupted action.

## Evidence and reproduction

Frozen parent: `f882f72de07e75acfdc6542add2bd54c613466b3`, archived outside OneDrive, plus only `tests/Tests.cs` and `tests/PowerOperationTests.cs` overlays pinned in `source-scope.json`. The peer's untracked `modern/CURRENT_COMBINED_BUILD.md` is excluded and untouched.

Packet: `C:\Users\rsmit\.codex\EasyEjectBuildTools\PowerOperationsFixedFixture-20261008`. Run `run-focused.ps1` from a fresh equivalent packet, with the existing SDK 10.0.401/cache and isolated build root; it compiles both runners with warnings as errors. The modern runner uses the unchanged exact review backend SHA-256 **F810DFB734CF47AE09FA8A9558DA5F819056FE059692CE31857ABE2AF1F35FAD** and all six matching bundled Core/Desktop runtime pairs. Framework compiles the same frozen production source, rather than executing the .NET backend bytes.

Actual focused parent **4200**, **22:04:56.1794922Z–22:05:34.2333600Z**, terminal exit0/failure null. Each runtime passes `--power-operations` **27/0**, `--power-connections` **23/0**, and `--resume-completion` **8/0**. Counts overlap with default-suite coverage. `focused-result.json` pins all six logs and the backend hash. Generated fixture roots are printed in the logs and retained.

The first packet `PowerOperations-20261008` retains **12/15** and its frozen source/log/result: all sharing cases lacked an injected Luna identity and returned before the intended boundary. This was a fixture admission error, not a production failure. Corrected fixtures assert exact original count and Luna eligibility before sharing; production policy was preserved.

`run-full-modern.ps1` completes with **1415/0 defaults** on that exact unchanged review backend, parent **18952**, **22:06:32.7390690Z–22:13:18.1697557Z**, exit0/failure null. Full log SHA-256 **FD6FA7F0130F0EC56DD4A6C2EB11C7BFF23C2BC025A2916B58B3D6B07533AEAD**. The actual runner PID24444 loaded the packet's bundled hostfxr/hostpolicy/coreclr, captured in `live-full-runtime-modules.json`. Both existing configuration record hashes and the original preview PID25172/start/path are unchanged in `full-modern-result.json`. The current Framework default suite was not rerun; **1388/0** remains its earlier evidence, supplemented by the new focused runs. No new app rebuild or foreground launch occurred for these test-only changes.

`test-input-conformance.json` records both test inputs' exact frozen/worktree raw hashes and Git-normalized blobs. Source archive, two overlays, first failed packet, all generated fixture roots, compile logs, runtime pairs and terminal result receipts remain retained outside OneDrive.

Local milestone **4b7abc989d04f75dcf4f359366c23fb3ea0ce681** independently matches both tested Git blobs, with unchanged production source, in `committed-test-conformance.json`. Ten completed evidence files are pinned in `completed-evidence-pins.json`; the six focused logs also have hashes in the focused receipt. No push/public delivery occurred.

## Remaining acceptance

The separate opt-in [native history follow-up](modern/tools/NATIVE_HISTORY_FIXTURE.md) now checks real read-only notification/destination integration, generated source-absent exact receipt verification and owned managed power refusal/fresh retry on this same backend. It leaves normal history/profile configuration untouched and does not increase the default count.

Named generated-operation coverage does not prove real OS message delivery, Modern Standby/hibernation/lid behavior, current-copy foreground/notification/cold lifecycle, actual camera/tool/LUT/filesystem/hardware/travel behavior or trusted distribution. Those full-port gates remain open. Live configuration and the original preview are preserved; no settings, power/security policy, public delivery or real device operation is authorized by this evidence.
