using System;
using System.Collections.Generic;
using System.Linq;

namespace EasyEject {
    public static partial class Tests {
        // Real code-owned vendor pins; synthetic authenticated release metadata.
        // No real package cache, publisher chain, component loading or device access.
        static ModernManifestFixture PinnedVendorManifestFixture() {
            var fixture = new ModernManifestFixture();
            fixture.Files.RemoveAll(file => !ApprovedModernWindowsComponents.IsApplicationFile(file.Name));
            fixture.Files.AddRange(ReviewedMicrosoftVendorLayout.Files);
            return fixture;
        }
        static void ReviewedMicrosoftVendorLayoutTests() {
            Test("code-owned local vendor layout binds complete runtime resources metadata and notices", delegate {
                var fixture = PinnedVendorManifestFixture(); var approval = ReviewedMicrosoftVendorLayout.CreateLocalReviewApproval();
                approval.Require(fixture.Parse());
                Check(ReviewedMicrosoftVendorLayout.Files.Count == 639 && ReviewedMicrosoftVendorLayout.Files.Count(file => file.Name.StartsWith("ThirdPartyNotices/", StringComparison.Ordinal)) == 48, "The independently derived vendor/notice set differs");
                Check(approval.SourceEvidenceHash == ReviewedMicrosoftVendorLayout.SourceDossierHash && approval.DotNet == "10.0.12" && approval.WindowsAppSdk == "2.5.1" && approval.MinimumWindows == "10.0.19041.0", "Local pins lost their independent source/runtime binding");
                Check(approval.CatalogHash == ReviewedMicrosoftVendorLayout.CreateLocalReviewApproval().CatalogHash, "Local review approval is not deterministic");
                Check(WindowsReleaseChecks.ModernProductionChannel == null && WindowsReleaseChecks.ModernProductionComponents == null, "Local vendor pins enabled production review");
            });
            Test("fresh authenticated inventory cannot replace a pinned native resource localization or notice", delegate {
                var approval = ReviewedMicrosoftVendorLayout.CreateLocalReviewApproval();
                foreach (string name in new[] { "coreclr.dll", "Microsoft.UI.Xaml.Controls.pri", "af-ZA/Microsoft.ui.xaml.dll.mui", "ThirdPartyNotices/Microsoft.NETCore.App.Runtime.win-x64-10.0.12/LICENSE.TXT" }) {
                    foreach (bool hashChange in new[] { false, true }) {
                        var fixture = PinnedVendorManifestFixture(); int index = fixture.Files.FindIndex(file => file.Name == name); Check(index >= 0, "Expected pinned class absent"); var row = fixture.Files[index];
                        fixture.Files[index] = new WindowsReleaseFile(row.Name, hashChange ? row.Length : row.Length + 1, hashChange ? new string('e',64) : row.Hash);
                        Throws<SafetyException>(() => approval.Require(fixture.Parse()));
                    }
                }
                var restored = PinnedVendorManifestFixture(); approval.Require(restored.Parse());
            });
            Test("local pin set refuses missing renamed or additional vendor publication paths", delegate {
                var approval = ReviewedMicrosoftVendorLayout.CreateLocalReviewApproval();
                string notice = ReviewedMicrosoftVendorLayout.Files.First(file => file.Name.EndsWith("PACKAGE-METADATA.txt", StringComparison.Ordinal)).Name;
                var missing = PinnedVendorManifestFixture(); missing.Files.RemoveAll(file => file.Name == notice); Throws<SafetyException>(() => approval.Require(missing.Parse()));
                var renamed = PinnedVendorManifestFixture(); int index = renamed.Files.FindIndex(file => file.Name == notice); var row = renamed.Files[index]; renamed.Files[index] = new WindowsReleaseFile(row.Name.Replace("PACKAGE-METADATA", "package-metadata"),row.Length,row.Hash); Throws<SafetyException>(() => approval.Require(renamed.Parse()));
                foreach (string extra in new[] { "unexpected.dll", "README-REVIEW.txt", "ThirdPartyNotices/unreviewed/LICENSE.txt" }) {
                    var added = PinnedVendorManifestFixture(); added.Files.Add(new WindowsReleaseFile(extra,1,new string('e',64))); Throws<SafetyException>(() => approval.Require(added.Parse()));
                }
            });
            Test("local vendor data remains frozen and does not approve app bytes or changed runtime families", delegate {
                var approval = ReviewedMicrosoftVendorLayout.CreateLocalReviewApproval(); string hash = approval.CatalogHash;
                Throws<NotSupportedException>(() => ((IList<WindowsReleaseFile>)ReviewedMicrosoftVendorLayout.Files).Clear());
                var fixture = PinnedVendorManifestFixture(); int index = fixture.Files.FindIndex(file => file.Name == "EasyEject.Runtime.dll"); fixture.Files[index] = new WindowsReleaseFile("EasyEject.Runtime.dll",2,new string('e',64)); approval.Require(fixture.Parse());
                fixture.DotNet = "10.0.13"; Throws<SafetyException>(() => approval.Require(fixture.Parse())); fixture.DotNet = "10.0.12";
                fixture.Sdk = "2.5.2"; Throws<SafetyException>(() => approval.Require(fixture.Parse()));
                Check(hash == approval.CatalogHash, "Caller changed frozen local approval");
            });
        }
    }
}
