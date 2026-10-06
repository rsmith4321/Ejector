using System;
using System.IO;
using System.Linq;
using System.Windows.Forms;

namespace EasyEject {
    public static partial class Tests {
        static void QuickShareDialogTests() {
            Test("Quick Share new and truly legacy profiles keep sharing off with original import choices", delegate { using (var f = new Fixture()) {
                var state = State(); var p = new ImportDefaults().NewProfile(state.Volumes[0], state.Disks[0]); p.Destination = f.Destination;
                Check(!QuickShareProfile.Enabled(p) && QuickShareProfile.Preset(p) == QuickSharePreset.Hd && QuickShareProfile.Color(p) == QuickShareColor.LunaILog, "New offer/default changed");
                p.UsesImportDefaults = p.VideosOnly = p.IncludePreviews = p.CleanLayout = null; Profiles.Save(p, f.Destination);
                string path = Path.Combine(f.Destination, Profiles.FileName(p.VolumeId));
                string legacy = System.Text.RegularExpressions.Regex.Replace(File.ReadAllText(path), "\"Sharing(Preset|Color)\":null,", ""); File.WriteAllText(path, legacy);
                Check(!File.ReadAllText(path).Contains("SharingPreset") && !File.ReadAllText(path).Contains("SharingColor"), "Fixture still has new fields");
                var loaded = Profiles.Load(state.Volumes[0], state.Disks[0], f.Destination);
                Check(!QuickShareProfile.Enabled(loaded) && QuickShareProfile.Color(loaded) == QuickShareColor.LunaILog && !loaded.Resolve(new ImportDefaults()).VideosOnly && loaded.Resolve(new ImportDefaults()).IncludePreviews, "Migration enabled sharing or changed legacy imports");
            } });
            Test("Quick Share per-device preferences survive connected and offline persistence without shared defaults", delegate { using (var f = new Fixture()) {
                var p = OfflineProfile(); p.SharingPreset = QuickShareProfile.Uhd; p.SharingColor = QuickShareProfile.Standard; Profiles.Save(p, f.Destination);
                var entry = Profiles.Catalog(f.Destination).Single(); var draft = entry.Profile;
                draft.Name = "Renamed sharing device"; Profiles.SaveOffline(entry, draft, f.Destination);
                var loaded = Profiles.Load(State().Volumes[0], State().Disks[0], f.Destination);
                Check(QuickShareProfile.Enabled(loaded) && QuickShareProfile.Preset(loaded) == QuickSharePreset.Uhd && QuickShareProfile.Color(loaded) == QuickShareColor.Standard, "Per-device settings lost");
                Check(!QuickShareProfile.Enabled(new ImportDefaults { VideosOnly = false, AutomaticImport = true }.NewProfile(State().Volumes[0], State().Disks[0])), "Other profiles inherited sharing");
                loaded.SetCustom(true, new ImportDefaults()); loaded.SetCustom(false, new ImportDefaults { AutomaticImport = true });
                Check(loaded.SharingPreset == QuickShareProfile.Uhd && loaded.SharingColor == QuickShareProfile.Standard, "Import mode switch overwrote sharing");
            } });
            Test("Quick Share invalid saved choices refuse connected save load and offline catalog", delegate { using (var f = new Fixture()) {
                foreach (var pair in new[] { new[] { "720p", QuickShareProfile.Standard }, new[] { "", QuickShareProfile.Standard }, new[] { QuickShareProfile.Hd, "auto" }, new[] { (string)null, "" } }) {
                    var p = OfflineProfile(); p.SharingPreset = pair[0]; p.SharingColor = pair[1];
                    Throws<SafetyException>(() => Profiles.Save(p, f.Destination));
                    Profiles.WriteJson(p, typeof(ImportProfile), f.Destination, Profiles.FileName(p.VolumeId));
                    Throws<SafetyException>(() => Profiles.Load(State().Volumes[0], State().Disks[0], f.Destination));
                    Check(!Profiles.Catalog(f.Destination).Single().CanEdit, "Invalid record offered editing/export");
                }
            } });
            Test("Quick Share invalid offline choices preserve the last valid profile", delegate { using (var f = new Fixture()) {
                Profiles.Save(OfflineProfile(), f.Destination); var saved = Profiles.Catalog(f.Destination).Single(); var draft = saved.Profile;
                draft.SharingPreset = "anything"; Throws<SafetyException>(() => Profiles.SaveOffline(saved, draft, f.Destination));
                Check(Profiles.Catalog(f.Destination).Single().Hash == saved.Hash, "Rejected choice modified saved profile");
            } });
            Test("Quick Share profile controls remain editable under defaults and preserve every manual choice across modes", delegate { using (var f = new Fixture()) {
                var p = OfflineProfile(); p.Destination = f.Destination;
                using (var dialog = new ImportProfileDialog(p, null, p.Name, new ImportDefaults(), true)) {
                    Check(!dialog.Sharing.Offer.Checked && dialog.Sharing.Enabled && !dialog.Selection.Enabled && !dialog.Sharing.SizeChoice.Enabled, "Defaults disabled device offer or enabled size without offer");
                    dialog.Sharing.Offer.Checked = true; dialog.Sharing.SizeChoice.SelectedIndex = 1; dialog.Sharing.Lut.Checked = false;
                    dialog.UseCustom.Checked = true; dialog.UseDefaults.Checked = true;
                    Check(dialog.Sharing.Offer.Checked && dialog.Sharing.SizeChoice.Enabled && dialog.Sharing.SizeChoice.SelectedIndex == 1 && !dialog.Sharing.Lut.Checked, "Mode switch changed manual preferences");
                    dialog.ApplyEdits();
                    Check(dialog.Draft.SharingPreset == QuickShareProfile.Uhd && dialog.Draft.SharingColor == QuickShareProfile.Standard && dialog.Draft.FollowsDefaults && !QuickShareProfile.Enabled(p), "Apply mutated original or lost independent choices");
                }
                Check(!QuickShareProfile.Enabled(p), "Editor affected original profile");
            } });
            Test("Quick Share cancelled profile edits do not enable or mutate saved preferences", delegate { using (var f = new Fixture()) {
                var p = OfflineProfile(); Profiles.Save(p, f.Destination); var saved = Profiles.Catalog(f.Destination).Single();
                using (var dialog = new ImportProfileDialog(saved.Profile, null, p.Name, new ImportDefaults(), true)) {
                    dialog.Sharing.Offer.Checked = true; dialog.Sharing.SizeChoice.SelectedIndex = 1; dialog.Sharing.Lut.Checked = false;
                    dialog.UseCustom.Checked = true; dialog.DialogResult = DialogResult.Cancel;
                }
                Check(Profiles.Catalog(f.Destination).Single().Hash == saved.Hash && !QuickShareProfile.Enabled(Profiles.Load(State().Volumes[0], State().Disks[0], f.Destination)), "Cancel persisted sharing");
            } });
            Test("Quick Share disabling offer hides eligibility but retains color preference for deliberate reenabling", delegate {
                var p = OfflineProfile(); p.SharingPreset = QuickShareProfile.Uhd; p.SharingColor = QuickShareProfile.Standard;
                using (var controls = new QuickShareControls(p)) {
                    controls.Offer.Checked = false; controls.SaveTo(p);
                    Check(!QuickShareProfile.Enabled(p) && !controls.SizeChoice.Enabled && p.SharingColor == QuickShareProfile.Standard, "Disabled profile remains eligible or reset color");
                }
                using (var controls = new QuickShareControls(p)) {
                    Check(!controls.Offer.Checked && controls.SizeChoice.SelectedIndex == 0 && !controls.Lut.Checked, "Reopening disabled profile changed defaults");
                    controls.Offer.Checked = true; controls.SaveTo(p); Check(p.SharingPreset == QuickShareProfile.Hd && QuickShareProfile.Color(p) == QuickShareColor.Standard, "Reenable changed color");
                }
            });
            Test("Quick Share export prompt requires current opt-in and defaults Enter Escape to Cancel", delegate {
                var p = OfflineProfile(); Throws<SafetyException>(() => new QuickSharePromptDialog(p.Name, @"Z:\saved-batch", p));
                p.SharingPreset = QuickShareProfile.Hd;
                using (var dialog = new QuickSharePromptDialog(p.Name, @"Z:\saved-batch", p)) {
                    Check(dialog.AcceptButton == dialog.CancelExport && dialog.CancelButton == dialog.CancelExport && dialog.CancelExport.DialogResult == DialogResult.Cancel && dialog.Export.DialogResult == DialogResult.OK, "Keyboard can silently start export");
                    Check(dialog.Preset == QuickSharePreset.Hd && dialog.ColorChoice == QuickShareColor.LunaILog && dialog.DialogResult == DialogResult.None, "Preferences or unopened choice changed");
                }
            });
            Test("Quick Share fresh prompts isolate explicit size LUT choices from device preferences", delegate {
                var p = OfflineProfile(); p.SharingPreset = QuickShareProfile.Uhd; p.SharingColor = QuickShareProfile.Standard;
                using (var dialog = new QuickSharePromptDialog(p.Name, @"Z:\saved-batch", p)) {
                    Check(dialog.Preset == QuickSharePreset.Uhd && dialog.ColorChoice == QuickShareColor.Standard, "Saved preference not displayed");
                    dialog.SizeChoice.SelectedIndex = 0; dialog.Lut.Checked = true; dialog.DialogResult = DialogResult.Cancel;
                    Check(p.SharingPreset == QuickShareProfile.Uhd && p.SharingColor == QuickShareProfile.Standard, "Prompt mutated profile");
                }
                using (var again = new QuickSharePromptDialog(p.Name, @"Z:\saved-batch", p)) Check(again.Preset == QuickSharePreset.Uhd && again.ColorChoice == QuickShareColor.Standard && again.AcceptButton == again.CancelExport, "Next action retained cancelled override");
            });
        }
    }
}
