using System;
using System.Threading;

namespace EasyEject {
    // Held managed-library metadata only. No candidate assembly load or execution;
    // complete content, vendor components and publisher approval remain separate.
    internal static class ModernWindowsBackendIdentity {
        internal static void Require(ModernWindowsPackageContentReview content, CancellationToken cancel) {
            if (content == null) throw new ArgumentNullException("content");
            cancel.ThrowIfCancellationRequested(); content.Revalidate(cancel);
            content.Inspect("EasyEject.Runtime.dll", stream => {
                cancel.ThrowIfCancellationRequested();
                WindowsManagedIdentity.ReadModernBackend(stream).RequireModernBackend(content.Release.Version);
                cancel.ThrowIfCancellationRequested();
            }, cancel);
            content.Revalidate(cancel);
        }
    }
}
