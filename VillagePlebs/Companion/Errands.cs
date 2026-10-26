using System.Linq;
using UnityEngine;
using static VillagePlebs.Words;

namespace VillagePlebs
{
    internal enum Errand { PutAway, Restock, BetterGear, RepairGear, RepairBase, Fires, Cook, Plant, Harvest, Fish, Smelt, Shop }

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
            (Errand.Plant, P("villagepleb_errands_plant"), "Sows seeds in neat rows on the tilled ground at home with its cultivator, fetching both from its own chests."),
            (Errand.Harvest, P("villagepleb_errands_harvest"), "Picks every ripe crop at home and carries the harvest."),
            (Errand.Fish, P("villagepleb_errands_fish"), "Fishes near home with a rod and the right bait (from its own chests) until the bait runs out or its bag is full."),
            (Errand.Smelt, P("villagepleb_errands_smelt"), "Empties the smelters near home, feeds them fuel and ore from its bag and the chests, and takes what they made home."),
            (Errand.Shop, P("villagepleb_errands_shop"), "Pick what to buy from the traders you have found: it pays from the coins in its bag and is back with them in 15 minutes."),
        };

        public static string Run(BrainState st, Errand what)
        {
            Humanoid me = st?.Body;
            if (me == null) return P("villagepleb_errands_not_here");
            // Shopping needs no home: it opens the list of what the traders sell.
            if (what == Errand.Shop) return Shopping.Open(st);
            if (!Work.HasHome(me)) return P("villagepleb_errands_no_home");
            Vector3 center = Work.Center(me);
            float radius = Work.RadiusOf(me);
            if (Vector3.Distance(center, me.transform.position) > radius + 40f) return P("villagepleb_errands_too_far");
            // A new errand takes the place of the one it was on.
            st.Farm = null;
            st.Then = null;
            if (st.Task != null && st.Task.Ordered) st.Task = null;
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
                    if (Mending.Hammer(me) != null) going = Go(st, Work.Kind.Mend, damaged);
                    else if (Work.OwnChestWith(me, Mending.IsHammer) is Container chest)
                        going = Work.Ordered(st, Work.Fetching(Work.New(Work.Kind.Mend, damaged, Job.None), chest, Mending.IsHammer));
                    else return P("villagepleb_errands_need_hammer");
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
                    bool raw = me.GetInventory().GetAllItems().Any(Work.IsCookable);
                    Container larder = raw ? null : Work.OwnChestWith(me, Work.IsCookable);
                    if (!raw && larder == null) return P("villagepleb_errands_nothing_raw");
                    CookingStation stove = Kitchen.Find(me, center, radius + 20f);
                    if (stove == null)
                    {
                        // A cooking station whose fire is out: it lights it, then cooks (fetching the food from its chest first if need be).
                        var food = raw ? me.GetInventory().GetAllItems() : larder.GetInventory().GetAllItems();
                        var cold = Kitchen.ToLight(me, center, radius + 20f, food);
                        if (cold.Fire == null) return P("villagepleb_errands_no_stove");
                        Work.Task cook = raw ? Work.New(Work.Kind.Cook, cold.Stove, Job.Cook) : Work.Fetching(Work.New(Work.Kind.Cook, cold.Stove, Job.Cook), larder, Work.IsCookable);
                        going = Work.Ordered(st, Work.LightFire(cold.Fire));
                        if (going) st.Then = cook;
                        return P("villagepleb_errands_lighting_fire");
                    }
                    going = raw ? Go(st, Work.Kind.Cook, stove, Job.Cook) : Work.Ordered(st, Work.Fetching(Work.New(Work.Kind.Cook, stove, Job.Cook), larder, Work.IsCookable));
                    return P("villagepleb_errands_off_to_cook");
                }
                case Errand.Plant:
                {
                    Farming.Forget(st);
                    if (!Farming.HasField(st, center, radius)) return P("villagepleb_errands_no_tilled");
                    bool InChests(System.Func<ItemDrop.ItemData, bool> has) => Home.Chests(me).Any(c => c.GetInventory().GetAllItems().Any(has));
                    if (Farming.Cultivator(me) == null && !InChests(Farming.IsCultivator)) return P("villagepleb_errands_need_cultivator");
                    if (!Farming.CanSow(me) && !me.GetInventory().GetAllItems().Any(Farming.IsSeedItem) && !InChests(Farming.IsSeedItem)) return P("villagepleb_errands_no_seeds");
                    going = Work.StartFarm(st, Errand.Plant);
                    return going ? P("villagepleb_errands_planting") : null;
                }
                case Errand.Harvest:
                {
                    if (!Farming.RipeNear(center, radius).Any()) return P("villagepleb_errands_nothing_ripe");
                    going = Work.StartFarm(st, Errand.Harvest);
                    return going ? P("villagepleb_errands_harvesting") : null;
                }
                case Errand.Fish:
                {
                    bool OwnChests(System.Func<ItemDrop.ItemData, bool> has) => Home.Chests(me).Any(c => c.GetInventory().GetAllItems().Any(has));
                    if (Fishing.Rod(me) == null && !OwnChests(Fishing.IsRod)) return P("villagepleb_errands_need_rod");
                    if (!Fishing.HasBait(me) && !OwnChests(Fishing.IsBait)) return P("villagepleb_errands_no_bait");
                    if (!Fishing.Near(center, radius + Farming.FieldReach).Any()) return P("villagepleb_errands_no_fish");
                    going = Work.StartFarm(st, Errand.Fish);
                    return going ? P("villagepleb_errands_fishing") : null;
                }
                case Errand.Smelt:
                {
                    if (!Smelting.Near(center, radius).Any()) return P("villagepleb_errands_no_smelter");
                    going = Work.StartFarm(st, Errand.Smelt);
                    return going ? P("villagepleb_errands_smelting") : null;
                }
            }
            return null;
        }
    }
}
