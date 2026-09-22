# Prototype verification

Tested locally on September 21, 2026 (UTC logs extend into September 22), macOS 27.0 build 26A428, Apple Silicon, Xcode 27.1 build 27A9269. No real air unit or user media was used as an import fixture.

## Observed

- Debug development-signed app launched with App Sandbox. Release Developer ID-signed universal app launched and completed the native import tests. Strict signature verification passed. Release entitlements are exactly the three documented in README; no debug entitlement. Both arm64 and x86_64 slices exist; only arm64 runtime was exercised.
- CUA drove real NSOpenPanel source and destination selection, native profile editor, import action, warning dialog, menu, and login toggle. Both bookmarks were present in the isolated container profile. Automatic import and deletion began off; keep-originals text was visible.
- Native single-volume HFS+ fixture import copied 2 files totaling 3,120,000 bytes, logged SHA-256 verification, displayed progress and the Ejecting phase, and then displayed successful completion. The volume disappeared. Remounting proved both source files remained and matched destination SHA-256 values.
- Relaunch after renaming the HFS+ volume, media folder, and destination folder resolved the saved bookmarks. The app refreshed the persisted source path and destination label, reused both matching copies, and automatically ejected. The Developer ID release also resolved the earlier development build's saved permissions and completed import/eject.
- CUA opened the deletion warning. It contained the permanent-deletion explanation and developer disclaimer. Cancel was focused; pressing Return left deletion off. Destructive deletion itself was exercised on disposable files by the signed sandbox test harness below, not on real media.
- Registered Carbon shortcut returned success, and the native UI displayed its status. The final combination is Control-Shift-Command-J, opening the eject menu. CUA synthetic key injection did not demonstrate Carbon hotkey delivery. This is an explicit remaining physical-keyboard test, not a claim that the global shortcut was proven end to end. The Eject menu button exercised the same menu-opening action successfully.
- SMAppService login registration toggled on in the UI; `sfltool dumpbtm` reported this exact bundle as sandboxed, enabled and allowed. Turning it off produced disabled/allowed status. Login remains off. An actual logout/login was not performed.
- Manual Eject EEStorePartA from the native menu unmounted both EEStorePartA and EEStorePartB on one disposable two-partition HFS+ disk image. Neither partition remained mounted.
- Enrolling a media folder on EEStorePartA, enabling automatic imports, detaching and reconnecting the image while the app ran triggered a native import of an 18-byte fixture, logged verification and completion, and ejected both partitions. No manual import was used for that reconnect run.
- Removing POSIX access to the disposable destination before reconnect produced a visible Needs attention / Permission denied state. Both source partitions stayed mounted; the original and sibling-partition sentinel were intact. Restoring access and pressing Import now completed verification and ejection. Final remount/hash inspection reconfirmed source retention, identical destination content, and the unchanged sibling sentinel.
- Closing the main window left the Store process running. The original full target also built successfully without signing into a separate temporary output directory.
- Full-edition production `drone-profiles.json` was read and remained `[]`. The released `/Applications/Easy Eject.app`, original development checkout, website repository, and real media were not edited by this implementation.

## Real air-unit hardware follow-up (September 21, 2026)

- The user connected the same 25 GB exFAT USB volume used in the earlier O4 tests (UUID `0C2389D6-DFAF-3D80-BA05-5BECE97A785F`). Its DCIM folder was empty, so this test used two clearly named generated files in `EasyEjectSandboxTest`, totaling 48 MiB. This proves the real storage path, not import of a newly recorded DJI clip.
- Native picker authorization granted only that source folder and `/Volumes/Drobo/Drone Videos/Easy Eject Sandbox Test`. A manual copy-only import passed; the original remained, and independently calculated destination SHA-256 matched the initial source hash.
- After enabling automatic imports and successful-import eject with deletion still off, a full app quit/relaunch restored both folder permissions, verified both files, and ejected the physical air unit. The native window displayed safe to unplug and the mount disappeared.
- After the larger-icon/count update, the user physically unplugged/reconnected the air unit. Without pressing Import now, the signed sandbox prototype displayed Checking, verified both files and ejected it. Logs record completion at 2026-09-22T03:15:53Z.
- The user physically pressed Control-Shift-Command-J and confirmed that the prototype menu opened. A deliberately controlled other-foreground-app/conflict case was not performed.
- The menu bar now uses an 18-point semibold `eject.fill` symbol, recognizes SD/CFexpress/XQD reader metadata and enrolled source identities, and deduplicates by physical disk. Import progress and errors override its idle count. No broader disk permission was added. Signature validation passed after the update.
- Final retention inspection and fixture cleanup are pending the last reconnect. Login is off, test automatic importing is now off, and original deletion stayed off throughout.
- Evidence: `evidence/hardware-source.json` and `evidence/hardware-imports.log` in the local prototype artifact folder.

