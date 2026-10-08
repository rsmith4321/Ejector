using System;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace EasyEject {
    public static partial class Tests {
        // Explicit local sample only: no extraction, application loading or real endpoint.
        // Complete payloads outgrow the tiny-fixture eight-second observation budget.
        // Always join the exact worker before assertions/disposal; observation is not termination.
        static T PinnedReleaseAwait<T>(Func<Task<T>> operation) { return Task.Run(operation).GetAwaiter().GetResult(); }
        static WindowsReleaseReviewResult PinnedReleaseDeliver(ModernReviewFixture fixture, Func<Task<WindowsReleaseReviewResult>> operation) {
            var result = PinnedReleaseAwait(operation); Check(fixture.Review.TryPresent(result), "Current pinned release status delivery failed"); return result;
        }
        static void PinnedReleaseZip(ModernArchiveFixture archive) {
            using (var output = new FileStream(archive.Path, FileMode.Create, FileAccess.Write, FileShare.None)) {
                using (var zip = new ZipArchive(output, ZipArchiveMode.Create, true)) {
                    foreach (string name in archive.Package.Metadata.Files.Select(file => file.Name).Concat(new[] { ModernWindowsReleaseManifest.ManifestName, ModernWindowsReleaseManifest.SignatureName, ModernWindowsReleaseManifest.IndexName })) {
                        using (var input = SafePaths.ReadFile(archive.Package.PathFor(name)))
                        using (var entry = zip.CreateEntry(name, CompressionLevel.Optimal).Open()) input.CopyTo(entry);
                    }
                }
                output.Flush(true);
            }
        }
        static ModernReviewFixture PinnedReleaseFixture(string folder) {
            var archive = new ModernArchiveFixture(PinnedVendorFixture(folder));
            try {
                PinnedReleaseZip(archive);
                return new ModernReviewFixture(new ModernChannelFixture(archive), ReviewedMicrosoftVendorLayout.CreateLocalReviewApproval());
            } catch { archive.Dispose(); throw; }
        }
        static int ModernPinnedReleaseSample(string folder) {
            Test("genuine pinned release session binds signed metadata ZIP and complete application with one-use delivery and held ownership", delegate {
                using (var f = PinnedReleaseFixture(folder)) {
                    var package = f.Source.Archive.Package;
                    var before = Directory.GetFiles(package.Owned.Root, "*", SearchOption.AllDirectories).ToDictionary(path => path, path => WindowsSignerEvidence.Hash(File.ReadAllBytes(path)));
                    var metadata = PinnedReleaseAwait(() => f.Review.CheckAsync(CancellationToken.None));
                    Check(metadata.Accepted && !f.Review.CanArchive, "Worker bypassed metadata delivery");
                    Check(f.Review.TryPresent(metadata) && !f.Review.TryPresent(metadata), "Metadata result replayed");
                    var zip = PinnedReleaseAwait(() => f.Review.ArchiveAsync(f.Source.Archive.Path, CancellationToken.None));
                    Check(zip.Accepted && !f.Review.CanExtracted && f.Review.TryPresent(zip) && !f.Review.TryPresent(zip), "ZIP granted early or replayed folder authority");
                    int held = 0;
                    f.BeforePackage = cancel => package.Certificate.During = () => {
                        held++;
                        Throws<IOException>(() => File.WriteAllText(f.Source.Archive.Path, "changed ZIP"));
                        Throws<IOException>(() => File.WriteAllText(package.PathFor("coreclr.dll"), "changed vendor"));
                    };
                    var result = PinnedReleaseDeliver(f, () => f.Review.ExtractedAsync(f.Source.Archive.Package.Root, CancellationToken.None));
                    Check(result.Accepted && !f.Review.TryPresent(result) && held > 0 && f.ArchiveCalls == 2 && f.PackageCalls == 1, "Full genuine composition or one-use ownership failed");
                    Check(result.Message.Contains("independently approved component catalog") && result.Message.Contains("No installation or launch permission"), "Result overstated distribution authority");
                    f.Source.Archive.Released();
                    Check(before.Count == Directory.GetFiles(package.Owned.Root, "*", SearchOption.AllDirectories).Length && before.All(pair => pair.Value == WindowsSignerEvidence.Hash(File.ReadAllBytes(pair.Key))), "Worker extracted or changed fixture content");
                }
            });
            Test("genuine pinned release backend presenter discards an activity-revoked folder result and permits a fresh manual review", delegate {
                using (var f = PinnedReleaseFixture(folder))
                using (var backend = WindowsReleaseViewBackend.ForModern(f.Checks, f.Source.Channel, ApplicationInformation.Version, f.Archive, f.Package))
                using (var view = new ReleaseView(backend, new InstallationFolders(f.Source.Archive.Package.Root, f.Source.Archive.Package.Owned.Destination, path => { throw new Exception("Unexpected folder navigation"); }))) {
                    view.SetAvailable(true);
                    var metadata = PinnedReleaseAwait(() => view.Check()); Check(metadata.Accepted && metadata.TryPresent() && !metadata.TryPresent(), "Presenter metadata failed");
                    var zip = PinnedReleaseAwait(() => view.FinishSelection(view.BeginChoice(WindowsReleaseReviewAction.Archive), f.Source.Archive.Path)); Check(zip.Accepted && zip.TryPresent(), "Presenter ZIP failed");
                    var result = PinnedReleaseAwait(() => view.FinishSelection(view.BeginChoice(WindowsReleaseReviewAction.Extracted), f.Source.Archive.Package.Root));
                    Check(result.Accepted && view.CanClose, "Full presenter worker failed");
                    view.SetAvailable(false); view.SetAvailable(true); Check(!result.TryPresent(), "Activity-revoked result was restored");
                    var retry = PinnedReleaseAwait(() => view.FinishSelection(view.BeginChoice(WindowsReleaseReviewAction.Extracted), f.Source.Archive.Package.Root));
                    Check(retry.Accepted && retry.TryPresent() && !retry.TryPresent() && retry.Description.Contains("cannot be compared") && retry.Description.Contains("approved component catalog") && view.CanClose, "Fresh manual presenter retry lost scope or replayed");
                    f.Source.Archive.Released();
                }
            });
            Test("genuine pinned release refuses changed ZIP and newly authenticated altered vendor folder while retaining bytes for restored retry", delegate {
                using (var f = PinnedReleaseFixture(folder)) {
                    Check(PinnedReleaseDeliver(f, () => f.Review.CheckAsync(CancellationToken.None)).Accepted && PinnedReleaseDeliver(f, () => f.Review.ArchiveAsync(f.Source.Archive.Path, CancellationToken.None)).Accepted, "Initial metadata/ZIP failed");
                    byte[] zip = File.ReadAllBytes(f.Source.Archive.Path); File.WriteAllText(f.Source.Archive.Path, "retained changed ZIP");
                    Check(!PinnedReleaseDeliver(f, () => f.Review.ExtractedAsync(f.Source.Archive.Package.Root, CancellationToken.None)).Accepted && f.PackageCalls == 0 && File.ReadAllText(f.Source.Archive.Path) == "retained changed ZIP", "Changed ZIP reached application or was cleaned");
                    File.WriteAllBytes(f.Source.Archive.Path, zip); Check(PinnedReleaseDeliver(f, () => f.Review.ArchiveAsync(f.Source.Archive.Path, CancellationToken.None)).Accepted, "Restored ZIP retry failed");
                    var package = f.Source.Archive.Package;
                    string name = "ThirdPartyNotices/Microsoft.NETCore.App.Runtime.win-x64-10.0.12/LICENSE.TXT";
                    byte[] original = File.ReadAllBytes(package.PathFor(name)), changed = (byte[])original.Clone(); changed[0] ^= 1;
                    ComponentPublish(package, name, changed);
                    using (var contents = package.Review()) contents.Revalidate(CancellationToken.None);
                    Check(!PinnedReleaseDeliver(f, () => f.Review.ExtractedAsync(f.Source.Archive.Package.Root, CancellationToken.None)).Accepted && f.PackageCalls == 1 && File.ReadAllBytes(package.PathFor(name)).SequenceEqual(changed), "Fresh authenticated changed vendor bypassed catalog or was cleaned");
                    f.Source.Archive.Released(); ComponentPublish(package, name, original); PinnedReleaseZip(f.Source.Archive);
                    Check(PinnedReleaseDeliver(f, () => f.Review.CheckAsync(CancellationToken.None)).Accepted && !f.Review.CanExtracted && PinnedReleaseDeliver(f, () => f.Review.ArchiveAsync(f.Source.Archive.Path, CancellationToken.None)).Accepted && PinnedReleaseDeliver(f, () => f.Review.ExtractedAsync(f.Source.Archive.Package.Root, CancellationToken.None)).Accepted, "Fresh restored metadata/ZIP/folder retry failed");
                    f.Source.Archive.Released();
                }
            });
            Test("genuine pinned release cancels between held ZIP and application review then releases files for manual retry", delegate {
                using (var f = PinnedReleaseFixture(folder)) {
                    Check(PinnedReleaseDeliver(f, () => f.Review.CheckAsync(CancellationToken.None)).Accepted && PinnedReleaseDeliver(f, () => f.Review.ArchiveAsync(f.Source.Archive.Path, CancellationToken.None)).Accepted, "Initial metadata/ZIP failed");
                    int called = 0; f.BeforePackage = cancel => { called++; Throws<IOException>(() => File.WriteAllText(f.Source.Archive.Path, "changed")); f.Review.Cancel(); };
                    var result = PinnedReleaseAwait(() => f.Review.ExtractedAsync(f.Source.Archive.Package.Root, CancellationToken.None));
                    Check(called == 1 && result.Cancelled && !result.Accepted && !f.Review.Busy && result.Offer == null && result.ArchivePath == null, "Cancelled worker retained approval or occupancy");
                    Check(f.Review.TryPresent(result) && !f.Review.TryPresent(result), "Cancellation status replayed");
                    f.Source.Archive.Released(); f.BeforePackage = null;
                    Check(PinnedReleaseDeliver(f, () => f.Review.ExtractedAsync(f.Source.Archive.Package.Root, CancellationToken.None)).Accepted, "Fresh manual retry after cancellation failed"); f.Source.Archive.Released();
                    Check(!AppDomain.CurrentDomain.GetAssemblies().Any(assembly => !String.IsNullOrEmpty(assembly.Location) && SafePaths.Within(assembly.Location, f.Source.Archive.Package.Root)), "Reviewed sample application loaded");
                }
            });
            Console.WriteLine(passed + " passed, " + failed + " failed. Real independently pinned vendor bytes through manual signed metadata/ZIP/folder workers; patched owned application, placeholder docs and fake publisher/chain trust. No extraction, application loading, installation, endpoint or device action.");
            return failed == 0 ? 0 : 1;
        }
    }
}
