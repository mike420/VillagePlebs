using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using UnityEngine;
using static VillagePlebs.Words;

namespace VillagePlebs
{
    internal class Mission
    {
        public string Key = "", What = "", Status = "active", Why = "";
        public int Day, StartCost, Cost;
    }

    internal static class Missions
    {
        private const int Keep = 12;

        public static List<Mission> All(Humanoid c)
        {
            var list = new List<Mission>();
            foreach (string line in Book.Of(c).Missions.Split(new[] { '\n' }, StringSplitOptions.RemoveEmptyEntries))
            {
                string[] f = line.Split('|');
                if (f.Length < 7) continue;
                list.Add(new Mission { Status = f[0], Key = f[1], What = f[2], Day = Int(f[3]), StartCost = Int(f[4]), Cost = Int(f[5]), Why = f[6] });
            }
            return list;
        }

        private static int Int(string s) => int.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out int n) ? n : 0;
        private static string Clean(string s) => (s ?? "").Replace('|', '/').Replace('\n', ' ');

        private static void Save(Humanoid c, List<Mission> list)
        {
            var keep = list.Where(m => m.Status == "active").Concat(list.Where(m => m.Status != "active").Reverse().Take(Keep).Reverse()).ToList();
            if (Companion.Zdo(c) != null) Book.SetMissions(c, string.Join("\n", keep.Select(m => string.Join("|", m.Status, Clean(m.Key), Clean(m.What), m.Day, m.StartCost, m.Cost, Clean(m.Why)))));
        }

        public static Mission Current(Humanoid c) => All(c).LastOrDefault(m => m.Status == "active");

        public static bool Blocked(BrainState st, string key) => key != null && st.DroppedUntil.TryGetValue(key, out float until) && Time.time < until;

        public static string KeyOf(Goal g) => g == null ? null : g.Item != null ? $"up:{g.Item.m_shared.m_name}:{g.Item.m_quality + 1}" : g.Recipe != null ? $"make:{g.Recipe.name}" : null;

        private static bool Achieved(Humanoid me, Mission m)
        {
            IEnumerable<ItemDrop.ItemData> have = me.GetInventory().GetAllItems().Concat(Home.Chests(me).SelectMany(ch => ch.GetInventory().GetAllItems()));
            string[] f = m.Key.Split(':');
            if (f[0] == "up" && f.Length >= 3) return have.Any(i => i.m_shared.m_name == f[1] && i.m_quality >= Int(f[2]));
            if (f[0] == "make" && f.Length >= 2)
            {
                Recipe r = ObjectDB.instance?.m_recipes.FirstOrDefault(x => x != null && x.name == f[1]);
                return r?.m_item != null && have.Any(i => i.m_shared.m_name == r.m_item.m_itemData.m_shared.m_name);
            }
            return false;
        }

        private static bool Ready(Humanoid me, Mission m)
        {
            string[] f = m.Key.Split(':');
            Recipe r = null;
            int quality = 1;
            if (f[0] == "up" && f.Length >= 3)
            {
                quality = Int(f[2]);
                ItemDrop.ItemData item = me.GetInventory().GetAllItems().FirstOrDefault(i => i.m_shared.m_name == f[1] && i.m_quality == quality - 1);
                if (item == null) return false;
                r = ObjectDB.instance?.GetRecipe(item);
            }
            else if (f[0] == "make" && f.Length >= 2) r = ObjectDB.instance?.m_recipes.FirstOrDefault(x => x != null && x.name == f[1]);
            if (r == null) return false;
            List<Container> chests = Home.Chests(me);
            return Upgrades.Needs(r).All(q => q.m_resItem == null
                || me.GetInventory().CountItems(q.m_resItem.m_itemData.m_shared.m_name) + chests.Sum(c => c.GetInventory().CountItems(q.m_resItem.m_itemData.m_shared.m_name)) >= q.GetAmount(quality));
        }

