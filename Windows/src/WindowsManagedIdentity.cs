using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;

namespace EasyEject {
    // Windows' metadata importer reads a pinned metadata snapshot. No assembly load,
    // dependency resolution, attribute construction, entry point or candidate IL execution.
    internal sealed class WindowsManagedIdentity {
        internal readonly string Name, Version, Product, Title, FileVersion, InformationalVersion;
        internal WindowsManagedIdentity(string name, string version, Dictionary<string, string> values) {
            Name = name; Version = version;
            Product = Required(values, "AssemblyProductAttribute"); Title = Required(values, "AssemblyTitleAttribute");
            FileVersion = Required(values, "AssemblyFileVersionAttribute"); InformationalVersion = Required(values, "AssemblyInformationalVersionAttribute");
        }
        static string Required(Dictionary<string, string> values, string name) { string value; if (!values.TryGetValue("System.Reflection." + name, out value)) throw new SafetyException("The managed Windows executable has missing identity attributes."); return value; }
        internal void Require(string version) {
            if (Name != "EasyEject.Windows" || Version != version || Product != "Easy Eject" || Title != "Easy Eject for Windows" || FileVersion != version || InformationalVersion != version)
                throw new SafetyException("The actual managed Windows application/version identity does not match the authenticated release and native resources.");
        }
        internal void RequireModern(string version) {
            if (!WindowsReleaseManifest.StableVersion(version) || Name != "EasyEject.Modern" || Version != version || Product != "Easy Eject" || Title != "Easy Eject for Windows" || FileVersion != version || InformationalVersion != version)
                throw new SafetyException("The actual managed modern Windows application/version identity does not match the authenticated release.");
        }
        internal void RequireModernBackend(string version) {
            if (!WindowsReleaseManifest.StableVersion(version) || Name != "EasyEject.Runtime" || Version != version || Product != "Easy Eject" || Title != "Easy Eject for Windows" || FileVersion != version || InformationalVersion != version)
                throw new SafetyException("The actual modern backend library identity differs from the authenticated release.");
        }
        internal static WindowsManagedIdentity Read(FileStream stream) {
            byte[] bytes = WindowsExecutableIdentity.ReadMetadata(stream);
            return NativeManagedMetadata.Read(bytes);
        }
        internal static WindowsManagedIdentity ReadModern(FileStream stream) {
            uint entryPoint; byte[] bytes = WindowsExecutableIdentity.ReadModernMetadata(stream, out entryPoint);
            return NativeManagedMetadata.ReadModern(bytes, entryPoint);
        }
        internal static WindowsManagedIdentity ReadModernBackend(FileStream stream) {
            return NativeManagedMetadata.ReadModernLibrary(WindowsExecutableIdentity.ReadModernLibraryMetadata(stream));
        }
    }
    internal static class NativeManagedMetadata {
        static readonly Guid DispenserClass = new Guid("e5cb7a31-7512-11d2-89ce-0080c792e5d8"), DispenserInterface = new Guid("809c652e-7396-11d2-9771-00a0c9b4d50c"), ImportInterface = new Guid("7dac8207-d3ae-4c75-9b67-92801a497d44"), AssemblyInterface = new Guid("ee62470b-e94b-424e-9b7c-2f00c9249f93");
        static readonly string[] Wanted = { "System.Reflection.AssemblyProductAttribute", "System.Reflection.AssemblyTitleAttribute", "System.Reflection.AssemblyFileVersionAttribute", "System.Reflection.AssemblyInformationalVersionAttribute" };
        [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
        [DllImport("rometadata.dll", ExactSpelling = true)] static extern int MetaDataGetDispenser(ref Guid clsid, ref Guid iid, out IntPtr dispenser);
        [UnmanagedFunctionPointer(CallingConvention.StdCall)] delegate int OpenMemory(IntPtr self, IntPtr data, uint size, uint flags, ref Guid iid, out IntPtr importer);
        [UnmanagedFunctionPointer(CallingConvention.StdCall)] delegate int AssemblyToken(IntPtr self, out uint token);
        [UnmanagedFunctionPointer(CallingConvention.StdCall, CharSet = CharSet.Unicode)] delegate int AssemblyProps(IntPtr self, uint token, out IntPtr key, out uint keySize, out uint algorithm, [Out] StringBuilder name, uint capacity, out uint count, ref AssemblyMetadata metadata, out uint flags);
        [UnmanagedFunctionPointer(CallingConvention.StdCall, CharSet = CharSet.Unicode)] delegate int ReferenceProps(IntPtr self, uint token, out IntPtr key, out uint keySize, [Out] StringBuilder name, uint capacity, out uint count, ref AssemblyMetadata metadata, out IntPtr hash, out uint hashSize, out uint flags);
        [UnmanagedFunctionPointer(CallingConvention.StdCall)] delegate int EnumerateAttributes(IntPtr self, ref IntPtr enumeration, uint owner, uint type, [Out, MarshalAs(UnmanagedType.LPArray, SizeParamIndex = 5)] uint[] tokens, uint capacity, out uint count);
        [UnmanagedFunctionPointer(CallingConvention.StdCall)] delegate void CloseEnumeration(IntPtr self, IntPtr enumeration);
        [UnmanagedFunctionPointer(CallingConvention.StdCall)] delegate int AttributeProps(IntPtr self, uint token, out uint owner, out uint constructor, out IntPtr data, out uint size);
        [UnmanagedFunctionPointer(CallingConvention.StdCall, CharSet = CharSet.Unicode)] delegate int MemberProps(IntPtr self, uint token, out uint type, [Out] StringBuilder name, uint capacity, out uint count, out IntPtr signature, out uint size);
        [UnmanagedFunctionPointer(CallingConvention.StdCall, CharSet = CharSet.Unicode)] delegate int TypeProps(IntPtr self, uint token, out uint scope, [Out] StringBuilder name, uint capacity, out uint count);
        [UnmanagedFunctionPointer(CallingConvention.StdCall, CharSet = CharSet.Unicode)] delegate int MethodDefinitionProps(IntPtr self, uint token, out uint owner, [Out] StringBuilder name, uint capacity, out uint count, out uint attributes, out IntPtr signature, out uint size, out uint rva, out uint implementation);
        [UnmanagedFunctionPointer(CallingConvention.StdCall, CharSet = CharSet.Unicode)] delegate int TypeDefinitionProps(IntPtr self, uint token, [Out] StringBuilder name, uint capacity, out uint count, out uint attributes, out uint parent);
        [StructLayout(LayoutKind.Sequential)] internal struct AssemblyMetadata {
            internal ushort Major, Minor, Build, Revision; internal IntPtr Locale; internal uint LocaleCount;
            internal IntPtr Processors; internal uint ProcessorCount; internal IntPtr Systems; internal uint SystemCount;
            internal string Version { get { return new Version(Major, Minor, Build, Revision).ToString(4); } }
        }
        internal static WindowsManagedIdentity Read(byte[] bytes) {
            return Read(bytes, false, 0);
        }
        internal static WindowsManagedIdentity ReadModern(byte[] bytes, uint entryPoint) {
            if ((entryPoint & 0xff000000) != 0x06000000 || (entryPoint & 0x00ffffff) == 0) throw new SafetyException("The modern managed application has no supported method entry point.");
            return Read(bytes, true, entryPoint);
        }
        internal static WindowsManagedIdentity ReadModernLibrary(byte[] bytes) { return Read(bytes, true, 0); }
        static WindowsManagedIdentity Read(byte[] bytes, bool modern, uint entryPoint) {
            if (bytes == null || bytes.Length < 16 || bytes.Length > 16777216 || BitConverter.ToUInt32(bytes, 0) != 0x424a5342) throw new SafetyException("Missing or unsupported bounded CLR metadata snapshot.");
            var pin = GCHandle.Alloc(bytes, GCHandleType.Pinned); IntPtr dispenser = IntPtr.Zero, importer = IntPtr.Zero, assemblies = IntPtr.Zero, enumeration = IntPtr.Zero, locale = IntPtr.Zero;
            try {
                Guid clsid = DispenserClass, iid = DispenserInterface; Status(MetaDataGetDispenser(ref clsid, ref iid, out dispenser), "create dispenser"); RequirePointer(dispenser);
                // ofReadOnly | ofNoTransform: no emit interface or WinMD projection.
                iid = ImportInterface; Status(Method<OpenMemory>(dispenser, 5)(dispenser, pin.AddrOfPinnedObject(), (uint)bytes.Length, 0x1010, ref iid, out importer), "open metadata"); RequirePointer(importer);
                iid = AssemblyInterface;
#if NET
                Status(Marshal.QueryInterface(importer, in iid, out assemblies), "assembly interface");
#else
                Status(Marshal.QueryInterface(importer, ref iid, out assemblies), "assembly interface");
#endif
                RequirePointer(assemblies);
                locale = Marshal.AllocHGlobal(512); Marshal.WriteInt16(locale, 0); uint assembly;
                Status(Method<AssemblyToken>(assemblies, 12)(assemblies, out assembly), "assembly token"); if (assembly != 0x20000001) throw new SafetyException("Ambiguous managed Windows assembly identity.");
                var metadata = new AssemblyMetadata { Locale = locale, LocaleCount = 256 }; var name = new StringBuilder(256); uint count, keySize, algorithm, flags; IntPtr key;
                Status(Method<AssemblyProps>(assemblies, 3)(assemblies, assembly, out key, out keySize, out algorithm, name, 256, out count, ref metadata, out flags), "assembly properties"); CheckName(count);
                RequireNeutral(metadata); if ((flags & 0x2000) != 0) throw new SafetyException("Windows Runtime metadata is not the supported managed application.");
                string assemblyName = name.ToString(), version = metadata.Version; var values = new Dictionary<string, string>(StringComparer.Ordinal); var tokens = new uint[32]; int total = 0;
                while (true) {
                    // Native SDK vtable slots include GetMethodProps at 30; MemberRef is 31,
                    // EnumCustomAttributes 53 and GetCustomAttributeProps 54.
                    uint fetched; int result = Method<EnumerateAttributes>(importer, 53)(importer, ref enumeration, assembly, 0, tokens, 32, out fetched);
                    if (result != 0 && result != 1) Status(result, "enumerate attributes after " + total + " rows / " + values.Count + " identities"); if (fetched > 32 || (total += (int)fetched) > 128) throw new SafetyException("Excessive managed assembly attributes.");
                    for (int i = 0; i < fetched; i++) {
                        uint owner, constructor, type, scope, size; IntPtr data, signature;
                        Status(Method<AttributeProps>(importer, 54)(importer, tokens[i], out owner, out constructor, out data, out size), "attribute properties");
                        if (owner != assembly || (constructor & 0xff000000) != 0x0a000000) throw new SafetyException("Unsupported managed assembly attribute constructor.");
                        name.Clear(); Status(Method<MemberProps>(importer, 31)(importer, constructor, out type, name, 256, out count, out signature, out keySize), "member properties"); CheckName(count);
                        if ((type & 0xff000000) != 0x01000000) throw new SafetyException("Managed assembly attributes require external type references.");
                        string member = name.ToString(); name.Clear(); Status(Method<TypeProps>(importer, 14)(importer, type, out scope, name, 256, out count), "type properties"); CheckName(count); string attribute = name.ToString();
                        if (!Wanted.Contains(attribute)) continue;
                        if (member != ".ctor" || keySize != 4 || !Copy(signature, keySize, 4).SequenceEqual(new byte[] { 0x20, 1, 1, 0x0e })) throw new SafetyException("The managed identity attribute has an unsupported constructor signature.");
                        RequireCoreReference(assemblies, scope, locale, modern);
                        if (values.ContainsKey(attribute)) throw new SafetyException("Duplicate managed Windows identity attributes."); values.Add(attribute, StringAttribute(Copy(data, size, 1024)));
                    }
                    if (result == 1 || fetched == 0) break;
                }
                if (modern && entryPoint != 0) RequireModernEntryPoint(importer, entryPoint);
                return new WindowsManagedIdentity(assemblyName, version, values);
            } finally {
                try { if (enumeration != IntPtr.Zero && importer != IntPtr.Zero) Method<CloseEnumeration>(importer, 3)(importer, enumeration); }
                finally { if (assemblies != IntPtr.Zero) Marshal.Release(assemblies); if (importer != IntPtr.Zero) Marshal.Release(importer); if (dispenser != IntPtr.Zero) Marshal.Release(dispenser); if (locale != IntPtr.Zero) Marshal.FreeHGlobal(locale); pin.Free(); }
            }
        }
        static void RequireCoreReference(IntPtr assemblies, uint token, IntPtr locale, bool modern) {
            if ((token & 0xff000000) != 0x23000000) throw new SafetyException("The managed identity attribute does not come from the Framework core assembly.");
            Marshal.WriteInt16(locale, 0); var metadata = new AssemblyMetadata { Locale = locale, LocaleCount = 256 }; var name = new StringBuilder(256); IntPtr key, hash; uint keySize, count, hashSize, flags;
            Status(Method<ReferenceProps>(assemblies, 4)(assemblies, token, out key, out keySize, name, 256, out count, ref metadata, out hash, out hashSize, out flags), "assembly properties"); CheckName(count); RequireNeutral(metadata);
            byte[] expectedKey = modern ? new byte[] { 0xb0, 0x3f, 0x5f, 0x7f, 0x11, 0xd5, 0x0a, 0x3a } : new byte[] { 0xb7, 0x7a, 0x5c, 0x56, 0x19, 0x34, 0xe0, 0x89 };
            if (name.ToString() != (modern ? "System.Runtime" : "mscorlib") || metadata.Version != (modern ? "10.0.0.0" : "4.0.0.0") || flags != 0 || keySize != 8 || !Copy(key, keySize, 8).SequenceEqual(expectedKey)) throw new SafetyException("The managed identity attribute references an unexpected core assembly.");
        }
        static void RequireModernEntryPoint(IntPtr importer, uint token) {
            var name = new StringBuilder(256); uint owner, count, attributes, size, rva, implementation; IntPtr signature;
            Status(Method<MethodDefinitionProps>(importer, 30)(importer, token, out owner, name, 256, out count, out attributes, out signature, out size, out rva, out implementation), "modern entry point"); CheckName(count);
            if (name.ToString() != "Main" || attributes != 0x91 || implementation != 0 || rva == 0 || (owner & 0xff000000) != 0x02000000 || size != 5 || !Copy(signature, size, 5).SequenceEqual(new byte[] { 0, 1, 1, 0x1d, 0x0e }))
                throw new SafetyException("The modern managed application has an unsupported entry point contract.");
            uint parent; name.Clear(); Status(Method<TypeDefinitionProps>(importer, 12)(importer, owner, name, 256, out count, out attributes, out parent), "modern entry point owner"); CheckName(count);
            if (name.ToString() != "EasyEject.Modern.Program") throw new SafetyException("The modern managed application has an unexpected entry point owner.");
        }
        static void RequireNeutral(AssemblyMetadata metadata) { if (metadata.LocaleCount > 256 || metadata.ProcessorCount != 0 || metadata.SystemCount != 0 || Marshal.PtrToStringUni(metadata.Locale) != "") throw new SafetyException("Unsupported managed assembly culture or platform metadata."); }
        static T Method<T>(IntPtr pointer, int slot) { RequirePointer(pointer); return (T)(object)Marshal.GetDelegateForFunctionPointer(Marshal.ReadIntPtr(Marshal.ReadIntPtr(pointer), slot * IntPtr.Size), typeof(T)); }
        static void Status(int result, string operation = "metadata operation") { if (result != 0) throw new SafetyException("Windows could not read managed application metadata during " + operation + " (0x" + unchecked((uint)result).ToString("X8") + ")."); }
        static void RequirePointer(IntPtr pointer) { if (pointer == IntPtr.Zero) throw new SafetyException("Windows returned incomplete managed metadata state."); }
        static void CheckName(uint count) { if (count == 0 || count > 256) throw new SafetyException("Managed metadata name exceeds its supported bound."); }
        static byte[] Copy(IntPtr pointer, uint size, uint maximum) { if (pointer == IntPtr.Zero || size == 0 || size > maximum) throw new SafetyException("Managed metadata blob exceeds its supported bound."); var bytes = new byte[(int)size]; Marshal.Copy(pointer, bytes, 0, bytes.Length); return bytes; }
        internal static string StringAttribute(byte[] bytes) {
            if (bytes == null || bytes.Length < 5 || bytes[0] != 1 || bytes[1] != 0) throw new SafetyException("Malformed managed identity attribute.");
            int position = 2, length, prefix = bytes[position++];
            if (prefix < 128) length = prefix;
            else if (prefix < 192) { if (position >= bytes.Length) throw new SafetyException("Truncated managed identity attribute."); length = ((prefix & 63) << 8) | bytes[position++]; if (length < 128) throw new SafetyException("Noncanonical managed identity attribute."); }
            else throw new SafetyException("Unsupported managed identity attribute length.");
            if (length == 0 || position + length + 2 != bytes.Length || bytes[bytes.Length - 1] != 0 || bytes[bytes.Length - 2] != 0) throw new SafetyException("Managed identity attribute has missing, trailing or named arguments.");
            try { string value = new UTF8Encoding(false, true).GetString(bytes, position, length); if (value.Any(Char.IsControl)) throw new SafetyException("Managed identity attribute contains control characters."); return value; }
            catch (DecoderFallbackException ex) { throw new SafetyException("Managed identity attribute is not valid UTF-8: " + ex.Message); }
        }
    }
}
