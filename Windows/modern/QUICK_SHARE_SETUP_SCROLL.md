# Readable Quick Share setup failures · October 8, 2026

When supplied-file validation fails, the tool setup dialog previously disabled its entire ScrollViewer. The error text follows the file choices and is below the initial visible area. An actual zero-byte fixture refusal on the e1f307c candidate reproduced the problem: Save and choices were correctly disabled, but scrolling did not expose the explanation. Close remained reachable and the existing saved setup stayed exact.

The correction disables a ContentControl containing the choices while keeping the surrounding viewer active. Failure still disables Save and requires a fresh manual review. Picker/Save ownership, Stop, close protection and exact configuration validation remain in place. The shared scrolling helper accepts UIElement so this wrapper uses the same layout limits as the other dialogs.

## Isolated software verification

External packet: `C:/Users/rsmit/.codex/EasyEjectBuildTools/QuickShareReadableFailure-20261008-Final`.

`source-scope.json` binds the exact **e1f307c5a67300e039986038cfac59b3f916b7c8** Windows archive, tree **531676e572e50d454a222497ec7e393064351e22**, archive SHA-256 **82ABC3AA5B84E95D53B2B6062134ACD796207CE85C059449D79BC1442700E87B**, plus only `Windows/modern/app/MainWindow.Sharing.cs`, SHA-256 **1A6452C1128CFFFE8114B8F053A135131FDBF05AECA7C68C3D8AB8E51AE6BB4F**. Concurrent vendor/package work is preserved and excluded. The manifest's declared revision is blank because this is an archive plus overlay, not the unmodified base commit.

```powershell
& C:/Users/rsmit/.codex/EasyEjectBuildTools/QuickShareReadableFailure-20261008-Final/verify.ps1
```

The cached SDK10.0.401 prepares a **601-file** unsigned self-contained review, .NET10.0.12/Windows App SDK2.5.1. Native app and selected runner compile warning-free with warnings as errors. Terminal **1755** finishes with exit0; `result.json` records **18:35:32.5985325Z–18:36:20.5300468Z**, passed, selected exit0 and **242/0**. `selected-backend-binding.json` binds the runner to the exact candidate backend, SHA-256 **BC047F127470CDF1C8F458CFE3A97CF39F7C2288545ACAC511085BBB9A1886D8**. No new full-default/Framework/actual-FFmpeg suite execution is claimed; earlier full results keep their source/byte scope.

The first attempt is retained in sibling `QuickShareReadableFailure-20261008`, including its failed result/build log. It used StackPanel.IsEnabled, which WinUI does not expose; CS1061 and cascading XAML compiler diagnostics stopped that build. The final ContentControl correction uses a fresh output directory.

## Native reproduction, correction and manual retry

Supported Computer Use inputs and snapshots provide these observations:

- Original **PID6148/window788100** selects our zero-byte `EasyEject-disposable-empty-20261008.exe`, refuses Save, disables choices/Save and permits Close. Mouse scrolling leaves the explanation below the visible fold. Reopening restores the valid supplied FFmpeg/FFprobe paths and editing. View issue displays the refusal; Dismiss acknowledges it without retrying.
- After that instance is verified idle with no open setup or enabled device-import action, only its exact PID/path/start identity is stopped to launch the isolated corrected copy. `original-idle-stop.json` retains this controlled process stop. This is not graceful tray Quit or active-worker shutdown acceptance.
- Corrected **PID25172**, start **18:38:52.3954229Z**, window **7472742**, reads the retained valid tool setup. Selecting the same empty fixture and choosing Save reproduces the refusal. Choices and Save stay disabled; the active viewer scrolls from offset0 to **236.66668701171875**, exposing the full error while Close stays fixed and reachable.
- Close then reopen restores valid paths and enabled controls. A fresh explicit Save succeeds with **“Quick Share setup saved. No export started.”** The saved setup remains byte-identical through refusal and manual retry.
- View issue displays the later failure as Needs attention after the earlier acknowledgement. Dismiss acknowledges only that new explanation, with **“Issue acknowledged. No import, export or removal was retried.”** The app is left idle.

