using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Windows.Forms;

namespace EasyEject {
    public static partial class Tests {
        static string ManualShareSetup(Fixture f, CompletedBatch batch, bool enabled = true, QuickShareTools tools = null) {
            string root = Path.Combine(f.Root, "manual-share-config"); Directory.CreateDirectory(root);
            var profile = new ImportProfile { Name = batch.DeviceName, VolumeId = batch.ProfileId, VolumeSerial = "fixture-serial", DiskInstance = "fixture-source-disk", RelativeSource = "", Destination = f.Destination, SavedDestination = batch.Destination.Copy(), SharingPreset = enabled ? QuickShareProfile.Hd : null, SharingColor = QuickShareProfile.Standard };
            Profiles.Save(profile, root); ImportHistory.Add(batch, root); tools = tools ?? FixtureShareTools(f);
            QuickShareConfiguration.Save(QuickShareConfiguration.Load(root), tools.Ffmpeg, tools.Ffprobe, null, root, CancellationToken.None); return root;
        }
        static ManualQuickShareService ManualShares(string root, IQuickShareProcess process, Operations operations = null, Func<CompletedBatch, IImportGuard> guard = null) {
            return new ManualQuickShareService(root, operations ?? new Operations(), process, guard ?? (_ => new FakeGuard()), _ => UInt64.MaxValue);
        }
        static ImportCompletion ShareCompletion(CompletedBatch batch) {
            var state = State(); state.Volumes[0].Id = batch.ProfileId;
            return new ImportCompletion(new ImportCompletionOrigin(state, batch.ProfileId, state.Volumes[0].Mounts[0]), batch.DeviceName, batch.Folder, batch.DestinationRoot, batch.Destination, true, "Fixture import verified.", batch);
        }
        static void ManualQuickShareTests() {
            Test("manual Quick Share enabled-after-import offers retained history and disabling or forgetting hides it", delegate { using (var f = new Fixture()) {
                var batch = LunaBatch(f); string root = ManualShareSetup(f, batch, false); var process = new FakeShareProcess(); var service = ManualShares(root, process);
                Check(!service.CanOffer(batch) && ManualQuickShareService.EligibleProfile(batch, Profiles.Catalog(root)) == null, "Off profile offers sharing"); Throws<SafetyException>(() => service.Prepare(batch));
                var entry = Profiles.Catalog(root).Single(); var enabled = entry.Profile; enabled.SharingPreset = QuickShareProfile.Uhd; Profiles.SaveOffline(entry, enabled, root);
                Check(service.CanOffer(batch) && service.Prepare(batch).Profile.SharingPreset == QuickShareProfile.Uhd && process.Encodes == 0 && !Directory.Exists(Path.Combine(batch.Folder, "Sharing Copies")), "Enable started work or omitted earlier batch");
                entry = Profiles.Catalog(root).Single(); var disabled = entry.Profile; disabled.SharingPreset = null; Profiles.SaveOffline(entry, disabled, root); Check(!service.CanOffer(batch), "Disable kept offer");
                Profiles.ForgetOffline(Profiles.Catalog(root).Single(), root); Check(!service.CanOffer(batch) && ImportHistory.Load(root).Single().Id == batch.Id, "Forget preserved action or deleted history");
            } });
            Test("manual Quick Share prepare and cancelled prompt do not probe encode publish or consume completion", delegate { using (var f = new Fixture()) {
                var batch = LunaBatch(f); string root = ManualShareSetup(f, batch); var process = new FakeShareProcess(); var service = ManualShares(root, process); var completion = ShareCompletion(batch); var registry = new ImportCompletionRegistry(); registry.Insert(completion);
                var offer = service.Prepare(batch); using (var dialog = new QuickSharePromptDialog(batch.DeviceName, batch.Folder, offer.Profile)) dialog.DialogResult = DialogResult.Cancel;
                Check(process.Probes == 0 && process.Encodes == 0 && registry.Get(completion.Id) == completion && !Directory.Exists(Path.Combine(batch.Folder, "Sharing Copies")), "Preparation/cancel performed work or consumed choice");
            } });
            Test("manual Quick Share uses chosen size color and exact batch after source absence and setup reload", delegate { using (var f = new Fixture()) {
                var batch = LunaBatch(f); string root = ManualShareSetup(f, batch); File.Delete(Path.Combine(f.Source, "VID_fixture.mp4"));
                var process = new FakeShareProcess(); var service = ManualShares(root, process); var request = service.Prepare(ImportHistory.Load(root).Single()).Choose(QuickSharePreset.Uhd, QuickShareColor.Standard);
                var result = service.Run(request, CancellationToken.None, _ => {});
                Check(result.Created == 1 && result.Outputs.Single().EndsWith("-sharing-4K.mp4") && process.Encodes == 1 && process.Decodes == 1 && process.Inputs.Single() == batch.Originals.Single().Path, "Explicit choices or source-independent exact paths lost");
                VerifyShareOriginals(batch); CheckSharePrivateGone(batch);
            } });
            Test("manual Quick Share full saved-selection native helper orchestration publishes and reuses only verified copies", delegate { using (var f = new Fixture()) {
                var batch = LunaBatch(f); string folder = Path.Combine(f.Root, "manual-native-helper"); Directory.CreateDirectory(folder); string helper = Path.Combine(folder, "EasyEject.ShareFixture.exe"); File.Copy(ShareHelper, helper);
                string root = ManualShareSetup(f, batch, tools: new QuickShareTools(helper, helper)); var service = ManualShares(root, new WindowsQuickShareProcess());
                var result = service.Run(service.Prepare(batch).Choose(QuickSharePreset.Hd, QuickShareColor.Standard), CancellationToken.None, _ => {});
                Check(result.Created == 1 && File.ReadAllText(result.Outputs.Single()).StartsWith("SYNTHETIC_SHARE|1080p|"), "Native manual workflow failed");
                result = service.Run(service.Prepare(batch).Choose(QuickSharePreset.Hd, QuickShareColor.Standard), CancellationToken.None, _ => {});
                Check(result.Created == 0 && result.Reused == 1, "Fresh manual action failed reuse"); VerifyShareOriginals(batch); CheckSharePrivateGone(batch);
            } });
            Test("manual Quick Share offer and request copies isolate paths identities preferences and tool selection", delegate { using (var f = new Fixture()) {
                var batch = LunaBatch(f); string root = ManualShareSetup(f, batch); var process = new FakeShareProcess(); var service = ManualShares(root, process); var offer = service.Prepare(batch);
                offer.Batch.Originals.Clear(); offer.Profile.SharingPreset = null; offer.Tools.Value.Ffmpeg.Path = "changed";
                var request = offer.Choose(QuickSharePreset.Hd, QuickShareColor.Standard); request.Batch.Originals[0].Path = "changed"; batch.DeviceName = "caller mutation";
                var result = service.Run(request, CancellationToken.None, _ => {}); Check(result.Created == 1 && process.Inputs.Single() == request.Batch.Originals.Single().Path && request.Batch.DeviceName != "caller mutation", "Immutable request changed");
            } });
            Test("manual Quick Share profile preference edits during a prompt never replace explicit export choices", delegate { using (var f = new Fixture()) {
                var batch = LunaBatch(f); string root = ManualShareSetup(f, batch); var process = new FakeShareProcess(); var service = ManualShares(root, process); var request = service.Prepare(batch).Choose(QuickSharePreset.Hd, QuickShareColor.Standard);
                var entry = Profiles.Catalog(root).Single(); var p = entry.Profile; p.SharingPreset = QuickShareProfile.Uhd; p.SharingColor = QuickShareProfile.LunaILog; Profiles.SaveOffline(entry, p, root);
                var result = service.Run(request, CancellationToken.None, _ => {}); Check(result.Outputs.Single().EndsWith("-sharing-1080p.mp4") && File.ReadAllText(result.Outputs.Single()).Contains(" Standard "), "Preference edits changed explicit choice or required unrelated LUT");
            } });
            Test("manual Quick Share profile disabled during a prompt refuses before any process or sharing folder", delegate { using (var f = new Fixture()) {
                var batch = LunaBatch(f); string root = ManualShareSetup(f, batch); var process = new FakeShareProcess(); var service = ManualShares(root, process); var request = service.Prepare(batch).Choose(QuickSharePreset.Hd, QuickShareColor.Standard);
                var entry = Profiles.Catalog(root).Single(); var p = entry.Profile; p.SharingPreset = null; Profiles.SaveOffline(entry, p, root);
                Throws<SafetyException>(() => service.Run(request, CancellationToken.None, _ => {})); Check(process.Probes == 0 && !Directory.Exists(Path.Combine(batch.Folder, "Sharing Copies")), "Disabled admission reached media");
            } });
            Test("manual Quick Share profile forgotten or corrupted during a prompt refuses and keeps history", delegate { using (var f = new Fixture()) {
                var batch = LunaBatch(f); string root = ManualShareSetup(f, batch); var process = new FakeShareProcess(); var service = ManualShares(root, process); var request = service.Prepare(batch).Choose(QuickSharePreset.Hd, QuickShareColor.Standard);
                var entry = Profiles.Catalog(root).Single(); File.WriteAllText(Path.Combine(root, entry.FileName), "invalid fixture JSON"); Throws<SafetyException>(() => service.Run(request, CancellationToken.None, _ => {}));
                File.Delete(Path.Combine(root, entry.FileName)); Throws<SafetyException>(() => service.Run(request, CancellationToken.None, _ => {})); Check(ImportHistory.Load(root).Single().Id == batch.Id && process.Encodes == 0, "Unavailable profile removed history or admitted work");
            } });
            Test("manual Quick Share tools changed during a prompt refuse even when profile and history remain valid", delegate { using (var f = new Fixture()) {
                var batch = LunaBatch(f); string root = ManualShareSetup(f, batch); var process = new FakeShareProcess(); var service = ManualShares(root, process); var request = service.Prepare(batch).Choose(QuickSharePreset.Hd, QuickShareColor.Standard);
                var config = QuickShareConfiguration.Load(root); QuickShareConfiguration.Save(config, config.Value.Ffmpeg.Path, config.Value.Ffprobe.Path, null, root, CancellationToken.None);
                Throws<SafetyException>(() => service.Run(request, CancellationToken.None, _ => {})); Check(process.Probes == 0, "Stale setup reached a process");
            } });
            Test("manual Quick Share changed executable refuses and releases setup handles before manual reselection", delegate { using (var f = new Fixture()) {
                var batch = LunaBatch(f); string root = ManualShareSetup(f, batch); var process = new FakeShareProcess(); var service = ManualShares(root, process); var request = service.Prepare(batch).Choose(QuickSharePreset.Hd, QuickShareColor.Standard);
                string tool = QuickShareConfiguration.Load(root).Value.Ffmpeg.Path; File.AppendAllText(tool, "changed"); Throws<SafetyException>(() => service.Run(request, CancellationToken.None, _ => {}));
                var config = QuickShareConfiguration.Load(root); QuickShareConfiguration.Save(config, tool, config.Value.Ffprobe.Path, null, root, CancellationToken.None);
                var result = service.Run(service.Prepare(batch).Choose(QuickSharePreset.Hd, QuickShareColor.Standard), CancellationToken.None, _ => {}); Check(result.Created == 1 && process.Encodes == 1, "Explicit retry after reselection failed");
            } });
            Test("manual Quick Share changed history manifest after prompt refuses rather than accepting the same id", delegate { using (var f = new Fixture()) {
                var batch = LunaBatch(f); string root = ManualShareSetup(f, batch); var process = new FakeShareProcess(); var service = ManualShares(root, process); var request = service.Prepare(batch).Choose(QuickSharePreset.Hd, QuickShareColor.Standard);
                var history = ImportHistory.Load(root); history[0].DeviceName = "changed history metadata"; Profiles.WriteJson(history, history.GetType(), root, "completed-imports.json");
                Check(!service.CanOffer(batch), "Changed snapshot still offered"); Throws<SafetyException>(() => service.Run(request, CancellationToken.None, _ => {})); Check(process.Probes == 0, "Same id bypassed exact admission");
            } });
            Test("manual Quick Share last-20 retention refuses older requests without rescanning retained destination folders", delegate { using (var f = new Fixture()) {
                var batch = LunaBatch(f); string root = ManualShareSetup(f, batch); var process = new FakeShareProcess(); var service = ManualShares(root, process); var request = service.Prepare(batch).Choose(QuickSharePreset.Hd, QuickShareColor.Standard);
                for (int i = 0; i < 20; i++) { var newer = ImportHistory.Clone(batch); newer.Id = Guid.NewGuid().ToString("N"); newer.ReceiptPath = Path.Combine(newer.Folder, ".easy-eject-receipts", newer.Id, "verified-import.json"); ImportHistory.Add(newer, root); }
                Check(!service.CanOffer(batch) && ImportHistory.Load(root).Count == 20 && File.Exists(batch.Originals[0].Path), "Old batch not expired or saved original deleted");
                Throws<SafetyException>(() => service.Run(request, CancellationToken.None, _ => {})); Check(process.Probes == 0 && !Directory.Exists(Path.Combine(batch.Folder, "Sharing Copies")), "Old folders were rescanned");
            } });
            Test("manual Quick Share changed saved original refuses before media tools and preserves receipt history", delegate { using (var f = new Fixture()) {
                var batch = LunaBatch(f); string root = ManualShareSetup(f, batch); var process = new FakeShareProcess(); var service = ManualShares(root, process); var request = service.Prepare(batch).Choose(QuickSharePreset.Hd, QuickShareColor.Standard);
                File.AppendAllText(batch.Originals[0].Path, "changed original"); Throws<QuickShareFailure>(() => service.Run(request, CancellationToken.None, _ => {}));
                Check(process.Probes == 0 && ImportHistory.Load(root).Single().Id == batch.Id && File.ReadAllText(batch.Originals[0].Path).EndsWith("changed original"), "Changed original repaired/deleted or probed");
            } });
            Test("manual Quick Share changed destination and occupied destination reservations refuse before encoding", delegate { using (var f = new Fixture()) {
                var batch = LunaBatch(f); string root = ManualShareSetup(f, batch); var process = new FakeShareProcess(); var operations = new Operations();
                var failed = ManualShares(root, process, operations, _ => new FakeGuard { FailAt = 1 }); var request = failed.Prepare(batch).Choose(QuickSharePreset.Hd, QuickShareColor.Standard);
                Throws<SafetyException>(() => failed.Run(request, CancellationToken.None, _ => {}));
                var service = ManualShares(root, process, operations); using (operations.Reserve(new[] { 701 })) Throws<QuickShareFailure>(() => service.Run(service.Prepare(batch).Choose(QuickSharePreset.Hd, QuickShareColor.Standard), CancellationToken.None, _ => {}));
                Check(process.Encodes == 0 && !Directory.Exists(Path.Combine(batch.Folder, "Sharing Copies")), "Destination failure reached encoding"); using (operations.Reserve(new[] { 700, 701 })) {}
            } });
            Test("manual Quick Share precancel starts no work and a later explicit request can succeed", delegate { using (var f = new Fixture()) using (var stop = new CancellationTokenSource()) {
                var batch = LunaBatch(f); string root = ManualShareSetup(f, batch); var process = new FakeShareProcess(); var service = ManualShares(root, process); var request = service.Prepare(batch).Choose(QuickSharePreset.Hd, QuickShareColor.Standard); stop.Cancel();
                Throws<OperationCanceledException>(() => service.Run(request, stop.Token, _ => {})); Check(process.Encodes == 0 && !Directory.Exists(Path.Combine(batch.Folder, "Sharing Copies")), "Precancellation reached work");
                Check(service.Run(service.Prepare(batch).Choose(QuickSharePreset.Hd, QuickShareColor.Standard), CancellationToken.None, _ => {}).Created == 1, "Cancelled service stayed busy");
            } });
            Test("manual Quick Share active cancellation preserves history originals and permits a fresh manual retry", delegate { using (var f = new Fixture()) using (var stop = new CancellationTokenSource()) {
                var batch = LunaBatch(f); string root = ManualShareSetup(f, batch); var process = new FakeShareProcess(); var service = ManualShares(root, process); process.OnStage = (stage, path) => { if (stage == "After encode") stop.Cancel(); };
                var failure = Throws<QuickShareFailure>(() => service.Run(service.Prepare(batch).Choose(QuickSharePreset.Hd, QuickShareColor.Standard), stop.Token, _ => {}));
                Check(failure.Cancelled && failure.Partial.PublishedCopies == 0 && !QuickSharePresentation.CancelNeedsAttention(failure) && ImportHistory.Load(root).Single().Id == batch.Id, "Cancel publication/history mismatch"); VerifyShareOriginals(batch); CheckSharePrivateGone(batch);
                process.OnStage = (stage, path) => {}; Check(service.Run(service.Prepare(batch).Choose(QuickSharePreset.Hd, QuickShareColor.Standard), CancellationToken.None, _ => {}).Created == 1, "Manual retry failed");
            } });
            Test("manual Quick Share busy admission refuses duplicate work and reserves only the supplied destination guard", delegate { using (var f = new Fixture()) {
                var batch = LunaBatch(f); string root = ManualShareSetup(f, batch); var process = new FakeShareProcess(); var operations = new Operations(); var service = ManualShares(root, process, operations); var request = service.Prepare(batch).Choose(QuickSharePreset.Hd, QuickShareColor.Standard); int checks = 0;
                process.OnStage = (stage, path) => { if (stage == "Before encode") { Throws<SafetyException>(() => service.Prepare(batch)); Throws<SafetyException>(() => service.Run(request, CancellationToken.None, _ => {})); Throws<SafetyException>(() => operations.Reserve(new[] { 701 })); using (operations.Reserve(new[] { 799 })) {} checks++; } };
                Check(service.Run(request, CancellationToken.None, _ => {}).Created == 1 && process.Encodes == 1 && checks == 1, "Duplicate work or wrong reservations"); using (operations.Reserve(new[] { 700, 701 })) {}
            } });
            Test("manual Quick Share validation failure retains retry history and a fresh manual retry succeeds", delegate { using (var f = new Fixture()) {
                var batch = LunaBatch(f); string root = ManualShareSetup(f, batch); var process = new FakeShareProcess(); var service = ManualShares(root, process); process.ChangeOutput = output => output.Video.Width++;
                var failure = Throws<QuickShareFailure>(() => service.Run(service.Prepare(batch).Choose(QuickSharePreset.Hd, QuickShareColor.Standard), CancellationToken.None, _ => {}));
                Check(!failure.Cancelled && failure.Partial.PublishedCopies == 0 && service.CanOffer(batch), "Failure lost history or published invalid output"); VerifyShareOriginals(batch);
                process.ChangeOutput = _ => {}; Check(service.Run(service.Prepare(batch).Choose(QuickSharePreset.Hd, QuickShareColor.Standard), CancellationToken.None, _ => {}).Created == 1, "Explicit retry failed");
            } });
            Test("manual Quick Share explicit I-Log choice requires an official LUT while a fresh standard-color choice succeeds", delegate { using (var f = new Fixture()) {
                var batch = LunaBatch(f); string root = ManualShareSetup(f, batch); var process = new FakeShareProcess(); var service = ManualShares(root, process);
                Throws<SafetyException>(() => service.Run(service.Prepare(batch).Choose(QuickSharePreset.Hd, QuickShareColor.LunaILog), CancellationToken.None, _ => {})); Check(process.Encodes == 0, "Missing official LUT reached work");
                Check(service.Run(service.Prepare(batch).Choose(QuickSharePreset.Hd, QuickShareColor.Standard), CancellationToken.None, _ => {}).Created == 1, "Fresh standard choice failed");
            } });
            Test("manual Quick Share unsupported-media success creates no sharing folder and does not claim copies", delegate { using (var f = new Fixture()) {
                f.Put("other.mp4", "synthetic unrelated camera"); var batch = Batch(f, f.Run(_ => {})); string root = ManualShareSetup(f, batch); var process = new FakeShareProcess(); var service = ManualShares(root, process);
                var result = service.Run(service.Prepare(batch).Choose(QuickSharePreset.Hd, QuickShareColor.Standard), CancellationToken.None, _ => {}); string summary = QuickSharePresentation.Success(batch, QuickSharePreset.Hd, result);
                Check(result.Created == 0 && result.Reused == 0 && process.Probes == 0 && !Directory.Exists(result.Folder) && summary.Contains("no supported") && summary.Contains("Import folder:"), "Unsupported media created work or false success");
            } });
            Test("manual Quick Share completion binds an immutable exact saved batch independently of process eject id", delegate { using (var f = new Fixture()) {
                var batch = LunaBatch(f); var completion = ShareCompletion(batch); Check(completion.Id != batch.Id && completion.Batch.Id == batch.Id, "Batch and process authority confused");
                batch.Originals.Clear(); completion.Batch.Destination.VolumeSerial = "changed"; Check(completion.Batch.Originals.Count == 1 && completion.Batch.Destination.VolumeSerial == HistoryIdentity().VolumeSerial, "Completion batch mutated");
            } });
            Test("manual Quick Share completion refuses another profile folder destination and metadata-only binding", delegate { using (var f = new Fixture()) {
                var batch = LunaBatch(f); var state = State(); state.Volumes[0].Id = batch.ProfileId; var origin = new ImportCompletionOrigin(state, batch.ProfileId, state.Volumes[0].Mounts[0]);
                foreach (Action<CompletedBatch> change in new Action<CompletedBatch>[] { b => b.ProfileId = "other-device", b => b.Destination.VolumeSerial = "other-destination" }) { var bad = ImportHistory.Clone(batch); change(bad); Throws<SafetyException>(() => new ImportCompletion(origin, batch.DeviceName, batch.Folder, batch.DestinationRoot, batch.Destination, true, "Fixture", bad)); }
                Throws<SafetyException>(() => new ImportCompletion(origin, batch.DeviceName, f.Destination, f.Destination, batch.Destination, true, "Fixture", batch));
                Throws<SafetyException>(() => new ImportCompletion(origin, batch.DeviceName, batch.Folder, batch.DestinationRoot, batch.Destination, false, "Fixture", batch));
            } });
            Test("manual Quick Share clean success returns only to the same unconsumed process completion", delegate { using (var f = new Fixture()) {
                var batch = LunaBatch(f); var completion = ShareCompletion(batch); var registry = new ImportCompletionRegistry(); registry.Insert(completion);
                Check(QuickSharePresentation.ReturnToCompletion(registry, completion, true) && !QuickSharePresentation.ReturnToCompletion(registry, completion, false), "Failure/cancel returned to eject choice");
                registry.InvalidateAll(); Check(!QuickSharePresentation.ReturnToCompletion(registry, completion, true), "Expired source regained authority");
            } });
            Test("manual Quick Share history after source expiry can export while no eject authority is restored", delegate { using (var f = new Fixture()) {
                var batch = LunaBatch(f); string root = ManualShareSetup(f, batch); var registry = new ImportCompletionRegistry(); var completion = ShareCompletion(batch); registry.Insert(completion); registry.InvalidateAll();
                var service = ManualShares(root, new FakeShareProcess()); Check(service.Run(service.Prepare(batch).Choose(QuickSharePreset.Hd, QuickShareColor.Standard), CancellationToken.None, _ => {}).Created == 1 && registry.Entries.Length == 0 && !QuickSharePresentation.ReturnToCompletion(registry, completion, true), "Saved-copy export restored eject permission");
            } });
            Test("manual Quick Share completion still requires fresh source identity and an explicit later eject request", delegate { using (var f = new Fixture()) {
                var batch = LunaBatch(f); string root = ManualShareSetup(f, batch); var completion = ShareCompletion(batch); var registry = new ImportCompletionRegistry(); registry.Insert(completion); var state = State(); state.Volumes[0].Id = batch.ProfileId; var discovery = new FakeDiscovery { State = state }; var eject = new FakeEject();
                var service = ManualShares(root, new FakeShareProcess()); service.Run(service.Prepare(batch).Choose(QuickSharePreset.Hd, QuickShareColor.Standard), CancellationToken.None, _ => {});
                Check(eject.Calls == 0 && registry.Get(completion.Id) == completion, "Export consumed completion"); state.Epoch++;
                Throws<SafetyException>(() => new CompletedEjectService(registry, discovery, eject, new Operations(), _ => batch.ProfileId).Execute(completion.Id)); Check(eject.Calls == 0, "Stale source reached eject");
            } });
            Test("manual Quick Share completion UI keeps Keep default and source expiry disables only eject", delegate { using (var f = new Fixture()) {
                var completion = ShareCompletion(LunaBatch(f)); using (var dialog = new ImportCompletionDialog(completion, true, canShare: true)) {
                    Check(dialog.Share.Enabled && dialog.Share.DialogResult == DialogResult.Retry && dialog.AcceptButton == dialog.Keep && dialog.CancelButton == dialog.Keep, "Export/eject became the default");
                    dialog.ExpireEject(); Check(!dialog.Eject.Enabled && dialog.Share.Enabled && dialog.OpenFolder.Enabled, "Source expiry disabled saved-copy actions");
                }
                using (var dialog = new ImportCompletionDialog(completion, true)) Check(!dialog.Share.Enabled, "Off/read-only dialog enables export");
                using (var dialog = new ImportCompletionDialog(Completion(State(), f, false), true, canShare: true)) Check(!dialog.Share.Enabled, "Metadata-only completion offers export");
            } });
            Test("manual Quick Share history selection shows opt-in action without hiding saved verification history", delegate { using (var f = new Fixture()) {
                var batch = LunaBatch(f); string root = ManualShareSetup(f, batch); var catalog = Profiles.Catalog(root);
                using (var dialog = new ImportHistoryDialog(ImportHistory.Load(root), catalog)) { IntPtr handle = dialog.Batches.Handle; dialog.Batches.Items[0].Selected = true; Check(dialog.Share.Enabled && dialog.SelectedBatch.Id == batch.Id && dialog.CancelButton != dialog.Share, "Enabled history action/Cancel lost"); }
                var entry = catalog.Single(); var p = entry.Profile; p.SharingPreset = null; Profiles.SaveOffline(entry, p, root);
                using (var dialog = new ImportHistoryDialog(ImportHistory.Load(root), Profiles.Catalog(root))) { IntPtr handle = dialog.Batches.Handle; dialog.Batches.Items[0].Selected = true; Check(!dialog.Share.Enabled && dialog.Batches.Items.Count == 1 && dialog.SelectedBatch.Id == batch.Id, "Disabled sharing removed verification history"); }
                using (var dialog = new ImportHistoryDialog(ImportHistory.Load(root))) { IntPtr handle = dialog.Batches.Handle; dialog.Batches.Items[0].Selected = true; Check(!dialog.Share.Enabled, "Read-only history exposes export"); }
            } });
            Test("manual Quick Share cancelled partial publication or retained private work preserves attention", delegate {
                var normal = new QuickShareFailure(new OperationCanceledException(), new QuickShareResult()); Check(!QuickSharePresentation.CancelNeedsAttention(normal), "Clean cancellation requests attention");
                var partial = new QuickShareFailure(new OperationCanceledException(), new QuickShareResult { PublishedCopies = 1, PublishedReceipts = 0 }); Check(QuickSharePresentation.CancelNeedsAttention(partial), "Unreceipted publication concealed");
                var retained = new QuickShareResult(); retained.CleanupWarnings.Add("Owned private work retained for review"); Check(QuickSharePresentation.CancelNeedsAttention(new QuickShareFailure(new OperationCanceledException(), retained)), "Retained work concealed");
            });
        }
    }
}
