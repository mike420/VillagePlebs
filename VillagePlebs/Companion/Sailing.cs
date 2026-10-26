using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using UnityEngine;
using static VillagePlebs.Words;

namespace VillagePlebs
{
    // Sailing a ship to a spot on the map: the companion takes the helm of a free ship near you, you pick where on the chart, and once it sets sail
    // it cannot be called back until it gets there. It steers round the coasts with the wind always behind it, and keeps going (along the
    // charted course) even when no one is near enough for the ship to be loaded. The game that owns the companion's player does the sailing.
    internal static class Sailing
    {
        public const string ShipKey = "vp_sail_ship", PrefabKey = "vp_sail_prefab", ToKey = "vp_sail_to", SetKey = "vp_sail_set";
        private const string DockKey = "vp_sail_docked", HomeKey = "vp_sail_tohome", DockNameKey = "vp_sail_dock";
        private const string TagKey = "vp_sail_tag", ByKey = "vp_sail_by", ByNameKey = "vp_sail_name";
        private const float Reach = 100f, ArriveAt = 20f, AwaySpeed = 9f, Draft = 2.5f, StepAshore = 45f;

        private static readonly AccessTools.FieldRef<Ship, Ship.Speed> SpeedOf = AccessTools.FieldRefAccess<Ship, Ship.Speed>("m_speed");
        private static readonly AccessTools.FieldRef<Ship, float> RudderOf = AccessTools.FieldRefAccess<Ship, float>("m_rudderValue");
        private static readonly AccessTools.FieldRef<Ship, Rigidbody> BodyOf = AccessTools.FieldRefAccess<Ship, Rigidbody>("m_body");
        private static readonly AccessTools.FieldRef<Ship, List<Player>> Aboard = AccessTools.FieldRefAccess<Ship, List<Player>>("m_players");
        internal static readonly AccessTools.FieldRef<Ship, Vector3> SailForce = AccessTools.FieldRefAccess<Ship, Vector3>("m_sailForce");
        internal static readonly AccessTools.FieldRef<Ship, Vector3> WindChange = AccessTools.FieldRefAccess<Ship, Vector3>("m_windChangeVelocity");
        private static readonly int Solid = LayerMask.GetMask("Default", "static_solid", "Default_small", "piece", "terrain");
        private static readonly RaycastHit[] Hits = new RaycastHit[12];

        internal sealed class Voyage
        {
            public long Id, Tag;
            public string Prefab = "";
            public Vector3 To;
            public ZDO Body, Hull;
            public Ship Ship;
            public SeaRoute Route;
            public float Progress, Best;
            public bool Planning;
            public int Fails, Tries;
            public float NextPlan, NextFind, NextClaim, NextMove, MissingSince, GoneSince;
            public float NextScan, StuckFor = -10f, NoHeadway, BackFor, Clearing;
            public bool Backing, Home, Landing;
            public float LandingSince, BlockAhead = float.MaxValue, BlockSeen, NextShore, NextLeviathans;
            public Vector3? ShoreSeen;
            public Vector3 TriedAt;
        }

        private static readonly Dictionary<long, Voyage> Voyages = new Dictionary<long, Voyage>();
        // Ships held still (kinematic) while being worked clear by hand: let go again whatever ends that.
        private static readonly HashSet<Ship> Held = new HashSet<Ship>();
        private static float _nextLook, _nextAsk;

        public static bool Underway(Component c) => Underway(Companion.Zdo(c));
        public static bool Underway(ZDO z) => z != null && z.GetBool(SetKey, false);
        public static bool AtHelm(Component c) => (Companion.Zdo(c)?.GetLong(ShipKey, 0L) ?? 0L) != 0L;

        // Sent off by ship: under way, or arrived somewhere and waiting aboard. It takes no orders and does no work until it is sent
        // somewhere else or home by sea.
        public static bool Away(Component c) => Away(Companion.Zdo(c));
        public static bool Away(ZDO z) => z != null && z.GetLong(ShipKey, 0L) != 0L && (z.GetBool(SetKey, false) || z.GetBool(DockKey, false));
        public static bool Docked(Component c) { ZDO z = Companion.Zdo(c); return z != null && z.GetLong(ShipKey, 0L) != 0L && z.GetBool(DockKey, false); }

        private static ZDO ZdoOf(Ship s) { ZNetView v = s != null ? s.GetComponent<ZNetView>() : null; return v != null && v.IsValid() ? v.GetZDO() : null; }
        private static long TagOf(Ship s) => ZdoOf(s)?.GetLong(TagKey, 0L) ?? 0L;

        // A companion has this ship (cheap: the mark alone, for every frame).
        public static bool Marked(Ship s) => (ZdoOf(s)?.GetLong(ByKey, 0L) ?? 0L) != 0L;

        // A companion has this ship, checked against the companion itself so a mark left behind does not lock the ship for ever.
        public static bool Manned(Ship s, out string name)
        {
            name = "";
            ZDO hull = ZdoOf(s);
            long by = hull?.GetLong(ByKey, 0L) ?? 0L;
            if (by == 0L) return false;
            ZDO body = Remote.Find(by);
            if (body == null || body.GetLong(ShipKey, 0L) != hull.GetLong(TagKey, 0L)) return false;
            name = hull.GetString(ByNameKey, "");
            return true;
        }

        private static float HalfLength(Ship s) => s != null && s.m_floatCollider != null ? s.m_floatCollider.size.z * 0.5f : 8f;
        private static float HalfBeam(Ship s) => s != null && s.m_floatCollider != null ? s.m_floatCollider.size.x * 0.5f : 2.5f;
        public static float Hull(Ship s) => Mathf.Clamp(HalfLength(s) + 3f, 8f, 24f);

        private static Ship ShipPrefab(string prefab) => ZNetScene.instance != null && !string.IsNullOrEmpty(prefab) ? ZNetScene.instance.GetPrefab(prefab)?.GetComponent<Ship>() : null;

        public static IEnumerable<Ship> Ships() => Ship.Instances.OfType<Ship>().Where(s => s != null && ZdoOf(s) != null);

        // The nearest ship by you with a helm that no one (and no companion) is steering.
        public static Ship FreeShip(Vector3 at, out bool taken)
        {
            taken = false;
            Ship best = null;
            float bestD = Reach;
            foreach (Ship s in Ships())
            {
                if (s.m_shipControlls == null || s.m_shipControlls.m_attachPoint == null) continue;
                float d = Vector3.Distance(s.transform.position, at);
                if (d > Reach) continue;
                if (s.m_shipControlls.HaveValidUser() || Manned(s, out _)) { taken = true; continue; }
                if (d < bestD) { bestD = d; best = s; }
            }
            return best;
        }

        // ---- taking the helm, setting sail, stepping down ----

