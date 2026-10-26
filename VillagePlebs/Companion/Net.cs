using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using HarmonyLib;
using UnityEngine;
using static VillagePlebs.Shared.Translations;

namespace VillagePlebs
{
    internal static class Net
    {
        private const string RpcStats = "VillagePlebs_Stats", RpcFallen = "VillagePlebs_Fallen", RpcCleared = "VillagePlebs_GearTaken", RpcSays = "VillagePlebs_Says";
        private const string RpcWhereIs = "VillagePlebs_WhereIs", RpcWhereIsAnswer = "VillagePlebs_WhereIsAnswer", RpcFetch = "VillagePlebs_Fetch";
        public const string CrateKey = "vp_fallen";
        private const string MarkerKey = "villagepleb_net_marker";

        private class Seen { public string Line; public float At; public string Master; }
        private class Fallen { public string Name, Master; public Vector3 Pos; public float At; public long Id, MasterId; }

        private static readonly Dictionary<long, Seen> Remote = new Dictionary<long, Seen>();
        private static readonly List<Fallen> RecentlyFallen = new List<Fallen>();
        private static readonly Dictionary<long, KeyValuePair<string, float>> LastSent = new Dictionary<long, KeyValuePair<string, float>>();
        private static ZRoutedRpc _registeredOn;
        private static float _nextSend, _nextMarkerCheck;
        private static int _peers, _registerAfterFrame;

        // This game's handlers are in place, so answers to what it asks are not lost.
        public static bool Ready => ZRoutedRpc.instance != null && _registeredOn == ZRoutedRpc.instance;

        public static void Start()
        {
            _registerAfterFrame = Time.frameCount + 2;
        }

        public static void Stop()
        {
            Book.Stop();
            RemoveLivePins();
            Unregister();
            _registeredOn = null;
            Remote.Clear();
            RecentlyFallen.Clear();
            LastSent.Clear();
        }

        public static void Update(Player me)
        {
            ZRoutedRpc rpc = ZRoutedRpc.instance;
            if (rpc == null) { _registeredOn = null; return; }
            if (Time.frameCount <= _registerAfterFrame) return;
            if (rpc != _registeredOn)
            {
                _registeredOn = rpc;
                Remote.Clear();
                Unregister();
                rpc.Register<string>(RpcStats, OnStats);
                rpc.Register<string>(RpcFallen, OnFallen);
                rpc.Register<string>(RpcCleared, OnCleared);
                rpc.Register<string>(RpcSays, OnSays);
                rpc.Register<string>(RpcWhereIs, OnWhereIs);
                rpc.Register<string>(RpcWhereIsAnswer, OnWhereIsAnswer);
                rpc.Register<string>(RpcFetch, OnFetch);
                Book.Register(rpc);
                Docks.Register(rpc);
            }
            Book.Tick();
            if (me == null) return;

            int peers = ZNet.instance != null ? ZNet.instance.GetPlayerList().Count : 0;
            if (peers > _peers && _peers > 0) VillagePlebsPlugin.Instance.StartCoroutine(TellNewcomers());
            _peers = peers;

            if (Time.time >= _nextSend)
            {
                _nextSend = Time.time + 1f;
                if (peers > 1)
                    foreach (Humanoid c in Companion.All())
                    {
                        if (!c.GetComponent<ZNetView>().IsOwner() || Shopping.Away(c)) continue;
                        string line = StatsLine(c), gist = Gist(line);
                        long id = Companion.IdOf(c);
                        if (LastSent.TryGetValue(id, out var last) && last.Key == gist && Time.time - last.Value < 3f) continue;
                        LastSent[id] = new KeyValuePair<string, float>(gist, Time.time);
                        Send(RpcStats, line);
                    }
            }
            if (Time.time >= _nextMarkerCheck) { _nextMarkerCheck = Time.time + 2f; CheckMarkers(me); }
            if (Time.time >= _nextPins) { _nextPins = Time.time + 0.1f; UpdatePins(); }
        }

        public static readonly Color PinColor = new Color(0.45f, 0.85f, 0.75f);
        private static readonly Dictionary<long, Minimap.PinData> LivePins = new Dictionary<long, Minimap.PinData>();
        private static float _nextPins;

        public static bool IsLivePin(Minimap.PinData pin) => LivePins.ContainsValue(pin);

