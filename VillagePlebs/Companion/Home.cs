using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using UnityEngine;
using static VillagePlebs.Shared.Translations;
using static VillagePlebs.Words;

namespace VillagePlebs
{
    internal static class Home
    {
        public const string HomeKey = "vp_home", HomeName = "vp_homename";

        public static Humanoid AssignFor;
        private static float _nextHint;

        public static void StartAssign(Humanoid c)
        {
            AssignFor = c;
            _nextHint = 0f;
        }

        public static void StopAssign(string why = null)
        {
            if (AssignFor != null && why != null) VillagePlebsPlugin.Tell(why);
            AssignFor = null;
        }

        public static long IdOn(Component piece) => Companion.Zdo(piece)?.GetLong(HomeKey, 0L) ?? 0L;

        private static bool Claim(Component piece)
        {
            ZNetView v = piece.GetComponent<ZNetView>();
            if (v == null || !v.IsValid()) return false;
            if (!v.IsOwner()) v.ClaimOwnership();
            return true;
        }

        public static void ToggleBed(Bed bed, Humanoid c)
        {
            ZDO z = Companion.Zdo(bed);
            long id = Companion.IdOf(c);
            if (z == null) return;
            if (z.GetLong(HomeKey, 0L) == id)
            {
                if (!Companion.Write(c, cz => cz.Set(Keys.HasBed, false))) { VillagePlebsPlugin.Tell(T("villagepleb_menu_busy")); return; }
                Claim(bed); z.Set(HomeKey, 0L); z.Set(HomeName, "");
                VillagePlebsPlugin.Tell(T("villagepleb_home_no_longer_sleeps", Companion.NameOf(c)));
                return;
            }
            long owner = z.GetLong(ZDOVars.s_owner, 0L);
            if (owner != 0L) { VillagePlebsPlugin.Tell(T("villagepleb_home_bed_players", Named(z.GetString(ZDOVars.s_ownerName, ""), "villagepleb_home_someone"), Companion.NameOf(c))); return; }
            if (z.GetLong(HomeKey, 0L) != 0L) { VillagePlebsPlugin.Tell(T("villagepleb_home_bed_others", Named(z.GetString(HomeName, ""), "villagepleb_home_another_companion"))); return; }
            if (!GiveBed(bed, c)) { VillagePlebsPlugin.Tell(T("villagepleb_menu_busy")); return; }
            VillagePlebsPlugin.Tell(T("villagepleb_home_bed_now", Companion.NameOf(c)));
        }

        internal static string Named(string name, string key) => string.IsNullOrEmpty(name) ? T(key) : name;

        private static bool FreeBed(Bed b) { ZDO z = Companion.Zdo(b); return z != null && z.GetLong(ZDOVars.s_owner, 0L) == 0L && z.GetLong(HomeKey, 0L) == 0L; }

        private static bool GiveBed(Bed bed, Humanoid c)
        {
            Vector3 spot = bed.GetSpawnPoint();
            if (!Companion.Write(c, cz => { cz.Set(Keys.HasBed, true); cz.Set(Keys.BedPos, spot); })) return false;
            long id = Companion.IdOf(c);
            foreach (Bed old in Object.FindObjectsByType<Bed>(FindObjectsSortMode.None)) if (old != bed && IdOn(old) == id) { Claim(old); Companion.Zdo(old).Set(HomeKey, 0L); Companion.Zdo(old).Set(HomeName, ""); }
            ZDO z = Companion.Zdo(bed);
            Claim(bed); z.Set(HomeKey, id); z.Set(HomeName, Companion.NameOf(c));
            VillagePlebsPlugin.Instance?.Note($"{Companion.NameOf(c)} was given the bed at {spot:F0}");
            return true;
        }

        private static void GiveChest(Container chest, Humanoid c)
        {
            Claim(chest);
            Companion.Zdo(chest).Set(HomeKey, Companion.IdOf(c));
            Companion.Zdo(chest).Set(HomeName, Companion.NameOf(c));
            _chestCache.Clear();
        }

