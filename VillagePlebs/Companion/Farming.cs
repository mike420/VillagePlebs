using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using UnityEngine;

namespace VillagePlebs
{
    internal static class Farming
    {
        internal class Crop
        {
            public Piece Piece;
            public Plant Plant;
            public string Name;
            public float Reach, Step;
            public readonly HashSet<string> Grown = new HashSet<string>();
        }

        private class Field
        {
            public Vector3 Center;
            public float Radius, Until;
            public HashSet<Vector2Int> Cells = new HashSet<Vector2Int>();
            public readonly Dictionary<Vector2Int, float> Bad = new Dictionary<Vector2Int, float>();
        }

        private static readonly int Space = LayerMask.GetMask("Default", "static_solid", "Default_small", "piece", "piece_nonsolid");
        private static readonly int Roof = LayerMask.GetMask("Default", "static_solid", "piece");
        private static readonly AccessTools.FieldRef<Piece, long> Creator = AccessTools.FieldRefAccess<Piece, long>("m_creator");
        private static readonly Dictionary<PieceTable, List<Crop>> Known = new Dictionary<PieceTable, List<Crop>>();
        private static readonly Dictionary<long, Field> Fields = new Dictionary<long, Field>();
        private static readonly List<Crop> None = new List<Crop>();

        public static ItemDrop.ItemData Cultivator(Humanoid h) =>
            h.GetInventory().GetAllItems().FirstOrDefault(i => IsCultivator(i) && (!i.m_shared.m_useDurability || i.m_durability > 0f));

        public static bool IsCultivator(ItemDrop.ItemData i) => Crops(i.m_shared.m_buildPieces).Count > 0;

        // Seeds it keeps rather than putting away: real seeds whenever it has a cultivator to sow them with, and while planting
        // also the food crops (carrots, turnips) a patch already grows from.
        public static bool IsSeed(Humanoid h, ItemDrop.ItemData i) =>
            Cultivator(h) is ItemDrop.ItemData tool && (IsSeedItem(i) || Brain.Get(h)?.Farm == Errand.Plant
                && Crops(tool.m_shared.m_buildPieces).Any(c => c.Piece.m_resources.Any(r => r.m_resItem != null && r.m_resItem.m_itemData.m_shared.m_name == i.m_shared.m_name)));

        private static HashSet<string> _seeds;

        // Seeds proper: what any cultivator plants that is not food, so it never raids the larder to sow.
        public static bool IsSeedItem(ItemDrop.ItemData i)
        {
            if (_seeds == null && ObjectDB.instance != null && ObjectDB.instance.m_items.Count > 0)
            {
                _seeds = new HashSet<string>();
                foreach (GameObject go in ObjectDB.instance.m_items)
                {
                    ItemDrop d = go != null ? go.GetComponent<ItemDrop>() : null;
                    if (d == null) continue;
                    foreach (Crop c in Crops(d.m_itemData.m_shared.m_buildPieces))
                        foreach (Piece.Requirement r in c.Piece.m_resources)
                            if (r.m_resItem != null && !Food.IsFood(r.m_resItem.m_itemData)) _seeds.Add(r.m_resItem.m_itemData.m_shared.m_name);
                }
            }
            return _seeds != null && _seeds.Contains(i.m_shared.m_name);
        }

        private static bool WantsFromChest(Humanoid me, ItemDrop.ItemData item, bool plant) =>
            IsCultivator(item) ? Cultivator(me) == null : plant && IsSeedItem(item) && me.GetInventory().CountItems(item.m_shared.m_name) < 50;

        public static bool Wants(Humanoid me, ItemDrop.ItemData item) => Brain.Get(me)?.Farm == Errand.Plant && WantsFromChest(me, item, true);

        public static bool CanSow(Humanoid me) =>
            Cultivator(me) is ItemDrop.ItemData tool && Crops(tool.m_shared.m_buildPieces).Any(c => Afford(me.GetInventory(), c.Piece));

        public static bool HasField(BrainState st, Vector3 center, float radius) => FieldOf(st, center, radius).Cells.Count > 0;

