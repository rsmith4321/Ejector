using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Text;
using System.Threading;
using System.Xml;

namespace EasyEject {
    // Read-only declarations for the reviewed flattened self-contained layout.
    // Not component/code identity, NuGet authenticity or permission to execute.
    internal static class ModernWindowsDependencyConfiguration {
        internal const string Name = "EasyEject.Modern.deps.json";
        internal const int MaximumBytes = 524288;
        const string Framework = ".NETCoreApp,Version=v10.0", Target = Framework + "/win-x64";
        internal static void Require(ModernWindowsPackageContentReview content, CancellationToken cancel) {
            if (content == null) throw new ArgumentNullException("content");
            cancel.ThrowIfCancellationRequested(); content.Revalidate(cancel);
            content.Inspect(Name, stream => {
                if (stream.Length <= 0 || stream.Length > MaximumBytes) Fail("Missing or excessive dependency configuration.");
                stream.Position = 0; byte[] bytes = new byte[(int)stream.Length]; int done = 0;
                while (done < bytes.Length) { cancel.ThrowIfCancellationRequested(); int read = stream.Read(bytes, done, bytes.Length - done); if (read == 0) Fail("Incomplete dependency configuration."); done += read; }
                Validate(bytes, content.Release); cancel.ThrowIfCancellationRequested();
            }, cancel);
            content.Revalidate(cancel);
        }
        internal static void Validate(byte[] supplied, ModernWindowsReleaseManifest release) {
            if (release == null) Fail("Dependency configuration requires authenticated release metadata.");
            var root = Exact(Read(supplied), "runtimeTarget", "compilationOptions", "targets", "libraries", "runtimes");
            var runtime = Exact(root["runtimeTarget"], "name", "signature");
            if (Text(runtime["name"]) != Target || Text(runtime["signature"]) != "") Fail("Unsupported dependency runtime target.");
            Exact(root["compilationOptions"]);
            var targets = Exact(root["targets"], Framework, Target); Exact(targets[Framework]);
            var graph = Object(targets[Target]); var libraries = Object(root["libraries"]);
            if (graph.Count < 5 || graph.Count > 128 || graph.Count != libraries.Count || graph.Keys.Any(key => !libraries.ContainsKey(key))) Fail("Dependency library/target sets differ or exceed policy.");
            var fallback = Exact(root["runtimes"], "win-x64"); Type(fallback["win-x64"], "array");
            var rid = fallback["win-x64"].ChildNodes.Cast<XmlNode>().Select(node => node as XmlElement).ToArray();
            if (rid.Length != 3 || rid.Any(item => item == null || item.LocalName != "item" || item.NamespaceURI != "") || !rid.Select(Text).SequenceEqual(new[] { "win", "any", "base" })) Fail("Unsupported dependency RID fallback.");
            string version = PackageVersion(release.Version), main = "EasyEject.Modern/" + version, backend = "EasyEject.Runtime/" + version;
            string core = "runtimepack.Microsoft.NETCore.App.Runtime.win-x64/" + release.DotNet, desktop = "runtimepack.Microsoft.WindowsDesktop.App.Runtime.win-x64/" + release.DotNet, sdk = "Microsoft.WindowsAppSDK/" + release.WindowsAppSdk;
            foreach (string required in new[] { main, backend, core, desktop, sdk }) if (!graph.ContainsKey(required)) Fail("Required dependency identity differs from the authenticated release.");
            var identities = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var assets = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            var inventory = new HashSet<string>(release.Files.Select(file => file.Name), StringComparer.Ordinal);
            foreach (var library in graph) {
                string[] id = library.Key.Split('/');
                if (id.Length != 2 || !LibraryName(id[0]) || !CanonicalVersion(id[1]) || !identities.Add(id[0])) Fail("Malformed, unstable or aliased dependency identity.");
                var record = Object(libraries[library.Key]);
                string kind = record.ContainsKey("type") ? Text(record["type"]) : "";
                bool project = library.Key == main || library.Key == backend;
                bool runtimepack = id[0].StartsWith("runtimepack.", StringComparison.Ordinal);
                bool projection = id[0] == "Microsoft.Web.WebView2.Core.Projection";
                if (kind != (project ? "project" : runtimepack ? "runtimepack" : projection ? "reference" : "package")) Fail("Dependency library type differs from its role.");
                Exact(libraries[library.Key], kind == "package" ? new[] { "type", "serviceable", "sha512", "path", "hashPath" } : new[] { "type", "serviceable", "sha512" });
                Type(record["serviceable"], "boolean");
                if (record["serviceable"].InnerText != (kind == "package" ? "true" : "false")) Fail("Unsupported dependency servicing declaration.");
                string hash = Text(record["sha512"]);
                if (kind == "package") {
                    string lower = id[0].ToLowerInvariant();
                    if (!Hash512(hash) || Text(record["path"]) != lower + "/" + id[1] || Text(record["hashPath"]) != lower + "." + id[1] + ".nupkg.sha512") Fail("Unsupported dependency package metadata.");
                } else if (hash != "") Fail("Project/runtime-pack hash declaration is unsupported.");
                var entry = Object(library.Value);
                if (entry.Count == 0 || entry.Keys.Any(key => key != "dependencies" && key != "runtime" && key != "native")) Fail("Unreviewed dependency target fields.");
                if (entry.ContainsKey("dependencies")) foreach (var edge in Object(entry["dependencies"])) {
                    if (!LibraryName(edge.Key) || !CanonicalVersion(Text(edge.Value)) || !graph.ContainsKey(edge.Key + "/" + Text(edge.Value))) Fail("Dangling or aliased dependency edge.");
                }
                foreach (string role in new[] { "runtime", "native" }) if (entry.ContainsKey(role)) foreach (var asset in Object(entry[role])) {
                    string path = asset.Key; string basename = path.Substring(path.LastIndexOf('/') + 1);
                    if (!AssetPath(path) || !inventory.Contains(basename) || assets.ContainsKey(basename) || assets.Count >= 2048 || (role == "runtime" ? !basename.EndsWith(".dll", StringComparison.Ordinal) : !basename.EndsWith(".dll", StringComparison.Ordinal) && !basename.EndsWith(".exe", StringComparison.Ordinal))) Fail("Dependency asset has no unique reviewed flattened destination.");
                    var attributes = Object(asset.Value);
                    if (attributes.Keys.Any(key => key != "fileVersion" && (key != "assemblyVersion" || role != "runtime")) || attributes.Values.Any(value => !ComponentVersion(Text(value)))) Fail("Unsupported dependency asset version metadata.");
                    assets.Add(basename, library.Key + ":" + role);
                }
            }
            var application = Exact(graph[main], "dependencies", "runtime"); var dependencies = Object(application["dependencies"]);
            foreach (string required in new[] { backend, core, desktop, sdk }) {
                string[] id = required.Split('/'); if (!dependencies.ContainsKey(id[0]) || Text(dependencies[id[0]]) != id[1]) Fail("Application omits its required direct dependency.");
            }
            const string webView = "Microsoft.Web.WebView2", projectionName = webView + ".Core.Projection";
            string webKey = graph.Keys.FirstOrDefault(key => key.StartsWith(webView + "/", StringComparison.Ordinal));
            string projectionKey = graph.Keys.FirstOrDefault(key => key.StartsWith(projectionName + "/", StringComparison.Ordinal));
            if (webKey != null || projectionKey != null) {
                if (webKey == null || projectionKey == null || webKey.Split('/')[1] != projectionKey.Split('/')[1] || !dependencies.ContainsKey(projectionName) || Text(dependencies[projectionName]) != projectionKey.Split('/')[1]) Fail("WebView package/projection identities or direct dependency differ.");
                var projection = Exact(graph[projectionKey], "runtime"); var projectionRuntime = Exact(projection["runtime"], projectionName + ".dll");
                var metadata = Exact(projectionRuntime[projectionName + ".dll"], "assemblyVersion", "fileVersion");
                string declaredVersion = projectionKey.Split('/')[1]; string fourPart = declaredVersion.Split('.').Length == 3 ? declaredVersion + ".0" : declaredVersion;
                if (Text(metadata["assemblyVersion"]) != fourPart || Text(metadata["fileVersion"]) != fourPart) Fail("WebView projection versions differ from its package declaration.");
                RequireAsset(assets, projectionName + ".dll", projectionKey, "runtime"); RequireAsset(assets, "WebView2Loader.dll", webKey, "native");
            }
            var reachable = new HashSet<string>(StringComparer.Ordinal); var pending = new Queue<string>(); pending.Enqueue(main);
            while (pending.Count != 0) {
                string current = pending.Dequeue(); if (!reachable.Add(current)) continue; var entry = Object(graph[current]);
                if (entry.ContainsKey("dependencies")) foreach (var edge in Object(entry["dependencies"])) pending.Enqueue(edge.Key + "/" + Text(edge.Value));
            }
            if (reachable.Count != graph.Count) Fail("Dependency graph includes libraries outside the application closure.");
            RequireAsset(assets, "EasyEject.Modern.dll", main, "runtime"); RequireAsset(assets, "EasyEject.Runtime.dll", backend, "runtime");
            RequireAsset(assets, "System.Private.CoreLib.dll", core, "runtime"); RequireAsset(assets, "System.Windows.Forms.dll", desktop, "runtime");
            foreach (string file in new[] { "coreclr.dll", "hostfxr.dll", "hostpolicy.dll" }) RequireAsset(assets, file, core, "native");
            foreach (string project in new[] { main, backend }) {
                var entry = Exact(graph[project], project == main ? new[] { "dependencies", "runtime" } : new[] { "runtime" });
                var declared = Exact(entry["runtime"], project == main ? "EasyEject.Modern.dll" : "EasyEject.Runtime.dll");
                if (project == main) Exact(declared["EasyEject.Modern.dll"]);
                else { var metadata = Exact(declared["EasyEject.Runtime.dll"], "assemblyVersion", "fileVersion"); if (Text(metadata["assemblyVersion"]) != release.Version || Text(metadata["fileVersion"]) != release.Version) Fail("Backend declared assembly/file version differs from release."); }
            }
        }
        static void RequireAsset(Dictionary<string, string> assets, string name, string library, string role) { string owner; if (!assets.TryGetValue(name, out owner) || owner != library + ":" + role) Fail("Required dependency asset has a substituted owner or role."); }
        internal static string PackageVersion(string version) { return version.EndsWith(".0", StringComparison.Ordinal) ? version.Substring(0, version.Length - 2) : version; }
        static bool CanonicalVersion(string text) { Version v; return Version.TryParse(text, out v) && v.Build >= 0 && v.Major <= 65535 && v.Minor <= 65535 && v.Build <= 65535 && v.Revision <= 65535 && v.ToString(v.Revision < 0 ? 3 : 4) == text && v.Revision != 0; }
        static bool ComponentVersion(string text) { return text == "0.0.0.0" || WindowsReleaseManifest.StableVersion(text); }
        static bool LibraryName(string name) { return name.Length > 0 && name.Length <= 120 && name[0] != '.' && name[name.Length - 1] != '.' && name.All(c => c >= 'a' && c <= 'z' || c >= 'A' && c <= 'Z' || c >= '0' && c <= '9' || c == '.' || c == '_' || c == '-'); }
        static bool AssetPath(string path) {
            if (path.Length > 200 || !ModernWindowsReleaseManifest.ValidPath(path)) return false;
            string[] parts = path.Split('/');
            return parts.Length == 1 || parts.Length == 3 && parts[0] == "lib" && parts[1].StartsWith("net", StringComparison.Ordinal) || parts.Length == 4 && parts[0] == "runtimes" && parts[1] == "win-x64" && parts[2] == "native";
        }
        static bool Hash512(string text) { try { if (!text.StartsWith("sha512-", StringComparison.Ordinal)) return false; byte[] bytes = Convert.FromBase64String(text.Substring(7)); return bytes.Length == 64 && "sha512-" + Convert.ToBase64String(bytes) == text; } catch (FormatException) { return false; } }
        static XmlElement Read(byte[] supplied) {
            if (supplied == null || supplied.Length == 0 || supplied.Length > MaximumBytes) { Fail("Missing or excessive dependency configuration."); }
            byte[] bytes = (byte[])supplied.Clone();
            try {
                string text = new UTF8Encoding(false, true).GetString(bytes); if (text[0] == '\ufeff') Fail("Dependency JSON requires UTF-8 without BOM."); JsonSyntax.Validate(text);
                var quotas = new XmlDictionaryReaderQuotas { MaxDepth = 10, MaxStringContentLength = 512, MaxArrayLength = 2048, MaxBytesPerRead = 4096, MaxNameTableCharCount = 16384 };
                var document = new XmlDocument { XmlResolver = null }; using (var reader = JsonReaderWriterFactory.CreateJsonReader(bytes, quotas)) document.Load(reader); return document.DocumentElement;
            } catch (DecoderFallbackException ex) { throw new SafetyException("Invalid dependency UTF-8: " + ex.Message); }
            catch (XmlException ex) { throw new SafetyException("Malformed dependency JSON: " + ex.Message); }
            catch (SerializationException ex) { throw new SafetyException("Malformed dependency JSON: " + ex.Message); }
        }
        static Dictionary<string, XmlElement> Object(XmlElement element) {
            Type(element, "object"); var fields = new Dictionary<string, XmlElement>(StringComparer.Ordinal); var aliases = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (XmlNode node in element.ChildNodes) {
                var child = node as XmlElement; if (child == null) Fail("Malformed dependency object.");
                string name = child.NamespaceURI == "item" && child.LocalName == "item" ? child.GetAttribute("item") : child.NamespaceURI == "" ? child.LocalName : null;
                if (String.IsNullOrEmpty(name) || name.Length > 200 || !aliases.Add(name) || fields.Count >= 2048) Fail("Duplicate, aliased or excessive dependency fields.");
                fields.Add(name, child);
            }
            return fields;
        }
        static Dictionary<string, XmlElement> Exact(XmlElement element, params string[] names) { var fields = Object(element); if (fields.Count != names.Length || names.Any(name => !fields.ContainsKey(name))) Fail("Unsupported dependency object schema."); return fields; }
        static void Type(XmlElement element, string type) {
            if (element == null || element.GetAttribute("type") != type || element.Attributes.Cast<XmlAttribute>().Any(attribute => !(attribute.Name == "type" && attribute.NamespaceURI == "") && !(attribute.Name == "item" && attribute.NamespaceURI == "" && element.NamespaceURI == "item" && element.LocalName == "item") && attribute.NamespaceURI != "http://www.w3.org/2000/xmlns/")) Fail("Unexpected dependency JSON type/annotation.");
            if (type != "object" && type != "array" && element.ChildNodes.Cast<XmlNode>().Any(node => node.NodeType != XmlNodeType.Text)) Fail("Malformed dependency primitive.");
        }
        static string Text(XmlElement element) { Type(element, "string"); return element.InnerText; }
        static void Fail(string reason) { throw new SafetyException(reason); }
    }
}