        public static string SetUp(Humanoid c)
        {
            ZDO z = Companion.Zdo(c);
            if (z == null) return null;
            string bedFar = null;
            int chestsTaken = 0;
            Vector3 from = c.transform.position;
            Bed bed = null;
            if (!z.GetBool(Keys.HasBed, false))
            {
                bed = Object.FindObjectsByType<Bed>(FindObjectsSortMode.None).Where(b => FreeBed(b) && Vector3.Distance(b.transform.position, from) < 40f)
                            .OrderBy(b => Vector3.Distance(b.transform.position, from)).FirstOrDefault();
                if (bed != null && GiveBed(bed, c)) bedFar = Vector3.Distance(bed.transform.position, from).ToString("0");
            }
            else bed = BedOf(c);
            if (bed != null && Chests(c).Count == 0)
            {
                Vector3 at = bed.transform.position;
                List<Container> empty = Object.FindObjectsByType<Container>(FindObjectsSortMode.None)
                    .Where(x => IsChest(x) && IdOn(x) == 0L && !Containers.InUse(x) && x.GetInventory().NrOfItems() == 0 && Vector3.Distance(x.transform.position, at) < 8f)
                    .OrderBy(x => Vector3.Distance(x.transform.position, at)).Take(2).ToList();
                foreach (Container ch in empty) GiveChest(ch, c);
                chestsTaken = empty.Count;
            }
            if (bedFar == null) return chestsTaken == 0 ? null : P(chestsTaken == 1 ? "villagepleb_home_took_chest" : "villagepleb_home_took_chests");
            return chestsTaken == 0 ? P("villagepleb_home_took_bed", bedFar) : P(chestsTaken == 1 ? "villagepleb_home_took_bed_chest" : "villagepleb_home_took_bed_chests", bedFar);
        }

        public static bool Follow(Humanoid c, bool manual = true)
        {
            bool ok = Companion.Write(c, z => z.Set(Keys.Order, (int)Order.Follow));
            if (!ok) return false;
            BrainState st = Brain.Get(c);
            if (manual) { st.ManualOrderAt = Time.time; st.Outing = false; st.Remember(P("villagepleb_home_remember_called")); Errands.ForTheRoad(st); }
            return true;
        }

        public static bool GoHome(Humanoid c, bool quiet = false, bool manual = true)
        {
            string setUp = Companion.Zdo(c)?.GetBool(Keys.HasBed, false) == true && Chests(c).Count > 0 ? null : SetUp(c);
            bool ok = Companion.Write(c, z =>
            {
                z.Set(Keys.Order, (int)Order.Gather);
                if (!z.GetBool(Keys.HasBed, false)) z.Set(Keys.Post, c.transform.position);
            });
            if (!ok) return false;
            BrainState st = Brain.Get(c);
            st.Outing = false;
            st.NextArmoryLook = 0f;
            if (manual) { st.ManualOrderAt = Time.time; st.Remember(P("villagepleb_home_remember_sent_home")); }
            bool bed = Companion.Zdo(c).GetBool(Keys.HasBed, false);
            if (setUp != null) Talk.Tell(c, setUp);
            else if (!bed && !quiet) Talk.Tell(c, P("villagepleb_home_no_free_bed"), "nobed", 10f);
            return true;
        }

        public static void ToggleAll(Player p)
        {
            var near = Companion.All().Where(c => Companion.IsMine(c, p) && !c.IsDead() && Vector3.Distance(c.transform.position, p.transform.position) < 100f).ToList();
            if (near.Count == 0) { VillagePlebsPlugin.Tell(T("villagepleb_home_none_near", MainConfig.menuKey.Value)); return; }
            bool home = near.Any(c => Companion.OrderOf(c) == Order.Follow);
            foreach (Humanoid c in near)
            {
                if (home) { if (GoHome(c, true)) Talk.Say(c, P("villagepleb_home_say_heading_home")); }
                else if (Follow(c)) Talk.Say(c, P("villagepleb_home_say_behind_you"));
            }
            string names = near.Select(Companion.NameOf).Aggregate((a, b) => T("villagepleb_home_and", a, b));
            bool one = near.Count == 1;
            VillagePlebsPlugin.Tell(home ? T(one ? "villagepleb_home_go_home_one" : "villagepleb_home_go_home_many", names, MainConfig.menuKey.Value)
                             : T(one ? "villagepleb_home_come_one" : "villagepleb_home_come_many", names));
        }

