using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using UnityEngine;
using static VillagePlebs.Words;

namespace VillagePlebs
{
    public enum Tactic { Attack, DefendPlayer, BackOff, Retreat, Flee }

    internal class Decision
    {
        public Tactic Action = Tactic.Attack;
        public Character Target;
        public bool Ranged;
        public bool Drink;
        public float Confidence = 1f;
        public string Note = "";
        public float Time;

        public string Describe(Func<Character, string> label)
        {
            bool bow = Ranged && (Action == Tactic.Attack || Action == Tactic.DefendPlayer);
            string what = Action switch
            {
                Tactic.Attack => Target != null ? P(bow ? "villagepleb_brain_do_attack_bow" : "villagepleb_brain_do_attack", label(Target))
                                                : P(bow ? "villagepleb_brain_do_attack_nearest_bow" : "villagepleb_brain_do_attack_nearest"),
                Tactic.DefendPlayer => P(bow ? "villagepleb_brain_do_defend_bow" : "villagepleb_brain_do_defend"),
                Tactic.BackOff => P("villagepleb_brain_do_back_off"),
                Tactic.Retreat => P("villagepleb_brain_do_fall_back"),
                _ => P("villagepleb_brain_do_flee"),
            };
            if (Drink) what = P("villagepleb_brain_do_drink", what);
            return what;
        }
    }

    internal class BrainState
    {
        public Humanoid Body;
        public MonsterAI Ai;
        public Decision Current = new Decision();
        public readonly List<Character> Enemies = new List<Character>();
        public readonly Dictionary<Character, string> Labels = new Dictionary<Character, string>();
        public readonly List<string> History = new List<string>();
        public bool InCombat;
        public int Fights, Potions;
        public float FightStart, Taken, Dealt, LastHealth;
        public int FightKills, FightDecisions;
        public bool Sitting, IdleReady;
        public Chair SitChair, IdleChair;
        public Fireplace IdleFire;
        public IdlePlan IdlePlan;
        public float SitKeep, StillSince, AwaySince, NextGreetLook, ComeUntil, EmoteUntil, PendingEmoteAt, YouStillSince, IdleUntil, IdleSince, NextLookAround;
        public string PendingEmote;
        public Character EmoteFace;
        public int YourEmoteId = int.MinValue;
        public Vector3 YouWere, IdleAt, LookAroundAt;
        public TombStone GraveOrdered, GraveOn;
        public readonly List<ItemDrop> PickQueue = new List<ItemDrop>();
        private Work.Area _area;
        public Work.Area Area { get => _area; set { _area = value; Work.SaveArea(this); } }
        public List<Vector3> Path; public Vector3? PathTo; public int PathIndex;
        public float PathUntil, NextPathTry, PathProgressAt, PathBest;
        public readonly Dictionary<Door, float> OpenedDoors = new Dictionary<Door, float>();
        public bool SwingMissed;
        public float NextRefillLook, NextArmoryLook, BedSince;
        public Vector3 BedFrom;
        public float NextDeliver, NextTidy, NextStockLook;
        public Dictionary<string, int> StockCaps;
        public float MissionSince, MissionBest, MissionAskSince, MissionReadySince;
        public readonly Dictionary<string, float> DroppedUntil = new Dictionary<string, float>();
        public List<Goal> Candidates = new List<Goal>();
        public float WokeAt = -999f;
        public Work.Task Task;
        public CraftingStation RepairAt;
        public float NextRepairLook, RepairSince;
        public bool RepairAll, RollCredited;
        public Ship Riding;
        public Chair Seat;
        public Vector3 DeckSpot;
        public float RideLastSeen;
        public ItemDrop.ItemData WorkTool;
        public string WorkNote;
        public Vector3 WorkSpot;
        public float NextWorkLook, NextDoorLook, NextUpgradeLook, YieldUntil;
        public float NextApartLook;
        public float NextBedLook;
        public int RanFrame; public float RanAt;
        public bool MadeAsOwner;
        public Humanoid ApartFrom;
        public Vector3 YieldTo;
        public Player YieldFrom;
        public bool CaughtUp;
        public float NextStamp, NextBagSave, NextGraveLook;
        public readonly List<Loot.Spot> LootSpots = new List<Loot.Spot>();
        public readonly List<KeyValuePair<ItemDrop, float>> LootDrops = new List<KeyValuePair<ItemDrop, float>>();
        public Vector3 CookedAt;
        public float NextPassBy;
        public Brain.DoorPlan Door;
        public Character StallOn;
        public float StallSince, StallHealth;
        public readonly Dictionary<Character, float> LeaveAlone = new Dictionary<Character, float>();
        public float BlockUntil;
        public float NextBanterLook;
        public bool SaidNight;
        public string LastLand = "", LastBoss = "";
        public bool Asleep;
        public Bed SleepBed;
        public Vagon Cart;
        public float CartSince, CartAsked;
        public float ClearUntil;
        public Vector3 ClearTo;
        public int UnstickStage;
        public float SideStepUntil, NextLoanLook;
        public Vector3 SideStepTo;
        public Character Focus;
        public float FocusUntil, CommandUntil;
        public float InvulnUntil, NextMeadLook, RunBackUntil;
        public bool HitAndRun;
        public int Blows, FightDodges, ParriesAtStart, BlocksAtStart, PotionsAtStart;
        public readonly HashSet<string> FightFoes = new HashSet<string>();
        public readonly List<string> FightLog = new List<string>();
        public float EvadeUntil, NextEvade, EvadeSide, CounterUntil;
        public Vector3 EvadeTo;
        public int Parries, Blocks;
        public Character Blocker;
        public float NextSnapshot, LastHurtAt, GraveSince, GraveBest = float.MaxValue;
        public string LastHurtBy = "";
        public bool Hungry, Weak;
        public Duties.Entry Duty;
        public bool OnDuties;
        public readonly Dictionary<string, float> Unfindable = new Dictionary<string, float>();
        public Goal Goal;
        public string GoalSaid;
        public float NextTripLook, TripUntil;
        public float NextFireLook, NextMendLook, NextMend;
        public float NextReadLook;
        public float ManualOrderAt = -999f, HomeSince;
        public bool Outing, MasterWasHome;
        public Doing Doing;
        public bool DefendOnly, Outmatched, Helping;
        public Style AutoStyle = Style.Balanced;
        public string AutoNote = "";
        public float NextFoodLook, NextNeedLook, MasterGoneSince, TripStart;
        public readonly HashSet<string> Wanted = new HashSet<string>();
        public readonly Dictionary<int, float> Skipped = new Dictionary<int, float>();
        public readonly Dictionary<string, int> Gathered = new Dictionary<string, int>();
        public float NextEnemyScan, NextGear, NextAsk, LastAsk, HealthAtAsk = 1f;
        public float NextPlant, NextToolLook, NextMissionCheck;
        public Work.Task Then;
        public Job Ticked;
        public Errand? Farm;
        public int FarmTrips;
        public float NextStuckCheck, StuckDistance = float.MaxValue;
        public Vector3 StuckFrom;
        public int StuckFor;
        public int EnemyCountAtAsk;
        public string Status = "";
        public float NextStatusSave;

        public string Label(Character c)
        {
            if (c == null) return "?";
            if (Labels.TryGetValue(c, out string l)) return l;
            string name = c.m_name;
            int n = 1;
            while (Labels.ContainsValue($"{name} {n}")) n++;
            return Labels[c] = $"{name} {n}";
        }

        public void Remember(string line)
        {
            Activity.Log(Body, "» " + line);
            History.Insert(0, line);
            if (History.Count > 8) History.RemoveAt(History.Count - 1);
        }
    }

    internal static class Brain
    {
        private static readonly Dictionary<Humanoid, BrainState> States = Cleanup.Track(new Dictionary<Humanoid, BrainState>());

