using System;
using System.IO;

namespace EasyEject {
    public static partial class Tests {
        static void CompletionPromptContractTests() {
            Test("saved eject-prompt opt-in survives hidden window and enabled notifications without broadening opt-out", delegate {
                foreach (bool visible in new[] { false, true }) foreach (bool notifications in new[] { true, false }) {
                    Check(CompletionPresentation.ShowChoice(true, visible, notifications), "Saved ask choice suppressed by window/notification state");
                    Check(!CompletionPresentation.ShowChoice(false, visible, notifications), "Opt-out unexpectedly opened an eject choice");
                }
            });
            Test("hidden notification-enabled selected and filtered imports retain frozen manual choice and unchanged originals", delegate {
                foreach (bool selected in new[] { true, false }) using (var f = new Fixture()) {
                    var state = State(); state.Volumes[0].Group = StorageGroup.Camera; state.Volumes[0].Mounts[0] = f.Source;
                    string settingsRoot = Path.Combine(f.Root, "completion-prompt-contract");
                    ImportDefaults.Save(new ImportDefaults { AskToEject = true }, settingsRoot);
                    AppSettings.Save(new AppSettings { ShowNotifications = true }, settingsRoot);
                    var profile = DefaultsContractProfile(f, state, ImportDefaults.Load(settingsRoot));
                    var options = profile.Resolve(ImportDefaults.Load(settingsRoot));
                    f.Put(@"DCIM\PHOTO.JPG", "retained generated photo");
                    if (selected) f.Put(@"DCIM\VIDEO.MP4", "retained generated video");
                    var origin = new ImportCompletionOrigin(state, profile.VolumeId, f.Source);
                    var result = DefaultsContractImport(f, profile, options);
                    var completion = new ImportCompletion(origin, profile.Name, result.Files == 0 ? f.Destination : result.Folder,
                        f.Destination, HistoryIdentity(), result.HasSelectedMedia, "Generated import complete; source retained.");
                    var registry = new ImportCompletionRegistry(); registry.Insert(completion); var fake = new FakeEject();
                    var eject = new CompletedEjectService(registry, new FakeDiscovery { State = state }, fake, new Operations(), _ => profile.VolumeId);
                    ImportDefaults.Save(new ImportDefaults { AskToEject = false }, settingsRoot);
                    Check(CompletionPresentation.ShowChoice(options.AskToEject, false, AppSettings.Load(settingsRoot).NotificationsEnabled), "Finished import lost its frozen saved ask choice");
                    Check(!CompletionPresentation.ShowChoice(profile.Resolve(ImportDefaults.Load(settingsRoot)).AskToEject, false, true), "Next import retained an obsolete ask choice");
                    using (var dialog = new ImportCompletionDialog(completion, true)) {
                        Check(dialog.AcceptButton == dialog.Keep && dialog.CancelButton == dialog.Keep && dialog.Keep.DialogResult == System.Windows.Forms.DialogResult.Cancel,
                            "Revealed review made Enter/Escape authorize eject");
                    }
                    Check(result.HasSelectedMedia == selected && result.Files == (selected ? 1 : 0) && result.RemovedOriginals == 0 && result.ClearedTrashFiles == 0,
                        "Generated selected/filtered completion changed import disposition");
                    Check(fake.Calls == 0 && registry.Get(completion.Id) == completion && File.ReadAllText(Path.Combine(f.Source, @"DCIM\PHOTO.JPG")) == "retained generated photo",
                        "Presentation consumed pending choice, requested eject or changed photo");
                    if (selected) Check(File.ReadAllText(Path.Combine(f.Source, @"DCIM\VIDEO.MP4")) == "retained generated video" && File.ReadAllText(Saved(result, "VIDEO.MP4")) == "retained generated video", "Source or full-quality generated copy changed");
                    Check(eject.Execute(completion.Id).Success && fake.Calls == 1, "Explicit fake eject lost the pending current choice");
                    Throws<SafetyException>(() => eject.Execute(completion.Id)); Check(fake.Calls == 1 && File.Exists(Path.Combine(f.Source, @"DCIM\PHOTO.JPG")), "Repeated choice requested removal or changed source media");
                }
            });
        }
    }
}
