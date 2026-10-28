using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using HarmonyLib;
using UnityEngine;
using static VillagePlebs.Words;
using static VillagePlebs.Shared.Translations;

namespace VillagePlebs
{
    internal static class Sleep
    {
        public static bool Tick(BrainState st, Action<Vector3, float, bool> moveTo, Action stop)
        {
            Humanoid me = st.Body;
            if (!st.Asleep) OffBed(st);
            bool night = EnvMan.instance != null && EnvMan.IsNight();
            if (!night || Companion.OrderOf(me) != Order.Gather || !Companion.Zdo(me).GetBool(Keys.HasBed, false) || Food.Meals(me).Count == 0 && !me.GetInventory().GetAllItems().Any(Food.IsFood) && me.GetHealthPercentage() < 0.5f)
            {
                if (st.Asleep) Wake(st, P("villagepleb_chores_wake_morning"));
                return false;
            }
            Bed bed = st.SleepBed;
            if (bed == null && Time.time >= st.NextBedLook) { st.NextBedLook = Time.time + 5f; bed = st.SleepBed = Home.BedOf(me); }
            if (bed == null)
            {
                if (st.Asleep) Wake(st, P("villagepleb_chores_wake_bed_gone"));
                return false;
            }
            if (st.Asleep) { Hold(st, bed); return true; }
            Vector3 at = bed.GetSpawnPoint();
            Vector3 flat = at - me.transform.position;
            float dy = Mathf.Abs(flat.y);
            flat.y = 0f;
            if (st.BedSince <= 0f || Time.time - st.BedSince > 600f) st.BedSince = Time.time;
            bool reach = flat.magnitude <= 1.6f && dy <= 2.6f || flat.magnitude <= 3.5f && dy <= 3f && Time.time - st.BedSince > 20f;
            if (!reach)
            {
                float d = Vector3.Distance(at, me.transform.position);
                moveTo(at, 0.6f, d > 8f);
                Brain.Status(st, P("villagepleb_chores_status_going_to_bed"));
                return true;
            }
            stop();
            st.BedSince = 0f;
            st.BedFrom = me.transform.position;
            st.Asleep = true;
            GameFields.Anim(me)?.SetBool("attach_bed", true);
            Rigidbody body = me.GetComponent<Rigidbody>();
            if (body != null) { body.linearVelocity = Vector3.zero; body.isKinematic = true; }
            st.Remember(P("villagepleb_chores_went_to_bed"));
            Hold(st, bed);
            return true;
        }

        private static void Hold(BrainState st, Bed bed)
        {
            Humanoid me = st.Body;
            Transform spot = bed.m_spawnPoint != null ? bed.m_spawnPoint : bed.transform;
            me.transform.SetPositionAndRotation(spot.position, spot.rotation);
            Rigidbody body = me.GetComponent<Rigidbody>();
            if (body != null) { body.position = spot.position; body.rotation = spot.rotation; }
            GameFields.Anim(me)?.SetBool("attach_bed", true);
            Brain.Status(st, P("villagepleb_chores_status_sleeping"));
        }

        public static void Wake(BrainState st, string why)
        {
            Humanoid me = st.Body;
            if (!st.Asleep || me == null) return;
            st.Asleep = false;
            GameFields.Anim(me)?.SetBool("attach_bed", false);
            Rigidbody body = me.GetComponent<Rigidbody>();
            if (body != null) body.isKinematic = false;
            Vector3? floor = st.SleepBed != null ? Beside(st.SleepBed.transform, me) : null;
            if (floor != null) me.transform.position = floor.Value;
            else if (st.BedFrom != Vector3.zero && st.SleepBed != null && Vector3.Distance(st.BedFrom, st.SleepBed.GetSpawnPoint()) < 5f) me.transform.position = st.BedFrom;
            else if (st.SleepBed != null) me.transform.position = st.SleepBed.GetSpawnPoint() + st.SleepBed.transform.forward * 1f;
            st.BedFrom = Vector3.zero;
            st.Remember(P("villagepleb_chores_got_up", why));
        }

        private static float _nextOffBed;

