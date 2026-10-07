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
        internal ManualQuickShareRequest Choose(QuickSharePreset preset, QuickShareColor color) { return new ManualQuickShareRequest(batch, Tools, preset, color); }
    }
    internal sealed class ManualQuickShareRequest {
        readonly CompletedBatch batch;
        internal CompletedBatch Batch { get { return ImportHistory.Clone(batch); } }
        internal readonly QuickShareConfigurationSnapshot Tools;
        internal readonly QuickSharePreset Preset; internal readonly QuickShareColor Color;
        internal ManualQuickShareRequest(CompletedBatch batch, QuickShareConfigurationSnapshot tools, QuickSharePreset preset, QuickShareColor color) {
            ImportHistory.Validate(batch);
            if (tools == null || !Enum.IsDefined(typeof(QuickSharePreset), preset) || !Enum.IsDefined(typeof(QuickShareColor), color)) throw new SafetyException("Choose a supported Quick Share size and color for this action.");
            this.batch = ImportHistory.Clone(batch); Tools = tools; Preset = preset; Color = color;
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
        CompletedBatch RequireCurrent(CompletedBatch expected) {
            ImportHistory.Validate(expected);
            var batch = ImportHistory.Load(root).SingleOrDefault(x => x.Id == expected.Id);
            if (batch == null || BatchHash(batch) != BatchHash(expected)) throw new SafetyException("This exact batch changed or is no longer in the last 20 completed imports. Close the choice and review Import history; no earlier folders are scanned.");
            if (EligibleProfile(batch, Profiles.Catalog(root)) == null) throw new SafetyException("Quick Share is no longer enabled for this saved device profile. Review Saved profiles before retrying.");
            return batch;
        }
        internal QuickShareOffer Prepare(CompletedBatch expected) {
            if (Volatile.Read(ref running) != 0) throw new SafetyException("Quick Share is already running. Wait or cancel it before another choice.");
            var batch = RequireCurrent(expected); var profile = EligibleProfile(batch, Profiles.Catalog(root));
            if (profile == null) throw new SafetyException("Quick Share is no longer enabled for this device.");
            return new QuickShareOffer(batch, profile, QuickShareConfiguration.Load(root));
        }
        internal bool CanOffer(CompletedBatch expected) {
            if (expected == null) return false; ImportHistory.Validate(expected);
            var current = ImportHistory.Load(root).SingleOrDefault(x => x.Id == expected.Id);
            return current != null && BatchHash(current) == BatchHash(expected) && EligibleProfile(current, Profiles.Catalog(root)) != null;
        }
        // Called only after the UI's explicit manual size/LUT dialog returns OK.
        internal QuickShareResult Run(ManualQuickShareRequest request, CancellationToken cancel, Action<string> report) {
            if (Interlocked.CompareExchange(ref running, 1, 0) != 0) throw new SafetyException("Quick Share is already running. No second export started.");
            try {
                cancel.ThrowIfCancellationRequested(); var batch = RequireCurrent(request.Batch);
                using (var tools = QuickShareConfiguration.Acquire(request.Tools, root, request.Color, cancel)) {
                    // Hashing tools may take time; admission is checked again immediately before media work.
                    cancel.ThrowIfCancellationRequested(); batch = RequireCurrent(batch);
                    using (var directories = QuickShareBatch.HoldDirectories(batch, cancel)) {
                        cancel.ThrowIfCancellationRequested(); directories.Validate();
                        var guard = makeGuard(ImportHistory.Clone(batch)); directories.Validate(); cancel.ThrowIfCancellationRequested();
                        guard.Validate(); directories.Validate(); cancel.ThrowIfCancellationRequested();
                        return new QuickShareEngine(process, operations, freeSpace: freeSpace).Run(batch, batch.Destination, guard, tools.Tools, request.Preset, request.Color, cancel, report);
                    }
                }
            } finally { Volatile.Write(ref running, 0); }
        }
    }
    internal static class QuickSharePresentation {
        internal static string Success(CompletedBatch batch, QuickSharePreset preset, QuickShareResult result) {
            int total = result.Created + result.Reused;
            return (total == 0 ? batch.DeviceName + ": no supported Luna master videos in this exact completed import." : batch.DeviceName + ": " + total + " " + (preset == QuickSharePreset.Hd ? "1080p" : "4K") + " sharing copies ready (" + result.Created + " new; " + result.Reused + " reused).")
                + "\nFull-quality imported originals are unchanged. No device was ejected.\n" + (total == 0 ? "Import folder: " + batch.Folder : "Sharing folder: " + result.Folder)
                + (result.AuditPath == null ? "" : "\nAudit: " + result.AuditPath) + (result.CleanupWarnings.Count == 0 ? "" : "\n" + String.Join("\n", result.CleanupWarnings));
        }
        internal static bool CancelNeedsAttention(QuickShareFailure failure) { return failure.Partial.CleanupWarnings.Count != 0 || failure.Partial.PublishedCopies != failure.Partial.PublishedReceipts; }
        internal static bool ReturnToCompletion(ImportCompletionRegistry entries, ImportCompletion expected, bool succeeded) { return succeeded && expected != null && Object.ReferenceEquals(entries.Get(expected.Id), expected); }
    }
}
