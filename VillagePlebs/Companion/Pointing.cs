using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using static VillagePlebs.Words;

namespace VillagePlebs
{
    internal static class Pointing
    {
        private static readonly int Mask = LayerMask.GetMask("Default", "static_solid", "Default_small", "piece", "piece_nonsolid", "terrain", "vehicle",
                                                             "character", "character_net", "character_ghost", "character_noenv", "hitbox", "item");

        public const float Reach = 150f;

        public static void Command(Player p)
        {
            var mine = Companion.All().Where(c => Companion.CanCommand(c, p) && !c.IsDead()).ToList();
            if (mine.Count == 0) { VillagePlebsPlugin.Tell(P("villagepleb_pointing_none_near")); return; }
            string refused = null;
            mine = mine.Where(c => { if (Brain.Claim(c, out string why) != null) return true; refused = refused ?? why; return false; }).ToList();
            if (mine.Count == 0) { VillagePlebsPlugin.Tell(refused ?? P("villagepleb_pointing_none_near")); return; }
            Transform eye = GameCamera.instance != null ? GameCamera.instance.transform : p.transform;
            RaycastHit[] hits = Physics.RaycastAll(eye.position, eye.forward, Reach, Mask, QueryTriggerInteraction.Ignore).OrderBy(h => h.distance).ToArray();
            foreach (RaycastHit hit in hits)
            {
                Character ch = hit.collider.GetComponentInParent<Character>();
                if (ch == p || (ch != null && Companion.Is(ch))) continue;
                if (Handle(p, mine, hit, ch)) return;
                return;
            }
            foreach (Humanoid c in mine) { Brain.Get(c).Area = null; if (Home.Follow(c)) Talk.Say(c, P("villagepleb_talk_reply_come")); }
            VillagePlebsPlugin.Tell(mine.Count == 1 ? P("villagepleb_pointing_comes_back", Companion.NameOf(mine[0])) : P("villagepleb_pointing_all_come_back"));
        }

        private static bool IsGround(RaycastHit hit) =>
            hit.collider.GetComponentInParent<Heightmap>() != null || hit.collider.GetComponentInParent<Piece>() != null || LayerMask.LayerToName(hit.collider.gameObject.layer) == "terrain";

        private static void MarkTask(BrainState st, Humanoid who, string doing)
        {
            Work.Task t = st.Task;
            if (t?.Target == null) return;
            Marks.Put(t.Target, who, doing, () => st.Task == t && t.Target != null);
        }

        private static GameObject WorkObject(Collider col)
        {
            Component c = col.GetComponentInParent<TreeLog>() as Component ?? col.GetComponentInParent<TreeBase>() as Component
                          ?? col.GetComponentInParent<MineRock5>() as Component ?? col.GetComponentInParent<MineRock>() as Component
                          ?? col.GetComponentInParent<Pickable>() as Component ?? col.GetComponentInParent<Destructible>() as Component;
            return c != null ? c.gameObject : null;
        }

        private static string WhyNot(Humanoid who, GameObject go)
        {
            if (go == null || go.GetComponent<Piece>() != null) return null;
            if (Groundwork.OnWorkedGround(go)) return P("villagepleb_pointing_worked_ground");
            ItemDrop.ItemData axe = Work.Axe(who), pick = Work.Pickaxe(who);
            int treeTier = go.GetComponent<TreeBase>()?.m_minToolTier ?? go.GetComponent<TreeLog>()?.m_minToolTier ?? -1;
            int rockTier = go.GetComponent<MineRock5>()?.m_minToolTier ?? go.GetComponent<MineRock>()?.m_minToolTier ?? -1;
            Destructible des = go.GetComponent<Destructible>();
            if (des != null)
            {
                DropTable drops = Work.DropsOf(des, out int tier);
                if (drops == null || drops.m_drops.Count == 0) return P("villagepleb_pointing_nothing_worth");
                bool wood = des.m_destructibleType == DestructibleType.Tree || des.m_damages.m_chop != HitData.DamageModifier.Immune && des.m_damages.m_pickaxe == HitData.DamageModifier.Immune;
                if (wood) treeTier = tier; else rockTier = tier;
            }
            if (treeTier >= 0) return axe == null ? P("villagepleb_pointing_need_axe") : P("villagepleb_pointing_axe_too_weak");
            if (rockTier >= 0) return pick == null ? P("villagepleb_pointing_need_pickaxe") : P("villagepleb_pointing_pickaxe_too_weak");
            Pickable p = go.GetComponent<Pickable>();
            if (p != null)
            {
                if (Companion.Zdo(p)?.GetBool(ZDOVars.s_picked, false) ?? false) return P("villagepleb_pointing_nothing_to_pick");
                return P("villagepleb_pointing_crop");
            }
            return null;
        }

        private static Humanoid Nearest(System.Collections.Generic.List<Humanoid> mine, Vector3 at) => mine.OrderBy(c => Vector3.Distance(c.transform.position, at)).First();

