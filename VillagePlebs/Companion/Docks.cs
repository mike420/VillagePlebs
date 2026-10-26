using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using BepInEx;
using UnityEngine;

namespace VillagePlebs
{
    // The docks: named spots on the sea chart to send companions' ships to, one list for the whole world that every player shares.
    // The host keeps it (a file per world) and sends it to each game that joins; additions and removals go through the host, which
    // passes the new list round. On a server without the mod, each player keeps a list of their own with their character instead.
    // A ship sent to a dock takes a free berth within it, clear of the ships already there and of those on their way.
    internal static class Docks
    {
        private const string Key = "vp_docks";
        private const string RpcAsk = "VillagePlebs_DocksAsk", RpcList = "VillagePlebs_Docks", RpcChange = "VillagePlebs_DockChange";
        public const float Radius = 40f;
        public static readonly string[] RpcNames = { RpcAsk, RpcList, RpcChange };

        internal sealed class Dock
        {
            public string Name;
            public Vector3 At;
        }

        private static readonly List<Dock> Shared = new List<Dock>();
        private static ZNet _world;
        private static string _file;
        private static bool _hostHasMod, _waiting;
        private static float _fallbackAt, _askAt, _nextPins;
        private static readonly Dictionary<string, Minimap.PinData> Pins = new Dictionary<string, Minimap.PinData>();

        private static bool Hosting => ZNet.instance != null && ZNet.instance.IsServer();
        private static bool UseShared => Hosting || _hostHasMod;

        // ---- keeping the shared list ----

        public static void Register(ZRoutedRpc rpc)
        {
            rpc.Register(RpcAsk, sender => { if (Hosting) rpc.InvokeRoutedRPC(sender, RpcList, Pack()); });
            rpc.Register<ZPackage>(RpcList, (sender, pkg) =>
            {
                if (Hosting) return;
                _hostHasMod = true;
                _waiting = false;
                Unpack(pkg);
            });
            rpc.Register<ZPackage>(RpcChange, (sender, pkg) =>
            {
                if (!Hosting) return;
                Apply(pkg.ReadBool(), pkg.ReadString(), pkg.ReadVector3());
                Save();
                rpc.InvokeRoutedRPC(ZRoutedRpc.Everybody, RpcList, Pack());
            });
        }

        // The docks on the game's own map too, with the anchor icon (not saved with your map: they come from the list).
        private static void UpdatePins()
        {
            Minimap mm = Minimap.instance;
            if (mm == null || Player.m_localPlayer == null) { Pins.Clear(); return; }
            var want = All(Player.m_localPlayer).GroupBy(PinKey).ToDictionary(g => g.Key, g => g.First());
            foreach (string gone in Pins.Keys.Where(k => !want.ContainsKey(k)).ToList()) { mm.RemovePin(Pins[gone]); Pins.Remove(gone); }
            Sprite icon = Wheel.Icon("dock_icon");
            foreach (var kv in want)
            {
                if (Pins.TryGetValue(kv.Key, out Minimap.PinData pin) && GameFields.Pins(mm).Contains(pin)) continue;
                pin = mm.AddPin(kv.Value.At, Minimap.PinType.Icon4, kv.Value.Name, false, false, 0L);
                if (icon != null) pin.m_icon = icon;
                Pins[kv.Key] = pin;
            }
        }

        private static string PinKey(Dock d) => d.Name + "@" + Mathf.RoundToInt(d.At.x) + "," + Mathf.RoundToInt(d.At.z);

        public static void Stop()
        {
            if (Minimap.instance != null) foreach (Minimap.PinData pin in Pins.Values) Minimap.instance.RemovePin(pin);
            Pins.Clear();
        }

