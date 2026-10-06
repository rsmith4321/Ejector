using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;

namespace EasyEject {
    public sealed class SafetyException : Exception { public SafetyException(string message) : base(message) {} }
    public sealed class Disk {
        public int Number; public uint DevInst; public string InstanceId = "", Model = "", Serial = "", TargetId = "";
        public uint TargetDevInst; public int Bus; public bool Removable, Hotplug; public string Error = "";
        public List<string> Ancestors = new List<string>();
        public string Fingerprint { get { return Number + "|" + InstanceId + "|" + Serial + "|" + TargetId + "|" + DevInst + "|" + TargetDevInst + "|" + Error + "|" + String.Join(";", Ancestors); } }
    }
    public sealed class Volume {
        public string Id = "", Label = "", Serial = "", Error = "", Extents = "";
        public List<string> Mounts = new List<string>(); public List<int> Disks = new List<int>();
        public bool System; public string Classification = "Unknown";
        public string Fingerprint { get { return Id + "|" + Serial + "|" + Extents + "|" + System + "|" + Error + "|" + String.Join(";", Mounts.OrderBy(x => x)); } }
        public string Display { get { return (String.IsNullOrEmpty(Label) ? "Unnamed volume" : Label) + " [" + String.Join(", ", Mounts) + "]"; } }
    }
    public sealed class Inventory {
        public long Epoch; public bool Complete = true;
        public List<Disk> Disks = new List<Disk>(); public List<Volume> Volumes = new List<Volume>();
        public List<string> Errors = new List<string>();
        public string GroupFingerprint(string target) {
            var disks = Disks.Where(d => d.Ancestors.Contains(target) || d.InstanceId == target).ToList();
            var numbers = new HashSet<int>(disks.Select(d => d.Number));
            return String.Join("\n", disks.Select(d => "D:" + d.Fingerprint).Concat(Volumes.Where(v => v.Disks.Any(numbers.Contains)).Select(v => "V:" + v.Fingerprint)).OrderBy(x => x));
        }
    }
    public interface IDiscovery { Inventory Scan(); long Epoch { get; } }
    public sealed class EjectResult {
        public bool Success; public uint Code; public int Veto; public string Name = "";
        public string Message { get { return Success ? "Windows accepted safe removal. The device may now be unplugged." : "Windows refused removal (CONFIGRET 0x" + Code.ToString("X") + ", veto " + ((VetoReason)Veto) + "). " + Name + " Finish transfers and close programs using this device, then refresh and try again."; } }
    }
    public enum VetoReason { Unknown, LegacyDevice, PendingClose, WindowsApp, WindowsService, OutstandingOpen, Device, Driver, IllegalRequest, InsufficientPower, NonDisableable, LegacyDriver, InsufficientRights }
    public interface IEjectApi { EjectResult Request(uint devInst, string instanceId); }
    // One shared coordinator reserves both source and destination disks throughout an operation.
    public sealed class Operations {
        readonly object gate = new object(); readonly HashSet<int> busy = new HashSet<int>();
        public IDisposable Reserve(IEnumerable<int> disks) {
            int[] keys = disks.Distinct().ToArray();
            lock (gate) { if (keys.Any(busy.Contains)) throw new SafetyException("This disk is busy with another Easy Eject operation."); foreach (int key in keys) busy.Add(key); }
            return new Release(delegate { lock (gate) foreach (int key in keys) busy.Remove(key); });
        }
        sealed class Release : IDisposable { Action action; public Release(Action value) { action = value; } public void Dispose() { var a = Interlocked.Exchange(ref action, null); if (a != null) a(); } }
    }
    public sealed class EjectPlan {
        public string TargetId, Fingerprint; public uint TargetDevInst; public long Epoch;
        public int[] Disks; public string[] Affected;
    }
    public sealed class EjectService {
        readonly IDiscovery discovery; readonly IEjectApi api; readonly Operations operations;
        public EjectService(IDiscovery d, IEjectApi a, Operations o) { discovery = d; api = a; operations = o; }
        public static EjectPlan Plan(Inventory inventory, string volumeId) {
            if (!inventory.Complete) throw new SafetyException("Device inventory is incomplete. Refresh or use Windows Safely Remove Hardware after reviewing the device.");
            var volume = inventory.Volumes.SingleOrDefault(v => v.Id == volumeId);
            if (volume == null || volume.Disks.Count != 1 || volume.Error != "") throw new SafetyException("This volume has an unknown or spanning physical disk. Ejection is blocked.");
            var disk = inventory.Disks.SingleOrDefault(d => d.Number == volume.Disks[0]);
            if (disk == null || disk.Error != "" || disk.TargetId == "") throw new SafetyException("No safe removable device node was identified. Use Windows Safely Remove Hardware.");
            var group = inventory.Disks.Where(d => d.InstanceId == disk.TargetId || d.Ancestors.Contains(disk.TargetId)).ToArray();
            var numbers = new HashSet<int>(group.Select(d => d.Number));
            var siblings = inventory.Volumes.Where(v => v.Disks.Any(numbers.Contains)).ToArray();
            if (group.Any(d => d.Error != "") || siblings.Any(v => v.System || v.Error != "" || v.Disks.Count != 1)) throw new SafetyException("Removal would affect protected, unknown, or spanning storage. Nothing was ejected.");
            return new EjectPlan { TargetId = disk.TargetId, TargetDevInst = disk.TargetDevInst, Epoch = inventory.Epoch,
                Fingerprint = inventory.GroupFingerprint(disk.TargetId), Disks = numbers.ToArray(), Affected = siblings.Select(v => v.Display).ToArray() };
        }
        public EjectResult Execute(EjectPlan plan) {
            using (operations.Reserve(plan.Disks)) {
                var current = discovery.Scan();
                if (!current.Complete || current.Epoch != plan.Epoch || discovery.Epoch != plan.Epoch || current.GroupFingerprint(plan.TargetId) != plan.Fingerprint)
                    throw new SafetyException("The device connection or volumes changed. Refresh and review a new eject confirmation.");
                return api.Request(plan.TargetDevInst, plan.TargetId);
            }
        }
    }
    public static class Classifier {
        public static string Classify(Disk disk, bool cameraFolders) {
            if (disk == null) return "Unknown mapping";
            if (!(disk.Removable || disk.Hotplug || disk.TargetId != "")) return "Internal / fixed storage";
            string text = (disk.Model + " " + disk.InstanceId).ToUpperInvariant();
            if (text.Contains("CFEXPRESS")) return "Camera card candidate (CFexpress hardware)";
            if (text.Contains("XQD")) return "Camera card candidate (XQD hardware)";
            if (disk.Bus == 12 || disk.Bus == 13) return "Removable SD / MMC candidate";
            if (cameraFolders) return "Camera media candidate (folder evidence; card type unconfirmed)";
            return "Other external storage (card type unconfirmed)";
        }
    }
}
