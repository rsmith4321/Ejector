using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace EasyEject {
    public static partial class Tests {
        static Dictionary<string, string> SnapshotCrashPrivateWork(CompletedBatch batch) {
            string folder = Path.Combine(batch.Folder, "Sharing Copies");
            var work = Directory.GetDirectories(folder).Where(path => Path.GetFileName(path).StartsWith(".easy-share-", StringComparison.Ordinal)
                && Path.GetFileName(path) != ".easy-share-audits").ToArray();
            Check(work.Length == 1, "Abrupt loss must retain exactly one untrusted private work directory");
            string name = Path.GetFileName(work[0]);
            Check(name.Length == 44 && name.Substring(12).All(c => "0123456789abcdef".Contains(c)), "Retained crash work has no generated identity");
            using (var directories = SafePaths.HoldDirectories(folder, work[0])) {
                var files = Directory.GetFiles(work[0]);
                Check(Directory.GetDirectories(work[0]).Length == 0 && files.Length == 1 && Path.GetFileName(files[0]) == "output.mp4", "Retained crash work is not the owned private encode");
                using (var file = SafePaths.ReadFile(files[0])) Check(file.Length > 0 && file.Length < 64 * 1024 * 1024 && Native.Info(file.SafeFileHandle).Links == 1, "Retained crash output is empty/excessive/aliased");
                var snapshot = files.ToDictionary(path => path, IndependentFileHash); directories.Validate(); return snapshot;
            }
        }
        static void RequireCrashPrivateWork(CompletedBatch batch, Dictionary<string, string> snapshot) {
            var current = SnapshotCrashPrivateWork(batch);
            Check(current.Count == snapshot.Count && snapshot.All(pair => current.ContainsKey(pair.Key) && current[pair.Key] == pair.Value), "Manual retry changed or trusted old private crash work");
        }
        static void RealShareColdCrash(QuickShareTools tools) {
            using (var f = new Fixture()) {
                var batch = GenerateRealShareBatch(f, tools, 640, 360, 30, true, clips: 2); string config = RealShareSetup(f, batch, tools);
                string history = Path.Combine(config, "completed-imports.json"), historyHash = IndependentFileHash(history);
                RunColdRealShare(f, batch, "crash");
                string folder = Path.Combine(batch.Folder, "Sharing Copies"), first = Directory.GetFiles(folder, "*.mp4").Single();
                string firstHash = IndependentFileHash(first), receiptHash = IndependentFileHash(QuickShareEngine.ReceiptPath(first));
                var privateWork = SnapshotCrashPrivateWork(batch);
                VerifyShareOriginals(batch);
                Check(IndependentFileHash(history) == historyHash && !Directory.Exists(f.Source), "Abrupt process loss changed history or source absence");
                RunColdRealShare(f, batch, "crashretry");
                Check(Directory.GetFiles(folder, "*.mp4").Length == 2 && IndependentFileHash(first) == firstHash
                    && IndependentFileHash(QuickShareEngine.ReceiptPath(first)) == receiptHash, "Manual restart replaced verified first output or skipped the remainder");
                RequireCrashPrivateWork(batch, privateWork);
                var published = Directory.GetFiles(folder, "*.mp4").SelectMany(path => new[] { path, QuickShareEngine.ReceiptPath(path) }).ToDictionary(path => path, IndependentFileHash);
                RunColdRealShare(f, batch, "crashreuseall");
                Check(published.All(pair => IndependentFileHash(pair.Key) == pair.Value), "Later fresh reuse changed either exact copy or receipt");
                RequireCrashPrivateWork(batch, privateWork); VerifyShareOriginals(batch);
                Check(IndependentFileHash(history) == historyHash && !Directory.Exists(f.Source), "Crash recovery changed history or restored source");
                Console.WriteLine("Crash rehearsal: first validated publication retained; second untrusted private output unchanged; manual retry creates only remainder; fresh reuse verifies both.");
            }
        }
    }
}
