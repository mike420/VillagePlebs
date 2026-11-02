using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using UnityEngine;
using static VillagePlebs.Words;

namespace VillagePlebs
{
    internal enum IdlePlan { None, Fire, Chair, Shelter, Stroll }

    internal static class Idle
    {
        private static readonly Dictionary<Chair, Humanoid> Seats = new Dictionary<Chair, Humanoid>();
        private static readonly int Roof = LayerMask.GetMask("Default", "static_solid", "piece");
        private static readonly List<Piece> Near = new List<Piece>();

        private static readonly Dictionary<string, string> Answer = new Dictionary<string, string>
        {
            ["wave"] = "wave", ["cheer"] = "cheer", ["thumbsup"] = "thumbsup", ["laugh"] = "laugh", ["dance"] = "dance", ["bow"] = "bow",
            ["flex"] = "flex", ["toast"] = "toast", ["challenge"] = "cheer", ["roar"] = "roar", ["headbang"] = "headbang", ["vibe"] = "vibe",
            ["blowkiss"] = "laugh", ["loveyou"] = "thumbsup", ["shrug"] = "shrug", ["nonono"] = "shrug", ["kneel"] = "bow", ["cower"] = "laugh",
            ["cry"] = "comehere", ["despair"] = "thumbsup", ["comehere"] = "thumbsup",
        };

        public static void Forget()
        {
            foreach (Humanoid c in Companion.All().ToList())
                if (c.GetComponent<ZNetView>().IsOwner()) Stand(Brain.Get(c));
            Seats.Clear();
        }

        public static void Tick(BrainState st, Player master)
        {
            Humanoid me = st.Body;
            if (!st.IdleReady)
            {
                st.IdleReady = true;
                ZSyncAnimation z = GameFields.Anim(me);
                z?.SetBool("emote_sit", false); z?.SetBool("attach_chair", false); z?.SetBool("attach_bed", false);
            }
            if ((st.Sitting || !ReferenceEquals(st.SitChair, null)) && (st.SitChair == null && !st.Sitting || Time.time > st.SitKeep || st.Enemies.Count > 0 || me.GetMoveDir().sqrMagnitude > 0.01f)) Stand(st);
            if (me.GetVelocity().sqrMagnitude > 0.1f) st.StillSince = Time.time;
            Greet(st, master);
            Mirror(st, master);
        }

        public static bool Emote(BrainState st, string emote)
        {
            Humanoid me = st.Body;
            if (me == null || st.Asleep || st.Riding != null || st.InCombat || me.InAttack() || me.IsAttached() || !me.IsOnGround() || me.InWater()) return false;
            Stand(st);
            st.Ai.StopMoving();
            GameFields.Anim(me)?.SetTrigger("emote_" + emote);
            st.EmoteUntil = Time.time + 2.2f;
            Activity.Log(me, "emote: " + emote);
            return true;
        }

        public static void Queue(BrainState st, string emote, float delay = 0.6f, Character face = null)
        {
            if (st == null) return;
            st.PendingEmote = emote;
            st.PendingEmoteAt = Time.time + delay;
            st.EmoteFace = face;
        }

        public static bool Emoting(BrainState st, Action<Vector3> lookAt)
        {
            if (st.PendingEmote != null && Time.time >= st.PendingEmoteAt)
            {
                string e = st.PendingEmote;
                st.PendingEmote = null;
                if (Time.time - st.PendingEmoteAt < 4f) Emote(st, e);
            }
            if (Time.time >= st.EmoteUntil) return false;
            st.Ai.StopMoving();
            if (st.EmoteFace != null && !st.EmoteFace.IsDead()) lookAt(st.EmoteFace.GetHeadPoint());
            return true;
        }

