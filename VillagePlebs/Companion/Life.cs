using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using HarmonyLib;
using UnityEngine;

namespace VillagePlebs
{
    internal static class Food
    {
        private const string Key = "vp_food", MaxStaminaKey = "vp_maxst";

        internal class Meal { public ItemDrop.ItemData Item; public float Time; public float Fraction => Mathf.Clamp01(Time / Mathf.Max(1f, Item.m_shared.m_foodBurnTime)); }
        private class State { public List<Meal> Meals = new List<Meal>(); public float NextTick, NextRegen, NextEat, NextSave; public string Saved; }
        private static readonly Dictionary<Humanoid, State> States = Cleanup.Track(new Dictionary<Humanoid, State>());

        public static bool IsFood(ItemDrop.ItemData i) => i.m_shared.m_itemType == ItemDrop.ItemData.ItemType.Consumable && i.m_shared.m_foodBurnTime > 0f && (i.m_shared.m_food > 0f || i.m_shared.m_foodStamina > 0f);

        private static State Of(Humanoid c)
        {
            if (States.TryGetValue(c, out State s)) return s;
            foreach (Humanoid gone in States.Keys.Where(k => k == null).ToList()) States.Remove(gone);
            States[c] = s = new State();
            foreach (string part in (Companion.Zdo(c)?.GetString(Key, "") ?? "").Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries))
            {
                string[] f = part.Split(':');
                GameObject prefab = f.Length == 2 ? ObjectDB.instance?.GetItemPrefab(f[0]) : null;
                ItemDrop drop = prefab != null ? prefab.GetComponent<ItemDrop>() : null;
                if (drop != null && float.TryParse(f[1], NumberStyles.Float, CultureInfo.InvariantCulture, out float t)) { ItemDrop.ItemData item = drop.m_itemData.Clone(); item.m_dropPrefab = drop.gameObject; s.Meals.Add(new Meal { Item = item, Time = t }); }
            }
            return s;
        }

        public static void Reload(Humanoid c) => States.Remove(c);

        public static List<Meal> Meals(Humanoid c) => Of(c).Meals;

        private static float Curve(Meal m) => Mathf.Pow(m.Fraction, 0.3f);

        public static float MaxHealth(Humanoid c) => MainConfig.baseHealth.Value + EpicMmo.Health(c) + Of(c).Meals.Sum(m => m.Item.m_shared.m_food * Curve(m));

        public static float MaxEitr(Humanoid c) => Of(c).Meals.Sum(m => m.Item.m_shared.m_foodEitr * Curve(m));

        public static float MaxStamina(Character c)
        {
            ZNetView v = c.GetComponent<ZNetView>();
            if (v != null && v.IsValid() && !v.IsOwner()) return v.GetZDO().GetFloat(MaxStaminaKey, MainConfig.baseStamina.Value);
            return MainConfig.baseStamina.Value + EpicMmo.Stamina(c) + (c is Humanoid h ? Of(h).Meals.Sum(m => m.Item.m_shared.m_foodStamina * Curve(m)) : 0f);
        }

        public static void Tick(Humanoid c, BrainState st)
        {
            State s = Of(c);
            if (Time.time >= s.NextTick)
            {
                s.NextTick = Time.time + 1f;
                foreach (Meal m in s.Meals) m.Time -= 1f * Game.m_foodRate;
                foreach (Meal done in s.Meals.Where(m => m.Time <= 0f).ToList()) { s.Meals.Remove(done); st.Remember(Words.P("villagepleb_life_finished_meal", Words.Low(done.Item.m_shared.m_name))); }
                c.SetMaxHealth(MaxHealth(c));
                ZDO z = Companion.Zdo(c);
                float maxSt = Mathf.Round(MaxStamina(c));
                if (z.GetFloat(MaxStaminaKey, -1f) != maxSt) z.Set(MaxStaminaKey, maxSt);
                string meals = string.Join(",", s.Meals.Where(m => m.Item.m_dropPrefab != null).Select(m => Utils.GetPrefabName(m.Item.m_dropPrefab)));
                if (meals != s.Saved || Time.time >= s.NextSave)
                {
                    s.Saved = meals;
                    s.NextSave = Time.time + 60f;
                    z.Set(Key, string.Join(",", s.Meals.Where(m => m.Item.m_dropPrefab != null).Select(m => Utils.GetPrefabName(m.Item.m_dropPrefab) + ":" + m.Time.ToString("0", CultureInfo.InvariantCulture))));
                }
            }
            if (Time.time >= s.NextRegen)
            {
                s.NextRegen = Time.time + 10f;
                float regen = s.Meals.Sum(m => m.Item.m_shared.m_foodRegen);
                if (regen > 0f)
                {
                    float mult = 1f;
                    c.GetSEMan().ModifyHealthRegen(ref mult);
                    if (Rest.IsRested(c)) mult *= Rest.Mult;
                    c.Heal(regen * mult, true);
                }
            }
            if (Time.time >= s.NextEat) { s.NextEat = Time.time + 2f; TryEat(c, s, st); }
        }