        public static IEnumerable<Pickable> RipeNear(Vector3 center, float radius) =>
            Physics.OverlapSphere(center, radius + FieldReach, ~0, QueryTriggerInteraction.Collide)
                .Select(col => (col.attachedRigidbody != null ? col.attachedRigidbody.gameObject : col.transform.root.gameObject).GetComponent<Pickable>())
                .Where(Ripe).Distinct();

        // One of its own chests holding the cultivator or seeds it lacks, when there is tilled ground at home to use them on.
        public static Container SeedChest(BrainState st, Vector3 center, float radius, System.Func<Container, bool> allowed)
        {
            Humanoid me = st.Body;
            ItemDrop.ItemData tool = Cultivator(me);
            bool needTool = tool == null;
            bool needSeeds = needTool || !Crops(tool.m_shared.m_buildPieces).Any(c => Afford(me.GetInventory(), c.Piece));
            if (!needTool && !needSeeds || FieldOf(st, center, radius).Cells.Count == 0) return null;
            return Home.Chests(me).Where(c => !Containers.InUse(c))
                .Where(c => needTool ? c.GetInventory().GetAllItems().Any(IsCultivator) : c.GetInventory().GetAllItems().Any(IsSeedItem))
                .OrderBy(c => Vector3.Distance(c.transform.position, me.transform.position)).FirstOrDefault(allowed);
        }

        public static void TakeFromChests(Humanoid me, bool plant)
        {
            Inventory mine = me.GetInventory();
            foreach (Container c in Home.Chests(me))
            {
                if (!c.GetInventory().GetAllItems().Any(i => WantsFromChest(me, i, plant)) || !Containers.Take(c)) continue;
                foreach (ItemDrop.ItemData item in c.GetInventory().GetAllItems().ToList())
                {
                    if (!WantsFromChest(me, item, plant) || !mine.CanAddItem(item)) continue;
                    item.m_equipped = false;
                    mine.MoveItemToThis(c.GetInventory(), item);
                }
            }
        }

        public static bool Ripe(Pickable p) =>
            p != null && p.m_itemPrefab != null && Goals.IsCrop(Utils.GetPrefabName(p.gameObject)) && !(Companion.Zdo(p)?.GetBool(ZDOVars.s_picked, false) ?? true);

        public static List<Crop> Crops(PieceTable table)
        {
            if (table == null) return None;
            if (Known.TryGetValue(table, out List<Crop> list)) return list;
            list = new List<Crop>();
            foreach (GameObject go in table.m_pieces)
            {
                Piece piece = go != null ? go.GetComponent<Piece>() : null;
                Plant plant = go != null ? go.GetComponent<Plant>() : null;
                if (piece == null || plant == null || !(plant.m_needCultivatedGround || piece.m_cultivatedGroundOnly) || piece.m_resources.Length == 0) continue;
                if (plant.m_grownPrefabs.Any(g => g != null && g.GetComponent<TreeBase>() != null)) continue;
                var crop = new Crop { Piece = piece, Plant = plant, Name = go.name };
                float reach = Reach(go);
                foreach (GameObject grown in plant.m_grownPrefabs)
                    if (grown != null) { crop.Grown.Add(grown.name); reach = Mathf.Max(reach, Reach(grown) * plant.m_maxScale); }
                crop.Reach = Mathf.Clamp(reach, 0.05f, 1.5f);
                crop.Step = plant.m_growRadius + crop.Reach + 0.05f;
                list.Add(crop);
            }
            Known[table] = list;
            return list;
        }

