using System;
using System.Drawing;
using System.Windows.Forms;

namespace EasyEject {
    internal static class RemovalResumeChoice {
        internal static string Explanation(RemovalResumeEvidence review, string device, string source, string destination) {
            string action = review.RemainingOriginals + review.RemainingTrashFiles + review.RemainingTrashRecords == 0 ? "All scheduled sources are already absent. This action re-verifies and records the completed batch.\n\n" :
                "THIS ACTION WILL PERMANENTLY REMOVE THE REMAINING VERIFIED SOURCES:\n" + review.RemainingOriginals + " ordinary originals, " + review.RemainingTrashFiles + " Trash files, and " + review.RemainingTrashRecords + " Windows restoration records. " + review.PendingWindowsRecords.Count + " of those records belong to payloads already absent.\n\n";
            return "Continue this interrupted verified batch on " + device + "?\n\nSource:\n" + source + "\n\nSaved batch:\n" + review.BatchFolder + "\n\nDestination:\n" + destination + "\n\n" +
                (review.Options.VideosOnly ? "Videos only" : "All media") + "; " + (review.Options.IncludePreviews ? "camera previews included." : "optional previews excluded.") + "\n" + (review.Options.RecoverTrash ? DeviceTrashChoice.Scope(review.Options.MacTrashUid) : "Device Trash recovery is off; Trash stays on the device.") + "\n\n" +
                "Already absent before this action: " + review.AbsentSources.Count + " recorded source files/records. Complete saved copies have been verified.\n\n" +
                action +
                "The choices are frozen from the interrupted batch. Ordinary originals are " + (review.Options.DeleteOriginals ? "removed only when recognized and verified." : "kept.") + " Unknown files, excluded photos/previews, device indexes, other owners and retained folder records remain. Removal skips Trash recovery. Keep independent backups.\n\n" +
                "The exact journal, owner, devices, saved copies, remaining sources and selected manifests will be checked again before any clearing. Changed or unexplained data stops the action. A fresh receipt and exact batch history are saved only after completion. No export or automatic eject starts; the device remains connected.";
        }
        internal static string Summary(ImportResult result) {
            return "This resume action permanently removed " + result.RemovedOriginals + " verified ordinary originals and cleared " + result.ClearedTrashFiles + " Trash files and " + result.ClearedTrashRecords + " restoration records.\nAlready absent before this action: " + result.PriorAbsentOriginals + " ordinary originals, " + result.PriorAbsentTrashFiles + " Trash files and " + result.PriorAbsentTrashRecords + " records. Complete saved copies are kept.";
        }
    }
    internal sealed class RemovalResumeConfirmationDialog : Form {
        internal readonly Button Cancel = new Button(), Resume = new Button(); internal readonly Label Explanation = new Label();
        internal RemovalResumeConfirmationDialog(string text) {
            Text = "Resume interrupted verified import?"; Font = new Font("Segoe UI", 10); AutoScaleMode = AutoScaleMode.Dpi;
            Size = new Size(720, 580); MinimumSize = new Size(650, 440); StartPosition = FormStartPosition.CenterParent; ShowInTaskbar = false;
            var body = new Panel { Dock = DockStyle.Fill, AutoScroll = true, Padding = new Padding(24) };
            Explanation.AutoSize = true; Explanation.MaximumSize = new Size(610, 0); Explanation.Location = new Point(24, 24); Explanation.Text = text;
            body.Controls.Add(Explanation); body.SizeChanged += delegate { Explanation.MaximumSize = new Size(Math.Max(250, body.ClientSize.Width - 60), 0); };
            Cancel.Text = "Cancel"; Cancel.DialogResult = DialogResult.Cancel; Resume.Text = "Resume verified batch"; Resume.DialogResult = DialogResult.Yes;
            var footer = new FlowLayoutPanel { Dock = DockStyle.Bottom, AutoSize = true, FlowDirection = FlowDirection.RightToLeft, Padding = new Padding(16) };
            foreach (var button in new[] { Cancel, Resume }) { button.AutoSize = true; button.MinimumSize = new Size(110, 36); footer.Controls.Add(button); }
            Controls.Add(body); Controls.Add(footer); AcceptButton = Cancel; CancelButton = Cancel;
        }
    }
}
