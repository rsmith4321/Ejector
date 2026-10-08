using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.Serialization.Json;
using System.Text;
using System.Threading;

namespace EasyEject {
    public static partial class Tests {
        // Only the existing explicit real-tool CLI enters this mode. Parent-issued
        // challenges bind generated storage and persisted tool choices; default
        // tests never invoke it. Source absence is a fixture rename, not eject.
        static int OwnedRealShareProbe(string[] args) {
            try {
                Guid nonce;
                Check(args.Length == 9 && Guid.TryParseExact(args[3], "N", out nonce)
                    && args.Skip(4).Take(3).All(ImportHistory.ValidHash)
                    && new[] { "prepare", "create", "reuse", "changed" }.Contains(args[7])
                    && (args[8] == "hd" || args[8] == "uhd"), "Invalid real cold-sharing challenge");
                string root = SafePaths.Normalize(args[2]);
                string allowed = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".codex", "EasyEjectFixtures");
                string leaf = Path.GetFileName(root), config = Path.Combine(root, "manual-share-config"), destination = Path.Combine(root, "destination");
                Check(Path.GetDirectoryName(root).Equals(allowed, StringComparison.OrdinalIgnoreCase)
                    && leaf.Length == 20 && leaf.StartsWith("run-", StringComparison.Ordinal)
                    && leaf.Substring(4).All(c => "0123456789abcdef".Contains(c)), "Real cold sharing escaped owned storage");
                using (var directories = SafePaths.HoldDirectories(root, config, destination)) {
                    using (var marker = SafePaths.ReadFile(Path.Combine(root, ".cold-real-share-owner"))) {
                        Check(marker.Length == 32 && Native.Info(marker.SafeFileHandle).Links == 1, "Invalid real cold-sharing marker");
                        using (var reader = new StreamReader(marker, Encoding.UTF8)) Check(reader.ReadToEnd() == args[3], "Real cold-sharing owner changed");
                    }
                    Check(!Directory.Exists(Path.Combine(root, "source")), "Real cold sharing requires absent generated source");
                    string snapshot = Path.Combine(root, ".cold-real-share-batch.json");
                    Check(IndependentFileHash(snapshot) == args[4]
                        && IndependentFileHash(Path.Combine(config, "completed-imports.json")) == args[5]
                        && IndependentFileHash(Path.Combine(config, QuickShareConfiguration.FileName)) == args[6], "Real cold-sharing inputs changed");
                    CompletedBatch batch;
                    using (var input = SafePaths.ReadFile(snapshot)) {
                        Check(input.Length <= 65536 && Native.Info(input.SafeFileHandle).Links == 1, "Invalid real cold-sharing snapshot");
                        batch = (CompletedBatch)new DataContractJsonSerializer(typeof(CompletedBatch)).ReadObject(input);
                    }
                    CheckColdShareBatch(batch, destination);
                    var history = ImportHistory.Load(config);
                    Check(history.Count == 1 && history[0].Id == batch.Id, "Real cold sharing did not restore the exact generated batch");
                    CheckColdShareBatch(history[0], destination);
                    var setup = QuickShareConfiguration.Load(config).Value;
                    Check(setup.Ffmpeg != null && setup.Ffprobe != null, "Real cold sharing has no explicitly selected tools");
                    var completions = new ImportCompletionRegistry(); var operations = new Operations(); var media = new ObservedRealShareProcess();
                    var service = new ManualQuickShareService(config, operations, media, value => ColdShareGuard(value, false));
                    var offer = service.Prepare(batch);
                    Check(media.Calls == 0 && completions.Entries.Length == 0, "Cold preparation ran tools or restored eject authority");
                    var preset = args[8] == "hd" ? QuickSharePreset.Hd : QuickSharePreset.Uhd;
                    if (args[7] == "changed") {
                        Throws<QuickShareFailure>(() => service.Run(offer.Choose(preset, QuickShareColor.Standard), CancellationToken.None, _ => {}));
                        Check(media.Calls == 0, "Changed original reached real tools");
                    } else if (args[7] != "prepare") {
                        var result = service.Run(offer.Choose(preset, QuickShareColor.Standard), CancellationToken.None, _ => {});
                        bool reuse = args[7] == "reuse";
                        Check(result.Created == (reuse ? 0 : 1) && result.Reused == (reuse ? 1 : 0)
                            && result.Outputs.Single().EndsWith(preset == QuickSharePreset.Hd ? "-sharing-1080p.mp4" : "-sharing-4K.mp4"), "Cold real size/publication/reuse differs");
                        Check(media.Encodes == (reuse ? 0 : 1) && media.Decodes == (reuse ? 0 : 1)
                            && media.Probes == (reuse ? 1 : 2), "Cold real export omitted validation or re-encoded verified output");
                        using (var receiptFile = SafePaths.ReadFile(QuickShareEngine.ReceiptPath(result.Outputs.Single()))) {
                            var receipt = JsonDocuments.Read<QuickShareReceipt>(receiptFile, 31999);
                            Check(receipt.SourceSha256 == batch.Originals.Single().Sha256 && receipt.OutputSha256 == IndependentFileHash(result.Outputs.Single())
                                && receipt.Width == 640 && receipt.Height == 360 && receipt.LutSha256 == null, "Cold real receipt differs from generated source/output");
                        }
                        VerifyShareOriginals(batch); CheckSharePrivateGone(batch);
                    }
                    Check(completions.Entries.Length == 0 && completions.Get(batch.Id) == null, "Cold real sharing acquired eject authority");
                    using (operations.Reserve(new[] { 700, 701 })) { }
                    directories.Validate();
                    Console.WriteLine("Cold real sharing: " + args[7] + " " + args[8] + "; encode=" + media.Encodes + "; decode=" + media.Decodes + "; eject tokens=0");
                }
                return 0;
            } catch (Exception ex) { Console.Error.WriteLine("Real cold-sharing probe: " + ex); return 2; }
        }

