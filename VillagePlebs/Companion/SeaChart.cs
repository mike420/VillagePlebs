using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Threading;
using System.Threading.Tasks;
using BepInEx;
using UnityEngine;

namespace VillagePlebs
{
    // A sailed course: points across the sea, and how far along it each one lies.
    internal sealed class SeaRoute
    {
        public readonly float[] X, Z, Dist;
        public float Length => Dist[Dist.Length - 1];
        public int Count => X.Length;
        public Vector3 End => new Vector3(X[Count - 1], 0f, Z[Count - 1]);

        public SeaRoute(List<Vector2> points)
        {
            X = new float[points.Count]; Z = new float[points.Count]; Dist = new float[points.Count];
            for (int i = 0; i < points.Count; i++)
            {
                X[i] = points[i].x; Z[i] = points[i].y;
                if (i > 0) Dist[i] = Dist[i - 1] + Vector2.Distance(points[i - 1], points[i]);
            }
        }

        public Vector3 PointAt(float s)
        {
            if (s <= 0f) return new Vector3(X[0], 0f, Z[0]);
            if (s >= Length) return End;
            int lo = 0, hi = Count - 1;
            while (hi - lo > 1) { int mid = (lo + hi) / 2; if (Dist[mid] <= s) lo = mid; else hi = mid; }
            float t = (s - Dist[lo]) / Mathf.Max(0.0001f, Dist[hi] - Dist[lo]);
            return new Vector3(Mathf.Lerp(X[lo], X[hi], t), 0f, Mathf.Lerp(Z[lo], Z[hi], t));
        }

        // How far along the course the nearest point to p lies, looking no further back than from and no further ahead than window.
        public float Project(Vector3 p, float from, float window, out float off)
        {
            float best = float.MaxValue, at = from, until = Mathf.Min(Length, from + window);
            for (int i = 1; i < Count; i++)
            {
                if (Dist[i] < from) continue;
                if (Dist[i - 1] > until) break;
                float dx = X[i] - X[i - 1], dz = Z[i] - Z[i - 1], len2 = dx * dx + dz * dz;
                float t = len2 > 0.0001f ? Mathf.Clamp01(((p.x - X[i - 1]) * dx + (p.z - Z[i - 1]) * dz) / len2) : 0f;
                float s = Mathf.Max(from, Dist[i - 1] + (Dist[i] - Dist[i - 1]) * t);
                if (s > until) continue;
                Vector3 q = PointAt(s);
                float d = (q.x - p.x) * (q.x - p.x) + (q.z - p.z) * (q.z - p.z);
                if (d < best) { best = d; at = s; }
            }
            off = best == float.MaxValue ? 0f : Mathf.Sqrt(best);
            return at;
        }
    }

    // The sea as the world seed makes it: how deep each 10 m square is, how far it lies from the shallows, and which waters join up.
    // Charted once per world on a background thread (a few seconds) and kept on disk; courses are found on it the same way.
    internal static class SeaChart
    {
        private const float Cell = 10f, WorldRadius = 10500f, Zone = 64f;
        // Water shallower than 2.5 m counts as shallows: no ship runs aground there.
        private const int MinDepth = 25, Magic = 0x56505343, Version = 1;

        private static int _size, _seed, _generation;
        private static float _half;
        private static byte[] _depth, _clear;
        private static ushort[] _comp;
        private static float[] _cost;
        private static byte[] _from;
        private static bool _labelsStale;
        private static readonly ConcurrentQueue<Action> MainThread = new ConcurrentQueue<Action>();
        private static readonly ConcurrentQueue<int> Newly = new ConcurrentQueue<int>();
        private static readonly object Lock = new object();

        public static bool Ready { get; private set; }
        public static bool Building { get; private set; }
        public static float Progress { get; private set; }
        public static float WaterLevel => ZoneSystem.instance != null ? ZoneSystem.instance.m_waterLevel : 30f;

        public static void Tick()
        {
            while (MainThread.TryDequeue(out Action a)) a();
            if ((Ready || Building) && (WorldGenerator.instance == null || ZNet.instance == null || WorldGenerator.instance.GetSeed() != _seed)) Unload();
        }

        public static void Unload()
        {
            _generation++;
            Ready = Building = false;
            _depth = _clear = _from = null; _comp = null; _cost = null;
        }

