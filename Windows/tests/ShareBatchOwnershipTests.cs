using System;
using System.IO;
using System.Linq;
using System.Threading;

namespace EasyEject {
    public static partial class Tests {
        sealed class ShareParentReplacement {
            readonly Fixture fixture;
            internal readonly CompletedBatch Batch;
            internal readonly string Parent, Saved;
            readonly byte[] original, receipt; readonly string originalSource;
            internal bool Changed;
            internal ShareParentReplacement(Fixture fixture, bool multipleParents = false) {
                this.fixture = fixture;
                if (multipleParents) fixture.Put(@"earlier\VID_earlier.mp4", "owned first synthetic Insta360 Luna Ultra master");
                fixture.Put(@"nested\VID_fixture.mp4", "owned synthetic Insta360 Luna Ultra master");
                var result = new VerifiedImporter(new Operations()).Run(fixture.Source, fixture.Source, fixture.Destination, "fixture-volume", new FakeGuard(), CancellationToken.None, message => {}, new MediaOptions(false, true, false));
                Batch = Tests.Batch(fixture, result); Saved = (multipleParents ? Batch.Originals.Last() : Batch.Originals.Single()).Path; Parent = Path.GetDirectoryName(Saved);
                originalSource = Path.Combine(fixture.Source, result.Receipt.Files.Single(entry => Path.Combine(result.Folder, entry.RelativePath) == Saved).SourceRelativePath);
                Check(Parent != Batch.Folder && SafePaths.Within(Parent, fixture.Root), "Owned replacement needs a nested exact recorded parent");
                original = File.ReadAllBytes(Saved); receipt = File.ReadAllBytes(Batch.ReceiptPath);
            }
            internal void Replace() {
                if (Changed) return; Changed = true; ReplaceFixtureDirectory(fixture, Parent); File.WriteAllBytes(Saved, original);
            }
            internal void CheckRetained(FakeShareProcess process, Operations operations) {
                Check(Changed && process.Probes == 0 && process.Encodes == 0 && process.Decodes == 0, "Replaced saved parent reached a media operation");
                Check(File.ReadAllBytes(Saved).SequenceEqual(original) && File.ReadAllBytes(Path.Combine(Parent + "-moved", Path.GetFileName(Saved))).SequenceEqual(original) && File.ReadAllBytes(Batch.ReceiptPath).SequenceEqual(receipt), "Refusal changed replacement/moved originals or receipt");
                Check(File.ReadAllText(Path.Combine(Parent, "foreign.keep")) == "retain replacement" && !Directory.Exists(Path.Combine(Batch.Folder, "Sharing Copies")), "Refusal changed foreign data or created export work");
                Check(File.ReadAllBytes(originalSource).SequenceEqual(original), "Refusal changed the generated source");
                using (operations.Reserve(new[] { 700, 701 })) {}
            }
            internal void Restore() {
                string replacement = Parent + "-replacement", moved = Parent + "-moved";
                Check(new[] { Parent, replacement, moved }.All(path => SafePaths.Within(Path.GetFullPath(path), fixture.Root) && path != fixture.Root), "Restore escaped owned fixture root");
                Directory.Move(Parent, replacement); Directory.Move(moved, Parent);
                Check(File.ReadAllText(Path.Combine(replacement, "foreign.keep")) == "retain replacement", "Fresh retry discarded foreign data");
            }
        }
        static void ShareBatchOwnershipTests() {
            Test("Quick Share batch captures recorded parents before its first device callback", () => {
                using (var fixture = new Fixture()) {
                    var changed = new ShareParentReplacement(fixture); var process = new FakeShareProcess(); var operations = new Operations();
                    var guard = new PathCallbackGuard { Callback = changed.Replace };
                    Throws<QuickShareFailure>(() => RunShare(fixture, changed.Batch, process, operations, guard));
                    changed.CheckRetained(process, operations); changed.Restore();
                    Check(RunShare(fixture, changed.Batch, process, operations).Created == 1, "Fresh direct review could not retry after refusal"); VerifyShareOriginals(changed.Batch); CheckSharePrivateGone(changed.Batch);
                }
            });
            Test("manual sharing captures recorded parents before constructing the destination guard", () => {
                using (var fixture = new Fixture()) {
                    var changed = new ShareParentReplacement(fixture); string root = ManualShareSetup(fixture, changed.Batch); var process = new FakeShareProcess(); var operations = new Operations();
                    var service = ManualShares(root, process, operations, batch => { changed.Replace(); return new FakeGuard(); });
                    var request = service.Prepare(changed.Batch).Choose(QuickSharePreset.Hd, QuickShareColor.Standard);
                    Throws<SafetyException>(() => service.Run(request, CancellationToken.None, message => {}));
                    changed.CheckRetained(process, operations); changed.Restore();
                    var fresh = ManualShares(root, process, operations); Check(fresh.Run(fresh.Prepare(changed.Batch).Choose(QuickSharePreset.Uhd, QuickShareColor.Standard), CancellationToken.None, message => {}).Created == 1, "Fresh manual factory review could not retry");
                    VerifyShareOriginals(changed.Batch); CheckSharePrivateGone(changed.Batch);
                }
            });
            Test("manual sharing initial guard validation cannot replace an exact recorded parent", () => {
                using (var fixture = new Fixture()) {
                    var changed = new ShareParentReplacement(fixture); string root = ManualShareSetup(fixture, changed.Batch); var process = new FakeShareProcess(); var operations = new Operations();
                    var service = ManualShares(root, process, operations, batch => new PathCallbackGuard { Callback = changed.Replace });
                    Throws<SafetyException>(() => service.Run(service.Prepare(changed.Batch).Choose(QuickSharePreset.Hd, QuickShareColor.Standard), CancellationToken.None, message => {}));
                    changed.CheckRetained(process, operations); changed.Restore();
                    var fresh = ManualShares(root, process, operations); Check(fresh.Run(fresh.Prepare(changed.Batch).Choose(QuickSharePreset.Hd, QuickShareColor.Standard), CancellationToken.None, message => {}).Created == 1, "Fresh manual guard review could not retry");
                    VerifyShareOriginals(changed.Batch); CheckSharePrivateGone(changed.Batch);
                }
            });
            Test("Quick Share saved-original report cannot replace its captured parent before opening", () => {
                using (var fixture = new Fixture()) {
                    var changed = new ShareParentReplacement(fixture); var process = new FakeShareProcess(); var operations = new Operations();
                    Throws<QuickShareFailure>(() => new QuickShareEngine(process, operations).Run(changed.Batch, HistoryIdentity(), new FakeGuard(), FixtureShareTools(fixture), QuickSharePreset.Hd, QuickShareColor.Standard, CancellationToken.None, message => {
                        if (message.StartsWith("Verifying saved ", StringComparison.Ordinal)) changed.Replace();
                    }));
                    changed.CheckRetained(process, operations); changed.Restore();
                    Check(RunShare(fixture, changed.Batch, process, operations).Created == 1, "Fresh report review could not retry"); VerifyShareOriginals(changed.Batch); CheckSharePrivateGone(changed.Batch);
                }
            });
            Test("Quick Share captures a future original parent before an earlier original report", () => {
                using (var fixture = new Fixture()) {
                    var changed = new ShareParentReplacement(fixture, true); var process = new FakeShareProcess(); var operations = new Operations();
                    Check(changed.Batch.Originals.Count == 2 && Path.GetDirectoryName(changed.Batch.Originals.First().Path) != changed.Parent, "Future-parent fixture needs separate recorded parents");
                    Throws<QuickShareFailure>(() => new QuickShareEngine(process, operations).Run(changed.Batch, HistoryIdentity(), new FakeGuard(), FixtureShareTools(fixture), QuickSharePreset.Hd, QuickShareColor.Standard, CancellationToken.None, message => {
                        if (message.StartsWith("Verifying saved ", StringComparison.Ordinal)) changed.Replace();
                    }));
                    changed.CheckRetained(process, operations); changed.Restore();
                    Check(RunShare(fixture, changed.Batch, process, operations).Created == 2, "Fresh multiple-parent review could not retry"); VerifyShareOriginals(changed.Batch); CheckSharePrivateGone(changed.Batch);
                }
            });
            Test("Quick Share parent ownership permits ordinary content changes and never scans new clips", () => {
                using (var fixture = new Fixture()) {
                    var owned = new ShareParentReplacement(fixture); string root = ManualShareSetup(fixture, owned.Batch); var process = new FakeShareProcess(); var operations = new Operations(); bool changed = false;
                    string unknown = Path.Combine(owned.Parent, "VID_unrecorded.mp4");
                    var service = ManualShares(root, process, operations, batch => new PathCallbackGuard { Callback = () => {
                        if (changed) return; changed = true; File.WriteAllText(unknown, "retain unrecorded Insta360 Luna Ultra clip"); Directory.SetLastWriteTimeUtc(owned.Parent, DateTime.UtcNow.AddMinutes(-1));
                    } });
                    var result = service.Run(service.Prepare(owned.Batch).Choose(QuickSharePreset.Hd, QuickShareColor.Standard), CancellationToken.None, message => {});
                    Check(changed && result.Created == 1 && process.Encodes == 1 && process.Decodes == 1 && process.Inputs.Single() == owned.Saved && File.ReadAllText(unknown) == "retain unrecorded Insta360 Luna Ultra clip", "Directory ownership rejected ordinary contents or scanned an unrecorded clip");
                    VerifyShareOriginals(owned.Batch); CheckSharePrivateGone(owned.Batch); using (operations.Reserve(new[] { 700, 701 })) {}
                }
            });
        }
    }
}
