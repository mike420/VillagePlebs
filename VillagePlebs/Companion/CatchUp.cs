using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using UnityEngine;
using static VillagePlebs.Words;

namespace VillagePlebs
{
    internal static class CatchUp
    {
        public const string LastKey = "vp_lastsim";
        private const float MinAway = 120f, MaxAway = 3f * 1800f;

        private static readonly Dictionary<Heightmap.Biome, string[]> Creatures = new Dictionary<Heightmap.Biome, string[]>
        {
            [Heightmap.Biome.Meadows] = new[] { "Boar", "Neck", "Greyling" },
            [Heightmap.Biome.BlackForest] = new[] { "Greydwarf", "Greydwarf", "Greydwarf_Elite", "Skeleton" },
            [Heightmap.Biome.Swamp] = new[] { "Draugr", "Leech", "Skeleton_Poison", "Blob" },
            [Heightmap.Biome.Mountain] = new[] { "Wolf", "Fenring", "Hatchling" },
            [Heightmap.Biome.Plains] = new[] { "Goblin", "Deathsquito", "Lox" },
            [Heightmap.Biome.Mistlands] = new[] { "Seeker", "Tick" },
            [Heightmap.Biome.AshLands] = new[] { "Charred_Melee", "Asksvin" },
        };

        private static readonly Dictionary<Heightmap.Biome, float> FightsPerHour = new Dictionary<Heightmap.Biome, float>
        {
            [Heightmap.Biome.Meadows] = 0.4f, [Heightmap.Biome.BlackForest] = 1.2f, [Heightmap.Biome.Swamp] = 2f, [Heightmap.Biome.Mountain] = 1.5f,
            [Heightmap.Biome.Plains] = 2f, [Heightmap.Biome.Mistlands] = 2f, [Heightmap.Biome.AshLands] = 3f,
        };

        private static readonly System.Collections.Generic.Dictionary<Heightmap.Biome, float> Threat = new System.Collections.Generic.Dictionary<Heightmap.Biome, float>
        {
            [Heightmap.Biome.Meadows] = 40f, [Heightmap.Biome.BlackForest] = 90f, [Heightmap.Biome.Swamp] = 160f, [Heightmap.Biome.Mountain] = 200f,
            [Heightmap.Biome.Plains] = 280f, [Heightmap.Biome.Mistlands] = 350f, [Heightmap.Biome.AshLands] = 450f,
        };

        public static float ThreatOf(Heightmap.Biome b) => Threat.TryGetValue(b, out float t) ? t : 100f;

        internal static float Power(Humanoid me)
        {
            ItemDrop.ItemData w = Companion.BestMelee(me) ?? Companion.BestRanged(me);
            float weapon = w != null ? w.GetDamage().GetTotalDamage() * Mathf.Lerp(0.4f, 1f, Skill.Get(me, w.m_shared.m_skillType) / 100f) * 3f : 5f;
            return weapon + Companion.Armor(me) * 2f + me.GetMaxHealth();
        }

        private static Vector3 Fall(Humanoid me, Vector3 center, float radius)
        {
            Vector2 r = UnityEngine.Random.insideUnitCircle * radius * 0.6f;
            Vector3 at = center + new Vector3(r.x, 0f, r.y);
            if (ZoneSystem.instance != null && ZoneSystem.instance.GetSolidHeight(at, out float h)) at.y = h + 0.5f;
            Inventory inv = me.GetInventory();
            var worn = new HashSet<ItemDrop.ItemData>(inv.GetAllItems().Where(Gear.InSlot));
            foreach (ItemDrop.ItemData i in inv.GetAllItems()) i.m_equipped = worn.Contains(i);
            bool any = inv.NrOfItems() > worn.Count;
            if (any) Companion.Tomb(me, inv, at, Quaternion.identity);
            foreach (ItemDrop.ItemData i in inv.GetAllItems()) i.m_equipped = false;
            Companion.SaveBag(me);
            ZDO z = Companion.Zdo(me);
            z.Set(Grave.HasKey, any);
            z.Set(Grave.PosKey, at);
            z.Set(Skill.Key, Skill.AfterDeath(Skill.Text(me)));
            EpicMmo.SetXp(me, EpicMmo.AfterDeath(EpicMmo.Xp(me)));
            Skill.Forget(me);
            Food.Clear(me);
            if (any) Net.AnnounceFall(me, at, true, lost: true);
            return at;
        }

        public static void Stamp(Humanoid c) => Companion.Zdo(c)?.Set(LastKey, (long)ZNet.instance.GetTimeSeconds());

