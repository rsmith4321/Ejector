using System;
using System.IO;
using System.Linq;
using System.Threading;

namespace EasyEject {
    public static partial class Tests {
        static SavedProfile SavedImportEntry(ModernImportFixture f) { return Profiles.Catalog(f.Config).Single(); }
        static SavedProfileImportChoice SavedImportChoice(ModernImportFixture f, SavedProfile entry = null) {
            return SavedProfileImportChoice.Capture(entry ?? SavedImportEntry(f), f.Discovery.State, f.Discovery.Epoch);
        }
        static ImportOperationReview PrepareSavedImport(ModernImportFixture f, SavedProfileImportChoice choice, ImportDefaults defaults = null, Action<string> checkpoint = null) {
            return choice.Prepare(f.Discovery, defaults ?? new ImportDefaults(), f.Config, f.Resolve, checkpoint);
        }
        static void NoSavedImport(ModernImportFixture f) {
            Check(!Directory.EnumerateFileSystemEntries(f.Target).Any() && File.Exists(Path.Combine(f.Source, "clip.mp4")) && f.Operations.Available(new[] { 7, 8 }), "Rejected/review-only choice copied, removed, or reserved fixture data");
        }
        static void SavedProfileImportTests() {
            Test("saved-profile import is manual even with automatic sharing and removal preferences", delegate {
                var f = new ModernImportFixture(); f.Put("clip.mp4"); f.Profile.AutomaticImport = true; f.Profile.DeleteOriginals = true; f.Profile.SharingPreset = QuickShareProfile.Uhd; Profiles.Save(f.Profile, f.Config);
                var before = SavedImportEntry(f); var choice = SavedImportChoice(f, before);
                using (var review = PrepareSavedImport(f, choice)) {
                    Check(!review.Automatic && !review.EngineStarted && review.Options.DeleteOriginals && review.Profile.SharingPreset == QuickShareProfile.Uhd && review.Description.Contains("Start this import?"), "Saved choice became automatic or lost explicit import scope");
                    Throws<SafetyException>(() => review.Execute(f.Importer, CancellationToken.None, message => {}));
                    Throws<SafetyException>(() => PrepareSavedImport(f, choice)); NoSavedImport(f);
                }
                Check(SavedImportEntry(f).Hash == before.Hash, "Manual review changed the profile");
            });
            Test("saved-profile import refuses absent duplicate and formatted source identity", delegate {
                Action<Action<ModernImportFixture>> reject = change => {
                    var f = new ModernImportFixture(); f.Put("clip.mp4"); var entry = SavedImportEntry(f); change(f);
                    Throws<SafetyException>(() => SavedImportChoice(f, entry)); NoSavedImport(f);
                };
                reject(f => f.Discovery.State.Volumes.RemoveAt(0));
                reject(f => f.Discovery.State.Volumes.Add(f.Discovery.State.Volumes[0]));
                reject(f => f.Discovery.State.Volumes[0].Serial = "formatted");
                reject(f => f.Discovery.State.Disks[0].InstanceId = "replacement-reader");
                reject(f => f.Discovery.State.Volumes[0].System = true);
            });
            Test("saved-profile import refuses invalid records and stale inventory", delegate {
                var f = new ModernImportFixture(); f.Put("clip.mp4"); var entry = SavedImportEntry(f);
                Throws<SafetyException>(() => SavedImportChoice(f, new SavedProfile(entry.FileName, "Corrupt record retained")));
                Throws<SafetyException>(() => SavedProfileImportChoice.Capture(entry, null, 1));
                Throws<SafetyException>(() => SavedProfileImportChoice.Capture(entry, f.Discovery.State, 2)); NoSavedImport(f);
            });
            Test("saved-profile import uses identity rather than source or saved display name", delegate {
                var f = new ModernImportFixture(); f.Put("clip.mp4"); f.Discovery.State.Volumes[0].Label = "Untitled 2";
                var choice = SavedImportChoice(f); using (var review = PrepareSavedImport(f, choice)) {
                    Check(review.Profile.Name == f.Profile.Name && review.Root == f.Source && review.Source == f.Source, "Display labels selected different folders");
                    review.ValidateForPresentation(); NoSavedImport(f);
                }
            });
            Test("saved-profile import refuses changed catalog record and allows fresh manual retry", delegate {
                var f = new ModernImportFixture(); f.Put("clip.mp4"); var choice = SavedImportChoice(f);
                var changed = f.Profile.Copy(); changed.Name = "New saved preferences"; changed.IncludePreviews = true; Profiles.Save(changed, f.Config);
                Throws<SafetyException>(() => PrepareSavedImport(f, choice)); Throws<SafetyException>(() => PrepareSavedImport(f, choice)); NoSavedImport(f);
                using (var fresh = PrepareSavedImport(f, SavedImportChoice(f))) Check(fresh.Profile.Name == changed.Name && fresh.Options.IncludePreviews, "Manual refresh did not adopt reviewed new preferences");
            });
            Test("saved-profile import refuses identical-byte replacement by native record identity", delegate {
                var f = new ModernImportFixture(); f.Put("clip.mp4"); var entry = SavedImportEntry(f); var choice = SavedImportChoice(f, entry);
                string path = Path.Combine(f.Config, entry.FileName), backup = path + ".retained"; byte[] bytes = File.ReadAllBytes(path);
                File.Move(path, backup); File.WriteAllBytes(path, bytes); File.SetLastWriteTimeUtc(path, File.GetLastWriteTimeUtc(backup));
                Check(SavedImportEntry(f).Hash == entry.Hash, "Replacement fixture was not byte-identical");
                Throws<SafetyException>(() => PrepareSavedImport(f, choice)); Check(File.ReadAllBytes(backup).SequenceEqual(bytes), "Rejected original record was changed"); NoSavedImport(f);
            });
            Test("saved-profile import refuses disappeared and malformed records without rewriting them", delegate {
                foreach (bool malformed in new[] { false, true }) {
                    var f = new ModernImportFixture(); f.Put("clip.mp4"); var entry = SavedImportEntry(f); var choice = SavedImportChoice(f, entry); string path = Path.Combine(f.Config, entry.FileName);
                    if (malformed) File.WriteAllText(path, "{ malformed owned fixture"); else File.Move(path, path + ".retained");
                    Throws<SafetyException>(() => PrepareSavedImport(f, choice));
                    Check(malformed ? File.ReadAllText(path) == "{ malformed owned fixture" : !File.Exists(path) && File.Exists(path + ".retained"), "Rejected record was overwritten/restored"); NoSavedImport(f);
                }
            });
            Test("saved-profile import consumes source reconnect before preparation", delegate {
                var f = new ModernImportFixture(); f.Put("clip.mp4"); var choice = SavedImportChoice(f);
                f.Discovery.State.GroupConnections["modern-usb"] = "replacement-connection";
                Throws<SafetyException>(() => PrepareSavedImport(f, choice)); Throws<SafetyException>(() => PrepareSavedImport(f, choice)); NoSavedImport(f);
            });
            Test("saved-profile import rejects storage event and late event during handoff", delegate {
                var f = new ModernImportFixture(); f.Put("clip.mp4"); var choice = SavedImportChoice(f);
                f.Discovery.Version = f.Discovery.State.Epoch = 2; Throws<SafetyException>(() => PrepareSavedImport(f, choice)); NoSavedImport(f);
                var fresh = SavedImportChoice(f);
                Throws<SafetyException>(() => PrepareSavedImport(f, fresh, checkpoint: stage => {
                    if (stage == "Before saved-profile import handoff") f.Discovery.Version = f.Discovery.State.Epoch = 3;
                })); NoSavedImport(f);
                // The failed handoff must release its held folders before a new choice.
                File.Move(Path.Combine(f.Source, "clip.mp4"), Path.Combine(f.Source, "retained.mp4"));
                using (var retry = PrepareSavedImport(f, SavedImportChoice(f))) Check(!retry.EngineStarted, "Fresh manual retry started work");
            });
            Test("saved-profile import refuses destination identity and shared physical disk changes", delegate {
                Action<Action<ModernImportFixture>> reject = change => {
                    var f = new ModernImportFixture(); f.Put("clip.mp4"); var choice = SavedImportChoice(f); change(f);
                    Throws<SafetyException>(() => PrepareSavedImport(f, choice)); NoSavedImport(f);
                };
                reject(f => f.Discovery.State.Disks[1].Serial = "different-destination");
                reject(f => f.Discovery.State.Volumes[1].Disks[0] = 7);
            });
            Test("saved-profile late record replacement revokes handoff and releases reviewed folders", delegate {
                var f = new ModernImportFixture(); f.Put("clip.mp4"); var choice = SavedImportChoice(f);
                Throws<SafetyException>(() => PrepareSavedImport(f, choice, checkpoint: stage => {
                    if (stage == "Before saved-profile import handoff") { var changed = f.Profile.Copy(); changed.Name = "Late retained choices"; Profiles.Save(changed, f.Config); }
                }));
                NoSavedImport(f); Check(SavedImportEntry(f).Profile.Name == "Late retained choices", "Rejected late record was restored or overwritten");
                string previous = f.Target + "-old"; Directory.Move(f.Target, previous); Directory.CreateDirectory(f.Target);
                using (var retry = PrepareSavedImport(f, SavedImportChoice(f))) Check(!retry.EngineStarted, "New explicit review started engine");
                Check(Directory.Exists(previous), "Failed handoff retained folder ownership");
            });
            Test("saved-profile review queued for display expires after protected connection change", delegate {
                var f = new ModernImportFixture(); f.Put("clip.mp4"); using (var review = PrepareSavedImport(f, SavedImportChoice(f))) {
                    f.Discovery.Version = f.Discovery.State.Epoch = 2;
                    Throws<SafetyException>(() => review.ValidateForPresentation()); Throws<SafetyException>(() => review.Confirm()); NoSavedImport(f);
                }
            });
            Test("saved-profile import freezes following defaults and exact selection through copying", delegate {
                var f = new ModernImportFixture(); f.Put("clip.mp4"); f.Put("clip.lrv"); f.Put("photo.jpg"); f.Profile.UsesImportDefaults = true; Profiles.Save(f.Profile, f.Config);
                var defaults = new ImportDefaults { VideosOnly = true, IncludePreviews = false, CleanLayout = true, AutomaticImport = true };
                using (var review = PrepareSavedImport(f, SavedImportChoice(f), defaults)) {
                    defaults.VideosOnly = false; defaults.IncludePreviews = true; defaults.DeleteOriginals = true;
                    var escaped = review.Profile; escaped.Destination = Path.Combine(f.Root, "wrong"); escaped.SharingPreset = QuickShareProfile.Uhd;
                    review.ValidateForPresentation(); review.Confirm(); var result = review.Execute(f.Importer, CancellationToken.None, message => {});
                    Check(!review.Automatic && result.Files == 1 && result.Receipt.VideosOnly && !result.Receipt.IncludePreviews && !result.Receipt.OriginalRemovalRequested && result.Receipt.Files.Single().SourceRelativePath == "clip.mp4", "Saved import drifted to newer defaults or caller edits");
                    Check(Directory.GetFiles(f.Source).Length == 3 && f.Operations.Available(new[] { 7, 8 }), "Manual import altered originals or held reservations");
                }
            });
            Test("saved-profile legacy import cancellation binds no destination and confirmed retry binds exact record", delegate {
                var f = new ModernImportFixture(true); f.Put("clip.mp4"); var original = SavedImportEntry(f);
                using (var review = PrepareSavedImport(f, SavedImportChoice(f))) { review.ValidateForPresentation(); NoSavedImport(f); }
                Check(SavedImportEntry(f).Hash == original.Hash && SavedImportEntry(f).Profile.SavedDestination == null, "Cancelled catalog review persisted legacy binding");
                using (var retry = PrepareSavedImport(f, SavedImportChoice(f))) { retry.Confirm(); var result = retry.Execute(f.Importer, CancellationToken.None, message => {}); Check(result.Files == 1 && retry.Profile.SavedDestination.VolumeId == "modern-target-volume", "Confirmed retry did not bind verified destination"); }
                Check(File.Exists(Path.Combine(f.Source, "clip.mp4")) && SavedImportEntry(f).Profile.SavedDestination.VolumeId == "modern-target-volume", "Confirmed legacy binding lost original or durable selection");
            });
        }
    }
}