        private static void Greet(BrainState st, Player master)
        {
            if (Time.time < st.NextGreetLook) return;
            st.NextGreetLook = Time.time + 1f;
            Humanoid me = st.Body;
            float d = master != null ? Vector3.Distance(master.transform.position, me.transform.position) : float.MaxValue;
            if (d > 60f) { if (st.AwaySince == 0f) st.AwaySince = Time.time; return; }
            if (d > 20f || st.AwaySince == 0f) return;
            bool longAway = Time.time - st.AwaySince > 120f;
            st.AwaySince = 0f;
            if (!longAway || st.InCombat || st.Asleep) return;
            Queue(st, "wave", 0.4f, master);
            Banter.Say(me, "welcome", 0f, P("villagepleb_idle_welcome_1", master.GetPlayerName()), P("villagepleb_idle_welcome_2"), P("villagepleb_idle_welcome_3"), P("villagepleb_idle_welcome_4"));
            st.Remember(P("villagepleb_idle_came_back"));
        }

        private static void Mirror(BrainState st, Player master)
        {
            ZDO z = master != null ? master.GetComponent<ZNetView>()?.GetZDO() : null;
            if (z == null) return;
            int id = z.GetInt(ZDOVars.s_emoteID, 0);
            if (st.YourEmoteId == int.MinValue || id == st.YourEmoteId) { st.YourEmoteId = id; return; }
            st.YourEmoteId = id;
            string e = z.GetString(ZDOVars.s_emote, "");
            if (e.Length == 0 || st.InCombat || st.Asleep || Vector3.Distance(master.transform.position, st.Body.transform.position) > 12f) return;
            if (e == "comehere") st.ComeUntil = Time.time + 20f;
            if (!Answer.TryGetValue(e, out string reply)) return;
            Queue(st, reply, 0.7f, master);
            if (e == "cry" || e == "despair") Banter.Say(st.Body, "cheerup", 1f, P("villagepleb_idle_cheerup_1"), P("villagepleb_idle_cheerup_2"), P("villagepleb_idle_cheerup_3"));
            st.Remember(P("villagepleb_idle_emote_answer", Low("$hud_emotes_" + e), Low("$hud_emotes_" + reply)));
        }

        public static bool Come(BrainState st, Player master, Action<Vector3, float, bool> moveTo, Action stop, Action<Vector3> lookAt)
        {
            if (master == null || Time.time >= st.ComeUntil) return false;
            float d = Vector3.Distance(master.transform.position, st.Body.transform.position);
            if (d > 40f) { st.ComeUntil = 0f; return false; }
            if (d > 2.2f) moveTo(master.transform.position, 1.6f, d > 8f);
            else { stop(); lookAt(master.GetHeadPoint()); }
            Brain.Status(st, P(d > 2.2f ? "villagepleb_idle_status_coming_over" : "villagepleb_idle_status_here"));
            return true;
        }

        public static void SitDown(BrainState st)
        {
            if (st.SitChair != null) { HoldChair(st); return; }
            st.SitKeep = Time.time + 0.3f;
            if (st.Sitting) return;
            Humanoid me = st.Body;
            if (!me.IsOnGround() || me.InWater() || me.InAttack() || me.IsAttached()) return;
            st.Ai.StopMoving();
            GameFields.Anim(me)?.SetBool("emote_sit", true);
            st.Sitting = true;
        }

        private static bool Free(Chair chair, Humanoid me) =>
            chair != null && !chair.m_inShip && chair.m_attachPoint != null && !chair.IsInUse() && (!Seats.TryGetValue(chair, out Humanoid who) || who == null || who == me);

        public static bool TakeChair(BrainState st, Chair chair)
        {
            Humanoid me = st.Body;
            if (st.SitChair == chair) { HoldChair(st); return true; }
            if (!Free(chair, me)) return false;
            Stand(st);
            Seats[chair] = me;
            st.SitChair = chair;
            st.Ai.StopMoving();
            GameFields.Anim(me)?.SetBool(chair.m_attachAnimation, true);
            Rigidbody body = me.GetComponent<Rigidbody>();
            if (body != null) { body.linearVelocity = Vector3.zero; body.isKinematic = true; }
            HoldChair(st);
            return true;
        }

