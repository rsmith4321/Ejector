# Windows direct distribution and manual updates

This is a reviewable delivery plan for the full Windows port. The current executable is an unsigned `0.1.0-dev` prototype. There is no approved Windows release channel, signing publisher, Windows package or public download. The new **Windows updates & installation** tray view gives manual instructions and read-only folder actions; it does not check releases, verify a downloaded package, download, install, replace or restart the app.

## Delivery and retained state

Deliver a versioned Windows x64 ZIP for Windows 11/.NET Framework 4.8, extracted into a folder owned by the user. Include `EasyEject.Windows.exe`, its `.exe.config`, approved release notes/licensing and authenticated verification information. Select the public version only when the release is approved. Do not use the Mac DMG/PKG/app, Sparkle appcast or Store assets as a Windows channel.

Keep application versions in separate folders so the previous folder remains available for rollback. Keep settings, enrolled profiles, defaults, exact last-20 batch history, selected tools, the user-supplied official LUT and diagnostics in the separately resolved `Profiles.Root`. The native guide displays the actual runtime folders rather than assuming the unpackaged `%LOCALAPPDATA%` path; packaged host redirection can change that path.

No FFmpeg/FFprobe installation or manufacturer LUT bundling is included. Their setup and every Quick Share action remain explicit. Startup remains off initially; installation does not create a registry entry. A user who already opted into startup must explicitly review the moved-copy registration when changing app folders.

## Manual install, update, rollback and removal

1. After an approved Windows release exists, verify its archive SHA-256 and trusted executable signature against its approved Windows publisher and authenticated download information. A familiar filename, matching version or any unrelated trusted signature is insufficient.
2. Before updating, finish or safely cancel work, close dialogs, and choose **Quit** from the tray. Closing the main window hides it and leaves work running. The guide cannot restart the app or cancel an import/removal request.
3. With the app stopped, make a settings-folder backup before upgrading. Extract the complete verified release into a new versioned folder, retain the previous folder, then manually start only the new copy. Review its version and saved choices.
4. To roll back, finish work and Quit the new copy, then manually start the retained previous version. It reads the same separate settings. If an older version refuses a newer format, review the earlier backup before any deliberate restoration; do not silently downgrade settings or overwrite changed state.
5. To remove the app, explicitly disable its own startup entry if enabled, finish work and Quit, then remove only the chosen application folder. Settings and imported media remain. Clearing settings is a separate deliberate decision.

The source busy/scan/dialog guard blocks opening the update guide during active work, including unrelated manual eject concurrent with a transfer. The disabled owner/owned modal checks cover native dialogs. Mac Sparkle's deferred relaunch timer is not used in this approved manual delivery direction: the Windows guide never queues installation or relaunch, and Close/Enter/Escape only closes it.

## Candidate validation still to implement

The next independent source milestone is a read-only Windows candidate/channel validator. This is pending, not implied by the guide's tests. Its contract must bind platform/version/source revision, exact package contents, file lengths/hashes and the authenticated release manifest to an approved Windows signing identity. Check actual x64 Windows PE/application identity as well as asset naming; reject Mac assets, other architectures, unsigned/changed/aliased/reparse/extra package files, unknown signers and unconfigured channels. Validate a trusted signature with native Windows trust APIs and an approved publisher binding; certificate extraction alone is insufficient. Missing/inconclusive proof cannot become “up to date” or “verified.” Keep validation separate from a user's manual Quit/extract/launch actions.

Use fakes and disposable fixture files for source tests. Read-only native unsigned/refusal evidence is allowed; a fake positive signer or synthetic executable is not trusted signing proof. Hold candidate file/directory identities through review and recheck before any subsequent action. Do not extract or execute a candidate merely to inspect it. Do not add automatic downloads, installers, relaunch timers, system-profile collection or silent registry changes.

## Release gates

Before preparing actual signed distribution artifacts, record the full source/parity audit, actual tools/official LUT conversion validation, foreground/DPI/notification/hotkey/login behavior, Ryan-identified disposable camera/card/reader/destination/filesystem and long-transfer/reconnect/cancellation/cleanup/removal/eject evidence. Then confirm the Windows signing identity/trust path and obtain any needed explicit account/cost authorization. Do not create signing accounts, spend money or change security policy as a shortcut.

Record the actual release commit, tool/runtime versions, resulting artifacts, hashes, approved publisher evidence and independent verification/rollback results. An unsigned development build or passing fixture count does not satisfy those gates. Publish a Windows channel/download only after explicit publishing authorization. Until then keep the Mac editions/website untouched and every channel state visibly unavailable.