        private static void UpdatePins()
        {
            if (Minimap.instance == null) { LivePins.Clear(); return; }
            var seen = new HashSet<long>();
            foreach (string line in CompanionLines())
            {
                string[] f = line.Split('|');
                if (f.Length < 6 || !long.TryParse(f[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out long id)) continue;
                var pos = new Vector3(P(f[3]), P(f[4]), P(f[5]));
                seen.Add(id);
                if (!LivePins.TryGetValue(id, out Minimap.PinData pin) || !GameFields.Pins(Minimap.instance).Contains(pin))
                    LivePins[id] = pin = Minimap.instance.AddPin(pos, Minimap.PinType.Player, f[1], false, false, 0L);
                pin.m_pos = pos;
            }
            foreach (long gone in LivePins.Keys.Where(k => !seen.Contains(k)).ToList())
            {
                Minimap.instance.RemovePin(LivePins[gone]);
                LivePins.Remove(gone);
            }
        }

        private static void RemoveLivePins()
        {
            if (Minimap.instance != null) foreach (Minimap.PinData pin in LivePins.Values) Minimap.instance.RemovePin(pin);
            LivePins.Clear();
        }

        private static void Unregister()
        {
            if (ZRoutedRpc.instance == null) return;
            var table = AccessTools.Field(typeof(ZRoutedRpc), "m_functions").GetValue(ZRoutedRpc.instance) as IDictionary;
            foreach (string name in new[] { RpcStats, RpcFallen, RpcCleared, RpcSays, RpcWhereIs, RpcWhereIsAnswer, RpcFetch }.Concat(Book.RpcNames).Concat(Docks.RpcNames)) table?.Remove(name.GetStableHashCode());
        }

        public static void AskWhere(long id)
        {
            if (ZRoutedRpc.instance == null || ZNet.instance == null || ZNet.instance.IsServer()) return;
            try { ZRoutedRpc.instance.InvokeRoutedRPC(RpcWhereIs, id.ToString(CultureInfo.InvariantCulture));  }
            catch (Exception e) { VillagePlebsPlugin.Instance?.Warn($"Could not ask the host about a companion: {e.Message}"); }
        }

        private static void OnWhereIs(long sender, string payload)
        {
            if (ZNet.instance == null || !ZNet.instance.IsServer() || !long.TryParse(payload, NumberStyles.Integer, CultureInfo.InvariantCulture, out long id)) return;
            VillagePlebs.Remote.Rescan();
            ZDO z = VillagePlebs.Remote.Find(id);
            bool up = z != null && z.GetFloat(ZDOVars.s_health, 1f) > 0f;
            Vector3? tomb = up ? null : VillagePlebs.Remote.FindTomb(id);
            ZRoutedRpc.instance.InvokeRoutedRPC(sender, RpcWhereIsAnswer, id.ToString(CultureInfo.InvariantCulture) + "|" + (up ? "1" : "0")
                + (tomb != null ? $"|{F(tomb.Value.x)}|{F(tomb.Value.y)}|{F(tomb.Value.z)}" : ""));
        }

        // After a relog your game knows nothing of what is far away: it asks the host to send it your companions that are not near you
        // (and the ships of any away sailing), so they show on the map and their voyages carry on from your game.
        public static void AskFor(IEnumerable<long> ids)
        {
            if (ZRoutedRpc.instance == null || ZNet.instance == null || ZNet.instance.IsServer()) return;
            string payload = string.Join(",", ids.Select(i => i.ToString(CultureInfo.InvariantCulture)));
            if (payload.Length == 0) return;
            try { ZRoutedRpc.instance.InvokeRoutedRPC(RpcFetch, payload); }
            catch (Exception e) { VillagePlebsPlugin.Instance?.Warn($"Could not ask the host about a voyage: {e.Message}"); }
        }

        private static void OnFetch(long sender, string payload)
        {
            if (ZNet.instance == null || !ZNet.instance.IsServer() || ZDOMan.instance == null) return;
            VillagePlebs.Remote.Rescan();
            foreach (string part in (payload ?? "").Split(','))
            {
                if (!long.TryParse(part, NumberStyles.Integer, CultureInfo.InvariantCulture, out long id)) continue;
                ZDO body = VillagePlebs.Remote.Find(id);
                if (body == null) continue;
                ZDOMan.instance.ForceSendZDO(sender, body.m_uid);
                if (!Sailing.Away(body)) continue;
                ZDO hull = Sailing.HullOf(body);
                if (hull != null) ZDOMan.instance.ForceSendZDO(sender, hull.m_uid);
            }
        }

        private static void OnWhereIsAnswer(long sender, string payload)
        {
            string[] f = (payload ?? "").Split('|');
            if (f.Length >= 2 && long.TryParse(f[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out long id))
                VillagePlebs.Remote.Heard(id, f[1] == "1", f.Length >= 5 ? new Vector3(P(f[2]), P(f[3]), P(f[4])) : (Vector3?)null);
        }

        public static void SendSays(long masterId, string name, string text)
        {
            if (masterId == 0L || ZNet.instance == null || ZNet.instance.GetPlayerList().Count < 2) return;
            Send(RpcSays, masterId.ToString(CultureInfo.InvariantCulture) + "|" + Clean(name) + "|" + text.Replace('\n', ' '));
        }

        private static void OnSays(long sender, string payload)
        {
            string[] f = payload.Split(new[] { '|' }, 3);
            if (f.Length < 3 || !long.TryParse(f[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out long master)) return;
            if (Player.m_localPlayer != null && Player.m_localPlayer.GetPlayerID() == master) Talk.ToChat(f[1], f[2]);
        }

        private static void Send(string rpc, string payload)
        {
            try { ZRoutedRpc.instance?.InvokeRoutedRPC(ZRoutedRpc.Everybody, rpc, payload); }
            catch (Exception e) { VillagePlebsPlugin.Instance?.Warn($"Could not send {rpc}: {e.Message}"); }
        }

        private static string Clean(string s) => (s ?? "").Replace('|', '/').Replace('\n', ' ');
        private static string F(float v) => v.ToString("0.#", CultureInfo.InvariantCulture);
        private static float P(string s) => float.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out float f) ? f : 0f;

        private static string StatsLine(Humanoid c)
        {
            Vector3 p = c.transform.position;
            return string.Join("|", Companion.IdOf(c).ToString(CultureInfo.InvariantCulture), Clean(Companion.NameOf(c)), Clean(Companion.Zdo(c).GetString(Keys.MasterName, "")),
                F(p.x), F(p.y), F(p.z));
        }

        private static string Gist(string line)
        {
            string[] f = line.Split('|');
            if (f.Length < 6) return line;
            foreach (int i in new[] { 3, 4, 5 }) f[i] = Mathf.Round(P(f[i])).ToString(CultureInfo.InvariantCulture);
            return string.Join("|", f);
        }

        private static void OnStats(long sender, string line)
        {
            string[] f = (line ?? "").Split('|');
            if (f.Length < 6 || !long.TryParse(f[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out long id)) return;
            Remote[id] = new Seen { Line = line, At = Time.time, Master = f[2] };
        }

        private static List<ZDO> _far = new List<ZDO>();
        private static float _nextFar;

        // Your companions this game has a record of but not loaded, looked up once a second.
        private static List<ZDO> FarMine()
        {
            if (Time.time < _nextFar) return _far;
            _nextFar = Time.time + 1f;
            _far = new List<ZDO>();
            Player me = Player.m_localPlayer;
            if (me == null || ZNetScene.instance == null) return _far;
            foreach (Profile prof in Profile.Here(me))
            {
                if (prof.Dead) continue;
                ZDO z = VillagePlebs.Remote.Find(prof.Id);
                if (z != null && ZNetScene.instance.FindInstance(z) == null && z.GetLong(Keys.Id, 0L) != 0L) _far.Add(z);
            }
            return _far;
        }

        private static List<string> CompanionLines()
        {
            var lines = new List<string>();
            try
            {
                if (ZNet.instance == null) return lines;
                var online = new HashSet<string>(ZNet.instance.GetPlayerList().Select(p => p.m_name));
                var have = new HashSet<long>();
                foreach (Humanoid c in Companion.All())
                {
                    // Out shopping: off the map too until it is back.
                    if (c.IsDead() || Shopping.Away(c) || !online.Contains(Companion.Zdo(c).GetString(Keys.MasterName, ""))) continue;
                    long cid = Companion.IdOf(c);
                    bool runHere = c.GetComponent<ZNetView>().IsOwner();
                    if (!runHere && Remote.TryGetValue(cid, out Seen s) && Time.time - s.At < 6f) continue;
                    lines.Add(StatsLine(c));
                    have.Add(cid);
                }
                foreach (var kv in Remote.Where(kv => Time.time - kv.Value.At < 8f && !have.Contains(kv.Key) && online.Contains(kv.Value.Master) && !Shopping.Away(VillagePlebs.Remote.Find(kv.Key))))
                {
                    lines.Add(kv.Value.Line);
                    have.Add(kv.Key);
                }
                // Your own that are too far away to be loaded: where the world last had them.
                foreach (ZDO z in FarMine())
                {
                    long cid = z.GetLong(Keys.Id, 0L);
                    if (have.Contains(cid) || Shopping.Away(z)) continue;
                    Vector3 p = z.GetPosition();
                    lines.Add(string.Join("|", cid.ToString(CultureInfo.InvariantCulture), Clean(z.GetString(Keys.Name, "")), Clean(z.GetString(Keys.MasterName, "")), F(p.x), F(p.y), F(p.z)));
                }
                RecentlyFallen.RemoveAll(f => Time.time - f.At > 600f || (Time.time - f.At > 10f && lines.Any(l => l.Split('|')[2] == f.Master)));
            }
            catch (Exception) { lines.Clear(); }
            return lines;
        }

        public static void AnnounceFall(Humanoid c, Vector3 pos, bool grave, bool lost = false)
        {
            Send(RpcFallen, string.Join("|", Clean(Companion.NameOf(c)), Clean(Companion.Zdo(c).GetString(Keys.MasterName, "")), F(pos.x), F(pos.y), F(pos.z),
                Companion.IdOf(c).ToString(CultureInfo.InvariantCulture), lost ? "0" : Companion.MasterId(c).ToString(CultureInfo.InvariantCulture), grave ? "1" : "0",
                "", lost ? "lost" : ""));
        }

        private static readonly HashSet<string> Heard = new HashSet<string>();

        private static void OnFallen(long sender, string payload)
        {
            string[] f = (payload ?? "").Split('|');
            if (f.Length < 5) return;
            var pos = new Vector3(P(f[2]), P(f[3]), P(f[4]));
            AddMarker(pos, f[0]);
            if (f.Length >= 6 && !Heard.Add(f[5] + "@" + f[2] + "," + f[3] + "," + f[4])) return;
            bool lost = f.Length >= 10 && f[9] == "lost";
            if (f.Length >= 7 && long.TryParse(f[5], NumberStyles.Integer, CultureInfo.InvariantCulture, out long id) && long.TryParse(f[6], NumberStyles.Integer, CultureInfo.InvariantCulture, out long masterId)
                && masterId != 0L && Player.m_localPlayer != null && Player.m_localPlayer.GetPlayerID() == masterId)
                Home.MarkDead(Player.m_localPlayer, id, pos, null, f.Length < 8 || f[7] == "1", f.Length >= 10 ? "" : f.Length >= 9 ? f[8] : null);
            if (f[1] == "") return;
            if (lost)
            {
                if (Player.m_localPlayer != null)
                    Player.m_localPlayer.Message(MessageHud.MessageType.TopLeft, f[1] != Player.m_localPlayer.GetPlayerName() ? T("villagepleb_net_lost_fight_other", f[1], f[0]) : T("villagepleb_net_lost_fight", f[0]));
                return;
            }
            RecentlyFallen.RemoveAll(x => x.Name == f[0] && x.Master == f[1]);
            RecentlyFallen.Add(new Fallen { Name = f[0], Master = f[1], Pos = pos, At = Time.time,
                Id = f.Length >= 7 && long.TryParse(f[5], out long fid) ? fid : 0L, MasterId = f.Length >= 7 && long.TryParse(f[6], out long fm) ? fm : 0L });
            if (Player.m_localPlayer != null && f[1] != Player.m_localPlayer.GetPlayerName())
                Player.m_localPlayer.Message(MessageHud.MessageType.TopLeft, T("villagepleb_net_fallen_other", f[1], f[0]));
        }

        private static void OnCleared(long sender, string payload)
        {
            string[] f = (payload ?? "").Split('|');
            if (f.Length < 3) return;
            RemoveMarkersNear(new Vector3(P(f[0]), P(f[1]), P(f[2])), 8f);
        }

        private static IEnumerable<Minimap.PinData> Markers() =>
            Minimap.instance == null ? Enumerable.Empty<Minimap.PinData>() : GameFields.Pins(Minimap.instance).Where(p => p.m_type == Minimap.PinType.Death && IsMarker(p.m_name, out _));

        private static bool IsMarker(string pin, out string name)
        {
            name = null;
            if (string.IsNullOrEmpty(pin)) return false;
            foreach (string template in new[] { Words.English(Words.P(MarkerKey, "\u0000")), T(MarkerKey, "\u0000") })
            {
                int at = template.IndexOf('\u0000');
                if (at < 0) continue;
                string before = template.Substring(0, at), after = template.Substring(at + 1);
                if (before.Length + after.Length == 0 || pin.Length <= before.Length + after.Length) continue;
                if (!pin.StartsWith(before, StringComparison.Ordinal) || !pin.EndsWith(after, StringComparison.Ordinal)) continue;
                name = pin.Substring(before.Length, pin.Length - before.Length - after.Length);
                return true;
            }
            return false;
        }

        private static void AddMarker(Vector3 pos, string name)
        {
            if (Minimap.instance == null) return;
            if (Markers().Any(p => Vector3.Distance(p.m_pos, pos) < 4f)) return;
            Minimap.instance.AddPin(pos, Minimap.PinType.Death, T(MarkerKey, name), true, false, 0L);
        }

        private static void RemoveMarkersNear(Vector3 pos, float within = 4f)
        {
            if (Minimap.instance == null) return;
            foreach (Minimap.PinData pin in Markers().Where(p => Vector3.Distance(p.m_pos, pos) < within).ToList()) Minimap.instance.RemovePin(pin);
        }

        public static void Recovered(Vector3 tomb)
        {
            RecentlyFallen.RemoveAll(f => Vector3.Distance(f.Pos, tomb) < 8f);
            foreach (Minimap.PinData pin in Markers().Where(p => Vector3.Distance(p.m_pos, tomb) < 8f).ToList())
            {
                Minimap.instance.RemovePin(pin);
                Send(RpcCleared, $"{F(pin.m_pos.x)}|{F(pin.m_pos.y)}|{F(pin.m_pos.z)}");
            }
            Send(RpcCleared, $"{F(tomb.x)}|{F(tomb.y)}|{F(tomb.z)}");
        }

        private static void CheckMarkers(Player me)
        {
            foreach (Minimap.PinData pin in Markers().ToList())
            {
                if (Vector3.Distance(pin.m_pos, me.transform.position) > 30f) continue;
                if (CrateNear(pin.m_pos) || RecentlyFallen.Any(f => Time.time - f.At < 120f && Vector3.Distance(f.Pos, pin.m_pos) < 4f)) continue;
                VillagePlebsPlugin.Instance?.Note($"The fallen companion's crate at {pin.m_pos:F0} is gone: marker removed");
                Minimap.instance.RemovePin(pin);
                Send(RpcCleared, $"{F(pin.m_pos.x)}|{F(pin.m_pos.y)}|{F(pin.m_pos.z)}");
            }
        }

        private static readonly List<ZDO> NearObjects = new List<ZDO>();
        private static readonly HashSet<Vector2s> CrateZones = new HashSet<Vector2s>();

        private static bool CrateNear(Vector3 at)
        {
            if (ZDOMan.instance == null) return true;
            CrateZones.Clear();
            foreach (float dx in new[] { -6f, 6f })
                foreach (float dz in new[] { -6f, 6f })
                    CrateZones.Add(ZoneSystem.GetZone(at + new Vector3(dx, 0f, dz)));
            foreach (Vector2s zone in CrateZones)
            {
                NearObjects.Clear();
                ZDOMan.instance.FindSectorObjects(zone, new SimulationDistance(0, 0), NearObjects);
                foreach (ZDO zdo in NearObjects)
                    if (zdo != null && zdo.IsValid() && Vector3.Distance(zdo.GetPosition(), at) < 6f && zdo.GetString(CrateKey, "") != "") return true;
            }
            return false;
        }

        private static IEnumerator TellNewcomers()
        {
            yield return new WaitForSeconds(10f);
            foreach (Minimap.PinData pin in Markers().ToList())
            {
                if (!IsMarker(pin.m_name, out string name)) continue;
                Send(RpcFallen, string.Join("|", Clean(name), "", F(pin.m_pos.x), F(pin.m_pos.y), F(pin.m_pos.z)));
            }
            foreach (Fallen f in RecentlyFallen.Where(x => x.Id != 0L))
                Send(RpcFallen, string.Join("|", Clean(f.Name), Clean(f.Master), F(f.Pos.x), F(f.Pos.y), F(f.Pos.z),
                    f.Id.ToString(CultureInfo.InvariantCulture), f.MasterId.ToString(CultureInfo.InvariantCulture)));
        }
    }
}