        public static void OnArrive(BrainState st)
        {
            Humanoid me = st.Body;
            st.CaughtUp = true;
            ZDO z = Companion.Zdo(me);
            long last = z.GetLong(LastKey, 0L);
            Stamp(me);
            if (last <= 0L || Companion.OrderOf(me) != Order.Gather) return;
            float away = (float)(ZNet.instance.GetTimeSeconds() - last);
            if (away < MinAway) return;
            VillagePlebsPlugin.Instance.StartCoroutine(Run(st, Mathf.Min(away, MaxAway), away));
        }

        internal static string Stage = "";

        private static IEnumerator Run(BrainState st, float seconds, float away)
        {
            yield return new WaitForSeconds(3f);
            Humanoid me = st.Body;
            if (me == null || me.IsDead()) yield break;
            if (me.GetComponent<ZNetView>() is ZNetView v && (!v.IsValid() || !v.IsOwner()))
            {
                VillagePlebsPlugin.Instance?.Note(Companion.NameOf(me) + "'s catch-up left to the game that runs it now");
                yield break;
            }
            Talk.Hush = true;
            try { Day(st, seconds, away); }
            catch (Exception e) { VillagePlebsPlugin.Instance?.Warn($"Companion catch-up (at {Stage}): " + e); }
            finally { Talk.Hush = false; }
        }