        private static bool Handle(Player p, System.Collections.Generic.List<Humanoid> mine, RaycastHit hit, Character ch)
        {
            GameObject go = hit.collider.attachedRigidbody != null ? hit.collider.attachedRigidbody.gameObject : hit.collider.transform.root.gameObject;

            if (ch != null && !ch.IsDead() && !ch.IsPlayer() && BaseAI.IsEnemy(mine[0], ch))
            {
                foreach (Humanoid c in mine)
                {
                    BrainState st = Brain.Get(c);
                    st.Focus = ch;
                    st.FocusUntil = Time.time + 30f;
                    st.NextAsk = 0f;
                    Talk.Say(c, P("villagepleb_pointing_on_it"));
                }
                BrainState first = Brain.Get(mine[0]);
                Marks.Put(ch, mine[0], P(mine.Count == 1 ? "villagepleb_pointing_mark_going_for" : "villagepleb_pointing_mark_going_for_all"), () => first.Focus == ch && Time.time < first.FocusUntil && !ch.IsDead());
                VillagePlebsPlugin.Tell(mine.Count == 1 ? P("villagepleb_pointing_go_for", Companion.NameOf(mine[0]), ch.m_name) : P("villagepleb_pointing_all_go_for", ch.m_name));
                return true;
            }

            TombStone tomb = go.GetComponentInParent<TombStone>();
            long tombOf = tomb != null ? Companion.Zdo(tomb)?.GetLong(Grave.OfKey, 0L) ?? 0L : 0L;
            Humanoid owner = tombOf != 0L ? mine.FirstOrDefault(c => Companion.IdOf(c) == tombOf) : null;
            if (owner != null)
            {
                BrainState os = Brain.Get(owner);
                Grave.Fetch(os, tomb);
                Talk.Say(owner, P("villagepleb_pointing_my_things"));
                Marks.Put(tomb, owner, P("villagepleb_pointing_mark_getting_things"), () => os.GraveOrdered == tomb || os.GraveOn == tomb);
                return true;
            }

            Humanoid who = Nearest(mine, hit.point);
            BrainState w = Brain.Get(who);
            w.Area = null;

            ItemDrop item = hit.collider.GetComponentInParent<ItemDrop>();
            Vector3 heap = item != null ? item.transform.position : hit.point;
            if ((item != null || IsGround(hit)) && Work.OrderPickUp(w, heap, item != null ? 3f : 1.5f))
            {
                Talk.Say(who, P("villagepleb_pointing_pick_those_up"));
                Marks.PutSpot(heap, who, P("villagepleb_pointing_mark_picking_up"), () => w.PickQueue.Count > 0 || (w.Task != null && w.Task.Kind == Work.Kind.PickUp && w.Task.Ordered));
                return true;
            }
            Activity.Log(who, $"you pointed at {Utils.GetPrefabName(go)} ({hit.collider.name}, layer {LayerMask.LayerToName(hit.collider.gameObject.layer)}, {hit.distance:0} m)");

            Bed bed = go.GetComponentInParent<Bed>();
            if (bed != null) { Home.ToggleBed(bed, who); return true; }

            Vagon cart = go.GetComponentInParent<Vagon>();
            if (cart != null) { Carts.Toggle(w, cart); if (w.Cart == cart) Marks.Put(cart, who, P("villagepleb_pointing_mark_pulling"), () => w.Cart == cart); return true; }

            Container chest = go.GetComponentInParent<Container>();
            if (chest != null && Home.IsChest(chest))
            {
                if (Work.Ordered(w, Work.Kind.Store, chest))
                {
                    Talk.Say(who, P("villagepleb_pointing_will_put"));
                    MarkTask(w, who, P("villagepleb_pointing_mark_putting"));
                    return true;
                }
            }

            GameObject thing = WorkObject(hit.collider) ?? go;
            Work.Task work = Work.Workable(w, thing);
            bool fetching = work?.FetchFrom != null;
            if (work != null && Work.Ordered(w, work))
            {
                List<Component> patch = Work.OrderArea(w, work);
                Work.Area area = w.Area;
                foreach (Component c in patch) { Component cc = c; Marks.Put(cc, who, null, () => w.Area == area && cc != null); }
                string doing = P(work.Kind == Work.Kind.Pick ? "villagepleb_pointing_mark_picking" : work.Job == Job.Wood ? "villagepleb_pointing_mark_chopping" : "villagepleb_pointing_mark_mining");
                int more = patch.Count(c => c != work.Target);
                Talk.Say(who, P(more == 0 ? (work.Kind == Work.Kind.Pick ? "villagepleb_pointing_will_pick" : "villagepleb_pointing_on_it_calm")
                    : work.Kind == Work.Kind.Pick ? "villagepleb_pointing_will_pick_all" : work.Job == Job.Wood ? "villagepleb_pointing_will_chop" : "villagepleb_pointing_will_mine"));
                if (fetching) Talk.Say(who, P("villagepleb_pointing_fetch_tool"));
                MarkTask(w, who, doing);
                return true;
            }
            string why = WhyNot(who, thing);
            if (why != null)
            {
                Talk.Say(who, why);
                VillagePlebsPlugin.Tell($"{Companion.NameOf(who)}: {why}");
                return true;
            }

            if (!IsGround(hit))
            {
                string name = Utils.GetPrefabName(go).ToLowerInvariant();
                string what = P(name.Contains("rock") || name.Contains("stone") || name.Contains("boulder") ? "villagepleb_pointing_rock_cant" : "villagepleb_pointing_cant_do");
                Talk.Say(who, what);
                VillagePlebsPlugin.Tell($"{Companion.NameOf(who)}: {what} " + P("villagepleb_pointing_ground_hint"));
                return true;
            }

            Vector3 spot = hit.point;
            if (Companion.Write(who, z => { z.Set(Keys.Order, (int)Order.Guard); z.Set(Keys.Post, spot); }))
            {
                w.ManualOrderAt = Time.time;
                Marks.PutSpot(spot, who, P("villagepleb_pointing_mark_waiting"), () => who != null && Companion.OrderOf(who) == Order.Guard && Vector3.Distance(Companion.Zdo(who).GetVec3(Keys.Post, Vector3.zero), spot) < 0.5f);
                Talk.Say(who, P("villagepleb_pointing_will_wait"));
                VillagePlebsPlugin.Tell(P("villagepleb_pointing_waits_there", Companion.NameOf(who), MainConfig.commandKey.Value));
            }
            return true;
        }
    }
}