        // Starts charting this world's seas if that is not done yet.
        public static void Ensure()
        {
            if (Ready || Building || WorldGenerator.instance == null || ZNet.instance == null) return;
            Building = true;
            Progress = 0f;
            int gen = ++_generation, seed = WorldGenerator.instance.GetSeed();
            _seed = seed;
            float water = WaterLevel;
            WorldGenerator world = WorldGenerator.instance;
            string file = Path.Combine(Paths.BepInExRootPath, "VillagePlebs", "Sea", seed + ".bin");
            var thread = new Thread(() =>
            {
                try
                {
                    int size = Mathf.CeilToInt(WorldRadius * 2f / Cell);
                    var depth = new byte[size * size];
                    if (!Load(file, seed, size, depth))
                    {
                        Fill(world, water, size, depth, gen);
                        if (gen != _generation) return;
                        Save(file, seed, size, depth);
                    }
                    byte[] clear = Clearance(size, depth);
                    ushort[] comp = Label(size, clear);
                    MainThread.Enqueue(() =>
                    {
                        if (gen != _generation) return;
                        _size = size; _half = size * Cell * 0.5f;
                        _depth = depth; _clear = clear; _comp = comp;
                        _cost = new float[size * size]; _from = new byte[size * size];
                        for (int i = 0; i < _cost.Length; i++) _cost[i] = float.PositiveInfinity;
                        Ready = true; Building = false; Progress = 1f;
                    });
                }
                catch (Exception e)
                {
                    MainThread.Enqueue(() => { VillagePlebsPlugin.Instance?.Warn("Could not chart the seas: " + e); if (gen == _generation) Building = false; });
                }
            }) { IsBackground = true, Priority = System.Threading.ThreadPriority.BelowNormal, Name = "VillagePlebs sea chart" };
            thread.Start();
        }

        private static float SmoothStep(float t) { t = Mathf.Clamp01(t); return t * t * (3f - 2f * t); }

        // Ground height the way the game makes its terrain: each zone corner's biome height, blended across the zone.
        private static void Fill(WorldGenerator world, float water, int size, byte[] depth, int gen)
        {
            int corners = Mathf.CeilToInt(WorldRadius * 2f / Zone) + 3, offset = corners / 2;
            var biome = new Heightmap.Biome[corners * corners];
            var opts = new ParallelOptions { MaxDegreeOfParallelism = Math.Max(1, Environment.ProcessorCount / 2) };
            Parallel.For(0, corners, opts, cz =>
            {
                for (int cx = 0; cx < corners; cx++) biome[cz * corners + cx] = world.GetBiome((cx - offset) * Zone - Zone / 2f, (cz - offset) * Zone - Zone / 2f);
            });
            int done = 0;
            float half = size * Cell * 0.5f;
            Parallel.For(0, size, opts, iz =>
            {
                if (gen != _generation) return;
                float z = (iz + 0.5f) * Cell - half;
                int cz = Mathf.FloorToInt((z + Zone / 2f) / Zone) + offset;
                float tz = SmoothStep((z - ((cz - offset) * Zone - Zone / 2f)) / Zone);
                for (int ix = 0; ix < size; ix++)
                {
                    float x = (ix + 0.5f) * Cell - half;
                    if (x * x + z * z > WorldRadius * WorldRadius) continue;
                    int cx = Mathf.FloorToInt((x + Zone / 2f) / Zone) + offset;
                    if (cx < 0 || cz < 0 || cx + 1 >= corners || cz + 1 >= corners) continue;
                    Heightmap.Biome a = biome[cz * corners + cx], b = biome[cz * corners + cx + 1], c = biome[(cz + 1) * corners + cx], d = biome[(cz + 1) * corners + cx + 1];
                    float h;
                    if (a == b && a == c && a == d) h = world.GetBiomeHeight(a, x, z, out _);
                    else
                    {
                        float tx = SmoothStep((x - ((cx - offset) * Zone - Zone / 2f)) / Zone);
                        float low = Mathf.Lerp(world.GetBiomeHeight(a, x, z, out _), world.GetBiomeHeight(b, x, z, out _), tx);
                        float high = Mathf.Lerp(world.GetBiomeHeight(c, x, z, out _), world.GetBiomeHeight(d, x, z, out _), tx);
                        h = Mathf.Lerp(low, high, tz);
                    }
                    depth[iz * size + ix] = (byte)Mathf.Clamp((water - h) * 10f, 0f, 255f);
                }
                Progress = Interlocked.Increment(ref done) / (float)size;
            });
        }

