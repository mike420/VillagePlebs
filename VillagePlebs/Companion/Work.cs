using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using HarmonyLib;
using UnityEngine;
using static VillagePlebs.Words;

namespace VillagePlebs
{
    [Flags]
    public enum Job { None = 0, Wood = 1, Stone = 2, Ore = 4, Forage = 8, Loot = 16, Hunt = 32, Cook = 64 }

    internal static class Work
    {
        internal enum Kind { None, Hit, Pick, PickUp, Store, Upgrade, Craft, Hunt, Cook, Fetch, Fuel, Mend, Armory, Plant }

        internal class Task
        {
            public Kind Kind;
            public Component Target;
            public Job Job;
            public ItemDrop.ItemData Item;
            public Recipe Recipe;
            public Farming.Crop Crop;
            public Container FetchFrom;
            public Func<ItemDrop.ItemData, bool> Take;
            public bool ForGoal;
            public bool Edible;
            public bool Trip;
            public bool WasTree;
            public bool Ordered;
            public Vector3 Pos;
            public float Closer = 1f;
            public int Swings, MissedRun;
            public bool UsedChop;
            public float HealthAt = -1f;
            public float Started, LastClose;
        }

        private static readonly string[] OreWords = { "Ore", "Scrap", "Flametal" };
        private static readonly HashSet<string> Prey = new HashSet<string> { "Deer", "Boar", "Neck", "Hare" };
        private static HashSet<string> GameFor(Humanoid me)
        {
            var game = new HashSet<string>(Prey);
            if (me.GetHealthPercentage() < 0.8f) return game;
            if (me.GetMaxHealth() >= 90f) game.Add("Wolf");
            if (me.GetMaxHealth() >= 130f && Companion.BestRanged(me) != null) game.Add("Lox");
            return game;
        }

        private static readonly HashSet<string> Harmless = new HashSet<string> { "Deer", "Hare" };
        private static HashSet<string> _cookable;

        public static bool IsCookable(ItemDrop.ItemData i)
        {
            if (_cookable == null && ZNetScene.instance != null)
            {
                _cookable = new HashSet<string>();
                foreach (GameObject prefab in ZNetScene.instance.m_prefabs)
                {
                    CookingStation s = prefab != null ? prefab.GetComponent<CookingStation>() : null;
                    if (s != null) foreach (CookingStation.ItemConversion c in s.m_conversion) if (c.m_from != null) _cookable.Add(c.m_from.m_itemData.m_shared.m_name);
                }
            }
            return _cookable != null && _cookable.Contains(i.m_shared.m_name);
        }

        internal static Goal Aim(BrainState st) => st.Goal;

        private static bool Directed(BrainState st) => st.Ticked != Job.None || st.Duty != null;

        // The ticked jobs it has no tool for: those wait, and it works on its mission instead.
        internal static Job Unequipped(Humanoid h, Job jobs)
        {
            Job missing = Job.None;
            if ((jobs & Job.Wood) != 0 && Axe(h) == null) missing |= Job.Wood;
            if ((jobs & (Job.Stone | Job.Ore)) != 0 && Pickaxe(h) == null) missing |= jobs & (Job.Stone | Job.Ore);
            if ((jobs & Job.Hunt) != 0 && Companion.BestMelee(h) == null && Companion.BestRanged(h) == null) missing |= Job.Hunt;
            return missing;
        }

        private static Func<ItemDrop.ItemData, bool> ToolPicker(Humanoid me, Job lacking) => i =>
            (lacking & Job.Wood) != 0 && i.m_shared.m_damages.m_chop > 0f && i.IsWeapon() && Usable(i)
            || (lacking & (Job.Stone | Job.Ore)) != 0 && i.m_shared.m_damages.m_pickaxe > 0f && Usable(i)
            || (lacking & Job.Hunt) != 0 && i.IsWeapon() && !IsTool(i) && Companion.Useful(me, i);

        public static Container OwnChestWith(Humanoid me, Func<ItemDrop.ItemData, bool> has) =>
            Home.Chests(me).Where(c => !Containers.InUse(c) && c.GetInventory().GetAllItems().Any(has))
                .OrderBy(c => Vector3.Distance(c.transform.position, me.transform.position)).FirstOrDefault(c => Brain.CanReach(me, c.transform.position));

        // Do t once it has fetched what take picks out of its own chest.
        public static Task Fetching(Task t, Container from, Func<ItemDrop.ItemData, bool> take) { t.FetchFrom = from; t.Take = take; return t; }

        public static Job JobsOf(Component c) => (Job)(Companion.Zdo(c)?.GetInt(Keys.Jobs, 0) ?? 0);

        public static Job AutoJobs(Humanoid h)
        {
            Job j = Job.None;
            if (Axe(h) != null) j |= Job.Wood;
            if (Pickaxe(h) != null) j |= Job.Stone | Job.Ore;
            bool hungry = h.GetInventory().GetAllItems().Where(Food.IsFood).Sum(f => f.m_stack) < 10;
            if (hungry) j |= Job.Forage | Job.Cook;
            if (hungry && (Companion.BestRanged(h) != null || Companion.BestMelee(h) != null)) j |= Job.Hunt;
            return j;
        }
        public const string PantryKey = "vp_pantry", StowKey = "vp_stow";
        public const string RationKey = "vp_ration", RationDayKey = "vp_rationday", RationNKey = "vp_rationn";
        public static int RationOf(Component c) => Companion.Zdo(c)?.GetInt(RationKey, 12) ?? 12;
        public static int TakenToday(Component c) { ZDO z = Companion.Zdo(c); return z != null && z.GetInt(RationDayKey, -1) == Journal.Day ? z.GetInt(RationNKey, 0) : 0; }
        public static int RationLeft(Component c) => Mathf.Max(0, RationOf(c) - TakenToday(c));
        public static bool UsesPantry(Component c) => Companion.Zdo(c)?.GetBool(PantryKey, false) ?? false;
        public static bool Stows(Component c) => Companion.Zdo(c)?.GetBool(StowKey, true) ?? true;

        internal static IEnumerable<Container> YourChests(Humanoid me, Vector3 center, float radius) =>
            UnityEngine.Object.FindObjectsByType<Container>(FindObjectsSortMode.None)
                .Where(c => c != null && c.GetInventory() != null && Home.IsChest(c) && Home.IdOn(c) == 0L && !Containers.InUse(c) && Vector3.Distance(c.transform.position, center) < radius
                            && (!c.m_checkGuardStone || PrivateArea.CheckAccess(c.transform.position, 0f, false)))
                .OrderBy(c => Vector3.Distance(c.transform.position, me.transform.position));

        public static int RadiusOf(Component c) => Companion.Zdo(c)?.GetInt(Keys.Radius, 30) ?? 30;

        public const string HomeSetKey = "vp_homeset", HomeSpotKey = "vp_homespot";

        public static Vector3 Center(Humanoid c)
        {
            ZDO z = Companion.Zdo(c);
            if (z.GetBool(HomeSetKey, false)) return z.GetVec3(HomeSpotKey, c.transform.position);
            if (z.GetBool(Keys.HasBed, false)) return z.GetVec3(Keys.BedPos, c.transform.position);
            return z.GetVec3(Keys.Post, c.transform.position);
        }

        public static bool HasHome(Component c) { ZDO z = Companion.Zdo(c); return z != null && (z.GetBool(Keys.HasBed, false) || z.GetBool(HomeSetKey, false)); }

        public static ItemDrop.ItemData Axe(Humanoid h) => h.GetInventory().GetAllItems().Where(i => i.m_shared.m_damages.m_chop > 0f && i.IsWeapon() && Usable(i))
            .OrderByDescending(i => i.m_shared.m_toolTier).ThenByDescending(i => i.m_shared.m_damages.m_chop).FirstOrDefault();

        public static ItemDrop.ItemData Pickaxe(Humanoid h) => h.GetInventory().GetAllItems().Where(i => i.m_shared.m_damages.m_pickaxe > 0f && Usable(i))
            .OrderByDescending(i => i.m_shared.m_toolTier).ThenByDescending(i => i.m_shared.m_damages.m_pickaxe).FirstOrDefault();

        private static bool Usable(ItemDrop.ItemData i) => !i.m_shared.m_useDurability || i.m_durability > 0f;

        public static bool IsTool(ItemDrop.ItemData i) => i.m_shared.m_damages.m_chop > 0f || i.m_shared.m_damages.m_pickaxe > 0f;

        public static string ToolFor(Humanoid h, Job job)
        {
            ItemDrop.ItemData tool = job == Job.Wood ? Axe(h) : job == Job.Stone || job == Job.Ore ? Pickaxe(h) : null;
            if (job == Job.Forage || job == Job.Loot) return P("villagepleb_work_tool_hands");
            return tool != null ? P("villagepleb_work_tool_tier", tool.m_shared.m_name, tool.m_shared.m_toolTier) : P(job == Job.Wood ? "villagepleb_work_tool_no_axe" : "villagepleb_work_tool_no_pickaxe");
        }

        internal static bool ToolMissing(Humanoid h, Job job) =>
            job != Job.Forage && job != Job.Loot && (job == Job.Wood ? Axe(h) : job == Job.Stone || job == Job.Ore ? Pickaxe(h) : null) == null;

        public static void Tick(BrainState st, Player master, float dt, Action<Vector3, float, bool> moveTo, Action stop, Action<Vector3> lookAt)
        {
            Humanoid me = st.Body;
            if (Companion.Zdo(me) == null) return;
            Job jobs = JobsOf(me);
            st.OnDuties = Duties.Configured(me);
            st.Duty = Duties.Active(me);
            if (st.OnDuties)
            {
                jobs = st.Duty != null ? st.Duty.Jobs : Job.None;
                if (st.Hungry) jobs |= Job.Forage | Job.Hunt | Job.Cook;
            }
            else
            {
                jobs &= ~Unequipped(me, jobs);
                if (jobs == Job.None) jobs = AutoJobs(me);
            }
            st.Ticked = st.OnDuties ? Job.None : JobsOf(me) & ~Unequipped(me, JobsOf(me));
            Vector3 center = Center(me);
            float radius = RadiusOf(me);
            if (TravelHome(st, center, radius, moveTo)) return;
            if (Clearing(st, moveTo)) return;
            if (Time.time >= st.NextLoanLook) { st.NextLoanLook = Time.time + 30f; Loans.Return(st); }

            if (st.Task != null && !Valid(st, st.Task)) Drop(st);
            if (st.Task == null && !NextQueued(st) && !NextInArea(st)) NextFarm(st);
            if (st.Task == null && Time.time >= st.NextWorkLook)
            {
                st.NextWorkLook = Time.time + 1.5f;
                st.Task = Choose(st, jobs, center, radius);
                if (st.Task == null) st.WorkTool = null;
                if (st.Task == null && st.Duty != null && st.WorkNote != null)
                {
                    if (Duties.Rest(me, st.Duty) == 1) st.Remember(P("villagepleb_work_duty_nothing", Low(Duties.Label(st.Duty.Duty))));
                    st.NextWorkLook = Time.time + 0.2f;
                }
            }
            else if (st.Task != null && st.Duty != null && st.Task.Job != Job.None) Duties.Worked(me, st.Duty);
            if (st.Task == null)
            {
                if (Idle.AtHome(st, master, center, radius, moveTo, stop, lookAt)) return;
                Go(center, moveTo, stop, me); Brain.Status(st, st.WorkNote ?? P("villagepleb_work_nothing_left")); return;
            }
            Execute(st, master, dt, moveTo, stop, lookAt);
        }

        public static bool Help(BrainState st, Player master, float dt, Action<Vector3, float, bool> moveTo, Action stop, Action<Vector3> lookAt, Job job)
        {
            Humanoid me = st.Body;
            st.Helping = false;
            if (Vector3.Distance(me.transform.position, master.transform.position) > 25f) { st.Task = null; return false; }
            if (Clearing(st, moveTo)) { st.Helping = true; return true; }
            if (st.Task != null && !Valid(st, st.Task)) Drop(st);
            if (st.Task != null && (st.Task.Kind != Kind.Hit || Flat(st.Task.Target.transform.position, master.transform.position) > 20f)) st.Task = null;
            if (st.Task == null && Time.time >= st.NextWorkLook)
            {
                st.NextWorkLook = Time.time + 1f;
                ItemDrop.ItemData axe = Axe(me), pick = Pickaxe(me);
                if ((job == Job.Wood && axe == null) || ((job & (Job.Stone | Job.Ore)) != 0 && pick == null)) return false;
                var seen = new HashSet<GameObject>();
                Task best = null;
                float bestD = float.MaxValue;
                foreach (Collider col in Physics.OverlapSphere(master.transform.position, 15f, ~0, QueryTriggerInteraction.Collide))
                {
                    GameObject go = col.attachedRigidbody != null ? col.attachedRigidbody.gameObject : col.transform.root.gameObject;
                    if (!seen.Add(go)) continue;
                    Task t = Consider(st, go, job, axe, pick);
                    if (t == null || t.Kind != Kind.Hit || Skipped(st, t.Target) || ClaimedByOther(st, t.Target)) continue;
                    float d = Vector3.Distance(t.Target.transform.position, master.transform.position);
                    if (d < bestD) { bestD = d; best = t; }
                }
                st.Task = best;
                Claim(st, best);
            }
            if (st.Task == null) { st.WorkTool = null; return false; }
            st.Helping = true;
            Execute(st, master, dt, moveTo, stop, lookAt);
            return true;
        }

