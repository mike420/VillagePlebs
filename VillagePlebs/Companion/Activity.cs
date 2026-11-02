using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using BepInEx;
using UnityEngine;

namespace VillagePlebs
{
    internal static class Activity
    {
        private static string _file;
        private static readonly List<string> Pending = new List<string>();
        private static readonly object FileLock = new object();
        private static float _nextFlush;

        // Its last lines this session, for the log card in its menu, kept by its id so they last when it is loaded again.
        private const int Kept = 200;
        private static readonly Dictionary<long, List<(string At, string Text)>> Recent = new Dictionary<long, List<(string, string)>>();

        public static void Log(Humanoid c, string text)
        {
            if (c == null || string.IsNullOrEmpty(text)) return;
            text = Words.English(text);
            string at = DateTime.Now.ToString("HH:mm:ss");
            Write($"{Companion.NameOf(c)}: {at} {text}");
            long id = Companion.IdOf(c);
            if (id == 0L) return;
            if (!Recent.TryGetValue(id, out var list)) Recent[id] = list = new List<(string, string)>();
            list.Add((at, text));
            if (list.Count > Kept) list.RemoveRange(0, list.Count - Kept);
        }

        // Newest first. Key events: what it remembers (»), its journal and what it says; everything leaves out only the 30 s vitals.
        public static List<(string At, string Text)> RecentOf(Humanoid c, bool key, int count)
        {
            if (c == null || !Recent.TryGetValue(Companion.IdOf(c), out var list)) return new List<(string, string)>();
            return Enumerable.Reverse(list).Where(l => !l.Text.StartsWith("· ") && (!key || l.Text.StartsWith("» ") || l.Text.StartsWith("journal:") || l.Text.StartsWith("said:")))
                .Take(count).ToList();
        }

        public static string FilePath => _file ?? Path.Combine(Paths.BepInExRootPath, VillagePlebsPlugin.PluginName, "activity.log");

        private static void Write(string line)
        {
            lock (Pending) Pending.Add(line);
            if (Time.time >= _nextFlush) Flush(false);
        }

        public static void Flush(bool now)
        {
            _nextFlush = Time.time + 2f;
            string[] lines;
            lock (Pending) { if (Pending.Count == 0) return; lines = Pending.ToArray(); Pending.Clear(); }
            try
            {
                if (_file == null)
                {
                    string dir = Path.Combine(Paths.BepInExRootPath, VillagePlebsPlugin.PluginName);
                    Directory.CreateDirectory(dir);
                    _file = Path.Combine(dir, "activity.log");
                }
            }
            catch (Exception) { return; }
            string file = _file;
            if (now) Append(file, lines); else ThreadPool.QueueUserWorkItem(_ => Append(file, lines));
        }

        private static void Append(string file, string[] lines)
        {
            lock (FileLock)
            {
                try
                {
                    var info = new FileInfo(file);
                    if (info.Exists && info.Length > 2_000_000)
                    {
                        string old = Path.Combine(info.DirectoryName, "activity.old.log");
                        if (File.Exists(old)) File.Delete(old);
                        File.Move(file, old);
                    }
                    File.AppendAllLines(file, lines);
                }
                catch (Exception) { }
            }
        }

        public static string Vitals(Humanoid c, BrainState st)
        {
            var meals = Food.Meals(c);
            Inventory inv = c.GetInventory();
            string food = meals.Count == 0 ? "none" : $"{meals.Count} ({string.Join(", ", meals.Select(m => $"{Localization.instance.Localize(m.Item.m_shared.m_name)} {Mathf.CeilToInt(m.Time / 60f)}m"))})";
            int carriedFood = inv.GetAllItems().Where(Food.IsFood).Sum(i => i.m_stack);
            return $"hp {c.GetHealth():0}/{c.GetMaxHealth():0}, st {Stamina.Get(c):0}/{Stamina.Max(c):0}, food {food}, food in bag {carriedFood}, " +
                   $"bag {inv.NrOfItems()}/{inv.GetWidth() * inv.GetHeight()} {Carry.Weight(c):0}kg, armour {Companion.Armor(c):0}, weapon {Name(Companion.BestMelee(c) ?? Companion.BestRanged(c))}, " +
                   $"order {Companion.OrderOf(c)}, goal {st.Goal?.What ?? "-"}";
        }

        private static string Name(ItemDrop.ItemData i) => i == null ? "none" : Localization.instance.Localize(i.m_shared.m_name);

        public static void Tick(BrainState st)
        {
            if (Time.time < st.NextSnapshot) return;
            st.NextSnapshot = Time.time + 30f;
            Log(st.Body, "· " + Vitals(st.Body, st));
        }
    }
}
