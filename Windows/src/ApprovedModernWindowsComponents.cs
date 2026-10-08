using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Threading;

namespace EasyEject {
    // Supplied only by independently approved application code. Never construct
    // production approval from a downloaded inventory or candidate's own bytes.
    internal sealed class ApprovedModernWindowsComponents {
        static readonly HashSet<string> ApplicationFiles = new HashSet<string>(new[] {
            "EasyEject.Modern.exe", "EasyEject.Modern.dll", "EasyEject.Modern.pri", "EasyEject.Modern.deps.json", "EasyEject.Modern.runtimeconfig.json",
            "EasyEject.Runtime.dll", "EasyEject.Modern.pdb", "EasyEject.Runtime.pdb", "Assets/EasyEject.png", "README.txt", "LICENSE.txt"
        }, StringComparer.OrdinalIgnoreCase);
        readonly Dictionary<string, WindowsReleaseFile> files;
        internal readonly string SourceEvidenceHash, DotNet, WindowsAppSdk, MinimumWindows, CatalogHash;
        internal static bool IsApplicationFile(string name) { return ApplicationFiles.Contains(name); }
        internal ApprovedModernWindowsComponents(string sourceEvidenceHash, string dotnet, string sdk, string minimum, IEnumerable<WindowsReleaseFile> approved) {
            if (!ImportHistory.ValidHash(sourceEvidenceHash) || !RuntimeVersion(dotnet, 10) || !RuntimeVersion(sdk, 2) || minimum != "10.0.19041.0" || approved == null) throw new SafetyException("Modern component approval requires independent source evidence and exact supported runtime identifiers.");
            SourceEvidenceHash = sourceEvidenceHash; DotNet = dotnet; WindowsAppSdk = sdk; MinimumWindows = minimum;
            files = new Dictionary<string, WindowsReleaseFile>(StringComparer.Ordinal); var unique = new HashSet<string>(StringComparer.OrdinalIgnoreCase); var directories = new HashSet<string>(StringComparer.OrdinalIgnoreCase); long total = 0;
            foreach (var file in approved) {
                if (file == null || !ModernWindowsReleaseManifest.ValidPath(file.Name) || IsApplicationFile(file.Name) || !unique.Add(file.Name) || file.Length <= 0 || file.Length > ModernWindowsReleaseManifest.MaximumFile || !ImportHistory.ValidHash(file.Hash) || files.Count >= ModernWindowsReleaseManifest.MaximumFiles) throw new SafetyException("Unsupported, duplicate or application-owned entry in approved vendor components.");
                total += file.Length; if (total > ModernWindowsReleaseManifest.MaximumTotal) throw new SafetyException("Approved component inventory exceeds its supported total.");
                files.Add(file.Name, new WindowsReleaseFile(file.Name, file.Length, file.Hash));
                int slash = file.Name.IndexOf('/'); while (slash >= 0) { directories.Add(file.Name.Substring(0, slash)); slash = file.Name.IndexOf('/', slash + 1); }
            }
            if (unique.Overlaps(directories)) throw new SafetyException("Approved component file and directory identities overlap.");
            if (ModernWindowsReleaseManifest.RequiredFiles.Where(name => !IsApplicationFile(name)).Any(name => !files.ContainsKey(name))) throw new SafetyException("Approved component catalog omits a required runtime component.");
            string canonical = "EasyEject.Modern/components-v1\n" + sourceEvidenceHash + "\n" + dotnet + "\n" + sdk + "\n" + minimum + "\n" + String.Join("\n", files.Values.OrderBy(file => file.Name, StringComparer.Ordinal).Select(file => file.Name + "\t" + file.Length.ToString(CultureInfo.InvariantCulture) + "\t" + file.Hash).ToArray());
            CatalogHash = WindowsSignerEvidence.Hash(Encoding.UTF8.GetBytes(canonical));
        }
        internal void Require(ModernWindowsReleaseManifest release) {
            if (release == null || release.DotNet != DotNet || release.WindowsAppSdk != WindowsAppSdk || release.MinimumWindows != MinimumWindows) throw new SafetyException("Authenticated runtime identifiers differ from independently approved components.");
            var actual = release.Files.Where(file => !IsApplicationFile(file.Name)).ToArray();
            if (actual.Length != files.Count) throw new SafetyException("Authenticated vendor component membership differs from independent approval.");
            foreach (var file in actual) {
                WindowsReleaseFile expected;
                if (!files.TryGetValue(file.Name, out expected) || file.Length != expected.Length || file.Hash != expected.Hash) throw new SafetyException("Authenticated component bytes differ from independent approval: " + file.Name);
            }
        }
        internal void Require(ModernWindowsPackageContentReview content, CancellationToken cancel) {
            if (content == null) throw new ArgumentNullException("content");
            cancel.ThrowIfCancellationRequested(); content.Revalidate(cancel); Require(content.Release); cancel.ThrowIfCancellationRequested(); content.Revalidate(cancel);
        }
        static bool RuntimeVersion(string text, int major) {
            Version value; return Version.TryParse(text, out value) && value.Major == major && value.Minor <= 65535 && value.Build >= 0 && value.Build <= 65535 && value.Revision == -1 && value.ToString(3) == text;
        }
    }
}
