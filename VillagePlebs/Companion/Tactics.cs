using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using UnityEngine;

namespace VillagePlebs
{
    internal static class Tactics
    {
        public static Character YourTarget;
        public static float YourTargetAt;

        public static Character Yours() => YourTarget != null && !YourTarget.IsDead() && Time.time - YourTargetAt < 6f ? YourTarget : null;

        public static Vector3? Pincer(Humanoid me, Player master, Character target, float reach)
        {
            if (master == null || target != Yours() || Brain.TargetOf(target) != master) return null;
            Vector3 fromYou = target.transform.position - master.transform.position; fromYou.y = 0f;
            Vector3 fromMe = me.transform.position - target.transform.position; fromMe.y = 0f;
            if (fromYou.sqrMagnitude < 0.01f || fromYou.magnitude > 6f) return null;
            if (Vector3.Angle(fromYou, fromMe) < 80f) return null;
            return target.transform.position + fromYou.normalized * (target.GetRadius() + reach * 0.7f);
        }

        public static bool Unaware(Character c) => c.GetBaseAI() is BaseAI ai && !ai.IsAlerted() && Brain.TargetOf(c) == null && !c.IsBoss();

        public static Vector3? Behind(Humanoid me, Character target, float reach)
        {
            Vector3 fromIt = me.transform.position - target.transform.position; fromIt.y = 0f;
            if (Vector3.Angle(target.transform.forward, fromIt) > 120f) return null;
            return target.transform.position - target.transform.forward * (target.GetRadius() + reach * 0.6f);
        }

        private static readonly AccessTools.FieldRef<Humanoid, Attack> CurrentAttack = AccessTools.FieldRefAccess<Humanoid, Attack>("m_currentAttack");
        private static readonly AccessTools.FieldRef<Attack, int> NextChain = AccessTools.FieldRefAccess<Attack, int>("m_nextAttackChainLevel");

        public static bool MidCombo(Humanoid me)
        {
            Attack last = CurrentAttack(me);
            return last != null && NextChain(last) > 0 && me.GetTimeSinceLastAttack() < 0.18f;
        }

        public static bool MidComboStarted(Humanoid me) { Attack a = CurrentAttack(me); return a != null && NextChain(a) > 0; }

        public static bool Special(BrainState st, ItemDrop.ItemData weapon, Character target, float dist)
        {
            if (weapon == null || !weapon.HaveSecondaryAttack() || MidCombo(st.Body)) return false;
            Attack special = weapon.m_shared.m_secondaryAttack;
            if (special == null || special.m_attackType == Attack.AttackType.Projectile || special.m_attackProjectile != null) return false;
            if (Stamina.Get(st.Body) < special.m_attackStamina + 15f || dist > Mathf.Max(1.2f, special.m_attackRange * 0.9f)) return false;
            bool wide = special.m_attackType == Attack.AttackType.Area || special.m_attackAngle >= 180f;
            int near = st.Enemies.Count(e => e != null && !e.IsDead() && Vector3.Distance(e.transform.position, st.Body.transform.position) < 3.5f);
            if (wide && near >= 3) return true;
            return !target.IsStaggering() && target.GetMaxHealth() >= 100f && st.Blows % 3 == 2;
        }

        public static bool Feared(Humanoid me, Character c)
        {
            var tally = Journal.Tally(me);
            return Journal.Tallied(tally, "killedby:", c.m_name) > 0 || Journal.Tallied(tally, "hurtby:", c.m_name) > me.GetMaxHealth() * 2f;
        }

        public static bool Heavy(Humanoid me, Character c)
        {
            if (c.IsBoss() || Feared(me, c)) return true;
            return HitOf(c) >= me.GetMaxHealth() * 0.25f;
        }

        private static float Harm(HitData.DamageTypes d) => d.m_damage + d.m_blunt + d.m_slash + d.m_pierce + d.m_fire + d.m_frost + d.m_lightning + d.m_poison + d.m_spirit;

        public static float HitOf(Character c)
        {
            if (!(c is Humanoid h)) return 0f;
            float best = h.GetCurrentWeapon() is ItemDrop.ItemData w ? Harm(w.GetDamage()) : 0f;
            foreach (GameObject go in (h.m_defaultItems ?? new GameObject[0]).Concat(h.m_randomWeapon ?? new GameObject[0]))
                if (go != null && go.GetComponent<ItemDrop>() is ItemDrop drop) best = Mathf.Max(best, Harm(drop.m_itemData.GetDamage()));
            return best * (1f + (Mathf.Max(1, c.GetLevel()) - 1) * 0.5f);
        }

        public static bool TooStrong(Humanoid me, Character c)
        {
            if (c == null || c.IsDead()) return false;
            float itsHit = HitOf(c);
            if (itsHit <= 0f) return false;
            ItemDrop.ItemData weapon = me.GetCurrentWeapon() ?? Companion.BestMelee(me);
            float myHit = Mathf.Max(3f, weapon != null ? Harm(weapon.GetDamage()) : 3f);
            float blowsToFell = c.GetHealth() / myHit, blowsToFall = me.GetHealth() / itsHit;
            return blowsToFell > blowsToFall * 3f;
        }

        public static bool Careful(BrainState st) => Food.Meals(st.Body).Count == 0 || Time.time - st.WokeAt < 90f;