        private static readonly Func<BaseAI, float, Vector3, float, bool, bool> MoveToRaw = AccessTools.MethodDelegate<Func<BaseAI, float, Vector3, float, bool, bool>>(AccessTools.Method(typeof(BaseAI), "MoveTo"));
        private static readonly Func<BaseAI, Vector3, bool> HavePath = AccessTools.MethodDelegate<Func<BaseAI, Vector3, bool>>(AccessTools.Method(typeof(BaseAI), "HavePath"));
        private static readonly Action<BaseAI, Vector3> LookAt =AccessTools.MethodDelegate<Action<BaseAI, Vector3>>(AccessTools.Method(typeof(BaseAI), "LookAt"));
        private static readonly Action<BaseAI, float> Regenerate = AccessTools.MethodDelegate<Action<BaseAI, float>>(AccessTools.Method(typeof(BaseAI), "UpdateRegeneration"));
        private static readonly AccessTools.FieldRef<BaseAI, float> TimeSinceHurt = AccessTools.FieldRefAccess<BaseAI, float>("m_timeSinceHurt");
        private static readonly AccessTools.FieldRef<Character, bool> Blocking = AccessTools.FieldRefAccess<Character, bool>("m_blocking");
        private static readonly AccessTools.FieldRef<Humanoid, float> DrawTime = AccessTools.FieldRefAccess<Humanoid, float>("m_attackDrawTime");

        private static bool MoveTo(BaseAI ai, float dt, Vector3 point, float dist, bool run)
        {
            Character c = ai.GetComponent<Character>();
            bool canRun = run && Stamina.CanRun(c);
            bool result = MoveToRaw(ai, dt, point, dist, canRun);
            Vector3 pos = c.transform.position;
            if (Utils.DistanceXZ(point, pos) <= Mathf.Max(dist, 0.75f) || c.GetMoveDir().sqrMagnitude > 0.01f) return result;
            Humanoid h = c as Humanoid;
            BrainState st = h != null ? Get(h) : null;
            if (st != null)
            {
                DoorPlan plan = st.Door != null && Time.time < st.Door.Until && Vector3.Distance(st.Door.Target, point) < 4f ? st.Door : st.Door = PlanDoor(ai, pos, point);
                if (plan != null)
                {
                    if (!plan.Through)
                    {
                        if (Utils.DistanceXZ(plan.Near, pos) > 1.2f) { MoveToRaw(ai, dt, plan.Near, 0.8f, canRun); if (c.GetMoveDir().sqrMagnitude > 0.01f) return false; }
                        if ((Companion.Zdo(plan.Door)?.GetInt(ZDOVars.s_state, 0) ?? 0) == 0 && plan.Door.m_keyItem == null) { plan.Door.Interact(h, false, false); Opened(st, plan.Door); }
                        plan.Through = true;
                    }
                    if (Utils.DistanceXZ(plan.Far, pos) > 1f)
                    {
                        Vector3 step = plan.Far - pos;
                        step.y = 0f;
                        c.SetMoveDir(step.normalized);
                        c.SetLookDir(step.normalized, 0f);
                        return false;
                    }
                    st.Door = plan.Next;
                    return false;
                }
            }
            if (st != null && Wayfinding.Follow(st, h, point, canRun)) return false;
            if (h != null && Steer.HazardsNear(h) > 0 && !ClearLine(h, pos, point)) return result;
            Vector3 to = point;
            Vector3 dir = to - pos;
            dir.y = 0f;
            if (dir.sqrMagnitude < 0.01f) return result;
            dir.Normalize();
            c.SetMoveDir(dir);
            c.SetRun(canRun);
            c.SetLookDir(dir, 0f);
            return false;
        }

        private static bool ClearLine(Humanoid h, Vector3 from, Vector3 to)
        {
            float len = Utils.DistanceXZ(from, to);
            if (len > 8f) return false;
            for (float t = 0.5f; t < len + 0.25f; t += 0.5f)
                if (!Steer.Safe(Vector3.Lerp(from, to, Mathf.Min(1f, t / len)), h)) return false;
            return true;
        }

        internal static bool CanReach(Humanoid me, Vector3 to, float near = 2.5f)
        {
            BaseAI ai = me.GetComponent<BaseAI>();
            Pathfinding pf = Pathfinding.instance;
            if (ai == null || pf == null) return true;
            if (!pf.FindValidPoint(out _, me.transform.position, 2f, ai.m_pathAgentType)) return Wayfinding.CanReach(Get(me), me, to);
            if (!pf.FindValidPoint(out Vector3 goal, to, near, ai.m_pathAgentType)) return false;
            return pf.HavePath(me.transform.position, goal, ai.m_pathAgentType) || PlanDoor(ai, me.transform.position, to) != null;
        }

        internal class DoorPlan { public Door Door; public Vector3 Near, Far, Target; public float Until; public bool Through; public DoorPlan Next; }

        private static DoorPlan PlanDoor(BaseAI ai, Vector3 from, Vector3 to)
        {
            Pathfinding pf = Pathfinding.instance;
            if (pf == null) return null;
            Pathfinding.AgentType agent = ai.m_pathAgentType;
            if (!pf.FindValidPoint(out Vector3 goal, to, 2.5f, agent)) goal = to;
            var doors = new List<KeyValuePair<Door, Vector3[]>>();
            var seen = new HashSet<Door>();
            int n = Physics.OverlapSphereNonAlloc(from, 40f, DoorHits, PieceMask);
            for (int i = 0; i < n; i++)
            {
                Door door = DoorHits[i].GetComponentInParent<Door>();
                if (door == null || door.m_keyItem != null || !seen.Add(door)) continue;
                if (!pf.FindValidPoint(out Vector3 a, door.transform.position + door.transform.forward * 1.6f, 1.5f, agent)) continue;
                if (!pf.FindValidPoint(out Vector3 b, door.transform.position - door.transform.forward * 1.6f, 1.5f, agent)) continue;
                doors.Add(new KeyValuePair<Door, Vector3[]>(door, new[] { a, b }));
            }
            doors.Sort((x, y) => Vector3.Distance(x.Key.transform.position, from).CompareTo(Vector3.Distance(y.Key.transform.position, from)));
            if (doors.Count > 10) doors.RemoveRange(10, doors.Count - 10);

            DoorPlan best = null;
            float bestCost = float.MaxValue;
            var firsts = new List<DoorPlan>();
            foreach (var d in doors)
            {
                Vector3 near = Side(d.Value, from, true), far = Side(d.Value, from, false);
                if (!pf.HavePath(from, near, agent)) continue;
                var step = new DoorPlan { Door = d.Key, Near = near, Far = far, Target = to, Until = Time.time + 20f };
                firsts.Add(step);
                float cost = Vector3.Distance(from, near) + Vector3.Distance(far, goal);
                if (cost < bestCost && pf.HavePath(far, goal, agent)) { bestCost = cost; best = step; }
            }
            if (best != null) return best;

            foreach (DoorPlan first in firsts)
                foreach (var d in doors)
                {
                    if (d.Key == first.Door) continue;
                    Vector3 near = Side(d.Value, first.Far, true), far = Side(d.Value, first.Far, false);
                    float cost = Vector3.Distance(from, first.Near) + Vector3.Distance(first.Far, near) + Vector3.Distance(far, goal);
                    if (cost >= bestCost || !pf.HavePath(first.Far, near, agent) || !pf.HavePath(far, goal, agent)) continue;
                    bestCost = cost;
                    best = new DoorPlan { Door = first.Door, Near = first.Near, Far = first.Far, Target = to, Until = Time.time + 30f,
                                          Next = new DoorPlan { Door = d.Key, Near = near, Far = far, Target = to, Until = Time.time + 30f } };
                }
            return best;
        }

        private static Vector3 Side(Vector3[] sides, Vector3 from, bool near) =>
            (Vector3.Distance(sides[0], from) < Vector3.Distance(sides[1], from)) == near ? sides[0] : sides[1];

        private static readonly Collider[] DoorHits = new Collider[128];

