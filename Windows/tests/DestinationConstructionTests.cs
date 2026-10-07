using System;
using System.IO;
using System.Linq;

namespace EasyEject {
    public static partial class Tests {
        sealed class ConstructionStampDiscovery : IDiscovery, IScopedConnectionDiscovery {
            internal readonly ScopedDiscovery Inner;
            internal Action BeforeStamp;
            internal ConstructionStampDiscovery(ScopedDiscovery inner) { Inner = inner; }
            public Inventory Scan() { return Inner.Scan(); }
            public long Epoch { get { return Inner.Epoch; } }
            public string Stamp(string[] instances) { var callback = BeforeStamp; BeforeStamp = null; if (callback != null) callback(); return Inner.Stamp(instances); }
        }
        static void CheckConstructionReplacement(Fixture fixture, bool changed) {
            Check(changed && Directory.Exists(fixture.Destination + "-moved") && File.ReadAllText(Path.Combine(fixture.Destination, "foreign.keep")) == "retain replacement", "Guard construction adopted or changed replacement data");
            string replacement = fixture.Destination + "-replacement";
            Check(SafePaths.Within(replacement, fixture.Root) && replacement != fixture.Root, "Construction retry escaped owned root");
            Directory.Move(fixture.Destination, replacement); Directory.Move(fixture.Destination + "-moved", fixture.Destination);
            Check(File.ReadAllText(Path.Combine(replacement, "foreign.keep")) == "retain replacement", "Guard construction retry removed foreign data");
        }
        static void DestinationConstructionTests() {
            Test("destination guard captures root before initial inventory callback", () => {
                using (var fixture = new Fixture()) {
                    var discovery = new ScopedDiscovery(fixture); var expected = DestinationIdentity.Capture(discovery.Scan(), "destination-volume"); bool changed = false;
                    discovery.AfterScan = () => { if (!changed) { changed = true; ReplaceFixtureDirectory(fixture, fixture.Destination); } };
                    Throws<SafetyException>(() => new WindowsDestinationGuard(discovery, fixture.Destination, expected, path => "destination-volume"));
                    CheckConstructionReplacement(fixture, changed); discovery.AfterScan = null;
                    new WindowsDestinationGuard(discovery, fixture.Destination, expected, path => "destination-volume").Validate();
                }
            });
            Test("destination guard volume resolution cannot adopt a replaced empty root", () => {
                using (var fixture = new Fixture()) {
                    var discovery = new ScopedDiscovery(fixture); var expected = DestinationIdentity.Capture(discovery.Scan(), "destination-volume"); bool changed = false;
                    Throws<SafetyException>(() => new WindowsDestinationGuard(discovery, fixture.Destination, expected, path => { if (!changed) { changed = true; ReplaceFixtureDirectory(fixture, fixture.Destination); } return "destination-volume"; }));
                    CheckConstructionReplacement(fixture, changed);
                    new WindowsDestinationGuard(discovery, fixture.Destination, expected, path => "destination-volume").Validate();
                }
            });
            Test("destination guard connection proof cannot replace its construction root", () => {
                using (var fixture = new Fixture()) {
                    var inner = new ScopedDiscovery(fixture); var expected = DestinationIdentity.Capture(inner.Scan(), "destination-volume"); bool changed = false;
                    var discovery = new ConstructionStampDiscovery(inner) { BeforeStamp = () => { changed = true; ReplaceFixtureDirectory(fixture, fixture.Destination); } };
                    Throws<SafetyException>(() => new WindowsDestinationGuard(discovery, fixture.Destination, expected, path => "destination-volume"));
                    CheckConstructionReplacement(fixture, changed);
                    new WindowsDestinationGuard(discovery, fixture.Destination, expected, path => "destination-volume").Validate();
                }
            });
            Test("destination guard protection uses its private identity after caller mutation", () => {
                using (var fixture = new Fixture()) {
                    var discovery = new ScopedDiscovery(fixture); var expected = DestinationIdentity.Capture(discovery.Scan(), "destination-volume"); bool changed = false;
                    discovery.AfterScan = () => { if (!changed) { changed = true; expected.VolumeId = "other-volume"; expected.VolumeSerial = "caller changed"; } };
                    var guard = new WindowsDestinationGuard(discovery, fixture.Destination, expected, path => "destination-volume");
                    Check(changed && guard.ProtectedDisks.SequenceEqual(new[] { 701 }), "Caller mutation changed the protected physical disk selection");
                    discovery.AfterScan = null; guard.Validate();
                    discovery.Event("other-usb"); guard.Validate();
                    discovery.Event("destination-disk"); Throws<SafetyException>(() => guard.Validate());
                }
            });
            Test("destination guard construction permits ordinary root contents and releases native leases", () => {
                using (var fixture = new Fixture()) {
                    var discovery = new ScopedDiscovery(fixture); var expected = DestinationIdentity.Capture(discovery.Scan(), "destination-volume"); bool wrote = false;
                    discovery.AfterScan = () => { if (!wrote) { wrote = true; File.WriteAllText(Path.Combine(fixture.Destination, "ordinary.keep"), "retain ordinary data"); } };
                    var guard = new WindowsDestinationGuard(discovery, fixture.Destination, expected, path => { Directory.SetLastWriteTimeUtc(fixture.Destination, DateTime.UtcNow.AddMinutes(-1)); return "destination-volume"; });
                    guard.Validate(); Check(wrote && File.ReadAllText(Path.Combine(fixture.Destination, "ordinary.keep")) == "retain ordinary data", "Guard rejected ordinary root changes");
                    string moved = fixture.Destination + "-released"; Check(SafePaths.Within(moved, fixture.Root), "Construction release escaped owned root");
                    Directory.Move(fixture.Destination, moved); Directory.CreateDirectory(fixture.Destination);
                    Check(File.ReadAllText(Path.Combine(moved, "ordinary.keep")) == "retain ordinary data", "Released construction changed ordinary data");
                }
            });
        }
    }
}
