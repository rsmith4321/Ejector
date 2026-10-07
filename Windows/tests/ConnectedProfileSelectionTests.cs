using System;
using System.IO;
using System.Linq;

namespace EasyEject {
    public static partial class Tests {
        static ConnectedProfileReview SelectionProfile(Fixture f, ScopedDiscovery d, out ImportProfile draft, bool existing = false) {
            var state = d.Scan(); draft = new ImportDefaults().NewProfile(state.Volumes[0], state.Disks[0]);
            draft.Name = "Reviewed connected camera"; draft.RelativeSource = "recordings"; draft.Destination = f.Destination;
            draft.SharingPreset = QuickShareProfile.Uhd; draft.SharingColor = QuickShareProfile.Standard;
            Directory.CreateDirectory(Path.Combine(f.Source, draft.RelativeSource)); f.Put("retained.mp4", "retain generated connected original");
            string root = Path.Combine(f.Root, "profiles"); if (existing) SaveSelectionSeed(draft, root);
            return Profiles.ReviewConnected(state.Volumes[0], state.Disks[0], root);
        }
        static void SaveSelectionSeed(ImportProfile draft, string root) { Profiles.Save(draft, root); }
        static ImportProfile SaveSelectionProfile(Fixture f, ScopedDiscovery d, ConnectedProfileReview reviewed, ImportProfile draft, bool chosen = true, Action<string> checkpoint = null, Func<string, string> resolver = null) {
            string source = Path.Combine(f.Source, "recordings");
            return Profiles.SaveConnectedSelection(reviewed, draft, d.State.Volumes[0], d.State.Disks[0], d.Epoch, f.Source, d, chosen, resolver ?? (path => path == source ? "source-volume" : "destination-volume"), checkpoint);
        }
        static void CheckSelectionProfile(Fixture f, ScopedDiscovery d, ConnectedProfileReview reviewed, ImportProfile expected) {
            var value = expected.Copy(); value.SavedDestination = DestinationIdentity.Capture(d.State, "destination-volume");
            var actual = Profiles.ReviewConnected(d.State.Volumes[0], d.State.Disks[0], reviewed.Root);
            Check(actual.Value != null && ReplaySerialize(actual.Value, typeof(ImportProfile)).SequenceEqual(ReplaySerialize(value, typeof(ImportProfile))), "Connected publication changed reviewed choices/destination");
            Check(File.ReadAllText(Path.Combine(f.Source, "retained.mp4")) == "retain generated connected original" && Directory.GetFiles(reviewed.Root).Length == 1, "Connected save changed originals or leaked private configuration");
        }
        static void ConnectedProfileSelectionTests() {
            foreach (string stage in new[] { "source resolver", "destination inventory", "source publication", "destination publication" }) {
                string selectedStage = stage;
                Test("connected selection refuses empty folder replacement during " + selectedStage, () => {
                    using (var f = new Fixture()) {
                        var d = new ScopedDiscovery(f); ImportProfile draft; var reviewed = SelectionProfile(f, d, out draft);
                        string selected = selectedStage.StartsWith("source", StringComparison.Ordinal) ? Path.Combine(f.Source, "recordings") : f.Destination;
                        string moved = selected + "-moved", replacement = selected + "-replacement"; bool changed = false;
                        Check(new[] { selected, moved, replacement }.All(path => path != f.Root && SafePaths.Within(path, f.Root)), "Connected fixture escaped owned root");
                        Action move = () => { if (changed) return; changed = true; Directory.Move(selected, moved); Directory.CreateDirectory(selected); File.WriteAllText(Path.Combine(selected, "foreign.keep"), "retain connected replacement"); };
                        if (selectedStage == "destination inventory") d.AfterScan = move;
                        Throws<SafetyException>(() => SaveSelectionProfile(f, d, reviewed, draft, checkpoint: stageName => { if (stageName == "Before publication" && selectedStage.EndsWith("publication", StringComparison.Ordinal)) move(); }, resolver: path => { if (selectedStage == "source resolver") move(); return path == Path.Combine(f.Source, "recordings") ? "source-volume" : "destination-volume"; }));
                        Check(changed && !File.Exists(Path.Combine(reviewed.Root, reviewed.FileName)) && Directory.Exists(moved) && !Directory.EnumerateFileSystemEntries(moved).Any() && File.ReadAllText(Path.Combine(selected, "foreign.keep")) == "retain connected replacement", "Folder refusal published metadata or changed original/replacement directories");
                        Check(File.ReadAllText(Path.Combine(f.Source, "retained.mp4")) == "retain generated connected original", "Folder refusal changed original data"); d.AfterScan = null;
                        Directory.Move(selected, replacement); Directory.Move(moved, selected);
                        SaveSelectionProfile(f, d, Profiles.ReviewConnected(d.State.Volumes[0], d.State.Disks[0], reviewed.Root), draft); CheckSelectionProfile(f, d, reviewed, draft);
                        Check(File.ReadAllText(Path.Combine(replacement, "foreign.keep")) == "retain connected replacement", "Retry changed retained replacement data");
                    }
                });
            }
            foreach (string instance in new[] { "source-usb", "destination-controller" }) {
                string changedInstance = instance;
                Test("connected selection refuses protected reconnect before publication on " + changedInstance, () => {
                    using (var f = new Fixture()) {
                        var d = new ScopedDiscovery(f); ImportProfile draft; var reviewed = SelectionProfile(f, d, out draft); bool changed = false;
                        Throws<SafetyException>(() => SaveSelectionProfile(f, d, reviewed, draft, checkpoint: stage => { if (stage == "Before publication") { changed = true; d.Event(changedInstance); } }));
                        Check(changed && !File.Exists(Path.Combine(reviewed.Root, reviewed.FileName)), "Protected reconnect published metadata");
                        SaveSelectionProfile(f, d, Profiles.ReviewConnected(d.State.Volumes[0], d.State.Disks[0], reviewed.Root), draft); CheckSelectionProfile(f, d, reviewed, draft);
                    }
                });
            }
            Test("connected selection freezes caller folders and action choices before device callbacks", () => {
                using (var f = new Fixture()) {
                    var d = new ScopedDiscovery(f); ImportProfile draft; var reviewed = SelectionProfile(f, d, out draft); var expected = draft.Copy(); bool changed = false;
                    var result = SaveSelectionProfile(f, d, reviewed, draft, resolver: path => {
                        if (!changed) { changed = true; draft.Name = "Caller-only choices"; draft.RelativeSource = "caller-only-recordings"; draft.Destination = Path.Combine(f.Root, "caller-only-destination"); draft.UsesImportDefaults = false; draft.AutomaticImport = draft.DeleteOriginals = draft.RecoverTrash = true; draft.SharingColor = QuickShareProfile.LunaILog; }
                        return path == Path.Combine(f.Source, "recordings") ? "source-volume" : "destination-volume";
                    });
                    Check(changed && draft.Name == "Caller-only choices" && !Directory.Exists(draft.Destination), "Caller changes were adopted or rolled back");
                    CheckSelectionProfile(f, d, reviewed, expected); expected.SavedDestination = DestinationIdentity.Capture(d.State, "destination-volume");
                    Check(ReplaySerialize(result, typeof(ImportProfile)).SequenceEqual(ReplaySerialize(expected, typeof(ImportProfile))) && !Object.ReferenceEquals(result, draft), "Return value is not the exact isolated saved choices");
                }
            });
            Test("connected selection permits unrelated events ordinary contents and released native leases", () => {
                using (var f = new Fixture()) {
                    var d = new ScopedDiscovery(f); ImportProfile draft; var reviewed = SelectionProfile(f, d, out draft); bool changed = false;
                    SaveSelectionProfile(f, d, reviewed, draft, checkpoint: stage => { if (stage == "Before publication") { changed = true; d.Event("other-usb"); File.WriteAllText(Path.Combine(f.Destination, "ordinary.keep"), "retain ordinary connected data"); Directory.SetLastWriteTimeUtc(Path.Combine(f.Source, "recordings"), DateTime.UtcNow.AddMinutes(-1)); } });
                    Check(changed, "Ordinary changes were not exercised"); CheckSelectionProfile(f, d, reviewed, draft);
                    string source = Path.Combine(f.Source, "recordings"), moved = source + "-released"; Check(SafePaths.Within(moved, f.Root), "Release path escaped fixture"); Directory.Move(source, moved); Directory.Move(moved, source);
                    Check(File.ReadAllText(Path.Combine(f.Destination, "ordinary.keep")) == "retain ordinary connected data", "Ordinary contents were lost");
                }
            });
            Test("connected selection reports exact already published metadata after late source reconnect", () => {
                using (var f = new Fixture()) {
                    var d = new ScopedDiscovery(f); ImportProfile draft; var reviewed = SelectionProfile(f, d, out draft); bool changed = false, reported = false;
                    try { SaveSelectionProfile(f, d, reviewed, draft, checkpoint: stage => { if (stage == "After publication") { changed = true; d.Event("source-usb"); } }); }
                    catch (SafetyException ex) { reported = ex.Message.Contains("Configuration was published at ") && ex.Message.Contains(reviewed.FileName); }
                    Check(changed && reported, "Late source reconnect concealed actual publication"); CheckSelectionProfile(f, d, reviewed, draft);
                    SaveSelectionProfile(f, d, Profiles.ReviewConnected(d.State.Volumes[0], d.State.Disks[0], reviewed.Root), draft); CheckSelectionProfile(f, d, reviewed, draft);
                }
            });
            Test("connected selection requires explicit destination rebinding while preserving old choices", () => {
                using (var f = new Fixture()) {
                    var d = new ScopedDiscovery(f); ImportProfile draft; SelectionProfile(f, d, out draft); draft.SavedDestination = HistoryIdentity(); string root = Path.Combine(f.Root, "profiles"); Profiles.Save(draft, root);
                    var reviewed = Profiles.ReviewConnected(d.State.Volumes[0], d.State.Disks[0], root); byte[] original = File.ReadAllBytes(Path.Combine(root, reviewed.FileName));
                    Throws<SafetyException>(() => SaveSelectionProfile(f, d, reviewed, draft, chosen: false)); Check(File.ReadAllBytes(Path.Combine(root, reviewed.FileName)).SequenceEqual(original), "Unchosen destination replaced prior binding");
                    SaveSelectionProfile(f, d, reviewed, draft, chosen: true); CheckSelectionProfile(f, d, reviewed, draft);
                }
            });
            Test("connected selection cannot overwrite a profile changed before publication", () => {
                using (var f = new Fixture()) {
                    var d = new ScopedDiscovery(f); ImportProfile draft; var reviewed = SelectionProfile(f, d, out draft, true); var other = draft.Copy(); other.Name = "New external reviewed profile"; byte[] changed = ReplaySerialize(other, typeof(ImportProfile));
                    Throws<SafetyException>(() => SaveSelectionProfile(f, d, reviewed, draft, checkpoint: stage => { if (stage == "Before connected publication") File.WriteAllBytes(Path.Combine(reviewed.Root, reviewed.FileName), changed); }));
                    Check(File.ReadAllBytes(Path.Combine(reviewed.Root, reviewed.FileName)).SequenceEqual(changed), "Stale connected save replaced external choices");
                    SaveSelectionProfile(f, d, Profiles.ReviewConnected(d.State.Volumes[0], d.State.Disks[0], reviewed.Root), draft); CheckSelectionProfile(f, d, reviewed, draft);
                }
            });
        }
    }
}
