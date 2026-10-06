using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Windows.Forms;

namespace EasyEject {
    public static partial class Tests {
        static QuickShareConfigurationSnapshot SaveShareSetup(Fixture f, string helper = null) {
            return QuickShareConfiguration.Save(QuickShareConfiguration.Load(f.Destination), helper ?? FixtureShareHelper(f), helper ?? FixtureShareHelper(f), null, f.Destination, CancellationToken.None);
        }
        static void PumpShareSetup(System.Threading.Tasks.Task task) {
            var deadline = DateTime.UtcNow.AddSeconds(10);
            while (!task.IsCompleted && DateTime.UtcNow < deadline) { Application.DoEvents(); Thread.Sleep(10); }
            Check(task.IsCompleted, "Owned setup task did not finish"); task.GetAwaiter().GetResult();
        }
        static void QuickShareConfigurationTests() {
            Test("Quick Share absent setup reads as unconfigured without probing PATH or starting a tool", delegate { using (var f = new Fixture()) {
                var snapshot = QuickShareConfiguration.Load(f.Destination); Check(!snapshot.Exists && snapshot.Value.Ffmpeg == null && snapshot.Value.Ffprobe == null && snapshot.Value.Lut == null && Directory.GetFiles(f.Destination).Length == 0, "Implicit setup or filesystem work");
                Throws<SafetyException>(() => QuickShareConfiguration.Acquire(snapshot, f.Destination, QuickShareColor.Standard, CancellationToken.None));
            } });
            Test("Quick Share tool selection records exact executable bytes and survives configuration-only reload", delegate { using (var f = new Fixture()) {
                string helper = FixtureShareHelper(f); var saved = SaveShareSetup(f, helper); var fresh = QuickShareConfiguration.Load(f.Destination);
                Check(saved.Exists && fresh.Hash == saved.Hash && fresh.Value.Ffmpeg.Path == helper && fresh.Value.Ffprobe.Path == helper && fresh.Value.Lut == null, "Paths lost");
                using (var stream = SafePaths.ReadFile(helper)) Check(fresh.Value.Ffmpeg.Sha256 == ImportHistory.Hash(stream, CancellationToken.None) && fresh.Value.Ffmpeg.Bytes == stream.Length, "Executable contents not bound");
                Check(Directory.GetFiles(f.Source).Length == 1 && Directory.GetFiles(f.Destination).Select(Path.GetFileName).SequenceEqual(new[] { QuickShareConfiguration.FileName }), "Setup executed a helper or installed an asset");
            } });
            Test("Quick Share saved tool leases work with the native synthetic export transaction and reuse", delegate { using (var f = new Fixture()) {
                var batch = LunaBatch(f); string folder = Path.Combine(f.Root, "configured-helper"); Directory.CreateDirectory(folder); string helper = Path.Combine(folder, "EasyEject.ShareFixture.exe"); File.Copy(ShareHelper, helper);
                var saved = SaveShareSetup(f, helper);
                using (var held = QuickShareConfiguration.Acquire(saved, f.Destination, QuickShareColor.Standard, CancellationToken.None)) {
                    var engine = new QuickShareEngine(new WindowsQuickShareProcess(), new Operations(), freeSpace: _ => UInt64.MaxValue);
                    var result = engine.Run(batch, HistoryIdentity(), new FakeGuard(), held.Tools, QuickSharePreset.Hd, QuickShareColor.Standard, CancellationToken.None, _ => {});
                    Check(result.Created == 1 && result.PublishedCopies == 1 && result.PublishedReceipts == 1 && File.ReadAllText(result.Outputs.Single()).StartsWith("SYNTHETIC_SHARE|1080p|"), "Held selection prevented native protocol orchestration");
                    result = engine.Run(batch, HistoryIdentity(), new FakeGuard(), held.Tools, QuickSharePreset.Hd, QuickShareColor.Standard, CancellationToken.None, _ => {});
                    Check(result.Created == 0 && result.Reused == 1, "Configured recipe did not reuse"); VerifyShareOriginals(batch); CheckSharePrivateGone(batch);
                }
            } });
            Test("Quick Share loading saved paths does not require disconnected tools to be available", delegate { using (var f = new Fixture()) {
                string helper = FixtureShareHelper(f); var saved = SaveShareSetup(f, helper); File.Delete(helper);
                var loaded = QuickShareConfiguration.Load(f.Destination); Check(loaded.Value.Ffmpeg.Path == helper && loaded.Hash == saved.Hash, "Setup reader opened missing tool");
                Throws<System.ComponentModel.Win32Exception>(() => QuickShareConfiguration.Acquire(loaded, f.Destination, QuickShareColor.Standard, CancellationToken.None));
            } });
            Test("Quick Share manual acquisition holds configured bytes and config against write rename and delete", delegate { using (var f = new Fixture()) {
                string helper = FixtureShareHelper(f); var saved = SaveShareSetup(f, helper);
                using (var held = QuickShareConfiguration.Acquire(saved, f.Destination, QuickShareColor.Standard, CancellationToken.None)) {
                    Check(held.Tools.Ffmpeg == helper && held.Tools.Ffprobe == helper && held.Tools.Lut == null, "Wrong tools granted");
                    Throws<IOException>(() => File.AppendAllText(helper, "changed")); Throws<IOException>(() => File.Move(helper, helper + ".moved")); Throws<IOException>(() => File.Delete(helper));
                    Throws<IOException>(() => File.AppendAllText(Path.Combine(f.Destination, QuickShareConfiguration.FileName), "changed"));
                }
                File.AppendAllText(helper, "changed after release");
                Throws<SafetyException>(() => QuickShareConfiguration.Acquire(saved, f.Destination, QuickShareColor.Standard, CancellationToken.None));
            } });
            Test("Quick Share same-size changed executable with restored timestamp refuses until explicitly selected again", delegate { using (var f = new Fixture()) {
                string helper = FixtureShareHelper(f); var saved = SaveShareSetup(f, helper); byte[] bytes = File.ReadAllBytes(helper); DateTime stamp = File.GetLastWriteTimeUtc(helper);
                bytes[bytes.Length - 1] ^= 1; File.WriteAllBytes(helper, bytes); File.SetLastWriteTimeUtc(helper, stamp);
                Throws<SafetyException>(() => QuickShareConfiguration.Acquire(saved, f.Destination, QuickShareColor.Standard, CancellationToken.None));
                var selectedAgain = SaveShareSetup(f, helper); Check(selectedAgain.Value.Ffmpeg.Sha256 != saved.Value.Ffmpeg.Sha256, "Reselection failed to bind new bytes");
            } });
            Test("Quick Share stale setup creation and changed choices refuse save and manual admission", delegate { using (var f = new Fixture()) {
                var absent = QuickShareConfiguration.Load(f.Destination); var first = SaveShareSetup(f);
                Throws<SafetyException>(() => QuickShareConfiguration.Save(absent, null, null, null, f.Destination, CancellationToken.None));
                var latest = QuickShareConfiguration.Save(first, null, null, null, f.Destination, CancellationToken.None);
                Throws<SafetyException>(() => QuickShareConfiguration.Save(first, FixtureShareHelper(f), FixtureShareHelper(f), null, f.Destination, CancellationToken.None));
                Throws<SafetyException>(() => QuickShareConfiguration.Acquire(first, f.Destination, QuickShareColor.Standard, CancellationToken.None));
                Check(QuickShareConfiguration.Load(f.Destination).Hash == latest.Hash && !File.Exists(Path.Combine(f.Destination, ".profile-write.lock")), "Stale edit overwrote current setup or leaked gate");
            } });
            Test("Quick Share identical JSON replacement with restored time refuses an earlier setup choice", delegate { using (var f = new Fixture()) {
                var saved = SaveShareSetup(f); string path = Path.Combine(f.Destination, QuickShareConfiguration.FileName); byte[] bytes = File.ReadAllBytes(path); DateTime stamp = File.GetLastWriteTimeUtc(path);
                File.Move(path, path + ".old"); File.WriteAllBytes(path, bytes); File.SetLastWriteTimeUtc(path, stamp);
                Throws<SafetyException>(() => QuickShareConfiguration.Save(saved, null, null, null, f.Destination, CancellationToken.None));
                Throws<SafetyException>(() => QuickShareConfiguration.Acquire(saved, f.Destination, QuickShareColor.Standard, CancellationToken.None));
            } });
            Test("Quick Share fake LUT refuses without changing setup or installing copyrighted content", delegate { using (var f = new Fixture()) {
                var saved = SaveShareSetup(f); string fake = Path.Combine(f.Source, QuickShareMedia.OfficialLutName); File.WriteAllText(fake, "synthetic nonofficial fixture");
                Throws<SafetyException>(() => QuickShareConfiguration.Save(saved, FixtureShareHelper(f), FixtureShareHelper(f), fake, f.Destination, CancellationToken.None));
                Check(QuickShareConfiguration.Load(f.Destination).Hash == saved.Hash && !File.Exists(QuickShareConfiguration.LutPath(f.Destination)) && Directory.GetFiles(f.Destination).Length == 1 && File.ReadAllText(fake) == "synthetic nonofficial fixture", "Rejected LUT/config/source modified");
            } });
            Test("Quick Share missing optional LUT permits normal color but explicit I-Log needs the official asset", delegate { using (var f = new Fixture()) {
                var saved = SaveShareSetup(f); using (var held = QuickShareConfiguration.Acquire(saved, f.Destination, QuickShareColor.Standard, CancellationToken.None)) Check(held.Tools != null, "Standard needs a LUT");
                Throws<SafetyException>(() => QuickShareConfiguration.Acquire(saved, f.Destination, QuickShareColor.LunaILog, CancellationToken.None));
                Throws<SafetyException>(() => QuickShareConfiguration.Acquire(saved, f.Destination, (QuickShareColor)99, CancellationToken.None));
            } });
            Test("Quick Share normal-color export ignores unavailable optional LUT while I-Log refuses it", delegate { using (var f = new Fixture()) {
                var saved = SaveShareSetup(f); var value = saved.Value;
                value.Lut = new QuickShareToolFile { Path = QuickShareConfiguration.LutPath(f.Destination), Bytes = 100, Sha256 = QuickShareMedia.OfficialLutSha };
                Profiles.WriteJson(value, typeof(QuickShareToolSettings), f.Destination, QuickShareConfiguration.FileName); var selected = QuickShareConfiguration.Load(f.Destination);
                using (var held = QuickShareConfiguration.Acquire(selected, f.Destination, QuickShareColor.Standard, CancellationToken.None)) Check(held.Tools != null, "Missing unused LUT blocked normal color");
                Throws<System.ComponentModel.Win32Exception>(() => QuickShareConfiguration.Acquire(selected, f.Destination, QuickShareColor.LunaILog, CancellationToken.None));
            } });
            Test("Quick Share partial configuration can be saved but cannot admit an export", delegate { using (var f = new Fixture()) {
                var saved = QuickShareConfiguration.Save(QuickShareConfiguration.Load(f.Destination), FixtureShareHelper(f), null, null, f.Destination, CancellationToken.None);
                Check(saved.Value.Ffmpeg != null && saved.Value.Ffprobe == null, "Partial selection lost"); Throws<SafetyException>(() => QuickShareConfiguration.Acquire(saved, f.Destination, QuickShareColor.Standard, CancellationToken.None));
            } });
            Test("Quick Share clearing configuration keeps supplied executables media and other settings", delegate { using (var f = new Fixture()) {
                var saved = SaveShareSetup(f); AppSettings.Save(new AppSettings { ShowNotifications = false }, f.Destination); File.WriteAllText(Path.Combine(f.Destination, "full-quality-original.mp4"), "fixture original");
                var cleared = QuickShareConfiguration.Save(saved, null, null, null, f.Destination, CancellationToken.None);
                Check(cleared.Value.Ffmpeg == null && cleared.Value.Ffprobe == null && File.Exists(FixtureShareHelper(f)) && !AppSettings.Load(f.Destination).NotificationsEnabled && File.ReadAllText(Path.Combine(f.Destination, "full-quality-original.mp4")) == "fixture original", "Clear deleted a tool or touched media/settings");
                Throws<SafetyException>(() => QuickShareConfiguration.Acquire(cleared, f.Destination, QuickShareColor.Standard, CancellationToken.None));
            } });
            Test("Quick Share cancelled setup and acquisition release files and preserve saved choices", delegate { using (var f = new Fixture()) using (var stop = new CancellationTokenSource()) {
                var saved = SaveShareSetup(f); stop.Cancel();
                Throws<OperationCanceledException>(() => QuickShareConfiguration.Save(saved, null, null, null, f.Destination, stop.Token));
                Throws<OperationCanceledException>(() => QuickShareConfiguration.Acquire(saved, f.Destination, QuickShareColor.Standard, stop.Token));
                Check(QuickShareConfiguration.Load(f.Destination).Hash == saved.Hash && !File.Exists(Path.Combine(f.Destination, ".profile-write.lock")), "Cancel changed config or leaked writer gate");
                File.AppendAllText(FixtureShareHelper(f), "released");
            } });
            Test("Quick Share tool scripts empty oversized and redirected files refuse safe selection", delegate { using (var f = new Fixture()) {
                var absent = QuickShareConfiguration.Load(f.Destination); string script = Path.Combine(f.Source, "ffmpeg.cmd"); File.WriteAllText(script, "synthetic");
                Throws<SafetyException>(() => QuickShareConfiguration.Save(absent, script, null, null, f.Destination, CancellationToken.None));
                string empty = Path.Combine(f.Source, "empty.exe"); File.WriteAllText(empty, ""); Throws<SafetyException>(() => QuickShareConfiguration.Save(absent, empty, null, null, f.Destination, CancellationToken.None));
                string huge = Path.Combine(f.Source, "oversized.exe"); using (var stream = new FileStream(huge, FileMode.CreateNew)) stream.SetLength(QuickShareConfiguration.MaximumToolBytes + 1);
                Throws<SafetyException>(() => QuickShareConfiguration.Save(absent, huge, null, null, f.Destination, CancellationToken.None)); File.Delete(huge);
                string target = Path.Combine(f.Source, "real"), link = Path.Combine(f.Source, "linked"); Directory.CreateDirectory(target); File.Copy(FixtureShareHelper(f), Path.Combine(target, "helper.exe")); CreateJunction(f, link, target);
                Throws<SafetyException>(() => QuickShareConfiguration.Save(absent, Path.Combine(link, "helper.exe"), null, null, f.Destination, CancellationToken.None));
                Check(!QuickShareConfiguration.Load(f.Destination).Exists, "Rejected selection stored config");
            } });
            Test("Quick Share malformed or oversized configuration refuses without discarding other settings", delegate { using (var f = new Fixture()) {
                AppSettings.Save(new AppSettings(), f.Destination); string path = Path.Combine(f.Destination, QuickShareConfiguration.FileName);
                File.WriteAllText(path, new string('x', 16385)); Throws<SafetyException>(() => QuickShareConfiguration.Load(f.Destination));
                foreach (var value in new[] { new QuickShareToolSettings { Version = 2 }, new QuickShareToolSettings { Ffmpeg = new QuickShareToolFile { Path = @"C:\ffmpeg.exe", Bytes = 1, Sha256 = "wrong" } }, new QuickShareToolSettings { Lut = new QuickShareToolFile { Path = @"C:\elsewhere.cube", Bytes = 1, Sha256 = QuickShareMedia.OfficialLutSha } } }) {
                    Profiles.WriteJson(value, typeof(QuickShareToolSettings), f.Destination, QuickShareConfiguration.FileName); Throws<SafetyException>(() => QuickShareConfiguration.Load(f.Destination));
                }
                Check(AppSettings.Load(f.Destination).NotificationsEnabled && Profiles.Catalog(f.Destination).Length == 0, "Other settings/catalog affected");
            } });
            Test("Quick Share snapshots expose isolated choices and unrelated settings saves preserve setup", delegate { using (var f = new Fixture()) {
                var saved = SaveShareSetup(f); var copy = saved.Value; copy.Ffmpeg.Sha256 = "changed"; copy.Ffprobe.Path = "changed"; copy.Version = 99;
                AppSettings.Save(new AppSettings { DebugLogging = true }, f.Destination);
                Check(saved.Value.Version == 1 && saved.Value.Ffmpeg.Sha256 != "changed" && QuickShareConfiguration.Load(f.Destination).Hash == saved.Hash, "Snapshot/settings overwrote setup");
            } });
            Test("Quick Share setup editor is explicit Cancel-default and construction cancellation touches no files", delegate { using (var f = new Fixture()) {
                var saved = SaveShareSetup(f); var before = Directory.GetFiles(f.Destination).OrderBy(x => x).ToArray();
                using (var dialog = new QuickShareSetupDialog(saved, f.Destination)) {
                    Check(dialog.Ffmpeg.ReadOnly && dialog.Ffprobe.ReadOnly && dialog.Lut.ReadOnly && dialog.AcceptButton == dialog.CancelSelection && dialog.CancelButton == dialog.CancelSelection && dialog.Saved == null, "Unsafe setup entry/default");
                    dialog.Ffmpeg.Text = "changed unsaved"; dialog.DialogResult = DialogResult.Cancel;
                }
                Check(QuickShareConfiguration.Load(f.Destination).Hash == saved.Hash && Directory.GetFiles(f.Destination).OrderBy(x => x).SequenceEqual(before), "Unsaved editor changed setup");
            } });
            Test("Quick Share setup Save runs the actual configuration transaction and reports committed preferences", delegate { using (var f = new Fixture()) {
                using (var dialog = new QuickShareSetupDialog(QuickShareConfiguration.Load(f.Destination), f.Destination)) {
                    dialog.Ffmpeg.Text = dialog.Ffprobe.Text = FixtureShareHelper(f);
                    var task = dialog.SaveAsync(); Check(!dialog.SaveSelection.Enabled && !dialog.ClearTools.Enabled && dialog.CancelSelection.Enabled, "Busy editor can change or resubmit setup");
                    PumpShareSetup(task);
                    Check(dialog.Saved != null && dialog.DialogResult == DialogResult.OK && QuickShareConfiguration.Load(f.Destination).Hash == dialog.Saved.Hash && Directory.GetFiles(f.Source).Length == 1, "Commit not reported or helper ran");
                }
            } });
            Test("Quick Share setup Cancel waits for the active validator and prevents duplicate saves", delegate { using (var f = new Fixture()) {
                int calls = 0; using (var entered = new ManualResetEventSlim()) using (var release = new ManualResetEventSlim())
                using (var dialog = new QuickShareSetupDialog(QuickShareConfiguration.Load(f.Destination), f.Destination, (mpeg, probe, lut, cancel) => {
                    Interlocked.Increment(ref calls); entered.Set(); release.Wait(5000); cancel.ThrowIfCancellationRequested(); return new QuickShareConfigurationSnapshot();
                })) {
                    var task = dialog.SaveAsync(); Check(entered.Wait(3000), "Validator did not start");
                    var duplicate = dialog.SaveAsync(); Check(duplicate.IsCompleted && calls == 1, "Duplicate save admitted");
                    dialog.RequestStop(); Check(!task.IsCompleted && !dialog.CancelSelection.Enabled && dialog.Saved == null && dialog.DialogResult == DialogResult.None, "Cancel closed before worker stopped");
                    release.Set(); PumpShareSetup(task);
                    Check(dialog.Saved == null && dialog.DialogResult == DialogResult.Cancel && !QuickShareConfiguration.Load(f.Destination).Exists, "Cancelled validator reported or saved success");
                }
            } });
            Test("Quick Share setup late Cancel reports an already committed save honestly", delegate { using (var f = new Fixture()) {
                var snapshot = QuickShareConfiguration.Load(f.Destination); using (var committed = new ManualResetEventSlim()) using (var release = new ManualResetEventSlim())
                using (var dialog = new QuickShareSetupDialog(snapshot, f.Destination, (mpeg, probe, lut, cancel) => {
                    var result = QuickShareConfiguration.Save(snapshot, FixtureShareHelper(f), FixtureShareHelper(f), null, f.Destination, CancellationToken.None); committed.Set(); release.Wait(5000); return result;
                })) {
                    var task = dialog.SaveAsync(); Check(committed.Wait(3000), "Own config did not commit"); dialog.RequestStop(); release.Set(); PumpShareSetup(task);
                    Check(dialog.Saved != null && dialog.DialogResult == DialogResult.OK && QuickShareConfiguration.Load(f.Destination).Hash == dialog.Saved.Hash, "Late Cancel hid committed preferences");
                }
            } });
            Test("Quick Share setup is unavailable in read-only settings and opening settings starts no setup", delegate {
                int calls = 0; using (var dialog = new AppSettingsDialog(new AppSettings())) Check(!dialog.ShareTools.Enabled, "Read-only setting offers writes");
                using (var dialog = new AppSettingsDialog(new AppSettings(), configureShareTools: () => calls++)) Check(dialog.ShareTools.Enabled && calls == 0 && !dialog.Value.ShortcutEnabled && !dialog.Value.CleanupCardsEnabled, "Settings construction performed setup or enabled unrelated actions");
            });
        }
    }
}
