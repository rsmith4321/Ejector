using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace EasyEject {
    public static partial class Tests {
        sealed class ModernChannelFixture : IDisposable {
            internal readonly ModernArchiveFixture Archive = new ModernArchiveFixture();
            internal readonly ApprovedWindowsChannel Channel; internal DateTime Now = DateTime.UtcNow;
            internal long Seconds { get { return (Now.Ticks - new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc).Ticks) / TimeSpan.TicksPerSecond; } }
            internal string History { get { return Path.Combine(Archive.Package.Owned.Destination, WindowsReleaseChecks.ModernHistoryDirectory); } }
            internal ModernChannelFixture() {
                Archive.Package.Metadata.Version = "0.1.0.0"; Archive.Package.Publish(); Archive.Write();
                Channel = ApprovedWindowsChannel.Modern("https://example.invalid/modern/channel.json", "https://example.invalid/modern/channel.json.p7s", "https://downloads.example.invalid/modern/releases/", Archive.Package.Signing.Publisher());
            }
            internal string Text(string version = "0.1.0.0", long? issued = null, long? expires = null) {
                string name = "EasyEject.Modern-" + version + "-x64.zip";
                return "{\"format\":2,\"kind\":\"windows-modern-channel\",\"application\":\"EasyEject.Modern\",\"platform\":\"windows-x64\",\"channel\":\"stable\",\"issuedUtc\":" + (issued ?? Seconds - 60) + ",\"expiresUtc\":" + (expires ?? Seconds + 3600) + ",\"release\":{\"version\":\"" + version + "\",\"sourceRevision\":\"" + Archive.Package.Metadata.Source + "\",\"manifestSha256\":\"" + Hash(ModernWindowsReleaseManifest.ManifestName) + "\",\"manifestSignatureSha256\":\"" + Hash(ModernWindowsReleaseManifest.SignatureName) + "\",\"certificateSha256\":\"" + Archive.Package.Signing.Evidence().CertificateHash + "\",\"archive\":{\"name\":\"" + name + "\",\"url\":\"" + Channel.Archives.AbsoluteUri + version + "/" + name + "\",\"length\":" + new FileInfo(Archive.Path).Length + ",\"sha256\":\"" + WindowsSignerEvidence.Hash(File.ReadAllBytes(Archive.Path)) + "\"}}}";
            }
            internal string Hash(string name) { return WindowsSignerEvidence.Hash(File.ReadAllBytes(Archive.Package.PathFor(name))); }
            internal WindowsChannelOffer Offer(string text = null, CancellationToken cancel = default(CancellationToken), IWindowsManifestEnvelopeTrust trust = null, ApprovedWindowsChannel channel = null) {
                byte[] body = Encoding.UTF8.GetBytes(text ?? Text()); return new WindowsChannelOffer(body, Archive.Package.Signing.Sign(body), channel ?? Channel, trust ?? new WindowsManifestTrust(Archive.Package.Certificate), () => Now, cancel);
            }
            internal ChannelTransportFixture Transport(string text = null) {
                byte[] body = Encoding.UTF8.GetBytes(text ?? Text()), signature = Archive.Package.Signing.Sign(body);
                return new ChannelTransportFixture { Fetch = (channel, resource, cancel) => { Check(channel.Same(Channel), "Transport received another approved policy"); return Task.FromResult(resource == WindowsChannelResource.Metadata ? body : signature); } };
            }
            internal WindowsUpdateCoordinator Coordinator(string text = null) { return WindowsReleaseChecks.CreateModern(Channel, () => Archive.Package.Owned.Destination, Transport(text), new WindowsManifestTrust(Archive.Package.Certificate), () => Now, false); }
            public void Dispose() { Archive.Dispose(); }
        }
        static void ModernWindowsChannelTests() {
            Test("modern signed channel binds the modern archive manifest inventory and signer", delegate {
                using (var f = new ModernChannelFixture()) using (var archive = f.Archive.Review()) {
                    var offer = f.Offer(); offer.VerifyArchive(archive, CancellationToken.None);
                    Check(offer.Release.Version == archive.Release.Version && offer.Release.SourceRevision == archive.Release.SourceRevision && offer.Release.ArchiveName == "EasyEject.Modern-0.1.0.0-x64.zip" && offer.Release.ManifestHash == archive.Authenticated.BodyHash, "Modern binding lost identity");
                    Check(offer.Release.Compare("0.1.0-dev") == WindowsReleaseComparison.UncomparableLocalVersion, "Development app became current");
                }
            });
            Test("modern policy is distinct even with identical endpoints and publisher pins", delegate {
                using (var f = new ModernChannelFixture()) {
                    var c = f.Channel; var classic = new ApprovedWindowsChannel(c.Metadata.AbsoluteUri, c.Signature.AbsoluteUri, c.Archives.AbsoluteUri, c.Publisher);
                    Check(!c.Same(classic) && !classic.Same(c) && c.Application == "EasyEject.Modern" && classic.Application == "EasyEject.Windows", "Policies inherited application approval");
                    Check(WindowsChannelReplayFloor.Policy(c) != WindowsChannelReplayFloor.Policy(classic), "Replay policy omits application");
                    Check(WindowsChannelReplayFloor.Policy(classic) == WindowsChannelReplayFloor.IdentityHash("EasyEject.Windows/channel-replay/policy/v1", c.Metadata.AbsoluteUri, c.Signature.AbsoluteUri, c.Archives.AbsoluteUri, c.Publisher.SubjectHash, c.Publisher.IssuerCertificateHash), "Classic saved policy changed");
                    Throws<SafetyException>(() => f.Offer(channel: classic));
                    string old = f.Text().Replace("\"format\":2", "\"format\":1").Replace("windows-modern-channel", "windows-channel").Replace("EasyEject.Modern", "EasyEject.Windows");
                    Throws<SafetyException>(() => f.Offer(old)); Check(f.Offer(old, channel: classic).IsFor(classic), "Classic format control failed");
                }
            });
            Test("modern endpoint approval retains canonical HTTPS paired resources and archive base", delegate {
                using (var f = new ModernChannelFixture()) foreach (string bad in new[] { "http://example.invalid/channel.json", "https://user@example.invalid/channel.json", "https://example.invalid/channel.json?q=1", "https://example.invalid/%63hannel.json", "https://localhost/channel.json" })
                    Throws<SafetyException>(() => ApprovedWindowsChannel.Modern(bad, bad + ".p7s", f.Channel.Archives.AbsoluteUri, f.Channel.Publisher));
            });
            Test("modern archive location refuses classic filename alternate origins and version aliases", delegate {
                using (var f = new ModernChannelFixture()) {
                    string good = f.Channel.Archives.AbsoluteUri + "0.1.0.0/EasyEject.Modern-0.1.0.0-x64.zip";
                    foreach (string bad in new[] { good.Replace("downloads.example.invalid", "foreign.invalid"), good.Replace("https:", "http:"), good + "?x=1", good.Replace("/0.1.0.0/", "/0.2.0.0/"), good.Replace(".zip", "%2ezip"), good.Replace("EasyEject.Modern", "EasyEject.Windows") }) Throws<SafetyException>(() => f.Offer(f.Text().Replace(good, bad)));
                    Throws<SafetyException>(() => f.Offer(f.Text().Replace("EasyEject.Modern-", "EasyEject.Windows-")));
                }
            });
            Test("modern cancelled or unapproved admission invokes neither native trust nor clock", delegate {
                using (var f = new ModernChannelFixture()) using (var cancel = new CancellationTokenSource()) {
                    int clocks = 0; var trust = new MutatingEnvelopeTrust(); Throws<SafetyException>(() => new WindowsChannelOffer(new byte[1], new byte[1], null, trust, () => { clocks++; return f.Now; }, CancellationToken.None));
                    cancel.Cancel(); Throws<OperationCanceledException>(() => f.Offer(cancel: cancel.Token)); Check(clocks == 0 && trust.Calls == 0 && f.Archive.Package.Certificate.Calls == 0, "Admission invoked native work");
                }
            });
            Test("modern channel rejects schema types duplicates trailing JSON and invalid identity bounds", delegate {
                using (var f = new ModernChannelFixture()) {
                    string text = f.Text(); foreach (string bad in new[] { text.Insert(1, "\"format\":2,"), text.Insert(1, "\"foreign\":1,"), text.Replace("\"format\":2", "\"format\":\"2\""), text + "{}", text.Replace("stable", "beta"), text.Replace("windows-x64", "macos-x64"), text.Replace(f.Archive.Package.Metadata.Source, "unknown"), text.Replace("\"length\":" + new FileInfo(f.Archive.Path).Length, "\"length\":2147483649"), text.Replace("0.1.0.0", "0.1.0-dev") }) Throws<SafetyException>(() => f.Offer(bad));
                }
            });
            Test("modern channel time rules expire metadata before later archive work", delegate {
                using (var f = new ModernChannelFixture()) {
                    foreach (long[] dates in new[] { new[] { f.Seconds - 60, f.Seconds }, new[] { f.Seconds + 301, f.Seconds + 3600 }, new[] { f.Seconds - 1, f.Seconds + WindowsChannelOffer.MaximumLifetime } }) Throws<SafetyException>(() => f.Offer(f.Text(issued: dates[0], expires: dates[1])));
                    var offer = f.Offer(f.Text(expires: f.Seconds + 60)); using (var archive = f.Archive.Review()) { f.Now = f.Now.AddMinutes(2); int calls = f.Archive.Package.Certificate.Calls; Throws<SafetyException>(() => offer.VerifyArchive(archive, CancellationToken.None)); Check(f.Archive.Package.Certificate.Calls == calls, "Expired channel began later trust work"); }
                }
            });
            Test("modern exact archive binding refuses every changed signed artifact field", delegate {
                using (var f = new ModernChannelFixture()) using (var archive = f.Archive.Review()) {
                    string text = f.Text(); foreach (string changed in new[] { text.Replace(archive.Hash, new string('a', 64)), text.Replace("\"length\":" + archive.Length, "\"length\":" + (archive.Length + 1)), text.Replace(archive.Authenticated.BodyHash, new string('a', 64)), text.Replace(archive.Authenticated.SignatureHash, new string('a', 64)), text.Replace(archive.Authenticated.Signer.CertificateHash, new string('a', 64)), text.Replace(archive.Release.SourceRevision, new string('a', 40)), f.Text("0.2.0.0") }) {
                        var offer = f.Offer(changed); Check(Throws<SafetyException>(() => offer.VerifyArchive(archive, CancellationToken.None)).Message.Contains("does not match"), "Different signed artifact accepted");
                    }
                }
            });
            Test("modern channel binds ZIP encoding as well as identical inventory contents", delegate {
                using (var f = new ModernChannelFixture()) { var offer = f.Offer(); f.Archive.Write(stored: true); using (var archive = f.Archive.Review()) Throws<SafetyException>(() => offer.VerifyArchive(archive, CancellationToken.None)); }
            });
            Test("classic and modern channels refuse each other's archive reviewers before trust", delegate {
                using (var f = new ModernChannelFixture()) using (var classic = new WindowsArchiveFixture()) using (var classicArchive = classic.Review()) using (var modernArchive = f.Archive.Review()) {
                    var modern = f.Offer(); var c = f.Channel; var policy = new ApprovedWindowsChannel(c.Metadata.AbsoluteUri, c.Signature.AbsoluteUri, c.Archives.AbsoluteUri, c.Publisher);
                    var old = f.Offer(f.Text().Replace("\"format\":2", "\"format\":1").Replace("windows-modern-channel", "windows-channel").Replace("EasyEject.Modern", "EasyEject.Windows"), channel: policy);
                    int count = f.Archive.Package.Certificate.Calls; Throws<SafetyException>(() => modern.VerifyArchive(classicArchive, CancellationToken.None)); Throws<SafetyException>(() => old.VerifyArchive(modernArchive, CancellationToken.None)); Check(count == f.Archive.Package.Certificate.Calls, "Cross-schema action reached trust");
                }
            });
            Test("modern channel freezes input arrays and refuses retargeted native authentication", delegate {
                using (var f = new ModernChannelFixture()) {
                    byte[] body = Encoding.UTF8.GetBytes(f.Text()), signature = f.Archive.Package.Signing.Sign(body); f.Archive.Package.Certificate.During = () => { body[0] = 0; signature[0] = 0; };
                    var offer = new WindowsChannelOffer(body, signature, f.Channel, new WindowsManifestTrust(f.Archive.Package.Certificate), () => f.Now, CancellationToken.None); offer.Revalidate(CancellationToken.None);
                    f.Archive.Package.Certificate.During = null; var trust = new MutatingEnvelopeTrust { Actual = new WindowsManifestTrust(f.Archive.Package.Certificate), Retarget = true }; Throws<SafetyException>(() => f.Offer(trust: trust));
                    f.Archive.Package.Certificate.Throw = true; Throws<SafetyException>(() => offer.Revalidate(CancellationToken.None));
                }
            });
            Test("modern process high water retains rollback equivocation and immutable-release restrictions", delegate {
                using (var f = new ModernChannelFixture()) {
                    var ledger = new WindowsChannelHighWater(f.Channel); ledger.Accept(f.Offer(f.Text("0.2.0.0", f.Seconds - 20)), CancellationToken.None);
                    foreach (string bad in new[] { f.Text("0.3.0.0", f.Seconds - 30), f.Text("0.1.0.0", f.Seconds - 10), f.Text("0.2.0.0", f.Seconds - 20, f.Seconds + 4000), f.Text("0.2.0.0", f.Seconds - 10).Replace(f.Hash(ModernWindowsReleaseManifest.ManifestName), new string('a', 64)) }) Throws<SafetyException>(() => ledger.Accept(f.Offer(bad), CancellationToken.None));
                    ledger.Accept(f.Offer(f.Text("0.2.0.0", f.Seconds - 10)), CancellationToken.None);
                }
            });
            Test("modern user-bound replay survives restart and refuses rollback without a cached offer", delegate {
                using (var f = new ModernChannelFixture()) {
                    var high = f.Offer(f.Text("0.2.0.0", f.Seconds - 20)); new WindowsChannelReplayStore(f.History).Accept(f.Channel, high, CancellationToken.None, null);
                    string path = Path.Combine(f.History, WindowsChannelReplayStore.FileName), hash = WindowsSignerEvidence.Hash(File.ReadAllBytes(path));
                    Throws<SafetyException>(() => new WindowsChannelReplayStore(f.History).Accept(f.Channel, f.Offer(f.Text("0.1.0.0", f.Seconds - 10)), CancellationToken.None, null)); Check(WindowsSignerEvidence.Hash(File.ReadAllBytes(path)) == hash, "Restart rollback changed state");
                }
            });
            Test("modern replay refuses classic state without rewriting or automatically resetting it", delegate {
                using (var f = new ModernChannelFixture()) using (var old = new WindowsChannelFixture()) {
                    string root = f.History; new WindowsChannelReplayStore(root).Accept(old.Channel, old.Offer(), CancellationToken.None, null);
                    string path = Path.Combine(root, WindowsChannelReplayStore.FileName), hash = WindowsSignerEvidence.Hash(File.ReadAllBytes(path));
                    Throws<SafetyException>(() => new WindowsChannelReplayStore(root).Accept(f.Channel, f.Offer(), CancellationToken.None, null)); Check(WindowsSignerEvidence.Hash(File.ReadAllBytes(path)) == hash, "Foreign policy was reset");
                }
            });
            Test("modern saved floor permits renewal but refuses same-version release substitution", delegate {
                using (var f = new ModernChannelFixture()) {
                    new WindowsChannelReplayStore(f.History).Accept(f.Channel, f.Offer(f.Text(issued: f.Seconds - 50)), CancellationToken.None, null);
                    new WindowsChannelReplayStore(f.History).Accept(f.Channel, f.Offer(f.Text(issued: f.Seconds - 20)), CancellationToken.None, null);
                    string path = Path.Combine(f.History, WindowsChannelReplayStore.FileName), hash = WindowsSignerEvidence.Hash(File.ReadAllBytes(path));
                    Throws<SafetyException>(() => new WindowsChannelReplayStore(f.History).Accept(f.Channel, f.Offer(f.Text(issued: f.Seconds - 10).Replace(f.Hash(ModernWindowsReleaseManifest.ManifestName), new string('a', 64))), CancellationToken.None, null)); Check(WindowsSignerEvidence.Hash(File.ReadAllBytes(path)) == hash, "Immutable release was replaced");
                }
            });
            Test("modern backup recovery restores only its policy and retains backup and neighboring records", delegate {
                using (var f = new ModernChannelFixture()) {
                    new WindowsChannelReplayStore(f.History).Accept(f.Channel, f.Offer(), CancellationToken.None, null);
                    string original = Path.Combine(f.History, WindowsChannelReplayStore.FileName), backupRoot = Path.Combine(f.Archive.Package.Owned.Destination, "backups"), restored = Path.Combine(f.Archive.Package.Owned.Destination, "restored-modern"); Directory.CreateDirectory(backupRoot); Directory.CreateDirectory(restored);
                    string backup = Path.Combine(backupRoot, WindowsChannelReplayStore.FileName), neighbor = Path.Combine(restored, "app-settings.json"); File.Copy(original, backup); File.WriteAllText(neighbor, "retained neighboring fixture");
                    var store = new WindowsChannelReplayStore(restored); using (var review = store.ReviewBackup(f.Channel, backup, CancellationToken.None)) Check(store.RestoreBackup(review, CancellationToken.None), "Exact backup was not restored");
                    Check(File.ReadAllBytes(original).SequenceEqual(File.ReadAllBytes(Path.Combine(restored, WindowsChannelReplayStore.FileName))) && File.ReadAllBytes(original).SequenceEqual(File.ReadAllBytes(backup)) && File.ReadAllText(neighbor) == "retained neighboring fixture", "Recovery changed unrelated data");
                    var c = f.Channel; var classic = new ApprovedWindowsChannel(c.Metadata.AbsoluteUri, c.Signature.AbsoluteUri, c.Archives.AbsoluteUri, c.Publisher); Throws<SafetyException>(() => new WindowsChannelReplayStore(restored).ReviewBackup(classic, backup, CancellationToken.None));
                }
            });
            Test("modern unconfigured factory is lazy and refuses classic approval", delegate {
                using (var f = new ModernChannelFixture()) {
                    int roots = 0; var transport = f.Transport(); using (var c = WindowsReleaseChecks.CreateModern(null, () => { roots++; return f.History; }, transport, new WindowsManifestTrust(f.Archive.Package.Certificate), () => f.Now, true)) {
                        c.Start(); c.PublishIdle(true); Check(UpdatePulse(c) == null && !c.Configured, "Unconfigured factory checked"); Throws<SafetyException>(() => UpdateManual(c)); Check(roots == 0 && transport.Metadata == 0 && transport.Signature == 0, "Unconfigured factory touched storage/network");
                    }
                    var p = f.Channel; var classic = new ApprovedWindowsChannel(p.Metadata.AbsoluteUri, p.Signature.AbsoluteUri, p.Archives.AbsoluteUri, p.Publisher); Throws<SafetyException>(() => WindowsReleaseChecks.CreateModern(classic, () => { roots++; return f.History; }, transport, new WindowsManifestTrust(f.Archive.Package.Certificate), () => f.Now, false));
                    Check(roots == 0 && WindowsReleaseChecks.ModernProductionChannel == null && WindowsReleaseChecks.ProductionChannel == null, "Approval installed production or accessed root");
                }
            });
            Test("modern configured checks persist separate history without changing classic or app settings", delegate {
                using (var f = new ModernChannelFixture()) {
                    string parent = f.Archive.Package.Owned.Destination, classic = Path.Combine(parent, WindowsChannelReplayStore.FileName), settings = Path.Combine(parent, "app-settings.json"); File.WriteAllText(classic, "retained classic fixture"); File.WriteAllText(settings, "retained settings fixture");
                    using (var c = f.Coordinator()) { c.Start(); c.PublishIdle(true); var result = UpdateManual(c); Check(result.Outcome == WindowsUpdateCheckOutcome.Accepted && c.TryPresent(result) && !c.TryPresent(result), "Configured check did not return one-use metadata"); }
                    Check(File.Exists(Path.Combine(f.History, WindowsChannelReplayStore.FileName)) && File.ReadAllText(classic) == "retained classic fixture" && File.ReadAllText(settings) == "retained settings fixture", "Modern factory altered neighboring stores");
                    using (var c = f.Coordinator()) { c.Start(); c.PublishIdle(true); Check(UpdateManual(c).Outcome == WindowsUpdateCheckOutcome.Accepted, "Configured restart did not reread exact retained state"); }
                }
            });
            Test("modern factory restart reports rollback failure and preserves restrictive history", delegate {
                using (var f = new ModernChannelFixture()) {
                    using (var c = f.Coordinator(f.Text("0.2.0.0", f.Seconds - 20))) { c.Start(); c.PublishIdle(true); Check(UpdateManual(c).Outcome == WindowsUpdateCheckOutcome.Accepted, "Initial high floor failed"); }
                    string path = Path.Combine(f.History, WindowsChannelReplayStore.FileName), hash = WindowsSignerEvidence.Hash(File.ReadAllBytes(path));
                    using (var c = f.Coordinator(f.Text("0.1.0.0", f.Seconds - 10))) { c.Start(); c.PublishIdle(true); var result = UpdateManual(c); Check(result.Outcome == WindowsUpdateCheckOutcome.Failed && result.Offer == null && result.Message.Contains("rolled back"), "Factory delivered rollback offer"); }
                    Check(WindowsSignerEvidence.Hash(File.ReadAllBytes(path)) == hash, "Failed check reset history");
                }
            });
            Test("modern scope loss during native trust revokes delivery and saves no history", delegate {
                using (var f = new ModernChannelFixture()) using (var c = f.Coordinator()) {
                    c.Start(); c.PublishIdle(true); f.Archive.Package.Certificate.During = () => c.PublishIdle(false); var result = UpdateManual(c);
                    Check(result.Outcome == WindowsUpdateCheckOutcome.Cancelled && result.Offer == null && !c.TryPresent(result) && !File.Exists(Path.Combine(f.History, WindowsChannelReplayStore.FileName)), "Lost activity accepted or persisted offer");
                }
            });
            Test("classic release presenter refuses a configured modern channel instead of substituting classic package policy", delegate {
                using (var f = new ModernChannelFixture()) using (var c = f.Coordinator()) Throws<SafetyException>(() => new WindowsReleaseReviewSession(c, f.Channel));
            });
        }
        static int ModernChannelApplicationSample(string folder) {
            byte[] host = File.ReadAllBytes(Path.Combine(folder, "EasyEject.Modern.exe")), managed = File.ReadAllBytes(Path.Combine(folder, "EasyEject.Modern.dll"));
            Test("modern signed channel composes exact archive and separately held application checks", delegate {
                using (var f = new ModernChannelFixture()) {
                    ApplicationPackageFixture(f.Archive.Package, host, managed); f.Archive.Write(); var offer = f.Offer();
                    using (var archive = f.Archive.Review()) using (var application = ApplicationPackage(f.Archive.Package)) offer.VerifyExtracted(archive, application, CancellationToken.None); f.Archive.Released();
                }
            });
            Test("modern signed channel refuses a separately valid changed extracted application snapshot", delegate {
                using (var f = new ModernChannelFixture()) {
                    ApplicationPackageFixture(f.Archive.Package, host, managed); f.Archive.Write(); var offer = f.Offer();
                    using (var archive = f.Archive.Review()) {
                        string name = "nested/fixture.json"; byte[] bytes = Encoding.UTF8.GetBytes("another valid snapshot"); File.WriteAllBytes(f.Archive.Package.PathFor(name), bytes);
                        f.Archive.Package.Metadata.Files.RemoveAll(file => file.Name == name); f.Archive.Package.Metadata.Files.Add(new WindowsReleaseFile(name, bytes.Length, WindowsSignerEvidence.Hash(bytes))); f.Archive.Package.Publish();
                        using (var application = ApplicationPackage(f.Archive.Package)) Throws<SafetyException>(() => offer.VerifyExtracted(archive, application, CancellationToken.None));
                    }
                    f.Archive.Released();
                }
            });
            Console.WriteLine(passed + " passed, " + failed + " failed"); return failed == 0 ? 0 : 1;
        }
    }
}
