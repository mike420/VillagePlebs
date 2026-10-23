using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using UnityEngine;
using static VillagePlebs.Words;

namespace VillagePlebs
{
    public enum Duty { Food, Wood, Mining }

    internal static class Duties
    {
        public const string Key = "vp_duties", DestKey = "vp_dutydest";

        public static bool ToYours(Component c) => (Companion.Zdo(c)?.GetInt(DestKey, 0) ?? 0) == 0;

        private static readonly Dictionary<long, KeyValuePair<float, bool>> Places = new Dictionary<long, KeyValuePair<float, bool>>();

        public static bool HasPlace(Humanoid me)
        {
            long id = Companion.IdOf(me);
            if (Places.TryGetValue(id, out var hit) && Time.time - hit.Key < 3f) return hit.Value;
            bool ok = ToYours(me) ? Work.Stows(me) && Work.YourChests(me, Work.Center(me), Work.RadiusOf(me) + 20f).Any() : Home.Chests(me).Count > 0;
            Places[id] = new KeyValuePair<float, bool>(Time.time, ok);
            return ok;
        }

        internal class Entry
        {
            public Duty Duty;
            public bool On;
            public int Target;

            public Job Jobs => Duty == Duty.Wood ? Job.Wood : Duty == Duty.Food ? Job.Forage | Job.Hunt | Job.Cook : Job.Stone | Job.Ore;
        }

        public static string Label(Duty d) => P("villagepleb_duties_label_" + d.ToString().ToLowerInvariant());
        public static string Amount(Duty d, int n) => P("villagepleb_duties_amount_" + d.ToString().ToLowerInvariant(), n);
        public static int Step(Duty d) => d == Duty.Food ? 10 : 20;
        public static int Max(Duty d) => d == Duty.Food ? 300 : 600;
        private static int Default(Duty d) => d == Duty.Food ? 60 : 100;

        public static List<Entry> Read(ZDO z)
        {
            var list = new List<Entry>();
            foreach (string part in (z?.GetString(Key, "") ?? "").Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries))
            {
                string[] f = part.Split(':');
                if (f.Length < 3 || !Enum.TryParse(f[0], out Duty d) || list.Any(e => e.Duty == d)) continue;
                int.TryParse(f[2], NumberStyles.Integer, CultureInfo.InvariantCulture, out int target);
                list.Add(new Entry { Duty = d, On = f[1] == "1", Target = Mathf.Clamp(target > 0 ? target : Default(d), Step(d), Max(d)) });
            }
            foreach (Duty d in Enum.GetValues(typeof(Duty))) if (list.All(e => e.Duty != d)) list.Add(new Entry { Duty = d, Target = Default(d) });
            return list;
        }

        public static void Write(ZDO z, List<Entry> list) => z.Set(Key, string.Join(",", list.Select(e => $"{e.Duty}:{(e.On ? 1 : 0)}:{e.Target}")));

        private static readonly Dictionary<string, List<Entry>> Parsed = new Dictionary<string, List<Entry>>();

        private static List<Entry> Cached(Component c)
        {
            ZDO z = Companion.Zdo(c);
            if (z == null) return new List<Entry>();
            string raw = z.GetString(Key, "");
            if (!Parsed.TryGetValue(raw, out List<Entry> list))
            {
                if (Parsed.Count > 40) Parsed.Clear();
                Parsed[raw] = list = Read(z);
            }
            return list;
        }

        public static bool Configured(Humanoid c) => Cached(c).Any(e => e.On) && HasPlace(c);

        public static bool AnyOn(Component c) => Cached(c).Any(e => e.On);

        private static readonly HashSet<string> WoodNames = new HashSet<string>
            { "$item_wood", "$item_finewood", "$item_corewood", "$item_elderbark", "$item_yggdrasilwood", "$item_blackwood", "$item_ashwood" };

        public static bool Counts(Duty d, ItemDrop.ItemData i)
        {
            string name = i.m_shared.m_name;
            bool material = i.m_shared.m_itemType == ItemDrop.ItemData.ItemType.Material;
            switch (d)
            {
                case Duty.Wood: return material && WoodNames.Contains(name);
                case Duty.Food: return Food.IsFood(i) || Work.IsCookable(i);
                default: return material && (name == "$item_stone" || name.EndsWith("ore", StringComparison.Ordinal) || name.EndsWith("scrap", StringComparison.Ordinal));
            }
        }

        public static float MealWeight(ItemDrop.ItemData i) =>
            Food.IsFood(i) ? Mathf.Clamp((i.m_shared.m_food + i.m_shared.m_foodStamina) / 50f, 0.2f, 1f) : 0.5f;

        private static readonly Dictionary<long, int[]> Haves = new Dictionary<long, int[]>();
        private static readonly Dictionary<long, float> HaveAt = new Dictionary<long, float>();
        private static readonly Dictionary<long, float[]> Idle = new Dictionary<long, float[]>();
        private static readonly Dictionary<long, int[]> Strikes = new Dictionary<long, int[]>();
        private static readonly Dictionary<long, float> LastDelivery = new Dictionary<long, float>();

