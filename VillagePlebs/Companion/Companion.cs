using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using UnityEngine;
using static VillagePlebs.Shared.Translations;
using static VillagePlebs.Words;

namespace VillagePlebs
{
    public enum Order { Follow, Stay, Guard, Gather }
    public enum Style { Aggressive, Balanced, Defensive, Passive, Auto }

    internal static class Keys
    {
        public const string Master = "vp_master", MasterName = "vp_mastername", Name = "vp_name", Order = "vp_order", Post = "vp_post",
            Style = "vp_style", Retreat = "vp_retreat", Potions = "vp_potions", Protect = "vp_protect", UseJev = "vp_usejev", Status = "vp_status", Id = "vp_id", Kills = "vp_kills",
            Jobs = "vp_jobs", Radius = "vp_radius", HasBed = "vp_hasbed", BedPos = "vp_bedpos";
    }

    internal static class Companion
    {
        private static readonly System.Reflection.FieldInfo HumanoidInventory = AccessTools.Field(typeof(Humanoid), "m_inventory");
        private static readonly AccessTools.FieldRef<Humanoid, ItemDrop.ItemData> Right = AccessTools.FieldRefAccess<Humanoid, ItemDrop.ItemData>("m_rightItem"),
            Left = AccessTools.FieldRefAccess<Humanoid, ItemDrop.ItemData>("m_leftItem"), Helmet = AccessTools.FieldRefAccess<Humanoid, ItemDrop.ItemData>("m_helmetItem"),
            Chest = AccessTools.FieldRefAccess<Humanoid, ItemDrop.ItemData>("m_chestItem"), Legs = AccessTools.FieldRefAccess<Humanoid, ItemDrop.ItemData>("m_legItem"),
            Shoulder = AccessTools.FieldRefAccess<Humanoid, ItemDrop.ItemData>("m_shoulderItem"), Utility = AccessTools.FieldRefAccess<Humanoid, ItemDrop.ItemData>("m_utilityItem"),
            Ammo = AccessTools.FieldRefAccess<Humanoid, ItemDrop.ItemData>("m_ammoItem");

        public static bool Is(Component c)
        {
            if (c == null) return false;
            ZNetView view = c.GetComponent<ZNetView>();
            return view != null && view.IsValid() && view.GetZDO().GetPrefab() == Prefab.Hash;
        }

        public static ZDO Zdo(Component c) { ZNetView v = c != null ? c.GetComponent<ZNetView>() : null; return v != null && v.IsValid() ? v.GetZDO() : null; }

        // Every companion loaded, gathered once a frame however many times it is asked for (a fresh list each time, so one being
        // walked through is never changed under it).
        private static List<Humanoid> _all = new List<Humanoid>();
        private static int _allFrame = -1;
        public static IEnumerable<Humanoid> All()
        {
            if (_allFrame != Time.frameCount) { _allFrame = Time.frameCount; _all = Character.GetAllCharacters().OfType<Humanoid>().Where(Is).ToList(); }
            return _all.Where(c => c != null);
        }

        public static long IdOf(Component c)
        {
            ZDO zdo = Zdo(c);
            if (zdo == null) return 0L;
            long id = zdo.GetLong(Keys.Id, 0L);
            if (id == 0L && c.GetComponent<ZNetView>().IsOwner())
            {
                id = ((long)Random.Range(1, int.MaxValue) << 31) | (long)Random.Range(1, int.MaxValue);
                zdo.Set(Keys.Id, id);
            }
            return id != 0L ? id : zdo.m_uid.ID;
        }

