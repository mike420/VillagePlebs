using System;
using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;

namespace VillagePlebs
{
    internal static class Defense
    {
        private static readonly AccessTools.FieldRef<Humanoid, Attack> CurrentAttack = AccessTools.FieldRefAccess<Humanoid, Attack>("m_currentAttack");
        internal static readonly AccessTools.FieldRef<Attack, Humanoid> AttackOwner = AccessTools.FieldRefAccess<Attack, Humanoid>("m_character");
        private static readonly AccessTools.FieldRef<Humanoid, float> BlockTimer = AccessTools.FieldRefAccess<Humanoid, float>("m_blockTimer");

        private static readonly Dictionary<int, KeyValuePair<string, float>> Started = new Dictionary<int, KeyValuePair<string, float>>();
        private static readonly Dictionary<string, float> WindUp = new Dictionary<string, float>();
        private const float ParryLead = 0.15f;

        public static void Forget() { Started.Clear(); WindUp.Clear(); }

        private static string Kind(Humanoid attacker) => Utils.GetPrefabName(attacker.gameObject) + "|" + (CurrentAttack(attacker)?.m_attackAnimation ?? "?");

        internal static void OnStart(Humanoid attacker)
        {
            if (attacker == null || attacker.IsPlayer() || Companion.Is(attacker)) return;
            Started[attacker.GetInstanceID()] = new KeyValuePair<string, float>(Kind(attacker), Time.time);
            if (Started.Count > 300) Started.Clear();
        }

        internal static void OnTrigger(Attack attack)
        {
            Humanoid owner = AttackOwner(attack);
            if (owner == null || owner.IsPlayer() || !Started.TryGetValue(owner.GetInstanceID(), out var start)) return;
            Started.Remove(owner.GetInstanceID());
            float delay = Time.time - start.Value;
            if (delay < 0.05f || delay > 3f) return;
            WindUp[start.Key] = WindUp.TryGetValue(start.Key, out float had) ? Mathf.Lerp(had, delay, 0.3f) : delay;
        }

        public static float Reach(Character attacker)
        {
            Attack attack = attacker is Humanoid h ? CurrentAttack(h) : null;
            if (attack != null && (attack.m_attackType == Attack.AttackType.Projectile || attack.m_attackProjectile != null)) return 30f;
            return Mathf.Max(4.5f, (attack?.m_attackRange ?? 3f) + 1.5f) + attacker.GetRadius();
        }

        public static bool RaiseNow(Character attacker)
        {
            if (!(attacker is Humanoid h) || !Started.TryGetValue(h.GetInstanceID(), out var start) || !WindUp.TryGetValue(start.Key, out float windUp)) return true;
            return Time.time - start.Value >= windUp - ParryLead;
        }

        public static bool Learned(Character attacker) => attacker is Humanoid h && Started.TryGetValue(h.GetInstanceID(), out var s) && WindUp.ContainsKey(s.Key);

        public static bool ShouldEvade(Humanoid me, Character attacker, out string why)
        {
            why = null;
            Attack attack = attacker is Humanoid h ? CurrentAttack(h) : null;
            ItemDrop.ItemData weapon = attacker is Humanoid hw ? hw.GetCurrentWeapon() : null;
            if (attack != null && (attack.m_attackType == Attack.AttackType.Area || attack.m_attackAngle > 150f)) { why = "a sweeping attack"; return true; }
            if (attack != null && (attack.m_attackType == Attack.AttackType.Projectile || attack.m_attackProjectile != null)) { why = "a shot"; return true; }
            if (weapon != null && !weapon.m_shared.m_blockable) { why = "an attack it cannot block"; return true; }
            if (!Companion.CanBlock(me)) { why = "nothing to block with"; return true; }
            if (Stamina.Get(me) < 20f && Stamina.Get(me) >= Tactics.DodgeCost) { why = "too tired to block"; return true; }
            ItemDrop.ItemData blocker = Companion.Shield(me) ?? me.GetCurrentWeapon();
            float block = blocker != null ? blocker.GetBlockPower(Skill.Get(me, Skills.SkillType.Blocking) / 100f) : 0f;
            float incoming = weapon != null ? weapon.GetDamage().GetTotalDamage() * (attack?.m_damageMultiplier ?? 1f) * Mathf.Max(1, attacker.GetLevel()) : 0f;
            if (block > 0f && incoming > block * 0.8f && !Learned(attacker)) { why = "a hit harder than its block"; return true; }
            return false;
        }

