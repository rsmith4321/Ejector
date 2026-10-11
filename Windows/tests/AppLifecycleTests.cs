using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.Serialization.Json;
using System.Text;
using System.Threading;

namespace EasyEject {
    // Remaining Windows software gates, exercised on generated fixtures, fake devices and
    // owned child processes only. Live UI, real devices and real media remain Ryan's tests.
    public static partial class Tests {
        static void AppLifecycleTests() {
            Test("tray close hides to the tray while idle and during work", delegate {
                Check(AppQuitPolicy.Closing(false, true, false) == AppCloseAction.HideToTray, "Idle window close exited instead of hiding");
                Check(AppQuitPolicy.Closing(false, true, true) == AppCloseAction.HideToTray, "Busy window close did not hide to the tray");
            });
            Test("tray Quit exits only while idle with no open editor", delegate {
                Check(AppQuitPolicy.QuitRequested(false, false) == AppCloseAction.Exit, "Idle Quit refused");
                Check(AppQuitPolicy.QuitRequested(true, false) == AppCloseAction.StayOpen && AppQuitPolicy.Refusal(true, false) == AppQuitPolicy.BusyMessage, "Busy Quit was admitted");
                Check(AppQuitPolicy.QuitRequested(false, true) == AppCloseAction.StayOpen && AppQuitPolicy.Refusal(false, true) == AppQuitPolicy.EditorMessage, "Quit discarded an open editor draft");
                Check(AppQuitPolicy.Refusal(false, false) == null, "Idle Quit reported a refusal");
            });
            Test("admitted Quit still stays open if work begins before the window closes", delegate {
                Check(AppQuitPolicy.Closing(true, true, false) == AppCloseAction.Exit, "Admitted idle Quit did not exit");
                Check(AppQuitPolicy.Closing(true, true, true) == AppCloseAction.StayOpen, "Quit raced past newly started work");
            });
            Test("system close exits only when idle and never hides", delegate {
                Check(AppQuitPolicy.Closing(false, false, false) == AppCloseAction.Exit, "Idle system close was refused");
                Check(AppQuitPolicy.Closing(false, false, true) == AppCloseAction.StayOpen, "System close abandoned active work");
            });
            Test("refused Quit keeps an unsaved defaults draft without saving or discarding it", delegate {
                int saves = 0; var initial = new ImportDefaults(); var edit = new ImportDefaultsEdit(initial, _ => saves++);
                bool videos = edit.Snapshot().VideosOnly ?? true; edit.Set(ImportDefaultChoice.VideosOnly, !videos);
                Check(AppQuitPolicy.QuitRequested(false, !edit.Closed) == AppCloseAction.StayOpen, "Quit admitted with an open draft");
                Check(saves == 0 && !edit.Closed && (edit.Snapshot().VideosOnly ?? true) == !videos, "Refused Quit saved, closed or lost the draft");
                Check(AppQuitPolicy.Closing(false, true, false) == AppCloseAction.HideToTray && (edit.Snapshot().VideosOnly ?? true) == !videos, "Hiding the window changed the draft");
                Check(edit.Cancel() && edit.Closed && saves == 0 && AppQuitPolicy.QuitRequested(false, !edit.Closed) == AppCloseAction.Exit, "Explicit Cancel did not release the draft without saving");
            });
            Test("no-media completion still offers an explicit Keep Connected default eject choice", delegate { using (var f = new Fixture()) {
                var entry = Completion(State(), f, false);
                Check(CompletionPresentation.ShowChoice(true, false, false) && CompletionPresentation.ShowChoice(true, true, true), "Ask-to-eject choice was suppressed by visibility or notifications");
                Check(!CompletionPresentation.ShowChoice(false, true, true), "Choice shown without the saved ask option");
                using (var dialog = new ImportCompletionDialog(entry, true)) {
                    Check(dialog.Text == CompletionPresentation.ChoiceTitle(false) && dialog.Text.StartsWith("No matching media", StringComparison.Ordinal), "No-media choice title wrong");
                    Check(dialog.AcceptButton == dialog.Keep && dialog.CancelButton == dialog.Keep && dialog.Keep.DialogResult == System.Windows.Forms.DialogResult.Cancel, "Return/Escape do not keep the device connected");
                    Check(dialog.Eject.Enabled && dialog.Eject.DialogResult == System.Windows.Forms.DialogResult.Yes && !dialog.Share.Visible, "Explicit eject missing or sharing offered without a batch");
                    Check(dialog.Explanation.Text.Contains("may still be on the device"), "No-media choice does not warn about remaining files");
                }
                Check(!CompletionPresentation.Notification(false).ToLowerInvariant().Contains("ejected"), "Completion notification claims an eject");
            } });
            Test("manual 4K Quick Share through the native helper process publishes and exactly reuses separately from 1080p", delegate { using (var f = new Fixture()) {
                var batch = LunaBatch(f); string folder = Path.Combine(f.Root, "manual-native-helper-4k"); Directory.CreateDirectory(folder); string helper = Path.Combine(folder, "EasyEject.ShareFixture.exe"); File.Copy(ShareHelper, helper);
                string root = ManualShareSetup(f, batch, tools: new QuickShareTools(helper, helper)); var service = ManualShares(root, new WindowsQuickShareProcess());
                var four = service.Run(service.Prepare(batch).Choose(QuickSharePreset.Uhd, QuickShareColor.Standard), CancellationToken.None, _ => {});
                Check(four.Created == 1 && four.Outputs.Single().EndsWith("-sharing-4K.mp4") && File.ReadAllText(four.Outputs.Single()).StartsWith("SYNTHETIC_SHARE|4K|"), "Native 4K manual workflow failed");
                string fourHash = IndependentFileHash(four.Outputs.Single()), receiptHash = IndependentFileHash(QuickShareEngine.ReceiptPath(four.Outputs.Single()));
                var hd = service.Run(service.Prepare(batch).Choose(QuickSharePreset.Hd, QuickShareColor.Standard), CancellationToken.None, _ => {});
                Check(hd.Created == 1 && File.ReadAllText(hd.Outputs.Single()).StartsWith("SYNTHETIC_SHARE|1080p|"), "1080p choice reused the 4K copy");
                var again = service.Run(service.Prepare(batch).Choose(QuickSharePreset.Uhd, QuickShareColor.Standard), CancellationToken.None, _ => {});
                Check(again.Created == 0 && again.Reused == 1 && again.Outputs.Single() == four.Outputs.Single(), "Fresh 4K action did not reuse its exact copy");
                Check(IndependentFileHash(four.Outputs.Single()) == fourHash && IndependentFileHash(QuickShareEngine.ReceiptPath(four.Outputs.Single())) == receiptHash, "4K reuse rewrote its copy or receipt");
                VerifyShareOriginals(batch); CheckSharePrivateGone(batch);
            } });
            Test("app session Quit then restart with source absent keeps history and exact manual 4K sharing", delegate { using (var f = new Fixture()) {
                var batch = LunaBatch(f); FixtureShareTools(f);
                using (var output = new FileStream(Path.Combine(f.Root, ".app-session-batch.json"), FileMode.CreateNew, FileAccess.Write, FileShare.None)) new DataContractJsonSerializer(typeof(CompletedBatch)).WriteObject(output, batch);
                string appData = Path.Combine(Path.GetFullPath(FixtureParent()), "appdata-" + Guid.NewGuid().ToString("N").Substring(0, 16)); Directory.CreateDirectory(appData);
                try {
                    RunAppSession(f, appData, "session", batch.Id);
                    string config = Path.Combine(appData, "EasyEject", "Profiles");
                    Check(File.Exists(Path.Combine(config, "completed-imports.json")) && !SafePaths.Within(Profiles.Root, appData), "Session did not persist to its own app-data");
                    HideOwnedSource(f);
                    RunAppSession(f, appData, "restart-create", batch.Id);
                    string export = ShareFile(batch, size: "4K"); string exportHash = IndependentFileHash(export), receiptHash = IndependentFileHash(QuickShareEngine.ReceiptPath(export));
                    RunAppSession(f, appData, "restart-reuse", batch.Id);
                    Check(IndependentFileHash(export) == exportHash && IndependentFileHash(QuickShareEngine.ReceiptPath(export)) == receiptHash, "Restarted reuse rewrote the 4K copy or receipt");
                    Check(ImportHistory.Load(config).Single().Id == batch.Id, "Restart changed persisted history");
                    VerifyShareOriginals(batch);
                } finally { if (Directory.Exists(appData)) RemoveOwnedTree(appData); }
            } });
            Test("travel rehearsal: sleep mid-copy, wake, card reconnect, fresh retry, reuse and history after source removal", delegate { using (var f = new Fixture()) {
                f.Put("first.mp4", "first travel original"); f.Put("long.mp4", new string('T', 2200000)); f.Put("third.mp4", "third travel original");
                var d = new ScopedDiscovery(f); var operations = new Operations(); bool slept = false;
                Throws<SafetyException>(() => new VerifiedImporter(operations, path => UInt64.MaxValue, line => {}).Run(f.Source, f.Source, f.Destination, "source-volume", ScopedImportGuard(f, d), CancellationToken.None, line => {
                    if (!line.StartsWith("Copying", StringComparison.Ordinal) || slept) return; slept = true; FakePower(d, 4);
                }));
                Check(slept && File.ReadAllText(Path.Combine(f.Source, "long.mp4")) == new string('T', 2200000), "Sleep during the long copy changed originals");
                NoRemovalCompletion(f); using (operations.Reserve(new[] { 700, 701 })) {}
                FakePower(d, 18);
                var beforeReconnect = ScopedImportGuard(f, d); d.Event("source-disk");
                Throws<SafetyException>(() => beforeReconnect.Validate());
                var first = new VerifiedImporter(operations, path => UInt64.MaxValue, line => {}).Run(f.Source, f.Source, f.Destination, "source-volume", ScopedImportGuard(f, d), CancellationToken.None, line => {});
                Check(first.Files == 3 && first.RemovedOriginals == 0 && File.ReadAllText(Saved(first, "first.mp4")) == "first travel original", "Fresh manual retry after reconnect failed");
                string config = Path.Combine(f.Root, "travel-config"); Directory.CreateDirectory(config); var batch = Batch(f, first); ImportHistory.Add(batch, config);
                FakePower(d, 4); FakePower(d, 18);
                var again = new VerifiedImporter(operations, path => UInt64.MaxValue, line => {}).Run(f.Source, f.Source, f.Destination, "source-volume", ScopedImportGuard(f, d), CancellationToken.None, line => {});
                Check(again.Files == 3 && again.Reused == 3, "Repeat import after another sleep did not reuse verified copies");
                HideOwnedSource(f);
                var saved = ImportHistory.Load(config).Single(); Check(saved.Id == batch.Id, "History lost after source removal"); VerifyShareOriginals(saved);
            } });
        }