## Automated checks

The original 14 engine/metadata tests passed outside the sandbox before target-specific tests were added. The signed standalone test app then passed 15 tests with the same three release sandbox entitlements:

1. Copy, verify, delete, nested folders.
2. Copy-only/reimport reuse.
3. Collision preserves previous content.
4. RAW, unknown sidecars, and hidden-item retention.
5. Missing destination retains originals.
6. Cancellation retains source and removes partial output.
7. Volume-validation failure retains originals.
8. Audit-write failure blocks deletion.
9. Source symlink rejection.
10. Destination symlink rejection.
11. New recording prevents successful completion.
12. Changed retained recording prevents successful completion.
13. Store engine rejects requested Trash recovery.
14. Sandbox denies reading a runner-created existing file outside the container without a grant.
15. Invalid security-scoped bookmark fails closed.

Run `Tests/StorePrototype/run-sandbox-tests.sh /an/absolute/noncontainer/output/path` with the existing Developer ID identity. It builds its own separately named test bundle and creates an external sentinel before executing. Test data inside the test container is removed by each test. Engine failure injection is controlled, not a real device disconnect or storage fault.

## Evidence and artifact

Local artifact directory: `/Users/ryansmith/Easy-Eject-Review/store-prototype/`.

- `Easy Eject Store Prototype.app`: final locally signed universal prototype.
- `sandbox-tests.log`: all 15 signed sandbox results.
- `release-build.log`, `full-target-build.log`: compiler evidence.
- `evidence/native-imports.log`: actual sandbox app SHA-256, completion and permission-failure records.
- `evidence/retention.json`: final multipartition source/destination hash and sibling preservation.
- `evidence/release-entitlements.plist`: extracted signed entitlements.
- `prototype.patch`: concrete baseline-to-prototype review diff.

## Remaining gates

- A controlled shortcut conflict/other-foreground-app check. Physical-keyboard delivery has now been confirmed by the user in the hardware follow-up.
- Actual logout/login launch; the system registration state is verified.
- Newly recorded DJI media and additional card hardware, removal during transfer, long sustained transfers, filesystems beyond the tested HFS+/exFAT sources and HFS+/APFS destinations, Intel runtime, and supported older macOS versions. Real air-unit storage is now covered by the hardware follow-up.
- Reboot persistence, revoked folder authorization, unresolvable stale bookmark recovery through reauthorization, busy-device/eject-denial UI, and competing apps accessing the same device. Rename-driven stale refresh and invalid bookmarks were covered; those do not establish every revocation/disconnect case.
- Multiple enrolled partitions require automatic eject off until each desired import finishes. The prototype does not aggregate multiple profiles into one physical-disk import transaction.
- App Store bundle provisioning/signing, final branding/version, App Store Connect metadata/privacy disclosures, minimum macOS review, App Review acceptance of eject behavior and the neutral comparison link. The prototype is not submitted, notarized, published, deployed, pushed, or presented as approved.

## Cleanup

Disposable profiles were removed through the prototype UI. Login registration was turned off and verified disabled. Test app processes were stopped. Test images were detached, generated fixture media/destinations and the standalone test app removed. The final signed app, source, build output, logs, diff and verification documents are preserved. The Store app's isolated empty container and disabled system login record may remain as normal macOS app metadata.
