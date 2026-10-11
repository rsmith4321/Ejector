using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace EasyEject {
    public static partial class Tests {
        sealed class WindowsChannelFixture : IDisposable {
            internal readonly WindowsArchiveFixture Archive = new WindowsArchiveFixture();
            internal readonly ApprovedWindowsChannel Channel; internal DateTime Now = DateTime.UtcNow;
            internal long Seconds { get { return (Now.Ticks - new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc).Ticks) / TimeSpan.TicksPerSecond; } }
            internal WindowsChannelFixture() { Channel = new ApprovedWindowsChannel("https://example.invalid/windows/windows-channel.json", "https://example.invalid/windows/windows-channel.json.p7s", "https://downloads.example.invalid/windows/releases/", Archive.Package.Signing.Publisher()); }
            internal string Text(string version = "0.2.0.0", long? issued = null, long? expires = null) {
                string name = "EasyEject.Windows-" + version + "-x64.zip";
                return "{\"format\":1,\"kind\":\"windows-channel\",\"application\":\"EasyEject.Windows\",\"platform\":\"windows-x64\",\"channel\":\"stable\",\"issuedUtc\":" + (issued ?? Seconds - 60) + ",\"expiresUtc\":" + (expires ?? Seconds + 3600) + ",\"release\":{\"version\":\"" + version + "\",\"sourceRevision\":\"384cb767ffbbb87ec5fe67f45d0b4d0ce324ad7c\",\"manifestSha256\":\"" + FileHash(WindowsReleaseManifest.ManifestName) + "\",\"manifestSignatureSha256\":\"" + FileHash(WindowsReleaseManifest.SignatureName) + "\",\"certificateSha256\":\"" + Archive.Package.Signing.Evidence().CertificateHash + "\",\"archive\":{\"name\":\"" + name + "\",\"url\":\"" + Channel.Archives.AbsoluteUri + version + "/" + name + "\",\"length\":" + new FileInfo(Archive.Path).Length + ",\"sha256\":\"" + WindowsSignerEvidence.Hash(File.ReadAllBytes(Archive.Path)) + "\"}}}";
            }
            internal string FileHash(string name) { return WindowsSignerEvidence.Hash(File.ReadAllBytes(System.IO.Path.Combine(Archive.Package.Root, name))); }
            internal WindowsChannelOffer Offer(string text = null, byte[] bytes = null, byte[] signature = null, IWindowsManifestEnvelopeTrust trust = null, ApprovedWindowsChannel channel = null, CancellationToken cancel = default(CancellationToken)) {
                byte[] body = bytes ?? Encoding.UTF8.GetBytes(text ?? Text()); return new WindowsChannelOffer(body, signature ?? (body.Length == 0 || body.Length > WindowsManifestTrust.MaximumManifest ? new byte[] { 1 } : Archive.Package.Signing.Sign(body)), channel ?? Channel, trust ?? new WindowsManifestTrust(Archive.Package.CertificateTrust), () => Now, cancel);
            }
            public void Dispose() { Archive.Dispose(); }
        }
        static void WindowsChannelTests() {
            Test("Signed Windows channel binds exact archive release manifest signature and certificate", delegate {
                using (var f = new WindowsChannelFixture()) {
                    string zipHash = WindowsSignerEvidence.Hash(File.ReadAllBytes(f.Archive.Path)); var offer = f.Offer();
                    Check(offer.Release.ArchiveHash == zipHash && offer.Release.Version == "0.2.0.0" && offer.Release.ManifestHash == f.FileHash(WindowsReleaseManifest.ManifestName) && offer.Release.ManifestSignatureHash == f.FileHash(WindowsReleaseManifest.SignatureName) && offer.Release.CertificateHash == f.Archive.Package.Signing.Evidence().CertificateHash && offer.Release.ArchiveUrl.AbsoluteUri == f.Channel.Archives.AbsoluteUri + "0.2.0.0/EasyEject.Windows-0.2.0.0-x64.zip", "Signed offer omitted exact release or archive binding");
                    using (var archive = f.Archive.Review()) using (var package = f.Archive.Package.Review()) { offer.VerifyArchive(archive, CancellationToken.None); offer.VerifyExtracted(archive, package, CancellationToken.None); }
                    Check(zipHash == WindowsSignerEvidence.Hash(File.ReadAllBytes(f.Archive.Path)), "Channel review changed ZIP contents");
                }
            });
            Test("Channel comparison is numeric and never treats development or invalid local version as current", delegate {
                using (var f = new WindowsChannelFixture()) { var release = f.Offer(f.Text("0.10.0.0")).Release;
                    Check(release.Compare("0.9.0.0") == WindowsReleaseComparison.NewerAvailable && release.Compare("0.10.0.0") == WindowsReleaseComparison.SameStableVersion && release.Compare("1.0.0.0") == WindowsReleaseComparison.ChannelOlder, "Stable versions use lexical or incorrect ordering");
                    foreach (string version in new[] { null, "", "0.2.0-dev", "0.10.0", "00.10.0.0", "0.0.0.0", "0.10.0.0-preview", "unknown" }) Check(release.Compare(version) == WindowsReleaseComparison.UncomparableLocalVersion, "Invalid/development version became up to date");
                }
            });
            Test("Channel approval uses code supplied HTTPS endpoints and exact paired signature", delegate {
                using (var f = new WindowsChannelFixture()) foreach (string uri in new[] { "http://example.invalid/channel.json", "file:///C:/channel.json", "https://user@example.invalid/channel.json", "https://example.invalid:444/channel.json", "https://example.invalid/channel.json?token=value", "https://example.invalid/channel.json#fragment", "https://example.invalid/a/../channel.json", "https://example.invalid/%63hannel.json", "https://example.invalid\\channel.json", "https://localhost/channel.json", "https://127.0.0.1/channel.json", "https://example.invalid/é.json", "https://EXAMPLE.invalid/channel.json" }) Throws<SafetyException>(() => new ApprovedWindowsChannel(uri, uri + ".p7s", f.Channel.Archives.AbsoluteUri, f.Channel.Publisher));
                using (var f = new WindowsChannelFixture()) { Throws<SafetyException>(() => new ApprovedWindowsChannel(f.Channel.Metadata.AbsoluteUri, "https://other.invalid/signature.p7s", f.Channel.Archives.AbsoluteUri, f.Channel.Publisher)); Throws<SafetyException>(() => new ApprovedWindowsChannel(f.Channel.Metadata.AbsoluteUri, f.Channel.Signature.AbsoluteUri, "https://downloads.example.invalid/windows/releases", f.Channel.Publisher)); }
            });
            Test("Unconfigured channel and cancelled admission perform no trust or clock work", delegate {
                int clocks = 0; var trust = new MutatingEnvelopeTrust(); Throws<SafetyException>(() => new WindowsChannelOffer(new byte[1], new byte[1], null, trust, () => { clocks++; return DateTime.UtcNow; }, CancellationToken.None)); Check(trust.Calls == 0 && clocks == 0, "Unconfigured channel performed work");
                using (var f = new WindowsChannelFixture()) using (var cancel = new CancellationTokenSource()) { cancel.Cancel(); Throws<OperationCanceledException>(() => f.Offer(cancel: cancel.Token)); Check(f.Archive.Package.CertificateTrust.Calls == 0, "Cancelled channel reached trust"); }
            });
            Test("Channel rejects malformed excessive body signature BOM UTF16 and trailing documents", delegate {
                using (var f = new WindowsChannelFixture()) {
                    foreach (byte[] bytes in new[] { new byte[0], new byte[65537], Encoding.Unicode.GetBytes(f.Text()), Encoding.UTF8.GetPreamble().Concat(Encoding.UTF8.GetBytes(f.Text())).ToArray(), Encoding.UTF8.GetBytes(f.Text() + "{}"), Encoding.UTF8.GetBytes("{broken}"), Encoding.UTF8.GetBytes("{\"x\":[[[[[[[[1]]]]]]]]}") }) Throws<SafetyException>(() => f.Offer(bytes: bytes));
                    foreach (byte[] bytes in new[] { new byte[0], new byte[262145], new byte[] { 1, 2, 3 } }) Throws<SafetyException>(() => f.Offer(signature: bytes));
                }
            });
            Test("Channel refuses unknown duplicate escaped duplicate annotation missing null and wrong typed fields", delegate {
                using (var f = new WindowsChannelFixture()) foreach (string kind in new[] { "unknown", "duplicate", "escaped", "annotation", "missing", "null", "number-string", "array", "release-extra", "archive-extra" }) {
                    string text = f.Text();
                    if (kind == "unknown" || kind == "duplicate" || kind == "escaped" || kind == "annotation") text = text.Insert(1, kind == "unknown" ? "\"foreign\":1," : kind == "duplicate" ? "\"format\":1," : kind == "escaped" ? "\"f\\u006frmat\":1," : "\"__type\":\"Injected:ns\",");
                    if (kind == "missing") text = text.Replace("\"channel\":\"stable\",", "");
                    if (kind == "null") text = text.Replace("\"channel\":\"stable\"", "\"channel\":null");
                    if (kind == "number-string") text = text.Replace("\"format\":1", "\"format\":\"1\"");
                    if (kind == "array") text = text.Replace("\"release\":{", "\"release\":[{").Replace("}}}", "}}]}");
                    if (kind == "release-extra") text = text.Replace("\"release\":{", "\"release\":{\"foreign\":1,");
                    if (kind == "archive-extra") text = text.Replace("\"archive\":{", "\"archive\":{\"foreign\":1,");
                    Throws<SafetyException>(() => f.Offer(text));
                }
            });
            Test("Channel rejects Mac wrong app unstable versions source identity and digest aliases", delegate {
                using (var f = new WindowsChannelFixture()) {
                    foreach (string bad in new[] { f.Text().Replace("windows-x64", "macos-x64"), f.Text().Replace("EasyEject.Windows\"", "EasyEject.Mac\""), f.Text().Replace("windows-channel", "windows-release"), f.Text().Replace("\"format\":1", "\"format\":2"), f.Text().Replace("stable", "beta"), f.Text().Replace("384cb767ffbbb87ec5fe67f45d0b4d0ce324ad7c", "unknown"), f.Text().Replace("384cb767ffbbb87ec5fe67f45d0b4d0ce324ad7c", "384CB767FFBBB87EC5FE67F45D0B4D0CE324AD7C") }) Throws<SafetyException>(() => f.Offer(bad));
                    foreach (string version in new[] { "0.2.0-dev", "0.2.0", "0.0.0.0", "00.2.0.0", "65536.1.0.0" }) Throws<SafetyException>(() => f.Offer(f.Text(version)));
                    foreach (string field in new[] { "manifestSha256", "manifestSignatureSha256", "certificateSha256", "sha256" }) { string text = f.Text(), prefix = "\"" + field + "\":\""; int start = text.IndexOf(prefix, StringComparison.Ordinal) + prefix.Length; foreach (string value in new[] { "", new string('A', 64), new string('g', 64), new string('a', 63) }) Throws<SafetyException>(() => f.Offer(text.Substring(0, start) + value + text.Substring(start + 64))); }
                }
            });
            Test("Channel refuses alternate host path version filename credentials encoded or downgraded archive URL", delegate {
                using (var f = new WindowsChannelFixture()) { string good = f.Channel.Archives.AbsoluteUri + "0.2.0.0/EasyEject.Windows-0.2.0.0-x64.zip";
                    foreach (string uri in new[] { good.Replace("https:", "http:"), good.Replace("downloads.example.invalid", "other.invalid"), good + "?token=value", good + "#fragment", good.Replace("/releases/", "/other/"), good.Replace("/0.2.0.0/", "/0.3.0.0/"), good.Replace("x64.zip", "x64.exe"), good.Replace("https://", "https://user@"), good.Replace(".zip", "%2ezip"), good.Replace("/releases/", "/releases/a/../") }) Throws<SafetyException>(() => f.Offer(f.Text().Replace(good, uri)));
                    Throws<SafetyException>(() => f.Offer(f.Text().Replace("EasyEject.Windows-0.2.0.0-x64.zip", "EasyEject.Mac-0.2.0.0-x64.zip")));
                }
            });
            Test("Channel requires canonical bounded integral archive length", delegate {
                using (var f = new WindowsChannelFixture()) { string prefix = "\"length\":" + new FileInfo(f.Archive.Path).Length;
                    foreach (string value in new[] { "0", "21", "2147483649", "-1", "1.0", "1e2", "\"123\"", "null", "9223372036854775808" }) Throws<SafetyException>(() => f.Offer(f.Text().Replace(prefix, "\"length\":" + value)));
                }
            });
            Test("Channel refuses expired future unbounded lifetime and non UTC clocks", delegate {
                using (var f = new WindowsChannelFixture()) {
                    foreach (long[] dates in new[] { new[] { f.Seconds - 3600, f.Seconds }, new[] { f.Seconds, f.Seconds }, new[] { f.Seconds + 301, f.Seconds + 3600 }, new[] { f.Seconds - 1, f.Seconds + WindowsChannelOffer.MaximumLifetime }, new[] { WindowsChannelOffer.MaximumEpoch + 1, WindowsChannelOffer.MaximumEpoch + 2 } }) Throws<SafetyException>(() => f.Offer(f.Text(issued: dates[0], expires: dates[1])));
                    f.Offer(f.Text(issued: f.Seconds + 300)); f.Now = DateTime.SpecifyKind(f.Now, DateTimeKind.Unspecified); Throws<SafetyException>(() => f.Offer()); f.Now = DateTime.SpecifyKind(f.Now, DateTimeKind.Local); Throws<SafetyException>(() => f.Offer()); f.Now = new DateTime(1969, 1, 1, 0, 0, 0, DateTimeKind.Utc); Throws<SafetyException>(() => f.Offer(f.Text(issued: 0, expires: 3600)));
                }
            });
            Test("Channel expiry during or after native trust refuses result and later archive action", delegate {
                using (var f = new WindowsChannelFixture()) { string text = f.Text(expires: f.Seconds + 60); f.Archive.Package.CertificateTrust.During = () => f.Now = f.Now.AddMinutes(2); Throws<SafetyException>(() => f.Offer(text)); f.Archive.Package.CertificateTrust.During = null; var offer = f.Offer(f.Text(expires: f.Seconds + 60)); f.Now = f.Now.AddMinutes(2); Throws<SafetyException>(() => offer.Revalidate(CancellationToken.None)); using (var archive = f.Archive.Review()) Throws<SafetyException>(() => offer.VerifyArchive(archive, CancellationToken.None)); }
            });
            Test("Already cancelled channel revalidation does not begin clock or native trust checks", delegate {
                using (var f = new WindowsChannelFixture()) using (var cancel = new CancellationTokenSource()) { var offer = f.Offer(); int count = f.Archive.Package.CertificateTrust.Calls; f.Now = f.Now.AddDays(8); cancel.Cancel(); Throws<OperationCanceledException>(() => offer.Revalidate(cancel.Token)); var ledger = new WindowsChannelHighWater(f.Channel); Throws<OperationCanceledException>(() => ledger.Accept(offer, cancel.Token)); Check(f.Archive.Package.CertificateTrust.Calls == count, "Cancelled review requested fresh certificate trust"); }
            });
            Test("Channel authenticates immutable original bytes across caller and trust callback mutation", delegate {
                using (var f = new WindowsChannelFixture()) { byte[] body = Encoding.UTF8.GetBytes(f.Text()), signature = f.Archive.Package.Signing.Sign(body); f.Archive.Package.CertificateTrust.During = () => { body[0] = 0; signature[0] = 0; }; var offer = f.Offer(bytes: body, signature: signature); offer.Revalidate(CancellationToken.None); Check(offer.Release.Version == "0.2.0.0", "Caller arrays retargeted channel state"); f.Archive.Package.CertificateTrust.During = null;
                    var trust = new MutatingEnvelopeTrust { Actual = new WindowsManifestTrust(f.Archive.Package.CertificateTrust) }; var frozen = f.Offer(trust: trust); frozen.Revalidate(CancellationToken.None); trust.Retarget = true; Throws<SafetyException>(() => f.Offer(trust: trust));
                }
            });
            Test("Channel native untrusted signer and late native refusal cannot become approved offers", delegate {
                using (var f = new WindowsChannelFixture()) { Check(Throws<SafetyException>(() => f.Offer(trust: new WindowsManifestTrust())).Message.Contains("0x800B0109"), "Native channel untrusted-root proof lost"); var offer = f.Offer(); f.Archive.Package.CertificateTrust.Throw = true; Throws<SafetyException>(() => offer.Revalidate(CancellationToken.None)); }
            });
            Test("Channel exact ZIP binding rejects every different signed artifact identity", delegate {
                using (var f = new WindowsChannelFixture()) using (var archive = f.Archive.Review()) {
                    string text = f.Text(); foreach (string kind in new[] { "zip-hash", "zip-size", "manifest", "signature", "certificate", "source", "version" }) {
                        string changed = kind == "zip-hash" ? text.Replace(archive.Hash, new string('a', 64)) : kind == "zip-size" ? text.Replace("\"length\":" + archive.Length, "\"length\":" + (archive.Length + 1)) : kind == "manifest" ? text.Replace(archive.Authenticated.BodyHash, new string('a', 64)) : kind == "signature" ? text.Replace(archive.Authenticated.SignatureHash, new string('a', 64)) : kind == "certificate" ? text.Replace(archive.Authenticated.Signer.CertificateHash, new string('a', 64)) : kind == "source" ? text.Replace("384cb767ffbbb87ec5fe67f45d0b4d0ce324ad7c", new string('a', 40)) : f.Text("0.3.0.0"); var offer = f.Offer(changed); Check(Throws<SafetyException>(() => offer.VerifyArchive(archive, CancellationToken.None)).Message.Contains("does not match"), "Signed artifact mismatch accepted: " + kind);
                    }
                }
            });
            Test("Channel binds ZIP encoding as well as identical authenticated payload contents", delegate {
                using (var f = new WindowsChannelFixture()) { var offer = f.Offer(); f.Archive.Write(descriptors: true); using (var archive = f.Archive.Review()) Check(Throws<SafetyException>(() => offer.VerifyArchive(archive, CancellationToken.None)).Message.Contains("does not match"), "Different ZIP encoding became signed channel artifact despite matching payloads"); }
            });
            Test("Channel process high water refuses rollback same issue equivocation and immutable version substitution", delegate {
                using (var f = new WindowsChannelFixture()) { var ledger = new WindowsChannelHighWater(f.Channel); var original = f.Offer(f.Text(issued: f.Seconds - 50)); ledger.Accept(original, CancellationToken.None); ledger.Accept(original, CancellationToken.None);
                    foreach (string text in new[] { f.Text(issued: f.Seconds - 51), f.Text(issued: f.Seconds - 50).Replace("384cb767ffbbb87ec5fe67f45d0b4d0ce324ad7c", new string('a', 40)), f.Text(issued: f.Seconds - 40).Replace("384cb767ffbbb87ec5fe67f45d0b4d0ce324ad7c", new string('a', 40)) }) { var offer = f.Offer(text); Throws<SafetyException>(() => ledger.Accept(offer, CancellationToken.None)); }
                    var sameReleaseLater = f.Offer(f.Text(issued: f.Seconds - 40)); ledger.Accept(sameReleaseLater, CancellationToken.None); var next = f.Offer(f.Text("0.3.0.0", f.Seconds - 30)); ledger.Accept(next, CancellationToken.None); var rollback = f.Offer(f.Text(issued: f.Seconds - 20)); Throws<SafetyException>(() => ledger.Accept(rollback, CancellationToken.None));
                }
            });
            Test("Channel high water cannot mix approved endpoint or publisher policies", delegate {
                using (var f = new WindowsChannelFixture()) { var other = new ApprovedWindowsChannel("https://other.invalid/channel.json", "https://other.invalid/channel.json.p7s", f.Channel.Archives.AbsoluteUri, f.Channel.Publisher); var offer = f.Offer(channel: other); var ledger = new WindowsChannelHighWater(f.Channel); Check(Throws<SafetyException>(() => ledger.Accept(offer, CancellationToken.None)).Message.Contains("different approved"), "Other channel policy contaminated replay state"); }
            });
            Test("Channel failed or cancelled fresh trust does not consume a high water release", delegate {
                using (var f = new WindowsChannelFixture()) using (var cancel = new CancellationTokenSource()) { var ledger = new WindowsChannelHighWater(f.Channel); var original = f.Offer(f.Text(issued: f.Seconds - 50)); ledger.Accept(original, CancellationToken.None); var next = f.Offer(f.Text("0.3.0.0", f.Seconds - 40)); f.Archive.Package.CertificateTrust.During = cancel.Cancel; Throws<OperationCanceledException>(() => ledger.Accept(next, cancel.Token)); f.Archive.Package.CertificateTrust.During = null; f.Archive.Package.CertificateTrust.Throw = true; Throws<SafetyException>(() => ledger.Accept(next, CancellationToken.None)); f.Archive.Package.CertificateTrust.Throw = false; ledger.Accept(original, CancellationToken.None); }
            });
            Test("Channel concurrent fresh verification cannot overwrite a later committed high water version", delegate {
                using (var f = new WindowsChannelFixture()) using (var olderEntered = new ManualResetEventSlim()) using (var releaseOlder = new ManualResetEventSlim()) {
                    var ledger = new WindowsChannelHighWater(f.Channel); var older = f.Offer(f.Text("0.3.0.0", f.Seconds - 40)); var newer = f.Offer(f.Text("0.4.0.0", f.Seconds - 20)); int olderThread = 0;
                    f.Archive.Package.CertificateTrust.During = () => { if (Thread.CurrentThread.ManagedThreadId == olderThread) { olderEntered.Set(); Check(releaseOlder.Wait(TimeSpan.FromSeconds(20)), "Concurrent channel fixture release timed out"); } };
                    Task delayed = Task.Run(() => { olderThread = Thread.CurrentThread.ManagedThreadId; Throws<SafetyException>(() => ledger.Accept(older, CancellationToken.None)); });
                    try { Check(olderEntered.Wait(TimeSpan.FromSeconds(20)), "Older fresh verification did not begin"); ledger.Accept(newer, CancellationToken.None); } finally { releaseOlder.Set(); }
                    Check(delayed.Wait(TimeSpan.FromSeconds(20)), "Delayed replay check did not finish"); f.Archive.Package.CertificateTrust.During = null; ledger.Accept(newer, CancellationToken.None); Throws<SafetyException>(() => ledger.Accept(older, CancellationToken.None));
                }
            });
        }
    }
}
