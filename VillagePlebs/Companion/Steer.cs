using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using UnityEngine;

namespace VillagePlebs
{
    internal static class Steer
    {
        private class Hazard { public Collider Col; public Vector3 Center; public float Radius; }
        private class State { public float NextScan, NextJump, NextMoveCheck, Side, SideUntil, NextNote, PressedSince = -1f, BlockedSince = -1f; public Vector3 LastPos; public readonly List<Hazard> Near = new List<Hazard>(); }

        private static readonly Dictionary<Humanoid, State> States = Cleanup.Track(new Dictionary<Humanoid, State>());
        private static readonly AccessTools.FieldRef<Aoe, Character> Owner = AccessTools.FieldRefAccess<Aoe, Character>("m_owner");
        private static readonly int Solid = LayerMask.GetMask("Default", "static_solid", "Default_small", "piece", "terrain", "vehicle");
        // Straight back last: the way it came is often the only way down off a rock it climbed.
        private static readonly float[] Angles = { 30f, 60f, 90f, 120f, 150f, 180f };

        public static void Forget() => States.Clear();

        public static int HazardsNear(Humanoid h) => h != null && States.TryGetValue(h, out State s) ? s.Near.Count : 0;

        // Standing on top of a piece of furniture (a workbench or its extensions, a chest, bed, chair, table, stove, fire, smelter):
        // the nearest free ground below it to step down to, or null if it is not up on one.
        public static Vector3? Perched(Humanoid me)
        {
            if (me == null || !me.IsOnGround()) return null;
            Vector3 pos = me.transform.position;
            // From well above its feet: sunk a little into the top of it, a ray from just above them would start inside and miss it.
            if (!Physics.Raycast(pos + Vector3.up * 1.2f, Vector3.down, out RaycastHit under, 1.9f, Solid, QueryTriggerInteraction.Ignore)) return null;
            if (!Furniture(under.collider)) return null;
            for (float r = 1.2f; r <= 3.01f; r += 0.6f)
                for (int k = 0; k < 12; k++)
                {
                    Vector3 p = pos + Quaternion.Euler(0f, k * 30f, 0f) * Vector3.forward * r;
                    if (!Physics.Raycast(new Vector3(p.x, pos.y + 1.5f, p.z), Vector3.down, out RaycastHit hit, 5f, Solid, QueryTriggerInteraction.Ignore)) continue;
                    if (Furniture(hit.collider) || hit.point.y > pos.y - 0.3f || hit.point.y < pos.y - 3f) continue;
                    return hit.point + Vector3.up * 0.1f;
                }
            return null;
        }

        private static bool Furniture(Collider c)
        {
            Piece p = c != null ? c.GetComponentInParent<Piece>() : null;
            if (p == null) return false;
            return p.GetComponent<CraftingStation>() != null || p.GetComponent<StationExtension>() != null || p.GetComponent<Container>() != null
                || p.GetComponent<Bed>() != null || p.GetComponent<Chair>() != null || p.GetComponent<CookingStation>() != null || p.GetComponent<Fireplace>() != null
                || p.GetComponent<Smelter>() != null || p.m_category == Piece.PieceCategory.Furniture;
        }

        // The nearest spot within a few metres with ground under it and room to stand: for one wedged where it cannot move.
        public static Vector3? FreeSpot(Humanoid me)
        {
            Vector3 pos = me.transform.position;
            for (float r = 1f; r <= 4.01f; r += 0.5f)
                for (int k = 0; k < 12; k++)
                {
                    Vector3 p = pos + Quaternion.Euler(0f, k * 30f, 0f) * Vector3.forward * r;
                    if (!Physics.Raycast(new Vector3(p.x, pos.y + 2.5f, p.z), Vector3.down, out RaycastHit hit, 6f, Solid, QueryTriggerInteraction.Ignore)) continue;
                    if (Furniture(hit.collider) || Mathf.Abs(hit.point.y - pos.y) > 2.5f) continue;
                    Vector3 feet = hit.point + Vector3.up * 0.05f;
                    if (Physics.CheckCapsule(feet + Vector3.up * 0.45f, feet + Vector3.up * 1.6f, 0.38f, Solid, QueryTriggerInteraction.Ignore)) continue;
                    return feet;
                }
            return null;
        }