        // The Sail order: takes the helm of a free ship near you and opens the chart. Returns what to tell you if it could not.
        public static string Offer(Player p, Humanoid c)
        {
            if (p == null || c == null || c.IsDead()) return null;
            if (SeaMap.IsOpen) return null;
            // Already away (under way, or waiting aboard): the chart opens to send it somewhere new, or home.
            if (Away(c)) return Companion.IsMine(c, p) ? SeaMap.OpenCourse(p, c) : P("villagepleb_sail_busy", Companion.NameOf(c));
            // A free ship by you, or else by it (one it came ashore from, say, to sail back home).
            bool takenThere = false;
            Ship ship = FreeShip(p.transform.position, out bool taken) ?? FreeShip(c.transform.position, out takenThere);
            if (ship == null) return P(taken || takenThere ? "villagepleb_sail_ship_taken" : "villagepleb_sail_no_ship", Companion.NameOf(c));
            BrainState st = Brain.Claim(c, out string why);
            if (st == null) return why;
            ZNetView sv = ship.GetComponent<ZNetView>();
            if (!sv.IsOwner()) sv.ClaimOwnership();
            ZDO hull = sv.GetZDO();
            long tag = hull.GetLong(TagKey, 0L);
            if (tag == 0L) { tag = ((long)Random.Range(1, int.MaxValue) << 31) ^ Random.Range(1, int.MaxValue); hull.Set(TagKey, tag); }
            string prefab = Utils.GetPrefabName(ship.gameObject);
            if (!Companion.Write(c, z => { z.Set(ShipKey, tag); z.Set(PrefabKey, prefab); z.Set(SetKey, false); })) return P("villagepleb_menu_busy");
            hull.Set(ByKey, Companion.IdOf(c));
            hull.Set(ByNameKey, Companion.NameOf(c));
            Ride.Leave(st);
            st.Helm = ship;
            st.HelmSince = Time.time;
            Seat(st, ship);
            SeaChart.Ensure();
            Talk.Say(c, P("villagepleb_sail_say_at_helm"));
            Activity.Log(c, $"took the helm of {prefab}");
            SeaMap.Open(p, c, ship);
            return null;
        }

        // Whether a course ends at its home (sent home, or to somewhere within reach of it).
        private static bool EndsHome(ZDO body, SeaRoute route) =>
            Remote.Home(body) is Vector3 home && Vector3.Distance(new Vector3(route.End.x, 0f, route.End.z), new Vector3(home.x, 0f, home.z)) < body.GetInt(Keys.Radius, 30) + 100f;

        // A companion's id from its record (as Companion.IdOf gives it for one that is loaded).
        public static long IdOf(ZDO z) { long id = z?.GetLong(Keys.Id, 0L) ?? 0L; return id != 0L || z == null ? id : z.m_uid.ID; }

        public static bool SetSail(Humanoid c, Ship ship, SeaRoute route, bool home = false, string dock = null)
        {
            Player p = Player.m_localPlayer;
            ZDO hull = ZdoOf(ship);
            if (p == null || c == null || c.IsDead() || hull == null || route == null || !AtHelm(c)) return false;
            Vector3 to = route.End;
            home |= EndsHome(Companion.Zdo(c), route);
            if (!Companion.Write(c, z => { z.Set(ToKey, to); z.Set(SetKey, true); z.Set(DockKey, false); z.Set(HomeKey, home); z.Set(DockNameKey, home ? "" : dock ?? ""); })) return false;
            ZNetView sv = ship.GetComponent<ZNetView>();
            if (!sv.IsOwner()) sv.ClaimOwnership();
            hull.Set(ZDOVars.s_user, p.GetPlayerID());
            var v = new Voyage { Id = Companion.IdOf(c), Tag = hull.GetLong(TagKey, 0L), Prefab = Utils.GetPrefabName(ship.gameObject), To = to, Body = Companion.Zdo(c), Hull = hull, Ship = ship, Route = route, Home = home };
            Voyages[v.Id] = v;
            string km = (route.Length / 1000f).ToString("0.0");
            Journal.Add(c, P("villagepleb_sail_journal_set", km));
            Talk.Say(c, P("villagepleb_sail_say_setting", km));
            Activity.Log(c, $"set sail: {route.Length:0} m to {to}");
            return true;
        }

        // Away, a new course from where the ship is now (on the game that runs the voyage): under way, or setting off again from where it waited.
        public static bool ChangeCourse(Humanoid c, SeaRoute route, bool home = false, string dock = null) => ChangeCourse(Companion.Zdo(c), c, route, home, dock);

        // c may be null: a companion too far away to be loaded, changed through its record.
        public static bool ChangeCourse(ZDO body, Humanoid c, SeaRoute route, bool home = false, string dock = null)
        {
            if (body == null || route == null || !Away(body)) return false;
            long id = IdOf(body);
            if (!Voyages.TryGetValue(id, out Voyage v))
            {
                ZDO hull = HullOf(body);
                if (hull == null) return false;
                v = new Voyage { Id = id, Tag = body.GetLong(ShipKey, 0L), Prefab = body.GetString(PrefabKey, ""), Body = body, Hull = hull };
            }
            Vector3 to = route.End;
            home |= EndsHome(body, route);
            System.Action<ZDO> write = z => { z.Set(ToKey, to); z.Set(SetKey, true); z.Set(DockKey, false); z.Set(HomeKey, home); z.Set(DockNameKey, home ? "" : dock ?? ""); };
            if (c != null) { if (!Companion.Write(c, write)) return false; }
            else
            {
                if (!body.IsOwner()) body.SetOwner(ZDOMan.GetSessionID());
                write(body);
            }
            Voyages[v.Id] = v;
            v.Home = home;
            v.To = to; v.Route = route; v.Progress = 0f; v.Best = 0f; v.Fails = 0;
            v.Backing = false; v.Landing = false; v.StuckFor = -10f; v.NoHeadway = 0f; v.Tries = 0;
            string km = (route.Length / 1000f).ToString("0.0");
            if (c != null)
            {
                Journal.Add(c, P("villagepleb_sail_journal_changed", km));
                Talk.Say(c, P("villagepleb_sail_say_changed", km));
                Activity.Log(c, $"changed course: {route.Length:0} m to {to}");
            }
            return true;
        }

        // Where the other voyages this game runs will stop, so two ships sent to one dock take different berths.
        public static IEnumerable<Vector3> BerthsTaken(long except) => Voyages.Values.Where(v => v.Id != except).Select(v => v.To).ToList();

        // The ship this companion has the helm of, if it is loaded here.
        public static Ship ShipOf(Humanoid c) => ShipOf(Companion.Zdo(c));

        public static Ship ShipOf(ZDO body)
        {
            long tag = body?.GetLong(ShipKey, 0L) ?? 0L;
            return tag == 0L ? null : Ships().FirstOrDefault(s => TagOf(s) == tag);
        }

        private static readonly Dictionary<long, KeyValuePair<ZDO, float>> HullSeen = new Dictionary<long, KeyValuePair<ZDO, float>>();

        // Where its ship is, loaded or not (from the voyage this game runs, or the ship's record), and which way it heads.
        public static Vector3? ShipAt(Humanoid c, out float yaw) => ShipAt(Companion.Zdo(c), out yaw);

