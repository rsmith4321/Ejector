using System;
using System.IO;
using System.Linq;

namespace EasyEject {
    public static partial class Tests {
        static SavedProfile SelectedOfflineProfile(Fixture f, out ScopedDiscovery discovery, out ImportProfile draft) {
            var expected = PublicationProfile(f); draft = expected.Profile; draft.Name = "Reviewed selected destination";
            draft.Destination = Path.Combine(f.Root, "chosen-destination"); Directory.CreateDirectory(draft.Destination);
            f.Put("original.mp4", "retain generated original"); discovery = new ScopedDiscovery(f);
            discovery.State.Volumes[1].Mounts.Clear(); discovery.State.Volumes[1].Mounts.Add(draft.Destination);
            return expected;
        }
        static void SaveSelectedOffline(Fixture f, SavedProfile expected, ImportProfile draft, ScopedDiscovery discovery, Action<string> checkpoint = null, Func<string, string> resolver = null) {
            OfflineProfileDestination.SaveSelected(expected, draft, discovery, f.Destination, resolver ?? (path => "destination-volume"), checkpoint);
        }
        static void CheckSelectedOffline(Fixture f, ScopedDiscovery discovery, ImportProfile reviewed) {
            var value = reviewed.Copy(); value.SavedDestination = DestinationIdentity.Capture(discovery.State, "destination-volume");
            var actual = Profiles.Catalog(f.Destination).Single();
            Check(ReplaySerialize(actual.Profile, typeof(ImportProfile)).SequenceEqual(ReplaySerialize(value, typeof(ImportProfile))), "Selected publication changed reviewed choices or destination binding");
            Check(File.ReadAllText(Path.Combine(f.Source, "original.mp4")) == "retain generated original", "Profile selection changed generated source data");
            CheckProfilePublicationFiles(f, 1);
        }
        static void OfflineProfileSelectionTests() {
            OfflineProfileSelectionChoicesTests();
            foreach (string stage in new[] { "resolver", "inventory", "Before publication" }) {
                string selectedStage = stage;
                Test("selected offline destination refuses empty-root replacement during " + selectedStage, () => {
                    using (var f = new Fixture()) {
                        ScopedDiscovery d; ImportProfile draft; var expected = SelectedOfflineProfile(f, out d, out draft);
                        string destination = draft.Destination, moved = destination + "-moved", replacement = destination + "-replacement"; bool changed = false;
                        Check(new[] { destination, moved, replacement }.All(path => path != f.Root && SafePaths.Within(path, f.Root)), "Selected fixture escaped owned root");
                        Action move = () => { if (changed) return; changed = true; Directory.Move(destination, moved); Directory.CreateDirectory(destination); File.WriteAllText(Path.Combine(destination, "foreign.keep"), "retain replacement data"); };
                        if (selectedStage == "inventory") d.AfterScan = move;
                        Throws<SafetyException>(() => SaveSelectedOffline(f, expected, draft, d,
                            stageName => { if (stageName == selectedStage) move(); }, path => { if (selectedStage == "resolver") move(); return "destination-volume"; }));
                        Check(changed && Profiles.Catalog(f.Destination).Single().Hash == expected.Hash && Directory.Exists(moved) && !Directory.EnumerateFileSystemEntries(moved).Any() && File.ReadAllText(Path.Combine(destination, "foreign.keep")) == "retain replacement data", "Selected refusal published choices or changed either owned directory");
                        CheckProfilePublicationFiles(f, 1); d.AfterScan = null;
                        Directory.Move(destination, replacement); Directory.Move(moved, destination);
                        SaveSelectedOffline(f, Profiles.Catalog(f.Destination).Single(), draft, d); CheckSelectedOffline(f, d, draft);
                        Check(File.ReadAllText(Path.Combine(replacement, "foreign.keep")) == "retain replacement data", "Fresh retry changed retained replacement data");
                    }
                });
            }
            Test("selected offline destination reconnect before publication preserves the old profile and permits fresh review", () => {
                using (var f = new Fixture()) {
                    ScopedDiscovery d; ImportProfile draft; var expected = SelectedOfflineProfile(f, out d, out draft); bool changed = false;
                    Throws<SafetyException>(() => SaveSelectedOffline(f, expected, draft, d, stage => { if (stage == "Before publication") { changed = true; d.Event("destination-controller"); } }));
                    Check(changed && Profiles.Catalog(f.Destination).Single().Hash == expected.Hash && !Directory.EnumerateFileSystemEntries(draft.Destination).Any(), "Reconnect published profile choices or wrote destination files");
                    CheckProfilePublicationFiles(f, 1); SaveSelectedOffline(f, expected, draft, d); CheckSelectedOffline(f, d, draft);
                }
            });
            Test("selected offline destination permits unrelated events through publication", () => {
                using (var f = new Fixture()) {
                    ScopedDiscovery d; ImportProfile draft; var expected = SelectedOfflineProfile(f, out d, out draft); bool changed = false;
                    SaveSelectedOffline(f, expected, draft, d, stage => { if (stage == "Before publication") { changed = true; d.Event("other-usb"); } });
                    Check(changed, "Unrelated event was not exercised"); CheckSelectedOffline(f, d, draft);
                    string moved = draft.Destination + "-released"; Check(SafePaths.Within(moved, f.Root), "Released path escaped fixture"); Directory.Move(draft.Destination, moved); Directory.Move(moved, draft.Destination);
                }
            });
            Test("selected offline destination retains entry choices while permitting ordinary directory changes", () => {
                using (var f = new Fixture()) {
                    ScopedDiscovery d; ImportProfile draft; var expected = SelectedOfflineProfile(f, out d, out draft); var reviewed = draft.Copy(); bool changed = false;
                    SaveSelectedOffline(f, expected, draft, d, stage => {
                        if (stage != "Before publication") return;
                        File.WriteAllText(Path.Combine(reviewed.Destination, "ordinary.keep"), "retain ordinary contents"); Directory.SetLastWriteTimeUtc(reviewed.Destination, DateTime.UtcNow.AddMinutes(-1));
                    }, path => { if (!changed) { changed = true; draft.Name = "caller-only-name"; draft.Destination = Path.Combine(f.Root, "caller-only-destination"); draft.DeleteOriginals = true; } return "destination-volume"; });
                    Check(changed && draft.Name == "caller-only-name" && !Directory.Exists(draft.Destination), "Initial caller changes were adopted or rolled back");
                    CheckSelectedOffline(f, d, reviewed); Check(File.ReadAllText(Path.Combine(reviewed.Destination, "ordinary.keep")) == "retain ordinary contents", "Ordinary destination changes were lost");
                }
            });
            Test("selected offline destination late reconnect reports the already published exact profile", () => {
                using (var f = new Fixture()) {
                    ScopedDiscovery d; ImportProfile draft; var expected = SelectedOfflineProfile(f, out d, out draft); bool changed = false, reported = false;
                    try { SaveSelectedOffline(f, expected, draft, d, stage => { if (stage == "After publication") { changed = true; d.Event("destination-controller"); } }); }
                    catch (SafetyException ex) { reported = ex.Message.Contains("Configuration was published at ") && ex.Message.Contains(expected.FileName); }
                    Check(changed && reported, "Late reconnect did not report actual publication"); CheckSelectedOffline(f, d, draft);
                    SaveSelectedOffline(f, Profiles.Catalog(f.Destination).Single(), draft, d); CheckSelectedOffline(f, d, draft);
                }
            });
            Test("unchanged offline profile choices need no connected source or destination", () => {
                using (var f = new Fixture()) {
                    var profile = OfflineProfile(); profile.Destination = Path.Combine(f.Root, "absent-destination"); Profiles.Save(profile, f.Destination);
                    var expected = Profiles.Catalog(f.Destination).Single(); var draft = expected.Profile; draft.Name = "Disconnected reviewed device";
                    Profiles.SaveOffline(expected, draft, f.Destination);
                    Check(!Directory.Exists(profile.Destination) && ReplaySerialize(Profiles.Catalog(f.Destination).Single().Profile, typeof(ImportProfile)).SequenceEqual(ReplaySerialize(draft, typeof(ImportProfile))), "Offline rename required or created the absent destination");
                }
            });
        }
        static void OfflineProfileSelectionChoicesTests() {
            Test("offline publication freezes validated choices before review callbacks", () => {
                using (var f = new Fixture()) {
                    var expected = PublicationProfile(f); var draft = expected.Profile; draft.Name = "Reviewed offline choices";
                    byte[] reviewed = ReplaySerialize(draft, typeof(ImportProfile)); bool changed = false;
                    Profiles.SaveOffline(expected, draft, f.Destination, checkpoint: stage => {
                        if (stage != "After profile review") return; changed = true;
                        draft.VolumeId = "caller-only-device"; draft.RelativeSource = "$RECYCLE.BIN"; draft.Destination = Path.Combine(f.Root, "caller-only-folder");
                        draft.UsesImportDefaults = false; draft.DeleteOriginals = draft.RecoverTrash = draft.AutomaticImport = true;
                        draft.SharingPreset = QuickShareProfile.Uhd; draft.SharingColor = QuickShareProfile.LunaILog;
                    });
                    Check(changed && draft.VolumeId == "caller-only-device", "Caller mutation was not exercised or was rolled back");
                    Check(File.ReadAllBytes(Path.Combine(f.Destination, expected.FileName)).SequenceEqual(reviewed), "Callback changed the validated source enrollment/folders/automatic/removal/sharing choices published by the offline editor");
                    CheckProfilePublicationFiles(f, 1); FreshProfilePublication(f);
                }
            });
            Test("offline publication freezes nested saved destination before writer callbacks", () => {
                using (var f = new Fixture()) {
                    var profile = OfflineProfile(); profile.SavedDestination = HistoryIdentity(); Profiles.Save(profile, f.Destination);
                    var expected = Profiles.Catalog(f.Destination).Single(); var draft = expected.Profile; draft.Name = "Reviewed nested destination";
                    byte[] reviewed = ReplaySerialize(draft, typeof(ImportProfile)); bool changed = false;
                    Profiles.SaveOffline(expected, draft, f.Destination, checkpoint: stage => {
                        if (stage != "Before temporary creation") return; changed = true;
                        draft.SavedDestination.VolumeSerial = "caller-only-volume"; draft.SavedDestination.DiskInstance = "caller-only-disk";
                    });
                    Check(changed && draft.SavedDestination.DiskInstance == "caller-only-disk", "Nested caller mutation was not exercised");
                    Check(File.ReadAllBytes(Path.Combine(f.Destination, expected.FileName)).SequenceEqual(reviewed), "Callback retargeted the privately reviewed destination identity");
                    var fresh = Profiles.Catalog(f.Destination).Single(); var retry = fresh.Profile; retry.Name = "Fresh explicit offline review";
                    Profiles.SaveOffline(fresh, retry, f.Destination); Check(Profiles.Catalog(f.Destination).Single().Profile.Name == retry.Name, "Nested review retained writer resources");
                }
            });
        }
    }
}
