using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using Microsoft.Win32;

namespace EasyEject {
    public static partial class Tests {
        const string StartupFixturePath = @"C:\Fixture apps\Easy Eject\EasyEject.Windows.exe";
        sealed class FakeStartupStore : IStartupStore {
            internal StartupValue State = StartupValue.Missing(); internal int Writes, Reads; internal Action BeforeChange; internal bool FailRead, FailWrite, FailReadback;
            internal readonly Dictionary<string, string> Others = new Dictionary<string, string> { { "Other app", "keep untouched" } };
            public StartupValue Read() { Reads++; if (FailRead || FailReadback && Writes > 0) throw new IOException("Fixture registry read refused"); return State; }
            public void Change(StartupValue expected, string command) { if (BeforeChange != null) BeforeChange(); if (!State.Same(expected)) throw new SafetyException("Fixture registration changed"); if (FailWrite) throw new UnauthorizedAccessException("Fixture registry write refused"); Writes++; State = command == null ? StartupValue.Missing() : new StartupValue(true, RegistryValueKind.String, command); }
        }
        sealed class StartupPin : IDisposable { readonly Action release; internal StartupPin(Action release) { this.release = release; } public void Dispose() { release(); } }
        static StartupRegistration FakeStartup(FakeStartupStore store, Func<string, IDisposable> hold = null) { return new StartupRegistration(store, StartupFixturePath, hold ?? (path => new StartupPin(() => {}))); }
        static void StartupTests() {
            Test("login registration starts off and inspecting status never writes or starts anything", delegate {
                var store = new FakeStartupStore(); var registration = FakeStartup(store); var state = registration.Inspect();
                Check(state.Available && !state.Owned && !state.Conflict && store.Writes == 0 && state.Message.Contains("starts off"), "Inspect registered startup or claimed enabled");
            });
            Test("quoted login command supports spaces unicode and only the exact tray argument", delegate {
                string command = StartupRegistration.Command(@"C:\Photo work\旅行\EasyEject.Windows.exe");
                Check(command == "\"C:\\Photo work\\旅行\\EasyEject.Windows.exe\" --startup" && StartupRegistration.OwnedPath(command) == @"C:\Photo work\旅行\EasyEject.Windows.exe", "Startup quoting or argument parser changed");
                foreach (string bad in new[] { "EasyEject.Windows.exe --startup", "\"C:\\Other.exe\" --startup", command + " --export", command + " & more", command.Replace("--startup", "--eject"), "\"\\\\server\\share\\EasyEject.Windows.exe\" --startup", "\"C:\\x\\..\\EasyEject.Windows.exe\" --startup" }) Check(StartupRegistration.OwnedPath(bad) == null, "Unrecognized command claimed as owned");
                Throws<SafetyException>(() => StartupRegistration.Command(@"C:\x\Other.exe"));
            });
            Test("explicit enabling holds the current executable and writes only the app's login value", delegate {
                var store = new FakeStartupStore(); bool held = false, released = false;
                store.BeforeChange = () => Check(held, "Registration was written without pinned executable");
                var registration = FakeStartup(store, path => { Check(path == StartupFixturePath, "Wrong executable pinned"); held = true; return new StartupPin(() => { held = false; released = true; }); });
                var result = registration.Apply(registration.Inspect(), true);
                Check(result.Here && store.Writes == 1 && released && !held && store.Others.Single().Value == "keep untouched" && result.Message.Contains("disable or delay"), "Enable changed other entries or claimed Windows override");
            });
            Test("unchanged current registration is a read-only no-op and disabling removes only its owned value", delegate {
                var store = new FakeStartupStore(); var registration = FakeStartup(store); registration.Apply(registration.Inspect(), true); int writes = store.Writes;
                registration.Apply(registration.Inspect(), true); Check(store.Writes == writes, "Unchanged save rewrote registration");
                var result = registration.Apply(registration.Inspect(), false); Check(!result.Owned && !result.Value.Present && store.Writes == writes + 1 && store.Others.Count == 1, "Disable touched other entries");
                registration.Apply(result, false); Check(store.Writes == writes + 1, "Disabling an absent entry created or modified registration");
            });
            Test("other Easy Eject location is reported without repair until explicit retarget or removal", delegate {
                var store = new FakeStartupStore { State = new StartupValue(true, RegistryValueKind.String, StartupRegistration.Command(@"D:\Previous copy\EasyEject.Windows.exe")) }; var registration = FakeStartup(store); var old = registration.Inspect();
                Check(old.Owned && !old.Here && old.RegisteredPath.StartsWith(@"D:\Previous copy") && store.Writes == 0, "Inspect silently repaired relocated registration");
                var current = registration.Apply(old, true); Check(current.Here && store.Writes == 1, "Explicit retarget did not select current copy");
                store.State = old.Value; registration.Apply(registration.Inspect(), false); Check(!store.State.Present && store.Others.Count == 1, "Explicit old-copy removal affected unrelated entries");
            });
            Test("foreign malformed and expanded-value collisions refuse enabling and disabling intact", delegate {
                foreach (StartupValue bad in new[] { new StartupValue(true, RegistryValueKind.String, "\"C:\\Other.exe\" --startup"), new StartupValue(true, RegistryValueKind.String, "unquoted command"), new StartupValue(true, RegistryValueKind.ExpandString, "%LOCALAPPDATA%\\EasyEject.Windows.exe --startup"), new StartupValue(true, RegistryValueKind.Binary, new byte[] { 1, 2, 3 }) }) {
                    var store = new FakeStartupStore { State = bad }; var registration = FakeStartup(store); var state = registration.Inspect();
                    Check(state.Conflict && state.Message.Contains("left intact"), "Foreign value interpreted as registration");
                    Throws<SafetyException>(() => registration.Apply(state, true)); Throws<SafetyException>(() => registration.Apply(state, false)); Check(store.Writes == 0 && store.State.Same(bad), "Foreign collision overwritten/deleted");
                }
            });
            Test("registration changed during Settings or executable validation refuses before write", delegate {
                var store = new FakeStartupStore(); var registration = FakeStartup(store); var old = registration.Inspect();
                store.State = new StartupValue(true, RegistryValueKind.String, "foreign change"); Throws<SafetyException>(() => registration.Apply(old, true)); Check(store.Writes == 0, "Stale settings overwrote later registration");
                store.State = StartupValue.Missing(); registration = FakeStartup(store, path => { store.State = new StartupValue(true, RegistryValueKind.String, "changed while pinning"); return new StartupPin(() => {}); });
                old = registration.Inspect(); Throws<SafetyException>(() => registration.Apply(old, true)); Check(store.Writes == 0, "Late change bypassed store's expected-value check");
            });
            Test("missing unreadable or redirected executable cannot gain a login registration", delegate {
                var store = new FakeStartupStore(); var registration = FakeStartup(store, path => { throw new IOException("Fixture executable unavailable"); });
                Throws<IOException>(() => registration.Apply(registration.Inspect(), true)); Check(store.Writes == 0, "Failed executable validation registered command");
                using (var f = new Fixture()) {
                    string absent = Path.Combine(f.Source, "EasyEject.Windows.exe"); var realValidation = new StartupRegistration(store, absent); Throws<System.ComponentModel.Win32Exception>(() => realValidation.Apply(realValidation.Inspect(), true));
                    string link = Path.Combine(f.Source, "redirected"); CreateJunction(f, link, f.Destination); File.WriteAllText(Path.Combine(f.Destination, "EasyEject.Windows.exe"), "synthetic fixture, never executed");
                    realValidation = new StartupRegistration(store, Path.Combine(link, "EasyEject.Windows.exe")); Throws<SafetyException>(() => realValidation.Apply(realValidation.Inspect(), true)); Check(store.Writes == 0, "Redirected executable gained registration");
                }
            });
            Test("failed registry writes release the executable and preserve every value", delegate {
                var store = new FakeStartupStore { FailWrite = true }; bool released = false; var registration = FakeStartup(store, path => new StartupPin(() => released = true));
                Throws<UnauthorizedAccessException>(() => registration.Apply(registration.Inspect(), true)); Check(released && store.Writes == 0 && !store.State.Present && store.Others.Count == 1, "Failed write retained handle or mutated registry");
            });
            Test("unavailable and unconfirmed registration status never claims success or retries", delegate {
                var store = new FakeStartupStore { FailRead = true }; var registration = FakeStartup(store); var state = registration.Inspect(); Check(!state.Available && state.Message.Contains("could not be checked"), "Read error claimed startup off/enabled");
                Throws<SafetyException>(() => registration.Apply(state, true)); Check(store.Writes == 0, "Unreadable status was written");
                store.FailRead = false; store.FailReadback = true; state = registration.Inspect(); var failure = Throws<SafetyException>(() => registration.Apply(state, true));
                Check(store.Writes == 1 && store.State.Present && failure.Message.Contains("may have been applied") && failure.Message.Contains("No automatic retry"), "Readback failure hid applied state or retried registration");
            });
            Test("binary collision snapshots do not alias mutable registry payloads", delegate {
                byte[] bytes = { 1, 2 }; var value = new StartupValue(true, RegistryValueKind.Binary, bytes); bytes[0] = 9; var read = (byte[])value.Value; read[1] = 8;
                Check(value.Same(new StartupValue(true, RegistryValueKind.Binary, new byte[] { 1, 2 })), "Snapshot aliased registry bytes");
            });
            Test("startup argument allows only exact tray mode without changing import flags", delegate {
                Check(!StartupLaunch.Parse(new string[0]) && !StartupLaunch.Parse(null) && StartupLaunch.Parse(new[] { "--startup" }), "Launch mode parsing incorrect");
                foreach (var args in new[] { new[] { "--startup", "--eject" }, new[] { "--export" }, new[] { "--Startup" } }) Throws<SafetyException>(() => StartupLaunch.Parse(args));
                Check(!new AppSettings().ShortcutEnabled && !new ImportDefaults().AutomaticEnabled && !new ImportDefaults().Snapshot().AskToEject, "Startup parsing enabled unrelated actions");
            });
            Test("tray startup creates its handle and schedules monitoring once before any visible show", delegate {
                var hidden = new StartupPresentation(true); int handles = 0, schedules = 0;
                Check(!hidden.AllowShow(() => handles++, () => schedules++) && handles == 1 && schedules == 1, "Tray mode flashed a window or skipped monitoring");
                Check(hidden.AllowShow(() => handles++, () => schedules++) && hidden.AllowShow(() => handles++, () => schedules++) && handles == 1 && schedules == 1, "Later manual show repeated startup work");
                var normal = new StartupPresentation(false); Check(normal.AllowShow(() => handles++, () => schedules++) && handles == 1 && schedules == 1, "Normal launch scheduled hidden monitoring");
            });
            Test("native read-only tray startup stays hidden and creates notifications without copying", delegate {
                using (var window = new DeviceWindow(true, true)) {
                    typeof(DeviceWindow).GetField("monitoringStarted", BindingFlags.NonPublic | BindingFlags.Instance).SetValue(window, true); // Keep default fixture tests from starting a real inventory refresh.
                    window.Visible = true;
                    Check(window.IsHandleCreated && !window.Visible, "Startup window became visible");
                    var discovery = (WindowsDiscovery)typeof(DeviceWindow).GetField("discovery", BindingFlags.NonPublic | BindingFlags.Instance).GetValue(window);
                    Check(discovery.NotificationsAvailable, "Hidden startup did not register notification handle");
                }
            });
            Test("login settings draft and cancel cannot mutate registration or import defaults", delegate {
                var store = new FakeStartupStore(); var registration = FakeStartup(store); var defaults = new ImportDefaults();
                using (var dialog = new AppSettingsDialog(new AppSettings(), null, registration.Inspect(), () => {})) {
                    Check(dialog.Login.Enabled && !dialog.Login.Checked && !dialog.LoginChange.HasValue, "Opening settings enabled startup"); dialog.Login.Checked = true;
                    Check(dialog.LoginChange == true && !defaults.AutomaticEnabled && store.Writes == 0 && dialog.Value.EnableShortcut == false, "Draft touched registration or import flags");
                }
                Check(store.Writes == 0 && !store.State.Present, "Cancel registered startup");
            });
            Test("moved-copy settings require explicit retarget and a later uncheck still means disable", delegate {
                var store = new FakeStartupStore { State = new StartupValue(true, RegistryValueKind.String, StartupRegistration.Command(@"D:\Old\EasyEject.Windows.exe")) }; var snapshot = FakeStartup(store).Inspect();
                using (var dialog = new AppSettingsDialog(new AppSettings(), null, snapshot, () => {})) {
                    Check(dialog.Login.Checked && !dialog.LoginChange.HasValue, "Opening relocated settings repaired registration"); dialog.RetargetLogin(); Check(dialog.LoginChange == true, "Explicit retarget not retained"); dialog.Login.Checked = false; Check(dialog.LoginChange == false && store.Writes == 0, "Unchecking after retarget still enabled startup");
                }
            });
            Test("unavailable foreign and read-only startup UI provide no mutation choice", delegate {
                foreach (StartupSnapshot state in new[] { (StartupSnapshot)null, new StartupSnapshot(null, StartupRegistration.Command(StartupFixturePath), "fixture access denied"), new StartupSnapshot(new StartupValue(true, RegistryValueKind.String, "foreign command"), StartupRegistration.Command(StartupFixturePath)) }) {
                    using (var dialog = new AppSettingsDialog(new AppSettings(), null, state, null)) { dialog.Login.Checked = true; dialog.RetargetLogin(); Check(!dialog.Login.Enabled && !dialog.LoginChange.HasValue, "Unavailable UI exposed startup change"); }
                }
            });
        }
    }
}