        private static bool Drink(Humanoid h, Func<SE_Stats, bool> fits, string what, BrainState st)
        {
            SEMan seman = h.GetSEMan();
            foreach (ItemDrop.ItemData item in h.GetInventory().GetAllItems().ToList())
            {
                if (item.m_shared.m_itemType != ItemDrop.ItemData.ItemType.Consumable || item.m_shared.m_food > 0f || !(item.m_shared.m_consumeStatusEffect is SE_Stats se) || !fits(se)) continue;
                if (seman.HaveStatusEffect(se.NameHash()) || (!string.IsNullOrEmpty(se.m_category) && seman.HaveStatusEffectCategory(se.m_category))) continue;
                seman.AddStatusEffect(se, true);
                h.m_consumeItemEffects.Create(h.transform.position, Quaternion.identity);
                h.GetInventory().RemoveOneItem(item);
                st.Remember(Words.P(what));
                Activity.Log(h, $"drank {Localization.instance.Localize(item.m_shared.m_name)}");
                return true;
            }
            return false;
        }

        private static bool Resists(SE_Stats se, HitData.DamageType type) =>
            se.m_mods != null && se.m_mods.Any(m => m.m_type == type && (m.m_modifier == HitData.DamageModifier.Resistant || m.m_modifier == HitData.DamageModifier.VeryResistant || m.m_modifier == HitData.DamageModifier.Immune));

        public static void Meads(BrainState st)
        {
            if (Time.time < st.NextMeadLook) return;
            st.NextMeadLook = Time.time + 1f;
            Humanoid me = st.Body;
            SEMan seman = me.GetSEMan();
            if (Stamina.Get(me) < Stamina.Max(me) * 0.2f && Drink(me, se => se.m_staminaUpFront > 0f || se.m_staminaOverTime > 0f, "villagepleb_tactics_drank_stamina", st)) return;
            if (Eitr.Max(me) > 0f && Companion.IsStaff(me.GetCurrentWeapon()) && Eitr.Get(me) < Eitr.Max(me) * 0.2f && Drink(me, se => se.m_eitrUpFront > 0f || se.m_eitrOverTime > 0f, "villagepleb_tactics_drank_eitr", st)) return;
            string boss = st.Enemies.FirstOrDefault(e => e != null && e.IsBoss())?.m_name ?? "";
            bool poison = seman.GetStatusEffects().Any(s => s is SE_Poison) || boss.Contains("bonemass");
            bool fire = seman.GetStatusEffects().Any(s => s is SE_Burning) || boss.Contains("yagluth") || boss.Contains("fader");
            bool frost = seman.HaveStatusEffect(SEMan.s_statusEffectFrost) || seman.HaveStatusEffect(SEMan.s_statusEffectFreezing) || boss.Contains("dragon");
            if (poison && Drink(me, se => Resists(se, HitData.DamageType.Poison), "villagepleb_tactics_drank_poison", st)) return;
            if (fire && Drink(me, se => Resists(se, HitData.DamageType.Fire), "villagepleb_tactics_drank_fire", st)) return;
            if (frost) Drink(me, se => Resists(se, HitData.DamageType.Frost), "villagepleb_tactics_drank_frost", st);
        }

        private static Player _playerPrefab;
        private static Player PlayerPrefab => _playerPrefab != null ? _playerPrefab : _playerPrefab = ZNetScene.instance?.GetPrefab("Player")?.GetComponent<Player>();

        public static float DodgeCost => PlayerPrefab != null ? PlayerPrefab.m_dodgeStaminaUsage : 10f;

        public static void Roll(BrainState st, Vector3 dir)
        {
            Humanoid me = st.Body;
            Stamina.Use(me, DodgeCost);
            me.transform.rotation = Quaternion.LookRotation(dir);
            GameFields.Anim(me)?.SetTrigger("dodge");
            PlayerPrefab?.m_dodgeEffects.Create(me.transform.position, Quaternion.identity, me.transform);
            Companion.Zdo(me)?.Set(ZDOVars.s_dodgeinv, true);
            st.InvulnUntil = Time.time + 0.4f;
            st.RollCredited = false;
            Skill.Raise(me, Skills.SkillType.Dodge, 0.1f);
            st.FightDodges++;
            Journal.Count(me, "dodges");
        }

        public static void Tick(BrainState st)
        {
            if (st.InvulnUntil > 0f && Time.time >= st.InvulnUntil) { st.InvulnUntil = 0f; Companion.Zdo(st.Body)?.Set(ZDOVars.s_dodgeinv, false); }
        }
    }

    [HarmonyPatch(typeof(Character), nameof(Character.IsDodgeInvincible))]
    internal static class Character_IsDodgeInvincible_Companion
    {
        private static void Postfix(Character __instance, ref bool __result)
        {
            if (__result || !(__instance is Humanoid h) || !Companion.Is(h)) return;
            __result = h.GetComponent<ZNetView>().IsOwner() ? Brain.Get(h).InvulnUntil > Time.time : Companion.Zdo(h)?.GetBool(ZDOVars.s_dodgeinv, false) ?? false;
        }
    }

    [HarmonyPatch(typeof(Character), "RPC_Damage")]
    internal static class Character_RPC_Damage_YourTarget
    {
        private static void Postfix(Character __instance, HitData hit)
        {
            if (hit == null || __instance.IsPlayer() || Companion.Is(__instance)) return;
            if (hit.GetAttacker() is Player p && p == Player.m_localPlayer) { Tactics.YourTarget = __instance; Tactics.YourTargetAt = Time.time; }
        }
    }
}
