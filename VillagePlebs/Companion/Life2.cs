using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using UnityEngine;
using static VillagePlebs.Shared.Translations;
using static VillagePlebs.Words;

namespace VillagePlebs
{
    internal static class Grave
    {
        public const string HasKey = "vp_hasgrave", PosKey = "vp_gravepos", OfKey = "vp_tombof";

        public static bool Interact(TombStone tomb, Humanoid user)
        {
            ZDO z = Companion.Zdo(tomb);
            long id = z?.GetLong(OfKey, 0L) ?? 0L;
            if (id == 0L && !string.IsNullOrEmpty(z?.GetString(Net.CrateKey, "")))
                id = Companion.All().Where(h => Companion.NameOf(h) == z.GetString(Net.CrateKey, "") && Companion.MasterId(h) == z.GetLong(ZDOVars.s_owner, 0L))
                                    .Select(Companion.IdOf).FirstOrDefault();
            if (id == 0L || !(user is Player p)) return true;
            Container grave = tomb.GetComponent<Container>();
            if (grave == null) return true;
            Humanoid owner = Companion.All().FirstOrDefault(h => Companion.IdOf(h) == id && !h.IsDead() && Vector3.Distance(h.transform.position, tomb.transform.position) < 60f);
            if (owner == null || !Companion.CanCommand(owner, p))
            {
                grave.Interact(user, false, false);
                return false;
            }
            if (!Containers.Take(grave)) { p.Message(MessageHud.MessageType.Center, T("villagepleb_life2_tomb_open")); return false; }
            if (!Companion.Write(owner, _ => { })) { p.Message(MessageHud.MessageType.Center, T("villagepleb_life2_things_busy", Companion.NameOf(owner))); return false; }
            Inventory its = grave.GetInventory(), theirs = owner.GetInventory();
            int gave = 0, left = 0;
            foreach (ItemDrop.ItemData item in its.GetAllItems().ToList())
            {
                item.m_equipped = false;
                if (theirs.CanAddItem(item)) { theirs.MoveItemToThis(its, item); gave++; } else left++;
            }
            if (left == 0) { Companion.Write(owner, cz => cz.Set(HasKey, false)); Net.Recovered(tomb.transform.position); }
            BrainState st = Brain.Get(owner);
            st.NextGear = 0f;
            st.Remember(P("villagepleb_life2_remember_given_back", gave));
            p.Message(MessageHud.MessageType.Center, left == 0 ? T("villagepleb_life2_gave_back", Companion.NameOf(owner)) : T("villagepleb_life2_gave_back_some", Companion.NameOf(owner), gave, left));
            VillagePlebsPlugin.Instance?.Note($"{p.GetPlayerName()} gave {Companion.NameOf(owner)} {gave} item stacks back from its tombstone ({left} left)");
            return false;
        }

        public static int WearAgain(Humanoid c, Vector3 fellAt, out bool left)
        {
            left = true;
            long id = Companion.IdOf(c);
            Vector3 near = fellAt != Vector3.zero ? fellAt : c.transform.position;
            TombStone tomb = Loaded.All<TombStone>()
                .Where(t => (Companion.Zdo(t)?.GetLong(OfKey, 0L) ?? 0L) == id && (t.GetComponent<Container>()?.GetInventory()?.GetAllItems().Any(Gear.InSlot) ?? false))
                .OrderBy(t => Vector3.Distance(t.transform.position, near)).FirstOrDefault();
            Container grave = tomb != null ? tomb.GetComponent<Container>() : null;
            if (grave == null || !Containers.Take(grave)) return 0;
            Inventory mine = c.GetInventory(), its = grave.GetInventory();
            int got = 0;
            foreach (ItemDrop.ItemData item in its.GetAllItems().Where(Gear.InSlot).ToList())
            {
                item.m_equipped = false;
                Vector2i cell = item.m_gridPos;
                if (mine.GetItemAt(cell.x, cell.y) == null && mine.MoveItemToThis(its, item, item.m_stack, cell.x, cell.y)) got++;
                else if (its.ContainsItem(item) && mine.CanAddItem(item)) { mine.MoveItemToThis(its, item); got++; }
            }
            left = its.NrOfItems() > 0;
            if (!left) Net.Recovered(tomb.transform.position);
            Companion.SaveBag(c);
            VillagePlebsPlugin.Instance?.Note($"{Companion.NameOf(c)} took its gear back out of its tombstone at {tomb.transform.position:F0} ({got} stacks{(left ? $", {its.NrOfItems()} left in it" : "")})");
            return got;
        }

        public static bool Has(Component c) => Companion.Zdo(c)?.GetBool(HasKey, false) ?? false;
        public static Vector3 Pos(Component c) => Companion.Zdo(c)?.GetVec3(PosKey, Vector3.zero) ?? Vector3.zero;

