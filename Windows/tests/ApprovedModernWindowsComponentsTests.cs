using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;

namespace EasyEject {
    public static partial class Tests {
        static ApprovedModernWindowsComponents ComponentApproval(ModernManifestFixture fixture, IEnumerable<WindowsReleaseFile> files = null) {
            return new ApprovedModernWindowsComponents(new string('f', 64), fixture.DotNet, fixture.Sdk, fixture.Minimum, files ?? fixture.Files.Where(file => !ApprovedModernWindowsComponents.IsApplicationFile(file.Name)));
        }
        static void ApprovedModernWindowsComponentsTests() {
            ReviewedMicrosoftVendorLayoutTests();
            Test("independent component approval freezes exact vendor membership lengths hashes and runtime identifiers", delegate {
                var f = new ModernManifestFixture(); var supplied = f.Files.Where(file => !ApprovedModernWindowsComponents.IsApplicationFile(file.Name)).ToList();
                var approval = ComponentApproval(f, supplied); approval.Require(f.Parse()); string hash = approval.CatalogHash;
                supplied.Clear(); approval.Require(f.Parse()); Check(approval.CatalogHash == hash && approval.SourceEvidenceHash == new string('f', 64), "Caller mutation changed approval");
                Check(ComponentApproval(f, f.Files.Where(file => !ApprovedModernWindowsComponents.IsApplicationFile(file.Name)).Reverse()).CatalogHash == hash, "Entry ordering changed canonical approval");
                foreach (string field in new[] { "DotNet", "Sdk", "Minimum" }) {
                    var changed = new ModernManifestFixture(); if (field == "DotNet") changed.DotNet = "10.0.13"; if (field == "Sdk") changed.Sdk = "2.5.2"; if (field == "Minimum") { Throws<SafetyException>(() => new ApprovedModernWindowsComponents(new string('f',64), f.DotNet, f.Sdk, "10.0.22000.0", supplied)); continue; }
                    Throws<SafetyException>(() => approval.Require(changed.Parse()));
                }
            });
            Test("fresh authenticated vendor inventory cannot approve its own altered bytes or additional files", delegate {
                var f = new ModernManifestFixture(); var approval = ComponentApproval(f);
                foreach (string change in new[] { "hash", "length", "case" }) {
                    var altered = new ModernManifestFixture(); int index = altered.Files.FindIndex(file => file.Name == "coreclr.dll"); var row = altered.Files[index];
                    altered.Files[index] = new WindowsReleaseFile(change == "case" ? "CORECLR.dll" : row.Name, change == "length" ? 2 : row.Length, change == "hash" ? new string('e', 64) : row.Hash);
                    // Required canonical names also reject case substitution.
                    Throws<SafetyException>(() => approval.Require(altered.Parse()));
                }
                foreach (string name in new[] { "foreign.dll", "foreign.winmd", "foreign.pri", "foreign.json", "fr-FR/foreign.mui", "foreign.txt" }) {
                    var altered = new ModernManifestFixture(); altered.Files.Add(new WindowsReleaseFile(name,1,new string('e',64))); Throws<SafetyException>(() => approval.Require(altered.Parse()));
                }
            });
            Test("approved component constructor refuses own code aliases unsafe entries omissions and unsupported sources", delegate {
                var f = new ModernManifestFixture(); var vendor = f.Files.Where(file => !ApprovedModernWindowsComponents.IsApplicationFile(file.Name)).ToArray();
                foreach (string name in new[] { "EasyEject.Runtime.dll", "EASYEJECT.RUNTIME.DLL", "EasyEject.Modern.pri", "../foreign.dll" }) {
                    var entries = vendor.Concat(new[] { new WindowsReleaseFile(name,1,new string('a',64)) });
                    Throws<SafetyException>(() => ComponentApproval(f, entries));
                }
                Throws<SafetyException>(() => ComponentApproval(f, vendor.Concat(new[] { new WindowsReleaseFile("CORECLR.dll",1,new string('a',64)) })));
                Throws<SafetyException>(() => ComponentApproval(f, vendor.Where(file => file.Name != "coreclr.dll")));
                Throws<SafetyException>(() => ComponentApproval(f, vendor.Concat(new[] { new WindowsReleaseFile("foreign.dll",1,new string('a',64)), new WindowsReleaseFile("FOREIGN.dll/child.dll",1,new string('a',64)) })));
                Throws<SafetyException>(() => ComponentApproval(f, vendor.Concat(Enumerable.Range(0,ModernWindowsReleaseManifest.MaximumFiles).Select(i => new WindowsReleaseFile("extra" + i + ".dll",1,new string('a',64))))));
                Throws<SafetyException>(() => ComponentApproval(f, vendor.Concat(Enumerable.Range(0,3).Select(i => new WindowsReleaseFile("large" + i + ".dll",ModernWindowsReleaseManifest.MaximumFile,new string('a',64))))));
                foreach (WindowsReleaseFile bad in new[] { null, new WindowsReleaseFile("foreign.dll",0,new string('a',64)), new WindowsReleaseFile("foreign.dll",ModernWindowsReleaseManifest.MaximumFile+1,new string('a',64)), new WindowsReleaseFile("foreign.dll",1,"invalid") }) Throws<SafetyException>(() => ComponentApproval(f,vendor.Concat(new[] {bad})));
                foreach (string source in new[] { null, "", "candidate.json", new string('g',64) }) Throws<SafetyException>(() => new ApprovedModernWindowsComponents(source,f.DotNet,f.Sdk,f.Minimum,vendor));
                foreach (string version in new[] { "9.0.12", "10.0.12.0", "10.0.12-dev", "010.0.12" }) Throws<SafetyException>(() => new ApprovedModernWindowsComponents(new string('f',64),version,f.Sdk,f.Minimum,vendor));
            });
            Test("catalog approval partitions application-owned payloads without approving their code identity", delegate {
                var f = new ModernManifestFixture(); var approval = ComponentApproval(f);
                int index = f.Files.FindIndex(file => file.Name == "EasyEject.Modern.dll"); f.Files[index] = new WindowsReleaseFile("EasyEject.Modern.dll",2,new string('e',64));
                approval.Require(f.Parse());
                Check(WindowsReleaseChecks.ModernProductionChannel == null && WindowsReleaseChecks.ModernProductionComponents == null, "Fixture approval leaked into production");
                Throws<SafetyException>(() => approval.Require((ModernWindowsReleaseManifest)null));
            });
            Test("held independent catalog refreshes publisher trust cancellation and disposable ownership", delegate {
                using (var fixture = new ModernPackageFixture()) {
                    var approval = FakeComponentApproval(fixture); var review = fixture.Review(); approval.Require(review,CancellationToken.None);
                    using (var cancel = new CancellationTokenSource()) { cancel.Cancel(); Throws<OperationCanceledException>(() => approval.Require(review,cancel.Token)); }
                    var original = fixture.Executable.Evidence;
                    fixture.Executable.Evidence = new WindowsSignerEvidence(original.CertificateHash,new string('1',64),original.IssuerCertificateHash,original.DigestOid);
                    Throws<SafetyException>(() => approval.Require(review,CancellationToken.None)); review.Dispose(); fixture.RequireReleased();
                    Throws<ObjectDisposedException>(() => approval.Require(review,CancellationToken.None));
                }
            });
            Test("unconfigured catalog refuses application review before accessing folders and preserves precancellation", delegate {
                using (var fixture = new ModernPackageFixture()) {
                    int calls = 0; fixture.Executable.During=(path,held)=>calls++; fixture.Certificate.During=()=>calls++;
                    string missing = Path.Combine(fixture.Owned.Destination,"absent");
                    Throws<SafetyException>(() => new ModernWindowsApplicationPackageReview(missing,fixture.Signing.Publisher(),fixture.Executable,new WindowsManifestTrust(fixture.Certificate),CancellationToken.None));
                    using (var cancel = new CancellationTokenSource()) { cancel.Cancel(); Throws<OperationCanceledException>(() => new ModernWindowsApplicationPackageReview(missing,fixture.Signing.Publisher(),fixture.Executable,new WindowsManifestTrust(fixture.Certificate),cancel.Token)); }
                    Check(calls == 0 && !Directory.Exists(missing),"Absent approval acquired file/trust access"); fixture.RequireReleased();
                }
            });
            Test("modern default release policy retains ZIP review but refuses extracted folder without component approval", delegate {
                using (var fixture = new ModernChannelFixture()) {
                    int archives = 0;
                    var policy = WindowsReleasePackagePolicy.Modern(fixture.Channel, (path,cancel) => { archives++; return new ModernWindowsArchiveReview(path,fixture.Channel.Publisher,new WindowsManifestTrust(fixture.Archive.Package.Certificate),cancel); }, null);
                    var offer = fixture.Offer(); var zip = policy.Review(offer,fixture.Archive.Path,null,false,CancellationToken.None,()=>{});
                    Check(zip.ArchivePath == fixture.Archive.Path, "Missing component approval blocked independent signed ZIP inspection");
                    Throws<SafetyException>(() => policy.Review(offer,fixture.Archive.Path,fixture.Archive.Package.Root,true,CancellationToken.None,()=>{}));
                    Check(archives == 2 && fixture.Archive.Package.Executable.Calls == 0, "Default folder policy reached candidate trust or skipped fresh archive"); fixture.Archive.Released();
                }
            });
        }
    }
}
