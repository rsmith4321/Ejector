using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;

namespace EasyEject {
    internal static class WindowsPackageStreams {
        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)] struct StreamData { internal long Length; [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 296)] internal string Name; }
        [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true, ExactSpelling = true)] static extern IntPtr FindFirstStreamW(string path, int level, out StreamData data, uint flags);
        [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true, ExactSpelling = true)] [return: MarshalAs(UnmanagedType.Bool)] static extern bool FindNextStreamW(IntPtr search, out StreamData data);
        [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
        [DllImport("kernel32.dll", SetLastError = true, ExactSpelling = true)] [return: MarshalAs(UnmanagedType.Bool)] static extern bool FindClose(IntPtr search);
        // Ordinary hashes exclude named NTFS data streams. Admit only bounded Windows download
        // metadata, preserving it; it does not establish publisher or package authority.
        internal static void Require(string path, long defaultLength) {
            StreamData data; IntPtr search = FindFirstStreamW(path, 0, out data, 0);
            if (search == new IntPtr(-1)) {
                int error = Marshal.GetLastWin32Error();
                if (error == 87 || (error == 38 && defaultLength < 0)) return; // documented no-stream filesystem / ordinary directory
                throw new SafetyException("Cannot establish Windows package stream inventory (Windows error " + error + "): " + path);
            }
            bool ordinary = false; var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            try {
                do {
                    if (!names.Add(data.Name) || names.Count > (defaultLength < 0 ? 1 : 2) || data.Length < 0) throw new SafetyException("The Windows package has an unsupported stream inventory.");
                    if (data.Name == "::$DATA" && defaultLength >= 0 && data.Length == defaultLength) ordinary = true;
                    else if (!String.Equals(data.Name, ":Zone.Identifier:$DATA", StringComparison.OrdinalIgnoreCase) || data.Length > 4096) throw new SafetyException("The Windows package contains an unlisted alternate data stream: " + path);
                } while (FindNextStreamW(search, out data));
                int error = Marshal.GetLastWin32Error(); if (error != 38) throw new SafetyException("Cannot complete Windows package stream inventory (Windows error " + error + ").");
                if (defaultLength >= 0 && !ordinary) throw new SafetyException("The Windows package's ordinary file stream is missing.");
            } finally { if (!FindClose(search)) throw Native.Error("Cannot release Windows package stream inspection"); }
        }
    }
}
