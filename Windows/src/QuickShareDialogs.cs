using System;
using System.Drawing;
using System.Windows.Forms;

namespace EasyEject {
    internal static class QuickShareProfile {
        internal const string Hd = "1080p", Uhd = "4K", Standard = "standard", LunaILog = "luna-ilog";
        internal static void Validate(ImportProfile value) {
            if (value == null) throw new SafetyException("Quick Share profile is unavailable.");
            if (value.SharingPreset != null && value.SharingPreset != Hd && value.SharingPreset != Uhd || value.SharingColor != null && value.SharingColor != Standard && value.SharingColor != LunaILog)
                throw new SafetyException("Saved Quick Share choices are invalid. Review this device's profile.");
        }
        internal static bool Enabled(ImportProfile value) { Validate(value); return value.SharingPreset != null; }
        internal static QuickSharePreset Preset(ImportProfile value) { Validate(value); return value.SharingPreset == Uhd ? QuickSharePreset.Uhd : QuickSharePreset.Hd; }
        internal static QuickShareColor Color(ImportProfile value) { Validate(value); return value.SharingColor == Standard ? QuickShareColor.Standard : QuickShareColor.LunaILog; }
        internal static void ConfigureSizeChoice(ComboBox choice, ImportProfile profile) {
            choice.DropDownStyle = ComboBoxStyle.DropDownList; choice.Items.AddRange(new object[] { Hd, Uhd }); choice.Width = 170;
            choice.DrawMode = DrawMode.OwnerDrawFixed;
            choice.DrawItem += delegate(object sender, DrawItemEventArgs e) {
                e.DrawBackground(); int index = e.Index < 0 ? choice.SelectedIndex : e.Index;
                if (index >= 0 && index < choice.Items.Count) TextRenderer.DrawText(e.Graphics, choice.Items[index].ToString(), e.Font, e.Bounds, e.ForeColor, TextFormatFlags.Left | TextFormatFlags.VerticalCenter);
                e.DrawFocusRectangle();
            };
            choice.SelectedIndex = Preset(profile) == QuickSharePreset.Uhd ? 1 : 0;
        }
    }
    // Editing preferences does not invoke the engine, tools or media filesystem.
    internal sealed class QuickShareControls : UserControl {
        internal readonly CheckBox Offer = new CheckBox(), Lut = new CheckBox();
        internal readonly ComboBox SizeChoice = new ComboBox();
        readonly FlowLayoutPanel choices = new FlowLayoutPanel();
        readonly Label explanation = new Label();
        internal QuickShareControls(ImportProfile profile) {
            QuickShareProfile.Validate(profile); AutoSize = true; Dock = DockStyle.Top;
            var layout = new FlowLayoutPanel { AutoSize = true, Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown, WrapContents = false };
            Offer.Text = "Offer manual Quick Share for this device"; Offer.AutoSize = true; Offer.Margin = new Padding(0, 8, 0, 8);
            choices.AutoSize = true; choices.FlowDirection = FlowDirection.TopDown; choices.WrapContents = false;
            choices.Controls.Add(new Label { AutoSize = true, Text = "Preferred size (asked again for every export)" });
            QuickShareProfile.ConfigureSizeChoice(SizeChoice, profile); choices.Controls.Add(SizeChoice);
            Lut.Text = "Prefer official Luna I-Log LUT in the export prompt"; Lut.AutoSize = true; Lut.Checked = QuickShareProfile.Color(profile) == QuickShareColor.LunaILog; choices.Controls.Add(Lut);
            explanation.AutoSize = true; explanation.Margin = new Padding(0, 8, 0, 0);
            explanation.Text = "Only supported Insta360 Luna VID MP4 recordings are eligible. Each export asks for 1080p/4K and LUT on/off. Keep the LUT off for normal-color footage.\n\nThis choice belongs only to this device, independently of import defaults. Enabling it offers saved verified batches, including earlier imports after the source is ejected. Disabling it hides sharing actions. Saving or importing starts no export; Quick Share never ejects. Full-quality imported originals remain unchanged. FFmpeg, FFprobe and the official LUT must be supplied separately.";
            layout.Controls.Add(Offer); layout.Controls.Add(choices); layout.Controls.Add(explanation); Controls.Add(layout);
            Offer.CheckedChanged += delegate { choices.Enabled = Offer.Checked; }; Offer.Checked = QuickShareProfile.Enabled(profile); choices.Enabled = Offer.Checked;
            SetTextWidth(580);
        }
        internal void SaveTo(ImportProfile draft) {
            draft.SharingPreset = Offer.Checked ? (SizeChoice.SelectedIndex == 1 ? QuickShareProfile.Uhd : QuickShareProfile.Hd) : null;
            draft.SharingColor = Lut.Checked ? QuickShareProfile.LunaILog : QuickShareProfile.Standard; QuickShareProfile.Validate(draft);
        }
        internal void SetTextWidth(int width) { explanation.MaximumSize = new Size(Math.Max(250, width - 12), 0); }
    }
    // A fresh dialog is required for every action, even when a device has saved preferences.
    internal sealed class QuickSharePromptDialog : Form {
        internal readonly ComboBox SizeChoice = new ComboBox();
        internal readonly CheckBox Lut = new CheckBox();
        internal readonly Button CancelExport = new Button(), Export = new Button();
        internal QuickSharePreset Preset { get { return SizeChoice.SelectedIndex == 1 ? QuickSharePreset.Uhd : QuickSharePreset.Hd; } }
        internal QuickShareColor ColorChoice { get { return Lut.Checked ? QuickShareColor.LunaILog : QuickShareColor.Standard; } }
        internal QuickSharePromptDialog(string deviceName, string folder, ImportProfile profile) {
            if (!QuickShareProfile.Enabled(profile)) throw new SafetyException("Quick Share is not enabled for this device.");
            Text = "Create sharing copies"; Font = new Font("Segoe UI", 10); AutoScaleMode = AutoScaleMode.Dpi; ClientSize = new Size(680, 480); MinimumSize = new Size(620, 440); StartPosition = FormStartPosition.CenterParent;
            var body = new Panel { Dock = DockStyle.Fill, AutoScroll = true, Padding = new Padding(20) };
            var content = new FlowLayoutPanel { Dock = DockStyle.Top, AutoSize = true, FlowDirection = FlowDirection.TopDown, WrapContents = false };
            var intro = new Label { AutoSize = true, Text = deviceName + "\n\nCreate sharing copies from the exact verified imported batch:\n" + folder, Margin = new Padding(0, 0, 0, 16) };
            content.Controls.Add(intro); content.Controls.Add(new Label { AutoSize = true, Text = "Choose size for this export" });
            QuickShareProfile.ConfigureSizeChoice(SizeChoice, profile); content.Controls.Add(SizeChoice);
            Lut.AutoSize = true; Lut.Text = "Apply official Luna I-Log LUT"; Lut.Checked = QuickShareProfile.Color(profile) == QuickShareColor.LunaILog; Lut.Margin = new Padding(0, 16, 0, 8); content.Controls.Add(Lut);
            var explanation = new Label { AutoSize = true, Text = "Use the LUT only for footage recorded in Luna I-Log. Leave it off for normal-color footage; BT.709 tags cannot determine the recording mode.\n\nLandscape/portrait framing and audio are preserved without enlarging the video; output is at most 30 fps. Approximate sizes: 60 MB/min at 1080p, 180 MB/min at 4K.\n\nCopies are validated and saved under Sharing Copies. Existing verified exports can be reused. Full-quality imported originals stay unchanged. This action never ejects the device.", Margin = new Padding(0, 8, 0, 0) }; content.Controls.Add(explanation);
            body.Controls.Add(content); Controls.Add(body);
            var footer = new FlowLayoutPanel { Dock = DockStyle.Bottom, AutoSize = true, FlowDirection = FlowDirection.RightToLeft, Padding = new Padding(16) };
            Export.Text = "Create sharing copies"; Export.AutoSize = true; Export.MinimumSize = new Size(180, 36); Export.DialogResult = DialogResult.OK;
            CancelExport.Text = "Cancel"; CancelExport.AutoSize = true; CancelExport.MinimumSize = new Size(100, 36); CancelExport.DialogResult = DialogResult.Cancel;
            footer.Controls.Add(Export); footer.Controls.Add(CancelExport); Controls.Add(footer); AcceptButton = CancelExport; CancelButton = CancelExport;
            body.SizeChanged += delegate { int width = Math.Max(300, body.ClientSize.Width - 60); intro.MaximumSize = explanation.MaximumSize = new Size(width, 0); };
            intro.MaximumSize = explanation.MaximumSize = new Size(560, 0);
        }
    }
}