        // How far a prefab's solid colliders reach out from its root, flat: what a neighbour's grow check would bump into.
        private static float Reach(GameObject prefab)
        {
            float reach = 0f;
            foreach (Collider col in prefab.GetComponentsInChildren<Collider>(true))
            {
                if (col.isTrigger || (Space & (1 << col.gameObject.layer)) == 0) continue;
                Vector3 s = col.transform.lossyScale;
                float sx = Mathf.Abs(s.x), sz = Mathf.Abs(s.z), k = Mathf.Max(sx, sz);
                Vector3 local = col is SphereCollider sc0 ? sc0.center : col is CapsuleCollider cc0 ? cc0.center : col is BoxCollider bc0 ? bc0.center
                              : col is MeshCollider mc0 && mc0.sharedMesh != null ? mc0.sharedMesh.bounds.center : Vector3.zero;
                Vector3 off = col.transform.TransformPoint(local) - prefab.transform.position;
                off.y = 0f;
                float size = col switch
                {
                    SphereCollider sc => sc.radius * k,
                    CapsuleCollider cc => cc.direction == 1 ? cc.radius * k : cc.height * 0.5f * k,
                    BoxCollider bc => new Vector2(bc.size.x * sx, bc.size.z * sz).magnitude * 0.5f,
                    MeshCollider mc when mc.sharedMesh != null => new Vector2(mc.sharedMesh.bounds.extents.x * sx, mc.sharedMesh.bounds.extents.z * sz).magnitude,
                    _ => 0.25f,
                };
                reach = Mathf.Max(reach, off.magnitude + size);
            }
            return reach;
        }

        private static bool Afford(Inventory inv, Piece piece) =>
            piece.m_resources.All(r => r.m_resItem == null || inv.CountItems(r.m_resItem.m_itemData.m_shared.m_name) >= r.m_amount);

        private static int Seeds(Inventory inv, Piece piece) =>
            piece.m_resources.Where(r => r.m_resItem != null).Select(r => inv.CountItems(r.m_resItem.m_itemData.m_shared.m_name) / Mathf.Max(1, r.m_amount)).DefaultIfEmpty(0).Min();

        private static Vector2Int Cell(Vector3 p) => new Vector2Int(Mathf.FloorToInt(p.x), Mathf.FloorToInt(p.z));

        private static Vector2Int Spot(Vector3 p) => new Vector2Int(Mathf.RoundToInt(p.x * 4f), Mathf.RoundToInt(p.z * 4f));

        private static float Flat(Vector3 a, Vector3 b) { a.y = 0f; b.y = 0f; return Vector3.Distance(a, b); }

        public const float FieldReach = 40f;

        private static Field FieldOf(BrainState st, Vector3 center, float radius)
        {
            long id = Companion.IdOf(st.Body);
            if (Fields.TryGetValue(id, out Field f) && Time.time < f.Until && Flat(f.Center, center) < 1f && Mathf.Approximately(f.Radius, radius)) return f;
            if (f == null) Fields[id] = f = new Field();
            f.Center = center; f.Radius = radius; f.Until = Time.time + 30f;
            f.Cells = new HashSet<Vector2Int>();
            Heightmap hm = null;
            bool Tilled(Vector2Int c)
            {
                var p = new Vector3(c.x + 0.5f, center.y, c.y + 0.5f);
                if (hm == null || !hm.IsPointInside(p)) hm = Heightmap.FindHeightmap(p);
                return hm != null && hm.IsCultivated(p);
            }
            int r = Mathf.CeilToInt(radius);
            var open = new Queue<Vector2Int>();
            for (int x = -r; x <= r; x++)
                for (int z = -r; z <= r; z++)
                {
                    if (x * x + z * z > r * r) continue;
                    var c = new Vector2Int(Mathf.FloorToInt(center.x) + x, Mathf.FloorToInt(center.z) + z);
                    if (Tilled(c) && f.Cells.Add(c)) open.Enqueue(c);
                }
            // A field that runs past the edge of its home is still one field: follow the tilled ground out to its end.
            float far = radius + FieldReach;
            while (open.Count > 0 && f.Cells.Count < 6000)
            {
                Vector2Int c = open.Dequeue();
                for (int dx = -1; dx <= 1; dx++)
                    for (int dz = -1; dz <= 1; dz++)
                    {
                        var n = new Vector2Int(c.x + dx, c.y + dz);
                        if (f.Cells.Contains(n) || Flat(new Vector3(n.x + 0.5f, 0f, n.y + 0.5f), center) > far || !Tilled(n)) continue;
                        f.Cells.Add(n);
                        open.Enqueue(n);
                    }
            }
            foreach (Vector2Int gone in f.Bad.Where(kv => kv.Value < Time.time).Select(kv => kv.Key).ToList()) f.Bad.Remove(gone);
            return f;
        }

