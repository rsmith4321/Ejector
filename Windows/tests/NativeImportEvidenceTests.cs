using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;

namespace EasyEject {
    public static partial class Tests {
        // Optional, fully allocated fixture; never included in the default suite.
        const long LargeGeneratedBytes = 2147483648L + 8 * 1048576L + 131;
        static string GenerateOwnedClip(Fixture fixture, long bytes) {
            byte[] buffer = new byte[1048576];
            for (int i = 0; i < buffer.Length; i++) buffer[i] = (byte)(i * 31 + 17);
            using (var sha = SHA256.Create()) using (var output = new FileStream(Path.Combine(fixture.Source, "generated.mp4"), FileMode.CreateNew, FileAccess.Write, FileShare.None, buffer.Length)) {
                for (long offset = 0; offset < bytes; ) {
                    int count = (int)Math.Min(buffer.Length, bytes - offset);
                    Array.Copy(BitConverter.GetBytes(offset), 0, buffer, 0, 8);
                    Array.Copy(BitConverter.GetBytes(~offset), 0, buffer, count - 8, 8);
                    output.Write(buffer, 0, count); sha.TransformBlock(buffer, 0, count, null, 0); offset += count;
                }
                output.Flush(true); sha.TransformFinalBlock(new byte[0], 0, 0);
                return BitConverter.ToString(sha.Hash).Replace("-", "").ToLowerInvariant();
            }
        }
        static string IndependentFileHash(string path) {
            using (var input = SafePaths.ReadFile(path)) using (var sha = SHA256.Create()) {
                Check(Native.Info(input.SafeFileHandle).Links == 1, "Evidence file has aliases");
                return BitConverter.ToString(sha.ComputeHash(input)).Replace("-", "").ToLowerInvariant();
            }
        }
        static string NativeHistoryRoot(Fixture fixture) { return Path.Combine(fixture.Root, "history"); }
        static void HideOwnedSource(Fixture fixture) {
            string from = Path.GetFullPath(fixture.Source), to = Path.GetFullPath(Path.Combine(fixture.Root, "source-absent"));
            Check(SafePaths.Within(from, fixture.Root) && from != fixture.Root && SafePaths.Within(to, fixture.Root) && to != fixture.Root, "Source absence fixture escaped its owned root");
            Directory.Move(from, to);
        }
        // Child mode reads only an explicitly challenged owned fixture. No discovery/eject/tool API.
        static int OwnedHistoryProbe(string[] args) {
            try {
                Check(args.Length == 7, "Invalid cold-history arguments");
                string root = SafePaths.Normalize(args[1]), allowed = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".codex", "EasyEjectFixtures");
                string leaf = Path.GetFileName(root); Guid challenge, newest; int count = 0;
                Check(Path.GetDirectoryName(root).Equals(allowed, StringComparison.OrdinalIgnoreCase) && leaf.Length == 20 && leaf.StartsWith("run-", StringComparison.Ordinal) && leaf.Substring(4).All(c => "0123456789abcdef".Contains(c)), "Cold probe is outside the owned fixture root");
                Check(Guid.TryParseExact(args[2], "N", out challenge) && ImportHistory.ValidHash(args[3]) && Int32.TryParse(args[4], out count) && count >= 1 && count <= 20 && Guid.TryParseExact(args[5], "N", out newest) && (args[6] == "verify" || args[6] == "refuse"), "Invalid cold-history challenge");
                string history = Path.Combine(root, "history"), destination = Path.Combine(root, "destination");
                using (var directories = SafePaths.HoldDirectories(root, history, destination)) {
                    using (var marker = SafePaths.ReadFile(Path.Combine(root, ".cold-probe-owner"))) {
                        Check(marker.Length == 32 && Native.Info(marker.SafeFileHandle).Links == 1, "Invalid cold probe ownership marker");
                        using (var reader = new StreamReader(marker, Encoding.UTF8)) Check(reader.ReadToEnd() == args[2], "Cold probe challenge changed");
                    }
                    Check(!Directory.Exists(Path.Combine(root, "source")), "Source path must be absent before cold verification");
                    Check(IndependentFileHash(Path.Combine(history, "completed-imports.json")) == args[3], "Cold probe history bytes changed");
                    var values = ImportHistory.Load(history); var completions = new ImportCompletionRegistry(); var operations = new Operations(); bool refused = false;
                    Check(values.Count == count && values[0].Id == args[5], "Cold process lost history retention/order");
                    Check(completions.Entries.Length == 0 && completions.Get(args[5]) == null, "History restored process eject authority");
                    foreach (var value in values) {
                        Check(value.ProfileId == "fixture-volume" && value.Destination.Matches(HistoryIdentity()) && value.DestinationRoot.Equals(destination, StringComparison.OrdinalIgnoreCase), "Cold process history escaped fixture identity");
                        Check(SafePaths.Within(value.Folder, destination) && SafePaths.Within(value.ReceiptPath, destination) && value.Originals.All(x => SafePaths.Within(x.Path, destination)), "Cold history escaped fixture destination");
                        foreach (string path in value.Originals.Select(x => x.Path).Concat(new[] { value.ReceiptPath })) {
                            using (var input = SafePaths.ReadFile(path)) Check(Native.Info(input.SafeFileHandle).Links == 1, "Cold fixture verification file has aliases");
                        }
                        try { ImportHistory.Verify(value, HistoryIdentity(), new FakeGuard(), operations, CancellationToken.None, message => {}); }
                        catch (SafetyException) { if (args[6] != "refuse") throw; refused = true; }
                    }
                    Check(refused == (args[6] == "refuse"), "Cold verification did not match the expected outcome");
                    using (operations.Reserve(new[] { 700, 701 })) {}
                    directories.Validate(); Console.WriteLine("Cold history: " + count + " batches; refused=" + refused + "; completion tokens=" + completions.Entries.Length);
                }
                return 0;
            } catch (Exception ex) { Console.Error.WriteLine("Cold-history probe: " + ex); return 2; }
        }
        static void RunOwnedHistoryProbe(Fixture fixture, int count, string newest, bool refuse = false) {
            string challenge = Guid.NewGuid().ToString("N");
            File.WriteAllText(Path.Combine(fixture.Root, ".cold-probe-owner"), challenge, new UTF8Encoding(false));
            string hash = IndependentFileHash(Path.Combine(NativeHistoryRoot(fixture), "completed-imports.json"));
            var arguments = new[] { "--owned-history-probe", fixture.Root, challenge, hash, count.ToString(System.Globalization.CultureInfo.InvariantCulture), newest, refuse ? "refuse" : "verify" };
            var start = new ProcessStartInfo(typeof(Tests).Assembly.Location, String.Join(" ", arguments.Select(WindowsQuickShareProcess.Quote))) { UseShellExecute = false, CreateNoWindow = true, WorkingDirectory = fixture.Root, RedirectStandardOutput = true, RedirectStandardError = true };
            using (var process = Process.Start(start)) {
                Check(process != null && process.Id != Process.GetCurrentProcess().Id, "Cold verification did not create a separate process");
                if (!process.WaitForExit(60000)) { process.Kill(); process.WaitForExit(); throw new Exception("Owned cold-history child timed out"); }
                string output = process.StandardOutput.ReadToEnd(), error = process.StandardError.ReadToEnd();
                Check(process.ExitCode == 0, "Owned cold-history child failed: " + output + error);
                Console.WriteLine("Owned child " + process.Id + ": " + output.Trim());
            }
        }
        sealed class ByteBoundaryGuard : IImportGuard, IConnectionGuard {
            readonly long boundary; readonly CancellationTokenSource cancellation;
            internal string Pending; internal long Observed;
            internal ByteBoundaryGuard(long boundary, CancellationTokenSource cancellation) { this.boundary = boundary; this.cancellation = cancellation; }
            public int[] ProtectedDisks { get { return new[] { 700, 701 }; } }
            public bool Changed { get {
                if (Pending == null) return false;
                long bytes = new FileInfo(Pending).Length;
                if (bytes <= boundary) return false;
                Observed = bytes; if (cancellation != null) cancellation.Cancel(); return true;
            } }
            public void Validate() {
                if (Observed <= boundary) return;
                if (cancellation != null) cancellation.Token.ThrowIfCancellationRequested();
                throw new SafetyException("Owned fixture guard disconnected beyond the byte boundary");
            }
        }
        static void CheckNativeStoppedTransfer(long bytes, long boundary) {
            using (var fixture = new Fixture()) {
                if (bytes > Int32.MaxValue) Check(Native.FreeSpace(fixture.Root) > 10UL * 1073741824UL, "Optional native fixture requires over ten GiB free");
                string expected = GenerateOwnedClip(fixture, bytes); var operations = new Operations();
                foreach (bool disconnect in new[] { false, true }) using (var cancellation = new CancellationTokenSource()) {
                    var guard = new ByteBoundaryGuard(boundary, disconnect ? null : cancellation);
                    Action run = () => new VerifiedImporter(operations).Run(fixture.Source, fixture.Source, fixture.Destination, "fixture-volume", guard, cancellation.Token, message => {
                        if (message.StartsWith("Copying ", StringComparison.Ordinal)) guard.Pending = Directory.GetFiles(fixture.Destination, ".easyeject-*.partial", SearchOption.AllDirectories).Single();
                    });
                    if (disconnect) Throws<SafetyException>(run); else Throws<OperationCanceledException>(run);
                    Check(guard.Observed > boundary && guard.Observed < bytes, "Transfer did not stop past the byte boundary inside the file");
                    Check(new FileInfo(Path.Combine(fixture.Source, "generated.mp4")).Length == bytes && IndependentFileHash(Path.Combine(fixture.Source, "generated.mp4")) == expected, "Stopped import changed generated originals");
                    Check(Directory.GetFiles(fixture.Destination, "*.partial", SearchOption.AllDirectories).Length == 0 && Directory.GetFiles(fixture.Destination, "*.mp4", SearchOption.AllDirectories).Length == 0 && Directory.GetFiles(fixture.Destination, "verified-import*.json", SearchOption.AllDirectories).Length == 0, "Stopped import retained private/published media or completed evidence");
                    using (operations.Reserve(new[] { 700, 701 })) {}
                    Console.WriteLine((disconnect ? "Fake disconnect" : "Cancellation") + " after " + guard.Observed + " native bytes; original retained");
                }
                var result = new VerifiedImporter(operations).Run(fixture.Source, fixture.Source, fixture.Destination, "fixture-volume", new FakeGuard(), CancellationToken.None, message => {});
                Check(result.Bytes == bytes && result.Files == 1 && result.Reused == 0 && result.Receipt.OriginalsKept && result.Receipt.Files.Single().Sha256 == expected && IndependentFileHash(Saved(result, "generated.mp4")) == expected, "Stopped native transfer did not permit exact clean retry");
            }
        }
        static void ColdHistoryTests() {
            Test("cold process verifies exact newest twenty imports after generated source path absence", () => {
                using (var fixture = new Fixture()) {
                    var ids = new List<string>();
                    for (int i = 0; i < 21; i++) { fixture.Put("clip.mp4", "owned cold history batch " + i); var value = Batch(fixture, fixture.Run(message => {})); ImportHistory.Add(value, NativeHistoryRoot(fixture)); ids.Insert(0, value.Id); }
                    Check(ImportHistory.Load(NativeHistoryRoot(fixture)).Select(x => x.Id).SequenceEqual(ids.Take(20)), "Parent newest twenty retention/order changed");
                    HideOwnedSource(fixture); RunOwnedHistoryProbe(fixture, 20, ids[0]);
                }
            });
            Test("cold process refuses changed original and exact receipt and allows restored retry", () => {
                using (var fixture = new Fixture()) {
                    fixture.Put("clip.mp4", "owned restart original"); var value = Batch(fixture, fixture.Run(message => {})); ImportHistory.Add(value, NativeHistoryRoot(fixture)); HideOwnedSource(fixture);
                    byte[] history = File.ReadAllBytes(Path.Combine(NativeHistoryRoot(fixture), "completed-imports.json"));
                    foreach (string path in new[] { value.Originals.Single().Path, value.ReceiptPath }) {
                        byte[] valid = File.ReadAllBytes(path), changed = path == value.ReceiptPath ? JsonSuffix(valid, " \r\n") : (byte[])valid.Clone();
                        if (path != value.ReceiptPath) changed[0] ^= 1;
                        File.WriteAllBytes(path, changed); RunOwnedHistoryProbe(fixture, 1, value.Id, true);
                        Check(File.ReadAllBytes(path).SequenceEqual(changed) && File.ReadAllBytes(Path.Combine(NativeHistoryRoot(fixture), "completed-imports.json")).SequenceEqual(history), "Cold refusal changed saved media/history");
                        File.WriteAllBytes(path, valid); RunOwnedHistoryProbe(fixture, 1, value.Id);
                    }
                }
            });
            Test("native buffered transfer boundary cancels and refuses fake disconnect with clean retry", () => CheckNativeStoppedTransfer(4 * 1048576L + 131, 1048576));
        }
        static void NativeLargeImportTests() {
            Test("allocated native import beyond two GiB retains full SHA progress reuse and cold history", () => {
                using (var fixture = new Fixture()) {
                    Check(Native.FreeSpace(fixture.Root) > 10UL * 1073741824UL, "Optional native fixture requires over ten GiB free");
                    Console.WriteLine("Generating fully allocated owned file: " + LargeGeneratedBytes + " bytes");
                    var elapsed = Stopwatch.StartNew(); string expected = GenerateOwnedClip(fixture, LargeGeneratedBytes); var progress = new List<ImportWorkProgress>();
                    var result = new VerifiedImporter(new Operations()).Run(fixture.Source, fixture.Source, fixture.Destination, "fixture-volume", new FakeGuard(), CancellationToken.None, message => {}, new MediaOptions(false, true), progress.Add);
                    Check(result.Bytes == LargeGeneratedBytes && result.Files == 1 && result.Reused == 0 && result.Receipt.OriginalsKept && result.Receipt.Files.Single().Bytes == LargeGeneratedBytes && result.Receipt.Files.Single().Sha256 == expected, "Large native result/receipt lost exact byte count or original retention");
                    Check(IndependentFileHash(Saved(result, "generated.mp4")) == expected && IndependentFileHash(Path.Combine(fixture.Source, "generated.mp4")) == expected, "Large native copies do not match independent generated SHA");
                    Check(progress.Count > 20 && progress.Last().Percent == 100 && progress.All(x => x.Fraction >= 0 && x.Fraction <= 1) && progress.Zip(progress.Skip(1), (a, b) => b.Fraction + 0.000001 >= a.Fraction).All(x => x), "Large native progress wrapped or regressed");
                    var reused = fixture.Run(message => {});
                    Check(reused.Folder == result.Folder && reused.Reused == 1 && reused.Bytes == LargeGeneratedBytes && reused.Receipt.Files.Single().Sha256 == expected && Directory.GetFiles(fixture.Destination, "*.mp4", SearchOption.AllDirectories).Length == 1, "Large verified reuse created a duplicate or changed evidence");
                    Check(IndependentFileHash(Saved(reused, "generated.mp4")) == expected && IndependentFileHash(Path.Combine(fixture.Source, "generated.mp4")) == expected, "Verified large reuse changed originals or saved bytes");
                    var batch = Batch(fixture, reused); ImportHistory.Add(batch, NativeHistoryRoot(fixture)); HideOwnedSource(fixture); RunOwnedHistoryProbe(fixture, 1, batch.Id);
                    Console.WriteLine("Allocated native bytes=" + LargeGeneratedBytes + "; SHA256=" + expected + "; elapsed seconds=" + elapsed.Elapsed.TotalSeconds.ToString("F1", System.Globalization.CultureInfo.InvariantCulture));
                }
            });
            Test("native cancellation and fake disconnect beyond signed thirty-two-bit offset retain originals", () => CheckNativeStoppedTransfer(LargeGeneratedBytes, Int32.MaxValue));
        }
    }
}
