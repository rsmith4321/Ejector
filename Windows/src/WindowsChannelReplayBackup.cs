using System;
using System.IO;
using System.Threading;

namespace EasyEject {
    internal sealed partial class WindowsChannelReplayStore {
        // Explicit config-only recovery. A backup never becomes a release offer or
        // installation authority, and cannot repair unknown/corrupt target history.
        internal sealed class BackupReview : IDisposable {
            internal readonly WindowsChannelReplayStore Owner;
            internal readonly string SourcePath, Policy, SourceHash, CurrentHash;
            internal readonly Native.FileInfo SourceStamp, CurrentStamp;
            internal readonly bool CurrentExists;
            internal readonly WindowsChannelReplayFloor Floor;
            internal readonly SafePaths.DirectoryLease Directories;
            internal readonly FileStream Source;
            readonly byte[] encrypted; bool disposed, used;
            internal BackupReview(WindowsChannelReplayStore owner, string sourcePath, string policy, Snapshot backup, Snapshot current, SafePaths.DirectoryLease directories, FileStream source) {
                Owner = owner; SourcePath = sourcePath; Policy = policy; SourceHash = backup.Hash; SourceStamp = backup.Stamp; Floor = backup.Floor;
                CurrentExists = current.Exists; CurrentHash = current.Hash; CurrentStamp = current.Stamp; Directories = directories; Source = source; encrypted = (byte[])backup.Encrypted.Clone();
            }
            internal string Description { get { return "Authenticated same-user, same-channel backup\r\n" + SourcePath + "\r\nVersion restriction: " + Floor.Version + "\r\nAccepted issue (UTC epoch seconds): " + Floor.IssuedUtc + "\r\nBackup SHA-256: " + SourceHash + "\r\n\r\nThis restores only the Windows update replay restriction. Other settings, media and the backup remain unchanged. A backup cannot prove that no later history was lost; no corrupted or foreign target is reset. Release checks still require fresh signed metadata and current native trust."; } }
            internal void Begin(WindowsChannelReplayStore owner) { if (Owner != owner || disposed || used) throw new SafetyException("This backup review is closed, already used or belongs to another settings store. Review it again."); used = true; }
            internal byte[] ProtectedCopy() { return (byte[])encrypted.Clone(); }
            public void Dispose() { lock (Owner.sync) { if (disposed) return; disposed = true; Source.Dispose(); Directories.Dispose(); } }
        }
        static void BackupGuard(CancellationToken cancel, Func<bool> allowed) {
            cancel.ThrowIfCancellationRequested(); if (allowed != null && !allowed()) throw new SafetyException("Windows backup review activity/lifetime changed. Existing state is retained."); cancel.ThrowIfCancellationRequested();
        }
        internal BackupReview ReviewBackup(ApprovedWindowsChannel channel, string sourcePath, CancellationToken cancel, Func<bool> stillAllowed = null) {
            if (channel == null) throw new SafetyException("Approve the Windows channel policy before reviewing replay backups.");
            sourcePath = SafePaths.Normalize(sourcePath); string target = Path.Combine(root, FileName), policy = WindowsChannelReplayFloor.Policy(channel);
            if (!Path.GetFileName(sourcePath).Equals(FileName, StringComparison.OrdinalIgnoreCase) || sourcePath.Equals(target, StringComparison.OrdinalIgnoreCase)) throw new SafetyException("Select windows-channel-replay.json from a separate settings backup.");
            lock (sync) {
                BackupGuard(cancel, stillAllowed); var directories = SafePaths.HoldDirectories(root, Path.GetDirectoryName(sourcePath)); FileStream source = null;
                try {
                    checkpoint("Before backup read", sourcePath); directories.Validate(); BackupGuard(cancel, stillAllowed);
                    source = SafePaths.ReadFile(sourcePath, 4096); var backup = ReadHeld(source, sourcePath, policy, cancel); directories.Validate(); BackupGuard(cancel, stillAllowed);
                    var current = Read(target, policy, cancel); directories.Validate(); BackupGuard(cancel, stillAllowed);
                    if (current.Exists) { current.Floor.RequireAtLeast(seen); seen = current.Floor; observed = true; }
                    backup.Floor.RequireAtLeast(current.Floor); backup.Floor.RequireAtLeast(seen);
                    var review = new BackupReview(this, sourcePath, policy, backup, current, directories, source);
                    checkpoint("After backup review", sourcePath); CheckBackup(review, cancel, stillAllowed);
                    return review;
                } catch { if (source != null) source.Dispose(); directories.Dispose(); throw; }
            }
        }
        void CheckBackup(BackupReview review, CancellationToken cancel, Func<bool> allowed) {
            review.Directories.Validate(); BackupGuard(cancel, allowed); review.Directories.Validate();
            var backup = ReadHeld(review.Source, review.SourcePath, review.Policy, cancel);
            if (backup.Hash != review.SourceHash || !VerifiedImporter.SameStamp(backup.Stamp, review.SourceStamp) || !backup.Floor.Same(review.Floor)) throw new SafetyException("The held backup changed. Review it again; existing state is retained.");
            review.Directories.Validate(); BackupGuard(cancel, allowed); review.Directories.Validate(); review.Floor.RequireAtLeast(seen);
        }
        // Caller must obtain deliberate confirmation for this exact review. It is
        // consumed on an attempt; a failure/retry requires another explicit review.
        internal bool RestoreBackup(BackupReview review, CancellationToken cancel, Func<bool> stillAllowed = null) {
            if (review == null) throw new ArgumentNullException("review");
            lock (sync) {
                BackupGuard(cancel, stillAllowed); review.Begin(this); CheckBackup(review, cancel, stillAllowed);
                string path = Path.Combine(root, FileName);
                using (var writer = Profiles.WriterGate(root)) {
                    CheckBackup(review, cancel, stillAllowed); var current = Read(path, review.Policy, cancel);
                    if (current.Exists != review.CurrentExists || current.Exists && (current.Hash != review.CurrentHash || !VerifiedImporter.SameStamp(current.Stamp, review.CurrentStamp))) throw new SafetyException("Current Windows replay state changed after review. Refresh and confirm the backup again.");
                    if (current.Exists) current.Floor.RequireAtLeast(seen);
                    review.Floor.RequireAtLeast(current.Floor); CheckBackup(review, cancel, stillAllowed);
                    if (review.Floor.Same(current.Floor)) { using (var same = SafePaths.ReadFile(path, 4096)) ValidatePrevious(current, same, path, cancel); CheckBackup(review, cancel, stillAllowed); return false; }
                    ConfigurationWriter.Write(new WindowsChannelReplayRecord(review.ProtectedCopy()), typeof(WindowsChannelReplayRecord), root, FileName,
                        (stage, file) => {
                            // Retain the restrictive live floor even if the final flush or
                            // postpublication callback fails; it must never be silently reset.
                            if (stage == "After publication") { seen = review.Floor; observed = true; }
                            checkpoint(stage, file); review.Directories.Validate(); if (stage != "After publication") CheckBackup(review, cancel, stillAllowed);
                        }, writer, previous => { CheckBackup(review, cancel, stillAllowed); ValidatePrevious(current, previous, path, cancel); });
                    seen = review.Floor; observed = true; return true;
                }
            }
        }
    }
}
