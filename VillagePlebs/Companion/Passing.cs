using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace VillagePlebs
{
    internal static class Passing
    {
        private static readonly HashSet<long> Done = new HashSet<long>();
        private static float _next;

        public static void Forget() => Done.Clear();

        public static void Tick()
        {
            if (Time.time < _next) return;
            _next = Time.time + 0.5f;
            List<Humanoid> companions = Companion.All().Where(c => c != null && !c.IsDead()).ToList();
            if (companions.Count == 0) return;
            var bodies = new List<Character>(companions);
            bodies.AddRange(Player.GetAllPlayers().Where(p => p != null));
            for (int i = 0; i < companions.Count; i++)
                for (int j = 0; j < bodies.Count; j++)
                {
                    Character a = companions[i], b = bodies[j];
                    if (a == b || (b is Humanoid bh && companions.IndexOf(bh) >= 0 && companions.IndexOf(bh) < i)) continue;
                    long key = Key(a.GetInstanceID(), b.GetInstanceID());
                    if (Done.Contains(key)) continue;
                    Ignore(a, b);
                    Done.Add(key);
                }
            if (Done.Count > 4000) Done.Clear();
        }

        private static long Key(int a, int b) => a < b ? ((long)a << 32) | (uint)b : ((long)b << 32) | (uint)a;

        private static void Ignore(Character a, Character b)
        {
            foreach (Collider x in a.GetComponentsInChildren<Collider>())
            {
                if (x == null || x.isTrigger) continue;
                foreach (Collider y in b.GetComponentsInChildren<Collider>())
                    if (y != null && !y.isTrigger) Physics.IgnoreCollision(x, y, true);
            }
        }
    }
}
