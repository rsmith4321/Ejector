using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using Microsoft.Win32.SafeHandles;

namespace EasyEject {
    internal interface IQuickShareProcess {
        byte[] Run(string executable, string[] arguments, string directory, double timeoutSeconds, CancellationToken cancel, Action validate, Action<string> progress = null);
    }
    internal sealed class WindowsQuickShareProcess : IQuickShareProcess {
        internal const int MaximumOutput = 4000000;
        // Native CreateProcessW gets an explicit executable and an argv command line, never a shell.
        // The suspended process enters our non-breakaway job before any tool code can run.
        public byte[] Run(string executable, string[] arguments, string directory, double timeoutSeconds, CancellationToken cancel, Action validate, Action<string> progress = null) {
            executable = SafePaths.Normalize(executable); directory = SafePaths.Normalize(directory);
            if (!Path.GetExtension(executable).Equals(".exe", StringComparison.OrdinalIgnoreCase) || !QuickShareProbe.Finite(timeoutSeconds) || timeoutSeconds <= 0) throw new SafetyException("Quick Share requires a local executable and a finite positive process deadline.");
            string command = Command(executable, arguments);
            Action checkOperation = () => { cancel.ThrowIfCancellationRequested(); if (validate != null) validate(); };
            checkOperation();
            using (var directories = SafePaths.HoldDirectories(Path.GetDirectoryName(executable), directory)) using (SafePaths.ReadFile(executable)) {
                Action check = () => { directories.Validate(); checkOperation(); directories.Validate(); };
                check();
                using (var input = QuickShareProcessNative.Inheritable((security) => Native.CreateFile("NUL", 0x80000000, 3, security, 3, 0, IntPtr.Zero))) {
                    if (input.IsInvalid) throw Native.Error("Cannot prepare null tool input");
                    using (var output = new ProcessLog(directory, "stdout")) using (var error = new ProcessLog(directory, "stderr")) {
                        check();
                        using (var child = new OwnedShareProcess(executable, command, directory, input, output.Writer, error.Writer)) {
                            check(); child.Resume(); var clock = Stopwatch.StartNew(); var lines = new ProgressLines(progress);
                            for (;;) {
                                check(); output.CheckSize(); error.CheckSize(); lines.Feed(output.ReadNew(), false);
                                if (clock.Elapsed.TotalSeconds >= timeoutSeconds) throw new SafetyException("Quick Share took too long. The verified originals remain saved; retry is manual.");
                                uint wait = child.Wait(100); if (wait == 0) break;
                                if (wait != 258) throw Native.Error("Cannot wait for the Quick Share tool");
                            }
                            uint code = child.ExitCode;
                            // A successful parent cannot leave a worker holding/writing private output.
                            child.Stop(); check(); output.CheckSize(); error.CheckSize(); lines.Feed(output.ReadNew(), true); check();
                            if (code != 0) {
                                byte[] detail = error.ReadAll(); string message = Encoding.UTF8.GetString(detail.Skip(Math.Max(0, detail.Length - 2000)).ToArray()).Trim();
                                throw new SafetyException("Quick Share could not finish: " + (message.Length == 0 ? "tool exited with " + code + "." : message));
                            }
                            return output.ReadAll();
                        }
                    }
                }
            }
        }
        internal static string Command(string executable, string[] arguments) {
            if (arguments == null) throw new SafetyException("Quick Share process arguments are missing.");
            var command = new StringBuilder(Quote(executable));
            foreach (string arg in arguments) { command.Append(' ').Append(Quote(arg)); if (command.Length >= 32767) throw new SafetyException("Quick Share process arguments exceed Windows' command-line limit."); }
            if (command.Length >= 32767) throw new SafetyException("Quick Share executable command is too long."); return command.ToString();
        }
        internal static string Quote(string value) {
            if (value == null || value.IndexOf('\0') >= 0 || value.Length >= 32767) throw new SafetyException("Quick Share process arguments are invalid.");
            var text = new StringBuilder("\""); int slashes = 0;
            foreach (char character in value) {
                if (character == '\\') { slashes++; continue; }
                if (character == '"') text.Append('\\', slashes * 2 + 1).Append('"');
                else text.Append('\\', slashes).Append(character);
                slashes = 0;
            }
            return text.Append('\\', slashes * 2).Append('"').ToString();
        }
        sealed class ProgressLines {
            readonly Action<string> report; readonly Decoder decoder = Encoding.UTF8.GetDecoder(); readonly StringBuilder pending = new StringBuilder();
            internal ProgressLines(Action<string> report) { this.report = report; }
            internal void Feed(byte[] bytes, bool final) {
                if (report == null) return;
                char[] characters = new char[Encoding.UTF8.GetMaxCharCount(bytes.Length)]; int count = decoder.GetChars(bytes, 0, bytes.Length, characters, 0, final);
                for (int i = 0; i < count; i++) { if (characters[i] == '\n') { report(pending.ToString().TrimEnd('\r')); pending.Clear(); } else pending.Append(characters[i]); }
                if (final && pending.Length != 0) { report(pending.ToString()); pending.Clear(); }
            }
        }
        sealed class ProcessLog : IDisposable {
            internal readonly SafeFileHandle Writer; readonly FileStream reader;
            internal ProcessLog(string directory, string kind) {
                string path = SafePaths.Normalize(Path.Combine(directory, ".easy-share-process-" + Guid.NewGuid().ToString("N") + "-" + kind));
                Writer = QuickShareProcessNative.Inheritable(security => Native.CreateFile(path, 0x40010000, 1, security, 1, 0x80200000, IntPtr.Zero));
                if (Writer.IsInvalid) { Writer.Dispose(); throw Native.Error("Cannot create exclusive private process log"); }
                SafeFileHandle read = null;
                try {
                    Native.Info(Writer); if (!Native.FinalPath(Writer).Equals(path, StringComparison.OrdinalIgnoreCase)) throw new SafetyException("The private process log resolves elsewhere.");
                    // Separate file objects keep reading from changing the child's output offset.
                    read = Native.CreateFile(path, 0x80000000, 7, IntPtr.Zero, 3, 0x00200000, IntPtr.Zero);
                    if (read.IsInvalid) throw Native.Error("Cannot read private process output");
                    Native.Info(read); if (!Native.FinalPath(read).Equals(path, StringComparison.OrdinalIgnoreCase)) throw new SafetyException("The private process reader resolves elsewhere.");
                    reader = new FileStream(read, FileAccess.Read, 4096, false); read = null;
                    Native.DeleteOnClose(Writer, "Cannot arrange owned process-log cleanup");
                } catch { if (read != null) read.Dispose(); if (reader != null) reader.Dispose(); try { Native.DeleteOnClose(Writer); } finally { Writer.Dispose(); } throw; }
            }
            long BoundedLength() { long length = reader.Length; if (length >= MaximumOutput) throw new SafetyException("Quick Share stopped after excessive encoder output. The verified originals remain saved."); return length; }
            internal void CheckSize() { BoundedLength(); }
            internal byte[] ReadNew() { long count = BoundedLength() - reader.Position; if (count < 0) throw new SafetyException("Quick Share process output changed unexpectedly."); return Read((int)count); }
            byte[] Read(int count) { var bytes = new byte[count]; int offset = 0, read; while (offset < count && (read = reader.Read(bytes, offset, count - offset)) > 0) offset += read; if (offset != count) throw new SafetyException("Quick Share could not read complete tool output."); return bytes; }
            internal byte[] ReadAll() { int length = (int)BoundedLength(); reader.Position = 0; return Read(length); }
            public void Dispose() { try { reader.Dispose(); } finally { Writer.Dispose(); } }
        }
    }
    internal sealed class OwnedShareProcess : IDisposable {
        readonly SafeFileHandle job; SafeFileHandle process, thread; bool assigned, stopped;
        internal OwnedShareProcess(string executable, string command, string directory, SafeFileHandle input, SafeFileHandle output, SafeFileHandle error) {
            job = QuickShareProcessNative.CreateJobObject(IntPtr.Zero, null);
            try {
                if (job.IsInvalid) throw Native.Error("Cannot create the Quick Share worker job");
                var limit = new QuickShareProcessNative.ExtendedLimit(); limit.Basic.Flags = 0x2000; // KILL_ON_JOB_CLOSE, no breakaway
                if (!QuickShareProcessNative.SetInformationJobObject(job, 9, ref limit, (uint)Marshal.SizeOf(limit))) throw Native.Error("Cannot constrain the Quick Share worker job");
                using (var attributes = new QuickShareProcessNative.HandleList(input, output, error)) {
                    var start = new QuickShareProcessNative.StartInfoEx { Attributes = attributes.Pointer };
                    start.Info.Size = (uint)Marshal.SizeOf(start); start.Info.Flags = 0x101; start.Info.Input = input.DangerousGetHandle(); start.Info.Output = output.DangerousGetHandle(); start.Info.Error = error.DangerousGetHandle();
                    QuickShareProcessNative.ProcessInfo value;
                    bool created = QuickShareProcessNative.CreateProcess(executable, new StringBuilder(command), IntPtr.Zero, IntPtr.Zero, true, 0x08080004, IntPtr.Zero, directory, ref start, out value);
                    int createError = Marshal.GetLastWin32Error(); GC.KeepAlive(input); GC.KeepAlive(output); GC.KeepAlive(error);
                    if (!created) throw new System.ComponentModel.Win32Exception(createError, "Cannot start the selected Quick Share executable");
                    process = new SafeFileHandle(value.Process, true); thread = new SafeFileHandle(value.Thread, true);
                }
                if (!QuickShareProcessNative.AssignProcessToJobObject(job, process)) throw Native.Error("Cannot contain the Quick Share worker; it was not allowed to run");
                assigned = true;
            } catch { Dispose(); throw; }
        }
        internal void Resume() { if (QuickShareProcessNative.ResumeThread(thread) == UInt32.MaxValue) throw Native.Error("Cannot resume the contained Quick Share worker"); }
        internal uint Wait(uint milliseconds) { return QuickShareProcessNative.WaitForSingleObject(process, milliseconds); }
        internal uint ExitCode { get { uint result; if (!QuickShareProcessNative.GetExitCodeProcess(process, out result)) throw Native.Error("Cannot read Quick Share tool exit status"); return result; } }
        internal void Stop() {
            if (stopped || process == null || process.IsInvalid) return;
            if (assigned) {
                if (!QuickShareProcessNative.TerminateJobObject(job, 1)) throw Native.Error("Cannot stop the owned Quick Share workers");
                var deadline = Stopwatch.StartNew();
                for (;;) {
                    QuickShareProcessNative.Accounting value; uint returned;
                    if (!QuickShareProcessNative.QueryInformationJobObject(job, 1, out value, (uint)Marshal.SizeOf(typeof(QuickShareProcessNative.Accounting)), out returned)) throw Native.Error("Cannot confirm Quick Share workers stopped");
                    if (value.ActiveProcesses == 0) break;
                    if (deadline.Elapsed.TotalSeconds >= 5) throw new SafetyException("Quick Share could not confirm its workers stopped. Keep the destination connected and review private work.");
                    Thread.Sleep(20);
                }
            } else if (Wait(0) == 258 && !QuickShareProcessNative.TerminateProcess(process, 1)) throw Native.Error("Cannot stop the suspended Quick Share worker");
            if (Wait(5000) != 0) throw new SafetyException("Quick Share could not confirm its process exit. Review private work before retrying.");
            stopped = true;
        }
        public void Dispose() { try { Stop(); } finally { job.Dispose(); if (thread != null) thread.Dispose(); if (process != null) process.Dispose(); } }
    }
    internal static class QuickShareProcessNative {
        [StructLayout(LayoutKind.Sequential)] internal struct Security { internal int Size; internal IntPtr Descriptor; internal int Inherit; }
        [StructLayout(LayoutKind.Sequential)] internal struct StartInfo {
            internal uint Size; internal IntPtr Reserved, Desktop, Title; internal uint X, Y, Width, Height, XChars, YChars, Fill, Flags; internal ushort Show, ReservedBytes;
            internal IntPtr ReservedData, Input, Output, Error;
        }
        [StructLayout(LayoutKind.Sequential)] internal struct StartInfoEx { internal StartInfo Info; internal IntPtr Attributes; }
        [StructLayout(LayoutKind.Sequential)] internal struct ProcessInfo { internal IntPtr Process, Thread; internal uint ProcessId, ThreadId; }
        [StructLayout(LayoutKind.Sequential)] internal struct BasicLimit { internal long ProcessTime, JobTime; internal uint Flags; internal UIntPtr MinimumWorking, MaximumWorking; internal uint ActiveProcessLimit; internal UIntPtr Affinity; internal uint Priority, Scheduling; }
        [StructLayout(LayoutKind.Sequential)] internal struct Counters { internal ulong ReadOps, WriteOps, OtherOps, ReadBytes, WriteBytes, OtherBytes; }
        [StructLayout(LayoutKind.Sequential)] internal struct ExtendedLimit { internal BasicLimit Basic; internal Counters Io; internal UIntPtr ProcessMemory, JobMemory, PeakProcessMemory, PeakJobMemory; }
        [StructLayout(LayoutKind.Sequential)] internal struct Accounting { internal long TotalUser, TotalKernel, PeriodUser, PeriodKernel; internal uint PageFaults, TotalProcesses, ActiveProcesses, TerminatedProcesses; }
        [DllImport("kernel32.dll", EntryPoint = "CreateProcessW", CharSet = CharSet.Unicode, SetLastError = true)] internal static extern bool CreateProcess(string executable, StringBuilder command, IntPtr processSecurity, IntPtr threadSecurity, bool inherit, uint flags, IntPtr environment, string directory, ref StartInfoEx start, out ProcessInfo result);
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)] internal static extern SafeFileHandle CreateJobObject(IntPtr security, string name);
        [DllImport("kernel32.dll", SetLastError = true)] internal static extern bool SetInformationJobObject(SafeFileHandle job, int kind, ref ExtendedLimit limit, uint size);
        [DllImport("kernel32.dll", SetLastError = true)] internal static extern bool AssignProcessToJobObject(SafeFileHandle job, SafeFileHandle process);
        [DllImport("kernel32.dll", SetLastError = true)] internal static extern bool TerminateJobObject(SafeFileHandle job, uint code);
        [DllImport("kernel32.dll", SetLastError = true)] internal static extern bool QueryInformationJobObject(SafeFileHandle job, int kind, out Accounting info, uint size, out uint returned);
        [DllImport("kernel32.dll", SetLastError = true)] internal static extern uint ResumeThread(SafeFileHandle thread);
        [DllImport("kernel32.dll", SetLastError = true)] internal static extern uint WaitForSingleObject(SafeFileHandle handle, uint milliseconds);
        [DllImport("kernel32.dll", SetLastError = true)] internal static extern bool GetExitCodeProcess(SafeFileHandle process, out uint code);
        [DllImport("kernel32.dll", SetLastError = true)] internal static extern bool TerminateProcess(SafeFileHandle process, uint code);
        [DllImport("kernel32.dll", SetLastError = true)] static extern bool InitializeProcThreadAttributeList(IntPtr list, int count, int flags, ref UIntPtr size);
        [DllImport("kernel32.dll", SetLastError = true)] static extern bool UpdateProcThreadAttribute(IntPtr list, uint flags, UIntPtr attribute, IntPtr value, UIntPtr size, IntPtr previous, IntPtr returned);
        [DllImport("kernel32.dll")] static extern void DeleteProcThreadAttributeList(IntPtr list);
        internal static T Inheritable<T>(Func<IntPtr, T> action) {
            var value = new Security { Size = Marshal.SizeOf(typeof(Security)), Inherit = 1 }; IntPtr data = Marshal.AllocHGlobal(value.Size);
            try { Marshal.StructureToPtr(value, data, false); return action(data); } finally { Marshal.FreeHGlobal(data); }
        }
        internal sealed class HandleList : IDisposable {
            internal IntPtr Pointer; IntPtr handles; bool initialized;
            internal HandleList(params SafeFileHandle[] values) {
                try {
                    UIntPtr size = UIntPtr.Zero; InitializeProcThreadAttributeList(IntPtr.Zero, 1, 0, ref size); if (size.ToUInt64() == 0 || size.ToUInt64() > 1048576) throw Native.Error("Cannot size Quick Share handle inheritance");
                    Pointer = Marshal.AllocHGlobal((int)size.ToUInt64()); if (!InitializeProcThreadAttributeList(Pointer, 1, 0, ref size)) throw Native.Error("Cannot prepare Quick Share handle inheritance"); initialized = true;
                    handles = Marshal.AllocHGlobal(values.Length * IntPtr.Size); for (int i = 0; i < values.Length; i++) Marshal.WriteIntPtr(handles, i * IntPtr.Size, values[i].DangerousGetHandle());
                    if (!UpdateProcThreadAttribute(Pointer, 0, (UIntPtr)0x20002, handles, (UIntPtr)(values.Length * IntPtr.Size), IntPtr.Zero, IntPtr.Zero)) throw Native.Error("Cannot limit Quick Share inherited handles");
                } catch { Dispose(); throw; }
            }
            public void Dispose() { if (initialized) { DeleteProcThreadAttributeList(Pointer); initialized = false; } if (Pointer != IntPtr.Zero) { Marshal.FreeHGlobal(Pointer); Pointer = IntPtr.Zero; } if (handles != IntPtr.Zero) { Marshal.FreeHGlobal(handles); handles = IntPtr.Zero; } }
        }
    }
}