        private static void HoldChair(BrainState st)
        {
            Chair chair = st.SitChair;
            if (chair == null) { Stand(st); return; }
            st.SitKeep = Time.time + 0.3f;
            Transform at = chair.m_attachPoint;
            st.Body.transform.SetPositionAndRotation(at.position, at.rotation);
            Rigidbody body = st.Body.GetComponent<Rigidbody>();
            if (body != null) { body.position = at.position; body.rotation = at.rotation; }
            GameFields.Anim(st.Body)?.SetBool(chair.m_attachAnimation, true);
        }

        public static void Stand(BrainState st)
        {
            Humanoid me = st?.Body;
            if (me == null) return;
            ZSyncAnimation z = GameFields.Anim(me);
            if (st.Sitting) { z?.SetBool("emote_sit", false); z?.SetTrigger("emote_stop"); st.Sitting = false; }
            if (!ReferenceEquals(st.SitChair, null) || Seats.Values.Contains(me))
            {
                Chair chair = st.SitChair;
                st.SitChair = null;
                foreach (Chair ch in Seats.Where(kv => kv.Value == me).Select(kv => kv.Key).ToList()) Seats.Remove(ch);
                if (chair != null)
                {
                    z?.SetBool(chair.m_attachAnimation, false);
                    me.transform.position = chair.m_attachPoint.position + Flat(chair.m_attachPoint.forward) * 0.8f + Vector3.up * 0.2f;
                }
                Rigidbody body = me.GetComponent<Rigidbody>();
                if (body != null && !st.Asleep && st.Riding == null) body.isKinematic = false;
            }
        }

        private static Vector3 Flat(Vector3 v) { v.y = 0f; return v.sqrMagnitude > 0.0001f ? v.normalized : Vector3.forward; }

        public static bool WithYou(BrainState st, Player master, float d, Action<Vector3, float, bool> moveTo, Action<Vector3> lookAt)
        {
            Humanoid me = st.Body;
            Vector3 you = master.transform.position;
            if (Vector3.Distance(you, st.YouWere) > 0.6f) { st.YouWere = you; st.YouStillSince = Time.time; }
            float still = Time.time - st.YouStillSince;
            bool youSit = master.IsSitting() || (master.IsAttached() && !master.InBed() && !master.IsAttachedToShip());
            bool sit = d < 8f && (youSit ? still > 1.5f : still > 25f && master.IsOnGround());
            if (sit)
            {
                if (st.SitChair != null) { HoldChair(st); Brain.Status(st, P("villagepleb_idle_status_sitting_with_you")); return true; }
                if (!st.Sitting)
                {
                    Chair chair = youSit ? FreeChairs(you, 4f, me).FirstOrDefault() : null;
                    if (chair != null)
                    {
                        float cd = Vector3.Distance(chair.m_attachPoint.position, me.transform.position);
                        if (cd > 1.3f) { moveTo(chair.m_attachPoint.position, 0.6f, false); Brain.Status(st, P("villagepleb_idle_status_taking_seat")); return true; }
                        if (TakeChair(st, chair)) { Brain.Status(st, P("villagepleb_idle_status_sitting_with_you")); return true; }
                    }
                    if (d > 3.5f) return false;
                }
                SitDown(st);
                lookAt(master.GetHeadPoint());
                Brain.Status(st, P(youSit ? "villagepleb_idle_status_sitting_with_you" : "villagepleb_idle_status_resting_while_you_look"));
                return true;
            }
            if (d < 6f && still > 2f) lookAt(master.GetHeadPoint());
            return false;
        }

        public static void Waiting(BrainState st)
        {
            if (Time.time - st.StillSince > 30f) SitDown(st);
        }

