using System;
using System.IO;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Text;

namespace EasyEject {
    internal static class JsonDocuments {
        // Framework's JSON/XML reader can combine adjacent root objects or ignore
        // a trailing null. Check the complete bounded input before decoding choices.
        // The caller keeps its original native file/directory leases throughout.
        internal static T Read<T>(Stream stream, int maximumBytes) where T : class {
            if (stream == null) throw new ArgumentNullException("stream");
            if (maximumBytes <= 0) throw new ArgumentOutOfRangeException("maximumBytes");
            using (var data = new MemoryStream()) {
                var buffer = new byte[4096]; int count;
                while ((count = stream.Read(buffer, 0, buffer.Length)) != 0) {
                    if (count > maximumBytes - data.Length) throw new SafetyException("Saved or probed JSON exceeds its supported size.");
                    data.Write(buffer, 0, count);
                }
                byte[] bytes = data.ToArray(); string text;
                try {
                    // Preserve Framework's BOM-less UTF-16 legacy records as well
                    // as writer-produced UTF-8. BOMs were already unsupported.
                    Encoding encoding = bytes.Length >= 2 && bytes[0] == 0 ? new UnicodeEncoding(true, false, true)
                        : bytes.Length >= 2 && bytes[1] == 0 ? (Encoding)new UnicodeEncoding(false, false, true) : new UTF8Encoding(false, true);
                    text = encoding.GetString(bytes);
                } catch (DecoderFallbackException ex) { throw new SerializationException("Saved or probed JSON has invalid text encoding: " + ex.Message, ex); }
                JsonSyntax.Validate(text); data.Position = 0;
                return (T)new DataContractJsonSerializer(typeof(T)).ReadObject(data);
            }
        }
    }
}
