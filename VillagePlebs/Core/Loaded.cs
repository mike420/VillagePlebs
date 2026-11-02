using System;
using System.Collections.Generic;
using UnityEngine;
using Object = UnityEngine.Object;

namespace VillagePlebs
{
    // Everything of a kind the game has loaded (chests, beds, stoves...), searched for at most once a second and shared by every
    // companion, instead of each of them searching the whole scene each time it looks. On a busy server that search is the dear part.
    internal static class Loaded
    {
        private class Found { public Object[] Items; public ZNetView[] Views; public float At; }

        private const float Fresh = 1f;
        private static readonly Dictionary<Type, Found> Cache = new Dictionary<Type, Found>();

        public static IEnumerable<T> All<T>() where T : Object
        {
            if (!Cache.TryGetValue(typeof(T), out Found f)) Cache[typeof(T)] = f = new Found();
            if (f.Items == null || Time.time - f.At >= Fresh || Time.time < f.At)
            {
                f.Items = Object.FindObjectsByType<T>(FindObjectsSortMode.None);
                f.Views = new ZNetView[f.Items.Length];
                for (int i = 0; i < f.Items.Length; i++) f.Views[i] = f.Items[i] is Component c ? c.GetComponentInParent<ZNetView>() : null;
                f.At = Time.time;
            }
            return Live<T>(f.Items, f.Views);
        }

        // Anything destroyed since the search is left out, and anything not in the world: the see-through piece you are placing
        // is a real fire (or chest, or bed) with no world object behind it, and asking it whether it burns threw every time.
        private static IEnumerable<T> Live<T>(Object[] items, ZNetView[] views) where T : Object
        {
            for (int i = 0; i < items.Length; i++)
                if (items[i] != null && views[i] != null && views[i].IsValid()) yield return (T)items[i];
        }

        public static void Forget() => Cache.Clear();
    }
}