        private static bool IsMine(TombStone t, Humanoid me, long id, long master, string name)
        {
            ZDO z = Companion.Zdo(t);
            if (z == null) return false;
            long of = z.GetLong(OfKey, 0L);
            return of != 0L ? of == id : z.GetLong(ZDOVars.s_owner, 0L) == master && z.GetString(Net.CrateKey, "") == name;
        }

        private static IEnumerable<TombStone> Mine(Humanoid me)
        {
            long id = Companion.IdOf(me), master = Companion.MasterId(me);
            string name = Companion.NameOf(me);
            return Loaded.All<TombStone>().Where(t => IsMine(t, me, id, master, name));
        }

        private static TombStone Find(Humanoid me)
        {
            Vector3 at = Pos(me);
            return Mine(me).FirstOrDefault(t => Vector3.Distance(t.transform.position, at) < 12f);
        }

        private static TombStone Next(BrainState st)
        {
            Humanoid me = st.Body;
            ZDO z = Companion.Zdo(me);
            if (z == null || !Work.HasHome(me)) return null;
            Vector3 home = Work.Center(me);
            float radius = Work.RadiusOf(me) + 10f;
            bool following = Companion.OrderOf(me) != Order.Gather;
            return Mine(me).Where(t => Full(t) && Vector3.Distance(t.transform.position, home) < radius && (!following || Vector3.Distance(t.transform.position, me.transform.position) < 40f))
                           .OrderBy(t => Vector3.Distance(t.transform.position, me.transform.position)).FirstOrDefault();
        }

        private static bool Full(TombStone t) => t != null && (t.GetComponent<Container>()?.GetInventory()?.NrOfItems() ?? 0) > 0;

        public static void Fetch(BrainState st, TombStone tomb)
        {
            st.GraveOrdered = tomb != null ? tomb : Find(st.Body);
            st.NextGraveLook = 0f;
        }

        public static bool Tick(BrainState st, Action<Vector3, float, bool> moveTo, Action stop)
        {
            Humanoid me = st.Body;
            if (Time.time < st.NextGraveLook) return false;
            if (st.GraveOrdered != null && !Full(st.GraveOrdered)) st.GraveOrdered = null;
            TombStone tomb = st.GraveOrdered ?? (Full(st.GraveOn) ? st.GraveOn : null);
            if (tomb == null)
            {
                if (Has(me) && Vector3.Distance(Pos(me), me.transform.position) < 30f && Find(me) == null)
                {
                    Companion.Write(me, z => z.Set(HasKey, false));
                    st.Remember(P("villagepleb_life2_remember_tomb_gone"));
                }
                tomb = Next(st);
                if (tomb == null) { st.GraveOn = null; st.NextGraveLook = Time.time + 5f; return false; }
            }
            if (st.GraveOn != tomb) { st.GraveOn = tomb; st.GraveBest = float.MaxValue; st.GraveSince = Time.time; }
            float d = Vector3.Distance(tomb.transform.position, me.transform.position);
            if (d > 2.2f)
            {
                if (st.GraveBest - d > 0.3f) { st.GraveBest = d; st.GraveSince = Time.time; }
                bool stuck = Time.time - st.GraveSince > 6f;
                if (stuck && d >= 8f)
                {
                    st.GraveBest = float.MaxValue;
                    st.GraveOrdered = null; st.GraveOn = null;
                    st.NextGraveLook = Time.time + 30f;
                    st.Remember(P("villagepleb_life2_remember_no_way_tomb"));
                    return false;
                }
                if (!stuck) { moveTo(tomb.transform.position, 1.5f, d > 6f); Brain.Status(st, P("villagepleb_life2_status_to_tomb")); return true; }
            }
            st.GraveBest = float.MaxValue;
            stop();
            Container grave = tomb.GetComponent<Container>();
            if (!Containers.Take(grave)) return true;
            Inventory mine = me.GetInventory(), its = grave.GetInventory();
            int took = 0, left = 0;
            foreach (ItemDrop.ItemData item in its.GetAllItems().ToList())
            {
                item.m_equipped = false;
                if (mine.CanAddItem(item)) { mine.MoveItemToThis(its, item); took++; } else left++;
            }
            if (left == 0 && Vector3.Distance(tomb.transform.position, Pos(me)) < 12f) Companion.Write(me, z => z.Set(HasKey, false));
            if (st.GraveOrdered == tomb) st.GraveOrdered = null;
            if (left == 0) { st.GraveOn = null; Net.Recovered(tomb.transform.position); }
            st.NextGear = 0f;
            st.Remember(left == 0 ? P("villagepleb_life2_remember_got_all", took) : P("villagepleb_life2_remember_got_some", took, left));
            VillagePlebsPlugin.Instance?.Note($"{Companion.NameOf(me)} took {took} item stacks back from its tombstone ({left} left)");
            int more = left == 0 ? Next(st) is TombStone n && n != tomb ? 1 : 0 : 0;
            Talk.Mention(me, more > 0 ? P("villagepleb_life2_say_another_tomb") : P("villagepleb_life2_say_got_things"));
            st.NextGraveLook = Time.time + (left > 0 ? 30f : 2f);
            return false;
        }
    }