        public static Vector3? ShipAt(ZDO body, out float yaw)
        {
            yaw = 0f;
            if (body == null) return null;
            long tag = body.GetLong(ShipKey, 0L);
            Ship s = tag == 0L ? null : Ships().FirstOrDefault(x => TagOf(x) == tag);
            if (s != null) { yaw = s.transform.eulerAngles.y; return s.transform.position; }
            long id = IdOf(body);
            ZDO hull = Voyages.TryGetValue(id, out Voyage v) ? v.Hull : null;
            if (!Exists(hull))
            {
                if (!HullSeen.TryGetValue(id, out var seen) || Time.time - seen.Value > 2f) HullSeen[id] = seen = new KeyValuePair<ZDO, float>(HullOf(body), Time.time);
                hull = seen.Key;
            }
            if (!Exists(hull)) return null;
            yaw = hull.GetRotation().eulerAngles.y;
            return hull.GetPosition();
        }

        // Go home, given while it waits aboard: it sails home (the chart opens just long enough to plot the way).
        public static bool HomeBySea(Humanoid c)
        {
            Player p = Player.m_localPlayer;
            if (p == null || c == null || !Companion.IsMine(c, p)) return false;
            if (!Work.HasHome(c)) { VillagePlebsPlugin.Tell(P("villagepleb_sail_no_home", Companion.NameOf(c))); return false; }
            if (VillagePlebsPlugin.MenuOpen) VillagePlebsPlugin.CloseFromEscape();
            string why = SeaMap.IsOpen ? null : SeaMap.OpenCourse(p, c);
            if (why != null) { VillagePlebsPlugin.Tell(why); return false; }
            SeaMap.HeadHome();
            return true;
        }

        // Its player, aboard, takes the helm: the voyage (or the wait aboard) is over, and it rides along with them as it would anyway.
        public static bool HandOver(Ship ship, Player p)
        {
            ZDO hull = ZdoOf(ship);
            long by = hull?.GetLong(ByKey, 0L) ?? 0L;
            ZDO body = by != 0L ? Remote.Find(by) : null;
            if (p == null || body == null || body.GetLong(Keys.Master, 0L) != p.GetPlayerID() || p.GetStandingOnShip() != ship) return false;
            Voyages.Remove(by);
            Humanoid c = ZNetScene.instance.FindInstance(body)?.GetComponent<Humanoid>();
            if (SeaMap.IsOpenFor(c)) SeaMap.Close();
            if (!body.IsOwner()) body.SetOwner(ZDOMan.GetSessionID());
            body.Set(SetKey, false);
            body.Set(DockKey, false);
            body.Set(HomeKey, false);
            body.Set(ShipKey, 0L);
            body.Set(Keys.Order, (int)Order.Follow);
            if (ship.IsOwner()) { SpeedOf(ship) = Ship.Speed.Stop; RudderOf(ship) = 0f; }
            Unmark(hull);
            string name = body.GetString(Keys.Name, "");
            VillagePlebsPlugin.Tell(P("villagepleb_sail_handover_tell", name));
            if (c != null) { Release(Brain.Get(c)); Talk.Say(c, P("villagepleb_sail_say_handover")); Activity.Log(c, "handed the helm to " + p.GetPlayerName()); }
            return true;
        }

        // Before it sets sail: gets up from the helm and comes back to you.
        public static void StepDown(Humanoid c, Ship ship)
        {
            if (c == null || Away(c)) return;
            Companion.Write(c, z => { z.Set(ShipKey, 0L); z.Set(SetKey, false); });
            Unmark(ZdoOf(ship));
            Release(Brain.Get(c));
            Player p = Player.m_localPlayer;
            if (p != null && Companion.IsMine(c, p) && !c.IsDead()) { Home.Follow(c, false); Brain.TeleportBehind(c, p, "villagepleb_brain_stepped_off_boat"); }
        }

        private static void Unmark(ZDO hull)
        {
            if (hull == null) return;
            if (!hull.IsOwner()) hull.SetOwner(ZDOMan.GetSessionID());
            hull.Set(ByKey, 0L);
            if (Player.m_localPlayer != null && hull.GetLong(ZDOVars.s_user, 0L) == Player.m_localPlayer.GetPlayerID()) hull.Set(ZDOVars.s_user, 0L);
        }

        // ---- the companion at the helm (its brain, on whichever game runs it) ----

        public static bool Tick(BrainState st)
        {
            Humanoid me = st.Body;
            ZDO z = Companion.Zdo(me);
            long tag = z != null ? z.GetLong(ShipKey, 0L) : 0L;
            if (tag == 0L) { if (!ReferenceEquals(st.Helm, null)) Release(st); return false; }
            bool underway = z.GetBool(SetKey, false), docked = z.GetBool(DockKey, false);
            if (st.Helm == null || TagOf(st.Helm) != tag)
            {
                st.Helm = Ships().FirstOrDefault(s => TagOf(s) == tag);
                if (st.HelmSince == 0f) st.HelmSince = Time.time;
            }
            // The chart was closed some other way before it set sail (you logged out, the game crashed): it gets up again.
            if (!underway && !docked && !SeaMap.IsOpenFor(me) && Time.time - st.HelmSince > 5f) { StepDown(me, st.Helm); return false; }
            st.Ai.StopMoving();
            if (st.Helm == null)
            {
                // Waiting aboard a ship that is no more (wrecked, or taken down): it is free again, where it is.
                if (docked && Time.time >= st.NextHelmLook)
                {
                    st.NextHelmLook = Time.time + 5f;
                    if (HullOf(z) == null && Time.time - st.HelmSince > 20f)
                    {
                        Companion.Write(me, w => { w.Set(ShipKey, 0L); w.Set(DockKey, false); w.Set(Keys.Order, (int)Order.Stay); });
                        Release(st);
                        return false;
                    }
                }
                Brain.Status(st, P(docked ? "villagepleb_sail_status_docked" : "villagepleb_sail_status_away"));
                return true;
            }
            Seat(st, st.Helm);
            if (underway)
            {
                Vector3 d = z.GetVec3(ToKey, Vector3.zero) - st.Helm.transform.position;
                d.y = 0f;
                Brain.Status(st, P("villagepleb_sail_status", (d.magnitude / 1000f).ToString("0.0")));
            }
            else Brain.Status(st, P(docked ? "villagepleb_sail_status_docked" : "villagepleb_sail_status_helm"));
            return true;
        }

        private static void Seat(BrainState st, Ship ship)
        {
            Humanoid me = st.Body;
            Transform at = ship.m_shipControlls.m_attachPoint;
            me.transform.SetPositionAndRotation(at.position, at.rotation);
            Rigidbody body = me.GetComponent<Rigidbody>();
            if (body != null)
            {
                if (!body.isKinematic) { body.linearVelocity = Vector3.zero; body.isKinematic = true; }
                body.detectCollisions = false;
                body.position = at.position;
                body.rotation = at.rotation;
            }
            GameFields.Anim(me)?.SetBool(ship.m_shipControlls.m_attachAnimation, true);
        }

