using System;
using System.Drawing;

namespace EasyEject {
    internal static class AppIcons {
        internal static Icon Load() {
            using (var stream = typeof(AppIcons).Assembly.GetManifestResourceStream("EasyEject.AppIcon.ico")) {
                if (stream == null) throw new InvalidOperationException("The Easy Eject icon resource is missing.");
                using (var source = new Icon(stream, 32, 32)) return (Icon)source.Clone();
            }
        }
    }
}