        // A fresh look at the field: new tilled ground, and spots given up on before it had these seeds.
        public static void Forget(BrainState st)
        {
            if (Fields.TryGetValue(Companion.IdOf(st.Body), out Field f)) { f.Until = 0f; f.Bad.Clear(); }
        }

        public static void Bad(BrainState st, Vector3 spot)
        {
            if (Fields.TryGetValue(Companion.IdOf(st.Body), out Field f)) f.Bad[Spot(spot)] = Time.time + 300f;
        }

        // Next free spot on the home's cultivated ground: one grid of rows and columns over all of it, lined up on the world
        // and spaced for the widest crop the cultivator plants, so every crop fits every spot and stays healthy.
        // A spot takes the crop growing next to it if it has those seeds, else whichever seeds it has most of.
        // Why FindSpot found nothing: every spot is planted or blocked, none of its seeds grow on the free ones, or it can't get to them.
        public enum Miss { Full, WontGrow, CantReach, NoSeeds }

        private enum Fit { Ok, Climate, Blocked }

        public static bool FindSpot(BrainState st, Vector3 center, float radius, out Crop crop, out Vector3 spot) => FindSpot(st, center, radius, out crop, out spot, out _);

        public static bool FindSpot(BrainState st, Vector3 center, float radius, out Crop crop, out Vector3 spot, out Miss why)
        {
            crop = null; spot = Vector3.zero; why = Miss.Full;
            Humanoid me = st.Body;
            ItemDrop.ItemData tool = Cultivator(me);
            if (tool == null) return false;
            List<Crop> all = Crops(tool.m_shared.m_buildPieces);
            Inventory inv = me.GetInventory();
            if (!all.Any(c => Afford(inv, c.Piece))) return false;
            Field field = FieldOf(st, center, radius);
            if (field.Cells.Count == 0) { Note(me, $"found no tilled ground within {radius:0} m of its home at {center:F0}"); return false; }
            var planted = new List<KeyValuePair<Vector3, Crop>>();
            var seen = new HashSet<GameObject>();
            int minX = field.Cells.Min(c => c.x), maxX = field.Cells.Max(c => c.x) + 1, minZ = field.Cells.Min(c => c.y), maxZ = field.Cells.Max(c => c.y) + 1;
            var box = new Vector3((minX + maxX) * 0.5f, center.y, (minZ + maxZ) * 0.5f);
            foreach (Collider col in Physics.OverlapBox(box, new Vector3((maxX - minX) * 0.5f + 2f, 200f, (maxZ - minZ) * 0.5f + 2f), Quaternion.identity, Space))
            {
                GameObject go = col.attachedRigidbody != null ? col.attachedRigidbody.gameObject : col.transform.root.gameObject;
                if (!seen.Add(go)) continue;
                string name = Utils.GetPrefabName(go);
                Crop c = all.FirstOrDefault(k => k.Name == name || k.Grown.Contains(name));
                if (c != null) planted.Add(new KeyValuePair<Vector3, Crop>(go.transform.position, c));
            }
            var near = new Dictionary<Vector2Int, List<KeyValuePair<Vector3, Crop>>>();
            foreach (var kv in planted)
            {
                Vector2Int cell = Cell(kv.Key);
                if (!near.TryGetValue(cell, out var l)) near[cell] = l = new List<KeyValuePair<Vector3, Crop>>();
                l.Add(kv);
            }
            IEnumerable<KeyValuePair<Vector3, Crop>> Around(Vector3 p, int cells)
            {
                Vector2Int c = Cell(p);
                for (int x = -cells; x <= cells; x++)
                    for (int z = -cells; z <= cells; z++)
                        if (near.TryGetValue(new Vector2Int(c.x + x, c.y + z), out var l)) foreach (var kv in l) yield return kv;
            }
            bool Taken(Vector3 p, float within) => Around(p, Mathf.CeilToInt(within)).Any(kv => Flat(kv.Key, p) < within);

            List<Crop> seeds = all.Where(k => Afford(inv, k.Piece) && k.Piece.m_resources.All(r => r.m_resItem == null || !Food.IsFood(r.m_resItem.m_itemData)))
                                  .OrderByDescending(k => Seeds(inv, k.Piece)).ToList();
            List<Crop> sown = planted.Select(kv => kv.Value).Where(c => Afford(inv, c.Piece)).Distinct().ToList();
            if (seeds.Count == 0 && sown.Count == 0) { why = Miss.NoSeeds; return false; }
            float step = seeds.Concat(sown).Concat(planted.Select(kv => kv.Value)).Max(c => c.Step);
            IEnumerable<Crop> Choices(Vector3 p) =>
                Around(p, Mathf.CeilToInt(step * 1.5f)).Where(kv => Flat(kv.Key, p) <= step * 1.5f).OrderBy(kv => Flat(kv.Key, p))
                    .Select(kv => kv.Value).Where(c => Afford(inv, c.Piece)).Concat(seeds).Distinct();

            var options = new List<Vector3>();
            for (int i = Mathf.CeilToInt(minX / step); i * step < maxX; i++)
                for (int j = Mathf.CeilToInt(minZ / step); j * step < maxZ; j++)
                {
                    var p = new Vector3(i * step, 0f, j * step);
                    if (field.Cells.Contains(Cell(p)) && !Taken(p, step * 0.5f)) options.Add(p);
                }
            int gridSpots = options.Count;
            if (gridSpots == 0)
                options.AddRange(field.Cells.Select(c => new Vector3(c.x + 0.5f, 0f, c.y + 0.5f)).Where(p => !Taken(p, step * 0.75f)));

            int reachTries = 0, skipped = 0, blockedSpots = 0, climateSpots = 0;
            bool climate = false;
            string firstBlock = null;
            foreach (Vector3 o in options.OrderBy(o => Flat(o, me.transform.position)))
            {
                if (field.Bad.TryGetValue(Spot(o), out float until) && until > Time.time) { skipped++; continue; }
                Crop chosen = null;
                Vector3 at = o;
                bool blocked = false;
                foreach (Crop c in Choices(o))
                {
                    Vector3 q = o;
                    Fit fit = Check(st, c, ref q, out string block);
                    if (fit == Fit.Ok) { chosen = c; at = q; break; }
                    if (fit == Fit.Blocked) { blocked = true; firstBlock = firstBlock ?? block; break; }
                }
                if (chosen == null) { field.Bad[Spot(o)] = Time.time + 60f; climate |= !blocked; if (blocked) blockedSpots++; else climateSpots++; continue; }
                if (!Brain.CanReach(me, at, 2f)) { field.Bad[Spot(o)] = Time.time + 300f; why = Miss.CantReach; if (++reachTries >= 3) return false; continue; }
                crop = chosen; spot = at;
                return true;
            }
            if (why != Miss.CantReach && climate) why = Miss.WontGrow;
            Note(me, $"found nowhere to sow around its home at {center:F0} ({radius:0} m): {field.Cells.Count} tilled squares, rows {step:0.00} m apart, {gridSpots} free grid spots ({options.Count} tried), "
                     + $"{blockedSpots} blocked (first: {firstBlock ?? "-"}), {climateSpots} too cold or the wrong biome for its seeds, {skipped} skipped, {reachTries} out of reach; "
                     + $"seeds: {string.Join(", ", seeds.Concat(sown).Distinct().Select(c => c.Name))}");
            return false;
        }

