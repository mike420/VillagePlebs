using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;

namespace VillagePlebs
{
    internal static class Prefab
    {
        public const string PrefabName = "villagepleb";
        public static readonly int Hash = PrefabName.GetStableHashCode();

        private static GameObject _holder, _prefab;
        private static readonly AccessTools.FieldRef<ZNetScene, Dictionary<int, GameObject>> Named =
            AccessTools.FieldRefAccess<ZNetScene, Dictionary<int, GameObject>>("m_namedPrefabs");

        public static GameObject Get() => _prefab;

        public static void Register(ZNetScene scene)
        {
            if (_prefab == null)
            {
                GameObject player = scene.GetPrefab("Player");
                if (player == null) { VillagePlebsPlugin.Instance?.Warn("The Player prefab was not found, so no companion can be made"); return; }
                _holder = new GameObject(VillagePlebsPlugin.PluginName + "Prefabs");
                _holder.SetActive(false);
                Object.DontDestroyOnLoad(_holder);
                _prefab = Make(player, scene);
            }
            Dictionary<int, GameObject> named = Named(scene);
            if (named.TryGetValue(Hash, out GameObject old) && old != null && old != _prefab && old.name == PrefabName)
            {
                scene.m_prefabs.Remove(old);
                if (old.transform.parent != null && old.transform.parent.name == VillagePlebsPlugin.PluginName + "Prefabs") Object.Destroy(old.transform.parent.gameObject);
            }
            if (!scene.m_prefabs.Contains(_prefab)) scene.m_prefabs.Add(_prefab);
            named[Hash] = _prefab;
        }

        public static void Unregister()
        {
            _prefab = null;
            _holder = null;
        }

        private static GameObject Make(GameObject source, ZNetScene scene)
        {
            GameObject go = Object.Instantiate(source, _holder.transform, false);
            go.name = PrefabName;
            Humanoid original = source.GetComponent<Humanoid>();

            foreach (System.Type t in new[] { typeof(PlayerController), typeof(Player), typeof(Talker), typeof(Skills) })
            {
                Component c = go.GetComponent(t);
                if (c != null) Object.DestroyImmediate(c);
            }

            Humanoid h = go.AddComponent<Humanoid>();
            if (original != null)
            {
                h.m_unarmedWeapon = original.m_unarmedWeapon;
                h.m_consumeItemEffects = original.m_consumeItemEffects;
                h.m_equipEffects = original.m_equipEffects;
                h.m_hitEffects = original.m_hitEffects;
                h.m_critHitEffects = original.m_critHitEffects;
                h.m_backstabHitEffects = original.m_backstabHitEffects;
                h.m_deathEffects = original.m_deathEffects;
            }
            h.m_name = "$villagepleb_companion";
            h.m_group = "villagepleb";
            h.m_faction = Character.Faction.Players;
            h.m_health = VillagePlebsPlugin.BaseHealth.Value;
            h.m_regenAllHPTime = 300f;
            h.m_walkSpeed = 1.6f; h.m_runSpeed = 7f; h.m_speed = 4f; h.m_crouchSpeed = 2f;
            h.m_turnSpeed = 300f; h.m_runTurnSpeed = 300f; h.m_acceleration = 1f;
            h.m_jumpForce = 8f; h.m_jumpForceForward = 2f;
            h.m_canSwim = true; h.m_swimDepth = 1.6f; h.m_swimSpeed = 2f; h.m_swimTurnSpeed = 100f; h.m_swimAcceleration = 0.05f;
            h.m_groundTilt = Character.GroundTiltType.None;
            h.m_eye = Utils.FindChild(go.transform, "EyePos");
            h.m_tolerateWater = true;
            h.m_staggerWhenBlocked = true;
            h.m_damageModifiers = new HitData.DamageModifiers
            {
                m_chop = HitData.DamageModifier.Immune, m_pickaxe = HitData.DamageModifier.Immune, m_spirit = HitData.DamageModifier.Immune,
            };

            CharacterAnimEvent anim = go.GetComponentInChildren<CharacterAnimEvent>(true);
            if (anim != null) AccessTools.Field(typeof(CharacterAnimEvent), "m_character")?.SetValue(anim, h);

            MonsterAI ai = go.AddComponent<MonsterAI>();
            ai.m_viewRange = 30f; ai.m_viewAngle = 150f; ai.m_hearRange = 30f; ai.m_mistVision = true;
            ai.m_alertedEffects = new EffectList(); ai.m_idleSound = new EffectList(); ai.m_idleSoundChance = 0f;
            ai.m_pathAgentType = Pathfinding.AgentType.Humanoid;
            ai.m_moveMinAngle = 90f; ai.m_smoothMovement = true; ai.m_jumpInterval = 0f;
            ai.m_randomMoveInterval = 10f; ai.m_randomMoveRange = 4f;
            ai.m_avoidFire = false; ai.m_afraidOfFire = false; ai.m_avoidWater = false; ai.m_aggravatable = false;
            ai.m_attackPlayerObjects = false;
            ai.m_consumeItems = new List<ItemDrop>();

            ZNetView view = go.GetComponent<ZNetView>();
            view.m_persistent = true;
            view.m_distant = false;
            view.m_type = ZDO.ObjectType.Default;
            ZSyncTransform sync = go.GetComponent<ZSyncTransform>();
            if (sync != null) { sync.m_syncPosition = true; sync.m_syncRotation = true; sync.m_syncBodyVelocity = false; sync.m_characterParentSync = false; }
            Rigidbody body = go.GetComponent<Rigidbody>();
            if (body != null) body.mass = 50f;

            Container gear = go.AddComponent<Container>();
            gear.m_name = "$villagepleb_companion";
            gear.m_width = Gear.Width; gear.m_height = Gear.Rows;
            gear.m_privacy = Container.PrivacySetting.Public;
            gear.m_checkGuardStone = false;
            gear.m_autoDestroyEmpty = false;
            gear.m_openEffects = new EffectList(); gear.m_closeEffects = new EffectList();
            return go;
        }
    }
}