        private static void OffBed(BrainState st)
        {
            if (Time.time < _nextOffBed) return;
            _nextOffBed = Time.time + 2f;
            Humanoid me = st.Body;
            if (!Physics.Raycast(me.transform.position + Vector3.up * 0.3f, Vector3.down, out RaycastHit under, 1.2f, ~0, QueryTriggerInteraction.Ignore)) return;
            Bed bed = under.collider.GetComponentInParent<Bed>();
            if (bed == null) return;
            Vector3? down = Beside(bed.transform, me);
            if (down == null) return;
            me.transform.position = down.Value;
            Rigidbody body = me.GetComponent<Rigidbody>();
            if (body != null) { body.position = down.Value; body.linearVelocity = Vector3.zero; }
            st.Remember(P("villagepleb_chores_hopped_off_bed"));
        }

        private static Vector3? Beside(Transform bed, Humanoid me)
        {
            Vector3 c = bed.position;
            Vector3? best = null;
            float bestScore = float.MaxValue;
            for (int i = 0; i < 16; i++)
            {
                float ang = i * 22.5f;
                foreach (float r in new[] { 1.3f, 1.8f, 2.4f })
                {
                    Vector3 p = c + Quaternion.Euler(0f, ang, 0f) * Vector3.forward * r;
                    if (!Physics.Raycast(p + Vector3.up * 2.0f, Vector3.down, out RaycastHit hit, 5f, ~0, QueryTriggerInteraction.Ignore)) continue;
                    if (hit.collider.GetComponentInParent<Bed>() != null || hit.collider.GetComponentInParent<Character>() != null || hit.collider.GetComponentInParent<Container>() != null) continue;
                    if (hit.point.y > c.y - 0.2f || hit.normal.y < 0.7f) continue;
                    Vector3 stand = hit.point + Vector3.up * 0.05f;
                    if (Physics.CheckCapsule(stand + Vector3.up * 0.5f, stand + Vector3.up * 1.6f, 0.3f, ~LayerMask.GetMask("character", "character_net", "character_ghost", "item"), QueryTriggerInteraction.Ignore)) continue;
                    float score = r + (c.y - hit.point.y) * 0.3f;
                    if (score < bestScore) { bestScore = score; best = stand; }
                }
            }
            return best;
        }
    }

    internal static class Carts
    {
        public static void Toggle(BrainState st, Vagon cart)
        {
            Humanoid me = st.Body;
            if (st.Cart == cart && cart.IsAttached(me)) { cart.Interact(me, false, false); st.Cart = null; Talk.Say(me, P("villagepleb_chores_cart_let_go")); return; }
            st.Cart = cart;
            st.CartSince = Time.time;
            Talk.Say(me, P("villagepleb_chores_cart_pull"));
        }

        public static bool Tick(BrainState st, Action<Vector3, float, bool> moveTo, Action stop)
        {
            Humanoid me = st.Body;
            Vagon cart = st.Cart;
            if (cart == null) return false;
            if (cart.IsAttached(me)) { st.CartSince = Time.time; return false; }
            if (Time.time - st.CartSince > 30f) { st.Cart = null; Talk.Say(me, P("villagepleb_chores_cart_failed")); return false; }
            Vector3 hitch = cart.m_attachPoint.position - cart.m_attachOffset;
            float d = Vector3.Distance(hitch, me.transform.position);
            if (d > 1.2f) { moveTo(hitch, 0.4f, d > 6f); Brain.Status(st, P("villagepleb_chores_status_to_cart")); return true; }
            stop();
            me.transform.position = hitch;
            Rigidbody body = me.GetComponent<Rigidbody>();
            if (body != null) { body.position = hitch; body.linearVelocity = Vector3.zero; }
            if (Time.time - st.CartAsked > 1.5f) { st.CartAsked = Time.time; cart.Interact(me, false, false); }
            Brain.Status(st, P("villagepleb_chores_status_hitching"));
            return true;
        }
    }

    internal static class Loans
    {
        public const string Tag = "vp_loan";

        public static void Mark(ItemDrop.ItemData item, Container from)
        {
            if (item.m_customData == null) item.m_customData = new Dictionary<string, string>();
            Vector3 p = from.transform.position;
            item.m_customData[Tag] = string.Format(CultureInfo.InvariantCulture, "{0:0.0},{1:0.0},{2:0.0}", p.x, p.y, p.z);
        }

