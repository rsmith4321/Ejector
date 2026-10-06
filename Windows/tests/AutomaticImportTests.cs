using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace EasyEject {
    public static partial class Tests {
        static ImportProfile AutomaticProfile(Volume volume, Disk disk) {
            var value = new ImportDefaults().NewProfile(volume, disk); value.UsesImportDefaults = false; value.AutomaticImport = true;
            value.Name = volume.Id; value.RelativeSource = ""; value.Destination = @"Z:\Synthetic backups"; value.SavedDestination = HistoryIdentity(); return value;
        }
        static AutomaticImportRequest Admit(AutomaticImportScheduler scheduler, Inventory state, params ImportProfile[] profiles) { return scheduler.Take(state, profiles, new ImportDefaults(), false); }
        static void AutomaticImportTests() {
            Test("automatic import starts off for factory defaults new and legacy custom profiles", delegate {
                var state = CardState(); var defaults = new ImportDefaults(); var profile = defaults.NewProfile(state.Volumes[0], state.Disks[0]);
                Check(!defaults.AutomaticEnabled && !profile.AutomaticEnabled(defaults), "Factory automatically imports");
                profile.UsesImportDefaults = profile.AutomaticImport = null; Check(!profile.AutomaticEnabled(new ImportDefaults { AutomaticImport = true }), "Legacy custom profile adopted automatic defaults");
                Check(Admit(new AutomaticImportScheduler(), state, profile) == null, "Off profile started on launch");
            });
            Test("automatic defaults inheritance and custom switches freeze their resolved opt-in", delegate {
                var state = CardState(); var defaults = new ImportDefaults { AutomaticImport = true }; var p = defaults.NewProfile(state.Volumes[0], state.Disks[0]);
                Check(p.AutomaticEnabled(defaults), "Default opt-in not inherited"); p.SetCustom(true, defaults); defaults.AutomaticImport = false;
                Check(p.AutomaticEnabled(defaults), "Switching to custom lost the frozen opt-in"); p.AutomaticImport = false; p.SetCustom(false, new ImportDefaults { AutomaticImport = true });
                Check(p.AutomaticEnabled(new ImportDefaults { AutomaticImport = true }), "Switching to shared defaults retained old automatic value");
            });
            Test("automatic and destination identity fields persist with deep snapshot isolation", delegate { using (var f = new Fixture()) {
                var s = CardState(); var p = AutomaticProfile(s.Volumes[0], s.Disks[0]); Profiles.Save(p, f.Destination); ImportDefaults.Save(new ImportDefaults { AutomaticImport = true }, f.Destination);
                var entry = Profiles.Catalog(f.Destination).Single(); var copy = entry.Profile; copy.SavedDestination.DiskInstance = "tampered draft";
                Check(entry.Profile.SavedDestination.Matches(HistoryIdentity()) && Profiles.Load(s.Volumes[0], s.Disks[0], f.Destination).AutomaticImport == true && ImportDefaults.Load(f.Destination).AutomaticEnabled, "Snapshot mutated persisted destination or lost opt-in");
            } });
            Test("scheduler admits launch once and ignores repeated refreshes for the same connection", delegate {
                var state = CardState(); var scheduler = new AutomaticImportScheduler(); var p = AutomaticProfile(state.Volumes[0], state.Disks[0]);
                Check(Admit(scheduler, state, p) != null, "Enabled launch did not admit import");
                for (int i = 0; i < 5; i++) Check(Admit(scheduler, state, p) == null, "Refresh repeated import on same connection");
            });
            Test("unrelated device connection does not repeat an already attempted source", delegate {
                var state = CardState(); var scheduler = new AutomaticImportScheduler(); var p = AutomaticProfile(state.Volumes[0], state.Disks[0]); Admit(scheduler, state, p);
                state.Epoch++; state.GroupConnections["usb-b"] = "unrelated-reconnected";
                Check(Admit(scheduler, state, p) == null, "Global connection event restarted completed source");
            });
            Test("same card reconnect between snapshots admits a new source connection", delegate {
                var state = CardState(); var scheduler = new AutomaticImportScheduler(); var p = AutomaticProfile(state.Volumes[0], state.Disks[0]); Admit(scheduler, state, p);
                state.Epoch++; state.GroupConnections["usb-a"] = "same-identities-new-connection";
                Check(Admit(scheduler, state, p) != null && Admit(scheduler, state, p) == null, "Reconnect was missed or repeated");
            });
            Test("source absence clears an attempt only after complete observation", delegate {
                var state = CardState(); var scheduler = new AutomaticImportScheduler(); var p = AutomaticProfile(state.Volumes[0], state.Disks[0]); Admit(scheduler, state, p);
                var empty = new Inventory { Epoch = 2, Complete = false }; scheduler.Observe(empty); Check(Admit(scheduler, state, p) == null, "Incomplete scan forgot a source attempt");
                empty.Complete = true; scheduler.Observe(empty); Check(Admit(scheduler, state, p) != null, "Complete source absence did not re-arm a later connection");
            });
            Test("busy application defers rather than consumes another enabled source", delegate {
                var state = CardState(); var scheduler = new AutomaticImportScheduler(); var p = AutomaticProfile(state.Volumes[0], state.Disks[0]);
                Check(scheduler.Take(state, new[] { p }, new ImportDefaults(), true) == null && Admit(scheduler, state, p) != null, "Busy state started or consumed a pending source");
            });
            Test("enabled devices are admitted serially once without queueing stale guards", delegate {
                var state = CardState(); var scheduler = new AutomaticImportScheduler(); var a = AutomaticProfile(state.Volumes[0], state.Disks[0]); var b = AutomaticProfile(state.Volumes[1], state.Disks[1]);
                var first = Admit(scheduler, state, b, a); var second = Admit(scheduler, state, b, a);
                Check(first != null && second != null && first.Connection.VolumeId != second.Connection.VolumeId && Admit(scheduler, state, a, b) == null, "Serial admission duplicated or lost a device");
                Check(first.Connection.Epoch == state.Epoch && second.Connection.Epoch == state.Epoch, "Requests lost current connection generation");
            });
            Test("saving defaults and profiles suppresses current connections until a later source connection", delegate {
                var state = CardState(); var scheduler = new AutomaticImportScheduler(); var p = AutomaticProfile(state.Volumes[0], state.Disks[0]);
                scheduler.SettingsSaved(); Check(Admit(scheduler, state, p) == null, "Saving enabled choices started current source");
                state.Epoch++; state.GroupConnections["usb-b"] = "other-event"; Check(Admit(scheduler, state, p) == null, "Unrelated event defeated save suppression");
                state.Epoch++; state.GroupConnections["usb-a"] = "next-source"; Check(Admit(scheduler, state, p) != null, "Next source connection did not use saved choices");
            });
            Test("save suppression survives incomplete inventory and covers connections arriving during the editor", delegate {
                var state = CardState(); var scheduler = new AutomaticImportScheduler(); var p = AutomaticProfile(state.Volumes[0], state.Disks[0]);
                scheduler.SettingsSaved(); state.Complete = false; scheduler.Observe(state); Check(Admit(scheduler, state, p) == null, "Incomplete state started automatic work");
                state.Complete = true; state.GroupConnections["usb-a"] = "connected-during-dialog"; state.Epoch++;
                Check(Admit(scheduler, state, p) == null, "Saving started a device that arrived during configuration");
                state.GroupConnections["usb-a"] = "connected-after-save"; state.Epoch++; Check(Admit(scheduler, state, p) != null, "Later fresh connection stayed suppressed");
            });
            Test("preflight wait retries only after a storage event while runtime attempts stay consumed", delegate {
                var state = CardState(); var scheduler = new AutomaticImportScheduler(); var p = AutomaticProfile(state.Volumes[0], state.Disks[0]); var request = Admit(scheduler, state, p); scheduler.PreflightWaiting(request);
                Check(Admit(scheduler, state, p) == null, "Preflight failure looped without event"); state.Epoch++; state.GroupConnections["usb-b"] = "destination-arrived";
                Check(Admit(scheduler, state, p) != null && Admit(scheduler, state, p) == null, "Storage event failed to retry wait or looped after runtime admission");
                state.Epoch++; Check(Admit(scheduler, state, p) == null, "Runtime failure gained waiting retry permission");
            });
            Test("manual start or cancellation consumes the source's automatic attempt for this connection", delegate {
                var state = CardState(); var scheduler = new AutomaticImportScheduler(); var p = AutomaticProfile(state.Volumes[0], state.Disks[0]); scheduler.MarkManual(state, state.Volumes[0]);
                Check(Admit(scheduler, state, p) == null, "Manual choice was followed by automatic import"); state.Epoch++; state.GroupConnections["usb-a"] = "next-source"; Check(Admit(scheduler, state, p) != null, "Manual choice suppressed every future connection");
            });
            Test("automatic request freezes profile default media removal prompt and destination choices", delegate {
                var state = CardState(); var defaults = new ImportDefaults { AutomaticImport = true, IncludePreviews = false, AskToEject = true, DeleteOriginals = true, RecoverTrash = true }; var p = defaults.NewProfile(state.Volumes[0], state.Disks[0]); p.Destination = @"Z:\Synthetic backups"; p.SavedDestination = HistoryIdentity(); p.MacTrashUid = "501";
                var request = new AutomaticImportScheduler().Take(state, new[] { p }, defaults, false);
                defaults.AutomaticImport = defaults.VideosOnly = defaults.AskToEject = defaults.DeleteOriginals = defaults.RecoverTrash = false; defaults.IncludePreviews = true; p.SavedDestination.DiskInstance = "changed"; p.RelativeSource = "Other";
                var mutableCopy = request.Profile; mutableCopy.SavedDestination.DiskInstance = "changed again";
                Check(request.Options.VideosOnly && !request.Options.IncludePreviews && request.Options.AskToEject && request.Options.DeleteOriginals && request.Options.RecoverTrash && request.Options.MacTrashUid == "501" && request.Profile.RelativeSource == "" && request.Profile.SavedDestination.Matches(HistoryIdentity()), "Running request adopted later choices or alias mutations");
            });
            Test("automatic admission refuses changed source serial disk mount or connection before copying", delegate {
                foreach (Action<Inventory> change in new Action<Inventory>[] { s => s.Volumes[0].Serial = "formatted", s => s.Disks[0].InstanceId = "replacement", s => s.Volumes[0].Mounts[0] = @"Q:\", s => s.GroupConnections["usb-a"] = "reconnected", s => s.Epoch++, s => s.Complete = false }) {
                    var state = CardState(); var request = Admit(new AutomaticImportScheduler(), state, AutomaticProfile(state.Volumes[0], state.Disks[0])); change(state);
                    Throws<SafetyException>(() => request.ValidateAdmission(state, HistoryIdentity()));
                }
            });
            Test("enrolled source identity mismatch and absent legacy destination refuse automatic admission", delegate {
                var state = CardState(); var p = AutomaticProfile(state.Volumes[0], state.Disks[0]); p.VolumeSerial = "old-before-format";
                var request = Admit(new AutomaticImportScheduler(), state, p); Throws<SafetyException>(() => request.ValidateAdmission(state, HistoryIdentity()));
                p.VolumeSerial = state.Volumes[0].Serial; p.SavedDestination = null; request = Admit(new AutomaticImportScheduler(), state, p);
                Throws<SafetyException>(() => request.ValidateAdmission(state, HistoryIdentity())); ProfileDestination.Require(p, HistoryIdentity(), false);
            });
            Test("saved destination refuses changed volume serial physical disk and disk serial", delegate {
                var state = CardState(); var p = AutomaticProfile(state.Volumes[0], state.Disks[0]);
                foreach (Action<DestinationIdentity> change in new Action<DestinationIdentity>[] { d => d.VolumeId = "another-volume", d => d.VolumeSerial = "formatted-destination", d => d.DiskInstance = "replacement-disk", d => d.DiskSerial = "replacement-serial" }) {
                    var target = HistoryIdentity(); change(target); Throws<SafetyException>(() => ProfileDestination.Require(p, target, true)); Throws<SafetyException>(() => ProfileDestination.Require(p, target, false));
                }
            });
            Test("automatic scheduler never admits protected internal spanning unknown or untracked sources", delegate {
                foreach (Action<Inventory> change in new Action<Inventory>[] { s => s.Volumes[0].System = true, s => { s.Disks[0].TargetId = ""; s.Disks[0].Removable = s.Disks[0].Hotplug = false; }, s => s.Volumes[0].Disks.Add(8), s => s.Volumes[0].Error = "mapping failed", s => s.Disks[0].Error = "unknown", s => s.GroupConnections.Clear(), s => s.Volumes[0].Mounts.Clear(), s => s.Complete = false }) {
                    var state = CardState(); var p = AutomaticProfile(state.Volumes[0], state.Disks[0]); change(state); Check(Admit(new AutomaticImportScheduler(), state, p) == null, "Unsafe/untracked source admitted");
                }
            });
            Test("externally enrolled noncard and non-ejectable sources retain automatic import parity", delegate {
                var state = CardState(); var p = AutomaticProfile(state.Volumes[0], state.Disks[0]); state.Volumes[0].Group = StorageGroup.External; state.Disks[0].TargetId = ""; state.Disks[0].Hotplug = true; state.GroupConnections[state.Disks[0].InstanceId] = "own-instance-stamp";
                Check(Admit(new AutomaticImportScheduler(), state, p) != null, "Automatic scheduler narrowed enrollment to camera/ejectable cards");
            });
            Test("offline destination rebinding requires a fresh explicit different-disk selection", delegate { using (var f = new Fixture()) {
                var state = CardState(); var p = AutomaticProfile(state.Volumes[0], state.Disks[0]); Profiles.Save(p, f.Destination); var entry = Profiles.Catalog(f.Destination).Single(); var draft = entry.Profile; draft.Destination = @"Y:\New backups";
                Throws<SafetyException>(() => Profiles.SaveOffline(entry, draft, f.Destination)); var selected = HistoryIdentity(); selected.VolumeId = "new-destination"; selected.VolumeSerial = "new-serial";
                Profiles.SaveOffline(entry, draft, f.Destination, selected); var saved = Profiles.Catalog(f.Destination).Single().Profile;
                Check(saved.Destination == draft.Destination && saved.SavedDestination.Matches(selected), "Explicit destination folder/identity did not persist"); selected.DiskInstance = p.DiskInstance;
                Throws<SafetyException>(() => Profiles.SaveOffline(Profiles.Catalog(f.Destination).Single(), saved, f.Destination, selected));
            } });
            Test("offline same-folder destination re-selection can explicitly bind a legacy profile", delegate { using (var f = new Fixture()) {
                var p = OfflineProfile(); Profiles.Save(p, f.Destination); var entry = Profiles.Catalog(f.Destination).Single(); var draft = entry.Profile;
                Profiles.SaveOffline(entry, draft, f.Destination, HistoryIdentity()); Check(Profiles.Catalog(f.Destination).Single().Profile.SavedDestination.Matches(HistoryIdentity()), "Same path explicit selection could not migrate destination binding");
            } });
            Test("offline editor shows inherited automatic mode and refuses unbound legacy enabling", delegate {
                var p = OfflineProfile(); var defaults = new ImportDefaults { AutomaticImport = true };
                using (var dialog = new ImportProfileDialog(p, null, "Saved device", defaults, true)) {
                    Check(dialog.Selection.Automatic.Checked && !dialog.Selection.Enabled, "Inherited automatic mode was editable or hidden"); Throws<SafetyException>(() => dialog.ApplyEdits());
                    dialog.UseCustom.Checked = true; dialog.Selection.Automatic.Checked = false; dialog.ApplyEdits(); Check(dialog.Draft.AutomaticImport == false && !p.AutomaticEnabled(new ImportDefaults()), "Cancel/custom mode mutated saved opt-in");
                }
            });
            Test("automatic UI choices remain separate from enabling permanent removal or Trash recovery", delegate {
                var p = AutomaticProfile(CardState().Volumes[0], CardState().Disks[0]); p.AutomaticImport = false;
                using (var dialog = new ImportProfileDialog(p, null, "Saved device", new ImportDefaults(), true)) {
                    int confirmations = 0; dialog.Selection.ConfirmRemoval = dialog.Selection.ConfirmRecovery = () => { confirmations++; return false; };
                    dialog.Selection.Automatic.Checked = true; dialog.ApplyEdits(); Check(dialog.Draft.AutomaticImport == true && !dialog.Draft.Resolve(new ImportDefaults()).DeleteOriginals && !dialog.Draft.Resolve(new ImportDefaults()).RecoverTrash && confirmations == 0, "Automatic checkbox broadened removal authority");
                }
                using (var dialog = new ImportDefaultsDialog(new ImportDefaults { AutomaticImport = true })) { Check(dialog.Value.AutomaticEnabled && !dialog.Value.Snapshot().DeleteOriginals && !dialog.Value.Snapshot().RecoverTrash, "Shared automatic choice enabled removal"); }
            });
            Test("admitted automatic fixture copies frozen media choices and reuses exact verified originals", delegate { using (var f = new Fixture()) {
                f.Put("clip.mp4", "synthetic recording"); f.Put("clip.lrv", "synthetic preview"); f.Put("photo.jpg", "synthetic photo");
                var state = CardState(); state.Volumes[0].Mounts[0] = f.Source; var p = AutomaticProfile(state.Volumes[0], state.Disks[0]); p.Destination = f.Destination; p.IncludePreviews = false;
                var scheduler = new AutomaticImportScheduler(); var request = Admit(scheduler, state, p); request.ValidateAdmission(state, HistoryIdentity());
                var result = new VerifiedImporter(new Operations()).Run(f.Source, f.Source, f.Destination, p.VolumeId, new FakeGuard(), CancellationToken.None, x => {}, request.Options);
                Check(result.Files == 1 && result.HasSelectedMedia && result.RemovedOriginals == 0 && Directory.GetFiles(f.Source).Length == 3 && Admit(scheduler, state, p) == null, "Automatic fixture broadened selection or repeated source");
                state.Epoch++; state.GroupConnections["usb-a"] = "next source"; request = Admit(scheduler, state, p); request.ValidateAdmission(state, HistoryIdentity());
                var repeated = new VerifiedImporter(new Operations()).Run(f.Source, f.Source, f.Destination, p.VolumeId, new FakeGuard(), CancellationToken.None, x => {}, request.Options);
                Check(repeated.Reused == 1 && result.Receipt.Files[0].RelativePath == repeated.Receipt.Files[0].RelativePath, "Automatic reconnect duplicated a matching verified group");
            } });
            Test("automatic empty selection does not create media history or eject authority", delegate { using (var f = new Fixture()) {
                f.Put("photo.jpg", "excluded synthetic photo"); var state = CardState(); state.Volumes[0].Mounts[0] = f.Source; var p = AutomaticProfile(state.Volumes[0], state.Disks[0]); p.Destination = f.Destination;
                var request = Admit(new AutomaticImportScheduler(), state, p); request.ValidateAdmission(state, HistoryIdentity());
                var result = new VerifiedImporter(new Operations()).Run(f.Source, f.Source, f.Destination, p.VolumeId, new FakeGuard(), CancellationToken.None, x => {}, request.Options);
                Check(!result.HasSelectedMedia && result.Files == 0 && Directory.GetFiles(f.Destination).Length == 0 && File.Exists(Path.Combine(f.Source, "photo.jpg")) && !request.Options.AskToEject, "Empty automatic result supplied media/removal authority");
            } });
            Test("automatic runtime cancellation preserves originals releases reservations and never loops", delegate { using (var f = new Fixture()) {
                f.Put("clip.mp4", new string('v', 1048576)); var state = CardState(); state.Volumes[0].Mounts[0] = f.Source; var p = AutomaticProfile(state.Volumes[0], state.Disks[0]); p.Destination = f.Destination;
                var scheduler = new AutomaticImportScheduler(); var request = Admit(scheduler, state, p); var cancellation = new CancellationTokenSource(); cancellation.Cancel(); var operations = new Operations();
                Throws<OperationCanceledException>(() => new VerifiedImporter(operations).Run(f.Source, f.Source, f.Destination, p.VolumeId, new FakeGuard(), cancellation.Token, x => {}, request.Options));
                using (operations.Reserve(new[] { 700, 701 })) {} state.Epoch++;
                Check(Admit(scheduler, state, p) == null && File.Exists(Path.Combine(f.Source, "clip.mp4")) && Directory.GetFiles(f.Destination, "*.partial", SearchOption.AllDirectories).Length == 0, "Cancelled runtime retried or lost source/locks/private work");
            } });
            Test("automatic acknowledged removal uses the same guarded whole-batch engine", delegate { using (var f = new Fixture()) {
                f.Put("clip.mp4", "synthetic disposable recording"); f.Put("photo.jpg", "retained synthetic photo"); f.Put("unknown.foo", "retained unknown");
                var state = CardState(); state.Volumes[0].Mounts[0] = f.Source; var p = AutomaticProfile(state.Volumes[0], state.Disks[0]); p.Destination = f.Destination; p.DeleteOriginals = true;
                var scheduler = new AutomaticImportScheduler(); var request = Admit(scheduler, state, p); request.ValidateAdmission(state, HistoryIdentity());
                var result = new VerifiedImporter(new Operations()).Run(f.Source, f.Source, f.Destination, p.VolumeId, new EvidenceGuard(), CancellationToken.None, x => {}, request.Options);
                Check(result.RemovedOriginals == 1 && File.Exists(Path.Combine(f.Source, "photo.jpg")) && File.Exists(Path.Combine(f.Source, "unknown.foo")) && result.Receipt.OriginalRemovalRequested && Admit(scheduler, state, p) == null, "Automatic path bypassed guarded removal or eligibility");
            } });
            Test("read-only device window cannot pump automatic import or change profiles", delegate {
                using (var window = new DeviceWindow(true)) {
                    IntPtr handle = window.Handle; window.PresentInventory(CardState());
                    var pump = typeof(DeviceWindow).GetMethod("PumpAutomaticImports", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                    ((Task)pump.Invoke(window, new object[0])).GetAwaiter().GetResult();
                    var scheduler = (AutomaticImportScheduler)typeof(DeviceWindow).GetField("automatic", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance).GetValue(window);
                    var state = CardState(); var p = AutomaticProfile(state.Volumes[0], state.Disks[0]); Check(Admit(scheduler, state, p) != null, "Read-only pump admitted a profile");
                    var button = (Button)typeof(DeviceWindow).GetField("savedProfiles", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance).GetValue(window); Check(!button.Enabled && handle != IntPtr.Zero && !window.Visible, "Read-only window offered profile writes or visible testing");
                }
            });
        }
    }
}
