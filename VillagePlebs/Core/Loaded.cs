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
        private class Found { public Object[] Items; public float At; }

        private const float Fresh = 1f;
        private static readonly Dictionary<Type, Found> Cache = new Dictionary<Type, Found>();

        public static IEnumerable<T> All<T>() where T : Object
        {
            if (!Cache.TryGetValue(typeof(T), out Found f)) Cache[typeof(T)] = f = new Found();
            if (f.Items == null || Time.time - f.At >= Fresh || Time.time < f.At)
            {
                f.Items = Object.FindObjectsByType<T>(FindObjectsSortMode.None);
                f.At = Time.time;
            }
            return Live<T>(f.Items);
        }

        // Anything destroyed since the search is left out.
        private static IEnumerable<T> Live<T>(Object[] items) where T : Object
        {
            foreach (Object o in items) if (o != null) yield return (T)o;
        }

        public static void Forget() => Cache.Clear();
    }
}