        private static State Of(Humanoid h)
        {
            if (States.TryGetValue(h, out State s)) return s;
            foreach (Humanoid gone in States.Keys.Where(k => k == null).ToList()) States.Remove(gone);
            return States[h] = new State { LastPos = h.transform.position };
        }

        public static void Tick(Humanoid me, BrainState st)
        {
            if (me == null || me.IsDead() || st.Riding != null || me.IsSwimming()) return;
            State s = Of(me);
            Vector3 pos = me.transform.position;
            if (Time.time >= s.NextScan) { s.NextScan = Time.time + 0.3f; Scan(me, s, pos); }
            Vector3 dir = me.GetMoveDir();
            dir.y = 0f;
            if (dir.sqrMagnitude < 0.01f) { s.PressedSince = -1f; s.LastPos = pos; s.BlockedSince = -1f; return; }
            if (Time.time >= s.NextMoveCheck)
            {
                s.NextMoveCheck = Time.time + 0.25f;
                bool still = Flat(pos - s.LastPos) < 0.1f;
                s.LastPos = pos;
                s.PressedSince = !still ? -1f : s.PressedSince < 0f ? Time.time : s.PressedSince;
            }
            dir.Normalize();

            Hazard inside = s.Near.FirstOrDefault(h => Hurts(h, pos, 0.3f));
            if (inside != null)
            {
                Vector3 away = pos - Closest(inside, pos + Vector3.up * 0.9f);
                away.y = 0f;
                Go(me, st, away.sqrMagnitude > 0.001f ? away.normalized : -dir);
                return;
            }

            if (Clear(s, pos, dir)) { s.BlockedSince = -1f; Jump(me, s, pos, dir); return; }

            float first = Time.time < s.SideUntil ? s.Side : 1f;
            foreach (float a in Angles)
                foreach (float sign in new[] { first, -first })
                {
                    Vector3 d = Quaternion.Euler(0f, a * sign, 0f) * dir;
                    if (!Clear(s, pos, d)) continue;
                    s.Side = sign;
                    s.SideUntil = Time.time + 1.5f;
                    s.BlockedSince = -1f;
                    Go(me, st, d);
                    return;
                }
            if (s.BlockedSince < 0f) s.BlockedSince = Time.time;
            // Nothing that hurts in the way, only drops every way round (up on a rock with steep sides, say): after a few seconds it
            // takes the shortest way down rather than stand up there for good. A fall hurts a little; standing there does nothing.
            if (Time.time - s.BlockedSince > 3f && ShortestDrop(s, pos, dir) is Vector3 down)
            {
                if (Time.time >= s.NextNote) { s.NextNote = Time.time + 10f; Activity.Log(me, $"only drops every way from {pos:F0}: takes the shortest way down"); }
                Go(me, st, down);
                return;
            }
            me.SetMoveDir(Vector3.zero);
            if (!st.InCombat) Brain.Status(st, Words.P("villagepleb_steer_status_safe_way"));
            if (Time.time >= s.NextNote) { s.NextNote = Time.time + 10f; Activity.Log(me, $"no safe way on from {pos:F0} heading {dir:F1}: {Blockers(s, pos, dir)}"); }
        }

        // Of the ways round with nothing that hurts along them, the one with the least drop at its end; null if every way has a hurt.
        private static Vector3? ShortestDrop(State s, Vector3 pos, Vector3 dir)
        {
            if (ZoneSystem.instance == null) return null;
            Vector3? best = null;
            float least = float.MaxValue;
            for (int k = 0; k < 12; k++)
            {
                Vector3 d = Quaternion.Euler(0f, k * 30f, 0f) * dir;
                bool hurts = false;
                for (float r = 0.5f; r <= 2.01f && !hurts; r += 0.5f)
                    foreach (Hazard h in s.Near) if (Hurts(h, pos + d * r, 0.5f)) { hurts = true; break; }
                if (hurts || !GroundAt(pos + d * 1.2f, pos.y, out float ground)) continue;
                float drop = pos.y - ground;
                if (drop < least) { least = drop; best = d; }
            }
            return best;
        }

