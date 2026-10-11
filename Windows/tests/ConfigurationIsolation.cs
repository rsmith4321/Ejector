using System;
using System.IO;

namespace EasyEject {
    // Every test process uses an owned disposable app-data folder. Child fixture processes inherit
    // the parent's folder through the environment, so separate-process restart tests still share it.
    // The user's live settings, profiles, history and tools under local app-data are never touched.
    public static partial class Tests {
        const string IsolatedAppDataVariable = "EASYEJECT_TEST_ISOLATED_APPDATA";
        static string ownedIsolatedAppData;

        static string FixtureParent() { return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".codex", "EasyEjectFixtures"); }

        static void IsolateConfiguration() {
            string inherited = Environment.GetEnvironmentVariable(IsolatedAppDataVariable);
            string parent = Path.GetFullPath(FixtureParent());
            if (!String.IsNullOrEmpty(inherited)) {
                string full = Path.GetFullPath(inherited);
                if (!SafePaths.Within(full, parent) || full.TrimEnd('\\').Equals(parent.TrimEnd('\\'), StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException("Inherited isolated app-data is outside the owned fixture folder.");
                Directory.CreateDirectory(full); Profiles.IsolatedAppDataForTests = full; return;
            }
            string root = Path.Combine(parent, "appdata-" + Guid.NewGuid().ToString("N").Substring(0, 16));
            Directory.CreateDirectory(root);
            ownedIsolatedAppData = root; Profiles.IsolatedAppDataForTests = root;
            Environment.SetEnvironmentVariable(IsolatedAppDataVariable, root);
            AppDomain.CurrentDomain.ProcessExit += delegate { RemoveIsolatedConfiguration(); };
        }

        static void RemoveIsolatedConfiguration() {
            string root = ownedIsolatedAppData; ownedIsolatedAppData = null;
            if (root == null) return;
            try {
                string parent = Path.GetFullPath(FixtureParent());
                // Only this process's own direct appdata-<id> child of the fixture folder.
                if (!String.Equals(Path.GetDirectoryName(Path.GetFullPath(root)).TrimEnd('\\'), parent.TrimEnd('\\'), StringComparison.OrdinalIgnoreCase) || !Path.GetFileName(root).StartsWith("appdata-", StringComparison.Ordinal)) return;
                if (Directory.Exists(root)) RemoveOwnedTree(root);
            } catch (Exception ex) { Console.WriteLine("Isolated app-data retained for review: " + root + " (" + ex.Message + ")"); }
        }

        static void RemoveOwnedTree(string path) {
            if ((File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0) { Directory.Delete(path, false); return; }
            foreach (var file in Directory.GetFiles(path)) { File.SetAttributes(file, FileAttributes.Normal); File.Delete(file); }
            foreach (var folder in Directory.GetDirectories(path)) RemoveOwnedTree(folder);
            Directory.Delete(path, false);
        }

        static void ConfigurationIsolationTests() {
            Test("test configuration root is isolated from the user's live app-data", delegate {
                string isolated = Profiles.IsolatedAppDataForTests;
                Check(!String.IsNullOrEmpty(isolated), "Test process has no isolated app-data");
                string live = Path.GetFullPath(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData));
                string root = Profiles.Root;
                Check(SafePaths.Within(root, isolated), "Configuration root escaped the isolated folder");
                Check(!SafePaths.Within(root, live), "Configuration root resolves inside live local app-data");
                Check(SafePaths.Within(isolated, Path.GetFullPath(FixtureParent())), "Isolated folder is outside owned fixtures");
            });
        }
    }
}