        private static void Release(BrainState st)
        {
            if (st == null) return;
            Humanoid me = st.Body;
            if (!ReferenceEquals(st.Helm, null) && me != null)
            {
                if (st.Helm != null) GameFields.Anim(me)?.SetBool(st.Helm.m_shipControlls.m_attachAnimation, false);
                Rigidbody body = me.GetComponent<Rigidbody>();
                if (body != null) { body.isKinematic = false; body.detectCollisions = true; }
                me.transform.position += Vector3.up * 0.3f;
            }
            st.Helm = null;
            st.HelmSince = 0f;
        }

        // ---- the voyage (on the game of the companion's player) ----

        public static void Update(Player p, float dt)
        {
            SeaChart.Tick();
            Docks.Tick();
            SeaMap.Tick();
            if (p == null || ZNetScene.instance == null || ZDOMan.instance == null) { Voyages.Clear(); return; }
            if (Time.time >= _nextLook) { _nextLook = Time.time + 5f; Resume(p); }
            foreach (Voyage v in Voyages.Values.ToList()) Step(p, v, dt);
        }

        // Voyages left running when you last played (or set sail by you on another machine) carry on.
        private static void Resume(Player p)
        {
            var unknown = new List<long>();
            foreach (Profile prof in Profile.Here(p))
            {
                if (prof.Dead) continue;
                ZDO z = Remote.Find(prof.Id);
                if (z == null || ZNetScene.instance.FindInstance(z) == null) unknown.Add(prof.Id);
                if (Voyages.ContainsKey(prof.Id) || !Underway(z)) continue;
                Voyages[prof.Id] = new Voyage { Id = prof.Id, Tag = z.GetLong(ShipKey, 0L), Prefab = z.GetString(PrefabKey, ""), To = z.GetVec3(ToKey, Vector3.zero), Body = z, Home = z.GetBool(HomeKey, false) };
            }
            // Ones too far away to be loaded: the host sends where they are (for the map), and the ships of any away sailing.
            if (unknown.Count > 0 && Time.time >= _nextAsk) { _nextAsk = Time.time + 30f; Net.AskFor(unknown); }
        }

        private static bool Exists(ZDO z) => z != null && ZDOMan.instance.GetZDO(z.m_uid) != null;

        private static ZDO FindHull(Voyage v) => FindHull(v.Prefab, v.Tag);

        // The ship a companion's voyage is on, from what its own record says.
        public static ZDO HullOf(ZDO body) => body == null ? null : FindHull(body.GetString(PrefabKey, ""), body.GetLong(ShipKey, 0L));

        private static ZDO FindHull(string prefab, long tag)
        {
            if (string.IsNullOrEmpty(prefab) || tag == 0L) return null;
            var list = new List<ZDO>();
            int index = 0;
            for (int guard = 0; guard < 1000 && !ZDOMan.instance.GetAllZDOsWithPrefabIterative(prefab, list, ref index); guard++) { }
            return list.FirstOrDefault(z => z != null && z.GetLong(TagKey, 0L) == tag);
        }

        private static void Step(Player p, Voyage v, float dt)
        {
            ZDO body = Remote.Find(v.Id);
            if (body != null) v.Body = body;
            if (!Exists(v.Body))
            {
                // Gone (it died at sea): the ship stops where it is.
                if (v.GoneSince == 0f) v.GoneSince = Time.time;
                if (Time.time - v.GoneSince < 15f) return;
                if (v.Ship != null) SpeedOf(v.Ship) = Ship.Speed.Stop;
                if (Exists(v.Hull)) Unmark(v.Hull);
                Voyages.Remove(v.Id);
                return;
            }
            v.GoneSince = 0f;
            if (!Underway(v.Body)) { Voyages.Remove(v.Id); return; }

            if (!Exists(v.Hull))
            {
                v.Hull = null; v.Ship = null;
                if (Time.time >= v.NextFind) { v.NextFind = Time.time + 3f; v.Hull = FindHull(v); }
                if (v.Hull == null)
                {
                    if (v.MissingSince == 0f) v.MissingSince = Time.time;
                    else if (Time.time - v.MissingSince > 20f) End(v, P("villagepleb_sail_ship_lost"), true);
                    return;
                }
            }
            v.MissingSince = 0f;
            ZNetView go = ZNetScene.instance.FindInstance(v.Hull);
            v.Ship = go != null ? go.GetComponent<Ship>() : null;
            Vector3 at = v.Ship != null ? v.Ship.transform.position : v.Hull.GetPosition();

            if (v.Route == null)
            {
                if (!v.Planning && Time.time >= v.NextPlan) Replan(v, at);
                return;
            }
            if (v.Ship != null)
            {
                ZNetView sv = v.Ship.GetComponent<ZNetView>();
                if (!sv.IsOwner() && Time.time >= v.NextClaim) { v.NextClaim = Time.time + 1f; sv.ClaimOwnership(); }
                if (sv.IsOwner() && v.Hull.GetLong(ZDOVars.s_user, 0L) != p.GetPlayerID()) v.Hull.Set(ZDOVars.s_user, p.GetPlayerID());
                // The ship is here but the companion is not loaded yet: it turns up at the helm.
                if (ZNetScene.instance.FindInstance(v.Body) == null && Time.time >= v.NextMove)
                {
                    v.NextMove = Time.time + 1f;
                    if (!v.Body.IsOwner()) v.Body.SetOwner(ZDOMan.GetSessionID());
                    v.Body.SetPosition(v.Ship.m_shipControlls.m_attachPoint.position);
                }
                return;
            }
            Away(v, dt);
        }

        private static void Replan(Voyage v, Vector3 from)
        {
            SeaChart.Ensure();
            if (!SeaChart.Ready) { v.NextPlan = Time.time + 2f; return; }
            v.Planning = true;
            float hull = Hull(v.Ship != null ? v.Ship : ShipPrefab(v.Prefab));
            SeaChart.Plan(from, v.To, hull, (route, why) =>
            {
                v.Planning = false;
                v.NextPlan = Time.time + 3f;
                if (route != null) { v.Route = route; v.Progress = 0f; v.Best = 0f; v.Fails = 0; return; }
                v.Fails++;
                // A course it has already got is kept; with none at all, after a few tries, it gives up and waits where it is.
                if (v.Route == null && v.Fails >= 5) End(v, P(why), false);
            });
        }

        // No one is near the ship, so the game is not running it: it goes on along the course at a steady sailing speed.
        private static void Away(Voyage v, float dt)
        {
            v.Progress = Mathf.Min(v.Route.Length, v.Progress + AwaySpeed * dt);
            bool there = v.Progress >= v.Route.Length - 1f;
            if (Time.time < v.NextMove && !there) return;
            v.NextMove = Time.time + 0.5f;
            Vector3 pt = v.Route.PointAt(v.Progress), dir = v.Route.PointAt(v.Progress + 10f) - pt;
            if (!v.Hull.IsOwner()) v.Hull.SetOwner(ZDOMan.GetSessionID());
            pt.y = v.Hull.GetPosition().y;
            v.Hull.SetPosition(pt);
            if (dir.sqrMagnitude > 0.01f) v.Hull.SetRotation(Quaternion.LookRotation(new Vector3(dir.x, 0f, dir.z)));
            if (!v.Body.IsOwner()) v.Body.SetOwner(ZDOMan.GetSessionID());
            v.Body.SetPosition(pt + Vector3.up * 1.5f);
            ZNetView seen = ZNetScene.instance.FindInstance(v.Body);
            if (seen != null) seen.transform.position = pt + Vector3.up * 1.5f;
            if (there && v.Home) { Vector3? shore = Shore(pt, HomeOf(v) ?? pt, 250f); if (shore != null) { Ashore(v, shore.Value); return; } }
            if (there) End(v, null, v.Home);
        }