        // What pointing at go asks of it, with the tools in its bag, else with a better one from its own chest (fetched first).
        public static Task Workable(BrainState st, GameObject go)
        {
            if (go == null) return null;
            Humanoid me = st.Body;
            const Job all = Job.Wood | Job.Stone | Job.Ore | Job.Forage;
            ItemDrop.ItemData axe = Axe(me), pick = Pickaxe(me);
            Task t = Consider(st, go, all, axe, pick, true);
            if (t != null) return t;
            foreach (Container chest in Home.Chests(me).Where(c => !Containers.InUse(c)).OrderBy(c => Vector3.Distance(c.transform.position, me.transform.position)))
            {
                List<ItemDrop.ItemData> items = chest.GetInventory().GetAllItems();
                ItemDrop.ItemData chestAxe = items.Where(i => i.m_shared.m_damages.m_chop > 0f && i.IsWeapon() && Usable(i)).OrderByDescending(i => i.m_shared.m_toolTier).FirstOrDefault();
                ItemDrop.ItemData chestPick = items.Where(i => i.m_shared.m_damages.m_pickaxe > 0f && Usable(i)).OrderByDescending(i => i.m_shared.m_toolTier).FirstOrDefault();
                if (chestAxe == null && chestPick == null) continue;
                t = Consider(st, go, all, chestAxe ?? axe, chestPick ?? pick, true);
                if (t == null || t.Kind != Kind.Hit || !Brain.CanReach(me, chest.transform.position)) continue;
                ItemDrop.ItemData tool = t.Job == Job.Wood ? chestAxe : chestPick;
                if (tool == null) return t;
                return Fetching(t, chest, i => i == tool);
            }
            return null;
        }

        public static bool Ordered(BrainState st, Task t, bool fromYou = true)
        {
            if (t == null || t.Target == null) return false;
            if (t.FetchFrom != null)
            {
                Task fetch = New(Kind.Store, t.FetchFrom, Job.None);
                fetch.Take = t.Take;
                t.FetchFrom = null;
                t.Take = null;
                if (!Ordered(st, fetch, fromYou)) return false;
                st.Then = t;
                return true;
            }
            Humanoid me = st.Body;
            Order order = Companion.OrderOf(me);
            if ((order == Order.Stay || order == Order.Guard) && !Companion.Write(me, z => z.Set(Keys.Order, (int)Order.Follow))) return false;
            t.Started = t.LastClose = Time.time;
            t.Ordered = true;
            st.Task = t;
            st.Helping = false;
            st.CommandUntil = Time.time + 120f;
            if (fromYou) { st.RepairAt = null; st.ComeUntil = 0f; st.EmoteUntil = 0f; st.PendingEmote = null; st.Farm = null; st.Then = null; }
            st.Remember(P(fromYou ? "villagepleb_work_you_pointed" : "villagepleb_work_next_in_patch", Describe(t)));
            return true;
        }

        public static bool OnOrder(BrainState st) =>
            st.Area != null || st.PickQueue.Count > 0 || st.Farm != null || st.Then != null || st.Task != null && st.Task.Ordered && Time.time < st.CommandUntil;

        public static bool Ordered(BrainState st, Kind kind, Component target) => Ordered(st, New(kind, target, Job.None));

        public static bool OrderPickUp(BrainState st, Vector3 at, float radius)
        {
            var drops = new HashSet<ItemDrop>();
            foreach (Collider col in Physics.OverlapSphere(at, radius, ~0, QueryTriggerInteraction.Collide))
            {
                ItemDrop d = col.GetComponentInParent<ItemDrop>();
                if (d != null && d.m_itemData != null && d.GetComponent<Piece>() == null && d.m_itemData.m_shared.m_itemType != ItemDrop.ItemData.ItemType.None) drops.Add(d);
            }
            if (drops.Count == 0) return false;
            List<ItemDrop> order = drops.OrderBy(d => Vector3.Distance(d.transform.position, at)).ToList();
            st.PickQueue.Clear();
            st.PickQueue.AddRange(order.Skip(1));
            return Ordered(st, New(Kind.PickUp, order[0], Job.Loot));
        }

        internal class Area
        {
            public Vector3 Center, YouAt;
            public float Radius = 12f, Until, YouAwaySince;
            public Job Job;
            public int Done, Left = -1;
            public float LeftAt;
            public readonly List<Component> Marked = new List<Component>();
        }

        public const string AreaKey = "vp_area";

        public static void SaveArea(BrainState st)
        {
            ZDO z = Companion.Zdo(st.Body);
            if (z == null) return;
            Area a = st.Area;
            if (a == null || ZNet.instance == null) { z.Set(AreaKey, ""); return; }
            double end = ZNet.instance.GetTimeSeconds() + Mathf.Max(0f, a.Until - Time.time);
            string V(Vector3 v) => $"{v.x.ToString("0.0", CultureInfo.InvariantCulture)},{v.y.ToString("0.0", CultureInfo.InvariantCulture)},{v.z.ToString("0.0", CultureInfo.InvariantCulture)}";
            z.Set(AreaKey, string.Join("|", V(a.Center), a.Radius.ToString(CultureInfo.InvariantCulture), (int)a.Job, V(a.YouAt), end.ToString("0", CultureInfo.InvariantCulture), a.Done));
        }

        public static bool RestoreArea(BrainState st)
        {
            ZDO z = Companion.Zdo(st.Body);
            string s = z?.GetString(AreaKey, "") ?? "";
            if (s.Length == 0 || ZNet.instance == null) return false;
            try
            {
                string[] f = s.Split('|');
                Vector3 V(string t) { string[] p = t.Split(','); return new Vector3(float.Parse(p[0], CultureInfo.InvariantCulture), float.Parse(p[1], CultureInfo.InvariantCulture), float.Parse(p[2], CultureInfo.InvariantCulture)); }
                double left = double.Parse(f[4], CultureInfo.InvariantCulture) - ZNet.instance.GetTimeSeconds();
                if (left <= 0) { z.Set(AreaKey, ""); return false; }
                var a = new Area { Center = V(f[0]), Radius = float.Parse(f[1], CultureInfo.InvariantCulture), Job = (Job)int.Parse(f[2]), YouAt = V(f[3]), Until = Time.time + (float)left, Done = int.Parse(f[5]) };
                st.Area = a;
                st.CommandUntil = Time.time + 120f;
                a.Marked.AddRange(AreaTasks(st, a).Take(8).Select(t => t.Target));
                foreach (Component c in a.Marked) { Component cc = c; Marks.Put(cc, st.Body, null, () => st.Area == a && cc != null); }
                st.Remember(P("villagepleb_work_patch_carried_on"));
                return true;
            }
            catch { z.Set(AreaKey, ""); return false; }
        }

        public static string JobText(BrainState st, out float progress)
        {
            progress = -1f;
            Humanoid me = st.Body;
            Area a = st.Area;
            if (a != null)
            {
                if (Time.time >= a.LeftAt) { a.LeftAt = Time.time + 3f; a.Left = AreaTasks(st, a).Count(); }
                if (a.Done + a.Left > 0) progress = a.Done / (float)(a.Done + a.Left);
                string key = a.Job == Job.Wood ? "villagepleb_work_job_patch_wood" : a.Job == Job.Forage ? "villagepleb_work_job_patch_forage" : "villagepleb_work_job_patch_rocks";
                return P(key, a.Done, Mathf.Max(0, a.Left));
            }
            if (st.PickQueue.Count > 0 || st.Task != null && st.Task.Ordered && st.Task.Kind == Kind.PickUp) return P("villagepleb_work_job_picking_up", st.PickQueue.Count + 1);
            if (st.Task != null && st.Task.Ordered && Time.time < st.CommandUntil) return P("villagepleb_work_job_pointed", Describe(st.Task));
            switch (Companion.OrderOf(me))
            {
                case Order.Guard: return P("villagepleb_work_job_guard");
                case Order.Stay: return P("villagepleb_work_job_stay");
                case Order.Follow: return string.IsNullOrEmpty(st.AutoNote) ? P("villagepleb_work_job_out") : P("villagepleb_work_job_out_note", st.AutoNote);
            }
            Mission m = Missions.Current(me);
            if (m != null && m.StartCost > 0) progress = Mathf.Clamp01(1f - m.Cost / (float)m.StartCost);
            return st.Goal == null ? P("villagepleb_work_job_home")
                 : st.Goal.Raw.Count > 0 ? P("villagepleb_work_job_home_mission_still", st.Goal.What, st.Goal.RawText()) : P("villagepleb_work_job_home_mission", st.Goal.What);
        }

        public static void StopJob(BrainState st)
        {
            st.Area = null;
            st.PickQueue.Clear();
            st.Farm = null;
            st.Then = null;
            if (st.Task != null && st.Task.Ordered) st.Task = null;
            st.CommandUntil = 0f;
            SaveArea(st);
            st.Remember(P("villagepleb_work_stopped_job"));
        }

        public static List<Component> OrderArea(BrainState st, Task first)
        {
            var area = new Area { Center = first.Target.transform.position, Until = Time.time + 900f,
                                  Job = first.Kind == Kind.Pick ? Job.Forage : first.Job == Job.Wood ? Job.Wood : Job.Stone | Job.Ore };
            Player you = Companion.Master(st.Body);
            area.YouAt = you != null ? you.transform.position : area.Center;
            st.Area = area;
            SaveArea(st);
            area.Marked.AddRange(AreaTasks(st, area).OrderBy(t => Flat(t.Target.transform.position, area.Center)).Take(8).Select(t => t.Target));
            return area.Marked;
        }

        private static IEnumerable<Task> AreaTasks(BrainState st, Area a)
        {
            ItemDrop.ItemData axe = Axe(st.Body), pick = Pickaxe(st.Body);
            var seen = new HashSet<GameObject>();
            foreach (Collider col in Physics.OverlapSphere(a.Center, a.Radius, ~0, QueryTriggerInteraction.Collide))
            {
                GameObject go = col.attachedRigidbody != null ? col.attachedRigidbody.gameObject : col.transform.root.gameObject;
                if (!seen.Add(go)) continue;
                Task t = Consider(st, go, a.Job, axe, pick, true);
                if (t == null || (t.Kind != Kind.Hit && t.Kind != Kind.Pick) || Skipped(st, t.Target) || ClaimedByOther(st, t.Target)) continue;
                yield return t;
            }
        }

        public static void EndArea(BrainState st, string say)
        {
            if (st.Area == null) return;
            st.Area = null;
            SaveArea(st);
            if (say != null) Talk.Tell(st.Body, say);
        }

        public static bool NextInArea(BrainState st)
        {
            Area a = st.Area;
            if (a == null) return false;
            Humanoid me = st.Body;
            Player master = Companion.Master(me);
            if (Time.time > a.Until) { EndArea(st, null); return false; }
            if (Companion.OrderOf(me) == Order.Follow && master != null && Flat(master.transform.position, a.YouAt) > 60f)
            {
                if (a.YouAwaySince == 0f) a.YouAwaySince = Time.time;
                else if (Time.time - a.YouAwaySince > 20f) { EndArea(st, null); return false; }
            }
            else a.YouAwaySince = 0f;
            Inventory inv = me.GetInventory();
            if (BagFull(me))
            {
                Vector3 home = Center(me);
                bool nearHome = HasHome(me) && Flat(a.Center, home) < RadiusOf(me) + 40f;
                if (nearHome)
                {
                    a.Until = Time.time + 900f;
                    Talk.Mention(me, P("villagepleb_work_area_full_back"), "areafull", 2f);
                    bool yoursToGive = Stows(me) && inv.GetAllItems().Any(i => YoursToGive(st, i));
                    if (yoursToGive)
                    {
                        Container yours = YourChests(me, home, RadiusOf(me) + 20f).Where(c => HasRoom(c, me) && !Skipped(st, c)).Take(6).FirstOrDefault(c => Brain.CanReach(me, c.transform.position));
                        if (yours != null) return Ordered(st, New(Kind.Store, yours, Job.None), false);
                    }
                    var stock = inv.GetAllItems().Where(i => !Keeps(me, i) && (!Stows(me) || IsStock(st, i) && StillWanted(st, i) > 0)).Select(i => i.m_shared.m_name).ToList();
                    Container own = stock.Count == 0 ? null : Home.Chests(me).Where(c => !Containers.InUse(c) && HasRoom(c, me) && !Skipped(st, c))
                        .OrderByDescending(c => c.GetInventory().GetAllItems().Count(i => stock.Contains(i.m_shared.m_name))).ThenBy(c => Vector3.Distance(c.transform.position, me.transform.position)).FirstOrDefault();
                    if (own != null) return Ordered(st, New(Kind.Store, own, Job.None), false);
                }
                EndArea(st, P("villagepleb_work_area_full"));
                return false;
            }
            ItemDrop drop = null;
            float best = float.MaxValue;
            foreach (Collider col in Physics.OverlapSphere(a.Center, a.Radius + 4f, ~0, QueryTriggerInteraction.Collide))
            {
                ItemDrop d = col.GetComponentInParent<ItemDrop>();
                if (d == null || d.m_itemData == null || d.GetComponent<Piece>() != null || Skipped(st, d)) continue;
                if (!Goals.DroppedBy(Utils.GetPrefabName(d.gameObject), a.Job)) continue;
                float dd = Vector3.Distance(d.transform.position, me.transform.position);
                if (dd < best) { best = dd; drop = d; }
            }
            if (drop != null) return Ordered(st, New(Kind.PickUp, drop, Job.Loot), false);
            Task next = AreaTasks(st, a).OrderBy(t => Vector3.Distance(t.Target.transform.position, me.transform.position) + Priority(t)).FirstOrDefault();
            if (next == null)
            {
                EndArea(st, a.Done > 0 ? P(a.Job == Job.Wood ? "villagepleb_work_area_done_trees" : a.Job == Job.Forage ? "villagepleb_work_area_done_picked" : "villagepleb_work_area_done_rocks") : null);
                return false;
            }
            a.Done++;
            SaveArea(st);
            Claim(st, next);
            if (!Ordered(st, next, false)) return false;
            Marks.Put(next.Target, me, P(next.Kind == Kind.Pick ? "villagepleb_work_mark_picking" : next.Job == Job.Wood ? "villagepleb_work_mark_chopping" : "villagepleb_work_mark_mining"), () => st.Task == next && next.Target != null);
            return true;
        }

