using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using static VillagePlebs.Words;

namespace VillagePlebs
{
    internal static class Armory
    {
        public const string Key = "vp_armory";
        public static bool Allowed(Component c) => Companion.Zdo(c)?.GetBool(Key, true) ?? true;

        private static readonly GearKind[] Kinds =
            { GearKind.Spare, GearKind.Shield, GearKind.Bow, GearKind.Ammo, GearKind.Axe, GearKind.Pickaxe, GearKind.Head, GearKind.Chest, GearKind.Legs, GearKind.Cape };

        private static bool Worn(ItemDrop.ItemData i) => i.m_shared.m_useDurability && i.m_shared.m_maxDurability > 0f && i.m_durability < i.GetMaxDurability() * 0.25f;

        private static float Current(Humanoid me, Gear.Slot s)
        {
            if (s.Kind == GearKind.Spare)
                return me.GetInventory().GetAllItems().Where(Companion.IsMelee).Select(i => Gear.Score(me, s, i)).DefaultIfEmpty(0f).Max();
            ItemDrop.ItemData there = Gear.In(me, s);
            return there != null ? Gear.Score(me, s, there) : -1f;
        }

        private static bool Better(Humanoid me, Gear.Slot s, ItemDrop.ItemData i)
        {
            if (!Gear.Fits(s, i) || Worn(i) || i.m_shared.m_questItem) return false;
            if (s.Kind == GearKind.Spare && (Gear.IsTool(i) || !Companion.IsMelee(i))) return false;
            if (s.Kind == GearKind.Ammo)
            {
                ItemDrop.ItemData bow = Gear.In(me, Gear.All.First(x => x.Kind == GearKind.Bow));
                if (bow == null || bow.m_shared.m_ammoType != i.m_shared.m_ammoType) return false;
                ItemDrop.ItemData arrows = Gear.In(me, s);
                return arrows == null || arrows.m_shared.m_ammoType != bow.m_shared.m_ammoType || Gear.Harm(i) > Gear.Harm(arrows) + 0.5f;
            }
            float now = Current(me, s), then = Gear.Score(me, s, i);
            if (now < 0f) return true;
            return then > now * 1.05f + 0.5f;
        }

        private static IEnumerable<Container> YourChests(Humanoid me, Vector3 center, float radius, Func<Container, bool> allowed) =>
            Work.YourChests(me, center, radius).Where(c => (allowed == null || allowed(c)) && (!c.m_checkGuardStone || PrivateArea.CheckAccess(c.transform.position, 0f, false)));

        private static Dictionary<ItemDrop.ItemData, Container> Best(Humanoid me, IEnumerable<Container> chests)
        {
            var all = chests.SelectMany(c => c.GetInventory().GetAllItems().Select(i => new KeyValuePair<ItemDrop.ItemData, Container>(i, c))).ToList();
            var picks = new Dictionary<ItemDrop.ItemData, Container>();
            foreach (GearKind kind in Kinds)
            {
                Gear.Slot s = Gear.All.First(x => x.Kind == kind);
                var best = all.Where(p => !picks.ContainsKey(p.Key) && Better(me, s, p.Key))
                              .OrderByDescending(p => Gear.Score(me, s, p.Key)).ThenBy(p => Vector3.Distance(p.Value.transform.position, me.transform.position)).FirstOrDefault();
                if (best.Key != null) picks[best.Key] = best.Value;
            }
            return picks;
        }

        private static List<ItemDrop.ItemData> Picks(Humanoid me, Container chest) =>
            Best(me, Work.YourChests(me, chest.transform.position, 30f)).Where(kv => kv.Value == chest).Select(kv => kv.Key).ToList();

        public static Container Find(Humanoid me, Vector3 center, float radius, Func<Container, bool> allowed = null, bool force = false)
        {
            if (!force && !Allowed(me)) return null;
            return Best(me, YourChests(me, center, radius, allowed)).Values.Distinct()
                .OrderBy(c => Vector3.Distance(c.transform.position, me.transform.position)).Take(4).FirstOrDefault(c => Brain.CanReach(me, c.transform.position));
        }

        public static string TakeFrom(BrainState st, Container chest, bool force = false)
        {
            Humanoid me = st.Body;
            if (chest == null || !force && !Allowed(me) || Home.IdOn(chest) != 0L) return null;
            if (!Containers.Take(chest)) return null;
            Inventory mine = me.GetInventory(), its = chest.GetInventory();
            var changes = new List<string>();
            foreach (ItemDrop.ItemData pick in Picks(me, chest))
            {
                if (!its.ContainsItem(pick)) continue;
                if (mine.GetEmptySlots() == 0) { Talk.Tell(me, P("villagepleb_armory_bag_full"), "armoryfull", 60f); break; }
                Gear.Slot slot = Gear.All.First(s => Gear.Fits(s, pick) && Kinds.Contains(s.Kind) && (s.Kind != GearKind.Spare || !Gear.IsTool(pick)));
                ItemDrop.ItemData old = Gear.In(me, slot);
                pick.m_equipped = false;
                int had = mine.CountItems(pick.m_shared.m_name);
                mine.MoveItemToThis(its, pick);
                if (mine.CountItems(pick.m_shared.m_name) <= had) continue;
                Gear.Arrange(me);
                string name = NameOf(pick);
                if (old != null && mine.ContainsItem(old) && !Gear.InSlot(old))
                {
                    if (me.IsItemEquiped(old)) me.UnequipItem(old, false);
                    old.m_equipped = false;
                    its.MoveItemToThis(mine, old);
                    string oldName = NameOf(old);
                    changes.Add(its.ContainsItem(old) ? P("villagepleb_armory_swapped", name, oldName) : P("villagepleb_armory_swapped_kept", name, oldName));
                }
                else changes.Add(name);
            }
            if (changes.Count == 0) return null;
            Companion.Maintain(me, false);
            Companion.SaveBag(me);
            string list = string.Join(", ", changes);
            st.Remember(P("villagepleb_armory_took", list));
            Talk.Say(me, P("villagepleb_armory_found", list));
            VillagePlebsPlugin.Instance?.Note($"{Companion.NameOf(me)} took better gear from the chest at {chest.transform.position:F0}: {Words.English(list)}");
            return list;
        }

        private static string NameOf(ItemDrop.ItemData i) =>
            i.m_shared.m_maxQuality > 1 ? P("villagepleb_armory_item_level", Low(i.m_shared.m_name), i.m_quality) : P("villagepleb_armory_item", Low(i.m_shared.m_name));

        public static string CatchUp(BrainState st, Vector3 center, float radius)
        {
            Humanoid me = st.Body;
            if (!Allowed(me)) return null;
            var all = new List<string>();
            for (int round = 0; round < 10; round++)
            {
                Container chest = Best(me, Work.YourChests(me, center, radius)).Values.FirstOrDefault();
                string got = chest != null ? TakeFrom(st, chest) : null;
                if (got == null) break;
                all.Add(got);
            }
            return all.Count > 0 ? string.Join(", ", all) : null;
        }
    }
}
