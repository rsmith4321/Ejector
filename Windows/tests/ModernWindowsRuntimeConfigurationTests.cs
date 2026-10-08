using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;

namespace EasyEject {
    public static partial class Tests {
        static string ModernRuntimeJson(string version = "10.0.12") {
            return "{\"runtimeOptions\":{\"tfm\":\"net10.0\",\"includedFrameworks\":[{\"name\":\"Microsoft.NETCore.App\",\"version\":\"" + version + "\"},{\"name\":\"Microsoft.WindowsDesktop.App\",\"version\":\"" + version + "\"}],\"configProperties\":{\"System.Reflection.Metadata.MetadataUpdater.IsSupported\":false,\"System.Runtime.Serialization.EnableUnsafeBinaryFormatterSerialization\":false,\"CSWINRT_USE_WINDOWS_UI_XAML_PROJECTIONS\":false}}}";
        }
        static void ModernRuntimeValidate(string json, ModernWindowsReleaseManifest release = null) {
            ModernWindowsRuntimeConfiguration.Validate(Encoding.UTF8.GetBytes(json), release ?? new ModernManifestFixture().Parse());
        }
        static void ModernRuntimePublish(ModernPackageFixture fixture, string text) {
            byte[] bytes = Encoding.UTF8.GetBytes(text); string name = ModernWindowsRuntimeConfiguration.Name;
            File.WriteAllBytes(fixture.PathFor(name), bytes); fixture.Metadata.Files.RemoveAll(file => file.Name == name);
            fixture.Metadata.Files.Add(new WindowsReleaseFile(name, bytes.Length, WindowsSignerEvidence.Hash(bytes))); fixture.Publish();
        }
        static void ModernWindowsRuntimeConfigurationTests() {
            Test("modern runtime configuration accepts exact declared self-contained families and harmless ordering", delegate {
                ModernRuntimeValidate(ModernRuntimeJson());
                ModernRuntimeValidate(ModernRuntimeJson().Replace("Microsoft.NETCore.App", "swap").Replace("Microsoft.WindowsDesktop.App", "Microsoft.NETCore.App").Replace("swap", "Microsoft.WindowsDesktop.App"));
                var release = new ModernManifestFixture { DotNet = "10.0.13" }.Parse(); ModernRuntimeValidate(" \r\n" + ModernRuntimeJson("10.0.13") + "\t", release);
            });
            Test("modern runtime configuration binds both included versions to the authenticated release", delegate {
                foreach (string version in new[] { "10.0.0", "9.0.12", "11.0.12", "10.0.12-preview", "10.00.12", "10.0.12.0" }) Throws<SafetyException>(() => ModernRuntimeValidate(ModernRuntimeJson(version)));
                Throws<SafetyException>(() => ModernRuntimeValidate(ModernRuntimeJson().Replace("\"Microsoft.WindowsDesktop.App\",\"version\":\"10.0.12\"", "\"Microsoft.WindowsDesktop.App\",\"version\":\"10.0.13\"")));
            });
            Test("modern runtime configuration refuses shared-host roll-forward probing and extra launch fields", delegate {
                foreach (string field in new[] { "\"framework\":{}", "\"frameworks\":[]", "\"rollForward\":\"LatestMajor\"", "\"applyPatches\":true", "\"additionalProbingPaths\":[\"C:/foreign\"]", "\"startupHooks\":[\"foreign.dll\"]", "\"includedFrameworksExtra\":[]" })
                    Throws<SafetyException>(() => ModernRuntimeValidate(ModernRuntimeJson().Replace("\"tfm\":", field + ",\"tfm\":")));
                Throws<SafetyException>(() => ModernRuntimeValidate(ModernRuntimeJson().Replace("\"runtimeOptions\":", "\"extra\":{},\"runtimeOptions\":")));
                Throws<SafetyException>(() => ModernRuntimeValidate(ModernRuntimeJson().Replace("net10.0", "net9.0")));
            });
            Test("modern runtime configuration refuses missing duplicate foreign and typed included frameworks", delegate {
                string valid = ModernRuntimeJson();
                foreach (string changed in new[] { valid.Replace("Microsoft.WindowsDesktop.App", "Microsoft.NETCore.App"), valid.Replace("Microsoft.NETCore.App", "Microsoft.AspNetCore.App"), valid.Replace("Microsoft.NETCore.App", "microsoft.netcore.app"), valid.Replace("\"includedFrameworks\":[", "\"includedFrameworks\":[{} ,"), valid.Replace("\"name\":\"Microsoft.NETCore.App\"", "\"name\":10"), valid.Replace("\"version\":\"10.0.12\"", "\"version\":10"), valid.Replace("\"includedFrameworks\":[{\"name\":\"Microsoft.NETCore.App\",\"version\":\"10.0.12\"},", "\"includedFrameworks\":[") }) Throws<SafetyException>(() => ModernRuntimeValidate(changed));
            });
            Test("modern runtime configuration requires all reviewed properties false and no additional properties", delegate {
                string valid = ModernRuntimeJson();
                foreach (string changed in new[] { valid.Replace(":false", ":true"), valid.Replace(":false", ":\"false\""), valid.Replace(":false", ":0"), valid.Replace(":false", ":null"), valid.Replace("\"CSWINRT_USE_WINDOWS_UI_XAML_PROJECTIONS\":false", "\"extra\":false"), valid.Replace("\"configProperties\":{", "\"configProperties\":{\"extra\":false,"), valid.Replace("\"CSWINRT_USE_WINDOWS_UI_XAML_PROJECTIONS\":false", "\"CSWINRT_USE_WINDOWS_UI_XAML_PROJECTIONS\":{}") }) Throws<SafetyException>(() => ModernRuntimeValidate(changed));
            });
            Test("modern runtime configuration refuses duplicate escaped names malformed framing and wrong object types", delegate {
                string valid = ModernRuntimeJson();
                foreach (string changed in new[] { valid.Replace("\"tfm\":", "\"tfm\":\"net10.0\",\"tfm\":"), valid.Replace("\"tfm\":", "\"t\\u0066m\":\"net10.0\",\"tfm\":"), valid.Replace("\"name\":", "\"name\":\"Microsoft.NETCore.App\",\"name\":"), valid.Replace("\"CSWINRT_USE_WINDOWS_UI_XAML_PROJECTIONS\":false", "\"CSWINRT_USE_WINDOWS_UI_XAML_PROJECTIONS\":false,\"CSWINRT_USE_WINDOWS_UI_XAML_PROJECTIONS\":false"), valid + "{}", valid.Substring(0, valid.Length - 1), valid.Replace("\"tfm\":\"net10.0\",", "\"tfm\":\"net10.0\",,"), "[]", "null", "{}", "{\"runtimeOptions\":[]}" }) Throws<SafetyException>(() => ModernRuntimeValidate(changed));
            });
            Test("modern runtime configuration requires bounded strict UTF8 without BOM and authenticated metadata", delegate {
                byte[] valid = Encoding.UTF8.GetBytes(ModernRuntimeJson()); var release = new ModernManifestFixture().Parse();
                Throws<SafetyException>(() => ModernWindowsRuntimeConfiguration.Validate(valid, null));
                foreach (byte[] bytes in new[] { new byte[0], new byte[ModernWindowsRuntimeConfiguration.MaximumBytes + 1], new byte[] { 0xef, 0xbb, 0xbf }.Concat(valid).ToArray(), new byte[] { 0xff }.Concat(valid).ToArray() }) Throws<SafetyException>(() => ModernWindowsRuntimeConfiguration.Validate(bytes, release));
                Throws<SafetyException>(() => ModernWindowsRuntimeConfiguration.Validate(null, release));
            });
            Test("modern runtime configuration inspects held authenticated content without loading it and releases on disposal", delegate {
                using (var fixture = new ModernPackageFixture()) {
                    ModernRuntimePublish(fixture, ModernRuntimeJson()); string hash = WindowsSignerEvidence.Hash(File.ReadAllBytes(fixture.PathFor(ModernWindowsRuntimeConfiguration.Name)));
                    using (var review = fixture.Review()) {
                        ModernWindowsRuntimeConfiguration.Require(review, CancellationToken.None);
                        Throws<IOException>(() => File.WriteAllText(fixture.PathFor(ModernWindowsRuntimeConfiguration.Name), "changed"));
                        ModernWindowsRuntimeConfiguration.Require(review, CancellationToken.None);
                    }
                    Check(WindowsSignerEvidence.Hash(File.ReadAllBytes(fixture.PathFor(ModernWindowsRuntimeConfiguration.Name))) == hash, "Configuration changed"); fixture.RequireReleased();
                }
            });
            Test("modern runtime configuration independently refuses authenticated unsupported launch policy and permits explicit fresh retry", delegate {
                using (var fixture = new ModernPackageFixture()) {
                    ModernRuntimePublish(fixture, ModernRuntimeJson().Replace(":false", ":true"));
                    using (var review = fixture.Review()) Throws<SafetyException>(() => ModernWindowsRuntimeConfiguration.Require(review, CancellationToken.None));
                    fixture.RequireReleased(); ModernRuntimePublish(fixture, ModernRuntimeJson());
                    using (var review = fixture.Review()) ModernWindowsRuntimeConfiguration.Require(review, CancellationToken.None);
                    fixture.RequireReleased();
                }
            });
            Test("modern runtime configuration cancellation disposal and refreshed trust cannot grant launch authority", delegate {
                Throws<ArgumentNullException>(() => ModernWindowsRuntimeConfiguration.Require(null, CancellationToken.None));
                using (var fixture = new ModernPackageFixture()) {
                    ModernRuntimePublish(fixture, ModernRuntimeJson()); var review = fixture.Review();
                    using (var cancellation = new CancellationTokenSource()) { cancellation.Cancel(); Throws<OperationCanceledException>(() => ModernWindowsRuntimeConfiguration.Require(review, cancellation.Token)); }
                    fixture.Certificate.Throw = true;
                    Throws<SafetyException>(() => ModernWindowsRuntimeConfiguration.Require(review, CancellationToken.None));
                    review.Dispose(); fixture.RequireReleased(); Throws<ObjectDisposedException>(() => ModernWindowsRuntimeConfiguration.Require(review, CancellationToken.None));
                }
            });
        }
    }
}