        public static bool NextQueued(BrainState st)
        {
            if (st.Then != null)
            {
                Task then = st.Then;
                st.Then = null;
                if (then.Target != null && Ordered(st, then, false)) return true;
            }
            st.PickQueue.RemoveAll(d => d == null);
            if (st.PickQueue.Count == 0) return false;
            ItemDrop next = st.PickQueue[0];
            st.PickQueue.RemoveAt(0);
            return Ordered(st, New(Kind.PickUp, next, Job.Loot));
        }

        public static bool RunOrdered(BrainState st, Player master, float dt, Action<Vector3, float, bool> moveTo, Action stop, Action<Vector3> lookAt)
        {
            if (Clearing(st, moveTo)) return true;
            if (st.Task == null || !Valid(st, st.Task)) { Drop(st); if (NextQueued(st) || NextInArea(st) || NextFarm(st)) return true; return Time.time < st.ClearUntil; }
            Execute(st, master, dt, moveTo, stop, lookAt);
            return st.Task != null || st.Area != null || st.PickQueue.Count > 0 || st.Farm != null || st.Then != null;
        }

        private static void Execute(BrainState st, Player master, float dt, Action<Vector3, float, bool> moveTo, Action stop, Action<Vector3> lookAt)
        {
            Humanoid me = st.Body;
            Task t = st.Task;
            if (t.Ordered) { Action<Vector3, float, bool> walk = moveTo; moveTo = (p, d, run) => walk(p, d, run || Flat(p, me.transform.position) > 3f); }
            Vector3 at = t.Kind == Kind.Plant ? t.Pos : Point(t.Target, me.transform.position);
            float dist = Flat(at, me.transform.position);
            if (Time.time - t.Started > (t.Trip ? 240f : t.Ordered ? 180f : 60f) || (dist > 3f && Time.time - t.LastClose > (t.Trip || t.Ordered ? 120f : 20f)))
            {
                if (t.Kind == Kind.Mend)
                    foreach (Collider col in Physics.OverlapSphere(t.Target.transform.position, 6f, LayerMask.GetMask("piece", "piece_nonsolid")))
                        if (col.GetComponentInParent<WearNTear>() is WearNTear near && near != t.Target) st.Skipped[near.gameObject.GetInstanceID()] = Time.time + 600f;
                if (t.Kind == Kind.Plant) { Farming.Bad(st, t.Pos); st.Task = null; return; }
                Skip(st, t.Target, P("villagepleb_work_skip_cant_reach"));
                return;
            }

            switch (t.Kind)
            {
                case Kind.PickUp:
                    if (BagFull(me) && !t.Ordered) { st.Task = null; break; }
                    if (dist > 1.2f) { moveTo(at, 0.5f, dist > 6f); break; }
                    t.LastClose = Time.time;
                    var drop = (ItemDrop)t.Target;
                    if (!drop.CanPickup(false)) { drop.RequestOwn(); break; }
                    string name = drop.m_itemData.m_shared.m_name;
                    int n = drop.m_itemData.m_stack;
                    if (!me.GetInventory().CanAddItem(drop.m_itemData)) { Skip(st, drop, P("villagepleb_work_skip_no_room")); st.Task = null; break; }
                    if (me.Pickup(drop.gameObject, false, false)) { st.Gathered[name] = (st.Gathered.TryGetValue(name, out int had) ? had : 0) + n; }
                    else Skip(st, drop, P("villagepleb_work_skip_no_room"));
                    st.Task = null;
                    break;

                case Kind.Pick:
                    if (dist > 1.5f) { moveTo(at, 0.8f, dist > 6f); break; }
                    t.LastClose = Time.time;
                    stop();
                    var pickable = (Pickable)t.Target;
                    if (pickable.m_itemPrefab != null) st.Wanted.Add(pickable.m_itemPrefab.name);
                    foreach (DropTable.DropData extra in pickable.m_extraDrops.m_drops) if (extra.m_item != null) st.Wanted.Add(extra.m_item.name);
                    st.WorkSpot = pickable.transform.position;
                    bool ripe = !(Companion.Zdo(pickable)?.GetBool(ZDOVars.s_picked, false) ?? true);
                    pickable.Interact(me, false, false);
                    if (ripe) EpicMmo.Worked(me, pickable, EpicMmo.Work.Pick);
                    Skip(st, pickable, null, 5f);
                    st.Task = null;
                    break;

                case Kind.Hit:
                    ItemDrop.ItemData tool = t.Job == Job.Wood ? Axe(me) : Pickaxe(me);
                    if (tool == null) { st.Task = null; break; }
                    if (st.WorkTool != tool) { st.WorkTool = tool; st.NextGear = 0f; }
                    Bounds size = Size(t.Target);
                    float big = Mathf.InverseLerp(0.6f, 3f, Mathf.Max(size.size.x, size.size.z, size.size.y * 0.5f));
                    float reach = Mathf.Lerp(0.6f, Mathf.Min(1.3f, (tool.m_shared.m_attack?.m_attackRange ?? 1.8f) * 0.6f), big) * t.Closer;
                    if (size.size.y > 0.01f && size.size.y < 1.5f) reach = Mathf.Min(reach, 0.5f);
                    reach = Mathf.Max(reach, me.GetRadius() + 0.25f);
                    if (dist > reach) { moveTo(at, reach * 0.5f, dist > 8f); break; }
                    t.LastClose = Time.time;
                    stop();
                    float tall = size.size.y > 0.01f ? size.max.y - at.y : 2f;
                    float swingHeight = tool.m_shared.m_attack?.m_attackHeight ?? 1f;
                    Vector3 swingFrom = new Vector3(me.transform.position.x, me.transform.position.y + swingHeight, me.transform.position.z);
                    Vector3 aim = new Vector3(at.x, Mathf.Min(swingFrom.y, at.y + Mathf.Max(0.25f, tall * 0.6f)), at.z);
                    lookAt(aim);
                    Vector3 level = aim - swingFrom;
                    if (level.sqrMagnitude > 0.01f) me.SetLookDir(level.normalized, 0f);
                    Vector3 facing = me.transform.forward, toAim = aim - me.transform.position;
                    facing.y = 0f; toAim.y = 0f;
                    if (!me.IsItemEquiped(tool) || me.InAttack() || toAim.sqrMagnitude > 0.01f && Vector3.Angle(facing, toAim) > 25f) break;
                    Attack second = tool.HaveSecondaryAttack() ? tool.m_shared.m_secondaryAttack : null;
                    bool downward = second != null && second.m_attackType == Attack.AttackType.Vertical && second.m_attackProjectile == null;
                    bool low = size.size.y > 0.01f && size.size.y < 1.2f;
                    bool chop = second != null && second.m_attackProjectile == null && second.m_attackType != Attack.AttackType.Projectile
                                && (downward && low || st.SwingMissed && t.MissedRun >= 2) && !Tactics.MidCombo(me);
                    float cost = (chop ? second.m_attackStamina : tool.m_shared.m_attack?.m_attackStamina) ?? 0f;
                    if (Stamina.Get(me) < cost + 1f) { if (chop) chop = false; else { Brain.Status(st, P("villagepleb_work_status_breath")); break; } }
                    if (me.GetTimeSinceLastAttack() < (chop ? 0.8f : 0.5f) && !Tactics.MidCombo(me)) break;
                    st.WorkSpot = at;
                    if (me.StartAttack(null, chop))
                    {
                        if (chop && !t.UsedChop) { t.UsedChop = true; Activity.Log(me, $"chops the {Hoverable(t.Target)} from above ({(low ? "it is low" : "its swings kept missing")}; its {Localization.instance.Localize(tool.m_shared.m_name).ToLowerInvariant()}'s secondary is {second.m_attackType})"); }
                        Missing(st, t);
                    }
                    break;

                case Kind.Store:
                    if (dist > 2f) { moveTo(at, 1.2f, dist > 8f); break; }
                    t.LastClose = Time.time;
                    stop();
                    Store(st, (Container)t.Target, t.Take);
                    st.Task = null;
                    break;

                case Kind.Craft:
                    if (dist > 2.6f && !CanReachOver(t, dist)) { moveTo(at, 1.8f, dist > 8f); break; }
                    t.LastClose = Time.time;
                    stop();
                    Upgrades.Craft(st, t.Recipe, (CraftingStation)t.Target, t.ForGoal);
                    st.Task = null;
                    break;

                case Kind.Cook:
                    if (dist > 2.2f && !CanReachOver(t, dist)) { moveTo(at, 1.5f, dist > 8f); break; }
                    t.LastClose = Time.time;
                    stop();
                    if (Time.time - t.Started > 240f || !Kitchen.Cook(st, (CookingStation)t.Target)) st.Task = null;
                    break;

                case Kind.Mend:
                    if (dist > 4.5f && !(dist < 6.5f && Time.time - t.Started > 8f)) { moveTo(at, 3.2f, dist > 8f); break; }
                    t.LastClose = Time.time;
                    stop();
                    lookAt(at + Vector3.up);
                    st.WorkTool = Mending.Hammer(me);
                    if (Time.time - t.Started < 0.8f || Time.time < st.NextMend) break;
                    st.NextMend = Time.time + 0.9f;
                    if (!Mending.Fix(st, (WearNTear)t.Target)) { st.Task = null; break; }
                    WearNTear more = Mending.Damaged(me, me.transform.position, 8f, c => !Skipped(st, c));
                    if (more != null) { t.Target = more; t.Started = Time.time; } else st.Task = null;
                    break;

                case Kind.Plant:
                    ItemDrop.ItemData cultivator = Farming.Cultivator(me);
                    if (cultivator == null) { st.Task = null; break; }
                    if (st.WorkTool != cultivator) { st.WorkTool = cultivator; st.NextGear = 0f; }
                    if (dist > 1.4f) { moveTo(at, 0.9f, dist > 6f); break; }
                    t.LastClose = Time.time;
                    stop();
                    lookAt(at);
                    if (!me.IsItemEquiped(cultivator) || Time.time < st.NextPlant) break;
                    st.NextPlant = Time.time + 0.8f;
                    if (!Farming.Place(st, t.Crop, t.Pos)) { Farming.Bad(st, t.Pos); st.Task = null; break; }
                    if (Farming.FindSpot(st, Center(me), RadiusOf(me), out Farming.Crop nextCrop, out Vector3 nextAt))
                    { t.Crop = nextCrop; t.Pos = nextAt; t.Target = Heightmap.FindHeightmap(nextAt); t.Started = t.LastClose = Time.time; }
                    else st.Task = null;
                    break;

                case Kind.Fuel:
                    if (dist > 2f) { moveTo(at, 1.2f, dist > 8f); break; }
                    t.LastClose = Time.time;
                    stop();
                    Fires.Feed(st, (Fireplace)t.Target);
                    st.Task = null;
                    break;

                case Kind.Fetch:
                    if (dist > 2f) { moveTo(at, 1.2f, dist > 8f); break; }
                    t.LastClose = Time.time;
                    stop();
                    TakeFood(st, (Container)t.Target);
                    st.Task = null;
                    break;

                case Kind.Armory:
                    if (dist > 2f) { moveTo(at, 1.2f, dist > 8f); break; }
                    t.LastClose = Time.time;
                    stop();
                    if (Armory.TakeFrom(st, (Container)t.Target, t.Ordered) == null) Skip(st, t.Target, P("villagepleb_work_skip_nothing_better"), 5f);
                    else if (t.Ordered && Armory.Find(me, Center(me), RadiusOf(me) + 20f, null, true) is Container next) { Ordered(st, New(Kind.Armory, next, Job.None), false); break; }
                    else st.NextArmoryLook = 0f;
                    st.Task = null;
                    break;

                case Kind.Hunt:
                    var prey = (Character)t.Target;
                    if (prey != null && prey.IsDead()) Loot.AddSpot(st, prey.transform.position);
                    if (prey == null || prey.IsDead()) { st.Task = null; break; }
                    if (Companion.BestRanged(me) == null && Harmless.Contains(Utils.GetPrefabName(prey.gameObject)) && Vector3.Distance(prey.transform.position, me.transform.position) > 30f)
                    { Skip(st, prey, P("villagepleb_work_skip_outran"), 3f); break; }
                    t.LastClose = Time.time;
                    st.WorkTool = Companion.BestRanged(me) ?? Companion.BestMelee(me);
                    Brain.Strike(st, prey, Time.deltaTime);
                    break;

                case Kind.Upgrade:
                    if (dist > 2.6f && !CanReachOver(t, dist)) { moveTo(at, 1.8f, dist > 8f); break; }
                    t.LastClose = Time.time;
                    stop();
                    Upgrades.Do(st, t.Item, (CraftingStation)t.Target);
                    st.Task = null;
                    break;
            }
            if (st.Task != null) Brain.Status(st, st.Helping ? P("villagepleb_work_status_helping", Describe(st.Task)) : st.Area != null ? P("villagepleb_work_status_patch", Describe(st.Task), st.Area.Done)
                                                : st.Task.ForGoal && st.Goal != null ? P("villagepleb_work_status_for_goal", Describe(st.Task), st.Goal.What) : Describe(st.Task));
        }