        private static Door DoorOnTheWay(Vector3 from, Vector3 to)
        {
            Vector3 way = to - from;
            way.y = 0f;
            Door best = null;
            float bestDist = float.MaxValue;
            int n = Physics.OverlapSphereNonAlloc(from, 12f, DoorHits, PieceMask);
            for (int i = 0; i < n; i++)
            {
                Door door = DoorHits[i].GetComponentInParent<Door>();
                if (door == null || door.m_keyItem != null) continue;
                Vector3 toDoor = door.transform.position - from;
                toDoor.y = 0f;
                float d = toDoor.magnitude;
                if (d < 1f || d >= bestDist || Vector3.Angle(way, toDoor) > 90f) continue;
                if ((Companion.Zdo(door)?.GetInt(ZDOVars.s_state, 0) ?? 0) != 0) continue;
                best = door; bestDist = d;
            }
            return best;
        }

        public static void Forget()
        {
            foreach (BrainState st in States.Values) if (st.Body != null) Blocking(st.Body) = false;
            States.Clear();
        }

        public static void Forget(Humanoid h) { if (h != null) States.Remove(h); }

        public static BrainState Get(Humanoid h)
        {
            if (h == null) return null;
            if (!States.TryGetValue(h, out BrainState st))
            {
                foreach (Humanoid gone in States.Keys.Where(k => k == null).ToList()) States.Remove(gone);
                States[h] = st = new BrainState { Body = h, Ai = h.GetComponent<MonsterAI>(), MadeAsOwner = h.GetComponent<ZNetView>() is ZNetView v && v.IsValid() && v.IsOwner() };
            }
            return st;
        }

        public static BrainState Claim(Humanoid c, out string why)
        {
            why = null;
            ZNetView v = c != null ? c.GetComponent<ZNetView>() : null;
            if (v == null || !v.IsValid()) return null;
            if (!v.IsOwner())
            {
                Player me = Player.m_localPlayer;
                long master = Companion.MasterId(c);
                Player near = me != null && master != me.GetPlayerID()
                    ? Player.GetAllPlayers().FirstOrDefault(p => p != null && p.GetPlayerID() == master && Vector3.Distance(p.transform.position, c.transform.position) < 64f) : null;
                if (near != null) { why = P("villagepleb_brain_with_its_player", Companion.NameOf(c), near.GetPlayerName()); return null; }
                if (!Companion.Write(c, _ => { })) { why = P("villagepleb_companion_gear_open"); return null; }
            }
            BrainState st = Get(c);
            if (Time.frameCount - st.RanFrame > 30 && Time.time - st.RanAt > 1f) st = TookOver(c);
            st.RanFrame = Time.frameCount; st.RanAt = Time.time;
            return st;
        }

        private static BrainState TookOver(Humanoid h)
        {
            bool fresh = States.TryGetValue(h, out BrainState had) && had.RanFrame == 0 && had.MadeAsOwner;
            if (!fresh)
            {
                Forget(h);
                Food.Reload(h);
                Stamina.Reload(h);
                Eitr.Reload(h);
            }
            BrainState st = Get(h);
            Work.RestoreArea(st);
            if (h.GetComponent<Rigidbody>() is Rigidbody body && body.isKinematic && h.GetComponentInParent<Ship>() == null && !h.IsAttached())
            { body.isKinematic = false; body.detectCollisions = true; }
            st.Cart = UnityEngine.Object.FindObjectsByType<Vagon>(FindObjectsSortMode.None).FirstOrDefault(cart => cart != null && cart.IsAttached(h));
            if (Companion.Zdo(h) is ZDO z && z.GetBool(ZDOVars.s_dodgeinv, false)) z.Set(ZDOVars.s_dodgeinv, false);
            if (h.GetComponent<Container>() is Container gear) Container_Load_Companion.Fresh(gear);
            return st;
        }

        public static bool Update(MonsterAI ai, float dt)
        {
            ZNetView view = ai.GetComponent<ZNetView>();
            if (view == null || !view.IsValid() || !view.IsOwner()) return false;
            Humanoid body = ai.GetComponent<Humanoid>();
            if (StarLevels.RemoveStray(body)) return true;
            BrainState ran = Get(body);
            if (Time.frameCount - ran.RanFrame > 30 && Time.time - ran.RanAt > 1f) ran = TookOver(body);
            ran.RanFrame = Time.frameCount; ran.RanAt = Time.time;
            Cleanup.Tick();
            StarLevels.Exempt(body);
            TimeSinceHurt(ai) += dt;
            Stamina.Tick(body, dt);
            Tactics.Tick(Get(body));
            Eitr.Tick(body, dt);
            Weather.Tick(body);
            Apart(body, dt);

            Humanoid me = ai.GetComponent<Humanoid>();
            BrainState st = Get(me);
            Player master = Companion.Master(me);
            Container gear = me.GetComponent<Container>();

            if (!st.CaughtUp) CatchUp.OnArrive(st);
            if (Companion.OrderOf(me) == Order.Gather && Time.time >= st.NextStamp) { st.NextStamp = Time.time + 30f; CatchUp.Stamp(me); }
            SaveStatus(st);
            Food.Tick(me, st);
            EpicMmo.Tick(me);
            Activity.Tick(st);
            Following.Tick(st, master);
            Banter.Tick(st, master);
            Idle.Tick(st, master);
            if (Time.time >= st.NextBagSave) { st.NextBagSave = Time.time + 30f; Companion.SaveBag(me); }
            if (Ride.Tick(st, master)) return true;
            Loot.PassBy(st);
            if (Time.time >= st.NextMissionCheck) { st.NextMissionCheck = Time.time + 1f; Missions.CheckDone(st); }
            if (Time.time >= st.NextGear) { st.NextGear = Time.time + 0.5f; Companion.Maintain(me, st.Current.Ranged, st.InCombat ? null : st.WorkTool, st.InCombat ? st.Current.Target : null); }
            if (gear != null && Containers.InUse(gear)) { ai.StopMoving(); Blocking(me) = false; SetStatus(st, P("villagepleb_brain_status_gear_open")); return true; }
            if (!st.Asleep && Carts.Tick(st, (p, dd, run) => MoveTo(ai, dt, p, dd, run), () => ai.StopMoving())) return true;

            if (Time.time >= st.NextEnemyScan) { st.NextEnemyScan = Time.time + 0.25f; ScanEnemies(st, master); }
            foreach (Character e in st.Enemies) if (e != null && e.IsDead()) Loot.AddSpot(st, e.transform.position);
            st.Enemies.RemoveAll(e => e == null || e.IsDead());

            if (Time.time >= st.NextDoorLook) { st.NextDoorLook = Time.time + 0.4f; OpenDoorAhead(me); CloseBehind(st); }
            if (Companion.OrderOf(me) != Order.Gather && !(Companion.OrderOf(me) == Order.Follow && (st.Helping || Time.time < st.CommandUntil || st.Area != null))) { st.Task = null; st.WorkTool = null; }

            if (st.Enemies.Count > 0 && st.Asleep) Sleep.Wake(st, P("villagepleb_brain_wake_something_came"));
            Character boss = st.Enemies.FirstOrDefault(e => e != null && e.IsBoss());
            if (boss != null && st.LastBoss != boss.m_name) { st.LastBoss = boss.m_name; Banter.BossSeen(me, boss.m_name); }
            if (st.Enemies.Count == 0)
            {
                if (st.InCombat)
                {
                    st.InCombat = false; st.Labels.Clear(); Summarise(st, false);
                    if (me.GetHealthPercentage() < 0.3f) Banter.CloseFight(me);
                    else if (st.FightKills >= 3) Idle.Queue(st, "cheer", 1f);
                }
                Blocking(me) = false;
                Peaceful(st, master, dt);
                return true;
            }

            if (!st.InCombat)
            {
                st.FightFoes.Clear(); st.FightDodges = 0; st.ParriesAtStart = st.Parries; st.BlocksAtStart = st.Blocks; st.PotionsAtStart = st.Potions;
                st.InCombat = true; st.NextAsk = 0f; st.Fights++;
                st.FightStart = Time.time; st.Taken = st.Dealt = 0f; st.FightKills = st.FightDecisions = 0; st.LastHealth = me.GetHealth();
            }
            foreach (Character e in st.Enemies)
            {
                if (e == null) continue;
                string foe = e.m_name;
                if (st.FightFoes.Add(foe) && Tactics.Feared(me, e))
                    Banter.Say(me, "feared:" + foe, 10f, P("villagepleb_brain_feared_1", foe), P("villagepleb_brain_feared_2", Low(foe)), P("villagepleb_brain_feared_3", Low(foe)));
            }
            float hp = me.GetHealth();
            if (hp < st.LastHealth) st.Taken += st.LastHealth - hp;
            st.LastHealth = hp;
            MaybeDecide(st, master);
            Fight(st, master, dt);
            return true;
        }

