using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Xml;

namespace EasyEject {
    internal static class WindowsExecutableIdentity {
        internal static void Validate(WindowsSignedFileReview executable, string version) {
            if (!WindowsReleaseManifest.StableVersion(version)) throw new SafetyException("The Windows release requires a stable four-part version.");
            executable.Inspect(ValidatePe);
            var metadata = FileVersionInfo.GetVersionInfo(executable.Path); executable.Validate();
            if (metadata.ProductName != "Easy Eject" || metadata.FileDescription != "Easy Eject for Windows" || metadata.OriginalFilename != WindowsReleaseManifest.Executable || metadata.FileVersion != version || metadata.ProductVersion != version)
                throw new SafetyException("The actual executable application/version identity does not match the authenticated Windows release.");
            executable.Inspect(stream => WindowsManagedIdentity.Read(stream).Require(version));
        }
        internal static void ValidatePe(FileStream stream) { ReadMetadata(stream); }
        internal static byte[] ReadMetadata(FileStream stream) {
            uint ignored; return ReadMetadata(stream, false, false, out ignored);
        }
        internal static byte[] ReadModernMetadata(FileStream stream, out uint entryPoint) {
            return ReadMetadata(stream, true, false, out entryPoint);
        }
        internal static byte[] ReadModernLibraryMetadata(FileStream stream) {
            uint ignored; return ReadMetadata(stream, true, true, out ignored);
        }
        static byte[] ReadMetadata(FileStream stream, bool modern, bool library, out uint entryPoint) {
            entryPoint = 0;
            byte[] dos = Read(stream, 0, 64); if (BitConverter.ToUInt16(dos, 0) != 0x5a4d) Fail();
            uint offset = BitConverter.ToUInt32(dos, 60); if (offset < 64 || offset > 1048576) Fail(); byte[] header = Read(stream, offset, 24);
            ushort sections = BitConverter.ToUInt16(header, 6), optionalSize = BitConverter.ToUInt16(header, 20), flags = BitConverter.ToUInt16(header, 22);
            if (BitConverter.ToUInt32(header, 0) != 0x4550 || BitConverter.ToUInt16(header, 4) != 0x8664 || sections == 0 || sections > 96 || optionalSize != 240 || (flags & 2) == 0 || (flags & 0x2000) != (library ? 0x2000 : 0)) Fail();
            byte[] optional = Read(stream, offset + 24L, optionalSize);
            if (BitConverter.ToUInt16(optional, 0) != 0x20b || BitConverter.ToUInt16(optional, 68) != (library ? 3 : 2) || BitConverter.ToUInt32(optional, 108) != 16) Fail();
            uint clrRva = BitConverter.ToUInt32(optional, 224), clrSize = BitConverter.ToUInt32(optional, 228); if (clrRva == 0 || clrSize != 72) Fail();
            byte[] table = Read(stream, offset + 24L + optionalSize, sections * 40); long clrOffset = -1;
            for (int i = 0; i < sections; i++) {
                uint address = BitConverter.ToUInt32(table, i * 40 + 12), rawSize = BitConverter.ToUInt32(table, i * 40 + 16), raw = BitConverter.ToUInt32(table, i * 40 + 20);
                if (rawSize != 0 && (raw == 0 || (long)raw + rawSize > stream.Length)) Fail();
                if ((ulong)clrRva >= address && (ulong)clrRva + 72 <= (ulong)address + rawSize) { if (clrOffset >= 0) Fail(); clrOffset = (long)raw + clrRva - address; }
            }
            if (clrOffset < 0) Fail(); byte[] clr = Read(stream, clrOffset, 72); uint clrFlags = BitConverter.ToUInt32(clr, 16);
            if (BitConverter.ToUInt32(clr, 0) != 72 || (clrFlags & 1) == 0 || (clrFlags & ~9U) != 0) Fail();
            entryPoint = BitConverter.ToUInt32(clr, 20);
            if (library ? entryPoint != 0 : modern && ((entryPoint & 0xff000000) != 0x06000000 || (entryPoint & 0x00ffffff) == 0)) Fail();
            uint metadataRva = BitConverter.ToUInt32(clr, 8), metadataSize = BitConverter.ToUInt32(clr, 12); long metadataOffset = -1;
            if (metadataRva == 0 || metadataSize < 16 || metadataSize > 16777216) Fail();
            for (int i = 0; i < sections; i++) {
                uint address = BitConverter.ToUInt32(table, i * 40 + 12), rawSize = BitConverter.ToUInt32(table, i * 40 + 16), raw = BitConverter.ToUInt32(table, i * 40 + 20);
                if ((ulong)metadataRva >= address && (ulong)metadataRva + metadataSize <= (ulong)address + rawSize) { if (metadataOffset >= 0) Fail(); metadataOffset = (long)raw + metadataRva - address; }
            }
            if (metadataOffset < 0) Fail(); var metadata = Read(stream, metadataOffset, (int)metadataSize); if (BitConverter.ToUInt32(metadata, 0) != 0x424a5342) Fail(); return metadata;
        }
        static byte[] Read(FileStream stream, long offset, int length) {
            if (offset < 0 || length < 0 || offset > stream.Length - length) { Fail(); }
            stream.Position = offset; byte[] bytes = new byte[length]; int done = 0, count;
            while (done < length && (count = stream.Read(bytes, done, length - done)) > 0) done += count;
            if (done != length) Fail(); return bytes;
        }
        static void Fail() { throw new SafetyException("The candidate is not a supported x64 Windows GUI managed executable."); }
        internal static void ValidateConfiguration(byte[] bytes) {
            try {
                var document = new XmlDocument { XmlResolver = null };
                using (var stream = new MemoryStream(bytes, false)) using (var reader = XmlReader.Create(stream, new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null, IgnoreComments = true, IgnoreWhitespace = true, MaxCharactersInDocument = 16384 })) document.Load(reader);
                var root = document.DocumentElement;
                if (root == null || root.Name != "configuration" || root.Attributes.Count != 0 || root.ChildNodes.Count != 1) throw new SafetyException("Unsupported Windows runtime configuration.");
                var startup = root.FirstChild as XmlElement; var runtime = startup == null ? null : startup.FirstChild as XmlElement;
                if (startup == null || startup.Name != "startup" || startup.Attributes.Count != 0 || startup.ChildNodes.Count != 1 || runtime == null || runtime.Name != "supportedRuntime" || runtime.Attributes.Count != 2 || runtime.GetAttribute("version") != "v4.0" || runtime.GetAttribute("sku") != ".NETFramework,Version=v4.8" || runtime.HasChildNodes) throw new SafetyException("The Windows runtime configuration does not match Framework 4.8.");
                if (document.ChildNodes.Cast<XmlNode>().Any(n => n != root && n.NodeType != XmlNodeType.XmlDeclaration)) throw new SafetyException("Unsupported Windows runtime configuration instructions.");
            } catch (XmlException ex) { throw new SafetyException("The Windows runtime configuration could not be validated: " + ex.Message); }
        }
        internal static void ValidateText(byte[] bytes) {
            try { string text = new UTF8Encoding(false, true).GetString(bytes); if (String.IsNullOrWhiteSpace(text) || text.Any(c => Char.IsControl(c) && c != '\r' && c != '\n' && c != '\t')) throw new SafetyException("The Windows release documentation is empty or contains unsupported text."); }
            catch (DecoderFallbackException ex) { throw new SafetyException("The Windows release documentation is not valid UTF-8: " + ex.Message); }
        }
    }
}