        public static bool AtHome(BrainState st, Player master, Vector3 center, float radius, Action<Vector3, float, bool> moveTo, Action stop, Action<Vector3> lookAt)
        {
            Humanoid me = st.Body;
            if (Time.time >= st.IdleUntil || (st.IdlePlan != IdlePlan.None && !StillGood(st))) Pick(st, center, radius);
            string With(string what) => st.WorkNote != null ? P("villagepleb_idle_with_note", what, st.WorkNote) : what;
            bool youNear = master != null && Vector3.Distance(master.transform.position, me.transform.position) < 8f;
            switch (st.IdlePlan)
            {
                case IdlePlan.Chair:
                {
                    Chair chair = st.IdleChair;
                    float d = Vector3.Distance(chair.m_attachPoint.position, me.transform.position);
                    string name = chair.m_name;
                    if (st.SitChair != chair && d > 1.3f) { moveTo(chair.m_attachPoint.position, 0.6f, d > 12f); Brain.Status(st, With(P("villagepleb_idle_status_going_to_sit", Low(name)))); return true; }
                    if (!TakeChair(st, chair)) { st.IdlePlan = IdlePlan.None; st.IdleUntil = 0f; return true; }
                    Brain.Status(st, With(P(st.IdleFire != null ? "villagepleb_idle_status_sitting_by_fire_on" : "villagepleb_idle_status_sitting_on", Low(name))));
                    return true;
                }
                case IdlePlan.Fire:
                {
                    float d = Vector3.Distance(st.IdleAt, me.transform.position);
                    if (!st.Sitting && d > 0.8f) { moveTo(st.IdleAt, 0.4f, d > 12f); Brain.Status(st, With(P("villagepleb_idle_status_going_to_fire"))); return true; }
                    stop();
                    lookAt(st.IdleFire.transform.position + Vector3.up * 0.5f);
                    SitDown(st);
                    bool cold = EnvMan.IsCold() || EnvMan.IsWet() || me.GetSEMan().HaveStatusEffect(SEMan.s_statusEffectWet) || Evening();
                    Brain.Status(st, With(P(cold ? "villagepleb_idle_status_warming" : "villagepleb_idle_status_resting_fire")));
                    return true;
                }
                case IdlePlan.Shelter:
                {
                    float d = Vector3.Distance(st.IdleAt, me.transform.position);
                    if (d > 0.8f) { moveTo(st.IdleAt, 0.4f, true); Brain.Status(st, With(P("villagepleb_idle_status_getting_out_of_rain"))); return true; }
                    stop();
                    if (youNear) lookAt(master.GetHeadPoint()); else LookAround(st, lookAt);
                    if (Time.time - st.StillSince > 15f) SitDown(st);
                    Brain.Status(st, With(P("villagepleb_idle_status_out_of_rain")));
                    return true;
                }
                case IdlePlan.Stroll:
                {
                    float d = Vector3.Distance(st.IdleAt, me.transform.position);
                    if (d > 1f && Time.time - st.IdleSince < 25f) { moveTo(st.IdleAt, 0.6f, false); Brain.Status(st, With(P("villagepleb_idle_status_stroll"))); return true; }
                    stop();
                    if (youNear) lookAt(master.GetHeadPoint()); else LookAround(st, lookAt);
                    Brain.Status(st, With(P("villagepleb_idle_status_looking_about")));
                    return true;
                }
            }
            return false;
        }

        private static bool Evening() => EnvMan.instance != null && (EnvMan.IsNight() || EnvMan.instance.GetDayFraction() > 0.72f);

        // Wedged on the way to its spot between jobs. Near a spot by the fire or out of the rain, it settles where it is (the spot
        // by the fire was a step past a bench or the hearth, and it walked at it every 6 s for as long as it rested); otherwise it
        // finds something else to do.
        internal static void Wedged(BrainState st)
        {
            if (st.IdlePlan == IdlePlan.None || Vector3.Distance(st.MoveGoal, st.IdleAt) > 1f) return;
            Vector3 here = st.Body.transform.position;
            if ((st.IdlePlan == IdlePlan.Fire || st.IdlePlan == IdlePlan.Shelter) && Utils.DistanceXZ(here, st.IdleAt) < 3f)
            {
                Activity.Log(st.Body, $"could not get right to its spot at {st.IdleAt:F0}: stays at {here:F0}");
                st.IdleAt = here;
                return;
            }
            Activity.Log(st.Body, $"could not get to its spot at {st.IdleAt:F0} ({st.IdlePlan.ToString().ToLowerInvariant()}): does something else");
            st.IdlePlan = IdlePlan.None;
            st.IdleUntil = 0f;
        }

