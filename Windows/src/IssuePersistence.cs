using System;
using System.IO;
using System.Threading;

namespace EasyEject {
    internal sealed class IssueRecordSnapshot {
        readonly AppIssue value;
        internal readonly bool Exists;
        internal readonly string Hash;
        internal readonly Native.FileInfo Stamp;
        internal readonly string Root;
        internal readonly Native.FileInfo RootIdentity;
        internal AppIssue Value { get { return IssueReview.Copy(value); } }
        internal IssueRecordSnapshot(string root, Native.FileInfo identity) { value = new AppIssue(); Root = root; RootIdentity = identity; }
        internal IssueRecordSnapshot(AppIssue value, string hash, Native.FileInfo stamp, string root, Native.FileInfo identity) : this(root, identity) { this.value = IssueReview.Copy(value); Hash = hash; Stamp = stamp; Exists = true; }
        internal static void Check(IssueRecordSnapshot expected, IssueRecordSnapshot current) {
            if (expected == null || !expected.Root.Equals(current.Root, StringComparison.OrdinalIgnoreCase) || expected.RootIdentity.VolumeSerial != current.RootIdentity.VolumeSerial || expected.RootIdentity.IndexHigh != current.RootIdentity.IndexHigh || expected.RootIdentity.IndexLow != current.RootIdentity.IndexLow || expected.Exists != current.Exists || expected.Exists && (expected.Hash != current.Hash || !VerifiedImporter.SameStamp(expected.Stamp, current.Stamp)))
                throw new SafetyException("Saved issue state changed. Open View issue to reread and review it before saving.");
        }
        internal static bool Same(AppIssue a, AppIssue b) { return a != null && b != null && a.Message == b.Message && a.ObservedUtc == b.ObservedUtc && a.Dismissed == b.Dismissed; }
    }

    // Pins the reviewed folder, not the issue file, while a manual choice is open.
    // The exact previous file is compared under the configuration writer's exclusive hold.
    internal sealed class IssueRecordReview : IDisposable {
        const string Name = "issue-state.json";
        readonly string root; readonly SafePaths.DirectoryLease directories; readonly Action<string> checkpoint; int phase;
        readonly Native.FileInfo rootIdentity;
        internal readonly IssueRecordSnapshot Snapshot;
        internal IssueRecordReview(string root, Action<string> checkpoint = null) {
            this.root = SafePaths.Normalize(root); this.checkpoint = checkpoint;
            directories = SafePaths.HoldDirectories(this.root);
            try { rootIdentity = directories.Identity(this.root); Boundary("Before issue read", null); Snapshot = ReadCurrent(); Boundary("After issue read", null); }
            catch { directories.Dispose(); throw; }
        }
        IssueRecordSnapshot Read(FileStream stream) {
            Configuration.CheckRecord(stream, 16384); var stamp = Native.Info(stream.SafeFileHandle);
            stream.Position = 0; string hash = ImportHistory.Hash(stream, CancellationToken.None); stream.Position = 0;
            var value = JsonDocuments.Read<AppIssue>(stream, 16384);
            if (value == null) throw new SafetyException("Saved issue state is invalid."); value.Validate();
            Configuration.CheckRecord(stream, 16384);
            if (!VerifiedImporter.SameStamp(stamp, Native.Info(stream.SafeFileHandle))) throw new SafetyException("Saved issue state changed during its held read.");
            return new IssueRecordSnapshot(value, hash, stamp, root, rootIdentity);
        }
        IssueRecordSnapshot ReadCurrent() {
            directories.Validate(); string path = Path.Combine(root, Name);
            try { if ((File.GetAttributes(path) & FileAttributes.Directory) != 0) throw new SafetyException("Saved issue state is a directory. Existing data is retained."); }
            catch (FileNotFoundException) { return new IssueRecordSnapshot(root, rootIdentity); }
            using (var stream = SafePaths.ReadFile(path)) { var value = Read(stream); directories.Validate(); return value; }
        }
        void Boundary(string stage, Action validateLive) {
            directories.Validate(); if (validateLive != null) validateLive();
            if (checkpoint != null) checkpoint(stage);
            directories.Validate(); if (validateLive != null) validateLive();
        }
        internal IssueRecordSnapshot Save(AppIssue value, Action validateLive = null) {
            if (Interlocked.CompareExchange(ref phase, 1, 0) != 0) throw new SafetyException("This saved-issue review closed or was already used. Open a fresh review.");
            try {
                if (value == null) throw new ArgumentNullException("value"); var frozen = IssueReview.Copy(value); frozen.Validate();
                Boundary("Before issue save", validateLive);
                ConfigurationWriter.Write(frozen, typeof(AppIssue), root, Name, (stage, path) => Boundary(stage, validateLive), null, previous => {
                    directories.Validate(); if (validateLive != null) validateLive();
                    IssueRecordSnapshot.Check(Snapshot, previous == null ? new IssueRecordSnapshot(root, rootIdentity) : Read(previous));
                });
                Boundary("Before issue readback", validateLive); var result = ReadCurrent();
                if (!result.Exists || !IssueRecordSnapshot.Same(result.Value, frozen)) throw new SafetyException("The issue save finished, but its saved status changed before readback. Reopen View issue before another action.");
                Boundary("After issue readback", validateLive); return result;
            } finally { Interlocked.Exchange(ref phase, 2); directories.Dispose(); }
        }
        public void Dispose() {
            if (Interlocked.CompareExchange(ref phase, 2, 0) == 1) throw new InvalidOperationException("The issue save still owns its reviewed folder.");
            if (Volatile.Read(ref phase) == 2) directories.Dispose();
        }
    }