        private static void Peaceful(BrainState st, Player master, float dt)
        {
            Humanoid me = st.Body;
            if (st.Asleep && Companion.OrderOf(me) != Order.Gather) Sleep.Wake(st, P("villagepleb_brain_wake_called"));
            if (MakeWay(st, dt)) return;
            bool onOrder = Work.OnOrder(st);
            if (!onOrder && Idle.Emoting(st, p => LookAt(st.Ai, p))) return;
            if (Idle.Come(st, master, (p, dd, run) => MoveTo(st.Ai, dt, p, dd, run), () => st.Ai.StopMoving(), p => LookAt(st.Ai, p))) return;
            if (!onOrder && Loot.Tick(st, (p, dd, run) => MoveTo(st.Ai, dt, p, dd, run))) return;
            if (Grave.Tick(st, (p, dd, run) => MoveTo(st.Ai, dt, p, dd, run), () => st.Ai.StopMoving())) return;
            if (!onOrder && Repair.Tick(st, (p, dd, run) => MoveTo(st.Ai, dt, p, dd, run), () => st.Ai.StopMoving())) return;
            Needs.Tick(st, master);
            switch (Companion.OrderOf(me))
            {
                case Order.Stay:
                    st.Ai.StopMoving();
                    Idle.Waiting(st);
                    SetStatus(st, P(st.Sitting ? "villagepleb_brain_status_sitting" : "villagepleb_brain_status_staying"));
                    break;
                case Order.Gather:
                    bool sent = st.Task != null && st.Task.Ordered && Time.time < st.CommandUntil;
                    if (sent && st.Asleep) Sleep.Wake(st, P("villagepleb_brain_wake_errand"));
                    if (!sent && Sleep.Tick(st, (p, dd, run) => MoveTo(st.Ai, dt, p, dd, run), () => st.Ai.StopMoving())) break;
                    Work.Tick(st, master, dt, (p, dd, run) => MoveTo(st.Ai, dt, p, dd, run), () => st.Ai.StopMoving(), p => LookAt(st.Ai, p));
                    break;
                case Order.Guard:
                    Vector3 post = Companion.Zdo(me).GetVec3(Keys.Post, me.transform.position);
                    if (Vector3.Distance(post, me.transform.position) > 2f) MoveTo(st.Ai, dt, post, 1f, Vector3.Distance(post, me.transform.position) > 8f);
                    else st.Ai.StopMoving();
                    SetStatus(st, P("villagepleb_brain_status_guarding"));
                    break;
                default:
                    if (master == null) { st.Ai.StopMoving(); { string friend = Companion.Zdo(me).GetString(Keys.MasterName, ""); SetStatus(st, string.IsNullOrEmpty(friend) ? P("villagepleb_brain_status_waiting_friend") : P("villagepleb_brain_status_waiting_for", friend)); } break; }
                    float d = Vector3.Distance(master.transform.position, me.transform.position);
                    if ((Time.time < st.CommandUntil || st.Area != null || st.Farm != null) && (st.Task != null || st.PickQueue.Count > 0 || st.Area != null || st.Farm != null || st.Then != null))
                    {
                        if (Work.RunOrdered(st, master, dt, (p, dd, run) => MoveTo(st.Ai, dt, p, dd, run), () => st.Ai.StopMoving(), p => LookAt(st.Ai, p))) break;
                        st.CommandUntil = 0f;
                    }
                    if (Companion.Chosen(me) == Style.Auto && (st.Doing == Doing.Mining || st.Doing == Doing.Chopping) && d < 25f
                        && Work.Help(st, master, dt, (p, dd, run) => MoveTo(st.Ai, dt, p, dd, run), () => st.Ai.StopMoving(), p => LookAt(st.Ai, p), st.Doing == Doing.Mining ? Job.Stone | Job.Ore : Job.Wood))
                        break;
                    if (d > 60f && !master.IsAttached() && master.IsOnGround() && st.Area == null) { TeleportBehind(me, master); break; }
                    if (Time.time < st.SideStepUntil) { MoveTo(st.Ai, dt, st.SideStepTo, 0.4f, false); break; }
                    if (d < 8f && st.Cart == null && Idle.WithYou(st, master, d, (p, dd, run) => MoveTo(st.Ai, dt, p, dd, run), p => LookAt(st.Ai, p))) break;
                    if (d > 3.5f)
                    {
                        MoveTo(st.Ai, dt, master.transform.position, 2.5f, d > 8f);
                        if (Stuck(st, master, d) && master.IsOnGround() && !master.IsAttached()) { Unstick(st, master); break; }
                    }
                    else { st.Ai.StopMoving(); st.StuckFor = 0; }
                    SetStatus(st, st.Cart != null && st.Cart.IsAttached(me) ? P("villagepleb_brain_status_following_cart", master.GetPlayerName())
                        : string.IsNullOrEmpty(st.AutoNote) || Companion.Chosen(me) != Style.Auto ? P("villagepleb_brain_status_following", master.GetPlayerName())
                        : P("villagepleb_brain_status_following_note", master.GetPlayerName(), st.AutoNote));
                    break;
            }
        }

        private static bool Stuck(BrainState st, Player master, float d)
        {
            if (Time.time < st.NextStuckCheck) return false;
            st.NextStuckCheck = Time.time + 1f;
            Vector3 here = st.Body.transform.position;
            bool noProgress = Vector3.Distance(here, st.StuckFrom) < 0.75f;
            st.StuckFrom = here;
            st.StuckDistance = d;
            st.StuckFor = noProgress ? st.StuckFor + 1 : 0;
            if (!noProgress && d < st.StuckDistance - 2f) st.UnstickStage = 0;
            bool noWay = d > 6f && !HavePath(st.Ai, master.transform.position);
            if (st.StuckFor >= 3 || (noWay && st.StuckFor >= 1)) { st.StuckFor = 0; return true; }
            return false;
        }

        private static void Unstick(BrainState st, Player master)
        {
            Humanoid me = st.Body;
            int stage = st.UnstickStage++;
            if (stage == 0)
            {
                Vector3 toYou = master.transform.position - me.transform.position; toYou.y = 0f;
                Vector3 side = Vector3.Cross(Vector3.up, toYou.normalized) * (UnityEngine.Random.value < 0.5f ? 1f : -1f);
                st.SideStepTo = me.transform.position + side * 2.5f + toYou.normalized * 0.5f;
                st.SideStepUntil = Time.time + 1.2f;
                return;
            }
            if (stage == 1) { me.SetMoveDir((master.transform.position - me.transform.position).normalized); me.Jump(false); return; }
            st.UnstickStage = 0;
            TeleportBehind(me, master, "villagepleb_brain_hopped_over_to");
        }

        internal static void TeleportBehind(Humanoid me, Player master, string how = "villagepleb_brain_caught_up_with")
        {
            Vector3 pos = FreeSpotNear(master, me);
            me.transform.position = pos;
            Rigidbody body = me.GetComponent<Rigidbody>();
            if (body != null) { body.position = pos; body.linearVelocity = Vector3.zero; }
            Companion.Zdo(me)?.SetPosition(pos);
            Get(me).Remember(P(how, master.GetPlayerName()));
            VillagePlebsPlugin.Instance?.Note($"{Companion.NameOf(me)} {English(P(how, master.GetPlayerName()))}");
        }