        private static bool TravelHome(BrainState st, Vector3 center, float radius, Action<Vector3, float, bool> moveTo)
        {
            Humanoid me = st.Body;
            float far = Flat(center, me.transform.position);
            if (far < radius + 60f || Time.time < st.TripUntil || OnOrder(st)) { st.TripStart = 0f; return false; }
            if (st.TripStart == 0f) { st.TripStart = Time.time; st.Task = null; st.Remember(P("villagepleb_work_set_off_home")); }
            moveTo(center, 2f, true);
            Brain.Status(st, P("villagepleb_work_status_heading_home"));
            if (Time.time - st.TripStart < (far > 250f ? 8f : 45f)) return true;
            st.TripStart = 0f;
            Vector3 pos = center + Vector3.up * 0.2f;
            me.transform.position = pos;
            Rigidbody body = me.GetComponent<Rigidbody>();
            if (body != null) { body.position = pos; body.linearVelocity = Vector3.zero; }
            Companion.Zdo(me)?.SetPosition(pos);
            st.Remember(P("villagepleb_work_got_home"));
            VillagePlebsPlugin.Instance?.Note($"{Companion.NameOf(me)} went home to {pos:F0}");
            return true;
        }

        private static Bounds Size(Component c)
        {
            Bounds b = new Bounds(c.transform.position, Vector3.zero);
            bool any = false;
            foreach (Collider col in c.GetComponentsInChildren<Collider>())
            {
                if (col == null || !col.enabled || col.isTrigger) continue;
                if (!any) { b = col.bounds; any = true; } else b.Encapsulate(col.bounds);
            }
            return b;
        }

        private static void Missing(BrainState st, Task t)
        {
            t.Swings++;
            t.MissedRun = st.SwingMissed ? t.MissedRun + 1 : 0;
            if (t.Swings % 3 != 1) return;
            float health = Companion.Zdo(t.Target)?.GetFloat(ZDOVars.s_health, -1f) ?? -1f;
            bool missed = health < 0f ? st.SwingMissed : t.HealthAt >= 0f && health >= t.HealthAt - 0.01f;
            if (missed && t.Closer > 0.35f)
            {
                t.Closer = Mathf.Max(0.6f, t.Closer * 0.75f);
                st.Remember(P("villagepleb_work_steps_closer", HoverName(t.Target)));
            }
            t.HealthAt = health;
        }

        private static bool CanReachOver(Task t, float dist) => dist < 6f && Time.time - t.Started > 8f;

        private static void Go(Vector3 center, Action<Vector3, float, bool> moveTo, Action stop, Humanoid me)
        {
            if (Flat(center, me.transform.position) > 4f) moveTo(center, 2f, Flat(center, me.transform.position) > 12f);
            else stop();
        }

        internal static string Describe(Task t)
        {
            string what = t.Target != null ? (t.Target is ItemDrop d ? d.m_itemData.m_shared.m_name : t.Target is Pickable p && p.m_itemPrefab != null ? p.m_itemPrefab.GetComponent<ItemDrop>()?.m_itemData.m_shared.m_name ?? "" : HoverName(t.Target)) : "";
            return t.Kind switch
            {
                Kind.Hit => P(t.Job == Job.Wood ? "villagepleb_work_do_chopping" : "villagepleb_work_do_mining", what),
                Kind.Pick => P(t.Target != null && Goals.IsCrop(Utils.GetPrefabName(t.Target.gameObject)) ? "villagepleb_work_do_harvesting" : "villagepleb_work_do_picking", what),
                Kind.Plant => P("villagepleb_work_do_planting", t.Crop?.Plant.m_name ?? ""),
                Kind.PickUp => P("villagepleb_work_do_picking_up", what),
                Kind.Store => P(Home.IdOn(t.Target) == 0L ? "villagepleb_work_do_store_yours" : "villagepleb_work_do_store_its"),
                Kind.Craft => P("villagepleb_work_do_making", t.Recipe?.m_item?.m_itemData.m_shared.m_name ?? "", ((CraftingStation)t.Target).m_name),
                Kind.Cook => P("villagepleb_work_do_cooking"),
                Kind.Fetch => P("villagepleb_work_do_fetch"),
                Kind.Armory => P("villagepleb_work_do_armory"),
                Kind.Fuel => P("villagepleb_work_do_fuel"),
                Kind.Mend => P("villagepleb_work_do_repairing", Low(HoverName(t.Target))),
                Kind.Hunt => P("villagepleb_work_do_hunting", ((Character)t.Target)?.m_name ?? ""),
                Kind.Upgrade => P("villagepleb_work_do_upgrading", t.Item?.m_shared.m_name ?? "", ((CraftingStation)t.Target).m_name),
                _ => P("villagepleb_work_do_gathering"),
            };
        }

        private static string Hoverable(Component c) => Localization.instance.Localize(HoverName(c));

        private static string HoverName(Component c)
        {
            Hoverable h = c.GetComponentInParent<Hoverable>();
            string n = h?.GetHoverName();
            if (!string.IsNullOrEmpty(n)) return n;
            string prefab = Utils.GetPrefabName(c.gameObject);
            return GameWord(prefab) ?? prefab.Replace('_', ' ');
        }

        private static string GameWord(string prefab)
        {
            string p = prefab.ToLowerInvariant();
            if (p.Contains("stub") || p.Contains("stump")) return "$prop_treestump";
            if (p.Contains("log")) return "$prop_treelog";
            foreach (var (part, token) in TreeWords) if (p.Contains(part)) return token;
            return null;
        }

        private static readonly (string, string)[] TreeWords =
        {
            ("yggashoot", "$prop_yggashoot"), ("ashland", "$prop_ashlandstree"), ("swamptree", "$prop_ancienttree"), ("beech", "$prop_beech"),
            ("birch", "$prop_birch"), ("oak", "$prop_oak"), ("pine", "$prop_pine"), ("fir", "$prop_fir"),
        };

        private static bool Valid(BrainState st, Task t)
        {
            if (t.Target == null) return false;
            if (t.Kind == Kind.Pick && Companion.Zdo(t.Target)?.GetBool(ZDOVars.s_picked, false) == true) return false;
            if ((t.Kind == Kind.Store || t.Kind == Kind.Fetch || t.Kind == Kind.Armory) && Containers.InUse((Container)t.Target)) return false;
            if (t.Kind == Kind.Mend && ((WearNTear)t.Target).GetHealthPercentage() >= 0.999f) return false;
            return true;
        }

        private static void Skip(BrainState st, Component target, string why, float minutes = 10f)
        {
            if (target != null) st.Skipped[target.gameObject.GetInstanceID()] = Time.time + minutes * 60f;
            if (why != null && target != null) st.Remember(P("villagepleb_work_gave_up_on", HoverName(target), why));
            st.Task = null;
        }

        private static bool Skipped(BrainState st, Component c) => st.Skipped.TryGetValue(c.gameObject.GetInstanceID(), out float until) && Time.time < until;

        private static Vector3 Point(Component c, Vector3 from)
        {
            if (c is TreeBase || c is Destructible des && (des.m_destructibleType == DestructibleType.Tree
                || des.m_damages.m_chop != HitData.DamageModifier.Immune && des.m_damages.m_pickaxe == HitData.DamageModifier.Immune))
            {
                Bounds low = Size(c);
                if (low.size.y > 0.01f && low.size.y < 1.5f)
                {
                    Vector3 mid = low.center, away = from - mid;
                    mid.y = c.transform.position.y; away.y = 0f;
                    float radius = Mathf.Min(low.extents.x, low.extents.z) * 0.8f;
                    return away.sqrMagnitude > 0.01f ? mid + away.normalized * radius : mid;
                }
                Vector3 foot = c.transform.position, nearest = foot;
                float bestD = float.MaxValue;
                foreach (Collider col in c.GetComponentsInChildren<Collider>())
                {
                    if (col == null || !col.enabled || col.isTrigger || col is MeshCollider m && !m.convex) continue;
                    Vector3 p = col.ClosestPoint(from);
                    if (Flat(p, foot) > 1.5f) continue;
                    float dd = (p - from).sqrMagnitude;
                    if (dd < bestD) { bestD = dd; nearest = p; }
                }
                if (bestD == float.MaxValue) { Vector3 toMe = from - foot; toMe.y = 0f; if (toMe.sqrMagnitude > 0.01f) nearest = foot + toMe.normalized * 0.25f; }
                nearest.y = foot.y;
                return nearest;
            }
            Collider best = null;
            float d = float.MaxValue;
            foreach (Collider col in c.GetComponentsInChildren<Collider>())
            {
                if (col == null || !col.enabled || col.isTrigger) continue;
                Vector3 p = Closest(col, from);
                float dd = (p - from).sqrMagnitude;
                if (dd < d) { d = dd; best = col; }
            }
            if (best == null) return c.transform.position;
            Vector3 q = Closest(best, from);
            q.y = Mathf.Max(c.transform.position.y, q.y - 0.5f);
            return q;
        }

        private static float Flat(Vector3 a, Vector3 b) { a.y = 0f; b.y = 0f; return Vector3.Distance(a, b); }

        private static Vector3 Closest(Collider col, Vector3 from)
        {
            if (!(col is MeshCollider mesh) || mesh.convex) return col.ClosestPoint(from);
            Vector3 start = new Vector3(from.x, Mathf.Clamp(from.y + 0.8f, col.bounds.min.y + 0.2f, col.bounds.max.y), from.z);
            Vector3 toMiddle = col.bounds.center - start;
            if (toMiddle.sqrMagnitude > 0.01f && col.Raycast(new Ray(start, toMiddle.normalized), out RaycastHit hit, toMiddle.magnitude + 1f)) return hit.point;
            return col.ClosestPointOnBounds(from);
        }

