using System;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace EasyEject {
    public static partial class Tests {
        [DllImport("user32.dll")] static extern bool IsProcessDPIAware();
        [DllImport("user32.dll")] static extern uint GetDpiForSystem();
        static void LoadDpiForm(Form form) {
            form.ShowInTaskbar = false; form.Opacity = 0; form.Show(); Application.DoEvents(); form.Hide();
        }
        static void CheckDpiSize(Size original, Size scaled, double factor, string explanation) {
            Check(Math.Abs(scaled.Width - original.Width * factor) <= 2 && Math.Abs(scaled.Height - original.Height * factor) <= 2, explanation + " | original=" + original + ", scaled=" + scaled + ", factor=" + factor);
        }
        static void DpiLayoutTests() {
            Test("native UI tests use the application's DPI awareness", () => {
                Check(IsProcessDPIAware(), "Test executable does not share the app's DPI-aware context");
                using (var form = new AppSettingsDialog(new AppSettings())) Check(form.CurrentAutoScaleDimensions.Width == GetDpiForSystem(), "Native UI dimensions disagree with system DPI");
                Console.WriteLine("Native system DPI=" + GetDpiForSystem() + "; app/test DPI contexts match.");
            });
            Test("settings scales its design viewport minimum and footer once at native DPI", () => {
                using (var form = new AppSettingsDialog(new AppSettings())) {
                    Size original = form.ClientSize, minimum = form.MinimumSize; float font = form.Font.Size; double factor = GetDpiForSystem() / 96.0;
                    var accept = form.AcceptButton; var cancelButton = form.CancelButton;
                    LoadDpiForm(form); CheckDpiSize(original, form.ClientSize, factor, "Settings viewport did not scale from its design basis"); CheckDpiSize(minimum, form.MinimumSize, factor, "Settings minimum did not scale");
                    Size scaled = form.Size; LoadDpiForm(form); Check(form.Size == scaled && form.Font.Size == font && form.AcceptButton == accept && form.CancelButton == cancelButton, "Repeated load changed scale, point size or defaults");
                    CheckFooterInside(form);
                }
            });
            Test("profile scales native layout while retaining draft source defaults and manual sharing", () => {
                var profile = OfflineProfile(); profile.SharingPreset = QuickShareProfile.Uhd; profile.SharingColor = QuickShareProfile.Standard;
                using (var form = new ImportProfileDialog(profile, null, profile.Name, new ImportDefaults(), true)) {
                    Size original = form.ClientSize; LoadDpiForm(form); CheckDpiSize(original, form.ClientSize, GetDpiForSystem() / 96.0, "Profile viewport did not scale from its design basis");
                    Check(form.SourceFolder.ReadOnly && form.DestinationFolder.ReadOnly && !form.ChooseSource.Enabled && form.Draft.SharingPreset == profile.SharingPreset && form.Draft.SharingColor == profile.SharingColor && form.Draft.Destination == profile.Destination, "Scaling changed disconnected profile/sharing choices"); CheckFooterInside(form);
                }
            });
            Test("native scaled device window keeps bounded results and reachable independent actions", () => {
                using (var form = new DeviceWindow(true)) {
                    Size original = form.ClientSize; var list = WindowControl<ListView>(form, "list"); int columnWidth = list.Columns[0].Width;
                    var status = WindowControl<TextBox>(form, "status"); LoadDpiForm(form);
                    // Startup read-only inventory owns the idle message. Inject the
                    // deterministic operation result after native load/event delivery.
                    status.Text = new string('x', 12000); form.PerformLayout();
                    double factor = GetDpiForSystem() / 96.0; CheckDpiSize(original, form.ClientSize, factor, "Device viewport did not scale");
                    Check(list.Height >= 100 * factor && status.Height <= 96 * factor + 2 && status.Text.Length == 12000 && status.ReadOnly, "Scaled status collapsed storage or lost full result | list=" + list.Height + ", status=" + status.Height + ", text=" + status.Text.Length + ", factor=" + factor);
                    Check(Math.Abs(list.Columns[0].Width - columnWidth * factor) <= 1, "Native storage columns did not retain logical widths");
                    var button = WindowControl<Button>(form, "cancel"); Point position = form.PointToClient(button.PointToScreen(Point.Empty)); Check(position.Y >= 0 && position.Y + button.Height <= form.ClientSize.Height, "Scaled cancel action is outside the window");
                }
            });
            Test("scaled removal recovery and import confirmations retain Cancel defaults and reachable explicit actions", () => {
                using (var removal = new OriginalRemovalConfirmationDialog("Generated device choices", true))
                using (var recovery = new DeviceTrashRecoveryConfirmationDialog("Generated selected Windows owner"))
                using (var import = new VerifiedImportConfirmationDialog("Generated reviewed import; originals retained.")) {
                    foreach (Form form in new Form[] { removal, recovery, import }) {
                        Size original = form.ClientSize; var cancel = form.CancelButton; LoadDpiForm(form);
                        CheckDpiSize(original, form.ClientSize, GetDpiForSystem() / 96.0, "Confirmation viewport did not scale");
                        Check(form.AcceptButton == cancel && form.CancelButton == cancel && ((Button)cancel).DialogResult == DialogResult.Cancel, "Scaling made a destructive action the keyboard default");
                        form.Size = form.MinimumSize; form.PerformLayout(); CheckFooterInside(form);
                    }
                }
            });
        }
        static void CheckFooterInside(Form form) {
            foreach (Control footer in form.Controls) if (footer is FlowLayoutPanel && footer.Dock == DockStyle.Bottom) foreach (Control button in footer.Controls) {
                Point position = form.PointToClient(button.PointToScreen(Point.Empty)); Check(position.X >= 0 && position.X + button.Width <= form.ClientSize.Width && position.Y >= 0 && position.Y + button.Height <= form.ClientSize.Height, "Scaled footer action is clipped: " + button.Text);
            }
        }
        static void DpiLayoutRender() {
            string output = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".codex", "EasyEjectWindowsPreview", "run-" + Guid.NewGuid().ToString("N").Substring(0, 16)); Directory.CreateDirectory(output);
            using (var form = new AppSettingsDialog(new AppSettings())) { LoadDpiForm(form); form.Size = form.MinimumSize; Render(form, Path.Combine(output, "dpi-settings-minimum.png")); }
            var profile = OfflineProfile(); profile.SharingPreset = QuickShareProfile.Uhd;
            using (var form = new ImportProfileDialog(profile, null, profile.Name, new ImportDefaults(), true)) { LoadDpiForm(form); form.Size = form.MinimumSize; Render(form, Path.Combine(output, "dpi-profile-minimum.png")); RenderAtBottom(form, Path.Combine(output, "dpi-profile-bottom.png")); }
            using (var form = new DeviceWindow(true)) { LoadDpiForm(form); form.Size = form.MinimumSize; form.PresentInventory(TrayState()); Render(form, Path.Combine(output, "dpi-storage-minimum.png")); }
            using (var form = new OriginalRemovalConfirmationDialog("Generated device choices", true)) { LoadDpiForm(form); form.Size = form.MinimumSize; Render(form, Path.Combine(output, "dpi-removal-minimum.png")); }
            using (var form = new DeviceTrashRecoveryConfirmationDialog("Generated selected Windows owner")) { LoadDpiForm(form); form.Size = form.MinimumSize; Render(form, Path.Combine(output, "dpi-recovery-minimum.png")); }
            Console.WriteLine("Native DPI=" + GetDpiForSystem() + "; hidden native renders: " + output);
        }
    }
}
