using System;
using System.IO;
using System.Linq;
using System.Threading;

namespace EasyEject {
    public static partial class Tests {
        static string ReplayBackupPath(ReplayFixture f, WindowsChannelOffer offer = null) {
            string folder = Path.Combine(f.Channel.Archive.Package.Files.Root, "replay-backup"); Directory.CreateDirectory(folder);
            string path = Path.Combine(folder, WindowsChannelReplayStore.FileName);
            if (offer == null) File.Copy(f.PathValue, path);
            else File.WriteAllBytes(path, ReplayEnvelope(ReplaySerialize(WindowsChannelReplayFloor.From(f.Channel.Channel, offer), typeof(WindowsChannelReplayFloor))));
            return path;
        }
        static void WindowsReplayBackupTests() {
            Test("replay backup review changes no settings and equal restore preserves protected bytes", () => {
                using (var f = new ReplayFixture()) { f.Accept(f.Store()); string path = ReplayBackupPath(f), hash = ReplayHash(f); byte[] backup = File.ReadAllBytes(path); int protects = f.Protection.Protects;
                    var store = f.Store(); using (var review = store.ReviewBackup(f.Channel.Channel, path, CancellationToken.None)) {
                        Check(ReplayHash(f) == hash && review.Description.Contains("no later history was lost") && !store.RestoreBackup(review, CancellationToken.None), "Review changed state or equal floor rewrote");
                        Throws<SafetyException>(() => store.RestoreBackup(review, CancellationToken.None));
                    } Check(ReplayHash(f) == hash && File.ReadAllBytes(path).SequenceEqual(backup) && protects == f.Protection.Protects, "Equal recovery changed backup/protection/state"); }
            });
            Test("replay backup advances exact compatible history without touching other settings", () => {
                using (var f = new ReplayFixture()) { f.Accept(f.Store(), f.Offer(issued: f.Channel.Seconds - 50)); string path = ReplayBackupPath(f, f.Offer("0.3.0.0", f.Channel.Seconds - 20)); byte[] backup = File.ReadAllBytes(path);
                    string other = Path.Combine(f.Root, "unrelated.keep"); File.WriteAllText(other, "retain unrelated settings"); var store = f.Store();
                    using (var review = store.ReviewBackup(f.Channel.Channel, path, CancellationToken.None)) Check(store.RestoreBackup(review, CancellationToken.None), "Compatible backup was not published");
                    Check(ReplaySavedFloor(f).Version == "0.3.0.0" && File.ReadAllBytes(path).SequenceEqual(backup) && File.ReadAllText(other) == "retain unrelated settings" && Directory.GetFiles(f.Root).Length == 2, "Recovery changed unrelated/backup files or leaked work");
                    Throws<SafetyException>(() => f.Accept(f.Store(), f.Offer("0.2.0.0", f.Channel.Seconds - 50))); }
            });
            Test("explicit backup recovery restores observed missing state only at its retained live floor", () => {
                using (var f = new ReplayFixture()) { var store = f.Store(); var accepted = f.Offer("0.3.0.0", f.Channel.Seconds - 20); f.Accept(store, accepted); string path = ReplayBackupPath(f); File.Delete(f.PathValue);
                    Throws<SafetyException>(() => f.Accept(store, accepted)); using (var review = store.ReviewBackup(f.Channel.Channel, path, CancellationToken.None)) Check(store.RestoreBackup(review, CancellationToken.None), "Deliberate exact recovery refused");
                    f.Accept(store, accepted); Check(ReplaySavedFloor(f).Version == "0.3.0.0", "Recovered floor lost identity"); }
            });
            Test("missing-state recovery refuses an older backup than the live observed restriction", () => {
                using (var f = new ReplayFixture()) { var store = f.Store(); f.Accept(store, f.Offer(issued: f.Channel.Seconds - 50)); string path = ReplayBackupPath(f); byte[] backup = File.ReadAllBytes(path); f.Accept(store, f.Offer("0.3.0.0", f.Channel.Seconds - 20)); File.Delete(f.PathValue);
                    Throws<SafetyException>(() => store.ReviewBackup(f.Channel.Channel, path, CancellationToken.None)); Check(!File.Exists(f.PathValue) && File.ReadAllBytes(path).SequenceEqual(backup), "Old backup silently recreated observed state"); }
            });
            Test("backup recovery refuses rollback and equivocation against readable target", () => {
                foreach (int variant in new[] { 0, 1, 2 }) using (var f = new ReplayFixture()) { f.Accept(f.Store(), f.Offer("0.3.0.0", f.Channel.Seconds - 20)); string hash = ReplayHash(f);
                    var backup = variant == 0 ? f.Offer("0.2.0.0", f.Channel.Seconds - 50) : variant == 1 ? f.Offer("0.2.0.0", f.Channel.Seconds - 10) : f.Channel.Offer(f.Channel.Text("0.3.0.0", f.Channel.Seconds - 20, f.Channel.Seconds + 4000));
                    string path = ReplayBackupPath(f, backup); byte[] bytes = File.ReadAllBytes(path); Throws<SafetyException>(() => f.Store().ReviewBackup(f.Channel.Channel, path, CancellationToken.None)); Check(ReplayHash(f) == hash && File.ReadAllBytes(path).SequenceEqual(bytes), "Conflicting history replaced state"); }
            });
            Test("corrupt current history is retained rather than reset from a valid backup", () => {
                using (var f = new ReplayFixture()) { f.Accept(f.Store()); string path = ReplayBackupPath(f); f.Write(new byte[] { 0, 1, 2 }); string hash = ReplayHash(f); byte[] backup = File.ReadAllBytes(path);
                    Throws<SafetyException>(() => f.Store().ReviewBackup(f.Channel.Channel, path, CancellationToken.None)); Check(ReplayHash(f) == hash && File.ReadAllBytes(path).SequenceEqual(backup), "Unknown target history was discarded"); }
            });
            Test("backup review refuses missing policy same target and nonrecord selections", () => {
                using (var f = new ReplayFixture()) { f.Accept(f.Store()); string hash = ReplayHash(f), other = Path.Combine(f.Root, "other.json"); File.WriteAllText(other, "retained other record"); var store = f.Store();
                    Throws<SafetyException>(() => store.ReviewBackup(null, f.PathValue, CancellationToken.None)); Throws<SafetyException>(() => store.ReviewBackup(f.Channel.Channel, f.PathValue, CancellationToken.None)); Throws<SafetyException>(() => store.ReviewBackup(f.Channel.Channel, other, CancellationToken.None)); Check(ReplayHash(f) == hash && File.ReadAllText(other) == "retained other record", "Invalid selection changed config"); }
            });
            Test("replay backup is held against replacement and review ownership is one use", () => {
                using (var f = new ReplayFixture()) { f.Accept(f.Store()); string path = ReplayBackupPath(f), hash = ReplayHash(f); var store = f.Store(); var review = store.ReviewBackup(f.Channel.Channel, path, CancellationToken.None);
                    using (review) { Throws<IOException>(() => File.Delete(path)); Throws<IOException>(() => File.WriteAllText(path, "replacement")); Throws<SafetyException>(() => f.Store().RestoreBackup(review, CancellationToken.None)); Check(!store.RestoreBackup(review, CancellationToken.None), "Wrong-owner attempt consumed legitimate review"); }
                    Throws<SafetyException>(() => store.RestoreBackup(review, CancellationToken.None)); Check(ReplayHash(f) == hash, "Closed review changed state"); }
            });
            Test("replay restore refuses same-byte current-file replacement after review", () => {
                using (var f = new ReplayFixture()) { f.Accept(f.Store()); string path = ReplayBackupPath(f, f.Offer("0.3.0.0", f.Channel.Seconds - 20)); var store = f.Store(); using (var review = store.ReviewBackup(f.Channel.Channel, path, CancellationToken.None)) {
                        byte[] old = File.ReadAllBytes(f.PathValue); DateTime modified = File.GetLastWriteTimeUtc(f.PathValue); File.Move(f.PathValue, f.PathValue + ".moved"); File.WriteAllBytes(f.PathValue, old); File.SetLastWriteTimeUtc(f.PathValue, modified);
                        Throws<SafetyException>(() => store.RestoreBackup(review, CancellationToken.None)); Check(File.ReadAllBytes(f.PathValue).SequenceEqual(old) && File.ReadAllBytes(f.PathValue + ".moved").SequenceEqual(old), "Stale review replaced same-byte foreign identity"); }
                }
            });
            Test("replay restore refuses a target appearing after absent review", () => {
                using (var f = new ReplayFixture()) { f.Accept(f.Store()); string path = ReplayBackupPath(f); byte[] backup = File.ReadAllBytes(path); File.Delete(f.PathValue); var store = f.Store();
                    using (var review = store.ReviewBackup(f.Channel.Channel, path, CancellationToken.None)) { File.WriteAllBytes(f.PathValue, backup); Throws<SafetyException>(() => store.RestoreBackup(review, CancellationToken.None)); Check(File.ReadAllBytes(f.PathValue).SequenceEqual(backup), "Appearing target was overwritten"); } }
            });
            Test("replay restore refuses a late backup alias while preserving both names", () => {
                using (var f = new ReplayFixture()) { f.Accept(f.Store()); string path = ReplayBackupPath(f, f.Offer("0.3.0.0", f.Channel.Seconds - 20)), hash = ReplayHash(f), alias = path + ".alias"; var store = f.Store();
                    using (var review = store.ReviewBackup(f.Channel.Channel, path, CancellationToken.None)) { Check(CreateHardLink(alias, path, IntPtr.Zero), "Could not create owned backup alias"); Throws<SafetyException>(() => store.RestoreBackup(review, CancellationToken.None)); Check(ReplayHash(f) == hash && File.ReadAllBytes(alias).SequenceEqual(File.ReadAllBytes(path)), "Aliased backup changed settings or was cleaned"); } }
            });
            Test("backup review rejects target directory replacement and releases its backup handle", () => {
                using (var f = new ReplayFixture()) { f.Accept(f.Store()); string path = ReplayBackupPath(f); File.Delete(f.PathValue); bool changed = false;
                    var store = f.Store((stage, _) => { if (stage == "After backup review") { ReplaceFixtureDirectory(f.Channel.Archive.Package.Files, f.Root); changed = true; } });
                    Throws<SafetyException>(() => store.ReviewBackup(f.Channel.Channel, path, CancellationToken.None)); Check(changed && File.ReadAllText(Path.Combine(f.Root, "foreign.keep")) == "retain replacement", "Directory replacement ignored/changed");
                    using (var held = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.None)) Check(held.Length > 0, "Failed review retained backup handle"); }
            });
            Test("backup native authentication cancellation or scope loss cannot change state", () => {
                foreach (bool cancel in new[] { true, false }) using (var f = new ReplayFixture()) using (var stop = new CancellationTokenSource()) { f.Accept(f.Store()); string path = ReplayBackupPath(f), hash = ReplayHash(f); bool allowed = true; f.Protection.BeforeUnprotect = () => { if (cancel) stop.Cancel(); else allowed = false; };
                    if (cancel) Throws<OperationCanceledException>(() => f.Store().ReviewBackup(f.Channel.Channel, path, stop.Token, () => allowed));
                    else Throws<SafetyException>(() => f.Store().ReviewBackup(f.Channel.Channel, path, stop.Token, () => allowed));
                    Check(ReplayHash(f) == hash && Directory.GetFiles(f.Root).Length == 1, "Lost review scope wrote state"); }
            });
            Test("final backup review rechecks held alias state before returning evidence", () => {
                using (var f = new ReplayFixture()) { f.Accept(f.Store()); string path = ReplayBackupPath(f), hash = ReplayHash(f), alias = path + ".alias"; bool reached = false;
                    var store = f.Store((stage, _) => { if (stage == "After backup review") { Check(CreateHardLink(alias, path, IntPtr.Zero), "Could not create late owned backup alias"); reached = true; } });
                    Throws<SafetyException>(() => store.ReviewBackup(f.Channel.Channel, path, CancellationToken.None)); Check(reached && ReplayHash(f) == hash && File.ReadAllBytes(path).SequenceEqual(File.ReadAllBytes(alias)), "Final review accepted alias or changed evidence"); }
            });
            Test("restore revoked scope at publication retains target and consumes old confirmation", () => {
                using (var f = new ReplayFixture()) { f.Accept(f.Store()); string path = ReplayBackupPath(f, f.Offer("0.3.0.0", f.Channel.Seconds - 20)), hash = ReplayHash(f); bool allowed = true;
                    var store = f.Store((stage, _) => { if (stage == "Before publication") allowed = false; });
                    using (var review = store.ReviewBackup(f.Channel.Channel, path, CancellationToken.None)) { Throws<SafetyException>(() => store.RestoreBackup(review, CancellationToken.None, () => allowed)); allowed = true; Throws<SafetyException>(() => store.RestoreBackup(review, CancellationToken.None)); }
                    Check(ReplayHash(f) == hash && Directory.GetFiles(f.Root).Length == 1, "Revoked restore wrote state or leaked private work"); }
            });
            Test("replay restore cancellation at publication preserves target and cleans owned private work", () => {
                using (var f = new ReplayFixture()) using (var stop = new CancellationTokenSource()) { f.Accept(f.Store()); string path = ReplayBackupPath(f, f.Offer("0.3.0.0", f.Channel.Seconds - 20)), hash = ReplayHash(f); var store = f.Store((stage, _) => { if (stage == "Before publication") stop.Cancel(); });
                    using (var review = store.ReviewBackup(f.Channel.Channel, path, CancellationToken.None)) { Throws<OperationCanceledException>(() => store.RestoreBackup(review, stop.Token)); Throws<SafetyException>(() => store.RestoreBackup(review, CancellationToken.None)); }
                    Check(ReplayHash(f) == hash && Directory.GetFiles(f.Root).Length == 1, "Cancelled restore changed target or leaked temporary"); }
            });
            Test("postpublication restore failure retains live restriction even after external deletion", () => {
                using (var f = new ReplayFixture()) { f.Accept(f.Store(), f.Offer(issued: f.Channel.Seconds - 50)); string path = ReplayBackupPath(f, f.Offer("0.3.0.0", f.Channel.Seconds - 20)); var store = f.Store((stage, _) => { if (stage == "After publication") throw new IOException("Injected final restore failure"); });
                    using (var review = store.ReviewBackup(f.Channel.Channel, path, CancellationToken.None)) Throws<SafetyException>(() => store.RestoreBackup(review, CancellationToken.None));
                    Check(ReplaySavedFloor(f).Version == "0.3.0.0", "Published restriction lost"); File.Delete(f.PathValue); Throws<SafetyException>(() => f.Accept(store, f.Offer(issued: f.Channel.Seconds - 50))); Check(!File.Exists(f.PathValue), "Postpublication failure forgot live history"); }
            });
            Test("native CurrentUser backup restores without plaintext fallback and refuses older offers", () => {
                using (var f = new ReplayFixture()) { var offer = f.Offer("0.3.0.0", f.Channel.Seconds - 20); f.Accept(new WindowsChannelReplayStore(f.Root), offer); string path = ReplayBackupPath(f); byte[] protectedBytes = File.ReadAllBytes(path); File.Delete(f.PathValue); var store = new WindowsChannelReplayStore(f.Root);
                    using (var review = store.ReviewBackup(f.Channel.Channel, path, CancellationToken.None)) Check(store.RestoreBackup(review, CancellationToken.None), "Native backup restore refused");
                    Check(File.ReadAllBytes(f.PathValue).SequenceEqual(protectedBytes) && !System.Text.Encoding.UTF8.GetString(protectedBytes).Contains("0.3.0.0"), "Native backup was changed or unprotected"); f.Accept(new WindowsChannelReplayStore(f.Root), offer);
                    Throws<SafetyException>(() => f.Accept(new WindowsChannelReplayStore(f.Root), f.Offer(issued: f.Channel.Seconds - 50))); }
            });
            Test("native replay backup tamper and foreign policy are retained and refused", () => {
                foreach (bool tamper in new[] { true, false }) using (var f = new ReplayFixture()) { f.Accept(new WindowsChannelReplayStore(f.Root)); string path = ReplayBackupPath(f); var channel = f.Channel.Channel;
                    if (tamper) { byte[] bytes = ReplayProtectedBytes(File.ReadAllBytes(path)); bytes[bytes.Length / 2] ^= 1; File.WriteAllBytes(path, ReplayEnvelope(bytes)); }
                    else channel = new ApprovedWindowsChannel("https://other.invalid/channel.json", "https://other.invalid/channel.json.p7s", channel.Archives.AbsoluteUri, channel.Publisher);
                    string hash = ReplayHash(f); byte[] backup = File.ReadAllBytes(path); Throws<SafetyException>(() => new WindowsChannelReplayStore(f.Root).ReviewBackup(channel, path, CancellationToken.None)); Check(ReplayHash(f) == hash && File.ReadAllBytes(path).SequenceEqual(backup), "Foreign/tampered backup reset state"); }
            });
        }
    }
}
