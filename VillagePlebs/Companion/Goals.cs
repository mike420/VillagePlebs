using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using static VillagePlebs.Words;

namespace VillagePlebs
{
    internal class Goal
    {
        public string What = "";
        public ItemDrop.ItemData Item;
        public Recipe Recipe;
        public CraftingStation Station;
        public readonly Dictionary<string, int> Raw = new Dictionary<string, int>();
        public readonly Dictionary<string, string> Names = new Dictionary<string, string>();
        public readonly List<string> Ask = new List<string>();
        public readonly List<KeyValuePair<Recipe, CraftingStation>> Steps = new List<KeyValuePair<Recipe, CraftingStation>>();
        public readonly HashSet<string> Smelt = new HashSet<string>();
        public int Cost;
        public bool NoSmelter;

        public bool Wants(string prefab) => prefab != null && Raw.ContainsKey(prefab);

        public string RawText() => string.Join(", ", Raw.Select(kv => P("villagepleb_goals_amount", kv.Value, Low(Names[kv.Key]))));

        public IEnumerable<string> Things() => Names.Values.Select(Goals.Thing);
    }

    internal static class Goals
    {
        private class Source { public Job Job; public int Tier; public string Creature, Prefab; }

        private static Dictionary<string, List<Source>> _sources;
        private static Dictionary<string, ItemDrop> _smeltedFrom;
        private static HashSet<string> _crops;
        private static Dictionary<string, float> _unfindable;
        private static List<Smelter> _smelters;

        private static readonly HashSet<string> Prey = new HashSet<string> { "Deer", "Boar", "Neck", "Hare" };

        public static void Forget() { _sources = null; _smeltedFrom = null; _crops = null; }

        private static void Learn()
        {
            if (_sources != null || ZNetScene.instance == null) return;
            _sources = new Dictionary<string, List<Source>>();
            _smeltedFrom = new Dictionary<string, ItemDrop>();
            _crops = new HashSet<string>();
            foreach (GameObject prefab in ZNetScene.instance.m_prefabs)
            {
                Plant plant = prefab != null ? prefab.GetComponent<Plant>() : null;
                if (plant?.m_grownPrefabs != null) foreach (GameObject grown in plant.m_grownPrefabs) if (grown != null) _crops.Add(grown.name);
            }
            foreach (GameObject prefab in ZNetScene.instance.m_prefabs)
            {
                if (prefab == null) continue;
                TreeBase tree = prefab.GetComponent<TreeBase>();
                if (tree != null) Add(tree.m_dropWhenDestroyed, Job.Wood, tree.m_minToolTier);
                TreeLog log = prefab.GetComponent<TreeLog>();
                if (log != null) Add(log.m_dropWhenDestroyed, Job.Wood, log.m_minToolTier);
                Destructible des = prefab.GetComponent<Destructible>();
                DropTable drops = prefab.GetComponent<DropOnDestroyed>()?.m_dropWhenDestroyed;
                if (des != null && drops != null && prefab.GetComponent<Piece>() == null && prefab.GetComponent<Character>() == null)
                {
                    bool wood = des.m_destructibleType == DestructibleType.Tree || des.m_damages.m_chop != HitData.DamageModifier.Immune && des.m_damages.m_pickaxe == HitData.DamageModifier.Immune;
                    if (wood) Add(drops, Job.Wood, des.m_minToolTier);
                    else if (des.m_damages.m_pickaxe != HitData.DamageModifier.Immune) Add(drops, Work.IsOre(drops) ? Job.Ore : Job.Stone, des.m_minToolTier);
                }
                MineRock rock = prefab.GetComponent<MineRock>();
                if (rock != null) Add(rock.m_dropItems, Work.IsOre(rock.m_dropItems) ? Job.Ore : Job.Stone, rock.m_minToolTier);
                MineRock5 rock5 = prefab.GetComponent<MineRock5>();
                if (rock5 != null) Add(rock5.m_dropItems, Work.IsOre(rock5.m_dropItems) ? Job.Ore : Job.Stone, rock5.m_minToolTier);
                Pickable pick = prefab.GetComponent<Pickable>();
                if (pick != null && pick.m_itemPrefab != null && !_crops.Contains(prefab.name)) AddOne(pick.m_itemPrefab.name, new Source { Job = Job.Forage, Prefab = prefab.name });
                CharacterDrop cd = prefab.GetComponent<CharacterDrop>();
                if (cd != null && Prey.Contains(prefab.name))
                    foreach (CharacterDrop.Drop d in cd.m_drops) if (d?.m_prefab != null) AddOne(d.m_prefab.name, new Source { Job = Job.Hunt, Creature = prefab.name });
                Smelter smelter = prefab.GetComponent<Smelter>();
                if (smelter != null)
                    foreach (Smelter.ItemConversion conv in smelter.m_conversion)
                        if (conv?.m_from != null && conv.m_to != null && !_smeltedFrom.ContainsKey(conv.m_to.gameObject.name)) _smeltedFrom[conv.m_to.gameObject.name] = conv.m_from;
            }
        }

