using System;
using System.IO;
using System.Text;
using System.Threading;

namespace EasyEject {
    public static partial class Tests {
        static ModernWindowsApplicationPackageReview ApplicationPackage(ModernPackageFixture fixture, CancellationToken cancel = default(CancellationToken)) {
            return new ModernWindowsApplicationPackageReview(fixture.Root, fixture.Signing.Publisher(), fixture.Executable, new WindowsManifestTrust(fixture.Certificate), cancel);
        }
        static void ModernWindowsApplicationPackageTests() {
            Test("modern composed application review refuses absent publisher without acquiring files", delegate {
                using (var fixture = new ModernPackageFixture()) {
                    Throws<SafetyException>(() => new ModernWindowsApplicationPackageReview(fixture.Root, null, fixture.Executable, new WindowsManifestTrust(fixture.Certificate), CancellationToken.None));
                    fixture.RequireReleased();
                }
            });
            Test("modern composed application review refuses pre-cancellation before trust and releases resources", delegate {
                using (var fixture = new ModernPackageFixture()) using (var cancel = new CancellationTokenSource()) {
                    int callbacks = 0; fixture.Executable.During = (path, stream) => callbacks++; fixture.Certificate.During = () => callbacks++; cancel.Cancel();
                    Throws<OperationCanceledException>(() => ApplicationPackage(fixture, cancel.Token));
                    Check(callbacks == 0, "Pre-cancelled application review invoked trust"); fixture.RequireReleased();
                }
            });
            Test("modern composed application review rejects authenticated classic executable and releases all held files", delegate {
                using (var fixture = new ModernPackageFixture()) {
                    using (var content = fixture.Review()) Check(content.Release.Version == fixture.Metadata.Version, "Content control refused");
                    Throws<SafetyException>(() => { using (var unexpected = ApplicationPackage(fixture)) { } }); fixture.RequireReleased();
                }
            });
        }
        static void BackendIdentityPublish(ModernPackageFixture fixture, byte[] bytes) {
            string name = "EasyEject.Runtime.dll"; File.WriteAllBytes(fixture.PathFor(name), bytes);
            fixture.Metadata.Files.RemoveAll(file => file.Name == name);
            fixture.Metadata.Files.Add(new WindowsReleaseFile(name, bytes.Length, WindowsSignerEvidence.Hash(bytes))); fixture.Publish();
        }
        static void ApplicationPackageFixture(ModernPackageFixture fixture, byte[] host, byte[] managed, byte[] backendBytes) {
            NativeHostPublish(fixture, NativeHostStable(host)); ModernIdentityPublish(fixture, ModernIdentityStable(managed));
            BackendIdentityPublish(fixture, ModernIdentityStable(backendBytes));
            ModernRuntimePublish(fixture, ModernRuntimeJson()); DependencyPublish(fixture, DependencyJson("0.1.0.0"));
        }
        static int ModernApplicationPackageSample(string folder) {
            byte[] host = File.ReadAllBytes(Path.Combine(folder, "EasyEject.Modern.exe"));
            byte[] managed = File.ReadAllBytes(Path.Combine(folder, "EasyEject.Modern.dll"));
            byte[] backendBytes = File.ReadAllBytes(Path.Combine(folder, "EasyEject.Runtime.dll"));
            Test("modern composed application accepts owned stable application copies and retains complete ownership", delegate {
                using (var fixture = new ModernPackageFixture()) {
                    ApplicationPackageFixture(fixture, host, managed, backendBytes);
                    var review = ApplicationPackage(fixture);
                    try {
                        Check(review.Folder == SafePaths.Normalize(fixture.Root) && review.Release.Version == "0.1.0.0", "Composed identity was lost");
                        Throws<IOException>(() => File.WriteAllText(fixture.PathFor("EasyEject.Modern.dll"), "changed"));
                        Throws<IOException>(() => File.WriteAllText(fixture.PathFor(ModernWindowsDependencyConfiguration.Name), "changed"));
                        review.Revalidate(CancellationToken.None);
                        using (var cancel = new CancellationTokenSource()) { cancel.Cancel(); Throws<OperationCanceledException>(() => review.Revalidate(cancel.Token)); }
                        review.Revalidate(CancellationToken.None);
                    } finally { review.Dispose(); }
                    fixture.RequireReleased(); review.Dispose(); Throws<ObjectDisposedException>(() => review.Revalidate(CancellationToken.None));
                }
            });
            Test("modern composed application refuses development managed identity after valid native host", delegate {
                using (var fixture = new ModernPackageFixture()) {
                    ApplicationPackageFixture(fixture, host, managed, backendBytes); ModernIdentityPublish(fixture, managed);
                    using (var content = fixture.Review()) ModernWindowsAppHostIdentity.Require(content, CancellationToken.None);
                    Throws<SafetyException>(() => { using (var unexpected = ApplicationPackage(fixture)) { } }); fixture.RequireReleased();
                }
            });
            Test("modern composed application refuses conflicting runtime after native and managed identity", delegate {
                using (var fixture = new ModernPackageFixture()) {
                    ApplicationPackageFixture(fixture, host, managed, backendBytes); ModernRuntimePublish(fixture, ModernRuntimeJson("10.0.13"));
                    using (var content = fixture.Review()) { ModernWindowsAppHostIdentity.Require(content, CancellationToken.None); ModernWindowsManagedApplicationIdentity.Require(content, CancellationToken.None); }
                    Throws<SafetyException>(() => { using (var unexpected = ApplicationPackage(fixture)) { } }); fixture.RequireReleased();
                }
            });
            Test("modern composed application refuses conflicting dependency runtime after valid application configuration", delegate {
                using (var fixture = new ModernPackageFixture()) {
                    ApplicationPackageFixture(fixture, host, managed, backendBytes); DependencyPublish(fixture, DependencyJson("0.1.0.0").Replace("10.0.12", "10.0.13"));
                    using (var content = fixture.Review()) { ModernWindowsAppHostIdentity.Require(content, CancellationToken.None); ModernWindowsManagedApplicationIdentity.Require(content, CancellationToken.None); ModernWindowsRuntimeConfiguration.Require(content, CancellationToken.None); }
                    Throws<SafetyException>(() => { using (var unexpected = ApplicationPackage(fixture)) { } }); fixture.RequireReleased();
                }
            });
            Test("modern composed application revalidation refuses late extra files and preserves them", delegate {
                using (var fixture = new ModernPackageFixture()) {
                    ApplicationPackageFixture(fixture, host, managed, backendBytes);
                    using (var review = ApplicationPackage(fixture)) {
                        string extra = fixture.PathFor("later.txt"); File.WriteAllText(extra, "retained");
                        Throws<SafetyException>(() => review.Revalidate(CancellationToken.None)); Check(File.ReadAllText(extra) == "retained", "Revalidation cleaned extra data");
                    }
                    fixture.RequireReleased();
                }
            });
            Test("modern composed application refuses authenticated malformed backend bytes", delegate {
                using (var fixture = new ModernPackageFixture()) {
                    ApplicationPackageFixture(fixture, host, managed, backendBytes); BackendIdentityPublish(fixture, Encoding.UTF8.GetBytes("owned non-library payload"));
                    Throws<SafetyException>(() => { using (var unexpected = ApplicationPackage(fixture)) { } }); fixture.RequireReleased();
                }
            });
            Test("modern composed application refuses development backend despite stable main and declarations", delegate {
                using (var fixture = new ModernPackageFixture()) {
                    ApplicationPackageFixture(fixture, host, managed, backendBytes); BackendIdentityPublish(fixture, backendBytes);
                    Throws<SafetyException>(() => { using (var unexpected = ApplicationPackage(fixture)) { } }); fixture.RequireReleased();
                }
            });
            Console.WriteLine(passed + " passed, " + failed + " failed"); return failed == 0 ? 0 : 1;
        }
    }
}