        public static long MasterId(Component c) => Zdo(c)?.GetLong(Keys.Master, 0L) ?? 0L;
        public static bool IsMine(Component c, Player p) => p != null && MasterId(c) == p.GetPlayerID();
        public static string NameOf(Component c) { string n = Zdo(c)?.GetString(Keys.Name, ""); return string.IsNullOrEmpty(n) ? T("villagepleb_companion") : n; }
        public static Order OrderOf(Component c) => (Order)(Zdo(c)?.GetInt(Keys.Order, 0) ?? 0);
        public static Style StyleOf(Component c) => c is Humanoid h ? Following.Effective(h) : Chosen(c);
        public static Style Chosen(Component c) => (Style)(Zdo(c)?.GetInt(Keys.Style, (int)Style.Auto) ?? (int)Style.Auto);
        public static int RetreatOf(Component c) => Zdo(c)?.GetInt(Keys.Retreat, 30) ?? 30;
        public static bool Potions(Component c) => Zdo(c)?.GetBool(Keys.Potions, true) ?? true;
        public static bool Protect(Component c) => Zdo(c)?.GetBool(Keys.Protect, true) ?? true;
        public static string StatusOf(Component c) => Brain.LiveStatus(c as Humanoid) ?? Zdo(c)?.GetString(Keys.Status, "") ?? "";

        public static Player Master(Component c)
        {
            long id = MasterId(c);
            if (id == 0L) return null;
            foreach (Player p in Player.GetAllPlayers()) if (p != null && p.GetPlayerID() == id) return p;
            return null;
        }

        // The companion last chosen on the command wheel or in the menu: both open on it next time while it is still near.
        public static long LastChosen;

        public static Humanoid LastOrNearest(Player p)
        {
            Humanoid last = LastChosen == 0L ? null : All().FirstOrDefault(c => !c.IsDead() && IdOf(c) == LastChosen && IsMine(c, p));
            return last ?? MineNear(p, float.MaxValue);
        }

        public static Humanoid MineNear(Player p, float range) =>
            All().Where(c => IsMine(c, p) && Vector3.Distance(c.transform.position, p.transform.position) < range)
                 .OrderBy(c => Vector3.Distance(c.transform.position, p.transform.position)).FirstOrDefault();

        // Only its own master gives it orders.
        // Yours, and not away by ship (it takes no orders until it is sent somewhere new or home).
        public static bool CanCommand(Component c, Player p) => IsMine(c, p) && !Sailing.Away(c) && !Shopping.Away(c);

        public static bool Write(Humanoid c, System.Action<ZDO> change)
        {
            ZNetView view = c != null ? c.GetComponent<ZNetView>() : null;
            if (view == null || !view.IsValid()) return false;
            Container gear = c.GetComponent<Container>();
            if (!view.IsOwner())
            {
                if (gear != null) { if (!Containers.Take(gear)) return false; }
                else view.ClaimOwnership();
            }
            change(view.GetZDO());
            return true;
        }

        private static float _nextClaim;

        // How far from you your game runs your companions (and, within it, nobody else's game takes them from you).
        public const float RunReach = 100f;

        public static void KeepOwnership(Player p)
        {
            if (Time.time < _nextClaim) return;
            _nextClaim = Time.time + 1f;
            foreach (Humanoid c in All())
            {
                if (!IsMine(c, p)) continue;
                ZNetView view = c.GetComponent<ZNetView>();
                if (view.IsOwner()) continue;
                bool orphan = !Running(view.GetZDO().GetOwner());
                if (!orphan && Vector3.Distance(c.transform.position, p.transform.position) > RunReach) continue;
                Container gear = c.GetComponent<Container>();
                if (gear != null) Containers.Take(gear, orphan);
                else view.ClaimOwnership();
            }
        }

        private static bool Running(long owner)
        {
            if (owner == 0L || ZNet.instance == null) return false;
            if (owner == ZDOMan.GetSessionID() || ZNet.instance.GetPeer(owner) != null) return true;
            foreach (ZNet.PlayerInfo info in ZNet.instance.GetPlayerList()) if (info.m_characterID.UserID == owner) return true;
            return false;
        }