        public static void Track(BrainState st, Goal g, IList<Goal> others)
        {
            Humanoid me = st.Body;
            if (Companion.Zdo(me) == null || !me.GetComponent<ZNetView>().IsOwner()) return;
            if (!Journal.Known(me)) return;
            List<Mission> list = All(me);
            Mission cur = list.LastOrDefault(m => m.Status == "active");
            string key = KeyOf(g);

            if (cur != null && cur.Key != key)
            {
                if (Achieved(me, cur)) Finish(st, list, cur);
                else if (Ready(me, cur))
                {
                    if (st.MissionReadySince == 0f) st.MissionReadySince = Time.time;
                    if (Time.time - st.MissionReadySince < 600f) return;
                    Drop(st, list, cur, P("villagepleb_missions_no_station"));
                }
                else Drop(st, list, cur, g == null ? P("villagepleb_missions_nothing_to_do") : P("villagepleb_missions_gone"));
                st.MissionReadySince = 0f;
                cur = null;
            }
            else st.MissionReadySince = 0f;
            if (cur == null && g != null && key != null)
            {
                cur = new Mission { Key = key, What = g.What, Day = Journal.Day, StartCost = Mathf.Max(1, g.Cost), Cost = g.Cost };
                list.Add(cur);
                st.MissionSince = Time.time; st.MissionBest = g.Cost; st.MissionAskSince = 0f;
                Journal.Add(me, P(g.Item != null ? "villagepleb_missions_set_upgrade" : "villagepleb_missions_set_make", g.What));
                Talk.Mention(me, P("villagepleb_missions_new", g.What), "mission:" + key, 30f);
                Save(me, list);
                return;
            }
            if (cur == null) return;

            cur.Cost = g.Cost;
            if (st.MissionSince == 0f) { st.MissionSince = Time.time; st.MissionBest = g.Cost; }
            if (g.Cost < st.MissionBest) { st.MissionBest = g.Cost; st.MissionSince = Time.time; }
            bool otherOwn = others.Any(o => o != g && o.Ask.Count == 0 && !Blocked(st, KeyOf(o)));
            if (g.Ask.Count > 0) { if (st.MissionAskSince == 0f) st.MissionAskSince = Time.time; } else st.MissionAskSince = 0f;
            if (st.MissionAskSince > 0f && Time.time - st.MissionAskSince > 1200f && otherOwn)
                Drop(st, list, cur, P("villagepleb_missions_waited", Goals.And(g.Ask)));
            else if (Time.time - st.MissionSince > 1800f && otherOwn)
                Drop(st, list, cur, P("villagepleb_missions_no_headway"));
            else Save(me, list);
        }

        // A mission is done the moment the item is in its hands (or its chests), not when it next thinks about its goals.
        public static void CheckDone(BrainState st)
        {
            Humanoid me = st.Body;
            if (Companion.Zdo(me) == null || !me.GetComponent<ZNetView>().IsOwner() || !Journal.Known(me)) return;
            List<Mission> list = All(me);
            Mission cur = list.LastOrDefault(m => m.Status == "active");
            if (cur == null || !Achieved(me, cur)) return;
            Finish(st, list, cur);
            st.NextUpgradeLook = 0f;
            st.NextWorkLook = 0f;
        }

        private static void Finish(BrainState st, List<Mission> list, Mission m)
        {
            m.Status = "done"; m.Cost = 0; m.Why = P("villagepleb_missions_day", Journal.Day);
            Journal.Add(st.Body, P("villagepleb_missions_done_journal", m.What));
            Talk.Mention(st.Body, P("villagepleb_missions_done_say", m.What), "missiondone:" + m.Key, 10f);
            st.MissionSince = 0f;
            Save(st.Body, list);
        }

        private static void Drop(BrainState st, List<Mission> list, Mission m, string why)
        {
            m.Status = "dropped"; m.Why = why;
            st.DroppedUntil[m.Key] = Time.time + 3600f;
            Journal.Add(st.Body, P("villagepleb_missions_gave_up_journal", m.What, why));
            Talk.Mention(st.Body, P("villagepleb_missions_gave_up_say", m.What, why), "missiondrop:" + m.Key, 30f);
            st.MissionSince = 0f;
            st.NextUpgradeLook = 0f;
            Save(st.Body, list);
        }
    }
}
