using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using UnityEngine;
using static VillagePlebs.Words;

namespace VillagePlebs
{
    internal static class Journal
    {
        public const string SinceKey = "vp_since";
        public const int MaxEntries = 150;

        public static int Day => EnvMan.instance != null ? EnvMan.instance.GetDay(ZNet.instance.GetTimeSeconds()) : 0;

        public static void Add(Humanoid c, string text)
        {
            if (Companion.Zdo(c) == null || string.IsNullOrEmpty(text) || !c.GetComponent<ZNetView>().IsOwner()) return;
            Book.AddEntry(c, $"{Day}|{text.Replace('\n', ' ').Replace('|', '/')}");
            Activity.Log(c, "journal: " + text);
        }

        public static List<string> Entries(Humanoid c) => Book.Of(c).Entries;

        public static Dictionary<string, int> Tally(Humanoid c) => Book.Of(c).Tally;

        public static bool Known(Humanoid c) => Book.Of(c).Fresh;

        public static int Count(Humanoid c, string what, int n = 1)
        {
            if (Companion.Zdo(c) == null || !c.GetComponent<ZNetView>().IsOwner()) return 0;
            return Book.AddCount(c, what, n);
        }

        public static int Tallied(Dictionary<string, int> tally, string prefix, string token, string old = null)
        {
            int n = tally.TryGetValue(prefix + token, out int now) ? now : 0;
            if (old == null && token.StartsWith("$") && Localization.instance != null) old = Localization.instance.Localize(token);
            if (old != null && old != token && tally.TryGetValue(prefix + old, out int before)) n += before;
            return n;
        }

        public static void Kill(Humanoid c, Character victim)
        {
            string name = victim.m_name;
            Count(c, "kill:" + name);
            int n = Tallied(Tally(c), "kill:", name);
            Count(c, "kills");
            if (victim.IsBoss())
            {
                Count(c, "boss:" + name);
                Add(c, P("villagepleb_journal_boss", name));
                Idle.Queue(Brain.Get(c), "cheer", 1.5f);
                Banter.Say(c, "bosskill", 0f, P("villagepleb_journal_boss_say_1", name), P("villagepleb_journal_boss_say_2", name), P("villagepleb_journal_boss_say_3", name));
            }
            else if (Tactics.Feared(c, victim))
            {
                Idle.Queue(Brain.Get(c), "cheer", 1f);
                Banter.Say(c, "revenge:" + name, 5f, P("villagepleb_journal_revenge_1", Low(name)), P("villagepleb_journal_revenge_2"), P("villagepleb_journal_revenge_3", Low(name)));
            }
            if (victim.IsBoss() || !Known(c)) return;
            if (n == 1) Add(c, P("villagepleb_journal_first_kill", Low(name)));
            else if (n == 10 || n == 50 || n == 100) Add(c, P("villagepleb_journal_nth_kill", n, Low(name)));
        }

        public static void Parried(Humanoid c, Character attacker)
        {
            if (Count(c, "parries") == 1 && Known(c)) Add(c, P("villagepleb_journal_first_parry", Low(attacker.m_name)));
        }

        public static void Fell(Humanoid c, string by, Vector3 where)
        {
            int n = Count(c, "deaths");
            bool effect = by.StartsWith("$se_");
            if (by.StartsWith("$") ? !effect : by.Length > 0 && char.IsUpper(by[0])) Count(c, "killedby:" + by);
            string what = by == "?" ? P("villagepleb_journal_something") : effect ? Low(by) : by.StartsWith("$") ? by
                        : HitWords.Contains(by) ? P("villagepleb_journal_hit_" + by) : by;
            Add(c, n > 1 ? P("villagepleb_journal_fell_again", what, n) : P("villagepleb_journal_fell", what));
        }

        private static readonly HashSet<string> HitWords = new HashSet<string> { "fall", "drowning", "burning", "freezing", "poisoned", "smoke", "impact", "tree", "cart", "boat", "water", "edgeofworld", "structural" };

        public static void Made(Humanoid c, string itemToken) { Count(c, "made"); Add(c, P("villagepleb_journal_made", Low(itemToken))); }

        public static void Upgraded(Humanoid c, string itemToken, int level) { Count(c, "upgraded"); Add(c, P("villagepleb_journal_upgraded", Low(itemToken), level)); }

