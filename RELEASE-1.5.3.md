# Website 1.5.3: shared features without per-card sandbox grants

Ryan requested restoring the website build after clarifying the sandbox's effect on folder-based CFexpress classification. This supersedes Store-only retirement. Both targets compile the same app code; only access/distribution and the direct welcome screen differ.

The direct target is not sandboxed and can inspect camera folders without Easy Eject's Authorize a Card flow, including after camera formatting. Ordinary macOS Files and Folders restrictions still apply. Readers that identify their card type in hardware can classify without folder access in Store; generic PCI/NVMe readers may need DCIM or other camera folders. Plain eject works in both, including from Other External Volumes. Import profiles remain UUID-based in both and may need enrollment again after formatting.

Changes from website 1.5.2: latest shared native menu/ejection coordination and metadata cleaner, optional post-import eject off for new profiles, compact dismissible issues, clearer import identity guidance, and direct permission help/usage descriptions. Originals remain kept by default, original deletion requires explicit confirmation and verified copies, and existing choices are retained. No production profile migration or real media cleanup is part of this release.

Distribution uses existing direct bundle com.ryansmithphotography.Ejector, version1.5.3. Store1.0(5) remains a separate signed/submitted binary and is not resubmitted for direct-only changes. Verification and release receipts live at the canonical workspace under release-evidence/website-release-1.5.3.

Verification: universal Intel/Apple Silicon archive with macOS 14 minimum; Developer ID signature and hardened runtime, no sandbox entitlement; app and DMG notarization Accepted, stapled, strict signature and Gatekeeper checks passed. Fourteen engine/cleanup tests passed. The signed isolated preview classified a generated camera volume before and after replacement with a same-name/different-UUID volume, without an authorization menu. Physical CFexpress formatting has not been exercised. Production profiles were not modified.

Final DMG SHA-256: `1ed85bb760bb88f427aa7422c56d1f8852e130c29a57251fc934026f27c8cac8`.
