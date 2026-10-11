using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;

namespace EasyEject {
    public static partial class Tests {
        static void ModernWindowsComponentVersionsTests() {
            Test("actual component versions bind held native resources and assembly manifest without loading code", delegate {
                string path = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, WindowsReleaseManifest.Executable);
                var before = AppDomain.CurrentDomain.GetAssemblies();
                using (var held = File.Open(path, FileMode.Open, FileAccess.Read, FileShare.Read)) {
                    held.Position = 13; ModernWindowsComponentVersions.Require(path, held, new ModernWindowsDependencyConfiguration.AssetVersions("owned classic fixture", "0.2.0.0", "0.2.0.0"), CancellationToken.None);
                    Check(held.Position == 13, "Metadata inspection moved held stream position");
                }
                Check(!AppDomain.CurrentDomain.GetAssemblies().Except(before).Any(assembly => assembly.GetName().Name == "EasyEject.Windows"), "Fixture code was loaded");
            });
            Test("actual component file and assembly version mismatches independently refuse", delegate {
                string path = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, WindowsReleaseManifest.Executable);
                using (var held = File.Open(path, FileMode.Open, FileAccess.Read, FileShare.Read)) {
                    Throws<SafetyException>(() => ModernWindowsComponentVersions.Require(path, held, new ModernWindowsDependencyConfiguration.AssetVersions("owned", null, "0.2.0.1"), CancellationToken.None));
                    Throws<SafetyException>(() => ModernWindowsComponentVersions.Require(path, held, new ModernWindowsDependencyConfiguration.AssetVersions("owned", "0.2.0.1", null), CancellationToken.None));
                }
            });
            Test("actual component metadata refuses another path cancellation and absent manifests", delegate {
                using (var fixture = new Fixture()) {
                    string path = Path.Combine(fixture.Source, "owned.dll"); File.WriteAllText(path, "owned malformed bytes");
                    using (var held = File.Open(path, FileMode.Open, FileAccess.Read, FileShare.Read)) {
                        Throws<SafetyException>(() => ModernWindowsComponentVersions.Require(Path.Combine(fixture.Source, "other.dll"), held, new ModernWindowsDependencyConfiguration.AssetVersions("owned", null, null), CancellationToken.None));
                        using (var cancel = new CancellationTokenSource()) { cancel.Cancel(); Throws<OperationCanceledException>(() => ModernWindowsComponentVersions.Require(path, held, new ModernWindowsDependencyConfiguration.AssetVersions("owned", null, null), cancel.Token)); }
                        Throws<SafetyException>(() => ModernWindowsComponentVersions.Require(path, held, new ModernWindowsDependencyConfiguration.AssetVersions("owned", null, "0.2.0.0"), CancellationToken.None));
                        Throws<SafetyException>(() => ModernWindowsComponentVersions.Require(path, held, new ModernWindowsDependencyConfiguration.AssetVersions("owned", "0.0.0.0", null), CancellationToken.None));
                    }
                }
            });
            Test("held batch component inspection freezes unique membership and restores exact streams", delegate {
                using (var fixture = new ModernPackageFixture()) {
                    using (var review = fixture.Review()) {
                        int calls = 0;
                        review.InspectMany(new[] { "coreclr.dll", "hostfxr.dll" }, (name, stream) => { Check(stream.CanRead && !stream.CanWrite, "Component stream was writable"); stream.Position = stream.Length; calls++; }, CancellationToken.None);
                        Check(calls == 2, "Batch skipped a held asset"); review.Inspect("coreclr.dll", stream => Check(stream.Position == 0, "Stream position leaked"), CancellationToken.None);
                        Throws<SafetyException>(() => review.InspectMany(new[] { "coreclr.dll", "coreclr.dll" }, (name, stream) => calls++, CancellationToken.None));
                        Throws<SafetyException>(() => review.InspectMany(new[] { "missing.dll" }, (name, stream) => calls++, CancellationToken.None));
                        Check(calls == 2, "Invalid membership invoked a component callback");
                        using (var cancel = new CancellationTokenSource()) { cancel.Cancel(); Throws<OperationCanceledException>(() => review.InspectMany(new[] { "coreclr.dll" }, (name, stream) => calls++, cancel.Token)); }
                    }
                    fixture.RequireReleased();
                }
            });
            Test("held batch component inspection refuses late layout changes and retains foreign files", delegate {
                using (var fixture = new ModernPackageFixture()) {
                    var review = fixture.Review(); string foreign = fixture.PathFor("foreign.txt");
                    Throws<SafetyException>(() => review.InspectMany(new[] { "coreclr.dll" }, (name, stream) => File.WriteAllText(foreign, "retained"), CancellationToken.None));
                    Check(File.ReadAllText(foreign) == "retained", "Foreign data was removed"); review.Dispose(); fixture.RequireReleased();
                    Throws<ObjectDisposedException>(() => review.InspectMany(new[] { "coreclr.dll" }, (name, stream) => {}, CancellationToken.None));
                }
            });
        }
        static int ModernComponentVersionsSample(string folder) {
            byte[] json = File.ReadAllBytes(Path.Combine(folder, ModernWindowsDependencyConfiguration.Name));
            byte[] stable = Encoding.UTF8.GetBytes(Encoding.UTF8.GetString(json).Replace("0.2.0-dev", "0.2.0"));
            var assets = ModernWindowsDependencyConfiguration.Validate(stable, ActualDependencyDeclaration(folder));
            Test("actual declared component versions match read-only sample without adding candidate assemblies", delegate {
                var before = AppDomain.CurrentDomain.GetAssemblies();
                foreach (var asset in assets) {
                    string path = Path.Combine(folder, asset.Name);
                    using (var directories = SafePaths.HoldDirectories(Path.GetDirectoryName(path))) using (var held = SafePaths.ReadFile(path)) {
                        string hash = VerifiedImporter.Hash(held, CancellationToken.None); ModernWindowsComponentVersions.Require(path, held, asset, CancellationToken.None);
                        directories.Validate(); held.Position = 0; Check(VerifiedImporter.Hash(held, CancellationToken.None) == hash, "Original sample changed");
                    }
                }
                string sampleRoot = SafePaths.Normalize(folder).TrimEnd('\\') + "\\";
                var added = AppDomain.CurrentDomain.GetAssemblies().Except(before).Where(assembly => !assembly.IsDynamic).ToArray();
                Console.WriteLine("New framework assembly locations: " + String.Join("; ", added.Select(assembly => assembly.GetName().Name + " = " + assembly.Location).ToArray()));
                Check(!added.Any(assembly => assembly.Location.StartsWith(sampleRoot, StringComparison.OrdinalIgnoreCase)), "Candidate component was loaded from the sample folder");
                Console.WriteLine(assets.Length + " declared assets inspected; declarations without versions remain outside version proof.");
            });
            Console.WriteLine(passed + " passed, " + failed + " failed"); return failed == 0 ? 0 : 1;
        }
    }
}