        private static readonly int Solid = LayerMask.GetMask("Default", "static_solid", "Default_small", "piece", "vehicle");

        private static void Apart(Humanoid me, float dt)
        {
            if (me.IsAttached() || !me.IsOnGround()) return;
            BrainState st = Get(me);
            if (st.ApartFrom == null && Time.time >= st.NextApartLook)
            {
                st.NextApartLook = Time.time + 0.25f;
                st.ApartFrom = Companion.All().FirstOrDefault(o => o != me && !o.IsDead() && Overlap(me, o, out _));
            }
            Humanoid other = st.ApartFrom;
            if (other == null) return;
            if (other.IsDead() || !Overlap(me, other, out Vector3 away)) { st.ApartFrom = null; return; }
            if (away.sqrMagnitude < 0.0001f) away = me.transform.right * (Companion.IdOf(me) < Companion.IdOf(other) ? 1f : -1f);
            Vector3 to = me.transform.position + away.normalized * (1.5f * dt);
            if (!Steer.Safe(to, me)) { st.ApartFrom = null; return; }
            Rigidbody body = me.GetComponent<Rigidbody>();
            if (body != null) body.position = to; else me.transform.position = to;
        }

        private static bool Overlap(Humanoid me, Humanoid other, out Vector3 away)
        {
            away = me.transform.position - other.transform.position;
            if (Mathf.Abs(away.y) > 1.5f) return false;
            away.y = 0f;
            return away.sqrMagnitude <= Room * Room;
        }

        private const float Room = 0.8f;

        private static Vector3 FreeSpotNear(Player master, Humanoid me)
        {
            Transform t = master.transform;
            Vector3[] offsets = { -t.forward * 1.8f, -t.right * 1.6f, t.right * 1.6f, (-t.forward - t.right).normalized * 2f, (-t.forward + t.right).normalized * 2f, t.forward * 1.8f };
            foreach (Vector3 o in offsets)
            {
                Vector3 p = t.position + o;
                if (t.position.y > 3000f) p.y = t.position.y;
                else if (ZoneSystem.instance != null && ZoneSystem.instance.GetSolidHeight(p, out float h))
                {
                    if (Mathf.Abs(h - t.position.y) > 1.2f) continue;
                    p.y = h;
                }
                else p.y = t.position.y;
                if (Physics.CheckCapsule(p + Vector3.up * 0.6f, p + Vector3.up * 1.6f, 0.35f, Solid)) continue;
                if (!Steer.Safe(p, null)) continue;
                if (Companion.All().Any(o => o != me && Vector3.Distance(o.transform.position, p) < Room + 0.4f)) continue;
                return p;
            }
            return t.position - t.forward * 0.8f;
        }

        private static void ScanEnemies(BrainState st, Player master)
        {
            Humanoid me = st.Body;
            float range = MainConfig.engageRange.Value;
            Style style = Companion.StyleOf(me);
            bool careful = st.Weak || Tactics.Careful(st);
            st.Enemies.Clear();
            foreach (Character c in Character.GetAllCharacters())
            {
                if (c == null || c == me || c.IsDead() || c.IsPlayer() || !BaseAI.IsEnemy(me, c)) continue;
                if (c.GetComponent<BaseAI>() == null) continue;
                if (st.LeaveAlone.TryGetValue(c, out float until) && Time.time < until && TargetOf(c) != me && TargetOf(c) != master) continue;
                if (c.GetFaction() == Character.Faction.AnimalsVeg && TargetOf(c) != me && TargetOf(c) != master) continue;
                if (st.Task != null && st.Task.Kind == Work.Kind.Hunt && st.Task.Target == c) continue;
                string kind = Utils.GetPrefabName(c.gameObject);
                if (kind == "Deer" || kind == "Hare") continue;
                float toMe = Vector3.Distance(c.transform.position, me.transform.position);
                float toMaster = master != null ? Vector3.Distance(c.transform.position, master.transform.position) : float.MaxValue;
                float limit = style == Style.Defensive ? range * 0.6f : range;
                Character theirs = TargetOf(c);
                bool home = Companion.OrderOf(me) == Order.Gather && Vector3.Distance(c.transform.position, Work.Center(me)) < Mathf.Min(Work.RadiusOf(me), 30f)
                            && theirs != null && (theirs == me || theirs.IsPlayer() || Companion.Is(theirs));
                if (careful && TargetOf(c) != me && TargetOf(c) != master) continue;
                if (Following.DefendOnly(me) && TargetOf(c) != me && TargetOf(c) != master && toMe > 3f && toMaster > 4f) continue;
                if (toMe < limit || toMaster < limit || home) st.Enemies.Add(c);
            }
            if (st.Focus != null && !st.Focus.IsDead() && Time.time < st.FocusUntil && !st.Enemies.Contains(st.Focus) && Vector3.Distance(st.Focus.transform.position, me.transform.position) < 50f)
                st.Enemies.Add(st.Focus);
            st.Enemies.Sort((a, b) => Vector3.Distance(a.transform.position, me.transform.position).CompareTo(Vector3.Distance(b.transform.position, me.transform.position)));
        }

        private static void MaybeDecide(BrainState st, Player master)
        {
            float now = Time.time;
            Humanoid me = st.Body;
            bool targetGone = st.Current.Target != null && (st.Current.Target.IsDead() || !st.Enemies.Contains(st.Current.Target));
            bool bigHit = st.HealthAtAsk - me.GetHealthPercentage() > 0.2f;
            bool newEnemy = st.Enemies.Count > st.EnemyCountAtAsk;
            bool urgent = targetGone || bigHit || newEnemy;
            if (now < st.NextAsk && !(urgent && now - st.LastAsk > 0.5f)) return;

            st.LastAsk = now;
            st.NextAsk = now + 1.5f;
            st.HealthAtAsk = me.GetHealthPercentage();
            st.EnemyCountAtAsk = st.Enemies.Count;
            Apply(st, BuiltIn(st, master));
        }

        private static readonly int PieceMask = LayerMask.GetMask("piece", "piece_nonsolid", "Default");

        private static void Opened(BrainState st, Door door)
        {
            if (st == null || door == null) return;
            if (!st.OpenedDoors.ContainsKey(door)) Activity.Log(st.Body, $"opened the {Localization.instance.Localize(door.m_name).ToLowerInvariant()} at {door.transform.position:F0}");
            st.OpenedDoors[door] = Time.time;
        }

        private static void CloseBehind(BrainState st)
        {
            if (st.OpenedDoors.Count == 0) return;
            Humanoid me = st.Body;
            foreach (Door door in st.OpenedDoors.Keys.ToList())
            {
                float since = st.OpenedDoors[door];
                if (door == null || Time.time - since > 60f) { st.OpenedDoors.Remove(door); continue; }
                ZDO z = Companion.Zdo(door);
                if (z == null || z.GetInt(ZDOVars.s_state, 0) == 0) { st.OpenedDoors.Remove(door); continue; }
                Vector3 at = door.transform.position;
                if (Time.time - since < 1.2f || Utils.DistanceXZ(at, me.transform.position) < 2.5f) continue;
                if (Utils.DistanceXZ(at, me.transform.position) > 12f) { st.OpenedDoors.Remove(door); continue; }
                bool someone = Player.GetAllPlayers().Any(p => p != null && Utils.DistanceXZ(p.transform.position, at) < 5f)
                               || Companion.All().Any(c => c != me && Utils.DistanceXZ(c.transform.position, at) < 2.5f)
                               || st.Enemies.Any(e => e != null && Utils.DistanceXZ(e.transform.position, at) < 6f);
                if (someone) continue;
                door.Interact(me, false, false);
                st.OpenedDoors.Remove(door);
                Activity.Log(me, $"shut the {Localization.instance.Localize(door.m_name).ToLowerInvariant()} behind it");
            }
        }

