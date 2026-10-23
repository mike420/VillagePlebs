using System.Collections.Generic;
using System.Linq;
using HarmonyLib;

namespace VillagePlebs
{
    internal static class Containers
    {
        private static readonly System.Reflection.MethodInfo Load = AccessTools.Method(typeof(Container), "Load");

        public static bool InUse(Container c)
        {
            if (c == null) return false;
            if (c.IsInUse()) return true;
            ZNetView view = c.GetComponent<ZNetView>();
            return view != null && view.IsValid() && !view.IsOwner() && view.GetZDO().GetInt(ZDOVars.s_inUse) == 1;
        }

        public static bool Take(Container c, bool orphan = false)
        {
            if (c == null || (orphan ? c.IsInUse() : InUse(c))) return false;
            ZNetView view = c.GetComponent<ZNetView>();
            if (view == null || !view.IsValid()) return false;
            if (view.IsOwner()) return true;
            view.ClaimOwnership();
            if (Companion.Is(c)) Container_Load_Companion.Fresh(c);
            Load?.Invoke(c, null);
            return true;
        }

        public static List<Container> TakeAll(IEnumerable<Container> chests) => chests.Where(c => Take(c)).ToList();
    }
}
