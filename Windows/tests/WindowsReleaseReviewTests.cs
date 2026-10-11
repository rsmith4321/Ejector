using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace EasyEject {
    public static partial class Tests {
        sealed class ReleaseReviewFixture : IDisposable {
            internal readonly UpdateCoordinatorFixture Update = new UpdateCoordinatorFixture();
            internal readonly WindowsUpdateCoordinator Checks; internal readonly WindowsReleaseReviewSession Review;
            internal int Archives, Packages; internal bool IgnoreArchiveCancellation;
            internal Action<CancellationToken> BeforeArchive, BeforePackage;
            internal ReleaseReviewFixture(bool automatic = false) {
                Checks = Update.Create(automatic); Checks.SetReviewOpen(true); Checks.PublishIdle(true);
                Review = new WindowsReleaseReviewSession(Checks, Update.Channel.Channel,
                    (path, cancel) => { Interlocked.Increment(ref Archives); if (BeforeArchive != null) BeforeArchive(cancel); return new WindowsArchiveReview(path, Update.Channel.Channel.Publisher, new WindowsManifestTrust(Update.Channel.Archive.Package.CertificateTrust), IgnoreArchiveCancellation ? CancellationToken.None : cancel); },
                    (path, cancel) => { Interlocked.Increment(ref Packages); if (BeforePackage != null) BeforePackage(cancel); return new WindowsPackageReview(path, Update.Channel.Channel.Publisher, Update.Channel.Archive.Package.ExecutableTrust, new WindowsManifestTrust(Update.Channel.Archive.Package.CertificateTrust), cancel); });
                Review.PublishAvailable(true);
            }
            internal WindowsReleaseReviewResult CheckRelease() { var result = ChannelAwait(() => Review.CheckAsync(CancellationToken.None)); Check(Review.TryPresent(result), "Review result was not presented"); return result; }
            internal WindowsReleaseReviewResult Zip(string path = null) { var result = ChannelAwait(() => Review.ArchiveAsync(path ?? Update.Channel.Archive.Path, CancellationToken.None)); Check(Review.TryPresent(result), "Archive result was not presented"); return result; }
            internal WindowsReleaseReviewResult Folder(string path = null) { var result = ChannelAwait(() => Review.ExtractedAsync(path ?? Update.Channel.Archive.Package.Root, CancellationToken.None)); Check(Review.TryPresent(result), "Folder result was not presented"); return result; }
            public void Dispose() { Review.Dispose(); Checks.Dispose(); Update.Dispose(); }
        }
        static void WindowsReleaseReviewTests() {
            Test("Manual release review unconfigured channel creates no request file verifier or folder", () => {
                using (var c = new WindowsUpdateCoordinator(false, null, () => DateTime.UtcNow, TimeSpan.FromDays(1), true))
                using (var r = new WindowsReleaseReviewSession(c, null, (p, t) => { throw new Exception("Unrequested archive"); }, (p, t) => { throw new Exception("Unrequested package"); })) {
                    r.PublishAvailable(true); c.PublishIdle(true); Check(!r.Configured && !r.CanCheck && !r.CanArchive && !r.CanExtracted, "Unavailable channel exposed actions");
                    Throws<SafetyException>(() => r.CheckAsync(CancellationToken.None)); Throws<SafetyException>(() => r.ArchiveAsync(@"C:\unrequested.zip", CancellationToken.None)); Check(!r.Busy, "Rejected action consumed a slot");
                }
            });
            Test("Opening exact manual review cancels old automatic scope and suppresses timer attempts", () => {
                using (var f = new UpdateCoordinatorFixture()) using (var c = f.Create()) {
                    var pending = new TaskCompletionSource<WindowsChannelOffer>(); f.Operation = _ => pending.Task; c.Start(); c.PublishIdle(true); var first = c.PulseAsync();
                    c.SetReviewOpen(true); Check(!f.Scope.Allowed && c.Checking, "Review revived or released the executing automatic request"); pending.SetResult(f.Channel.Offer()); var stopped = ChannelAwait(() => first);
                    Check(stopped.Outcome == WindowsUpdateCheckOutcome.Cancelled && !c.TryPresent(stopped), "Opening review published an old automatic result");
                    f.Operation = null; f.Channel.Now = f.Channel.Now.AddDays(2); Check(UpdatePulse(c) == null && f.Calls == 1, "Timer ran inside manual review");
                    var manual = UpdateManual(c); Check(manual.Outcome == WindowsUpdateCheckOutcome.Accepted, "Manual review could not check while automatic mode was suspended");
                    c.SetReviewOpen(false); Check(!c.TryPresent(manual), "Closed review retained result authority"); f.Channel.Now = f.Channel.Now.AddDays(2); Check(UpdatePulse(c).Outcome == WindowsUpdateCheckOutcome.Accepted && f.Calls == 3, "Closing review did not restore normal cadence");
                }
            });
            Test("Manual review enables ZIP then extracted selection only after one-use foreground acceptance", () => {
                using (var f = new ReleaseReviewFixture()) {
                    Check(f.Review.CanCheck && !f.Review.CanArchive && !f.Review.CanExtracted && f.Update.Calls == 0, "Construction starts work or grants file authority");
                    Throws<SafetyException>(() => f.Review.ArchiveAsync(f.Update.Channel.Archive.Path, CancellationToken.None));
                    var checkedRelease = ChannelAwait(() => f.Review.CheckAsync(CancellationToken.None)); Check(checkedRelease.Accepted && !f.Review.CanArchive, "Worker granted action before foreground delivery");
                    Check(f.Review.TryPresent(checkedRelease) && !f.Review.TryPresent(checkedRelease) && f.Review.CanArchive && !f.Review.CanExtracted, "Result replay or wrong admission");
                    var zip = ChannelAwait(() => f.Review.ArchiveAsync(f.Update.Channel.Archive.Path, CancellationToken.None)); Check(zip.Accepted && !f.Review.CanExtracted, "ZIP worker granted extracted action early");
                    Check(f.Review.TryPresent(zip) && f.Review.CanExtracted && f.Folder().Accepted && f.Archives == 2 && f.Packages == 1, "Extracted review did not reopen ZIP or verify package");
                }
            });
            Test("Manual selected-file review preserves every ZIP package and settings byte without extraction execution or publication", () => {
                using (var f = new ReleaseReviewFixture()) {
                    var files = Directory.GetFiles(f.Update.Channel.Archive.Package.Files.Root, "*", SearchOption.AllDirectories).ToDictionary(p => p, p => WindowsSignerEvidence.Hash(File.ReadAllBytes(p)));
                    Check(f.CheckRelease().Accepted && f.Zip().Accepted && f.Folder().Accepted, "Read-only review failed");
                    var after = Directory.GetFiles(f.Update.Channel.Archive.Package.Files.Root, "*", SearchOption.AllDirectories); Check(after.Length == files.Count && after.All(p => files.ContainsKey(p) && files[p] == WindowsSignerEvidence.Hash(File.ReadAllBytes(p))), "Review wrote extracted files, receipts or private work");
                }
            });
            Test("Manual review comparisons expose developer same newer older status without installed-file proof", () => {
                using (var f = new WindowsChannelFixture()) {
                    var offer = f.Offer(); string dev = WindowsReleaseReviewSession.Describe(offer, ApplicationInformation.Version);
                    Check(dev.Contains("cannot be compared") && dev.Contains(offer.Release.ArchiveHash) && dev.Contains(offer.Release.ArchiveUrl.AbsoluteUri) && dev.Contains("publisher attestation") && dev.Contains("expires UTC"), "Review hid exact release identity or asserted current development build");
                    Check(WindowsReleaseReviewSession.Describe(offer, "0.2.0.0").Contains("does not verify the installed files") && WindowsReleaseReviewSession.Describe(offer, "0.0.0.1").Contains("newer stable") && WindowsReleaseReviewSession.Describe(offer, "1.0.0.0").Contains("older than"), "Numeric comparison grants executable trust");
                }
            });
            Test("Manual review refused new check clears prior selected ZIP and offer before retry", () => {
                using (var f = new ReleaseReviewFixture()) {
                    Check(f.CheckRelease().Accepted && f.Zip().Accepted && f.Review.CanExtracted, "Setup failed"); f.Update.Operation = _ => Task.FromException<WindowsChannelOffer>(new SafetyException("fixture check failed"));
                    var failed = f.CheckRelease(); Check(!failed.Accepted && failed.Message.Contains("fixture check failed") && !f.Review.CanArchive && !f.Review.CanExtracted && f.Review.CanCheck, "Failed refresh preserved stale file permission or forced retry");
                    f.Update.Operation = null; Check(f.CheckRelease().Accepted && f.Review.CanArchive && !f.Review.CanExtracted, "Explicit retry failed or restored old ZIP");
                }
            });
            Test("Manual review archive failure clears earlier selected ZIP and does not reach extracted verifier", () => {
                using (var f = new ReleaseReviewFixture()) {
                    Check(f.CheckRelease().Accepted && f.Zip().Accepted, "Setup failed"); string bad = Path.Combine(f.Update.Channel.Archive.Package.Files.Destination, "wrong.zip"); File.WriteAllText(bad, "keep invalid ZIP");
                    var failed = f.Zip(bad); Check(!failed.Accepted && !f.Review.CanExtracted && f.Packages == 0 && File.ReadAllText(bad) == "keep invalid ZIP", "Invalid ZIP granted extracted action or was changed");
                    Check(f.Zip().Accepted && f.Review.CanExtracted, "Explicit original ZIP retry failed");
                }
            });
            Test("Manual extracted review refuses changed ZIP before opening package and retains changed input", () => {
                using (var f = new ReleaseReviewFixture()) {
                    Check(f.CheckRelease().Accepted && f.Zip().Accepted, "Setup failed"); byte[] saved = File.ReadAllBytes(f.Update.Channel.Archive.Path); File.WriteAllText(f.Update.Channel.Archive.Path, "changed archive retain");
                    var failed = f.Folder(); Check(!failed.Accepted && f.Packages == 0 && File.ReadAllText(f.Update.Channel.Archive.Path) == "changed archive retain", "Changed ZIP reached extracted verification or was reverted");
                    File.WriteAllBytes(f.Update.Channel.Archive.Path, saved); Check(f.Folder().Accepted, "Manual retry after restoring exact ZIP failed");
                }
            });
            Test("Manual extracted review refuses changed extra aliased and unsigned package while retaining originals", () => {
                using (var f = new ReleaseReviewFixture()) {
                    Check(f.CheckRelease().Accepted && f.Zip().Accepted, "Setup failed"); var p = f.Update.Channel.Archive.Package; string readme = Path.Combine(p.Root, "README.txt"); byte[] saved = File.ReadAllBytes(readme);
                    File.WriteAllText(readme, "changed selected folder keep"); Check(!f.Folder().Accepted && File.ReadAllText(readme) == "changed selected folder keep", "Changed payload passed or was removed"); File.WriteAllBytes(readme, saved);
                    string extra = Path.Combine(p.Root, "foreign.keep"); File.WriteAllText(extra, "keep extra"); Check(!f.Folder().Accepted && File.ReadAllText(extra) == "keep extra", "Extra contents passed or were cleaned"); File.Delete(extra);
                    string alias = Path.Combine(p.Files.Destination, "package-link"); CreateJunction(p.Files, alias, p.Root); Check(!f.Folder(alias).Accepted, "Aliased folder passed");
                    f.BeforePackage = _ => { throw new SafetyException("fixture unsigned executable refusal"); }; Check(!f.Folder().Accepted, "Untrusted executable passed");
                }
            });
            Test("Manual review expired offer is refused before selected-file access", () => {
                using (var f = new ReleaseReviewFixture()) { Check(f.CheckRelease().Accepted, "Setup failed"); f.Update.Channel.Now = f.Update.Channel.Now.AddHours(2); var failed = f.Zip(); Check(!failed.Accepted && f.Archives == 0 && failed.Message.Contains("expired"), "Expired metadata reached local-file review"); }
            });
            Test("Manual review fresh native publisher trust refusal prevents selected-file access", () => {
                using (var f = new ReleaseReviewFixture()) { Check(f.CheckRelease().Accepted, "Setup failed"); f.Update.Channel.Archive.Package.CertificateTrust.Throw = true; var failed = f.Zip(); Check(!failed.Accepted && f.Archives == 0, "Old metadata trust approved selected ZIP"); }
            });
            Test("Manual review permanently revokes busy then idle scope until ignored-token checker actually terminates", () => {
                using (var f = new ReleaseReviewFixture()) {
                    var pending = new TaskCompletionSource<WindowsChannelOffer>(); f.Update.Operation = _ => pending.Task; var work = f.Review.CheckAsync(CancellationToken.None);
                    f.Review.PublishAvailable(false); f.Checks.PublishIdle(false); f.Review.PublishAvailable(true); f.Checks.PublishIdle(true);
                    Check(f.Review.Busy && f.Checks.Checking && !f.Review.CanCheck, "Ignored request slot released early"); Throws<SafetyException>(() => f.Review.CheckAsync(CancellationToken.None)); pending.SetResult(f.Update.Channel.Offer());
                    var cancelled = ChannelAwait(() => work); Check(cancelled.Cancelled && !f.Review.TryPresent(cancelled) && !f.Review.CanArchive && !f.Review.Busy && !f.Checks.Checking, "Busy/idle revived old result");
                    f.Update.Operation = null; Check(f.CheckRelease().Accepted, "Manual retry after terminal refused");
                }
            });
            Test("Manual review close cancellation owns ignored-token request and observes late exception without presentation", () => {
                using (var f = new ReleaseReviewFixture()) {
                    var pending = new TaskCompletionSource<WindowsChannelOffer>(); f.Update.Operation = _ => pending.Task; var work = f.Review.CheckAsync(CancellationToken.None); f.Review.Dispose();
                    Check(f.Review.Busy && f.Checks.Checking && !f.Review.CanCheck, "Closed request declared finished before actual task"); pending.SetException(new InvalidOperationException("late owned failure"));
                    var result = ChannelAwait(() => work); Check(result.Cancelled && !f.Review.TryPresent(result) && !f.Checks.Checking, "Closed view received late result or retained slot"); Throws<ObjectDisposedException>(() => f.Review.CheckAsync(CancellationToken.None));
                }
            });
            Test("Manual selected ZIP cancellation disposes late held files and requires explicit retry", () => {
                using (var f = new ReleaseReviewFixture()) using (var entered = new ManualResetEventSlim()) using (var release = new ManualResetEventSlim()) {
                    Check(f.CheckRelease().Accepted, "Setup failed"); f.IgnoreArchiveCancellation = true; f.BeforeArchive = _ => { entered.Set(); Check(release.Wait(3000), "Owned fixture wait expired"); };
                    var work = f.Review.ArchiveAsync(f.Update.Channel.Archive.Path, CancellationToken.None); Check(entered.Wait(2000), "Archive worker did not start"); f.Review.Cancel(); Check(f.Review.Busy && !f.Review.CanArchive, "Cancellation released executing verifier"); release.Set();
                    var result = ChannelAwait(() => work); Check(result.Cancelled && f.Review.TryPresent(result) && !f.Review.CanExtracted && f.Review.CanArchive, "Cancelled selected ZIP retained accepted status or blocked manual retry");
                    using (var write = new FileStream(f.Update.Channel.Archive.Path, FileMode.Open, FileAccess.ReadWrite, FileShare.None)) Check(write.Length > 0, "Late verifier kept its held ZIP handle");
                    f.BeforeArchive = null; f.IgnoreArchiveCancellation = false; Check(f.Zip().Accepted, "Explicit ZIP retry failed");
                }
            });
            Test("Manual selected folder lifecycle revoked inside native verification cannot publish success", () => {
                using (var f = new ReleaseReviewFixture()) {
                    Check(f.CheckRelease().Accepted && f.Zip().Accepted, "Setup failed"); f.Update.Channel.Archive.Package.ExecutableTrust.During = (p, stream) => { f.Review.PublishAvailable(false); f.Review.PublishAvailable(true); };
                    var result = ChannelAwait(() => f.Review.ExtractedAsync(f.Update.Channel.Archive.Package.Root, CancellationToken.None)); Check(result.Cancelled && !f.Review.TryPresent(result), "Native verification survived intervening dialog/activity");
                }
            });
            Test("Manual queued release results refuse other owner new request activity and duplicate publication", () => {
                using (var f = new ReleaseReviewFixture()) using (var other = new ReleaseReviewFixture()) {
                    var old = ChannelAwait(() => f.Review.CheckAsync(CancellationToken.None)); Check(!other.Review.TryPresent(old), "Wrong review owner accepted result");
                    Check(f.CheckRelease().Accepted && !f.Review.TryPresent(old), "Old queued request survived newer check");
                    var next = ChannelAwait(() => f.Review.CheckAsync(CancellationToken.None)); f.Review.PublishAvailable(false); f.Review.PublishAvailable(true); Check(!f.Review.TryPresent(next) && !f.Review.CanArchive, "Queued result crossed activity epoch");
                }
            });
            Test("Manual review rejects mismatched approved policy before admitting file review", () => {
                using (var f = new ReleaseReviewFixture()) {
                    var p = f.Update.Channel.Channel; var other = new ApprovedWindowsChannel("https://other.invalid/channel.json", "https://other.invalid/channel.json.p7s", p.Archives.AbsoluteUri, p.Publisher);
                    f.Update.Operation = _ => Task.FromResult(f.Update.Channel.Offer(channel: other)); var result = f.CheckRelease(); Check(!result.Accepted && !f.Review.CanArchive && f.Archives == 0, "Other approved policy offered file permission");
                }
            });
            Test("Manual hidden review keeps Close default read-only instructions bounded result and honest configured guide", () => {
                using (var f = new ReleaseReviewFixture()) using (var folders = new InstallationFolders(f.Update.Channel.Archive.Package.Root, f.Update.Channel.Archive.Package.Files.Destination, _ => { throw new Exception("Unrequested folder open"); }))
                using (var dialog = new WindowsReleaseReviewDialog(ApplicationInformation.Version, f.Review, folders, () => {})) {
                    dialog.Size = dialog.MinimumSize; dialog.CreateControl(); dialog.PerformLayout(); dialog.PublishAvailable(true);
                    Check(dialog.AcceptButton == dialog.CloseReview && dialog.CancelButton == dialog.CloseReview && dialog.CheckRelease.Enabled && !dialog.VerifyArchive.Enabled && !dialog.VerifyExtracted.Enabled && dialog.ReleaseContent.ReadOnly && dialog.GuideContent.ReadOnly && dialog.ReviewStatus.Height == 94 && dialog.ReviewStatus.ScrollBars == ScrollBars.Vertical && dialog.CloseReview.Bottom <= dialog.CloseReview.Parent.ClientSize.Height, "Unsafe default or unbounded/clipped result footer");
                    Check(!dialog.GuideContent.Text.Contains(WindowsUpdateSafety.ChannelStatus) && dialog.GuideContent.Text.Contains("manual check") && dialog.ReleaseContent.Text.Contains("manual"), "Configured review guide says no channel or automatically downloads");
                }
            });
            Test("Manual hidden review close prevents pending UI writes and releases selected folder leases", () => {
                using (var f = new ReleaseReviewFixture()) {
                    var pending = new TaskCompletionSource<WindowsChannelOffer>(); f.Update.Operation = _ => pending.Task; var folders = new InstallationFolders(f.Update.Channel.Archive.Package.Root, f.Update.Channel.Archive.Package.Files.Destination, _ => {});
                    var dialog = new WindowsReleaseReviewDialog(ApplicationInformation.Version, f.Review, folders, () => {}); var operation = dialog.RunAction(() => f.Review.CheckAsync(CancellationToken.None)); dialog.Dispose(); pending.SetResult(f.Update.Channel.Offer());
                    PumpShareSetup(operation); Check(dialog.IsDisposed && !f.Review.Busy && !f.Review.CanArchive, "Closed view wrote controls or retained review slot");
                    Throws<ObjectDisposedException>(() => folders.Open(false));
                }
            });
            Test("Manual release review unavailable or precancelled admission invokes no check or file factory", () => {
                using (var f = new ReleaseReviewFixture()) using (var cancel = new CancellationTokenSource()) {
                    f.Review.PublishAvailable(false); Throws<SafetyException>(() => f.Review.CheckAsync(CancellationToken.None)); f.Review.PublishAvailable(true); cancel.Cancel();
                    Throws<OperationCanceledException>(() => f.Review.CheckAsync(cancel.Token)); Check(f.Update.Calls == 0 && !f.Review.Busy && f.Archives == 0 && f.Packages == 0, "Rejected manual request reached worker");
                    Check(f.CheckRelease().Accepted, "Setup failed"); Throws<OperationCanceledException>(() => f.Review.ArchiveAsync(f.Update.Channel.Archive.Path, cancel.Token)); Check(f.Archives == 0 && f.Review.CanArchive, "Precancellation accessed files or cleared accepted metadata");
                }
            });
            Test("Manual release review missing selected ZIP never creates or extracts its location", () => {
                using (var f = new ReleaseReviewFixture()) { Check(f.CheckRelease().Accepted, "Setup failed"); string missing = Path.Combine(f.Update.Channel.Archive.Package.Files.Destination, "missing", "release.zip");
                    Check(!f.Zip(missing).Accepted && !Directory.Exists(Path.GetDirectoryName(missing)) && !f.Review.CanExtracted && f.Packages == 0, "Missing ZIP created a download/extraction folder");
                }
            });
            Test("Manual hidden review async buttons and bounded result follow accepted then refused current verification", () => {
                using (var f = new ReleaseReviewFixture()) using (var folders = new InstallationFolders(f.Update.Channel.Archive.Package.Root, f.Update.Channel.Archive.Package.Files.Destination, _ => {}))
                using (var dialog = new WindowsReleaseReviewDialog(ApplicationInformation.Version, f.Review, folders, () => {})) {
                    var pending = new TaskCompletionSource<WindowsChannelOffer>(); f.Update.Operation = _ => pending.Task; var check = dialog.RunAction(() => f.Review.CheckAsync(CancellationToken.None));
                    Check(dialog.StopReview.Enabled && !dialog.CheckRelease.Enabled && !dialog.VerifyArchive.Enabled && dialog.ReleaseContent.Text.Contains("in progress"), "Async check leaves stale result/actions live"); pending.SetResult(f.Update.Channel.Offer()); PumpShareSetup(check);
                    Check(!dialog.StopReview.Enabled && dialog.VerifyArchive.Enabled && !dialog.VerifyExtracted.Enabled && dialog.ReleaseContent.Text.Contains("SHA-256:"), "Authenticated foreground result lacks exact review/actions");
                    PumpShareSetup(dialog.RunAction(() => f.Review.ArchiveAsync(f.Update.Channel.Archive.Path, CancellationToken.None))); Check(dialog.VerifyExtracted.Enabled, "Verified ZIP did not enable explicit folder review");
                    PumpShareSetup(dialog.RunAction(() => f.Review.ExtractedAsync(f.Update.Channel.Archive.Package.Root, CancellationToken.None))); Check(dialog.ReleaseContent.Text.Contains("read-only result") && dialog.ReviewStatus.Height == 94, "Folder result overgrew bounded viewport or claims install authority");
                    File.WriteAllText(f.Update.Channel.Archive.Path, "changed ZIP keep"); PumpShareSetup(dialog.RunAction(() => f.Review.ArchiveAsync(f.Update.Channel.Archive.Path, CancellationToken.None)));
                    Check(!dialog.VerifyExtracted.Enabled && dialog.ReleaseContent.Text.Contains("Review refused") && !dialog.ReleaseContent.Text.Contains("matches the freshly verified ZIP"), "Refused current ZIP retained previous package success");
                }
            });
            Test("Manual hidden review activity loss reports actual terminal cancellation without reviving result or immediate retry", () => {
                using (var f = new ReleaseReviewFixture()) using (var folders = new InstallationFolders(f.Update.Channel.Archive.Package.Root, f.Update.Channel.Archive.Package.Files.Destination, _ => {}))
                using (var dialog = new WindowsReleaseReviewDialog(ApplicationInformation.Version, f.Review, folders, () => {})) {
                    var pending = new TaskCompletionSource<WindowsChannelOffer>(); f.Update.Operation = _ => pending.Task; var operation = dialog.RunAction(() => f.Review.CheckAsync(CancellationToken.None));
                    var duplicate = dialog.RunAction(() => f.Review.CheckAsync(CancellationToken.None)); Check(duplicate.IsCompleted && f.Update.Calls == 1, "Duplicate UI action disturbed active request");
                    dialog.PublishAvailable(false); dialog.PublishAvailable(true); pending.SetResult(f.Update.Channel.Offer()); PumpShareSetup(operation);
                    Check(dialog.ReviewStatus.Text.Contains("Review ended") && !dialog.VerifyArchive.Enabled && dialog.CheckRelease.Enabled && f.Update.Calls == 1, "Activity loss leaves permanent checking status, revives result or retries");
                }
            });
        }
    }
}
