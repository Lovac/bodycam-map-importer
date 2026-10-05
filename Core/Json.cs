// Small JSON reader and writer for the installer files.
using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;

namespace BodycamMapInstaller.Core
{
    public sealed class JsonObject : Dictionary<string, object>
    {
        public JsonObject() : base(StringComparer.Ordinal) { }

        public string Str(string key)
        {
            object v;
            return TryGetValue(key, out v) ? v as string : null;
        }

        public bool Has(string key) { return ContainsKey(key); }
    }

    public sealed class JsonOut : List<KeyValuePair<string, object>>
    {
        public JsonOut Set(string key, object value)
        {
            Add(new KeyValuePair<string, object>(key, value));
            return this;
        }
    }

    public static class Json
    {
        public static object Parse(string text)
        {
            int i = 0;
            if (text.Length > 0 && text[0] == (char)0xFEFF) i = 1;
            object v = Value(text, ref i, 0);
            Ws(text, ref i);
            if (i != text.Length) throw new InvalidDataException("json: text after the value at " + i);
            return v;
        }

        public static JsonObject ParseObject(byte[] utf8)
        {
            string text;
            try { text = new UTF8Encoding(false, true).GetString(utf8); }
            catch (ArgumentException) { throw new InvalidDataException("json: not UTF-8"); }
            JsonObject o = Parse(text) as JsonObject;
            if (o == null) throw new InvalidDataException("json: not an object");
            return o;
        }

        static void Ws(string t, ref int i)
        {
            while (i < t.Length && (t[i] == ' ' || t[i] == '\t' || t[i] == '\r' || t[i] == '\n')) i++;
        }

        static object Value(string t, ref int i, int depth)
        {
            if (depth > 64) throw new InvalidDataException("json: nesting too deep");
            Ws(t, ref i);
            if (i >= t.Length) throw new InvalidDataException("json: unexpected end");
            char c = t[i];
            if (c == '{')
            {
                i++;
                JsonObject o = new JsonObject();
                Ws(t, ref i);
                if (i < t.Length && t[i] == '}') { i++; return o; }
                while (true)
                {
                    Ws(t, ref i);
                    if (i >= t.Length || t[i] != '"') throw new InvalidDataException("json: key expected at " + i);
                    string key = Str(t, ref i);
                    Ws(t, ref i);
                    if (i >= t.Length || t[i] != ':') throw new InvalidDataException("json: ':' expected at " + i);
                    i++;
                    object v = Value(t, ref i, depth + 1);
                    if (o.ContainsKey(key)) throw new InvalidDataException("json: duplicate key " + key);
                    o[key] = v;
                    Ws(t, ref i);
                    if (i < t.Length && t[i] == ',') { i++; continue; }
                    if (i < t.Length && t[i] == '}') { i++; return o; }
                    throw new InvalidDataException("json: ',' or '}' expected at " + i);
                }
            }
            if (c == '[')
            {
                i++;
                List<object> a = new List<object>();
                Ws(t, ref i);
                if (i < t.Length && t[i] == ']') { i++; return a; }
                while (true)
                {
                    a.Add(Value(t, ref i, depth + 1));
                    Ws(t, ref i);
                    if (i < t.Length && t[i] == ',') { i++; continue; }
                    if (i < t.Length && t[i] == ']') { i++; return a; }
                    throw new InvalidDataException("json: ',' or ']' expected at " + i);
                }
            }
            if (c == '"') return Str(t, ref i);
            if (Lit(t, ref i, "true")) return true;
            if (Lit(t, ref i, "false")) return false;
            if (Lit(t, ref i, "null")) return null;
            int s = i;
            if (i < t.Length && t[i] == '-') i++;
            while (i < t.Length && (char.IsDigit(t[i]) || t[i] == '.' || t[i] == 'e' || t[i] == 'E' || t[i] == '+' || t[i] == '-')) i++;
            double d;
            if (i == s || !double.TryParse(t.Substring(s, i - s), NumberStyles.Float, CultureInfo.InvariantCulture, out d))
                throw new InvalidDataException("json: value expected at " + s);
            return d;
        }

        static bool Lit(string t, ref int i, string lit)
        {
            if (string.CompareOrdinal(t, i, lit, 0, lit.Length) != 0) return false;
            i += lit.Length;
            return true;
        }