        private static void Day(BrainState st, float seconds, float away)
        {
            Humanoid me = st.Body;
            var got = new Dictionary<string, int>();
            var notes = new List<string>();
            bool full = false;
            var also = new List<string>();
            var did0 = new List<string>();
            float budget = seconds;
            int felled = 0, mined = 0, picked = 0, fights = 0, harvested = 0, sown = 0;
            string fellTo = null;
            Vector3 center = Work.Center(me);
            float radius = Work.RadiusOf(me);

            Stage = "food";
            int cooked = Kitchen.CookAll(me, center, radius);
            string fromYou = Provision(me, center, radius);

            Stage = "creatures";
            if (MainConfig.whileAway.Value != AwayMode.Off)
            {
                Heightmap.Biome biome = WorldGenerator.instance != null ? WorldGenerator.instance.GetBiome(me.transform.position) : Heightmap.Biome.Meadows;
                float rate = FightsPerHour.TryGetValue(biome, out float r) ? r : 1f;
                int expected = Mathf.Min(8, Mathf.FloorToInt(Hours(seconds) / 24f * rate * 3f + UnityEngine.Random.value));
                for (int i = 0; i < expected; i++)
                {
                    string prefab = Creatures.TryGetValue(biome, out string[] list) ? list[UnityEngine.Random.Range(0, list.Length)] : null;
                    GameObject go = prefab != null ? ZNetScene.instance.GetPrefab(prefab) : null;
                    if (go == null) continue;
                    fights++;
                    budget -= 60f;
                    if (MainConfig.whileAway.Value == AwayMode.Real)
                    {
                        float threat = Threat.TryGetValue(biome, out float th) ? th : 100f;
                        float power = Power(me);
                        if (UnityEngine.Random.value > power / (power + threat * 0.4f))
                        {
                            fellTo = go.GetComponent<Character>()?.m_name ?? prefab;
                            Fall(me, center, radius);
                            budget = 0f;
                            break;
                        }
                    }
                    Drops(got, go);
                    EpicMmo.KilledAway(me, go);
                    Wear(me, 0.03f);
                    ItemDrop.ItemData weapon = me.GetCurrentWeapon();
                    if (weapon != null) Skill.Raise(me, weapon.m_shared.m_skillType, 3f);
                    string name = go.GetComponent<Character>()?.m_name ?? prefab;
                    if (!notes.Contains(name)) notes.Add(name);
                }
            }

            Stage = "hunting";
            bool hungry = !me.GetInventory().GetAllItems().Any(Food.IsFood);
            st.Goal = fellTo == null ? Goals.Pick(me, center, radius, st) : null;
            Job goalJobs = Goals.JobsFor(st.Goal, out HashSet<string> goalPrey);
            var hunted = new Dictionary<string, int>();
            if (fellTo == null && (Companion.BestRanged(me) != null || Companion.BestMelee(me) != null) && (hungry || goalPrey.Count > 0 || Duties.Open(me, Duty.Food)))
                Hunt(me, got, hunted, goalPrey, ref budget, seconds);

            Stage = "gathering";
            Job jobs = Work.JobsOf(me);
            if (Duties.Configured(me)) jobs = Duties.OpenJobs(me) | (goalJobs & ~(Job.Loot | Job.Hunt));
            else if (jobs == Job.None) jobs = Work.AutoJobs(me) | (goalJobs & ~(Job.Loot | Job.Hunt));
            ItemDrop.ItemData axe = Work.Axe(me), pick = Work.Pickaxe(me);
            var seen = new HashSet<GameObject>();
            var targets = new List<Component>();
            foreach (Collider col in Physics.OverlapSphere(center, radius + Work.OutsideReach, ~0, QueryTriggerInteraction.Collide))
            {
                GameObject go = col.attachedRigidbody != null ? col.attachedRigidbody.gameObject : col.transform.root.gameObject;
                if (!seen.Add(go) || go.GetComponent<Piece>() != null || go.GetComponent<Character>() != null) continue;
                Component t = (Component)go.GetComponent<TreeLog>() ?? (Component)go.GetComponent<TreeBase>() ?? (Component)go.GetComponent<MineRock5>() ?? (Component)go.GetComponent<MineRock>()
                              ?? (Component)go.GetComponent<Destructible>() ?? go.GetComponent<Pickable>();
                if (t == null) continue;
                if (t is Pickable ? Vector3.Distance(t.transform.position, center) > radius : Work.InBase(t.transform.position) || Groundwork.OnWorkedGround(go)) continue;
                targets.Add(t);
            }
            targets = targets.OrderBy(t => hungry && Work.Edible(t) ? 0 : st.Goal != null && Work.Drops(t).Any(st.Goal.Wants) ? 1 : 2)
                             .ThenBy(t => Vector3.Distance(t.transform.position, center)).ToList();

            void Gather(Func<bool> done)
            {
            foreach (Component t in targets)
            {
                if (budget <= 0f || done != null && done()) break;
                if (Free(me) <= 1) { full = true; break; }
                switch (t)
                {
                    case Pickable p when (jobs & Job.Forage) != 0 && p.m_respawnTimeMinutes > 0f && p.m_itemPrefab != null && !(Companion.Zdo(p)?.GetBool(ZDOVars.s_picked, false) ?? true)
                                         && (!hungry || Work.Edible(p) || st.Goal != null && st.Goal.Wants(p.m_itemPrefab.name)):
                        Add(got, p.m_itemPrefab, p.m_amount);
                        foreach (GameObject extra in p.m_extraDrops.GetDropList()) Add(got, extra, 1);
                        p.GetComponent<ZNetView>()?.InvokeRPC(ZNetView.Everybody, "RPC_SetPicked", true);
                        EpicMmo.Worked(me, p, EpicMmo.Work.Pick, away: true);
                        budget -= 15f; picked++;
                        break;
                    case TreeLog log when (jobs & Job.Wood) != 0 && axe != null && axe.m_shared.m_toolTier >= log.m_minToolTier:
                        if (!Chop(me, axe, log.m_health, ref budget)) break;
                        EpicMmo.Worked(me, log, EpicMmo.Work.Tree, _hits);
                        AddTable(got, log.m_dropWhenDestroyed); AddSubLogs(got, log.m_subLogPrefab, 3);
                        Remove(log.gameObject); felled++;
                        break;
                    case TreeBase tree when (jobs & Job.Wood) != 0 && axe != null && axe.m_shared.m_toolTier >= tree.m_minToolTier:
                        if (!Chop(me, axe, tree.m_health, ref budget)) break;
                        EpicMmo.Worked(me, tree, EpicMmo.Work.Tree);
                        AddTable(got, tree.m_dropWhenDestroyed);
                        TreeLog fallen = tree.m_logPrefab != null ? tree.m_logPrefab.GetComponent<TreeLog>() : null;
                        if (fallen != null) { AddTable(got, fallen.m_dropWhenDestroyed); AddSubLogs(got, fallen.m_subLogPrefab, 3); budget -= 40f; }
                        if (fallen != null) EpicMmo.WorkedAway(me, fallen.gameObject, EpicMmo.Work.Tree, Mathf.CeilToInt(fallen.m_health / _perHit));
                        if (tree.m_stubPrefab != null) UnityEngine.Object.Instantiate(tree.m_stubPrefab, tree.transform.position, tree.transform.rotation);
                        Remove(tree.gameObject); felled++;
                        break;
                    case MineRock5 rock5 when pick != null && pick.m_shared.m_toolTier >= rock5.m_minToolTier && (jobs & (Work.IsOre(rock5.m_dropItems) ? Job.Ore : Job.Stone)) != 0:
                        int areas = (AccessTools.Field(typeof(MineRock5), "m_hitAreas").GetValue(rock5) as IList)?.Count ?? 4;
                        if (!Chop(me, pick, rock5.m_health * areas, ref budget)) break;
                        EpicMmo.Worked(me, rock5, EpicMmo.Work.Mining, _hits);
                        for (int i = 0; i < areas; i++) AddTable(got, rock5.m_dropItems);
                        Remove(rock5.gameObject); mined++;
                        break;
                    case MineRock rock when pick != null && pick.m_shared.m_toolTier >= rock.m_minToolTier && (jobs & (Work.IsOre(rock.m_dropItems) ? Job.Ore : Job.Stone)) != 0:
                        if (!Chop(me, pick, rock.m_health, ref budget)) break;
                        EpicMmo.Worked(me, rock, EpicMmo.Work.Mining, _hits);
                        AddTable(got, rock.m_dropItems);
                        Remove(rock.gameObject); mined++;
                        break;
                    case Destructible des:
                        DropTable table = des.GetComponent<DropOnDestroyed>()?.m_dropWhenDestroyed;
                        if (table == null || table.m_drops.Count == 0) break;
                        bool wood = des.m_destructibleType == DestructibleType.Tree;
                        ItemDrop.ItemData tool = wood ? axe : pick;
                        if (tool == null || tool.m_shared.m_toolTier < des.m_minToolTier) break;
                        if ((jobs & (wood ? Job.Wood : Work.IsOre(table) ? Job.Ore : Job.Stone)) == 0) break;
                        if (!Chop(me, tool, des.m_health, ref budget)) break;
                        EpicMmo.Worked(me, des, EpicMmo.Work.Destructible);
                        AddTable(got, table);
                        Remove(des.gameObject);
                        if (wood) felled++; else mined++;
                        break;
                }
            }
            }

            if (Duties.Configured(me))
            {
                var open = Duties.OnEntries(me).Where(e => Duties.Have(me, e.Duty) < e.Target).ToList();
                float slice = open.Count > 0 ? budget / open.Count : 0f;
                foreach (Duties.Entry e in open)
                {
                    float floor = Mathf.Max(0f, budget - slice);
                    int baseHave = Duties.Have(me, e.Duty);
                    jobs = e.Jobs;
                    Gather(() => baseHave + Duties.Accrued(e.Duty, got) >= e.Target || budget <= floor);
                }
                jobs = goalJobs & ~(Job.Loot | Job.Hunt);
                Gather(null);
            }
            else Gather(null);

            Stage = "harvest";
            if (fellTo == null) harvested = Harvest(me, center, radius, got, ref budget, ref full);

            Stage = "trip";
            string trip = fellTo == null ? AwayTrip(me, center, radius, got, ref budget) : null;
            if (trip != null) Journal.Trip(me, P("villagepleb_catchup_trip_journal", trip));

            Stage = "storing";
            int total = got.Values.Sum();
            int stored = Store(me, got);
            Armory.CatchUp(st, center, radius);
            cooked += Kitchen.CookAll(me, center, radius);

            Stage = "planting";
            if (fellTo == null && Farming.HasField(st, center, radius))
            {
                Farming.TakeFromChests(me, true);
                for (int i = 0; i < 150 && budget > 0f && Farming.FindSpot(st, center, radius, out Farming.Crop crop, out Vector3 spot); i++)
                {
                    budget -= 5f;
                    if (Farming.Place(st, crop, spot, Mathf.Max(0f, budget))) sown++;
                    else Farming.Bad(st, spot);
                }
            }

            Stage = "crafting";
            var made = new List<string>();
            for (int round = 0; round < 12 && fellTo == null; round++)
            {
                string done = null;
                var up = Upgrades.Find(me, center, radius);
                if (up != null) done = Upgrades.Do(st, up.Value.Key, up.Value.Value);
                if (done == null) { var craft = Upgrades.FindCraft(me, center, radius); if (craft != null) done = Upgrades.Craft(st, craft.Value.Key, craft.Value.Value); }
                if (done == null)
                {
                    st.Goal = Goals.Pick(me, center, radius, st);
                    var step = Goals.StepReady(me, st.Goal);
                    if (step != null) done = Upgrades.Craft(st, step.Value.Key, step.Value.Value, true);
                }
                if (done == null) break;
                made.Add(done);
            }

            Stage = "repairs";
            int repaired = Repair.All(me, center, radius);
            int mended = Mending.Hammer(me) != null ? Mending.FixAll(st, center, radius) : 0;

            Stage = "time";
            Food.PassTime(me, st, seconds);
            if (Food.Meals(me).Count > 0 && fellTo == null) me.Heal(me.GetMaxHealth(), false);
            string later = Provision(me, center, radius);
            if (fromYou == null) fromYou = later;
            Companion.SaveBag(me);
            Portraits.Dirty(me);

            Stage = "report";
            var did = new List<string>(did0);
            if (felled > 0) did.Add(P(felled == 1 ? "villagepleb_catchup_felled_one" : "villagepleb_catchup_felled_many", felled));
            if (mined > 0) did.Add(P(mined == 1 ? "villagepleb_catchup_mined_one" : "villagepleb_catchup_mined_many", mined));
            if (picked > 0) did.Add(P(picked == 1 ? "villagepleb_catchup_picked_one" : "villagepleb_catchup_picked_many", picked));
            if (harvested > 0) did.Add(P(harvested == 1 ? "villagepleb_catchup_harvested_one" : "villagepleb_catchup_harvested_many", harvested));
            if (sown > 0) did.Add(P(sown == 1 ? "villagepleb_catchup_sown_one" : "villagepleb_catchup_sown_many", sown));
            if (hunted.Count > 0) did.Add(P("villagepleb_catchup_hunted", string.Join(", ", hunted.Select(kv => Amount(kv, true)))));
            if (fights > 0) did.Add(P(fights == 1 ? "villagepleb_catchup_fought_one" : "villagepleb_catchup_fought_many", fights, string.Join(", ", notes.Take(3))));
            if (cooked > 0) did.Add(P(cooked == 1 ? "villagepleb_catchup_cooked_one" : "villagepleb_catchup_cooked_many", cooked));
            if (made.Count > 0) did.Add(string.Join(", ", made));
            if (repaired > 0) did.Add(P(repaired == 1 ? "villagepleb_catchup_repaired_one" : "villagepleb_catchup_repaired_many", repaired));
            if (mended > 0) did.Add(P(mended == 1 ? "villagepleb_catchup_mended_one" : "villagepleb_catchup_mended_many", mended));
            if (trip != null) did.Add(trip);
            if (did.Count == 0 && fellTo == null && fromYou == null) return;
            string items = string.Join(", ", got.OrderByDescending(kv => kv.Value).Take(8).Select(kv => Amount(kv, false)));
            var parts = new List<string> { P("villagepleb_catchup_report", Mathf.RoundToInt(away / 60f), Companion.NameOf(me), did.Count > 0 ? string.Join(", ", did) : P("villagepleb_catchup_rested")) };
            if (items.Length > 0) parts.Add(P(stored < total ? "villagepleb_catchup_brought_kept" : "villagepleb_catchup_brought", items));
            if (full) parts.Add(P("villagepleb_catchup_chests_full"));
            if (fromYou != null) parts.Add(P("villagepleb_catchup_took_food", fromYou));
            if (Food.Meals(me).Count == 0 && fellTo == null) parts.Add(P("villagepleb_catchup_no_food"));
            if (fellTo != null) parts.Add(P("villagepleb_catchup_fell", fellTo));
            string line = string.Join(" ", parts);
            Talk.Hush = false;
            st.Remember(line);
            VillagePlebsPlugin.Instance?.Note(Words.English(line));
            Player master = Companion.Master(me);
            if (master != null && master == Player.m_localPlayer) master.Message(MessageHud.MessageType.Center, Words.Show(line));
            Talk.Tell(me, line);
        }