        public const string FrugalKey = "vp_frugal", AteDayKey = "vp_ateday", AteNKey = "vp_aten";

        public static bool Frugal(Component c) => Companion.Zdo(c)?.GetBool(FrugalKey, true) ?? true;

        public static int AteToday(Component c) { ZDO z = Companion.Zdo(c); return z != null && z.GetInt(AteDayKey, -1) == Journal.Day ? z.GetInt(AteNKey, 0) : 0; }

        private static void TryEat(Humanoid c, State s, BrainState st)
        {
            float below = MainConfig.eatBelow.Value / 100f;
            bool calm = Frugal(c) && Companion.OrderOf(c) == Order.Gather && !st.InCombat && Time.time - st.LastHurtAt > 60f;
            float hp = c.GetHealthPercentage();
            int want = !calm ? 3 : hp < 0.7f ? 2 : st.Asleep ? 0 : st.Task != null ? 2 : 1;
            if (s.Meals.Count(m => m.Time >= m.Item.m_shared.m_foodBurnTime * below) >= want) return;
            Meal depleted = s.Meals.Where(m => m.Time < m.Item.m_shared.m_foodBurnTime * below).OrderBy(m => m.Time).FirstOrDefault();
            ItemDrop.ItemData best = c.GetInventory().GetAllItems().Where(IsFood)
                .Where(i => !s.Meals.Any(m => m.Item.m_shared.m_name == i.m_shared.m_name && m.Time >= m.Item.m_shared.m_foodBurnTime * below))
                .OrderBy(i => (calm ? 1 : -1) * (i.m_shared.m_food + i.m_shared.m_foodStamina)).FirstOrDefault();
            if (best == null) return;
            Meal same = s.Meals.FirstOrDefault(m => m.Item.m_shared.m_name == best.m_shared.m_name);
            if (same != null) s.Meals.Remove(same);
            else if (s.Meals.Count >= 3 && depleted != null) s.Meals.Remove(depleted);
            s.Meals.Add(new Meal { Item = best.Clone(), Time = best.m_shared.m_foodBurnTime });
            if (best.m_shared.m_consumeStatusEffect != null) c.GetSEMan().AddStatusEffect(best.m_shared.m_consumeStatusEffect, true);
            c.m_consumeItemEffects.Create(c.transform.position, Quaternion.identity);
            c.GetInventory().RemoveOneItem(best);
            st.Remember(Words.P("villagepleb_life_ate", best.m_shared.m_name));
            ZDO ate = Companion.Zdo(c);
            if (ate != null) { int day = Journal.Day; ate.Set(AteNKey, (ate.GetInt(AteDayKey, -1) == day ? ate.GetInt(AteNKey, 0) : 0) + 1); ate.Set(AteDayKey, day); }
            s.NextTick = 0f;
        }

        public static void PassTime(Humanoid c, BrainState st, float seconds)
        {
            State s = Of(c);
            for (float t = 0f; t < seconds; t += 60f)
            {
                foreach (Meal m in s.Meals) m.Time -= 60f * Game.m_foodRate;
                s.Meals.RemoveAll(m => m.Time <= 0f);
                TryEat(c, s, st);
            }
            s.NextTick = 0f;
            s.NextSave = 0f;
        }

        public static void Clear(Humanoid c) { if (States.TryGetValue(c, out State s)) s.Meals.Clear(); }

