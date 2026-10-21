using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace WorkshopWreckage
{
    /// <summary>Minimal JSON reader/writer for the shared, engine-neutral data files
    /// (objects -> Dictionary&lt;string, object&gt;, arrays -> List&lt;object&gt;, numbers -> double).</summary>
    public static class Json
    {
        public static object Parse(string text)
        {
            int i = 0;
            var v = ParseValue(text, ref i);
            return v;
        }

        static void Skip(string s, ref int i)
        {
            while (i < s.Length && char.IsWhiteSpace(s[i])) i++;
        }

        static object ParseValue(string s, ref int i)
        {
            Skip(s, ref i);
            if (i >= s.Length) throw new FormatException("Unexpected end of JSON");
            char c = s[i];
            switch (c)
            {
                case '{': return ParseObject(s, ref i);
                case '[': return ParseArray(s, ref i);
                case '"': return ParseString(s, ref i);
                case 't': i += 4; return true;
                case 'f': i += 5; return false;
                case 'n': i += 4; return null;
                default: return ParseNumber(s, ref i);
            }
        }

        static Dictionary<string, object> ParseObject(string s, ref int i)
        {
            var d = new Dictionary<string, object>();
            i++;
            Skip(s, ref i);
            if (s[i] == '}') { i++; return d; }
            while (true)
            {
                Skip(s, ref i);
                string key = ParseString(s, ref i);
                Skip(s, ref i);
                if (s[i] != ':') throw new FormatException($"Expected ':' at {i}");
                i++;
                d[key] = ParseValue(s, ref i);
                Skip(s, ref i);
                if (s[i] == ',') { i++; continue; }
                if (s[i] == '}') { i++; return d; }
                throw new FormatException($"Expected ',' or '}}' at {i}");
            }
        }

        static List<object> ParseArray(string s, ref int i)
        {
            var l = new List<object>();
            i++;
            Skip(s, ref i);
            if (s[i] == ']') { i++; return l; }
            while (true)
            {
                l.Add(ParseValue(s, ref i));
                Skip(s, ref i);
                if (s[i] == ',') { i++; continue; }
                if (s[i] == ']') { i++; return l; }
                throw new FormatException($"Expected ',' or ']' at {i}");
            }
        }

        static string ParseString(string s, ref int i)
        {
            if (s[i] != '"') throw new FormatException($"Expected string at {i}");
            i++;
            var sb = new StringBuilder();
            while (s[i] != '"')
            {
                char c = s[i++];
                if (c == '\\')
                {
                    char e = s[i++];
                    switch (e)
                    {
                        case 'n': sb.Append('\n'); break;
                        case 't': sb.Append('\t'); break;
                        case 'r': sb.Append('\r'); break;
                        case 'b': sb.Append('\b'); break;
                        case 'f': sb.Append('\f'); break;
                        case 'u': sb.Append((char)Convert.ToInt32(s.Substring(i, 4), 16)); i += 4; break;
                        default: sb.Append(e); break;
                    }
                }
                else sb.Append(c);
            }
            i++;
            return sb.ToString();
        }

        static object ParseNumber(string s, ref int i)
        {
            int start = i;
            while (i < s.Length && "+-0123456789.eE".IndexOf(s[i]) >= 0) i++;
            return double.Parse(s.Substring(start, i - start), NumberStyles.Float, CultureInfo.InvariantCulture);
        }

        // ------------------------------------------------------------------ writing

        public static string Write(object v)
        {
            var sb = new StringBuilder();
            WriteValue(sb, v);
            return sb.ToString();
        }

        static void WriteValue(StringBuilder sb, object v)
        {
            switch (v)
            {
                case null: sb.Append("null"); break;
                case string str: WriteString(sb, str); break;
                case bool b: sb.Append(b ? "true" : "false"); break;
                case double d: sb.Append(d.ToString("R", CultureInfo.InvariantCulture)); break;
                case float f: sb.Append(((double)f).ToString("R", CultureInfo.InvariantCulture)); break;
                case int n: sb.Append(n); break;
                case long n: sb.Append(n); break;
                case System.Collections.IDictionary dict:
                    sb.Append('{');
                    bool first = true;
                    foreach (System.Collections.DictionaryEntry e in dict)
                    {
                        if (!first) sb.Append(',');
                        first = false;
                        WriteString(sb, e.Key.ToString());
                        sb.Append(':');
                        WriteValue(sb, e.Value);
                    }
                    sb.Append('}');
                    break;
                case System.Collections.IEnumerable list:
                    sb.Append('[');
                    bool f2 = true;
                    foreach (var x in list)
                    {
                        if (!f2) sb.Append(',');
                        f2 = false;
                        WriteValue(sb, x);
                    }
                    sb.Append(']');
                    break;
                default: WriteString(sb, v.ToString()); break;
            }
        }

        static void WriteString(StringBuilder sb, string s)
        {
            sb.Append('"');
            foreach (char c in s)
            {
                switch (c)
                {
                    case '"': sb.Append("\\\""); break;
                    case '\\': sb.Append("\\\\"); break;
                    case '\n': sb.Append("\\n"); break;
                    case '\r': sb.Append("\\r"); break;
                    case '\t': sb.Append("\\t"); break;
                    default:
                        if (c < 32) sb.Append("\\u").Append(((int)c).ToString("x4"));
                        else sb.Append(c);
                        break;
                }
            }
            sb.Append('"');
        }

        // ------------------------------------------------------------------ helpers

        public static Dictionary<string, object> Obj(object o) => o as Dictionary<string, object>;
        public static List<object> Arr(object o) => o as List<object> ?? new List<object>();

        public static double Num(Dictionary<string, object> d, string key, double fallback = 0)
            => d != null && d.TryGetValue(key, out var v) && v is double x ? x : fallback;

        public static int Int(Dictionary<string, object> d, string key, int fallback = 0)
            => (int)Math.Round(Num(d, key, fallback));

        public static string Str(Dictionary<string, object> d, string key, string fallback = "")
            => d != null && d.TryGetValue(key, out var v) && v != null ? (v is double x ? x.ToString(CultureInfo.InvariantCulture) : v.ToString()) : fallback;

        public static bool Bool(Dictionary<string, object> d, string key, bool fallback = false)
            => d != null && d.TryGetValue(key, out var v) && v is bool b ? b : fallback;

        public static float F(object o) => o is double d ? (float)d : 0f;
    }
}