        // What turns it back every way it could go: the hurting things near (by name and how far) and any drop straight ahead.
        private static string Blockers(State s, Vector3 pos, Vector3 dir)
        {
            var parts = new List<string>();
            foreach (Hazard h in s.Near.Take(4))
            {
                string name = h.Col != null ? Utils.GetPrefabName(h.Col.transform.root.gameObject) + "/" + h.Col.name : "hit area";
                parts.Add($"{name} {Vector3.Distance(Closest(h, pos + Vector3.up * 0.9f), pos + Vector3.up * 0.9f):0.0} m off");
            }
            if (Drop(pos, pos + dir * 1.2f)) parts.Add("a drop of over 3.5 m ahead");
            return parts.Count > 0 ? string.Join(", ", parts) : "nothing found (ground or water?)";
        }

        private static void Go(Humanoid me, BrainState st, Vector3 d)
        {
            me.SetMoveDir(d);
            if (!st.InCombat) me.SetLookDir(d, 0f);
        }

        private static bool Clear(State s, Vector3 pos, Vector3 dir)
        {
            for (float d = 0.5f; d <= 2.01f; d += 0.5f)
            {
                Vector3 p = pos + dir * d;
                foreach (Hazard h in s.Near) if (Hurts(h, p, 0.5f)) return false;
            }
            return !Drop(pos, pos + dir * 1.2f);
        }

        // Ground more than 0.2 m under water (a wet shore or a puddle doesn't count).
        public static bool Underwater(Vector3 at)
        {
            if (at.y > 3000f) return false;
            if (ZoneSystem.instance == null || !ZoneSystem.instance.GetSolidHeight(at, out float ground)) return false;
            return ground < ZoneSystem.instance.m_waterLevel - 0.2f;
        }

        private static bool Drop(Vector3 from, Vector3 to)
        {
            if (from.y > 3000f) return false;
            if (!GroundAt(to, from.y, out float ground)) return false;
            if (ZoneSystem.instance != null && ground < ZoneSystem.instance.m_waterLevel - 0.5f) return false;
            return from.y - ground > 3.5f;
        }

        // The ground at a spot, a bridge or floor counted: the game's own height (terrain and some pieces) missed the deck of a
        // lowered drawbridge, so the ditch under it read as a drop and it would not step on, stuck at the gate by the stakes.
        private static bool GroundAt(Vector3 at, float fromY, out float ground)
        {
            if (Physics.Raycast(new Vector3(at.x, fromY + 1f, at.z), Vector3.down, out RaycastHit hit, 12f, Solid, QueryTriggerInteraction.Ignore)) { ground = hit.point.y; return true; }
            ground = 0f;
            return ZoneSystem.instance != null && ZoneSystem.instance.GetSolidHeight(at, out ground);
        }

        private static bool Hurts(Hazard h, Vector3 feet, float margin)
        {
            for (float y = 0.3f; y <= 1.6f; y += 0.65f)
            {
                Vector3 body = feet + Vector3.up * y;
                if ((Closest(h, body) - body).sqrMagnitude < (margin + 0.4f) * (margin + 0.4f)) return true;
            }
            return false;
        }

        private static Vector3 Closest(Hazard h, Vector3 p)
        {
            if (h.Col != null) return h.Col.ClosestPoint(p);
            Vector3 off = p - h.Center;
            return off.magnitude <= h.Radius ? p : h.Center + off.normalized * h.Radius;
        }

        private static Collider[] _scanHits = new Collider[256];
        private static readonly HashSet<Aoe> _scanSeen = new HashSet<Aoe>();
        private static readonly Collider[] _safeHits = new Collider[64];

        // Every collider in a sphere, into a shared buffer grown as needed (in a base, a 7 m sphere holds hundreds).
        private static int Overlap(Vector3 at, float radius, ref Collider[] hits)
        {
            int n;
            while ((n = Physics.OverlapSphereNonAlloc(at, radius, hits, ~0, QueryTriggerInteraction.Collide)) == hits.Length) hits = new Collider[hits.Length * 2];
            return n;
        }