        private static void Note(Humanoid me, string text) => VillagePlebsPlugin.Instance?.Note($"{Companion.NameOf(me)} {text}");

        // The game's own checks for a healthy plant: right biome, tilled, open sky and its grow radius clear, and nothing near it crowded by it.
        private static bool Fits(BrainState st, Crop crop, ref Vector3 p) => Check(st, crop, ref p, out _) == Fit.Ok;

        // Climate: this crop won't grow in this biome or weather, though another might. Blocked: nothing can go on this spot.
        private static Fit Check(BrainState st, Crop crop, ref Vector3 p, out string block)
        {
            block = null;
            if (ZoneSystem.instance == null || !ZoneSystem.instance.GetGroundHeight(p, out float y)) { block = "no ground"; return Fit.Blocked; }
            p.y = y;
            Heightmap hm = Heightmap.FindHeightmap(p);
            if (hm == null || !hm.IsCultivated(p)) { block = "not tilled"; return Fit.Blocked; }
            if (!PrivateArea.CheckAccess(p, 0f, false)) { block = "someone's ward"; return Fit.Blocked; }
            if (Physics.Raycast(p + Vector3.up * 0.1f, Vector3.up, out RaycastHit roof, 100f, Roof)) { block = "under " + roof.collider.name; return Fit.Blocked; }
            Heightmap.Biome biome = hm.GetBiome(p);
            if ((biome & crop.Plant.m_biome) == 0) return Fit.Climate;
            if (crop.Piece.m_onlyInBiome != Heightmap.Biome.None && (biome & crop.Piece.m_onlyInBiome) == 0) return Fit.Climate;
            bool shielded = ShieldGenerator.IsInsideShield(p);
            if (!crop.Plant.m_tolerateHeat && biome == Heightmap.Biome.AshLands && !shielded) return Fit.Climate;
            if (!crop.Plant.m_tolerateCold && (biome == Heightmap.Biome.DeepNorth || biome == Heightmap.Biome.Mountain) && !shielded) return Fit.Climate;
            foreach (Collider col in Physics.OverlapSphere(p, crop.Plant.m_growRadius, Space))
            {
                Plant other = col.GetComponent<Plant>();
                if (other == null || other.GetStatus() == Plant.Status.Healthy) { block = $"{col.name} ({LayerMask.LayerToName(col.gameObject.layer)}) in the way"; return Fit.Blocked; }
            }
            foreach (Collider col in Physics.OverlapSphere(p, crop.Reach + 2f, Space))
            {
                Plant other = col.GetComponentInParent<Plant>();
                if (other != null && Flat(other.transform.position, p) < other.m_growRadius + crop.Reach) { block = "too close to " + Utils.GetPrefabName(other.gameObject); return Fit.Blocked; }
            }
            foreach (Humanoid c in Companion.All())
            {
                BrainState o = c.GetComponent<ZNetView>().IsOwner() ? Brain.Get(c) : null;
                if (o != null && o != st && o.Task != null && o.Task.Kind == Work.Kind.Plant && Flat(o.Task.Pos, p) < crop.Step * 0.9f) { block = "another companion is sowing there"; return Fit.Blocked; }
            }
            return Fit.Ok;
        }