        public static int Return(BrainState st)
        {
            Humanoid me = st.Body;
            if (Work.UsesPantry(me)) return 0;
            Inventory inv = me.GetInventory();
            List<ItemDrop.ItemData> borrowed = inv.GetAllItems().Where(i => i.m_customData != null && i.m_customData.ContainsKey(Tag)).ToList();
            if (borrowed.Count == 0) return 0;
            int own = inv.GetAllItems().Where(i => Food.IsFood(i) && (i.m_customData == null || !i.m_customData.ContainsKey(Tag))).Sum(i => i.m_stack);
            if (own < 5 && Food.Meals(me).Count < 2) return 0;
            int back = 0;
            foreach (ItemDrop.ItemData item in borrowed)
            {
                string[] f = item.m_customData[Tag].Split(',');
                if (f.Length == 6) f = new[] { f[0] + "." + f[1], f[2] + "." + f[3], f[4] + "." + f[5] };
                if (f.Length != 3 || !float.TryParse(f[0], NumberStyles.Float, CultureInfo.InvariantCulture, out float x) || !float.TryParse(f[1], NumberStyles.Float, CultureInfo.InvariantCulture, out float y)
                    || !float.TryParse(f[2], NumberStyles.Float, CultureInfo.InvariantCulture, out float z)) continue;
                var spot = new Vector3(x, y, z);
                Container chest = Loaded.All<Container>().FirstOrDefault(c => Vector3.Distance(c.transform.position, spot) < 1f && !Containers.InUse(c));
                if (chest == null || Vector3.Distance(chest.transform.position, me.transform.position) > 60f) continue;
                if (!Containers.Take(chest)) continue;
                int n = item.m_stack;
                item.m_customData.Remove(Tag);
                if (chest.GetInventory().AddItem(item)) { inv.RemoveItem(item); back += n; }
                else item.m_customData[Tag] = string.Join(",", f);
            }
            if (back > 0) { Companion.SaveBag(me); st.Remember(P("villagepleb_chores_put_back_food", back)); }
            return back;
        }
    }

    [HarmonyPatch(typeof(Container), nameof(Container.UseItem))]
    internal static class Container_UseItem_Give
    {
        private static bool Prefix(Container __instance, Humanoid user, ItemDrop.ItemData item, ref bool __result)
        {
            if (!Companion.Is(__instance) || !(user is Player p) || item == null) return true;
            Humanoid c = __instance.GetComponent<Humanoid>();
            if (c == null || !Companion.CanCommand(c, p)) return true;
            __result = true;
            try
            {
                bool one = Food.IsFood(item) || item.m_shared.m_itemType == ItemDrop.ItemData.ItemType.Consumable;
                if (one)
                {
                    if (!Companion.Write(c, _ => { })) { p.Message(MessageHud.MessageType.Center, T("villagepleb_chores_gear_open", Companion.NameOf(c))); return false; }
                    ItemDrop.ItemData copy = item.Clone();
                    copy.m_stack = 1;
                    if (!c.GetInventory().CanAddItem(copy)) { p.Message(MessageHud.MessageType.Center, T("villagepleb_chores_no_room", Companion.NameOf(c))); return false; }
                    if (!p.GetInventory().RemoveItem(item, 1)) return false;
                    if (!c.GetInventory().AddItem(copy)) { p.GetInventory().AddItem(copy); p.Message(MessageHud.MessageType.Center, T("villagepleb_chores_no_room", Companion.NameOf(c))); return false; }
                    Companion.SaveBag(c);
                    Talk.Say(c, Food.IsFood(item) ? P("villagepleb_chores_thanks_food") : P("villagepleb_chores_thanks_potion"));
                    Idle.Queue(Brain.Get(c), "thumbsup", 0.3f, p);
                    return false;
                }
                p.Message(MessageHud.MessageType.Center, T("villagepleb_chores_only_food", Companion.NameOf(c)));
                __result = false;
            }
            catch (Exception e) { VillagePlebsPlugin.Instance?.Warn("Giving to the companion: " + e.Message); }
            return false;
        }
    }
}
