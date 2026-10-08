# Combined local review · October 8, 2026

The complete committed Windows tree **0dcda0c6874ac24730adc7b4936710fce43ab57d** is now prepared and tested together, including modern release package/session integration **4508c61**, Quick Share setup lifetime **3b8adc0**, and damaged-frame real-tool acceptance **3b9b1cd**. No dirty overlays or prior review folders are incorporated or overwritten.

The new **unsigned development** review is `%USERPROFILE%/.codex/EasyEjectBuildTools/IntegratedReview-20261008/app`. Its 601 files total **284,325,866 bytes**, include the compiled `EasyEject.Modern.pri`, and bundle Core/Desktop **.NET10.0.12** plus Windows App SDK. The app was **not launched**. `README-REVIEW.txt` explains normal user configuration and version switching; this folder is not a sandbox or approved distribution.

## Exact candidate verification

The separate selected and full self-contained runners were linked from the same source tree. Their initially built backend DLLs differed from the app's DLL. Only the two **owned test-runner** copies were replaced with the app's exact backend before execution; the candidate folder remained untouched. All three copies then match **AA22DBB09C6F6B6787B5E3E9A3D89A15C480070C10C4F02C6A74E1A7DF72C08A**.

- WinUI app and both runners compile with warnings as errors and no reported warning/error.
- Selected contracts: **236/0**.
- Full default suite: **1340/0**, explicitly reporting no real device eject API used.
- Explicit actual-tool generated-clip rehearsal: **14/0**, including separate-process manual choices/reuse, partial cancellation/retry and real damaged-frame decode refusal. Official positive LUT tests remain **not run**.
- Software preparation/selected/full terminal **24143 exit zero**; durable result **passed**, **16:32:29Z–16:39:41Z**. Actual-tool terminal **91292 exit zero**. Final owned candidate/test process count **zero**.
- Actual default parent **PID16660**, started **12:33:19 EDT**, loads its owned bundled `full/coreclr.dll`, SHA **128AEE8C62A673D64739E585E3876B61133571CEC83A46240A62581D5465639B**, matching the candidate's CLR. This is default-test process evidence; no new WinUI or separate actual-tool module capture is claimed.
- All 601 candidate lengths/hashes and the file count match the preparation manifest after default tests. Counts overlap earlier checkpoints and must not be summed.

Candidate managed app SHA **0D5057B502D194BB37CA64870F90EC84467A69D549ADCA6C52561C26CA67823F**; native host **786AD10735F65BBC544B76E1F6EA5DC6E1E8D34153EDA6165B048B245C434E2E**; app PRI **FADDBC964E528275BC521B9A86F7443E081607D13FCD599E454BC6BE8E0477BD**. The local manifest records every payload hash; it is not a signature.

Static inspection reports **357 PE files/1500 import references/zero ordinary names absent locally**. This checks names, not actual loader behavior, complete component/catalog identity, publisher trust or clean-computer acceptance.

## Reproduction and evidence

Source archive `integrated-source-20261008.zip`, snapshot `IntegratedSource-20261008`, review `IntegratedReview-20261008` and verifier `IntegratedVerify-20261008` are under `%USERPROFILE%/.codex/EasyEjectBuildTools`. Archive SHA **AB49D06D5E5C2DEC2F052910C98F38AE87AE3D3F39BAC94B7259E12DBD04CBA4**; Windows tree **223de5d7ffe0ea0d622d9dfba2bd66ffb66b8fa8**. The retained helper requires fresh directories and the already installed pinned SDK; running it does not launch or install the app.

```powershell
& C:/Users/rsmit/.codex/EasyEjectBuildTools/verify-integrated-review-20261008.ps1
& C:/Users/rsmit/.codex/EasyEjectBuildTools/IntegratedVerify-20261008/full/EasyEject.FullContracts.exe --share-real-tools <existing-ffmpeg.exe> <existing-ffprobe.exe>
```

The explicit actual run used the previously supplied tools in `Documents/Gyroflow-DJI-LUT/ext/ffmpeg-n9.0.2-22-g46d8f462ee-win64-gpl-shared-9.0/bin`: FFmpeg SHA **6C03ED874BF9C219D3BA865339B48A106EEF11FAC07E8E53BB599AC59214469D**, FFprobe **FB41BE6F4386DF4625D13EEC932EC926BE4744853759C7C100947EDC7B917096**. Generated owned clips only; no new tool/LUT installation or real media.

Verifier evidence: `result.json`, selected/full compile and test logs, `selected-backend-binding.json`, `full-backend-binding.json`, `full-runtime-observed.json`, `actual-tools.log`, `actual-tools-result.json`, and Framework fixture compile logs. Review evidence: `build.log`, `native-imports.json`, `review-build.json`. Preview evidence: `%USERPROFILE%/.codex/EasyEjectWindowsPreview/integrated-source-20261008.json`, `integrated-review-verification-20261008.log`, `integrated-terminal-evidence-20261008.json`.

## Remaining full-port gates

This is the combined **software** checkpoint. New native startup/module/foreground/tray/dialog/picker/nonempty workflow, notifications/hotkey/lifetime/theme/DPI and cold/silent/busy restart acceptance remain open. Earlier foreground observations used a different candidate. Supported desktop input remains denied pending human recovery; the older PID8124 stays separate from this unlaunched review.

Complete component/catalog review, approved publisher/endpoints and trusted direct distribution, clean-computer behavior, supplied official LUT/genuine camera footage, and explicitly identified disposable device/filesystem/travel acceptance remain required. Both production channels are unconfigured. Nothing signs, publishes, installs startup, changes system security/power/display or acts on real devices/originals. Mac and website files remain outside this Windows-only work. Full finish goal active; expired overnight heartbeat paused.
