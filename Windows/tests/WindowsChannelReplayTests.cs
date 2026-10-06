using System;
using System.IO;
using System.Linq;
using System.Runtime.Serialization.Json;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace EasyEject {
    public static partial class Tests {
        sealed class ReplayProtectionFixture : IWindowsReplayProtection {
            internal int Protects, Unprotects; internal Action BeforeProtect, BeforeUnprotect;
            internal Func<byte[], byte[]> ProtectResult, UnprotectResult;
            public byte[] Protect(byte[] bytes, byte[] entropy) { Protects++; Check(entropy.Length > 64 && Encoding.ASCII.GetString(entropy).StartsWith("EasyEject.Windows/channel-replay/protection/v1:"), "Missing policy-bound domain separation"); if (BeforeProtect != null) BeforeProtect(); return ProtectResult == null ? (byte[])bytes.Clone() : ProtectResult(bytes); }
            public byte[] Unprotect(byte[] bytes, byte[] entropy) { Unprotects++; if (BeforeUnprotect != null) BeforeUnprotect(); return UnprotectResult == null ? (byte[])bytes.Clone() : UnprotectResult(bytes); }
        }
        sealed class ReplayFixture : IDisposable {
            internal readonly WindowsChannelFixture Channel = new WindowsChannelFixture();
            internal readonly ReplayProtectionFixture Protection = new ReplayProtectionFixture();
            internal string Root { get { return Path.Combine(Channel.Archive.Package.Files.Root, "replay"); } }
            internal string PathValue { get { return Path.Combine(Root, WindowsChannelReplayStore.FileName); } }
            internal WindowsChannelReplayStore Store(Action<string, string> checkpoint = null) { return new WindowsChannelReplayStore(Root, Protection, checkpoint); }
            internal WindowsChannelOffer Offer(string version = "0.1.0.0", long? issued = null) { return Channel.Offer(Channel.Text(version, issued)); }
            internal void Accept(WindowsChannelReplayStore store, WindowsChannelOffer offer = null, CancellationToken cancel = default(CancellationToken), Func<bool> allowed = null) { store.Accept(Channel.Channel, offer ?? Offer(), cancel, allowed); }
            internal void Write(byte[] bytes) { Directory.CreateDirectory(Root); File.WriteAllBytes(PathValue, bytes); }
            public void Dispose() { Channel.Dispose(); }
        }
        static byte[] ReplaySerialize(object value, Type type) { using (var output = new MemoryStream()) { new DataContractJsonSerializer(type).WriteObject(output, value); return output.ToArray(); } }
        static byte[] ReplayEnvelope(byte[] protectedBytes) { return ReplaySerialize(new WindowsChannelReplayRecord(protectedBytes), typeof(WindowsChannelReplayRecord)); }
        static byte[] ReplayProtectedBytes(byte[] record) { var fields = WindowsReleaseManifest.Fields(WindowsReleaseManifest.ReadJson(record), new[] { "format", "protectedFloor" }); return Convert.FromBase64String(WindowsReleaseManifest.Text(fields["protectedFloor"])); }
        static string ReplayHash(ReplayFixture f) { return WindowsSignerEvidence.Hash(File.ReadAllBytes(f.PathValue)); }
        static WindowsChannelReplayFloor ReplaySavedFloor(ReplayFixture f) { return WindowsChannelReplayFloor.Parse(ReplayProtectedBytes(File.ReadAllBytes(f.PathValue)), WindowsChannelReplayFloor.Policy(f.Channel.Channel)); }
        static string ReplayFixtureStream(ReplayFixture f, string name, string text = null) {
            Check(SafePaths.Within(f.PathValue, f.Channel.Archive.Package.Files.Root) && (name == "Zone.Identifier" || name == "foreign"), "Unexpected disposable replay stream path");
            using (var handle = Native.CreateFile(f.PathValue + ":" + name, text == null ? 0x80000000U : 0x40000000U, 1, IntPtr.Zero, text == null ? 3U : 2U, 0x00200000, IntPtr.Zero)) {
                if (handle.IsInvalid) throw Native.Error("Cannot access owned replay stream fixture"); using (var stream = new FileStream(handle, text == null ? FileAccess.Read : FileAccess.Write)) { if (text != null) { byte[] bytes = Encoding.UTF8.GetBytes(text); stream.Write(bytes, 0, bytes.Length); stream.Flush(true); return text; } using (var reader = new StreamReader(stream, Encoding.UTF8)) return reader.ReadToEnd(); }
            }
        }
        static void WindowsChannelReplayTests() {
            Test("Windows replay floor survives a new store and blocks rollback without a cached offer", () => {
                using (var f = new ReplayFixture()) { var offer = f.Offer("0.2.0.0", f.Channel.Seconds - 20); f.Accept(f.Store(), offer); string hash = ReplayHash(f);
                    Check(ReplaySavedFloor(f).Same(WindowsChannelReplayFloor.From(f.Channel.Channel, offer)), "Saved identity differs from accepted offer");
                    var restarted = f.Store(); Throws<SafetyException>(() => f.Accept(restarted, f.Offer("0.1.0.0", f.Channel.Seconds - 50))); Check(ReplayHash(f) == hash, "Restart rollback changed state");
                }
            });
            Test("Windows replay equivalent checks preserve bytes without protection or publication", () => {
                using (var f = new ReplayFixture()) { var offer = f.Offer(); f.Accept(f.Store(), offer); string hash = ReplayHash(f); int protects = f.Protection.Protects, publications = 0;
                    f.Accept(f.Store((stage, path) => { if (stage == "Before publication") publications++; }), offer); Check(ReplayHash(f) == hash && protects == f.Protection.Protects && publications == 0, "Equivalent check rewrote saved state"); }
            });
            Test("Windows replay renewal permits a later issue of the same immutable release", () => {
                using (var f = new ReplayFixture()) { f.Accept(f.Store(), f.Offer(issued: f.Channel.Seconds - 50)); var renewal = f.Offer(issued: f.Channel.Seconds - 20); f.Accept(f.Store(), renewal); Check(ReplaySavedFloor(f).IssuedUtc == renewal.IssuedUtc, "Renewed issue was not saved"); }
            });
            Test("Windows replay rejects earlier issue, lower version and same-issue body equivocation", () => {
                using (var f = new ReplayFixture()) { f.Accept(f.Store(), f.Offer("0.2.0.0", f.Channel.Seconds - 20)); string hash = ReplayHash(f);
                    foreach (string text in new[] { f.Channel.Text("0.3.0.0", f.Channel.Seconds - 50), f.Channel.Text("0.1.0.0", f.Channel.Seconds - 10), f.Channel.Text("0.2.0.0", f.Channel.Seconds - 20, f.Channel.Seconds + 4000) }) Throws<SafetyException>(() => f.Accept(f.Store(), f.Channel.Offer(text))); Check(ReplayHash(f) == hash, "Refused replay replaced accepted state"); }
            });
            Test("Windows replay preserves every immutable release hash/source/length field across renewal", () => {
                using (var f = new ReplayFixture()) { var offer = f.Offer(issued: f.Channel.Seconds - 50); f.Accept(f.Store(), offer); string hash = ReplayHash(f); var r = offer.Release;
                    foreach (string[] change in new[] { new[] { "\"sourceRevision\":\"" + r.SourceRevision, "\"sourceRevision\":\"" + new string('a', 40) }, new[] { "\"manifestSha256\":\"" + r.ManifestHash, "\"manifestSha256\":\"" + new string('b', 64) }, new[] { "\"manifestSignatureSha256\":\"" + r.ManifestSignatureHash, "\"manifestSignatureSha256\":\"" + new string('b', 64) }, new[] { "\"certificateSha256\":\"" + r.CertificateHash, "\"certificateSha256\":\"" + new string('b', 64) }, new[] { "\"sha256\":\"" + r.ArchiveHash, "\"sha256\":\"" + new string('b', 64) }, new[] { "\"length\":" + r.ArchiveLength, "\"length\":" + (r.ArchiveLength + 1) } }) {
                        string text = f.Channel.Text(issued: f.Channel.Seconds - 20), changed = text.Replace(change[0], change[1]); Check(text != changed, "Release mutation fixture did not change field"); Throws<SafetyException>(() => f.Accept(f.Store(), f.Channel.Offer(changed)));
                    } Check(ReplayHash(f) == hash, "Immutable version substitution changed floor"); }
            });
            Test("Windows replay policy binds both endpoints archive origin and both publisher pins", () => {
                using (var f = new ReplayFixture()) { var c = f.Channel.Channel; string expected = WindowsChannelReplayFloor.Policy(c);
                    foreach (var different in new[] { new ApprovedWindowsChannel("https://other.invalid/channel.json", "https://other.invalid/channel.json.p7s", c.Archives.AbsoluteUri, c.Publisher), new ApprovedWindowsChannel(c.Metadata.AbsoluteUri, c.Signature.AbsoluteUri, "https://other.invalid/releases/", c.Publisher), new ApprovedWindowsChannel(c.Metadata.AbsoluteUri, c.Signature.AbsoluteUri, c.Archives.AbsoluteUri, new ApprovedWindowsPublisher(new string('a', 64), c.Publisher.IssuerCertificateHash)), new ApprovedWindowsChannel(c.Metadata.AbsoluteUri, c.Signature.AbsoluteUri, c.Archives.AbsoluteUri, new ApprovedWindowsPublisher(c.Publisher.SubjectHash, new string('a', 64))) }) Check(WindowsChannelReplayFloor.Policy(different) != expected, "Policy digest omitted an approved boundary");
                    f.Accept(f.Store()); string hash = ReplayHash(f); var other = new ApprovedWindowsChannel("https://other.invalid/channel.json", "https://other.invalid/channel.json.p7s", c.Archives.AbsoluteUri, c.Publisher); var offer = f.Channel.Offer(channel: other);
                    Throws<SafetyException>(() => f.Store().Accept(other, offer, CancellationToken.None, null)); Check(ReplayHash(f) == hash, "Other policy migrated or replaced saved state"); }
            });
            Test("Windows replay old freshness does not expire the restriction while new offers need fresh time", () => {
                using (var f = new ReplayFixture()) { var old = f.Offer("0.2.0.0", f.Channel.Seconds - 20); f.Accept(f.Store(), old); f.Channel.Now = f.Channel.Now.AddDays(8);
                    Throws<SafetyException>(() => f.Accept(f.Store(), old)); Throws<SafetyException>(() => f.Accept(f.Store(), f.Offer("0.1.0.0"))); var fresh = f.Offer("0.2.0.0", f.Channel.Seconds - 20); f.Accept(f.Store(), fresh); Check(ReplaySavedFloor(f).IssuedUtc == fresh.IssuedUtc, "Expired historic metadata discarded its restriction or blocked renewal"); }
            });
            Test("Windows replay native CurrentUser protection roundtrips across restart and detects tampering", () => {
                using (var f = new ReplayFixture()) { var native = new WindowsChannelReplayStore(f.Root); var accepted = f.Offer("0.2.0.0", f.Channel.Seconds - 20); f.Accept(native, accepted); byte[] original = File.ReadAllBytes(f.PathValue), encrypted = ReplayProtectedBytes(original);
                    Check(!Encoding.UTF8.GetString(original).Contains("0.2.0.0"), "Native replay was saved in plaintext"); f.Accept(new WindowsChannelReplayStore(f.Root), accepted); Check(File.ReadAllBytes(f.PathValue).SequenceEqual(original), "Native equivalent state rewrote");
                    Throws<SafetyException>(() => f.Accept(new WindowsChannelReplayStore(f.Root), f.Offer("0.1.0.0", f.Channel.Seconds - 50)));
                    encrypted[encrypted.Length / 2] ^= 1; f.Write(ReplayEnvelope(encrypted)); string hash = ReplayHash(f); Check(Throws<SafetyException>(() => f.Accept(new WindowsChannelReplayStore(f.Root), accepted)).Message.Contains("could not be authenticated") && ReplayHash(f) == hash, "Native protected tamper reset/fell back or changed state"); }
            });
            Test("Windows replay native protected state cannot move to another approved policy", () => {
                using (var f = new ReplayFixture()) { f.Accept(new WindowsChannelReplayStore(f.Root)); string hash = ReplayHash(f); var c = f.Channel.Channel; var other = new ApprovedWindowsChannel("https://other.invalid/channel.json", "https://other.invalid/channel.json.p7s", c.Archives.AbsoluteUri, c.Publisher);
                    var offer = f.Channel.Offer(channel: other); Check(Throws<SafetyException>(() => new WindowsChannelReplayStore(f.Root).Accept(other, offer, CancellationToken.None, null)).Message.Contains("could not be authenticated") && ReplayHash(f) == hash, "Different policy entropy accepted native state"); }
            });
            Test("Windows replay pre-cancel scope and policy failures create no storage or protection work", () => {
                using (var f = new ReplayFixture()) using (var cancel = new CancellationTokenSource()) { var store = f.Store(); var offer = f.Offer(); cancel.Cancel(); Throws<OperationCanceledException>(() => f.Accept(store, offer, cancel.Token)); Throws<SafetyException>(() => f.Accept(store, offer, allowed: () => false));
                    var c = f.Channel.Channel; var other = new ApprovedWindowsChannel("https://other.invalid/channel.json", "https://other.invalid/channel.json.p7s", c.Archives.AbsoluteUri, c.Publisher); Throws<SafetyException>(() => store.Accept(other, offer, CancellationToken.None, null)); Check(!Directory.Exists(f.Root) && f.Protection.Protects == 0 && f.Protection.Unprotects == 0, "Denied admission created state"); }
            });
            Test("Windows replay fresh native trust refusal/cancellation does not create state", () => {
                using (var f = new ReplayFixture()) using (var cancel = new CancellationTokenSource()) { var offer = f.Offer(); f.Channel.Archive.Package.CertificateTrust.Throw = true; Throws<SafetyException>(() => f.Accept(f.Store(), offer)); f.Channel.Archive.Package.CertificateTrust.Throw = false; f.Channel.Archive.Package.CertificateTrust.During = cancel.Cancel; Throws<OperationCanceledException>(() => f.Accept(f.Store(), offer, cancel.Token)); Check(!Directory.Exists(f.Root) && f.Protection.Protects == 0, "Fresh failed trust persisted a floor"); }
            });
            Test("Windows replay malformed envelopes fail closed without reset or rewriting", () => {
                using (var f = new ReplayFixture()) { var offer = f.Offer(); var floor = WindowsChannelReplayFloor.From(f.Channel.Channel, offer); string good = Encoding.UTF8.GetString(ReplayEnvelope(ReplaySerialize(floor, typeof(WindowsChannelReplayFloor))));
                    foreach (string bad in new[] { good.Replace("\"format\":1", "\"format\":2"), good.Replace("\"format\":1", "\"format\":\"1\""), good.Replace("\"format\":1", "\"format\":1,\"format\":1"), "{\"format\":1}", good.Insert(1, "\"foreign\":true,"), good + "{}", "{\"format\":1,\"protectedFloor\":null}", "{\"format\":1,\"protectedFloor\":\"@@\"}", "{\"format\":1,\"protectedFloor\":\" YQ==\"}", "{\"format\":1,\"protectedFloor\":\"\"}" }) { f.Write(Encoding.UTF8.GetBytes(bad)); string hash = ReplayHash(f); Throws<SafetyException>(() => f.Accept(f.Store(), offer)); Check(ReplayHash(f) == hash, "Malformed envelope was reset or replaced"); }
                    f.Write(Encoding.Unicode.GetBytes(good)); Throws<SafetyException>(() => f.Accept(f.Store(), offer)); }
            });
            Test("Windows replay malformed protected floor types fields hashes and bounds refuse", () => {
                using (var f = new ReplayFixture()) { var offer = f.Offer(); var floor = WindowsChannelReplayFloor.From(f.Channel.Channel, offer); string good = Encoding.UTF8.GetString(ReplaySerialize(floor, typeof(WindowsChannelReplayFloor)));
                    foreach (string bad in new[] { good.Replace("\"format\":1", "\"format\":2"), good.Replace("\"issuedUtc\":" + floor.IssuedUtc, "\"issuedUtc\":-1"), good.Replace("\"issuedUtc\":" + floor.IssuedUtc, "\"issuedUtc\":" + (WindowsChannelOffer.MaximumEpoch + 1)), good.Replace("\"version\":\"0.1.0.0\"", "\"version\":\"0.1.0-dev\""), good.Replace(floor.PolicyHash, new string('a', 64)), good.Replace(floor.BodyHash, "invalid"), good.Replace(floor.ReleaseHash, new string('A', 64)), good.Insert(1, "\"foreign\":true,"), good + "{}", "{}" }) { f.Write(ReplayEnvelope(Encoding.UTF8.GetBytes(bad))); string hash = ReplayHash(f); Throws<SafetyException>(() => f.Accept(f.Store(), offer)); Check(ReplayHash(f) == hash, "Malformed floor was overwritten"); }
                }
            });
            Test("Windows replay enforces envelope ciphertext and plaintext bounds before publication", () => {
                using (var f = new ReplayFixture()) { var offer = f.Offer(); foreach (byte[] bytes in new[] { new byte[0], new byte[WindowsChannelReplayStore.MaximumRecord + 1], ReplayEnvelope(new byte[WindowsChannelReplayStore.MaximumProtected + 1]), ReplayEnvelope(new byte[WindowsChannelReplayStore.MaximumPlaintext + 1]) }) { f.Write(bytes); string hash = ReplayHash(f); Throws<SafetyException>(() => f.Accept(f.Store(), offer)); Check(ReplayHash(f) == hash, "Oversize state rewritten"); } }
            });
            Test("Windows replay protection failures never downgrade to plaintext or reset old state", () => {
                using (var f = new ReplayFixture()) { f.Accept(f.Store()); string hash = ReplayHash(f); var next = f.Offer("0.2.0.0", f.Channel.Seconds - 20); f.Protection.BeforeProtect = () => { throw new CryptographicException("fixture protection refusal"); }; Check(Throws<SafetyException>(() => f.Accept(f.Store(), next)).Message.Contains("could not be protected"), "Protect refusal bypassed"); f.Protection.BeforeProtect = null; f.Protection.BeforeUnprotect = () => { throw new CryptographicException("fixture user mismatch"); }; Check(Throws<SafetyException>(() => f.Accept(f.Store(), next)).Message.Contains("could not be authenticated"), "Unprotect refusal bypassed"); Check(ReplayHash(f) == hash && Directory.GetFiles(f.Root).Length == 1, "Failed protection replaced state or leaked work"); }
            });
            Test("Windows replay checks protection output bounds and roundtrip identity before committing", () => {
                using (var f = new ReplayFixture()) { var next = f.Offer(); var wrong = WindowsChannelReplayFloor.From(f.Channel.Channel, f.Offer("0.2.0.0", f.Channel.Seconds - 20)); foreach (byte[] result in new[] { null, new byte[0], new byte[WindowsChannelReplayStore.MaximumProtected + 1], ReplaySerialize(wrong, typeof(WindowsChannelReplayFloor)) }) { f.Protection.ProtectResult = bytes => result; Throws<SafetyException>(() => f.Accept(f.Store(), next)); Check(!File.Exists(f.PathValue) && Directory.GetFiles(f.Root).Length == 0, "Invalid protect result published or leaked work"); }
                    f.Protection.ProtectResult = null; foreach (byte[] result in new[] { null, new byte[WindowsChannelReplayStore.MaximumPlaintext + 1] }) { f.Protection.UnprotectResult = bytes => result; Throws<SafetyException>(() => f.Accept(f.Store(), next)); Check(!File.Exists(f.PathValue), "Invalid decrypted result was published"); } }
            });
            Test("Windows replay cancellation or scope loss during protection preserves prior state", () => {
                using (var f = new ReplayFixture()) { f.Accept(f.Store()); string hash = ReplayHash(f); var next = f.Offer("0.2.0.0", f.Channel.Seconds - 20); using (var cancel = new CancellationTokenSource()) { f.Protection.BeforeProtect = cancel.Cancel; Throws<OperationCanceledException>(() => f.Accept(f.Store(), next, cancel.Token)); } int allowed = 1; f.Protection.BeforeProtect = () => Interlocked.Exchange(ref allowed, 0); Throws<SafetyException>(() => f.Accept(f.Store(), next, allowed: () => Volatile.Read(ref allowed) != 0)); Check(ReplayHash(f) == hash, "Late protection scope/cancel consumed floor"); }
            });
            Test("Windows replay rechecks expiry and activity just before native publication", () => {
                using (var f = new ReplayFixture()) { f.Accept(f.Store()); string hash = ReplayHash(f); var next = f.Offer("0.2.0.0", f.Channel.Seconds - 20); int allowed = 1, reached = 0; var store = f.Store((stage, path) => { if (stage == "Before publication") { reached++; Interlocked.Exchange(ref allowed, 0); } }); Throws<SafetyException>(() => f.Accept(store, next, allowed: () => Volatile.Read(ref allowed) != 0));
                    var expired = f.Store((stage, path) => { if (stage == "Before publication") { reached++; f.Channel.Now = f.Channel.Now.AddHours(2); } }); Throws<SafetyException>(() => f.Accept(expired, next)); Check(reached == 2 && ReplayHash(f) == hash && Directory.GetFiles(f.Root).Length == 1, "Publication boundary was not reached or late refusal consumed floor/leaked temporary"); }
            });
            Test("Windows replay cancelled publication retains old state and permits a later manual attempt", () => {
                using (var f = new ReplayFixture()) using (var cancel = new CancellationTokenSource()) { f.Accept(f.Store()); string hash = ReplayHash(f); var next = f.Offer("0.2.0.0", f.Channel.Seconds - 20); var store = f.Store((stage, path) => { if (stage == "Before publication") cancel.Cancel(); }); Throws<OperationCanceledException>(() => f.Accept(store, next, cancel.Token)); Check(ReplayHash(f) == hash && Directory.GetFiles(f.Root).Length == 1, "Precommit cancellation modified old floor"); f.Accept(f.Store(), next); Check(ReplaySavedFloor(f).Version == "0.2.0.0", "Manual retry did not publish"); }
            });
            Test("Windows replay cancellation after publication retains committed restrictive state", () => {
                using (var f = new ReplayFixture()) using (var cancel = new CancellationTokenSource()) { f.Accept(f.Store()); var next = f.Offer("0.2.0.0", f.Channel.Seconds - 20); f.Accept(f.Store((stage, path) => { if (stage == "After publication") cancel.Cancel(); }), next, cancel.Token); Check(cancel.IsCancellationRequested && ReplaySavedFloor(f).Version == "0.2.0.0", "Committed floor was undone or reported as old state"); Throws<SafetyException>(() => f.Accept(f.Store(), f.Offer("0.1.0.0"))); }
            });
            Test("Windows replay postpublication failure reports committed state and restart blocks older releases", () => {
                using (var f = new ReplayFixture()) { f.Accept(f.Store()); var next = f.Offer("0.2.0.0", f.Channel.Seconds - 20); var store = f.Store((stage, path) => { if (stage == "After publication") throw new InvalidOperationException("fixture final check refusal"); }); Check(Throws<SafetyException>(() => f.Accept(store, next)).Message.Contains("was published"), "Ambiguous commit failure hid publication"); Check(ReplaySavedFloor(f).Version == "0.2.0.0" && Directory.GetFiles(f.Root).Length == 1, "Postcommit failure undid floor or leaked work"); Throws<SafetyException>(() => f.Accept(f.Store(), f.Offer("0.1.0.0"))); f.Accept(f.Store(), next); }
            });
            Test("Windows replay exact prior hash is checked inside exclusive replacement despite restored timestamps", () => {
                using (var f = new ReplayFixture()) { f.Accept(f.Store()); var next = f.Offer("0.2.0.0", f.Channel.Seconds - 20); byte[] original = File.ReadAllBytes(f.PathValue), foreign = (byte[])original.Clone(); foreign[foreign.Length / 2] = foreign[foreign.Length / 2] == (byte)'A' ? (byte)'B' : (byte)'A'; var modified = File.GetLastWriteTimeUtc(f.PathValue);
                    var store = f.Store((stage, path) => { if (stage == "After replay read") { File.WriteAllBytes(f.PathValue, foreign); File.SetLastWriteTimeUtc(f.PathValue, modified); } }); Check(Throws<SafetyException>(() => f.Accept(store, next)).Message.Contains("changed before its exclusive"), "Exact prior hash did not guard replacement"); Check(File.ReadAllBytes(f.PathValue).SequenceEqual(foreign) && Directory.GetFiles(f.Root).Length == 1, "Foreign bytes overwritten or temporary leaked"); }
            });
            Test("Windows replay initially absent state refuses a file appearing before writer replacement", () => {
                using (var f = new ReplayFixture()) { var store = f.Store((stage, path) => { if (stage == "After replay read") File.WriteAllText(f.PathValue, "foreign appeared state"); }); Throws<SafetyException>(() => f.Accept(store)); Check(File.ReadAllText(f.PathValue) == "foreign appeared state" && Directory.GetFiles(f.Root).Length == 1, "Late appearing state overwritten"); }
            });
            Test("Windows replay observed deletion or restoration of earlier valid local state is refused in-process", () => {
                using (var f = new ReplayFixture()) { var store = f.Store(); var first = f.Offer(); f.Accept(store, first); byte[] old = File.ReadAllBytes(f.PathValue); var next = f.Offer("0.2.0.0", f.Channel.Seconds - 20); f.Accept(store, next); File.Delete(f.PathValue); Throws<SafetyException>(() => f.Accept(store, next)); Check(!File.Exists(f.PathValue), "Deleted observed state was recreated"); f.Write(old); Throws<SafetyException>(() => f.Accept(store, next)); Check(File.ReadAllBytes(f.PathValue).SequenceEqual(old), "Restored old local state was silently overwritten"); }
            });
            Test("Windows replay rejects aliases protection and directory-shaped state while retaining content", () => {
                using (var f = new ReplayFixture()) { f.Accept(f.Store()); var next = f.Offer("0.2.0.0", f.Channel.Seconds - 20); string hash = ReplayHash(f); foreach (FileAttributes flag in new[] { FileAttributes.ReadOnly, FileAttributes.System, FileAttributes.Offline }) { File.SetAttributes(f.PathValue, flag); try { Throws<SafetyException>(() => f.Accept(f.Store(), next)); Check(ReplayHash(f) == hash, "Protected state changed"); } finally { File.SetAttributes(f.PathValue, FileAttributes.Normal); } }
                    string alias = Path.Combine(f.Channel.Archive.Package.Files.Source, "replay-alias.json"); Check(CreateHardLink(alias, f.PathValue, IntPtr.Zero), "Owned replay alias creation failed"); Throws<SafetyException>(() => f.Accept(f.Store(), next)); Check(ReplayHash(f) == hash && File.ReadAllBytes(alias).SequenceEqual(File.ReadAllBytes(f.PathValue)), "Aliased state changed"); File.Delete(alias); File.Delete(f.PathValue); Directory.CreateDirectory(f.PathValue); Throws<SafetyException>(() => f.Accept(f.Store(), next)); Check(Directory.Exists(f.PathValue), "Directory-shaped state removed"); }
            });
            Test("Windows replay rejects redirected root and holds directories across protection callbacks", () => {
                using (var f = new ReplayFixture()) { string link = Path.Combine(f.Channel.Archive.Package.Files.Root, "replay-link"); Directory.CreateDirectory(f.Root); CreateJunction(f.Channel.Archive.Package.Files, link, f.Root); Throws<SafetyException>(() => new WindowsChannelReplayStore(link, f.Protection).Accept(f.Channel.Channel, f.Offer(), CancellationToken.None, null)); Check(Directory.GetFiles(f.Root).Length == 0, "Redirected root received state");
                    bool blocked = false; Exception failure = null; f.Protection.BeforeProtect = () => { try { Directory.Move(f.Root, f.Root + "-moved"); } catch (IOException) { blocked = true; } }; try { f.Accept(f.Store()); } catch (Exception ex) { failure = ex; }
                    if (blocked) Check(failure == null && File.Exists(f.PathValue), "Blocked root movement prevented an unchanged valid commit"); else Check(failure != null && Directory.Exists(f.Root + "-moved") && !File.Exists(f.PathValue) && Directory.GetFiles(f.Root + "-moved").Length == 0, "Moved root received state or leaked writer work"); }
            });
            Test("Windows replay serializes separate stores through the same native writer gate", () => {
                using (var f = new ReplayFixture()) using (var entered = new ManualResetEventSlim()) using (var release = new ManualResetEventSlim()) { var next = f.Offer("0.2.0.0", f.Channel.Seconds - 20); var original = f.Offer(); var first = f.Store((stage, path) => { if (stage == "After replay read") { entered.Set(); if (!release.Wait(5000)) throw new Exception("Replay writer fixture timed out"); } });
                    var task = Task.Run(() => { f.Accept(first, next); return true; }); try { Check(entered.Wait(2000), "First replay writer did not enter"); Throws<System.ComponentModel.Win32Exception>(() => f.Accept(f.Store(), original)); } finally { release.Set(); } Check(ChannelAwait(() => task), "First writer failed"); Throws<SafetyException>(() => f.Accept(f.Store(), original)); Check(ReplaySavedFloor(f).Version == "0.2.0.0", "Separate writer replaced newer floor"); }
            });
            Test("Windows replay shared writer gate blocks configuration editors without modifying choices", () => {
                using (var f = new ReplayFixture()) { f.Accept(f.Store()); string hash = ReplayHash(f); using (Profiles.WriterGate(f.Root)) Throws<System.ComponentModel.Win32Exception>(() => f.Accept(f.Store(), f.Offer("0.2.0.0", f.Channel.Seconds - 20))); Check(ReplayHash(f) == hash, "Competing editor changed replay state"); f.Accept(f.Store(), f.Offer("0.2.0.0", f.Channel.Seconds - 20)); Check(ReplaySavedFloor(f).Version == "0.2.0.0", "Released gate prevented manual attempt"); }
            });
            Test("Windows discovery composes persistent replay across new service instances and manual retries", () => {
                using (var f = new ReplayFixture()) { var c = f.Channel; var transport = ChannelTransport(c, c.Text("0.2.0.0", c.Seconds - 20)); var first = new WindowsChannelDiscovery(c.Channel, transport, new WindowsManifestTrust(c.Archive.Package.CertificateTrust), () => c.Now, () => true, f.Store()); Check(ChannelCheck(first).Release.Version == "0.2.0.0", "Persistent discovery did not accept"); string hash = ReplayHash(f);
                    transport.Fetch = ChannelTransport(c, c.Text("0.1.0.0", c.Seconds - 50)).Fetch; var restarted = new WindowsChannelDiscovery(c.Channel, transport, new WindowsManifestTrust(c.Archive.Package.CertificateTrust), () => c.Now, () => true, f.Store()); Throws<SafetyException>(() => ChannelCheck(restarted)); Check(ReplayHash(f) == hash && transport.Metadata == 2 && transport.Signature == 2, "Restarted discovery lost replay restriction or automatically retried");
                    transport.Fetch = ChannelTransport(c, c.Text("0.2.0.0", c.Seconds - 20)).Fetch; Check(ChannelCheck(restarted).Release.Version == "0.2.0.0", "Replay refusal retained discovery slot"); }
            });
            Test("Windows discovery late scope loss after persistence refuses return and retains restrictive replay", () => {
                using (var f = new ReplayFixture()) { var c = f.Channel; int allowed = 1; var transport = ChannelTransport(c, c.Text("0.2.0.0", c.Seconds - 20)); var store = f.Store((stage, path) => { if (stage == "After publication") Interlocked.Exchange(ref allowed, 0); }); var discovery = new WindowsChannelDiscovery(c.Channel, transport, new WindowsManifestTrust(c.Archive.Package.CertificateTrust), () => c.Now, () => Volatile.Read(ref allowed) != 0, store);
                    Throws<SafetyException>(() => ChannelCheck(discovery)); Check(ReplaySavedFloor(f).Version == "0.2.0.0", "Postcommit scope loss undid restrictive floor"); Interlocked.Exchange(ref allowed, 1); transport.Fetch = ChannelTransport(c, c.Text("0.1.0.0", c.Seconds - 50)).Fetch; Throws<SafetyException>(() => ChannelCheck(discovery)); Check(transport.Metadata == 2 && transport.Signature == 2, "Failed discovery did not release slot"); }
            });
            Test("Windows replay leaves other configuration history tools and profile catalog intact", () => {
                using (var f = new ReplayFixture()) { Directory.CreateDirectory(f.Root); string[] names = { "app-settings.json", "import-defaults.json", "completed-imports.json", "quick-share-tools.json", "luna-lut-fixture.keep" }; foreach (string name in names) File.WriteAllText(Path.Combine(f.Root, name), "owned fixture bytes: " + name);
                    var hashes = names.ToDictionary(name => name, name => WindowsSignerEvidence.Hash(File.ReadAllBytes(Path.Combine(f.Root, name)))); f.Accept(f.Store()); f.Accept(f.Store(), f.Offer("0.2.0.0", f.Channel.Seconds - 20));
                    Check(names.All(name => WindowsSignerEvidence.Hash(File.ReadAllBytes(Path.Combine(f.Root, name))) == hashes[name]) && Directory.GetFiles(f.Root).Length == names.Length + 1 && Profiles.Catalog(f.Root).Length == 0, "Replay changed other settings or became a device profile"); }
            });
            Test("Windows replay identical fast path rejects replacement identity despite identical saved bytes", () => {
                using (var f = new ReplayFixture()) { var offer = f.Offer(); f.Accept(f.Store(), offer); byte[] bytes = File.ReadAllBytes(f.PathValue); string moved = Path.Combine(f.Channel.Archive.Package.Files.Source, "prior-replay.keep"); var modified = File.GetLastWriteTimeUtc(f.PathValue);
                    var store = f.Store((stage, path) => { if (stage == "After replay read") { File.Move(f.PathValue, moved); File.WriteAllBytes(f.PathValue, bytes); File.SetLastWriteTimeUtc(f.PathValue, modified); } }); Check(Throws<SafetyException>(() => f.Accept(store, offer)).Message.Contains("changed before its exclusive"), "Equivalent fast path ignored replacement identity"); Check(File.ReadAllBytes(f.PathValue).SequenceEqual(bytes) && File.ReadAllBytes(moved).SequenceEqual(bytes), "Fast path cleaned either old or replacement state"); }
            });
            Test("Windows replay late alias during unprotection refuses without altering either name", () => {
                using (var f = new ReplayFixture()) { f.Accept(f.Store()); string hash = ReplayHash(f), alias = Path.Combine(f.Channel.Archive.Package.Files.Source, "late-replay-alias.keep"); bool reached = false; f.Protection.BeforeUnprotect = () => { reached = true; Check(CreateHardLink(alias, f.PathValue, IntPtr.Zero), "Late owned alias creation failed"); };
                    Throws<SafetyException>(() => f.Accept(f.Store(), f.Offer("0.2.0.0", f.Channel.Seconds - 20))); Check(reached && ReplayHash(f) == hash && File.ReadAllBytes(alias).SequenceEqual(File.ReadAllBytes(f.PathValue)), "Late alias check changed state"); }
            });
            Test("Windows replay publication exclusively holds prior state and retains external reader locks", () => {
                using (var f = new ReplayFixture()) { f.Accept(f.Store()); string hash = ReplayHash(f); var next = f.Offer("0.2.0.0", f.Channel.Seconds - 20); using (var reader = new FileStream(f.PathValue, FileMode.Open, FileAccess.Read, FileShare.Read)) { Throws<System.ComponentModel.Win32Exception>(() => f.Accept(f.Store(), next)); Check(ReplayHash(f) == hash, "Replay bypassed external reader lock"); }
                    int blocked = 0; f.Accept(f.Store((stage, path) => { if (stage == "Before publication") { Throws<IOException>(() => File.WriteAllText(f.PathValue, "foreign old state")); Throws<IOException>(() => File.Delete(f.PathValue)); Throws<IOException>(() => File.WriteAllText(path, "foreign temporary")); blocked = 3; } }), next);
                    Check(blocked == 3 && ReplaySavedFloor(f).Version == "0.2.0.0" && Directory.GetFiles(f.Root).Length == 1, "Exclusive old/new state ownership failed"); }
            });
            Test("Windows replay preserves bounded zone metadata and refuses unlisted alternate streams", () => {
                using (var f = new ReplayFixture()) { var offer = f.Offer(); f.Accept(f.Store(), offer); string hash = ReplayHash(f), zone = "[ZoneTransfer]\r\nZoneId=3\r\n"; ReplayFixtureStream(f, "Zone.Identifier", zone); f.Accept(f.Store(), offer); Check(ReplayFixtureStream(f, "Zone.Identifier") == zone && ReplayHash(f) == hash, "Unchanged state lost download metadata");
                    ReplayFixtureStream(f, "foreign", "keep owned fixture stream"); Throws<SafetyException>(() => f.Accept(f.Store(), f.Offer("0.2.0.0", f.Channel.Seconds - 20))); Check(ReplayHash(f) == hash && ReplayFixtureStream(f, "foreign") == "keep owned fixture stream" && ReplayFixtureStream(f, "Zone.Identifier") == zone, "Alternate stream refusal changed or cleaned state"); }
            });
            Test("Windows discovery scope lost during extra persistent native validation cannot consume either floor", () => {
                using (var f = new ReplayFixture()) { var c = f.Channel; int allowed = 1; var transport = ChannelTransport(c, c.Text("0.2.0.0", c.Seconds - 20)); var discovery = new WindowsChannelDiscovery(c.Channel, transport, new WindowsManifestTrust(c.Archive.Package.CertificateTrust), () => c.Now, () => Volatile.Read(ref allowed) != 0, f.Store());
                    c.Archive.Package.CertificateTrust.During = () => { if (c.Archive.Package.CertificateTrust.Calls == 3) Interlocked.Exchange(ref allowed, 0); }; Throws<SafetyException>(() => ChannelCheck(discovery)); Check(!Directory.Exists(f.Root) && c.Archive.Package.CertificateTrust.Calls == 3, "Failed persistent native scope advanced state");
                    c.Archive.Package.CertificateTrust.During = null; Interlocked.Exchange(ref allowed, 1); transport.Fetch = ChannelTransport(c, c.Text("0.1.0.0", c.Seconds - 50)).Fetch; Check(ChannelCheck(discovery).Release.Version == "0.1.0.0" && ReplaySavedFloor(f).Version == "0.1.0.0", "Failed persistence advanced the process floor or consumed retry slot"); }
            });
        }
    }
}
