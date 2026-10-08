using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Threading;

namespace EasyEject {
    // Binds existing dependency version declarations to held actual files.
    // This is not vendor/catalog authenticity, code identity or launch authority.
    internal static class ModernWindowsComponentVersions {
        [DllImport("version.dll", CharSet = CharSet.Unicode, ExactSpelling = true, SetLastError = true)]
        static extern uint GetFileVersionInfoSizeW(string path, out uint ignored);
        internal static void Require(ModernWindowsPackageContentReview content, CancellationToken cancel) {
            if (content == null) throw new ArgumentNullException("content");
            cancel.ThrowIfCancellationRequested(); content.Revalidate(cancel);
            ModernWindowsDependencyConfiguration.AssetVersions[] assets = null;
            content.Inspect(ModernWindowsDependencyConfiguration.Name, stream => {
                if (stream.Length <= 0 || stream.Length > ModernWindowsDependencyConfiguration.MaximumBytes) throw new SafetyException("Unsupported dependency metadata size.");
                stream.Position = 0; byte[] bytes = new byte[(int)stream.Length]; int done = 0;
                while (done < bytes.Length) { cancel.ThrowIfCancellationRequested(); int read = stream.Read(bytes, done, bytes.Length - done); if (read == 0) throw new SafetyException("Incomplete dependency metadata."); done += read; }
                assets = ModernWindowsDependencyConfiguration.Validate(bytes, content.Release);
            }, cancel);
            var declared = assets.ToDictionary(asset => asset.Name, StringComparer.Ordinal);
            content.InspectMany(assets.Select(asset => asset.Name), (name, stream) => {
                string path = SafePaths.Normalize(Path.Combine(content.Folder, name));
                Require(path, stream, declared[name], cancel);
            }, cancel);
            content.Revalidate(cancel);
        }
        internal static void Require(string path, FileStream held, ModernWindowsDependencyConfiguration.AssetVersions versions, CancellationToken cancel) {
            if (held == null || versions == null) throw new ArgumentNullException("held");
            cancel.ThrowIfCancellationRequested(); string canonical = SafePaths.Normalize(path);
            var before = Native.Info(held.SafeFileHandle); Bind(canonical, held, before);
            if (versions.FileVersion != null) {
                uint ignored; uint size = GetFileVersionInfoSizeW(canonical, out ignored);
                // SDK declarations also use zero for absent version resources.
                // Zero absence is not evidence of a component's code identity.
                if (size > 1048576 || size == 0 && versions.FileVersion != "0.0.0.0") throw new SafetyException("Component has no supported bounded version resource: " + versions.Name);
                var info = FileVersionInfo.GetVersionInfo(canonical);
                string actual = String.Format(CultureInfo.InvariantCulture, "{0}.{1}.{2}.{3}", info.FileMajorPart, info.FileMinorPart, info.FileBuildPart, info.FilePrivatePart);
                if (actual != versions.FileVersion) throw new SafetyException("Actual component file version differs from dependency metadata: " + versions.Name);
            }
            cancel.ThrowIfCancellationRequested(); Bind(canonical, held, before);
            if (versions.AssemblyVersion != null) {
                AssemblyName identity;
                try { identity = AssemblyName.GetAssemblyName(canonical); }
                catch (BadImageFormatException ex) { throw new SafetyException("Component has no supported assembly manifest: " + versions.Name + ". " + ex.Message); }
                if (identity.Version == null || identity.Version.ToString(4) != versions.AssemblyVersion) throw new SafetyException("Actual component assembly version differs from dependency metadata: " + versions.Name);
            }
            cancel.ThrowIfCancellationRequested(); Bind(canonical, held, before);
        }
        static void Bind(string path, FileStream held, Native.FileInfo before) {
            var current = Native.Info(held.SafeFileHandle);
            if (!VerifiedImporter.SameStamp(before, current) || current.Links != 1 || (current.Attributes & 0x1410) != 0 || !Native.FinalPath(held.SafeFileHandle).Equals(path, StringComparison.OrdinalIgnoreCase)) throw new SafetyException("Component metadata path differs from its exact held file.");
        }
    }
}
