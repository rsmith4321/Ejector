using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;

namespace EasyEject {
    public static partial class Tests {
        // Explicit sample mode only. Never load or execute the supplied application.
        // Publisher/chain approval is fake; CMS cryptography and vendor pins are real.
        static void PinnedVendorCopy(ModernPackageFixture fixture, string folder, string name, WindowsReleaseFile pin = null) {
            string source = Path.Combine(folder, name.Replace('/', '\\')), target = fixture.PathFor(name);
            using (var parents = SafePaths.HoldDirectories(Path.GetDirectoryName(source)))
            using (var input = SafePaths.ReadFile(source)) {
                var stamp = Native.Info(input.SafeFileHandle);
                Check(input.Length > 0 && input.Length <= 1073741824, "Excessive/empty sample file");
                string hash = VerifiedImporter.Hash(input, CancellationToken.None);
                if (pin != null) Check(input.Length == pin.Length && hash == pin.Hash, "Sample differs from independent vendor pin: " + name);
                Directory.CreateDirectory(Path.GetDirectoryName(target)); input.Position = 0;
                using (var output = new FileStream(target, FileMode.CreateNew, FileAccess.Write, FileShare.None)) { input.CopyTo(output); output.Flush(true); }
                input.Position = 0; parents.Validate();
                Check(VerifiedImporter.SameStamp(stamp, Native.Info(input.SafeFileHandle)) && VerifiedImporter.Hash(input, CancellationToken.None) == hash, "Sample changed during read-only copy");
                Check(WindowsSignerEvidence.Hash(File.ReadAllBytes(target)) == hash, "Owned copy differs from source");
                fixture.Metadata.Files.Add(new WindowsReleaseFile(name, input.Length, hash));
            }
        }
        static ModernPackageFixture PinnedVendorFixture(string folder) {
            var fixture = new ModernPackageFixture(false);
            try {
                folder = SafePaths.Normalize(folder);
                foreach (var pin in ReviewedMicrosoftVendorLayout.Files) PinnedVendorCopy(fixture, folder, pin.Name, pin);
                foreach (string name in new[] { "EasyEject.Modern.exe", "EasyEject.Modern.dll", "EasyEject.Runtime.dll", "EasyEject.Modern.pri", "EasyEject.Modern.pdb", "EasyEject.Runtime.pdb", "EasyEject.Modern.deps.json", "EasyEject.Modern.runtimeconfig.json", "Assets/EasyEject.png" })
                    PinnedVendorCopy(fixture, folder, name);
                // Change only disposable application-owned copies. These are not a signed release.
                NativeHostPublish(fixture, NativeHostStable(File.ReadAllBytes(fixture.PathFor("EasyEject.Modern.exe"))));
                ModernIdentityPublish(fixture, ModernIdentityStable(File.ReadAllBytes(fixture.PathFor("EasyEject.Modern.dll"))));
                BackendIdentityPublish(fixture, ModernIdentityStable(File.ReadAllBytes(fixture.PathFor("EasyEject.Runtime.dll"))));
                string deps = File.ReadAllText(fixture.PathFor(ModernWindowsDependencyConfiguration.Name));
                Check(deps.Contains("0.2.0-dev"), "Expected explicit development sample");
                DependencyPublish(fixture, deps.Replace("0.2.0-dev", "0.2.0"));
                foreach (string name in new[] { "README.txt", "LICENSE.txt" }) ComponentPublish(fixture, name, Encoding.UTF8.GetBytes("Owned test placeholder; no distribution approval: " + name));
                Check(fixture.Metadata.Files.Count == ReviewedMicrosoftVendorLayout.Files.Count + 11, "Unexpected composed fixture membership");
                fixture.Publish(); return fixture;
            } catch { fixture.Dispose(); throw; }
        }
        static int ModernPinnedVendorSample(string folder) {
            Test("genuine pinned vendor composition retains complete held ownership without application loading", delegate {
                using (var fixture = PinnedVendorFixture(folder)) {
                    var review = ApplicationPackage(fixture, CancellationToken.None, ReviewedMicrosoftVendorLayout.CreateLocalReviewApproval());
                    try {
                        Check(review.Release.Files.Count == 650 && review.Release.Version == "0.2.0.0", "Full composed identity lost");
                        foreach (string name in new[] { "coreclr.dll", "Microsoft.UI.Xaml.Controls.pri", "af-ZA/Microsoft.ui.xaml.dll.mui", "ThirdPartyNotices/Microsoft.NETCore.App.Runtime.win-x64-10.0.12/LICENSE.TXT", "EasyEject.Modern.dll", ModernWindowsDependencyConfiguration.Name })
                            Throws<IOException>(() => File.WriteAllText(fixture.PathFor(name), "changed"));
                        review.Revalidate(CancellationToken.None);
                        using (var cancel = new CancellationTokenSource()) { cancel.Cancel(); Throws<OperationCanceledException>(() => review.Revalidate(cancel.Token)); }
                        review.Revalidate(CancellationToken.None);
                        Check(!AppDomain.CurrentDomain.GetAssemblies().Any(assembly => !String.IsNullOrEmpty(assembly.Location) && SafePaths.Within(assembly.Location, fixture.Root)), "Sample component loaded into runner");
                    } finally { review.Dispose(); }
                    fixture.RequireReleased(); review.Dispose(); Throws<ObjectDisposedException>(() => review.Revalidate(CancellationToken.None));
                }
            });
            Test("genuine pinned vendor changed native resource localized and notice copies refuse freshly authenticated inventory then permit fresh restored review", delegate {
                using (var fixture = PinnedVendorFixture(folder)) {
                    var approval = ReviewedMicrosoftVendorLayout.CreateLocalReviewApproval();
                    foreach (string name in new[] { "coreclr.dll", "Microsoft.UI.Xaml.Controls.pri", "af-ZA/Microsoft.ui.xaml.dll.mui", "ThirdPartyNotices/Microsoft.NETCore.App.Runtime.win-x64-10.0.12/LICENSE.TXT" }) {
                        byte[] original = File.ReadAllBytes(fixture.PathFor(name)), changed = (byte[])original.Clone(); changed[0] ^= 1;
                        ComponentPublish(fixture, name, changed);
                        using (var content = fixture.Review()) content.Revalidate(CancellationToken.None);
                        Throws<SafetyException>(() => { using (var unexpected = ApplicationPackage(fixture, CancellationToken.None, approval)) { } });
                        fixture.RequireReleased(); Check(File.ReadAllBytes(fixture.PathFor(name)).SequenceEqual(changed), "Refusal cleaned changed fixture");
                        ComponentPublish(fixture, name, original);
                        using (var retry = ApplicationPackage(fixture, CancellationToken.None, approval)) retry.Revalidate(CancellationToken.None);
                    }
                }
            });
            Test("genuine pinned vendor held review refuses late extra content while preserving original copies", delegate {
                using (var fixture = PinnedVendorFixture(folder)) {
                    using (var review = ApplicationPackage(fixture, CancellationToken.None, ReviewedMicrosoftVendorLayout.CreateLocalReviewApproval())) {
                        string extra = fixture.PathFor("late-owned.txt"); File.WriteAllText(extra, "retained owned extra");
                        Throws<SafetyException>(() => review.Revalidate(CancellationToken.None)); Check(File.ReadAllText(extra) == "retained owned extra", "Late content cleaned");
                    }
                    fixture.RequireReleased();
                }
            });
            Test("genuine pinned vendor composition refuses unconfigured and cancelled approval before trust", delegate {
                using (var fixture = PinnedVendorFixture(folder)) using (var cancel = new CancellationTokenSource()) {
                    int calls = 0; fixture.Executable.During = (path, held) => calls++; fixture.Certificate.During = () => calls++;
                    Throws<SafetyException>(() => { using (var unexpected = new ModernWindowsApplicationPackageReview(fixture.Root, fixture.Signing.Publisher(), fixture.Executable, new WindowsManifestTrust(fixture.Certificate), CancellationToken.None)) { } });
                    cancel.Cancel(); Throws<OperationCanceledException>(() => ApplicationPackage(fixture, cancel.Token, ReviewedMicrosoftVendorLayout.CreateLocalReviewApproval()));
                    Check(calls == 0, "Unconfigured/pre-cancelled review reached trust"); fixture.RequireReleased();
                }
            });
            Console.WriteLine(passed + " passed, " + failed + " failed. Real independently pinned vendor bytes; owned patched development application copies, placeholder application docs and fake publisher/chain trust only. No sample loading, execution, installation or device action.");
            return failed == 0 ? 0 : 1;
        }
    }
}