        public static bool Place(BrainState st, Crop crop, Vector3 p, float age = 0f)
        {
            Humanoid me = st.Body;
            Inventory inv = me.GetInventory();
            ItemDrop.ItemData tool = Cultivator(me);
            if (tool == null || crop == null || !Afford(inv, crop.Piece) || !Fits(st, crop, ref p)) return false;
            Quaternion rot = Quaternion.Euler(0f, Random.Range(0, 16) * 22.5f, 0f);
            TerrainModifier.SetTriggerOnPlaced(true);
            GameObject go = Object.Instantiate(crop.Piece.gameObject, p, rot);
            TerrainModifier.SetTriggerOnPlaced(false);
            long owner = Companion.Zdo(me)?.GetLong(Keys.Master, 0L) ?? 0L;
            Piece placed = go.GetComponent<Piece>();
            if (placed != null && owner != 0L) { Creator(placed) = owner; Companion.Zdo(placed)?.Set(ZDOVars.s_creator, owner); }
            foreach (IPlaced done in go.GetComponents<IPlaced>()) done.OnPlaced();
            if (age > 0f && Companion.Zdo(go.transform) is ZDO pz) pz.Set(ZDOVars.s_plantTime, (ZNet.instance.GetTime() - System.TimeSpan.FromSeconds(age)).Ticks);
            crop.Piece.m_placeEffect.Create(p, rot, go.transform);
            foreach (Piece.Requirement r in crop.Piece.m_resources)
                if (r.m_resItem != null) inv.RemoveItem(r.m_resItem.m_itemData.m_shared.m_name, r.m_amount);
            if (tool.m_shared.m_useDurability) tool.m_durability = Mathf.Max(0f, tool.m_durability - tool.m_shared.m_useDurabilityDrain);
            Mending.Swing(me, tool);
            return true;
        }
    }
}