        public static Humanoid Summon(Player p, string name)
        {
            GameObject prefab = Prefab.Get();
            if (prefab == null) { VillagePlebsPlugin.Tell(T("villagepleb_companion_not_made")); return null; }
            if (!VillagePlebs.Shared.ServerCheck.Installed) { VillagePlebsPlugin.Tell(T("villagepleb_server_missing")); return null; }
            Vector3 pos = p.transform.position - p.transform.forward * 2f + p.transform.right * 1f;
            if (ZoneSystem.instance != null && ZoneSystem.instance.GetSolidHeight(pos, out float height)) pos.y = Mathf.Max(pos.y, height);
            GameObject go = Object.Instantiate(prefab, pos, Quaternion.LookRotation(p.transform.forward));
            Humanoid c = go.GetComponent<Humanoid>();
            ZDO zdo = Zdo(c);
            zdo.Set(Keys.Master, p.GetPlayerID());
            zdo.Set(Keys.MasterName, p.GetPlayerName());
            zdo.Set(Keys.Name, string.IsNullOrEmpty(name) ? "Rádvar" : name);
            zdo.Set(Keys.Order, (int)Order.Gather);
            zdo.Set(Keys.Post, pos);
            zdo.Set(Work.SummonSetKey, true);
            zdo.Set(Work.SummonSpotKey, pos);
            zdo.Set(Keys.Style, (int)Style.Auto);
            zdo.Set(Following.MigratedKey, true);
            zdo.Set(Keys.Retreat, 30);
            zdo.Set(Keys.Potions, true);
            zdo.Set(Keys.Protect, true);
            Dress(c);
            Journal.Started(c);
            Journal.Add(c, P("villagepleb_companion_joined", p.GetPlayerName()));
            p.m_customData["vp_name"] = NameOf(c);
            Profile.Save(p, Profile.Of(c));
            VillagePlebsPlugin.Instance?.Note($"{p.GetPlayerName()} summoned the companion {NameOf(c)} at {pos:F0}");
            return c;
        }

        private static void Dress(Humanoid c) => Looks.Randomise(c, true);

        // Released for good: what it carries and wears goes into its own chests (what won't fit, into a crate where it stands),
        // its chests and bed become yours, and it is gone.
        public static bool Release(Humanoid c, Player p, out string why)
        {
            Container gear = c.GetComponent<Container>();
            ZNetView view = c.GetComponent<ZNetView>();
            if (gear != null && !Containers.Take(gear)) { why = P("villagepleb_companion_gear_open"); return false; }
            if (!view.IsOwner()) view.ClaimOwnership();
            Inventory inv = gear != null ? gear.GetInventory() : c.GetInventory();
            int stored = 0;
            foreach (ItemDrop.ItemData item in inv.GetAllItems().ToList())
            {
                item.m_equipped = false;
                foreach (Container chest in Home.Chests(c))
                {
                    if (!chest.GetInventory().CanAddItem(item) || !Containers.Take(chest)) continue;
                    chest.GetInventory().MoveItemToThis(inv, item);
                    stored++;
                    break;
                }
            }
            int left = inv.NrOfItems();
            if (left > 0) Tomb(c, inv, c.transform.position, c.transform.rotation);
            Home.HandOver(c, p);
            VillagePlebsPlugin.Instance?.Note($"The companion {NameOf(c)} was released: {stored} item stacks into its chests, {left} into a crate where it stood");
            VillagePlebsPlugin.Tell(P(left > 0 ? "villagepleb_companion_released_crate" : "villagepleb_companion_released", NameOf(c)));
            if (p != null) Profile.Forget(p, IdOf(c));
            view.Destroy();
            why = null;
            return true;
        }

        public static void SaveBag(Humanoid c)
        {
            if (c != null && c.GetComponent<ZNetView>() is ZNetView v && v.IsValid() && v.IsOwner()) c.GetInventory().m_onChanged?.Invoke();
        }

        // The regular save: only when its bag is not already in the world as it is, since every save goes out to every player.
        public static void SaveBagIfChanged(Humanoid c)
        {
            if (c == null || !(c.GetComponent<ZNetView>() is ZNetView v) || !v.IsValid() || !v.IsOwner()) return;
            var pkg = new ZPackage();
            c.GetInventory().Save(pkg);
            if (pkg.GetBase64() != v.GetZDO().GetString(ZDOVars.s_items, "")) c.GetInventory().m_onChanged?.Invoke();
        }