    internal static class Loot
    {
        public const string Key = "vp_loot", ListKey = "vp_pick";
        public static bool On(Component c) => Companion.Zdo(c)?.GetBool(Key, true) ?? true;

        [Flags] public enum Kinds { None = 0, Ore = 1, Wood = 2, Stone = 4, Food = 8, Creature = 16, Gear = 32, Other = 64 }
        public const Kinds Default = Kinds.Ore | Kinds.Wood | Kinds.Stone | Kinds.Food | Kinds.Creature;
        public static Kinds List(Component c) => (Kinds)(Companion.Zdo(c)?.GetInt(ListKey, (int)Default) ?? (int)Default);

        private static readonly string[] WoodWords = { "Wood", "RoundLog", "ElderBark", "Resin" };
        private static readonly string[] StoneWords = { "Stone", "Flint", "Obsidian", "Marble", "Grausten", "Crystal" };
        private static readonly string[] CreatureWords = { "Hide", "Leather", "Pelt", "Scale", "Feather", "Bone", "Entrails", "Chitin", "Fang", "Tooth", "Horn", "Antler", "Ooze", "Wisp", "Carapace", "Mandible", "Guck", "Bloodbag", "Neck", "Tail" };

        public static Kinds KindOf(ItemDrop.ItemData i)
        {
            string name = i.m_dropPrefab != null ? i.m_dropPrefab.name : i.m_shared.m_name;
            var t = i.m_shared.m_itemType;
            if (t == ItemDrop.ItemData.ItemType.Trophy) return Kinds.Creature;
            if (i.IsWeapon() || t == ItemDrop.ItemData.ItemType.Shield || t == ItemDrop.ItemData.ItemType.Helmet || t == ItemDrop.ItemData.ItemType.Chest
                || t == ItemDrop.ItemData.ItemType.Legs || t == ItemDrop.ItemData.ItemType.Shoulder || t == ItemDrop.ItemData.ItemType.Utility || t == ItemDrop.ItemData.ItemType.Ammo) return Kinds.Gear;
            if (t == ItemDrop.ItemData.ItemType.Consumable || Work.IsCookable(i) || Food.IsFood(i)) return Kinds.Food;
            if (name.Contains("Ore") || name.Contains("Scrap") || name.Contains("Flametal") || (!i.m_shared.m_teleportable && t == ItemDrop.ItemData.ItemType.Material)) return Kinds.Ore;
            if (WoodWords.Any(w => name.Contains(w))) return Kinds.Wood;
            if (StoneWords.Any(w => name.Contains(w))) return Kinds.Stone;
            if (CreatureWords.Any(w => name.Contains(w))) return Kinds.Creature;
            return Kinds.Other;
        }

        public static bool Wants(Humanoid c, ItemDrop d) => d != null && On(c) && !d.m_itemData.m_pickedUp && (List(c) & KindOf(d.m_itemData)) != 0;

        public static void PassBy(BrainState st)
        {
            if (Time.time < st.NextPassBy) return;
            st.NextPassBy = Time.time + 0.3f;
            Humanoid me = st.Body;
            if (!On(me)) return;
            Inventory inv = me.GetInventory();
            Vector3 at = me.transform.position;
            foreach (ItemDrop d in Drops())
            {
                if (d == null || Vector3.Distance(d.transform.position, at) > 2.2f || !Wants(me, d) || !inv.CanAddItem(d.m_itemData)) continue;
                if (Carry.Weight(me) + d.m_itemData.GetWeight() > Carry.Max(me)) continue;
                if (!d.CanPickup(false)) { d.RequestOwn(); continue; }
                string token = d.m_itemData.m_shared.m_name;
                int n = d.m_itemData.m_stack;
                if (me.Pickup(d.gameObject, false, false)) { st.Gathered[token] = (st.Gathered.TryGetValue(token, out int had) ? had : 0) + n; Activity.Log(me, $"picked up {n} {Localization.instance.Localize(token)} on the way"); }
            }
        }

        internal class Spot { public Vector3 Pos; public float At; public HashSet<int> Before = new HashSet<int>(); }

        private static IEnumerable<ItemDrop> Drops() => GameFields.ItemDrops() ?? new List<ItemDrop>();

