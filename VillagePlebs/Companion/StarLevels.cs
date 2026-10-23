using System;
using System.Reflection;
using BepInEx;
using HarmonyLib;
using UnityEngine;

namespace VillagePlebs
{
    internal static class StarLevels
    {
        public const string PluginId = "MidnightsFX.StarLevelSystem";
        private const string Managed = "SLS_EXTMGD", Multiplied = "SLS_MULT", Modifiers = "SLS_MODV2", NoModifiers = "{\"None\": \"Minor\"}", Size = "SLS_SIZE";

        private static bool _hooked;
        private static MethodInfo _clear;

        public static void Hook()
        {
            if (_hooked) return;
            _hooked = true;
            try
            {
                if (!BepInEx.Bootstrap.Chainloader.PluginInfos.TryGetValue(PluginId, out PluginInfo info) || info.Instance == null) return;
                Assembly sls = info.Instance.GetType().Assembly;
                MethodInfo enqueue = AccessTools.Method(sls.GetType("StarLevelSystem.modules.CreatureSetup.CreatureSetupQueue"), "Enqueue");
                Type cache = sls.GetType("StarLevelSystem.Data.CompositeLazyCache");
                MethodInfo build = AccessTools.Method(cache, "GetAndSetLocalCache");
                _clear = AccessTools.Method(cache, "ClearCachedCreature", new[] { typeof(Character) });
                Harmony harmony = VillagePlebsPlugin.Instance?.Harmony;
                if (harmony == null || enqueue == null || build == null || _clear == null)
                {
                    VillagePlebsPlugin.Instance?.Warn("StarLevelSystem found, but not the parts to keep it off companions (a new version?): it may give them star levels; they still keep its spawn multiplier off them");
                    return;
                }
                harmony.Patch(enqueue, prefix: new HarmonyMethod(typeof(StarLevels), nameof(NotSetUp)));
                harmony.Patch(build, postfix: new HarmonyMethod(typeof(StarLevels), nameof(NotKept)));
                VillagePlebsPlugin.Instance?.Note("StarLevelSystem found: it leaves companions as they are (no copies, star levels or modifiers), as it does players");
            }
            catch (Exception e) { VillagePlebsPlugin.Instance?.Warn("Keeping StarLevelSystem off companions: " + e.Message); }
        }

        private static bool NotSetUp(Character __0, ref bool __result)
        {
            if (!(__0 is Humanoid h) || !Companion.Is(h)) return true;
            __result = false;
            return false;
        }

        private static void NotKept(Character __0)
        {
            if (!(__0 is Humanoid h) || !Companion.Is(h)) return;
            try { _clear?.Invoke(null, new object[] { __0 }); } catch { }
        }

        public static void Exempt(Humanoid c)
        {
            ZNetView view = c != null ? c.GetComponent<ZNetView>() : null;
            if (view == null || !view.IsValid() || !view.IsOwner()) return;
            ZDO z = view.GetZDO();
            if (z.GetBool(Managed, false) && z.GetString(Modifiers, "") == NoModifiers && c.GetLevel() <= 1) return;
            if (z.GetInt(ZDOVars.s_level, 0) <= 0) z.Set(ZDOVars.s_level, 1);
            z.Set(Managed, true);
            z.Set(Multiplied, true);
            z.Set(Modifiers, NoModifiers);
            if (c.GetLevel() > 1)
            {
                VillagePlebsPlugin.Instance?.Note($"{Companion.NameOf(c)} had {c.GetLevel() - 1} star(s) from StarLevelSystem: back to none");
                c.SetLevel(1);
            }
            if (z.GetVec3(Size, Vector3.zero) != Vector3.zero)
            {
                z.Set(Size, Vector3.zero);
                c.transform.localScale = Vector3.one;
            }
        }

        public static bool RemoveStray(Humanoid c)
        {
            ZNetView view = c != null ? c.GetComponent<ZNetView>() : null;
            if (view == null || !view.IsValid() || !view.IsOwner()) return false;
            ZDO z = view.GetZDO();
            if (z.GetLong(Keys.Master, 0L) != 0L || !string.IsNullOrEmpty(z.GetString(Keys.Name, ""))) return false;
            Container gear = c.GetComponent<Container>();
            if (gear != null && !Containers.Take(gear)) return false;
            int kept = gear != null && gear.GetInventory().NrOfItems() > 0 ? Companion.DropGear(c) : 0;
            VillagePlebsPlugin.Instance?.Note($"Removed a companion with no player and no name at {c.transform.position:F0} (a copy another mod made, such as StarLevelSystem's spawn multiplier)"
                                  + (kept > 0 ? $"; {kept} item stacks from its bag are in a tombstone there" : ""));
            ZNetScene.instance.Destroy(c.gameObject);
            return true;
        }
    }

    [HarmonyPatch(typeof(Character), "Awake")]
    internal static class Character_Awake_StarLevels
    {
        private static void Postfix(Character __instance) { if (__instance is Humanoid h && Companion.Is(h)) StarLevels.Exempt(h); }
    }
}