        private static void OpenDoorAhead(Humanoid me)
        {
            Vector3 dir = me.GetMoveDir();
            dir.y = 0f;
            if (dir.sqrMagnitude < 0.01f) return;
            foreach (Collider col in Physics.OverlapSphere(me.transform.position + dir.normalized * 1.1f + Vector3.up, 0.9f, PieceMask))
            {
                Door door = col.GetComponentInParent<Door>();
                if (door == null || door.m_keyItem != null) continue;
                ZDO z = Companion.Zdo(door);
                if (z == null || z.GetInt(ZDOVars.s_state, 0) != 0) continue;
                door.Interact(me, false, false);
                Opened(Get(me), door);
                return;
            }
        }

        private static bool MakeWay(BrainState st, float dt)
        {
            Humanoid me = st.Body;
            if (Time.time < st.YieldUntil)
            {
                MoveToRaw(st.Ai, dt, st.YieldTo, 0.4f, true);
                return true;
            }
            st.YieldFrom = null;
            foreach (Player p in Player.GetAllPlayers())
            {
                if (p == null) continue;
                Vector3 toMe = me.transform.position - p.transform.position; toMe.y = 0f;
                float d = toMe.magnitude;
                if (d > 2.5f) continue;
                Vector3 v = p.GetVelocity(); v.y = 0f;
                bool walkingInto = v.magnitude > 0.5f && Vector3.Dot(v.normalized, toMe.normalized) > 0.3f;
                bool touching = d < 1.1f;
                if (!walkingInto && !touching) continue;
                Vector3 dir = v.magnitude > 0.5f ? v.normalized : (d > 0.01f ? toMe.normalized : me.transform.forward);
                Vector3 side = Vector3.Cross(Vector3.up, dir);
                if (Vector3.Dot(side, toMe) < 0f) side = -side;
                st.YieldTo = me.transform.position + side * 3f + dir * 1f;
                st.YieldUntil = Time.time + 1.2f;
                st.YieldFrom = p;
                MoveToRaw(st.Ai, dt, st.YieldTo, 0.4f, true);
                return true;
            }
            return false;
        }

        internal static Character TargetOf(Character enemy) => enemy != null && enemy.GetBaseAI() is MonsterAI m ? m.GetTargetCreature() : null;

        private static void Enforce(BrainState st, Decision d)
        {
            Humanoid me = st.Body;
            if (st.Focus != null && (st.Focus.IsDead() || Time.time > st.FocusUntil)) st.Focus = null;
            if (st.Focus != null && st.Enemies.Contains(st.Focus) && (d.Action == Tactic.Attack || d.Action == Tactic.DefendPlayer)) { d.Action = Tactic.Attack; d.Target = st.Focus; d.Note = P("villagepleb_brain_note_pointed"); }
            Style style = Companion.StyleOf(me);
            float health = me.GetHealthPercentage();
            float retreat = Companion.RetreatOf(me) / 100f * (style == Style.Aggressive ? 0.5f : 1f);
            bool fighting = d.Action == Tactic.Attack || d.Action == Tactic.DefendPlayer;
            string rule = null;
            if (health < retreat * 0.5f && d.Action != Tactic.Flee) { d.Action = Tactic.Flee; rule = P("villagepleb_brain_rule_flee", $"{retreat * 50f:0}"); }
            else if (health < retreat && fighting) { d.Action = Tactic.Retreat; rule = P("villagepleb_brain_rule_fall_back", $"{retreat * 100f:0}"); }
            else if (style == Style.Passive && fighting) { d.Action = Tactic.Retreat; rule = P("villagepleb_brain_rule_passive"); }
            if (rule == null) return;
            d.Note = string.IsNullOrEmpty(d.Note) ? rule : d.Note + "; " + rule;
            if (d.Action != Tactic.Attack) d.Drink |= Companion.Potions(me) && Companion.HealingPotions(me).Count > 0;
        }

        private static void Apply(BrainState st, Decision d)
        {
            Enforce(st, d);
            d.Time = Time.time;
            st.FightDecisions++;
            bool changed = d.Action != st.Current.Action || d.Target != st.Current.Target || d.Ranged != st.Current.Ranged;
            st.Current = d;
            if (d.Drink && Companion.Drink(st.Body)) { st.Remember(P("villagepleb_brain_drank_potion")); st.Potions++; }
            string line = d.Describe(st.Label);
            if (changed || d.Drink) st.Remember(line + (string.IsNullOrEmpty(d.Note) ? "" : "  (" + d.Note + ")"));
            SetStatus(st, line);
            if (changed && MainConfig.showDecisions.Value && Chat.instance != null)
                Chat.instance.SetNpcText(st.Body.gameObject, Vector3.up * 2.3f, 25f, 2.5f, "", Words.Show(line), false);
        }

        public static Decision BuiltIn(BrainState st, Player master)
        {
            Humanoid me = st.Body;
            Style style = Companion.StyleOf(me);
            float health = me.GetHealthPercentage();
            float retreat = Companion.RetreatOf(me) / 100f * (style == Style.Aggressive ? 0.5f : 1f);
            var d = new Decision { Note = "" };
            d.Drink = Companion.Potions(me) && health < 0.5f && Companion.HealingPotions(me).Count > 0;
            Character nearest = st.Enemies.FirstOrDefault();
            if (style == Style.Passive) { d.Action = health < retreat ? Tactic.Flee : Tactic.Retreat; return d; }
            if (health < retreat * 0.5f) { d.Action = Tactic.Flee; d.Note = P("villagepleb_brain_note_nearly_dead"); return d; }
            if (health < retreat) { d.Action = Tactic.Retreat; d.Note = P("villagepleb_brain_note_hurt"); return d; }
            bool careful = Tactics.Careful(st);
            Character danger = st.Enemies.FirstOrDefault(e => e != null && Vector3.Distance(e.transform.position, me.transform.position) < 20f && Tactics.TooStrong(me, e));
            if (danger != null && (careful || Companion.BestRanged(me) == null || Vector3.Distance(danger.transform.position, me.transform.position) < 5f))
            {
                string what = danger.m_name;
                d.Action = Tactic.Flee; d.Note = P("villagepleb_brain_note_too_strong", Low(what));
                Talk.Mention(me, P("villagepleb_brain_say_too_strong", Low(what)), "toostrong:" + what, 3f);
                return d;
            }
            if (careful && health < 0.6f) { d.Action = health < 0.35f ? Tactic.Flee : Tactic.Retreat; d.Note = P("villagepleb_brain_note_careful"); return d; }
            if (Stamina.Get(me) < Stamina.Max(me) * 0.15f && nearest != null && Vector3.Distance(nearest.transform.position, me.transform.position) < 6f)
            { d.Action = Tactic.BackOff; d.Note = P("villagepleb_brain_note_out_of_breath"); return d; }
            d.Action = Tactic.Attack;
            d.Target = PickTarget(st, master);
            Character t = d.Target;
            d.Ranged = Companion.BestRanged(me) != null && t != null && (Companion.BestMelee(me) == null || Vector3.Distance(t.transform.position, me.transform.position) > 10f
                                                                    || t.IsBoss() || Tactics.Feared(me, t) || Tactics.TooStrong(me, t));
            if (d.Ranged && Companion.BestMelee(me) != null && st.Enemies.Any(e => e != null && TargetOf(e) == me && Vector3.Distance(e.transform.position, me.transform.position) < 2.5f))
                d.Ranged = false;
            if (st.Enemies.Any(e => e != null && e.IsBoss()) && Companion.Potions(me) && health < 0.6f && Companion.HealingPotions(me).Count > 0) d.Drink = true;
            return d;
        }

