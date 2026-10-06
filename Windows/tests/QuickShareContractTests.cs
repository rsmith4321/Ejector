using System;
using System.IO;
using System.Linq;
using System.Runtime.Serialization.Json;
using System.Threading;

namespace EasyEject {
    public static partial class Tests {
        static QuickShareReceipt ShareContractReceipt(string output) {
            using (var file = SafePaths.ReadFile(QuickShareEngine.ReceiptPath(output))) return (QuickShareReceipt)new DataContractJsonSerializer(typeof(QuickShareReceipt)).ReadObject(file);
        }
        static void QuickShareContractTests() {
            Test("sharing hash-matching original outside the recorded batch refuses before any media process", () => { using (var f = new Fixture()) {
                var batch = LunaBatch(f); var changed = ImportHistory.Clone(batch); string outside = Path.Combine(f.Root, "VID_outside.mp4");
                byte[] original = File.ReadAllBytes(batch.Originals.Single().Path), receipt = File.ReadAllBytes(batch.ReceiptPath); File.WriteAllBytes(outside, original); changed.Originals[0].Path = outside;
                using (var file = SafePaths.ReadFile(outside)) Check(file.Length == changed.Originals[0].Bytes && ImportHistory.Hash(file, CancellationToken.None) == changed.Originals[0].Sha256, "Outside fixture does not match claimed original bytes/hash");
                var process = new FakeShareProcess(); var failure = Throws<QuickShareFailure>(() => RunShare(f, changed, process));
                Check(failure.InnerException is SafetyException && process.Probes == 0 && process.Encodes == 0 && process.Decodes == 0 && !Directory.Exists(Path.Combine(batch.Folder, "Sharing Copies")) && File.ReadAllBytes(outside).SequenceEqual(original) && File.ReadAllBytes(batch.ReceiptPath).SequenceEqual(receipt), "Outside original reached media work or changed either owned input/receipt");
                VerifyShareOriginals(batch);
            } });
            Test("sharing Mac mixed batch converts only the selected VID Luna master and counts other MP4 skipped", () => { using (var f = new Fixture()) {
                f.Put("other.mp4", "synthetic fixture with injected Insta360 Luna Ultra identity"); var batch = LunaBatch(f); var process = new FakeShareProcess();
                var result = RunShare(f, batch, process); string master = batch.Originals.Single(x => Path.GetFileName(x.Path) == "VID_fixture.mp4").Path;
                Check(result.Created == 1 && result.Reused == 0 && result.Skipped == 1 && process.Probes == 2 && process.Encodes == 1 && process.Decodes == 1 && process.Inputs.SequenceEqual(new[] { master }) && result.Outputs.SequenceEqual(new[] { ShareFile(batch) }) && !File.Exists(ShareFile(batch, "other")), "Mixed saved batch broadened processing, skipped count or published names");
                VerifyShareOriginals(batch); CheckSharePrivateGone(batch);
            } });
            foreach (var dimensions in new[] { new[] { 640, 360 }, new[] { 720, 1280 } }) {
                int width = dimensions[0], height = dimensions[1];
                Test("sharing Mac full silent " + width + "x" + height + " transaction keeps framing no upscale and 30 fps in both sizes", () => { using (var f = new Fixture()) {
                    var batch = LunaBatch(f); var process = new FakeShareProcess { Input = ShareInput(width, height, "60/1", false) }; int outputChecks = 0;
                    process.ChangeOutput = output => { Check(output.Video.Width == width && output.Video.Height == height && output.Video.FrameRate == "30/1" && output.Audio == null, "Output probe changed silent framing or rate"); outputChecks++; };
                    var hd = RunShare(f, batch, process); byte[] hdBytes = File.ReadAllBytes(hd.Outputs.Single()), hdReceiptBytes = File.ReadAllBytes(QuickShareEngine.ReceiptPath(hd.Outputs.Single()));
                    var uhd = RunShare(f, batch, process, preset: QuickSharePreset.Uhd); var hdReceipt = ShareContractReceipt(hd.Outputs.Single()); var uhdReceipt = ShareContractReceipt(uhd.Outputs.Single());
                    Check(hd.Created == 1 && uhd.Created == 1 && outputChecks == 2 && process.Encodes == 2 && process.Decodes == 2 && hdReceipt.Width == width && hdReceipt.Height == height && hdReceipt.FrameRate == 30 && hdReceipt.Preset == "1080p" && uhdReceipt.Width == width && uhdReceipt.Height == height && uhdReceipt.FrameRate == 30 && uhdReceipt.Preset == "4K" && uhdReceipt.Color == QuickShareColor.Standard.ToString() && uhdReceipt.LutSha256 == null, "Full transaction/receipts lost independent selected size, silent probe or no-upscale dimensions");
                    Check(File.ReadAllBytes(hd.Outputs.Single()).SequenceEqual(hdBytes) && File.ReadAllBytes(QuickShareEngine.ReceiptPath(hd.Outputs.Single())).SequenceEqual(hdReceiptBytes), "4K selection replaced prior 1080p output/receipt"); VerifyShareOriginals(batch); CheckSharePrivateGone(batch);
                } });
            }
            Test("sharing normal-color action preserves a prior receipt advertising I-Log without reusing or replacing it", () => { using (var f = new Fixture()) {
                var batch = LunaBatch(f); var first = RunShare(f, batch, new FakeShareProcess()); string output = first.Outputs.Single(), receiptPath = QuickShareEngine.ReceiptPath(output); byte[] priorOutput = File.ReadAllBytes(output);
                // Inject receipt metadata only. This does not claim a real LUT transform occurred.
                var receipt = ShareContractReceipt(output); receipt.Color = QuickShareColor.LunaILog.ToString(); receipt.LutSha256 = QuickShareMedia.OfficialLutSha;
                using (var file = File.Create(receiptPath)) new DataContractJsonSerializer(typeof(QuickShareReceipt)).WriteObject(file, receipt); byte[] priorReceipt = File.ReadAllBytes(receiptPath);
                var process = new FakeShareProcess(); var next = RunShare(f, batch, process);
                Check(next.Created == 1 && next.Reused == 0 && process.Encodes == 1 && process.Decodes == 1 && next.Outputs.Single() != output && ShareContractReceipt(next.Outputs.Single()).Color == QuickShareColor.Standard.ToString() && ShareContractReceipt(next.Outputs.Single()).LutSha256 == null && File.ReadAllBytes(output).SequenceEqual(priorOutput) && File.ReadAllBytes(receiptPath).SequenceEqual(priorReceipt), "Color/LUT receipt mismatch reused or overwrote earlier output/receipt");
                VerifyShareOriginals(batch); CheckSharePrivateGone(batch);
            } });
            Test("sharing changed original after successful export preserves prior copies receipt and manual retry history", () => { using (var f = new Fixture()) {
                var batch = LunaBatch(f); string root = ManualShareSetup(f, batch); var process = new FakeShareProcess(); var service = ManualShares(root, process);
                var first = service.Run(service.Prepare(batch).Choose(QuickSharePreset.Hd, QuickShareColor.Standard), CancellationToken.None, _ => {}); string output = first.Outputs.Single(), receiptPath = QuickShareEngine.ReceiptPath(output);
                byte[] outputBytes = File.ReadAllBytes(output), receiptBytes = File.ReadAllBytes(receiptPath), originalBytes = File.ReadAllBytes(batch.Originals.Single().Path), historyBytes = File.ReadAllBytes(Path.Combine(root, "completed-imports.json")); int probes = process.Probes;
                File.Delete(Path.Combine(f.Source, "VID_fixture.mp4")); File.AppendAllText(batch.Originals.Single().Path, "changed saved original");
                Throws<QuickShareFailure>(() => service.Run(service.Prepare(batch).Choose(QuickSharePreset.Hd, QuickShareColor.Standard), CancellationToken.None, _ => {}));
                Check(process.Probes == probes && process.Encodes == 1 && File.ReadAllBytes(output).SequenceEqual(outputBytes) && File.ReadAllBytes(receiptPath).SequenceEqual(receiptBytes) && File.ReadAllBytes(Path.Combine(root, "completed-imports.json")).SequenceEqual(historyBytes) && service.CanOffer(batch) && ImportHistory.Load(root).Single().Id == batch.Id, "Refused altered original discarded prior copies/receipt/history, hid retry or reached tools");
                File.WriteAllBytes(batch.Originals.Single().Path, originalBytes); var retry = service.Run(service.Prepare(batch).Choose(QuickSharePreset.Hd, QuickShareColor.Standard), CancellationToken.None, _ => {});
                Check(retry.Reused == 1 && retry.Created == 0 && process.Encodes == 1 && File.ReadAllBytes(output).SequenceEqual(outputBytes) && File.ReadAllBytes(receiptPath).SequenceEqual(receiptBytes), "Fresh explicit retry after exact fixture repair changed an earlier verified copy"); VerifyShareOriginals(batch); CheckSharePrivateGone(batch);
            } });
        }
    }
}
