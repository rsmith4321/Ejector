# Insta360 card review · October 11, 2026

Ryan identified the attached Insta360 card and allowed formatting. No format was needed or performed. Native actions used the explicit UI-test engineering build with isolated preferences/history, not the live configuration. Source writes were confined to a newly created generated-fixture folder on the identified SD card. Existing DCIM/MISC contents and camera internal storage were not changed.

## Bound scope

- SD card **F:**, exFAT, 512,663,486,464 bytes, volume `\\?\Volume{e8b09d42-c528-11f1-8188-40c73cd727f5}\`, serial `EEEFBFD0`.
- Disk 2: `USBSTOR\DISK&VEN_SD_CARD&PROD_SD_CARD&REV_0100\33840FA1&1`.
- Camera internal **E:** is another volume on the same USB removal target. Every removal review correctly lists both. All such reviews were cancelled; actual eject/veto awaits separate scope approval.
- Source: `F:\EasyEject-Test-6c51a7d94e8c4a63b095d8a3e0fb5c42`.
- Destination: `C:\Users\rsmit\.codex\EasyEjectFixtures\device-33840fa1-20261011\destination`.
- Preferences/history: `C:\Users\rsmit\.codex\EasyEjectFixtures\ui-device-review-20261011`.
- Manual import only; videos only, previews included, clean layout, ask before eject. Original removal, Trash recovery, automatic import and Quick Share off.

The three files contain deterministic repeated random bytes. Their extensions exercise copy/selection; they are **not decodable media or genuine footage**.

| Generated file | Bytes | SHA-256 after retry |
| --- | ---: | --- |
| `GENERATED_BYTES_copy_test.mp4` | 536870912 | `5a48c58d62163b34e9f0199d7eb1fbf7ff32a7e80c2d01236129ad122b8fa300` |
| `GENERATED_BYTES_stop_test.mp4` | 4294967296 | `36e99bdc5a90cf871c29b557383b12aeb239353060db19391a9eb06a701a75c2` |
| `GENERATED_BYTES_excluded_photo.jpg` | 1048576 | `45f3d2377a124f104ab5deb33ef0130231bce73349760947cb2106feda89fe4a` |

## Observed native results

1. Empty DCIM: only the zero-byte device index backed up. Repeat reused it, created no media history, and displayed **No matching media. Eject now?**. Return selected highlighted **Keep Connected**.
2. Generated 512 MiB copy: one verified file, independent matching destination hash. Repeat showed one reused file. The excluded photo was absent from the destination.
3. Busy tray Quit: actual tray menu `PerformClick` through the test-only footer refused Quit while verification continued. Idle Quit through that same callback ended the owned process. Restart with the exact marked isolated root retained history; the first pre-Quit batch's **Verify saved originals** reported all one exact saved files verified.
4. Stop regression: the original custom panel skipped measuring collapsed children, and a newly visible Stop action had no cached clickable bounds. After measuring every child before excluding collapsed sizes, the rebuilt window visibly showed Stop. The native regression also checks its nonzero width/height.
5. Active 4 GiB copy: clicking Stop displayed the stopping explanation, then **Import cancelled** and idle actions. Independent inspection found zero partials, zero completed `stop_test` copies, and the same five existing completion receipts. No automatic retry occurred.
6. Fresh manual retry: **2 files verified (4,831,838,208 bytes; 1 reused)**. Independent hashes of both source and destination files matched the table. The excluded photo remained on the source with its original hash and no destination copy. Return kept both volumes connected.
7. Flyout: E:/F: capacities, Quick Share hidden while off, correctly rooted E:/F: removal confirmation cancelled by Return, and Open window revealing the main window.

Local manifests and independent hashes are retained in the destination fixture's parent: `scope.json`, `generated-files.json`, `stop-fixture.json`, `post-retry-hashes.json`. The generated source files are deliberately retained; no real card cleanup ran.

These observations cover this card connection's manual copy/Stop/retry/reuse and limited native UI behavior. They do not establish camera model, genuine-media Quick Share/LUT behavior, source-absent native verification, physical eject/veto/reconnect, sleep/wake, all physical DPI/themes, clean-machine startup or trusted signing. The test-only footer is absent from distributable builds; actual Windows tray-icon input was not exposed by the supported window inventory.
