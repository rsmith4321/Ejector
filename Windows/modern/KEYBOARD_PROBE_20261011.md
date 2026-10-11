# Native Explorer-return Escape diagnostic · October 11, 2026

The earlier Settings/update run left an unresolved Escape-after-Explorer observation. A fresh isolated engineering build now passes both exact-folder return paths: Open settings folder and Open app folder each open the expected Explorer location, only the new Explorer window is closed, and Escape dismisses the still-open installation guide after app reactivation. A settled second observation confirms the dialog is absent and the normal device review is idle. This does not establish why the previous run failed or prove every focus path.

`UiTestHost.cs` now adds a passive review-only probe under `UI_TESTS`. It observes window activation and Escape routed events on the root and open popup children, including handled events. It never handles input and records no typed text, other keys, control contents or user paths. The trace has an exclusively created file in the already guarded isolated fixture, and its timer/writer stop on window close. Production dialog behavior is unchanged. Accessibility reported guide text focus after Explorer return, while received Escape events identified a Button; accessibility focus alone is therefore insufficient to diagnose the earlier case.

## Exact run

- Fresh fixture: `C:\Users\rsmit\.codex\EasyEjectFixtures\ui-keyboard-probe-20261011`.
- Engineering build/evidence: `C:\Users\rsmit\.codex\EasyEjectBuildTools\Codex020-keyboard-probe-20261011`.
- Loaded UI DLL: **503808 bytes**, SHA-256 `C9242435D24B81EDD469742BACCD1BE1CD719199A16F10641EDDD1DDC049CD5B`.
- Loaded runtime DLL: **1008128 bytes**, SHA-256 `11E109EC98842572ADCEFA63F1039BFC016E2F6DE1D51F8654A56CB62DCB2EF3`.
- Owned PID **29896** exited through the actual tray Quit callback after both dialog checks. No owned process remains.
- Settings Explorer **6358930**, app-folder Explorer **20055596**, both closed with Alt+F4. Preexisting Downloads Explorer **787338** retained.
- Settings return: deactivation `16:31:40.949594Z`, code activation `16:31:51.2608526Z`, PreviewEscape `16:31:58.3409926Z`, handled Escape `16:31:58.3456109Z`.
- App-folder return: deactivation `16:32:30.0940948Z`, code activation `16:32:37.328344Z`, PreviewEscape `16:32:44.6193407Z`, handled Escape `16:32:44.6221972Z`.
- Root and dialog observers produce two records per routed event. Independent JSON assertions confirm four PreviewEscape and four handled Escape records across the two inputs.

Trace `keyboard-probe-a906cc278a514ee2b068d17e2ae1f481.jsonl` SHA-256: `595FC4E5780BD53AF0629A60A4E10F3C15714BEBFC1D4F76D7C30629923FB50E`.
Evidence `native-keyboard-result.json` SHA-256: `2BD3CB0FA984480440D758ABA50F7DE6AAD09BE6BFAE0D32BC30A4E5172C4B8A`.
An attempted hash while the exclusive writer was open refused; the final hash was taken after normal Quit.

## Verification and limits

`pwsh -NoProfile -File Windows/modern/build-ui-tests.ps1 -BuildRoot C:/Users/rsmit/.codex/EasyEjectBuildTools/Codex020-keyboard-probe-20261011` built with **0 warnings / 0 errors** and passed actual tray Quit draft preservation, busy/editor refusal, idle Quit and Stop layout regression. Automated result retained at `C:\Users\rsmit\.codex\EasyEjectFixtures\ui-03fcefd7ef734c4fb5c21d5649c05ab5\result.txt`.

A separate normal build with `-p:EasyEjectUiTests=false -p:EasyEjectBuildRoot=C:/Users/rsmit/.codex/EasyEjectBuildTools/Codex020-normal-keyboard-exclusion-20261011` also built with **0 warnings / 0 errors**. Its UI assembly metadata contains neither `UiTestHost` nor `InitializeReviewKeyboardProbe`. The normal executable was not launched. No production payload/package or default-suite count changes follow.

Initial accessibility-only input reported unavailable coordinate geometry; fresh window selection, activation and screenshot recovered it once before input continued. Settled UI observations were used after dialog animation. This is supported native input evidence in one isolated scope, not an explanation of the older failure or a broad keyboard acceptance claim.

No camera was visible in this run: only internal disk/five volumes. No media, eject, format, cleanup, OS settings, login registration, installation or publication actions occurred. All physical/media/LUT/trust and explicit publication gates remain open. Full Windows port goal remains active.
