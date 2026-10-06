using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;

namespace EasyEject {
    public static partial class Tests {
        sealed class BulkEpochDiscovery : IDiscovery {
            internal readonly CardDiscovery Inner = new CardDiscovery(); internal bool Stale;
            public Inventory Scan() { return Inner.Scan(); }
            public long Epoch { get { return Inner.Epoch + (Stale ? 1 : 0); } }
        }
        static void BulkFinalTests() {
            Test("bulk final review retracts earlier reconnect while retaining still-absent later device", () => {
                var d = new CardDiscovery(); var plan = BulkEjectPolicy.Plan(d.State); var api = new CardApi { OnRequest = id => { d.Remove(id); if (id == "usb-b") { AddCard(d.State, 7, "a", StorageGroup.Camera); d.Changed("disk-a"); } return new EjectResult { Success = true }; } };
                var result = ExecuteCards(d, plan, api, new Operations());
                Check(!result.Success && result.Requested == 2 && result.Confirmed == 1 && api.Calls.Count == 2 && result.SafeDevices.SequenceEqual(new[] { String.Join("; ", plan.Targets[1].Affected) }), "Reconnected earlier device remained safe or later absence was concealed");
                Check(result.Message.Contains("changed") && d.State.Volumes.Any(v => v.Id == "volume-a"), "Changed-device explanation or retained reconnect missing");
            });
            Test("bulk later veto cannot retain earlier reconnect as a safe device", () => {
                var d = new CardDiscovery(); var api = new CardApi { OnRequest = id => { if (id == "usb-a") { d.Remove(id); return new EjectResult { Success = true }; } AddCard(d.State, 7, "a", StorageGroup.Camera); d.Changed("disk-a"); return new EjectResult { Code = 23, Veto = 6, Name = "fixture veto" }; } };
                var result = ExecuteCards(d, BulkEjectPolicy.Plan(d.State), api, new Operations());
                Check(!result.Success && result.Confirmed == 0 && result.SafeDevices.Count == 0 && api.Calls.Count == 2 && result.Message.Contains("fixture veto"), "Veto retained stale safe name or lost its reason");
            });
            Test("bulk final incomplete inventory cannot claim any earlier device safe", () => {
                var d = new CardDiscovery(); bool final = false; d.BeforeScan = number => { if (number == 8) { final = true; d.State.Complete = false; } }; var api = new CardApi { OnRequest = id => { d.Remove(id); return new EjectResult { Success = true }; } };
                var result = ExecuteCards(d, BulkEjectPolicy.Plan(d.State), api, new Operations());
                Check(final && !result.Success && result.Confirmed == 0 && result.SafeDevices.Count == 0 && api.Calls.Count == 2, "Missing/incomplete final inventory left safe-to-unplug claims");
            });
            Test("bulk empty confirmed plan performs no preparation cleanup callback API construction or wait", () => {
                var d = new CardDiscovery(); d.State.Volumes.Clear(); d.State.Disks.Clear(); var plan = BulkEjectPolicy.Plan(d.State); int prepare = 0, factories = 0, requests = 0, waits = 0; var operations = new Operations();
                var service = new BulkEjectService(d, d, operations, validate => { factories++; throw new Exception("Empty plan constructed removal adapter"); }, () => waits++, 2);
                var result = service.Execute(plan, _ => prepare++, (target, validate) => requests++, CancellationToken.None);
                Check(plan.Targets.Length == 0 && result.Success && result.Requested == 0 && result.Confirmed == 0 && prepare == 0 && factories == 0 && requests == 0 && waits == 0 && result.Message.Contains("No camera"), "Empty review executed device preparation or reported wrong result");
                using (operations.Reserve(new[] { 7, 8 })) { }
            });
            Test("bulk final scan exception clears provisional safe names and releases all reservations", () => {
                var d = new CardDiscovery(); var operations = new Operations(); bool final = false;
                d.BeforeScan = number => { if (number == 8) { final = true; Throws<SafetyException>(() => operations.Reserve(new[] { 7 })); Throws<SafetyException>(() => operations.Reserve(new[] { 8 })); throw new SafetyException("fixture final scan failed"); } };
                var api = new CardApi { OnRequest = id => { d.Remove(id); return new EjectResult { Success = true }; } };
                var result = ExecuteCards(d, BulkEjectPolicy.Plan(d.State), api, operations);
                Check(final && !result.Success && result.Confirmed == 0 && result.SafeDevices.Count == 0 && api.Calls.Count == 2 && result.Message.Contains("fixture final scan failed"), "Final scan failure left a safe claim or lost its diagnostic");
                using (operations.Reserve(new[] { 7, 8 })) { }
            });
            Test("bulk final stale scan refuses all safe names without another request", () => {
                var discovery = new BulkEpochDiscovery(); var d = discovery.Inner; d.BeforeScan = number => { if (number == 8) discovery.Stale = true; };
                var api = new CardApi { OnRequest = id => { d.Remove(id); return new EjectResult { Success = true }; } };
                var service = new BulkEjectService(discovery, d, new Operations(), validate => { api.Validate = validate; return api; }, () => {}, 2);
                var result = service.Execute(BulkEjectPolicy.Plan(d.State));
                Check(discovery.Stale && !result.Success && result.Confirmed == 0 && result.SafeDevices.Count == 0 && api.Calls.Count == 2 && result.Message.Contains("changed"), "Stale final scan reported safe or repeated removal");
            });
            foreach (var variant in new[] { "mount", "volume", "native target", "native ancestor", "disk instance" }) {
                string kind = variant;
                Test("bulk final review refuses a reused " + kind + " while retaining the other absent device", () => {
                    var d = new CardDiscovery(); var plan = BulkEjectPolicy.Plan(d.State);
                    d.BeforeScan = number => { if (number != 8) return; AddCard(d.State, 9, "c", StorageGroup.Camera); var disk = d.State.Disks.Single(); var volume = d.State.Volumes.Single();
                        if (kind == "mount") volume.Mounts[0] = "e:/";
                        else if (kind == "volume") volume.Id = "VOLUME-A";
                        else if (kind == "native target") disk.TargetId = "USB-A";
                        else if (kind == "native ancestor") disk.Ancestors.Add("USB-A");
                        else disk.InstanceId = "DISK-A";
                        d.Changed("usb-c");
                    };
                    var api = new CardApi { OnRequest = id => { d.Remove(id); return new EjectResult { Success = true }; } };
                    var result = ExecuteCards(d, plan, api, new Operations());
                    Check(!result.Success && result.Confirmed == 1 && result.SafeDevices.SequenceEqual(new[] { String.Join("; ", plan.Targets[1].Affected) }) && api.Calls.SequenceEqual(new[] { "usb-a", "usb-b" }) && d.State.Volumes.Count == 1, "Reused confirmed location or native identity retained a safe name, or new card was requested");
                });
            }
            Test("bulk final review permits a new unrelated card without adding it to confirmed actions", () => {
                var d = new CardDiscovery(); var plan = BulkEjectPolicy.Plan(d.State);
                d.BeforeScan = number => { if (number == 8) { AddCard(d.State, 9, "c", StorageGroup.Camera); d.Changed("usb-c"); } };
                var api = new CardApi { OnRequest = id => { d.Remove(id); return new EjectResult { Success = true }; } };
                var result = ExecuteCards(d, plan, api, new Operations());
                Check(result.Success && result.Requested == 2 && result.Confirmed == 2 && result.SafeDevices.Count == 2 && api.Calls.SequenceEqual(new[] { "usb-a", "usb-b" }) && d.State.Volumes.Single().Id == "volume-c", "Unrelated new card was included or invalidated absent confirmed devices");
            });
            Test("bulk later cancellation retains only freshly proved earlier absence and never requests remaining card", () => {
                var d = new CardDiscovery(); var operations = new Operations(); var plan = BulkEjectPolicy.Plan(d.State);
                using (var cancellation = new CancellationTokenSource()) {
                    var api = new CardApi { OnRequest = id => { d.Remove(id); return new EjectResult { Success = true }; } };
                    var service = new BulkEjectService(d, d, operations, validate => { api.Validate = validate; return api; }, () => {}, 2);
                    var result = service.Execute(plan, _ => {}, (target, validate) => { if (target.Id == "usb-b") cancellation.Cancel(); }, cancellation.Token);
                    Check(!result.Success && result.Confirmed == 1 && result.SafeDevices.SequenceEqual(new[] { String.Join("; ", plan.Targets[0].Affected) }) && api.Calls.SequenceEqual(new[] { "usb-a" }) && d.State.Volumes.Single().Id == "volume-b", "Cancelled operation retried or concealed freshly verified earlier absence");
                }
                using (operations.Reserve(new[] { 7, 8 })) { }
            });
            Test("bulk cancellation during final proof cannot return success or issue another request", () => {
                var d = new CardDiscovery(); using (var cancellation = new CancellationTokenSource()) {
                    d.BeforeScan = number => { if (number == 8) cancellation.Cancel(); };
                    var api = new CardApi { OnRequest = id => { d.Remove(id); return new EjectResult { Success = true }; } };
                    var service = new BulkEjectService(d, d, new Operations(), validate => { api.Validate = validate; return api; }, () => {}, 2);
                    var result = service.Execute(BulkEjectPolicy.Plan(d.State), _ => {}, (target, validate) => {}, cancellation.Token);
                    Check(cancellation.IsCancellationRequested && !result.Success && result.Confirmed == 2 && result.SafeDevices.Count == 2 && api.Calls.Count == 2 && result.Message.Contains("cancelled"), "Final cancellation reported success, lost fresh absence or retried");
                }
            });
            Test("bulk partial failure and failed final scan preserve veto while retracting earlier safe names", () => {
                var d = new CardDiscovery(); d.BeforeScan = number => { if (number == 7) throw new SafetyException("fixture lost final inventory"); };
                var api = new CardApi { OnRequest = id => { if (id == "usb-a") { d.Remove(id); return new EjectResult { Success = true }; } return new EjectResult { Code = 23, Veto = 5, Name = "fixture editor" }; } };
                var result = ExecuteCards(d, BulkEjectPolicy.Plan(d.State), api, new Operations());
                Check(!result.Success && result.Confirmed == 0 && result.SafeDevices.Count == 0 && api.Calls.Count == 2 && result.Message.Contains("fixture editor") && result.Message.Contains("fixture lost final inventory"), "Final failure concealed veto or retained provisional safe name");
            });
        }
    }
}
