# Store-only release 1.0 (3)

Ryan explicitly authorized replacing the old local and website build, installing the Store app and submitting the replacement. This supersedes the earlier audit-only/no-release boundary.

- Based on shared-code parity commit `f400d623a55c804a71227c571c491618e9d094e6`.
- Added first-launch, Help, Settings and chooser instructions explaining whole-card authorization versus media/destination folder grants. Plain eject requires no folder grant. Product filename is now Easy Eject.app.
- Archived with Xcode 26.6 RC2 17F113. Apple build `d923baa8-8fab-4183-8364-ae16e89c7d7f` is VALID and APP_STORE_ELIGIBLE.
- Local `/Applications/Easy Eject.app` is the Store archive re-signed for local use with Developer ID, sandbox intact, notarized and stapled. Notary ID `cfd589a8-8e77-4bd3-88ea-752f0fd4235a` Accepted. It is not yet an App Store-delivered installation.
- Retired app backup: `release-evidence/store-only-release/retired-local-build/Easy Eject 1.5.2.app`. Both production profile arrays were empty before installation. No real card was ejected/cleaned and no media deleted.
- Website direct-download links retired, single-app transition and authorization guides deployed. Historical GitHub releases are retained. Public availability remains pending.
- New 1440x900 screenshot embeds the actual build-3 authorization screen, with first-launch help observed. Native menu text was observed with the connected CFexpress card; menu pixels remain unavailable through the capture tool. No fake menu or historical full-app screenshot was used for the new asset.
- Existing build-2 review was cancelled for replacement on the same app/version record. Exact final submission state is recorded in `release-evidence/store-only-release/final-submission-state.json` when complete.

The shared engine and ejection implementation are unchanged from the parity verification: 14 full and 16 signed sandbox tests, 9 bulk planning and 9 automatic-eject policy cases passed. Signed sandbox disposable-volume tests proved manual eject, Clean & Eject with media retention, Trash recovery, saved permissions and reconnect/import/auto-eject. Real CFexpress cleaning, physical global shortcut, notification delivery, login/reboot, older OS/Intel and broader hardware/filesystem runtime remain limited or unverified. See SHARED-CODE-VERIFICATION.md for scope.
