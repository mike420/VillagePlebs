using System;
using System.Linq;
using HarmonyLib;
using UnityEngine;

namespace VillagePlebs
{
    internal static class Mending
    {
        private static readonly int Pieces = LayerMask.GetMask("piece", "piece_nonsolid");

        public static ItemDrop.ItemData Hammer(Humanoid h) =>
            h.GetInventory().GetAllItems().FirstOrDefault(i => IsHammer(i) && (!i.m_shared.m_useDurability || i.m_durability > 0f));

        public static bool IsHammer(ItemDrop.ItemData i) => i.m_shared.m_buildPieces != null && i.m_shared.m_buildPieces.name.IndexOf("Hammer", StringComparison.OrdinalIgnoreCase) >= 0;

        public static WearNTear Damaged(Humanoid me, Vector3 at, float range, Func<Component, bool> allowed)
        {
            return Physics.OverlapSphere(at, range, Pieces).Select(c => c.GetComponentInParent<WearNTear>()).Where(w => w != null).Distinct()
                .Where(w => w.GetHealthPercentage() < 0.9f && Repairable(w))
                .OrderBy(w => Vector3.Distance(w.transform.position, me.transform.position)).Take(3).FirstOrDefault(w => allowed(w));
        }

        private static bool Repairable(WearNTear w)
        {
            Piece p = w.GetComponent<Piece>();
            if (p == null || !p.IsPlacedByPlayer()) return false;
            return p.m_craftingStation == null || CraftingStation.HaveBuildStationInRange(p.m_craftingStation.m_name, w.transform.position) != null;
        }

        public static bool Fix(BrainState st, WearNTear w)
        {
            Humanoid me = st.Body;
            ItemDrop.ItemData hammer = Hammer(me);
            if (w == null || hammer == null || !Repairable(w)) return false;
            if (!w.Repair()) return false;
            Piece p = w.GetComponent<Piece>();
            Swing(me, hammer);
            p?.m_placeEffect.Create(w.transform.position, w.transform.rotation);
            int n = Journal.Count(me, "mended");
            if (n % 10 == 1) st.Remember(Words.P("villagepleb_mending_repaired_base"));
            return true;
        }

        public static void Swing(Humanoid me, ItemDrop.ItemData hammer)
        {
            if (hammer?.m_shared.m_attack != null && !string.IsNullOrEmpty(hammer.m_shared.m_attack.m_attackAnimation)) GameFields.Anim(me)?.SetTrigger(hammer.m_shared.m_attack.m_attackAnimation);
        }

        public static int FixAll(BrainState st, Vector3 center, float radius)
        {
            int n = 0;
            for (int i = 0; i < 200; i++)
            {
                WearNTear w = Damaged(st.Body, center, radius, c => true);
                if (w == null || !Fix(st, w)) break;
                n++;
            }
            return n;
        }

        public static void MakeHammer(BrainState st)
        {
            GameObject prefab = ObjectDB.instance?.GetItemPrefab("Hammer");
            Recipe r = prefab != null ? ObjectDB.instance.GetRecipe(prefab.GetComponent<ItemDrop>().m_itemData) : null;
            if (r != null) Upgrades.Craft(st, r, null, true);
        }
    }
}
