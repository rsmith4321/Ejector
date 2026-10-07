using System;

namespace EasyEject {
    // Exercise the actual runtime with fixture events/disposable files/fake removal APIs.
    // Native monitoring is a separate explicit read-only command, never real removal.
    public static partial class Tests {
        static int passed, failed;
        static void Check(bool value, string message) { if (!value) throw new Exception(message); }
        static T Throws<T>(Action action) where T : Exception {
            try { action(); } catch (T ex) { return ex; }
            throw new Exception("Expected " + typeof(T).Name);
        }
        static void Test(string name, Action action) {
            try { action(); passed++; Console.WriteLine("PASS " + name); }
            catch (Exception ex) { failed++; Console.WriteLine("FAIL " + name + ": " + ex); }
        }
        [STAThread]
        static int Main(string[] args) {
            if (args.Length > 0 && args[0] == "--reopen-signal-fixture") return ReopenSignalFixture(args);
            if (args.Length == 1 && args[0] == "--monitor-native") return NativeStorageMonitorSmoke();
            if (args.Length != 0) return 2;
            WindowReopenTests();
            DefaultsEditTests();
            StorageControllerTests();
            ProfileEditTests();
            ImportOperationTests();
            SharingOperationTests();
            ResumeOperationTests();
            SettingsOperationTests();
            GroupedOperationTests();
            StartupOperationTests();
            DiagnosticsOperationTests();
            IssuePersistenceTests();
            ReleaseOperationTests();
            Console.WriteLine(passed + " passed, " + failed + " failed");
            return failed == 0 ? 0 : 1;
        }
    }
}