        static void RunAppSession(Fixture fixture, string appData, string phase, string batchId) {
#if NET
            string executable = Environment.ProcessPath;
#else
            string executable = typeof(Tests).Assembly.Location;
#endif
            var arguments = new[] { "--app-session-probe", phase, fixture.Root, batchId };
            var start = new ProcessStartInfo(executable, String.Join(" ", arguments.Select(WindowsQuickShareProcess.Quote))) { UseShellExecute = false, CreateNoWindow = true, WorkingDirectory = fixture.Root, RedirectStandardOutput = true, RedirectStandardError = true };
            start.EnvironmentVariables[IsolatedAppDataVariable] = appData;
            using (var child = Process.Start(start)) {
                Check(child != null && child.Id != Process.GetCurrentProcess().Id, "App session did not use a separate process");
                var output = child.StandardOutput.ReadToEndAsync(); var error = child.StandardError.ReadToEndAsync();
                if (!child.WaitForExit(60000)) { child.Kill(); child.WaitForExit(); throw new Exception("App session child timed out"); }
                string stdout = output.GetAwaiter().GetResult(), stderr = error.GetAwaiter().GetResult();
                Check(child.ExitCode == 0, "App session " + phase + " failed: " + stdout + stderr);
                Console.WriteLine("App session child " + child.Id + ": " + stdout.Trim());
            }
        }

