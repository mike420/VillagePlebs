using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using UnityEngine;

namespace VillagePlebs
{
    // Fishing with a real rod: the fish it has bait for, from a dry spot on the shore within a cast, the line reeled in by holding block
    // (as a player does), the game's own float doing the rest and training its Fishing skill.
    internal static class Fishing
    {
        public const string OwnerKey = "vp_floatowner";
        private const float MaxCast = 18f, MinCast = 5f;

        private static readonly AccessTools.FieldRef<FishingFloat, Fish> Nibbler = AccessTools.FieldRefAccess<FishingFloat, Fish>("m_nibbler");
        private static readonly AccessTools.FieldRef<FishingFloat, float> NibbleTime = AccessTools.FieldRefAccess<FishingFloat, float>("m_nibbleTime");
        private static readonly AccessTools.FieldRef<FishingFloat, float> LineLength = AccessTools.FieldRefAccess<FishingFloat, float>("m_lineLength");
        private static readonly AccessTools.FieldRef<FishingFloat, ZNetView> View = AccessTools.FieldRefAccess<FishingFloat, ZNetView>("m_nview");
        private static readonly AccessTools.FieldRef<Humanoid, float> DrawTime = AccessTools.FieldRefAccess<Humanoid, float>("m_attackDrawTime");
        private static readonly AccessTools.FieldRef<Humanoid, ItemDrop.ItemData> AmmoItem = AccessTools.FieldRefAccess<Humanoid, ItemDrop.ItemData>("m_ammoItem");
        private static readonly AccessTools.FieldRef<Character, bool> Blocking = AccessTools.FieldRefAccess<Character, bool>("m_blocking");
        private static readonly int Sight = LayerMask.GetMask("Default", "static_solid", "piece", "terrain");

        private static HashSet<string> _baits;

        // A rod: something whose cast throws a float (main or secondary attack), or a weapon trained by Fishing (rods from other mods).
        public static bool IsRod(ItemDrop.ItemData i) =>
            i != null && (i.m_shared.m_name == "$item_fishingrod" || Casts(i.m_shared.m_attack) || Casts(i.m_shared.m_secondaryAttack)
                          || i.m_shared.m_skillType == Skills.SkillType.Fishing && i.m_shared.m_itemType != ItemDrop.ItemData.ItemType.Ammo && i.m_shared.m_itemType != ItemDrop.ItemData.ItemType.AmmoNonEquipable
                          || Companion.IsRanged(i) && (i.m_shared.m_ammoType ?? "").IndexOf("fish", System.StringComparison.OrdinalIgnoreCase) >= 0);

        private static bool Casts(Attack a) => a?.m_attackProjectile != null && a.m_attackProjectile.GetComponentInChildren<FishingFloat>(true) != null;

        private static HashSet<string> _rodAmmo;

        public static bool IsBait(ItemDrop.ItemData i)
        {
            if (_baits == null && ZNetScene.instance != null)
            {
                var baits = new HashSet<string>();
                foreach (GameObject prefab in ZNetScene.instance.m_prefabs)
                    if (prefab != null && prefab.GetComponent<Fish>() is Fish fish)
                        foreach (Fish.BaitSetting b in fish.m_baits) if (b?.m_bait != null) baits.Add(b.m_bait.m_itemData.m_shared.m_name);
                _baits = baits;
            }
            if (i == null || IsRod(i)) return false;
            if (_baits != null && _baits.Contains(i.m_shared.m_name)) return true;
            bool ammo = i.m_shared.m_itemType == ItemDrop.ItemData.ItemType.Ammo || i.m_shared.m_itemType == ItemDrop.ItemData.ItemType.AmmoNonEquipable;
            if (ammo && (i.m_shared.m_ammoType ?? "").IndexOf("fish", System.StringComparison.OrdinalIgnoreCase) >= 0) return true;
            // Or anything a rod takes as its ammo, for bait no fish here lists.
            if (_rodAmmo == null && ObjectDB.instance != null && ObjectDB.instance.m_items.Count > 0)
                _rodAmmo = new HashSet<string>(ObjectDB.instance.m_items.Select(go => go != null ? go.GetComponent<ItemDrop>()?.m_itemData : null)
                    .Where(d => d != null && IsRod(d) && !string.IsNullOrEmpty(d.m_shared.m_ammoType)).Select(d => d.m_shared.m_ammoType));
            return _rodAmmo != null && !IsRod(i) && !string.IsNullOrEmpty(i.m_shared.m_ammoType) && _rodAmmo.Contains(i.m_shared.m_ammoType)
                   && (i.m_shared.m_itemType == ItemDrop.ItemData.ItemType.Ammo || i.m_shared.m_itemType == ItemDrop.ItemData.ItemType.AmmoNonEquipable);
        }