        public static int Have(Humanoid me, Duty d)
        {
            long id = Companion.IdOf(me);
            if (!Haves.TryGetValue(id, out int[] have)) Haves[id] = have = new int[Enum.GetValues(typeof(Duty)).Length];
            if (!HaveAt.TryGetValue(id, out float at) || Time.time - at > 3f)
            {
                HaveAt[id] = Time.time;
                Array.Clear(have, 0, have.Length);
                var kinds = new Dictionary<string, float>();
                foreach (Container chest in ToYours(me) ? Work.YourChests(me, Work.Center(me), Work.RadiusOf(me) + 20f).ToList() : Home.Chests(me))
                    foreach (ItemDrop.ItemData i in chest.GetInventory().GetAllItems())
                        foreach (Duty each in Enum.GetValues(typeof(Duty)))
                            if (each == Duty.Food) { if (Counts(each, i)) kinds[i.m_shared.m_name] = (kinds.TryGetValue(i.m_shared.m_name, out float k) ? k : 0f) + i.m_stack * MealWeight(i); }
                            else if (Counts(each, i)) have[(int)each] += i.m_stack;
                float cap = Mathf.Max(1f, (Cached(me).FirstOrDefault(e => e.Duty == Duty.Food)?.Target ?? 40) * 0.6f);
                have[(int)Duty.Food] = Mathf.FloorToInt(kinds.Values.Sum(v => Mathf.Min(v, cap)));
                foreach (ItemDrop.ItemData i in me.GetInventory().GetAllItems())
                    if (!Work.Keeps(me, i))
                        foreach (Duty each in Enum.GetValues(typeof(Duty)))
                            if (each != Duty.Food && Counts(each, i)) have[(int)each] += i.m_stack;
            }
            return have[(int)d];
        }

        public static bool Resting(Humanoid me, Duty d) => Idle.TryGetValue(Companion.IdOf(me), out float[] until) && until[(int)d] > Time.time;

        public static Entry Active(Humanoid me)
        {
            foreach (Entry e in Cached(me))
            {
                if (!e.On || Resting(me, e.Duty)) continue;
                if (HasPlace(me) && Have(me, e.Duty) < e.Target) return e;
            }
            return null;
        }

        public static int Rest(Humanoid me, Entry e)
        {
            long id = Companion.IdOf(me);
            int n = Enum.GetValues(typeof(Duty)).Length;
            if (!Idle.TryGetValue(id, out float[] until)) Idle[id] = until = new float[n];
            if (!Strikes.TryGetValue(id, out int[] strikes)) Strikes[id] = strikes = new int[n];
            int k = ++strikes[(int)e.Duty];
            until[(int)e.Duty] = Time.time + Mathf.Min(300f, 60f * (1 << Mathf.Min(k - 1, 3)));
            return k;
        }

        public static void Worked(Humanoid me, Entry e)
        {
            if (Strikes.TryGetValue(Companion.IdOf(me), out int[] strikes)) strikes[(int)e.Duty] = 0;
        }

        public static bool Open(Humanoid me, Duty d) => Cached(me).Any(e => e.Duty == d && e.On && Have(me, d) < e.Target);

        public static List<Entry> OnEntries(Humanoid me) => Cached(me).Where(e => e.On).ToList();

        public static int Accrued(Duty d, Dictionary<string, int> got)
        {
            float total = 0f;
            foreach (var kv in got)
            {
                ItemDrop drop = ObjectDB.instance?.GetItemPrefab(kv.Key)?.GetComponent<ItemDrop>();
                if (drop != null && Counts(d, drop.m_itemData)) total += kv.Value * (d == Duty.Food ? MealWeight(drop.m_itemData) : 1f);
            }
            return Mathf.FloorToInt(total);
        }

        public static Job OpenJobs(Humanoid me)
        {
            Job j = Job.None;
            foreach (Entry e in Cached(me)) if (e.On && Have(me, e.Duty) < e.Target) j |= e.Jobs;
            return j;
        }

        public static bool FoodForYou(Humanoid me) => ToYours(me) && Cached(me).Any(e => e.Duty == Duty.Food && e.On);

        public static int Cap(Humanoid me, ItemDrop.ItemData i)
        {
            List<Entry> list = Cached(me);
            if (!list.Any(e => e.On) || ToYours(me) || Home.Chests(me).Count == 0) return 0;
            foreach (Entry e in list) if (e.On && Counts(e.Duty, i)) return e.Target;
            return 0;
        }

        public static bool Deliver(Humanoid me, Entry active)
        {
            long id = Companion.IdOf(me);
            if (LastDelivery.TryGetValue(id, out float last) && Time.time - last < 60f) return false;
            if (!HasPlace(me)) return false;
            Inventory inv = me.GetInventory();
            foreach (Entry e in Cached(me))
            {
                if (!e.On) continue;
                int carried = inv.GetAllItems().Where(i => Counts(e.Duty, i) && !Work.Keeps(me, i)).Sum(i => i.m_stack) - (e.Duty == Duty.Food ? 10 : 0);
                if (carried <= 0) continue;
                if (carried >= (e.Duty == Duty.Food ? 8 : 20) || e != active || Have(me, e.Duty) >= e.Target) { LastDelivery[id] = Time.time; return true; }
            }
            return false;
        }

        public static string Status(Humanoid me, Entry e, Entry active)
        {
            if (!e.On) return Localization.instance.Localize("$hud_off");
            int have = Have(me, e.Duty);
            if (have >= e.Target) return P("villagepleb_duties_stocked", have, e.Target);
            if (e == active) return P("villagepleb_duties_working", have, e.Target);
            if (Resting(me, e.Duty)) return P("villagepleb_duties_nothing_near", have, e.Target);
            return P("villagepleb_duties_turn_have", have, e.Target);
        }
    }
}
