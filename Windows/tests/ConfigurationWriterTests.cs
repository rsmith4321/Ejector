using System;
using System.IO;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;

namespace EasyEject {
    public static partial class Tests {
        [DataContract] sealed class ConfigurationFixture {
            [DataMember] internal string Value;
        }
        [DataContract] sealed class FailingConfiguration {
            [DataMember] internal string Value = "incomplete";
            [OnSerializing] void Stop(StreamingContext context) { throw new InvalidOperationException("Injected serializer failure"); }
        }
        static ConfigurationFixture ConfigValue(string value) { return new ConfigurationFixture { Value = value }; }
        static string ReadConfig(string path) { using (var file = File.OpenRead(path)) return ((ConfigurationFixture)new DataContractJsonSerializer(typeof(ConfigurationFixture)).ReadObject(file)).Value; }
        static void ConfigurationWriterTests() {
            Test("configuration writer keeps its temporary file exclusive through publication", delegate {
                using (var fixture = new Fixture()) {
                    bool blocked = false;
                    Profiles.WriteJson(ConfigValue("owned configuration"), typeof(ConfigurationFixture), fixture.Destination, "fixture.json", (stage, path) => {
                        if (stage == "Before publication") { try { File.WriteAllText(path, "foreign replacement"); } catch (IOException) { blocked = true; } }
                    });
                    Check(blocked && ReadConfig(Path.Combine(fixture.Destination, "fixture.json")) == "owned configuration", "Temporary configuration was replaceable before publication");
                }
            });
            Test("configuration writer cleans only its held temporary after a failed replacement attempt", delegate {
                using (var fixture = new Fixture()) {
                    string final = Path.Combine(fixture.Destination, "fixture.json"); Profiles.WriteJson(ConfigValue("earlier settings"), typeof(ConfigurationFixture), fixture.Destination, "fixture.json");
                    bool blocked = false; string temporary = null, moved = null;
                    Throws<InvalidOperationException>(() => Profiles.WriteJson(ConfigValue("new settings"), typeof(ConfigurationFixture), fixture.Destination, "fixture.json", (stage, path) => {
                        if (stage == "Before publication") {
                            temporary = path; moved = path + ".moved"; try { File.Move(path, moved); File.WriteAllText(path, "foreign temporary"); } catch (IOException) { blocked = true; }
                            throw new InvalidOperationException("Injected serialization/publication stop");
                        }
                    }));
                    Check(blocked && !File.Exists(temporary) && !File.Exists(moved) && ReadConfig(final) == "earlier settings", "Failed write released or cleaned an unowned temporary");
                }
            });
            Test("configuration writer preserves a final filename that appears after initial absent review", delegate {
                using (var fixture = new Fixture()) {
                    string final = Path.Combine(fixture.Destination, "fixture.json");
                    Throws<Exception>(() => Profiles.WriteJson(ConfigValue("new settings"), typeof(ConfigurationFixture), fixture.Destination, "fixture.json", (stage, path) => { if (stage == "Before publication") File.WriteAllText(final, "late foreign settings"); }));
                    Check(File.ReadAllText(final) == "late foreign settings" && Directory.GetFiles(fixture.Destination).Length == 1, "Late configuration overwritten or private temporary leaked");
                }
            });
            Test("configuration writer refuses hard-linked final settings and preserves both names", delegate {
                using (var fixture = new Fixture()) {
                    string final = Path.Combine(fixture.Destination, "fixture.json"), alias = Path.Combine(fixture.Source, "foreign-alias.json"); File.WriteAllText(final, "aliased settings"); Check(CreateHardLink(alias, final, IntPtr.Zero), "Could not create disposable configuration alias");
                    Throws<SafetyException>(() => Profiles.WriteJson(ConfigValue("new settings"), typeof(ConfigurationFixture), fixture.Destination, "fixture.json"));
                    Check(File.ReadAllText(final) == "aliased settings" && File.ReadAllText(alias) == "aliased settings", "Aliased settings were replaced");
                }
            });
            Test("configuration writer keeps the published handle exclusive until final flush returns", delegate {
                using (var fixture = new Fixture()) {
                    bool blocked = false;
                    Profiles.WriteJson(ConfigValue("published settings"), typeof(ConfigurationFixture), fixture.Destination, "fixture.json", (stage, path) => { if (stage == "After publication") { try { File.WriteAllText(path, "post-publication change"); } catch (IOException) { blocked = true; } } });
                    Check(blocked && ReadConfig(Path.Combine(fixture.Destination, "fixture.json")) == "published settings", "Publication handle released before completion");
                }
            });
            Test("configuration writer refuses external read and write locks before making a temporary", delegate {
                using (var fixture = new Fixture()) {
                    string final = Path.Combine(fixture.Destination, "fixture.json"); Profiles.WriteJson(ConfigValue("locked settings"), typeof(ConfigurationFixture), fixture.Destination, "fixture.json");
                    foreach (FileAccess access in new[] { FileAccess.Read, FileAccess.ReadWrite }) using (var external = new FileStream(final, FileMode.Open, access, FileShare.Read)) {
                        int checkpoints = 0; Throws<System.ComponentModel.Win32Exception>(() => Profiles.WriteJson(ConfigValue("new settings"), typeof(ConfigurationFixture), fixture.Destination, "fixture.json", (stage, path) => { if (stage == "Before temporary creation") checkpoints++; }));
                        Check(checkpoints == 0 && Directory.GetFiles(fixture.Destination).Length == 1 && !external.SafeFileHandle.IsClosed, "External lock was bypassed or private work started");
                    }
                    Check(ReadConfig(final) == "locked settings", "External locked settings changed");
                }
            });
            Test("configuration writer holds existing settings against ordinary write delete and rename at publication", delegate {
                using (var fixture = new Fixture()) {
                    string final = Path.Combine(fixture.Destination, "fixture.json"); Profiles.WriteJson(ConfigValue("earlier settings"), typeof(ConfigurationFixture), fixture.Destination, "fixture.json"); int blocked = 0;
                    Profiles.WriteJson(ConfigValue("replacement settings"), typeof(ConfigurationFixture), fixture.Destination, "fixture.json", (stage, path) => { if (stage == "Before publication") {
                        Throws<IOException>(() => File.WriteAllText(final, "forbidden write")); blocked++;
                        Throws<IOException>(() => File.Delete(final)); blocked++;
                        Throws<IOException>(() => File.Move(final, final + ".foreign")); blocked++;
                    } });
                    Check(blocked == 3 && ReadConfig(final) == "replacement settings" && Directory.GetFiles(fixture.Destination).Length == 1, "Existing configuration was released or incompletely replaced");
                }
            });
            Test("native configuration replacement releases its validator only at commit and publishes the held incoming identity", delegate {
                using (var fixture = new Fixture()) {
                    string final = Path.Combine(fixture.Destination, "fixture.json"), temporary = Path.Combine(fixture.Destination, "owned.tmp"); File.WriteAllText(final, "old held configuration");
                    using (Profiles.WriterGate(fixture.Destination)) using (var previous = SafePaths.ProtectOriginal(final, true)) using (var incoming = SafePaths.NewFile(temporary)) {
                        byte[] bytes = System.Text.Encoding.UTF8.GetBytes("new held configuration"); incoming.Write(bytes, 0, bytes.Length); incoming.Flush(true); var identity = Native.Info(incoming.SafeFileHandle);
                        using (var reader = new StreamReader(previous, System.Text.Encoding.UTF8, false, 1024, true)) Check(reader.ReadToEnd() == "old held configuration", "Old configuration was not retained through validation");
                        var oldHandle = previous.SafeFileHandle; Native.ReplaceConfiguration(incoming.SafeFileHandle, previous, final); incoming.Flush(true);
                        Check(oldHandle.IsClosed && !previous.CanRead, "Native commit did not release its own validator");
                        var published = Native.Info(incoming.SafeFileHandle); Check(identity.VolumeSerial == published.VolumeSerial && identity.IndexHigh == published.IndexHigh && identity.IndexLow == published.IndexLow && Native.FinalPath(incoming.SafeFileHandle).Equals(final, StringComparison.OrdinalIgnoreCase) && !File.Exists(temporary), "Replacement did not publish the owned incoming identity");
                    }
                    Check(File.ReadAllText(final) == "new held configuration" && Directory.GetFiles(fixture.Destination).Length == 1, "Closing old/incoming handles deleted the published configuration");
                }
            });
            Test("configuration serialization failure cleans only the owned partial and preserves present or absent settings", delegate {
                using (var fixture = new Fixture()) {
                    string final = Path.Combine(fixture.Destination, "fixture.json");
                    foreach (bool existing in new[] { false, true }) {
                        if (existing) Profiles.WriteJson(ConfigValue("earlier settings"), typeof(ConfigurationFixture), fixture.Destination, "fixture.json");
                        Throws<Exception>(() => Profiles.WriteJson(new FailingConfiguration(), typeof(FailingConfiguration), fixture.Destination, "fixture.json"));
                        Check(Directory.GetFiles(fixture.Destination).Length == (existing ? 1 : 0) && (!existing || ReadConfig(final) == "earlier settings"), "Serializer failure changed earlier choices or leaked private work");
                    }
                }
            });
            Test("configuration writer refuses renamed empty root before creating private work", delegate {
                using (var fixture = new Fixture()) {
                    string root = fixture.Destination, moved = root + "-moved";
                    Throws<SafetyException>(() => Profiles.WriteJson(ConfigValue("new settings"), typeof(ConfigurationFixture), root, "fixture.json", (stage, path) => { if (stage == "Before writer gate") { Directory.Move(root, moved); Directory.CreateDirectory(root); File.WriteAllText(Path.Combine(root, "foreign.json"), "foreign root content"); } }));
                    Check(Directory.Exists(moved) && Directory.GetFileSystemEntries(moved).Length == 0 && Directory.GetFiles(root).Length == 1 && File.ReadAllText(Path.Combine(root, "foreign.json")) == "foreign root content", "Writer used or cleaned a replacement root");
                }
            });
            Test("configuration writer validates filenames before creating an absent root", delegate {
                using (var fixture = new Fixture()) {
                    string root = Path.Combine(fixture.Root, "absent-config");
                    foreach (string name in new[] { "..\\escape.json", "sub\\file.json", "CON.json", "NUL.json", "COM1.json", "LPT9.json", ".json", "future.txt", "bad:stream.json", "trailing.json " })
                        Throws<SafetyException>(() => Profiles.WriteJson(ConfigValue("refuse"), typeof(ConfigurationFixture), root, name));
                    Check(!Directory.Exists(root) && !File.Exists(Path.Combine(fixture.Root, "escape.json")), "Invalid configuration path created storage or escaped its folder");
                }
            });
            Test("configuration writer rejects null value and type before creating storage", delegate {
                using (var fixture = new Fixture()) {
                    string root = Path.Combine(fixture.Root, "absent-config"); Throws<ArgumentNullException>(() => Profiles.WriteJson(null, typeof(ConfigurationFixture), root, "fixture.json")); Throws<ArgumentNullException>(() => Profiles.WriteJson(ConfigValue("refuse"), null, root, "fixture.json")); Check(!Directory.Exists(root), "Null input created configuration storage");
                }
            });
            Test("configuration writer rejects redirected ancestors before creating missing configuration children", delegate {
                using (var fixture = new Fixture()) {
                    string target = Path.Combine(fixture.Root, "foreign-target"), link = Path.Combine(fixture.Root, "config-link"); Directory.CreateDirectory(target); CreateJunction(fixture, link, target);
                    Throws<SafetyException>(() => Profiles.WriteJson(ConfigValue("refuse"), typeof(ConfigurationFixture), Path.Combine(link, "absent-child"), "fixture.json"));
                    Check(!Directory.Exists(Path.Combine(target, "absent-child")) && Directory.GetFileSystemEntries(target).Length == 0 && (File.GetAttributes(link) & FileAttributes.ReparsePoint) != 0, "Redirected storage was populated or its junction altered");
                }
            });
            Test("configuration writer refuses a directory occupying the final settings name", delegate {
                using (var fixture = new Fixture()) {
                    string final = Path.Combine(fixture.Destination, "fixture.json"); Directory.CreateDirectory(final); File.WriteAllText(Path.Combine(final, "foreign.keep"), "keep directory contents");
                    Throws<Exception>(() => Profiles.WriteJson(ConfigValue("refuse"), typeof(ConfigurationFixture), fixture.Destination, "fixture.json"));
                    Check(Directory.GetFiles(fixture.Destination).Length == 0 && File.ReadAllText(Path.Combine(final, "foreign.keep")) == "keep directory contents", "Occupied directory modified or private temporary leaked");
                }
            });
            Test("configuration writer preserves read-only and system settings without changing protection", delegate {
                using (var fixture = new Fixture()) {
                    string final = Path.Combine(fixture.Destination, "fixture.json");
                    foreach (FileAttributes protection in new[] { FileAttributes.ReadOnly, FileAttributes.System }) {
                        File.WriteAllText(final, "protected settings"); File.SetAttributes(final, protection);
                        try { Throws<SafetyException>(() => Profiles.WriteJson(ConfigValue("refuse"), typeof(ConfigurationFixture), fixture.Destination, "fixture.json")); Check(File.ReadAllText(final) == "protected settings" && (File.GetAttributes(final) & protection) == protection && Directory.GetFiles(fixture.Destination).Length == 1, "Protection bypassed or private work created"); }
                        finally { File.SetAttributes(final, FileAttributes.Normal); }
                    }
                }
            });
            Test("configuration writer retains a newly protected owned temporary and reports its path", delegate {
                using (var fixture = new Fixture()) {
                    string temporary = null;
                    var failure = Throws<SafetyException>(() => Profiles.WriteJson(ConfigValue("protected partial"), typeof(ConfigurationFixture), fixture.Destination, "fixture.json", (stage, path) => { if (stage == "Before publication") { temporary = path; File.SetAttributes(path, FileAttributes.ReadOnly); } }));
                    try { Check(failure.Message.Contains("not published") && failure.Message.Contains(temporary) && File.Exists(temporary) && (File.GetAttributes(temporary) & FileAttributes.ReadOnly) != 0 && !File.Exists(Path.Combine(fixture.Destination, "fixture.json")), "Private protection changed or retained partial concealed"); }
                    finally { if (temporary != null && File.Exists(temporary)) File.SetAttributes(temporary, FileAttributes.Normal); }
                }
            });
            Test("configuration writer preserves an aliased temporary and its foreign alias rather than broad cleanup", delegate {
                using (var fixture = new Fixture()) {
                    string temporary = null, alias = Path.Combine(fixture.Source, "temporary-alias.keep");
                    var failure = Throws<SafetyException>(() => Profiles.WriteJson(ConfigValue("aliased partial"), typeof(ConfigurationFixture), fixture.Destination, "fixture.json", (stage, path) => { if (stage == "Before publication") { temporary = path; Check(CreateHardLink(alias, path, IntPtr.Zero), "Could not alias disposable private temporary"); } }));
                    Check(failure.Message.Contains(temporary) && File.Exists(temporary) && ReadConfig(temporary) == "aliased partial" && ReadConfig(alias) == "aliased partial" && !File.Exists(Path.Combine(fixture.Destination, "fixture.json")), "Alias publication or cleanup changed another name");
                }
            });
            Test("configuration writer excludes a competing existing-file writer and allows one manual retry", delegate {
                using (var fixture = new Fixture()) {
                    string final = Path.Combine(fixture.Destination, "fixture.json"); Profiles.WriteJson(ConfigValue("earlier settings"), typeof(ConfigurationFixture), fixture.Destination, "fixture.json"); int refused = 0;
                    Profiles.WriteJson(ConfigValue("first writer"), typeof(ConfigurationFixture), fixture.Destination, "fixture.json", (stage, path) => { if (stage == "Before publication") { Throws<System.ComponentModel.Win32Exception>(() => Profiles.WriteJson(ConfigValue("competing writer"), typeof(ConfigurationFixture), fixture.Destination, "fixture.json")); refused++; } });
                    Check(refused == 1 && ReadConfig(final) == "first writer", "Competing writer changed the held review"); Profiles.WriteJson(ConfigValue("manual retry"), typeof(ConfigurationFixture), fixture.Destination, "fixture.json");
                    Check(ReadConfig(final) == "manual retry" && Directory.GetFiles(fixture.Destination).Length == 1, "Reservation survived completion or retry left private work");
                }
            });
            Test("configuration writer excludes a competing absent-file writer and allows a later manual retry", delegate {
                using (var fixture = new Fixture()) {
                    string final = Path.Combine(fixture.Destination, "fixture.json");
                    Profiles.WriteJson(ConfigValue("first published writer"), typeof(ConfigurationFixture), fixture.Destination, "fixture.json", (stage, path) => { if (stage == "Before publication") Throws<System.ComponentModel.Win32Exception>(() => Profiles.WriteJson(ConfigValue("competing writer"), typeof(ConfigurationFixture), fixture.Destination, "fixture.json")); });
                    Check(ReadConfig(final) == "first published writer" && Directory.GetFiles(fixture.Destination).Length == 1, "Competing writer changed an absent-file review or leaked work");
                    Profiles.WriteJson(ConfigValue("manual retry"), typeof(ConfigurationFixture), fixture.Destination, "fixture.json"); Check(ReadConfig(final) == "manual retry", "Manual retry was blocked after completion");
                }
            });
            Test("configuration writer reports committed preferences honestly when a final callback fails", delegate {
                using (var fixture = new Fixture()) {
                    string final = Path.Combine(fixture.Destination, "fixture.json");
                    var failure = Throws<SafetyException>(() => Profiles.WriteJson(ConfigValue("committed settings"), typeof(ConfigurationFixture), fixture.Destination, "fixture.json", (stage, path) => { if (stage == "After publication") throw new InvalidOperationException("Injected late finish failure"); }));
                    Check(failure.Message.Contains("was published") && failure.Message.Contains(final) && ReadConfig(final) == "committed settings" && Directory.GetFiles(fixture.Destination).Length == 1, "Late failure removed or concealed committed preferences");
                }
            });
            Test("configuration writer reuses only a live same-root writer and retains the caller's lease", delegate {
                using (var fixture = new Fixture()) {
                    string final = Path.Combine(fixture.Destination, "fixture.json"); var writer = Profiles.WriterGate(fixture.Destination);
                    try {
                        Profiles.WriteJson(ConfigValue("owned writer settings"), typeof(ConfigurationFixture), fixture.Destination, "fixture.json", writer: writer); writer.Validate(fixture.Destination);
                        Throws<System.ComponentModel.Win32Exception>(() => Profiles.WriteJson(ConfigValue("competing settings"), typeof(ConfigurationFixture), fixture.Destination, "fixture.json"));
                        Throws<SafetyException>(() => Profiles.WriteJson(ConfigValue("wrong root"), typeof(ConfigurationFixture), fixture.Source, "fixture.json", writer: writer));
                        string absent = Path.Combine(fixture.Source, "wrong-root-absent"); Throws<SafetyException>(() => Profiles.WriteJson(ConfigValue("wrong absent root"), typeof(ConfigurationFixture), absent, "fixture.json", writer: writer));
                        Check(!Directory.Exists(absent) && !File.Exists(Path.Combine(fixture.Source, "fixture.json")) && ReadConfig(final) == "owned writer settings", "Wrong writer retargeted storage or borrowed lease was disposed");
                    } finally { writer.Dispose(); }
                    Throws<SafetyException>(() => Profiles.WriteJson(ConfigValue("closed writer"), typeof(ConfigurationFixture), fixture.Destination, "fixture.json", writer: writer));
                    Check(ReadConfig(final) == "owned writer settings" && Directory.GetFiles(fixture.Destination).Length == 1, "Closed writer changed earlier choices or retained the lock");
                }
            });
            Test("configuration writer preserves an occupied nonempty lock instead of deleting foreign contents", delegate {
                using (var fixture = new Fixture()) {
                    string lockPath = Path.Combine(fixture.Destination, ".profile-write.lock"); File.WriteAllText(lockPath, "foreign reserved-name contents");
                    Throws<SafetyException>(() => Profiles.WriteJson(ConfigValue("refuse"), typeof(ConfigurationFixture), fixture.Destination, "fixture.json"));
                    Check(File.ReadAllText(lockPath) == "foreign reserved-name contents" && Directory.GetFiles(fixture.Destination).Length == 1, "Writer deleted an unowned occupied lock");
                }
            });
            Test("configuration writer preserves a hard-linked lock and its alias", delegate {
                using (var fixture = new Fixture()) {
                    string lockPath = Path.Combine(fixture.Destination, ".profile-write.lock"), alias = Path.Combine(fixture.Source, "lock-alias.keep"); File.WriteAllText(lockPath, ""); Check(CreateHardLink(alias, lockPath, IntPtr.Zero), "Could not create disposable lock alias");
                    Throws<SafetyException>(() => Profiles.WriteJson(ConfigValue("refuse"), typeof(ConfigurationFixture), fixture.Destination, "fixture.json"));
                    Check(File.Exists(lockPath) && File.Exists(alias) && new FileInfo(lockPath).Length == 0 && Directory.GetFiles(fixture.Destination).Length == 1, "Aliased lock was cleared or broad private cleanup occurred");
                }
            });
            Test("configuration writer rechecks newly protected final settings at the native commit boundary", delegate {
                using (var fixture = new Fixture()) {
                    string final = Path.Combine(fixture.Destination, "fixture.json");
                    foreach (FileAttributes protection in new[] { FileAttributes.ReadOnly, FileAttributes.System }) {
                        Profiles.WriteJson(ConfigValue("earlier protected settings"), typeof(ConfigurationFixture), fixture.Destination, "fixture.json");
                        try {
                            Throws<SafetyException>(() => Profiles.WriteJson(ConfigValue("refuse late protection"), typeof(ConfigurationFixture), fixture.Destination, "fixture.json", (stage, path) => { if (stage == "Before publication") File.SetAttributes(final, protection); }));
                            Check(ReadConfig(final) == "earlier protected settings" && (File.GetAttributes(final) & protection) == protection && Directory.GetFiles(fixture.Destination).Length == 1, "Late protection was bypassed or private cleanup broadened");
                        } finally { File.SetAttributes(final, FileAttributes.Normal); }
                    }
                }
            });
        }
    }
}
