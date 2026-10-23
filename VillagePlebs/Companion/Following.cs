using System.Linq;
using UnityEngine;
using static VillagePlebs.Words;

namespace VillagePlebs
{
    internal enum Doing { Travelling, Fighting, Mining, Chopping }

    internal static class Following
    {
        public const string MigratedKey = "vp_auto1", AutoHomeKey = "vp_autohome";

        public static bool AutoHome(Component c) => Companion.Zdo(c)?.GetBool(AutoHomeKey, true) ?? true;

        private static void HomeAndAway(BrainState st, Player master)
        {
            Humanoid me = st.Body;
            ZDO z = Companion.Zdo(me);
            if (master == null || !AutoHome(me) || !Work.HasHome(me) || st.InCombat) return;
            Vector3 home = Work.Center(me);
            float radius = Work.RadiusOf(me);
            float youFromHome = Vector3.Distance(master.transform.position, home), meFromHome = Vector3.Distance(me.transform.position, home);
            Order order = Companion.OrderOf(me);
            if (order == Order.Gather)
            {
                if (youFromHome < radius) { st.MasterWasHome = true; return; }
                if (st.MasterWasHome && youFromHome > radius + 15f && Vector3.Distance(me.transform.position, master.transform.position) < 150f && Time.time - st.ManualOrderAt > 300f)
                {
                    st.MasterWasHome = false;
                    if (Home.Follow(me, false)) { st.Outing = true; Talk.Tell(me, P("villagepleb_following_coming"), "autofollow", 1f); st.Remember(P("villagepleb_following_came_along")); }
                }
                return;
            }
            st.MasterWasHome = false;
            if (order != Order.Follow) return;
            if (meFromHome > radius + 15f) st.Outing = true;
            if (!st.Outing || youFromHome > radius * 0.6f || meFromHome > radius) { st.HomeSince = 0f; return; }
            if (st.HomeSince == 0f) { st.HomeSince = Time.time; return; }
            if (Time.time - st.HomeSince < 10f) return;
            st.HomeSince = 0f;
            if (Home.GoHome(me, true, false)) { Talk.Tell(me, P("villagepleb_following_home_again"), "autohome", 1f); st.Remember(P("villagepleb_following_lives_here_again")); st.MasterWasHome = true; }
        }

        public static Style Effective(Humanoid c)
        {
            Style chosen = Companion.Chosen(c);
            if (chosen != Style.Auto) return chosen;
            BrainState st = Brain.Get(c);
            return st != null ? st.AutoStyle : Style.Balanced;
        }

        public static bool DefendOnly(Humanoid c) => Companion.Chosen(c) == Style.Auto && (Brain.Get(c)?.DefendOnly ?? false);

        public static void Tick(BrainState st, Player master)
        {
            if (Time.time < st.NextReadLook) return;
            st.NextReadLook = Time.time + 1f;
            Humanoid me = st.Body;
            ZDO z = Companion.Zdo(me);
            if (z == null) return;
            if (!z.GetBool(MigratedKey, false))
            {
                if (Companion.Chosen(me) == Style.Balanced) z.Set(Keys.Style, (int)Style.Auto);
                z.Set(MigratedKey, true);
            }
            HomeAndAway(st, master);
            if (Companion.OrderOf(me) != Order.Follow || master == null)
            {
                st.Doing = Doing.Travelling; st.DefendOnly = false; st.Outmatched = false; st.AutoStyle = Style.Balanced; st.AutoNote = "";
                return;
            }

            Heightmap.Biome biome = WorldGenerator.instance != null ? WorldGenerator.instance.GetBiome(me.transform.position) : Heightmap.Biome.Meadows;
            if (!me.InInterior() && st.LastLand != biome.ToString())
            {
                st.LastLand = biome.ToString();
                if (Journal.Place(me, BiomeToken(biome), false, OldBiomeWord(biome))) Banter.NewLand(me, BiomeToken(biome));
            }
            float threat = CatchUp.ThreatOf(biome), power = CatchUp.Power(me);
            bool outmatched = power < threat * 0.9f;
            if (outmatched && !st.Outmatched)
            {
                ItemDrop.ItemData weapon = Companion.BestMelee(me) ?? Companion.BestRanged(me);
                string place = BiomeToken(biome);
                Talk.Tell(me, P("villagepleb_following_outmatched", place, weapon != null ? Low(weapon.m_shared.m_name) : P("villagepleb_following_no_weapon"), $"{Companion.Armor(me):0}"),
                          "outmatched:" + biome, 10f);
                st.Remember(P("villagepleb_following_outmatched_note", place));
            }
            st.Outmatched = outmatched;

            st.Doing = Read(me, master);
            if (st.Doing != Doing.Mining && st.Doing != Doing.Chopping) st.Helping = false;
            bool hurt = me.GetHealthPercentage() < 0.4f;
            st.DefendOnly = outmatched || st.Doing != Doing.Fighting;
            st.AutoStyle = outmatched || hurt ? Style.Defensive : Style.Balanced;
            st.AutoNote = outmatched ? P("villagepleb_following_auto_outmatched", BiomeToken(biome))
                        : st.Doing == Doing.Mining ? P("villagepleb_following_auto_mine") : st.Doing == Doing.Chopping ? P("villagepleb_following_auto_chop")
                        : st.Doing == Doing.Fighting ? P("villagepleb_following_auto_fight") : "";
        }

        private static Doing Read(Humanoid me, Player p)
        {
            bool threatened = Character.GetAllCharacters().Any(c => c != null && !c.IsDead() && !c.IsPlayer() && !Companion.Is(c) && Brain.TargetOf(c) is Character t
                                                                    && (t == p || t == me) && Vector3.Distance(c.transform.position, p.transform.position) < 20f);
            if (threatened) return Doing.Fighting;
            ItemDrop.ItemData held = p.GetCurrentWeapon();
            bool swung = p.GetTimeSinceLastAttack() < 30f;
            if (held != null && swung && held.m_shared.m_damages.m_pickaxe > 0f) return Doing.Mining;
            if (held != null && swung && held.m_shared.m_damages.m_chop > 0f && held.m_shared.m_skillType == Skills.SkillType.Axes) return Doing.Chopping;
            return Doing.Travelling;
        }

        private static string BiomeToken(Heightmap.Biome b) => "$biome_" + b.ToString().ToLowerInvariant();

        private static string OldBiomeWord(Heightmap.Biome b) => b switch
        {
            Heightmap.Biome.BlackForest => "forest",
            Heightmap.Biome.Swamp => "swamp",
            Heightmap.Biome.Mountain => "mountain",
            Heightmap.Biome.Plains => "plains",
            Heightmap.Biome.Mistlands => "mist",
            Heightmap.Biome.AshLands => "ash land",
            Heightmap.Biome.DeepNorth => "frozen north",
            Heightmap.Biome.Ocean => "sea",
            _ => "place",
        };
    }
}