        private static void Add(DropTable table, Job job, int tier)
        {
            if (table?.m_drops == null) return;
            foreach (DropTable.DropData d in table.m_drops) if (d.m_item != null) AddOne(d.m_item.name, new Source { Job = job, Tier = tier });
        }

        private static void AddOne(string item, Source s)
        {
            if (!_sources.TryGetValue(item, out List<Source> list)) _sources[item] = list = new List<Source>();
            list.Add(s);
        }

        private static bool CanGather(Humanoid me, string item)
        {
            if (!_sources.TryGetValue(item, out List<Source> list)) return false;
            if (_unfindable != null && _unfindable.TryGetValue(item, out float until) && until > Time.time) return false;
            int axe = Work.Axe(me)?.m_shared.m_toolTier ?? -1, pick = Work.Pickaxe(me)?.m_shared.m_toolTier ?? -1;
            bool armed = Companion.BestMelee(me) != null || Companion.BestRanged(me) != null;
            return list.Any(s => s.Job == Job.Forage || (s.Job == Job.Hunt && armed) || (s.Job == Job.Wood && axe >= s.Tier) || ((s.Job == Job.Stone || s.Job == Job.Ore) && pick >= s.Tier));
        }

        public static bool DroppedBy(string item, Job job)
        {
            Learn();
            return _sources != null && _sources.TryGetValue(item, out List<Source> list) && list.Any(s => (s.Job & job) != 0);
        }

        public static IEnumerable<string> PickablesFor(string item)
        {
            Learn();
            return _sources != null && _sources.TryGetValue(item, out List<Source> list) ? list.Where(s => s.Prefab != null).Select(s => s.Prefab).Distinct() : Enumerable.Empty<string>();
        }

        public static bool IsCrop(string prefab) { Learn(); return _crops != null && _crops.Contains(prefab); }

        public static Job JobsFor(Goal g, out HashSet<string> creatures)
        {
            creatures = new HashSet<string>();
            Job jobs = Job.None;
            if (g == null || _sources == null) return jobs;
            foreach (string item in g.Raw.Keys)
                if (_sources.TryGetValue(item, out List<Source> list))
                    foreach (Source s in list) { jobs |= s.Job; if (s.Creature != null) creatures.Add(s.Creature); }
            return jobs;
        }

        private static int Have(Humanoid me, List<Container> chests, ItemDrop item)
        {
            string name = item.m_itemData.m_shared.m_name;
            return me.GetInventory().CountItems(name) + chests.Sum(c => c != null ? c.GetInventory().CountItems(name) : 0);
        }

        // One at this level or better already in its bag or its chests: a mission for it would be done the moment it was set (the
        // mission counts its chests too). With a bronze buckler in its chest it set itself "make a bronze buckler", found it done,
        // and set it again, every second.
        private static bool Owned(Humanoid me, List<Container> chests, string name, int quality) =>
            me.GetInventory().GetAllItems().Concat(chests.Where(c => c != null).SelectMany(c => c.GetInventory().GetAllItems())).Any(i => i.m_shared.m_name == name && i.m_quality >= quality);

