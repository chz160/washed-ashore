using System.Globalization;
using System.IO;
using System.Text;

namespace WashedAshore.Fish
{
    /// <summary>
    /// The one gate for fish result files (team-lead / f-qa: the literal "F1" format bug). Numbers are written in the
    /// invariant culture, and every file or JSON line is checked by a strict parser before it is written: a malformed
    /// writer throws at the source instead of leaving a file f-qa can't read. Used by the PlayMode result writer, the
    /// probe, the perf walk and the editor tools.
    /// </summary>
    public static class FishJson
    {
        static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

        /// <summary>A number with <paramref name="decimals"/> places, invariant culture; NaN/infinity become null.</summary>
        public static string Num(double v, int decimals = 3) =>
            double.IsNaN(v) || double.IsInfinity(v) ? "null" : v.ToString("F" + decimals, Inv);

        /// <summary>A quoted, escaped JSON string.</summary>
        public static string Str(string s)
        {
            if (s == null) return "null";
            var sb = new StringBuilder(s.Length + 2).Append('"');
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
                        if (c < 0x20) sb.Append("\\u").Append(((int)c).ToString("x4", Inv));
                        else sb.Append(c);
                        break;
                }
            }
            return sb.Append('"').ToString();
        }

        /// <summary>Writes <paramref name="json"/> to <paramref name="path"/> if it parses; throws (writing nothing) if not.</summary>
        public static void WriteFile(string path, string json)
        {
            if (!TryValidate(json, out string error))
                throw new System.FormatException($"FishJson: refusing to write {Path.GetFileName(path)}: {error}");
            string dir = Path.GetDirectoryName(Path.GetFullPath(path));
            if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
            File.WriteAllText(path, json, new UTF8Encoding(false));
        }

        /// <summary>Validates every non-empty line of a JSON-lines text.</summary>
        public static bool TryValidateLines(string text, out string error)
        {
            int n = 0;
            foreach (var line in text.Split('\n'))
            {
                n++;
                string l = line.TrimEnd('\r');
                if (l.Trim().Length == 0) continue;
                if (!TryValidate(l, out error)) { error = $"line {n}: {error}"; return false; }
            }
            error = null;
            return true;
        }

        /// <summary>Strict RFC 8259 check: one value, nothing after it, no trailing commas, no bare words but true/false/null.</summary>
        public static bool TryValidate(string json, out string error)
        {
            int i = 0;
            error = null;
            if (json == null) { error = "null text"; return false; }
            if (!Value(json, ref i, ref error, 0)) return false;
            Ws(json, ref i);
            if (i != json.Length) { error = At(json, i, "text after the value"); return false; }
            return true;
        }

        static bool Value(string s, ref int i, ref string error, int depth)
        {
            if (depth > 256) { error = "nesting too deep"; return false; }
            Ws(s, ref i);
            if (i >= s.Length) { error = "unexpected end"; return false; }
            char c = s[i];
            if (c == '{')
            {
                i++;
                Ws(s, ref i);
                if (i < s.Length && s[i] == '}') { i++; return true; }
                while (true)
                {
                    Ws(s, ref i);
                    if (i >= s.Length || s[i] != '"') { error = At(s, i, "expected a key"); return false; }
                    if (!String(s, ref i, ref error)) return false;
                    Ws(s, ref i);
                    if (i >= s.Length || s[i] != ':') { error = At(s, i, "expected ':'"); return false; }
                    i++;
                    if (!Value(s, ref i, ref error, depth + 1)) return false;
                    Ws(s, ref i);
                    if (i < s.Length && s[i] == ',') { i++; continue; }
                    if (i < s.Length && s[i] == '}') { i++; return true; }
                    error = At(s, i, "expected ',' or '}'");
                    return false;
                }
            }
            if (c == '[')
            {
                i++;
                Ws(s, ref i);
                if (i < s.Length && s[i] == ']') { i++; return true; }
                while (true)
                {
                    if (!Value(s, ref i, ref error, depth + 1)) return false;
                    Ws(s, ref i);
                    if (i < s.Length && s[i] == ',') { i++; continue; }
                    if (i < s.Length && s[i] == ']') { i++; return true; }
                    error = At(s, i, "expected ',' or ']'");
                    return false;
                }
            }
            if (c == '"') return String(s, ref i, ref error);
            if (c == '-' || (c >= '0' && c <= '9')) return Number(s, ref i, ref error);
            foreach (var word in new[] { "true", "false", "null" })
                if (string.CompareOrdinal(s, i, word, 0, word.Length) == 0) { i += word.Length; return true; }
            error = At(s, i, "unexpected character");
            return false;
        }

        static bool String(string s, ref int i, ref string error)
        {
            i++;
            while (i < s.Length)
            {
                char c = s[i++];
                if (c == '"') return true;
                if (c < 0x20) { error = At(s, i - 1, "control character in a string"); return false; }
                if (c != '\\') continue;
                if (i >= s.Length) break;
                char e = s[i++];
                if (e == 'u')
                {
                    for (int k = 0; k < 4; k++, i++)
                        if (i >= s.Length || !Uri.IsHexDigit(s[i])) { error = At(s, i, "bad \\u escape"); return false; }
                }
                else if ("\"\\/bfnrt".IndexOf(e) < 0) { error = At(s, i - 1, "bad escape"); return false; }
            }
            error = "unterminated string";
            return false;
        }

        static bool Number(string s, ref int i, ref string error)
        {
            int start = i;
            if (s[i] == '-') i++;
            if (i >= s.Length || !char.IsDigit(s[i])) { error = At(s, start, "bad number"); return false; }
            if (s[i] == '0') i++;
            else while (i < s.Length && char.IsDigit(s[i])) i++;
            if (i < s.Length && s[i] == '.')
            {
                i++;
                if (i >= s.Length || !char.IsDigit(s[i])) { error = At(s, start, "bad fraction"); return false; }
                while (i < s.Length && char.IsDigit(s[i])) i++;
            }
            if (i < s.Length && (s[i] == 'e' || s[i] == 'E'))
            {
                i++;
                if (i < s.Length && (s[i] == '+' || s[i] == '-')) i++;
                if (i >= s.Length || !char.IsDigit(s[i])) { error = At(s, start, "bad exponent"); return false; }
                while (i < s.Length && char.IsDigit(s[i])) i++;
            }
            return true;
        }

        static void Ws(string s, ref int i)
        {
            while (i < s.Length && (s[i] == ' ' || s[i] == '\t' || s[i] == '\n' || s[i] == '\r')) i++;
        }

        static string At(string s, int i, string what)
        {
            int a = System.Math.Max(0, i - 30), b = System.Math.Min(s.Length, i + 30);
            return $"{what} at {i}: ...{s.Substring(a, b - a)}...";
        }

        static class Uri
        {
            public static bool IsHexDigit(char c) => (c >= '0' && c <= '9') || (c >= 'a' && c <= 'f') || (c >= 'A' && c <= 'F');
        }
    }
}