        public static void Trip(Humanoid c, string packedLine) { Count(c, "trips"); Add(c, packedLine); }

        public static void Skill(Humanoid c, string skill, int level)
        {
            if (level % 10 != 0) return;
            Add(c, P("villagepleb_journal_skill", level, skill));
            Banter.Say(c, "skill", 3f, P("villagepleb_journal_skill_say_1", Low(skill)), P("villagepleb_journal_skill_say_2", skill, level), P("villagepleb_journal_skill_say_3"));
        }

        public static bool Place(Humanoid c, string place, bool dungeon, string old = null)
        {
            Count(c, "place:" + place);
            if (!Known(c) || Tallied(Tally(c), "place:", place, old ?? place) != 1) return false;
            Add(c, dungeon ? P("villagepleb_journal_dungeon") : P("villagepleb_journal_new_land", place));
            return true;
        }

        public static void Started(Humanoid c)
        {
            ZDO z = Companion.Zdo(c);
            if (z != null && z.GetInt(SinceKey, -1) < 0) z.Set(SinceKey, Day);
        }

        public static int DaysWithYou(Humanoid c) => Mathf.Max(0, Day - (Companion.Zdo(c)?.GetInt(SinceKey, Day) ?? Day));
    }

    internal static class Banter
    {
        private static readonly Dictionary<long, float> LastAny = new Dictionary<long, float>();
        private static readonly Dictionary<string, float> LastTopic = new Dictionary<string, float>();

        public static void Forget() { LastAny.Clear(); LastTopic.Clear(); }

        public static void Say(Humanoid c, string topic, float minutes, params string[] lines)
        {
            if (c == null || lines.Length == 0 || !Talk.Chatty(c)) return;
            long id = Companion.IdOf(c);
            if (LastAny.TryGetValue(id, out float any) && Time.time - any < 40f) return;
            string key = id + ":" + topic;
            if (minutes > 0f && LastTopic.TryGetValue(key, out float last) && Time.time - last < minutes * 60f) return;
            LastAny[id] = Time.time;
            LastTopic[key] = Time.time;
            string line = lines[UnityEngine.Random.Range(0, lines.Length)];
            Talk.Say(c, line);
            Activity.Log(c, "small talk: " + line);
        }

        public static void Tick(BrainState st, Player master)
        {
            if (Time.time < st.NextBanterLook) return;
            st.NextBanterLook = Time.time + 3f;
            Humanoid me = st.Body;
            SEMan se = me.GetSEMan();
            if (se.HaveStatusEffect(SEMan.s_statusEffectWet)) Say(me, "wet", 15f, Lines("wet", 3));
            else if (se.HaveStatusEffect(SEMan.s_statusEffectFreezing)) Say(me, "cold", 10f, Lines("cold", 3));
            if (EnvMan.instance != null && EnvMan.IsNight() && !st.SaidNight) { st.SaidNight = true; Say(me, "night", 20f, Lines("night", 3)); }
            if (EnvMan.instance != null && !EnvMan.IsNight()) st.SaidNight = false;
            if (master != null && st.InCombat && master.GetHealthPercentage() < 0.3f && Vector3.Distance(master.transform.position, me.transform.position) < 20f)
                Say(me, "youhurt", 2f, Lines("youhurt", 3));
            if (!st.InCombat && Companion.OrderOf(me) == Order.Gather && !st.Asleep && UnityEngine.Random.value < 0.05f)
                Say(me, "idle", 12f, Lines("idle", 6));
        }

        public static void CloseFight(Humanoid c) => Say(c, "close", 3f, Lines("close", 4));
        public static void BossSeen(Humanoid c, string boss) => Say(c, "boss:" + boss, 10f, Lines("boss", 3, boss));
        public static void Dungeon(Humanoid c) => Say(c, "dungeon", 5f, Lines("dungeon", 3));
        public static void NewLand(Humanoid c, string land) => Say(c, "land:" + land, 60f, Lines("land", 3, land));

        private static string[] Lines(string topic, int n, params object[] args) =>
            Enumerable.Range(1, n).Select(i => P($"villagepleb_banter_{topic}_{i}", args)).ToArray();
    }
}
