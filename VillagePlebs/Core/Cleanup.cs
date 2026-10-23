using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace VillagePlebs
{
    internal static class Cleanup
    {
        private static readonly List<IDictionary> Tracked = new List<IDictionary>();
        private static float _next;

        public static Dictionary<TKey, TValue> Track<TKey, TValue>(Dictionary<TKey, TValue> d) where TKey : Object
        {
            Tracked.Add(d);
            return d;
        }

        public static void Tick()
        {
            if (Time.time < _next) return;
            _next = Time.time + 30f;
            foreach (IDictionary d in Tracked)
                foreach (object gone in d.Keys.Cast<object>().Where(k => k as Object == null).ToList()) d.Remove(gone);
        }
    }
}