`native-before-fix.json`, `native-after-fix.json`, acknowledged issue copies and `fixture-recovery.json` retain the observations. The zero-byte fixture was temporarily created alongside the existing supplied tools solely for direct picker selection after helper failures. Guards check its exact path, zero length, creation time and empty-file hash before moving only that fixture to `empty-tool-fixture.exe` in the evidence packet. It is absent from the tool folder; supplied FFmpeg/FFprobe hashes stay exact. An earlier disposable text fixture remains unselected in the older packet; this is not a native wrong-extension test.

Helper limitations are retained: cached picker element1970 fails, the Alt+n focus report remains Search Box, and no blind typing follows. Escape does not dismiss one reopened original dialog on this attempt; visible Cancel succeeds and the cause is unestablished. One evidence guard initially compares an auto-parsed JSON timestamp to a string and stops before mutation; the corrected UTC comparison succeeds. None of these is described as successful keyboard/focus acceptance.

## Preservation and remaining scope

Final receipt **18:43:16.6673481Z** confirms all **601/601** corrected files exact and **76** candidate-owned loaded modules matching the manifest. Module observation is a subset, not clean-computer/complete-loader or publisher trust. Original PID6148 is absent; corrected PID25172 stays idle. The existing **486-byte** setup, SHA-256 **C366C5017700613C17F734FFC01C8AEACC0F386B61EF19558FD8DE57EE94728D**, retains both supplied tool bindings and null LUT. Profiles contains that setup plus the acknowledged test issue; no device profile, defaults, application-settings or import-history Save occurs. Older empty-store/process-alive observations describe their earlier checkpoints.

No fixture/tool execution, export, LUT copy, real media/import/eject/cleanup/removal, system settings, installation or publication occurs. This closes the reproduced failure-scroll bug and checks native refused Save/manual retry/issue acknowledgement. It does not prove busy validation Stop/close, other layouts, tray Quit, nonempty device/history workflows, genuine Luna footage/official LUT, identified disposable hardware/filesystems/travel or trusted distribution. Full port goal remains active.

## Native late-Stop attempt and exact restoration

On the same corrected PID25172/window7472742, a single owned 1 GiB zero-filled `.exe` fixture is selected solely for hashing; it is never executed. Save exposes disabled choices/Save and the visible **Stop validation** button. The next supported input requests Stop, but validation has already committed: the dialog closes with **“Quick Share setup saved. No export started.”** The exact saved record contains the fixture binding, SHA-256 **A92C17B0A806B4271A217CE5E0173AB3855DCA6D97226F996B4171648F1D239C**. This is a late request, not a successful cancellation, and no timing retry or busy-close proof is claimed.

A fresh native review selects the original supplied FFmpeg again and explicitly saves it. The original 486-byte configuration hash **C366C5017700613C17F734FFC01C8AEACC0F386B61EF19558FD8DE57EE94728D** and both supplied executable hashes are restored exactly. Once the app is visibly idle without an open setup/picker, exact path/length/creation-time/hash and ordinary-path guards move only the owned fixture into recoverable evidence storage as `validation-fixture.bin`; it is absent from the supplied tool folder. No issue or export is produced by this successful Save.

External packet `C:/Users/rsmit/.codex/EasyEjectBuildTools/QuickShareSetupStop-20261008` retains `fixture-before.json`, `setup-before.json`, `setup-late-commit.json`, `setup-restored.json` and `native-late-stop.json`, observed **18:54:43.3004239Z**. Fixture hash **49BC20DF15E412A64472421E13FE86FF1C5165E18B2AFCCF160D4DC19FE68A14**, length **1,073,741,824 bytes**. Source/code and candidate remain unchanged; no software suite is rerun. Native actual cancellation and busy close remain open.