        private static bool StillGood(BrainState st)
        {
            switch (st.IdlePlan)
            {
                case IdlePlan.Fire: return st.IdleFire != null && st.IdleFire.IsBurning();
                case IdlePlan.Chair: return st.IdleChair != null && (st.SitChair == st.IdleChair || Free(st.IdleChair, st.Body)) && (st.IdleFire == null || st.IdleFire.IsBurning());
                case IdlePlan.Shelter: return EnvMan.IsWet();
                case IdlePlan.Stroll: return !EnvMan.IsWet() || Roofed(st.Body.transform.position);
            }
            return false;
        }

        private static void Pick(BrainState st, Vector3 center, float radius)
        {
            Humanoid me = st.Body;
            st.IdlePlan = IdlePlan.None; st.IdleFire = null; st.IdleChair = null; st.IdleSince = Time.time;
            st.IdleUntil = Time.time + 5f;
            bool rain = EnvMan.IsWet();
            bool cold = EnvMan.IsCold() || Evening() || me.GetSEMan().HaveStatusEffect(SEMan.s_statusEffectWet) || me.GetSEMan().HaveStatusEffect(SEMan.s_statusEffectCold);
            Fireplace fire = Fires(center, radius).OrderBy(f => Vector3.Distance(f.transform.position, me.transform.position)).FirstOrDefault(f => !rain || Roofed(f.transform.position));
            float roll = UnityEngine.Random.value;
            if (fire != null && (cold || rain || roll < 0.45f))
            {
                Chair byFire = FreeChairs(fire.transform.position, 4f, me).FirstOrDefault(ch => !rain || Roofed(ch.m_attachPoint.position));
                if (byFire != null && UnityEngine.Random.value < 0.6f) { Plan(st, IdlePlan.Chair, byFire.m_attachPoint.position, 60f, 120f); st.IdleChair = byFire; st.IdleFire = fire; return; }
                Vector3? spot = BySide(fire, me);
                if (spot != null) { Plan(st, IdlePlan.Fire, spot.Value, 60f, 120f); st.IdleFire = fire; return; }
            }
            if (rain && !Roofed(me.transform.position))
            {
                Vector3? dry = Dry(center, radius, me);
                if (dry != null) { Plan(st, IdlePlan.Shelter, dry.Value, 40f, 60f); return; }
            }
            if (!rain && roll < 0.7f)
            {
                Chair chair = FreeChairs(center, radius, me).OrderBy(ch => UnityEngine.Random.value).FirstOrDefault();
                if (chair != null) { Plan(st, IdlePlan.Chair, chair.m_attachPoint.position, 45f, 90f); st.IdleChair = chair; return; }
            }
            if (rain && Roofed(me.transform.position)) { Plan(st, IdlePlan.Shelter, me.transform.position, 40f, 60f); return; }
            for (int i = 0; i < 8; i++)
            {
                Vector2 r = UnityEngine.Random.insideUnitCircle * radius * 0.6f;
                Vector3 p = center + new Vector3(r.x, 0f, r.y);
                if (!Ground(ref p, center.y, 4f) || !Steer.Safe(p, me)) continue;
                Plan(st, IdlePlan.Stroll, p, 25f, 45f);
                return;
            }
        }

        private static void Plan(BrainState st, IdlePlan plan, Vector3 at, float min, float max)
        {
            st.IdlePlan = plan;
            st.IdleAt = at;
            st.IdleUntil = Time.time + UnityEngine.Random.Range(min, max);
            Activity.Log(st.Body, $"between jobs: {plan.ToString().ToLowerInvariant()}");
        }