        // Arrived (why is null) or stopped for good. Home (or with its ship lost), it steps off and gets on with its life: back at home
        // it carries on with its orders and missions. Anywhere else it waits aboard, at the helm, until it is sent somewhere new or home.
        private static void End(Voyage v, string why, bool off)
        {
            Voyages.Remove(v.Id);
            Ship ship = v.Ship;
            if (ship != null && ship.IsOwner()) { SpeedOf(ship) = Ship.Speed.Stop; RudderOf(ship) = 0f; }
            Vector3 spot = ship != null ? ship.m_shipControlls.m_attachPoint.position : v.Body.GetPosition();
            string name = v.Body.GetString(Keys.Name, "");
            bool home = off && why == null;
            if (!v.Body.IsOwner()) v.Body.SetOwner(ZDOMan.GetSessionID());
            v.Body.Set(SetKey, false);
            v.Body.Set(HomeKey, false);
            v.Body.Set(DockKey, !off);
            if (off)
            {
                v.Body.Set(ShipKey, 0L);
                v.Body.Set(Keys.Order, (int)(home ? Order.Gather : Order.Stay));
                if (!home) v.Body.Set(Keys.Post, spot);
                if (Exists(v.Hull)) Unmark(v.Hull);
            }
            else if (Exists(v.Hull) && Player.m_localPlayer != null && v.Hull.GetLong(ZDOVars.s_user, 0L) == Player.m_localPlayer.GetPlayerID())
            {
                if (!v.Hull.IsOwner()) v.Hull.SetOwner(ZDOMan.GetSessionID());
                v.Hull.Set(ZDOVars.s_user, 0L);
            }
            Humanoid c = ZNetScene.instance.FindInstance(v.Body)?.GetComponent<Humanoid>();
            if (home)
            {
                VillagePlebsPlugin.Tell(P("villagepleb_sail_home_arrived", name));
                if (c != null) { Talk.Say(c, P("villagepleb_sail_say_home")); Journal.Add(c, P("villagepleb_sail_journal_home")); Activity.Log(c, "sailed home"); }
            }
            else if (why == null)
            {
                // At a dock of yours it says so; anywhere else the spot is kept as a new dock, to send ships back to.
                string dock = v.Body.GetString(DockNameKey, "");
                Player me = Player.m_localPlayer;
                if (dock.Length == 0 && me != null && v.Body.GetLong(Keys.Master, 0L) == me.GetPlayerID())
                {
                    Docks.Dock near = Docks.Near(me, v.To, Docks.Radius + 20f);
                    if (near == null) { near = Docks.Add(me, null, v.To); VillagePlebsPlugin.Tell(P("villagepleb_sail_dock_saved", near.Name)); }
                    dock = near.Name;
                }
                v.Body.Set(DockNameKey, dock);
                VillagePlebsPlugin.Tell(dock.Length > 0 ? P("villagepleb_sail_docked_at", name, dock) : P("villagepleb_sail_arrived", name));
                if (c != null) { Talk.Say(c, P("villagepleb_sail_say_arrived")); Journal.Add(c, P("villagepleb_sail_journal_arrived")); Activity.Log(c, "arrived by ship"); }
            }
            else
            {
                VillagePlebsPlugin.Tell(P("villagepleb_sail_ended", name, why));
                if (c != null) Activity.Log(c, "voyage ended: " + why);
            }
        }

        // ---- coming ashore at home ----

        private static Vector3? HomeOf(Voyage v) => v.Body != null ? Remote.Home(v.Body) : null;

        // Height of the ground (or a pier) at a spot: the loaded world where it is loaded, the world seed where it is not.
        private static float Ground(Vector3 p)
        {
            if (ZoneSystem.instance != null && ZoneSystem.instance.GetSolidHeight(p, out float h)) return h;
            return WorldGenerator.instance != null ? WorldGenerator.instance.GetHeight(p.x, p.z) : 0f;
        }

        // Dry land within reach of a spot, the nearest to home first: straight towards home, then all round.
        private static Vector3? Shore(Vector3 from, Vector3 home, float within)
        {
            float dry = SeaChart.WaterLevel + 0.4f;
            Vector3 dir = home - from;
            dir.y = 0f;
            if (dir.sqrMagnitude > 0.01f)
            {
                dir.Normalize();
                for (float d = 4f; d <= within; d += 3f)
                {
                    Vector3 p = from + dir * d;
                    float h = Ground(p);
                    if (h > dry) return new Vector3(p.x, h, p.z);
                }
            }
            Vector3? best = null;
            float bestD = float.MaxValue;
            for (float r = 6f; r <= within && best == null; r += 6f)
                for (int k = 0; k < 16; k++)
                {
                    float a = k * Mathf.PI / 8f;
                    Vector3 p = from + new Vector3(Mathf.Sin(a), 0f, Mathf.Cos(a)) * r;
                    float h = Ground(p);
                    if (h <= dry) continue;
                    float d = Vector3.Distance(p, home);
                    if (d < bestD) { bestD = d; best = new Vector3(p.x, h, p.z); }
                }
            return best;
        }

        // Home: the ship creeps in towards the shore, as close as its draft lets it, and then the companion steps ashore.
        private static void Land(Voyage v, Ship ship, Rigidbody body, Vector3 pos, Vector3 fwd, float spd, float dt)
        {
            if (!v.Landing) { v.Landing = true; v.LandingSince = Time.time; }
            Vector3 home = HomeOf(v) ?? v.To;
            if (Time.time >= v.NextShore) { v.NextShore = Time.time + 0.5f; v.ShoreSeen = Shore(pos, home, StepAshore); }
            Vector3? shore = v.ShoreSeen;
            if (shore != null && Mathf.Abs(spd) < 2f) { Ashore(v, shore.Value); return; }
            float water = SeaChart.WaterLevel, half = HalfLength(ship);
            bool shallow = Ground(pos + fwd * (half + 4f)) > water - Draft || Ground(pos + fwd * (half + 12f)) > water - Draft;
            float angle = Vector3.SignedAngle(fwd, new Vector3(home.x - pos.x, 0f, home.z - pos.z), Vector3.up);
            AvoidShips(ship, pos, fwd, out float swerve);
            angle = Mathf.Clamp(angle + swerve, -180f, 180f);
            SpeedOf(ship) = shallow || shore != null ? Ship.Speed.Stop : Ship.Speed.Slow;
            Steer(ship, angle / 35f - body.angularVelocity.y * Mathf.Rad2Deg / 60f, dt);
            if (spd > 2.5f) body.AddForce(-fwd * (Mathf.Min(spd - 2.5f, 2f) * dt), ForceMode.VelocityChange);
            // As close as it can get (or it has tried for a minute): it goes the rest of the way to the nearest dry land.
            if ((shallow && Mathf.Abs(spd) < 1f) || Time.time - v.LandingSince > 60f)
            {
                shore = Shore(pos, home, 250f);
                if (shore != null) Ashore(v, shore.Value);
                else End(v, null, false);
            }
        }

