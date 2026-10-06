using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.Serialization.Json;
using System.Text;
using System.Threading;
using Microsoft.Win32.SafeHandles;

namespace EasyEject {
    public static partial class Tests {
        static string ShareHelper { get { return Process.GetCurrentProcess().MainModule.FileName; } }
        static string FixtureShareHelper(Fixture fixture) { string path = Path.Combine(fixture.Source, "helper.exe"); if (!File.Exists(path)) File.Copy(ShareHelper, path); return path; }
        static string[] ShareHelperArguments(string mode, params string[] values) { return new[] { "--share-process-fixture", mode }.Concat(values).ToArray(); }
        static byte[] RunShareHelper(Fixture fixture, string mode, Action<string> progress = null, CancellationToken cancel = default(CancellationToken), Action validate = null, double seconds = 10) {
            return new WindowsQuickShareProcess().Run(FixtureShareHelper(fixture), ShareHelperArguments(mode), fixture.Destination, seconds, cancel, validate ?? (() => {}), progress);
        }
        static void CheckShareLogsGone(Fixture fixture) { Check(Directory.GetFileSystemEntries(fixture.Destination).Length == 0, "Own process logs/work remain"); }
        static bool ProcessGone(int id) { try { using (var process = Process.GetProcessById(id)) return process.HasExited; } catch (ArgumentException) { return true; } }
        // Default tests invoke only this helper in an approved disposable fixture working folder.
        static int ShareProcessFixture(string[] args) {
            string allowed = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".codex", "EasyEjectFixtures");
            if (!SafePaths.Within(SafePaths.Normalize(Environment.CurrentDirectory), allowed) || !Environment.CurrentDirectory.Substring(allowed.Length).TrimStart('\\').StartsWith("run-", StringComparison.Ordinal)) return 92;
            Console.OutputEncoding = new UTF8Encoding(false);
            string mode = args[1];
            if (mode == "echo") { using (var output = Console.OpenStandardOutput()) new DataContractJsonSerializer(typeof(string[])).WriteObject(output, args.Skip(2).ToArray()); return 0; }
            if (mode == "stdin") { Console.WriteLine(Console.ReadLine() == null ? "null-input" : "unexpected-input"); return 0; }
            if (mode == "cwd") { Console.WriteLine(Environment.CurrentDirectory); return 0; }
            if (mode == "fail") { Console.Error.WriteLine(new string('e', 2500) + "last failure detail"); return 23; }
            if (mode == "emptyfail") return 17;
            if (mode == "floodout" || mode == "flooderr") { using (var output = mode == "floodout" ? Console.OpenStandardOutput() : Console.OpenStandardError()) { byte[] data = Enumerable.Repeat((byte)'f', 4500000).ToArray(); output.Write(data, 0, data.Length); output.Flush(); } return 0; }
            if (mode == "progress") { for (int i = 1; i <= 3; i++) { Console.WriteLine("out_time_us=" + i + "000000"); Console.Out.Flush(); Thread.Sleep(180); } Console.Write("progress=end"); Console.Out.Flush(); return 0; }
            if (mode == "utf8") {
                byte[] data = Encoding.UTF8.GetBytes("out_time_us=日本\r\nprogress=end\n"); using (var output = Console.OpenStandardOutput()) { for (int i = 0; i < data.Length; i++) { output.Write(data, i, 1); output.Flush(); Thread.Sleep(15); } } return 0;
            }
            if (mode == "handle") {
                bool inherited = false; using (var handle = new SafeFileHandle(new IntPtr(Int64.Parse(args[2])), false)) { try { inherited = Native.FinalPath(handle).Equals(args[3], StringComparison.OrdinalIgnoreCase); } catch (Exception) {} }
                Console.WriteLine(inherited ? "unexpected-inherited-file" : "not-inherited"); return 0;
            }
            Console.WriteLine("parent=" + Process.GetCurrentProcess().Id); Console.Out.Flush();
            if (mode == "spawn" || mode == "spawnexit") {
                using (var child = Process.Start(new ProcessStartInfo(ShareHelper, String.Join(" ", ShareHelperArguments("wait").Select(WindowsQuickShareProcess.Quote))) { UseShellExecute = false, CreateNoWindow = true, WorkingDirectory = Environment.CurrentDirectory })) { Console.WriteLine("child=" + child.Id); Console.Out.Flush(); }
                if (mode == "spawnexit") return 0;
            }
            if (mode == "wait" || mode == "spawn") { for (;;) Thread.Sleep(100); }
            return 93;
        }
        static void QuickShareProcessTests() {
            Test("native Quick Share process refuses replaced working directory before creating logs or child", delegate {
                using (var fixture = new Fixture()) {
                    int checks = 0; string moved = fixture.Destination + "-moved";
                    Throws<SafetyException>(() => RunShareHelper(fixture, "cwd", validate: () => { if (++checks == 2) { Directory.Move(fixture.Destination, moved); Directory.CreateDirectory(fixture.Destination); File.WriteAllText(Path.Combine(fixture.Destination, "foreign.keep"), "retain foreign working directory"); } }));
                    Check(checks == 2 && Directory.Exists(moved) && Directory.GetFileSystemEntries(fixture.Destination).Length == 1 && File.ReadAllText(Path.Combine(fixture.Destination, "foreign.keep")) == "retain foreign working directory", "Native process/log creation or cleanup touched replacement work");
                }
            });
            Test("Quick Share direct argv preserves empty unicode quotes slashes and shell metacharacters", delegate {
                using (var fixture = new Fixture()) {
                    string[] values = { "", "ordinary", "two words", "日本 vidéo", "a\"b", "\\", "path ends \\", "two\\\\\"quotes\"", "& | < > ^ %PATH% $(ignored) `ignored`", "line\nbreak", "tab\tvalue" };
                    byte[] data = new WindowsQuickShareProcess().Run(FixtureShareHelper(fixture), ShareHelperArguments("echo", values), fixture.Destination, 10, CancellationToken.None, () => {});
                    using (var json = new MemoryStream(data)) { var actual = (string[])new DataContractJsonSerializer(typeof(string[])).ReadObject(json); Check(actual.SequenceEqual(values), "Process argv changed or interpreted shell syntax"); }
                    CheckShareLogsGone(fixture);
                }
            });
            Test("Quick Share explicit executable path works with spaces and unicode without PATH lookup", delegate {
                using (var fixture = new Fixture()) {
                    string folder = Path.Combine(fixture.Source, "tool 日本 with spaces"); Directory.CreateDirectory(folder); string executable = Path.Combine(folder, "helper.exe"); File.Copy(ShareHelper, executable);
                    byte[] data = new WindowsQuickShareProcess().Run(executable, ShareHelperArguments("echo", "correct executable"), fixture.Destination, 10, CancellationToken.None, () => {});
                    Check(Encoding.UTF8.GetString(data).Contains("correct executable"), "Explicit module selection failed"); CheckShareLogsGone(fixture);
                }
            });
            Test("Quick Share process gets exact pinned working directory and null standard input", delegate {
                using (var fixture = new Fixture()) { Check(Encoding.UTF8.GetString(RunShareHelper(fixture, "cwd")).Trim() == fixture.Destination, "Work directory drifted"); Check(Encoding.UTF8.GetString(RunShareHelper(fixture, "stdin")).Trim() == "null-input", "Tool can wait on user input"); CheckShareLogsGone(fixture); }
            });
            Test("Quick Share streams progress during execution and preserves complete raw output", delegate {
                using (var fixture = new Fixture()) { var lines = new List<string>(); byte[] output = RunShareHelper(fixture, "progress", line => lines.Add(line)); Check(lines.SequenceEqual(new[] { "out_time_us=1000000", "out_time_us=2000000", "out_time_us=3000000", "progress=end" }) && Encoding.UTF8.GetString(output).EndsWith("progress=end"), "Progress/output lost"); CheckShareLogsGone(fixture); }
            });
            Test("Quick Share incremental progress handles split UTF8 and CRLF without corruption", delegate {
                using (var fixture = new Fixture()) { var lines = new List<string>(); RunShareHelper(fixture, "utf8", line => lines.Add(line)); Check(lines.SequenceEqual(new[] { "out_time_us=日本", "progress=end" }), "Incremental progress corrupted characters/line boundaries"); CheckShareLogsGone(fixture); }
            });
            Test("Quick Share nonzero exit reports bounded last stderr detail and cleans owned logs", delegate {
                using (var fixture = new Fixture()) { var failure = Throws<SafetyException>(() => RunShareHelper(fixture, "fail")); Check(failure.Message.Contains("last failure detail") && failure.Message.Length < 2100, "Exit detail unbounded/lost"); failure = Throws<SafetyException>(() => RunShareHelper(fixture, "emptyfail")); Check(failure.Message.Contains("17"), "Empty error concealed exit status"); CheckShareLogsGone(fixture); }
            });
            Test("Quick Share stdout and stderr floods fail safely without full-pipe deadlock", delegate {
                using (var fixture = new Fixture()) { foreach (string mode in new[] { "floodout", "flooderr" }) { var elapsed = Stopwatch.StartNew(); var failure = Throws<SafetyException>(() => RunShareHelper(fixture, mode)); Check(failure.Message.Contains("excessive") && elapsed.Elapsed.TotalSeconds < 10, "Flood accepted or process deadlocked"); CheckShareLogsGone(fixture); } }
            });
            Test("Quick Share cancellation before launch creates no process logs", delegate {
                using (var fixture = new Fixture()) using (var token = new CancellationTokenSource()) { token.Cancel(); Throws<OperationCanceledException>(() => RunShareHelper(fixture, "wait", cancel: token.Token)); CheckShareLogsGone(fixture); }
            });
            Test("Quick Share preflight guard failure launches nothing and leaves other fixture files intact", delegate {
                using (var fixture = new Fixture()) { fixture.Put("original.keep", "unaltered fixture original"); Throws<SafetyException>(() => RunShareHelper(fixture, "wait", validate: () => { throw new SafetyException("fixture destination changed before launch"); })); Check(File.ReadAllText(Path.Combine(fixture.Source, "original.keep")) == "unaltered fixture original", "Preflight changed original"); CheckShareLogsGone(fixture); }
            });
            Test("Quick Share cancellation during execution confirms parent exit and releases private work", delegate {
                using (var fixture = new Fixture()) using (var token = new CancellationTokenSource()) { int parent = 0; Throws<OperationCanceledException>(() => RunShareHelper(fixture, "wait", line => { if (line.StartsWith("parent=")) { parent = Int32.Parse(line.Substring(7)); token.Cancel(); } }, token.Token)); Check(parent > 0 && ProcessGone(parent), "Canceled worker remains live"); CheckShareLogsGone(fixture); }
            });
            Test("Quick Share owned-job cancellation stops descendants as well as their parent", delegate {
                using (var fixture = new Fixture()) using (var token = new CancellationTokenSource()) { int parent = 0, child = 0; Throws<OperationCanceledException>(() => RunShareHelper(fixture, "spawn", line => { if (line.StartsWith("parent=")) parent = Int32.Parse(line.Substring(7)); if (line.StartsWith("child=")) { child = Int32.Parse(line.Substring(6)); token.Cancel(); } }, token.Token)); Check(parent > 0 && child > 0 && ProcessGone(parent) && ProcessGone(child), "Owned descendant survived cancellation"); CheckShareLogsGone(fixture); }
            });
            Test("Quick Share successful parent exit cannot leave a worker descendant running", delegate {
                using (var fixture = new Fixture()) { int child = 0; RunShareHelper(fixture, "spawnexit", line => { if (line.StartsWith("child=")) child = Int32.Parse(line.Substring(6)); }); Check(child > 0 && ProcessGone(child), "Success left descendant running"); CheckShareLogsGone(fixture); }
            });
            Test("Quick Share timeout stops its process with no automatic retry", delegate {
                using (var fixture = new Fixture()) { int parent = 0; var elapsed = Stopwatch.StartNew(); var failure = Throws<SafetyException>(() => RunShareHelper(fixture, "wait", line => { if (line.StartsWith("parent=")) parent = Int32.Parse(line.Substring(7)); }, seconds: 2)); Check(failure.Message.Contains("too long") && parent > 0 && ProcessGone(parent) && elapsed.Elapsed.TotalSeconds < 8, "Deadline/exit confirmation failed"); CheckShareLogsGone(fixture); }
            });
            Test("Quick Share cancellation from final progress cannot return successful output", delegate {
                using (var fixture = new Fixture()) using (var token = new CancellationTokenSource()) { bool final = false; Throws<OperationCanceledException>(() => RunShareHelper(fixture, "progress", line => { if (line == "progress=end") { final = true; token.Cancel(); } }, token.Token)); Check(final, "Final progress boundary not exercised"); CheckShareLogsGone(fixture); }
            });
            Test("Quick Share destination guard loss during execution stops the owned worker", delegate {
                using (var fixture = new Fixture()) { int parent = 0; bool lost = false; Throws<SafetyException>(() => RunShareHelper(fixture, "wait", line => { if (line.StartsWith("parent=")) { parent = Int32.Parse(line.Substring(7)); lost = true; } }, validate: () => { if (lost) throw new SafetyException("fixture destination disconnected"); })); Check(parent > 0 && ProcessGone(parent), "Guard failure left process live"); CheckShareLogsGone(fixture); }
            });
            Test("Quick Share progress callback failure stops rather than concealing a running tool", delegate {
                using (var fixture = new Fixture()) { int parent = 0; Throws<InvalidOperationException>(() => RunShareHelper(fixture, "wait", line => { if (line.StartsWith("parent=")) { parent = Int32.Parse(line.Substring(7)); throw new InvalidOperationException("fixture progress failure"); } })); Check(parent > 0 && ProcessGone(parent), "Callback failure leaked worker"); CheckShareLogsGone(fixture); }
            });
            Test("Quick Share whitelist excludes unrelated inheritable file handles", delegate {
                using (var fixture = new Fixture()) {
                    string sentinel = Path.Combine(fixture.Source, "private-sentinel");
                    using (var handle = QuickShareProcessNative.Inheritable(security => Native.CreateFile(sentinel, 0x40000000, 1, security, 1, 0x00200000, IntPtr.Zero))) {
                        Check(!handle.IsInvalid, "Could not create inheritance fixture"); var data = new WindowsQuickShareProcess().Run(FixtureShareHelper(fixture), ShareHelperArguments("handle", handle.DangerousGetHandle().ToInt64().ToString(), sentinel), fixture.Destination, 10, CancellationToken.None, () => {}); Check(Encoding.UTF8.GetString(data).Trim() == "not-inherited", "Unrelated handle inherited by tool");
                    }
                    CheckShareLogsGone(fixture);
                }
            });
            Test("Quick Share missing bad or redirected executables fail without operating on targets", delegate {
                using (var fixture = new Fixture()) {
                    var runner = new WindowsQuickShareProcess(); string missing = Path.Combine(fixture.Source, "missing.exe"); Throws<System.ComponentModel.Win32Exception>(() => runner.Run(missing, ShareHelperArguments("echo"), fixture.Destination, 1, CancellationToken.None, () => {}));
                    string bad = Path.Combine(fixture.Source, "bad.exe"); File.WriteAllText(bad, "not an executable"); Throws<System.ComponentModel.Win32Exception>(() => runner.Run(bad, ShareHelperArguments("echo"), fixture.Destination, 1, CancellationToken.None, () => {})); Check(File.ReadAllText(bad) == "not an executable", "Rejected tool changed");
                    string tools = Path.Combine(fixture.Source, "real tools"); Directory.CreateDirectory(tools); File.Copy(ShareHelper, Path.Combine(tools, "helper.exe"));
                    string link = Path.Combine(fixture.Source, "tools-link"); CreateJunction(fixture, link, tools); Throws<SafetyException>(() => runner.Run(Path.Combine(link, "helper.exe"), ShareHelperArguments("echo"), fixture.Destination, 1, CancellationToken.None, () => {})); CheckShareLogsGone(fixture);
                }
            });
            Test("Quick Share redirected work folder and locked executable cannot launch", delegate {
                using (var fixture = new Fixture()) { string link = Path.Combine(fixture.Source, "work-link"); CreateJunction(fixture, link, fixture.Destination); var runner = new WindowsQuickShareProcess(); Throws<SafetyException>(() => runner.Run(ShareHelper, ShareHelperArguments("echo"), link, 1, CancellationToken.None, () => {}));
                    string locked = Path.Combine(fixture.Source, "locked.exe"); File.Copy(ShareHelper, locked); using (var held = new FileStream(locked, FileMode.Open, FileAccess.ReadWrite, FileShare.None)) Throws<System.ComponentModel.Win32Exception>(() => runner.Run(locked, ShareHelperArguments("echo"), fixture.Destination, 1, CancellationToken.None, () => {})); CheckShareLogsGone(fixture);
                }
            });
            Test("Quick Share invalid argv deadlines and shell-script paths refuse before log creation", delegate {
                using (var fixture = new Fixture()) { var runner = new WindowsQuickShareProcess(); foreach (double deadline in new[] { 0.0, -1, Double.NaN, Double.PositiveInfinity }) Throws<SafetyException>(() => runner.Run(ShareHelper, ShareHelperArguments("echo"), fixture.Destination, deadline, CancellationToken.None, () => {}));
                    foreach (string[] values in new[] { (string[])null, new string[] { null }, new[] { "nul\0argument" }, new[] { new string('x', 32767) }, new[] { new string('x', 20000), new string('y', 20000) } }) Throws<SafetyException>(() => runner.Run(ShareHelper, values, fixture.Destination, 1, CancellationToken.None, () => {}));
                    Throws<SafetyException>(() => runner.Run(Path.Combine(fixture.Source, "tool.cmd"), ShareHelperArguments("echo"), fixture.Destination, 1, CancellationToken.None, () => {})); CheckShareLogsGone(fixture);
                }
            });
        }
    }
}
