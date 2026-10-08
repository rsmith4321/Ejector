using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Text;
using System.Xml;

namespace EasyEject {
    // A small authenticated envelope binds the larger complete runtime inventory.
    // This parser does not approve a publisher, inspect files or enable a channel.
    internal sealed class ModernWindowsReleaseManifest {
        internal const string Application = "EasyEject.Modern", Platform = "windows-x64";
        internal const string ManifestName = "windows-modern-release.json", SignatureName = "windows-modern-release.json.p7s", IndexName = "windows-modern-files.json";
        internal const int MaximumIndex = 524288, MaximumFiles = 2048;
        internal const long MaximumFile = 1073741824, MaximumTotal = 2147483648;
        internal static readonly ReadOnlyCollection<string> RequiredFiles = Array.AsReadOnly(new[] {
            "EasyEject.Modern.exe", "EasyEject.Modern.dll", "EasyEject.Modern.pri", "EasyEject.Modern.deps.json", "EasyEject.Modern.runtimeconfig.json", "EasyEject.Runtime.dll",
            "coreclr.dll", "hostfxr.dll", "hostpolicy.dll", "System.Private.CoreLib.dll", "System.Windows.Forms.dll",
            "Microsoft.ui.xaml.dll", "Microsoft.WindowsAppRuntime.dll", "Assets/EasyEject.png", "README.txt", "LICENSE.txt"
        });
        static readonly string[] Extensions = { ".dll", ".exe", ".html", ".json", ".mui", ".pdb", ".png", ".pri", ".txt", ".winmd" };
        internal readonly string Version, SourceRevision, DotNet, WindowsAppSdk, MinimumWindows, IndexHash;
        internal readonly long IndexLength, TotalLength;
        internal readonly ReadOnlyCollection<WindowsReleaseFile> Files;
        internal readonly WindowsAuthenticatedManifest Authenticated;
        ModernWindowsReleaseManifest(WindowsAuthenticatedManifest authenticated, string version, string source, string dotnet, string sdk, string minimum, byte[] index, long total, List<WindowsReleaseFile> files) {
            Authenticated = authenticated; Version = version; SourceRevision = source; DotNet = dotnet; WindowsAppSdk = sdk; MinimumWindows = minimum;
            IndexHash = WindowsSignerEvidence.Hash(index); IndexLength = index.Length; TotalLength = total; Files = files.AsReadOnly();
        }
        internal static ModernWindowsReleaseManifest Parse(WindowsAuthenticatedManifest authenticated, byte[] suppliedIndex) {
            if (authenticated == null) throw new SafetyException("Modern release metadata requires an authenticated envelope.");
            if (suppliedIndex == null || suppliedIndex.Length == 0 || suppliedIndex.Length > MaximumIndex) throw new SafetyException("The modern runtime inventory is missing or excessive.");
            byte[] index = (byte[])suppliedIndex.Clone();
            var root = WindowsReleaseManifest.Fields(WindowsReleaseManifest.ReadJson(authenticated.CopyBytes()), new[] { "format", "application", "platform", "version", "sourceRevision", "runtime", "fileIndex" });
            if (Number(root["format"]) != 2 || Text(root["application"]) != Application || Text(root["platform"]) != Platform) throw new SafetyException("This is not a modern Windows x64 release envelope.");
            string version = Text(root["version"]), source = Text(root["sourceRevision"]);
            if (!WindowsReleaseManifest.StableVersion(version) || source.Length != 40 || source.Any(c => !LowerHex(c))) throw new SafetyException("The modern release version or source revision is invalid.");
            var runtime = WindowsReleaseManifest.Fields(root["runtime"], new[] { "dotnet", "windowsAppSdk", "minimumWindows" });
            string dotnet = Text(runtime["dotnet"]), sdk = Text(runtime["windowsAppSdk"]), minimum = Text(runtime["minimumWindows"]);
            if (!ThreePartVersion(dotnet, 10) || !ThreePartVersion(sdk, 2) || minimum != "10.0.19041.0") throw new SafetyException("The declared modern runtime family or compiled minimum Windows contract is unsupported.");
            var binding = WindowsReleaseManifest.Fields(root["fileIndex"], new[] { "name", "length", "sha256", "count", "totalLength" });
            string hash = Text(binding["sha256"]); long count = Number(binding["count"]), total = Number(binding["totalLength"]);
            if (Text(binding["name"]) != IndexName || Number(binding["length"]) != index.Length || !ImportHistory.ValidHash(hash) || hash != WindowsSignerEvidence.Hash(index) || count < RequiredFiles.Count || count > MaximumFiles || total <= 0 || total > MaximumTotal)
                throw new SafetyException("The runtime inventory does not match its authenticated byte/count/size binding.");
            var document = WindowsReleaseManifest.Fields(ReadIndex(index), new[] { "format", "files" });
            if (Number(document["format"]) != 1) throw new SafetyException("Unsupported modern runtime inventory format.");
            Type(document["files"], "array");
            var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase); var directories = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var files = new List<WindowsReleaseFile>(); long actualTotal = 0; string previous = null;
            foreach (XmlNode node in document["files"].ChildNodes) {
                var entry = node as XmlElement; if (entry == null || entry.LocalName != "item") throw new SafetyException("Malformed modern file inventory.");
                var fields = WindowsReleaseManifest.Fields(entry, new[] { "path", "length", "sha256" });
                string path = Text(fields["path"]), fileHash = Text(fields["sha256"]); long length = Number(fields["length"]);
                if (!ValidPath(path) || !names.Add(path) || previous != null && StringComparer.Ordinal.Compare(previous, path) >= 0 || length <= 0 || length > MaximumFile || !ImportHistory.ValidHash(fileHash) || files.Count >= MaximumFiles)
                    throw new SafetyException("Modern payload paths, order, sizes or hashes are unsupported.");
                previous = path; actualTotal += length;
                if (actualTotal > MaximumTotal) throw new SafetyException("Modern payload total exceeds its supported bound.");
                int slash = path.IndexOf('/'); while (slash >= 0) { directories.Add(path.Substring(0, slash)); slash = path.IndexOf('/', slash + 1); }
                files.Add(new WindowsReleaseFile(path, length, fileHash));
            }
            if (files.Count != count || actualTotal != total || names.Overlaps(directories) || RequiredFiles.Any(required => !files.Any(file => file.Name == required)))
                throw new SafetyException("The modern payload count, total, required files or file/directory identities do not match.");
            return new ModernWindowsReleaseManifest(authenticated, version, source, dotnet, sdk, minimum, index, total, files);
        }
        internal static bool ValidPath(string path) {
            if (String.IsNullOrEmpty(path) || path.Length > 200 || path.Equals(ManifestName, StringComparison.OrdinalIgnoreCase) || path.Equals(SignatureName, StringComparison.OrdinalIgnoreCase) || path.Equals(IndexName, StringComparison.OrdinalIgnoreCase)) return false;
            string[] parts = path.Split('/'); if (parts.Length > 5) return false;
            foreach (string part in parts) {
                if (part.Length == 0 || part == "." || part == ".." || part.EndsWith(".", StringComparison.Ordinal) || part.Any(c => !(c >= 'A' && c <= 'Z') && !(c >= 'a' && c <= 'z') && !(c >= '0' && c <= '9') && c != '_' && c != '-' && c != '.')) return false;
                string basis = part.Split('.')[0].ToUpperInvariant();
                if (basis == "CON" || basis == "PRN" || basis == "AUX" || basis == "NUL" || basis.Length == 4 && (basis.StartsWith("COM", StringComparison.Ordinal) || basis.StartsWith("LPT", StringComparison.Ordinal)) && basis[3] >= '1' && basis[3] <= '9') return false;
            }
            int dot = parts[parts.Length - 1].LastIndexOf('.');
            return dot > 0 && Extensions.Contains(parts[parts.Length - 1].Substring(dot).ToLowerInvariant());
        }
        static bool LowerHex(char c) { return c >= '0' && c <= '9' || c >= 'a' && c <= 'f'; }
        static bool ThreePartVersion(string text, int major) {
            System.Version version; return System.Version.TryParse(text, out version) && version.Major == major && version.Build >= 0 && version.Revision == -1 && version.Minor <= 65535 && version.Build <= 65535 && version.ToString(3) == text;
        }
        static XmlElement ReadIndex(byte[] bytes) {
            try {
                string text = new UTF8Encoding(false, true).GetString(bytes); JsonSyntax.Validate(text);
                if (text.Length == 0 || text[0] == '\ufeff') throw new SafetyException("Modern inventory requires UTF-8 without a byte-order mark.");
                var quotas = new XmlDictionaryReaderQuotas { MaxDepth = 6, MaxStringContentLength = 512, MaxArrayLength = MaximumFiles, MaxBytesPerRead = 4096, MaxNameTableCharCount = 4096 };
                var document = new XmlDocument { XmlResolver = null }; using (var reader = JsonReaderWriterFactory.CreateJsonReader(bytes, quotas)) document.Load(reader); return document.DocumentElement;
            } catch (DecoderFallbackException ex) { throw new SafetyException("Invalid modern inventory UTF-8: " + ex.Message); }
            catch (XmlException ex) { throw new SafetyException("Malformed modern inventory JSON: " + ex.Message); }
            catch (SerializationException ex) { throw new SafetyException("Malformed modern inventory JSON: " + ex.Message); }
        }
        static void Type(XmlElement element, string type) { if (element == null || element.Attributes.Count != 1 || element.GetAttribute("type") != type) throw new SafetyException("Unexpected modern release metadata type."); }
        static string Text(XmlElement element) { Type(element, "string"); if (element.ChildNodes.Cast<XmlNode>().Any(node => node.NodeType != XmlNodeType.Text)) throw new SafetyException("Invalid modern release string."); return element.InnerText; }
        static long Number(XmlElement element) { Type(element, "number"); long value; if (!Int64.TryParse(element.InnerText, NumberStyles.None, CultureInfo.InvariantCulture, out value) || value.ToString(CultureInfo.InvariantCulture) != element.InnerText) throw new SafetyException("Modern release sizes require canonical nonnegative integers."); return value; }
    }
}
