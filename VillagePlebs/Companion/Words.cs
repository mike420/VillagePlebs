using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using static VillagePlebs.Shared.Translations;

namespace VillagePlebs
{
    internal static class Words
    {
        private const char Start = '\u0001', Sep = '\u0002', End = '\u0003', Lower = '\u0004';

        public static string P(string key, params object[] args)
        {
            var sb = new StringBuilder().Append(Start).Append(key);
            foreach (object a in args ?? new object[0])
                sb.Append(Sep).Append(Convert.ToString(a, CultureInfo.InvariantCulture));
            return sb.Append(End).ToString();
        }

        public static string Low(string value) => Lower + (value ?? "");

        public static bool Packed(string text) => text != null && text.IndexOf(Start) >= 0;

        public static string Show(string text) => Render(text, false);

        public static string English(string text) => Render(text, true);

        public static string Mid(string s)
        {
            if (string.IsNullOrEmpty(s)) return s;
            string lang = Localization.instance != null ? Localization.instance.GetSelectedLanguage() : "English";
            if (lang == "German") return s;
            return lang == "Turkish" ? s.ToLower(new CultureInfo("tr-TR")) : s.ToLowerInvariant();
        }

        private static string Render(string text, bool english)
        {
            if (string.IsNullOrEmpty(text) || text.IndexOf(Start) < 0) return StripMarks(text);
            int i = 0;
            return Sequence(text, ref i, english, false);
        }

        private static string Sequence(string s, ref int i, bool english, bool inValue)
        {
            var sb = new StringBuilder();
            while (i < s.Length)
            {
                char ch = s[i];
                if (inValue && (ch == Sep || ch == End)) break;
                if (ch == Start) { i++; sb.Append(Part(s, ref i, english)); continue; }
                if (ch != Lower) sb.Append(ch);
                i++;
            }
            return sb.ToString();
        }

        private static string Part(string s, ref int i, bool english)
        {
            int k = i;
            while (i < s.Length && s[i] != Sep && s[i] != End) i++;
            string key = s.Substring(k, i - k);
            var args = new List<object>();
            while (i < s.Length && s[i] == Sep)
            {
                i++;
                bool low = i < s.Length && s[i] == Lower;
                if (low) i++;
                string v = Sequence(s, ref i, english, true);
                if (v.IndexOf('$') >= 0 && Localization.instance != null) v = Localization.instance.Localize(v);
                args.Add(low ? (english ? v.ToLowerInvariant() : Mid(v)) : v);
            }
            if (i < s.Length && s[i] == End) i++;
            return english ? EnglishText(key, args.ToArray()) : T(key, args.ToArray());
        }

        private static string StripMarks(string text) => text == null || text.IndexOf(Lower) < 0 ? text : text.Replace(Lower.ToString(), "");

        private static Dictionary<string, string> _english;

        private static string EnglishText(string key, object[] args)
        {
            if (_english == null) _english = ReadEnglish();
            string text = _english.TryGetValue(key, out string t) ? t : key;
            if (args.Length == 0) return text;
            try { return string.Format(text, args); }
            catch (FormatException) { return text; }
        }

        private static Dictionary<string, string> ReadEnglish()
        {
            var map = new Dictionary<string, string>();
            try
            {
                using (Stream stream = typeof(Words).Assembly.GetManifestResourceStream("translations/English.json"))
                using (var reader = new StreamReader(stream))
                    foreach (Match m in Regex.Matches(reader.ReadToEnd(), "\"((?:[^\"\\\\]|\\\\.)*)\"\\s*:\\s*\"((?:[^\"\\\\]|\\\\.)*)\""))
                        map[Regex.Unescape(m.Groups[1].Value)] = Regex.Unescape(m.Groups[2].Value);
            }
            catch (Exception) { }
            return map;
        }

        public static void Forget() => _english = null;
    }
}