        public static void Fresh(ItemDrop drop)
        {
            if (drop == null) return;
            Vector3 pos = drop.transform.position;
            foreach (Humanoid c in Companion.All())
            {
                if (!c.GetComponent<ZNetView>().IsOwner() || Vector3.Distance(c.transform.position, pos) > 30f || !Wants(c, drop)) continue;
                BrainState st = Brain.Get(c);
                if (st.LootDrops.Count < 200) st.LootDrops.Add(new KeyValuePair<ItemDrop, float>(drop, Time.time));
            }
        }

        public static void Died(Character victim)
        {
            if (victim == null || Companion.Is(victim) || victim.IsPlayer()) return;
            Vector3 pos = victim.transform.position;
            foreach (Humanoid c in Companion.All())
            {
                if (!c.GetComponent<ZNetView>().IsOwner() || Vector3.Distance(c.transform.position, pos) > 25f || !On(c)) continue;
                AddSpot(Brain.Get(c), pos);
            }
        }

        public static void AddSpot(BrainState st, Vector3 pos)
        {
            if (st == null || !On(st.Body) || st.LootSpots.Any(s => Vector3.Distance(s.Pos, pos) < 2f && Time.time - s.At < 5f)) return;
            var spot = new Spot { Pos = pos, At = Time.time };
            foreach (ItemDrop d in Drops()) if (d != null && Vector3.Distance(d.transform.position, pos) < 10f) spot.Before.Add(d.GetInstanceID());
            st.LootSpots.Add(spot);
        }

        public static bool Tick(BrainState st, Action<Vector3, float, bool> moveTo)
        {
            Humanoid me = st.Body;
            st.LootSpots.RemoveAll(s => Time.time - s.At > 90f);
            st.LootDrops.RemoveAll(kv => kv.Key == null || Time.time - kv.Value > 180f);
            if (st.LootSpots.Count == 0 && st.LootDrops.Count == 0) return false;
            if (Work.BagFull(me)) return false;
            if (st.LootSkip.Count > 50) st.LootSkip.RemoveWhere(d => d == null);
            Inventory inv = me.GetInventory();
            ItemDrop next = null;
            float best = float.MaxValue;
            foreach (var kv in st.LootDrops)
            {
                ItemDrop d = kv.Key;
                if (Time.time - kv.Value < 0.6f || st.LootSkip.Contains(d) || !inv.CanAddItem(d.m_itemData)) continue;
                float dist = Vector3.Distance(d.transform.position, me.transform.position);
                if (dist < 40f && dist < best) { best = dist; next = d; }
            }
            foreach (ItemDrop d in Drops())
            {
                if (d == null || st.LootSkip.Contains(d) || !inv.CanAddItem(d.m_itemData) || !Wants(me, d)) continue;
                foreach (Spot s in st.LootSpots)
                {
                    float fromSpot = Vector3.Distance(d.transform.position, s.Pos);
                    if (fromSpot > 8f || s.Before.Contains(d.GetInstanceID())) continue;
                    float dist = Vector3.Distance(d.transform.position, me.transform.position);
                    if (dist < best) { best = dist; next = d; }
                }
            }
            if (next == null) { st.LootSpots.RemoveAll(s => Time.time - s.At > 20f); return false; }
            if (Carry.Weight(me) + next.m_itemData.GetWeight() > Carry.Max(me)) { st.LootDrops.RemoveAll(kv => kv.Key == next); return false; }
            st.LootTarget = next;
            if (best > 1.2f) { moveTo(next.transform.position, 0.5f, best > 6f); Brain.Status(st, P("villagepleb_life2_status_loot")); return true; }
            if (!next.CanPickup(false)) { next.RequestOwn(); return true; }
            string token = next.m_itemData.m_shared.m_name;
            int n = next.m_itemData.m_stack;
            if (me.GetInventory().CanAddItem(next.m_itemData) && me.Pickup(next.gameObject, false, false)) { st.Remember(P("villagepleb_life2_remember_picked_up", n, token)); st.Gathered[token] = (st.Gathered.TryGetValue(token, out int had) ? had : 0) + n; }
            st.LootDrops.RemoveAll(kv => kv.Key == next);
            return true;
        }
    }

    internal static class Fires
    {
        public static Fireplace Low(Humanoid me, Vector3 center, float radius)
        {
            List<CookingStation> stoves = Loaded.All<CookingStation>().Where(s => Vector3.Distance(s.transform.position, center) < radius).ToList();
            return Loaded.All<Fireplace>()
                .Where(f => f != null && !f.m_infiniteFuel && f.m_canRefill && f.m_fuelItem != null && Vector3.Distance(f.transform.position, center) < radius
                            && (Vector3.Distance(f.transform.position, center) < 10f || stoves.Any(s => Vector3.Distance(s.transform.position, f.transform.position) < 3f))
                            && Fuel(f) < f.m_maxFuel * 0.4f && Spare(me, f.m_fuelItem) > 0)
                .OrderBy(f => Vector3.Distance(f.transform.position, center)).FirstOrDefault();
        }