        public static void Forget() => States.Clear();
    }

    internal static class Skill
    {
        public const string Key = "vp_skills";
        private class Level { public float Value, Progress; }
        private static readonly Dictionary<Humanoid, Dictionary<Skills.SkillType, Level>> Levels = Cleanup.Track(new Dictionary<Humanoid, Dictionary<Skills.SkillType, Level>>());
        private static Dictionary<Skills.SkillType, Skills.SkillDef> _defs;

        private static Skills.SkillDef Def(Skills.SkillType type)
        {
            if (_defs == null)
            {
                _defs = new Dictionary<Skills.SkillType, Skills.SkillDef>();
                Skills skills = ZNetScene.instance?.GetPrefab("Player")?.GetComponent<Skills>();
                if (skills != null) foreach (Skills.SkillDef d in skills.m_skills) _defs[d.m_skill] = d;
            }
            return _defs.TryGetValue(type, out Skills.SkillDef def) ? def : null;
        }

        private static Dictionary<Skills.SkillType, Level> Of(Humanoid c)
        {
            ZNetView v = c.GetComponent<ZNetView>();
            if (v != null && v.IsValid() && !v.IsOwner()) return Parse(v.GetZDO().GetString(Key, ""));
            string saved = Companion.Zdo(c)?.GetString(Key, "") ?? "";
            if (Levels.TryGetValue(c, out var map) && Seen.TryGetValue(c, out string seen) && seen == saved) return map;
            foreach (Humanoid gone in Levels.Keys.Where(k => k == null).ToList()) { Levels.Remove(gone); Seen.Remove(gone); NextSave.Remove(gone); }
            Levels[c] = map = Parse(saved);
            Seen[c] = saved;
            return map;
        }

        private static readonly Dictionary<Humanoid, string> Seen = Cleanup.Track(new Dictionary<Humanoid, string>());

        private static Dictionary<Skills.SkillType, Level> Parse(string text)
        {
            var map = new Dictionary<Skills.SkillType, Level>();
            foreach (string part in text.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries))
            {
                string[] f = part.Split(':');
                if (f.Length == 3 && int.TryParse(f[0], out int id) && float.TryParse(f[1], NumberStyles.Float, CultureInfo.InvariantCulture, out float lv) && float.TryParse(f[2], NumberStyles.Float, CultureInfo.InvariantCulture, out float acc))
                    map[(Skills.SkillType)id] = new Level { Value = lv, Progress = acc };
            }
            return map;
        }

        private static string Write(Dictionary<Skills.SkillType, Level> map) =>
            string.Join(",", map.Select(kv => $"{(int)kv.Key}:{kv.Value.Value.ToString("0.##", CultureInfo.InvariantCulture)}:{kv.Value.Progress.ToString("0.###", CultureInfo.InvariantCulture)}"));

        public static float Get(Character c, Skills.SkillType type)
        {
            if (!(c is Humanoid h)) return 0f;
            return Of(h).TryGetValue(type, out Level l) ? l.Value : MainConfig.startingSkill.Value;
        }

        // Every skill the game itself has (as in your own skills list), and only those: none a mod adds to the player.
        private static readonly Skills.SkillType[] Vanilla = ((Skills.SkillType[])Enum.GetValues(typeof(Skills.SkillType)))
            .Where(t => t != Skills.SkillType.None && t != Skills.SkillType.All).ToArray();

        public static IEnumerable<Skills.SkillType> Types() => Vanilla;

        public static float Progress(Humanoid c, Skills.SkillType type)
        {
            if (!Of(c).TryGetValue(type, out Level l) || l.Value >= 100f) return 0f;
            float need = Mathf.Pow(Mathf.Floor(l.Value + 1f), 1.5f) * 0.5f + 0.5f;
            return Mathf.Clamp01(l.Progress / need);
        }

        public static IEnumerable<KeyValuePair<Skills.SkillType, float>> All(Humanoid c) =>
            Of(c).Where(kv => kv.Value.Value >= 1f).Select(kv => new KeyValuePair<Skills.SkillType, float>(kv.Key, kv.Value.Value)).OrderByDescending(kv => kv.Value);

        public static string Text(Humanoid c) => Write(Of(c));

        private static readonly Dictionary<Humanoid, float> NextSave = Cleanup.Track(new Dictionary<Humanoid, float>());

        public static void Raise(Humanoid c, Skills.SkillType type, float factor)
        {
            if (type == Skills.SkillType.None || Companion.Zdo(c) == null || !c.GetComponent<ZNetView>().IsOwner()) return;
            Dictionary<Skills.SkillType, Level> map = Of(c);
            if (!map.TryGetValue(type, out Level l)) map[type] = l = new Level { Value = MainConfig.startingSkill.Value };
            if (l.Value >= 100f) return;
            l.Progress += (Def(type)?.m_increseStep ?? 1f) * factor * Game.m_skillGainRate;
            float need = Mathf.Pow(Mathf.Floor(l.Value + 1f), 1.5f) * 0.5f + 0.5f;
            bool level = l.Progress >= need;
            if (level)
            {
                l.Value = Mathf.Min(100f, l.Value + 1f);
                l.Progress = 0f;
                string skill = "$skill_" + type.ToString().ToLower();
                Brain.Get(c)?.Remember(Words.P("villagepleb_life_skill_level", skill, (int)l.Value));
                Journal.Skill(c, skill, (int)l.Value);
                if (Companion.Master(c) == Player.m_localPlayer) VillagePlebsPlugin.Tell(Words.P("villagepleb_life_skill_tell", Companion.NameOf(c), skill, (int)l.Value));
            }
            if (!level && NextSave.TryGetValue(c, out float next) && Time.time < next) return;
            NextSave[c] = Time.time + 10f;
            string text = Write(map);
            Companion.Zdo(c).Set(Key, text);
            Seen[c] = text;
        }

        public static string AfterDeath(string text)
        {
            Dictionary<Skills.SkillType, Level> map = Parse(text ?? "");
            foreach (Level l in map.Values) { l.Value = Mathf.Max(0f, l.Value - l.Value * 0.05f); l.Progress = 0f; }
            return Write(map);
        }

        private static readonly Dictionary<Humanoid, Dictionary<Skills.SkillType, float>> Timers = Cleanup.Track(new Dictionary<Humanoid, Dictionary<Skills.SkillType, float>>());

        public static void Doing(Humanoid c, Skills.SkillType type, float dt, float amount = 1f)
        {
            if (!Timers.TryGetValue(c, out var t)) Timers[c] = t = new Dictionary<Skills.SkillType, float>();
            t.TryGetValue(type, out float time);
            time += dt;
            if (time > 1f) { time = 0f; Raise(c, type, amount); }
            t[type] = time;
        }

        public static void Forget() { Levels.Clear(); _defs = null; Timers.Clear(); NextSave.Clear(); Seen.Clear(); }
        public static void Forget(Humanoid c) { Levels.Remove(c); Seen.Remove(c); NextSave.Remove(c); }
    }

    internal static class Weather
    {
        private static readonly Dictionary<Humanoid, float> Next = Cleanup.Track(new Dictionary<Humanoid, float>());

        public static void Tick(Humanoid c)
        {
            if (Next.TryGetValue(c, out float t) && Time.time < t) return;
            Next[c] = Time.time + 1f;
            SEMan se = c.GetSEMan();
            Vector3 pos = c.transform.position;
            bool fire = EffectArea.IsPointInsideArea(pos, EffectArea.Type.Heat, 1f) != null;
            Cover.GetCoverForPoint(c.GetCenterPoint(), out float cover, out bool underRoof);
            bool shelter = cover >= 0.8f && underRoof;
            bool freezingAir = EnvMan.IsFreezing(), coldAir = EnvMan.IsCold(), rain = EnvMan.IsWet();
            bool cozy = EffectArea.IsPointInsideArea(pos, EffectArea.Type.WarmCozyArea, 1f) != null;
            bool freezing = freezingAir && !fire && !shelter;
            bool cold = (coldAir && !fire) || (freezingAir && fire && !shelter) || (freezingAir && !fire && shelter);
            HitData.DamageModifier frost = Gear.Modifiers(c).GetModifier(HitData.DamageType.Frost);
            if (frost == HitData.DamageModifier.Resistant || frost == HitData.DamageModifier.VeryResistant || frost == HitData.DamageModifier.SlightlyResistant || cozy) { freezing = false; cold = false; }

            if (rain && !underRoof && !ShieldGenerator.IsInsideShield(pos)) se.AddStatusEffect(SEMan.s_statusEffectWet, true);
            Rest.Update(c, shelter, fire);
            if (shelter) se.AddStatusEffect(SEMan.s_statusEffectShelter); else se.RemoveStatusEffect(SEMan.s_statusEffectShelter);
            if (fire) se.AddStatusEffect(SEMan.s_statusEffectCampFire); else se.RemoveStatusEffect(SEMan.s_statusEffectCampFire);
            if (freezing) { if (!se.RemoveStatusEffect(SEMan.s_statusEffectCold, true)) se.AddStatusEffect(SEMan.s_statusEffectFreezing); }
            else if (cold) { if (!se.RemoveStatusEffect(SEMan.s_statusEffectFreezing, true)) se.AddStatusEffect(SEMan.s_statusEffectCold); }
            else { se.RemoveStatusEffect(SEMan.s_statusEffectCold); se.RemoveStatusEffect(SEMan.s_statusEffectFreezing); }
        }

        public static void Forget() => Next.Clear();
    }

    internal static class Repair
    {
        private static bool Worn(ItemDrop.ItemData i) => i.m_shared.m_useDurability && i.m_shared.m_canBeReparied && i.GetMaxDurability() > 0f && i.m_durability < i.GetMaxDurability() * 0.5f;

        private static bool CanRepairAt(ItemDrop.ItemData item, CraftingStation station)
        {
            Recipe recipe = ObjectDB.instance?.GetRecipe(item);
            if (recipe == null || (recipe.m_craftingStation == null && recipe.m_repairStation == null)) return false;
            bool right = (recipe.m_repairStation != null && recipe.m_repairStation.m_name == station.m_name) || (recipe.m_craftingStation != null && recipe.m_craftingStation.m_name == station.m_name);
            return right && Mathf.Min(station.GetLevel(), 4) >= recipe.m_minStationLevel;
        }

        public static int All(Humanoid me, Vector3 center, float radius)
        {
            List<CraftingStation> near = Upgrades.StationsNear(center, radius + 10f);
            int n = 0;
            foreach (ItemDrop.ItemData item in me.GetInventory().GetAllItems().Where(i => i.m_shared.m_useDurability && i.m_shared.m_canBeReparied && i.GetMaxDurability() > 0f && i.m_durability < i.GetMaxDurability() * 0.9f))
                if (near.Any(s => CanRepairAt(item, s))) { item.m_durability = item.GetMaxDurability(); n++; }
            return n;
        }

        private static bool Scuffed(ItemDrop.ItemData i) => i.m_shared.m_useDurability && i.m_shared.m_canBeReparied && i.GetMaxDurability() > 0f && i.m_durability < i.GetMaxDurability() - 0.5f;

        public static string Order(BrainState st, out bool going)
        {
            Humanoid me = st.Body;
            going = false;
            var worn = me.GetInventory().GetAllItems().Where(Scuffed).ToList();
            if (worn.Count == 0) return Words.P("villagepleb_life_gear_fine");
            CraftingStation station = (GameFields.Stations() ?? new List<CraftingStation>()).Where(s => s != null && Vector3.Distance(s.transform.position, me.transform.position) < 60f && worn.Any(i => CanRepairAt(i, s)))
                                          .OrderBy(s => Vector3.Distance(s.transform.position, me.transform.position)).FirstOrDefault();
            if (station == null) return Words.P("villagepleb_life_no_station");
            st.RepairAt = station;
            st.RepairSince = Time.time;
            st.RepairAll = true;
            st.NextRepairLook = Time.time + 60f;
            going = true;
            return Words.P("villagepleb_life_taking_gear", Words.Low(station.m_name));
        }

        public static bool Tick(BrainState st, Action<Vector3, float, bool> moveTo, Action stop)
        {
            Humanoid me = st.Body;
            if (st.RepairAt == null && Time.time >= st.NextRepairLook)
            {
                st.NextRepairLook = Time.time + 5f;
                var worn = me.GetInventory().GetAllItems().Where(Worn).Where(i => me.IsItemEquiped(i) || Work.IsTool(i)).ToList();
                if (worn.Count == 0) return false;
                List<CraftingStation> all = GameFields.Stations() ?? new List<CraftingStation>();
                st.RepairAt = all.Where(s => s != null && Vector3.Distance(s.transform.position, me.transform.position) < 30f && worn.Any(i => CanRepairAt(i, s)))
                                 .OrderBy(s => Vector3.Distance(s.transform.position, me.transform.position)).FirstOrDefault();
                if (st.RepairAt == null) return false;
                st.RepairSince = Time.time;
            }
            if (st.RepairAt == null) return false;
            CraftingStation station = st.RepairAt;
            if (Time.time - st.RepairSince > 30f) { st.RepairAt = null; return false; }
            float d = Vector3.Distance(station.transform.position, me.transform.position);
            if (d > 2.6f) { moveTo(station.transform.position, 1.8f, d > 8f); Brain.Status(st, Words.P("villagepleb_life_status_going_repair", station.m_name)); return true; }
            stop();
            var fixedItems = new List<string>();
            bool everything = st.RepairAll;
            st.RepairAll = false;
            foreach (ItemDrop.ItemData item in me.GetInventory().GetAllItems().Where(i => Worn(i) || everything && Scuffed(i)).Where(i => CanRepairAt(i, station)))
            {
                Skill.Raise(me, Skills.SkillType.Crafting, 1f - item.m_durability / item.GetMaxDurability());
                item.m_durability = item.GetMaxDurability();
                fixedItems.Add(item.m_shared.m_name);
            }
            if (fixedItems.Count > 0)
            {
                Companion.SaveBag(me);
                station.m_repairItemDoneEffects.Create(station.transform.position, Quaternion.identity);
                st.Remember(Words.P("villagepleb_life_repaired", string.Join(", ", fixedItems), station.m_name));
                VillagePlebsPlugin.Instance?.Note($"{Companion.NameOf(me)} repaired {Localization.instance.Localize(string.Join(", ", fixedItems))}");
            }
            st.RepairAt = null;
            return false;
        }
    }

    internal static class Ride
    {
        private static readonly Dictionary<Chair, Humanoid> Taken = new Dictionary<Chair, Humanoid>();

        public static Ship ShipOf(Player p) => p == null ? null : p.GetStandingOnShip() ?? p.GetControlledShip() ?? (p == Player.m_localPlayer ? Ship.GetLocalShip() : null);

        public static bool Tick(BrainState st, Player master)
        {
            Humanoid me = st.Body;
            Ship ship = Companion.OrderOf(me) == Order.Follow ? ShipOf(master) : null;
            if (ship == null)
            {
                if (ReferenceEquals(st.Riding, null))
                {
                    Rigidbody b = me.GetComponent<Rigidbody>();
                    if (b != null && !b.detectCollisions) b.detectCollisions = true;
                    return false;
                }
                if (st.Riding != null && Time.time - st.RideLastSeen < 1.5f) { Hold(st); return true; }
                Off(st, master);
                return false;
            }
            st.RideLastSeen = Time.time;
            if (st.Riding != ship) Board(st, ship, master);
            Hold(st);
            return true;
        }

        private static void Board(BrainState st, Ship ship, Player master)
        {
            Humanoid me = st.Body;
            if (st.Riding != null) Off(st, null);
            Chair seat = ship.GetComponentsInChildren<Chair>().Where(ch => ch.m_inShip && !ch.IsInUse() && (!Taken.TryGetValue(ch, out Humanoid who) || who == null || who == me))
                             .OrderBy(ch => Vector3.Distance(ch.transform.position, master.transform.position)).FirstOrDefault();
            st.Riding = ship;
            st.Seat = seat;
            st.DeckSpot = ship.transform.InverseTransformPoint(master.transform.position) + new Vector3(-0.8f, 0f, -0.8f);
            if (seat != null) { Taken[seat] = me; GameFields.Anim(me)?.SetBool(seat.m_attachAnimation, true); }
            Rigidbody body = me.GetComponent<Rigidbody>();
            if (body != null) { body.linearVelocity = Vector3.zero; body.isKinematic = true; body.detectCollisions = false; }
            st.Ai.StopMoving();
            st.Remember(Words.P("villagepleb_life_boarded"));
            VillagePlebsPlugin.Instance?.Note($"{Companion.NameOf(me)} boarded {Utils.GetPrefabName(ship.gameObject)}{(seat != null ? " and sat down" : "")}");
        }

        private static void Hold(BrainState st)
        {
            Humanoid me = st.Body;
            if (st.Riding == null) return;
            Transform t = st.Riding.transform;
            Vector3 pos = st.Seat != null ? st.Seat.m_attachPoint.position : t.TransformPoint(st.DeckSpot);
            Quaternion rot = st.Seat != null ? st.Seat.m_attachPoint.rotation : Quaternion.LookRotation(Vector3.ProjectOnPlane(t.forward, Vector3.up), Vector3.up);
            me.transform.SetPositionAndRotation(pos, rot);
            Rigidbody body = me.GetComponent<Rigidbody>();
            if (body != null) { body.position = pos; body.rotation = rot; }
            if (st.Seat != null) GameFields.Anim(me)?.SetBool(st.Seat.m_attachAnimation, true);
            Brain.Status(st, Words.P("villagepleb_life_status_riding"));
        }

        private static void Off(BrainState st, Player master)
        {
            Humanoid me = st.Body;
            if (!ReferenceEquals(st.Seat, null)) { GameFields.Anim(me)?.SetBool(st.Seat.m_attachAnimation, false); Taken.Remove(st.Seat); }
            Rigidbody body = me.GetComponent<Rigidbody>();
            if (body != null) { body.isKinematic = false; body.detectCollisions = true; }
            st.Riding = null;
            st.Seat = null;
            if (master != null && master.IsOnGround()) Brain.TeleportBehind(me, master, "villagepleb_brain_stepped_off_boat");
        }

        public static void Leave(BrainState st) { if (!ReferenceEquals(st.Riding, null)) Off(st, null); }

        public static void Forget()
        {
            foreach (var kv in Taken) if (kv.Value != null) { Rigidbody b = kv.Value.GetComponent<Rigidbody>(); if (b != null) { b.isKinematic = false; b.detectCollisions = true; } }
            Taken.Clear();
        }
    }

    [HarmonyPatch(typeof(Character), nameof(Character.RaiseSkill))]
    internal static class Character_RaiseSkill
    {
        private static bool Prefix(Character __instance, Skills.SkillType skill, float value)
        {
            if (!(__instance is Humanoid h) || !Companion.Is(h)) return true;
            if (h.GetComponent<ZNetView>().IsOwner()) Skill.Raise(h, skill, value);
            return false;
        }
    }

    [HarmonyPatch(typeof(Character), nameof(Character.GetSkillFactor))]
    internal static class Character_GetSkillFactor
    {
        private static void Postfix(Character __instance, Skills.SkillType skill, ref float __result) { if (Companion.Is(__instance)) __result = Skill.Get(__instance, skill) / 100f; }
    }

    [HarmonyPatch(typeof(Character), nameof(Character.GetRandomSkillFactor))]
    internal static class Character_GetRandomSkillFactor
    {
        private static bool Prefix(Character __instance, Skills.SkillType skill, ref float __result)
        {
            if (!Companion.Is(__instance)) return true;
            float mid = Mathf.Lerp(0.4f, 1f, Skill.Get(__instance, skill) / 100f);
            __result = Mathf.Lerp(Mathf.Clamp01(mid - 0.15f), Mathf.Clamp01(mid + 0.15f), UnityEngine.Random.value);
            return false;
        }
    }

    [HarmonyPatch(typeof(Character), nameof(Character.GetSkillLevel))]
    internal static class Character_GetSkillLevel
    {
        private static void Postfix(Character __instance, Skills.SkillType skillType, ref float __result) { if (Companion.Is(__instance)) __result = Skill.Get(__instance, skillType); }
    }

    [HarmonyPatch(typeof(Character), "ApplyArmorDamageMods")]
    internal static class Character_ApplyArmorDamageMods
    {
        private static void Postfix(Character __instance, ref HitData.DamageModifiers mods)
        {
            if (!(__instance is Humanoid h) || !Companion.Is(h)) return;
            foreach (ItemDrop.ItemData item in Companion.Worn(h)) mods.Apply(item.m_shared.m_damageModifiers);
        }
    }
}
