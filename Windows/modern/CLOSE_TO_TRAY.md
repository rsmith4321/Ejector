# Modern close-to-tray correction · October 8

Source **ee7fcc5** corrects the modern window's Closing handler. A normal window
close now cancels destruction and hides the window before considering active
work. The window, worker, dialog, monitoring timer and tray ownership remain
alive. Explicit Quit keeps its separate busy guard; a refused Quit clears the
quitting flag and reveals the current window instead of leaving a deferred Quit.

This is a source-discovered mismatch, not a reproduced native UI failure. Before
the change, `UiBusy` returned before Hide, although the footer and Help promised
close-to-tray during work. The classic `DeviceWindow.FormClosing` already hides
UserClosing before its busy shutdown guard. Mac baseline
`384cb767ffbbb87ec5fe67f45d0b4d0ce324ad7c`, `AppLifecycle.swift:163–172`, keeps the
app running after its last window closes and refuses termination during import,
eject or a sheet. Existing operation cancellation and completion visibility
policies are unchanged. The correction does not start, cancel, export or eject.

Fresh source is isolated archive **eb0470a** plus only MainWindow.xaml.cs,
`ModernCloseTraySourceVerified-20261008`. Another task's dirty sharing files are
excluded/preserved. The maintained review helper produces
`C:\Users\rsmit\.codex\EasyEjectBuildTools\ReviewCloseTrayVerified-20261008\app`:
**601 files /284307266 bytes**, bundled .NET10.0.12/SDK2.5.1, generated app PRI and
unchanged artwork. Build and self-contained Release selected harness compile
with warnings as errors and no warnings/errors. **236 passed/0 failed**, using
this app's exact backend bytes, SHA
`3c961a92365443409ba7581d3558e558737d9eeec8775b0a51903f76af6df73a`.
All payload hashes and the owned source hash match. Managed app SHA
`6b7a431686eb21522f4453362ff345d97337af77990d9d5ac6f0dc412a8bab50`;
PRI retains SHA `faddbc964e528275bc521b9a86f7443e081607d13fcd599e454bc6be8e0477bd`.

Command: `verify-modern-close-tray-v2-20261008.ps1`, under the external
`C:\Users\rsmit\.codex\EasyEjectBuildTools` root. Durable parent 2004 runs
13:54:47Z–13:55:31Z, terminal 70409 exit zero, failure null.
`modern-close-tray-verified-source-scope-20261008.json`,
`modern-close-tray-verified-result-20261008.json` and
`modern-close-tray-verified-evidence-20261008.json` retain scope/results/hashes;
the review folder holds build.log/contracts compile.log/selected.log.

All 102 runtime C# source files match the previous full-test snapshot after
normalizing only CRLF/LF. The first comparison refused a raw newline difference
in ModernWindowsReleaseManifest.cs; `git diff --no-index --ignore-cr-at-eol`
confirms no other difference. The initial failed snapshot/result is retained.
The full suite was not rerun for this UI-only change; the prior **1289/0** belongs
to its [separate build](PUBLISH_RESOURCES.md). Selected worker/lifecycle tests
prove existing software contracts, not this native Closing event. No mirror
test or test/warning suppression was introduced.

Desktop input remains paused after physical Escape. This folder has not been
launched. Actual close/hide during work and an open dialog, retained progress,
tray/secondary reveal, separate Stop, completion visibility, explicit idle Quit
and session shutdown still need foreground acceptance. Quit the running preview
normally before switching copies; no process was killed or silently replaced.
No real media/device/settings/startup/security/power/display action or public
release occurred. Full port, identified disposable hardware, official LUT and
remaining distribution acceptance stay open; the goal remains active.
