using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using BepInEx;
using BepInEx.Configuration;
using HarmonyLib;
using UnityEngine;

namespace VillagePlebs.Shared
{
    internal static class Translations
    {
        public static readonly string[] Shipped = { "English", "Russian", "German", "Chinese", "Portuguese_Brazilian", "Spanish", "French", "Polish", "Turkish" };

        private static readonly Dictionary<string, Dictionary<string, string>> Languages = new Dictionary<string, Dictionary<string, string>>(StringComparer.OrdinalIgnoreCase);
        private static Dictionary<string, string> _builtIn = new Dictionary<string, string>();
        private static Dictionary<string, string> _english = new Dictionary<string, string>();
        private static string _folder, _mod;
        private static bool _hooked;

        private static FileSystemWatcher _watcher;
        private static volatile bool _changed;
        private static float _reloadAt = -1f;
        private static readonly MethodInfo AddWord = AccessTools.Method(typeof(Localization), "AddWord");
        private static readonly FieldInfo Cache = AccessTools.Field(typeof(Localization), "m_cache");
        private static readonly FieldInfo GameWords = AccessTools.Field(typeof(Localization), "m_translations");

        private static Dictionary<string, string> _given = new Dictionary<string, string>();
        private static Dictionary<string, string> _files = new Dictionary<string, string>();

        private static readonly Dictionary<string, Dictionary<string, Dictionary<string, string>>> Runtime = new Dictionary<string, Dictionary<string, Dictionary<string, string>>>();
        private static readonly HashSet<string> _runtimeGiven = new HashSet<string>();

        private sealed class Blank { public string Tag; public Func<string> Value; public Action Unhook; }
        private static readonly Dictionary<string, List<Blank>> Fills = new Dictionary<string, List<Blank>>();

        public static void Load(BaseUnityPlugin plugin)
        {
            _mod = plugin.Info.Metadata.Name;
            using (Stream res = plugin.GetType().Assembly.GetManifestResourceStream("translations/English.json"))
                _builtIn = res != null ? Parse(new StreamReader(res).ReadToEnd()) : new Dictionary<string, string>();
            _folder = Path.Combine(Paths.ConfigPath, _mod, "Translations");
            try { Directory.CreateDirectory(_folder); }
            catch (Exception e) { Debug.LogWarning($"[{_mod}] Could not make the translations folder {_folder}: {e.Message}"); }
            ReadFiles();
            int missing = _builtIn.Keys.Count(k => !(Languages.TryGetValue("English", out var file) && file.ContainsKey(k)));
            if (missing > 0) Debug.LogWarning($"[{_mod}] {missing} English translation(s) missing from {_folder}, using the built-in text for them.");
            Watch();
            if (!_hooked) { Localization.OnLanguageChange += Apply; _hooked = true; }
            Apply();
        }

        public static void Tick()
        {
            if (_changed) { _changed = false; _reloadAt = Time.unscaledTime + 0.5f; }
            if (_reloadAt < 0f || Time.unscaledTime < _reloadAt) return;
            _reloadAt = -1f;
            try
            {
                ReadFiles();
                Apply();
                Debug.Log($"[{_mod}] Translations read again from {_folder}");
            }
            catch (Exception e) { Debug.LogWarning($"[{_mod}] Could not read the translations again: {e.Message}"); }
        }

        private static void ReadFiles()
        {
            Languages.Clear();
            if (_folder != null && Directory.Exists(_folder))
            {
                string root = Path.GetFullPath(_folder).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
                foreach (string path in Directory.GetFiles(_folder, "*.json", SearchOption.AllDirectories))
                {
                    string folder = Path.GetDirectoryName(Path.GetFullPath(path));
                    if (string.Equals(folder, root, StringComparison.OrdinalIgnoreCase))
                    {
                        Debug.LogWarning($"[{_mod}] Skipping {path}: translation files must be in a language folder, e.g. Translations{Path.DirectorySeparatorChar}English.");
                        continue;
                    }
                    string language = new DirectoryInfo(folder).Name;
                    try
                    {
                        if (!Languages.TryGetValue(language, out var words)) Languages[language] = words = new Dictionary<string, string>();
                        foreach (var kv in Parse(File.ReadAllText(path))) words[kv.Key] = kv.Value;
                    }
                    catch (Exception e) { Debug.LogWarning($"[{_mod}] Could not read {path}: {e.Message}"); }
                }
            }
            _english = new Dictionary<string, string>(_builtIn);
            if (Languages.TryGetValue("English", out var english)) foreach (var kv in english) _english[kv.Key] = kv.Value;
        }