        public static void ToggleChest(Container chest, Humanoid c)
        {
            ZDO z = Companion.Zdo(chest);
            long id = Companion.IdOf(c);
            if (z == null) return;
            if (Containers.InUse(chest)) { VillagePlebsPlugin.Tell(T("villagepleb_home_chest_open")); return; }
            bool mine = z.GetLong(HomeKey, 0L) == id;
            if (!mine && z.GetLong(HomeKey, 0L) != 0L) { VillagePlebsPlugin.Tell(T("villagepleb_home_chest_others", Named(z.GetString(HomeName, ""), "villagepleb_home_another_companion"))); return; }
            Claim(chest);
            z.Set(HomeKey, mine ? 0L : id);
            z.Set(HomeName, mine ? "" : Companion.NameOf(c));
            _chestCache.Clear();
            int count = Chests(c).Count;
            VillagePlebsPlugin.Tell(mine ? T("villagepleb_home_chest_let_go", Companion.NameOf(c)) : T(count == 1 ? "villagepleb_home_chest_taken_one" : "villagepleb_home_chest_taken_many", Companion.NameOf(c), count));
        }

        public static bool IsChest(Container c)
        {
            if (c == null || c.GetComponent<TombStone>() != null || c.GetComponentInParent<Character>() != null) return false;
            Piece piece = c.GetComponentInParent<Piece>();
            if (piece == null || c.GetComponentInParent<Vagon>() != null || c.GetComponentInParent<Ship>() != null) return false;
            return Companion.Zdo(c) != null && !NotStorage(c);
        }

        public static bool NotStorage(Container c) => VillagePlebs.Shared.SpecialContainers.NotStorage(c);

        private static void LetGo(Container chest, Humanoid c)
        {
            if (!Containers.Take(chest)) return;
            ZDO z = Companion.Zdo(chest);
            if (z == null) return;
            z.Set(HomeKey, 0L);
            z.Set(HomeName, "");
            string what = Localization.instance != null ? Localization.instance.Localize(chest.m_name) : chest.m_name;
            int items = chest.GetInventory()?.NrOfItems() ?? 0;
            VillagePlebsPlugin.Instance?.Note($"{Companion.NameOf(c)} let go of the {what} at {chest.transform.position:F0}: it is not a chest to keep things in ({items} stacks left in it)");
            if (Player.m_localPlayer != null && Companion.IsMine(c, Player.m_localPlayer))
                VillagePlebsPlugin.Tell(T(items > 0 ? "villagepleb_home_not_storage_items" : "villagepleb_home_not_storage", Companion.NameOf(c), what));
        }

        private static readonly Dictionary<long, KeyValuePair<float, List<Container>>> _chestCache = new Dictionary<long, KeyValuePair<float, List<Container>>>();

        public static List<Container> Chests(Humanoid c)
        {
            long id = Companion.IdOf(c);
            if (_chestCache.TryGetValue(id, out var hit) && Time.time - hit.Key < 4f) return hit.Value.Where(x => x != null).ToList();
            List<Container> claimed = Object.FindObjectsByType<Container>(FindObjectsSortMode.None).Where(x => x != null && IdOn(x) == id).OrderBy(x => x.GetInstanceID()).ToList();
            if (id != 0L) foreach (Container bad in claimed.Where(NotStorage)) LetGo(bad, c);
            List<Container> found = claimed.Where(IsChest).ToList();
            _chestCache[id] = new KeyValuePair<float, List<Container>>(Time.time, found);
            return found;
        }

        public static Bed BedOf(Humanoid c)
        {
            long id = Companion.IdOf(c);
            return Object.FindObjectsByType<Bed>(FindObjectsSortMode.None).FirstOrDefault(b => IdOn(b) == id);
        }

        private static float _nextTick, _nextSnapshot;
        private static readonly Dictionary<long, Minimap.PinData> BedPins = new Dictionary<long, Minimap.PinData>();

        public static void Tick(Player p)
        {
            if (AssignFor != null)
            {
                if (AssignFor == null || AssignFor.IsDead() || Input.GetKeyDown(KeyCode.Escape) || MainConfig.menuKey.Value.IsDown()) StopAssign(T("villagepleb_home_done_assigning"));
                else if (Time.time >= _nextHint) { _nextHint = Time.time + 6f; VillagePlebsPlugin.Tell(T("villagepleb_home_assigning", Companion.NameOf(AssignFor), MainConfig.menuKey.Value)); }
            }
            if (Time.time < _nextTick) return;
            _nextTick = Time.time + 1f;

            if (Time.time >= _nextSnapshot)
            {
                _nextSnapshot = Time.time + 5f;
                var snaps = new List<Profile>();
                foreach (Humanoid c in Companion.All().Where(c => Companion.IsMine(c, p) && !c.IsDead() && c.GetHealth() > 0f))
                {
                    Profile known = Profile.Find(p, Companion.IdOf(c));
                    if (known != null && known.Dead) continue;
                    snaps.Add(Profile.Of(c));
                }
                if (snaps.Count > 0) Profile.Save(p, snaps);
            }

            double now = ZNet.instance.GetTimeSeconds();
            foreach (Profile prof in Profile.Here(p).Where(x => x.Dead && now - x.DiedAt >= MainConfig.respawnSeconds.Value).ToList())
                Respawn(p, prof);

            UpdateBedPins(p);
        }

