# Native manual resolution and unavailable-LUT checks · October 11, 2026

The existing generated UHD batch now passes an explicit native 1080p override of its saved 4K preference, independent output probe/full decode and separate resolution-specific exact reuse. Requesting the unavailable official Luna LUT refuses before destination writes. These checks preserve originals, existing 4K outputs and saved choices. Positive official LUT and genuine camera/color acceptance remain open.

## Scope and exact inputs

The fixture is the same two generated 15-second 3840×2160/30fps H.264 clips with synthetic audio from [the native Stop/retry checkpoint](UHD_CANCEL_RETRY_20261011.md), under `C:\Users\rsmit\.codex\EasyEjectBuildTools\Codex020-native-uhd-fixture-20261011`. Synthetic Luna metadata tests selection only; it does not identify real footage or camera recording mode. Its `source` folder remains absent; the two exact imported originals and production import receipt remain recorded in history.

PID **29068** used the unchanged engineering host at `C:\Users\rsmit\.codex\EasyEjectBuildTools\Codex020-ui-layout-20261011\bin\EasyEject.Modern\Debug\net10.0-windows10.0.26100.0\win-x64\EasyEject.Modern.exe`, with `C:\Users\rsmit\.codex\EasyEjectFixtures\ui-uhd-review-20261011 review-restart`. The exclusive review owner lock was checked before launch. Current loaded UI/backend hashes remain **46F2E9E5C1A522F8100F481CCFB6956E34D82882B8A315D3929FF1A689DF387B** / **6F38BE334769E31A918D23CACB1E8E5CDA04D40AEE087FC6AB27C7ED9181359F**. Normal distributable/live preferences were not selected.

Before input, all **11** destination files and **3** pre-existing configuration files were recorded with hash, size and UTC write time. Existing supplied FFmpeg/FFprobe hashes were independently rechecked. Tool configuration has `Lut=null`; no tool or LUT was installed. The exact baseline Mac `384cb767ffbbb87ec5fe67f45d0b4d0ce324ad7c:Ejector/EasyShare.swift` and Windows `QuickShareRecipe.cs` both specify the 1920×1080 bound, Lanczos scaling and stereo AAC recipe.

## Native observations and independent checks

1. History selected only the retained two-file generated batch. Quick Share opened with **4K**, LUT off and Cancel as default. Explicitly checking **Apply official Insta360 Luna I-Log LUT** and choosing Create sharing copies stopped with: **Choose the official Luna I-Log LUT in Quick Share tools or leave the LUT off for this export.** The UI required a manual retry. All 11 destination files retained exact hashes, bytes and write times; zero files were added. No owned FFmpeg worker remained at the terminal check.
2. A new explicit history action again offered saved **4K / LUT off**, so the failed request had not changed the saved preference. Computer control selected **1080p · smaller files**, left LUT off and explicitly created copies. Active native progress showed **50%** and Stop export; read-only process enumeration bound FFmpeg PID22240 to parent29068. The terminal result reported **2 1080p copies ready (2 new; 0 reused)**. No device was ejected.
3. Independent file checks preserved all 11 prior files and all three pre-existing configuration files. Exactly five destination files were added: two 1080p MP4s, their two exclusive receipts and one successful audit. No private work folder or owned worker remained.
4. Both outputs independently probed and fully decoded with exit0: **1920×1080, 30fps, H.264/yuv420p/BT.709, 15 seconds, 48kHz stereo AAC**. Each is **15,304,281 bytes**, SHA-256 **AE0B1D22582D236940E5D03F66E3F2AFFAC81B766548EAA3A0504B461260B63B**. Both receipts independently matched preset `1080p`, `Standard`, no LUT hash, dimensions, byte count and output hash.
5. Closing the result and opening a separate manual action again showed **4K / LUT off**. Explicitly selecting 1080p and Create reported **0 new / 2 reused**, choosing the matching 1080p recipe rather than the existing 4K variant. All **16** prior destination files retained hashes, bytes and write times. Only a new audit was added, ending `2026-10-11T16:18:36.2812319Z Completed manual Quick Share | new 0 | reused 2 | skipped MP4 0`. The three saved configuration files remained exact.
6. Close result → actual tray callback Quit exited PID29068 normally. Final census found no owned review process, FFmpeg worker or private work folder. No process was killed. Inventory during this run showed only the internal disk; the camera connection was absent. This is an external-state observation, not evidence that Easy Eject removed hardware.

Supported Computer Use performed all input. Immediate accessibility captures sometimes retained the previous frame; a fresh observation confirmed each settled result. One summary projection displayed nulls when selecting dictionary keys; reading the retained JSON confirmed the five exact additions. No failed application behavior or false passing check is inferred from that presentation artifact.

## Retained evidence

| File under the generated fixture | SHA-256 |
| --- | --- |
| `before-native-1080-files.json` | `0780A1ACB01B1E402AC881CD00A246FF94376CA93681F47531ED7E05EF06396B` |
| `after-native-missing-lut.json` | `CE95509FF85C2E2454DC59B3DCAB494F6BA1D5789F74523BECAD6013FCE2962C` |
| `after-native-1080-files.json` | `189A07CDB66F0DF189CA5BDADE740C9A09EE555900CBF493CBEAB299014B56F1` |
| `independent-native-1080-validation.json` | `9A7E20EACDCBAA83FC84AE06C12E68CC4D5988342918D4B141D1C6242BDC5CC8` |
| `after-native-1080-reuse.json` | `6FE16AFB57D166305577CC82595527313E69537E30DC1EF5DB13FA2A3A16B566` |
| `native-choices-terminal.json` | `E5D39D3D8B15450A3256450E6441114108C5C4A7D9A2E6C7AA68C4988BE9DA18` |

`native-1080-workers.json` and `native-choices-module-pins.json` retain process/module provenance. No production source or payload change follows; prior default counts remain **1454/0** each runtime and **246/0** selected modern. Generated manual 1080p/4K choice/reuse and unavailable-LUT refusal have native evidence; genuine footage, positive official LUT, physical removal/destination replacement/power/travel, OS tray/notifications, all display scales/themes and trusted distribution still require acceptance. The full goal remains active. The prepared 0.2 ZIP still awaits Ryan's publishing OK.