        private static int Harvest(Humanoid me, Vector3 center, float radius, Dictionary<string, int> got, ref float budget, ref bool full)
        {
            int n = 0;
            var seen = new HashSet<GameObject>();
            foreach (Collider col in Physics.OverlapSphere(center, radius, ~0, QueryTriggerInteraction.Collide))
            {
                if (budget <= 0f) break;
                GameObject go = col.attachedRigidbody != null ? col.attachedRigidbody.gameObject : col.transform.root.gameObject;
                if (!seen.Add(go) || !(go.GetComponent<Pickable>() is Pickable p) || !Farming.Ripe(p)) continue;
                if (Free(me) <= 1) { full = true; break; }
                Add(got, p.m_itemPrefab, p.m_amount);
                foreach (GameObject extra in p.m_extraDrops.GetDropList()) Add(got, extra, 1);
                p.GetComponent<ZNetView>()?.InvokeRPC(ZNetView.Everybody, "RPC_SetPicked", true);
                EpicMmo.Worked(me, p, EpicMmo.Work.Pick, away: true);
                budget -= 10f; n++;
            }
            return n;
        }

        private static string AwayTrip(Humanoid me, Vector3 center, float radius, Dictionary<string, int> got, ref float budget)
        {
            if (budget < 300f || ZDOMan.instance == null) return null;
            Goal g = Goals.Pick(me, center, radius);
            if (g == null || g.Raw.Count == 0) return null;
            var brought = new Dictionary<string, int>();
            float farthest = 0f;
            Vector3 farWay = Vector3.zero;
            var list = new List<ZDO>();
            foreach (var need in g.Raw)
            {
                int left = need.Value;
                GameObject item = ObjectDB.instance.GetItemPrefab(need.Key);
                if (item == null) continue;
                foreach (string source in Goals.PickablesFor(need.Key))
                {
                    if (left <= 0) break;
                    GameObject sourcePrefab = ZNetScene.instance.GetPrefab(source);
                    Pickable kind = sourcePrefab != null ? sourcePrefab.GetComponent<Pickable>() : null;
                    if (kind == null) continue;
                    list.Clear();
                    int index = 0;
                    for (int guard = 0; guard < 1000 && !ZDOMan.instance.GetAllZDOsWithPrefabIterative(source, list, ref index); guard++) { }
                    foreach (ZDO z in list.Where(z => z != null && !z.GetBool(ZDOVars.s_picked, false) && Vector3.Distance(z.GetPosition(), center) < 400f)
                                          .OrderBy(z => Vector3.Distance(z.GetPosition(), center)))
                    {
                        if (left <= 0 || budget <= 0f) break;
                        Vector3 at = z.GetPosition();
                        ZNetView loaded = ZNetScene.instance.FindInstance(z);
                        if (loaded != null) { if (loaded.GetComponent<Pickable>() == null) continue; loaded.InvokeRPC(ZNetView.Everybody, "RPC_SetPicked", true); }
                        else
                        {
                            z.SetOwner(ZDOMan.GetSessionID());
                            z.Set(ZDOVars.s_picked, true);
                            z.Set(ZDOVars.s_pickedTime, ZNet.instance.GetTime().Ticks);
                        }
                        int n = Mathf.Max(1, kind.m_amount);
                        Add(got, item, n);
                        string name = item.GetComponent<ItemDrop>().m_itemData.m_shared.m_name;
                        brought[name] = (brought.TryGetValue(name, out int had) ? had : 0) + n;
                        left -= n;
                        budget -= 20f;
                        float d = Vector3.Distance(at, center);
                        if (d > farthest) { farthest = d; farWay = at - center; }
                    }
                }
            }
            if (brought.Count == 0) return null;
            budget -= farthest * 2f / 3f;
            return P(TripKeys[Remote.Dir(farWay)], farthest.ToString("0"), string.Join(", ", brought.Select(kv => Amount(kv, true))));
        }