        public static void MarkDead(Player p, long id, Vector3 where, Humanoid body = null, bool grave = true, string kept = null)
        {
            Profile known = Profile.Find(p, id);
            if (known != null && known.Dead && ZNet.instance.GetTimeSeconds() - known.DiedAt < 60.0) return;
            Profile prof = body != null ? Profile.Of(body) : known;
            if (prof == null) return;
            prof.Dead = true;
            prof.Skills = Skill.AfterDeath(prof.Skills);
            prof.MmoXp = EpicMmo.AfterDeath(prof.MmoXp);
            prof.DiedAt = ZNet.instance.GetTimeSeconds();
            prof.DiedPos = where;
            prof.HasGrave = grave;
            Remote.Forget(id);
            if (kept != null) prof.Kept = kept;
            Profile.Save(p, prof);
            VillagePlebsPlugin.Instance?.Note($"{prof.Name} will wake {(prof.HasBed ? "in their bed" : "beside you")} in {MainConfig.respawnSeconds.Value:0} s");
        }

        public static Humanoid Respawn(Player p, Profile prof)
        {
            Humanoid up = Companion.All().FirstOrDefault(h => Companion.IdOf(h) == prof.Id && !h.IsDead() && h.GetHealth() > 0f);
            ZDO away = up == null && Remote.Sure ? Remote.Find(prof.Id) : null;
            bool? elsewhere = up != null || Remote.Sure ? (bool?)false : Remote.InWorld(prof.Id);
            if (up != null || (away != null && away.GetFloat(ZDOVars.s_health, 1f) > 0f) || elsewhere == true)
            {
                prof.Dead = false;
                if (elsewhere != true) prof.Kept = "";
                Profile.Save(p, prof);
                VillagePlebsPlugin.Instance?.Note($"{prof.Name} is already up: not woken again");
                return up;
            }
            if (elsewhere == null) return null;
            GameObject prefab = Prefab.Get();
            if (prefab == null) return null;
            Vector3 pos = prof.HasBed ? prof.Bed : p.transform.position - p.transform.forward * 2f;
            if (!prof.HasBed && ZoneSystem.instance != null && ZoneSystem.instance.GetSolidHeight(pos, out float h)) pos.y = Mathf.Max(pos.y, h);
            GameObject go = Object.Instantiate(prefab, pos + Vector3.up * 0.2f, Quaternion.identity);
            Humanoid c = go.GetComponent<Humanoid>();
            ZDO z = Companion.Zdo(c);
            z.Set(Keys.Master, p.GetPlayerID());
            z.Set(Keys.MasterName, p.GetPlayerName());
            if (!prof.Dead && Remote.TombOf(prof.Id) is Vector3 tomb) { prof.DiedPos = tomb; prof.HasGrave = true; }
            prof.ApplyTo(c);
            int kept = Companion.Unpack(c, prof.Kept);
            kept += Grave.WearAgain(c, prof.HasGrave || prof.Dead ? prof.DiedPos : Vector3.zero, out bool left);
            if (kept > 0) VillagePlebsPlugin.Instance?.Note($"{prof.Name} woke wearing its gear ({kept})");
            prof.Kept = "";
            Companion.Zdo(c)?.Set(Companion.KeptKey, "");
            prof.HasGrave = prof.HasGrave && left;
            Companion.Zdo(c)?.Set(Grave.HasKey, prof.HasGrave);
            prof.Dead = false;
            Profile.Save(p, prof);
            string where = prof.HasBed ? $"in {(prof.Model == 1 ? "her" : "his")} bed" : "beside you";
            VillagePlebsPlugin.Tell(T(!prof.HasBed ? "villagepleb_home_wakes_beside" : prof.Model == 1 ? "villagepleb_home_wakes_bed_her" : "villagepleb_home_wakes_bed_his", prof.Name));
            Brain.Get(c).WokeAt = Time.time;
            if (prof.HasGrave)
            {
                bool atHome = prof.HasBed && Vector3.Distance(prof.DiedPos, Work.Center(c)) < Work.RadiusOf(c) + 10f;
                Talk.Tell(c, atHome ? P("villagepleb_home_up_get_things") : P("villagepleb_home_up_things_far", Remote.FromHere(prof.DiedPos - pos)), "woke", 0.5f);
            }
            VillagePlebsPlugin.Instance?.Note($"{prof.Name} woke {where} at {pos:F0}");
            return c;
        }