        private static void Watch()
        {
            _watcher?.Dispose();
            _watcher = null;
            if (_folder == null || !Directory.Exists(_folder)) return;
            try
            {
                _watcher = new FileSystemWatcher(_folder, "*.json")
                {
                    IncludeSubdirectories = true,
                    NotifyFilter = NotifyFilters.FileName | NotifyFilters.DirectoryName | NotifyFilters.LastWrite,
                };
                _watcher.Changed += (s, e) => _changed = true;
                _watcher.Created += (s, e) => _changed = true;
                _watcher.Deleted += (s, e) => _changed = true;
                _watcher.Renamed += (s, e) => _changed = true;
                _watcher.EnableRaisingEvents = true;
            }
            catch (Exception e) { Debug.LogWarning($"[{_mod}] Could not watch the translation files: {e.Message}"); }
        }

        public static void Unload()
        {
            _watcher?.Dispose();
            _watcher = null;
            if (_hooked) { Localization.OnLanguageChange -= Apply; _hooked = false; }
            foreach (List<Blank> blanks in Fills.Values) foreach (Blank b in blanks) b.Unhook?.Invoke();
            Fills.Clear();
            Runtime.Clear();
            Localization loc = _runtimeGiven.Count > 0 ? Localization.instance : null;
            if (loc != null && AddWord != null)
            {
                var game = GameWords?.GetValue(loc) as Dictionary<string, string>;
                foreach (string key in _runtimeGiven)
                    if (_files.TryGetValue(key, out string text)) AddWord.Invoke(loc, new object[] { key, text });
                    else game?.Remove(key);
                EvictCache(loc);
            }
            _runtimeGiven.Clear();
        }

        public static string T(string key, params object[] args)
        {
            string text = Localization.instance != null ? Localization.instance.Localize("$" + key) : null;
            if (string.IsNullOrEmpty(text) || text == "[" + key + "]") text = _english.TryGetValue(key, out var en) ? FillIn(key, en) : key;
            if (args == null || args.Length == 0) return text;
            try { return string.Format(text, args); }
            catch (FormatException) { return _english.TryGetValue(key, out var en) ? SafeFormat(FillIn(key, en), args) : text; }
        }

        private static string SafeFormat(string text, object[] args) { try { return string.Format(text, args); } catch (FormatException) { return text; } }

        public static void Fill<TValue>(string key, string placeholder, ConfigEntry<TValue> setting, Func<TValue, string> format = null)
        {
            if (setting == null) return;
            Fill(key, placeholder, () => format != null ? format(setting.Value) : Text(setting.Value), setting);
        }

        public static void Fill(string key, string placeholder, Func<string> value, params ConfigEntryBase[] settings)
        {
            if (string.IsNullOrEmpty(key) || string.IsNullOrEmpty(placeholder) || value == null) return;
            key = key.TrimStart('$');
            string tag = placeholder.StartsWith("{") ? placeholder : "{" + placeholder + "}";
            if (!Fills.TryGetValue(key, out List<Blank> blanks)) Fills[key] = blanks = new List<Blank>();
            int old = blanks.FindIndex(b => b.Tag == tag);
            if (old >= 0) { blanks[old].Unhook?.Invoke(); blanks.RemoveAt(old); }
            var blank = new Blank { Tag = tag, Value = value };
            if (settings != null)
            {
                EventHandler changed = (s, e) => Show(key);
                foreach (ConfigEntryBase setting in settings.Where(s => s != null).Distinct())
                {
                    EventInfo ev = setting.GetType().GetEvent("SettingChanged");
                    if (ev == null) continue;
                    ev.AddEventHandler(setting, changed);
                    blank.Unhook += () => ev.RemoveEventHandler(setting, changed);
                }
            }
            blanks.Add(blank);
            Show(key);
        }

        public static void Refresh()
        {
            Localization loc = Localization.instance;
            if (loc == null || AddWord == null) return;
            foreach (string key in Fills.Keys) if (_given.TryGetValue(key, out string text)) AddWord.Invoke(loc, new object[] { key, FillIn(key, text) });
            EvictCache(loc);
        }

        private static void Show(string key)
        {
            Localization loc = Localization.instance;
            if (loc == null || AddWord == null || !_given.TryGetValue(key, out string text)) return;
            AddWord.Invoke(loc, new object[] { key, FillIn(key, text) });
            EvictCache(loc);
        }