        public const string KeptKey = "vp_kept";

        public static int DropGear(Humanoid c)
        {
            Container gear = c.GetComponent<Container>();
            Inventory inv = gear != null ? gear.GetInventory() : null;
            if (inv == null || inv.NrOfItems() == 0) return 0;
            int had = inv.NrOfItems(), worn = inv.GetAllItems().Count(Gear.InSlot);
            foreach (ItemDrop.ItemData item in inv.GetAllItems()) item.m_equipped = false;

            Container grave = Tomb(c, inv, c.GetCenterPoint(), c.transform.rotation);

            int dropped = 0;
            foreach (ItemDrop.ItemData item in inv.GetAllItems().ToList())
            {
                ItemDrop.DropItem(item, item.m_stack, c.transform.position + Vector3.up + Random.insideUnitSphere * 0.5f, Quaternion.identity);
                inv.RemoveItem(item);
                dropped++;
            }
            int saved = grave != null ? grave.GetInventory().NrOfItems() : 0;
            VillagePlebsPlugin.Instance?.Note($"{NameOf(c)} fell at {c.transform.position:F0}: {saved} of {had} item stacks in their tombstone ({worn} of them its gear)" + (dropped > 0 ? $", {dropped} dropped beside it" : ""));
            if (saved + dropped < had) VillagePlebsPlugin.Instance?.Warn($"{had - saved - dropped} item stacks of {NameOf(c)} could not be placed");
            return saved;
        }

        public static Container Tomb(Humanoid c, Inventory inv, Vector3 at, Quaternion rot)
        {
            GameObject scenePlayer = ZNetScene.instance.GetPrefab("Player");
            Player playerPrefab = scenePlayer != null ? scenePlayer.GetComponent<Player>() : null;
            GameObject tombPrefab = playerPrefab != null && playerPrefab.m_tombstone != null ? playerPrefab.m_tombstone : ZNetScene.instance.GetPrefab("CargoCrate");
            GameObject tomb = tombPrefab != null ? Object.Instantiate(tombPrefab, at, rot) : null;
            Container grave = tomb != null ? tomb.GetComponent<Container>() : null;
            if (grave == null) return null;
            grave.GetInventory().MoveInventoryToGrave(inv);
            tomb.GetComponent<TombStone>()?.Setup(NameOf(c), MasterId(c));
            ZDO z = grave.GetComponent<ZNetView>().GetZDO();
            z.Set(Net.CrateKey, NameOf(c));
            z.Set(Grave.OfKey, IdOf(c));
            return grave;
        }

        public static int Unpack(Humanoid c, string packed)
        {
            if (string.IsNullOrEmpty(packed)) return 0;
            try
            {
                var temp = new Inventory("kept", null, 8, 4);
                temp.Load(new ZPackage(System.Convert.FromBase64String(packed)));
                int n = 0;
                foreach (ItemDrop.ItemData item in temp.GetAllItems().ToList())
                {
                    item.m_equipped = false;
                    if (c.GetInventory().AddItem(item)) n++;
                    else ItemDrop.DropItem(item, item.m_stack, c.transform.position + Vector3.up, Quaternion.identity);
                }
                SaveBag(c);
                return n;
            }
            catch (System.Exception e) { VillagePlebsPlugin.Instance?.Warn("Could not give the companion its gear back: " + e.Message); return 0; }
        }

        public static void ShareInventory(Container gear)
        {
            Humanoid h = gear.GetComponent<Humanoid>();
            Gear.Grow(gear.GetInventory());
            if (h != null && HumanoidInventory != null) HumanoidInventory.SetValue(h, gear.GetInventory());
        }

