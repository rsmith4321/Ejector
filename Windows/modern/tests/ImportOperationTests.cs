using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;

namespace EasyEject {
    public static partial class Tests {
        sealed class ModernImportFixture {
            internal readonly string Root, Source, Target, Config;
            internal readonly ModernStorageDiscovery Discovery;
            internal readonly Operations Operations = new Operations();
            internal readonly ImportProfile Profile;
            internal ModernImportFixture(bool legacy = false) {
                Root = ModernDefaultsFixture(); Source = Path.Combine(Root, "source"); Target = Path.Combine(Root, "destination"); Config = Path.Combine(Root, "profiles");
                Directory.CreateDirectory(Source); Directory.CreateDirectory(Target);
                Discovery = new ModernStorageDiscovery { State = ModernStorageState(Source) };
                Discovery.State.GroupConnections["modern-usb"] = "fixture-generation-1";
                Discovery.State.Disks.Add(new Disk { Number = 8, InstanceId = "modern-target", Serial = "target-serial", DevInst = 11 });
                Discovery.State.Volumes.Add(new Volume { Id = "modern-target-volume", Serial = "DEF", Extents = "8:0:1000", Disks = new List<int> { 8 }, Mounts = new List<string> { Target } });
                Profile = ModernProfile(Target); Profile.UsesImportDefaults = false; Profile.VideosOnly = true; Profile.IncludePreviews = false; Profile.CleanLayout = true;
                if (!legacy) Profile.SavedDestination = DestinationIdentity.Capture(Discovery.State, "modern-target-volume"); Profiles.Save(Profile, Config);
            }
            internal AutomaticConnection Connection { get { var value = AutomaticConnection.Capture(Discovery.State, Discovery.State.Volumes[0]); Check(value != null, "No fake fixture connection"); return value; } }
            internal string Resolve(string path) { return SafePaths.Within(SafePaths.Normalize(path), Source) ? "modern-volume" : "modern-target-volume"; }
            internal ConnectedProfileReview Reviewed { get { return Profiles.ReviewConnected(Discovery.State.Volumes[0], Discovery.State.Disks[0], Config); } }
            internal ImportOperationReview Manual(ImportDefaults defaults = null) { return ImportOperationReview.Manual(Connection, Reviewed, defaults ?? new ImportDefaults(), Discovery, Resolve); }
            internal VerifiedImporter Importer { get { return new VerifiedImporter(Operations, path => UInt64.MaxValue, path => {}); } }
            internal void Put(string name, byte[] bytes = null) { File.WriteAllBytes(Path.Combine(Source, name), bytes ?? new byte[] { 1, 2, 3, 4 }); }
            internal ImportedOutcome Outcome(ImportResult result, ImportOperationReview review, Action<CompletedBatch> save = null) {
                return new ImportedOutcome(result, review.Profile, review.Guard, Source, Discovery, save ?? (batch => ImportHistory.Add(batch, Config)), Resolve);
            }
        }
        static void ImportOperationTests() {
            Test("modern acknowledged removal deletes only verified selected disposable originals", delegate {
                var f = new ModernImportFixture(); f.Put("clip.mp4"); f.Put("clip.lrv"); f.Put("photo.jpg"); f.Profile.DeleteOriginals = true; Profiles.Save(f.Profile, f.Config);
                using (var review = f.Manual()) {
                    Check(review.Options.DeleteOriginals && !review.Options.RecoverTrash && review.Description.Contains("permanent"), "Reviewed removal scope was lost");
                    review.Confirm(); var result = review.Execute(f.Importer, CancellationToken.None, message => {}); var outcome = f.Outcome(result, review);
                    Check(result.RemovedOriginals == 1 && !File.Exists(Path.Combine(f.Source, "clip.mp4")) && File.Exists(Path.Combine(f.Source, "clip.lrv")) && File.Exists(Path.Combine(f.Source, "photo.jpg")) && result.Receipt.Files.Single(e => e.IsMedia).RemovedSource && outcome.Batch != null && outcome.SourceCurrent && f.Operations.Available(new[] { 7, 8 }), "Optional removal exceeded verified selected scope or lost durable result");
                }
            });
            Test("modern cancelled unconfirmed and replayed manual imports never start copying", delegate {
                var f = new ModernImportFixture(); f.Put("clip.mp4");
                using (var review = f.Manual()) { Throws<SafetyException>(() => review.Execute(f.Importer, CancellationToken.None, message => {})); Check(!review.EngineStarted, "Unconfirmed engine ran"); }
                var closed = f.Manual(); closed.Dispose(); Throws<SafetyException>(() => closed.Confirm()); Throws<SafetyException>(() => closed.Execute(f.Importer, CancellationToken.None, message => {}));
                Check(!Directory.EnumerateFileSystemEntries(f.Target).Any() && File.Exists(Path.Combine(f.Source, "clip.mp4")), "Cancelled review touched files");
            });
            Test("modern confirmed import freezes profile choices and records exact verified originals", delegate {
                var f = new ModernImportFixture(); f.Put("clip.mp4"); f.Put("clip.lrv"); f.Put("photo.jpg"); ImportResult result;
                using (var review = f.Manual()) {
                    var caller = review.Profile; caller.VideosOnly = false; caller.IncludePreviews = true; caller.Destination = Path.Combine(f.Root, "wrong");
                    Check(review.Description.Contains("required companions") && review.Description.Contains("stay on the device") && review.Options.VideosOnly && !review.Options.IncludePreviews, "Review lost scope");
                    review.Confirm(); result = review.Execute(f.Importer, CancellationToken.None, message => {});
                    Check(result.Files == 1 && result.HasSelectedMedia && result.Receipt.Files.Single().SourceRelativePath == "clip.mp4" && result.Receipt.VideosOnly && !result.Receipt.IncludePreviews && result.Receipt.CleanLayout, "Copy did not use frozen options");
                    Throws<SafetyException>(() => review.Execute(f.Importer, CancellationToken.None, message => {}));
                    var outcome = f.Outcome(result, review); Check(outcome.SourceCurrent && outcome.HistoryWarning == null && outcome.Batch.Originals.Count == 1 && outcome.Message.Contains("Import complete"), "Verified outcome/history failed");
                    var batch = outcome.Batch; batch.ProfileId = "caller-only"; Check(outcome.Batch.ProfileId == f.Profile.VolumeId && ImportHistory.Load(f.Config).Single().Originals.Single().Sha256 == result.Receipt.Files.Single().Sha256, "Exact batch leaked mutable scope");
                }
                Check(Directory.GetFiles(f.Source).Length == 3 && File.Exists(result.ReceiptPath) && f.Operations.Available(new[] { 7, 8 }), "Originals or reservations were lost");
            });
            Test("modern manual legacy binding occurs only after confirmed non-cancelled admission", delegate {
                var f = new ModernImportFixture(true); f.Put("clip.mp4");
                using (var cancelled = f.Manual()) using (var stop = new CancellationTokenSource()) { cancelled.Confirm(); stop.Cancel(); Throws<OperationCanceledException>(() => cancelled.Execute(f.Importer, stop.Token, message => {})); }
                Check(Profiles.Catalog(f.Config).Single().Profile.SavedDestination == null && !Directory.EnumerateFileSystemEntries(f.Target).Any(), "Pre-cancel persisted legacy binding or copied");
                using (var review = f.Manual()) { review.Confirm(); var result = review.Execute(f.Importer, CancellationToken.None, message => {}); Check(result.Files == 1 && review.Profile.SavedDestination.VolumeId == "modern-target-volume", "Confirmed legacy destination not bound"); }
                Check(Profiles.Catalog(f.Config).Single().Profile.SavedDestination.VolumeId == "modern-target-volume", "Guarded binding not persisted");
            });
            Test("modern legacy record changes during confirmation refuse binding and copying", delegate {
                var f = new ModernImportFixture(true); f.Put("clip.mp4");
                using (var review = f.Manual()) { var changed = f.Profile.Copy(); changed.Name = "External saved choice"; Profiles.Save(changed, f.Config); review.Confirm(); Throws<SafetyException>(() => review.Execute(f.Importer, CancellationToken.None, message => {})); Check(!review.EngineStarted, "Changed legacy record admitted engine"); }
                Check(Profiles.Catalog(f.Config).Single().Profile.Name == "External saved choice" && !Directory.EnumerateFileSystemEntries(f.Target).Any() && File.Exists(Path.Combine(f.Source, "clip.mp4")), "Refusal changed config/source/destination");
            });
            Test("modern source reconnect and changed destination refuse before copy", delegate {
                var f = new ModernImportFixture(); f.Put("clip.mp4");
                using (var review = f.Manual()) { review.Confirm(); f.Discovery.Version = f.Discovery.State.Epoch = 2; Throws<SafetyException>(() => review.Execute(f.Importer, CancellationToken.None, message => {})); Check(!review.EngineStarted, "Reconnect admitted copying"); }
                f.Discovery.Version = f.Discovery.State.Epoch = 1; var changed = f.Profile.Copy(); changed.SavedDestination.DiskSerial = "wrong-destination"; Profiles.Save(changed, f.Config);
                Throws<SafetyException>(() => f.Manual()); Check(!Directory.EnumerateFileSystemEntries(f.Target).Any(), "Changed destination copied data");
            });
            Test("modern mid-copy cancellation retains source and releases both fake disks", delegate {
                var f = new ModernImportFixture(); f.Put("clip.mp4", new byte[2097152]);
                using (var stop = new CancellationTokenSource()) using (var review = f.Manual()) {
                    review.Confirm(); Throws<OperationCanceledException>(() => review.Execute(f.Importer, stop.Token, message => { if (message.StartsWith("Copying", StringComparison.Ordinal)) stop.Cancel(); }));
                    Check(review.EngineStarted && File.ReadAllBytes(Path.Combine(f.Source, "clip.mp4")).Length == 2097152 && !Directory.GetFiles(f.Target, "verified-import.json", SearchOption.AllDirectories).Any() && f.Operations.Available(new[] { 7, 8 }), "Cancel lost data/receipt/reservation");
                }
            });
            Test("modern late terminal cancellation and display failure cannot revoke receipt", delegate {
                var f = new ModernImportFixture(); f.Put("clip.mp4");
                using (var stop = new CancellationTokenSource()) using (var review = f.Manual()) {
                    review.Confirm(); var result = review.Execute(f.Importer, stop.Token, message => {}, measured => { if (measured.Phase == "Import complete") { stop.Cancel(); throw new IOException("Owned terminal display failure"); } });
                    var outcome = f.Outcome(result, review); Check(stop.IsCancellationRequested && result.Files == 1 && File.Exists(result.ReceiptPath) && outcome.Batch != null && outcome.SourceCurrent, "Terminal display/cancel changed durable result");
                }
            });
            Test("modern history failure and later reconnect retain verified import evidence", delegate {
                var f = new ModernImportFixture(); f.Put("clip.mp4");
                using (var review = f.Manual()) {
                    review.Confirm(); var result = review.Execute(f.Importer, CancellationToken.None, message => {}); f.Discovery.Version = f.Discovery.State.Epoch = 2;
                    var outcome = f.Outcome(result, review, batch => { throw new IOException("Owned history failure"); });
                    Check(!outcome.SourceCurrent && outcome.Batch == null && outcome.HistoryWarning.Contains("Import verified") && outcome.Message.Contains("Import complete") && outcome.Message.Contains("connection changed") && File.Exists(result.ReceiptPath) && File.Exists(Path.Combine(f.Source, "clip.mp4")), "Late history/reconnect relabelled or destroyed completed import");
                }
            });
            Test("modern repeated import reuses exact copies with fresh receipt and original names", delegate {
                var f = new ModernImportFixture(); f.Put("clip.mp4"); ImportResult first, second;
                using (var review = f.Manual()) { review.Confirm(); first = review.Execute(f.Importer, CancellationToken.None, message => {}); }
                using (var review = f.Manual()) { review.Confirm(); second = review.Execute(f.Importer, CancellationToken.None, message => {}); }
                Check(first.Reused == 0 && second.Reused == 1 && first.Receipt.Id != second.Receipt.Id && second.Receipt.Files.Single().RelativePath == first.Receipt.Files.Single().RelativePath && File.ReadAllBytes(Path.Combine(f.Source, "clip.mp4")).SequenceEqual(new byte[] { 1, 2, 3, 4 }), "Fresh import lost verified reuse or names/original");
            });
            foreach (bool index in new[] { false, true }) {
                bool indexOnly = index;
                Test("modern missing media folder retains " + (indexOnly ? "index-only card" : "empty-card") + " no-batch completion", delegate {
                    var f = new ModernImportFixture(); if (indexOnly) f.Put("fileinfo_list.list"); f.Profile.RelativeSource = "DCIM"; Profiles.Save(f.Profile, f.Config);
                    using (var review = f.Manual()) {
                        review.Confirm(); var result = review.Execute(f.Importer, CancellationToken.None, message => {}); int writes = 0; var outcome = f.Outcome(result, review, batch => writes++);
                        Check(!result.HasSelectedMedia && result.Files == 0 && result.ReceiptPath == null && outcome.SourceCurrent && outcome.Batch == null && writes == 0 && outcome.Message.StartsWith("No matching media", StringComparison.Ordinal), "Missing selection fabricated media/history or failed identity");
                        if (indexOnly) Check(File.Exists(Path.Combine(f.Source, "fileinfo_list.list")), "Device index was removed");
                    }
                });
            }
            Test("modern whole-card device index backup remains metadata-only and retains original", delegate {
                var f = new ModernImportFixture(); f.Put("fileinfo_list.list");
                using (var review = f.Manual()) {
                    review.Confirm(); var result = review.Execute(f.Importer, CancellationToken.None, message => {}); int writes = 0; var outcome = f.Outcome(result, review, batch => writes++);
                    Check(result.Files == 1 && !result.HasSelectedMedia && result.Receipt.Files.Single().IsMedia == false && outcome.Batch == null && writes == 0 && outcome.Message.Contains("Device index backup only") && File.Exists(Path.Combine(f.Source, "fileinfo_list.list")), "Index backup created sharing/history media or removed original");
                }
            });
            Test("modern completion asks separately and expires on reconnect without losing saved history", delegate {
                var f = new ModernImportFixture(); f.Put("clip.mp4"); var registry = new ImportCompletionRegistry(); var api = new ModernRemovalApi(); ImportCompletion entry;
                using (var review = f.Manual()) {
                    review.Confirm(); var result = review.Execute(f.Importer, CancellationToken.None, message => {}); var outcome = f.Outcome(result, review);
                    entry = new ImportCompletion(review.Guard.CompletionOrigin, outcome.Profile.Name, result.Folder, f.Target, review.Guard.DestinationIdentity, true, outcome.Message, outcome.Batch); registry.Insert(entry);
                    Check(api.Calls == 0 && registry.Get(entry.Id) != null, "Import automatically requested eject");
                }
                f.Discovery.Version = f.Discovery.State.Epoch = 2; registry.InvalidateNotifications();
                var service = new CompletedEjectService(registry, f.Discovery, validate => { api.Validate = validate; return api; }, f.Operations, f.Resolve);
                Throws<SafetyException>(() => service.Execute(entry.Id)); Check(api.Calls == 0 && registry.Get(entry.Id) == null && ImportHistory.Load(f.Config).Single().Id == entry.Batch.Id && File.Exists(Path.Combine(f.Source, "clip.mp4")), "Reconnect reused eject authority or lost saved batch");
            });
            Test("modern current completion performs exactly one explicitly chosen fake removal", delegate {
                var f = new ModernImportFixture(); f.Put("clip.mp4"); var registry = new ImportCompletionRegistry(); var api = new ModernRemovalApi(); ImportCompletion entry;
                using (var review = f.Manual()) {
                    review.Confirm(); var result = review.Execute(f.Importer, CancellationToken.None, message => {}); var outcome = f.Outcome(result, review);
                    entry = new ImportCompletion(review.Guard.CompletionOrigin, outcome.Profile.Name, result.Folder, f.Target, review.Guard.DestinationIdentity, true, outcome.Message, outcome.Batch); registry.Insert(entry);
                }
                var service = new CompletedEjectService(registry, f.Discovery, validate => { api.Validate = validate; return api; }, f.Operations, f.Resolve);
                Check(service.Execute(entry.Id).Success && api.Calls == 1 && registry.Get(entry.Id) == null && f.Operations.Available(new[] { 7, 8 }), "Exact current completion did not consume/release");
                Throws<SafetyException>(() => service.Execute(entry.Id)); Check(api.Calls == 1 && File.Exists(Path.Combine(f.Source, "clip.mp4")), "Completion replay requested removal or changed files");
            });
            Test("modern missing saved media folder with remaining media refuses empty completion", delegate {
                var f = new ModernImportFixture(); f.Put("clip.mp4"); f.Profile.RelativeSource = "DCIM"; Profiles.Save(f.Profile, f.Config);
                using (var review = f.Manual()) { review.Confirm(); Throws<SafetyException>(() => review.Execute(f.Importer, CancellationToken.None, message => {})); }
                Check(File.Exists(Path.Combine(f.Source, "clip.mp4")) && !Directory.GetFiles(f.Target, "verified-import.json", SearchOption.AllDirectories).Any(), "Missing media produced a false empty completion");
            });
            Test("modern automatic request requires opt-in destination proof and never manual replay", delegate {
                var f = new ModernImportFixture(); f.Put("clip.mp4"); var scheduler = new AutomaticImportScheduler(); var defaults = new ImportDefaults();
                Check(scheduler.Take(f.Discovery.State, new[] { f.Profile }, defaults, false) == null, "Off profile scheduled");
                Throws<SafetyException>(() => ImportOperationReview.AutomaticRequest(new AutomaticImportRequest(f.Connection, f.Profile, defaults), f.Discovery, volumeId: f.Resolve));
                f.Profile.AutomaticImport = true; var request = scheduler.Take(f.Discovery.State, new[] { f.Profile }, defaults, false); Check(request != null, "Opt-in profile not admitted");
                f.Profile.VideosOnly = false; f.Profile.IncludePreviews = true;
                using (var review = ImportOperationReview.AutomaticRequest(request, f.Discovery, volumeId: f.Resolve)) {
                    Throws<SafetyException>(() => review.Confirm()); var result = review.Execute(f.Importer, CancellationToken.None, message => {});
                    Check(review.Automatic && result.Files == 1 && result.Receipt.VideosOnly && !result.Receipt.IncludePreviews && scheduler.Take(f.Discovery.State, new[] { f.Profile }, defaults, false) == null, "Automatic copy drifted/repeated");
                }
                var legacy = new ModernImportFixture(true); legacy.Profile.AutomaticImport = true;
                Throws<SafetyException>(() => ImportOperationReview.AutomaticRequest(new AutomaticImportRequest(legacy.Connection, legacy.Profile, defaults), legacy.Discovery, volumeId: legacy.Resolve));
                Check(!Directory.EnumerateFileSystemEntries(legacy.Target).Any(), "Automatic legacy profile copied without identity");
            });
            Test("modern automatic scheduler suppresses saved choices and consumed failures until new connection", delegate {
                var f = new ModernImportFixture(); f.Profile.AutomaticImport = true; var defaults = new ImportDefaults(); var scheduler = new AutomaticImportScheduler();
                scheduler.SettingsSaved(); Check(scheduler.Take(f.Discovery.State, new[] { f.Profile }, defaults, false) == null, "Saving started work on existing connection");
                f.Discovery.State.GroupConnections["modern-usb"] = "fixture-generation-2"; var request = scheduler.Take(f.Discovery.State, new[] { f.Profile }, defaults, false); Check(request != null && scheduler.Take(f.Discovery.State, new[] { f.Profile }, defaults, false) == null, "Failure/cancel attempt was repeated");
                scheduler.PreflightWaiting(request, 1); Check(scheduler.Take(f.Discovery.State, new[] { f.Profile }, defaults, false) == null, "Unchanged preflight looped");
                f.Discovery.Version = f.Discovery.State.Epoch = 2; Check(scheduler.Take(f.Discovery.State, new[] { f.Profile }, defaults, false) != null, "Storage-event preflight did not allow fresh review");
            });
        }
    }
}
