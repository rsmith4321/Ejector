using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Xml;

namespace EasyEject {
    // On-disk launch configuration only. Does not approve component identity,
    // process/environment policy, archive extraction or installation.
    internal static class ModernWindowsRuntimeConfiguration {
        internal const string Name = "EasyEject.Modern.runtimeconfig.json";
        internal const int MaximumBytes = 8192;
        internal static void Require(ModernWindowsPackageContentReview content, CancellationToken cancel) {
            if (content == null) throw new ArgumentNullException("content");
            cancel.ThrowIfCancellationRequested();
            content.Revalidate(cancel);
            content.Inspect(Name, stream => {
                if (stream.Length <= 0 || stream.Length > MaximumBytes) throw new SafetyException("The modern runtime configuration is missing or excessive.");
                stream.Position = 0; byte[] bytes = new byte[checked((int)stream.Length)]; int done = 0;
                while (done < bytes.Length) {
                    cancel.ThrowIfCancellationRequested(); int read = stream.Read(bytes, done, bytes.Length - done);
                    if (read == 0) throw new SafetyException("The modern runtime configuration is incomplete."); done += read;
                }
                Validate(bytes, content.Release); cancel.ThrowIfCancellationRequested();
            }, cancel);
            content.Revalidate(cancel);
        }
        internal static void Validate(byte[] supplied, ModernWindowsReleaseManifest release) {
            if (release == null) throw new SafetyException("Modern runtime configuration requires the authenticated release inventory.");
            if (supplied == null || supplied.Length == 0 || supplied.Length > MaximumBytes) throw new SafetyException("The modern runtime configuration is missing or excessive.");
            // Parse only our bounded immutable byte snapshot; never ask the host to load it.
            var root = WindowsReleaseManifest.Fields(WindowsReleaseManifest.ReadJson((byte[])supplied.Clone()), new[] { "runtimeOptions" });
            var options = WindowsReleaseManifest.Fields(root["runtimeOptions"], new[] { "tfm", "includedFrameworks", "configProperties" });
            if (WindowsReleaseManifest.Text(options["tfm"]) != "net10.0") throw new SafetyException("The modern runtime target does not match the supported application family.");
            var frameworks = options["includedFrameworks"]; Type(frameworks, "array");
            var expected = new HashSet<string>(new[] { "Microsoft.NETCore.App", "Microsoft.WindowsDesktop.App" }, StringComparer.Ordinal);
            foreach (XmlNode node in frameworks.ChildNodes) {
                var item = node as XmlElement;
                if (item == null || item.LocalName != "item") throw new SafetyException("Malformed modern included runtime declaration.");
                var entry = WindowsReleaseManifest.Fields(item, new[] { "name", "version" });
                if (!expected.Remove(WindowsReleaseManifest.Text(entry["name"])) || WindowsReleaseManifest.Text(entry["version"]) != release.DotNet)
                    throw new SafetyException("The included runtime names or versions differ from the authenticated release.");
            }
            if (expected.Count != 0) throw new SafetyException("The modern self-contained package omits an included runtime declaration.");
            var properties = WindowsReleaseManifest.Fields(options["configProperties"], new[] {
                "System.Reflection.Metadata.MetadataUpdater.IsSupported", "System.Runtime.Serialization.EnableUnsafeBinaryFormatterSerialization", "CSWINRT_USE_WINDOWS_UI_XAML_PROJECTIONS"
            });
            foreach (var property in properties.Values) {
                Type(property, "boolean");
                if (property.ChildNodes.Cast<XmlNode>().Any(node => node.NodeType != XmlNodeType.Text) || property.InnerText != "false")
                    throw new SafetyException("The modern runtime configuration changes a reviewed launch property.");
            }
        }
        static void Type(XmlElement element, string type) {
            if (element == null || element.Attributes.Count != 1 || element.GetAttribute("type") != type) throw new SafetyException("Unexpected modern runtime configuration type.");
        }
    }
}
