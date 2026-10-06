using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;

namespace EasyEject {
    public sealed class WindowsDiscovery : IDiscovery, IScopedConnectionDiscovery {
        long epoch; readonly object scanGate = new object(); string previous = null;
        public bool NotificationsAvailable;
        internal readonly InstanceNotifications Instances;
        public WindowsDiscovery() { Instances = new InstanceNotifications(ConnectionChanged); }
        public long Epoch { get { return Interlocked.Read(ref epoch); } }
        string IScopedConnectionDiscovery.Stamp(string[] instances) { return Instances.Versions.Stamp(instances); }
        public void ConnectionChanged() { Interlocked.Increment(ref epoch); }
        public Inventory Scan() {
            lock (scanGate) {
                long start = Epoch; var inventory = new Inventory();
                try { ScanDisks(inventory); } catch (Exception ex) { inventory.Complete = false; inventory.Errors.Add("Disk inventory: " + ex.Message); }
                try { ScanVolumes(inventory); } catch (Exception ex) { inventory.Complete = false; inventory.Errors.Add("Volume inventory: " + ex.Message); }
                foreach (var v in inventory.Volumes) {
                    var d = v.Disks.Count == 1 ? inventory.Disks.SingleOrDefault(x => x.Number == v.Disks[0]) : null;
                    bool folders = false; int emulatorHits = 0;
                    if (!v.System && Classifier.External(d)) {
                        foreach (string root in v.Mounts) {
                            try {
                                var evidence = FolderEvidence.Read(root); folders |= evidence.Camera; emulatorHits = Math.Max(emulatorHits, evidence.EmulatorHits);
                            } catch (Exception) { /* Classification evidence is optional, never permission to eject. */ }
                        }
                    }
                    Classifier.Apply(v, d, folders, emulatorHits);
                    if (v.Disks.Any(n => !inventory.Disks.Any(x => x.Number == n))) { inventory.Complete = false; inventory.Errors.Add("A volume maps to a disk missing from PnP inventory."); }
                }
                foreach (string target in inventory.Disks.Where(d => d.TargetId != "").Select(d => d.TargetId).Distinct(StringComparer.OrdinalIgnoreCase)) {
                    inventory.GroupConnections[target] = Instances.Versions.Stamp(GroupInstances(inventory, target));
                }
                foreach (var disk in inventory.Disks.Where(d => d.TargetId == "" && d.InstanceId != "")) inventory.GroupConnections[disk.InstanceId] = Instances.Versions.Stamp(GroupInstances(inventory, disk.InstanceId));
                string fingerprint = String.Join("\n", inventory.Disks.Select(d => d.Fingerprint).Concat(inventory.Volumes.Select(v => v.Fingerprint)).OrderBy(x => x));
                if (start != Epoch) { inventory.Complete = false; inventory.Errors.Add("Connection changed while scanning; refresh again."); }
                if (!NotificationsAvailable) { inventory.Complete = false; inventory.Errors.Add("Connection notifications unavailable; operations are blocked."); }
                if (previous != null && previous != fingerprint) ConnectionChanged();
                previous = fingerprint;
                inventory.Epoch = Epoch;
                return inventory;
            }
        }
        internal static string[] GroupInstances(Inventory state, string target) { return state.Disks.Where(x => x.InstanceId == target || x.Ancestors.Contains(target)).SelectMany(x => x.Ancestors.Concat(new[] { x.InstanceId, target })).Distinct(StringComparer.OrdinalIgnoreCase).ToArray(); }
        static void ScanDisks(Inventory inventory) {
            Guid guid = new Guid("53f56307-b6bf-11d0-94f2-00a0c91efb8b");
            IntPtr set = Native.SetupDiGetClassDevs(ref guid, null, IntPtr.Zero, 0x12);
            if (set == new IntPtr(-1)) throw Native.Error("Cannot enumerate disk interfaces");
            try {
                for (uint i = 0; ; i++) {
                    var face = new Native.InterfaceInfo { Size = Marshal.SizeOf(typeof(Native.InterfaceInfo)) };
                    if (!Native.SetupDiEnumDeviceInterfaces(set, IntPtr.Zero, ref guid, i, ref face)) {
                        if (Marshal.GetLastWin32Error() != 259) throw Native.Error("Disk interface enumeration failed"); break;
                    }
                    var dev = new Native.DeviceInfo { Size = Marshal.SizeOf(typeof(Native.DeviceInfo)) }; int required;
                    Native.SetupDiGetDeviceInterfaceDetail(set, ref face, IntPtr.Zero, 0, out required, ref dev);
                    if (required <= 0 || required > 65536) throw new SafetyException("Unexpected disk interface size.");
                    IntPtr detail = Marshal.AllocHGlobal(required);
                    try {
                        Marshal.WriteInt32(detail, IntPtr.Size == 8 ? 8 : 6);
                        if (!Native.SetupDiGetDeviceInterfaceDetail(set, ref face, detail, required, out required, ref dev)) throw Native.Error("Cannot read disk interface");
                        string path = Marshal.PtrToStringUni(IntPtr.Add(detail, 4));
                        try {
                            using (var handle = Native.OpenDevice(path)) {
                                var number = Native.Ioctl(handle, 0x2D1080, null);
                                if (number.Length < 12 || BitConverter.ToInt32(number, 0) != 7) throw new SafetyException("Not a disk interface.");
                                var disk = new Disk { Number = BitConverter.ToInt32(number, 4), DevInst = dev.DevInst, InstanceId = Native.DeviceId(dev.DevInst) };
                                inventory.Disks.Add(disk);
                                try {
                                    var descriptor = Native.Ioctl(handle, 0x2D1400, new byte[12]);
                                    if (descriptor.Length < 36) throw new SafetyException("Storage descriptor is incomplete.");
                                    disk.Model = (ReadAscii(descriptor, 12) + " " + ReadAscii(descriptor, 16)).Trim();
                                    disk.Serial = ReadAscii(descriptor, 24); disk.Bus = BitConverter.ToInt32(descriptor, 28); disk.Removable = descriptor[10] != 0;
                                    var hotplug = Native.Ioctl(handle, 0x2D0C14, null);
                                    if (hotplug.Length < 8) throw new SafetyException("Hotplug information is incomplete.");
                                    disk.Hotplug = hotplug[6] != 0; disk.Removable |= hotplug[4] != 0;
                                    FindTarget(disk);
                                } catch (Exception ex) { disk.Error = ex.Message; }
                            }
                        } catch (Exception ex) { inventory.Complete = false; inventory.Errors.Add("Disk interface unavailable: " + ex.Message); }
                    } finally { Marshal.FreeHGlobal(detail); }
                }
                if (inventory.Disks.GroupBy(d => d.Number).Any(g => g.Count() != 1)) throw new SafetyException("Ambiguous physical disk mapping.");
            } finally { Native.SetupDiDestroyDeviceInfoList(set); }
        }
        static string ReadAscii(byte[] data, int field) {
            int offset = BitConverter.ToInt32(data, field); if (offset == 0) return "";
            if (offset < 0 || offset >= data.Length) throw new SafetyException("Invalid storage descriptor string offset.");
            int end = Array.IndexOf(data, (byte)0, offset); if (end < 0) throw new SafetyException("Unterminated storage descriptor string.");
            return Encoding.ASCII.GetString(data, offset, end - offset).Trim();
        }
        static void FindTarget(Disk disk) {
            uint node = disk.DevInst;
            for (int depth = 0; depth < 32; depth++) {
                string id = Native.DeviceId(node); disk.Ancestors.Add(id);
                string service = Native.PropertyText(node, 5); // CM_DRP_SERVICE
                byte[] caps = Native.Property(node, 16); // CM_DRP_CAPABILITIES
                // Stop before hubs/controllers; eject only the nearest removable device, never a shared hub.
                bool boundary = service.IndexOf("hub", StringComparison.OrdinalIgnoreCase) >= 0 || id.StartsWith("PCI\\", StringComparison.OrdinalIgnoreCase) || id.StartsWith("ACPI\\", StringComparison.OrdinalIgnoreCase) || id.StartsWith("ROOT\\", StringComparison.OrdinalIgnoreCase);
                if (!boundary && disk.TargetId == "" && caps.Length >= 4 && (BitConverter.ToUInt32(caps, 0) & 4) != 0 && (BitConverter.ToUInt32(caps, 0) & 8) == 0) {
                    disk.TargetId = id; disk.TargetDevInst = node;
                }
                if (boundary) break;
                uint parent; if (Native.CM_Get_Parent(out parent, node, 0) != 0) break; node = parent;
            }
        }
        static void ScanVolumes(Inventory inventory) {
            var name = new StringBuilder(1024); IntPtr find = Native.FindFirstVolume(name, name.Capacity);
            if (find == new IntPtr(-1)) throw Native.Error("Cannot enumerate volumes");
            string osRoot = VolumeId(Environment.GetFolderPath(Environment.SpecialFolder.Windows));
            try {
                do {
                    var v = new Volume { Id = name.ToString() }; v.System = v.Id == osRoot; inventory.Volumes.Add(v);
                    try {
                        int required; var paths = new char[32768];
                        if (!Native.GetVolumePathNamesForVolumeName(v.Id, paths, paths.Length, out required)) throw Native.Error("Cannot enumerate volume mount points");
                        v.Mounts.AddRange(new string(paths).Split(new[] { '\0' }, StringSplitOptions.RemoveEmptyEntries));
                        using (var handle = Native.OpenDevice(v.Id)) {
                            var extents = Native.Ioctl(handle, 0x560000, null);
                            if (extents.Length < 32) throw new SafetyException("Volume disk extents are incomplete.");
                            int count = BitConverter.ToInt32(extents, 0);
                            if (count < 1 || count > 2048 || extents.Length < 8 + count * 24) throw new SafetyException("Invalid volume disk extents.");
                            var keys = new List<string>();
                            for (int e = 0; e < count; e++) {
                                int offset = 8 + e * 24, disk = BitConverter.ToInt32(extents, offset);
                                if (!v.Disks.Contains(disk)) v.Disks.Add(disk);
                                keys.Add(disk + ":" + BitConverter.ToInt64(extents, offset + 8) + ":" + BitConverter.ToInt64(extents, offset + 16));
                            }
                            v.Extents = String.Join(";", keys.OrderBy(x => x));
                        }
                    } catch (Exception ex) { v.Error = ex.Message; inventory.Complete = false; inventory.Errors.Add("Unmapped volume: " + v.Id + " " + ex.Message); }
                    uint serial, max, flags; var label = new StringBuilder(1024); var fs = new StringBuilder(128);
                    if (Native.GetVolumeInformation(v.Id, label, label.Capacity, out serial, out max, out flags, fs, fs.Capacity)) { v.Serial = serial.ToString("X8"); v.Label = label.ToString(); }
                    else v.Error = "Volume identity unavailable (unformatted, locked, or unsupported filesystem).";
                } while (Native.FindNextVolume(find, name, name.Capacity));
                if (Marshal.GetLastWin32Error() != 18) throw Native.Error("Volume enumeration ended unexpectedly");
            } finally { Native.FindVolumeClose(find); }
        }
        public static string VolumeId(string path) {
            var root = new StringBuilder(1024); var name = new StringBuilder(1024);
            if (!Native.GetVolumePathName(path, root, root.Capacity) || !Native.GetVolumeNameForVolumeMountPoint(root.ToString(), name, name.Capacity)) throw Native.Error("Cannot identify local volume for path");
            return name.ToString();
        }
    }
}
