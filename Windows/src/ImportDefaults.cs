using System;
using System.IO;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;

namespace EasyEject {
    // Supported media/preview/layout/removal/Trash and manual completion choices. Automatic options are added
    // alongside their engine implementation rather than stored as inert flags.
    [DataContract] public sealed class ImportDefaults {
        [DataMember] public bool? VideosOnly;
        [DataMember] public bool? IncludePreviews; [DataMember] public bool? CleanLayout;
        [DataMember] public bool? AskToEject;
        [DataMember] public bool? DeleteOriginals;
        [DataMember] public bool? RecoverTrash;
        public MediaOptions Snapshot() { return new MediaOptions(VideosOnly ?? true, IncludePreviews ?? true, CleanLayout ?? true, AskToEject ?? false, DeleteOriginals ?? false, RecoverTrash ?? false); }
        public void SetVideosOnly(bool value) { if (!value && Snapshot().VideosOnly) { DeleteOriginals = false; RecoverTrash = false; } VideosOnly = value; }
        public ImportProfile NewProfile(Volume volume, Disk disk) {
            var options = Snapshot();
            return new ImportProfile { VolumeId = volume.Id, VolumeSerial = volume.Serial, DiskInstance = disk.InstanceId,
                RelativeSource = "", Destination = "", UsesImportDefaults = true, VideosOnly = options.VideosOnly, IncludePreviews = options.IncludePreviews, CleanLayout = options.CleanLayout, AskToEject = options.AskToEject, DeleteOriginals = options.DeleteOriginals, RecoverTrash = options.RecoverTrash };
        }
        public static ImportDefaults Load() { return Load(Profiles.Root); }
        internal static ImportDefaults Load(string root) {
            string path = Path.Combine(root, "import-defaults.json");
            using (SafePaths.LockDirectories(root)) {
                if (!File.Exists(path)) return new ImportDefaults();
                using (var stream = SafePaths.ReadFile(path)) {
                    if (stream.Length > 16384) throw new SafetyException("Saved import defaults are too large to read.");
                    var value = (ImportDefaults)new DataContractJsonSerializer(typeof(ImportDefaults)).ReadObject(stream);
                    if (value == null) throw new SafetyException("Saved import defaults are invalid.");
                    return value;
                }
            }
        }
        public static void Save(ImportDefaults value) { Save(value, Profiles.Root); }
        internal static void Save(ImportDefaults value, string root) {
            if (value == null) throw new ArgumentNullException("value");
            Profiles.WriteJson(value, typeof(ImportDefaults), root, "import-defaults.json");
        }
    }
}