        // Metres from each sailable square to the nearest shallows (two-pass chamfer distance), up to 255.
        private static byte[] Clearance(int size, byte[] depth)
        {
            int n = size * size;
            var d = new ushort[n];
            for (int i = 0; i < n; i++) d[i] = depth[i] >= MinDepth ? ushort.MaxValue : (ushort)0;
            for (int z = 0; z < size; z++)
                for (int x = 0; x < size; x++)
                {
                    int i = z * size + x;
                    if (d[i] == 0) continue;
                    if (x == 0 || z == 0 || x == size - 1 || z == size - 1) { d[i] = 3; continue; }
                    int v = d[i];
                    v = Math.Min(v, d[i - 1] + 3); v = Math.Min(v, d[i - size] + 3);
                    v = Math.Min(v, d[i - size - 1] + 4); v = Math.Min(v, d[i - size + 1] + 4);
                    d[i] = (ushort)v;
                }
            for (int z = size - 2; z >= 1; z--)
                for (int x = size - 2; x >= 1; x--)
                {
                    int i = z * size + x;
                    if (d[i] == 0) continue;
                    int v = d[i];
                    v = Math.Min(v, d[i + 1] + 3); v = Math.Min(v, d[i + size] + 3);
                    v = Math.Min(v, d[i + size + 1] + 4); v = Math.Min(v, d[i + size - 1] + 4);
                    d[i] = (ushort)v;
                }
            var clear = new byte[n];
            for (int i = 0; i < n; i++) clear[i] = (byte)Math.Min(255f, d[i] * Cell / 3f);
            return clear;
        }

        // Which waters join up, so a course is only looked for where there is one.
        private static ushort[] Label(int size, byte[] clear)
        {
            int n = size * size;
            var comp = new ushort[n];
            var stack = new Stack<int>();
            int next = 1;
            for (int i = 0; i < n; i++)
            {
                if (clear[i] == 0 || comp[i] != 0) continue;
                ushort id = (ushort)Math.Min(next++, ushort.MaxValue);
                comp[i] = id;
                stack.Push(i);
                while (stack.Count > 0)
                {
                    int j = stack.Pop(), x = j % size;
                    if (x > 0 && clear[j - 1] > 0 && comp[j - 1] == 0) { comp[j - 1] = id; stack.Push(j - 1); }
                    if (x < size - 1 && clear[j + 1] > 0 && comp[j + 1] == 0) { comp[j + 1] = id; stack.Push(j + 1); }
                    if (j >= size && clear[j - size] > 0 && comp[j - size] == 0) { comp[j - size] = id; stack.Push(j - size); }
                    if (j < n - size && clear[j + size] > 0 && comp[j + size] == 0) { comp[j + size] = id; stack.Push(j + size); }
                }
            }
            return comp;
        }