        private static int Spare(Humanoid me, ItemDrop fuel)
        {
            Goal g = Brain.Get(me)?.Goal;
            int needed = 0;
            if (g != null)
            {
                Recipe r = g.Recipe ?? (g.Item != null ? ObjectDB.instance?.GetRecipe(g.Item) : null);
                int quality = g.Item != null ? g.Item.m_quality + 1 : 1;
                if (r != null) needed = Upgrades.Needs(r).Where(q => q.m_resItem != null && q.m_resItem.m_itemData.m_shared.m_name == fuel.m_itemData.m_shared.m_name).Sum(q => q.GetAmount(quality));
            }
            return Have(me, fuel) - needed - 5;
        }

        private static float Fuel(Fireplace f) => Companion.Zdo(f)?.GetFloat(ZDOVars.s_fuel, 0f) ?? 0f;

        public static bool HasFuel(Humanoid me, ItemDrop fuel) => Have(me, fuel) > 0;

        private static int Have(Humanoid me, ItemDrop fuel)
        {
            string name = fuel.m_itemData.m_shared.m_name;
            return me.GetInventory().CountItems(name) + Home.Chests(me).Sum(c => c.GetInventory().CountItems(name));
        }

        // light: lighting it to cook on, so a few of its fuel do even if it keeps the rest for its mission.
        public static void Feed(BrainState st, Fireplace f, bool light = false)
        {
            Humanoid me = st.Body;
            ZNetView view = f.GetComponent<ZNetView>();
            if (view == null || !view.IsValid() || f.m_fuelItem == null) return;
            string name = f.m_fuelItem.m_itemData.m_shared.m_name;
            int spare = Spare(me, f.m_fuelItem);
            if (light) spare = Mathf.Max(spare, Mathf.Min(3, Have(me, f.m_fuelItem)));
            int want = Mathf.Min(Mathf.Min(6, spare), Mathf.FloorToInt(f.m_maxFuel - Fuel(f)));
            int added = 0;
            for (int i = 0; i < want; i++)
            {
                if (me.GetInventory().CountItems(name) > 0) me.GetInventory().RemoveItem(name, 1);
                else
                {
                    Container chest = Home.Chests(me).FirstOrDefault(c => !Containers.InUse(c) && c.GetInventory().CountItems(name) > 0);
                    if (chest == null || !Containers.Take(chest) || chest.GetInventory().CountItems(name) <= 0) break;
                    chest.GetInventory().RemoveItem(name, 1);
                }
                view.InvokeRPC("RPC_AddFuel");
                added++;
            }
            if (added == 0) return;
            st.Remember(P("villagepleb_life2_remember_fuel", added, Words.Low(name)));
        }
    }

    internal static class Kitchen
    {
        private static readonly System.Reflection.MethodInfo HaveDone = AccessTools.Method(typeof(CookingStation), "HaveDoneItem");
        private static readonly System.Reflection.MethodInfo FreeSlot = AccessTools.Method(typeof(CookingStation), "GetFreeSlot");
        private static readonly System.Reflection.MethodInfo FireLit = AccessTools.Method(typeof(CookingStation), "IsFireLit");
        private static readonly System.Reflection.MethodInfo Slot = AccessTools.Method(typeof(CookingStation), "GetSlot");

        public static bool Raw(CookingStation s, ItemDrop.ItemData i) => s.m_conversion.Any(c => c.m_from != null && c.m_from.m_itemData.m_shared.m_name == i.m_shared.m_name);

        private static bool Busy(CookingStation s)
        {
            for (int i = 0; i < s.m_slots.Length; i++)
            {
                object[] a = { i, null, null, null, null };
                Slot.Invoke(s, a);
                if (!string.IsNullOrEmpty(a[1] as string)) return true;
            }
            return false;
        }