        private static Task Choose(BrainState st, Job jobs, Vector3 center, float radius)
        {
            Humanoid me = st.Body;
            Inventory inv = me.GetInventory();
            st.WorkNote = null;

            bool full = BagFull(me) || inv.GetAllItems().Count(i => !Keeps(me, i)) >= 18;
            bool dutyLoad = st.OnDuties && Duties.Deliver(me, st.Duty);
            bool yoursToGive = Stows(me) && inv.GetAllItems().Any(i => YoursToGive(st, i));
            if (yoursToGive && (full || dutyLoad || Time.time >= st.NextDeliver))
            {
                st.NextDeliver = Time.time + 120f;
                Container yours = YourChests(me, center, radius + 20f).Where(c => HasRoom(c, me) && !Skipped(st, c)).Take(6).FirstOrDefault(c => Brain.CanReach(me, c.transform.position));
                if (yours != null) return New(Kind.Store, yours, Job.None);
            }
            if (full || dutyLoad)
            {
                var stock =inv.GetAllItems().Where(i => !Keeps(me, i) && (!Stows(me) || IsStock(st, i) && StillWanted(st, i) > 0)).Select(i => i.m_shared.m_name).ToList();
                Container chest = Home.Chests(me).Where(c => !Containers.InUse(c) && Vector3.Distance(c.transform.position, center) < radius + 40f && HasRoom(c, me) && !Skipped(st, c))
                                     .OrderByDescending(c => c.GetInventory().GetAllItems().Count(i => stock.Contains(i.m_shared.m_name)))
                                     .ThenBy(c => Vector3.Distance(c.transform.position, me.transform.position)).FirstOrDefault(c => Brain.CanReach(me, c.transform.position));
                if (chest != null && stock.Count > 0) return New(Kind.Store, chest, Job.None);
                if (full && inv.GetEmptySlots() == 0) { st.WorkNote = P(Stows(me) ? "villagepleb_work_note_bag_full_yours" : "villagepleb_work_note_bag_full_own"); return null; }
            }
            if (Stows(me) && Time.time >= st.NextTidy)
            {
                st.NextTidy = Time.time + 600f;
                Container messy = Home.Chests(me).Where(c => !Containers.InUse(c) && !Skipped(st, c) && Messy(st, c)).FirstOrDefault(c => Brain.CanReach(me, c.transform.position));
                if (messy != null) return New(Kind.Store, messy, Job.None);
            }

            bool hungry = !inv.GetAllItems().Any(Food.IsFood);
            st.Hungry = hungry && Food.Meals(me).Count < 2;
            st.Weak = Food.Meals(me).Count == 0 && me.GetHealthPercentage() < 0.4f;
            if (hungry && Time.time >= st.NextFoodLook)
            {
                st.NextFoodLook = Time.time + 20f;
                Container pantry = Home.Chests(me).Where(c => !Containers.InUse(c) && c.GetInventory().GetAllItems().Any(i => Food.IsFood(i) || IsCookable(i)) && !Skipped(st, c))
                                       .OrderBy(c => Vector3.Distance(c.transform.position, me.transform.position)).FirstOrDefault(c => Brain.CanReach(me, c.transform.position));
                if (pantry != null) return New(Kind.Store, pantry, Job.None);
                if (Food.Meals(me).Count == 0 && UsesPantry(me))
                {
                    Container yours = YourFood(me, center, radius, c => !Skipped(st, c));
                    if (yours != null) return New(Kind.Fetch, yours, Job.None);
                }
                if (Food.Meals(me).Count == 0) Talk.Tell(me, P("villagepleb_work_no_food"), "nofood", 20f);
            }
            if (Gear.FoodShort(me) && Time.time >= st.NextRefillLook)
            {
                st.NextRefillLook = Time.time + 120f;
                Container larder = Home.Chests(me).Where(c => !Containers.InUse(c) && !Skipped(st, c) && c.GetInventory().GetAllItems().Any(i => Gear.WantsFood(me, i)))
                                       .OrderBy(c => Vector3.Distance(c.transform.position, me.transform.position)).FirstOrDefault(c => Brain.CanReach(me, c.transform.position));
                if (larder != null) { st.Remember(P("villagepleb_work_to_own_food")); return New(Kind.Store, larder, Job.None); }
                if (UsesPantry(me))
                {
                    Container yours = YourFood(me, center, radius, c => !Skipped(st, c));
                    if (yours != null) { st.Remember(P("villagepleb_work_to_your_food")); return New(Kind.Fetch, yours, Job.None); }
                }
            }

            Job lacking = st.OnDuties ? Job.None : Unequipped(me, JobsOf(me));
            if (lacking != Job.None && Time.time >= st.NextToolLook)
            {
                st.NextToolLook = Time.time + 60f;
                Func<ItemDrop.ItemData, bool> tool = ToolPicker(me, lacking);
                Container toolChest = Home.Chests(me).Where(c => !Containers.InUse(c) && !Skipped(st, c) && c.GetInventory().GetAllItems().Any(tool))
                                          .FirstOrDefault(c => Brain.CanReach(me, c.transform.position));
                if (toolChest != null) { Task fetch = New(Kind.Store, toolChest, Job.None); fetch.Take = tool; return fetch; }
            }

            if (Time.time >= st.NextArmoryLook)
            {
                st.NextArmoryLook = Time.time + 300f;
                Container better = Armory.Find(me, center, radius, c => !Skipped(st, c));
                if (better != null) { st.Remember(P("villagepleb_work_to_armory")); return New(Kind.Armory, better, Job.None); }
            }

            if (Time.time >= st.NextFireLook)
            {
                st.NextFireLook = Time.time + 20f;
                Fireplace fire = Fires.Low(me, center, radius);
                if (fire != null) return New(Kind.Fuel, fire, Job.None);
            }

            if (Time.time >= st.NextMendLook)
            {
                st.NextMendLook = Time.time + 20f;
                WearNTear damaged = Mending.Damaged(me, center, radius, c => !Skipped(st, c) && Brain.CanReach(me, c.transform.position, 4f));
                if (damaged != null)
                {
                    if (Mending.Hammer(me) == null) Mending.MakeHammer(st);
                    if (Mending.Hammer(me) != null) return New(Kind.Mend, damaged, Job.None);
                    Talk.Tell(me, P("villagepleb_work_need_hammer"), "nohammer", 30f);
                }
            }

            if (Time.time >= st.NextUpgradeLook)
            {
                st.NextUpgradeLook = Time.time + 30f;
                var up = Upgrades.Find(me, center, radius);
                if (up != null) { Task u = New(Kind.Upgrade, up.Value.Value, Job.None); u.Item = up.Value.Key; return u; }
                var craft = Upgrades.FindCraft(me, center, radius);
                if (craft != null && craft.Value.Value == null) { Upgrades.Craft(st, craft.Value.Key, null); craft = null; }
                if (craft != null) { Task t2 = New(Kind.Craft, craft.Value.Value, Job.None); t2.Recipe = craft.Value.Key; return t2; }
                foreach (string gone in st.Unfindable.Where(kv => kv.Value < Time.time).Select(kv => kv.Key).ToList()) st.Unfindable.Remove(gone);
                st.Goal = Goals.Pick(me, center, radius, st);
                Missions.Track(st, st.Goal, st.Candidates);
                Goals.Announce(st, st.Goal);
                var step = Goals.StepReady(me, st.Goal);
                if (step != null && step.Value.Value == null) { Upgrades.Craft(st, step.Value.Key, null, true); step = null; }
                if (step != null) { Task t3 = New(Kind.Craft, step.Value.Value, Job.None); t3.Recipe = step.Value.Key; t3.ForGoal = true; return t3; }
            }

            Goal goal = Aim(st);
            Job goalJobs = Goals.JobsFor(goal, out HashSet<string> goalPrey);
            if (!Directed(st)) jobs |= goalJobs & ~Job.Loot;

            if ((jobs & Job.Cook) != 0)
            {
                CookingStation stove = Kitchen.Find(me, center, radius);
                if (stove == null && st.Hungry && inv.GetAllItems().Any(IsCookable))
                    Talk.Tell(me, P("villagepleb_work_no_stove"), "nostove", 20f);
                if (stove != null)
                {
                    foreach (CookingStation.ItemConversion conv in stove.m_conversion) if (conv.m_to != null) st.Wanted.Add(conv.m_to.name);
                    st.WorkSpot = stove.transform.position;
                    return New(Kind.Cook, stove, Job.Cook);
                }
            }

            if ((jobs & Job.Hunt) != 0 && (!st.Weak || Companion.BestRanged(me) != null) || (st.Weak && st.Hungry && Companion.BestRanged(me) != null))
            {
                bool forGoal = goalPrey.Count > 0 && !st.Hungry;
                HashSet<string> kinds = st.Weak ? Harmless : forGoal ? goalPrey : GameFor(me);
                float reachable = Companion.BestRanged(me) != null ? float.MaxValue : 25f;
                bool fit = forGoal && !st.Weak && me.GetHealthPercentage() > 0.6f;
                Character Find(float range) => Character.GetAllCharacters().Where(ch => ch != null && !ch.IsDead() && !ch.IsTamed() && kinds.Contains(Utils.GetPrefabName(ch.gameObject))
                        && !(st.LeaveAlone.TryGetValue(ch, out float until) && Time.time < until)
                        && (Companion.BestRanged(me) != null || !(ch.IsSwimming() || ch.InWater()))
                        && (!Harmless.Contains(Utils.GetPrefabName(ch.gameObject)) || Vector3.Distance(ch.transform.position, me.transform.position) < reachable)
                        && Vector3.Distance(ch.transform.position, center) < range && !Skipped(st, ch))
                    .OrderBy(ch => Vector3.Distance(ch.transform.position, me.transform.position)).Take(6)
                    .FirstOrDefault(ch => Companion.BestRanged(me) != null || Brain.CanReach(me, ch.transform.position, 3f));
                Character prey = Find(radius);
                bool trip = false;
                if (prey == null && fit) { prey = Find(170f); trip = prey != null; }
                if (prey != null)
                {
                    Task h = New(Kind.Hunt, prey, Job.Hunt);
                    h.ForGoal = forGoal;
                    h.Trip = trip;
                    if (trip)
                    {
                        if (Time.time >= st.TripUntil)
                        {
                            string what = Low(prey.m_name);
                            Talk.Mention(me, P("villagepleb_work_hunt_trip", what, FromHome(prey.transform.position, center), goal?.What ?? ""), "hunt:" + prey.m_name, 10f);
                            st.Remember(P("villagepleb_work_hunt_trip_remember", what));
                        }
                        st.TripUntil = Time.time + 180f;
                    }
                    return h;
                }
            }

            ItemDrop loot = ItemDrops().Where(d => d != null && Vector3.Distance(d.transform.position, center) < radius && !Skipped(st, d)
                    && ((jobs & Job.Loot) != 0 || (st.Wanted.Contains(Utils.GetPrefabName(d.gameObject)) && Vector3.Distance(d.transform.position, st.WorkSpot) < 10f))
                    && inv.CanAddItem(d.m_itemData))
                .OrderBy(d => Vector3.Distance(d.transform.position, me.transform.position)).FirstOrDefault();
            if (loot != null && !BagFull(me)) return New(Kind.PickUp, loot, Job.Loot);

            ItemDrop.ItemData axe = Axe(me), pick = Pickaxe(me);
            var seen = new HashSet<GameObject>();
            bool goalSeen = false;
            Task best = null;
            float bestScore = float.MaxValue;
            float reach = (jobs & (Job.Wood | Job.Stone | Job.Ore)) != 0 ? radius + OutsideReach : radius;
            foreach (Collider col in Physics.OverlapSphere(center, reach, ~0, QueryTriggerInteraction.Collide))
            {
                GameObject go = col.attachedRigidbody != null ? col.attachedRigidbody.gameObject : col.transform.root.gameObject;
                if (!seen.Add(go)) continue;
                Task t = Consider(st, go, jobs, axe, pick);
                if (t == null || Skipped(st, t.Target) || ClaimedByOther(st, t.Target)) continue;
                if (t.Kind != Kind.Hit && Flat(t.Target.transform.position, center) > radius) continue;
                t.ForGoal = goal != null && Drops(t.Target).Any(goal.Wants);
                if (t.ForGoal) goalSeen = true;
                float score = Vector3.Distance(t.Target.transform.position, me.transform.position) + Priority(t) - (t.ForGoal ? 60f : 0f) - (t.Edible && st.Hungry ? 200f : 0f);
                if (score < bestScore) { bestScore = score; best = t; }
            }
            if (best == null)
            {
                var missing = new List<string>();
                if ((jobs & Job.Wood) != 0 && axe == null) missing.Add(P("villagepleb_work_an_axe"));
                if ((jobs & (Job.Stone | Job.Ore)) != 0 && pick == null) missing.Add(P("villagepleb_work_a_pickaxe"));
                st.WorkNote = goal != null && goal.Raw.Count > 0 && !goalSeen ? P("villagepleb_work_note_looking", Goals.Or(goal.Things()), goal.What)
                            : missing.Count > 0 ? P("villagepleb_work_note_needs_tools", Goals.And(missing)) : P("villagepleb_work_note_nothing_within", radius.ToString("0"));
            }
            bool searched = false;
            if (goal != null && goal.Raw.Count > 0 && !goalSeen && !Directed(st) && !st.Weak && !st.Hungry && me.GetHealthPercentage() > 0.6f
                && (Time.time >= st.NextTripLook || Time.time < st.TripUntil))
            {
                st.NextTripLook = Time.time + 20f;
                Task trip = FindTrip(st, center, radius, jobs | goalJobs, axe, pick);
                searched = trip == null;
                if (trip != null)
                {
                    bool starting = Time.time >= st.TripUntil;
                    st.TripUntil = Time.time + 300f;
                    if (starting)
                    {
                        string away = FromHome(trip.Target.transform.position, center);
                        string names = Goals.And(goal.Things());
                        Talk.Mention(me, P("villagepleb_work_trip_say", names, away), "trip:" + goal.What, 10f);
                        st.Remember(P("villagepleb_work_trip_remember", names));
                        Journal.Trip(me, P("villagepleb_work_trip_journal", away, names));
                    }
                    return trip;
                }
                if (Time.time < st.TripUntil) { st.TripUntil = 0f; st.Remember(P("villagepleb_work_trip_back")); }
            }
            if (searched && goal != null && goal.Raw.Count > 0 && !goalSeen && !Directed(st) && !goalPrey.Any())
            {
                Talk.Tell(me, P("villagepleb_work_cant_find", Goals.Or(goal.Things()), goal.What), "far:" + goal.What, 30f);
                foreach (string item in goal.Raw.Keys) st.Unfindable[item] = Time.time + 600f;
                st.NextUpgradeLook = 0f;
            }
            if (best == null && inv.GetEmptySlots() == 0) Talk.Tell(me, P("villagepleb_work_bag_no_room"), "full", 15f);
            Claim(st, best);
            return best;
        }

        private static float Priority(Task t)
        {
            if (t.Target is TreeLog) return -15f;
            if (t.Target is Destructible) return -5f;
            if (t.Target is TreeBase) return 5f;
            return 0f;
        }

        internal static Task New(Kind kind, Component target, Job job) => new Task { Kind = kind, Target = target, Job = job, Started = Time.time, LastClose = Time.time,
                                                                                     WasTree = target is TreeBase, Pos = target != null ? target.transform.position : Vector3.zero };

        private static readonly Dictionary<int, KeyValuePair<long, float>> Claims = new Dictionary<int, KeyValuePair<long, float>>();

        private static bool ClaimedByOther(BrainState st, Component target) =>
            target != null && Claims.TryGetValue(target.gameObject.GetInstanceID(), out var claim) && claim.Key != Companion.IdOf(st.Body) && claim.Value > Time.time;

        private static void Claim(BrainState st, Task t)
        {
            if (t?.Target == null || t.Kind != Kind.Hit) return;
            if (Claims.Count > 200) foreach (int gone in Claims.Where(kv => kv.Value.Value < Time.time).Select(kv => kv.Key).ToList()) Claims.Remove(gone);
            Claims[t.Target.gameObject.GetInstanceID()] = new KeyValuePair<long, float>(Companion.IdOf(st.Body), Time.time + 60f);
        }