        public static KeyValuePair<string, ItemDrop.ItemData>[] Slots(Humanoid h) => new[]
        {
            new KeyValuePair<string, ItemDrop.ItemData>("Weapon", Right(h)), new KeyValuePair<string, ItemDrop.ItemData>("Shield", Left(h)),
            new KeyValuePair<string, ItemDrop.ItemData>("Helmet", Helmet(h)), new KeyValuePair<string, ItemDrop.ItemData>("Chest", Chest(h)),
            new KeyValuePair<string, ItemDrop.ItemData>("Legs", Legs(h)), new KeyValuePair<string, ItemDrop.ItemData>("Cape", Shoulder(h)),
            new KeyValuePair<string, ItemDrop.ItemData>("Ammo", Ammo(h)),
        };

        public static bool Useful(Humanoid h, ItemDrop.ItemData i)
        {
            var t = i.m_shared.m_itemType;
            return IsMelee(i) || IsRanged(i) || t == ItemDrop.ItemData.ItemType.Shield || t == ItemDrop.ItemData.ItemType.Helmet || t == ItemDrop.ItemData.ItemType.Chest
                || t == ItemDrop.ItemData.ItemType.Legs || t == ItemDrop.ItemData.ItemType.Shoulder || t == ItemDrop.ItemData.ItemType.Ammo
                || (t == ItemDrop.ItemData.ItemType.Consumable && i.m_shared.m_consumeStatusEffect is SE_Stats se && (se.m_healthOverTime > 0f || se.m_healthUpFront > 0f) && i.m_shared.m_food <= 0f)
                || Food.IsFood(i);
        }

        public static bool Take(Humanoid c, Player p, ItemDrop.ItemData item, out string why)
        {
            why = null;
            if (!Write(c, _ => { })) { why = P("villagepleb_companion_gear_open"); return false; }
            Inventory mine = p.GetInventory(), its = c.GetInventory();
            if (!its.ContainsItem(item))
            {
                ItemDrop.ItemData now = its.GetItemAt(item.m_gridPos.x, item.m_gridPos.y);
                if (now == null || now.m_shared.m_name != item.m_shared.m_name) return false;
                item = now;
            }
            if (!mine.CanAddItem(item)) { why = P("villagepleb_companion_your_inventory_full"); return false; }
            if (c.IsItemEquiped(item)) c.UnequipItem(item, false);
            item.m_equipped = false;
            mine.MoveItemToThis(its, item);
            Activity.Log(c, $"you took its {Localization.instance.Localize(item.m_shared.m_name)}");
            return true;
        }

        public static bool Give(Humanoid c, Player p, ItemDrop.ItemData item, out string why)
        {
            why = null;
            if (Gear.IsGear(item)) return Gear.Put(c, p, item, out why);
            if (!Write(c, _ => { })) { why = P("villagepleb_companion_gear_open"); return false; }
            Inventory mine = p.GetInventory(), its = c.GetInventory();
            if (!mine.ContainsItem(item)) return false;
            if (!its.CanAddItem(item)) { why = P("villagepleb_companion_bag_full", NameOf(c)); return false; }
            if (p.IsItemEquiped(item)) p.UnequipItem(item, false);
            item.m_equipped = false;
            its.MoveItemToThis(mine, item);
            Activity.Log(c, $"you gave it {Localization.instance.Localize(item.m_shared.m_name)} (into its bag)");
            return true;
        }

        public static IEnumerable<ItemDrop.ItemData> Worn(Humanoid h) =>
            new[] { Right(h), Left(h), Helmet(h), Chest(h), Legs(h), Shoulder(h), Utility(h), Ammo(h) }.Where(i => i != null);

        public static bool IsStaff(ItemDrop.ItemData item) => item != null && item.m_shared.m_attack != null && item.m_shared.m_attack.m_attackType == Attack.AttackType.Projectile
            && string.IsNullOrEmpty(item.m_shared.m_ammoType) && item.m_shared.m_attack.m_attackEitr > 0f && item.m_shared.m_skillType == Skills.SkillType.ElementalMagic;

        public static bool IsRanged(ItemDrop.ItemData item) => IsStaff(item) || item != null && item.m_shared.m_attack != null && item.m_shared.m_attack.m_attackType == Attack.AttackType.Projectile
            && !string.IsNullOrEmpty(item.m_shared.m_ammoType);