    // An uncertain save never retries itself. Only a fresh manual review reconciles
    // durable state, retaining a newer or unsaved explanation until explicitly saved.
    internal sealed class IssuePersistenceState {
        internal readonly AppIssue Live = new AppIssue();
        IssueRecordSnapshot known; AppIssue attempted, attemptedFrom; long attemptedRevision; bool dirty;
        internal bool RequiresSave { get { return dirty; } }
        internal static IssuePersistenceState Load(string root) {
            using (var review = new IssueRecordReview(root)) {
                var result = new IssuePersistenceState(); result.known = review.Snapshot; result.Live.Adopt(review.Snapshot.Value); return result;
            }
        }
        internal static IssuePersistenceState Unavailable(string error) {
            var result = new IssuePersistenceState(); result.Live.Record("Saved issue state could not be read and was retained. " + error); result.dirty = true; return result;
        }
        internal string Refresh(IssueRecordReview review) {
            if (known != null && !known.Root.Equals(review.Snapshot.Root, StringComparison.OrdinalIgnoreCase)) throw new SafetyException("This issue review belongs to another saved-state folder.");
            var current = review.Snapshot.Value;
            if (!dirty || IssueRecordSnapshot.Same(current, Live) || attempted != null && Live.Revision == attemptedRevision && IssueRecordSnapshot.Same(current, attempted) && IssueRecordSnapshot.Same(Live, attemptedFrom)) {
                Live.Adopt(current); dirty = false; attempted = attemptedFrom = null;
            }
            known = review.Snapshot;
            return dirty ? "The current explanation has not been confirmed saved. Save current explanation explicitly replaces the saved issue shown below. It does not acknowledge the warning or retry any operation.\n\nSaved issue: " + (current.HasIssue ? current.Message + "\nObserved: " + current.ObservedUtc + (current.Dismissed ? "\nAcknowledged" : "\nNeeds attention") : "No saved issue.") : "";
        }
        void Publish(IssueRecordReview review, AppIssue value, Action validateLive) {
            attempted = IssueReview.Copy(value); attemptedFrom = IssueReview.Copy(Live); attemptedRevision = Live.Revision; dirty = true;
            IssueRecordSnapshot.Check(known, review.Snapshot);
            known = review.Save(value, validateLive); dirty = false; attempted = attemptedFrom = null;
        }
        internal void SaveReviewed(IssueRecordReview review, AppIssue value, Action validateLive) { Publish(review, value, validateLive); }
        internal void Record(string message, string root) { Record(message, () => root); }
        internal void Record(string message, Func<string> root) {
            Live.Record(message); dirty = true; var owner = new IssueReview(Live, value => {});
            try {
                if (known == null) throw new SafetyException("Saved issue state is unavailable. Open View issue to reread it; the existing record was retained.");
                using (var review = new IssueRecordReview(root())) Publish(review, IssueReview.Copy(Live), owner.ValidateCurrent);
            } finally { owner.Cancel(); }
        }
        internal void Clear(string root) {
            if (!Live.HasIssue) return;
            if (dirty || known == null || !IssueRecordSnapshot.Same(known.Value, Live)) throw new SafetyException("The earlier issue save is unconfirmed. Open View issue before clearing its saved status.");
            var owner = new IssueReview(Live, value => {}); var cleared = IssueReview.Copy(Live); cleared.Clear();
            try { using (var review = new IssueRecordReview(root)) Publish(review, cleared, owner.ValidateCurrent); Live.Clear(); }
            finally { owner.Cancel(); }
        }
    }
}
