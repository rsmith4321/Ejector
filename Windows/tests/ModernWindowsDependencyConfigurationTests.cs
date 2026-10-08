using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;

namespace EasyEject {
    public static partial class Tests {
        const string DependencyCore = "runtimepack.Microsoft.NETCore.App.Runtime.win-x64", DependencyDesktop = "runtimepack.Microsoft.WindowsDesktop.App.Runtime.win-x64";
        static string DependencyJson(string version = "1.2.3.4") {
            string v = ModernWindowsDependencyConfiguration.PackageVersion(version);
            return "{\"runtimeTarget\":{\"name\":\".NETCoreApp,Version=v10.0/win-x64\",\"signature\":\"\"},\"compilationOptions\":{},\"targets\":{\".NETCoreApp,Version=v10.0\":{},\".NETCoreApp,Version=v10.0/win-x64\":{" +
                "\"EasyEject.Modern/" + v + "\":{\"dependencies\":{\"EasyEject.Runtime\":\"" + v + "\",\"" + DependencyCore + "\":\"10.0.12\",\"" + DependencyDesktop + "\":\"10.0.12\",\"Microsoft.WindowsAppSDK\":\"2.5.1\"},\"runtime\":{\"EasyEject.Modern.dll\":{}}}," +
                "\"EasyEject.Runtime/" + v + "\":{\"runtime\":{\"EasyEject.Runtime.dll\":{\"assemblyVersion\":\"" + version + "\",\"fileVersion\":\"" + version + "\"}}}," +
                "\"" + DependencyCore + "/10.0.12\":{\"runtime\":{\"System.Private.CoreLib.dll\":{}},\"native\":{\"coreclr.dll\":{},\"hostfxr.dll\":{},\"hostpolicy.dll\":{}}}," +
                "\"" + DependencyDesktop + "/10.0.12\":{\"runtime\":{\"System.Windows.Forms.dll\":{}}}," +
                "\"Microsoft.WindowsAppSDK/2.5.1\":{\"runtime\":{\"lib/net6.0/Microsoft.WindowsAppRuntime.dll\":{\"assemblyVersion\":\"2.0.0.0\",\"fileVersion\":\"2.0.0.0\"}}}}},\"libraries\":{" +
                DependencyLibrary("EasyEject.Modern/" + v, "project") + "," + DependencyLibrary("EasyEject.Runtime/" + v, "project") + "," + DependencyLibrary(DependencyCore + "/10.0.12", "runtimepack") + "," + DependencyLibrary(DependencyDesktop + "/10.0.12", "runtimepack") + "," + DependencyLibrary("Microsoft.WindowsAppSDK/2.5.1", "package") + "},\"runtimes\":{\"win-x64\":[\"win\",\"any\",\"base\"]}}";
        }
        static string DependencyLibrary(string id, string type) {
            string[] parts = id.Split('/'); string lower = parts[0].ToLowerInvariant();
            return "\"" + id + "\":{\"type\":\"" + type + "\",\"serviceable\":" + (type == "package" ? "true" : "false") + ",\"sha512\":\"" + (type == "package" ? "sha512-" + Convert.ToBase64String(new byte[64]) : "") + "\"" + (type == "package" ? ",\"path\":\"" + lower + "/" + parts[1] + "\",\"hashPath\":\"" + lower + "." + parts[1] + ".nupkg.sha512\"" : "") + "}";
        }
        static void DependencyValidate(string json, ModernWindowsReleaseManifest release = null) { ModernWindowsDependencyConfiguration.Validate(Encoding.UTF8.GetBytes(json), release ?? new ModernManifestFixture().Parse()); }
        static void DependencyPublish(ModernPackageFixture fixture, string text) {
            string name = ModernWindowsDependencyConfiguration.Name; byte[] bytes = Encoding.UTF8.GetBytes(text); File.WriteAllBytes(fixture.PathFor(name), bytes);
            fixture.Metadata.Files.RemoveAll(file => file.Name == name); fixture.Metadata.Files.Add(new WindowsReleaseFile(name, bytes.Length, WindowsSignerEvidence.Hash(bytes))); fixture.Publish();
        }
        static void ModernWindowsDependencyConfigurationTests() {
            Test("modern dependency graph accepts exact project runtime families and flattened authenticated assets", delegate {
                DependencyValidate(DependencyJson()); DependencyValidate("\r\n " + DependencyJson("1.2.3.0") + "\t", new ModernManifestFixture { Version = "1.2.3.0" }.Parse());
                DependencyValidate(DependencyJson().Replace("\"assemblyVersion\":\"2.0.0.0\",\"fileVersion\":\"2.0.0.0\"", "\"assemblyVersion\":\"0.0.0.0\",\"fileVersion\":\"0.0.0.0\""));
                Check(WindowsManifestTrust.MaximumManifest == 65536, "Classic JSON limit changed");
            });
            Test("modern dependencies bind application backend Core Desktop and SDK versions", delegate {
                foreach (string change in new[] { DependencyJson().Replace("1.2.3.4", "1.2.3.5"), DependencyJson().Replace("10.0.12", "10.0.13"), DependencyJson().Replace("2.5.1", "2.5.2"), DependencyJson().Replace("EasyEject.Runtime", "Foreign.Runtime"), DependencyJson().Replace("Microsoft.WindowsDesktop.App.Runtime.win-x64", "Microsoft.AspNetCore.App.Runtime.win-x64") }) Throws<SafetyException>(() => DependencyValidate(change));
            });
            Test("modern dependencies refuse foreign runtime targets fallbacks and compilation policy", delegate {
                foreach (string change in new[] { DependencyJson().Replace("win-x64", "win-arm64"), DependencyJson().Replace("v10.0", "v9.0"), DependencyJson().Replace("\"signature\":\"\"", "\"signature\":\"foreign\""), DependencyJson().Replace("\"compilationOptions\":{}", "\"compilationOptions\":{\"defines\":[]}"), DependencyJson().Replace("[\"win\",\"any\",\"base\"]", "[\"win\",\"base\",\"any\"]"), DependencyJson().Replace("[\"win\",\"any\",\"base\"]", "[\"win\",\"any\",\"base\",\"linux\"]") }) Throws<SafetyException>(() => DependencyValidate(change));
            });
            Test("modern dependencies refuse unsupported launch fields and nonempty neutral target", delegate {
                foreach (string change in new[] { DependencyJson().Replace("\"compilationOptions\":", "\"foreign\":{},\"compilationOptions\":"), DependencyJson().Replace("\".NETCoreApp,Version=v10.0\":{}", "\".NETCoreApp,Version=v10.0\":{\"foreign\":{}}"), DependencyJson().Replace("\"native\":{", "\"runtimeTargets\":{},\"native\":{"), DependencyJson().Replace("\"native\":{", "\"resources\":{},\"native\":{") }) Throws<SafetyException>(() => DependencyValidate(change));
            });
            Test("modern dependencies refuse escaped duplicate case aliases malformed framing and typed fields", delegate {
                foreach (string change in new[] { DependencyJson().Replace("\"runtimeTarget\":", "\"runtimeTarget\":{},\"runtimeTarget\":"), DependencyJson().Replace("\"EasyEject.Modern/1.2.3.4\":", "\"EasyEject.Modern\\u002f1.2.3.4\":{},\"EasyEject.Modern/1.2.3.4\":"), DependencyJson().Replace("\"hostfxr.dll\":", "\"HOSTFXR.dll\":{},\"hostfxr.dll\":"), DependencyJson().Replace("\"hostfxr.dll\":", "\"ho\\u0073tfxr.dll\":{},\"hostfxr.dll\":"), DependencyJson().Replace("\"runtimeTarget\":", "\"__type\":\"foreign\",\"runtimeTarget\":"), DependencyJson().Replace("\"serviceable\":true", "\"serviceable\":\"true\""), DependencyJson() + "{}", "[]", "null", "{}", DependencyJson().Replace("\"signature\":\"\"", "\"signature\":\"\",,") }) Throws<SafetyException>(() => DependencyValidate(change));
            });
            Test("modern dependencies refuse missing target library declarations and dangling direct edges", delegate {
                Throws<SafetyException>(() => DependencyValidate(DependencyJson().Replace("\"EasyEject.Runtime\":\"1.2.3.4\"", "\"EasyEject.Runtime\":\"1.2.3.5\"")));
                Throws<SafetyException>(() => DependencyValidate(DependencyJson().Replace(DependencyLibrary("EasyEject.Runtime/1.2.3.4", "project") + ",", "")));
                Throws<SafetyException>(() => DependencyValidate(DependencyJson().Replace("\"Microsoft.WindowsAppSDK\":\"2.5.1\"", "\"microsoft.windowsappsdk\":\"2.5.1\"")));
                Throws<SafetyException>(() => DependencyValidate(DependencyJson().Replace("\"Microsoft.WindowsAppSDK\":\"2.5.1\"", "\"Foreign\":\"2.5.1\"")));
                Throws<SafetyException>(() => DependencyValidate(DependencyJson().Replace("\"Microsoft.WindowsAppSDK\":\"2.5.1\"", "\"Foreign\":\"2.5.1\",\"Microsoft.WindowsAppSDK\":\"2.5.1\"")));
            });
            Test("modern dependencies refuse project type servicing and package cache hash metadata changes", delegate {
                foreach (string change in new[] { DependencyJson().Replace("\"type\":\"project\"", "\"type\":\"package\""), DependencyJson().Replace("\"serviceable\":false", "\"serviceable\":true"), DependencyJson().Replace("\"serviceable\":true", "\"serviceable\":false"), DependencyJson().Replace("sha512-", "sha256-"), DependencyJson().Replace("microsoft.windowsappsdk/2.5.1", "../microsoft.windowsappsdk/2.5.1"), DependencyJson().Replace("microsoft.windowsappsdk.2.5.1.nupkg.sha512", "foreign.sha512"), DependencyJson().Replace("\"sha512\":\"\"", "\"sha512\":\"foreign\"") }) Throws<SafetyException>(() => DependencyValidate(change));
            });
            Test("modern dependency closure refuses declared libraries unreachable from the application", delegate {
                string json = DependencyJson().Replace("\"libraries\":{", "\"libraries\":{" + DependencyLibrary("Foreign/1.0.0", "package") + ",").Replace("\"EasyEject.Modern/1.2.3.4\":{\"dependencies\":", "\"Foreign/1.0.0\":{\"dependencies\":{}},\"EasyEject.Modern/1.2.3.4\":{\"dependencies\":");
                Throws<SafetyException>(() => DependencyValidate(json));
            });
            Test("modern dependencies refuse external traversal alternate architecture ambiguous and absent flattened assets", delegate {
                foreach (string path in new[] { "../coreclr.dll", "C:/coreclr.dll", "nested/coreclr.dll", "coreclr.dll:extra", "runtimes/win-arm64/native/coreclr.dll", "runtimes/win-x64/native/../coreclr.dll", "foreign.dll", "CoreCLR.dll", "coreclr.exe", "coreclr.dll.", "coreclr.dll\\foreign" }) Throws<SafetyException>(() => DependencyValidate(DependencyJson().Replace("\"coreclr.dll\":", "\"" + path.Replace("\\", "\\\\") + "\":")));
                Throws<SafetyException>(() => DependencyValidate(DependencyJson().Replace("\"hostfxr.dll\":{}", "\"hostfxr.dll\":{},\"runtimes/win-x64/native/hostfxr.dll\":{}")));
            });
            Test("modern dependencies refuse swapped asset owners roles and declared backend assembly identity", delegate {
                Throws<SafetyException>(() => DependencyValidate(DependencyJson().Replace("System.Private.CoreLib.dll", "swap.dll").Replace("System.Windows.Forms.dll", "System.Private.CoreLib.dll").Replace("swap.dll", "System.Windows.Forms.dll")));
                Throws<SafetyException>(() => DependencyValidate(DependencyJson().Replace("\"native\":{", "\"runtime\":{")));
                Throws<SafetyException>(() => DependencyValidate(DependencyJson().Replace("\"assemblyVersion\":\"1.2.3.4\"", "\"assemblyVersion\":\"1.2.3.5\"")));
                Throws<SafetyException>(() => DependencyValidate(DependencyJson().Replace("\"fileVersion\":\"1.2.3.4\"", "\"fileVersion\":\"1.2.3.5\"")));
                Throws<SafetyException>(() => DependencyValidate(DependencyJson().Replace("\"EasyEject.Modern.dll\":{}", "\"EasyEject.Modern.dll\":{},\"Microsoft.ui.xaml.dll\":{}")));
            });
            Test("modern dependency bytes require strict bounded UTF8 and stable canonical library versions", delegate {
                var release = new ModernManifestFixture().Parse(); byte[] valid = Encoding.UTF8.GetBytes(DependencyJson());
                Throws<SafetyException>(() => ModernWindowsDependencyConfiguration.Validate(valid, null));
                foreach (byte[] bytes in new[] { null, new byte[0], new byte[ModernWindowsDependencyConfiguration.MaximumBytes + 1], new byte[] { 0xff }.Concat(valid).ToArray(), new byte[] { 0xef, 0xbb, 0xbf }.Concat(valid).ToArray() }) Throws<SafetyException>(() => ModernWindowsDependencyConfiguration.Validate(bytes, release));
                foreach (string version in new[] { "2.05.1", "2.5.1-preview", "2.5.1.0", "2.5", "2.5.65536", "2.5.1+foreign" }) Throws<SafetyException>(() => DependencyValidate(DependencyJson().Replace("2.5.1", version)));
            });
            Test("modern dependency held review refreshes trust retains bytes and permits only fresh explicit retry", delegate {
                using (var f = new ModernPackageFixture()) {
                    DependencyPublish(f, DependencyJson()); string hash = WindowsSignerEvidence.Hash(File.ReadAllBytes(f.PathFor(ModernWindowsDependencyConfiguration.Name)));
                    using (var review = f.Review()) { ModernWindowsDependencyConfiguration.Require(review, CancellationToken.None); Throws<IOException>(() => File.WriteAllText(f.PathFor(ModernWindowsDependencyConfiguration.Name), "replacement")); f.Certificate.Throw = true; Throws<SafetyException>(() => ModernWindowsDependencyConfiguration.Require(review, CancellationToken.None)); }
                    f.RequireReleased(); Check(hash == WindowsSignerEvidence.Hash(File.ReadAllBytes(f.PathFor(ModernWindowsDependencyConfiguration.Name))), "Dependency bytes changed");
                    f.Certificate.Throw = false; DependencyPublish(f, DependencyJson().Replace("2.5.1", "2.5.2")); using (var review = f.Review()) Throws<SafetyException>(() => ModernWindowsDependencyConfiguration.Require(review, CancellationToken.None));
                    f.RequireReleased(); DependencyPublish(f, DependencyJson()); using (var review = f.Review()) ModernWindowsDependencyConfiguration.Require(review, CancellationToken.None); f.RequireReleased();
                }
            });
            Test("modern dependency cancellation and disposed review release every owned resource", delegate {
                Throws<ArgumentNullException>(() => ModernWindowsDependencyConfiguration.Require(null, CancellationToken.None));
                using (var f = new ModernPackageFixture()) { DependencyPublish(f, DependencyJson()); var review = f.Review(); using (var cancel = new CancellationTokenSource()) { cancel.Cancel(); Throws<OperationCanceledException>(() => ModernWindowsDependencyConfiguration.Require(review, cancel.Token)); } review.Dispose(); f.RequireReleased(); Throws<ObjectDisposedException>(() => ModernWindowsDependencyConfiguration.Require(review, CancellationToken.None)); }
            });
        }
        static int ModernDependencySample(string suppliedPath) {
            ModernWindowsDependencyConfigurationTests(); string path = SafePaths.Normalize(suppliedPath); byte[] bytes;
            using (var parents = SafePaths.HoldDirectories(Path.GetDirectoryName(path))) using (var held = SafePaths.ReadFile(path, 65536)) {
                Check(held.Length > 0 && held.Length <= ModernWindowsDependencyConfiguration.MaximumBytes, "Sample size unsupported"); var stamp = Native.Info(held.SafeFileHandle); string hash = VerifiedImporter.Hash(held, CancellationToken.None); bytes = new byte[(int)held.Length]; held.Position = 0; int done = 0; while (done < bytes.Length) { int n = held.Read(bytes, done, bytes.Length - done); Check(n > 0, "Incomplete sample"); done += n; }
                var release = ActualDependencyDeclaration(Path.GetDirectoryName(path));
                Test("modern actual development dependency sample refuses stable project declaration", delegate { Throws<SafetyException>(() => ModernWindowsDependencyConfiguration.Validate(bytes, release)); });
                string stable = Encoding.UTF8.GetString(bytes).Replace("0.1.0-dev", "0.1.0");
                Test("modern actual dependency snapshot admits unchanged complete graph under explicit synthetic stable declaration", delegate { ModernWindowsDependencyConfiguration.Validate(Encoding.UTF8.GetBytes(stable), release); });
                Test("modern actual dependency snapshot refuses absent inventoried asset and substituted runtime version", delegate {
                    Check(release.Files.Any(file => file.Name == "System.Text.Json.dll"), "Actual missing-asset case has no original asset");
                    var missing = ActualDependencyDeclaration(Path.GetDirectoryName(path), "System.Text.Json.dll"); Throws<SafetyException>(() => ModernWindowsDependencyConfiguration.Validate(Encoding.UTF8.GetBytes(stable), missing));
                    Throws<SafetyException>(() => ModernWindowsDependencyConfiguration.Validate(Encoding.UTF8.GetBytes(stable.Replace("10.0.12", "10.0.13")), release));
                });
                Test("modern actual WebView projection binds reference type direct edge and matching declared assembly version", delegate {
                    string changed = stable.Replace("\"type\": \"reference\"", "\"type\": \"package\""); Check(changed != stable, "Actual reference mutation matched no declaration"); Throws<SafetyException>(() => ModernWindowsDependencyConfiguration.Validate(Encoding.UTF8.GetBytes(changed), release));
                    changed = stable.Replace("\"assemblyVersion\": \"1.0.3719.77\"", "\"assemblyVersion\": \"1.0.3719.78\""); Check(changed != stable, "Actual projection version mutation matched no declaration"); Throws<SafetyException>(() => ModernWindowsDependencyConfiguration.Validate(Encoding.UTF8.GetBytes(changed), release));
                    changed = stable.Replace("\"Microsoft.Web.WebView2.Core.Projection\": \"1.0.3719.77\"", "\"Microsoft.Web.WebView2.Core.Projection\": \"1.0.3719.78\""); Check(changed != stable, "Actual projection edge mutation matched no declaration"); Throws<SafetyException>(() => ModernWindowsDependencyConfiguration.Validate(Encoding.UTF8.GetBytes(changed), release));
                });
                parents.Validate(); held.Position = 0; Check(VerifiedImporter.SameStamp(stamp, Native.Info(held.SafeFileHandle)) && VerifiedImporter.Hash(held, CancellationToken.None) == hash && Native.FinalPath(held.SafeFileHandle).Equals(path, StringComparison.OrdinalIgnoreCase), "Read-only sample changed");
                Console.WriteLine("Read-only dependency sample SHA256 " + hash + "; only immutable in-memory copies modified.");
            }
            Console.WriteLine(passed + " passed; " + failed + " failed. Generated graphs and read-only owned development sample; fake declarations are not publisher/component approval. No candidate execution or device action."); return failed == 0 ? 0 : 1;
        }
        static ModernWindowsReleaseManifest ActualDependencyDeclaration(string folder, string omit = null) {
            // Synthetic declaration of observed names, not authentication of this
            // unsigned sample folder. No content-review approval is asserted.
            var f = new ModernManifestFixture { Version = "0.1.0.0" };
            foreach (string path in Directory.GetFiles(folder, "*", SearchOption.AllDirectories)) { string name = path.Substring(folder.Length + 1).Replace('\\', '/'); if (name != omit && !f.Files.Any(file => file.Name == name)) f.Files.Add(new WindowsReleaseFile(name, new FileInfo(path).Length, new string('a', 64))); } return f.Parse();
        }
    }
}