        public static Goal Pick(Humanoid me, Vector3 center, float radius, BrainState st = null)
        {
            Learn();
            if (_sources == null || ObjectDB.instance == null) return null;
            _unfindable = st?.Unfindable;
            _smelters = Smelting.Near(center, radius).ToList();
            List<CraftingStation> stations = Upgrades.StationsNear(center, radius + 10f);
            List<Container> chests = Home.Chests(me);
            var goals = new List<Goal>();

            foreach (ItemDrop.ItemData item in me.GetInventory().GetAllItems().Where(i => Upgrades.Upgradable(i)))
            {
                Recipe r = ObjectDB.instance.GetRecipe(item);
                if (r == null || r.m_craftingStation == null || Upgrades.SkipItem(item, item.m_dropPrefab != null ? item.m_dropPrefab.name : null)) continue;
                int next = item.m_quality + 1;
                if (Owned(me, chests, item.m_shared.m_name, next)) continue;
                CraftingStation at = stations.FirstOrDefault(s => s.m_name == r.m_craftingStation.m_name && s.GetLevel() >= r.GetRequiredStationLevel(next));
                if (at == null) continue;
                var g = new Goal { Item = item, Station = at, What = P("villagepleb_goals_upgrade_what", Low(item.m_shared.m_name), next) };
                if (Build(me, chests, stations, g, r, next)) { if (me.IsItemEquiped(item)) g.Cost -= 1; goals.Add(g); }
            }
            foreach (Recipe r in ObjectDB.instance.m_recipes)
            {
                if (r == null || !r.m_enabled || r.m_item == null || !Upgrades.Allowed(me, r) || !Upgrades.WorthMaking(me, r.m_item.m_itemData)) continue;
                if (r.m_item.m_itemData.m_shared.m_itemType == ItemDrop.ItemData.ItemType.Ammo) continue;
                if (Owned(me, chests, r.m_item.m_itemData.m_shared.m_name, 1)) continue;
                CraftingStation at = r.m_craftingStation == null ? null : stations.FirstOrDefault(s => s.m_name == r.m_craftingStation.m_name && s.GetLevel() >= Mathf.Max(1, r.m_minStationLevel));
                if (at == null && r.m_craftingStation != null) continue;
                var g = new Goal { Recipe = r, Station = at, What = P("villagepleb_goals_make_what", Low(r.m_item.m_itemData.m_shared.m_name)) };
                if (Build(me, chests, stations, g, r, 1)) goals.Add(g);
            }
            _smelters = null;
            // Bars (or anything else a smelter makes) it has not got and no smelter near home makes: that waits until one is built.
            if (st != null) { st.NoSmelter.Clear(); foreach (Goal wait in goals.Where(x => x.NoSmelter)) st.NoSmelter.Add(Missions.KeyOf(wait)); }
            var ranked = goals.Where(g => !g.NoSmelter && (st == null || !Missions.Blocked(st, Missions.KeyOf(g))))
                              .OrderBy(g => g.Ask.Count > 0 ? 1 : 0).ThenBy(g => g.Ask.Count).ThenBy(g => g.Cost).ToList();
            if (st != null)
            {
                string mission = Missions.Current(me)?.Key;
                Goal kept = mission != null ? ranked.FirstOrDefault(g => Missions.KeyOf(g) == mission) : null;
                if (kept != null) { ranked.Remove(kept); ranked.Insert(0, kept); }
                st.Candidates = ranked.Take(4).ToList();
            }
            return ranked.FirstOrDefault();
        }

        public static Goal ForNeeds(Humanoid me, Vector3 center, float radius, BrainState st, IEnumerable<KeyValuePair<ItemDrop, int>> short_, List<Container> chests, string what)
        {
            Learn();
            if (_sources == null || ObjectDB.instance == null) return null;
            _unfindable = st?.Unfindable;
            List<CraftingStation> stations = Upgrades.StationsNear(center, radius + 10f);
            var g = new Goal { What = what };
            foreach (var kv in short_) if (kv.Key != null && kv.Value > 0) Expand(me, chests, stations, g, kv.Key, kv.Value, 0);
            return g.Cost > 0 ? g : null;
        }

        private static bool Build(Humanoid me, List<Container> chests, List<CraftingStation> stations, Goal g, Recipe r, int quality)
        {
            bool missing = false;
            foreach (Piece.Requirement q in Upgrades.Needs(r))
            {
                if (q.m_resItem == null) continue;
                int need = q.GetAmount(quality) - Have(me, chests, q.m_resItem);
                if (need <= 0) continue;
                missing = true;
                Expand(me, chests, stations, g, q.m_resItem, need, 0);
            }
            return missing;
        }

