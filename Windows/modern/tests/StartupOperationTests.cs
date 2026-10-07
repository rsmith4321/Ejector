using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using Microsoft.Win32;

namespace EasyEject {
    public static partial class Tests {
        const string ModernStartupPath = @"C:\Fixture apps\旅行\EasyEject.Modern.exe";
        sealed class ModernStartupStore : IStartupStore {
            internal StartupValue State = StartupValue.Missing(); internal int Writes;
            internal bool RefuseWrite, RefuseReadback; internal Action DuringChange;
            public StartupValue Read() { if (RefuseReadback && Writes > 0) throw new IOException("Generated startup readback failure"); return State; }
            public void Change(StartupValue expected, string command) {
                if (DuringChange != null) DuringChange(); if (!State.Same(expected)) throw new SafetyException("Generated startup entry changed");
                if (RefuseWrite) throw new UnauthorizedAccessException("Generated registry write refusal");
                Writes++; State = command == null ? StartupValue.Missing() : new StartupValue(true, RegistryValueKind.String, command);
            }
        }
        sealed class ModernStartupPin : IDisposable { readonly Action release; internal ModernStartupPin(Action action) { release = action; } public void Dispose() { release(); } }
        static StartupRegistration ModernRegistration(ModernStartupStore store, Func<string, IDisposable> hold = null) { return new StartupRegistration(store, ModernStartupPath, hold ?? (path => new ModernStartupPin(() => {})), true); }
        static void StartupOperationTests() {
            Test("modern login command preserves classic strict policy and validates exact modern identity", delegate {
                string modern = StartupRegistration.Command(ModernStartupPath, true), classic = StartupRegistration.Command(@"C:\Old copy\EasyEject.Windows.exe");
                Check(modern == "\"" + ModernStartupPath + "\" --startup" && StartupRegistration.OwnedPath(modern, true) == ModernStartupPath && StartupRegistration.OwnedPath(modern) == null && StartupRegistration.OwnedPath(classic, true) == @"C:\Old copy\EasyEject.Windows.exe", "Modern/classic ownership changed");
                Throws<SafetyException>(() => StartupRegistration.Command(ModernStartupPath)); Throws<SafetyException>(() => StartupRegistration.Command(@"C:\EasyEject.Windows.exe", true));
                foreach (string bad in new[] { modern + " --eject", modern + " & more", modern.Replace("--startup", "--export"), @"""C:\Other.exe"" --startup", @"""\\server\share\EasyEject.Modern.exe"" --startup" }) Check(StartupRegistration.OwnedPath(bad, true) == null, "Unrecognized modern command claimed");
            });
            Test("modern login inspection and cancelled draft never register or start anything", delegate {
                var store = new ModernStartupStore(); var draft = new StartupEdit(ModernRegistration(store)); Check(draft.Available && !draft.Enabled && !draft.Change.HasValue && store.Writes == 0, "Inspect enabled login");
                draft.Set(true); draft.Cancel(); Throws<SafetyException>(() => draft.Save()); Check(store.Writes == 0 && !store.State.Present, "Cancelled login draft wrote");
            });
            Test("modern explicit login save holds exact binary and consumes draft", delegate {
                var store = new ModernStartupStore(); bool held = false, released = false;
                store.DuringChange = () => Check(held, "Modern executable not held");
                var draft = new StartupEdit(ModernRegistration(store, path => { Check(path == ModernStartupPath, "Wrong binary held"); held = true; return new ModernStartupPin(() => { held = false; released = true; }); }));
                draft.Set(true); var result = draft.Save(); Check(result.Here && released && !held && store.Writes == 1 && result.Message.Contains("disable or delay"), "Wrong registration or claimed policy override");
                Throws<SafetyException>(() => draft.Save()); Throws<SafetyException>(() => draft.Set(false));
            });
            Test("modern legacy login remains unchanged until explicit retarget and disable needs no binary hold", delegate {
                var store = new ModernStartupStore { State = new StartupValue(true, RegistryValueKind.String, StartupRegistration.Command(@"D:\Old copy\EasyEject.Windows.exe")) };
                var original = store.State; var unchanged = new StartupEdit(ModernRegistration(store)); Check(unchanged.Enabled && unchanged.CanRetarget && !unchanged.Change.HasValue, "Legacy login did not remain owned"); unchanged.Save(); Check(store.State.Same(original) && store.Writes == 0, "Saving other settings retargeted classic login");
                var moved = new StartupEdit(ModernRegistration(store)); moved.UseThisCopy(); Check(moved.Save().Here && store.Writes == 1, "Explicit retarget failed");
                var disable = new StartupEdit(ModernRegistration(store, path => { throw new Exception("Disable must not open executable"); })); disable.Set(false); disable.Save(); Check(!store.State.Present && store.Writes == 2, "Explicit disable failed");
            });
            Test("modern foreign startup values and cross-copy snapshots cannot be changed", delegate {
                foreach (var value in new[] { new StartupValue(true, RegistryValueKind.String, "Foreign generated startup command"), new StartupValue(true, RegistryValueKind.Binary, new byte[] { 1, 2 }) }) {
                    var store = new ModernStartupStore { State = value }; var draft = new StartupEdit(ModernRegistration(store)); Check(!draft.Available && !draft.Change.HasValue, "Foreign value editable"); Throws<SafetyException>(() => draft.Set(true)); Check(draft.Save().Conflict && store.State.Same(value) && store.Writes == 0, "Foreign entry replaced");
                }
                var owned = new ModernStartupStore(); var registration = ModernRegistration(owned);
                var other = new StartupRegistration(owned, @"D:\Other copy\EasyEject.Modern.exe", path => new ModernStartupPin(() => {}), true);
                Throws<SafetyException>(() => registration.Apply(other.Inspect(), true));
                var classic = new StartupRegistration(owned, @"C:\EasyEject.Windows.exe", path => new ModernStartupPin(() => {})); Throws<SafetyException>(() => registration.Apply(classic.Inspect(), true)); Check(owned.Writes == 0, "Foreign review registered this copy");
            });
            Test("modern stale login entry refuses without writes and draft cannot retry", delegate {
                var store = new ModernStartupStore(); var draft = new StartupEdit(ModernRegistration(store)); draft.Set(true); store.State = new StartupValue(true, RegistryValueKind.String, "Generated later foreign record");
                Throws<SafetyException>(() => draft.Save()); Throws<SafetyException>(() => draft.Save()); Check(store.Writes == 0 && (string)store.State.Value == "Generated later foreign record", "Stale draft overwrote new record");
            });
            Test("modern unchanged login save reports fresh status without repairing a later entry", delegate {
                var store = new ModernStartupStore { State = new StartupValue(true, RegistryValueKind.String, StartupRegistration.Command(ModernStartupPath, true)) };
                var draft = new StartupEdit(ModernRegistration(store)); Check(draft.Snapshot.Here && !draft.Change.HasValue, "Fixture login not registered");
                store.State = new StartupValue(true, RegistryValueKind.String, "Generated later foreign command");
                var result = draft.Save(); Check(result.Conflict && !result.Here && store.Writes == 0 && (string)store.State.Value == "Generated later foreign command", "Unchanged save returned stale success or repaired login");
                Throws<SafetyException>(() => draft.Save());
            });
            Test("modern failed login write and uncertain readback keep precise consumed outcomes", delegate {
                var refused = new ModernStartupStore { RefuseWrite = true }; var draft = new StartupEdit(ModernRegistration(refused)); draft.Set(true); Throws<UnauthorizedAccessException>(() => draft.Save()); Throws<SafetyException>(() => draft.Save()); Check(refused.Writes == 0, "Refused write repeated");
                var uncertain = new ModernStartupStore { RefuseReadback = true }; draft = new StartupEdit(ModernRegistration(uncertain)); draft.Set(true); Check(Throws<SafetyException>(() => draft.Save()).Message.Contains("may have been applied"), "Uncertain publication claimed rollback"); Throws<SafetyException>(() => draft.Save()); Check(uncertain.Writes == 1, "Uncertain publication retried");
            });
            Test("modern login choice captured before settings write cannot be changed by saver callback", delegate {
                var store = new ModernStartupStore(); var login = new StartupEdit(ModernRegistration(store)); bool? choice = login.Change;
                var settings = new AppSettingsEdit(new AppSettings(), p => login.Set(true)); var saved = settings.Save();
                Throws<SafetyException>(() => login.SaveReviewed(choice)); Check(saved.NotificationsEnabled && store.Writes == 0, "Callback enabled unreviewed login");
            });
            Test("modern login worker rejects cancel edits and duplicate save at registry callback", delegate {
                var store = new ModernStartupStore(); var draft = new StartupEdit(ModernRegistration(store)); draft.Set(true);
                store.DuringChange = () => { Throws<InvalidOperationException>(() => draft.Cancel()); Throws<SafetyException>(() => draft.Set(false)); Throws<SafetyException>(() => draft.Save()); };
                Check(draft.Save().Here && store.Writes == 1, "Worker ownership lost or duplicated login");
            });
            Test("modern launch parser accepts only normal or exact startup and no operation arguments", delegate {
                Check(!StartupLaunch.Parse(new string[0]) && StartupLaunch.Parse(new[] { "--startup" }), "Normal/startup routing wrong");
                foreach (string[] bad in new[] { new[] { "--eject" }, new[] { "--export" }, new[] { "--startup", "--eject" }, new[] { "--Startup" } }) Throws<SafetyException>(() => StartupLaunch.Parse(bad));
            });
            Test("modern hidden startup initializes and monitors once without show", delegate {
                int initialized = 0, monitored = 0, shown = 0; var queue = new List<Action>(); using (var gate = new TrayStartupGate()) {
                    gate.Start(true, () => initialized++, () => monitored++, a => { queue.Add(a); return true; }, () => shown++);
                    Check(initialized == 1 && monitored == 1 && shown == 0 && queue.Count == 0, "Hidden startup showed or failed monitor");
                    Check(gate.Request(true) && queue.Count == 0, "Repeated login showed window"); Throws<SafetyException>(() => gate.Start(true, () => initialized++, () => monitored++, a => true, () => shown++));
                }
            });
            Test("modern normal launch and pre-initialization wakeup reveal through owned queue", delegate {
                foreach (bool startup in new[] { false, true }) {
                    int shown = 0; var queue = new List<Action>(); using (var gate = new TrayStartupGate()) {
                        if (startup) gate.Request(false);
                        gate.Start(startup, () => {}, () => {}, a => { queue.Add(a); return true; }, () => shown++);
                        gate.Request(false); gate.Request(false); Check(queue.Count == 1 && shown == 0, "Initial/duplicate wakeup failed coalescing"); queue[0](); Check(shown == 1, "Normal wakeup lost");
                    }
                }
            });
            Test("modern shutdown revokes queued startup reveal and initialization failure closes authority", delegate {
                Action queued = null; int shown = 0; var gate = new TrayStartupGate(); gate.Start(false, () => {}, () => {}, a => { queued = a; return true; }, () => shown++); gate.Dispose(); queued(); Check(shown == 0 && !gate.Request(false), "Closed launch revealed");
                using (var failed = new TrayStartupGate()) { Throws<IOException>(() => failed.Start(false, () => { throw new IOException("Generated initialization failure"); }, () => { throw new Exception("No monitoring expected"); }, a => true, () => shown++)); Check(!failed.Request(false) && shown == 0, "Failed launch retained authority"); }
            });
            Test("modern wakeup during monitor initialization is retained without restarting monitoring", delegate {
                int monitors = 0, shown = 0; var queue = new List<Action>(); using (var gate = new TrayStartupGate()) {
                    gate.Start(true, () => {}, () => { monitors++; gate.Request(false); }, a => { queue.Add(a); return true; }, () => shown++);
                    Check(monitors == 1 && queue.Count == 1, "Initialization wakeup lost or monitor duplicated"); queue[0](); Check(shown == 1, "Retained wakeup not delivered");
                }
            });
        }
    }
}