        public static bool HasAmmoFor(Humanoid h, ItemDrop.ItemData weapon) => weapon != null && (IsStaff(weapon) ? Eitr.Get(h) >= weapon.m_shared.m_attack.m_attackEitr : h.GetInventory().GetAmmoItem(weapon.m_shared.m_ammoType) != null);

        internal static bool IsMelee(ItemDrop.ItemData item) =>
            item.IsWeapon() && !IsRanged(item) && item.m_shared.m_skillType != Skills.SkillType.Pickaxes && item.m_shared.m_skillType != Skills.SkillType.Unarmed
            && item.m_shared.m_attack != null && item.m_shared.m_attack.m_attackType != Attack.AttackType.Projectile && item.m_shared.m_buildPieces == null;

        public static ItemDrop.ItemData BestMelee(Humanoid h) => h.GetInventory().GetAllItems().Where(IsMelee).OrderByDescending(i => i.GetDamage().GetTotalDamage()).FirstOrDefault();

        public static ItemDrop.ItemData BestMeleeAgainst(Humanoid h, Character target)
        {
            if (target == null) return BestMelee(h);
            HitData.DamageModifiers mods = target.GetDamageModifiers(null);
            return h.GetInventory().GetAllItems().Where(IsMelee).OrderByDescending(i => Effective(i.GetDamage(), mods)).FirstOrDefault();
        }

        private static float Factor(HitData.DamageModifier m) => m switch
        {
            HitData.DamageModifier.Immune => 0f, HitData.DamageModifier.Ignore => 0f, HitData.DamageModifier.VeryResistant => 0.25f,
            HitData.DamageModifier.Resistant => 0.5f, HitData.DamageModifier.Weak => 1.5f, HitData.DamageModifier.VeryWeak => 2f, _ => 1f,
        };

        private static float Effective(HitData.DamageTypes d, HitData.DamageModifiers m) =>
            d.m_blunt * Factor(m.m_blunt) + d.m_slash * Factor(m.m_slash) + d.m_pierce * Factor(m.m_pierce) + d.m_fire * Factor(m.m_fire)
            + d.m_frost * Factor(m.m_frost) + d.m_lightning * Factor(m.m_lightning) + d.m_poison * Factor(m.m_poison) + d.m_spirit * Factor(m.m_spirit)
            + d.m_chop * Factor(m.m_chop) * 0.2f + d.m_pickaxe * Factor(m.m_pickaxe) * 0.2f;
        public static ItemDrop.ItemData BestRanged(Humanoid h) => h.GetInventory().GetAllItems().Where(i => IsRanged(i) && !Fishing.IsRod(i) && HasAmmoFor(h, i)).OrderByDescending(i => i.GetDamage().GetTotalDamage()).FirstOrDefault();

        public static List<ItemDrop.ItemData> HealingPotions(Humanoid h) =>
            h.GetInventory().GetAllItems().Where(i => i.m_shared.m_itemType == ItemDrop.ItemData.ItemType.Consumable && i.m_shared.m_consumeStatusEffect is SE_Stats se
                && (se.m_healthOverTime > 0f || se.m_healthUpFront > 0f) && i.m_shared.m_food <= 0f).ToList();