        // Each frame: on joining a world the host reads its file, and a player's game asks the host for the list.
        public static void Tick()
        {
            if (Time.time >= _nextPins) { _nextPins = Time.time + 1f; UpdatePins(); }
            ZNet net = ZNet.instance;
            if (net == null || WorldGenerator.instance == null || ZRoutedRpc.instance == null || !Net.Ready)
            {
                if (_world != null) { _world = null; Shared.Clear(); _hostHasMod = false; _waiting = false; }
                return;
            }
            if (_world != net)
            {
                _world = net;
                Shared.Clear();
                _hostHasMod = false;
                string name = net.GetWorldName() ?? "world";
                foreach (char c in Path.GetInvalidFileNameChars()) name = name.Replace(c, '_');
                _file = Path.Combine(Paths.BepInExRootPath, "VillagePlebs", "Docks", name + "_" + WorldGenerator.instance.GetSeed() + ".txt");
                if (Hosting) Load();
                else { _waiting = true; _fallbackAt = 0f; _askAt = 0f; }
            }
            if (!_waiting) return;
            // Asked again every few seconds once you are in the world; no answer in 20 s: the host does not have the mod, so each player
            // keeps their own.
            if (Player.m_localPlayer == null) return;
            if (_fallbackAt == 0f) _fallbackAt = Time.time + 20f;
            if (Time.time > _fallbackAt) { _waiting = false; return; }
            if (Time.time >= _askAt)
            {
                _askAt = Time.time + 4f;
                ZRoutedRpc.instance.InvokeRoutedRPC(ZRoutedRpc.instance.GetServerPeerID(), RpcAsk);
            }
        }

        private static void Apply(bool add, string name, Vector3 at)
        {
            if (add) Shared.Add(new Dock { Name = name, At = new Vector3(at.x, 0f, at.z) });
            else Shared.RemoveAll(d => d.Name == name && Flat(d.At, at) < 1f);
        }

        private static void Edit(bool add, string name, Vector3 at)
        {
            Apply(add, name, at);
            if (Hosting)
            {
                Save();
                ZRoutedRpc.instance?.InvokeRoutedRPC(ZRoutedRpc.Everybody, RpcList, Pack());
                return;
            }
            var pkg = new ZPackage();
            pkg.Write(add);
            pkg.Write(name);
            pkg.Write(at);
            ZRoutedRpc.instance?.InvokeRoutedRPC(ZRoutedRpc.instance.GetServerPeerID(), RpcChange, pkg);
        }

        private static ZPackage Pack()
        {
            var pkg = new ZPackage();
            pkg.Write(Shared.Count);
            foreach (Dock d in Shared) { pkg.Write(d.Name); pkg.Write(d.At); }
            return pkg;
        }

        private static void Unpack(ZPackage pkg)
        {
            Shared.Clear();
            int n = pkg.ReadInt();
            for (int i = 0; i < n; i++) Shared.Add(new Dock { Name = pkg.ReadString(), At = pkg.ReadVector3() });
        }

        private static void Load()
        {
            Shared.Clear();
            try
            {
                if (_file == null || !File.Exists(_file)) return;
                foreach (string line in File.ReadAllLines(_file))
                {
                    string[] f = line.Split('\t');
                    if (f.Length == 3 && float.TryParse(f[1], NumberStyles.Float, CultureInfo.InvariantCulture, out float x) && float.TryParse(f[2], NumberStyles.Float, CultureInfo.InvariantCulture, out float z))
                        Shared.Add(new Dock { Name = f[0], At = new Vector3(x, 0f, z) });
                }
            }
            catch (Exception e) { VillagePlebsPlugin.Instance?.Warn("Could not read the docks: " + e.Message); }
        }