        public static int CookAll(Humanoid me, Vector3 center, float radius)
        {
            CookingStation stove = Loaded.All<CookingStation>()
                .Where(s => Vector3.Distance(s.transform.position, center) < radius + 10f && Usable(s)).OrderBy(s => Vector3.Distance(s.transform.position, center)).FirstOrDefault();
            if (stove == null) return 0;
            int done = 0;
            var sources = new List<Inventory> { me.GetInventory() };
            foreach (Container c in Home.Chests(me).Where(c => Containers.Take(c))) sources.Add(c.GetInventory());
            foreach (Inventory inv in sources)
                foreach (ItemDrop.ItemData raw in inv.GetAllItems().ToList())
                {
                    CookingStation.ItemConversion conv = stove.m_conversion.FirstOrDefault(c => c.m_from != null && c.m_to != null && c.m_from.m_itemData.m_shared.m_name == raw.m_shared.m_name);
                    if (conv == null) continue;
                    int n = Mathf.Min(raw.m_stack, 20 - done);
                    if (n <= 0) return done;
                    if (!me.GetInventory().CanAddItem(conv.m_to.gameObject, n)) continue;
                    inv.RemoveItem(raw, n);
                    me.GetInventory().AddItem(conv.m_to.gameObject, n);
                    for (int k = 0; k < n; k++) Skill.Raise(me, stove.m_skill != Skills.SkillType.None ? stove.m_skill : Skills.SkillType.Cooking, 1f);
                    done += n;
                }
            return done;
        }

        public static IEnumerable<string> Describe(Humanoid me, Vector3 center, float radius) =>
            Loaded.All<CookingStation>().Where(s => Vector3.Distance(s.transform.position, center) < radius + 60f)
                .OrderBy(s => Vector3.Distance(s.transform.position, center))
                .Select(s => $"{Localization.instance.Localize(s.m_name)} at {s.transform.position:F0}: {Vector3.Distance(s.transform.position, center):0} m from home, {Vector3.Distance(s.transform.position, me.transform.position):0} m from it, " +
                             $"fire {(!s.m_requireFire ? "not needed" : (bool)FireLit.Invoke(s, null) ? "lit" : "OUT")}, free spot {((int)FreeSlot.Invoke(s, null) >= 0 ? "yes" : "no")}, " +
                             $"cooks its food {me.GetInventory().GetAllItems().Any(i => Raw(s, i))}, reachable {Brain.CanReach(me, s.transform.position)}, " + FireUnder(s));

        private static string FireUnder(CookingStation s)
        {
            Fireplace f = Loaded.All<Fireplace>().OrderBy(x => Vector3.Distance(x.transform.position, s.transform.position)).FirstOrDefault();
            if (f == null) return "no fireplace";
            float fuel = Companion.Zdo(f)?.GetFloat(ZDOVars.s_fuel, 0f) ?? 0f;
            return $"nearest fire {Utils.GetPrefabName(f.gameObject)} {Vector3.Distance(f.transform.position, s.transform.position):0.0} m off (flat {Vector2.Distance(new Vector2(f.transform.position.x, f.transform.position.z), new Vector2(s.transform.position.x, s.transform.position.z)):0.0}), fuel {fuel:0.#}/{f.m_maxFuel:0}, burning {f.IsBurning()}";
        }

        internal static bool Usable(CookingStation s) => s != null && (!s.m_requireFire || (bool)FireLit.Invoke(s, null)) && (!s.m_useFuel || OwnFuel(s) > 0f);

        // What a station that burns its own fuel (an oven) has left in it.
        private static float OwnFuel(CookingStation s) => Companion.Zdo(s)?.GetFloat(ZDOVars.s_fuel, 0f) ?? 0f;

        // Fuels a station that burns its own (an oven), from its bag, then its chests, as a player puts it in: a few at a time.
        public static void Fuel(BrainState st, CookingStation s)
        {
            Humanoid me = st.Body;
            ZNetView view = s.GetComponent<ZNetView>();
            if (view == null || !view.IsValid() || !s.m_useFuel || s.m_fuelItem == null) return;
            string name = s.m_fuelItem.m_itemData.m_shared.m_name;
            int want = Mathf.Min(4, Mathf.FloorToInt(s.m_maxFuel - OwnFuel(s))), added = 0;
            for (int i = 0; i < want; i++)
            {
                if (me.GetInventory().CountItems(name) > 0) me.GetInventory().RemoveItem(name, 1);
                else
                {
                    Container chest = Home.Chests(me).FirstOrDefault(c => !Containers.InUse(c) && c.GetInventory().CountItems(name) > 0);
                    if (chest == null || !Containers.Take(chest) || chest.GetInventory().CountItems(name) <= 0) break;
                    chest.GetInventory().RemoveItem(name, 1);
                }
                view.InvokeRPC("RPC_AddFuel");
                added++;
            }
            if (added > 0) st.Remember(P("villagepleb_life2_remember_fuel", added, Words.Low(name)));
        }

