using System.Collections.Generic;
using UnityEngine;

namespace VillagePlebs
{
    internal static class Remote
    {
        private static readonly Dictionary<long, ZDO> Found = new Dictionary<long, ZDO>();
        private static float _nextScan;
        private static bool _scanned;

        public static ZDO Find(long id)
        {
            if (ZDOMan.instance == null) return null;
            if (Time.unscaledTime >= _nextScan) { _nextScan = Time.unscaledTime + 2f; Scan(); }
            return Found.TryGetValue(id, out ZDO z) ? z : null;
        }

        public static bool Sure => _scanned && ZNet.instance != null && ZNet.instance.IsServer();

        public static void Rescan() { _nextScan = Time.unscaledTime + 2f; Scan(); }

        private static readonly Dictionary<long, KeyValuePair<bool, float>> Answers = new Dictionary<long, KeyValuePair<bool, float>>();
        private static readonly Dictionary<long, float> Asked = new Dictionary<long, float>();

        public static bool? InWorld(long id)
        {
            if (Find(id) != null) return true;
            if (Sure) return false;
            if (Answers.TryGetValue(id, out var a) && Time.unscaledTime - a.Value < 20f) return a.Key;
            if (!Asked.TryGetValue(id, out float at) || Time.unscaledTime - at > 5f) { Asked[id] = Time.unscaledTime; Net.AskWhere(id); }
            return Answers.TryGetValue(id, out a) ? a.Key : (bool?)null;
        }

        public static void Heard(long id, bool found, Vector3? tomb = null)
        {
            Answers[id] = new KeyValuePair<bool, float>(found, Time.unscaledTime);
            if (tomb != null) Tombs[id] = tomb.Value;
        }

        private static readonly Dictionary<long, Vector3> Tombs = new Dictionary<long, Vector3>();

        public static Vector3? TombOf(long id)
        {
            if (ZNet.instance != null && ZNet.instance.IsServer() && (!TombLook.TryGetValue(id, out float at) || Time.unscaledTime - at > 5f))
            {
                TombLook[id] = Time.unscaledTime;
                if (FindTomb(id) is Vector3 found) Tombs[id] = found; else Tombs.Remove(id);
            }
            return Tombs.TryGetValue(id, out Vector3 pos) ? pos : (Vector3?)null;
        }

        private static readonly Dictionary<long, float> TombLook = new Dictionary<long, float>();

        public static Vector3? FindTomb(long id)
        {
            if (ZDOMan.instance == null || ZNetScene.instance == null) return null;
            GameObject player = ZNetScene.instance.GetPrefab("Player");
            string name = player != null && player.GetComponent<Player>()?.m_tombstone != null ? player.GetComponent<Player>().m_tombstone.name : "Player_tombstone";
            var list = new List<ZDO>();
            int index = 0;
            for (int guard = 0; guard < 1000 && !ZDOMan.instance.GetAllZDOsWithPrefabIterative(name, list, ref index); guard++) { }
            ZDO newest = null;
            foreach (ZDO z in list)
                if (z != null && z.GetLong(Grave.OfKey, 0L) == id && (newest == null || z.GetLong(ZDOVars.s_timeOfDeath, 0L) > newest.GetLong(ZDOVars.s_timeOfDeath, 0L))) newest = z;
            return newest?.GetPosition();
        }

        public static void Forget(long id) { Answers.Remove(id); Asked.Remove(id); Tombs.Remove(id); TombLook.Remove(id); }

        private static void Scan()
        {
            Found.Clear();
            var list = new List<ZDO>();
            int index = 0;
            for (int guard = 0; guard < 1000 && !ZDOMan.instance.GetAllZDOsWithPrefabIterative(Prefab.PrefabName, list, ref index); guard++) { }
            foreach (ZDO z in list)
            {
                if (z == null) continue;
                long id = z.GetLong(Keys.Id, 0L);
                Found[id != 0L ? id : z.m_uid.ID] = z;
            }
            _scanned = true;
        }

        public static string Status(ZDO z) => z.GetString(Keys.Status, "");
        public static Order OrderOf(ZDO z) => (Order)z.GetInt(Keys.Order, 0);
        public static Vector3 Position(ZDO z) => z.GetPosition();

        public static Vector3? Home(ZDO z)
        {
            if (z.GetBool(Work.HomeSetKey, false)) return z.GetVec3(Work.HomeSpotKey, Vector3.zero);
            if (z.GetBool(Keys.HasBed, false)) return z.GetVec3(Keys.BedPos, Vector3.zero);
            return null;
        }

        public static bool AtHome(ZDO z)
        {
            Vector3? home = Home(z);
            if (home == null) return false;
            Vector3 d = Position(z) - home.Value;
            d.y = 0f;
            return d.magnitude <= z.GetInt(Keys.Radius, 30) + 10f;
        }

        public static List<string> Journal(ZDO z, int last)
        {
            long id = z.GetLong(Keys.Id, 0L);
            List<string> lines = Book.Of(id != 0L ? id : z.m_uid.ID, z).Entries;
            return lines.GetRange(Mathf.Max(0, lines.Count - last), Mathf.Min(last, lines.Count));
        }

        public static string Where(Vector3 at, Vector3 from)
        {
            Vector3 d = at - from;
            d.y = 0f;
            if (d.magnitude < 20f) return Words.P("villagepleb_remote_right_here");
            return Words.P(OfYou[Dir(d)], d.magnitude.ToString("0"));
        }

        public static string FromHere(Vector3 d)
        {
            d.y = 0f;
            return Words.P(OfHere[Dir(d)], d.magnitude.ToString("0"));
        }

        public static int Dir(Vector3 d) => (int)(((Mathf.Atan2(d.x, d.z) * Mathf.Rad2Deg + 360f + 22.5f) % 360f) / 45f) % 8;

        private static readonly string[] OfYou =
        {
            "villagepleb_remote_n_of_you", "villagepleb_remote_ne_of_you", "villagepleb_remote_e_of_you", "villagepleb_remote_se_of_you",
            "villagepleb_remote_s_of_you", "villagepleb_remote_sw_of_you", "villagepleb_remote_w_of_you", "villagepleb_remote_nw_of_you",
        };
        private static readonly string[] OfHere =
        {
            "villagepleb_remote_n_of_here", "villagepleb_remote_ne_of_here", "villagepleb_remote_e_of_here", "villagepleb_remote_se_of_here",
            "villagepleb_remote_s_of_here", "villagepleb_remote_sw_of_here", "villagepleb_remote_w_of_here", "villagepleb_remote_nw_of_here",
        };

        public static bool Call(ZDO z, Player p, out string why)
        {
            why = null;
            if (z == null || p == null) return false;
            if (z.GetInt(ZDOVars.s_inUse, 0) == 1) { why = Words.P("villagepleb_companion_gear_open"); return false; }
            if (!z.IsOwner()) z.SetOwner(ZDOMan.GetSessionID());
            Vector3 spot = p.transform.position - p.transform.forward * 2f + Vector3.up * 0.3f;
            z.SetPosition(spot);
            z.Set(Keys.Order, (int)Order.Follow);
            VillagePlebsPlugin.Instance?.Note($"{z.GetString(Keys.Name, "A companion")} was called to {p.GetPlayerName()} from afar");
            return true;
        }
    }
}
