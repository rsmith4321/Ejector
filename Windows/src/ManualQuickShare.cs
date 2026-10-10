using System;
using System.IO;
using System.Linq;
using System.Runtime.Serialization.Json;
using System.Threading;

namespace EasyEject {
    internal sealed class QuickShareOffer {
        readonly CompletedBatch batch; readonly ImportProfile profile;
        internal readonly QuickShareConfigurationSnapshot Tools;
        internal CompletedBatch Batch { get { return ImportHistory.Clone(batch); } }
        internal ImportProfile Profile { get { return profile.Copy(); } }
        internal QuickShareOffer(CompletedBatch batch, ImportProfile profile, QuickShareConfigurationSnapshot tools) { this.batch = ImportHistory.Clone(batch); this.profile = profile.Copy(); Tools = tools; }
        internal QuickShareCamera Camera { get { return QuickShareProfile.Camera(profile); } }
        internal ManualQuickShareRequest Choose(QuickSharePreset preset, QuickShareColor color) { return new ManualQuickShareRequest(batch, Tools, preset, color, Camera); }
    }
    internal sealed class ManualQuickShareRequest {
        readonly CompletedBatch batch;
        internal CompletedBatch Batch { get { return ImportHistory.Clone(batch); } }
        internal readonly QuickShareConfigurationSnapshot Tools;
        internal readonly QuickSharePreset Preset; internal readonly QuickShareColor Color; internal readonly QuickShareCamera Camera;
        internal ManualQuickShareRequest(CompletedBatch batch, QuickShareConfigurationSnapshot tools, QuickSharePreset preset, QuickShareColor color, QuickShareCamera camera = QuickShareCamera.Luna) {
            ImportHistory.Validate(batch);
            if (tools == null || !Enum.IsDefined(typeof(QuickSharePreset), preset) || !Enum.IsDefined(typeof(QuickShareColor), color) || !Enum.IsDefined(typeof(QuickShareCamera), camera) || color != QuickShareColor.Standard && color != QuickShareMedia.LogColor(camera)) throw new SafetyException("Choose a supported Quick Share size and color for this action.");
            this.batch = ImportHistory.Clone(batch); Tools = tools; Preset = preset; Color = color; Camera = camera;
        }
    }
    internal sealed class ManualQuickShareService {
        readonly string root; readonly Operations operations; readonly IQuickShareProcess process; readonly Func<CompletedBatch, IImportGuard> makeGuard; readonly Func<string, ulong> freeSpace;
        int running;
        internal ManualQuickShareService(string root, Operations operations, IQuickShareProcess process, Func<CompletedBatch, IImportGuard> makeGuard, Func<string, ulong> freeSpace = null) {
            this.root = SafePaths.Normalize(root); this.operations = operations; this.process = process; this.makeGuard = makeGuard; this.freeSpace = freeSpace;
        }
        internal static ImportProfile EligibleProfile(CompletedBatch batch, SavedProfile[] profiles) {
            if (batch == null) return null;
            var matches = profiles.Where(x => x.CanEdit).Select(x => x.Profile).Where(x => x.VolumeId == batch.ProfileId).ToArray();
            if (matches.Length != 1 || !QuickShareProfile.Enabled(matches[0])) return null; return matches[0].Copy();
        }
        internal static string BatchHash(CompletedBatch batch) {
            ImportHistory.Validate(batch);
            using (var stream = new MemoryStream()) { new DataContractJsonSerializer(typeof(CompletedBatch)).WriteObject(stream, batch); stream.Position = 0; return ImportHistory.Hash(stream, CancellationToken.None); }
        }
        CompletedBatch RequireCurrent(CompletedBatch expected, QuickShareCamera? camera = null) {
            ImportHistory.Validate(expected);
            var batch = ImportHistory.Load(root).SingleOrDefault(x => x.Id == expected.Id);
            if (batch == null || BatchHash(batch) != BatchHash(expected)) throw new SafetyException("This exact batch changed or is no longer in the last 20 completed imports. Close the choice and review Import history; no earlier folders are scanned.");
            var profile = EligibleProfile(batch, Profiles.Catalog(root));
            if (profile == null) throw new SafetyException("Quick Share is no longer enabled for this saved device profile. Review Saved profiles before retrying.");
            if (camera.HasValue && QuickShareProfile.Camera(profile) != camera.Value) throw new SafetyException("This device's Quick Share camera changed. Open a fresh Quick Share choice.");
            return batch;
        }
        internal QuickShareOffer Prepare(CompletedBatch expected) {
            if (Volatile.Read(ref running) != 0) throw new SafetyException("Quick Share is already running. Wait or cancel it before another choice.");
            var batch = RequireCurrent(expected); var profile = EligibleProfile(batch, Profiles.Catalog(root));
            if (profile == null) throw new SafetyException("Quick Share is no longer enabled for this device.");
            return new QuickShareOffer(batch, profile, QuickShareConfiguration.Load(root));
        }
        // DJI O4 Pro batches are stabilized in Gyroflow before Quick Share is offered.
        internal bool GyroflowFirst(CompletedBatch expected) {
            if (!CanOffer(expected)) return false;
            var current = ImportHistory.Load(root).SingleOrDefault(x => x.Id == expected.Id);
            return current != null && QuickShareProfile.GyroflowFirst(EligibleProfile(current, Profiles.Catalog(root)));
        }
        internal bool CanOffer(CompletedBatch expected) {
            if (expected == null) return false; ImportHistory.Validate(expected);
            var current = ImportHistory.Load(root).SingleOrDefault(x => x.Id == expected.Id);
            return current != null && BatchHash(current) == BatchHash(expected) && EligibleProfile(current, Profiles.Catalog(root)) != null;
        }
        // Called only after the UI's explicit manual size/LUT dialog returns OK.
        internal QuickShareResult Run(ManualQuickShareRequest request, CancellationToken cancel, Action<string> report, Action<IImportGuard> ready = null) {
            if (Interlocked.CompareExchange(ref running, 1, 0) != 0) throw new SafetyException("Quick Share is already running. No second export started.");
            try {
                cancel.ThrowIfCancellationRequested(); var batch = RequireCurrent(request.Batch, request.Camera);
                using (var tools = QuickShareConfiguration.Acquire(request.Tools, root, request.Color, cancel)) {
                    // Hashing tools may take time; admission is checked again immediately before media work.
                    cancel.ThrowIfCancellationRequested(); batch = RequireCurrent(batch, request.Camera);
                    using (var directories = QuickShareBatch.HoldDirectories(batch, cancel)) {
                        cancel.ThrowIfCancellationRequested(); directories.Validate();
                        var guard = makeGuard(ImportHistory.Clone(batch)); directories.Validate(); cancel.ThrowIfCancellationRequested();
                        guard.Validate(); directories.Validate(); cancel.ThrowIfCancellationRequested();
                        if (ready != null) ready(guard);
                        return new QuickShareEngine(process, operations, freeSpace: freeSpace).Run(batch, batch.Destination, guard, tools.Tools, request.Preset, request.Color, cancel, report, request.Camera);
                    }
                }
            } finally { Volatile.Write(ref running, 0); }
        }
    }
    internal static class QuickSharePresentation {
        // Advisory only, for an early message before the prompt. The engine repeats this
        // check while holding the batch and never shares a partial Gyroflow set.
        internal static string GyroflowPending(CompletedBatch batch) {
            if (batch == null) return null;
            var missing = batch.Originals.Where(x => QuickShareMedia.IsDjiO4ProMasterName(x.Path) && !File.Exists(QuickShareMedia.GyroflowExportPath(x.Path))).Select(x => Path.GetFileName(x.Path)).ToArray();
            if (missing.Length > 0) return "Finish these O4 Pro videos in Gyroflow first: " + String.Join(", ", missing) + ". Export beside the originals with the default _stabilized.mp4 name, then choose Quick Share.";
            return batch.Originals.Any(x => QuickShareMedia.IsDjiO4ProMasterName(x.Path)) ? null : "Stabilize the O4 Pro originals in Gyroflow, then use Quick Share for their _stabilized.mp4 exports.";
        }
        internal static string Success(CompletedBatch batch, QuickSharePreset preset, QuickShareResult result, QuickShareCamera camera = QuickShareCamera.Luna) {
            int total = result.Created + result.Reused;
            return (total == 0 ? batch.DeviceName + ": no supported " + (camera == QuickShareCamera.Luna ? "Luna" : "DJI O4 Pro") + " master videos in this exact completed import." : batch.DeviceName + ": " + total + " " + (preset == QuickSharePreset.Hd ? "1080p" : "4K") + " sharing copies ready (" + result.Created + " new; " + result.Reused + " reused).")
                + "\nFull-quality imported originals are unchanged. No device was ejected.\n" + (total == 0 ? "Import folder: " + batch.Folder : "Sharing folder: " + result.Folder)
                + (result.AuditPath == null ? "" : "\nAudit: " + result.AuditPath) + (result.CleanupWarnings.Count == 0 ? "" : "\n" + String.Join("\n", result.CleanupWarnings));
        }
        internal static bool CancelNeedsAttention(QuickShareFailure failure) { return failure.Partial.CleanupWarnings.Count != 0 || failure.Partial.PublishedCopies != failure.Partial.PublishedReceipts; }
        internal static bool ReturnToCompletion(ImportCompletionRegistry entries, ImportCompletion expected, bool succeeded) { return succeeded && expected != null && Object.ReferenceEquals(entries.Get(expected.Id), expected); }
    }
}
