# Website updater publishing

Sparkle 2.10.0 is pinned in the Xcode project and Package.resolved and linked only to Ejector. EjectorStore must have no Sparkle framework, SU Info.plist fields, or updater network entitlement.

The website app reads https://github.com/rsmith4321/Ejector/releases/latest/download/appcast.xml. Each public GitHub release must include the generated signed appcast and the exact signed archive it references. Use a version-specific download URL in the appcast, never a moving latest URL for the archive. Create releases as drafts, upload all assets, verify, then publish as latest. Do not mark unrelated or Store-only releases latest.

Use a stable production Xcode, archive and export with Developer ID so Sparkle nested helpers are signed correctly. Notarize and staple the app, create/sign/notarize/staple the DMG, then generate the appcast over the FINAL DMG bytes. Add matching Easy-Eject.md notes and use generate_appcast --account com.ryansmithphotography.Ejector --download-url-prefix https://github.com/rsmith4321/Ejector/releases/download/vVERSION/ --embed-release-notes RELEASE_DIRECTORY. The generate_appcast tool signs the feed because SURequireSignedFeed is true in the bundle. Verify the feed with sign_update --verify and verify the archive's enclosure signature. Never edit a generated feed without re-signing it.

The private Ed25519 key lives in this Mac's login Keychain under account com.ryansmithphotography.Ejector. Only the public key is committed. Protect and back up the signing key through the normal secure Keychain workflow; never put it in GitHub assets, source or logs.

Installation requires approval; SUAllowsAutomaticUpdates and SUAutomaticallyUpdate are false. No system profiling or JavaScript release notes. The busy check defers relaunch through shouldPostponeRelaunchForUpdate; do not reject activity in updaterShouldRelaunchApplication because Sparkle checks that first and aborts instead of postponing. The app's final termination hook refuses to stop an active import/eject or close its profile sheet.

Validation includes both targets, defaults snapshot/legacy-profile tests, busy and sheet restart tests, and a disposable signed fixture updated from version1 to2 through Sparkle. The fixture uses loopback HTTP and separate bundle IDs; production uses HTTPS only and must never inherit the fixture's ATS override. Keep actual-device proof separate from these tests.