        private static string Amount(KeyValuePair<string, int> kv, bool low) => P("villagepleb_catchup_amount", kv.Value, low ? Words.Low(kv.Key) : kv.Key);

        private static string And(IEnumerable<string> parts) => parts.Aggregate((a, b) => P("villagepleb_home_and", a, b));

        private static readonly string[] TripKeys =
        {
            "villagepleb_catchup_trip_n", "villagepleb_catchup_trip_ne", "villagepleb_catchup_trip_e", "villagepleb_catchup_trip_se",
            "villagepleb_catchup_trip_s", "villagepleb_catchup_trip_sw", "villagepleb_catchup_trip_w", "villagepleb_catchup_trip_nw",
        };

        private static float Hours(float seconds) => seconds / (EnvMan.instance != null ? EnvMan.instance.m_dayLengthSec / 24f : 75f);

        private static void Drops(Dictionary<string, int> got, GameObject creature)
        {
            CharacterDrop drops = creature.GetComponent<CharacterDrop>();
            if (drops == null) return;
            foreach (CharacterDrop.Drop d in drops.m_drops)
                if (d.m_prefab != null && UnityEngine.Random.value <= d.m_chance) Add(got, d.m_prefab, UnityEngine.Random.Range(d.m_amountMin, d.m_amountMax + 1));
        }

