using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using BepInEx;
using UnityEngine;

namespace VillagePlebs
{
    /// <summary>
    /// A running record of what each companion does, for finding out why it did something: every change in what it is doing, what it
    /// remembers (ate, made, gave up on), every hit it takes and what dealt it, what it said, and a snapshot of its health, stamina, food
    /// and bag every 30 seconds. Written to BepInEx/VillagePlebs/activity.log (started again when it passes 2 MB, the old one kept as
    /// activity.old.log). On the game that runs it. Lines are gathered and written every two seconds off the game's thread: written one by
    /// one as they came (every hit, pickup and door, from each companion's brain), the disk made the game stutter in a fight.
    /// </summary>
    internal static class Activity
    {
        private static string _file;
        private static readonly List<string> Pending = new List<string>();
        private static readonly object FileLock = new object();
        private static float _nextFlush;

        public static void Log(Humanoid c, string text)
        {
            if (c == null || string.IsNullOrEmpty(text)) return;
            text = Words.English(text);   // (its words in English here, whatever the player's language)
            Write($"{Companion.NameOf(c)}: {DateTime.Now:HH:mm:ss} {text}");
        }

        private static void Write(string line)
        {
            lock (Pending) Pending.Add(line);
            if (Time.time >= _nextFlush) Flush(false);
        }

        /// <summary>What was gathered, to the file: off the game's thread, or (the mod unloading) now.</summary>
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
            catch (Exception) { return; } // a log must never break the companion
            string file = _file;
            if (now) Append(file, lines); else ThreadPool.QueueUserWorkItem(_ => Append(file, lines));
        }

        private static void Append(string file, string[] lines)
        {
            lock (FileLock)   // (one write at a time: two batches can be on their way)
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
                catch (Exception) { } // a log must never break the companion
            }
        }

        /// <summary>Its vitals in one line: "hp 12/25, st 40/75, food 2 (cooked meat 12m, raspberries 3m), bag 6/32 18kg, goal flint axe".</summary>
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

        /// <summary>Every 30 s on the game running it: a snapshot line.</summary>
        public static void Tick(BrainState st)
        {
            if (Time.time < st.NextSnapshot) return;
            st.NextSnapshot = Time.time + 30f;
            Log(st.Body, "· " + Vitals(st.Body, st));
        }
    }
}
