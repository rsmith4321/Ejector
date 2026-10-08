using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace EasyEject {
    public static partial class Tests {
        // Read-only ToolHelp inventory binds a live tool to the already-owned
        // challenged child. Never select or terminate a process by name alone.
        static Process BusyShareTool(int owner, string expectedPath) {
            var candidates = new List<int>(); var observed = new List<string>();
            using (var snapshot = BusyShareNative.CreateToolhelp32Snapshot(2, 0)) {
                if (snapshot.IsInvalid) throw Native.Error("Cannot inspect owned sharing worker ancestry");
                var entry = new BusyShareNative.Entry { Size = (uint)Marshal.SizeOf(typeof(BusyShareNative.Entry)) };
                if (!BusyShareNative.Process32FirstW(snapshot, ref entry)) throw Native.Error("Cannot read owned sharing worker ancestry");
                do {
                    if (entry.Parent == (uint)owner) {
                        observed.Add(entry.Id + ":" + entry.Name);
                        if (entry.Name.Equals(Path.GetFileName(expectedPath), StringComparison.OrdinalIgnoreCase)) candidates.Add(checked((int)entry.Id));
                    }
                }
                while (BusyShareNative.Process32NextW(snapshot, ref entry));
                if (Marshal.GetLastWin32Error() != 18) throw Native.Error("Owned sharing worker ancestry ended unexpectedly");
            }
            Check(candidates.Count == 1, "Owned busy sharing worker does not have exactly one live encoder; owner=" + owner
                + "; candidates=" + candidates.Count + "; children=" + String.Join(",", observed));
            var tool = Process.GetProcessById(candidates.Single());
            try {
                // Force a retained process handle before checking start/image;
                // subsequent WaitForExit observes this instance, not a reused PID.
                Check(tool.Handle != IntPtr.Zero && !tool.HasExited
                    && SafePaths.Normalize(tool.MainModule.FileName).Equals(SafePaths.Normalize(expectedPath), StringComparison.OrdinalIgnoreCase),
                    "Owned busy child is not the exact selected live encoder");
                return tool;
            } catch { tool.Dispose(); throw; }
        }

        static void StopBusyShareOwner(Process child, string nonce, string expectedTool) {
            Process tool = null;
            try {
                var error = child.StandardError.ReadToEndAsync();
                var ready = child.StandardOutput.ReadLineAsync();
                Check(ready.Wait(30000), "Owned live encoder checkpoint timed out");
                string line = ready.GetAwaiter().GetResult();
                if (line == null) {
                    Check(child.WaitForExit(5000), "Owned live encoder closed output without becoming terminal");
                    throw new Exception("Owned live encoder checkpoint exited " + child.ExitCode + ": " + error.GetAwaiter().GetResult());
                }
                var parts = line.Split(' ');
                int pid = 0, frame = 0; long ticks = 0;
                Check(parts.Length == 5 && parts[0] == "OWNED_BUSY_CRASH_READY" && parts[1] == nonce
                    && Int32.TryParse(parts[2], NumberStyles.None, CultureInfo.InvariantCulture, out pid)
                    && Int64.TryParse(parts[3], NumberStyles.None, CultureInfo.InvariantCulture, out ticks)
                    && Int32.TryParse(parts[4], NumberStyles.None, CultureInfo.InvariantCulture, out frame) && frame >= 1 && frame < 90,
                    "Exact owned live encoder checkpoint was not reached: " + line);
                tool = BusyShareTool(child.Id, expectedTool);
                Check(tool.Id == pid && pid > 0 && ticks > 0
                    && tool.Id.ToString(CultureInfo.InvariantCulture) == parts[2]
                    && tool.StartTime.ToUniversalTime().Ticks.ToString(CultureInfo.InvariantCulture) == parts[3]
                    && tool.StartTime.ToUniversalTime() >= child.StartTime.ToUniversalTime()
                    && !tool.HasExited && !child.HasExited, "Live encoder identity changed before owned owner termination");
                Check((DateTime.UtcNow - tool.StartTime.ToUniversalTime()).TotalSeconds < 2.5, "Paced four-second encoder observation arrived too late for abrupt-loss evidence");
                var stopped = Stopwatch.StartNew();
                child.Kill();
                Check(child.WaitForExit(30000) && child.ExitCode != 0, "Owned busy sharing process loss was not observed");
                // Do not kill the encoder to manufacture containment evidence.
                Check(tool.WaitForExit(2000) && stopped.ElapsedMilliseconds < 2000, "Owner loss did not promptly stop its live encoder through job closure");
                Console.WriteLine("Owned real sharing child " + child.Id + ": abrupt termination during live encoder " + parts[2]
                    + " startUtcTicks=" + parts[3] + "; progressFrame=" + frame + "; encoder exit=" + tool.ExitCode
                    + "; stopMs=" + stopped.ElapsedMilliseconds + "; eject tokens=0 at checkpoint");
            } finally {
                if (!child.HasExited) { child.Kill(); child.WaitForExit(30000); }
                if (tool != null) {
                    // Failure-only cleanup of the retained exact owned handle;
                    // the failed assertion above remains a failed test result.
                    if (!tool.HasExited && !tool.WaitForExit(5000)) { tool.Kill(); tool.WaitForExit(30000); }
                    tool.Dispose();
                }
            }
        }

        static class BusyShareNative {
            [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
            internal struct Entry {
                internal uint Size, Usage, Id;
                internal UIntPtr Heap;
                internal uint Module, Threads, Parent;
                internal int Priority;
                internal uint Flags;
                [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)] internal string Name;
            }
            [DllImport("kernel32.dll", SetLastError = true)] internal static extern SafeFileHandle CreateToolhelp32Snapshot(uint flags, uint process);
            [DllImport("kernel32.dll", CharSet = CharSet.Unicode, ExactSpelling = true, SetLastError = true)] internal static extern bool Process32FirstW(SafeFileHandle snapshot, ref Entry entry);
            [DllImport("kernel32.dll", CharSet = CharSet.Unicode, ExactSpelling = true, SetLastError = true)] internal static extern bool Process32NextW(SafeFileHandle snapshot, ref Entry entry);
        }
    }
}