        static void RunColdRealShare(Fixture fixture, CompletedBatch batch, string mode, QuickSharePreset preset = QuickSharePreset.Hd, bool rejectChallenge = false) {
            string nonce = Guid.NewGuid().ToString("N"), snapshot = Path.Combine(fixture.Root, ".cold-real-share-batch.json"), config = Path.Combine(fixture.Root, "manual-share-config");
            File.WriteAllText(Path.Combine(fixture.Root, ".cold-real-share-owner"), nonce, new UTF8Encoding(false));
            using (var output = new FileStream(snapshot, FileMode.Create, FileAccess.Write, FileShare.None)) new DataContractJsonSerializer(typeof(CompletedBatch)).WriteObject(output, batch);
            var arguments = new[] { "--share-real-tools", "--owned-cold", fixture.Root, rejectChallenge ? Guid.NewGuid().ToString("N") : nonce,
                IndependentFileHash(snapshot), IndependentFileHash(Path.Combine(config, "completed-imports.json")),
                IndependentFileHash(Path.Combine(config, QuickShareConfiguration.FileName)), mode, preset == QuickSharePreset.Hd ? "hd" : "uhd" };
#if NET
            string executable = Environment.ProcessPath;
#else
            string executable = typeof(Tests).Assembly.Location;
#endif
            var start = new ProcessStartInfo(executable, String.Join(" ", arguments.Select(WindowsQuickShareProcess.Quote))) {
                UseShellExecute = false, CreateNoWindow = true, WorkingDirectory = fixture.Root, RedirectStandardOutput = true, RedirectStandardError = true
            };
            using (var child = Process.Start(start)) {
                Check(child != null && child.Id != Process.GetCurrentProcess().Id, "Real sharing did not create a separate process");
                var output = child.StandardOutput.ReadToEndAsync(); var error = child.StandardError.ReadToEndAsync();
                if (!child.WaitForExit(300000)) { child.Kill(); child.WaitForExit(); throw new Exception("Owned real cold-sharing child timed out"); }
                string stdout = output.GetAwaiter().GetResult(), stderr = error.GetAwaiter().GetResult();
                Check(child.ExitCode == (rejectChallenge ? 2 : 0), "Real cold-sharing child failed: " + stdout + stderr);
                if (rejectChallenge) Check(stdout.Length == 0 && stderr.Contains("Real cold-sharing owner changed"), "Invalid real challenge reached manual work");
                Console.WriteLine("Owned real sharing child " + child.Id + ": " + (rejectChallenge ? "invalid challenge refused" : stdout.Trim()));
            }
        }

        static void RealShareColdRestart(QuickShareTools tools) {
            using (var f = new Fixture()) {
                var batch = GenerateRealShareBatch(f, tools, 640, 360, 30, true); string config = RealShareSetup(f, batch, tools);
                string historyHash = IndependentFileHash(Path.Combine(config, "completed-imports.json"));
                RunColdRealShare(f, batch, "prepare"); RunColdRealShare(f, batch, "create", rejectChallenge: true);
                Check(!Directory.Exists(Path.Combine(batch.Folder, "Sharing Copies")), "Preparation or invalid challenge published output");
                foreach (var preset in new[] { QuickSharePreset.Hd, QuickSharePreset.Uhd }) {
                    RunColdRealShare(f, batch, "create", preset);
                    string output = Directory.GetFiles(Path.Combine(batch.Folder, "Sharing Copies"), preset == QuickSharePreset.Hd ? "*-sharing-1080p.mp4" : "*-sharing-4K.mp4").Single();
                    string outputHash = IndependentFileHash(output), receiptHash = IndependentFileHash(QuickShareEngine.ReceiptPath(output));
                    RunColdRealShare(f, batch, "reuse", preset);
                    Check(IndependentFileHash(output) == outputHash && IndependentFileHash(QuickShareEngine.ReceiptPath(output)) == receiptHash, "Cold reuse changed previously published bytes");
                }
                var retained = Directory.GetFiles(Path.Combine(batch.Folder, "Sharing Copies")).ToDictionary(path => path, IndependentFileHash);
                string original = batch.Originals.Single().Path; byte[] prior = File.ReadAllBytes(original); File.AppendAllText(original, "changed generated original");
                RunColdRealShare(f, batch, "changed");
                Check(new FileInfo(original).Length > prior.Length, "Refusal repaired the changed original");
                Check(retained.All(pair => IndependentFileHash(pair.Key) == pair.Value), "Refusal changed a retained export or receipt");
                File.WriteAllBytes(original, prior); RunColdRealShare(f, batch, "reuse");
                Check(IndependentFileHash(Path.Combine(config, "completed-imports.json")) == historyHash && !Directory.Exists(f.Source), "Cold sharing changed saved history or restored source");
                VerifyShareOriginals(batch); CheckSharePrivateGone(batch);
            }
        }
    }
}
