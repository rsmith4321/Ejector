# Easy Eject icon and foreground checkpoint · October 7

Source `4a865b6` adds Windows artwork derived from the existing white glyph in
`Ejector/AppIcon.icon/Assets/Eject.svg` and its red app background. The Mac assets
are unchanged. Windows/assets contains a reproducible local PowerShell/System.Drawing
generator, a transparent PNG and a nine-size ICO (16 through 256 pixels). No tools
or artwork were downloaded. The native executable, window title, app header and
tray use this icon. Busy and attention badges retain the Easy Eject glyph; owned
icon handles/resources are disposed on exit. Classic builds and test fixtures
embed the same resource without adding external package files.

Actual foreground observation on this 150% display exposed an existing sizing
bug: 1120 by 760 was treated as physical pixels rather than XAML effective pixels,
clipping the third import action. Initial sizing now uses the window DPI and caps
dimensions to the current working area. Action rows wrap at narrower widths.
The new initial-size screenshot shows every complete action label. Interactive
drag attempts did not change the size, so narrower-window wrapping and other
monitor/theme/scale combinations remain separate acceptance checks. See Microsoft's
[window coordinate guidance](https://learn.microsoft.com/en-us/windows/apps/develop/ui/windowing-overview).

Software evidence (builds/logs outside OneDrive):

- `modern-icon-verified.log`: app/runtime/selected builds, zero warnings/errors,
  **124 passed / 0 failed**.
- `modern-icon-full.log`: modern full-suite and owned unsigned Framework fixture
  builds, zero warnings/errors, **1214 passed / 0 failed**, terminal 95603 exit 0.
  The initial fixture compiler failure is retained in `modern-icon-full-initial.log`;
  renaming one unused lambda parameter in ResumeOperationReview fixes the older
  compiler's name-shadowing refusal without changing behavior.
- `modern-icon-native-final.log`: final owned-icon disposal source rebuild and
  **84 passed / 0 failed** hidden native controls/diagnostics/tray/review fixtures,
  terminal 59765 exit 0. The direct SDK call also initialized an unused untrusted
  ASP.NET development certificate. Its exact friendly name, subject, thumbprint
  and creation time matched this command; it was absent from trusted roots and
  only that generated certificate was removed. Subsequent builds use the isolated
  script environment that disables certificate initialization and telemetry.
- `modern-icon-commit-verified.log`: a separate archive of exact commit `4a865b6`,
  containing only its tracked Windows sources, builds app/runtime/selected tests
  with zero warnings/errors and **124 / 0**. This isolates the icon milestone from
  the other task's ongoing startup edits. Counts overlap and are not added.

Foreground checks on the own ModernIconPreview/ModernIconLayout windows:
the icon renders in the title/header; internal-only five-volume inventory blocks
device actions; empty history has disabled verification and Close default; empty
saved profiles disable edit/forget; Settings cleanup and defaults original-removal
warnings visibly default to Cancel and Escape returns with those choices off;
page Cancel discards drafts and returns to storage. No Save, import, cleanup,
eject, login registration or real-media operation was performed. The first own
idle preview was stopped after exact PID/path/payload checks to load the layout
build; this is engineering replacement, not tray Quit proof.

Full-port completion remains open. Device arrival/removal, copy/hash/reuse,
disconnect/cancel/reconnect/veto and manual eject require Ryan to identify a
disposable device. Actual tray menu/Quit/notifications, supplied Luna tools/LUT,
foreground integrated workflows, remaining modern integrations and trusted direct
distribution still need acceptance. The existing port task continues its startup
milestone; its dirty edits were preserved and excluded from this commit. Permission
to message that task is pending in the testing thread.