        private static void UpdateBedPins(Player p)
        {
            if (Minimap.instance == null) return;
            var want = Profile.Here(p).Where(x => x.HasBed).ToDictionary(x => x.Id);
            foreach (long gone in BedPins.Keys.Where(k => !want.ContainsKey(k)).ToList()) { Minimap.instance.RemovePin(BedPins[gone]); BedPins.Remove(gone); }
            foreach (Profile prof in want.Values)
            {
                if (!BedPins.TryGetValue(prof.Id, out Minimap.PinData pin) || pin == null)
                    BedPins[prof.Id] = pin = Minimap.instance.AddPin(prof.Bed, Minimap.PinType.Bed, T("villagepleb_home_bed_of", prof.Name), false, false, 0L);
                pin.m_pos = prof.Bed;
            }
        }

        public static void Stop()
        {
            if (Minimap.instance != null) foreach (Minimap.PinData pin in BedPins.Values) Minimap.instance.RemovePin(pin);
            BedPins.Clear();
            _chestCache.Clear();
            AssignFor = null;
        }
    }

    [HarmonyPatch(typeof(Bed), nameof(Bed.Interact))]
    internal static class Bed_Interact
    {
        private static bool Prefix(Bed __instance, bool repeat, ref bool __result)
        {
            if (repeat) return true;
            if (Home.AssignFor != null) { Home.ToggleBed(__instance, Home.AssignFor); __result = true; return false; }
            if (Home.IdOn(__instance) == 0L) return true;
            VillagePlebsPlugin.Tell(T("villagepleb_home_this_bed_of", Home.Named(Companion.Zdo(__instance).GetString(Home.HomeName, ""), "villagepleb_home_a_companion")));
            __result = true;
            return false;
        }
    }

    [HarmonyPatch(typeof(Bed), nameof(Bed.GetHoverText))]
    internal static class Bed_GetHoverText
    {
        private static void Postfix(Bed __instance, ref string __result)
        {
            if (Home.AssignFor != null) { __result += "\n<color=#7fe0c8>[E] " + T(Home.IdOn(__instance) == Companion.IdOf(Home.AssignFor) ? "villagepleb_home_take_back_from" : "villagepleb_home_give_to", Companion.NameOf(Home.AssignFor)) + "</color>"; return; }
            if (Home.IdOn(__instance) != 0L) __result = T("villagepleb_home_bed_of", Home.Named(Companion.Zdo(__instance).GetString(Home.HomeName, ""), "villagepleb_home_a_companion_start"));
        }
    }

    [HarmonyPatch(typeof(Container), nameof(Container.Interact))]
    internal static class Container_Interact_Assign
    {
        [HarmonyPriority(Priority.First)]
        private static bool Prefix(Container __instance, bool hold, ref bool __result)
        {
            if (hold || Home.AssignFor == null) return true;
            if (Home.NotStorage(__instance) && __instance.GetComponentInParent<Piece>() != null)
            {
                VillagePlebsPlugin.Tell(T("villagepleb_home_not_a_chest", Companion.NameOf(Home.AssignFor)));
                __result = true;
                return false;
            }
            if (!Home.IsChest(__instance)) return true;
            Home.ToggleChest(__instance, Home.AssignFor);
            __result = true;
            return false;
        }
    }

    [HarmonyPatch(typeof(Container), nameof(Container.GetHoverText))]
    internal static class Container_GetHoverText_Home
    {
        private static void Postfix(Container __instance, ref string __result)
        {
            if (!Home.IsChest(__instance)) return;
            if (Home.AssignFor != null) { __result += "\n<color=#7fe0c8>[E] " + T(Home.IdOn(__instance) == Companion.IdOf(Home.AssignFor) ? "villagepleb_home_take_back_from" : "villagepleb_home_give_to", Companion.NameOf(Home.AssignFor)) + "</color>"; return; }
            if (Home.IdOn(__instance) != 0L) __result += "\n<color=#7fe0c8>" + T("villagepleb_home_chest_of", Home.Named(Companion.Zdo(__instance).GetString(Home.HomeName, ""), "villagepleb_home_a_companion_start")) + "</color>";
        }
    }
}