        static string Str(string t, ref int i)
        {
            i++;
            StringBuilder sb = new StringBuilder();
            while (true)
            {
                if (i >= t.Length) throw new InvalidDataException("json: unterminated string");
                char c = t[i++];
                if (c == '"') return sb.ToString();
                if (c < 0x20) throw new InvalidDataException("json: control character in a string");
                if (c != '\\') { sb.Append(c); continue; }
                if (i >= t.Length) throw new InvalidDataException("json: unterminated escape");
                char e = t[i++];
                switch (e)
                {
                    case '"': sb.Append('"'); break;
                    case '\\': sb.Append('\\'); break;
                    case '/': sb.Append('/'); break;
                    case 'b': sb.Append('\b'); break;
                    case 'f': sb.Append('\f'); break;
                    case 'n': sb.Append('\n'); break;
                    case 'r': sb.Append('\r'); break;
                    case 't': sb.Append('\t'); break;
                    case 'u':
                        if (i + 4 > t.Length) throw new InvalidDataException("json: short \\u escape");
                        sb.Append((char)int.Parse(t.Substring(i, 4), NumberStyles.HexNumber, CultureInfo.InvariantCulture));
                        i += 4;
                        break;
                    default: throw new InvalidDataException("json: unknown escape \\" + e);
                }
            }
        }

        public static byte[] ToBytes(object value)
        {
            StringBuilder sb = new StringBuilder();
            Write(sb, value, 0);
            sb.Append('\n');
            return Encoding.ASCII.GetBytes(sb.ToString());
        }

        static bool IsScalar(object v)
        {
            return v == null || v is string || v is bool || v is int || v is long || v is double;
        }

        static void Write(StringBuilder sb, object v, int indent)
        {
            if (v == null) { sb.Append("null"); return; }
            string s = v as string;
            if (s != null) { Quote(sb, s); return; }
            if (v is bool) { sb.Append((bool)v ? "true" : "false"); return; }
            if (v is int) { sb.Append(((int)v).ToString(CultureInfo.InvariantCulture)); return; }
            if (v is long) { sb.Append(((long)v).ToString(CultureInfo.InvariantCulture)); return; }
            if (v is double) { sb.Append(((double)v).ToString("R", CultureInfo.InvariantCulture)); return; }
            JsonOut o = v as JsonOut;
            if (o != null)
            {
                if (o.Count == 0) { sb.Append("{}"); return; }
                sb.Append("{\n");
                for (int k = 0; k < o.Count; k++)
                {
                    sb.Append(' ', (indent + 1) * 2);
                    Quote(sb, o[k].Key);
                    sb.Append(": ");
                    Write(sb, o[k].Value, indent + 1);
                    sb.Append(k + 1 < o.Count ? ",\n" : "\n");
                }
                sb.Append(' ', indent * 2).Append('}');
                return;
            }
            IEnumerable e = v as IEnumerable;
            if (e != null)
            {
                List<object> items = new List<object>();
                foreach (object x in e) items.Add(x);
                bool flat = true;
                foreach (object x in items) if (!IsScalar(x)) flat = false;
                if (items.Count == 0) { sb.Append("[]"); return; }
                if (flat)
                {
                    sb.Append('[');
                    for (int k = 0; k < items.Count; k++) { if (k > 0) sb.Append(", "); Write(sb, items[k], indent); }
                    sb.Append(']');
                    return;
                }
                sb.Append("[\n");
                for (int k = 0; k < items.Count; k++)
                {
                    sb.Append(' ', (indent + 1) * 2);
                    Write(sb, items[k], indent + 1);
                    sb.Append(k + 1 < items.Count ? ",\n" : "\n");
                }
                sb.Append(' ', indent * 2).Append(']');
                return;
            }
            throw new ArgumentException("json: cannot write " + v.GetType().Name);
        }

        static void Quote(StringBuilder sb, string s)
        {
            sb.Append('"');
            foreach (char c in s)
            {
                if (c == '"') sb.Append("\\\"");
                else if (c == '\\') sb.Append("\\\\");
                else if (c == '\n') sb.Append("\\n");
                else if (c == '\r') sb.Append("\\r");
                else if (c == '\t') sb.Append("\\t");
                else if (c < 0x20 || c > 0x7E) sb.Append("\\u").Append(((int)c).ToString("x4"));
                else sb.Append(c);
            }
            sb.Append('"');
        }
    }
}
