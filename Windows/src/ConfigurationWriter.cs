using System;
using System.IO;
using System.Linq;
using System.Runtime.Serialization.Json;
using Microsoft.Win32.SafeHandles;

namespace EasyEject {
    internal sealed class ProfileWriter : IDisposable {
        readonly string root, path; readonly SafeFileHandle handle; readonly Native.FileInfo identity; bool deletePending;
        internal ProfileWriter(string root) {
            this.root = SafePaths.Normalize(root); path = Path.Combine(this.root, ".profile-write.lock");
            handle = Native.CreateFile(path, 0x10080, 0, IntPtr.Zero, 4, 0x00200000, IntPtr.Zero);
            if (handle.IsInvalid) { handle.Dispose(); throw Native.Error("Saved configuration is being changed. Close the other editor and retry"); }
            try { identity = Native.Info(handle); Validate(this.root); Native.DeleteOnClose(handle); deletePending = true; Validate(this.root); } catch { handle.Dispose(); throw; }
        }
        internal void Validate(string expectedRoot) {
            if (handle.IsClosed || handle.IsInvalid || !root.Equals(SafePaths.Normalize(expectedRoot), StringComparison.OrdinalIgnoreCase)) throw new SafetyException("The configuration writer is closed or belongs to another folder.");
            var info = Native.Info(handle);
            // Marking our owned lock delete-pending can report zero links until its handle closes.
            if ((info.Attributes & 0x1015) != 0 || (!deletePending && info.Links != 1) || info.Links > 1 || info.SizeHigh != 0 || info.SizeLow != 0 || info.VolumeSerial != identity.VolumeSerial || info.IndexHigh != identity.IndexHigh || info.IndexLow != identity.IndexLow || !Native.FinalPath(handle).Equals(path, StringComparison.OrdinalIgnoreCase)) throw new SafetyException("The configuration write lock is not an empty owned private file.");
        }
        public void Dispose() { handle.Dispose(); }
    }
    // Configuration-only replacement. Media publication continues to use exclusive, nonreplace rename.
    internal static class ConfigurationWriter {
        internal static void Write(object value, Type type, string root, string name, Action<string, string> checkpoint, ProfileWriter writer, Action<FileStream> validatePrevious = null) {
            if (value == null) throw new ArgumentNullException("value"); if (type == null) throw new ArgumentNullException("type");
            root = SafePaths.Normalize(root); string final = FinalPath(root, name);
            checkpoint = checkpoint ?? ((stage, path) => {});
            if (writer != null) writer.Validate(root);
            using (var directories = CreateRoot(root)) {
                checkpoint("Before writer gate", root); directories.Validate();
                using (var ownedWriter = writer == null ? Profiles.WriterGate(root) : null) {
                    writer = writer ?? ownedWriter; writer.Validate(root);
                    using (var previous = OpenPrevious(final)) {
                        // Optional compare against a previously reviewed snapshot while
                        // this exact prior file is exclusively held through publication.
                        if (validatePrevious != null) validatePrevious(previous);
                        directories.Validate(); checkpoint("Before temporary creation", root); directories.Validate(); writer.Validate(root);
                        string temporary = SafePaths.Normalize(Path.Combine(root, Guid.NewGuid().ToString("N") + ".tmp"));
                        using (var stream = SafePaths.NewFile(temporary)) {
                            var identity = Native.Info(stream.SafeFileHandle); bool published = false;
                            try {
                                CheckTemporary(stream, identity, temporary, directories);
                                new DataContractJsonSerializer(type).WriteObject(stream, value); stream.Flush(true);
                                checkpoint("Before publication", temporary); CheckTemporary(stream, identity, temporary, directories); writer.Validate(root);
                                if (previous == null) Native.Rename(stream.SafeFileHandle, final);
                                else Native.ReplaceConfiguration(stream.SafeFileHandle, previous, final);
                                published = true;
                                checkpoint("After publication", final); CheckTemporary(stream, identity, final, directories); stream.Flush(true);
                            } catch (Exception ex) {
                                if (published) throw new SafetyException("Configuration was published at " + final + ", but its final check/flush could not finish. Refresh the saved choices before retrying. " + ex.Message);
                                throw;
                            } finally {
                                if (!published) {
                                    try { CheckTemporary(stream, identity, temporary, directories); Native.DeleteOnClose(stream.SafeFileHandle); }
                                    catch (Exception ex) { throw new SafetyException("Configuration was not published. Its private temporary was retained at " + temporary + ": " + ex.Message); }
                                }
                            }
                        }
                    }
                }
            }
        }
        static string FinalPath(string root, string name) {
            if (String.IsNullOrWhiteSpace(name) || name.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 || Path.GetFileName(name) != name || !name.EndsWith(".json", StringComparison.OrdinalIgnoreCase) || name.Length <= 5)
                throw new SafetyException("Choose an ordinary private JSON configuration filename.");
            string first = name.Split('.')[0].ToUpperInvariant();
            if (new[] { "CON", "PRN", "AUX", "NUL", "CONIN$", "CONOUT$" }.Contains(first) || first.Length == 4 && (first.StartsWith("COM", StringComparison.Ordinal) || first.StartsWith("LPT", StringComparison.Ordinal)) && first[3] >= '1' && first[3] <= '9')
                throw new SafetyException("Device filenames cannot be used for configuration.");
            string path = SafePaths.Normalize(Path.Combine(root, name));
            if (!SafePaths.Within(path, root) || Path.GetFileName(path) != name) throw new SafetyException("Configuration filename escaped its folder.");
            return path;
        }
        internal static SafePaths.DirectoryLease CreateRoot(string root) {
            string ancestor = root;
            for (;;) {
                try { if ((File.GetAttributes(ancestor) & FileAttributes.Directory) == 0) throw new SafetyException("Configuration storage is not a directory."); break; }
                catch (FileNotFoundException) { ancestor = Parent(ancestor); }
                catch (DirectoryNotFoundException) { ancestor = Parent(ancestor); }
            }
            using (var parent = SafePaths.HoldDirectories(ancestor)) {
                parent.Validate(); Directory.CreateDirectory(root); parent.Validate();
                var result = SafePaths.HoldDirectories(root); try { result.Validate(); return result; } catch { result.Dispose(); throw; }
            }
        }
        static string Parent(string path) { var parent = Directory.GetParent(path); if (parent == null) throw new SafetyException("Configuration drive is unavailable."); return parent.FullName; }
        internal static FileStream OpenPrevious(string path) {
            try { File.GetAttributes(path); }
            catch (FileNotFoundException) { return null; }
            // The root is already pinned. A missing ancestor is an identity failure, not absent settings.
            var handle = Native.CreateFile(path, 0x80010000, 0, IntPtr.Zero, 3, 0x00300000, IntPtr.Zero);
            if (handle.IsInvalid) { handle.Dispose(); throw Native.Error("Cannot exclusively hold existing configuration. Close its other reader/editor and retry"); }
            try {
                var info = Native.Info(handle);
                if ((info.Attributes & 0x1015) != 0 || info.Links != 1 || !Native.FinalPath(handle).Equals(path, StringComparison.OrdinalIgnoreCase)) throw new SafetyException("Existing configuration is protected, redirected or has another alias. No protection is changed.");
                return new FileStream(handle, FileAccess.Read, 4096, false);
            } catch { handle.Dispose(); throw; }
        }
        static void CheckTemporary(FileStream stream, Native.FileInfo identity, string path, SafePaths.DirectoryLease directories) {
            directories.Validate(); var current = Native.Info(stream.SafeFileHandle);
            if ((current.Attributes & 0x1015) != 0 || current.Links != 1 || current.VolumeSerial != identity.VolumeSerial || current.IndexHigh != identity.IndexHigh || current.IndexLow != identity.IndexLow || !Native.FinalPath(stream.SafeFileHandle).Equals(path, StringComparison.OrdinalIgnoreCase))
                throw new SafetyException("The owned configuration temporary is protected, redirected or has another alias. No protection is changed.");
        }
    }
}