        private static void LookAround(BrainState st, Action<Vector3> lookAt)
        {
            if (Time.time >= st.NextLookAround)
            {
                st.NextLookAround = Time.time + UnityEngine.Random.Range(3f, 7f);
                Vector2 r = UnityEngine.Random.insideUnitCircle.normalized * 10f;
                st.LookAroundAt = st.Body.transform.position + new Vector3(r.x, 1.5f, r.y);
            }
            lookAt(st.LookAroundAt);
        }

        private static bool Roofed(Vector3 feet) => Physics.Raycast(feet + Vector3.up * 2.1f, Vector3.up, 20f, Roof);

        private static IEnumerable<Fireplace> Fires(Vector3 center, float radius)
        {
            Near.Clear();
            Piece.GetAllPiecesInRadius(center, radius, Near);
            foreach (Piece p in Near)
            {
                Fireplace f = p != null ? p.GetComponent<Fireplace>() : null;
                if (f == null || !f.IsBurning()) continue;
                if (!f.GetComponentsInChildren<EffectArea>().Any(a => (a.m_type & EffectArea.Type.Heat) != 0)) continue;
                yield return f;
            }
        }

        private static List<Chair> FreeChairs(Vector3 center, float radius, Humanoid me)
        {
            Near.Clear();
            Piece.GetAllPiecesInRadius(center, radius, Near);
            return Near.Where(p => p != null).SelectMany(p => p.GetComponentsInChildren<Chair>()).Where(ch => Free(ch, me))
                       .OrderBy(ch => Vector3.Distance(ch.m_attachPoint.position, center)).ToList();
        }

        private static Vector3? BySide(Fireplace fire, Humanoid me)
        {
            Vector3 c = fire.transform.position;
            Vector3 toMe = Flat(me.transform.position - c);
            foreach (float dist in new[] { 2.2f, 2.8f, 3.4f })
                for (int i = 0; i < 8; i++)
                {
                    Vector3 p = c + Quaternion.Euler(0f, (i % 2 == 0 ? 1 : -1) * (i + 1) / 2 * 45f, 0f) * toMe * dist;
                    if (!Ground(ref p, c.y, 1f) || !Steer.Safe(p, me)) continue;
                    if (Physics.CheckCapsule(p + Vector3.up * 0.6f, p + Vector3.up * 1.6f, 0.35f, Roof)) continue;
                    return p;
                }
            return null;
        }

        private static Vector3? Dry(Vector3 center, float radius, Humanoid me)
        {
            for (float ring = 0f; ring <= radius; ring += 3f)
                for (int i = 0; i < (ring == 0f ? 1 : 12); i++)
                {
                    Vector3 p = center + Quaternion.Euler(0f, i * 30f, 0f) * Vector3.forward * ring;
                    if (!Ground(ref p, center.y, 6f) || !Roofed(p) || !Steer.Safe(p, me)) continue;
                    if (Physics.CheckCapsule(p + Vector3.up * 0.6f, p + Vector3.up * 1.6f, 0.35f, Roof)) continue;
                    return p;
                }
            return null;
        }

        private static bool Ground(ref Vector3 p, float near, float within)
        {
            p.y = near;
            if (ZoneSystem.instance == null || !ZoneSystem.instance.GetSolidHeight(p, out float h, 2)) return false;
            if (Mathf.Abs(h - near) > within) return false;
            p.y = h;
            return true;
        }
    }

    [HarmonyPatch(typeof(Character), "UpdateMotion")]
    internal static class Character_UpdateMotion_Held
    {
        private static readonly AccessTools.FieldRef<Character, Rigidbody> Body = AccessTools.FieldRefAccess<Character, Rigidbody>("m_body");

        private static bool Prefix(Character __instance)
        {
            Rigidbody body = Body(__instance);
            return body == null || !body.isKinematic || !Companion.Is(__instance);
        }
    }
}