        public static bool StepAside(BrainState st, Character attacker)
        {
            Humanoid me = st.Body;
            if (Stamina.Get(me) < Tactics.DodgeCost) return false;
            Vector3 away = me.transform.position - attacker.transform.position;
            away.y = 0f;
            if (away.sqrMagnitude < 0.01f) away = -me.transform.forward;
            away.Normalize();
            Vector3 side = Vector3.Cross(Vector3.up, away);
            if (st.EvadeSide == 0f) st.EvadeSide = UnityEngine.Random.value < 0.5f ? 1f : -1f;
            foreach (Vector3 dir in new[] { side * st.EvadeSide, -side * st.EvadeSide, (side * st.EvadeSide + away).normalized, (-side * st.EvadeSide + away).normalized, away })
            {
                Vector3 to = me.transform.position + dir * 3f;
                if (!Safe(me, to)) continue;
                Tactics.Roll(st, dir);
                me.ApplyPushback(dir, 25f);
                me.SetMoveDir(dir);
                me.SetRun(Stamina.CanRun(me));
                st.EvadeUntil = Time.time + 0.6f;
                st.EvadeTo = to;
                st.EvadeSide = Vector3.Dot(dir, side) >= 0f ? 1f : -1f;
                return true;
            }
            return false;
        }

        private static readonly int Ground = LayerMask.GetMask("Default", "static_solid", "Default_small", "piece", "terrain");

        private static bool Safe(Humanoid me, Vector3 to)
        {
            if (!Physics.Raycast(to + Vector3.up * 1.5f, Vector3.down, out RaycastHit hit, 3.5f, Ground)) return false;
            if (Mathf.Abs(hit.point.y - me.transform.position.y) > 1f) return false;
            if (Physics.CheckCapsule(me.transform.position + Vector3.up * 0.8f, to + Vector3.up * 0.8f, 0.3f, LayerMask.GetMask("Default", "static_solid", "piece"))) return false;
            return Steer.Safe(hit.point, me);
        }

        internal static void OnBlocked(Humanoid me, Character attacker, bool blocked)
        {
            if (!blocked || !Companion.Is(me) || attacker == null) return;
            bool parry = BlockTimer(me) >= 0f && BlockTimer(me) < 0.25f;
            BrainState st = Brain.Get(me);
            if (parry) { st.Parries++; st.CounterUntil = Time.time + 1.2f; Journal.Parried(me, attacker); }
            else st.Blocks++;
            Activity.Log(me, (parry ? "parried " : "blocked ") + Localization.instance.Localize(attacker.m_name) + (Learned(attacker) ? " (timed)" : ""));
        }
    }

    [HarmonyPatch(typeof(Humanoid), nameof(Humanoid.StartAttack))]
    internal static class Humanoid_StartAttack_Learn
    {
        private static void Postfix(Humanoid __instance, bool __result) { if (__result) try { Defense.OnStart(__instance); } catch (Exception) { } }
    }

    [HarmonyPatch(typeof(Attack), nameof(Attack.OnAttackTrigger))]
    internal static class Attack_OnAttackTrigger_Learn
    {
        private static void Prefix(Attack __instance) { try { Defense.OnTrigger(__instance); } catch (Exception) { } }
    }

    [HarmonyPatch(typeof(Humanoid), "BlockAttack")]
    internal static class Humanoid_BlockAttack_Log
    {
        private static void Postfix(Humanoid __instance, Character attacker, bool __result) { try { Defense.OnBlocked(__instance, attacker, __result); } catch (Exception) { } }
    }
}
