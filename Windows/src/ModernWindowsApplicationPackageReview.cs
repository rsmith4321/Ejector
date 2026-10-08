using System;
using System.Threading;

namespace EasyEject {
    // Composes held content with application/launch declarations. Component code,
    // archive/channel approval and permission to execute/install remain separate.
    internal sealed class ModernWindowsApplicationPackageReview : IDisposable {
        readonly ModernWindowsPackageContentReview content;
        readonly ApprovedModernWindowsComponents components;
        internal readonly string Folder;
        internal readonly ModernWindowsReleaseManifest Release;
        bool disposed;
        internal ModernWindowsApplicationPackageReview(string folder, ApprovedWindowsPublisher publisher,
            IWindowsExecutableTrust executableTrust, IWindowsManifestTrust manifestTrust, CancellationToken cancel, ApprovedModernWindowsComponents components = null) {
            try {
                cancel.ThrowIfCancellationRequested();
                if (components == null) throw new SafetyException("No independently approved modern vendor component catalog is configured. The application folder is not approved.");
                this.components = components;
                content = new ModernWindowsPackageContentReview(folder, publisher, executableTrust, manifestTrust, cancel);
                RequireApplication(cancel);
                Folder = content.Folder; Release = content.Release;
            } catch { Dispose(); throw; }
        }
        void RequireApplication(CancellationToken cancel) {
            cancel.ThrowIfCancellationRequested();
            components.Require(content, cancel);
            ModernWindowsAppHostIdentity.Require(content, cancel);
            ModernWindowsManagedApplicationIdentity.Require(content, cancel);
            ModernWindowsBackendIdentity.Require(content, cancel);
            ModernWindowsRuntimeConfiguration.Require(content, cancel);
            ModernWindowsDependencyConfiguration.Require(content, cancel);
            ModernWindowsComponentVersions.Require(content, cancel);
            content.Revalidate(cancel);
        }
        internal void Revalidate(CancellationToken cancel) {
            if (disposed) throw new ObjectDisposedException("ModernWindowsApplicationPackageReview");
            RequireApplication(cancel);
        }
        public void Dispose() {
            if (disposed) return; disposed = true;
            if (content != null) content.Dispose();
        }
    }
}