        public static ItemDrop.ItemData Rod(Humanoid h) =>
            h.GetInventory().GetAllItems().FirstOrDefault(i => IsRod(i) && (!i.m_shared.m_useDurability || i.m_durability > 0f));

        public static bool HasBait(Humanoid h) => h.GetInventory().GetAllItems().Any(IsBait);

        // The bait in its bag this fish takes best.
        public static ItemDrop.ItemData BaitFor(Humanoid h, Fish fish)
        {
            Inventory inv = h.GetInventory();
            return fish.m_baits.Where(b => b?.m_bait != null).OrderByDescending(b => b.m_chance)
                .Select(b => inv.GetAllItems().FirstOrDefault(i => i.m_shared.m_name == b.m_bait.m_itemData.m_shared.m_name)).FirstOrDefault(i => i != null);
        }

        public static IEnumerable<Fish> Near(Vector3 center, float range) =>
            Fish.Instances.OfType<Fish>().Where(f => f != null && !f.IsHooked() && Flat(f.transform.position, center) < range);

        private static float Water => ZoneSystem.instance != null ? ZoneSystem.instance.m_waterLevel : 30f;

        // A dry spot within a cast of the fish with a clear line to it, nearest to where it stands.
        public static bool Spot(Humanoid me, Fish fish, out Vector3 stand)
        {
            stand = Vector3.zero;
            Vector3 at = fish.transform.position;
            var spots = new List<Vector3>();
            foreach (float d in new[] { 7f, 10f, 13f, 16f })
                for (int k = 0; k < 16; k++)
                {
                    float a = k * Mathf.PI / 8f;
                    var p = new Vector3(at.x + Mathf.Sin(a) * d, 0f, at.z + Mathf.Cos(a) * d);
                    if (ZoneSystem.instance == null || !ZoneSystem.instance.GetSolidHeight(p, out float h) || h < Water + 0.3f) continue;
                    p.y = h;
                    if (Physics.Linecast(p + Vector3.up * 1.6f, new Vector3(at.x, Water + 0.3f, at.z), Sight)) continue;
                    spots.Add(p);
                }
            foreach (Vector3 p in spots.OrderBy(p => Vector3.Distance(p, me.transform.position)).Take(4))
                if (Brain.CanReach(me, p, 1.5f)) { stand = p; return true; }
            return false;
        }

        // Its own float, found by the owner it was cast for.
        public static FishingFloat FloatOf(Character c)
        {
            if (c == null) return null;
            ZDOID id = c.GetZDOID();
            foreach (FishingFloat f in FishingFloat.GetAllInstances())
            {
                ZNetView v = f != null ? View(f) : null;
                if (v != null && v.IsValid() && v.GetZDO().GetZDOID(OwnerKey) == id) return f;
            }
            return null;
        }

        // Casts at the fish: the draw held just long enough to land the float near it, as a player judges it.
        public static bool Cast(Humanoid me, ItemDrop.ItemData rod, ItemDrop.ItemData bait, Vector3 target)
        {
            AmmoItem(me) = bait;
            float fraction = Mathf.Clamp01(Flat(me.transform.position, target) / 24f);
            float draw = rod.m_shared.m_attack.m_drawDurationMin;
            draw = Mathf.Lerp(draw, draw * 0.2f, me.GetSkillFactor(rod.m_shared.m_skillType));
            DrawTime(me) = draw > 0f ? Mathf.Max(0.01f, fraction * draw) : 0f;
            bool cast = me.StartAttack(null, false);
            DrawTime(me) = 0f;
            return cast;
        }