        private static void Ashore(Voyage v, Vector3 spot)
        {
            End(v, null, true);
            spot += Vector3.up * 0.3f;
            if (!v.Body.IsOwner()) v.Body.SetOwner(ZDOMan.GetSessionID());
            v.Body.SetPosition(spot);
            Humanoid c = ZNetScene.instance.FindInstance(v.Body)?.GetComponent<Humanoid>();
            if (c == null) return;
            Release(Brain.Get(c));
            c.transform.position = spot;
            Rigidbody b = c.GetComponent<Rigidbody>();
            if (b != null) { b.position = spot; b.linearVelocity = Vector3.zero; }
        }

        // Leviathans: a ship that brushes one is thrown about or sunk. They are looked for every two seconds, kept off the course with a
        // wide berth on the chart, and steered round when one is close ahead.
        private const float LeviathanRoom = 32f;
        private static readonly List<Leviathan> Leviathans = new List<Leviathan>();
        private static readonly HashSet<int> Charted = new HashSet<int>();
        private static float _nextLeviathans;

        private static IEnumerable<Leviathan> LeviathansNear(Vector3 pos)
        {
            if (Time.time >= _nextLeviathans)
            {
                _nextLeviathans = Time.time + 2f;
                Leviathans.Clear();
                Leviathans.AddRange(Object.FindObjectsByType<Leviathan>(FindObjectsSortMode.None));
            }
            return Leviathans.Where(l => l != null && Vector3.Distance(l.transform.position, pos) < 300f);
        }

        // Marks those near on the chart (once each) and asks for a new course if the current one passes too close to one.
        private static void ChartLeviathans(Voyage v, Vector3 pos)
        {
            bool near = false;
            foreach (Leviathan l in LeviathansNear(pos))
            {
                Vector3 at = l.transform.position;
                if (Charted.Add(l.GetInstanceID()))
                    for (float dx = -LeviathanRoom; dx <= LeviathanRoom; dx += 8f)
                        for (float dz = -LeviathanRoom; dz <= LeviathanRoom; dz += 8f)
                            if (dx * dx + dz * dz <= LeviathanRoom * LeviathanRoom) SeaChart.MarkBlocked(at + new Vector3(dx, 0f, dz));
                for (float s = 0f; s <= 300f && v.Progress + s <= v.Route.Length; s += 10f)
                {
                    Vector3 q = v.Route.PointAt(v.Progress + s);
                    if (new Vector2(q.x - at.x, q.z - at.z).magnitude < LeviathanRoom + 6f) { near = true; break; }
                }
            }
            if (near && !v.Planning && Time.time >= v.NextPlan) Replan(v, pos);
        }

        // Other ships (and leviathans) in its way: how far ahead the nearest one is, and how many degrees to turn to pass it (away from the side it is on).
        private static float AvoidShips(Ship ship, Vector3 pos, Vector3 fwd, out float swerve)
        {
            swerve = 0f;
            float nearest = float.MaxValue, half = HalfLength(ship), beam = HalfBeam(ship);
            Vector3 right = Vector3.Cross(Vector3.up, fwd);
            var others = Ships().Where(o => o != ship).Select(o => (o.transform.position, Mathf.Max(HalfLength(o), HalfBeam(o))))
                                .Concat(LeviathansNear(pos).Select(l => (l.transform.position, LeviathanRoom)));
            foreach (var (at, size) in others)
            {
                Vector3 d = at - pos;
                d.y = 0f;
                float ahead = Vector3.Dot(d, fwd) - half, side = Vector3.Dot(d, right);
                float room = beam + size + 8f;
                if (ahead < -half || ahead > 100f + size || Mathf.Abs(side) > room) continue;
                nearest = Mathf.Min(nearest, Mathf.Max(0f, ahead));
                float push = Mathf.Lerp(60f, 15f, Mathf.Clamp01(ahead / 100f)) * (1f - Mathf.Abs(side) / room * 0.5f);
                swerve += side >= 0f ? -push : push;
            }
            swerve = Mathf.Clamp(swerve, -70f, 70f);
            return nearest;
        }

        // ---- the helm (in the ship's own physics step, on the game that owns the ship) ----

        public static Voyage PilotOf(Ship s)
        {
            if (Voyages.Count == 0 || s == null) return null;
            foreach (Voyage v in Voyages.Values) if (v.Ship == s) return v;
            return null;
        }

        // A ship left held still (its voyage ended, or was handed over, while it was being worked clear): free again before the game moves it,
        // or the game's own physics would set the speed of a kinematic body every step.
        public static void LetGo(Ship s)
        {
            if (Held.Count == 0 || !Held.Contains(s)) return;
            Voyage v = PilotOf(s);
            if (v != null && v.Clearing > 0f) return;
            Held.Remove(s);
            Rigidbody b = BodyOf(s);
            if (b != null && b.isKinematic) b.isKinematic = false;
        }

        // The game stops a ship with no player aboard: while a companion sails it, you count as aboard for that.
        public static bool Crew(Ship s, bool add)
        {
            Player me = Player.m_localPlayer;
            List<Player> list = Aboard(s);
            if (me == null || list == null) return false;
            if (!add) { list.Remove(me); return false; }
            if (list.Contains(me)) return false;
            list.Add(me);
            return true;
        }

        private static void Steer(Ship s, float to, float dt) =>
            RudderOf(s) = Mathf.MoveTowards(RudderOf(s), Mathf.Clamp(to, -1f, 1f), Mathf.Max(0.5f, s.m_rudderSpeed) * 1.5f * dt);

