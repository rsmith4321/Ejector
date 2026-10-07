using System;
using System.IO;
using System.Linq;

namespace EasyEject {
    public static partial class Tests {
        static void CatalogReplacement(string stage, int count, bool corrupt = false) {
            using (var f = new Fixture()) {
                for (int i = 0; i < count; i++) {
                    var profile = OfflineProfile(); profile.VolumeId += "-" + i; profile.Name = "Original " + i;
                    Profiles.Save(profile, f.Destination);
                }
                var names = Directory.GetFiles(f.Destination).Select(Path.GetFileName).OrderBy(n => n, StringComparer.Ordinal).ToArray();
                if (corrupt) File.WriteAllText(Path.Combine(f.Destination, names[0]), "malformed generated profile");
                var bytes = names.Select(n => File.ReadAllBytes(Path.Combine(f.Destination, n))).ToArray(); bool changed = false;
                Throws<SafetyException>(() => Profiles.Catalog(f.Destination, value => {
                    if (!changed && value == stage) {
                        ReplaceFixtureDirectory(f, f.Destination); changed = true;
                        for (int i = 0; i < count; i++) File.WriteAllBytes(Path.Combine(f.Destination, names[i]), bytes[i]);
                    }
                }));
                Check(changed && File.ReadAllText(Path.Combine(f.Destination, "foreign.keep")) == "retain replacement", "Replacement was missed or altered");
                for (int i = 0; i < count; i++) {
                    Check(File.ReadAllBytes(Path.Combine(f.Destination + "-moved", names[i])).SequenceEqual(bytes[i]) && File.ReadAllBytes(Path.Combine(f.Destination, names[i])).SequenceEqual(bytes[i]), "Read changed old or replacement profile bytes");
                }
                var fresh = Profiles.Catalog(f.Destination);
                Check(fresh.Length == count && fresh.Count(p => p.CanEdit) == count - (corrupt ? 1 : 0) && Directory.GetFileSystemEntries(f.Destination).Length == count + 1, "Failed read retained leases or damaged replacement catalog");
                foreach (var entry in fresh.Where(p => p.CanEdit)) {
                    var draft = entry.Profile; draft.Name = "Fresh explicit edit"; Profiles.SaveOffline(entry, draft, f.Destination);
                }
                foreach (var entry in Profiles.Catalog(f.Destination).Where(p => p.CanEdit)) Profiles.ForgetOffline(entry, f.Destination);
                Check(File.ReadAllText(Path.Combine(f.Destination, "foreign.keep")) == "retain replacement" && Directory.GetFileSystemEntries(f.Destination).Length == (corrupt ? 2 : 1), "Fresh edit/forget failed or changed unrelated replacement data");
                for (int i = 0; i < count; i++) Check(File.ReadAllBytes(Path.Combine(f.Destination + "-moved", names[i])).SequenceEqual(bytes[i]), "Fresh replacement edit changed original catalog bytes");
            }
        }
        static void ProfileCatalogOwnershipTests() {
            Test("profile catalog refuses empty-root replacement before enumeration", () => CatalogReplacement("Before catalog enumeration", 0));
            Test("profile catalog refuses replacement after enumerating profile names", () => CatalogReplacement("After catalog enumeration", 1));
            Test("profile catalog refuses replacement before opening a profile", () => CatalogReplacement("Before profile read", 1));
            Test("profile catalog refuses replacement between two profile reads", () => CatalogReplacement("After profile read", 2));
            Test("profile catalog does not downgrade a changed root to one malformed entry", () => CatalogReplacement("After profile read", 2, true));
            Test("profile catalog refuses replacement before returning its snapshot", () => CatalogReplacement("Before catalog return", 1));
        }
    }
}
