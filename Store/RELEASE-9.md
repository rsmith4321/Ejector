# Store 1.1 (9): current shared import and eject workflow

Includes the shared changes through Website local 1.6.5: media selection with Videos only and camera-preview controls, clean dated-folder layout, protected Insta360 index backups, explicit Keep Connected / Eject dialogs, one outer import-window scroll area, and coordinated unmount of Internal + SD Card on a shared USB camera. The grouped confirmation names all affected sources and warns about unimported media; no force eject occurs. Existing profiles retain their options and source files are kept by default. The Store bundle, sandbox and scoped folder permissions remain unchanged.

Release archive uses the preserved production Xcode 26.6 toolchain. Universal arm64/x86_64 signature, version/build/bundle and sandbox entitlements verified. All 48 signed sandbox importer checks and the two-volume sandbox unmount integration pass. Native dialog and fail-closed grouped sequencing tests were verified in the shared 1.6.4/1.6.5 work. Physical multi-source camera ejection is not newly tested.

Prepared for App Store Connect app 6767951388 as an update to approved/live Store 1.0 (8). Upload/processing/submission status is recorded in release-evidence/store-latest-2026-09-28; this document alone does not prove approval or public availability of 1.1. The installed Website app is not replaced.
