using System;
using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32.SafeHandles;

namespace EasyEject {
    internal static class Native {
        [StructLayout(LayoutKind.Sequential)] internal struct DeviceInfo { public int Size; public Guid ClassGuid; public uint DevInst; public IntPtr Reserved; }
        [StructLayout(LayoutKind.Sequential)] internal struct InterfaceInfo { public int Size; public Guid ClassGuid; public int Flags; public IntPtr Reserved; }
        [StructLayout(LayoutKind.Sequential)] internal struct FileInfo {
            public uint Attributes; public System.Runtime.InteropServices.ComTypes.FILETIME Created, Accessed, Modified;
            public uint VolumeSerial, SizeHigh, SizeLow, Links, IndexHigh, IndexLow;
        }
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)] internal static extern SafeFileHandle CreateFile(string path, uint access, uint share, IntPtr security, uint disposition, uint flags, IntPtr template);
        [DllImport("kernel32.dll", SetLastError = true)] internal static extern bool DeviceIoControl(SafeFileHandle h, uint code, byte[] input, int inputSize, byte[] output, int outputSize, out int returned, IntPtr overlapped);
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)] internal static extern IntPtr FindFirstVolume(StringBuilder name, int length);
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)] internal static extern bool FindNextVolume(IntPtr find, StringBuilder name, int length);
        [DllImport("kernel32.dll")] internal static extern bool FindVolumeClose(IntPtr find);
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)] internal static extern bool GetVolumePathNamesForVolumeName(string name, char[] paths, int length, out int required);
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)] internal static extern bool GetVolumeInformation(string root, StringBuilder label, int labelLength, out uint serial, out uint maxComponent, out uint flags, StringBuilder fs, int fsLength);
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)] internal static extern bool GetVolumePathName(string path, StringBuilder root, int length);
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)] internal static extern bool GetVolumeNameForVolumeMountPoint(string root, StringBuilder name, int length);
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)] internal static extern uint GetFinalPathNameByHandle(SafeFileHandle handle, StringBuilder path, int length, uint flags);
        [DllImport("kernel32.dll", SetLastError = true)] internal static extern bool GetFileInformationByHandle(SafeFileHandle handle, out FileInfo info);
        [DllImport("kernel32.dll", SetLastError = true)] internal static extern bool SetFileInformationByHandle(SafeFileHandle handle, int infoClass, IntPtr info, uint length);
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)] internal static extern bool CreateDirectory(string path, IntPtr security);
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)] static extern bool GetDiskFreeSpaceEx(string path, out ulong available, out ulong total, out ulong free);
        [DllImport("kernel32.dll", SetLastError = true)] static extern bool SetFileTime(SafeFileHandle handle, IntPtr created, IntPtr accessed, ref System.Runtime.InteropServices.ComTypes.FILETIME modified);
        internal static ulong FreeSpace(string path) { ulong available, total, free; if (!GetDiskFreeSpaceEx(path, out available, out total, out free)) throw Error("Cannot check destination free space"); return available; }
        internal static void PreserveModified(SafeFileHandle handle, FileInfo source) { if (!SetFileTime(handle, IntPtr.Zero, IntPtr.Zero, ref source.Modified)) throw Error("Cannot preserve camera modification time"); }
        [DllImport("setupapi.dll", CharSet = CharSet.Unicode, SetLastError = true)] internal static extern IntPtr SetupDiGetClassDevs(ref Guid guid, string enumerator, IntPtr parent, uint flags);
        [DllImport("setupapi.dll", SetLastError = true)] internal static extern bool SetupDiEnumDeviceInterfaces(IntPtr set, IntPtr device, ref Guid guid, uint index, ref InterfaceInfo info);
        [DllImport("setupapi.dll", CharSet = CharSet.Unicode, SetLastError = true)] internal static extern bool SetupDiGetDeviceInterfaceDetail(IntPtr set, ref InterfaceInfo info, IntPtr detail, int length, out int required, ref DeviceInfo device);
        [DllImport("setupapi.dll")] internal static extern bool SetupDiDestroyDeviceInfoList(IntPtr set);
        [DllImport("cfgmgr32.dll", CharSet = CharSet.Unicode)] internal static extern uint CM_Get_Device_ID(uint devInst, StringBuilder id, int length, uint flags);
        [DllImport("cfgmgr32.dll")] internal static extern uint CM_Get_Parent(out uint parent, uint devInst, uint flags);
        [DllImport("cfgmgr32.dll", CharSet = CharSet.Unicode)] internal static extern uint CM_Get_DevNode_Registry_Property(uint devInst, uint property, out uint type, byte[] buffer, ref uint length, uint flags);
        [DllImport("cfgmgr32.dll", CharSet = CharSet.Unicode)] internal static extern uint CM_Locate_DevNode(out uint devInst, string id, uint flags);
        [DllImport("cfgmgr32.dll", CharSet = CharSet.Unicode)] internal static extern uint CM_Request_Device_Eject(uint devInst, out int veto, StringBuilder vetoName, int length, uint flags);
        internal static Exception Error(string operation) { return new Win32Exception(Marshal.GetLastWin32Error(), operation); }
        internal static SafeFileHandle OpenDevice(string path) {
            var handle = CreateFile(path.TrimEnd('\\'), 0, 3, IntPtr.Zero, 3, 0, IntPtr.Zero);
            if (handle.IsInvalid) { handle.Dispose(); throw Error("Cannot query storage device"); } return handle;
        }
        internal static byte[] Ioctl(SafeFileHandle handle, uint code, byte[] input) {
            int returned; var data = new byte[65536];
            if (!DeviceIoControl(handle, code, input, input == null ? 0 : input.Length, data, data.Length, out returned, IntPtr.Zero)) throw Error("Storage query failed (" + code.ToString("X") + ")");
            Array.Resize(ref data, returned); return data;
        }
        internal static string DeviceId(uint node) {
            var id = new StringBuilder(1024); if (CM_Get_Device_ID(node, id, id.Capacity, 0) != 0) throw new SafetyException("Cannot identify PnP device node."); return id.ToString();
        }
        internal static byte[] Property(uint node, uint property) {
            uint type, size = 4096; var data = new byte[size]; if (CM_Get_DevNode_Registry_Property(node, property, out type, data, ref size, 0) != 0) return new byte[0]; Array.Resize(ref data, (int)size); return data;
        }
        internal static string PropertyText(uint node, uint property) { return Encoding.Unicode.GetString(Property(node, property)).TrimEnd('\0'); }
        internal static string FinalPath(SafeFileHandle handle) {
            var path = new StringBuilder(32768); uint count = GetFinalPathNameByHandle(handle, path, path.Capacity, 0);
            if (count == 0 || count >= path.Capacity) throw Error("Cannot validate opened path");
            string result = path.ToString(); if (result.StartsWith(@"\\?\")) result = result.Substring(4); return result;
        }
        internal static FileInfo Info(SafeFileHandle handle) {
            FileInfo info; if (!GetFileInformationByHandle(handle, out info)) throw Error("Cannot inspect opened file");
            if ((info.Attributes & 0x400) != 0) throw new SafetyException("Reparse points and symbolic links are not allowed."); return info;
        }
        // Rename the held, verified file itself; never replace an existing target.
        internal static void Rename(SafeFileHandle handle, string target) {
            byte[] name = Encoding.Unicode.GetBytes(target); int offset = IntPtr.Size == 8 ? 20 : 12; int rootOffset = IntPtr.Size == 8 ? 8 : 4;
            // The Win32 wrapper normalizes a NUL-terminated path before issuing the counted NT rename.
            int size = offset + name.Length + 2;
            IntPtr data = Marshal.AllocHGlobal(size);
            try {
                Marshal.Copy(new byte[size], 0, data, size);
                Marshal.WriteIntPtr(data, rootOffset, IntPtr.Zero); Marshal.WriteInt32(data, offset - 4, name.Length);
                Marshal.Copy(name, 0, IntPtr.Add(data, offset), name.Length);
                if (!SetFileInformationByHandle(handle, 3, data, (uint)size)) throw Error("Cannot publish verified copy without overwriting");
            } finally { Marshal.FreeHGlobal(data); }
        }
        internal static void DeleteOnClose(SafeFileHandle handle) {
            IntPtr data = Marshal.AllocHGlobal(1);
            try { Marshal.WriteByte(data, 1); if (!SetFileInformationByHandle(handle, 4, data, 1)) throw Error("Cannot clean up this operation's private partial file"); }
            finally { Marshal.FreeHGlobal(data); }
        }
    }
    public sealed class WindowsEjectApi : IEjectApi {
        readonly Action validate;
        public WindowsEjectApi(IDiscovery discovery, long epoch) : this(delegate { if (discovery.Epoch != epoch) throw new SafetyException("Connection changed before the Windows removal request. Nothing was ejected."); }) {}
        internal WindowsEjectApi(Action validate) { this.validate = validate; }
        public EjectResult Request(uint devInst, string instanceId) {
            uint located;
            if (Native.CM_Locate_DevNode(out located, instanceId, 0) != 0 || located != devInst || Native.DeviceId(devInst) != instanceId)
                throw new SafetyException("The removal device changed. Nothing was ejected.");
            int veto; var name = new StringBuilder(260);
            validate();
            uint code = Native.CM_Request_Device_Eject(devInst, out veto, name, name.Capacity, 0);
            return new EjectResult { Success = code == 0, Code = code, Veto = veto, Name = name.ToString() };
        }
    }
}