        private static void Expand(Humanoid me, List<Container> chests, List<CraftingStation> stations, Goal g, ItemDrop item, int need, int depth)
        {
            string prefab = item.gameObject.name;
            if (depth > 0) need -= Have(me, chests, item);
            if (need <= 0) return;
            // Metal (or anything made from what a smelter makes) it has not got: only with a smelter near home that makes it, however
            // else it might turn up.
            if (_smelters != null && NeedsSmelter(me, chests, item, depth)) { g.NoSmelter = true; return; }
            g.Cost += need;
            if (CanGather(me, prefab))
            {
                g.Raw[prefab] = (g.Raw.TryGetValue(prefab, out int had) ? had : 0) + need;
                g.Names[prefab] = item.m_itemData.m_shared.m_name;
                return;
            }
            if (depth < 3 && _smeltedFrom.TryGetValue(prefab, out ItemDrop ore))
            {
                if (_smelters != null && !_smelters.Any(s => Smelting.Makes(s, prefab))) { g.NoSmelter = true; return; }
                g.Smelt.Add(item.m_itemData.m_shared.m_name);
                Expand(me, chests, stations, g, ore, need, depth + 1);
                return;
            }
            Recipe made = depth < 3 ? ObjectDB.instance.GetRecipe(item.m_itemData) : null;
            CraftingStation at = made?.m_craftingStation != null ? stations.FirstOrDefault(s => s.m_name == made.m_craftingStation.m_name && s.GetLevel() >= Mathf.Max(1, made.m_minStationLevel)) : null;
            if (made != null && (at != null || made.m_craftingStation == null) && Upgrades.Needs(made).Any())
            {
                int batches = Mathf.CeilToInt(need / (float)Mathf.Max(1, made.m_amount));
                foreach (Piece.Requirement q in Upgrades.Needs(made)) if (q.m_resItem != null) Expand(me, chests, stations, g, q.m_resItem, q.GetAmount(1) * batches, depth + 1);
                if (!g.Steps.Any(s => s.Key == made)) g.Steps.Add(new KeyValuePair<Recipe, CraftingStation>(made, at));
                return;
            }
            g.Ask.Add(P("villagepleb_goals_amount", need, Low(item.m_itemData.m_shared.m_name)));
            g.Cost += 100;
        }

        // Whether this, or what it is made from (and not already to hand), comes out of a smelter with none near home making it.
        private static bool NeedsSmelter(Humanoid me, List<Container> chests, ItemDrop item, int depth)
        {
            string prefab = item.gameObject.name;
            if (_smeltedFrom.ContainsKey(prefab)) return !_smelters.Any(s => Smelting.Makes(s, prefab));
            if (depth >= 3) return false;
            Recipe r = ObjectDB.instance.GetRecipe(item.m_itemData);
            return r != null && Upgrades.Needs(r).Any(q => q.m_resItem != null && Have(me, chests, q.m_resItem) < q.GetAmount(1) && NeedsSmelter(me, chests, q.m_resItem, depth + 1));
        }

        public static KeyValuePair<Recipe, CraftingStation>? StepReady(Humanoid me, Goal g)
        {
            if (g == null) return null;
            foreach (var step in g.Steps)
            {
                List<Container> chests = Upgrades.ChestsNear(me, step.Value != null ? step.Value.transform.position : me.transform.position);
                if (Upgrades.Needs(step.Key).All(q => q.m_resItem == null || Have(me, chests, q.m_resItem) >= q.GetAmount(1))) return step;
            }
            return null;
        }

        public static void Announce(BrainState st, Goal g)
        {
            Humanoid me = st.Body;
            if (g == null) return;
            if (st.GoalSaid != g.What)
            {
                st.GoalSaid = g.What;
                string need = g.Raw.Count > 0 ? " " + P("villagepleb_goals_still_need", g.RawText()) : "";
                string smelt = g.Smelt.Count > 0 ? " " + P(g.Smelt.Count == 1 ? "villagepleb_goals_smelter_one" : "villagepleb_goals_smelter_many", And(g.Smelt.Select(Thing))) : "";
                Talk.Mention(me, P("villagepleb_goals_working_toward", g.What) + need + smelt, "goal:" + g.What, 30f);
                st.Remember(P("villagepleb_goals_remember_working", g.What));
            }
            if (g.Ask.Count > 0)
                Talk.Tell(me, P("villagepleb_goals_ask", g.What, And(g.Ask)), "ask:" + g.What, 60f);
        }

        public static string Thing(string token) => P("villagepleb_goals_thing", Low(token));

        public static string And(IEnumerable<string> items) => Join(items, "villagepleb_goals_and");

        public static string Or(IEnumerable<string> items) => Join(items, "villagepleb_goals_or");

        private static string Join(IEnumerable<string> items, string key)
        {
            List<string> list = items.ToList();
            if (list.Count <= 1) return list.FirstOrDefault() ?? "";
            return P(key, string.Join(", ", list.Take(list.Count - 1)), list[list.Count - 1]);
        }
    }
}
