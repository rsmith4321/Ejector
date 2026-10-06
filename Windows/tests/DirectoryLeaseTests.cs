using System;
using System.IO;

namespace EasyEject {
    public static partial class Tests {
        static void DirectoryLeaseTests() {
            Test("directory lease accepts unchanged identity while ordinary contents and timestamps change", delegate {
                using (var fixture = new Fixture()) {
                    string folder = Path.Combine(fixture.Root, "directory-proof"); Directory.CreateDirectory(folder);
                    using (var held = SafePaths.HoldDirectories(folder, folder)) {
                        held.Validate(); File.WriteAllText(Path.Combine(folder, "new.keep"), "ordinary new content"); Directory.SetLastWriteTimeUtc(folder, DateTime.UtcNow.AddMinutes(-2)); held.Validate();
                        File.Delete(Path.Combine(folder, "new.keep")); held.Validate();
                    }
                }
            });
            Test("directory lease rejects renamed empty directory with same-path replacement", delegate {
                using (var fixture = new Fixture()) {
                    string folder = Path.Combine(fixture.Root, "directory-proof"); Directory.CreateDirectory(folder);
                    using (var held = SafePaths.HoldDirectories(folder)) {
                        Directory.Move(folder, folder + "-moved"); Directory.CreateDirectory(folder); File.WriteAllText(Path.Combine(folder, "foreign.keep"), "replacement content");
                        Throws<SafetyException>(() => held.Validate()); Check(File.ReadAllText(Path.Combine(folder, "foreign.keep")) == "replacement content" && Directory.Exists(folder + "-moved"), "Identity review modified either directory");
                    }
                }
            });
            Test("directory lease pins a nonempty ancestor and releases it for a fresh directory review", delegate {
                using (var fixture = new Fixture()) {
                    string ancestor = Path.Combine(fixture.Root, "directory-parent"), folder = Path.Combine(ancestor, "child"); Directory.CreateDirectory(folder);
                    using (var held = SafePaths.HoldDirectories(folder)) {
                        Throws<IOException>(() => Directory.Move(ancestor, ancestor + "-moved")); held.Validate();
                    }
                    Directory.Move(ancestor, ancestor + "-moved"); Directory.CreateDirectory(folder);
                    using (var fresh = SafePaths.HoldDirectories(folder)) fresh.Validate();
                    Check(Directory.Exists(Path.Combine(ancestor + "-moved", "child")) && Directory.Exists(folder), "Released ancestor or fresh hierarchy was modified");
                }
            });
            Test("directory lease cannot validate after disposal and opening a regular file refuses", delegate {
                using (var fixture = new Fixture()) {
                    string folder = Path.Combine(fixture.Root, "directory-proof"); Directory.CreateDirectory(folder);
                    var held = SafePaths.HoldDirectories(folder); held.Dispose(); held.Dispose(); Throws<ObjectDisposedException>(() => held.Validate());
                    string file = Path.Combine(folder, "regular.keep"); File.WriteAllText(file, "keep regular file"); Throws<SafetyException>(() => SafePaths.HoldDirectories(file));
                    Check(File.ReadAllText(file) == "keep regular file", "Directory admission changed a regular file");
                }
            });
        }
    }
}
