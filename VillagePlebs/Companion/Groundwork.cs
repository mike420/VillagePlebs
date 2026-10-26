using HarmonyLib;
using UnityEngine;

namespace VillagePlebs
{
    // Ground a player has worked: raised, lowered, levelled or smoothed with the hoe or pickaxe, or painted (paved, dirt, cultivated).
    internal static class Groundwork
    {
        private static readonly AccessTools.FieldRef<TerrainComp, bool[]> Height = AccessTools.FieldRefAccess<TerrainComp, bool[]>("m_modifiedHeight");
        private static readonly AccessTools.FieldRef<TerrainComp, bool[]> Paint = AccessTools.FieldRefAccess<TerrainComp, bool[]>("m_modifiedPaint");
        private static readonly AccessTools.FieldRef<TerrainComp, Heightmap> Map = AccessTools.FieldRefAccess<TerrainComp, Heightmap>("m_hmap");
        private static readonly AccessTools.FieldRef<TerrainComp, int> Pitch = AccessTools.FieldRefAccess<TerrainComp, int>("m_pitch");

        public static bool Worked(Vector3 at, float radius)
        {
            TerrainComp comp = TerrainComp.FindTerrainCompiler(at);
            Heightmap hm = comp != null ? Map(comp) : null;
            if (hm == null) return false;
            bool[] height = Height(comp), paint = Paint(comp);
            int pitch = Pitch(comp), reach = Mathf.CeilToInt(radius / Mathf.Max(0.01f, hm.m_scale));
            hm.WorldToVertex(at, out int hx, out int hy);
            if (Any(height, pitch, hx, hy, reach)) return true;
            hm.WorldToVertexMask(at, out int px, out int py);
            return Any(paint, pitch, px, py, reach);
        }

        private static bool Any(bool[] marks, int pitch, int cx, int cy, int reach)
        {
            if (marks == null) return false;
            for (int y = Mathf.Max(0, cy - reach); y <= Mathf.Min(pitch - 1, cy + reach); y++)
                for (int x = Mathf.Max(0, cx - reach); x <= Mathf.Min(pitch - 1, cx + reach); x++)
                {
                    int i = y * pitch + x;
                    if (i < marks.Length && marks[i]) return true;
                }
            return false;
        }

        // Rocks and ore it would mine, as opposed to wood.
        public static bool Mined(GameObject go)
        {
            if (go.GetComponent<MineRock>() != null || go.GetComponent<MineRock5>() != null) return true;
            Destructible des = go.GetComponent<Destructible>();
            return des != null && des.m_destructibleType != DestructibleType.Tree && des.m_damages.m_pickaxe != HitData.DamageModifier.Immune;
        }

        // A rock or ore deposit standing on worked ground, or wood sunk into it (raised over with the hoe), which it leaves alone.
        public static bool OnWorkedGround(GameObject go) => Worked(go.transform.position, 2f) && (Mined(go) || Buried(go));

        // Half or more of it below the ground: a swing at it only hits the earth.
        private static bool Buried(GameObject go)
        {
            if (ZoneSystem.instance == null) return false;
            Bounds b = default;
            bool any = false;
            foreach (Collider c in go.GetComponentsInChildren<Collider>())
            {
                if (c == null || c.isTrigger) continue;
                if (!any) { b = c.bounds; any = true; } else b.Encapsulate(c.bounds);
            }
            if (!any) return false;
            return ZoneSystem.instance.GetGroundHeight(b.center) > b.center.y;
        }
    }

    // A companion's pickaxe never digs the ground at all, so worked ground only ever means a player's work.
    [HarmonyPatch(typeof(Attack), nameof(Attack.SpawnOnHitTerrain))]
    internal static class Attack_SpawnOnHitTerrain_Groundwork
    {
        private static bool Prefix(Character character, ref GameObject __result)
        {
            if (!Companion.Is(character)) return true;
            __result = null;
            return false;
        }
    }
}
