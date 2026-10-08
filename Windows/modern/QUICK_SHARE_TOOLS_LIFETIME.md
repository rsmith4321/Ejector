# Quick Share setup dialog lifetime · October 8, 2026

Source **3b8adc0** retains the exact native picker and Save tasks until they finish, including when XAML presentation fails. Previously this dialog used only busy flags and an `async void` Save callback; its outer finally could release review ownership and dispose the shared cancellation source without awaiting that callback's file worker. This is source-discovered hardening, not a reproduced native failure claim.

Picker, clear and Save callbacks now require the exact active dialog and an open window. Late picker results cannot alter an expired review. Save freezes selected paths and retains its own cancellation source through the complete async Save workflow, instead of reading the mutable cancellation field from the background lambda. The outer finally invalidates callbacks, awaits both retained tasks, then releases the busy/tray review. Closing by Escape or another dialog route waits while a picker or Save is active; the explicit Stop validation button still requests cancellation. Uncertain or cancelled Save requires reopening saved choices before retrying. Nothing exports, installs tools, removes originals or retries automatically.

The three picker buttons and read-only path fields also have distinct accessibility names for FFmpeg, FFprobe and the official LUT. Visible text and manual/default behavior remain.

## Software evidence

Isolated archive **b61afbc** plus only `Windows/modern/app/MainWindow.Sharing.cs` is retained at `%USERPROFILE%/.codex/EasyEjectBuildTools/ModernToolsLifetimeSource-20261008`. It excludes the other task's modern release-policy integration. Worktree/snapshot source SHA **7AD54CE4618C124109DA93A76155CE3AB0FBAB523444D74913DADB84DEDBE2F2** matches.

```powershell
& C:/Users/rsmit/.codex/EasyEjectBuildTools/ModernToolsLifetimeSource-20261008/Windows/modern/build.ps1 -Test -BuildRoot C:/Users/rsmit/.codex/EasyEjectBuildTools/ModernToolsLifetimeBuild-20261008
```

App/runtime/selected contracts compile with **0 warnings/0 errors**, and the existing selected suite passes **236/0**, terminal **97133 exit zero**. These tests preserve existing domain contracts; they do not simulate the new XAML/picker/event failure paths. No new default/full-suite or combined current-worktree acceptance is claimed.

- Managed app SHA: **DB9399A853C7F271DB3F80D2259AE97C2A2201C7BCA4FA89E93BDEA5E1A4AC5C**.
- All three runtime copies SHA: **2975048F030D587D040AAA6EBCCA09776BB72025C0026C12B1EC0D323BBB5DEA**.
- Selected managed harness SHA: **DE2B9635491A68973463667C6B80DEBC93BB2B071BC60D8594D5FFDBB5E4F8CB**.
- Final owned build/test/app process count is zero before attempted foreground follow-up. The new candidate is not launched.

Evidence in `%USERPROFILE%/.codex/EasyEjectWindowsPreview`: `modern-tools-lifetime-source-20261008.json`, `modern-tools-lifetime-build-20261008.log`, `modern-tools-lifetime-terminal-20261008.json`, and `modern-tools-lifetime-input-denial-20261008.json`.

## Remaining native acceptance

After announcing further authorized UI checks, supported normal launch of the **older** dialog candidate refuses with `GetCursorPos failed: Access is denied. (0x80070005)`. Input stops. Its existing PID8124/start/path remains hidden; the new candidate has zero processes. No lock cause is inferred and no security/power/display change or alternative input path is attempted. Both direct EasyEject JSON/profile stores remain empty. The human tray-click result is still unverified; this failed normal launch does not prove it.

Actual new-dialog picker cancellation, distinct accessibility names, Escape/close while picking or validating, explicit Stop, immutable choices, uncertain-save reopening and injected presentation failure with a held worker remain open. These need a working supported desktop plus owned fixture setup where saving can be tested without normal user configuration. Full foreground/tray/nonempty import workflows, genuine camera/official LUT, explicitly identified disposable hardware/filesystems/travel and trusted direct distribution remain required. The full goal stays active; the expired heartbeat stays paused.