        // How far past its home radius it goes for wood and stone, since it never takes them from inside a base.
        public const float OutsideReach = 50f;

        // A player base: the game's own (workbenches, fires) or within reach of anything a player built.
        internal static bool InBase(Vector3 at) => EffectArea.IsPointInsideArea(at, EffectArea.Type.PlayerBase) != null || NearBuildings(at);

        private static readonly Dictionary<int, KeyValuePair<bool, float>> BaseSeen = new Dictionary<int, KeyValuePair<bool, float>>();

        private static bool InBase(GameObject go)
        {
            int id = go.GetInstanceID();
            if (BaseSeen.TryGetValue(id, out var seen) && Time.time < seen.Value) return seen.Key;
            if (BaseSeen.Count > 4000) BaseSeen.Clear();
            bool inside = InBase(go.transform.position);
            BaseSeen[id] = new KeyValuePair<bool, float>(inside, Time.time + 30f);
            return inside;
        }

        internal static bool Breaks(GameObject go) =>
            go.GetComponent<TreeBase>() != null || go.GetComponent<TreeLog>() != null || go.GetComponent<MineRock>() != null || go.GetComponent<MineRock5>() != null || go.GetComponent<Destructible>() != null;

        private static bool NearBuildings(Vector3 at) =>
            Physics.OverlapSphere(at, 10f, LayerMask.GetMask("piece", "piece_nonsolid")).Any(col => col.GetComponentInParent<Piece>() is Piece p && p.IsPlacedByPlayer());

        private static void BeginClear(BrainState st, Vector3 tree)
        {
            Humanoid me = st.Body;
            Vector3 toTree = tree - me.transform.position; toTree.y = 0f;
            Vector3 side = Vector3.Cross(Vector3.up, toTree.sqrMagnitude > 0.01f ? toTree.normalized : me.transform.forward);
            Vector3 a = me.transform.position + side * 4f - toTree.normalized, b = me.transform.position - side * 4f - toTree.normalized;
            float ha = 0f, hb = 0f;
            if (ZoneSystem.instance != null) { ZoneSystem.instance.GetSolidHeight(a, out ha); ZoneSystem.instance.GetSolidHeight(b, out hb); }
            st.ClearTo = ha >= hb ? a : b;
            st.ClearUntil = Time.time + 2f;
        }

        private static bool Clearing(BrainState st, Action<Vector3, float, bool> moveTo)
        {
            if (Time.time >= st.ClearUntil) return false;
            moveTo(st.ClearTo, 0.5f, true);
            Brain.Status(st, P("villagepleb_work_status_clear_tree"));
            return true;
        }

        private static void Drop(BrainState st)
        {
            if (st.Task != null && st.Task.Kind == Kind.Hit && st.Task.WasTree && st.Task.Target == null) BeginClear(st, st.Task.Pos);
            st.Task = null;
        }

        internal static Task Consider(BrainState st, GameObject go, Job jobs, ItemDrop.ItemData axe, ItemDrop.ItemData pick, bool ordered = false)
        {
            if (!ordered && Breaks(go) && InBase(go)) return null;
            if (go.GetComponent<Piece>() != null || go.GetComponent<Character>() != null) return null;
            if ((jobs & Job.Wood) != 0 && axe != null)
            {
                TreeLog log = go.GetComponent<TreeLog>();
                if (log != null && axe.m_shared.m_toolTier >= log.m_minToolTier) return Wood(st, log, log.m_dropWhenDestroyed);
                TreeBase tree = go.GetComponent<TreeBase>();
                if (tree != null && axe.m_shared.m_toolTier >= tree.m_minToolTier) return Wood(st, tree, tree.m_dropWhenDestroyed);
            }
            Destructible des = go.GetComponent<Destructible>();
            if (des != null)
            {
                DropTable drops = go.GetComponent<DropOnDestroyed>()?.m_dropWhenDestroyed;
                if (drops == null || drops.m_drops.Count == 0) return null;
                bool wood = des.m_destructibleType == DestructibleType.Tree || des.m_damages.m_chop != HitData.DamageModifier.Immune && des.m_damages.m_pickaxe == HitData.DamageModifier.Immune;
                if (wood && (jobs & Job.Wood) != 0 && axe != null && axe.m_shared.m_toolTier >= des.m_minToolTier) return Wood(st, des, drops);
                if (!wood && pick != null && pick.m_shared.m_toolTier >= des.m_minToolTier && des.m_damages.m_pickaxe != HitData.DamageModifier.Immune)
                {
                    Job job = IsOre(drops) ? Job.Ore : Job.Stone;
                    if ((jobs & job) != 0) return Rock(st, des, drops, job);
                }
                return null;
            }
            if (pick != null && (jobs & (Job.Stone | Job.Ore)) != 0)
            {
                MineRock rock = go.GetComponent<MineRock>();
                if (rock != null && pick.m_shared.m_toolTier >= rock.m_minToolTier) { Job job = IsOre(rock.m_dropItems) ? Job.Ore : Job.Stone; if ((jobs & job) != 0) return Rock(st, rock, rock.m_dropItems, job); }
                MineRock5 rock5 = go.GetComponent<MineRock5>();
                if (rock5 != null && pick.m_shared.m_toolTier >= rock5.m_minToolTier) { Job job = IsOre(rock5.m_dropItems) ? Job.Ore : Job.Stone; if ((jobs & job) != 0) return Rock(st, rock5, rock5.m_dropItems, job); }
            }
            if ((jobs & Job.Forage) != 0)
            {
                Pickable p = go.GetComponent<Pickable>();
                bool wanted = p != null && p.m_itemPrefab != null && Aim(st) != null && Aim(st).Wants(p.m_itemPrefab.name);
                if (p != null && p.m_itemPrefab != null && !(Companion.Zdo(p)?.GetBool(ZDOVars.s_picked, false) ?? true) && (p.m_respawnTimeMinutes > 0f || wanted || ordered)
                    && !Goals.IsCrop(Utils.GetPrefabName(p.gameObject)) && !(Heightmap.FindHeightmap(p.transform.position)?.IsCultivated(p.transform.position) ?? false))
                {
                    ItemDrop.ItemData item = p.m_itemPrefab.GetComponent<ItemDrop>()?.m_itemData;
                    bool edible = item != null && (Food.IsFood(item) || IsCookable(item));
                    if (st.Hungry && !edible && !ordered) return null;
                    Task t = New(Kind.Pick, p, Job.Forage);
                    t.Edible = edible;
                    return t;
                }
            }
            return null;
        }

        private static Task FindTrip(BrainState st, Vector3 center, float radius, Job jobs, ItemDrop.ItemData axe, ItemDrop.ItemData pick)
        {
            Humanoid me = st.Body;
            var seen = new HashSet<GameObject>();
            Task best = null;
            float bestDist = float.MaxValue;
            foreach (Collider col in Physics.OverlapSphere(center, 170f, ~0, QueryTriggerInteraction.Collide))
            {
                GameObject go = col.attachedRigidbody != null ? col.attachedRigidbody.gameObject : col.transform.root.gameObject;
                if (!seen.Add(go)) continue;
                Task t = Consider(st, go, jobs, axe, pick);
                Goal aim = Aim(st);
                if (t == null || Skipped(st, t.Target) || aim == null || !Drops(t.Target).Any(aim.Wants)) continue;
                float d = Vector3.Distance(t.Target.transform.position, me.transform.position);
                if (d >= bestDist) continue;
                bestDist = d;
                best = t;
            }
            if (best != null) { best.Trip = true; best.ForGoal = true; }
            return best;
        }

        internal static string Compass(Vector3 way)
        {
            string[] names = { "n", "ne", "e", "se", "s", "sw", "w", "nw" };
            float angle = (Mathf.Atan2(way.x, way.z) * Mathf.Rad2Deg + 360f + 22.5f) % 360f;
            return names[Mathf.FloorToInt(angle / 45f) % 8];
        }

        private static string FromHome(Vector3 at, Vector3 center) => P("villagepleb_work_from_home_" + Compass(at - center), Flat(at, center).ToString("0"));

        internal static IEnumerable<string> Drops(Component c)
        {
            GameObject go = c.gameObject;
            if (go.GetComponent<Pickable>() is Pickable p) return p.m_itemPrefab != null ? new[] { p.m_itemPrefab.name } : new string[0];
            DropTable t = go.GetComponent<TreeBase>()?.m_dropWhenDestroyed ?? go.GetComponent<TreeLog>()?.m_dropWhenDestroyed ?? go.GetComponent<MineRock>()?.m_dropItems
                          ?? go.GetComponent<MineRock5>()?.m_dropItems ?? go.GetComponent<DropOnDestroyed>()?.m_dropWhenDestroyed;
            return t?.m_drops?.Where(d => d.m_item != null).Select(d => d.m_item.name) ?? Enumerable.Empty<string>();
        }

        private static Task Wood(BrainState st, Component c, DropTable drops) { Want(st, drops); return New(Kind.Hit, c, Job.Wood); }
        private static Task Rock(BrainState st, Component c, DropTable drops, Job job) { Want(st, drops); return New(Kind.Hit, c, job); }

        private static void Want(BrainState st, DropTable drops)
        {
            if (drops?.m_drops == null) return;
            foreach (DropTable.DropData d in drops.m_drops) if (d.m_item != null) st.Wanted.Add(d.m_item.name);
        }

        internal static bool IsOre(DropTable drops) => drops?.m_drops != null && drops.m_drops.Any(d => d.m_item != null && OreWords.Any(w => d.m_item.name.Contains(w)));

        private static IEnumerable<ItemDrop> ItemDrops() => GameFields.ItemDrops() ?? new List<ItemDrop>();

        internal static bool Edible(Component c)
        {
            ItemDrop.ItemData item = (c as Pickable)?.m_itemPrefab?.GetComponent<ItemDrop>()?.m_itemData;
            return item != null && (Food.IsFood(item) || IsCookable(item));
        }

        internal static bool Move(Container chest, Humanoid me, ItemDrop.ItemData item, int n, bool loan = false)
        {
            if (!Containers.Take(chest)) return false;
            Inventory its = chest.GetInventory();
            if (!its.ContainsItem(item))
                item = its.GetAllItems().FirstOrDefault(i => i.m_shared.m_name == item.m_shared.m_name && i.m_quality == item.m_quality && i.m_worldLevel == item.m_worldLevel && i.m_stack >= n);
            if (item == null || item.m_stack < n || !me.GetInventory().CanAddItem(item, n)) return false;
            ItemDrop.ItemData copy = item.Clone();
            copy.m_stack = n;
            copy.m_equipped = false;
            if (loan) Loans.Mark(copy, chest);
            if (!its.RemoveItem(item, n)) return false;
            if (!(loan ? AddApart(me.GetInventory(), copy) : me.GetInventory().AddItem(copy))) { its.AddItem(copy); return false; }
            return true;
        }

        private static bool AddApart(Inventory bag, ItemDrop.ItemData item)
        {
            int rows = Gear.IsCompanion(bag) ? Mathf.Min(bag.GetHeight(), Gear.BagRows) : bag.GetHeight();
            for (int y = 0; y < rows; y++)
                for (int x = 0; x < bag.GetWidth(); x++)
                {
                    if (bag.GetItemAt(x, y) != null) continue;
                    var hand = new Inventory("loan", null, 1, 1);
                    if (!hand.AddItem(item)) return bag.AddItem(item);
                    return bag.MoveItemToThis(hand, item, item.m_stack, x, y);
                }
            return bag.AddItem(item);
        }

        internal static Container YourFood(Humanoid me, Vector3 center, float radius, Func<Container, bool> allowed = null) =>
            UnityEngine.Object.FindObjectsByType<Container>(FindObjectsSortMode.None)
                .Where(c => c != null && c.GetInventory() != null && Home.IsChest(c) && Home.IdOn(c) == 0L && !Containers.InUse(c) && Vector3.Distance(c.transform.position, center) < radius && (allowed == null || allowed(c))
                            && c.GetInventory().GetAllItems().Any(Food.IsFood) && (!c.m_checkGuardStone || PrivateArea.CheckAccess(c.transform.position, 0f, false)))
                .OrderBy(c => Vector3.Distance(c.transform.position, me.transform.position)).Take(6).FirstOrDefault(c => Brain.CanReach(me, c.transform.position));

        internal static string TakeFoodFrom(Humanoid me, Container chest, int max, bool rationed = false)
        {
            if (!Containers.Take(chest)) return null;
            var took = new Dictionary<string, int>();
            bool yours = Home.IdOn(chest) == 0L;
            int left = rationed && yours ? Mathf.Min(max, RationLeft(me)) : max;
            bool slots = max > 5;
            foreach (ItemDrop.ItemData food in chest.GetInventory().GetAllItems().Where(Food.IsFood).OrderByDescending(i => i.m_shared.m_food + i.m_shared.m_foodStamina).ToList())
            {
                if (left <= 0) break;
                if (slots && !Gear.WantsFood(me, food)) continue;
                int carried = me.GetInventory().CountItems(food.m_shared.m_name);
                int n = Mathf.Min(left, food.m_stack, slots ? Mathf.Max(0, 5 - carried) : left);
                if (n <= 0) continue;
                string name = food.m_shared.m_name;
                if (!Move(chest, me, food, n, true)) continue;
                took[name] = (took.TryGetValue(name, out int had) ? had : 0) + n;
                left -= n;
                if (slots) Gear.Arrange(me);
            }
            if (took.Count == 0) return null;
            if (yours)
            {
                ZDO z = Companion.Zdo(me);
                if (z != null) { int day = Journal.Day; z.Set(RationNKey, (z.GetInt(RationDayKey, -1) == day ? z.GetInt(RationNKey, 0) : 0) + took.Values.Sum()); z.Set(RationDayKey, day); }
            }
            string list = string.Join(", ", took.Select(kv => P("villagepleb_work_amount", kv.Value, Low(kv.Key))));
            VillagePlebsPlugin.Instance?.Note($"{Companion.NameOf(me)} took {Words.English(list)} to eat from the chest at {chest.transform.position:F0}");
            return list;
        }

