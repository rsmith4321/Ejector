using System;
using System.Collections.Generic;
using System.Runtime.Serialization;

namespace EasyEject {
    // RFC 8259 token/separator grammar only. Existing decoders, schemas, byte/depth
    // bounds, native ownership and authenticated-byte policies remain authoritative.
    // Iterative container states retain deeply nested legacy unknown fields without
    // introducing recursive parser calls or a new nesting limit.
    internal static class JsonSyntax {
        internal static void Validate(string text) {
            if (text == null) throw new ArgumentNullException("text");
            new Reader(text).Run();
        }
        enum State { ObjectFirstKey, ObjectKey, ObjectColon, ObjectValue, ObjectEnd, ArrayFirstValue, ArrayValue, ArrayEnd }
        sealed class Reader {
            readonly string text; readonly Stack<State> pending = new Stack<State>(); int position;
            internal Reader(string text) { this.text = text; }
            internal void Run() {
                Value();
                while (pending.Count != 0) {
                    WhiteSpace(); State state = pending.Pop();
                    switch (state) {
                        case State.ObjectFirstKey:
                            if (Take('}')) break;
                            StringToken(); pending.Push(State.ObjectColon); break;
                        case State.ObjectKey:
                            StringToken(); pending.Push(State.ObjectColon); break;
                        case State.ObjectColon:
                            Require(':'); pending.Push(State.ObjectValue); break;
                        case State.ObjectValue:
                            pending.Push(State.ObjectEnd); Value(); break;
                        case State.ObjectEnd:
                            if (Take('}')) break;
                            Require(','); pending.Push(State.ObjectKey); break;
                        case State.ArrayFirstValue:
                            if (Take(']')) break;
                            pending.Push(State.ArrayEnd); Value(); break;
                        case State.ArrayValue:
                            pending.Push(State.ArrayEnd); Value(); break;
                        case State.ArrayEnd:
                            if (Take(']')) break;
                            Require(','); pending.Push(State.ArrayValue); break;
                    }
                }
                WhiteSpace(); if (position != text.Length) Fail("data after the complete document");
            }
            void Value() {
                WhiteSpace(); if (position == text.Length) Fail("an incomplete value");
                char c = text[position];
                if (c == '{') { position++; pending.Push(State.ObjectFirstKey); }
                else if (c == '[') { position++; pending.Push(State.ArrayFirstValue); }
                else if (c == '"') StringToken();
                else if (c == 't') Literal("true");
                else if (c == 'f') Literal("false");
                else if (c == 'n') Literal("null");
                else if (c == '-' || Digit(c)) NumberToken();
                else Fail("an invalid value");
            }
            void StringToken() {
                Require('"');
                while (position < text.Length) {
                    char c = text[position++]; if (c == '"') return;
                    if (c < 32) Fail("a raw control character in a string");
                    if (c != '\\') continue;
                    if (position == text.Length) Fail("an incomplete string escape");
                    c = text[position++];
                    if (c == 'u') {
                        for (int i = 0; i < 4; i++) {
                            if (position == text.Length || !Hex(text[position])) Fail("an invalid Unicode escape");
                            position++;
                        }
                    } else if (c != '"' && c != '\\' && c != '/' && c != 'b' && c != 'f' && c != 'n' && c != 'r' && c != 't') Fail("an invalid string escape");
                }
                Fail("an incomplete string");
            }
            void NumberToken() {
                Take('-');
                if (!Take('0')) {
                    if (position == text.Length || text[position] < '1' || text[position] > '9') Fail("an invalid integer");
                    position++; Digits();
                }
                if (Take('.')) RequiredDigits();
                if (Take('e') || Take('E')) { if (!Take('+')) Take('-'); RequiredDigits(); }
            }
            void RequiredDigits() {
                if (position == text.Length || !Digit(text[position])) Fail("an incomplete number");
                Digits();
            }
            void Digits() { while (position < text.Length && Digit(text[position])) position++; }
            void Literal(string value) {
                if (text.Length - position < value.Length || String.CompareOrdinal(text, position, value, 0, value.Length) != 0) Fail("an invalid literal");
                position += value.Length;
            }
            void WhiteSpace() { while (position < text.Length && Space(text[position])) position++; }
            bool Take(char c) { if (position == text.Length || text[position] != c) return false; position++; return true; }
            void Require(char c) { if (!Take(c)) Fail("a missing or misplaced '" + c + "'"); }
            void Fail(string reason) { throw new SerializationException("JSON contains " + reason + " at character " + position + "."); }
            static bool Space(char c) { return c == ' ' || c == '\t' || c == '\r' || c == '\n'; }
            static bool Digit(char c) { return c >= '0' && c <= '9'; }
            static bool Hex(char c) { return Digit(c) || c >= 'a' && c <= 'f' || c >= 'A' && c <= 'F'; }
        }
    }
}