        // Child mode: one app "session" using only the production default configuration root,
        // which the parent pointed at an owned app-data folder. Fake media process and guard.
        static int AppSessionProbe(string[] args) {
            try {
                Check(args.Length == 4 && new[] { "session", "restart-create", "restart-reuse" }.Contains(args[1]) && args[3].Length > 0, "Invalid app session request");
                string root = SafePaths.Normalize(args[2]), parent = Path.GetFullPath(FixtureParent()), leaf = Path.GetFileName(root);
                Check(Path.GetDirectoryName(root).Equals(parent.TrimEnd('\\'), StringComparison.OrdinalIgnoreCase) && leaf.StartsWith("run-", StringComparison.Ordinal), "App session is outside the owned fixture root");
                string isolated = Profiles.IsolatedAppDataForTests;
                Check(isolated != null && Path.GetFileName(isolated).StartsWith("appdata-", StringComparison.Ordinal) && Environment.GetEnvironmentVariable(IsolatedAppDataVariable) == isolated, "App session is not using inherited isolated app-data");
                string destination = Path.Combine(root, "destination"), tools = Path.Combine(root, "synthetic-tools");
                if (args[1] == "session") {
                    CompletedBatch batch;
                    using (var input = SafePaths.ReadFile(Path.Combine(root, ".app-session-batch.json"))) batch = (CompletedBatch)new DataContractJsonSerializer(typeof(CompletedBatch)).ReadObject(input);
                    Check(batch.Id == args[3] && SafePaths.Within(batch.Folder, destination), "App session batch escaped its fixture");
                    ImportHistory.Add(batch);
                    Profiles.Save(new ImportProfile { Name = batch.DeviceName, VolumeId = batch.ProfileId, VolumeSerial = "fixture-serial", DiskInstance = "fixture-source-disk", RelativeSource = "", Destination = destination, SavedDestination = batch.Destination.Copy(), SharingPreset = QuickShareProfile.Hd, SharingColor = QuickShareProfile.Standard });
                    QuickShareConfiguration.Save(QuickShareConfiguration.Load(), Path.Combine(tools, "ffmpeg.exe"), Path.Combine(tools, "ffprobe.exe"), null, Profiles.Root, CancellationToken.None);
                    Console.WriteLine("Session saved profile, history and tools; quitting.");
                    return 0;
                }
                Check(!Directory.Exists(Path.Combine(root, "source")), "Restart requires the source to be absent");
                var history = ImportHistory.Load(); var saved = history.Single(x => x.Id == args[3]);
                var media = new FakeShareProcess(); var completions = new ImportCompletionRegistry();
                var service = new ManualQuickShareService(Profiles.Root, new Operations(), media, _ => new FakeGuard(), _ => UInt64.MaxValue);
                Check(service.CanOffer(saved), "Restarted app does not offer opted-in sharing");
                var offer = service.Prepare(saved); Check(media.Probes == 0 && media.Encodes == 0, "Restart performed automatic export");
                var result = service.Run(offer.Choose(QuickSharePreset.Uhd, QuickShareColor.Standard), CancellationToken.None, _ => {});
                bool reuse = args[1] == "restart-reuse";
                Check(result.Created == (reuse ? 0 : 1) && result.Reused == (reuse ? 1 : 0) && result.Outputs.Single().EndsWith("-sharing-4K.mp4"), "Restarted manual 4K choice or reuse changed");
                Check(completions.Entries.Length == 0, "Restart restored eject authority");
                VerifyShareOriginals(saved);
                Console.WriteLine("Restart " + args[1] + ": history=" + history.Count + "; encode=" + media.Encodes + "; eject tokens=0");
                return 0;
            } catch (Exception ex) { Console.Error.WriteLine("App session probe: " + ex); return 2; }
        }
    }
}