        private static void Wear(Humanoid me, float share)
        {
            foreach (ItemDrop.ItemData worn in Companion.Worn(me).Where(w => w.m_shared.m_useDurability))
                worn.m_durability = Mathf.Max(worn.GetMaxDurability() * 0.1f, worn.m_durability - worn.GetMaxDurability() * share);
        }

        private static readonly Dictionary<Heightmap.Biome, string[]> Quarry = new Dictionary<Heightmap.Biome, string[]>
        {
            [Heightmap.Biome.Meadows] = new[] { "Deer", "Deer", "Boar", "Neck" },
            [Heightmap.Biome.BlackForest] = new[] { "Deer", "Deer", "Boar" },
            [Heightmap.Biome.Mountain] = new[] { "Wolf" },
            [Heightmap.Biome.Plains] = new[] { "Lox", "Deer" },
        };

        private static void Hunt(Humanoid me, Dictionary<string, int> got, Dictionary<string, int> hunted, HashSet<string> goalPrey, ref float budget, float seconds)
        {
            Heightmap.Biome biome = WorldGenerator.instance != null ? WorldGenerator.instance.GetBiome(me.transform.position) : Heightmap.Biome.Meadows;
            string[] kinds = goalPrey.Count > 0 ? goalPrey.ToArray() : Quarry.TryGetValue(biome, out string[] k) ? k : null;
            if (kinds == null || kinds.Length == 0) return;
            bool bow = Companion.BestRanged(me) != null;
            int kills = Mathf.Min(6, Mathf.FloorToInt(Hours(seconds) / 24f * (bow ? 3f : 2f) + UnityEngine.Random.value));
            ItemDrop.ItemData weapon = Companion.BestRanged(me) ?? Companion.BestMelee(me);
            for (int i = 0; i < kills && budget > 0f; i++)
            {
                GameObject go = ZNetScene.instance.GetPrefab(kinds[UnityEngine.Random.Range(0, kinds.Length)]);
                if (go == null) continue;
                Drops(got, go);
                EpicMmo.KilledAway(me, go);
                Wear(me, 0.01f);
                if (weapon != null) Skill.Raise(me, weapon.m_shared.m_skillType, 2f);
                budget -= 180f;
                string name = go.GetComponent<Character>()?.m_name ?? go.name;
                hunted[name] = (hunted.TryGetValue(name, out int had) ? had : 0) + 1;
            }
        }