        // fighting: its shield only comes out in a fight; the rest of the time it stays in its shield slot, out of sight.
        public static void Maintain(Humanoid h, bool wantRanged, ItemDrop.ItemData tool = null, Character against = null, bool fighting = false)
        {
            if (h.InAttack()) return;
            Inventory inv = h.GetInventory();
            if (h.GetComponent<ZNetView>()?.IsOwner() ?? false) Gear.Arrange(h);
            foreach (ItemDrop.ItemData worn in Worn(h).ToList())
                if (!inv.ContainsItem(worn)) h.UnequipItem(worn, false);

            List<ItemDrop.ItemData> items = inv.GetAllItems();
            foreach (ItemDrop.ItemData.ItemType type in new[] { ItemDrop.ItemData.ItemType.Helmet, ItemDrop.ItemData.ItemType.Chest, ItemDrop.ItemData.ItemType.Legs, ItemDrop.ItemData.ItemType.Shoulder, ItemDrop.ItemData.ItemType.Utility })
            {
                ItemDrop.ItemData best = items.Where(i => i.m_shared.m_itemType == type && (type != ItemDrop.ItemData.ItemType.Utility || i.m_shared.m_maxStackSize <= 1))
                                              .OrderByDescending(i => i.GetArmor()).ThenByDescending(i => i.m_quality).FirstOrDefault();
                if (best != null) Equip(h, best);
            }

            // Work tools (rod, cultivator, hammer) go away once it is done with them, even with no weapon to take their place.
            foreach (ItemDrop.ItemData held in new[] { Right(h), Left(h) })
                if (held != null && held != tool && (Fishing.IsRod(held) || Farming.IsCultivator(held) || held.m_shared.m_buildPieces != null)) h.UnequipItem(held, false);

            ItemDrop.ItemData weapon = tool != null && inv.ContainsItem(tool) ? tool : wantRanged ? BestRanged(h) ?? BestMeleeAgainst(h, against) : BestMeleeAgainst(h, against) ?? BestRanged(h);
            if (weapon != null) Equip(h, weapon);
            if (weapon != null && IsRanged(weapon))
            {
                ItemDrop.ItemData ammo = inv.GetAmmoItem(weapon.m_shared.m_ammoType);
                if (ammo != null && ammo.m_shared.m_itemType == ItemDrop.ItemData.ItemType.Ammo) Equip(h, ammo);
            }
            if (fighting && (weapon == null || weapon.m_shared.m_itemType == ItemDrop.ItemData.ItemType.OneHandedWeapon))
            {
                ItemDrop.ItemData shield = BestShield(h);
                if (shield != null) Equip(h, shield);
            }
            else if (!fighting && Shield(h) is ItemDrop.ItemData away) h.UnequipItem(away, false);
        }

        private static void Equip(Humanoid h, ItemDrop.ItemData item)
        {
            if (!h.IsItemEquiped(item)) h.EquipItem(item, false);
            item.m_equipped = false;
        }

        // The shield it takes up in a fight: the best it carries.
        public static ItemDrop.ItemData BestShield(Humanoid h) =>
            h.GetInventory().GetAllItems().Where(i => i.m_shared.m_itemType == ItemDrop.ItemData.ItemType.Shield).OrderByDescending(i => i.m_shared.m_blockPower).FirstOrDefault();

        public static ItemDrop.ItemData Shield(Humanoid h) { ItemDrop.ItemData l = Left(h); return l != null && l.m_shared.m_itemType == ItemDrop.ItemData.ItemType.Shield ? l : null; }

        public static bool CanBlock(Humanoid h) => Shield(h) != null || (Right(h) != null && !IsRanged(Right(h)));

        public static float Armor(Humanoid h) => Worn(h).Where(i => IsArmour(i)).Sum(i => i.GetArmor());

        private static bool IsArmour(ItemDrop.ItemData i)
        {
            var t = i.m_shared.m_itemType;
            return t == ItemDrop.ItemData.ItemType.Helmet || t == ItemDrop.ItemData.ItemType.Chest || t == ItemDrop.ItemData.ItemType.Legs
                || t == ItemDrop.ItemData.ItemType.Shoulder || t == ItemDrop.ItemData.ItemType.Utility;
        }

        public static bool Drink(Humanoid h)
        {
            SEMan seman = h.GetSEMan();
            foreach (ItemDrop.ItemData potion in HealingPotions(h))
            {
                StatusEffect se = potion.m_shared.m_consumeStatusEffect;
                if (seman.HaveStatusEffect(se.NameHash()) || (!string.IsNullOrEmpty(se.m_category) && seman.HaveStatusEffectCategory(se.m_category))) continue;
                seman.AddStatusEffect(se, true);
                h.m_consumeItemEffects.Create(h.transform.position, Quaternion.identity);
                h.GetInventory().RemoveOneItem(potion);
                return true;
            }
            return false;
        }
    }
}