        private static Character PickTarget(BrainState st, Player master)
        {
            Humanoid me = st.Body;
            float Dist(Character e) => Vector3.Distance(e.transform.position, me.transform.position);
            if (master != null && Companion.Protect(me) && master.GetHealthPercentage() < 0.4f)
            {
                Character onYou = st.Enemies.Where(e => TargetOf(e) == master).OrderBy(e => Vector3.Distance(e.transform.position, master.transform.position)).FirstOrDefault();
                if (onYou != null) return onYou;
            }
            Character current = st.Current.Target;
            if (current != null && !current.IsDead() && st.Enemies.Contains(current) && Dist(current) < 6f && TargetOf(current) != null) return current;
            var onMe = st.Enemies.Where(e => TargetOf(e) == me && Dist(e) < 5f).ToList();
            if (onMe.Count > 0) return onMe.OrderBy(e => e.GetHealthPercentage()).ThenBy(Dist).First();
            Character yours = Tactics.Yours();
            if (yours != null && st.Enemies.Contains(yours)) return yours;
            if (master != null && Companion.Protect(me))
            {
                Character onMaster = st.Enemies.Where(e => TargetOf(e) == master).OrderBy(e => Vector3.Distance(e.transform.position, master.transform.position)).FirstOrDefault();
                if (onMaster != null) return onMaster;
            }
            return st.Enemies.Where(e => TargetOf(e) == me).OrderBy(Dist).FirstOrDefault() ?? st.Enemies.FirstOrDefault();
        }

        private static void Fight(BrainState st, Player master, float dt)
        {
            Humanoid me = st.Body;
            Tactics.Meads(st);
            Decision d = st.Current;
            Character nearest = st.Enemies.FirstOrDefault();
            switch (d.Action)
            {
                case Tactic.Attack:
                    Strike(st, d.Target != null && !d.Target.IsDead() && st.Enemies.Contains(d.Target) ? d.Target : nearest, dt);
                    break;
                case Tactic.DefendPlayer:
                    Character onMaster = master == null ? null : st.Enemies.Where(e => e != null && TargetOf(e) == master).OrderBy(e => Vector3.Distance(e.transform.position, master.transform.position)).FirstOrDefault();
                    Character onMe = st.Enemies.Where(e => e != null && TargetOf(e) == me).OrderBy(e => Vector3.Distance(e.transform.position, me.transform.position)).FirstOrDefault();
                    if (onMaster != null && onMe == null && Vector3.Distance(me.transform.position, master.transform.position) > 12f)
                    { Blocking(me) = false; MoveTo(st.Ai, dt, master.transform.position, 3f, true); }
                    else Strike(st, onMaster ?? onMe ?? nearest, dt);
                    break;
                case Tactic.BackOff:
                    Guarded(st, nearest, me.transform.position + (me.transform.position - nearest.transform.position).normalized * 4f, false, dt);
                    break;
                case Tactic.Retreat:
                    Vector3 to = master != null ? master.transform.position : me.transform.position + (me.transform.position - nearest.transform.position).normalized * 6f;
                    Guarded(st, nearest, to, Vector3.Distance(to, me.transform.position) > 6f, dt);
                    break;
                default:
                    Blocking(me) = false;
                    Vector3? safe = SafePlace(me, master);
                    if (safe.HasValue && Vector3.Distance(safe.Value, me.transform.position) > 3f) { MoveTo(st.Ai, dt, safe.Value, 2f, true); break; }
                    Vector3 away = Vector3.zero;
                    foreach (Character e in st.Enemies) away += (me.transform.position - e.transform.position).normalized;
                    Vector3 dir = away.normalized + (master != null ? (master.transform.position - me.transform.position).normalized * 0.5f : Vector3.zero);
                    Vector3 off = me.transform.position + dir.normalized * 10f;
                    if (safe.HasValue && Vector3.Distance(off, safe.Value) > 12f) off = safe.Value;
                    MoveTo(st.Ai, dt, off, 0f, true);
                    break;
            }
        }

        private static Vector3? SafePlace(Humanoid me, Player master)
        {
            Order order = Companion.OrderOf(me);
            if (order == Order.Gather) return Work.Center(me);
            if (master != null && order == Order.Follow) return master.transform.position;
            if (order == Order.Guard || order == Order.Stay) return Companion.Zdo(me)?.GetVec3(Keys.Post, me.transform.position);
            return null;
        }