        private static void TakeFood(BrainState st, Container chest)
        {
            Humanoid me = st.Body;
            bool starving = !me.GetInventory().GetAllItems().Any(Food.IsFood);
            int have = me.GetInventory().GetAllItems().Where(Food.IsFood).Sum(i => i.m_stack);
            bool yours = Home.IdOn(chest) == 0L;
            if (yours && !starving && RationLeft(me) == 0)
            {
                Skip(st, chest, P("villagepleb_work_skip_ration"), 30f);
                Talk.Mention(me, P("villagepleb_work_ration_said", RationOf(me)), "ration", 120f);
                return;
            }
            string list = TakeFoodFrom(me, chest, starving ? 3 : Mathf.Clamp(9 - have, 3, 9), rationed: !starving);
            if (list == null) { Skip(st, chest, P("villagepleb_work_skip_no_room_food"), 5f); return; }
            st.Remember(P(starving ? "villagepleb_work_took_food_to_eat" : "villagepleb_work_took_food_for_slots", list));
            if (starving) Talk.Tell(me, P("villagepleb_work_took_food_starving", list));
            else Talk.Mention(me, P("villagepleb_work_took_food_later", list));
        }

        public static Dictionary<string, int> StockCaps(BrainState st)
        {
            if (st.StockCaps != null && Time.time < st.NextStockLook) return st.StockCaps;
            st.NextStockLook = Time.time + 15f;
            var caps = new Dictionary<string, int>();
            void Add(string name, int n) { if (name != null && n > 0) caps[name] = (caps.TryGetValue(name, out int had) ? had : 0) + n; }
            Humanoid me = st.Body;
            foreach (ItemDrop.ItemData item in me.GetInventory().GetAllItems().Where(i => Gear.InSlot(i) && Upgrades.Upgradable(i)))
            {
                Recipe r = ObjectDB.instance?.GetRecipe(item);
                foreach (Piece.Requirement q in Upgrades.Needs(r)) Add(q.m_resItem.m_itemData.m_shared.m_name, q.GetAmount(item.m_quality + 1));
            }
            Goal g = st.Goal;
            if (g?.Recipe != null) foreach (Piece.Requirement q in Upgrades.Needs(g.Recipe)) Add(q.m_resItem.m_itemData.m_shared.m_name, q.GetAmount(1));
            if (g != null)
                foreach (var kv in g.Raw)
                {
                    string name = ObjectDB.instance?.GetItemPrefab(kv.Key)?.GetComponent<ItemDrop>()?.m_itemData.m_shared.m_name;
                    if (name != null && !caps.ContainsKey(name)) Add(name, kv.Value);
                }
            return st.StockCaps = caps;
        }

        public static int StockCap(BrainState st, ItemDrop.ItemData i)
        {
            int duty = Duties.Cap(st.Body, i);
            if (Food.IsFood(i)) return Duties.FoodForYou(st.Body) ? 12 : Mathf.Max(40, duty);
            if (IsCookable(i)) return Mathf.Max(20, duty);
            if (i.m_shared.m_itemType == ItemDrop.ItemData.ItemType.Ammo) return 100;
            if (i.m_shared.m_itemType == ItemDrop.ItemData.ItemType.Consumable && Companion.Useful(st.Body, i)) return 10;
            return Mathf.Max(duty, StockCaps(st).TryGetValue(i.m_shared.m_name, out int n) ? n : 0);
        }

        public static bool IsStock(BrainState st, ItemDrop.ItemData i) => StockCap(st, i) > 0;

        public static bool BagFull(Humanoid me) => me.GetInventory().GetEmptySlots() <= 1 || Carry.Weight(me) > Carry.Max(me) * 0.9f;

        private static int Stocked(Humanoid me, string name) => Home.Chests(me).Sum(c => c.GetInventory().GetAllItems().Where(i => i.m_shared.m_name == name).Sum(i => i.m_stack));

        private static bool Messy(BrainState st, Container c) =>
            c.GetInventory().GetAllItems().Any(i => !IsStock(st, i) || Stocked(st.Body, i.m_shared.m_name) > StockCap(st, i));

        private static int StillWanted(BrainState st, ItemDrop.ItemData i) => Mathf.Max(0, StockCap(st, i) - Stocked(st.Body, i.m_shared.m_name));

        private static bool YoursToGive(BrainState st, ItemDrop.ItemData i)
        {
            if (Keeps(st.Body, i)) return false;
            if (!IsStock(st, i)) return true;
            int carried = st.Body.GetInventory().GetAllItems().Where(x => x.m_shared.m_name == i.m_shared.m_name && !Keeps(st.Body, x)).Sum(x => x.m_stack);
            return carried > StillWanted(st, i);
        }

        private static int MovePart(Inventory from, Inventory to, ItemDrop.ItemData item, int n)
        {
            if (n <= 0) return 0;
            if (n >= item.m_stack)
            {
                if (!to.CanAddItem(item)) return 0;
                int all = item.m_stack;
                to.MoveItemToThis(from, item);
                return all;
            }
            ItemDrop.ItemData part = item.Clone();
            part.m_stack = n;
            if (!to.CanAddItem(part) || !to.AddItem(part)) return 0;
            from.RemoveItem(item, n);
            return n;
        }

        public static bool Keeps(Humanoid h, ItemDrop.ItemData i) => Gear.InSlot(i) || h.IsItemEquiped(i) || Companion.Useful(h, i) || IsTool(i) || IsCookable(i) || Mending.IsHammer(i)
                                                                    || Farming.IsCultivator(i) || Farming.IsSeed(h, i);

        // The plant or harvest errand, one step at a time: carry in the crop it just picked, fetch a cultivator or seeds
        // from its own chest, then the next ripe crop or free spot. When there is nothing left it says so and stops.
        public static bool NextFarm(BrainState st)
        {
            if (st.Farm == null) return false;
            Humanoid me = st.Body;
            Vector3 center = Center(me);
            float radius = RadiusOf(me);
            string done = null;
            bool plant = st.Farm == Errand.Plant;
            if (BagFull(me)) done = P("villagepleb_work_farm_bag_full");
            else
            {
                ItemDrop crop = ItemDrops().Where(d => d != null && st.Wanted.Contains(Utils.GetPrefabName(d.gameObject)) && Vector3.Distance(d.transform.position, st.WorkSpot) < 4f
                                                       && !Skipped(st, d) && me.GetInventory().CanAddItem(d.m_itemData))
                                           .OrderBy(d => Vector3.Distance(d.transform.position, me.transform.position)).FirstOrDefault();
                if (crop != null) return Ordered(st, New(Kind.PickUp, crop, Job.Loot), false);
                if (plant)
                {
                    if (st.FarmTrips < 3 && Farming.SeedChest(st, center, radius, c => !Skipped(st, c) && Brain.CanReach(me, c.transform.position)) is Container chest)
                    {
                        st.FarmTrips++;
                        return Ordered(st, New(Kind.Store, chest, Job.None), false);
                    }
                    if (Farming.Cultivator(me) == null) done = P("villagepleb_errands_need_cultivator");
                    else if (Farming.FindSpot(st, center, radius, out Farming.Crop seed, out Vector3 spot))
                    {
                        Task sow = New(Kind.Plant, Heightmap.FindHeightmap(spot), Job.None);
                        sow.Crop = seed;
                        sow.Pos = spot;
                        return Ordered(st, sow, false);
                    }
                    else done = Farming.CanSow(me) ? P("villagepleb_work_farm_sown") : P("villagepleb_work_farm_out_of_seeds");
                }
                else
                {
                    Pickable ripe = Farming.RipeNear(center, radius).Where(p => !Skipped(st, p)).OrderBy(p => Vector3.Distance(p.transform.position, me.transform.position)).FirstOrDefault();
                    if (ripe != null) return Ordered(st, New(Kind.Pick, ripe, Job.Forage), false);
                    done = P("villagepleb_work_farm_harvested");
                }
            }
            st.Farm = null;
            Talk.Tell(me, done);
            return false;
        }

        public static bool StartFarm(BrainState st, Errand what)
        {
            Order order = Companion.OrderOf(st.Body);
            if ((order == Order.Stay || order == Order.Guard) && !Companion.Write(st.Body, z => z.Set(Keys.Order, (int)Order.Follow))) return false;
            st.Area = null;
            st.PickQueue.Clear();
            st.RepairAt = null; st.ComeUntil = 0f; st.EmoteUntil = 0f; st.PendingEmote = null;
            if (st.Task != null && st.Task.Ordered) st.Task = null;
            st.Farm = what;
            st.FarmTrips = 0;
            st.CommandUntil = Time.time + 120f;
            return NextFarm(st);
        }

        private static bool HasRoom(Container chest, Humanoid h) =>
            chest.GetInventory().HaveEmptySlot() || h.GetInventory().GetAllItems().Any(i => !Keeps(h, i) && chest.GetInventory().CanAddItem(i, 1));

        public static void Store(BrainState st, Container chest, Func<ItemDrop.ItemData, bool> take = null)
        {
            Humanoid me = st.Body;
            if (chest == null || Containers.InUse(chest)) return;
            if (chest.m_checkGuardStone && !PrivateArea.CheckAccess(chest.transform.position, 0f, false)) { Skip(st, chest, P("villagepleb_work_skip_ward"), 5f); return; }
            if (!Containers.Take(chest)) return;
            Inventory mine = me.GetInventory(), its = chest.GetInventory();
            int put = 0, took = 0, tidied = 0;
            bool yours = Home.IdOn(chest) == 0L;
            bool stows = Stows(me);
            foreach (ItemDrop.ItemData item in mine.GetAllItems().Where(i => !Keeps(me, i)).OrderBy(i => i.m_stack).ToList())
            {
                if (item.m_stack <= 0 || !mine.ContainsItem(item)) continue;
                int n;
                if (!yours) n = !stows ? item.m_stack : IsStock(st, item) ? Mathf.Min(item.m_stack, StillWanted(st, item)) : 0;
                else if (!IsStock(st, item)) n = item.m_stack;
                else
                {
                    int carried = mine.GetAllItems().Where(x => x.m_shared.m_name == item.m_shared.m_name && !Keeps(me, x)).Sum(x => x.m_stack);
                    n = Mathf.Min(item.m_stack, carried - StillWanted(st, item));
                }
                if (MovePart(mine, its, item, n) > 0) put++;
            }
            Job jobs = JobsOf(me) | (st.Duty?.Jobs ?? Job.None);
            if (!yours)
            {
                foreach (ItemDrop.ItemData item in its.GetAllItems().ToList())
                {
                    if (!mine.HaveEmptySlot() && !mine.CanAddItem(item)) break;
                    if (Wants(me, item, jobs) || take != null && take(item)) { item.m_equipped = false; mine.MoveItemToThis(its, item); took++; }
                }
                if (stows)
                    foreach (ItemDrop.ItemData item in its.GetAllItems().OrderBy(i => i.m_stack).ToList())
                    {
                        if (mine.GetEmptySlots() <= 2 || Carry.Weight(me) > Carry.Max(me) * 0.8f) break;
                        int extra = IsStock(st, item) ? Stocked(me, item.m_shared.m_name) - StockCap(st, item) : item.m_stack;
                        if (item.m_shared.m_weight > 0f) extra = Mathf.Min(extra, Mathf.FloorToInt((Carry.Max(me) * 0.9f - Carry.Weight(me)) / item.m_shared.m_weight));
                        if (MovePart(its, mine, item, Mathf.Min(item.m_stack, extra)) > 0) tidied++;
                    }
            }
            if (put + took + tidied > 0)
            {
                st.Remember(yours ? P("villagepleb_work_put_away_yours", put) : tidied > 0 ? P("villagepleb_work_at_its_chest_tidied", put, took, tidied) : P("villagepleb_work_at_its_chest", put, took));
                VillagePlebsPlugin.Instance?.Note($"{Companion.NameOf(me)} put away {put}, took {took}, took out {tidied} at {(yours ? "your" : "its")} chest {chest.transform.position:F0}");
            }
            if (tidied > 0) st.NextDeliver = 0f;
            if (yours) Armory.TakeFrom(st, chest);
        }

