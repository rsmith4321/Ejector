using System;
using System.Threading;

namespace EasyEject {
    // Managed PE/metadata identity only; not apphost/component/dependency/channel approval.
    internal static class ModernWindowsManagedApplicationIdentity {
        internal static void Require(ModernWindowsPackageContentReview content, CancellationToken cancel) {
            if (content == null) throw new ArgumentNullException("content"); cancel.ThrowIfCancellationRequested();
            content.Revalidate(cancel);
            content.Inspect("EasyEject.Modern.dll", stream => { WindowsManagedIdentity.ReadModern(stream).RequireModern(content.Release.Version); cancel.ThrowIfCancellationRequested(); }, cancel);
            content.Revalidate(cancel);
        }
    }
}
