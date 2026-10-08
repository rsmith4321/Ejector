# Owned sleep/wake connection probe

This standalone opt-in fixture is excluded from both the application and default test projects. It reproduced the gap in [POWER_CONNECTION_AUDIT.md](../../POWER_CONNECTION_AUDIT.md): warning-free pinned-backend **9/6** baseline observations and three specific admission refusals, followed by **15/0** on both the atomic targeted backend and the final review's exact backend. [Acceptance evidence](../../POWER_CONNECTION_ACCEPTANCE.md) preserves source/runtime/logs and both **1388/0** final full-runtime results. Adding the fixture alone changes no production behavior; the separate shared tracker/notification fix is documented there.

Build outside OneDrive using the existing SDK and a fresh output directory. Supply `EasyEjectFixtureBackend` pointing to an exact existing review's `EasyEject.Runtime.dll`; record its SHA-256 before building and confirm the copied backend matches it afterward. The project refuses a missing backend reference. Do not run a build or the probe while another task owns the laptop testing slot.

Invocation has three required arguments:

```text
PowerConnectionProbe.exe --verify-owned-power-boundaries <existing canonical Easy Eject Profiles directory> <exact loaded backend SHA-256>
```

Without the opt-in or correct backend hash it refuses before constructing a window. The existing configuration must contain ordinary direct records only and match the pinned backend's normal configuration resolver. The probe hashes all direct records before and after; it creates no configuration records. Existing configuration must already be present, including the normal app-data alias. A canonical packaged-process directory may differ from that ordinary alias. Preserve logs and partial evidence on refusal.

The probe constructs and disposes its own invisible storage notification window and a read-only classic form. It hides the classic fixture's constructor-owned tray icon immediately and never shows/runs the form or pumps UI events. It does not enter either application entry point, claim the normal singleton, register a user shortcut, start automatic imports/updates, or control the existing app. Native actions are notification registration/unregistration and read-only inventory of the protected internal Windows volume.

Each case captures an actual scoped `StorageConnectionProof` from that owned discovery, then directly invokes the owned handler's managed `WndProc` with `WM_POWERBROADCAST`. It never sends/posts a native message to any window or broadcasts to the OS. Suspend `4`, automatic resume `18` and user resume `7` must retire the earlier proof. Suspend must also block admission until synthetic resume; a fresh post-resume proof must validate. Separate controls check that charger status `10` preserves a current proof, a global epoch change alone still permits scoped revalidation, resume does not clear an injected fault on this owned tracker, and disposal closes notification admission.

Output is structured JSON with event, epochs, stamps, inventory completeness, old/fresh proof results, per-case errors and backend identity. Exit zero requires every expectation. An expected before-fix failure is evidence only after inspecting those observations and excluding native registration/inventory failures or unrelated live device changes. An admission failure is labeled separately. Keep the complete output, not only totals.

This fixture does not suspend the computer, change lid/sleep/display/security settings, request eject, import media, delete originals or replace the running preview. It cannot prove real resume delivery, Modern Standby behavior, actual missed native callbacks, or supported device/filesystem behavior. Those remain separate foreground and explicitly identified disposable-device acceptance gates.