        private static string Provision(Humanoid me, Vector3 center, float radius)
        {
            Inventory mine = me.GetInventory();
            int have = mine.GetAllItems().Where(Food.IsFood).Sum(i => i.m_stack);
            foreach (Container chest in Home.Chests(me).Where(c => !Containers.InUse(c)))
            {
                if (have >= 10) break;
                if (!Containers.Take(chest)) continue;
                foreach (ItemDrop.ItemData food in chest.GetInventory().GetAllItems().Where(Food.IsFood).OrderByDescending(i => i.m_shared.m_food + i.m_shared.m_foodStamina).ToList())
                {
                    if (have >= 10) break;
                    int n = Mathf.Min(10 - have, food.m_stack);
                    if (Work.Move(chest, me, food, n)) have += n;
                }
            }
            if (have > 0 || Food.Meals(me).Count > 0 || !Work.UsesPantry(me)) return null;
            Container yours = Work.YourFood(me, center, radius);
            return yours != null ? Work.TakeFoodFrom(me, yours, 5) : null;
        }

        private static int _hits;
        private static float _perHit = 1f;

        private static bool Chop(Humanoid me, ItemDrop.ItemData tool, float health, ref float budget)
        {
            float perHit = Mathf.Max(1f, (tool.m_shared.m_damages.m_chop + tool.m_shared.m_damages.m_pickaxe) * Mathf.Lerp(0.4f, 1f, Skill.Get(me, tool.m_shared.m_skillType) / 100f));
            int hits = Mathf.CeilToInt(health / perHit);
            _hits = hits; _perHit = perHit;
            float wear = hits * tool.m_shared.m_useDurabilityDrain;
            if (tool.m_shared.m_useDurability && tool.m_durability - wear <= 0f) return false;
            if (tool.m_shared.m_useDurability) tool.m_durability -= wear;
            budget -= hits * 1.3f + 20f;
            for (int i = 0; i < hits; i++) Skill.Raise(me, tool.m_shared.m_skillType, 1f);
            return true;
        }

        private static void Add(Dictionary<string, int> got, GameObject prefab, int amount)
        {
            if (prefab == null || amount <= 0 || prefab.GetComponent<ItemDrop>() == null) return;
            string key = prefab.name;
            got[key] = (got.TryGetValue(key, out int had) ? had : 0) + amount;
        }

        private static void AddTable(Dictionary<string, int> got, DropTable table)
        {
            if (table == null) return;
            foreach (GameObject go in table.GetDropList()) Add(got, go, 1);
        }

