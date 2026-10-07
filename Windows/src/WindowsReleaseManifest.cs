using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Text;
using System.Xml;

namespace EasyEject {
    internal sealed class WindowsReleaseFile {
        internal readonly string Name, Hash; internal readonly long Length;
        internal WindowsReleaseFile(string name, long length, string hash) { Name = name; Length = length; Hash = hash; }
    }
    internal sealed class WindowsReleaseManifest {
        internal const string Application = "EasyEject.Windows", Platform = "windows-x64", Executable = "EasyEject.Windows.exe", Configuration = "EasyEject.Windows.exe.config", ManifestName = "windows-release.json", SignatureName = "windows-release.json.p7s";
        internal static readonly ReadOnlyCollection<string> PayloadNames = Array.AsReadOnly(new[] { Executable, Configuration, "README.txt", "LICENSE.txt" });
        internal readonly string Version, SourceRevision;
        internal readonly ReadOnlyCollection<WindowsReleaseFile> Files;
        WindowsReleaseManifest(string version, string source, List<WindowsReleaseFile> files) { Version = version; SourceRevision = source; Files = files.AsReadOnly(); }
        internal static long MaximumLength(string name) {
            if (name == Executable) return 1024L * 1024 * 1024;
            if (name == Configuration) return 16384;
            if (name == ManifestName) return WindowsManifestTrust.MaximumManifest;
            if (name == SignatureName) return WindowsManifestTrust.MaximumSignature;
            if (name == "README.txt" || name == "LICENSE.txt") return 65536;
            throw new SafetyException("Unsupported Windows release filename.");
        }
        internal static bool StableVersion(string text) {
            System.Version version;
            return text != null && System.Version.TryParse(text, out version) && version.Build >= 0 && version.Revision >= 0 && version.Major <= 65535 && version.Minor <= 65535 && version.Build <= 65535 && version.Revision <= 65535 && version.ToString(4) == text && text != "0.0.0.0";
        }
        internal static WindowsReleaseManifest Parse(WindowsAuthenticatedManifest authenticated) {
            if (authenticated == null) throw new SafetyException("Release metadata must come from an authenticated Windows manifest snapshot.");
            byte[] bytes = authenticated.CopyBytes();
            try {
                var root = Fields(ReadJson(bytes), new[] { "format", "application", "platform", "version", "sourceRevision", "files" });
                if (Number(root["format"]) != 1 || Text(root["application"]) != Application || Text(root["platform"]) != Platform) throw new SafetyException("This is not a supported Easy Eject Windows x64 release manifest.");
                string version = Text(root["version"]), source = Text(root["sourceRevision"]);
                if (!StableVersion(version) || source.Length != 40 || source.Any(c => !(c >= '0' && c <= '9') && !(c >= 'a' && c <= 'f'))) throw new SafetyException("The Windows release version or source revision is invalid.");
                var array = root["files"]; Type(array, "array"); var files = new List<WindowsReleaseFile>(); var names = new HashSet<string>(StringComparer.Ordinal);
                foreach (XmlNode node in array.ChildNodes) {
                    var entry = node as XmlElement; if (entry == null || entry.LocalName != "item") throw new SafetyException("Malformed Windows release file inventory.");
                    var item = Fields(entry, new[] { "name", "length", "sha256" }); string name = Text(item["name"]), hash = Text(item["sha256"]); long length = Number(item["length"]);
                    if (!PayloadNames.Contains(name) || !names.Add(name) || length <= 0 || length > MaximumLength(name) || !ImportHistory.ValidHash(hash)) throw new SafetyException("The Windows release has unsupported, duplicate or invalid payload files.");
                    files.Add(new WindowsReleaseFile(name, length, hash));
                }
                if (files.Count != PayloadNames.Count) throw new SafetyException("The Windows release payload inventory is incomplete.");
                return new WindowsReleaseManifest(version, source, files.OrderBy(f => f.Name, StringComparer.Ordinal).ToList());
            } catch (DecoderFallbackException ex) { throw new SafetyException("The Windows release manifest is not valid UTF-8: " + ex.Message); }
            catch (XmlException ex) { throw new SafetyException("The Windows release manifest is malformed: " + ex.Message); }
            catch (SerializationException ex) { throw new SafetyException("The Windows release manifest is malformed: " + ex.Message); }
        }
        internal static XmlElement ReadJson(byte[] bytes) {
            if (bytes == null || bytes.Length == 0 || bytes.Length > WindowsManifestTrust.MaximumManifest) throw new SafetyException("Signed Windows JSON metadata exceeds its supported bound.");
            try {
                string text = new UTF8Encoding(false, true).GetString(bytes);
                JsonSyntax.Validate(text); RequireJsonDocument(text);
                if (bytes.Length >= 3 && bytes[0] == 0xef && bytes[1] == 0xbb && bytes[2] == 0xbf) throw new SafetyException("Signed Windows JSON metadata must use UTF-8 without a byte-order mark.");
                var quotas = new XmlDictionaryReaderQuotas { MaxDepth = 6, MaxStringContentLength = 65536, MaxArrayLength = 64, MaxBytesPerRead = 4096, MaxNameTableCharCount = 4096 };
                var document = new XmlDocument { XmlResolver = null }; using (var reader = JsonReaderWriterFactory.CreateJsonReader(bytes, quotas)) document.Load(reader); return document.DocumentElement;
            } catch (DecoderFallbackException ex) { throw new SafetyException("Signed Windows JSON metadata is not valid UTF-8: " + ex.Message); }
            catch (XmlException ex) { throw new SafetyException("Signed Windows JSON metadata is malformed: " + ex.Message); }
            catch (SerializationException ex) { throw new SafetyException("Signed Windows JSON metadata is malformed: " + ex.Message); }
        }
        static void RequireJsonDocument(string text) {
            var stack = new Stack<char>(); bool quoted = false, escape = false, started = false;
            for (int i = 0; i < text.Length; i++) {
                char c = text[i];
                if (quoted) {
                    if (c < 32) throw new SafetyException("Raw control characters are unsupported in Windows release JSON.");
                    if (escape) { escape = false; continue; } if (c == '\\') { escape = true; continue; } if (c == '"') quoted = false; continue;
                }
                if (!started) { if (JsonSpace(c)) continue; if (c != '{') throw new SafetyException("The Windows release manifest must be one UTF-8 JSON object."); started = true; }
                if (c == '"') { quoted = true; continue; }
                if (c == '{' || c == '[') { stack.Push(c); if (stack.Count > 6) throw new SafetyException("The Windows release JSON nesting exceeds its supported bound."); }
                else if (c == '}' || c == ']') {
                    if (stack.Count == 0 || stack.Pop() != (c == '}' ? '{' : '[')) throw new SafetyException("Malformed Windows release JSON framing.");
                    if (stack.Count == 0) { for (int j = i + 1; j < text.Length; j++) if (!JsonSpace(text[j])) throw new SafetyException("The Windows release manifest has trailing data."); return; }
                } else if (c < 32 && !JsonSpace(c)) throw new SafetyException("The Windows release manifest is not supported UTF-8 JSON.");
            }
            throw new SafetyException("The Windows release JSON object is incomplete.");
        }
        static bool JsonSpace(char c) { return c == ' ' || c == '\t' || c == '\r' || c == '\n'; }
        internal static Dictionary<string, XmlElement> Fields(XmlElement element, string[] expected) {
            Type(element, "object"); var result = new Dictionary<string, XmlElement>(StringComparer.Ordinal);
            foreach (XmlNode node in element.ChildNodes) {
                var field = node as XmlElement;
                if (field == null || field.NamespaceURI.Length != 0 || !expected.Contains(field.LocalName) || result.ContainsKey(field.LocalName)) throw new SafetyException("The Windows release contains unknown or duplicate metadata fields.");
                result.Add(field.LocalName, field);
            }
            if (result.Count != expected.Length) throw new SafetyException("The Windows release metadata is incomplete."); return result;
        }
        static void Type(XmlElement element, string type) { if (element == null || element.Attributes.Count != 1 || element.GetAttribute("type") != type) throw new SafetyException("The Windows release contains unexpected metadata types or annotations."); }
        internal static string Text(XmlElement element) { Type(element, "string"); if (element.ChildNodes.Cast<XmlNode>().Any(n => n.NodeType != XmlNodeType.Text)) throw new SafetyException("Invalid Windows release string."); return element.InnerText; }
        internal static long Number(XmlElement element) { Type(element, "number"); string text = element.InnerText; long value; if (!Int64.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out value) || value.ToString(CultureInfo.InvariantCulture) != text) throw new SafetyException("The Windows release requires canonical nonnegative integer metadata."); return value; }
    }
}