        private static bool Wants(Humanoid me, ItemDrop.ItemData item, Job jobs)
        {
            var type = item.m_shared.m_itemType;
            List<ItemDrop.ItemData> have = me.GetInventory().GetAllItems();
            if (type == ItemDrop.ItemData.ItemType.Helmet || type == ItemDrop.ItemData.ItemType.Chest || type == ItemDrop.ItemData.ItemType.Legs || type == ItemDrop.ItemData.ItemType.Shoulder)
                return item.GetArmor() > (have.Where(i => i.m_shared.m_itemType == type).Select(i => i.GetArmor()).DefaultIfEmpty(0f).Max()) + 0.5f;
            if (type == ItemDrop.ItemData.ItemType.Shield)
                return item.m_shared.m_blockPower > have.Where(i => i.m_shared.m_itemType == type).Select(i => i.m_shared.m_blockPower).DefaultIfEmpty(0f).Max() + 0.5f;
            if (Companion.IsRanged(item)) return Companion.BestRanged(me) == null && have.Any(a => a.m_shared.m_ammoType == item.m_shared.m_ammoType && a.m_shared.m_itemType == ItemDrop.ItemData.ItemType.Ammo);
            if (item.IsWeapon() && !IsTool(item) && Companion.Useful(me, item))
                return item.GetDamage().GetTotalDamage() > (Companion.BestMelee(me)?.GetDamage().GetTotalDamage() ?? 0f) + 1f;
            if (type == ItemDrop.ItemData.ItemType.Ammo)
            {
                ItemDrop.ItemData bow = Companion.BestRanged(me) ?? have.FirstOrDefault(Companion.IsRanged);
                return bow != null && item.m_shared.m_ammoType == bow.m_shared.m_ammoType && have.Where(a => a.m_shared.m_name == item.m_shared.m_name).Sum(a => a.m_stack) < 40;
            }
            if (Food.IsFood(item)) return have.Where(Food.IsFood).Sum(f => f.m_stack) < 10;
            if (Gear.WantsFood(me, item)) return true;
            if (IsCookable(item)) return have.Where(Food.IsFood).Sum(f => f.m_stack) < 10 && have.Where(IsCookable).Sum(f => f.m_stack) < 10;
            if (type == ItemDrop.ItemData.ItemType.Consumable && Companion.Useful(me, item)) return Companion.HealingPotions(me).Sum(p => p.m_stack) < 3;
            if ((jobs & Job.Wood) != 0 && item.m_shared.m_damages.m_chop > 0f && item.IsWeapon() && Axe(me) == null) return true;
            if (Farming.Wants(me, item)) return true;
            if ((jobs & (Job.Stone | Job.Ore)) != 0 && item.m_shared.m_damages.m_pickaxe > 0f && Pickaxe(me) == null) return true;
            return false;
        }
    }

    internal static class Upgrades
    {
        public static List<CraftingStation> StationsNear(Vector3 center, float range) =>
            (GameFields.Stations() ?? new List<CraftingStation>()).Where(s => s != null && !s.m_upgrader && Vector3.Distance(s.transform.position, center) < range).ToList();

        public static IEnumerable<Piece.Requirement> Needs(Recipe r) =>
            r == null ? Enumerable.Empty<Piece.Requirement>() : r.m_resources.Where(q => q.m_resItem != null && !q.m_upgraderResource);

        public static bool Upgradable(ItemDrop.ItemData i)
        {
            var t = i.m_shared.m_itemType;
            bool gear = i.IsWeapon() || Work.IsTool(i) || t == ItemDrop.ItemData.ItemType.Shield || t == ItemDrop.ItemData.ItemType.Helmet || t == ItemDrop.ItemData.ItemType.Chest
                        || t == ItemDrop.ItemData.ItemType.Legs || t == ItemDrop.ItemData.ItemType.Shoulder;
            return gear && i.m_quality < i.m_shared.m_maxQuality;
        }

        internal static List<Container> ChestsNear(Humanoid me, Vector3 at) => Home.Chests(me).Where(c => c != null && !Containers.InUse(c) && Vector3.Distance(c.transform.position, at) < 25f).ToList();

        private static int Have(Humanoid me, List<Container> chests, string name) => me.GetInventory().CountItems(name) + chests.Sum(c => c.GetInventory().CountItems(name));

        public static KeyValuePair<ItemDrop.ItemData, CraftingStation>? Find(Humanoid me, Vector3 center, float radius)
        {
            List<CraftingStation> stations = StationsNear(center, radius + 10f);
            if (stations.Count == 0) return null;
            foreach (ItemDrop.ItemData item in me.GetInventory().GetAllItems().Where(Upgradable).OrderByDescending(i => me.IsItemEquiped(i)))
            {
                Recipe recipe = ObjectDB.instance?.GetRecipe(item);
                if (recipe == null || recipe.m_craftingStation == null) continue;
                int next = item.m_quality + 1;
                CraftingStation station = stations.FirstOrDefault(s => s.m_name == recipe.m_craftingStation.m_name && s.GetLevel() >= recipe.GetRequiredStationLevel(next));
                if (station == null) continue;
                List<Container> chests = ChestsNear(me, station.transform.position);
                if (Upgrades.Needs(recipe).All(r => r.m_resItem == null || Have(me, chests, r.m_resItem.m_itemData.m_shared.m_name) >= r.GetAmount(next)))
                    return new KeyValuePair<ItemDrop.ItemData, CraftingStation>(item, station);
            }
            return null;
        }

        public static KeyValuePair<Recipe, CraftingStation>? FindCraft(Humanoid me, Vector3 center, float radius)
        {
            if (ObjectDB.instance == null) return null;
            List<CraftingStation> stations = StationsNear(center, radius + 10f);
            foreach (Recipe r in ObjectDB.instance.m_recipes)
            {
                if (r == null || !r.m_enabled || r.m_item == null) continue;
                if (!WorthMaking(me, r.m_item.m_itemData)) continue;
                CraftingStation station = r.m_craftingStation == null ? null : stations.FirstOrDefault(s => s.m_name == r.m_craftingStation.m_name && s.GetLevel() >= Mathf.Max(1, r.m_minStationLevel));
                if (station == null && r.m_craftingStation != null) continue;
                List<Container> chests = ChestsNear(me, station != null ? station.transform.position : me.transform.position);
                if (Upgrades.Needs(r).All(q => q.m_resItem == null || Have(me, chests, q.m_resItem.m_itemData.m_shared.m_name) >= q.GetAmount(1)))
                    return new KeyValuePair<Recipe, CraftingStation>(r, station);
            }
            return null;
        }

        public static bool WorthMaking(Humanoid me, ItemDrop.ItemData made)
        {
            var have = me.GetInventory().GetAllItems();
            var t = made.m_shared.m_itemType;
            if (t == ItemDrop.ItemData.ItemType.Helmet || t == ItemDrop.ItemData.ItemType.Chest || t == ItemDrop.ItemData.ItemType.Legs || t == ItemDrop.ItemData.ItemType.Shoulder)
                return made.GetArmor() > have.Where(i => i.m_shared.m_itemType == t).Select(i => i.GetArmor()).DefaultIfEmpty(0f).Max() + 1f;
            if (t == ItemDrop.ItemData.ItemType.Shield)
                return made.m_shared.m_blockPower > have.Where(i => i.m_shared.m_itemType == t).Select(i => i.m_shared.m_blockPower).DefaultIfEmpty(0f).Max() + 1f;
            if (made.m_shared.m_damages.m_chop > 0f && made.IsWeapon() && made.m_shared.m_skillType == Skills.SkillType.Axes)
                return made.m_shared.m_toolTier > (Work.Axe(me)?.m_shared.m_toolTier ?? -1);
            if (made.m_shared.m_damages.m_pickaxe > 0f)
                return made.m_shared.m_toolTier > (Work.Pickaxe(me)?.m_shared.m_toolTier ?? -1);
            if (t == ItemDrop.ItemData.ItemType.Ammo)
            {
                ItemDrop.ItemData bow = have.FirstOrDefault(i => Companion.IsRanged(i) && !Companion.IsStaff(i));
                return bow != null && made.m_shared.m_ammoType == bow.m_shared.m_ammoType && have.Where(a => a.m_shared.m_ammoType == bow.m_shared.m_ammoType && a.m_shared.m_itemType == t).Sum(a => a.m_stack) < 20;
            }
            if (Companion.IsRanged(made) && !Companion.IsStaff(made)) return !have.Any(i => Companion.IsRanged(i) && !Companion.IsStaff(i));
            if (made.IsWeapon() && !Work.IsTool(made) && made.m_shared.m_itemType != ItemDrop.ItemData.ItemType.Torch && made.m_shared.m_skillType != Skills.SkillType.Unarmed && made.m_shared.m_attack != null && made.m_shared.m_attack.m_attackType != Attack.AttackType.Projectile)
                return made.GetDamage().GetTotalDamage() > (Companion.BestMelee(me)?.GetDamage().GetTotalDamage() ?? 0f) + 2f;
            return false;
        }

        public static string Craft(BrainState st, Recipe r, CraftingStation station, bool forGoal = false)
        {
            Humanoid me = st.Body;
            if (r == null || (!forGoal && !WorthMaking(me, r.m_item.m_itemData))) return null;
            if (!me.GetInventory().HaveEmptySlot()) return null;
            List<Container> chests = Containers.TakeAll(ChestsNear(me, station != null ? station.transform.position : me.transform.position));
            if (!Upgrades.Needs(r).All(q => q.m_resItem == null || Have(me, chests, q.m_resItem.m_itemData.m_shared.m_name) >= q.GetAmount(1))) return null;
            Pay(me, chests, r, 1);
            me.GetInventory().AddItem(r.m_item.gameObject.name, Mathf.Max(1, r.m_amount), 1, 0, 0L, Companion.NameOf(me), false);
            if (station != null) station.m_craftItemEffects.Create(station.transform.position, Quaternion.identity);
            string token = r.m_item.m_itemData.m_shared.m_name, what = Localization.instance.Localize(token);
            st.Remember(P("villagepleb_work_made_a", token));
            Skill.Raise(me, station != null && station.m_craftingSkill != Skills.SkillType.None ? station.m_craftingSkill : Skills.SkillType.Crafting, 1f);
            st.NextGear = 0f;
            VillagePlebsPlugin.Instance?.Note($"{Companion.NameOf(me)} made a {what} at {(station != null ? station.transform.position : me.transform.position):F0}");
            if (forGoal) Talk.Mention(me, P("villagepleb_work_made_for_goal", Mathf.Max(1, r.m_amount), Low(token), Work.Aim(st)?.What ?? ""));
            else Talk.Tell(me, P(st.Goal != null && st.Goal.Recipe == r ? "villagepleb_work_made_my" : "villagepleb_work_made_a_say", Low(token)));
            if (!forGoal) Journal.Made(me, token);
            return forGoal ? P("villagepleb_work_made_n", Mathf.Max(1, r.m_amount), Low(token)) : P("villagepleb_work_made_a", Low(token));
        }

        private static void Pay(Humanoid me, List<Container> chests, Recipe r, int quality)
        {
            foreach (Piece.Requirement q in Upgrades.Needs(r))
            {
                if (q.m_resItem == null) continue;
                string name = q.m_resItem.m_itemData.m_shared.m_name;
                int left = q.GetAmount(quality);
                int fromBag = Mathf.Min(left, me.GetInventory().CountItems(name));
                if (fromBag > 0) { me.GetInventory().RemoveItem(name, fromBag); left -= fromBag; }
                foreach (Container chest in chests)
                {
                    if (left <= 0) break;
                    int take = Mathf.Min(left, chest.GetInventory().CountItems(name));
                    if (take <= 0) continue;
                    chest.GetInventory().RemoveItem(name, take);
                    left -= take;
                }
            }
        }

        public static string Do(BrainState st, ItemDrop.ItemData item, CraftingStation station)
        {
            Humanoid me = st.Body;
            Recipe recipe = ObjectDB.instance?.GetRecipe(item);
            if (recipe == null || !me.GetInventory().ContainsItem(item) || item.m_quality >= item.m_shared.m_maxQuality) return null;
            int next = item.m_quality + 1;
            List<Container> chests = Containers.TakeAll(ChestsNear(me, station.transform.position));
            if (!Upgrades.Needs(recipe).All(r => r.m_resItem == null || Have(me, chests, r.m_resItem.m_itemData.m_shared.m_name) >= r.GetAmount(next))) return null;
            foreach (Piece.Requirement r in Upgrades.Needs(recipe))
            {
                if (r.m_resItem == null) continue;
                string name = r.m_resItem.m_itemData.m_shared.m_name;
                int left = r.GetAmount(next);
                int fromBag = Mathf.Min(left, me.GetInventory().CountItems(name));
                if (fromBag > 0) { me.GetInventory().RemoveItem(name, fromBag); left -= fromBag; }
                foreach (Container chest in chests)
                {
                    if (left <= 0) break;
                    int take = Mathf.Min(left, chest.GetInventory().CountItems(name));
                    if (take <= 0) continue;
                    chest.GetInventory().RemoveItem(name, take);
                    left -= take;
                }
            }
            item.m_quality = next;
            item.m_durability = item.GetMaxDurability();
            Companion.SaveBag(me);
            station.m_craftItemEffects.Create(station.transform.position, Quaternion.identity);
            string token = item.m_shared.m_name, what = Localization.instance.Localize(token);
            st.Remember(P("villagepleb_work_upgraded_its", token, next));
            Skill.Raise(me, station.m_craftingSkill != Skills.SkillType.None ? station.m_craftingSkill : Skills.SkillType.Crafting, 1f);
            VillagePlebsPlugin.Instance?.Note($"{Companion.NameOf(me)} upgraded its {what} to level {next} at {station.transform.position:F0}");
            Talk.Tell(me, P("villagepleb_work_upgraded_say", token, next));
            Journal.Upgraded(me, token, next);
            Portraits.Dirty(me);
            return P("villagepleb_work_upgraded_its", Low(token), next);
        }
    }
}
