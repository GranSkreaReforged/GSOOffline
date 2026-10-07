using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Reflection;
using System.Text;

namespace GSOOffline
{
    /// <summary>
    /// Minimal reflection JSON for the plugin's own data classes. UnityEngine.JsonUtility silently skips
    /// lists of classes defined outside Unity-built assemblies (i.e. in this plugin), which dropped
    /// inventories from saves. Supports public instance fields of primitive, string, enum, List&lt;T&gt;,
    /// T[] and nested class types; unknown JSON members are ignored, missing ones keep their defaults.
    /// </summary>
    internal static class Json
    {
        // ---- serialize -----------------------------------------------------------------------

        public static string Serialize(object value)
        {
            var sb = new StringBuilder();
            Write(sb, value, 0);
            return sb.ToString();
        }

        private static void Indent(StringBuilder sb, int depth) => sb.Append('\n').Append(' ', depth * 2);

        private static void Write(StringBuilder sb, object v, int depth)
        {
            switch (v)
            {
                case null: sb.Append("null"); return;
                case string s: WriteString(sb, s); return;
                case bool b: sb.Append(b ? "true" : "false"); return;
                case float f: sb.Append(f.ToString("R", CultureInfo.InvariantCulture)); return;
                case double d: sb.Append(d.ToString("R", CultureInfo.InvariantCulture)); return;
                case Enum e: sb.Append(Convert.ToInt64(e).ToString(CultureInfo.InvariantCulture)); return;
                case IConvertible c when v.GetType().IsPrimitive:
                    sb.Append(c.ToString(CultureInfo.InvariantCulture)); return;
                case IList list:
                    sb.Append('[');
                    for (int i = 0; i < list.Count; i++)
                    {
                        if (i > 0) sb.Append(',');
                        Indent(sb, depth + 1);
                        Write(sb, list[i], depth + 1);
                    }
                    if (list.Count > 0) Indent(sb, depth);
                    sb.Append(']');
                    return;
            }
            sb.Append('{');
            bool first = true;
            foreach (var f in Fields(v.GetType()))
            {
                if (!first) sb.Append(',');
                first = false;
                Indent(sb, depth + 1);
                WriteString(sb, f.Name);
                sb.Append(": ");
                Write(sb, f.GetValue(v), depth + 1);
            }
            if (!first) Indent(sb, depth);
            sb.Append('}');
        }

        private static void WriteString(StringBuilder sb, string s)
        {
            sb.Append('"');
            foreach (char ch in s)
            {
                switch (ch)
                {
                    case '"': sb.Append("\\\""); break;
                    case '\\': sb.Append("\\\\"); break;
                    case '\n': sb.Append("\\n"); break;
                    case '\r': sb.Append("\\r"); break;
                    case '\t': sb.Append("\\t"); break;
                    default:
                        if (ch < 0x20) sb.Append("\\u").Append(((int)ch).ToString("x4"));
                        else sb.Append(ch);
                        break;
                }
            }
            sb.Append('"');
        }

        private static IEnumerable<FieldInfo> Fields(Type t)
        {
            foreach (var f in t.GetFields(BindingFlags.Public | BindingFlags.Instance))
                if (!f.IsInitOnly && !f.IsNotSerialized) yield return f;
        }

        // ---- deserialize ---------------------------------------------------------------------

        public static T Deserialize<T>(string json) where T : new()
        {
            var p = new Parser(json);
            object raw = p.ParseValue();
            p.SkipWs();
            if (!p.AtEnd) throw p.Error("trailing characters");
            return (T)FromRaw(raw, typeof(T));
        }

        private static object FromRaw(object raw, Type t)
        {
            if (raw == null) return t.IsValueType ? Activator.CreateInstance(t) : null;
            if (t == typeof(object)) return raw;
            if (t == typeof(string)) return raw as string ?? Convert.ToString(raw, CultureInfo.InvariantCulture);
            if (t == typeof(bool)) return raw is bool b ? b : Convert.ToDouble(raw, CultureInfo.InvariantCulture) != 0;
            if (t.IsEnum) return Enum.ToObject(t, Convert.ToInt64(raw, CultureInfo.InvariantCulture));
            if (t.IsPrimitive || t == typeof(decimal)) return Convert.ChangeType(raw, t, CultureInfo.InvariantCulture);