        // Steers and trims the sail for the course. Returns false while it works the ship clear by hand (the game's own physics then waits).
        public static bool Drive(Voyage v, Ship ship, float dt)
        {
            Rigidbody body = BodyOf(ship);
            if (v.Route == null || body == null) { SpeedOf(ship) = Ship.Speed.Stop; return true; }
            if (v.Clearing > 0f) return !WorkClear(v, ship, body, dt);
            Vector3 pos = ship.transform.position, fwd = ship.transform.forward;
            fwd.y = 0f;
            fwd.Normalize();
            float spd = Vector3.Dot(body.linearVelocity, fwd);
            v.Progress = v.Route.Project(pos, v.Progress, 120f + Mathf.Abs(spd) * 4f, out float off);
            float left = v.Route.Length - v.Progress;
            if (v.Landing || (left <= ArriveAt && v.Home)) { Land(v, ship, body, pos, fwd, spd, dt); return true; }
            if (left <= ArriveAt) { End(v, null, false); return true; }
            if (off > 60f && !v.Planning && Time.time >= v.NextPlan) Replan(v, pos);
            if (Time.time >= v.NextScan)
            {
                v.NextScan = Time.time + 0.3f;
                Scan(v, ship, body, pos, fwd, spd);
                if (Time.time >= v.NextLeviathans) { v.NextLeviathans = Time.time + 2f; ChartLeviathans(v, pos); }
            }

            Vector3 aim = v.Route.PointAt(v.Progress + Mathf.Clamp(Mathf.Abs(spd) * 3.5f, 14f, 60f));
            float angle = Vector3.SignedAngle(fwd, new Vector3(aim.x - pos.x, 0f, aim.z - pos.z), Vector3.up);
            float turning = body.angularVelocity.y * Mathf.Rad2Deg;

            if (v.Backing)
            {
                v.BackFor += dt;
                SpeedOf(ship) = Ship.Speed.Back;
                Steer(ship, angle > 0f ? -1f : 1f, dt);
                if (v.BackFor > 7f || (v.BackFor > 3f && Mathf.Abs(angle) < 30f))
                {
                    v.Backing = false;
                    v.StuckFor = -6f;
                    if (!v.Planning) Replan(v, pos);
                }
                return true;
            }

            float near = AvoidShips(ship, pos, fwd, out float swerve);
            angle = Mathf.Clamp(angle + swerve, -180f, 180f);
            float blocked = Time.time - v.BlockSeen < 1f ? v.BlockAhead : float.MaxValue;
            float room = SeaChart.ClearAt(pos);
            for (float s = 20f; s <= 80f; s += 20f) room = Mathf.Min(room, SeaChart.ClearAt(v.Route.PointAt(v.Progress + s)));
            Ship.Speed want = Ship.Speed.Full;
            if (Mathf.Abs(angle) > 35f || room < 30f || left < 120f || near < 70f || blocked < 50f) want = Ship.Speed.Half;
            if (Mathf.Abs(angle) > 75f || room < 12f || left < 45f || near < 30f || blocked < 25f) want = Ship.Speed.Slow;
            SpeedOf(ship) = want;
            Steer(ship, angle / 35f - turning / 60f, dt);
            float cap = Mathf.Max(2.5f, left * 0.12f);
            if (Mathf.Abs(angle) > 40f) cap = Mathf.Min(cap, 5f);
            // Shallows, rocks or a ship close ahead: it slows right down while the course goes round them.
            if (near < 30f || blocked < 25f) cap = Mathf.Min(cap, 1.5f);
            if (spd > cap + 1f) body.AddForce(-fwd * (Mathf.Min(spd - cap, 3f) * dt), ForceMode.VelocityChange);

            bool still = Mathf.Abs(spd) < 0.5f && Mathf.Abs(turning) < 3f;
            v.StuckFor = still ? v.StuckFor + dt : Mathf.Min(v.StuckFor, 0f);
            if (v.Progress > v.Best + 5f) { v.Best = v.Progress; v.NoHeadway = 0f; }
            else v.NoHeadway += dt;
            if (v.StuckFor > 6f || v.NoHeadway > 45f)
            {
                v.StuckFor = 0f;
                v.NoHeadway = 0f;
                if ((pos - v.TriedAt).sqrMagnitude > 900f) { v.Tries = 0; v.TriedAt = pos; }
                v.Tries++;
                float half = HalfLength(ship);
                for (float d = 4f; d <= 20f; d += 8f) SeaChart.MarkBlocked(pos + fwd * (half + d));
                if (v.Tries >= 3) { v.Tries = 0; v.Clearing = 20f; }
                else { v.Backing = true; v.BackFor = 0f; }
            }
            return true;
        }

        // Stuck three times over in one spot: the ship is eased along its course by hand, through whatever holds it, until it has open water.
        private static bool WorkClear(Voyage v, Ship ship, Rigidbody body, float dt)
        {
            v.Clearing -= dt;
            if (!body.isKinematic) { body.isKinematic = true; Held.Add(ship); }
            SpeedOf(ship) = Ship.Speed.Stop;
            RudderOf(ship) = 0f;
            v.Progress = Mathf.Min(v.Route.Length, v.Progress + 3f * dt);
            Vector3 pos = body.position, to = v.Route.PointAt(v.Progress), dir = v.Route.PointAt(v.Progress + 20f) - to;
            body.MovePosition(new Vector3(to.x, pos.y, to.z));
            if (dir.sqrMagnitude > 0.01f)
            {
                Vector3 e = body.rotation.eulerAngles;
                float yaw = Mathf.MoveTowardsAngle(e.y, Quaternion.LookRotation(new Vector3(dir.x, 0f, dir.z)).eulerAngles.y, 30f * dt);
                body.MoveRotation(Quaternion.Euler(e.x, yaw, e.z));
            }
            bool open = SeaChart.ClearAt(to) >= Hull(ship) + 10f && v.Clearing < 17f;
            if (v.Clearing > 0f && !open && v.Progress < v.Route.Length) return true;
            v.Clearing = 0f;
            body.isKinematic = false;
            Held.Remove(ship);
            body.linearVelocity = Vector3.zero;
            body.angularVelocity = Vector3.zero;
            if (!v.Planning) Replan(v, to);
            return false;
        }

        // Looks ahead for what the chart does not know about (rocks, piers, raised ground) and plans round it.
        private static void Scan(Voyage v, Ship ship, Rigidbody body, Vector3 pos, Vector3 fwd, float spd)
        {
            float water = SeaChart.WaterLevel, half = HalfLength(ship), beam = HalfBeam(ship);
            Vector3 side = Vector3.Cross(Vector3.up, fwd);
            float range = Mathf.Clamp(Mathf.Abs(spd) * 7f, 30f, 120f), ahead = float.MaxValue;
            bool found = false;
            for (float d = half + 4f; d <= half + range; d += 8f)
                for (int k = -2; k <= 2; k++)
                {
                    Vector3 o = pos + fwd * d + side * (k * beam * 0.7f);
                    bool known = SeaChart.ClearAt(o) <= 0f;
                    // From well above the tallest sea stack: a ray that starts inside a rock does not see it.
                    o.y = water + 80f;
                    // Ground, rocks or a pier less than the ship's draft below the surface: it would run aground there.
                    if (!known && !Obstructed(o, water - Draft, body)) continue;
                    if (Mathf.Abs(k) * beam * 0.7f < beam + 1f) ahead = Mathf.Min(ahead, d - half);
                    else ahead = Mathf.Min(ahead, d - half + 10f);
                    if (known) continue;
                    SeaChart.MarkBlocked(o);
                    found = true;
                }
            v.BlockAhead = ahead;
            v.BlockSeen = Time.time;
            if (found && !v.Planning && Time.time >= v.NextPlan) Replan(v, pos);
        }

        private static bool Obstructed(Vector3 from, float bottom, Rigidbody own)
        {
            int n = Physics.RaycastNonAlloc(from, Vector3.down, Hits, from.y - bottom, Solid, QueryTriggerInteraction.Ignore);
            for (int i = 0; i < n; i++)
            {
                Rigidbody r = Hits[i].collider.attachedRigidbody;
                if (r == null || r.GetComponent<Leviathan>() != null) return true;
            }
            return false;
        }
    }

