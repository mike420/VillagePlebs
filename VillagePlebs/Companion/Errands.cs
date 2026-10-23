using System.Linq;
using UnityEngine;
using static VillagePlebs.Words;

namespace VillagePlebs
{
    internal enum Errand { PutAway, Restock, BetterGear, RepairGear, RepairBase, Fires, Cook }

    internal static class Errands
    {
        public static readonly (Errand What, string Label, string Help)[] All =
        {
            (Errand.PutAway, P("villagepleb_errands_put_away"), "What it carries but does not keep goes into the chests: yours, and its stock into its own."),
            (Errand.Restock, P("villagepleb_errands_restock"), "Fills its three food slots from its chests, else yours."),
            (Errand.BetterGear, P("villagepleb_errands_better_gear"), "Looks through your chests for a better weapon, armour, shield, bow, arrows or tool, wears it and puts its old one back."),
            (Errand.RepairGear, P("villagepleb_errands_repair_gear"), "Takes its worn gear to the workbench or forge that can fix it."),
            (Errand.RepairBase, P("villagepleb_errands_repair_base"), "Fixes damaged walls, floors and the rest with its hammer (makes one if it has none)."),
            (Errand.Fires, P("villagepleb_errands_fires"), "Tops up the fires that are running low with wood."),
            (Errand.Cook, P("villagepleb_errands_cook"), "Puts the raw food it carries on a cooking station near home."),
        };

        public static string Run(BrainState st, Errand what)
        {
            Humanoid me = st?.Body;
            if (me == null) return P("villagepleb_errands_not_here");
            if (!Work.HasHome(me)) return P("villagepleb_errands_no_home");
            Vector3 center = Work.Center(me);
            float radius = Work.RadiusOf(me);
            if (Vector3.Distance(center, me.transform.position) > radius + 40f) return P("villagepleb_errands_too_far");
            string said = Do(st, what, center, radius, out bool going);
            if (said != null) Talk.Say(me, said);
            Activity.Log(me, $"errand ({what}): {said ?? "on its way"}");
            return going ? $"{Companion.NameOf(me)}: {said ?? P("villagepleb_errands_on_my_way")}" : said;
        }

        private static bool Reach(Humanoid me, Component c) => c != null && Brain.CanReach(me, c.transform.position);

        private static bool Go(BrainState st, Work.Kind kind, Component target, Job job = Job.None) =>
            target != null && Work.Ordered(st, Work.New(kind, target, job));

        public static string ForTheRoad(BrainState st)
        {
            Humanoid me = st?.Body;
            if (me == null || !Work.HasHome(me)) return null;
            Vector3 center = Work.Center(me);
            float radius = Work.RadiusOf(me);
            if (Vector3.Distance(center, me.transform.position) > radius) return null;
            if (me.GetInventory().GetAllItems().Where(Food.IsFood).Sum(i => i.m_stack) >= 12) return null;
            Container mine = Home.Chests(me).Where(c => !Containers.InUse(c) && c.GetInventory().GetAllItems().Any(i => Gear.WantsFood(me, i)))
                                 .OrderBy(c => Vector3.Distance(c.transform.position, me.transform.position)).FirstOrDefault(c => Reach(me, c));
            if (mine != null && Go(st, Work.Kind.Store, mine)) { Talk.Say(me, P("villagepleb_errands_food_for_road")); return "food from its chest"; }
            if (Work.UsesPantry(me) && Work.RationLeft(me) > 0)
            {
                Container yours = Work.YourFood(me, center, radius + 20f);
                if (yours != null && Go(st, Work.Kind.Fetch, yours)) { Talk.Say(me, P("villagepleb_errands_food_for_road")); return "food from your chest"; }
            }
            return null;
        }

        private static string Do(BrainState st, Errand what, Vector3 center, float radius, out bool going)
        {
            Humanoid me = st.Body;
            going = false;
            switch (what)
            {
                case Errand.PutAway:
                {
                    bool rest = me.GetInventory().GetAllItems().Any(i => !Work.Keeps(me, i));
                    if (!rest) return P("villagepleb_errands_nothing_to_put_away");
                    Container chest = Home.Chests(me).Where(c => !Containers.InUse(c) && c.GetInventory().HaveEmptySlot()).OrderBy(c => Vector3.Distance(c.transform.position, me.transform.position)).FirstOrDefault(c => Reach(me, c))
                                      ?? (Work.Stows(me) ? Work.YourChests(me, center, radius + 20f).Where(c => c.GetInventory().HaveEmptySlot()).Take(6).FirstOrDefault(c => Reach(me, c)) : null);
                    if (chest == null) return P("villagepleb_errands_no_chest_room");
                    going = Go(st, Work.Kind.Store, chest);
                    return going ? P("villagepleb_errands_putting_away") : P("villagepleb_errands_things_open");
                }
                case Errand.Restock:
                {
                    if (!Gear.FoodLow(me)) return P("villagepleb_errands_food_full");
                    Container mine = Home.Chests(me).Where(c => !Containers.InUse(c) && c.GetInventory().GetAllItems().Any(i => Gear.WantsFood(me, i)))
                                         .OrderBy(c => Vector3.Distance(c.transform.position, me.transform.position)).FirstOrDefault(c => Reach(me, c));
                    if (mine != null) { going = Go(st, Work.Kind.Store, mine); return P("villagepleb_errands_food_from_mine"); }
                    Container yours = Work.YourFood(me, center, radius + 20f);
                    if (yours != null) { going = Go(st, Work.Kind.Fetch, yours); return P("villagepleb_errands_food_from_yours"); }
                    return P("villagepleb_errands_no_food");
                }
                case Errand.BetterGear:
                {
                    Container chest = Armory.Find(me, center, radius + 20f, force: true);
                    if (chest == null) return P("villagepleb_errands_nothing_better");
                    going = Go(st, Work.Kind.Armory, chest);
                    return P("villagepleb_errands_looking_gear");
                }
                case Errand.RepairGear:
                    return Repair.Order(st, out going);
                case Errand.RepairBase:
                {
                    WearNTear damaged = Mending.Damaged(me, center, radius, c => Brain.CanReach(me, c.transform.position, 4f));
                    if (damaged == null) return P("villagepleb_errands_nothing_to_fix");
                    if (Mending.Hammer(me) == null) Mending.MakeHammer(st);
                    if (Mending.Hammer(me) == null) return P("villagepleb_errands_need_hammer");
                    going = Go(st, Work.Kind.Mend, damaged);
                    return P("villagepleb_errands_mending");
                }
                case Errand.Fires:
                {
                    Fireplace fire = Fires.Low(me, center, radius);
                    if (fire == null) return P("villagepleb_errands_fires_fed");
                    going = Go(st, Work.Kind.Fuel, fire);
                    return P("villagepleb_errands_fueling");
                }
                case Errand.Cook:
                {
                    if (!me.GetInventory().GetAllItems().Any(Work.IsCookable)) return P("villagepleb_errands_nothing_raw");
                    CookingStation stove = Kitchen.Find(me, center, radius + 20f);
                    if (stove == null) return P("villagepleb_errands_no_stove");
                    going = Go(st, Work.Kind.Cook, stove, Job.Cook);
                    return P("villagepleb_errands_off_to_cook");
                }
            }
            return null;
        }
    }
}