        internal static void Strike(BrainState st, Character target, float dt)
        {
            Humanoid me = st.Body;
            Blocking(me) = false;
            if (target == null) return;
            if (st.StallOn != target || target.GetHealth() < st.StallHealth - 0.1f) { st.StallOn = target; st.StallSince = Time.time; st.StallHealth = target.GetHealth(); }
            else if (Time.time - st.StallSince > 20f && TargetOf(target) != me && TargetOf(target) != Companion.Master(me))
            {
                st.LeaveAlone[target] = Time.time + 120f;
                string kind = Utils.GetPrefabName(target.gameObject);
                foreach (Character like in Character.GetAllCharacters().Where(c => c != null && c != target && Utils.GetPrefabName(c.gameObject) == kind && Vector3.Distance(c.transform.position, target.transform.position) < 15f))
                    st.LeaveAlone[like] = Time.time + 120f;
                foreach (Character gone in st.LeaveAlone.Keys.Where(k => k == null).ToList()) st.LeaveAlone.Remove(gone);
                st.Enemies.Remove(target);
                st.Remember(P("villagepleb_brain_left_alone", target.m_name));
                if (st.Task != null && st.Task.Target == target) st.Task = null;
                st.StallOn = null;
                return;
            }
            ItemDrop.ItemData weapon = me.GetCurrentWeapon();
            bool ranged = Companion.IsRanged(weapon) && Companion.HasAmmoFor(me, weapon);
            float reach = ranged ? 30f : Mathf.Max(1.2f, (weapon?.m_shared.m_attack?.m_attackRange ?? 1.5f) * 0.9f);
            float dist = Vector3.Distance(target.transform.position, me.transform.position) - target.GetRadius();
            Vector3 aim = target.GetCenterPoint();

            if (Time.time < st.EvadeUntil)
            {
                Vector3 step = st.EvadeTo - me.transform.position;
                step.y = 0f;
                Blocking(me) = false;
                if (step.sqrMagnitude > 0.25f) { me.SetMoveDir(step.normalized); return; }
                st.EvadeUntil = 0f;
            }
            bool counter = Time.time < st.CounterUntil && target.IsStaggering();
            if (!counter && !me.InAttack())
            {
                Character swinging = st.Enemies.FirstOrDefault(e => e != null && !e.IsDead() && e.InAttack() && TargetOf(e) == me
                                                                    && Vector3.Distance(e.transform.position, me.transform.position) < Defense.Reach(e));
                if (swinging != null)
                {
                    if (Time.time >= st.NextEvade && Defense.ShouldEvade(me, swinging, out string why) && Defense.StepAside(st, swinging))
                    {
                        st.NextEvade = Time.time + 1.2f;
                        Blocking(me) = false;
                        Activity.Log(me, $"rolled away from {Localization.instance.Localize(swinging.m_name)} ({why})");
                        return;
                    }
                    if (!ranged && Companion.CanBlock(me) && Stamina.Get(me) > 8f)
                    {
                        st.Blocker = swinging;
                        st.Ai.StopMoving();
                        LookAt(st.Ai, swinging.GetCenterPoint());
                        bool up = Defense.RaiseNow(swinging);
                        Blocking(me) = up;
                        if (up) st.BlockUntil = Time.time + 0.35f;
                        return;
                    }
                }
                else if (Time.time < st.BlockUntil && !ranged && Companion.CanBlock(me))
                {
                    Character facing = st.Blocker != null && !st.Blocker.IsDead() ? st.Blocker : target;
                    st.Ai.StopMoving();
                    LookAt(st.Ai, facing.GetCenterPoint());
                    Blocking(me) = true;
                    return;
                }
            }

            if (st.HitAndRun && !me.InAttack()) { st.HitAndRun = false; st.RunBackUntil = Time.time + 0.8f; }
            if (!ranged && Time.time < st.RunBackUntil && !target.IsStaggering())
            {
                Vector3 back = me.transform.position - target.transform.position; back.y = 0f;
                MoveTo(st.Ai, dt, target.transform.position + back.normalized * (Defense.Reach(target) + 1f), 0.3f, true);
                LookAt(st.Ai, target.GetCenterPoint());
                return;
            }
            Player you = Companion.Master(me);

            if (!ranged && Tactics.Unaware(target) && !st.Enemies.Any(e => e != null && TargetOf(e) == me))
            {
                Vector3? behind = Tactics.Behind(me, target, reach);
                if (behind != null && Vector3.Distance(behind.Value, me.transform.position) > 0.8f) { MoveTo(st.Ai, dt, behind.Value, 0.3f, false); Skill.Doing(me, Skills.SkillType.Sneak, dt); if (st.Task == null) Brain.Status(st, P("villagepleb_brain_status_sneaking", Low(target.m_name))); return; }
                if (dist > reach) { MoveTo(st.Ai, dt, target.transform.position, reach * 0.6f, false); return; }
            }

            if (target.IsBoss())
            {
                Vector3 fromBoss = me.transform.position - target.transform.position; fromBoss.y = 0f;
                if (st.EvadeSide == 0f) st.EvadeSide = UnityEngine.Random.value < 0.5f ? 1f : -1f;
                if (ranged && dist < 12f) { MoveTo(st.Ai, dt, target.transform.position + Quaternion.Euler(0f, 25f * st.EvadeSide, 0f) * fromBoss.normalized * 15f, 1f, true); return; }
                if (!ranged && TargetOf(target) != me && Vector3.Angle(target.transform.forward, fromBoss) < 60f)
                {
                    Vector3 flank = target.transform.position + Quaternion.Euler(0f, 100f * st.EvadeSide, 0f) * target.transform.forward * (target.GetRadius() + reach * 0.8f);
                    MoveTo(st.Ai, dt, flank, 0.6f, true);
                    Brain.Status(st, P("villagepleb_brain_status_flank"));
                    return;
                }
            }
            if (!ranged && Tactics.Pincer(me, you, target, reach) is Vector3 far && Vector3.Distance(far, me.transform.position) > 1.2f)
            {
                MoveTo(st.Ai, dt, far, 0.5f, true);
                if (st.Task == null) Brain.Status(st, P("villagepleb_brain_status_behind", Low(target.m_name)));
                return;
            }
            if (ranged && dist < 7f && TargetOf(target) == me && !target.IsBoss() && (dist < 4f || me.GetTimeSinceLastAttack() < 1.6f))
            {
                Vector3 away = me.transform.position - target.transform.position; away.y = 0f;
                MoveTo(st.Ai, dt, me.transform.position + away.normalized * 5f, 0.5f, true);
                return;
            }
            if (dist > reach || (ranged && !st.Ai.CanSeeTarget(target))) { MoveTo(st.Ai, dt, target.transform.position, ranged ? reach * 0.7f : reach * 0.6f, dist > 5f); return; }

            st.Ai.StopMoving();
            Vector3 lookAt = aim;
            if (ranged)
            {
                Vector3? shot = Archery.Aim(me, weapon, target);
                if (shot == null) { MoveTo(st.Ai, dt, target.transform.position, 8f, dist > 12f); return; }
                me.SetLookDir(shot.Value, 0f);
                lookAt = me.transform.position + new Vector3(shot.Value.x, 0f, shot.Value.z) * 10f;
                LookAt(st.Ai, lookAt);
                me.SetLookDir(shot.Value, 0f);
            }
            else
            {
                LookAt(st.Ai, aim);
                Vector3 swingFrom = me.transform.position + Vector3.up * (weapon?.m_shared.m_attack?.m_attackHeight ?? 1f);
                Vector3 toTarget = aim - swingFrom;
                if (toTarget.sqrMagnitude > 0.01f) me.SetLookDir(toTarget.normalized, 0f);
            }
            float cost = weapon?.m_shared.m_attack?.m_attackStamina ?? 0f;
            if (!me.InAttack() && cost > 0f && Stamina.Get(me) < cost + 0.1f)
            {
                Guarded(st, target, me.transform.position + (me.transform.position - target.transform.position).normalized * 3f, false, dt);
                return;
            }
            if (me.InAttack() || !st.Ai.IsLookingAt(ranged ? lookAt : aim, ranged ? 4f : 25f)) return;
            bool combo = !ranged && Tactics.MidCombo(me);
            if (!combo && me.GetTimeSinceLastAttack() < (ranged ? 1.6f : 0.35f)) return;
            if (ranged) DrawTime(me) = 10f;
            bool special = !ranged && Tactics.Special(st, weapon, target, dist);
            if (me.StartAttack(target, special))
            {
                st.Blows++;
                if (special) Activity.Log(me, $"used its {Localization.instance.Localize(weapon.m_shared.m_name).ToLowerInvariant()}'s special on the {Localization.instance.Localize(target.m_name).ToLowerInvariant()}");
                if (!ranged && TargetOf(target) == me && Tactics.Heavy(me, target) && !Tactics.MidComboStarted(me)) st.HitAndRun = true;
                if (combo) Journal.Count(me, "combo blows");
            }
            if (ranged) DrawTime(me) = 0f;
        }

        private static void Guarded(BrainState st, Character threat, Vector3 to, bool run, float dt)
        {
            Humanoid me = st.Body;
            bool close = threat != null && Vector3.Distance(threat.transform.position, me.transform.position) < 5f;
            Blocking(me) = close && !run && Companion.CanBlock(me);
            if (Vector3.Distance(to, me.transform.position) > 1.5f) MoveTo(st.Ai, dt, to, 1f, run);
            else st.Ai.StopMoving();
            if (threat != null && close) LookAt(st.Ai, threat.GetCenterPoint());
        }

        internal static void Summarise(BrainState st, bool fell)
        {
            string secs = $"{Time.time - st.FightStart:0}", kills = P(st.FightKills == 1 ? "villagepleb_brain_kills_one" : "villagepleb_brain_kills_many", st.FightKills);
            int parries = st.Parries - st.ParriesAtStart;
            string line = P(fell ? "villagepleb_brain_fight_fell" : "villagepleb_brain_fight_over", secs, $"{st.Taken:0}", $"{st.Dealt:0}", kills, st.FightDecisions);
            st.Remember(line);
            VillagePlebsPlugin.Instance?.Note($"{Companion.NameOf(st.Body)}: {English(line)}");
            string record = P(fell ? "villagepleb_brain_record_fell" : "villagepleb_brain_record_over", $"{DateTime.Now:HH:mm}", secs, string.Join(", ", st.FightFoes.Take(4)),
                $"{st.Dealt:0}", $"{st.Taken:0}", kills, P(parries == 1 ? "villagepleb_brain_parries_one" : "villagepleb_brain_parries_many", parries),
                st.Blocks - st.BlocksAtStart, st.FightDodges, st.Potions - st.PotionsAtStart);
            st.FightLog.Insert(0, record);
            if (st.FightLog.Count > 8) st.FightLog.RemoveAt(st.FightLog.Count - 1);
        }

        internal static void Status(BrainState st, string status) => SetStatus(st, status);

        private static void SetStatus(BrainState st, string status)
        {
            if (st.Status == status) return;
            st.Status = status;
            Activity.Log(st.Body, $"now: {status}  (hp {st.Body.GetHealth():0}/{st.Body.GetMaxHealth():0})");
            SaveStatus(st);
        }

        internal static void SaveStatus(BrainState st)
        {
            if (Time.time < st.NextStatusSave) return;
            ZDO z = Companion.Zdo(st.Body);
            if (z == null || !st.Body.GetComponent<ZNetView>().IsOwner() || z.GetString(Keys.Status, "") == st.Status) return;
            st.NextStatusSave = Time.time + 1f;
            z.Set(Keys.Status, st.Status);
        }

        internal static string LiveStatus(Humanoid h) =>
            h != null && States.TryGetValue(h, out BrainState st) && st.Status != "" && h.GetComponent<ZNetView>() is ZNetView v && v.IsValid() && v.IsOwner() ? st.Status : null;
    }
}
