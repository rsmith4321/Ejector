using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace EasyEject {
    public static partial class Tests {
        sealed class ModernReviewFixture : IDisposable {
            internal readonly ModernChannelFixture Source = new ModernChannelFixture();
            internal readonly WindowsUpdateCoordinator Checks; internal readonly WindowsReleaseReviewSession Review;
            internal int ArchiveCalls, PackageCalls; internal Action<CancellationToken> BeforeArchive, BeforePackage;
            internal bool IgnoreArchiveCancellation;
            internal Func<WindowsUpdateCheckScope, Task<WindowsChannelOffer>> Operation;
            internal ModernReviewFixture() {
                Checks = new WindowsUpdateCoordinator(true, scope => Operation == null ? Task.FromResult(Source.Offer()) : Operation(scope), () => Source.Now, TimeSpan.FromDays(1), false);
                Review = WindowsReleaseReviewSession.ForModern(Checks, Source.Channel, Archive, Package);
                Checks.Start(); Checks.SetReviewOpen(true); Checks.PublishIdle(true); Review.PublishAvailable(true);
            }
            internal ModernWindowsArchiveReview Archive(string path, CancellationToken cancel) { ArchiveCalls++; if (BeforeArchive != null) BeforeArchive(cancel); return new ModernWindowsArchiveReview(path, Source.Channel.Publisher, new WindowsManifestTrust(Source.Archive.Package.Certificate), IgnoreArchiveCancellation ? CancellationToken.None : cancel); }
            internal ModernWindowsApplicationPackageReview Package(string path, CancellationToken cancel) { PackageCalls++; if (BeforePackage != null) BeforePackage(cancel); return new ModernWindowsApplicationPackageReview(path, Source.Channel.Publisher, Source.Archive.Package.Executable, new WindowsManifestTrust(Source.Archive.Package.Certificate), cancel, FakeComponentApproval(Source.Archive.Package)); }
            internal WindowsReleaseReviewResult CheckRelease() { var result = ChannelAwait(() => Review.CheckAsync(CancellationToken.None)); Check(Review.TryPresent(result), "Current metadata delivery failed"); return result; }
            internal WindowsReleaseReviewResult Zip(string path = null) { var result = ChannelAwait(() => Review.ArchiveAsync(path ?? Source.Archive.Path, CancellationToken.None)); Check(Review.TryPresent(result), "Current ZIP delivery failed"); return result; }
            internal WindowsReleaseReviewResult Folder() { var result = ChannelAwait(() => Review.ExtractedAsync(Source.Archive.Package.Root, CancellationToken.None)); Check(Review.TryPresent(result), "Current folder delivery failed"); return result; }
            public void Dispose() { try { Review.Dispose(); Checks.Dispose(); } finally { Source.Dispose(); } }
        }
        static void ModernWindowsReleaseReviewTests() {
            Test("modern unconfigured review admits no check or file factory and writes no history", delegate {
                using (var f = new ModernChannelFixture()) using (var checks = new WindowsUpdateCoordinator(false, null, () => f.Now, TimeSpan.FromDays(1), true)) {
                    int calls = 0; using (var review = WindowsReleaseReviewSession.ForModern(checks, null,
                        (path, cancel) => { calls++; throw new Exception("Unexpected archive"); }, (path, cancel) => { calls++; throw new Exception("Unexpected application"); })) {
                        review.PublishAvailable(true); checks.Start(); checks.PublishIdle(true);
                        Throws<SafetyException>(() => review.CheckAsync(CancellationToken.None)); Throws<SafetyException>(() => review.ArchiveAsync(f.Archive.Path, CancellationToken.None));
                        Check(calls == 0 && !review.Configured && !review.CanArchive && !Directory.Exists(f.History), "Unconfigured review acquired authority/storage");
                    }
                }
            });
            Test("review factories cannot substitute classic and modern policies", delegate {
                using (var f = new ModernReviewFixture()) {
                    Throws<SafetyException>(() => new WindowsReleaseReviewSession(f.Checks, f.Source.Channel));
                    var p = f.Source.Channel; var classic = new ApprovedWindowsChannel(p.Metadata.AbsoluteUri, p.Signature.AbsoluteUri, p.Archives.AbsoluteUri, p.Publisher);
                    Throws<SafetyException>(() => WindowsReleaseReviewSession.ForModern(f.Checks, classic)); Throws<SafetyException>(() => WindowsReleaseViewBackend.ForModern(f.Checks, classic, ApplicationInformation.Version));
                    Check(f.ArchiveCalls == 0 && f.PackageCalls == 0, "Wrong policy invoked files");
                }
            });
            Test("modern review admits each selected-file action only after one-use foreground delivery", delegate {
                using (var f = new ModernReviewFixture()) {
                    Throws<SafetyException>(() => f.Review.ArchiveAsync(f.Source.Archive.Path, CancellationToken.None));
                    var check = ChannelAwait(() => f.Review.CheckAsync(CancellationToken.None)); Check(check.Accepted && !f.Review.CanArchive, "Metadata worker granted action early");
                    Check(f.Review.TryPresent(check) && !f.Review.TryPresent(check) && f.Review.CanArchive, "Metadata delivery is replayable");
                    var zip = ChannelAwait(() => f.Review.ArchiveAsync(f.Source.Archive.Path, CancellationToken.None)); Check(zip.Accepted && !f.Review.CanExtracted, "ZIP worker granted action early");
                    Check(f.Review.TryPresent(zip) && !f.Review.TryPresent(zip) && f.Review.CanExtracted, "ZIP delivery is replayable");
                    var folder = f.Folder(); Check(!folder.Accepted && f.ArchiveCalls == 2 && f.PackageCalls == 1, "Dummy classic application gained modern approval or ZIP was not reopened"); f.Source.Archive.Released();
                }
            });
            Test("modern selected-file review refuses missing and changed ZIP without reaching application factory", delegate {
                using (var f = new ModernReviewFixture()) {
                    Check(f.CheckRelease().Accepted && f.Zip().Accepted, "Setup failed"); byte[] saved = File.ReadAllBytes(f.Source.Archive.Path); File.WriteAllText(f.Source.Archive.Path, "retained changed archive");
                    Check(!f.Folder().Accepted && f.PackageCalls == 0 && File.ReadAllText(f.Source.Archive.Path) == "retained changed archive", "Changed ZIP reached application or was cleaned");
                    File.WriteAllBytes(f.Source.Archive.Path, saved); Check(f.Zip().Accepted, "Manual ZIP retry refused");
                    string missing = Path.Combine(f.Source.Archive.Package.Owned.Destination, "absent.zip"); Check(!f.Zip(missing).Accepted && !f.Review.CanExtracted && !File.Exists(missing), "Missing ZIP created content or retained prior extracted permission");
                }
            });
            Test("modern malformed selected ZIP is retained and explicit original retry reopens fresh contents", delegate {
                using (var f = new ModernReviewFixture()) {
                    Check(f.CheckRelease().Accepted, "Setup failed"); string bad = Path.Combine(f.Source.Archive.Package.Owned.Destination, "bad.zip"); File.WriteAllText(bad, "retained invalid ZIP");
                    Check(!f.Zip(bad).Accepted && !f.Review.CanExtracted && f.PackageCalls == 0 && File.ReadAllText(bad) == "retained invalid ZIP", "Bad ZIP was cleaned or approved");
                    Check(f.Zip().Accepted && f.Review.CanExtracted, "Explicit retry failed"); f.Source.Archive.Released();
                }
            });
            Test("modern failed metadata refresh clears prior ZIP and requires fresh manual selection", delegate {
                using (var f = new ModernReviewFixture()) {
                    Check(f.CheckRelease().Accepted && f.Zip().Accepted, "Setup failed"); f.Operation = scope => Task.FromException<WindowsChannelOffer>(new SafetyException("fixture metadata failure"));
                    Check(!f.CheckRelease().Accepted && !f.Review.CanArchive && !f.Review.CanExtracted, "Failed refresh retained file authority");
                    f.Operation = null; Check(f.CheckRelease().Accepted && f.Review.CanArchive && !f.Review.CanExtracted, "Fresh check restored old ZIP");
                }
            });
            Test("modern application factory failure releases reopened ZIP and retains selected contents", delegate {
                using (var f = new ModernReviewFixture()) {
                    Check(f.CheckRelease().Accepted && f.Zip().Accepted, "Setup failed"); f.BeforePackage = cancel => { throw new SafetyException("fixture application failure"); };
                    var result = f.Folder(); Check(!result.Accepted && result.Message.Contains("fixture application failure") && f.ArchiveCalls == 2 && f.PackageCalls == 1, "Application callback failure was hidden or skipped");
                    f.Source.Archive.Released(); Check(File.Exists(f.Source.Archive.Path), "Failure cleaned selected archive");
                }
            });
            Test("modern expired or freshly untrusted metadata refuses before selected file access", delegate {
                foreach (bool expire in new[] { false, true }) using (var f = new ModernReviewFixture()) {
                    Check(f.CheckRelease().Accepted, "Setup failed"); if (expire) f.Source.Now = f.Source.Now.AddHours(2); else f.Source.Archive.Package.Certificate.Throw = true;
                    Check(!f.Zip().Accepted && f.ArchiveCalls == 0 && f.PackageCalls == 0, "Stale trust opened a selected file");
                }
            });
            Test("modern ignored metadata cancellation retains request ownership and cannot revive on idle return", delegate {
                using (var f = new ModernReviewFixture()) {
                    var pending = new TaskCompletionSource<WindowsChannelOffer>(); f.Operation = scope => pending.Task; var work = f.Review.CheckAsync(CancellationToken.None);
                    f.Review.PublishAvailable(false); f.Checks.PublishIdle(false); f.Review.PublishAvailable(true); f.Checks.PublishIdle(true);
                    Check(f.Review.Busy && f.Checks.Checking, "Ignored worker released early"); Throws<SafetyException>(() => f.Review.CheckAsync(CancellationToken.None));
                    pending.SetResult(f.Source.Offer()); var result = ChannelAwait(() => work);
                    Check(result.Cancelled && !f.Review.TryPresent(result) && !f.Review.CanArchive && !f.Review.Busy, "Idle return revived old metadata");
                    f.Operation = null; Check(f.CheckRelease().Accepted, "Fresh manual retry failed");
                }
            });
            Test("modern late archive returned after Stop is disposed before fresh manual retry", delegate {
                using (var f = new ModernReviewFixture()) using (var entered = new ManualResetEventSlim()) using (var release = new ManualResetEventSlim()) {
                    Check(f.CheckRelease().Accepted, "Setup failed");
                    f.IgnoreArchiveCancellation = true; f.BeforeArchive = cancel => { entered.Set(); if (!release.Wait(8000)) throw new Exception("Archive fixture wait exceeded bound"); };
                    var work = f.Review.ArchiveAsync(f.Source.Archive.Path, CancellationToken.None);
                    try { Check(entered.Wait(8000), "Archive callback not reached"); f.Review.Cancel(); Check(f.Review.Busy && !f.Review.CanCheck, "Stop released active lifetime"); }
                    finally { release.Set(); }
                    var result = ChannelAwait(() => work); Check(result.Cancelled && !result.Accepted && !f.Review.CanExtracted, "Stopped ZIP returned authority"); f.Source.Archive.Released();
                    f.BeforeArchive = null; f.IgnoreArchiveCancellation = false; Check(f.Zip().Accepted, "Manual retry failed");
                }
            });
            Test("modern queued metadata delivery refuses intervening activity and requires a fresh check", delegate {
                using (var f = new ModernReviewFixture()) {
                    var result = ChannelAwait(() => f.Review.CheckAsync(CancellationToken.None)); f.Review.PublishAvailable(false); f.Review.PublishAvailable(true);
                    Check(!f.Review.TryPresent(result) && !f.Review.CanArchive, "Queued old result survived activity"); Check(f.CheckRelease().Accepted, "Fresh check failed");
                }
            });
            Test("modern history review and restore keep explicit confirmation separate from release authority", delegate {
                using (var f = new ModernChannelFixture()) using (var checks = f.Coordinator()) using (var review = WindowsReleaseReviewSession.ForModern(checks, f.Channel)) {
                    checks.Start(); checks.SetReviewOpen(true); checks.PublishIdle(true); review.PublishAvailable(true);
                    var metadata = ChannelAwait(() => review.CheckAsync(CancellationToken.None)); Check(metadata.Accepted && review.TryPresent(metadata), "Configured metadata check failed");
                    string current = Path.Combine(f.History, WindowsChannelReplayStore.FileName), backupRoot = Path.Combine(f.Archive.Package.Owned.Destination, "backups"); Directory.CreateDirectory(backupRoot); string backup = Path.Combine(backupRoot, WindowsChannelReplayStore.FileName); File.Copy(current, backup); string hash = WindowsSignerEvidence.Hash(File.ReadAllBytes(current));
                    var result = ChannelAwait(() => review.BackupAsync(backup, CancellationToken.None)); Check(result.Accepted && !review.CanRestoreBackup && review.TryPresent(result) && review.CanRestoreBackup, "Backup bypassed delivery");
                    Throws<SafetyException>(() => review.RestoreBackupAsync(review.BackupId + 1, CancellationToken.None));
                    var restored = ChannelAwait(() => review.RestoreBackupAsync(review.BackupId, CancellationToken.None)); Check(restored.Accepted && review.TryPresent(restored) && !review.CanArchive && !review.CanExtracted && !review.CanRestoreBackup, "Restore retained offer or confirmation");
                    Check(hash == WindowsSignerEvidence.Hash(File.ReadAllBytes(current)) && hash == WindowsSignerEvidence.Hash(File.ReadAllBytes(backup)), "No-op restore rewrote history or backup");
                }
            });
            Test("actual modern release backend and view perform ordered metadata ZIP and honest application refusal", delegate {
                using (var f = new ModernReviewFixture()) using (var backend = WindowsReleaseViewBackend.ForModern(f.Checks, f.Source.Channel, ApplicationInformation.Version, f.Archive, f.Package))
                using (var view = new ReleaseView(backend, new InstallationFolders(f.Source.Archive.Package.Root, f.Source.Archive.Package.Owned.Destination, path => { throw new Exception("Unexpected folder open"); }))) {
                    view.SetAvailable(true); var metadata = ChannelAwait(() => view.Check()); Check(metadata.Accepted && metadata.TryPresent() && !metadata.TryPresent(), "View metadata delivery failed");
                    var zipChoice = view.BeginChoice(WindowsReleaseReviewAction.Archive); var zip = ChannelAwait(() => view.FinishSelection(zipChoice, f.Source.Archive.Path)); Check(zip.Accepted && zip.TryPresent(), "View ZIP delivery failed");
                    var folderChoice = view.BeginChoice(WindowsReleaseReviewAction.Extracted); var folder = ChannelAwait(() => view.FinishSelection(folderChoice, f.Source.Archive.Package.Root));
                    Check(!folder.Accepted && folder.TryPresent() && folder.Message.Contains("Review refused") && f.ArchiveCalls == 2 && f.PackageCalls == 1 && view.CanClose, "View substituted classic approval or lost lifetime"); f.Source.Archive.Released();
                }
            });
        }
        static int ModernReleaseApplicationSample(string folder) {
            byte[] host = File.ReadAllBytes(Path.Combine(folder, "EasyEject.Modern.exe")), managed = File.ReadAllBytes(Path.Combine(folder, "EasyEject.Modern.dll"));
            byte[] backendBytes = File.ReadAllBytes(Path.Combine(folder, "EasyEject.Runtime.dll"));
            Test("modern session composes ordered signed metadata ZIP and actual owned application review without writes", delegate {
                using (var f = new ModernReviewFixture()) {
                    ApplicationPackageFixture(f.Source.Archive.Package, host, managed, backendBytes); f.Source.Archive.Write(); Check(f.CheckRelease().Accepted, "Metadata failed");
                    var before = Directory.GetFiles(f.Source.Archive.Package.Owned.Root, "*", SearchOption.AllDirectories).ToDictionary(path => path, path => WindowsSignerEvidence.Hash(File.ReadAllBytes(path)));
                    Check(f.Zip().Accepted, "ZIP failed"); var result = f.Folder();
                    Check(result.Accepted && result.Message.Contains("application/launch declarations") && result.Message.Contains("approved component catalog") && !result.Message.Contains("all six") && result.Message.Contains("No installation or launch permission") && f.ArchiveCalls == 2 && f.PackageCalls == 1, "Modern result overstated or omitted policy scope");
                    Check(before.Count == Directory.GetFiles(f.Source.Archive.Package.Owned.Root, "*", SearchOption.AllDirectories).Length && before.All(pair => pair.Value == WindowsSignerEvidence.Hash(File.ReadAllBytes(pair.Key))), "File review wrote/extracted content"); f.Source.Archive.Released();
                }
            });
            Test("actual modern backend and presenter accept exact application sample while preserving later acceptance gates", delegate {
                using (var f = new ModernReviewFixture()) {
                    ApplicationPackageFixture(f.Source.Archive.Package, host, managed, backendBytes); f.Source.Archive.Write();
                    using (var backend = WindowsReleaseViewBackend.ForModern(f.Checks, f.Source.Channel, ApplicationInformation.Version, f.Archive, f.Package))
                    using (var view = new ReleaseView(backend, new InstallationFolders(f.Source.Archive.Package.Root, f.Source.Archive.Package.Owned.Destination, path => { throw new Exception("Unexpected navigation"); }))) {
                        view.SetAvailable(true); var check = ChannelAwait(() => view.Check()); Check(check.Accepted && check.TryPresent(), "Metadata failed");
                        var zip = ChannelAwait(() => view.FinishSelection(view.BeginChoice(WindowsReleaseReviewAction.Archive), f.Source.Archive.Path)); Check(zip.Accepted && zip.TryPresent(), "ZIP failed");
                        var result = ChannelAwait(() => view.FinishSelection(view.BeginChoice(WindowsReleaseReviewAction.Extracted), f.Source.Archive.Package.Root));
                        Check(result.Accepted && result.TryPresent() && !result.TryPresent() && result.Description.Contains("approved component catalog") && result.Description.Contains("cannot be compared") && view.CanClose, "Presenter misreported incomplete acceptance or replayed result");
                    }
                    f.Source.Archive.Released();
                }
            });
            Console.WriteLine(passed + " passed, " + failed + " failed"); return failed == 0 ? 0 : 1;
        }
    }
}