        private static void AddSubLogs(Dictionary<string, int> got, GameObject sub, int depth)
        {
            if (sub == null || depth <= 0) return;
            TreeLog log = sub.GetComponent<TreeLog>();
            if (log == null) return;
            AddTable(got, log.m_dropWhenDestroyed);
            AddSubLogs(got, log.m_subLogPrefab, depth - 1);
        }

        private static void Remove(GameObject go)
        {
            ZNetView v = go.GetComponent<ZNetView>();
            if (v == null || !v.IsValid()) return;
            if (!v.IsOwner()) v.ClaimOwnership();
            ZNetScene.instance.Destroy(go);
        }

        private static int Free(Humanoid me) => me.GetInventory().GetEmptySlots() + Home.Chests(me).Sum(c => c.GetInventory().GetEmptySlots())
                                                + (Work.Stows(me) ? Work.YourChests(me, Work.Center(me), Work.RadiusOf(me) + 20f).Sum(c => c.GetInventory().GetEmptySlots()) : 0);

        private static int AddUpTo(Inventory inv, GameObject prefab, int amount)
        {
            ItemDrop drop = prefab.GetComponent<ItemDrop>();
            string name = drop.m_itemData.m_shared.m_name;
            int max = Mathf.Max(1, drop.m_itemData.m_shared.m_maxStackSize), total = 0;
            while (amount > 0)
            {
                int before = inv.CountItems(name);
                inv.AddItem(prefab, Mathf.Min(amount, max));
                int added = inv.CountItems(name) - before;
                if (added <= 0) break;
                amount -= added; total += added;
            }
            return total;
        }

        private static void DropAt(Humanoid me, GameObject prefab, int amount)
        {
            int max = Mathf.Max(1, prefab.GetComponent<ItemDrop>().m_itemData.m_shared.m_maxStackSize);
            for (; amount > 0; amount -= max)
            {
                GameObject go = UnityEngine.Object.Instantiate(prefab, me.transform.position + Vector3.up * 0.5f + UnityEngine.Random.insideUnitSphere * 0.3f, Quaternion.identity);
                go.GetComponent<ItemDrop>().SetStack(Mathf.Min(amount, max));
            }
            VillagePlebsPlugin.Instance?.Note($"{Companion.NameOf(me)} had no room for all it brought back: the rest is on the ground beside it");
        }

        private static int Store(Humanoid me, Dictionary<string, int> got)
        {
            int stored = 0;
            var named = new Dictionary<string, int>();
            foreach (var kv in got)
            {
                GameObject prefab = ObjectDB.instance.GetItemPrefab(kv.Key);
                ItemDrop drop = prefab != null ? prefab.GetComponent<ItemDrop>() : null;
                if (drop == null) continue;
                int left = kv.Value;
                List<Container> own = Home.Chests(me).Where(c => !Containers.InUse(c)).ToList();
                List<Container> yours = Work.Stows(me) ? Work.YourChests(me, Work.Center(me), Work.RadiusOf(me) + 20f).ToList() : new List<Container>();
                bool forYou = Duties.Configured(me) && Duties.ToYours(me) && yours.Count > 0;
                IEnumerable<Container> chests = forYou ? yours.Concat(own) : own.Concat(yours);
                if (forYou && Food.IsFood(drop.m_itemData))
                {
                    int larder = own.Sum(c => c.GetInventory().GetAllItems().Where(Food.IsFood).Sum(i => i.m_stack));
                    int toLarder = Mathf.Clamp(12 - larder, 0, left);
                    foreach (Container chest in own)
                    {
                        if (toLarder <= 0) break;
                        if (!Containers.Take(chest)) continue;
                        int put = AddUpTo(chest.GetInventory(), prefab, toLarder);
                        toLarder -= put; left -= put; stored += put;
                    }
                }
                foreach (Container chest in chests)
                {
                    if (left <= 0) break;
                    if (!Containers.Take(chest)) continue;
                    int added = AddUpTo(chest.GetInventory(), prefab, left);
                    left -= added; stored += added;
                }
                if (left > 0) left -= AddUpTo(me.GetInventory(), prefab, left);
                if (left > 0) DropAt(me, prefab, left);
                named[drop.m_itemData.m_shared.m_name] = kv.Value;
            }
            got.Clear();
            foreach (var kv in named) got[kv.Key] = kv.Value;
            return stored;
        }
    }

    public enum AwayMode { Off, Mild, Real }
}
