using System;
using System.Collections.Generic;
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
                RequireOneDocument(text); data.Position = 0;
                return (T)new DataContractJsonSerializer(typeof(T)).ReadObject(data);
            }
        }
        static void RequireOneDocument(string text) {
            var stack = new Stack<char>(); bool started = false, quoted = false, escaped = false;
            for (int i = 0; i < text.Length; i++) {
                char c = text[i];
                if (quoted) {
                    if (c < 32) throw new SerializationException("Saved or probed JSON contains a raw control character.");
                    if (escaped) { escaped = false; continue; }
                    if (c == '\\') { escaped = true; continue; }
                    if (c == '"') quoted = false;
                    continue;
                }
                if (!started) {
                    if (Space(c)) continue;
                    // Keep existing caller-specific null-record refusal semantics.
                    if (c == 'n' && text.Length - i >= 4 && text.Substring(i, 4) == "null") {
                        RequireEnd(text, i + 4); return;
                    }
                    if (c != '{' && c != '[') throw new SerializationException("Saved or probed JSON must contain one complete object or array.");
                    started = true;
                }
                if (c == '"') { quoted = true; continue; }
                if (c == '{' || c == '[') stack.Push(c);
                else if (c == '}' || c == ']') {
                    if (stack.Count == 0 || stack.Pop() != (c == '}' ? '{' : '[')) throw new SerializationException("Saved or probed JSON has mismatched object or array boundaries.");
                    if (stack.Count == 0) {
                        RequireEnd(text, i + 1); return;
                    }
                } else if (c < 32 && !Space(c)) throw new SerializationException("Saved or probed JSON contains an unsupported control character.");
            }
            throw new SerializationException("Saved or probed JSON is incomplete.");
        }
        static void RequireEnd(string text, int start) {
            for (int i = start; i < text.Length; i++) if (!Space(text[i])) throw new SerializationException("Saved or probed JSON contains data after its complete document.");
        }
        static bool Space(char c) { return c == ' ' || c == '\t' || c == '\r' || c == '\n'; }
    }
}