        public static bool Nibbling(FishingFloat f) => Nibbler(f) != null && Time.time - NibbleTime(f) < 0.4f;

        // How far the line is stretched past its length: eased off when it gets near breaking.
        public static float Tension(Humanoid me, FishingFloat f)
        {
            Transform top = Utils.FindChild(me.transform, "_RodTop");
            return top == null ? 0f : Vector3.Distance(top.position, f.transform.position) - LineLength(f);
        }

        public static void Reel(Humanoid me, bool on) => Blocking(me) = on;

        public static int Caught(Humanoid me) => me.GetInventory().GetAllItems().Where(i => i.m_dropPrefab != null && i.m_dropPrefab.GetComponent<Fish>() != null).Sum(i => i.m_stack);

        private static float Flat(Vector3 a, Vector3 b) { a.y = 0f; b.y = 0f; return Vector3.Distance(a, b); }

        internal static Character Owner(FishingFloat f)
        {
            ZNetView v = View(f);
            if (v == null || !v.IsValid()) return null;
            ZDOID id = v.GetZDO().GetZDOID(OwnerKey);
            if (id.IsNone() || ZNetScene.instance == null) return null;
            GameObject go = ZNetScene.instance.FindInstance(id);
            return go != null ? go.GetComponent<Character>() : null;
        }
    }

    // The game ties a float to its owner by the player's own id: a companion's float is marked as the companion's instead,
    // so the line, the catch and the skill are its, and its unused bait comes back to its bag.
    [HarmonyPatch(typeof(FishingFloat), nameof(FishingFloat.Setup))]
    internal static class FishingFloat_Setup_Companion
    {
        private static readonly AccessTools.FieldRef<FishingFloat, ZNetView> View = AccessTools.FieldRefAccess<FishingFloat, ZNetView>("m_nview");

        // After the game's own setup, which first clears away the owner's last float (marking it before would clear this one).
        private static void Postfix(FishingFloat __instance, Character owner)
        {
            if (!Companion.Is(owner)) return;
            ZNetView v = View(__instance);
            if (v != null && v.IsValid()) v.GetZDO().Set(Fishing.OwnerKey, owner.GetZDOID());
            FishingFloat_FixedUpdate_Companion.Cast[__instance.GetInstanceID()] = Time.time;
        }
    }

    // The float reels itself in and hands the bait back if its owner is still attacking: a companion's cast animation runs on past
    // the throw, so for a few seconds after a companion casts, the float waits for the swing to end instead of giving up at once.
    [HarmonyPatch(typeof(FishingFloat), "FixedUpdate")]
    internal static class FishingFloat_FixedUpdate_Companion
    {
        internal static readonly System.Collections.Generic.Dictionary<int, float> Cast = new System.Collections.Generic.Dictionary<int, float>();
        private const float Grace = 3f;

        private static bool Prefix(FishingFloat __instance)
        {
            int id = __instance.GetInstanceID();
            if (!Cast.TryGetValue(id, out float at)) return true;
            if (Time.time - at > Grace) { Cast.Remove(id); return true; }
            Character owner = Fishing.Owner(__instance);
            return owner == null || !(owner.InAttack() || owner.IsDrawingBow());
        }
    }

    [HarmonyPatch(typeof(FishingFloat), "GetOwner")]
    internal static class FishingFloat_GetOwner_Companion
    {
        private static bool Prefix(FishingFloat __instance, ref Character __result)
        {
            Character owner = Fishing.Owner(__instance);
            if (owner == null) return true;
            __result = owner;
            return false;
        }
    }

    [HarmonyPatch(typeof(FishingFloat), "ReturnBait")]
    internal static class FishingFloat_ReturnBait_Companion
    {
        private static bool Prefix(FishingFloat __instance, ref bool ___m_baitConsumed)
        {
            if (!(Fishing.Owner(__instance) is Humanoid h) || !Companion.Is(h)) return true;
            if (!___m_baitConsumed && ZNetScene.instance?.GetPrefab(__instance.GetBait()) is GameObject bait) h.GetInventory().AddItem(bait, 1);
            ___m_baitConsumed = true;
            return false;
        }
    }
}
