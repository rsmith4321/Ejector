using System;
using System.IO;
using System.Linq;
using System.Threading;

namespace EasyEject {
    public static partial class Tests {
        static void OriginalOwnershipTests() {
            foreach (bool atReport in new[] { false, true }) {
                bool reportBoundary = atReport;
                Test("original removal " + (reportBoundary ? "later disposition report" : "earlier removal audit") + " cannot replace an emptied parent and clear another source", () => { using (var f = new Fixture()) {
                    f.Put(@"A\first.mp4", "first verified generated original"); f.Put(@"B\second.mp4", "second retained generated original"); string first = Path.Combine(f.Source, @"A\first.mp4"), second = Path.Combine(f.Source, @"B\second.mp4"), parent = Path.GetDirectoryName(first); bool changed = false;
                    Action<string> audit = line => { if (!reportBoundary && !changed && line.StartsWith("Removed verified original") && line.EndsWith(first)) { changed = true; ReplaceFixtureDirectory(f, parent); } };
                    Action<string> report = line => { if (reportBoundary && !changed && line.StartsWith("Removing verified original") && line.EndsWith(@"B\second.mp4")) { changed = true; ReplaceFixtureDirectory(f, parent); } };
                    var failure = Throws<OriginalRemovalFailure>(() => new VerifiedImporter(new Operations(), Native.FreeSpace, audit).Run(f.Source, f.Source, f.Destination, "fixture-volume", new EvidenceGuard(), CancellationToken.None, report, RemoveOptions()));
                    Check(changed && failure.RemovedOriginals == 1 && failure.ClearedTrashFiles == 0 && failure.ClearedTrashRecords == 0 && !File.Exists(first) && File.ReadAllText(second) == "second retained generated original" && File.ReadAllText(Path.Combine(parent, "foreign.keep")) == "retain replacement", "Changed parent crossed a later disposition or lost precise counts/remaining source");
                    CheckOwnershipSaved(ReadRemovalIntent(IntentPath(f))); NoRemovalCompletion(f);
                } });
            }
            Test("original removal final receipt report cannot replace the emptied source parent and publish completion", () => { using (var f = new Fixture()) {
                f.Put(@"A\first.mp4", "verified generated original"); string parent = Path.Combine(f.Source, "A"); bool changed = false;
                var failure = Throws<OriginalRemovalFailure>(() => new VerifiedImporter(new Operations()).Run(f.Source, f.Source, f.Destination, "fixture-volume", new EvidenceGuard(), CancellationToken.None, line => { if (!changed && line.StartsWith("Publishing verified import receipt")) { changed = true; ReplaceFixtureDirectory(f, parent); } }, RemoveOptions()));
                Check(changed && failure.RemovedOriginals == 1 && File.ReadAllText(Path.Combine(parent, "foreign.keep")) == "retain replacement", "Final parent replacement was not exercised or changed foreign contents"); CheckOwnershipSaved(ReadRemovalIntent(IntentPath(f))); NoRemovalCompletion(f);
            } });
            foreach (bool cancel in new[] { false, true }) {
                bool cancelBoundary = cancel;
                Test("original removal private receipt guard " + (cancelBoundary ? "cancellation" : "parent replacement") + " cannot publish completion", () => { using (var f = new Fixture()) using (var stop = new CancellationTokenSource()) {
                    f.Put(@"A\first.mp4", "verified generated original"); string source = Path.Combine(f.Source, @"A\first.mp4"), parent = Path.GetDirectoryName(source); bool changed = false;
                    var guard = new EvidenceGuard { OnValidate = () => { if (!changed && !File.Exists(source) && Directory.GetFiles(f.Destination, ".receipt-*.partial", SearchOption.AllDirectories).Length == 1) { changed = true; if (cancelBoundary) stop.Cancel(); else ReplaceFixtureDirectory(f, parent); } } };
                    var failure = Throws<OriginalRemovalFailure>(() => new VerifiedImporter(new Operations()).Run(f.Source, f.Source, f.Destination, "fixture-volume", guard, stop.Token, _ => {}, RemoveOptions()));
                    Check(changed && failure.RemovedOriginals == 1 && !File.Exists(source) && (cancelBoundary ? stop.IsCancellationRequested : File.ReadAllText(Path.Combine(parent, "foreign.keep")) == "retain replacement"), "Private receipt boundary was not exercised or counts/foreign data changed"); CheckOwnershipSaved(ReadRemovalIntent(IntentPath(f))); NoRemovalCompletion(f);
                } });
            }
            Test("source change after durable receipt at terminal display preserves the completed saved batch and manual history", () => { using (var f = new Fixture()) {
                f.Put(@"A\first.mp4", "verified generated original"); string parent = Path.Combine(f.Source, "A"); bool changed = false;
                var result = new VerifiedImporter(new Operations()).Run(f.Source, f.Source, f.Destination, "fixture-volume", new EvidenceGuard(), CancellationToken.None, _ => {}, RemoveOptions(), progress => { if (progress.Phase == "Import complete") { Check(Directory.GetFiles(f.Destination, "verified-import.json", SearchOption.AllDirectories).Length == 1, "Terminal display preceded durable receipt"); ReplaceFixtureDirectory(f, parent); changed = true; } });
                Check(changed && result.RemovedOriginals == 1 && File.Exists(result.ReceiptPath) && File.ReadAllText(Path.Combine(parent, "foreign.keep")) == "retain replacement", "Post-publication source change invalidated saved completion or altered foreign data"); CheckOwnershipSaved(ReadRemovalIntent(IntentPath(f)));
                string history = Path.Combine(f.Root, "owned-history"); Directory.CreateDirectory(history); ImportHistory.Add(Batch(f, result), history); var restored = ImportHistory.Load(history).Single(); ImportHistory.Verify(restored, HistoryIdentity(), new FakeGuard(), new Operations(), CancellationToken.None, _ => {});
                Check(restored.Id == result.Receipt.Id && restored.Originals.Count == 1, "Post-publication source change lost the exact completed manifest");
            } });
            foreach (int boundary in new[] { 1, 2, 3 }) {
                int selected = boundary;
                Test("held durable intent blocks record-folder move at " + (selected == 3 ? "audit" : selected == 1 ? "initial guard" : "publication guard") + " without returning publication authority", () => { using (var f = new Fixture()) {
                    string record = Path.Combine(f.Destination, "owned-intent-record"); Directory.CreateDirectory(record); var intent = new RemovalIntent { ImportId = Guid.NewGuid().ToString("N"), CreatedUtc = "2026-10-06T10:00:00Z" }; int calls = 0; bool changed = false; string auditedHash = null;
                    Action replace = () => { changed = true; ReplaceFixtureDirectory(f, record); };
                    Action validate = () => { if (++calls == selected && selected != 3) replace(); };
                    Action<string> audit = line => { auditedHash = line.Split(' ')[5]; if (selected == 3) replace(); };
                    Throws<IOException>(() => { using (intent.Publish(record, CancellationToken.None, validate, audit)) { } });
                    Check(changed && !Directory.Exists(record + "-moved") && !File.Exists(Path.Combine(record, "foreign.keep")) && Directory.GetFiles(f.Destination, "*.partial", SearchOption.AllDirectories).Length == 0, "Native-blocked move changed the owner or retained private work");
                    string prior = Path.Combine(record, "removal-intent.json");
                    if (selected == 3) { using (var file = SafePaths.ReadFile(prior)) Check(VerifiedImporter.Hash(file, CancellationToken.None) == auditedHash, "Already published intent evidence was removed or changed after audit refusal"); }
                    else Check(!File.Exists(prior), "Unpublished intent was retained as published evidence");
                } });
            }
            Test("held import intent and audit block record-folder move before every original disposition", () => { using (var f = new Fixture()) {
                f.Put("clip.mp4", "retained generated original"); bool changed = false; string record = null;
                Throws<IOException>(() => new VerifiedImporter(new Operations(), Native.FreeSpace, line => { if (!changed && line.StartsWith("Durable removal intent")) { record = Path.GetDirectoryName(IntentPath(f)); changed = true; ReplaceFixtureDirectory(f, record); } }).Run(f.Source, f.Source, f.Destination, "fixture-volume", new EvidenceGuard(), CancellationToken.None, _ => {}, RemoveOptions()));
                Check(changed && File.ReadAllText(Path.Combine(f.Source, "clip.mp4")) == "retained generated original" && !Directory.Exists(record + "-moved") && !File.Exists(Path.Combine(record, "foreign.keep")) && File.Exists(Path.Combine(record, "removal-intent.json")), "Native-blocked intent move crossed disposition or lost retained evidence"); CheckOwnershipSaved(ReadRemovalIntent(IntentPath(f))); NoRemovalCompletion(f);
            } });
        }
    }
}
