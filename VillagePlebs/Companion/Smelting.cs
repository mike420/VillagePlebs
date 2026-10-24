using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace VillagePlebs
{
    // Tending the smelters near home: furnaces, blast furnaces, charcoal kilns, windmills, spinning wheels, eitr refineries,
    // anything the game runs as a smelter. Fuel and ore go in the way a player puts them in, one at a time, and what is made comes out.
    internal static class Smelting
    {
        public static IEnumerable<Smelter> Near(Vector3 center, float radius) =>
            Object.FindObjectsByType<Smelter>(FindObjectsSortMode.None)
                .Where(s => s != null && Companion.Zdo(s) != null && Flat(s.transform.position, center) < radius + 20f);

        private static float Fuel(Smelter s) => Companion.Zdo(s)?.GetFloat(ZDOVars.s_fuel, 0f) ?? 0f;
        private static int Queued(Smelter s) => Companion.Zdo(s)?.GetInt(ZDOVars.s_queued, 0) ?? 0;

        // Finished goods held inside (furnaces stack them until someone empties them).
        public static int Ready(Smelter s) => s.m_spawnStack ? Companion.Zdo(s)?.GetInt(ZDOVars.s_spawnAmount, 0) ?? 0 : 0;

        private static bool Takes(Smelter s, ItemDrop.ItemData i) => s.m_conversion.Any(c => c.m_from != null && c.m_from.m_itemData.m_shared.m_name == i.m_shared.m_name);
        private static bool Burns(Smelter s, ItemDrop.ItemData i) => s.m_maxFuel > 0 && s.m_fuelItem != null && s.m_fuelItem.m_itemData.m_shared.m_name == i.m_shared.m_name;

        // Whether this smelter has room for that item now, as ore or as fuel.
        public static bool Wants(Smelter s, ItemDrop.ItemData i) =>
            Takes(s, i) && Queued(s) < s.m_maxOre || Burns(s, i) && Fuel(s) < s.m_maxFuel - 1f;

        public static bool Feeds(ItemDrop.ItemData i, IEnumerable<Smelter> smelters) => smelters.Any(s => Takes(s, i) || Burns(s, i));

        public static bool Makes(Smelter s, string prefab) => s.m_conversion.Any(c => c.m_to != null && c.m_to.gameObject.name == prefab);

        public static Vector3 Out(Smelter s) => s.m_outputPoint != null ? s.m_outputPoint.position : s.transform.position;

        // Fuel first, then ore, one at a time while there is room. How many went in.
        public static int Feed(Humanoid me, Smelter s)
        {
            ZNetView view = s.GetComponent<ZNetView>();
            if (view == null || !view.IsValid()) return 0;
            Inventory inv = me.GetInventory();
            int n = 0;
            if (s.m_maxFuel > 0 && s.m_fuelItem != null)
            {
                string fuel = s.m_fuelItem.m_itemData.m_shared.m_name;
                for (int room = Mathf.FloorToInt(s.m_maxFuel - Fuel(s)); room > 0 && inv.CountItems(fuel) > 0; room--, n++)
                {
                    inv.RemoveItem(fuel, 1);
                    view.InvokeRPC("RPC_AddFuel");
                }
            }
            int oreRoom = s.m_maxOre - Queued(s);
            foreach (Smelter.ItemConversion c in s.m_conversion)
            {
                if (c.m_from == null) continue;
                string ore = c.m_from.m_itemData.m_shared.m_name;
                for (; oreRoom > 0 && inv.CountItems(ore) > 0; oreRoom--, n++)
                {
                    inv.RemoveItem(ore, 1);
                    view.InvokeRPC("RPC_AddOre", c.m_from.gameObject.name, false);
                }
            }
            return n;
        }

        public static void Empty(Smelter s) => s.GetComponent<ZNetView>()?.InvokeRPC("RPC_EmptyProcessed");

        private static float Flat(Vector3 a, Vector3 b) { a.y = 0f; b.y = 0f; return Vector3.Distance(a, b); }
    }
}