        private static void Save(string file, int seed, int size, byte[] depth)
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(file));
                using (var fs = File.Create(file))
                using (var gz = new GZipStream(fs, System.IO.Compression.CompressionLevel.Fastest))
                using (var w = new BinaryWriter(gz))
                {
                    w.Write(Magic); w.Write(Version); w.Write(seed); w.Write(size); w.Write(Cell);
                    w.Write(depth);
                }
            }
            catch (Exception e) { MainThread.Enqueue(() => VillagePlebsPlugin.Instance?.Warn("Could not save the sea chart: " + e.Message)); }
        }

        private static bool Load(string file, int seed, int size, byte[] depth)
        {
            try
            {
                if (!File.Exists(file)) return false;
                using (var fs = File.OpenRead(file))
                using (var gz = new GZipStream(fs, CompressionMode.Decompress))
                using (var r = new BinaryReader(gz))
                {
                    if (r.ReadInt32() != Magic || r.ReadInt32() != Version || r.ReadInt32() != seed || r.ReadInt32() != size || Math.Abs(r.ReadSingle() - Cell) > 0.001f) return false;
                    for (int at = 0; at < depth.Length;)
                    {
                        int got = r.Read(depth, at, depth.Length - at);
                        if (got <= 0) return false;
                        at += got;
                    }
                    return true;
                }
            }
            catch { return false; }
        }

        private static int Index(float x, float z) =>
            Mathf.Clamp(Mathf.FloorToInt((z + _half) / Cell), 0, _size - 1) * _size + Mathf.Clamp(Mathf.FloorToInt((x + _half) / Cell), 0, _size - 1);

        private static Vector2 Centre(int i) => new Vector2((i % _size + 0.5f) * Cell - _half, (i / _size + 0.5f) * Cell - _half);

        // Metres of open water around a spot (0 on land or in the shallows).
        public static float ClearAt(Vector3 p) => Ready ? _clear[Index(p.x, p.z)] : 255f;

        // Something in the way that the seed does not know about (a rock, a pier): the course goes round it from now on.
        public static void MarkBlocked(Vector3 p) { if (Ready) Newly.Enqueue(Index(p.x, p.z)); }

        private static void ApplyBlocked()
        {
            while (Newly.TryDequeue(out int i))
            {
                if (_clear[i] == 0) continue;
                _clear[i] = 0;
                _labelsStale = true;
                int cx = i % _size, cz = i / _size;
                for (int dz = -6; dz <= 6; dz++)
                    for (int dx = -6; dx <= 6; dx++)
                    {
                        int x = cx + dx, z = cz + dz;
                        if (x < 0 || z < 0 || x >= _size || z >= _size) continue;
                        int j = z * _size + x;
                        float m = Mathf.Sqrt(dx * dx + dz * dz) * Cell;
                        if (_clear[j] > 0 && m < _clear[j]) _clear[j] = (byte)Mathf.Max(1f, m);
                    }
            }
        }

        // Finds a course for a ship from one point to another (or to the nearest sailable water to it). The answer comes back on the main thread:
        // a route, or the reason there is none (a translation key).
        public static void Plan(Vector3 from, Vector3 to, float hull, Action<SeaRoute, string> done)
        {
            if (!Ready) { done(null, "villagepleb_sail_chart_not_ready"); return; }
            int gen = _generation;
            float pref = Mathf.Max(hull + 8f, 40f);
            Task.Run(() =>
            {
                SeaRoute route = null;
                string why = "villagepleb_sail_no_route";
                try
                {
                    lock (Lock)
                    {
                        if (gen != _generation) return;
                        ApplyBlocked();
                        if (_labelsStale) { _comp = Label(_size, _clear); _labelsStale = false; }
                        route = Find(from, to, hull, pref, gen, out why);
                    }
                }
                catch (Exception e) { MainThread.Enqueue(() => VillagePlebsPlugin.Instance?.Warn("Sea route planning failed: " + e)); }
                MainThread.Enqueue(() => { if (gen == _generation) done(route, why); });
            });
        }

        private static readonly int[] Dx = { 1, -1, 0, 0, 1, 1, -1, -1 }, Dz = { 0, 0, 1, -1, 1, -1, 1, -1 };

        private static SeaRoute Find(Vector3 from, Vector3 to, float hull, float pref, int gen, out string why)
        {
            why = null;
            int start = Nearest(Index(from.x, from.z), 25, 0);
            if (start < 0) { why = "villagepleb_sail_no_water_here"; return null; }
            int goal = Nearest(Index(to.x, to.z), 60, _comp[start]);
            if (goal < 0) { why = Nearest(Index(to.x, to.z), 60, 0) < 0 ? "villagepleb_sail_no_water_there" : "villagepleb_sail_not_joined"; return null; }
            List<int> cells = start == goal ? new List<int> { start } : Search(start, goal, hull, pref, gen);
            if (cells == null) { why = gen != _generation ? "villagepleb_sail_chart_not_ready" : "villagepleb_sail_no_route"; return null; }
            cells = Straighten(cells, pref);
            var points = new List<Vector2> { new Vector2(from.x, from.z) };
            for (int i = cells.Count > 1 ? 1 : 0; i < cells.Count; i++) points.Add(Centre(cells[i]));
            if (points.Count == 1) points.Add(points[0]);
            return new SeaRoute(points);
        }

        // The sailable square nearest to i (within rings squares), in the waters comp if given.
        private static int Nearest(int i, int rings, ushort comp)
        {
            int cx = i % _size, cz = i / _size;
            for (int r = 0; r <= rings; r++)
            {
                int best = -1, bestD = int.MaxValue;
                for (int dz = -r; dz <= r; dz++)
                {
                    int z = cz + dz;
                    if (z < 0 || z >= _size) continue;
                    bool edge = dz == -r || dz == r;
                    for (int dx = -r; dx <= r; dx += edge || r == 0 ? 1 : 2 * r)
                    {
                        int x = cx + dx;
                        if (x >= 0 && x < _size)
                        {
                            int j = z * _size + x;
                            if (_clear[j] > 0 && (comp == 0 || _comp[j] == comp) && dx * dx + dz * dz < bestD) { bestD = dx * dx + dz * dz; best = j; }
                        }
                        if (r == 0) break;
                    }
                }
                if (best >= 0) return best;
            }
            return -1;
        }

        // A* over the squares, steering clear of the shallows where there is room.
        private static List<int> Search(int start, int goal, float hull, float pref, int gen)
        {
            var touched = new List<int>(65536);
            var heap = new List<KeyValuePair<float, int>>(16384);
            int gx = goal % _size, gz = goal / _size;
            try
            {
                _cost[start] = 0f; _from[start] = 9; touched.Add(start);
                Push(heap, Heur(start % _size, start / _size, gx, gz), start);
                int popped = 0;
                while (heap.Count > 0)
                {
                    int cur = Pop(heap);
                    if ((_from[cur] & 0x80) != 0) continue;
                    if (cur == goal) return Back(start, goal);
                    _from[cur] |= 0x80;
                    if ((++popped & 0xFFF) == 0 && gen != _generation) return null;
                    int cx = cur % _size, cz = cur / _size;
                    for (int k = 0; k < 8; k++)
                    {
                        int x = cx + Dx[k], z = cz + Dz[k];
                        if (x < 0 || z < 0 || x >= _size || z >= _size) continue;
                        int j = z * _size + x;
                        byte c = _clear[j];
                        if (c == 0 || (_from[j] & 0x80) != 0) continue;
                        float step = Cell;
                        if (k >= 4)
                        {
                            if (_clear[cz * _size + x] == 0 || _clear[z * _size + cx] == 0) continue;
                            step = Cell * 1.4142135f;
                        }
                        float pen = 0f;
                        if (c < pref) { float f = (pref - c) / pref; pen = 3f * f * f + (c < hull ? 8f : 0f); }
                        float g = _cost[cur] + step * (1f + pen);
                        if (g >= _cost[j]) continue;
                        if (_from[j] == 0) touched.Add(j);
                        _cost[j] = g;
                        _from[j] = (byte)(k + 1);
                        Push(heap, g + Heur(x, z, gx, gz), j);
                    }
                }
                return null;
            }
            finally
            {
                foreach (int i in touched) { _cost[i] = float.PositiveInfinity; _from[i] = 0; }
            }
        }

        private static float Heur(int x, int z, int gx, int gz)
        {
            int a = Math.Abs(x - gx), b = Math.Abs(z - gz);
            return (Math.Max(a, b) + 0.41421357f * Math.Min(a, b)) * Cell;
        }

        private static List<int> Back(int start, int goal)
        {
            var path = new List<int>();
            for (int i = goal; i != start;)
            {
                path.Add(i);
                int k = (_from[i] & 0x7F) - 1;
                i = (i / _size - Dz[k]) * _size + (i % _size - Dx[k]);
            }
            path.Add(start);
            path.Reverse();
            return path;
        }

        // Cuts the corners of the square-by-square path wherever a straight line keeps as much water to each side.
        private static List<int> Straighten(List<int> path, float pref)
        {
            if (path.Count <= 2) return path;
            var keep = new List<int> { path[0] };
            int anchor = 0;
            float need = Mathf.Min(pref, _clear[path[0]]);
            for (int i = 1; i < path.Count - 1;)
            {
                float n = Mathf.Min(need, Mathf.Min(_clear[path[i]], _clear[path[i + 1]]));
                if (Sight(path[anchor], path[i + 1], n)) { need = n; i++; continue; }
                keep.Add(path[i]);
                anchor = i;
                need = Mathf.Min(pref, _clear[path[i]]);
                i = anchor + 1;
            }
            keep.Add(path[path.Count - 1]);
            return keep;
        }

        private static bool Sight(int a, int b, float need)
        {
            float ax = a % _size, az = a / _size, dx = b % _size - ax, dz = b / _size - az, len = Mathf.Sqrt(dx * dx + dz * dz);
            int steps = Mathf.CeilToInt(len * 3f);
            need = Mathf.Max(1f, need);
            for (int s = 1; s < steps; s++)
            {
                float t = s / (float)steps;
                int x = Mathf.RoundToInt(ax + dx * t), z = Mathf.RoundToInt(az + dz * t);
                if (_clear[z * _size + x] < need) return false;
            }
            return true;
        }

        private static void Push(List<KeyValuePair<float, int>> heap, float key, int val)
        {
            heap.Add(new KeyValuePair<float, int>(key, val));
            for (int i = heap.Count - 1; i > 0;)
            {
                int p = (i - 1) >> 1;
                if (heap[p].Key <= heap[i].Key) break;
                (heap[p], heap[i]) = (heap[i], heap[p]);
                i = p;
            }
        }

        private static int Pop(List<KeyValuePair<float, int>> heap)
        {
            int top = heap[0].Value, last = heap.Count - 1;
            heap[0] = heap[last];
            heap.RemoveAt(last);
            for (int i = 0; ;)
            {
                int l = 2 * i + 1, r = l + 1, m = i;
                if (l < heap.Count && heap[l].Key < heap[m].Key) m = l;
                if (r < heap.Count && heap[r].Key < heap[m].Key) m = r;
                if (m == i) break;
                (heap[m], heap[i]) = (heap[i], heap[m]);
                i = m;
            }
            return top;
        }
    }
}
