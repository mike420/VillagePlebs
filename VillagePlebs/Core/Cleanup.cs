using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace VillagePlebs
{
    /// <summary>
    /// What the mod keeps for each companion (its brain, meals, stamina, skills...) is kept by its body, and a body goes each time its area
    /// unloads or it is made again (a new body): the old ones were kept for good, a slow leak through a long session. A dictionary kept by
    /// bodies is put here as it is made (Track), and every half minute what it kept for bodies that are gone is let go.
    /// </summary>
    internal static class Cleanup
    {
        private static readonly List<IDictionary> Tracked = new List<IDictionary>();
        private static float _next;

        public static Dictionary<TKey, TValue> Track<TKey, TValue>(Dictionary<TKey, TValue> d) where TKey : Object
        {
            Tracked.Add(d);
            return d;
        }

        /// <summary>Often (each companion's brain asks): every 30 seconds, the gone bodies out of every tracked dictionary.</summary>
        public static void Tick()
        {
            if (Time.time < _next) return;
            _next = Time.time + 30f;
            foreach (IDictionary d in Tracked)
                foreach (object gone in d.Keys.Cast<object>().Where(k => k as Object == null).ToList()) d.Remove(gone);
        }
    }
}