        // A cooking station that would cook this food but for its fire being out (or, an oven, its own fuel run out), and what to light:
        // the fire under it, or the station itself. It has the fuel for it.
        public static (CookingStation Stove, Component Fire) ToLight(Humanoid me, Vector3 center, float radius, IEnumerable<ItemDrop.ItemData> food)
        {
            var raw = food.ToList();
            if (raw.Count == 0) return (null, null);
            var fires = Loaded.All<Fireplace>()
                .Where(f => f != null && !f.m_infiniteFuel && f.m_canRefill && f.m_fuelItem != null && !f.IsBurning() && Fires.HasFuel(me, f.m_fuelItem)).ToList();
            foreach (CookingStation s in Loaded.All<CookingStation>()
                         .Where(s => (s.m_requireFire || s.m_useFuel) && Vector3.Distance(s.transform.position, center) < radius + 10f && !Usable(s) && raw.Any(i => Raw(s, i)) && (int)FreeSlot.Invoke(s, null) >= 0)
                         .OrderBy(s => Vector3.Distance(s.transform.position, center)))
            {
                if (s.m_useFuel && OwnFuel(s) <= 0f)
                {
                    if (s.m_fuelItem != null && Fires.HasFuel(me, s.m_fuelItem) && Brain.CanReach(me, s.transform.position, 2f)) return (s, s);
                    continue;
                }
                if (!s.m_requireFire) continue;
                Fireplace fire = fires.Where(f => Flat(f.transform.position, s.transform.position) < 2.5f).OrderBy(f => Vector3.Distance(f.transform.position, s.transform.position)).FirstOrDefault();
                if (fire != null && Brain.CanReach(me, fire.transform.position, 2f)) return (s, fire);
            }
            return (null, null);
        }

        private static float Flat(Vector3 a, Vector3 b) { a.y = 0f; b.y = 0f; return Vector3.Distance(a, b); }

        public static CookingStation Find(Humanoid me, Vector3 center, float radius)
        {
            var raw = me.GetInventory().GetAllItems();
            if (raw.Count == 0) return null;
            return Loaded.All<CookingStation>()
                .Where(s => Vector3.Distance(s.transform.position, center) < radius + 10f && Usable(s) && raw.Any(i => Raw(s, i)) && (int)FreeSlot.Invoke(s, null) >= 0)
                .OrderBy(s => Vector3.Distance(s.transform.position, center)).FirstOrDefault();
        }

        public static bool Cook(BrainState st, CookingStation s)
        {
            Humanoid me = st.Body;
            ZNetView view = s.GetComponent<ZNetView>();
            if (view == null || !view.IsValid()) return false;
            if ((bool)HaveDone.Invoke(s, null))
            {
                view.InvokeRPC("RPC_RemoveDoneItem", me.transform.position, 1);
                st.CookedAt = s.transform.position;
                Skill.Raise(me, s.m_skill != Skills.SkillType.None ? s.m_skill : Skills.SkillType.Cooking, 0.6f);
                return true;
            }
            if (Usable(s) && (int)FreeSlot.Invoke(s, null) >= 0)
            {
                ItemDrop.ItemData raw = me.GetInventory().GetAllItems().FirstOrDefault(i => Raw(s, i));
                if (raw != null && s.UseItem(me, raw))
                {
                    st.Remember(P("villagepleb_life2_remember_cooking", raw.m_shared.m_name));
                    Skill.Raise(me, s.m_skill != Skills.SkillType.None ? s.m_skill : Skills.SkillType.Cooking, 0.4f);
                    return true;
                }
            }
            return Busy(s);
        }
    }

    internal static class Rest
    {
        public const float Duration = 480f, Mult = 1.5f;
        private static readonly Dictionary<Humanoid, float> Sheltered = Cleanup.Track(new Dictionary<Humanoid, float>()), Until = Cleanup.Track(new Dictionary<Humanoid, float>());

        public static bool IsRested(Humanoid c) => Until.TryGetValue(c, out float u) && Time.time < u;
        public static float Left(Humanoid c) => Until.TryGetValue(c, out float u) ? Mathf.Max(0f, u - Time.time) : 0f;

        public static void Update(Humanoid c, bool shelter, bool fire)
        {
            if (shelter && fire)
            {
                if (!Sheltered.TryGetValue(c, out float since)) Sheltered[c] = since = Time.time;
                if (Time.time - since > 20f)
                {
                    bool was = IsRested(c);
                    Until[c] = Time.time + Duration;
                    if (!was) Brain.Get(c)?.Remember(P("villagepleb_life2_remember_rested"));
                }
            }
            else Sheltered.Remove(c);
        }

        public static void Forget() { Sheltered.Clear(); Until.Clear(); }
    }

    internal static class Eitr
    {
        private class Pool { public float Value, LastUse = -99f; }
        private static readonly Dictionary<Character, Pool> Pools = Cleanup.Track(new Dictionary<Character, Pool>());

        public static float Max(Character c) => c is Humanoid h ? Food.MaxEitr(h) : 0f;

