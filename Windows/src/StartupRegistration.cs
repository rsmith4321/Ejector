using System;
using System.IO;
using System.Linq;
using Microsoft.Win32;

namespace EasyEject {
    internal sealed class StartupValue {
        internal readonly bool Present; internal readonly RegistryValueKind Kind; readonly object value;
        internal object Value { get { var bytes = value as byte[]; return bytes == null ? value : bytes.Clone(); } }
        internal StartupValue(bool present, RegistryValueKind kind, object value) { Present = present; Kind = kind; var bytes = value as byte[]; this.value = bytes == null ? value : bytes.Clone(); }
        internal static StartupValue Missing() { return new StartupValue(false, RegistryValueKind.None, null); }
        internal bool Same(StartupValue other) {
            if (other == null || Present != other.Present || Kind != other.Kind) return false;
            var a = value as byte[]; var b = other.value as byte[];
            return a != null ? b != null && a.SequenceEqual(b) : Object.Equals(value, other.value);
        }
    }
    internal interface IStartupStore { StartupValue Read(); void Change(StartupValue expected, string command); }
    internal sealed class WindowsStartupStore : IStartupStore {
        internal const string KeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run", ValueName = "EasyEject.Windows";
        static StartupValue Read(RegistryKey key) {
            if (key == null || !key.GetValueNames().Contains(ValueName, StringComparer.OrdinalIgnoreCase)) return StartupValue.Missing();
            return new StartupValue(true, key.GetValueKind(ValueName), key.GetValue(ValueName, null, RegistryValueOptions.DoNotExpandEnvironmentNames));
        }
        public StartupValue Read() { using (var hive = RegistryKey.OpenBaseKey(RegistryHive.CurrentUser, RegistryView.Registry64)) using (var key = hive.OpenSubKey(KeyPath, false)) return Read(key); }
        public void Change(StartupValue expected, string command) {
            using (var hive = RegistryKey.OpenBaseKey(RegistryHive.CurrentUser, RegistryView.Registry64)) {
                using (var existing = hive.OpenSubKey(KeyPath, false)) if (!Read(existing).Same(expected)) throw new SafetyException("Login registration changed. Refresh Settings before trying again.");
                // Creation happens only on the explicitly requested enable action; no HKLM,
                // scheduled task, service, RunOnce or Windows enablement/policy value is changed.
                using (var key = command == null ? hive.OpenSubKey(KeyPath, true) : hive.CreateSubKey(KeyPath)) {
                    if (!Read(key).Same(expected)) throw new SafetyException("Login registration changed. Refresh Settings before trying again.");
                    if (command == null) { if (key != null) key.DeleteValue(ValueName, false); }
                    else key.SetValue(ValueName, command, RegistryValueKind.String);
                    if (key != null) key.Flush();
                }
            }
        }
    }
    internal sealed class StartupSnapshot {
        internal readonly StartupValue Value; internal readonly string CurrentCommand, RegisteredPath, Error;
        internal StartupSnapshot(StartupValue value, string command, string error = null) { Value = value; CurrentCommand = command; Error = error; if (value != null && value.Present && value.Kind == RegistryValueKind.String) RegisteredPath = StartupRegistration.OwnedPath(value.Value as string); }
        internal bool Available { get { return Error == null && Value != null; } }
        internal bool Owned { get { return Available && RegisteredPath != null; } }
        internal bool Here { get { return Owned && String.Equals(Value.Value as string, CurrentCommand, StringComparison.OrdinalIgnoreCase); } }
        internal bool Conflict { get { return Available && Value.Present && !Owned; } }
        internal string Message { get {
            if (!Available) return "Login registration could not be checked. " + Error;
            if (Conflict) return "This login entry contains an unrecognized command and will be left intact. Review Windows Startup apps before changing it.";
            if (!Owned) return "Launch at login is not registered. It starts off.";
            return (Here ? "This copy is registered for login." : "A different Easy Eject copy is registered at: " + RegisteredPath) + " Windows Startup apps or policy can still disable or delay it. Review Windows' enablement there; Easy Eject does not override it.";
        } }
    }
    internal sealed class StartupRegistration {
        const string Suffix = " --startup";
        readonly IStartupStore store; readonly string executable, command; readonly Func<string, IDisposable> hold;
        internal StartupRegistration(IStartupStore store, string executable, Func<string, IDisposable> hold = null) { this.store = store; this.executable = SafePaths.Normalize(executable); command = Command(this.executable); this.hold = hold ?? HoldExecutable; }
        internal static string Command(string executable) {
            string path = SafePaths.Normalize(executable);
            if (!Path.GetFileName(path).Equals("EasyEject.Windows.exe", StringComparison.OrdinalIgnoreCase) || path.Any(Char.IsControl) || path.Contains("\"")) throw new SafetyException("Login registration requires the local EasyEject.Windows.exe application.");
            string value = "\"" + path + "\"" + Suffix;
            if (value.Length > 260) throw new SafetyException("The login command exceeds Windows' supported length."); return value;
        }
        internal static string OwnedPath(string value) {
            if (String.IsNullOrEmpty(value) || value.Length > 260 || !value.StartsWith("\"", StringComparison.Ordinal) || !value.EndsWith("\"" + Suffix, StringComparison.Ordinal)) return null;
            try { string path = value.Substring(1, value.Length - Suffix.Length - 2); return Command(path) == value ? path : null; }
            catch (Exception ex) { if (ex is SafetyException || ex is ArgumentException || ex is NotSupportedException) return null; throw; }
        }
        static IDisposable HoldExecutable(string path) {
            var directories = SafePaths.LockDirectories(Path.GetDirectoryName(path));
            try { var stream = SafePaths.ReadFile(path); return new HeldExecutable(directories, stream); } catch { directories.Dispose(); throw; }
        }
        sealed class HeldExecutable : IDisposable { readonly IDisposable directory, file; internal HeldExecutable(IDisposable directory, IDisposable file) { this.directory = directory; this.file = file; } public void Dispose() { file.Dispose(); directory.Dispose(); } }
        internal StartupSnapshot Inspect() { try { return new StartupSnapshot(store.Read(), command); } catch (Exception ex) { return new StartupSnapshot(null, command, ex.Message); } }
        internal StartupSnapshot Apply(StartupSnapshot expected, bool enable) {
            if (expected == null || !expected.Available || expected.Conflict) throw new SafetyException("Login registration is unavailable or belongs to an unrecognized command. Nothing was changed.");
            if (!store.Read().Same(expected.Value)) throw new SafetyException("Login registration changed while Settings was open. Review its current status first.");
            if (!enable && !expected.Owned || enable && expected.Here) return Inspect();
            using (var held = enable ? hold(executable) : null) {
                store.Change(expected.Value, enable ? command : null);
                var current = Inspect();
                if (!current.Available || (enable ? !current.Here : current.Value.Present)) throw new SafetyException("The login registration change could not be confirmed. It may have been applied; review Windows Startup apps and Settings. No automatic retry occurs.");
                return current;
            }
        }
    }
    internal static class StartupLaunch {
        internal static bool Parse(string[] args) {
            if (args == null || args.Length == 0) return false;
            if (args.Length == 1 && args[0] == "--startup") return true;
            throw new SafetyException("Unsupported launch arguments. Start Easy Eject normally or use its registered --startup command.");
        }
    }
    internal sealed class StartupPresentation {
        readonly bool hidden; bool first = true;
        internal StartupPresentation(bool hidden) { this.hidden = hidden; }
        internal bool AllowShow(Action createHandle, Action scheduleMonitoring) {
            if (!first) return true; first = false;
            if (!hidden) return true;
            createHandle(); scheduleMonitoring(); return false;
        }
    }
}
