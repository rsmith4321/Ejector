using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace EasyEject {
    public enum StorageGroup { Unknown, Camera, Emulator, External, Internal, Protected }
    internal sealed class FolderEvidence {
        internal bool Camera; internal int EmulatorHits;
        static readonly string[] cameras = { "DCIM", "PRIVATE", "AVCHD", "MP_ROOT", "XDROOT", "BPAV", "NIKON", "CANONMSC", "FUJI", "GOPRO", "SONY" };
        static readonly string[] emulators = { "roms", "retroarch", "bios", ".emulationstation" };
        internal static FolderEvidence Read(string root) {
            using (SafePaths.LockDirectories(root)) {
                var names = new HashSet<string>(Directory.GetDirectories(root).Where(x => (File.GetAttributes(x) & FileAttributes.ReparsePoint) == 0).Select(Path.GetFileName), StringComparer.OrdinalIgnoreCase);
                return new FolderEvidence { Camera = cameras.Any(names.Contains), EmulatorHits = emulators.Count(names.Contains) };
            }
        }
    }
}
