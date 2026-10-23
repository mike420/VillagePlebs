using UnityEngine;

namespace VillagePlebs
{
    internal static class Archery
    {
        public static Vector3? Aim(Humanoid me, ItemDrop.ItemData bow, Character target)
        {
            if (bow == null || target == null) return null;
            ItemDrop.ItemData ammo = me.GetAmmoItem();
            Attack attack = bow.m_shared.m_attack, extra = ammo?.m_shared.m_attack?.m_attackProjectile != null ? ammo.m_shared.m_attack : null;
            if (attack == null) return null;
            float speed = Mathf.Max(5f, attack.m_projectileVel + (extra?.m_projectileVel ?? 0f));
            GameObject flying = extra?.m_attackProjectile ?? attack.m_attackProjectile;
            Projectile arrow = flying != null ? flying.GetComponent<Projectile>() : null;
            float gravity = arrow != null ? Mathf.Max(0f, arrow.m_gravity) : 0f;

            Vector3 from = me.transform.position + Vector3.up * 1.5f;
            Vector3 at = target.GetCenterPoint();
            Vector3 moving = target.GetVelocity();
            if (target.IsOnGround()) moving.y = 0f;

            Vector3 aimed = at;
            for (int i = 0; i < 3; i++)
            {
                float t = Vector3.Distance(from, aimed) / speed;
                aimed = at + moving * t;
            }

            Vector3 flat = aimed - from;
            float height = flat.y;
            flat.y = 0f;
            float d = flat.magnitude;
            if (d < 0.5f) return (aimed - from).normalized;
            Vector3 dir = flat / d;
            if (gravity <= 0.01f) return (aimed - from).normalized;

            float v2 = speed * speed;
            float under = v2 * v2 - gravity * (gravity * d * d + 2f * height * v2);
            if (under < 0f) return null;
            float angle = Mathf.Atan((v2 - Mathf.Sqrt(under)) / (gravity * d));
            return (dir * Mathf.Cos(angle) + Vector3.up * Mathf.Sin(angle)).normalized;
        }
    }
}