        private static void Scan(Humanoid me, State s, Vector3 pos)
        {
            s.Near.Clear();
            _scanSeen.Clear();
            HashSet<Aoe> seen = _scanSeen;
            int n = Overlap(pos + Vector3.up, 7f, ref _scanHits);
            for (int i = 0; i < n; i++)
            {
                Collider col = _scanHits[i];
                Aoe aoe = col.GetComponent<Aoe>() ?? col.GetComponentInParent<Aoe>();
                if (aoe == null || !seen.Add(aoe) || !Dangerous(aoe, me)) continue;
                Collider own = aoe.m_useCollider != null ? (Collider)aoe.m_useCollider : aoe.GetComponent<Collider>();
                s.Near.Add(own != null && own.enabled ? new Hazard { Col = own } : new Hazard { Center = aoe.transform.position, Radius = Mathf.Max(0.5f, aoe.m_radius) });
            }
        }

        private static bool Dangerous(Aoe aoe, Humanoid me)
        {
            if (!aoe.isActiveAndEnabled || !aoe.m_hitCharacters) return false;
            if (aoe.m_damage.GetTotalDamage() <= 0f && string.IsNullOrEmpty(aoe.m_statusEffect)) return false;
            Character owner = Owner(aoe);
            return owner == null ? aoe.GetComponentInParent<Character>() == null : owner != me && !owner.IsPlayer() && !Companion.Is(owner);
        }

        private static readonly Collider[] ClearHits = new Collider[32];

        public static bool Clear(Vector3 feet, Humanoid me, float radius = 0.45f)
        {
            int n = Physics.OverlapCapsuleNonAlloc(feet + Vector3.up * 0.3f, feet + Vector3.up * 1.5f, radius, ClearHits, ~0, QueryTriggerInteraction.Collide);
            for (int i = 0; i < n; i++)
            {
                Collider col = ClearHits[i];
                Aoe aoe = col.GetComponent<Aoe>() ?? col.GetComponentInParent<Aoe>();
                if (aoe != null && Dangerous(aoe, me)) return false;
            }
            return true;
        }

        public static bool Safe(Vector3 feet, Humanoid me)
        {
            int n = Physics.OverlapSphereNonAlloc(feet + Vector3.up * 0.9f, 1f, _safeHits, ~0, QueryTriggerInteraction.Collide);
            for (int i = 0; i < n; i++)
            {
                Collider col = _safeHits[i];
                Aoe aoe = col.GetComponent<Aoe>() ?? col.GetComponentInParent<Aoe>();
                if (aoe != null && Dangerous(aoe, me)) return false;
            }
            return true;
        }

        // Safe by the hazards its last scan found (within 7 m of it, 0.3 s old at most), with no physics query: as Safe, a 1 m
        // sphere at its middle clear of them. For spots near it only, as its next few metres.
        public static bool SafeNear(Humanoid me, Vector3 feet)
        {
            if (me == null || !States.TryGetValue(me, out State s)) return Safe(feet, me);
            Vector3 mid = feet + Vector3.up * 0.9f;
            foreach (Hazard h in s.Near)
                if ((Closest(h, mid) - mid).sqrMagnitude <= 1f) return false;
            return true;
        }

        private static void Jump(Humanoid me, State s, Vector3 pos, Vector3 dir)
        {
            if (s.PressedSince < 0f || Time.time - s.PressedSince < 0.25f || Time.time < s.NextJump || !me.IsOnGround() || me.InAttack()) return;
            if (!Physics.Raycast(pos + Vector3.up * 0.45f, dir, out RaycastHit knee, 1f, Solid)) return;
            if (knee.collider.GetComponentInParent<Character>() != null) return;
            if (Physics.Raycast(pos + Vector3.up * 1.7f, dir, 1.3f, Solid)) return;
            Vector3 over = knee.point + dir * 0.3f;
            if (!Physics.Raycast(new Vector3(over.x, pos.y + 1.7f, over.z), Vector3.down, out RaycastHit top, 2.5f, Solid)) return;
            float height = top.point.y - pos.y;
            if (height < 0.3f || height > 1.3f) return;
            Vector3 land = pos + dir * 1.6f;
            if (s.Near.Any(h => Hurts(h, land, 0.4f)) || Drop(pos, land)) return;
            s.NextJump = Time.time + 1.2f;
            s.PressedSince = -1f;
            me.Jump(false);
        }

        private static float Flat(Vector3 v) { v.y = 0f; return v.magnitude; }
    }
}
