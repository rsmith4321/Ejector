using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.Serialization.Json;
using System.Text;
using System.Threading;

namespace EasyEject {
    public static partial class Tests {
        // Separate test processes use only challenged generated fixture storage,
        // synthetic media processes and fresh fake destination discovery. This
        // proves persisted manual admission, never real encoding or physical eject.
        static void CheckColdShareBatch(CompletedBatch batch, string destination) {
            ImportHistory.Validate(batch);
            Check(batch.ProfileId == "fixture-volume" && batch.Destination.Matches(HistoryIdentity()) && batch.DestinationRoot.Equals(destination, StringComparison.OrdinalIgnoreCase), "Cold sharing escaped fixture identity");
            Check(SafePaths.Within(batch.Folder, destination) && SafePaths.Within(batch.ReceiptPath, destination) && batch.Originals.All(x => SafePaths.Within(x.Path, destination)), "Cold sharing escaped its owned destination");
        }
        static IImportGuard ColdShareGuard(CompletedBatch batch, bool changed) {
            var state = new Inventory { Epoch = 1 };
            state.Disks.Add(new Disk { Number = 701, InstanceId = "destination-disk", Serial = "serial-destination" });
            state.Volumes.Add(new Volume { Id = "destination-fixture", Serial = changed ? "changed-fixture" : "ABC", Extents = "701:0:1000", Disks = new List<int> { 701 }, Mounts = new List<string> { batch.DestinationRoot } });
            return new WindowsDestinationGuard(new FakeDiscovery { State = state }, batch.DestinationRoot, batch.Destination, _ => "destination-fixture");
        }
        static int OwnedShareProbe(string[] args) {
            try {
                Guid challenge; var modes = new[] { "prepare", "create", "reuse", "original", "receipt", "expired", "disabled", "destination", "lutmissing" };
                Check(args.Length == 7 && Guid.TryParseExact(args[2], "N", out challenge) && ImportHistory.ValidHash(args[3]) && ImportHistory.ValidHash(args[4]) && modes.Contains(args[5]) && (args[6] == "hd" || args[6] == "uhd"), "Invalid cold-sharing challenge");
                string root = SafePaths.Normalize(args[1]), allowed = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".codex", "EasyEjectFixtures");
                string leaf = Path.GetFileName(root), destination = Path.Combine(root, "destination"), config = Path.Combine(root, "manual-share-config");
                Check(Path.GetDirectoryName(root).Equals(allowed, StringComparison.OrdinalIgnoreCase) && leaf.Length == 20 && leaf.StartsWith("run-", StringComparison.Ordinal) && leaf.Substring(4).All(c => "0123456789abcdef".Contains(c)), "Cold sharing is outside the owned fixture root");
                using (var directories = SafePaths.HoldDirectories(root, config, destination)) {
                    using (var marker = SafePaths.ReadFile(Path.Combine(root, ".cold-share-owner"))) {
                        Check(marker.Length == 32 && Native.Info(marker.SafeFileHandle).Links == 1, "Invalid cold-sharing marker");
                        using (var reader = new StreamReader(marker, Encoding.UTF8)) Check(reader.ReadToEnd() == args[2], "Cold-sharing owner changed");
                    }
                    Check(!Directory.Exists(Path.Combine(root, "source")), "Cold sharing requires its generated source path to be absent");
                    string snapshot = Path.Combine(root, ".cold-share-batch.json");
                    Check(IndependentFileHash(snapshot) == args[3] && IndependentFileHash(Path.Combine(config, "completed-imports.json")) == args[4], "Cold-sharing input bytes changed");
                    CompletedBatch batch;
                    using (var input = SafePaths.ReadFile(snapshot)) { Check(input.Length <= 65536 && Native.Info(input.SafeFileHandle).Links == 1, "Invalid cold-sharing snapshot"); batch = (CompletedBatch)new DataContractJsonSerializer(typeof(CompletedBatch)).ReadObject(input); }
                    CheckColdShareBatch(batch, destination);
                    var values = ImportHistory.Load(config); Check(values.Count >= 1 && values.Count <= 20, "Cold process lost history retention");
                    foreach (var value in values) CheckColdShareBatch(value, destination);
                    foreach (var profile in Profiles.Catalog(config).Where(x => x.CanEdit)) Check(profile.Profile.VolumeId == "fixture-volume" && profile.Profile.Destination.Equals(destination, StringComparison.OrdinalIgnoreCase), "Cold profile escaped its destination");
                    var setup = QuickShareConfiguration.Load(config).Value;
                    foreach (var tool in new[] { setup.Ffmpeg, setup.Ffprobe }) {
                        Check(SafePaths.Within(tool.Path, Path.Combine(root, "synthetic-tools")) && File.ReadAllText(tool.Path) == "synthetic tool, never executed", "Cold sharing selected a real executable");
                    }
                    var completions = new ImportCompletionRegistry(); var operations = new Operations(); var media = new FakeShareProcess();
                    var service = new ManualQuickShareService(config, operations, media, value => ColdShareGuard(value, args[5] == "destination"), _ => UInt64.MaxValue);
                    Check(completions.Entries.Length == 0, "Cold sharing restored eject permission");
                    var preset = args[6] == "hd" ? QuickSharePreset.Hd : QuickSharePreset.Uhd;
                    if (args[5] == "expired" || args[5] == "disabled") {
                        Check(!service.CanOffer(batch), "Expired or disabled batch was offered"); Throws<SafetyException>(() => service.Prepare(batch));
                    } else {
                        var offer = service.Prepare(batch);
                        Check(media.Probes == 0 && media.Encodes == 0 && media.Decodes == 0, "Preparation performed automatic export");
                        if (args[5] == "original" || args[5] == "receipt") Throws<QuickShareFailure>(() => service.Run(offer.Choose(preset, QuickShareColor.Standard), CancellationToken.None, _ => {}));
                        else if (args[5] == "lutmissing") Throws<SafetyException>(() => service.Run(offer.Choose(preset, QuickShareColor.LunaILog), CancellationToken.None, _ => {}));
                        else if (args[5] == "destination") Throws<SafetyException>(() => service.Run(offer.Choose(preset, QuickShareColor.Standard), CancellationToken.None, _ => {}));
                        else if (args[5] != "prepare") {
                            var result = service.Run(offer.Choose(preset, QuickShareColor.Standard), CancellationToken.None, _ => {});
                            bool reuse = args[5] == "reuse";
                            Check(result.Created == (reuse ? 0 : 1) && result.Reused == (reuse ? 1 : 0) && result.Outputs.Single().EndsWith(preset == QuickSharePreset.Hd ? "-sharing-1080p.mp4" : "-sharing-4K.mp4"), "Cold manual choice or exact reuse changed");
                            Check(media.Encodes == (reuse ? 0 : 1) && media.Decodes == (reuse ? 0 : 1) && media.Probes == (reuse ? 1 : 2), "Cold sharing omitted validation requests or re-encoded an exact receipt");
                            VerifyShareOriginals(batch); CheckSharePrivateGone(batch);
                        }
                    }
                    if (args[5] != "create" && args[5] != "reuse") Check(media.Probes == 0 && media.Encodes == 0 && media.Decodes == 0, "Cold refusal or preparation reached media processing");
                    Check(completions.Entries.Length == 0 && completions.Get(batch.Id) == null, "Saved sharing acquired source eject authority");
                    using (operations.Reserve(new[] { 700, 701 })) { }
                    directories.Validate(); Console.WriteLine("Cold manual sharing: " + args[5] + "; retained=" + values.Count + "; encode=" + media.Encodes + "; decode=" + media.Decodes + "; eject tokens=0");
                }
                return 0;
            } catch (Exception ex) { Console.Error.WriteLine("Cold-sharing probe: " + ex); return 2; }
        }
        static string ColdShareSetup(Fixture fixture, CompletedBatch batch) {
            return ManualShareSetup(fixture, batch);
        }
        static void RunColdShare(Fixture fixture, CompletedBatch batch, string mode, QuickSharePreset preset = QuickSharePreset.Hd, bool rejectChallenge = false) {
            string challenge = Guid.NewGuid().ToString("N"), snapshot = Path.Combine(fixture.Root, ".cold-share-batch.json");
            File.WriteAllText(Path.Combine(fixture.Root, ".cold-share-owner"), challenge, new UTF8Encoding(false));
            using (var output = new FileStream(snapshot, FileMode.Create, FileAccess.Write, FileShare.None)) new DataContractJsonSerializer(typeof(CompletedBatch)).WriteObject(output, batch);
            var arguments = new[] { "--owned-share-probe", fixture.Root, rejectChallenge ? Guid.NewGuid().ToString("N") : challenge, IndependentFileHash(snapshot), IndependentFileHash(Path.Combine(fixture.Root, "manual-share-config", "completed-imports.json")), mode, preset == QuickSharePreset.Hd ? "hd" : "uhd" };
#if NET
            string executable = Environment.ProcessPath;
#else
            string executable = typeof(Tests).Assembly.Location;
#endif
            var start = new ProcessStartInfo(executable, String.Join(" ", arguments.Select(WindowsQuickShareProcess.Quote))) { UseShellExecute = false, CreateNoWindow = true, WorkingDirectory = fixture.Root, RedirectStandardOutput = true, RedirectStandardError = true };
            using (var child = Process.Start(start)) {
                Check(child != null && child.Id != Process.GetCurrentProcess().Id, "Sharing did not create a separate process");
                var output = child.StandardOutput.ReadToEndAsync(); var error = child.StandardError.ReadToEndAsync();
                if (!child.WaitForExit(30000)) { child.Kill(); child.WaitForExit(); throw new Exception("Owned cold-sharing child timed out"); }
                string stdout = output.GetAwaiter().GetResult(), stderr = error.GetAwaiter().GetResult();
                Check(child.ExitCode == (rejectChallenge ? 2 : 0), "Cold-sharing child failed: " + stdout + stderr);
                if (rejectChallenge) Check(stdout.Length == 0 && stderr.Contains("Cold-sharing owner changed"), "Invalid challenge reached manual work");
                Console.WriteLine("Owned sharing child " + child.Id + ": " + (rejectChallenge ? "invalid challenge refused" : stdout.Trim()));
            }
        }
        static void ColdQuickShareTests() {
            Test("cold manual sharing preparation after source absence starts no export or eject", delegate { using (var f = new Fixture()) {
                var batch = LunaBatch(f); ColdShareSetup(f, batch); HideOwnedSource(f); RunColdShare(f, batch, "prepare");
                Check(!Directory.Exists(Path.Combine(batch.Folder, "Sharing Copies")), "Cold preparation published output"); VerifyShareOriginals(batch);
            } });
            Test("separate cold manual processes create and exactly reuse each explicit sharing size", delegate { using (var f = new Fixture()) {
                var batch = LunaBatch(f); ColdShareSetup(f, batch); HideOwnedSource(f);
                foreach (var preset in new[] { QuickSharePreset.Hd, QuickSharePreset.Uhd }) {
                    RunColdShare(f, batch, "create", preset); var exports = Directory.GetFiles(Path.Combine(batch.Folder, "Sharing Copies"), "*.mp4");
                    var records = exports.SelectMany(path => new[] { path, QuickShareEngine.ReceiptPath(path) }).ToArray();
                    var hashes = records.Select(IndependentFileHash).ToArray(); RunColdShare(f, batch, "reuse", preset);
                    Check(records.Select(IndependentFileHash).SequenceEqual(hashes), "Cold reuse changed an earlier export or receipt");
                }
                Check(Directory.GetFiles(Path.Combine(batch.Folder, "Sharing Copies"), "*.mp4").Length == 2, "Cold reuse duplicated output"); VerifyShareOriginals(batch);
            } });
            Test("cold manual sharing refuses a dropped twenty-first batch without rescanning its retained files", delegate { using (var f = new Fixture()) {
                var batch = LunaBatch(f); string config = ColdShareSetup(f, batch);
                for (int i = 0; i < 20; i++) ImportHistory.Add(Batch(f, f.Run(_ => {})), config);
                var current = ImportHistory.Load(config); Check(current.Count == 20 && current.All(x => x.Id != batch.Id), "Fixture retention did not drop the old batch");
                HideOwnedSource(f); RunColdShare(f, batch, "expired"); RunColdShare(f, current[0], "prepare");
                Check(!Directory.Exists(Path.Combine(batch.Folder, "Sharing Copies")), "Expired cold choice scanned or published output"); VerifyShareOriginals(batch);
            } });
            Test("cold manual sharing refuses changed saved originals and allows only fresh restored-file retry", delegate { using (var f = new Fixture()) {
                var batch = LunaBatch(f); ColdShareSetup(f, batch); HideOwnedSource(f); string path = batch.Originals.Single().Path; byte[] prior = File.ReadAllBytes(path);
                File.AppendAllText(path, "changed generated original"); string changed = IndependentFileHash(path); RunColdShare(f, batch, "original");
                Check(IndependentFileHash(path) == changed && !Directory.Exists(Path.Combine(batch.Folder, "Sharing Copies")), "Cold refusal repaired data or published output");
                File.WriteAllBytes(path, prior); RunColdShare(f, batch, "create"); VerifyShareOriginals(batch);
            } });
            Test("cold manual sharing refuses a changed import receipt without repairing or deleting it", delegate { using (var f = new Fixture()) {
                var batch = LunaBatch(f); ColdShareSetup(f, batch); HideOwnedSource(f); byte[] prior = File.ReadAllBytes(batch.ReceiptPath);
                File.AppendAllText(batch.ReceiptPath, "changed generated receipt"); string changed = IndependentFileHash(batch.ReceiptPath); RunColdShare(f, batch, "receipt");
                Check(IndependentFileHash(batch.ReceiptPath) == changed && !Directory.Exists(Path.Combine(batch.Folder, "Sharing Copies")), "Cold refusal repaired receipt or exported");
                File.WriteAllBytes(batch.ReceiptPath, prior); RunColdShare(f, batch, "create"); VerifyShareOriginals(batch);
            } });
            Test("cold manual sharing keeps per-device opt-in and forgotten profiles separate from history", delegate { using (var f = new Fixture()) {
                var batch = LunaBatch(f); string config = ColdShareSetup(f, batch); HideOwnedSource(f);
                var saved = Profiles.Catalog(config).Single(); var profile = saved.Profile; profile.SharingPreset = null; Profiles.SaveOffline(saved, profile, config); RunColdShare(f, batch, "disabled");
                Profiles.ForgetOffline(Profiles.Catalog(config).Single(), config); RunColdShare(f, batch, "disabled");
                Check(ImportHistory.Load(config).Single().Id == batch.Id && !Directory.Exists(Path.Combine(batch.Folder, "Sharing Copies")), "Opt-in refusal lost history or exported");
                profile.SharingPreset = QuickShareProfile.Hd; Profiles.Save(profile, config); RunColdShare(f, batch, "create"); VerifyShareOriginals(batch);
            } });
            Test("cold manual sharing requires a fresh matching destination guard before processing", delegate { using (var f = new Fixture()) {
                var batch = LunaBatch(f); ColdShareSetup(f, batch); HideOwnedSource(f); RunColdShare(f, batch, "destination");
                Check(!Directory.Exists(Path.Combine(batch.Folder, "Sharing Copies")), "Changed destination published output"); RunColdShare(f, batch, "create"); VerifyShareOriginals(batch);
            } });
            Test("cold manual sharing refuses an invalid fixture challenge before reading choices or exporting", delegate { using (var f = new Fixture()) {
                var batch = LunaBatch(f); string config = ColdShareSetup(f, batch); HideOwnedSource(f);
                string history = IndependentFileHash(Path.Combine(config, "completed-imports.json")); RunColdShare(f, batch, "create", rejectChallenge: true);
                Check(IndependentFileHash(Path.Combine(config, "completed-imports.json")) == history && !Directory.Exists(Path.Combine(batch.Folder, "Sharing Copies")), "Invalid challenge changed history or published output");
                VerifyShareOriginals(batch); RunColdShare(f, batch, "create");
            } });
            Test("cold manual LUT choice requires supplied configuration while Standard overrides the saved preference", delegate { using (var f = new Fixture()) {
                var batch = LunaBatch(f); string config = ColdShareSetup(f, batch); HideOwnedSource(f);
                var saved = Profiles.Catalog(config).Single(); var profile = saved.Profile; profile.SharingColor = QuickShareProfile.LunaILog; Profiles.SaveOffline(saved, profile, config);
                RunColdShare(f, batch, "lutmissing"); Check(!Directory.Exists(Path.Combine(batch.Folder, "Sharing Copies")) && QuickShareConfiguration.Load(config).Value.Lut == null, "Missing LUT was installed or exported");
                RunColdShare(f, batch, "create"); Check(Profiles.Catalog(config).Single().Profile.SharingColor == QuickShareProfile.LunaILog, "Explicit Standard choice changed saved preference"); VerifyShareOriginals(batch);
            } });
        }
    }
}