            if (t.IsArray)
            {
                var items = (List<object>)raw;
                var arr = Array.CreateInstance(t.GetElementType(), items.Count);
                for (int i = 0; i < items.Count; i++) arr.SetValue(FromRaw(items[i], t.GetElementType()), i);
                return arr;
            }
            if (t.IsGenericType && t.GetGenericTypeDefinition() == typeof(List<>))
            {
                var elem = t.GetGenericArguments()[0];
                var list = (IList)Activator.CreateInstance(t);
                foreach (var item in (List<object>)raw) list.Add(FromRaw(item, elem));
                return list;
            }

            var obj = Activator.CreateInstance(t);
            var members = (Dictionary<string, object>)raw;
            foreach (var f in Fields(t))
                if (members.TryGetValue(f.Name, out var mv))
                    f.SetValue(obj, FromRaw(mv, f.FieldType));
            return obj;
        }

        private sealed class Parser
        {
            private readonly string s;
            private int i;

            public Parser(string s)
            {
                this.s = s;
            }

            public bool AtEnd => i >= s.Length;

            public FormatException Error(string what) => new FormatException($"JSON: {what} at offset {i}");

            public void SkipWs()
            {
                while (i < s.Length && char.IsWhiteSpace(s[i])) i++;
            }

            public object ParseValue()
            {
                SkipWs();
                if (AtEnd) throw Error("unexpected end");
                char c = s[i];
                if (c == '{') return ParseObject();
                if (c == '[') return ParseArray();
                if (c == '"') return ParseString();
                if (Match("true")) return true;
                if (Match("false")) return false;
                if (Match("null")) return null;
                return ParseNumber();
            }

            private bool Match(string word)
            {
                if (string.CompareOrdinal(s, i, word, 0, word.Length) != 0) return false;
                i += word.Length;
                return true;
            }

            private void Expect(char c)
            {
                SkipWs();
                if (AtEnd || s[i] != c) throw Error($"expected '{c}'");
                i++;
            }

            private Dictionary<string, object> ParseObject()
            {
                var d = new Dictionary<string, object>();
                Expect('{');
                SkipWs();
                if (!AtEnd && s[i] == '}') { i++; return d; }
                while (true)
                {
                    SkipWs();
                    string key = ParseString();
                    Expect(':');
                    d[key] = ParseValue();
                    SkipWs();
                    if (!AtEnd && s[i] == ',') { i++; continue; }
                    Expect('}');
                    return d;
                }
            }

            private List<object> ParseArray()
            {
                var l = new List<object>();
                Expect('[');
                SkipWs();
                if (!AtEnd && s[i] == ']') { i++; return l; }
                while (true)
                {
                    l.Add(ParseValue());
                    SkipWs();
                    if (!AtEnd && s[i] == ',') { i++; continue; }
                    Expect(']');
                    return l;
                }
            }

            private string ParseString()
            {
                if (AtEnd || s[i] != '"') throw Error("expected string");
                i++;
                var sb = new StringBuilder();
                while (true)
                {
                    if (AtEnd) throw Error("unterminated string");
                    char c = s[i++];
                    if (c == '"') return sb.ToString();
                    if (c != '\\') { sb.Append(c); continue; }
                    char e = s[i++];
                    switch (e)
                    {
                        case 'n': sb.Append('\n'); break;
                        case 'r': sb.Append('\r'); break;
                        case 't': sb.Append('\t'); break;
                        case 'b': sb.Append('\b'); break;
                        case 'f': sb.Append('\f'); break;
                        case 'u':
                            sb.Append((char)int.Parse(s.Substring(i, 4), NumberStyles.HexNumber));
                            i += 4;
                            break;
                        default: sb.Append(e); break;
                    }
                }
            }

            private object ParseNumber()
            {
                int start = i;
                while (i < s.Length && "+-0123456789.eE".IndexOf(s[i]) >= 0) i++;
                string num = s.Substring(start, i - start);
                if (num.Length == 0) throw Error("unexpected character");
                if (long.TryParse(num, NumberStyles.Integer, CultureInfo.InvariantCulture, out long l)) return l;
                return double.Parse(num, NumberStyles.Float, CultureInfo.InvariantCulture);
            }
        }
    }
}