        private static void Save()
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(_file));
                File.WriteAllLines(_file, Shared.Select(d => string.Join("\t", Clean(d.Name), d.At.x.ToString("0.0", CultureInfo.InvariantCulture), d.At.z.ToString("0.0", CultureInfo.InvariantCulture))));
            }
            catch (Exception e) { VillagePlebsPlugin.Instance?.Warn("Could not save the docks: " + e.Message); }
        }

        private static string Clean(string s) => (s ?? "").Replace('\t', ' ').Replace('\n', ' ');

        // ---- the list you see ----

        public static List<Dock> All(Player p) => UseShared ? Shared.Select(d => new Dock { Name = d.Name, At = d.At }).ToList() : Own(p);

        public static Dock Add(Player p, string name, Vector3 at)
        {
            if (p == null) return null;
            List<Dock> mine = All(p);
            var d = new Dock { Name = string.IsNullOrWhiteSpace(name) ? NextName(mine) : Clean(name.Trim()), At = new Vector3(at.x, 0f, at.z) };
            if (UseShared) { Edit(true, d.Name, d.At); return d; }
            mine.Add(d);
            SaveOwn(p, mine);
            return d;
        }

        public static void Remove(Player p, Dock d)
        {
            if (p == null || d == null) return;
            if (UseShared) { Edit(false, d.Name, d.At); return; }
            List<Dock> mine = Own(p);
            mine.RemoveAll(x => x.Name == d.Name && Flat(x.At, d.At) < 1f);
            SaveOwn(p, mine);
        }

        // Without the mod on the server: a list per player, kept with the character (one per world).
        private static List<Dock> Own(Player p)
        {
            var list = new List<Dock>();
            if (p == null || !p.m_customData.TryGetValue(Key, out string text) || string.IsNullOrEmpty(text)) return list;
            string world = Profile.WorldId.ToString(CultureInfo.InvariantCulture);
            foreach (string line in text.Split('\n'))
            {
                string[] f = line.Split('\t');
                if (f.Length != 4 || f[0] != world) continue;
                if (!float.TryParse(f[2], NumberStyles.Float, CultureInfo.InvariantCulture, out float x) || !float.TryParse(f[3], NumberStyles.Float, CultureInfo.InvariantCulture, out float z)) continue;
                list.Add(new Dock { Name = f[1], At = new Vector3(x, 0f, z) });
            }
            return list;
        }

        private static void SaveOwn(Player p, List<Dock> mine)
        {
            string world = Profile.WorldId.ToString(CultureInfo.InvariantCulture);
            var lines = new List<string>();
            if (p.m_customData.TryGetValue(Key, out string text) && !string.IsNullOrEmpty(text))
                lines.AddRange(text.Split('\n').Where(l => l.Length > 0 && l.Split('\t')[0] != world));
            lines.AddRange(mine.Select(d => string.Join("\t", world, Clean(d.Name),
                d.At.x.ToString("0.0", CultureInfo.InvariantCulture), d.At.z.ToString("0.0", CultureInfo.InvariantCulture))));
            p.m_customData[Key] = string.Join("\n", lines);
        }

        public static string NextName(List<Dock> mine)
        {
            for (int n = mine.Count + 1; ; n++)
            {
                string name = Words.Show(Words.P("villagepleb_sail_dock_name", n));
                if (mine.All(d => d.Name != name)) return name;
            }
        }

        public static Dock Near(Player p, Vector3 at, float within) =>
            All(p).Where(d => Flat(d.At, at) <= within).OrderBy(d => Flat(d.At, at)).FirstOrDefault();

        private static float Flat(Vector3 a, Vector3 b) { a.y = 0f; b.y = 0f; return Vector3.Distance(a, b); }

        // A free berth within the dock: open water deep enough, nearest the dock's middle, and a hull's length clear of every other ship
        // there (and of where the others sent there will stop).
        public static Vector3 Berth(Vector3 centre, float hull, Ship own, IEnumerable<Vector3> taken)
        {
            var others = Sailing.Ships().Where(s => s != own).Select(s => s.transform.position).Concat(taken).ToList();
            Vector3 best = centre;
            float bestScore = float.MaxValue;
            for (float dx = -Radius; dx <= Radius; dx += 6f)
                for (float dz = -Radius; dz <= Radius; dz += 6f)
                {
                    float r2 = dx * dx + dz * dz;
                    if (r2 > Radius * Radius) continue;
                    var p = new Vector3(centre.x + dx, 0f, centre.z + dz);
                    if (SeaChart.ClearAt(p) < Mathf.Max(4f, hull * 0.4f)) continue;
                    float gap = others.Count == 0 ? float.MaxValue : others.Min(o => Flat(o, p)) - hull * 2f;
                    float score = gap >= 2f ? Mathf.Sqrt(r2) : 10000f - gap;
                    if (score < bestScore) { bestScore = score; best = p; }
                }
            return best;
        }
    }
}