        private static string FillIn(string key, string text)
        {
            if (text == null || !Fills.TryGetValue(key, out List<Blank> blanks)) return text;
            foreach (Blank b in blanks)
            {
                if (text.IndexOf(b.Tag, StringComparison.Ordinal) < 0) continue;
                try { text = text.Replace(b.Tag, b.Value() ?? ""); }
                catch (Exception e) { Debug.LogWarning($"Translations: filling {b.Tag} in {key}: {e.Message}"); }
            }
            return text;
        }

        private static string Text(object value) => value is IFormattable f ? f.ToString(null, CultureInfo.InvariantCulture) : value?.ToString() ?? "";

        public static void AddWords(string group, string language, IDictionary<string, string> words)
        {
            if (string.IsNullOrEmpty(group) || string.IsNullOrEmpty(language)) return;
            SetWords(group, language, words);
            Apply();
        }

        public static void AddWords(string group, IDictionary<string, Dictionary<string, string>> languages)
        {
            if (string.IsNullOrEmpty(group)) return;
            Runtime.Remove(group);
            if (languages != null) foreach (var kv in languages) if (!string.IsNullOrEmpty(kv.Key)) SetWords(group, kv.Key, kv.Value);
            Apply();
        }

        public static void RemoveWords(string group)
        {
            if (group != null && Runtime.Remove(group)) Apply();
        }

        public static Dictionary<string, string> ParseJson(string json) => string.IsNullOrEmpty(json) ? new Dictionary<string, string>() : Parse(json);

        private static void SetWords(string group, string language, IDictionary<string, string> words)
        {
            if (!Runtime.TryGetValue(group, out var languages))
                Runtime[group] = languages = new Dictionary<string, Dictionary<string, string>>(StringComparer.OrdinalIgnoreCase);
            if (words == null || words.Count == 0) { languages.Remove(language); return; }
            var copy = new Dictionary<string, string>();
            foreach (var kv in words) if (!string.IsNullOrEmpty(kv.Key) && kv.Value != null) copy[kv.Key.TrimStart('$')] = kv.Value;
            languages[language] = copy;
        }

        private static void AddRuntime(Dictionary<string, string> words, string language)
        {
            foreach (var languages in Runtime.Values)
                if (languages.TryGetValue(language, out var group)) foreach (var kv in group) words[kv.Key] = kv.Value;
        }

        private static void Apply()
        {
            Localization loc = Localization.instance;
            if (loc == null || AddWord == null) return;
            string language = loc.GetSelectedLanguage();
            var words = new Dictionary<string, string>(_english);
            var files = new Dictionary<string, string>(_english);
            AddRuntime(words, "English");
            if (!string.Equals(language, "English", StringComparison.OrdinalIgnoreCase))
            {
                if (Languages.TryGetValue(language, out var file)) foreach (var kv in file) { words[kv.Key] = kv.Value; files[kv.Key] = kv.Value; }
                AddRuntime(words, language);
            }

            var game = GameWords?.GetValue(loc) as Dictionary<string, string>;
            foreach (string key in _given.Keys) if (!words.ContainsKey(key)) game?.Remove(key);
            _runtimeGiven.Clear();
            foreach (var languages in Runtime.Values) foreach (var group in languages.Values) foreach (string key in group.Keys) _runtimeGiven.Add(key);

            _given = words;
            _files = files;
            foreach (var kv in words) AddWord.Invoke(loc, new object[] { kv.Key, FillIn(kv.Key, kv.Value) });
            EvictCache(loc);
        }

        private static void EvictCache(Localization loc)
        {
            object cache = Cache?.GetValue(loc);
            cache?.GetType().GetMethod("EvictAll")?.Invoke(cache, null);
        }

        private static Dictionary<string, string> Parse(string json)
        {
            var result = new Dictionary<string, string>();
            foreach (Match m in Regex.Matches(json, "\"((?:[^\"\\\\]|\\\\.)*)\"\\s*:\\s*\"((?:[^\"\\\\]|\\\\.)*)\""))
                result[Unescape(m.Groups[1].Value)] = Unescape(m.Groups[2].Value);
            return result;
        }

        private static string Unescape(string s) => Regex.Replace(s, @"\\(u[0-9a-fA-F]{4}|.)", m =>
        {
            string e = m.Groups[1].Value;
            switch (e[0])
            {
                case 'n': return "\n";
                case 't': return "\t";
                case 'u': return ((char)Convert.ToInt32(e.Substring(1), 16)).ToString();
                default: return e;
            }
        });
    }
}