    // A companion held in place aboard (at the helm, or riding along) has a kinematic body: the game's walking and swimming would
    // set its velocity every physics step, which Unity warns about.
    [HarmonyPatch(typeof(Character), "UpdateMotion")]
    internal static class Character_UpdateMotion_Aboard
    {
        private static bool Prefix(Character __instance) =>
            !(__instance is Humanoid h) || !Companion.Is(h) || !(h.GetComponent<Rigidbody>() is Rigidbody b) || !b.isKinematic;
    }

    // The game's network sync learns once, when a thing appears, whether its body is kinematic. A companion held in place aboard (or a
    // ship being worked clear) turns kinematic later, so on a game that does not own it the sync would go on setting its speed every
    // step, which Unity warns about: for companions and ships it looks again each time, and moves a held one as the game moves kinematic things.
    [HarmonyPatch(typeof(ZSyncTransform), "ClientSync")]
    internal static class ZSyncTransform_ClientSync_Held
    {
        private static void Prefix(ZSyncTransform __instance, Rigidbody ___m_body, ref bool ___m_isKinematicBody)
        {
            if (___m_body == null || ___m_body.isKinematic == ___m_isKinematicBody) return;
            if (__instance.GetComponent<Ship>() == null && !(__instance.GetComponent<Humanoid>() is Humanoid h && Companion.Is(h))) return;
            ___m_isKinematicBody = ___m_body.isKinematic;
        }
    }

    // The same at the moment the game hands it to this game: it then sets the speed it had, which a held body cannot take.
    [HarmonyPatch(typeof(ZSyncTransform), "OwnerSync")]
    internal static class ZSyncTransform_OwnerSync_Held
    {
        private static void Prefix(ZSyncTransform __instance, Rigidbody ___m_body, ref bool ___m_syncBodyVelocity, out bool __state)
        {
            __state = false;
            if (___m_body == null || !___m_body.isKinematic || !___m_syncBodyVelocity) return;
            if (__instance.GetComponent<Ship>() == null && !(__instance.GetComponent<Humanoid>() is Humanoid h && Companion.Is(h))) return;
            ___m_syncBodyVelocity = false;
            __state = true;
        }

        private static void Postfix(ref bool ___m_syncBodyVelocity, bool __state) { if (__state) ___m_syncBodyVelocity = true; }
    }

    [HarmonyPatch(typeof(Ship), nameof(Ship.CustomFixedUpdate))]
    internal static class Ship_CustomFixedUpdate_Sailing
    {
        private static bool Prefix(Ship __instance, float fixedDeltaTime, out bool __state)
        {
            __state = false;
            Sailing.LetGo(__instance);
            Sailing.Voyage v = Sailing.PilotOf(__instance);
            if (v == null || !__instance.IsOwner()) return true;
            if (!Sailing.Drive(v, __instance, fixedDeltaTime)) return false;
            if (Sailing.PilotOf(__instance) == null) return true;
            __state = Sailing.Crew(__instance, true);
            return true;
        }

        private static void Postfix(Ship __instance, bool __state) { if (__state) Sailing.Crew(__instance, false); }
    }

    // The wind is always behind a ship a companion sails: the full push the wind's strength gives, straight ahead.
    [HarmonyPatch(typeof(Ship), "GetSailForce")]
    internal static class Ship_GetSailForce_Sailing
    {
        private static bool Prefix(Ship __instance, float sailSize, ref Vector3 __result)
        {
            if (Sailing.PilotOf(__instance) == null) return true;
            float wind = EnvMan.instance != null ? EnvMan.instance.GetWindIntensity() : 0.5f;
            Vector3 target = __instance.transform.forward * (__instance.m_sailForceFactor * sailSize * Mathf.Lerp(0.6f, 1f, wind));
            ref Vector3 force = ref Sailing.SailForce(__instance);
            force = Vector3.SmoothDamp(force, target, ref Sailing.WindChange(__instance), 1f, 99f);
            __result = force;
            return false;
        }
    }

    // ...and its sail is squared to that wind, for everyone who sees it.
    [HarmonyPatch(typeof(Ship), "UpdateSail")]
    internal static class Ship_UpdateSail_Sailing
    {
        private static void Postfix(Ship __instance, float dt)
        {
            if (__instance.m_mastObject == null || !__instance.IsSailUp() || !Sailing.Marked(__instance)) return;
            Quaternion to = Quaternion.LookRotation(__instance.transform.forward, __instance.transform.up);
            __instance.m_mastObject.transform.rotation = Quaternion.RotateTowards(__instance.m_mastObject.transform.rotation, to, 60f * dt);
        }
    }

    // The game hands a ship to a player aboard when its owner is not: not while a companion steers it from here.
    [HarmonyPatch(typeof(Ship), "UpdateOwner")]
    internal static class Ship_UpdateOwner_Sailing
    {
        private static bool Prefix(Ship __instance) => Sailing.PilotOf(__instance) == null;
    }

    // A ship a companion has the helm of (sailing, about to, or waiting aboard) takes no damage: not from rocks, the sea, capsizing or
    // anything that hits it, and those hits show nothing either (unless Ships Take Damage is on). The Ashlands water still burns it.
    [HarmonyPatch(typeof(WearNTear), "RPC_Damage")]
    internal static class WearNTear_RPC_Damage_Sailing
    {
        private static bool Prefix(WearNTear __instance, HitData hit) =>
            MainConfig.shipDamage.Value || hit?.m_hitType == HitData.HitType.AshlandsOcean || !(__instance.GetComponent<Ship>() is Ship s) || !Sailing.Marked(s);
    }

    [HarmonyPatch(typeof(WearNTear), nameof(WearNTear.ApplyDamage))]
    internal static class WearNTear_ApplyDamage_Sailing
    {
        private static bool Prefix(WearNTear __instance, HitData hitData, ref bool __result)
        {
            if (MainConfig.shipDamage.Value || hitData?.m_hitType == HitData.HitType.AshlandsOcean || !(__instance.GetComponent<Ship>() is Ship s) || !Sailing.Marked(s)) return true;
            __result = false;
            return false;
        }
    }

    [HarmonyPatch(typeof(ShipControlls), nameof(ShipControlls.Interact))]
    internal static class ShipControlls_Interact_Sailing
    {
        private static bool Prefix(ShipControlls __instance, Humanoid character, bool repeat, ref bool __result)
        {
            if (repeat || !(character is Player p) || !Sailing.Manned(__instance.m_ship, out string name)) return true;
            // Its own player, standing aboard, may take over: it hands them the helm and the game's own controls take it from there.
            if (Sailing.HandOver(__instance.m_ship, p)) return true;
            VillagePlebsPlugin.Tell(P("villagepleb_sail_at_the_helm", name));
            __result = false;
            return false;
        }
    }

    [HarmonyPatch(typeof(ShipControlls), "RPC_RequestControl")]
    internal static class ShipControlls_RequestControl_Sailing
    {
        private static bool Prefix(ShipControlls __instance, long sender)
        {
            if (!Sailing.Manned(__instance.m_ship, out _)) return true;
            __instance.m_ship.GetComponent<ZNetView>().InvokeRPC(sender, "RequestRespons", false);
            return false;
        }
    }
}