        private static Pool Of(Character c)
        {
            if (!Pools.TryGetValue(c, out Pool p)) Pools[c] = p = new Pool { Value = Max(c) };
            return p;
        }

        public static void Reload(Character c) => Pools.Remove(c);

        public static float Get(Character c) => Mathf.Min(Max(c), Of(c).Value);
        public static void Use(Character c, float v) { Pool p = Of(c); p.Value = Mathf.Max(0f, p.Value - v); p.LastUse = Time.time; }
        public static void Add(Character c, float v) { Pool p = Of(c); p.Value = Mathf.Min(Max(c), p.Value + v); }

        public static void Tick(Humanoid c, float dt)
        {
            Pool p = Of(c);
            float max = Max(c);
            if (p.Value > max) p.Value = max;
            if (Time.time - p.LastUse < 2f || p.Value >= max) return;
            float mult = 1f;
            c.GetSEMan().ModifyEitrRegen(ref mult);
            p.Value = Mathf.Min(max, p.Value + 2f * mult * dt);
        }

        public static void Forget() => Pools.Clear();
    }

    internal static class Carry
    {
        public static float Max(Humanoid c) => MainConfig.baseCarry.Value + Companion.Worn(c).Select(i => i.m_shared.m_equipStatusEffect as SE_Stats).Where(se => se != null).Sum(se => se.m_addMaxCarryWeight);
        public static float Weight(Humanoid c) => c.GetInventory().GetTotalWeight();
        public static bool Over(Humanoid c) => Weight(c) > Max(c);
    }

    [HarmonyPatch(typeof(TombStone), nameof(TombStone.Interact))]
    internal static class TombStone_Interact
    {
        private static bool Prefix(TombStone __instance, Humanoid character, bool hold, ref bool __result)
        {
            if (hold) return true;
            try
            {
                if (Grave.Interact(__instance, character)) return true;
                __result = true;
                return false;
            }
            catch (Exception e) { VillagePlebsPlugin.Instance?.Warn("Companion tombstone: " + e.Message); return true; }
        }
    }

    [HarmonyPatch(typeof(TombStone), nameof(TombStone.GetHoverText))]
    internal static class TombStone_GetHoverText
    {
        private static void Postfix(TombStone __instance, ref string __result)
        {
            if ((Companion.Zdo(__instance)?.GetLong(Grave.OfKey, 0L) ?? 0L) != 0L)
                __result += "\n<size=14>" + T("villagepleb_life2_tomb_hover") + "</size>";
        }
    }

    [HarmonyPatch(typeof(ItemDrop), nameof(ItemDrop.OnCreateNew), new[] { typeof(ItemDrop), typeof(bool) })]
    internal static class ItemDrop_OnCreateNew_Item
    {
        private static void Postfix(ItemDrop item) { try { Loot.Fresh(item); } catch (Exception) { } }
    }

    [HarmonyPatch(typeof(ItemDrop), nameof(ItemDrop.OnCreateNew), new[] { typeof(GameObject), typeof(bool) })]
    internal static class ItemDrop_OnCreateNew_Object
    {
        private static void Postfix(GameObject go) { try { Loot.Fresh(go != null ? go.GetComponent<ItemDrop>() : null); } catch (Exception) { } }
    }

    [HarmonyPatch(typeof(Character), nameof(Character.HaveEitr))]
    internal static class Character_HaveEitr
    {
        private static bool Prefix(Character __instance, float amount, ref bool __result)
        {
            if (!Companion.Is(__instance)) return true;
            __result = Eitr.Get(__instance) > amount;
            return false;
        }
    }

    [HarmonyPatch(typeof(Character), nameof(Character.UseEitr))]
    internal static class Character_UseEitr
    {
        private static bool Prefix(Character __instance, float eitr) { if (!Companion.Is(__instance)) return true; Eitr.Use(__instance, eitr); return false; }
    }

    [HarmonyPatch(typeof(Character), nameof(Character.AddEitr))]
    internal static class Character_AddEitr
    {
        private static bool Prefix(Character __instance, float v) { if (!Companion.Is(__instance)) return true; Eitr.Add(__instance, v); return false; }
    }

    [HarmonyPatch(typeof(Character), nameof(Character.GetMaxEitr))]
    internal static class Character_GetMaxEitr
    {
        private static void Postfix(Character __instance, ref float __result) { if (Companion.Is(__instance)) __result = Eitr.Max(__instance); }
    }

    [HarmonyPatch(typeof(Character), nameof(Character.GetEitrPercentage))]
    internal static class Character_GetEitrPercentage
    {
        private static void Postfix(Character __instance, ref float __result) { if (Companion.Is(__instance)) __result = Eitr.Max(__instance) > 0f ? Eitr.Get(__instance) / Eitr.Max(__instance) : 0f; }
    }
}
