using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;

namespace EasyEject {
    public static partial class Tests {
        static int passed, failed;
        sealed class FakeDiscovery : IDiscovery { public Inventory State; public Inventory Scan() { return State; } public long Epoch { get { return State.Epoch; } } }
        sealed class FakeEject : IEjectApi { public int Calls; public uint LastNode; public string LastId; public Action BeforeRequest; public EjectResult Result = new EjectResult { Success = true }; public EjectResult Request(uint node, string id) { if (BeforeRequest != null) BeforeRequest(); Calls++; LastNode = node; LastId = id; return Result; } }
        sealed class FakeGuard : IImportGuard { public int Calls, FailAt = Int32.MaxValue; public int[] ProtectedDisks { get { return new[] { 700, 701 }; } } public void Validate() { if (++Calls >= FailAt) throw new SafetyException("Fixture disconnected"); } }
        sealed class InterruptedGuard : IImportGuard, IConnectionGuard {
            readonly Fixture fixture; readonly CancellationTokenSource cancellation; int checks; public long WrittenBeforeStop;
            public InterruptedGuard(Fixture fixture, CancellationTokenSource cancellation) { this.fixture = fixture; this.cancellation = cancellation; }
            public int[] ProtectedDisks { get { return new[] { 700, 701 }; } }
            public bool Changed { get {
                if (++checks != 2) return false;
                string pending = Directory.GetFiles(fixture.Destination, "*.partial", SearchOption.AllDirectories).Single();
                WrittenBeforeStop = new FileInfo(pending).Length;
                if (cancellation != null) cancellation.Cancel();
                return true;
            } }
            public void Validate() {
                if (WrittenBeforeStop == 0) return;
                if (cancellation != null) cancellation.Token.ThrowIfCancellationRequested();
                throw new SafetyException("Fixture disconnected during transfer");
            }
        }
        static void CreateJunction(Fixture fixture, string link, string target) {
            Check(SafePaths.Within(link, fixture.Root) && SafePaths.Within(target, fixture.Root), "Junction fixture must stay isolated");
            Directory.CreateDirectory(link);
            byte[] substitute = System.Text.Encoding.Unicode.GetBytes(@"\??\" + target), print = System.Text.Encoding.Unicode.GetBytes(target);
            byte[] data = new byte[16 + substitute.Length + 2 + print.Length + 2];
            Array.Copy(BitConverter.GetBytes(0xA0000003U), 0, data, 0, 4);
            Array.Copy(BitConverter.GetBytes((ushort)(data.Length - 8)), 0, data, 4, 2);
            Array.Copy(BitConverter.GetBytes((ushort)substitute.Length), 0, data, 10, 2);
            Array.Copy(BitConverter.GetBytes((ushort)(substitute.Length + 2)), 0, data, 12, 2);
            Array.Copy(BitConverter.GetBytes((ushort)print.Length), 0, data, 14, 2);
            Array.Copy(substitute, 0, data, 16, substitute.Length); Array.Copy(print, 0, data, 18 + substitute.Length, print.Length);
            using (var h = Native.CreateFile(link, 0x40000000, 0, IntPtr.Zero, 3, 0x02200000, IntPtr.Zero)) {
                if (h.IsInvalid) throw Native.Error("Cannot open isolated junction fixture");
                int returned;
                if (!Native.DeviceIoControl(h, 0x900A4, data, data.Length, null, 0, out returned, IntPtr.Zero)) throw Native.Error("Cannot create isolated junction fixture");
            }
        }
        static void Check(bool value, string message) { if (!value) throw new Exception(message); }
        static T Throws<T>(Action action) where T : Exception { try { action(); } catch (T ex) { return ex; } throw new Exception("Expected " + typeof(T).Name); }
        static string Saved(ImportResult result, string sourceRelative) { return Path.Combine(result.Folder, result.Receipt.Files.Single(x => x.SourceRelativePath == sourceRelative).RelativePath); }
        static DestinationIdentity HistoryIdentity() { return new DestinationIdentity { VolumeId = "destination-fixture", VolumeSerial = "ABC", DiskInstance = "destination-disk", DiskSerial = "serial-destination" }; }
        static CompletedBatch Batch(Fixture fixture, ImportResult result) { return ImportHistory.Create(result, new ImportProfile { VolumeId = result.Receipt.SourceVolume, Destination = fixture.Destination }, "Fixture camera", HistoryIdentity()); }
        static ImportCompletion Completion(Inventory state, Fixture fixture, bool hasMedia) { return new ImportCompletion(new ImportCompletionOrigin(state, state.Volumes[0].Id, state.Volumes[0].Mounts[0]), "Fixture camera", fixture.Destination, fixture.Destination, HistoryIdentity(), hasMedia, hasMedia ? "Files verified. Originals kept." : "No media matches your import options."); }
        static void Test(string name, Action action) { try { action(); passed++; Console.WriteLine("PASS " + name); } catch (Exception ex) { failed++; Console.WriteLine("FAIL " + name + ": " + ex); } }
        static Inventory State() {
            var state = new Inventory { Epoch = 1 };
            state.Disks.Add(new Disk { Number = 7, DevInst = 10, InstanceId = "disk-a", Serial = "serial-a", TargetId = "usb-a", TargetDevInst = 20, Ancestors = new List<string> { "disk-a", "usb-a" } });
            state.Volumes.Add(new Volume { Id = "volume-a", Serial = "ABC", Extents = "7:0:1000", Disks = new List<int> { 7 }, Mounts = new List<string> { @"E:\" } }); return state;
        }
        sealed class Fixture : IDisposable {
            public readonly string Root, Source, Destination;
            public Fixture() {
                Root = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".codex", "EasyEjectFixtures", "run-" + Guid.NewGuid().ToString("N").Substring(0, 16));
                Source = Path.Combine(Root, "source"); Destination = Path.Combine(Root, "destination"); Directory.CreateDirectory(Source); Directory.CreateDirectory(Destination);
            }
            public void Put(string name, string text) { string path = Path.Combine(Source, name); Directory.CreateDirectory(Path.GetDirectoryName(path)); File.WriteAllText(path, text); }
            public ImportResult Run(Action<string> report) { return new VerifiedImporter(new Operations()).Run(Source, Source, Destination, "fixture-volume", new FakeGuard(), CancellationToken.None, report); }
            public void Dispose() {
                string allowed = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".codex", "EasyEjectFixtures");
                if (!SafePaths.Within(Path.GetFullPath(Root), Path.GetFullPath(allowed)) || Root == allowed) throw new Exception("Unsafe fixture cleanup path");
                Clean(Root);
            }
            static void Clean(string path) {
                if ((File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0) { Directory.Delete(path, false); return; }
                foreach (var file in Directory.GetFiles(path)) File.Delete(file);
                foreach (var folder in Directory.GetDirectories(path)) Clean(folder);
                Directory.Delete(path, false);
            }
        }
        [STAThread] public static int Main(string[] args) {
            if (args.Length >= 2 && args[0] == "--share-process-fixture") return ShareProcessFixture(args);
            if (AppDomain.CurrentDomain.FriendlyName == "EasyEject.ShareFixture.exe") return ShareToolFixture(args);
            System.Windows.Forms.Application.EnableVisualStyles(); System.Windows.Forms.Application.SetCompatibleTextRenderingDefault(false);
            if (args.Length == 1 && args[0] == "--media-parity") { MediaParityTests(); MediaMatrixTests(); Console.WriteLine(passed + " passed; " + failed + " failed. Disposable files and native read-only path resolution only."); return failed == 0 ? 0 : 1; }
            if (args.Length == 1 && args[0] == "--storage-connections") { StorageConnectionTests(); Console.WriteLine(passed + " passed; " + failed + " failed. Fake notifications, fake eject and disposable files only."); return failed == 0 ? 0 : 1; }
            if (args.Length == 1 && args[0] == "--completion-connections") { CompletionConnectionTests(); Console.WriteLine(passed + " passed; " + failed + " failed. Fake removal and owned disposable files only."); return failed == 0 ? 0 : 1; }
            if (args.Length == 1 && args[0] == "--concurrent-storage") { ConcurrentStorageTests(); Console.WriteLine(passed + " passed; " + failed + " failed. Fake eject, owned disposable files and hidden controls only."); return failed == 0 ? 0 : 1; }
            if (args.Length == 1 && args[0] == "--automatic-admission") { AutomaticAdmissionTests(); Console.WriteLine(passed + " passed; " + failed + " failed. Fake storage evidence and disposable files only."); return failed == 0 ? 0 : 1; }
            if (args.Length == 1 && args[0] == "--share-directories") { DirectoryLeaseTests(); QuickShareEngineTests(); QuickShareProcessTests(); Console.WriteLine(passed + " passed; " + failed + " failed. Disposable directories, fake exports and contained synthetic child processes only."); return failed == 0 ? 0 : 1; }
            if (args.Length == 1 && args[0] == "--configuration-writer") { ConfigurationWriterTests(); Console.WriteLine(passed + " passed; " + failed + " failed. Disposable native configuration files only."); return failed == 0 ? 0 : 1; }
            if (args.Length == 1 && args[0] == "--manual-installation") { ManualInstallationTests(); Console.WriteLine(passed + " passed; " + failed + " failed. Disposable directories, fake folder viewers and hidden native controls only."); return failed == 0 ? 0 : 1; }
            if (args.Length == 1 && args[0] == "--windows-signatures") { WindowsAuthenticodeTests(); Console.WriteLine(passed + " passed; " + failed + " failed. Fake positive trust and disposable native unsigned refusals only; no execution or trusted release proof."); return failed == 0 ? 0 : 1; }
            if (args.Length == 1 && args[0] == "--windows-manifests") { WindowsManifestTrustTests(); Console.WriteLine(passed + " passed; " + failed + " failed. Ephemeral fixture keys, fake positive chain trust and real native untrusted refusals only; no trust-store import or release signing."); return failed == 0 ? 0 : 1; }
            if (args.Length == 1 && args[0] == "--windows-update-coordinator") { WindowsUpdateCoordinatorTests(); Console.WriteLine(passed + " passed; " + failed + " failed. Owned fake checks, disposable settings/replay and hidden native controls only; no production channel request."); return failed == 0 ? 0 : 1; }
            if (args.Length == 1 && args[0] == "--windows-release-review") { WindowsReleaseReviewTests(); Console.WriteLine(passed + " passed; " + failed + " failed. Read-only disposable ZIP/package review and fake positive native trust only; no real download, extraction, execution or installation."); return failed == 0 ? 0 : 1; }
            if (args.Length == 1 && args[0] == "--path-ownership") { PathOwnershipTests(); Console.WriteLine(passed + " passed; " + failed + " failed. Owned disposable directory replacement only; no real device operation."); return failed == 0 ? 0 : 1; }
            if (args.Length == 1 && args[0] == "--bulk-final") { BulkFinalTests(); Console.WriteLine(passed + " passed; " + failed + " failed. Fake removal and inventory only; no real eject."); return failed == 0 ? 0 : 1; }
            if (args.Length == 1 && args[0] == "--windows-replay") { WindowsChannelReplayTests(); Console.WriteLine(passed + " passed; " + failed + " failed. Disposable replay files, native CurrentUser protection and fake positive publisher trust only; no real network or cached installation authority."); return failed == 0 ? 0 : 1; }
            if (args.Length == 1 && args[0] == "--windows-discovery") { WindowsChannelDiscoveryTests(); Console.WriteLine(passed + " passed; " + failed + " failed. Fake HTTP operations, owned disposable signed records and fake positive native trust only; no real network request."); return failed == 0 ? 0 : 1; }
            if (args.Length == 1 && args[0] == "--windows-channels") { WindowsChannelTests(); Console.WriteLine(passed + " passed; " + failed + " failed. Disposable signed channel fixtures and fake positive native trust only; no endpoint network request or production channel configured."); return failed == 0 ? 0 : 1; }
            if (args.Length == 1 && args[0] == "--windows-archives") { WindowsArchiveTests(); Console.WriteLine(passed + " passed; " + failed + " failed. Owned disposable ZIP contents only; fake positive native certificate/executable trust, no extraction or release proof."); return failed == 0 ? 0 : 1; }
            if (args.Length == 1 && args[0] == "--windows-managed") { WindowsManagedIdentityTests(); Console.WriteLine(passed + " passed; " + failed + " failed. Read-only native managed metadata."); return failed == 0 ? 0 : 1; }
            if (args.Length == 1 && args[0] == "--windows-packages") { WindowsPackageTests(); Console.WriteLine(passed + " passed; " + failed + " failed. Owned disposable packages, patched unsigned PE resources, ephemeral fixture CMS and fake positive trust only; no execution/install/release proof."); return failed == 0 ? 0 : 1; }
            if (args.Length == 1 && args[0] == "--ui-render") {
                string output = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".codex", "EasyEjectWindowsPreview", "run-" + Guid.NewGuid().ToString("N").Substring(0, 16));
                Directory.CreateDirectory(output); var state = State(); var defaults = new ImportDefaults(); var profile = defaults.NewProfile(state.Volumes[0], state.Disks[0]); profile.RelativeSource = "DCIM"; profile.Destination = @"D:\Travel backups";
                using (var manualCancel = new CancellationTokenSource()) using (var dialog = new ManualStorageDialog("Fixture other camera card", manualCancel)) {
                    dialog.Size = dialog.MinimumSize; dialog.Status.Text = "Removing owned metadata from the reviewed card…\r\n\r\nThe import continues on separate source and destination disks. Its Cancel button controls only that import."; Render(dialog, Path.Combine(output, "manual-storage-progress.png"));
                    dialog.Complete("Windows refused removal (fixture veto: OutstandingOpen). Finish the other program's transfer, then review this device again.\r\n\r\nRemoved 3 metadata files. Photos, recordings and device Trash are kept.\r\n\r\nThe other import retains its own progress and cancellation controls. No automatic retry."); Render(dialog, Path.Combine(output, "manual-storage-result.png"));
                }
                var shareProfile = profile.Copy(); shareProfile.Name = "Fixture Luna travel card"; shareProfile.SharingPreset = QuickShareProfile.Uhd; shareProfile.SharingColor = QuickShareProfile.Standard;
                using (var check = new WindowsUpdateCoordinator(false, null, () => DateTime.UtcNow, TimeSpan.FromDays(1), true))
                using (var session = new WindowsReleaseReviewSession(check, null)) using (var folders = new InstallationFolders(output, output, _ => {}))
                using (var dialog = new WindowsReleaseReviewDialog(ApplicationInformation.Version, session, folders, () => {})) { dialog.Size = dialog.MinimumSize; Render(dialog, Path.Combine(output, "windows-release-unavailable.png")); }
                using (var review = new ReleaseReviewFixture()) using (var folders = new InstallationFolders(output, output, _ => {}))
                using (var dialog = new WindowsReleaseReviewDialog(ApplicationInformation.Version, review.Review, folders, () => {})) {
                    dialog.Size = dialog.MinimumSize; PumpShareSetup(dialog.RunAction(() => review.Review.CheckAsync(CancellationToken.None))); Render(dialog, Path.Combine(output, "windows-release-metadata.png"));
                    PumpShareSetup(dialog.RunAction(() => review.Review.ArchiveAsync(review.Update.Channel.Archive.Path, CancellationToken.None)));
                    PumpShareSetup(dialog.RunAction(() => review.Review.ExtractedAsync(review.Update.Channel.Archive.Package.Root, CancellationToken.None))); Render(dialog, Path.Combine(output, "windows-release-package.png"));
                }
                using (var folders = new InstallationFolders(output, output, _ => {})) using (var dialog = new ManualInstallationDialog(ApplicationInformation.Version, folders)) {
                    dialog.Size = dialog.MinimumSize; Render(dialog, Path.Combine(output, "manual-installation-top.png"));
                    string complete = dialog.Content.Text; dialog.Content.Text = complete.Substring(complete.IndexOf("Manual rollback", StringComparison.Ordinal));
                    Render(dialog, Path.Combine(output, "manual-installation-bottom-preview.png")); dialog.Content.Text = complete;
                    Console.WriteLine("Installation end-content preview uses the same hidden control; it is not proof of native foreground scrolling.");
                }
                using (var dialog = new ImportProfileDialog(shareProfile, null, shareProfile.Name, defaults, true)) { dialog.Size = dialog.MinimumSize; RenderAtBottom(dialog, Path.Combine(output, "profile-quick-share-bottom.png")); }
                using (var dialog = new QuickSharePromptDialog(shareProfile.Name, @"D:\Travel backups\2026-10-06\Fixture Luna travel card", shareProfile)) { dialog.Size = dialog.MinimumSize; Render(dialog, Path.Combine(output, "quick-share-prompt.png")); RenderAtBottom(dialog, Path.Combine(output, "quick-share-prompt-bottom.png")); }
                using (var dialog = new QuickShareSetupDialog(new QuickShareConfigurationSnapshot(), Profiles.Root)) { dialog.Size = dialog.MinimumSize; Render(dialog, Path.Combine(output, "quick-share-setup.png")); RenderAtBottom(dialog, Path.Combine(output, "quick-share-setup-bottom.png")); }
                using (var dialog = new ImportProfileDialog(profile, @"E:\", "Fixture Insta360 card · E:\\", defaults, true)) {
                    dialog.Size = dialog.MinimumSize; Render(dialog, Path.Combine(output, "profile-defaults.png"));
                    dialog.UseCustom.Checked = true; dialog.Selection.Previews.Checked = false; Render(dialog, Path.Combine(output, "profile-custom.png"));
                }
                profile.Name = "Insta360 travel card";
                using (var dialog = new ImportProfileDialog(profile, null, profile.Name + " (saved profile)", defaults, true)) { dialog.Size = dialog.MinimumSize; Render(dialog, Path.Combine(output, "profile-offline.png")); RenderAtBottom(dialog, Path.Combine(output, "profile-offline-bottom.png")); }
                var previewProfiles = new[] { new SavedProfile(Profiles.FileName(profile.VolumeId), profile, new string('a', 64), new Native.FileInfo()), new SavedProfile(new string('B', 64) + ".json", "Saved profile device identity does not match its filename. Set up this device again.") };
                using (var dialog = new SavedProfilesDialog(() => previewProfiles, () => defaults, (entry, draft) => { throw new Exception("Preview does not save profiles"); }, entry => { throw new Exception("Preview does not forget profiles"); })) {
                    dialog.Size = dialog.MinimumSize; Render(dialog, Path.Combine(output, "saved-profiles.png")); dialog.ProfilesList.Items[0].Selected = true; dialog.UpdateSelection(); Render(dialog, Path.Combine(output, "saved-profiles-selected.png"));
                }
                using (var dialog = new SavedProfilesDialog(() => new SavedProfile[0], () => defaults, (entry, draft) => {}, entry => {})) { dialog.Size = dialog.MinimumSize; Render(dialog, Path.Combine(output, "saved-profiles-empty.png")); }
                using (var dialog = new ImportDefaultsDialog(defaults)) { dialog.Size = dialog.MinimumSize; Render(dialog, Path.Combine(output, "shared-defaults.png")); }
                var automaticDefaults = new ImportDefaults { AutomaticImport = true }; var automaticProfile = automaticDefaults.NewProfile(state.Volumes[0], state.Disks[0]); automaticProfile.Name = "Fixture automatic Insta360 card"; automaticProfile.RelativeSource = "DCIM"; automaticProfile.Destination = @"D:\Travel backups"; automaticProfile.SavedDestination = HistoryIdentity();
                using (var dialog = new ImportProfileDialog(automaticProfile, null, automaticProfile.Name, automaticDefaults, true)) { dialog.Size = dialog.MinimumSize; Render(dialog, Path.Combine(output, "profile-automatic.png")); RenderAtBottom(dialog, Path.Combine(output, "profile-automatic-bottom.png")); }
                using (var dialog = new ImportDefaultsDialog(automaticDefaults)) { dialog.Size = dialog.MinimumSize; Render(dialog, Path.Combine(output, "shared-automatic.png")); RenderAtBottom(dialog, Path.Combine(output, "shared-automatic-bottom.png")); }
                using (var dialog = new ImportDefaultsDialog(new ImportDefaults { DeleteOriginals = true })) { dialog.Size = dialog.MinimumSize; Render(dialog, Path.Combine(output, "shared-removal.png")); }
                using (var dialog = new OriginalRemovalConfirmationDialog("Applies to EVERY device using defaults, including future profiles.", true)) { dialog.Size = dialog.MinimumSize; Render(dialog, Path.Combine(output, "enable-removal.png")); }
                using (var dialog = new VerifiedImportConfirmationDialog("Copy selected videos and their required companions under:\nE:\\DCIM\n\nTo a date folder under:\nD:\\Travel backups\n\nOriginal filenames are kept. SHA-256 verifies new and reused copies.\n\n" + OriginalRemovalChoice.BeforeImport(RemoveOptions()) + "\n\nDevice remains connected. Start this manual import?")) { dialog.Size = dialog.MinimumSize; Render(dialog, Path.Combine(output, "manual-removal.png")); }
                using (var dialog = new DeviceTrashRecoveryConfirmationDialog("Applies to EVERY current/future device using defaults.")) { dialog.Size = dialog.MinimumSize; Render(dialog, Path.Combine(output, "enable-recovery.png")); }
                using (var dialog = new VerifiedImportConfirmationDialog("Copy selected ordinary media and device Trash to:\nD:\\Travel backups\n\n" + OriginalRemovalChoice.BeforeImport(RecoverOptions(macUid: "501")) + "\n\n" + DeviceTrashChoice.BeforeImport(RecoverOptions(macUid: "501")))) { dialog.Size = dialog.MinimumSize; Render(dialog, Path.Combine(output, "manual-recovery.png")); RenderAtBottom(dialog, Path.Combine(output, "manual-recovery-bottom.png")); }
                var recoveryDefaults = new ImportDefaults { RecoverTrash = true }; var recoveryProfile = recoveryDefaults.NewProfile(state.Volumes[0], state.Disks[0]); recoveryProfile.MacTrashUid = "501";
                using (var dialog = new ImportProfileDialog(recoveryProfile, @"E:\", "Fixture camera card", recoveryDefaults, true)) { dialog.Size = dialog.MinimumSize; RenderAtBottom(dialog, Path.Combine(output, "profile-recovery-owner.png")); }
                var previewBatch = new CompletedBatch { Id = Guid.NewGuid().ToString("N"), CompletedUtc = DateTime.UtcNow.ToString("o"), DeviceName = "Fixture Insta360 card", Folder = @"D:\Travel backups\2026-10-06", ReceiptPath = @"D:\Travel backups\2026-10-06\.easy-eject-receipts\fixture\verified-import.json", Originals = new List<VerifiedCopy> { new VerifiedCopy { Path = @"D:\Travel backups\2026-10-06\VID_20261006_001.mp4", Sha256 = new string('a', 64), Bytes = 1234567 } } };
                using (var dialog = new ImportHistoryDialog(new[] { previewBatch })) { dialog.Size = dialog.MinimumSize; Render(dialog, Path.Combine(output, "history.png")); dialog.Batches.Items[0].Selected = true; Render(dialog, Path.Combine(output, "history-selected.png")); }
                using (var dialog = new ImportHistoryDialog(new CompletedBatch[0])) { dialog.Size = dialog.MinimumSize; Render(dialog, Path.Combine(output, "history-empty.png")); }
                previewBatch.ProfileId = "fixture-preview-volume"; previewBatch.DestinationRoot = @"D:\Travel backups"; previewBatch.Destination = HistoryIdentity(); previewBatch.ReceiptSha256 = new string('a', 64); previewBatch.ReceiptPath = Path.Combine(previewBatch.Folder, ".easy-eject-receipts", previewBatch.Id, "verified-import.json");
                var previewShareProfile = new ImportProfile { Name = previewBatch.DeviceName, VolumeId = previewBatch.ProfileId, VolumeSerial = "fixture", DiskInstance = "fixture-source", RelativeSource = "", Destination = previewBatch.DestinationRoot, SharingPreset = QuickShareProfile.Hd, SharingColor = QuickShareProfile.Standard };
                var previewShareCatalog = new[] { new SavedProfile(Profiles.FileName(previewShareProfile.VolumeId), previewShareProfile, new string('a', 64), new Native.FileInfo()) };
                using (var dialog = new ImportHistoryDialog(new[] { previewBatch }, previewShareCatalog)) { dialog.Size = dialog.MinimumSize; Render(dialog, Path.Combine(output, "history-share-unselected.png")); dialog.Batches.Items[0].Selected = true; Render(dialog, Path.Combine(output, "history-share-selected.png")); }
                using (var dialog = new ImportCompletionDialog(ShareCompletion(previewBatch), true, canShare: true)) { dialog.Size = dialog.MinimumSize; Render(dialog, Path.Combine(output, "completion-share.png")); RenderAtBottom(dialog, Path.Combine(output, "completion-share-bottom.png")); dialog.ExpireEject(); Render(dialog, Path.Combine(output, "completion-share-source-expired.png")); }
                using (var dialog = new ImportCompletionDialog(ShareCompletion(previewBatch), true, "Permanently remove only reviewed macOS metadata before manual eject.", true, "Fixture: 1 sharing copy ready. Imported originals are unchanged. No device was ejected.")) { dialog.Size = dialog.MinimumSize; RenderAtBottom(dialog, Path.Combine(output, "completion-share-return.png")); }
                using (var fixture = new Fixture()) {
                    InterruptedRecovery(fixture); var resumeReview = ReviewInterrupted(fixture);
                    using (var dialog = new RemovalResumeConfirmationDialog(RemovalResumeChoice.Explanation(resumeReview, "Fixture camera card", fixture.Source, fixture.Destination))) { dialog.Size = dialog.MinimumSize; Render(dialog, Path.Combine(output, "resume-import.png")); RenderAtBottom(dialog, Path.Combine(output, "resume-import-bottom.png")); }
                    var completion = Completion(state, fixture, true);
                    using (var dialog = new ImportCompletionDialog(completion, true)) { dialog.Size = dialog.MinimumSize; Render(dialog, Path.Combine(output, "completion.png")); dialog.ExpireEject(); Render(dialog, Path.Combine(output, "completion-expired.png")); }
                    using (var dialog = new ImportCompletionDialog(completion, true, "Permanently remove macOS metadata from E:\\ before safe removal. Photos, recordings and device Trash are kept. Cleanup failure stops ejection.")) { dialog.Size = dialog.MinimumSize; Render(dialog, Path.Combine(output, "completion-cleanup.png")); }
                    using (var dialog = new ImportCompletionDialog(Completion(state, fixture, false), true)) { dialog.Size = dialog.MinimumSize; Render(dialog, Path.Combine(output, "completion-no-media.png")); }
                    using (var dialog = new PendingCompletionsDialog(new[] { completion })) { dialog.Size = dialog.MinimumSize; Render(dialog, Path.Combine(output, "completed-imports.png")); }
                    using (var dialog = new AppSettingsDialog(new AppSettings(), "Global eject shortcut is off.")) { dialog.Size = dialog.MinimumSize; Render(dialog, Path.Combine(output, "app-settings.png")); dialog.Shortcut.Checked = true; dialog.ShortcutLetter.SelectedItem = "R"; Render(dialog, Path.Combine(output, "app-settings-shortcut.png")); }
                    var startupCommand = StartupRegistration.Command(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "EasyEject.Windows.exe"));
                    using (var dialog = new AppSettingsDialog(new AppSettings(), "Shortcut off", new StartupSnapshot(StartupValue.Missing(), startupCommand), () => {})) { dialog.Size = dialog.MinimumSize; Render(dialog, Path.Combine(output, "startup-off.png")); RenderAtBottom(dialog, Path.Combine(output, "startup-off-bottom.png")); }
                    using (var dialog = new AppSettingsDialog(new AppSettings(), "Shortcut off", new StartupSnapshot(new StartupValue(true, Microsoft.Win32.RegistryValueKind.String, startupCommand), startupCommand), () => {})) { dialog.Size = dialog.MinimumSize; Render(dialog, Path.Combine(output, "startup-registered.png")); }
                    using (var dialog = new AppSettingsDialog(new AppSettings(), "Shortcut off", new StartupSnapshot(new StartupValue(true, Microsoft.Win32.RegistryValueKind.String, StartupRegistration.Command(@"D:\Old copy\EasyEject.Windows.exe")), startupCommand), () => {})) { dialog.Size = dialog.MinimumSize; Render(dialog, Path.Combine(output, "startup-relocated.png")); }
                    using (var dialog = new AppSettingsDialog(new AppSettings(), "Shortcut off", new StartupSnapshot(new StartupValue(true, Microsoft.Win32.RegistryValueKind.String, "Unrecognized fixture command"), startupCommand), () => {})) { dialog.Size = dialog.MinimumSize; Render(dialog, Path.Combine(output, "startup-conflict.png")); }
                    var issue = new AppIssue(); issue.Record("Fixture open-file veto. Finish transfers and close programs using the device, then review a fresh safe-removal choice.");
                    using (var dialog = new IssueDialog(issue)) { dialog.Size = dialog.MinimumSize; Render(dialog, Path.Combine(output, "issue.png")); }
                    DebugLog.Append(fixture.Destination, "Synthetic private log. No real device operation.");
                    using (var dialog = new DiagnosticsDialog(DebugLog.Load(fixture.Destination), () => DebugLog.Load(fixture.Destination), snapshot => DebugLog.Clear(snapshot, fixture.Destination), text => {})) { dialog.Size = dialog.MinimumSize; Render(dialog, Path.Combine(output, "debug-log-actions.png")); }
                    using (var dialog = new InformationDialog(false)) { dialog.Size = dialog.MinimumSize; Render(dialog, Path.Combine(output, "help.png")); }
                    using (var dialog = new InformationDialog(true)) { dialog.Size = dialog.MinimumSize; Render(dialog, Path.Combine(output, "about.png")); }
                }
                var cleanupState = CardState(); var cleanupBulk = BulkEjectPolicy.Plan(cleanupState);
                using (var dialog = new BulkEjectDialog(cleanupBulk)) { dialog.Size = dialog.MinimumSize; Render(dialog, Path.Combine(output, "bulk-eject.png")); }
                using (var dialog = new BulkEjectDialog(cleanupBulk, String.Join("\r\n\r\n", cleanupBulk.Targets.Select(t => MetadataCleanupPlan.Bulk(cleanupState, t).Description)))) { dialog.Size = dialog.MinimumSize; Render(dialog, Path.Combine(output, "bulk-cleanup.png")); }
                var bulkResult = new BulkEjectResult { Requested = 2, Confirmed = 1, Detail = "Fixture open-file veto. Close the editor before reviewing the remaining card." }; bulkResult.SafeDevices.Add(@"Fixture camera [E:\]");
                using (var dialog = new BulkEjectResultDialog(bulkResult)) { dialog.Size = dialog.MinimumSize; Render(dialog, Path.Combine(output, "bulk-partial-result.png")); }
                using (var window = new DeviceWindow(true)) {
                    window.Size = window.MinimumSize; window.PresentInventory(CardState()); Render(window, Path.Combine(output, "device-window.png"));
                    var statusControl = (System.Windows.Forms.Control)typeof(DeviceWindow).GetField("status", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance).GetValue(window);
                    statusControl.Text = String.Join(Environment.NewLine, Enumerable.Range(1, 80).Select(i => "Synthetic retained-work result " + i + ": full-quality originals are unchanged; retry is manual."));
                    Render(window, Path.Combine(output, "device-window-long-result.png"));
                    var volumesList = (System.Windows.Forms.ListView)typeof(DeviceWindow).GetField("list", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance).GetValue(window);
                    Console.WriteLine("Long-result layout: list-height=" + volumesList.Height + " status-height=" + statusControl.Height + " client-height=" + window.ClientSize.Height);
                    typeof(DeviceWindow).GetField("working", PrivateInstance).SetValue(window, true);
                    var activity = ActivityField<TrayActivity>(window, "trayActivity"); var importing = activity.Begin(TrayOperationKind.Import, "Synthetic camera", "Preparing import");
                    window.ApplyActivityProgress(importing, "Copying synthetic fixture.mp4; all originals kept.", new ImportWorkProgress("Copying", 0.43)); RefreshActivityWindow(window);
                    Console.WriteLine("Native tray importing: " + ActivityField<System.Windows.Forms.NotifyIcon>(window, "tray").Text + " | " + ActivityField<System.Windows.Forms.ToolStripMenuItem>(window, "activityMenu").Text);
                    using (var icon = ActivityField<System.Windows.Forms.NotifyIcon>(window, "tray").Icon.ToBitmap()) icon.Save(Path.Combine(output, "tray-busy-icon.png"));
                    Render(window, Path.Combine(output, "device-window-importing.png"));
                    ActivityField<AppIssue>(window, "appIssue").Record("Synthetic earlier issue remains available."); var sharing = activity.Begin(TrayOperationKind.Share, "Synthetic Luna", "Checking saved originals");
                    window.ApplyActivityProgress(sharing, "Creating sharing copy: 27%"); RefreshActivityWindow(window);
                    Console.WriteLine("Native tray sharing: " + ActivityField<System.Windows.Forms.NotifyIcon>(window, "tray").Text + " | " + ActivityField<System.Windows.Forms.ToolStripMenuItem>(window, "activityMenu").Text);
                    using (var icon = ActivityField<System.Windows.Forms.NotifyIcon>(window, "tray").Icon.ToBitmap()) icon.Save(Path.Combine(output, "tray-busy-attention-icon.png"));
                    Render(window, Path.Combine(output, "device-window-sharing.png"));
                }
                Console.WriteLine("Hidden forms rendered at minimum size. No device operations: " + output); return 0;
            }
            if (args.Length == 1 && args[0] == "--ui-smoke") {
                using (var window = new DeviceWindow(true)) {
                    IntPtr handle = window.Handle;
                    var field = typeof(DeviceWindow).GetField("discovery", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                    var discovery = (WindowsDiscovery)field.GetValue(window); var state = discovery.Scan();
                    Check(handle != IntPtr.Zero && !window.Visible && discovery.NotificationsAvailable, "Hidden window / notifications unavailable");
                    Check(discovery.Instances.Versions.Available, "Device-instance tracking did not register: " + discovery.Instances.Error);
                    var hotkey = (GlobalHotkey)typeof(DeviceWindow).GetField("hotkey", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance).GetValue(window);
                    Check(!hotkey.Registered, "Read-only smoke registered a global key");
                    Check(state.Complete && state.Volumes.Any(v => v.System), "Read-only UI inventory incomplete");
                    var storageProof = new StorageConnectionProof(discovery, state, state.Disks.Select(d => d.Number));
                    Check(storageProof.Scoped, "Native internal-disk connection proof unavailable"); storageProof.Validate(discovery.Scan());
                    foreach (var v in state.Volumes.Where(v => v.System)) Throws<SafetyException>(() => EjectService.Plan(state, v.Id));
                    var menuState = TrayState(); menuState.Epoch = discovery.Epoch; window.PresentInventory(menuState); window.UpdateTrayStorageMenu();
                    var storageMenu = (System.Windows.Forms.ToolStripMenuItem)typeof(DeviceWindow).GetField("storageMenu", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance).GetValue(window);
                    var menuActions = TrayActions(storageMenu.DropDownItems.Cast<System.Windows.Forms.ToolStripItem>()).ToArray();
                    Check(menuActions.Length == 4 && menuActions.All(item => !item.Enabled), "Read-only native window exposes tray removal"); foreach (var action in menuActions) action.PerformClick();
                    Check(!hotkey.Registered, "Read-only tray inspection registered a shortcut");
                    Console.WriteLine("PASS hidden device window initializes, disk and per-instance notifications register, read-only inventory maps " + state.Volumes.Count + " volumes, scoped internal-disk identity proof validates, system eject is blocked, no global key is registered, and fake tray storage actions remain disabled in the real read-only window.");
                    return 0;
                }
            }
            if (args.Length == 1 && args[0] == "--startup-inspect") {
                var store = new WindowsStartupStore(); var before = store.Read();
                string executable = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "EasyEject.Windows.exe");
                var status = new StartupRegistration(store, executable).Inspect();
                Check(status.Available && store.Read().Same(before), "Read-only startup inspection failed or observed a concurrent change");
                Console.WriteLine("PASS read-only per-user login entry inspected; " + (status.Conflict ? "unrecognized entry retained" : status.Owned ? "existing registration retained" : "no registration exists") + ". No startup value was written."); return 0;
            }
            if (args.Length == 1 && args[0] == "--inventory") {
                var inventory = new WindowsDiscovery().Scan(); // Read-only mode: no notification window, hence no actions permitted.
                Console.WriteLine("READ ONLY: " + inventory.Disks.Count + " disks, " + inventory.Volumes.Count + " volumes");
                foreach (var v in inventory.Volumes) Console.WriteLine(v.Display + " | disks " + String.Join(",", v.Disks) + " | " + v.Classification + " | " + v.Error);
                foreach (var error in inventory.Errors) Console.WriteLine(error); return 0;
            }
            Test("plan includes all partitions", delegate { var s = State(); s.Volumes.Add(new Volume { Id = "v2", Serial = "DEF", Disks = new List<int> { 7 } }); var p = EjectService.Plan(s, "volume-a"); Check(p.Affected.Length == 2 && p.Disks.Length == 1, "Missing sibling"); });
            Test("plan includes sibling LUN under same removal target", delegate { var s = State(); s.Disks.Add(new Disk { Number = 8, InstanceId = "disk-b", Ancestors = new List<string> { "usb-a" } }); s.Volumes.Add(new Volume { Id = "v2", Disks = new List<int> { 8 } }); Check(EjectService.Plan(s, "volume-a").Disks.Length == 2, "Missing LUN"); });
            Test("unknown mapping blocks eject", delegate { var s = State(); s.Volumes[0].Disks.Clear(); Throws<SafetyException>(() => EjectService.Plan(s, "volume-a")); });
            Test("spanning volume blocks eject", delegate { var s = State(); s.Volumes[0].Disks.Add(8); Throws<SafetyException>(() => EjectService.Plan(s, "volume-a")); });
            Test("protected sibling blocks eject", delegate { var s = State(); s.Volumes.Add(new Volume { Id = "OS", System = true, Disks = new List<int> { 7 } }); Throws<SafetyException>(() => EjectService.Plan(s, "volume-a")); });
            Test("incomplete inventory blocks eject", delegate { var s = State(); s.Complete = false; Throws<SafetyException>(() => EjectService.Plan(s, "volume-a")); });
            Test("missing removable node blocks eject", delegate { var s = State(); s.Disks[0].TargetId = ""; Throws<SafetyException>(() => EjectService.Plan(s, "volume-a")); });
            Test("unknown sibling identity blocks eject", delegate { var s = State(); s.Volumes.Add(new Volume { Id = "v2", Disks = new List<int> { 7 }, Error = "locked" }); Throws<SafetyException>(() => EjectService.Plan(s, "volume-a")); });
            Test("unchanged plan calls fake eject once", delegate { var d = new FakeDiscovery { State = State() }; var a = new FakeEject(); var p = EjectService.Plan(d.State, "volume-a"); Check(new EjectService(d, a, new Operations()).Execute(p).Success && a.Calls == 1, "Wrong result"); });
            Test("disconnect/reconnect invalidates identical identities", delegate { var d = new FakeDiscovery { State = State() }; var a = new FakeEject(); var p = EjectService.Plan(d.State, "volume-a"); d.State.Epoch++; Throws<SafetyException>(() => new EjectService(d, a, new Operations()).Execute(p)); Check(a.Calls == 0, "Ejected stale connection"); });
            Test("new sibling invalidates old confirmation", delegate { var d = new FakeDiscovery { State = State() }; var a = new FakeEject(); var p = EjectService.Plan(d.State, "volume-a"); d.State.Volumes.Add(new Volume { Id = "v2", Disks = new List<int> { 7 } }); Throws<SafetyException>(() => new EjectService(d, a, new Operations()).Execute(p)); Check(a.Calls == 0, "Ejected unconfirmed sibling"); });
            Test("changed serial blocks old confirmation", delegate { var d = new FakeDiscovery { State = State() }; var a = new FakeEject(); var p = EjectService.Plan(d.State, "volume-a"); d.State.Volumes[0].Serial = "new"; Throws<SafetyException>(() => new EjectService(d, a, new Operations()).Execute(p)); Check(a.Calls == 0, "Wrong card ejected"); });
            Test("changed extent blocks old confirmation", delegate { var d = new FakeDiscovery { State = State() }; var p = EjectService.Plan(d.State, "volume-a"); d.State.Volumes[0].Extents = "7:1:1000"; Throws<SafetyException>(() => new EjectService(d, new FakeEject(), new Operations()).Execute(p)); });
            Test("veto reason and name reported", delegate { var d = new FakeDiscovery { State = State() }; var a = new FakeEject { Result = new EjectResult { Code = 0x17, Veto = 5, Name = "Fixture app" } }; var r = new EjectService(d, a, new Operations()).Execute(EjectService.Plan(d.State, "volume-a")); Check(!r.Success && r.Message.Contains("OutstandingOpen") && r.Message.Contains("Fixture app"), "Veto lost"); });
            Test("reservations prevent import/eject overlap and release", delegate { var ops = new Operations(); using (ops.Reserve(new[] { 7, 8 })) Throws<SafetyException>(() => ops.Reserve(new[] { 8 })); using (ops.Reserve(new[] { 8 })) {} });
            Test("different volumes on same physical disk cannot import", delegate { var s = State(); s.Volumes.Add(new Volume { Id = "destination", Serial = "other-partition", Disks = new List<int> { 7 } }); Throws<SafetyException>(() => VolumeImportGuard.PlanProtection(s, "volume-a", "destination")); });
            Test("separate LUN on same removable device cannot be destination", delegate { var s = State(); s.Disks.Add(new Disk { Number = 8, InstanceId = "disk-b", TargetId = "usb-a", Ancestors = new List<string> { "usb-a" } }); s.Volumes.Add(new Volume { Id = "destination", Serial = "other-LUN", Disks = new List<int> { 8 } }); Throws<SafetyException>(() => VolumeImportGuard.PlanProtection(s, "volume-a", "destination")); });
            Test("import reserves source sibling disks and destination", delegate { var s = State(); s.Disks.Add(new Disk { Number = 8, InstanceId = "disk-b", Ancestors = new List<string> { "usb-a" } }); s.Disks.Add(new Disk { Number = 0, InstanceId = "internal" }); s.Volumes.Add(new Volume { Id = "destination", Serial = "OS", System = true, Disks = new List<int> { 0 } }); Check(VolumeImportGuard.PlanProtection(s, "volume-a", "destination").OrderBy(x => x).SequenceEqual(new[] { 0, 7, 8 }), "Missing protected disk"); });
            Test("generic NVMe SSD never confirmed CFexpress", delegate { var d = new Disk { Model = "Generic NVMe SSD", Bus = 17, Hotplug = true }; Check(!Classifier.Classify(d, true).Contains("CFexpress") && Classifier.Classify(d, true).Contains("unconfirmed"), "Overconfident classifier"); });
            Test("internal disk with DCIM remains internal", delegate { Check(Classifier.Classify(new Disk { Model = "SSD" }, true) == "Internal / fixed storage", "Internal misclassified"); });
            Test("hardware SD is only a candidate", delegate { Check(Classifier.Classify(new Disk { Bus = 12, Removable = true }, false).Contains("candidate"), "No SD evidence"); });
            Test("path prefix boundary enforced", delegate { Check(!SafePaths.Within(@"C:\card-other\file", @"C:\card"), "Prefix escaped"); });
            Test("ambiguous and alternate stream paths rejected", delegate { Throws<SafetyException>(() => SafePaths.Normalize(@"C:\card\..\outside")); Throws<SafetyException>(() => SafePaths.Normalize(@"C:\card\file:stream")); Throws<SafetyException>(() => SafePaths.Normalize(@"C:\card\folder.\file")); Throws<SafetyException>(() => SafePaths.Normalize(@"\\server\card")); });
            Test("copy preserves relative paths, unknown files, originals and receipts", delegate {
                using (var f = new Fixture()) { f.Put(@"DCIM\100\clip.mov", "video fixture"); f.Put(@"DCIM\100\clip.xml", "sidecar"); f.Put("unknown.data", "unknown"); var r = f.Run(x => {});
                    Check(r.Files == 3 && File.ReadAllText(Saved(r, @"DCIM\100\clip.mov")) == "video fixture" && r.Receipt.Files.All(x => x.RelativePath.StartsWith("Camera originals")) && Saved(r, @"DCIM\100\clip.mov").EndsWith(@"DCIM\100\clip.mov"), "Bad copy or unfamiliar structure flattened"); Check(File.Exists(r.ReceiptPath) && File.Exists(Path.Combine(f.Source, "unknown.data")), "Receipt/original missing"); }
            });
            Test("repeat import never overwrites earlier files", delegate { using (var f = new Fixture()) { f.Put("clip.mov", "first"); var a = f.Run(x => {}); f.Put("clip.mov", "second"); var b = f.Run(x => {}); Check(a.Folder == b.Folder && Saved(a, "clip.mov") != Saved(b, "clip.mov") && File.ReadAllText(Saved(a, "clip.mov")) == "first" && File.ReadAllText(Saved(b, "clip.mov")) == "second" && Saved(b, "clip.mov").Contains("Additional media"), "Overwritten or conflict group lost"); } });
            Test("source/destination nesting rejected", delegate { using (var f = new Fixture()) { Throws<SafetyException>(() => new VerifiedImporter(new Operations()).Run(f.Source, f.Source, Path.Combine(f.Source, "output"), "fixture", new FakeGuard(), CancellationToken.None, x => {})); } });
            Test("partial package selection rejected", delegate { using (var f = new Fixture()) { string clip = Path.Combine(f.Source, "CLIP"); Directory.CreateDirectory(clip); Throws<SafetyException>(() => new VerifiedImporter(new Operations()).Run(f.Source, clip, f.Destination, "fixture", new FakeGuard(), CancellationToken.None, x => {})); } });
            Test("empty source creates no batch or completion receipt", delegate { using (var f = new Fixture()) { Check(f.Run(x => {}).Files == 0 && Directory.GetFileSystemEntries(f.Destination).Length == 0, "Empty misleading batch"); } });
            Test("cancellation leaves originals and no completion receipt", delegate { using (var f = new Fixture()) { f.Put("clip.mov", new string('x', 2000000)); var token = new CancellationTokenSource(); Throws<OperationCanceledException>(() => new VerifiedImporter(new Operations()).Run(f.Source, f.Source, f.Destination, "fixture", new FakeGuard(), token.Token, x => token.Cancel())); Check(File.Exists(Path.Combine(f.Source, "clip.mov")) && Directory.GetFiles(f.Destination, "verified-import.json", SearchOption.AllDirectories).Length == 0 && Directory.GetFiles(f.Destination, "*.partial", SearchOption.AllDirectories).Length == 0, "Cancelled state unsafe"); } });
            foreach (bool cancelled in new[] { false, true }) {
                Test(cancelled ? "mid-transfer cancellation cleans partials and releases disks" : "mid-transfer disconnection cleans partials and releases disks", delegate { using (var f = new Fixture()) using (var token = new CancellationTokenSource()) {
                    byte[] bytes = new byte[3145851]; for (int i = 0; i < bytes.Length; i++) bytes[i] = (byte)(i * 13 + 3);
                    string original = Path.Combine(f.Source, "clip.mov"); File.WriteAllBytes(original, bytes);
                    var operations = new Operations(); var guard = new InterruptedGuard(f, cancelled ? token : null);
                    Action run = () => new VerifiedImporter(operations).Run(f.Source, f.Source, f.Destination, "fixture", guard, token.Token, x => {});
                    if (cancelled) Throws<OperationCanceledException>(run);
                    else Check(Throws<SafetyException>(run).Message == "Fixture disconnected during transfer", "Wrong interruption cause");
                    Check(guard.WrittenBeforeStop == 1048576 && File.ReadAllBytes(original).SequenceEqual(bytes), "Test did not interrupt after a copied chunk or source changed");
                    Check(Directory.GetFiles(f.Destination, "*.partial", SearchOption.AllDirectories).Length == 0 && Directory.GetFiles(f.Destination, "verified-import.json", SearchOption.AllDirectories).Length == 0 && Directory.GetFiles(f.Destination, "clip.mov", SearchOption.AllDirectories).Length == 0, "Interrupted batch was published or partial retained");
                    var retry = new VerifiedImporter(operations).Run(f.Source, f.Source, f.Destination, "fixture", new FakeGuard(), CancellationToken.None, x => {});
                    Check(retry.Files == 1 && retry.Bytes == bytes.Length && File.ReadAllBytes(Saved(retry, "clip.mov")).SequenceEqual(bytes), "Disk reservation was not released or retry was incorrect");
                } });
            }
            Test("identity loss leaves originals and incomplete batch", delegate { using (var f = new Fixture()) { f.Put("clip.mov", "media"); var guard = new FakeGuard { FailAt = 3 }; var error = Throws<SafetyException>(() => new VerifiedImporter(new Operations()).Run(f.Source, f.Source, f.Destination, "fixture", guard, CancellationToken.None, x => {})); Check(guard.Calls == 3 && error.Message == "Fixture disconnected" && File.Exists(Path.Combine(f.Source, "clip.mov")) && Directory.GetFiles(f.Destination, "verified-import.json", SearchOption.AllDirectories).Length == 0, "Identity-loss state unsafe"); } });
            Test("destination conflict never overwritten", delegate { using (var f = new Fixture()) { f.Put("clip.mov", "new-media"); Throws<System.ComponentModel.Win32Exception>(() => f.Run(x => { if (x.StartsWith("Copying")) { string batch = Directory.GetDirectories(f.Destination).Single(); File.WriteAllText(Path.Combine(batch, "clip.mov"), "existing"); } })); string saved = Directory.GetFiles(f.Destination, "clip.mov", SearchOption.AllDirectories).Single(); Check(File.ReadAllText(saved) == "existing", "Conflict overwritten"); } });
            Test("source cannot be modified while copied", delegate { using (var f = new Fixture()) { f.Put("clip.mov", "original"); bool denied = false; var r = f.Run(x => { if (x.StartsWith("Copying")) { try { File.WriteAllText(Path.Combine(f.Source, "clip.mov"), "changed"); } catch (IOException) { denied = true; } } }); Check(denied && r.Files == 1, "Source not pinned"); } });
            Test("earlier saved copy tampering invalidates completion", delegate { using (var f = new Fixture()) { f.Put("a.mov", "first"); f.Put("b.mov", "second"); bool changed = false; var error = Throws<SafetyException>(() => f.Run(x => { if (x == "Copying b.mov") { string batch = Directory.GetDirectories(f.Destination).Single(); File.WriteAllText(Path.Combine(batch, "a.mov"), "tampered"); changed = true; } })); Check(changed && error.Message.Contains("earlier saved copy changed") && Directory.GetFiles(f.Destination, "verified-import.json", SearchOption.AllDirectories).Length == 0, "Tampering marked complete or test never reached tampering"); } });
            Test("earlier source changes invalidate completion", delegate { using (var f = new Fixture()) { f.Put("a.mov", "first"); f.Put("b.mov", "second"); bool changed = false; var error = Throws<SafetyException>(() => f.Run(x => { if (x == "Copying b.mov") { File.WriteAllText(Path.Combine(f.Source, "a.mov"), "changed"); changed = true; } })); Check(changed && error.Message.Contains("source recording changed") && Directory.GetFiles(f.Destination, "verified-import.json", SearchOption.AllDirectories).Length == 0, "Changed source marked complete or test never reached source change"); } });
            Test("directory rename is denied while source path is pinned", delegate { using (var f = new Fixture()) { f.Put(@"DCIM\clip.mov", "media"); bool denied = false; f.Run(x => { if (x.StartsWith("Copying")) { try { Directory.Move(Path.Combine(f.Source, "DCIM"), Path.Combine(f.Source, "renamed")); } catch (IOException) { denied = true; } } }); Check(denied, "Source directory not pinned"); } });
            Test("source junction escape rejected", delegate { using (var f = new Fixture()) {
                string outside = Path.Combine(f.Root, "outside"); Directory.CreateDirectory(outside); File.WriteAllText(Path.Combine(outside, "private.mov"), "outside"); string link = Path.Combine(f.Source, "link");
                CreateJunction(f, link, outside);
                var error = Throws<SafetyException>(() => f.Run(x => {})); Check(error.Message.Contains("reparse points") && Directory.GetFileSystemEntries(f.Destination).Length == 0, "Link imported or wrong rejection cause");
            } });
            Test("destination ancestor junction rejected before copying", delegate { using (var f = new Fixture()) { f.Put("clip.mov", "media"); string outside = Path.Combine(f.Root, "outside"); Directory.CreateDirectory(outside); string link = Path.Combine(f.Destination, "link"); CreateJunction(f, link, outside); var error = Throws<SafetyException>(() => new VerifiedImporter(new Operations()).Run(f.Source, f.Source, link, "fixture", new FakeGuard(), CancellationToken.None, x => {})); Check(error.Message.Contains("Reparse points") && Directory.GetFileSystemEntries(outside).Length == 0, "Destination escaped or wrong rejection cause"); } });
            Test("profile survives replacement and rejects a reformatted card", delegate { using (var f = new Fixture()) { var s = State(); string root = Path.Combine(f.Root, "profiles"); var p = new ImportProfile { VolumeId = s.Volumes[0].Id, VolumeSerial = s.Volumes[0].Serial, DiskInstance = s.Disks[0].InstanceId, RelativeSource = "DCIM", Destination = f.Destination }; Profiles.Save(p, root); Check(Profiles.Load(s.Volumes[0], s.Disks[0], root).RelativeSource == "DCIM", "Profile roundtrip failed"); p.RelativeSource = "PRIVATE"; Profiles.Save(p, root); Check(Profiles.Load(s.Volumes[0], s.Disks[0], root).RelativeSource == "PRIVATE", "Profile replacement failed"); s.Volumes[0].Serial = "formatted"; Throws<SafetyException>(() => Profiles.Load(s.Volumes[0], s.Disks[0], root)); } });
            Test("binary and empty media copies have complete verified receipts", delegate { using (var f = new Fixture()) {
                byte[] bytes = new byte[2097275]; for (int i = 0; i < bytes.Length; i++) bytes[i] = (byte)(i * 31 + 7);
                File.WriteAllBytes(Path.Combine(f.Source, "large.mov"), bytes); File.WriteAllBytes(Path.Combine(f.Source, "empty.xmp"), new byte[0]); var result = f.Run(x => {});
                Check(result.Files == 2 && result.Bytes == bytes.Length, "Incorrect large/empty byte counts");
                Check(File.ReadAllBytes(Saved(result, "large.mov")).SequenceEqual(bytes), "Large copy differs or lost its tail");
                ImportReceipt receipt; using (var file = File.OpenRead(result.ReceiptPath)) receipt = (ImportReceipt)new System.Runtime.Serialization.Json.DataContractJsonSerializer(typeof(ImportReceipt)).ReadObject(file);
                string expected; using (var hash = System.Security.Cryptography.SHA256.Create()) expected = BitConverter.ToString(hash.ComputeHash(bytes)).Replace("-", "").ToLowerInvariant();
                Check(receipt.OriginalsKept && receipt.SourceVolume == "fixture-volume" && receipt.Files.Count == 2 && receipt.Files.Single(x => x.Bytes == bytes.Length).Sha256 == expected && receipt.Files.Single(x => x.Bytes == 0).Sha256 == "e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855", "Receipt is incomplete or hashes are incorrect");
            } });
            Test("production configuration storage supports package redirection", delegate {
                var state = State(); state.Volumes[0].Id = "fixture-only-" + Guid.NewGuid().ToString("N"); string root = Profiles.Root;
                string name; using (var hash = System.Security.Cryptography.SHA256.Create()) name = BitConverter.ToString(hash.ComputeHash(System.Text.Encoding.UTF8.GetBytes(state.Volumes[0].Id))).Replace("-", "") + ".json";
                string path = Path.Combine(root, name); Check(!File.Exists(path), "Disposable profile already exists");
                using (SafePaths.LockDirectories(root)) {
                    try { Profiles.Save(new ImportProfile { VolumeId = state.Volumes[0].Id, VolumeSerial = state.Volumes[0].Serial, DiskInstance = state.Disks[0].InstanceId, RelativeSource = "DCIM", Destination = @"Z:\fixture-only" }); Check(Profiles.Load(state.Volumes[0], state.Disks[0]).RelativeSource == "DCIM", "Production configuration roundtrip failed"); }
                    finally { if (File.Exists(path)) { Check((File.GetAttributes(path) & FileAttributes.ReparsePoint) == 0 && SafePaths.Within(path, root), "Unsafe disposable profile cleanup"); File.Delete(path); } }
                }
            });
            Test("video selection preserves still pairs and ambiguous companions", delegate { using (var f = new Fixture()) {
                foreach (string name in new[] { "photo.JPG", "photo.MOV", "clip.mp4", "clip.mp4.xmp", "clip.xmp", "clip.bin", "orphan.LRV", "unknown.raw" }) f.Put(name, name);
                var result = new VerifiedImporter(new Operations()).Run(f.Source, f.Source, f.Destination, "fixture", new FakeGuard(), CancellationToken.None, x => {}, new MediaOptions(true, true));
                var saved = result.Receipt.Files.Select(x => Path.GetFileName(Path.Combine(result.Folder, x.RelativePath))).OrderBy(x => x).ToArray();
                Check(result.HasSelectedMedia && saved.SequenceEqual(new[] { "clip.mp4", "clip.mp4.xmp" }), "Live Photo, orphan preview or ambiguous companion selected");
                Check(Directory.GetFiles(f.Source).Length == 8, "Selection deleted originals");
            } });
            Test("Insta360 lens pairs remain intact with previews disabled", delegate { using (var f = new Fixture()) {
                foreach (string name in new[] { "VID_20261006_000001_00_001.insv", "VID_20261006_000001_10_001.insv", "LRV_20261006_000001_01_001.lrv", "VID_20261006_000001_00_001.thm" }) f.Put(name, name);
                var withPreviews = MediaSelection.Plan(VerifiedImporter.Scan(f.Source, CancellationToken.None), f.Source, f.Source, new MediaOptions(true, true));
                Check(withPreviews.Media.Count == 4, "Associated lens preview or thumbnail omitted by default");
                var result = new VerifiedImporter(new Operations()).Run(f.Source, f.Source, f.Destination, "fixture", new FakeGuard(), CancellationToken.None, x => {}, new MediaOptions(true, false));
                Check(result.Files == 2 && Directory.GetFiles(result.Folder, "*.insv", SearchOption.AllDirectories).Length == 2 && Directory.GetFiles(result.Folder, "*.lrv", SearchOption.AllDirectories).Length == 0 && Directory.GetFiles(f.Source).Length == 4, "Lens names changed, preview copied or originals removed");
            } });
            Test("Sony package keeps audio metadata and required previews", delegate { using (var f = new Fixture()) {
                foreach (string name in new[] { @"PRIVATE\M4ROOT\CLIP\clip.MP4", @"PRIVATE\M4ROOT\CLIP\clip.XML", @"PRIVATE\M4ROOT\THMBNL\clip.JPG", @"PRIVATE\M4ROOT\AUDIO\clip.WAV", @"DCIM\photo.JPG", @"DCIM\photo.MOV" }) f.Put(name, name);
                var result = new VerifiedImporter(new Operations()).Run(f.Source, f.Source, f.Destination, "fixture", new FakeGuard(), CancellationToken.None, x => {}, new MediaOptions(true, false));
                Check(result.Files == 4 && File.Exists(Saved(result, @"PRIVATE\M4ROOT\THMBNL\clip.JPG")) && result.Receipt.Files.All(x => x.RelativePath.StartsWith("Camera originals") && !x.SourceRelativePath.StartsWith("DCIM")), "Package dependency dropped or photo workflow included");
            } });
            Test("P2 package keeps LASTCLIP and audio outside the selected video", delegate { using (var f = new Fixture()) {
                foreach (string name in new[] { @"CONTENTS\VIDEO\clip.MXF", @"CONTENTS\AUDIO\clip.MXF", @"CONTENTS\ICON\clip.BMP", "LASTCLIP.TXT", @"DCIM\photo.JPG" }) f.Put(name, name);
                var result = new VerifiedImporter(new Operations()).Run(f.Source, f.Source, f.Destination, "fixture", new FakeGuard(), CancellationToken.None, x => {}, new MediaOptions(true, false));
                Check(result.Files == 4 && File.Exists(Saved(result, "LASTCLIP.TXT")) && result.Receipt.Files.All(x => x.RelativePath.StartsWith("Camera originals")), "P2 support file omitted");
            } });
            Test("partial RED reel and unfamiliar video package stop before copying", delegate { using (var f = new Fixture()) {
                f.Put(@"reel.RDC\clip.R3D", "recording");
                Throws<SafetyException>(() => new VerifiedImporter(new Operations()).Run(f.Source, Path.Combine(f.Source, "reel.RDC"), f.Destination, "fixture", new FakeGuard(), CancellationToken.None, x => {}, new MediaOptions(false, true)));
                f.Put("PRO.PRJ", "unknown package");
                Throws<SafetyException>(() => new VerifiedImporter(new Operations()).Run(f.Source, f.Source, f.Destination, "fixture", new FakeGuard(), CancellationToken.None, x => {}, new MediaOptions(true, true)));
                Check(Directory.GetFileSystemEntries(f.Destination).Length == 0, "Unsupported package wrote a partial batch");
                Check(f.Run(x => {}).Files == 2, "Whole-card all-media backup rejected");
            } });
            Test("device index backup is separate from a selected-media result", delegate { using (var f = new Fixture()) {
                f.Put("fileinfo_list.list", "index"); f.Put("photo.JPG", "photo");
                var result = new VerifiedImporter(new Operations()).Run(f.Source, f.Source, f.Destination, "fixture", new FakeGuard(), CancellationToken.None, x => {}, new MediaOptions(true, true));
                Check(result.Files == 1 && !result.HasSelectedMedia && File.Exists(Saved(result, "fileinfo_list.list")) && File.ReadAllText(Path.Combine(f.Source, "fileinfo_list.list")) == "index", "Index marked as media or original removed");
                ImportReceipt receipt; using (var file = File.OpenRead(result.ReceiptPath)) receipt = (ImportReceipt)new System.Runtime.Serialization.Json.DataContractJsonSerializer(typeof(ImportReceipt)).ReadObject(file);
                Check(!receipt.HasSelectedMedia && receipt.Files.Single().RelativePath.StartsWith(".easy-eject-metadata"), "Receipt cannot distinguish metadata-only import");
            } });
            Test("preview-only video selection creates no misleading completion batch", delegate { using (var f = new Fixture()) {
                f.Put("orphan.lrv", "preview"); f.Put("photo.jpg", "photo");
                var result = new VerifiedImporter(new Operations()).Run(f.Source, f.Source, f.Destination, "fixture", new FakeGuard(), CancellationToken.None, x => {}, new MediaOptions(true, false));
                Check(!result.HasSelectedMedia && result.Files == 0 && Directory.GetFileSystemEntries(f.Destination).Length == 0, "No matching video was marked complete");
            } });
            Test("shared defaults preserve custom choices and freeze an in-flight selection", delegate {
                var state = State(); var defaults = new ImportDefaults(); var profile = defaults.NewProfile(state.Volumes[0], state.Disks[0]);
                Check(profile.FollowsDefaults && profile.Resolve(defaults).VideosOnly && profile.Resolve(defaults).IncludePreviews && profile.Resolve(defaults).CleanLayout, "Factory selection or inheritance incorrect");
                var frozen = profile.Resolve(defaults); defaults.VideosOnly = false; defaults.IncludePreviews = false; defaults.CleanLayout = false;
                var current = profile.Resolve(defaults);
                Check(!current.VideosOnly && !current.IncludePreviews && !current.CleanLayout && frozen.VideosOnly && frozen.IncludePreviews && frozen.CleanLayout && profile.VideosOnly == true && profile.IncludePreviews == true && profile.CleanLayout == true, "Resolution changed stored profile or frozen options");
                profile.SetCustom(true, defaults); profile.VideosOnly = true;
                defaults.VideosOnly = false; defaults.IncludePreviews = true; defaults.CleanLayout = true;
                Check(!profile.FollowsDefaults && profile.Resolve(defaults).VideosOnly && !profile.Resolve(defaults).IncludePreviews && !profile.Resolve(defaults).CleanLayout, "Custom settings followed shared edits");
                profile.SetCustom(false, defaults);
                Check(profile.FollowsDefaults && !profile.Resolve(defaults).VideosOnly && profile.Resolve(defaults).IncludePreviews && profile.Resolve(defaults).CleanLayout, "Switch to defaults failed");
            });
            Test("legacy profiles remain custom all-media after settings roundtrip", delegate { using (var f = new Fixture()) {
                var state = State(); string root = Path.Combine(f.Root, "profiles");
                var profile = new ImportProfile { VolumeId = state.Volumes[0].Id, VolumeSerial = state.Volumes[0].Serial, DiskInstance = state.Disks[0].InstanceId, RelativeSource = "DCIM", Destination = f.Destination };
                Profiles.Save(profile, root);
                // Remove new option keys entirely to represent an actual pre-options JSON file.
                string path = Directory.GetFiles(root).Single();
                string legacy = File.ReadAllText(path); legacy = System.Text.RegularExpressions.Regex.Replace(legacy, @",?""(?:UsesImportDefaults|VideosOnly|IncludePreviews|CleanLayout|AskToEject)"":null", "").Replace("{,", "{"); File.WriteAllText(path, legacy);
                var loaded = Profiles.Load(state.Volumes[0], state.Disks[0], root); var options = loaded.Resolve(new ImportDefaults { VideosOnly = true, IncludePreviews = false });
                Check(!loaded.FollowsDefaults && !options.VideosOnly && options.IncludePreviews && !options.CleanLayout, "Legacy import semantics changed");
                loaded.SetCustom(false, new ImportDefaults()); Profiles.Save(loaded, root); var restored = Profiles.Load(state.Volumes[0], state.Disks[0], root);
                Check(restored.FollowsDefaults && restored.Resolve(new ImportDefaults()).VideosOnly, "Explicit migration was not retained");
            } });
            Test("defaults persistence does not overwrite device profiles", delegate { using (var f = new Fixture()) {
                var state = State(); string root = Path.Combine(f.Root, "profiles"); Directory.CreateDirectory(root);
                var empty = ImportDefaults.Load(root); Check(empty.Snapshot().VideosOnly && empty.Snapshot().IncludePreviews, "Absent settings not factory defaults");
                var profile = empty.NewProfile(state.Volumes[0], state.Disks[0]); profile.Destination = f.Destination; Profiles.Save(profile, root);
                ImportDefaults.Save(new ImportDefaults { VideosOnly = false, IncludePreviews = false }, root); ImportDefaults.Save(new ImportDefaults { VideosOnly = true, IncludePreviews = false }, root);
                Check(Directory.GetFiles(root).Length == 2 && ImportDefaults.Load(root).Snapshot().VideosOnly && !ImportDefaults.Load(root).Snapshot().IncludePreviews && Profiles.Load(state.Volumes[0], state.Disks[0], root).Destination == f.Destination, "Settings replacement damaged another profile");
                File.WriteAllText(Path.Combine(root, "import-defaults.json"), "{}"); Check(ImportDefaults.Load(root).Snapshot().VideosOnly && ImportDefaults.Load(root).Snapshot().IncludePreviews, "Missing defaults fields changed factory behavior");
                File.WriteAllText(Path.Combine(root, "import-defaults.json"), "null"); Throws<SafetyException>(() => ImportDefaults.Load(root));
                File.WriteAllText(Path.Combine(root, "import-defaults.json"), "invalid-json"); Throws<System.Runtime.Serialization.SerializationException>(() => ImportDefaults.Load(root));
                File.WriteAllText(Path.Combine(root, "import-defaults.json"), "{}" + new string(' ', 20000)); Throws<SafetyException>(() => ImportDefaults.Load(root));
            } });
            Test("forget profile leaves defaults and all fixture media intact", delegate { using (var f = new Fixture()) {
                var state = State(); string root = Path.Combine(f.Root, "profiles"); var defaults = new ImportDefaults(); var profile = defaults.NewProfile(state.Volumes[0], state.Disks[0]); profile.Destination = f.Destination;
                f.Put("clip.mov", "source-original"); Profiles.Save(profile, root); ImportDefaults.Save(defaults, root);
                string serial = state.Volumes[0].Serial; state.Volumes[0].Serial = "changed"; Throws<SafetyException>(() => Profiles.Remove(state.Volumes[0], state.Disks[0], root)); state.Volumes[0].Serial = serial;
                Check(Profiles.Load(state.Volumes[0], state.Disks[0], root) != null, "Stale device identity removed the profile");
                Profiles.Remove(state.Volumes[0], state.Disks[0], root);
                Check(Profiles.Load(state.Volumes[0], state.Disks[0], root) == null && Directory.GetFiles(root).Length == 1 && ImportDefaults.Load(root).Snapshot().VideosOnly && File.ReadAllText(Path.Combine(f.Source, "clip.mov")) == "source-original", "Forget affected media or defaults");
            } });
            Test("profile editor mode changes stay isolated until saving", delegate { using (var f = new Fixture()) {
                var state = State(); var defaults = new ImportDefaults(); var original = defaults.NewProfile(state.Volumes[0], state.Disks[0]); original.Destination = f.Destination;
                using (var dialog = new ImportProfileDialog(original, f.Source, "Fixture card", defaults, true)) {
                    Check(dialog.UseDefaults.Checked && !dialog.Selection.Enabled, "Default values are editable as custom");
                    dialog.UseCustom.Checked = true; Check(!dialog.Draft.FollowsDefaults && dialog.Selection.Enabled, "Custom radio did not enable fields");
                    dialog.Selection.Videos.Checked = false; dialog.Selection.Previews.Checked = false; dialog.Selection.Clean.Checked = false; dialog.SaveCustomSelection();
                    Check(!dialog.Draft.Resolve(defaults).VideosOnly && !dialog.Draft.Resolve(defaults).IncludePreviews && !dialog.Draft.Resolve(defaults).CleanLayout && original.FollowsDefaults && original.VideosOnly == true && original.CleanLayout == true, "Draft or original isolation broken");
                    dialog.UseDefaults.Checked = true; Check(dialog.Draft.FollowsDefaults && !dialog.Selection.Enabled && dialog.Selection.Videos.Checked && dialog.Selection.Previews.Checked, "Switching back did not inherit defaults");
                    Check(dialog.AcceptButton != null && dialog.CancelButton != null, "Save/Cancel keyboard controls unavailable");
                }
                Check(original.FollowsDefaults && original.Resolve(defaults).VideosOnly, "Closing an unsaved editor mutated its profile");
            } });
            Test("editing defaults during a copy affects only the next import", delegate { using (var f = new Fixture()) {
                f.Put("clip.mp4", "video"); f.Put("photo.jpg", "photo"); var state = State(); var defaults = new ImportDefaults(); var profile = defaults.NewProfile(state.Volumes[0], state.Disks[0]); var frozen = profile.Resolve(defaults);
                var importer = new VerifiedImporter(new Operations());
                var first = importer.Run(f.Source, f.Source, f.Destination, "fixture", new FakeGuard(), CancellationToken.None, x => { if (x.StartsWith("Copying")) defaults.VideosOnly = false; }, frozen);
                var second = importer.Run(f.Source, f.Source, f.Destination, "fixture", new FakeGuard(), CancellationToken.None, x => {}, profile.Resolve(defaults));
                Check(first.Files == 1 && first.Receipt.Files.All(x => !x.SourceRelativePath.EndsWith(".jpg")) && second.Files == 2 && second.Receipt.Files.Count(x => x.SourceRelativePath.EndsWith(".jpg")) == 1 && Directory.GetFiles(f.Source).Length == 2, "Running selection changed or future defaults were ignored");
            } });
            Test("identical repeated import reuses verified files and retains camera dates", delegate { using (var f = new Fixture()) {
                f.Put(@"DCIM\clip.mp4", "master"); f.Put(@"DCIM\clip.srt", "subtitle");
                DateTime recorded = new DateTime(2025, 4, 3, 12, 13, 14, DateTimeKind.Utc); File.SetLastWriteTimeUtc(Path.Combine(f.Source, @"DCIM\clip.mp4"), recorded);
                var importer = new VerifiedImporter(new Operations()); var options = new MediaOptions(true, true, true);
                var a = importer.Run(f.Source, f.Source, f.Destination, "fixture", new FakeGuard(), CancellationToken.None, x => {}, options);
                var b = importer.Run(f.Source, f.Source, f.Destination, "fixture", new FakeGuard(), CancellationToken.None, x => {}, options);
                Check(a.Files == 2 && a.Reused == 0 && b.Files == 2 && b.Reused == 2 && b.HasSelectedMedia && Saved(a, @"DCIM\clip.mp4") == Saved(b, @"DCIM\clip.mp4") && Path.GetDirectoryName(Saved(a, @"DCIM\clip.mp4")) == a.Folder, "Repeat duplicated media or failed clean layout");
                Check(File.GetLastWriteTimeUtc(Saved(a, @"DCIM\clip.mp4")) == recorded && Directory.GetFiles(a.Folder, "*.mp4", SearchOption.AllDirectories).Length == 1 && a.ReceiptPath != b.ReceiptPath && File.Exists(a.ReceiptPath) && File.Exists(b.ReceiptPath), "Camera time or immutable receipts lost");
                Check(b.Receipt.Files.All(x => x.Reused && File.Exists(Path.Combine(b.Folder, x.RelativePath))) && File.ReadAllText(Path.Combine(Path.GetDirectoryName(b.ReceiptPath), "import-audit.log")).Contains("Reused verified SHA256"), "Reuse omitted exact manifest or durable audit");
            } });
            foreach (bool clean in new[] { false, true }) Test("lens-pair conflicts preserve the whole group with clean layout=" + clean, delegate { using (var f = new Fixture()) {
                string[] names = { "VID_20261006_000001_00_001.insv", "VID_20261006_000001_10_001.insv", "VID_20261006_000001_00_001.insv.gyro" };
                foreach (string name in names) f.Put(name, "new-" + name);
                string date = Path.Combine(f.Destination, DateTime.Now.ToString("yyyy-MM-dd")); Directory.CreateDirectory(date); File.WriteAllText(Path.Combine(date, names[0]), "previous recording");
                var importer = new VerifiedImporter(new Operations()); var options = new MediaOptions(true, true, clean);
                var result = importer.Run(f.Source, f.Source, f.Destination, "fixture", new FakeGuard(), CancellationToken.None, x => {}, options);
                string folder = Path.GetDirectoryName(Saved(result, names[0]));
                Check(result.Files == 3 && folder.Contains("Additional media") && result.Receipt.Files.All(x => Path.GetDirectoryName(Path.Combine(result.Folder, x.RelativePath)) == folder) && names.All(x => File.ReadAllText(Path.Combine(folder, x)) == "new-" + x), "Conflict split or renamed a recording group");
                Check(File.ReadAllText(Path.Combine(date, names[0])) == "previous recording" && importer.Run(f.Source, f.Source, f.Destination, "fixture", new FakeGuard(), CancellationToken.None, x => {}, options).Reused == 3, "Prior media changed or stable conflict group not reused");
            } });
            Test("clean layout keeps semantic groups from different cameras separate", delegate { using (var f = new Fixture()) {
                foreach (string name in new[] { @"camera-a\clip.MOV", @"camera-a\clip.XMP", @"camera-b\clip.MP4", @"camera-b\clip.SRT" }) f.Put(name, name);
                var result = new VerifiedImporter(new Operations()).Run(f.Source, f.Source, f.Destination, "fixture", new FakeGuard(), CancellationToken.None, x => {}, new MediaOptions(true, true, true));
                string a = Path.GetDirectoryName(Saved(result, @"camera-a\clip.MOV")), b = Path.GetDirectoryName(Saved(result, @"camera-b\clip.MP4"));
                Check(a != b && a.Contains("Additional media") && b.Contains("Additional media") && Path.GetDirectoryName(Saved(result, @"camera-a\clip.XMP")) == a && Path.GetDirectoryName(Saved(result, @"camera-b\clip.SRT")) == b, "Different camera companions merged");
            } });
            Test("unfamiliar media preserves its selected folder and reuses the snapshot", delegate { using (var f = new Fixture()) {
                f.Put(@"DCIM\clip.mp4", "master"); f.Put(@"DCIM\unfamiliar.data", "unknown"); var importer = new VerifiedImporter(new Operations()); var options = new MediaOptions(false, true, true);
                var a = importer.Run(f.Source, Path.Combine(f.Source, "DCIM"), f.Destination, "fixture", new FakeGuard(), CancellationToken.None, x => {}, options);
                var b = importer.Run(f.Source, Path.Combine(f.Source, "DCIM"), f.Destination, "fixture", new FakeGuard(), CancellationToken.None, x => {}, options);
                Check(a.Receipt.Files.All(x => x.RelativePath.StartsWith("Camera originals") && Path.GetDirectoryName(Path.Combine(a.Folder, x.RelativePath)).EndsWith("DCIM")) && b.Reused == 2 && Saved(a, "unfamiliar.data") == Saved(b, "unfamiliar.data"), "Unknown media flattened or duplicate snapshot created");
            } });
            Test("proxy and master with the same filename retain distinct camera paths", delegate { using (var f = new Fixture()) {
                f.Put(@"DCIM\clip.mp4", "master"); f.Put(@"DCIM\Proxy\clip.mp4", "preview");
                var result = new VerifiedImporter(new Operations()).Run(f.Source, f.Source, f.Destination, "fixture", new FakeGuard(), CancellationToken.None, x => {}, new MediaOptions(true, true, true));
                Check(result.Files == 2 && Saved(result, @"DCIM\clip.mp4") != Saved(result, @"DCIM\Proxy\clip.mp4") && File.ReadAllText(Saved(result, @"DCIM\clip.mp4")) == "master" && File.ReadAllText(Saved(result, @"DCIM\Proxy\clip.mp4")) == "preview" && Saved(result, @"DCIM\Proxy\clip.mp4").EndsWith(@"DCIM\Proxy\clip.mp4"), "Proxy overwrote or became associated with master");
            } });
            Test("saved-copy change during reuse stops a new completion receipt", delegate { using (var f = new Fixture()) {
                f.Put("clip.mov", "original"); var first = f.Run(x => {}); string saved = Saved(first, "clip.mov"); bool changed = false;
                var error = Throws<SafetyException>(() => f.Run(x => { if (x.StartsWith("Reusing")) { File.WriteAllText(saved, "tampered"); changed = true; } }));
                Check(changed && error.Message.Contains("Destination changed") && File.ReadAllText(saved) == "tampered" && File.ReadAllText(Path.Combine(f.Source, "clip.mov")) == "original" && Directory.GetFiles(f.Destination, "verified-import.json", SearchOption.AllDirectories).Length == 1, "Changed reuse was restored, originals lost or failed operation marked complete");
            } });
            Test("late audit failure preserves originals and supports a verified retry", delegate { using (var f = new Fixture()) {
                f.Put("clip.mov", "original"); var operations = new Operations();
                var importer = new VerifiedImporter(operations, Native.FreeSpace, x => { if (x.StartsWith("Batch verification complete")) throw new IOException("Fixture audit write failed"); });
                Check(Throws<IOException>(() => importer.Run(f.Source, f.Source, f.Destination, "fixture", new FakeGuard(), CancellationToken.None, x => {})).Message == "Fixture audit write failed", "Wrong audit failure");
                Check(File.ReadAllText(Path.Combine(f.Source, "clip.mov")) == "original" && Directory.GetFiles(f.Destination, "verified-import.json", SearchOption.AllDirectories).Length == 0 && Directory.GetFiles(f.Destination, "import-audit.log", SearchOption.AllDirectories).Any(x => File.ReadAllText(x).Contains("Stopped; originals kept")), "Failed audit lost originals or implied completion");
                var retry = new VerifiedImporter(operations).Run(f.Source, f.Source, f.Destination, "fixture", new FakeGuard(), CancellationToken.None, x => {}); Check(retry.Reused == 1 && File.ReadAllText(Saved(retry, "clip.mov")) == "original", "Audit failure retained reservations or damaged copies");
            } });
            Test("free-space refusal leaves no copied media or completion receipt", delegate { using (var f = new Fixture()) {
                f.Put("clip.mov", "original"); var importer = new VerifiedImporter(new Operations(), x => 0UL, x => {});
                Check(Throws<SafetyException>(() => importer.Run(f.Source, f.Source, f.Destination, "fixture", new FakeGuard(), CancellationToken.None, x => {})).Message.Contains("free space"), "Wrong capacity failure");
                Check(File.ReadAllText(Path.Combine(f.Source, "clip.mov")) == "original" && Directory.GetFiles(f.Destination, "*.mov", SearchOption.AllDirectories).Length == 0 && Directory.GetFiles(f.Destination, "*.partial", SearchOption.AllDirectories).Length == 0 && Directory.GetFiles(f.Destination, "verified-import.json", SearchOption.AllDirectories).Length == 0, "Capacity refusal left misleading completed media");
            } });
            Test("same-size source mutation after planning cannot pass restored timestamps", delegate { using (var f = new Fixture()) {
                f.Put("a.mov", "first"); f.Put("b.mov", "second"); string file = Path.Combine(f.Source, "a.mov"); DateTime modified = File.GetLastWriteTimeUtc(file); bool changed = false;
                var error = Throws<SafetyException>(() => f.Run(x => { if (x == "Checking b.mov") { File.WriteAllText(file, "FIRST"); File.SetLastWriteTimeUtc(file, modified); changed = true; } }));
                Check(changed && error.Message.Contains("SHA-256") && File.ReadAllText(file) == "FIRST" && Directory.GetFiles(f.Destination, "verified-import.json", SearchOption.AllDirectories).Length == 0 && Directory.GetFiles(f.Destination, "*.partial", SearchOption.AllDirectories).Length == 0, "Cached plan accepted changed bytes or test failed earlier");
            } });
            Test("Unicode-equivalent source names fail before destination creation", delegate { using (var f = new Fixture()) {
                f.Put("\u00e9.mov", "composed"); f.Put("e\u0301.mov", "decomposed");
                Check(Throws<SafetyException>(() => f.Run(x => {})).Message.Contains("Unicode"), "Wrong name collision failure");
                Check(Directory.GetFiles(f.Source).Length == 2 && Directory.GetFileSystemEntries(f.Destination).Length == 0, "Ambiguous names mutated destination or source");
            } });
            Test("verified reuse needs no space reservation for another media copy", delegate { using (var f = new Fixture()) {
                f.Put("clip.mov", "original"); var first = f.Run(x => {}); int checks = 0;
                var second = new VerifiedImporter(new Operations(), x => { checks++; return 0UL; }, x => {}).Run(f.Source, f.Source, f.Destination, "fixture-volume", new FakeGuard(), CancellationToken.None, x => {});
                Check(second.Reused == 1 && checks == 0 && Saved(first, "clip.mov") == Saved(second, "clip.mov") && File.ReadAllText(Saved(second, "clip.mov")) == "original", "Reuse made a duplicate space reservation or changed the copy");
            } });
            Test("read-only saved copy cannot be claimed as flushed reuse", delegate { using (var f = new Fixture()) {
                f.Put("clip.mov", "original"); var first = f.Run(x => {}); string saved = Saved(first, "clip.mov"); FileAttributes attributes = File.GetAttributes(saved);
                try {
                    File.SetAttributes(saved, attributes | FileAttributes.ReadOnly); Throws<System.ComponentModel.Win32Exception>(() => f.Run(x => {}));
                    Check(File.ReadAllText(saved) == "original" && Directory.GetFiles(f.Destination, "verified-import.json", SearchOption.AllDirectories).Length == 1 && File.ReadAllText(Path.Combine(f.Source, "clip.mov")) == "original", "Unflushed reuse marked complete or data changed");
                } finally { File.SetAttributes(saved, attributes); }
            } });
            Test("history survives source removal and verifies only exact receipt files", delegate { using (var f = new Fixture()) {
                f.Put("clip.mov", "original"); f.Put("fileinfo_list.list", "device index"); var result = f.Run(x => {}); var batch = Batch(f, result); string root = Path.Combine(f.Root, "history");
                ImportHistory.Add(batch, root); File.Delete(Path.Combine(f.Source, "clip.mov")); File.Delete(Path.Combine(f.Source, "fileinfo_list.list")); File.WriteAllText(Path.Combine(result.Folder, "unrelated.mov"), "do not scan");
                var restored = ImportHistory.Load(root).Single(); int verified = 0;
                ImportHistory.Verify(restored, HistoryIdentity(), new FakeGuard(), new Operations(), CancellationToken.None, x => verified++);
                Check(restored.Id == result.Receipt.Id && restored.Originals.Count == 1 && restored.Originals.Single().Path == Saved(result, "clip.mov") && verified == 1 && File.ReadAllText(Path.Combine(result.Folder, "unrelated.mov")) == "do not scan", "History lost exact membership, depended on source or scanned extra files");
                string json = File.ReadAllText(Path.Combine(root, "completed-imports.json")); Check(!json.Contains("\"Epoch\"") && !json.Contains("\"DevInst\"") && !json.Contains("\"EjectPlan\"") && !json.Contains("\"ProtectedDisks\"") && !json.Contains("\"TargetId\""), "History persisted connection-bound authority");
            } });
            Test("history stores newest twenty batches without deleting copies or receipts", delegate { using (var f = new Fixture()) {
                string root = Path.Combine(f.Root, "history"); var retained = new List<ImportResult>();
                for (int i = 0; i < 22; i++) { f.Put("clip.mov", "recording-" + i); var result = f.Run(x => {}); retained.Add(result); ImportHistory.Add(Batch(f, result), root); }
                var values = ImportHistory.Load(root); Check(values.Count == 20 && values[0].Id == retained[21].Receipt.Id && values[19].Id == retained[2].Receipt.Id && retained.All(x => File.Exists(x.ReceiptPath) && File.Exists(Saved(x, "clip.mov"))), "History retention altered copies/receipts or ordering");
            } });
            Test("history refuses a changed destination before opening saved files", delegate { using (var f = new Fixture()) {
                f.Put("clip.mov", "original"); var batch = Batch(f, f.Run(x => {})); var wrong = HistoryIdentity(); wrong.VolumeSerial = "formatted"; int reads = 0;
                Check(Throws<SafetyException>(() => ImportHistory.Verify(batch, wrong, new FakeGuard(), new Operations(), CancellationToken.None, x => reads++)).Message.Contains("destination changed") && reads == 0 && File.ReadAllText(batch.Originals.Single().Path) == "original", "Changed destination reached saved media");
            } });
            Test("persistent destination identity survives disk renumbering but rejects substitution", delegate {
                var state = State(); var identity = DestinationIdentity.Capture(state, "volume-a"); state.Disks[0].Number = 99; state.Disks[0].DevInst = 999; state.Volumes[0].Disks[0] = 99;
                Check(identity.Matches(DestinationIdentity.Capture(state, "volume-a")), "History identity depended on transient disk/devnode");
                state.Disks[0].Serial = "substitution"; Check(!identity.Matches(DestinationIdentity.Capture(state, "volume-a")), "Replacement disk accepted");
                state.Complete = false; Throws<SafetyException>(() => DestinationIdentity.Capture(state, "volume-a"));
            });
            Test("history refuses changed receipt and changed saved original", delegate { using (var f = new Fixture()) {
                f.Put("clip.mov", "original"); var batch = Batch(f, f.Run(x => {})); string receipt = File.ReadAllText(batch.ReceiptPath); File.AppendAllText(batch.ReceiptPath, " ");
                Check(Throws<SafetyException>(() => ImportHistory.Verify(batch, HistoryIdentity(), new FakeGuard(), new Operations(), CancellationToken.None, x => {})).Message.Contains("receipt changed"), "Edited receipt accepted");
                File.WriteAllText(batch.ReceiptPath, receipt); string original = batch.Originals.Single().Path; File.WriteAllText(original, "ORIGINAL");
                Check(Throws<SafetyException>(() => ImportHistory.Verify(batch, HistoryIdentity(), new FakeGuard(), new Operations(), CancellationToken.None, x => {})).Message.Contains("original changed") && File.ReadAllText(original) == "ORIGINAL", "Changed saved original accepted or restored");
            } });
            Test("metadata-only and failed imports cannot enter completed history", delegate { using (var f = new Fixture()) {
                f.Put("fileinfo_list.list", "index"); var result = f.Run(x => {}); string root = Path.Combine(f.Root, "history"); ImportHistory.Add(Batch(f, result), root);
                Check(!Directory.Exists(root), "Index-only run created sharing history");
                f.Put("clip.mov", "original"); var source = new CancellationTokenSource(); source.Cancel(); Throws<OperationCanceledException>(() => new VerifiedImporter(new Operations()).Run(f.Source, f.Source, f.Destination, "fixture", new FakeGuard(), source.Token, x => {}));
                Check(!Directory.Exists(root), "Cancelled import persisted completion history"); source.Dispose();
            } });
            Test("history rejects corrupt, duplicate and escaping records without replacing saved state", delegate { using (var f = new Fixture()) {
                f.Put("clip.mov", "original"); var batch = Batch(f, f.Run(x => {})); string root = Path.Combine(f.Root, "history"); ImportHistory.Add(batch, root); string path = Path.Combine(root, "completed-imports.json"), saved = File.ReadAllText(path);
                batch.Originals[0].Path = Path.Combine(f.Source, "clip.mov"); Throws<SafetyException>(() => ImportHistory.Add(batch, root)); Check(File.ReadAllText(path) == saved, "Invalid entry replaced history");
                File.WriteAllText(path, "null"); Throws<SafetyException>(() => ImportHistory.Load(root)); File.WriteAllText(path, "invalid"); Throws<System.Runtime.Serialization.SerializationException>(() => ImportHistory.Load(root));
                File.WriteAllText(path, saved); var good = ImportHistory.Load(root).Single(); Throws<SafetyException>(() => ImportHistory.Add(good, root));
                Profiles.WriteJson(new List<CompletedBatch> { good, good }, typeof(List<CompletedBatch>), root, "completed-imports.json"); Throws<SafetyException>(() => ImportHistory.Load(root));
                File.WriteAllText(path, new string(' ', 16777217)); Throws<SafetyException>(() => ImportHistory.Load(root));
            } });
            Test("history verification rejects path substitution and releases reservations after cancellation", delegate { using (var f = new Fixture()) {
                f.Put(@"DCIM\clip.mov", "original"); var batch = Batch(f, f.Run(x => {})); string parent = Path.GetDirectoryName(batch.Originals.Single().Path), moved = Path.Combine(f.Root, "moved-copy"); Directory.Move(parent, moved); CreateJunction(f, parent, moved);
                Throws<SafetyException>(() => ImportHistory.Verify(batch, HistoryIdentity(), new FakeGuard(), new Operations(), CancellationToken.None, x => {})); Directory.Delete(parent, false); Directory.Move(moved, parent);
                var operations = new Operations(); using (var cancellation = new CancellationTokenSource()) { cancellation.Cancel(); Throws<OperationCanceledException>(() => ImportHistory.Verify(batch, HistoryIdentity(), new FakeGuard(), operations, cancellation.Token, x => {})); }
                ImportHistory.Verify(batch, HistoryIdentity(), new FakeGuard(), operations, CancellationToken.None, x => {}); Check(File.ReadAllText(batch.Originals.Single().Path) == "original", "Cancelled verification altered copies or retained reservations");
            } });
            Test("history store snapshots callers and refuses fabricated extra membership", delegate { using (var f = new Fixture()) {
                f.Put("clip.mov", "original"); var result = f.Run(x => {}); var batch = Batch(f, result); string root = Path.Combine(f.Root, "history"); ImportHistory.Add(batch, root);
                batch.DeviceName = "Changed caller"; batch.Destination.VolumeSerial = "changed"; batch.Originals.Clear(); var restored = ImportHistory.Load(root).Single(); Check(restored.DeviceName == "Fixture camera" && restored.Destination.VolumeSerial == "ABC" && restored.Originals.Count == 1, "Saved snapshot was mutable through caller");
                string extra = Path.Combine(result.Folder, "extra.mov"); File.WriteAllText(extra, "extra"); restored.Originals.Add(new VerifiedCopy { Path = extra, Sha256 = new string('a', 64), Bytes = 5 });
                Check(Throws<SafetyException>(() => ImportHistory.Verify(restored, HistoryIdentity(), new FakeGuard(), new Operations(), CancellationToken.None, x => {})).Message.Contains("exact receipt"), "Fabricated membership reached saved files");
            } });
            Test("history verification detects disconnect and respects shared disk reservations", delegate { using (var f = new Fixture()) {
                f.Put("a.mov", "first"); f.Put("b.mov", "second"); var batch = Batch(f, f.Run(x => {})); var operations = new Operations();
                Check(Throws<SafetyException>(() => ImportHistory.Verify(batch, HistoryIdentity(), new FakeGuard { FailAt = 3 }, operations, CancellationToken.None, x => {})).Message.Contains("disconnected"), "Destination loss accepted");
                using (operations.Reserve(new[] { 701 })) Check(Throws<SafetyException>(() => ImportHistory.Verify(batch, HistoryIdentity(), new FakeGuard(), operations, CancellationToken.None, x => {})).Message.Contains("busy"), "Verification ignored destination reservation");
                ImportHistory.Verify(batch, HistoryIdentity(), new FakeGuard(), operations, CancellationToken.None, x => {}); Check(File.ReadAllText(batch.Originals[0].Path) == "first", "Failure changed saved files or retained reservations");
            } });
            Test("history capture refuses a receipt changed after import completion", delegate { using (var f = new Fixture()) {
                f.Put("clip.mov", "original"); var result = f.Run(x => {}); File.AppendAllText(result.ReceiptPath, " ");
                Check(Throws<SafetyException>(() => Batch(f, result)).Message.Contains("does not match this operation") && File.ReadAllText(Saved(result, "clip.mov")) == "original", "Changed receipt became the trusted import snapshot");
            } });
            Test("history dialog displays exact membership through an isolated snapshot", delegate { using (var f = new Fixture()) {
                f.Put("clip.mov", "original"); var batch = Batch(f, f.Run(x => {}));
                using (var dialog = new ImportHistoryDialog(new[] { batch })) {
                    IntPtr handle = dialog.Handle; CreateRenderHandles(dialog); dialog.Batches.Items[0].Selected = true;
                    batch.DeviceName = "Edited outside"; batch.Originals.Clear(); var selected = dialog.SelectedBatch;
                    Check(handle != IntPtr.Zero && !dialog.Visible && selected != null && selected.DeviceName == "Fixture camera" && selected.Originals.Count == 1 && dialog.Details.Text.Contains("clip.mov"), "History UI depended on mutable caller or lost membership");
                    selected.Originals.Clear(); Check(dialog.SelectedBatch.Originals.Count == 1, "UI selection exposed stored entries for mutation");
                }
            } });
            Test("completion registry checks source root, volume, disk, topology and connection", delegate { using (var f = new Fixture()) {
                var state = State(); var entry = Completion(state, f, true); var registry = new ImportCompletionRegistry(); registry.Insert(entry);
                Check(registry.Matches(entry.Id, state, "volume-a", @"E:\"), "Exact source rejected");
                Check(!registry.Matches(entry.Id, state, "replacement", @"E:\") && !registry.Matches(entry.Id, state, "volume-a", @"E:\DCIM") && !registry.Matches("untrusted-payload", state, "volume-a", @"E:\"), "Untrusted source/token accepted");
                state.Disks[0].Serial = "changed"; Check(!registry.Matches(entry.Id, state, "volume-a", @"E:\"), "Changed physical identity accepted"); state.Disks[0].Serial = "serial-a";
                state.Volumes[0].Serial = "format"; Check(!registry.Matches(entry.Id, state, "volume-a", @"E:\"), "Reformatted source accepted"); state.Volumes[0].Serial = "ABC";
                state.Epoch++; Check(!registry.Matches(entry.Id, state, "volume-a", @"E:\"), "Reconnected identical card accepted"); state.Epoch--;
                state.Volumes.Add(new Volume { Id = "sibling", Serial = "DEF", Disks = new List<int> { 7 }, Extents = "7:1000:1000", Mounts = new List<string> { @"F:\" } });
                Check(!registry.Matches(entry.Id, state, "volume-a", @"E:\"), "New sibling accepted");
            } });
            Test("new import replaces only its source action and restart restores none", delegate { using (var f = new Fixture()) {
                var state = State(); var registry = new ImportCompletionRegistry(); var first = Completion(state, f, true); var next = Completion(state, f, false); registry.Insert(first);
                state.Disks.Add(new Disk { Number = 8, DevInst = 11, InstanceId = "disk-b", Serial = "serial-b", TargetId = "usb-b", TargetDevInst = 21, Ancestors = new List<string> { "disk-b", "usb-b" } });
                state.Volumes.Add(new Volume { Id = "volume-b", Serial = "DEF", Disks = new List<int> { 8 }, Mounts = new List<string> { @"F:\" } });
                var other = new ImportCompletion(new ImportCompletionOrigin(state, "volume-b", @"F:\"), "Other camera", f.Destination, f.Destination, HistoryIdentity(), true, "Other complete"); registry.Insert(other);
                Check(registry.Insert(next).SequenceEqual(new[] { first.Id }) && registry.Get(first.Id) == null && registry.Get(other.Id) != null, "New import retained old authority or erased another device");
                Check(registry.InvalidateSource(@"E:\").SequenceEqual(new[] { next.Id }) && registry.Get(other.Id) != null && new ImportCompletionRegistry().Entries.Length == 0, "Disconnect/restart authority incorrect");
                Check(registry.InvalidateAll().SequenceEqual(new[] { other.Id }) && registry.Entries.Length == 0, "Connection invalidation left tokens");
            } });
            Test("completion captures immutable removal plan and destination identity", delegate { using (var f = new Fixture()) {
                var state = State(); var entry = Completion(state, f, true); var changed = entry.Origin.Plan; changed.Disks[0] = 999; changed.TargetId = "hub"; changed.Affected[0] = "wrong";
                var destination = entry.Destination; destination.VolumeSerial = "wrong";
                Check(entry.Origin.Plan.Disks[0] == 7 && entry.Origin.Plan.TargetId == "usb-a" && entry.Destination.VolumeSerial == "ABC" && entry.Origin.Matches(state, "volume-a", @"E:\"), "Caller mutated captured authorization");
                state.Disks[0].DevInst = 999; Check(!entry.Origin.Matches(state, "volume-a", @"E:\"), "Origin retained a mutable source reference");
                Throws<SafetyException>(() => new ImportCompletionOrigin(state, "volume-a", @"E:\DCIM"));
            } });
            Test("post-import eject is explicit and consumed once before fake removal", delegate { using (var f = new Fixture()) {
                var state = State(); var registry = new ImportCompletionRegistry(); var entry = Completion(state, f, true); registry.Insert(entry); var fake = new FakeEject();
                var service = new CompletedEjectService(registry, new FakeDiscovery { State = state }, fake, new Operations(), x => "volume-a");
                Check(fake.Calls == 0 && registry.Get(entry.Id) != null, "Completion automatically ejected");
                Check(service.Execute(entry.Id).Success && fake.Calls == 1 && registry.Get(entry.Id) == null, "Explicit action not consumed once"); Throws<SafetyException>(() => service.Execute(entry.Id)); Check(fake.Calls == 1, "Repeat token requested another removal");
            } });
            Test("busy post-import eject preserves the choice for a later explicit attempt", delegate { using (var f = new Fixture()) {
                var state = State(); var registry = new ImportCompletionRegistry(); var entry = Completion(state, f, true); registry.Insert(entry); var fake = new FakeEject(); var operations = new Operations();
                var service = new CompletedEjectService(registry, new FakeDiscovery { State = state }, fake, operations, x => "volume-a");
                using (operations.Reserve(new[] { 7 })) Throws<SafetyException>(() => service.Execute(entry.Id));
                Check(fake.Calls == 0 && registry.Get(entry.Id) != null, "Busy operation consumed authorization or ejected"); Check(service.Execute(entry.Id).Success && fake.Calls == 1, "Busy reservation not released");
            } });
            Test("changed source path and topology cannot use a completed-import action", delegate { using (var f = new Fixture()) {
                var state = State(); var registry = new ImportCompletionRegistry(); var entry = Completion(state, f, true); registry.Insert(entry); var fake = new FakeEject(); var discovery = new FakeDiscovery { State = state };
                Throws<SafetyException>(() => new CompletedEjectService(registry, discovery, fake, new Operations(), x => "replacement-volume").Execute(entry.Id));
                state.Epoch++; Throws<SafetyException>(() => new CompletedEjectService(registry, discovery, fake, new Operations(), x => "volume-a").Execute(entry.Id)); state.Epoch--;
                state.Complete = false; Throws<SafetyException>(() => new CompletedEjectService(registry, discovery, fake, new Operations(), x => "volume-a").Execute(entry.Id));
                Check(fake.Calls == 0, "Stale action reached fake eject");
            } });
            Test("veto consumes completed action and never retries automatically", delegate { using (var f = new Fixture()) {
                var state = State(); var registry = new ImportCompletionRegistry(); var entry = Completion(state, f, true); registry.Insert(entry); var fake = new FakeEject { Result = new EjectResult { Code = 23, Veto = 6, Name = "Fixture process" } };
                var service = new CompletedEjectService(registry, new FakeDiscovery { State = state }, fake, new Operations(), x => "volume-a"); var result = service.Execute(entry.Id);
                Check(!result.Success && result.Message.Contains("Fixture process") && registry.Get(entry.Id) == null && fake.Calls == 1, "Veto lost reason or kept replay authority"); Throws<SafetyException>(() => service.Execute(entry.Id)); Check(fake.Calls == 1, "Veto retried");
            } });
            Test("completion with no removable node keeps files accessible without eject authority", delegate { using (var f = new Fixture()) {
                var state = State(); state.Disks[0].TargetId = ""; var entry = Completion(state, f, false); var registry = new ImportCompletionRegistry(); registry.Insert(entry);
                Check(entry.Origin.Plan == null && entry.Origin.BlockedReason != "" && !registry.Matches(entry.Id, state, "volume-a", @"E:\"), "Missing target authorized removal");
                int opened = 0; CompletionFolder.Open(entry, HistoryIdentity(), new FakeGuard(), new Operations(), x => { Check(x == f.Destination, "Wrong folder opened"); opened++; }); Check(opened == 1, "Safe destination access needed source eject authority");
            } });
            Test("completion folder refuses changed destination and redirected paths", delegate { using (var f = new Fixture()) {
                var entry = Completion(State(), f, true); var changed = HistoryIdentity(); changed.VolumeSerial = "wrong"; int opened = 0;
                Throws<SafetyException>(() => CompletionFolder.Open(entry, changed, new FakeGuard(), new Operations(), x => opened++));
                string moved = Path.Combine(f.Root, "moved-destination"); Directory.Move(f.Destination, moved); CreateJunction(f, f.Destination, moved);
                Throws<SafetyException>(() => CompletionFolder.Open(entry, HistoryIdentity(), new FakeGuard(), new Operations(), x => opened++)); Check(opened == 0, "Unsafe folder was opened");
            } });
            Test("completion dialogs default to keep connected for media and no-match outcomes", delegate { using (var f = new Fixture()) {
                foreach (bool media in new[] { true, false }) using (var dialog = new ImportCompletionDialog(Completion(State(), f, media), true)) {
                    Check(dialog.AcceptButton == dialog.Keep && dialog.CancelButton == dialog.Keep && dialog.Keep.DialogResult == System.Windows.Forms.DialogResult.Cancel && dialog.Eject.DialogResult == System.Windows.Forms.DialogResult.Yes, "Default/escape authorized removal");
                    if (!media) Check(dialog.Explanation.Text.Contains("Photos, skipped previews"), "No-match claimed empty device");
                    Check(dialog.Explanation.Text.Contains("all affected partitions") && dialog.Explanation.Text.Contains("Fixture camera"), "Affected storage explanation missing");
                    dialog.ExpireEject(); Check(!dialog.Eject.Enabled && dialog.Explanation.Text.Contains("connection changed"), "Changed connection kept dialog action enabled");
                }
            } });
            Test("notification route keeps completion choices manual with opt-in eject prompting", delegate {
                foreach (bool ask in new[] { true, false }) foreach (bool foreground in new[] { true, false }) foreach (bool notifications in new[] { true, false })
                    Check(CompletionPresentation.ShowChoice(ask, foreground, notifications) == (ask && (foreground || !notifications)), "Wrong foreground/background completion route");
                Check(CompletionPresentation.Notification(false).Contains("Photos or skipped files") && CompletionPresentation.Notification(true).Contains("Device remains connected") && !CompletionPresentation.Notification(true).Contains("volume-a"), "Notification contains authority or misleading outcome");
            });
            Test("ask-to-eject defaults start off and preserve custom legacy and frozen choices", delegate { using (var f = new Fixture()) {
                var state = State(); var defaults = new ImportDefaults(); var profile = defaults.NewProfile(state.Volumes[0], state.Disks[0]); Check(!profile.Resolve(defaults).AskToEject && profile.AskToEject == false, "Eject prompting not opt-in");
                defaults.AskToEject = true; var frozen = profile.Resolve(defaults); defaults.AskToEject = false; Check(frozen.AskToEject && !profile.Resolve(defaults).AskToEject, "In-flight completion option changed");
                profile.SetCustom(true, new ImportDefaults { AskToEject = true }); profile.AskToEject = false; Check(!profile.Resolve(new ImportDefaults { AskToEject = true }).AskToEject, "Custom choice inherited defaults");
                profile.AskToEject = null; profile.UsesImportDefaults = null; Check(!profile.Resolve(new ImportDefaults { AskToEject = true }).AskToEject, "Legacy profile enabled prompting");
                string root = Path.Combine(f.Root, "preferences"); ImportDefaults.Save(new ImportDefaults { AskToEject = true }, root); Check(ImportDefaults.Load(root).Snapshot().AskToEject, "Prompting choice did not persist");
            } });
            Test("app notification and debug settings persist without altering import profiles", delegate { using (var f = new Fixture()) {
                Check(AppSettings.Load(f.Root).NotificationsEnabled && !AppSettings.Load(f.Root).DebugEnabled, "Wrong factory app settings");
                var state = State(); var profile = new ImportDefaults().NewProfile(state.Volumes[0], state.Disks[0]); profile.Destination = f.Destination; Profiles.Save(profile, f.Root);
                AppSettings.Save(new AppSettings { ShowNotifications = false, DebugLogging = true }, f.Root); var restored = AppSettings.Load(f.Root);
                Check(!restored.NotificationsEnabled && restored.DebugEnabled && Profiles.Load(state.Volumes[0], state.Disks[0], f.Root).Destination == f.Destination, "App settings altered profile or failed roundtrip");
                File.WriteAllText(Path.Combine(f.Root, "app-settings.json"), "{}"); Check(AppSettings.Load(f.Root).NotificationsEnabled && !AppSettings.Load(f.Root).DebugEnabled, "Missing fields changed defaults");
                File.WriteAllText(Path.Combine(f.Root, "app-settings.json"), "null"); Throws<SafetyException>(() => AppSettings.Load(f.Root)); File.WriteAllText(Path.Combine(f.Root, "app-settings.json"), new string(' ', 17000)); Throws<SafetyException>(() => AppSettings.Load(f.Root));
                Throws<ArgumentNullException>(() => AppSettings.Save(null, f.Root));
            } });
            Test("issue dismissal persists without retry and later identical failure restores attention", delegate { using (var f = new Fixture()) {
                var issue = AppIssue.Load(f.Root); var fake = new FakeEject(); Check(!issue.HasIssue && !issue.NeedsAttention, "Factory issue present");
                issue.Record("Fixture veto"); AppIssue.Save(issue, f.Root); issue = AppIssue.Load(f.Root); Check(issue.NeedsAttention, "Failure attention not persisted");
                issue.Dismiss(); AppIssue.Save(issue, f.Root); issue = AppIssue.Load(f.Root); Check(issue.HasIssue && !issue.NeedsAttention && fake.Calls == 0, "Dismiss cleared context or retried eject");
                issue.Record("Fixture veto"); Check(issue.NeedsAttention && !issue.Dismissed && fake.Calls == 0, "Later failure did not restore attention"); issue.Clear(); AppIssue.Save(issue, f.Root); Check(!AppIssue.Load(f.Root).HasIssue, "Successful clear not retained");
                using (var dialog = new IssueDialog(issue)) Check(dialog.AcceptButton == dialog.CancelButton, "Issue dialog default changed state");
            } });
            Test("debug logs stay bounded and retain existing diagnostics on capacity refusal", delegate { using (var f = new Fixture()) {
                DebugLog.Append(f.Root, "one\nsecond line"); DebugLog.Append(f.Root, "two"); string text = DebugLog.Read(f.Root); Check(text.Contains("one second line") && text.Contains("two") && !text.Contains("\nsecond line"), "Debug events lost or multiline entries escaped boundaries");
                string path = Path.Combine(f.Root, "debug.log"); File.WriteAllText(path, new string('x', 1048576)); Throws<SafetyException>(() => DebugLog.Append(f.Root, "overflow")); Check(new FileInfo(path).Length == 1048576 && File.ReadAllText(path).All(x => x == 'x'), "Log limit deleted or replaced diagnostics");
            } });
            Test("settings editor uses a draft and saving never changes source files", delegate { using (var f = new Fixture()) {
                f.Put("clip.mov", "original"); var settings = new AppSettings();
                using (var dialog = new AppSettingsDialog(settings)) { dialog.Notifications.Checked = false; dialog.Debug.Checked = true; Check(settings.NotificationsEnabled && !settings.DebugEnabled && !dialog.Value.NotificationsEnabled && dialog.Value.DebugEnabled, "Unsaved editor mutated live settings"); AppSettings.Save(dialog.Value, f.Root); }
                Check(File.ReadAllText(Path.Combine(f.Source, "clip.mov")) == "original", "Saving preferences changed media");
            } });
            Test("closing the device window during work hides it while keeping the operation alive", delegate {
                using (var window = new DeviceWindow(true)) {
                    var field = typeof(DeviceWindow).GetField("working", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance); field.SetValue(window, true);
                    var argsClose = new System.Windows.Forms.FormClosingEventArgs(System.Windows.Forms.CloseReason.UserClosing, false);
                    typeof(System.Windows.Forms.Form).GetMethod("OnFormClosing", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance).Invoke(window, new object[] { argsClose });
                    Check(argsClose.Cancel && !window.Visible && !window.IsDisposed && (bool)field.GetValue(window), "Close terminated or cancelled background work");
                }
            });
            Test("completion origin refuses protected or unconfirmed source identities", delegate { using (var f = new Fixture()) {
                var state = State(); state.Volumes[0].Serial = ""; Throws<SafetyException>(() => Completion(state, f, true)); state.Volumes[0].Serial = "ABC";
                state.Volumes[0].System = true; Throws<SafetyException>(() => Completion(state, f, true)); state.Volumes[0].System = false;
                state.Disks[0].InstanceId = ""; Throws<SafetyException>(() => Completion(state, f, true)); state.Disks[0].InstanceId = "disk-a";
                state.Complete = false; Throws<SafetyException>(() => Completion(state, f, true));
            } });
            Test("completed camera eject confirms and reserves every sibling LUN", delegate { using (var f = new Fixture()) {
                var state = State(); state.Volumes[0].Label = "Internal camera storage";
                state.Disks.Add(new Disk { Number = 8, DevInst = 11, InstanceId = "disk-b", Serial = "serial-b", TargetId = "usb-a", TargetDevInst = 20, Ancestors = new List<string> { "disk-b", "usb-a" } });
                state.Volumes.Add(new Volume { Id = "volume-b", Label = "Camera SD Card", Serial = "DEF", Disks = new List<int> { 8 }, Mounts = new List<string> { @"F:\" } });
                var entry = Completion(state, f, true); var registry = new ImportCompletionRegistry(); registry.Insert(entry); var operations = new Operations();
                using (var dialog = new ImportCompletionDialog(entry, true)) Check(dialog.Eject.Text == "Eject Device" && dialog.Explanation.Text.Contains("Internal camera storage") && dialog.Explanation.Text.Contains("Camera SD Card"), "Grouped camera storage not fully described");
                var fake = new FakeEject { BeforeRequest = delegate { Throws<SafetyException>(() => operations.Reserve(new[] { 7 })); Throws<SafetyException>(() => operations.Reserve(new[] { 8 })); } };
                var service = new CompletedEjectService(registry, new FakeDiscovery { State = state }, fake, operations, x => "volume-a");
                using (operations.Reserve(new[] { 8 })) Throws<SafetyException>(() => service.Execute(entry.Id));
                Check(fake.Calls == 0 && registry.Get(entry.Id) != null, "Busy sibling was ignored"); Check(service.Execute(entry.Id).Success && fake.Calls == 1 && fake.LastId == "usb-a" && fake.LastNode == 20, "Wrong removal group or repeated API request");
            } });
            Test("corrupt or redirected issue storage fails without concealing a failure", delegate { using (var f = new Fixture()) {
                string path = Path.Combine(f.Root, "issue-state.json"); File.WriteAllText(path, "{\"Message\":\"Failure\",\"ObservedUtc\":\"invalid\"}"); Throws<SafetyException>(() => AppIssue.Load(f.Root));
                File.WriteAllText(path, "invalid"); Throws<System.Runtime.Serialization.SerializationException>(() => AppIssue.Load(f.Root));
                string root = Path.Combine(f.Root, "settings"), target = Path.Combine(f.Root, "redirect"); Directory.CreateDirectory(target); CreateJunction(f, root, target);
                var issue = new AppIssue(); issue.Record("Failure still needs attention"); Throws<SafetyException>(() => AppIssue.Save(issue, root)); Check(issue.NeedsAttention && Directory.GetFileSystemEntries(target).Length == 0, "Rejected storage concealed issue or wrote through a link");
            } });
            BulkTests();
            CleanupTests();
            RemovalTests();
            TrashCatalogTests();
            TrashRecoveryTests();
            RemovalIntentTests();
            RemovalResumeTests();
            RemovalResumeActionTests();
            SavedProfileTests();
            AutomaticImportTests();
            StartupTests();
            QuickShareRecipeTests();
            QuickShareProcessTests();
            BulkFinalTests(); PathOwnershipTests(); WindowsReleaseReviewTests(); WindowsUpdateCoordinatorTests(); WindowsChannelReplayTests(); WindowsChannelDiscoveryTests(); WindowsChannelTests(); WindowsArchiveTests(); WindowsManagedIdentityTests(); WindowsPackageTests(); WindowsManifestTrustTests(); WindowsAuthenticodeTests(); ManualInstallationTests(); ConfigurationWriterTests(); DirectoryLeaseTests(); QuickShareEngineTests();
            QuickShareDialogTests();
            QuickShareConfigurationTests();
            ManualQuickShareTests();
            TrayStorageMenuTests();
            StatusLayoutTests();
            InformationDiagnosticsTests();
            TrayActivityTests();
            MediaParityTests();
            MediaMatrixTests();
            StorageConnectionTests();
            CompletionConnectionTests();
            ConcurrentStorageTests();
            AutomaticAdmissionTests();
            Console.WriteLine(passed + " passed; " + failed + " failed. No real device eject API was used."); return failed == 0 ? 0 : 1;
        }
        static void Render(System.Windows.Forms.Form form, string path) {
            IntPtr handle = form.Handle; CreateRenderHandles(form); form.PerformLayout();
            Console.WriteLine(Path.GetFileName(path) + " size=" + form.Size + " minimum=" + form.MinimumSize);
            Check(handle != IntPtr.Zero && !form.Visible, "Rendering must use a hidden fixture window");
            using (var bitmap = new System.Drawing.Bitmap(form.Width, form.Height)) { form.DrawToBitmap(bitmap, new System.Drawing.Rectangle(0, 0, bitmap.Width, bitmap.Height)); bitmap.Save(path, System.Drawing.Imaging.ImageFormat.Png); }
        }
        static void RenderAtBottom(System.Windows.Forms.Form form, string path) {
            IntPtr handle = form.Handle; CreateRenderHandles(form); form.PerformLayout();
            var body = form.Controls.OfType<System.Windows.Forms.Panel>().First(x => x.AutoScroll);
            int offset = Math.Max(0, body.Controls.Cast<System.Windows.Forms.Control>().Max(x => x.Bottom) + body.Padding.Bottom - body.ClientSize.Height);
            // Hidden WinForms don't apply native scroll positions. Composite the lower region of the owned
            // fixture's body render for inspection; this is not proof of foreground scrolling/interaction.
            Console.WriteLine(Path.GetFileName(path) + " bottom-region offset=" + offset + " viewport=" + body.ClientSize);
            Check(handle != IntPtr.Zero && !form.Visible, "Bottom rendering must use hidden fixture windows");
            using (var bitmap = new System.Drawing.Bitmap(form.Width, form.Height)) {
                form.DrawToBitmap(bitmap, new System.Drawing.Rectangle(0, 0, bitmap.Width, bitmap.Height));
                int border = (form.Width - form.ClientSize.Width) / 2, left = border + body.Left, top = form.Height - form.ClientSize.Height - border + body.Top;
                // A separate viewport bitmap bounds native drawing physically; negative child origins
                // cannot overwrite the form's caption/footer even when a GDI paint ignores clipping.
                using (var frame = (System.Drawing.Bitmap)bitmap.Clone()) using (var viewport = new System.Drawing.Bitmap(body.ClientSize.Width, body.ClientSize.Height)) using (var graphics = System.Drawing.Graphics.FromImage(viewport)) {
                    graphics.Clear(body.BackColor);
                    foreach (System.Windows.Forms.Control child in body.Controls) using (var content = new System.Drawing.Bitmap(child.Width, child.Height)) {
                        child.DrawToBitmap(content, new System.Drawing.Rectangle(0, 0, content.Width, content.Height));
                        graphics.DrawImageUnscaled(content, child.Left, child.Top - offset);
                    }
                    // Some native children paint outside the bitmap requested by DrawToBitmap.
                    // Restore the captured frame before adding only the bounded body region.
                    using (var target = System.Drawing.Graphics.FromImage(bitmap)) {
                        target.DrawImageUnscaled(frame, 0, 0);
                        target.DrawImage(viewport, new System.Drawing.Rectangle(left, top, viewport.Width, viewport.Height), 0, 0, viewport.Width, viewport.Height, System.Drawing.GraphicsUnit.Pixel);
                    }
                }
                bitmap.Save(path, System.Drawing.Imaging.ImageFormat.Png);
            }
        }
        static void CreateRenderHandles(System.Windows.Forms.Control control) {
            IntPtr handle = control.Handle; control.PerformLayout();
            if (control is System.Windows.Forms.TextBox) Console.WriteLine("Text field bounds=" + control.Bounds + " characters=" + control.Text.Length);
            foreach (System.Windows.Forms.Control child in control.Controls) CreateRenderHandles(child);
        }
    }
}
