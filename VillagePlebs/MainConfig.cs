using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using BepInEx.Configuration;
using Jotunn.Extensions;
using UnityEngine;

namespace VillagePlebs
{
    internal class MainConfig
    {
        public static ConfigEntry<KeyboardShortcut> menuKey;
        public static ConfigEntry<KeyboardShortcut> commandKey;
        public static ConfigEntry<KeyboardShortcut> assignKey;
        public static ConfigEntry<KeyboardShortcut> wheelKey;
        public static ConfigEntry<int> maxCompanions;
        public static ConfigEntry<int> maxBought;
        public static ConfigEntry<int> companionPrice;
        public static ConfigEntry<AwayMode> whileAway;
        public static ConfigEntry<float> engageRange;
        public static ConfigEntry<bool> showDecisions;
        public static ConfigEntry<float> respawnSeconds;
        public static ConfigEntry<float> baseHealth;
        public static ConfigEntry<float> baseStamina;
        public static ConfigEntry<float> baseCarry;
        public static ConfigEntry<float> eatBelow;
        public static ConfigEntry<float> startingSkill;
        public static ConfigEntry<float> mmoExp;
        public static ConfigEntry<bool> shipDamage;
        public static ConfigEntry<string> skipItems;

        public static void InitConfigs(ConfigFile config)
        {
            menuKey = config.BindConfigInOrder("General", "Menu Key", defaultValue: new KeyboardShortcut(KeyCode.J), "Tap: your companion's menu (or the summon panel). Hold: all your companions near you come with you, or go home. E on a companion opens its menu too.", synced: false, sectionOrder: true, settingOrder: true);
            commandKey = config.BindConfigInOrder("General", "Command Key", defaultValue: new KeyboardShortcut(KeyCode.H), "Point at something and press it: an enemy (they attack it), a tree, rock or plant (it works it), your chest (it puts its things in), a cart (it pulls it), its tombstone, a free bed (its bed), the ground (it waits there), or the sky (it comes back).", synced: false, sectionOrder: true, settingOrder: true);
            assignKey = config.BindConfigInOrder("General", "Assign Key", defaultValue: new KeyboardShortcut(KeyCode.Y), "While choosing a companion's bed and chests (its menu: Home): look at a bed or a chest and press it to give it to the companion, again to take it back.", synced: false, sectionOrder: true, settingOrder: true);
            wheelKey = config.BindConfigInOrder("General", "Wheel Key", defaultValue: new KeyboardShortcut(KeyCode.F, KeyCode.LeftShift), "Opens the command wheel for the companion you look at (or your nearest): its orders and errands round the outside, how it fights inside. Hold it and let go over a choice, or tap it and click; Esc or right-click closes it.", synced: false, sectionOrder: true, settingOrder: true);
            maxCompanions = config.BindConfigInOrder("Companion", "Max Companions", defaultValue: 1, "How many companions each player can have for free (summoned from the menu). More are bought from traders: Max Bought Companions. Synced from the server.", synced: true, sectionOrder: true, settingOrder: true, acceptableValues: new AcceptableValueRange<int>(1, 10));
            maxBought = config.BindConfigInOrder("Companion", "Max Bought Companions", defaultValue: 2, "How many more companions each player can buy from any trader (Haldor, Hildir, the Bog Witch) on top of their free ones (Max Companions). Buying one lets you name them; the coins are taken when they join you. 0: none for sale. Synced from the server.", synced: true, sectionOrder: true, settingOrder: true, acceptableValues: new AcceptableValueRange<int>(0, 6));
            companionPrice = config.BindConfigInOrder("Companion", "Companion Price", defaultValue: 500, "What a trader asks for a companion, in coins. Synced from the server.", synced: true, sectionOrder: true, settingOrder: true, acceptableValues: new AcceptableValueRange<int>(0, 10000));
            whileAway = config.BindConfigInOrder("Companion", "While Away", defaultValue: AwayMode.Mild, "What a companion living at home does while nobody is near. It always catches up on its work when you come back. Mild: it also fights off a few creatures and keeps their drops, and never falls. Real: those fights can go badly and it can fall. Off: work only. Synced from the server.", synced: true, sectionOrder: true, settingOrder: true);
            engageRange = config.BindConfigInOrder("Companion", "Fight Range", defaultValue: 12f, "It fights enemies that come this close (in metres) to it or to you. Also in its menu: Orders.", sectionOrder: true, settingOrder: true, acceptableValues: new AcceptableValueRange<float>(4f, 50f));
            showDecisions = config.BindConfigInOrder("Companion", "Show Decisions", defaultValue: true, "Show what it decides in a fight above its head. Also in its menu: Home.", synced: false, sectionOrder: true, settingOrder: true);
            respawnSeconds = config.BindConfigInOrder("Companion", "Respawn Seconds", defaultValue: 30f, "Seconds after falling before it wakes in its bed (or beside you). Synced from the server.", synced: true, sectionOrder: true, settingOrder: true, acceptableValues: new AcceptableValueRange<float>(5f, 600f));
            baseHealth = config.BindConfigInOrder("Companion", "Base Health", defaultValue: 25f, "Its health before food (a player has 25). Synced from the server.", synced: true, sectionOrder: true, settingOrder: true, acceptableValues: new AcceptableValueRange<float>(5f, 500f));
            baseStamina = config.BindConfigInOrder("Companion", "Base Stamina", defaultValue: 75f, "Its stamina before food (a player has 75). Synced from the server.", synced: true, sectionOrder: true, settingOrder: true, acceptableValues: new AcceptableValueRange<float>(10f, 500f));
            baseCarry = config.BindConfigInOrder("Companion", "Base Carry Weight", defaultValue: 300f, "How much it can carry before what its gear adds, such as a belt (a player carries 300). Synced from the server.", synced: true, sectionOrder: true, settingOrder: true, acceptableValues: new AcceptableValueRange<float>(50f, 3000f));
            eatBelow = config.BindConfigInOrder("Companion", "Eat Below Percent", defaultValue: 10f, "It eats a food it is already under again once its time left drops below this percent (the game allows it from 50). Lower saves food: a meal lasts 80% of its time instead of 50%, and its effect weakens a little towards the end. Synced from the server.", synced: true, sectionOrder: true, settingOrder: true, acceptableValues: new AcceptableValueRange<float>(1f, 50f));
            startingSkill = config.BindConfigInOrder("Companion", "Starting Skill", defaultValue: 0f, "The skill level a new companion starts at (a new player: 0). Synced from the server.", synced: true, sectionOrder: true, settingOrder: true, acceptableValues: new AcceptableValueRange<float>(0f, 100f));
            mmoExp = config.BindConfigInOrder("EpicMMO", "Companion Exp", defaultValue: 1f, "With EpicMMO System installed: the experience a companion earns from its kills and its work (chopping, mining, picking), as a multiple of what a player gets for them (EpicMMO's RateExp counts too). 0: companions don't level up, and their levels give nothing. Their kills count for their player as a tame's do (EpicMMO's \"Tames give XP on Mob kill\"). Synced from the server.", synced: true, sectionOrder: true, settingOrder: true, acceptableValues: new AcceptableValueRange<float>(0f, 10f));

            skipItems = config.BindConfigInOrder("Missions", "Skip Items", defaultValue: "$item_axe_early", "Items companions never make, upgrade or work towards as a mission, separated by commas: the game's name for them ($item_axe_early), their prefab name (AxeFlint) or the name you see (Flint axe). Synced from the server.", synced: true, sectionOrder: true, settingOrder: true);
            shipDamage = config.BindConfigInOrder("Sailing", "Ships Take Damage", defaultValue: false, "Whether a ship a companion has the helm of (sailing, about to, or waiting aboard) can be damaged: by rocks, the sea, capsizing and anything that hits it. Off: it takes none. The Ashlands sea burns it either way. Synced from the server.", synced: true, sectionOrder: true, settingOrder: true);

            CarryOverOldSections(config);

            // Decimal settings rounded (and kept in range) whichever editor sets them
            MagicMike.ModSettings.ConfigRounding.RoundAll(config);
        }

        private static readonly Dictionary<string, string[]> OldSections = new Dictionary<string, string[]>
        {
        };

        private static void CarryOverOldSections(ConfigFile config)
        {
            Dictionary<ConfigDefinition, string> orphans;
            try
            {
                PropertyInfo property = typeof(ConfigFile).GetProperty("OrphanedEntries", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                orphans = property?.GetValue(config) as Dictionary<ConfigDefinition, string>;
            }
            catch (System.Exception e)
            {
                Jotunn.Logger.LogWarning("[VillagePlebs] Could not look for settings under old section names: " + e.Message);
                return;
            }
            if (orphans == null || orphans.Count == 0)
            {
                return;
            }

            int carried = 0, dropped = 0;
            HashSet<ConfigEntryBase> handled = new HashSet<ConfigEntryBase>();
            bool saveOnSet = config.SaveOnConfigSet;
            config.SaveOnConfigSet = false;
            try
            {
                foreach (ConfigDefinition old in orphans.Keys.OrderByDescending(d => SectionNumber(d.Section)).ToList())
                {
                    string oldName = SectionName(old.Section);
                    ConfigEntryBase entry = ((IDictionary<ConfigDefinition, ConfigEntryBase>)config).Values.FirstOrDefault(e => e.Definition.Key == old.Key && e.Definition.Section != old.Section
                        && (SectionName(e.Definition.Section) == oldName || OldSections.TryGetValue(SectionName(e.Definition.Section), out string[] before) && before.Contains(oldName)));
                    if (entry == null)
                    {
                        continue;
                    }
                    if (handled.Add(entry) && Equals(entry.BoxedValue, entry.DefaultValue))
                    {
                        try
                        {
                            entry.SetSerializedValue(orphans[old]);
                            carried++;
                        }
                        catch (System.Exception e)
                        {
                            Jotunn.Logger.LogWarning("[VillagePlebs] Could not carry over " + old.Section + " / " + old.Key + ": " + e.Message);
                        }
                    }
                    orphans.Remove(old);
                    dropped++;
                }
            }
            finally
            {
                config.SaveOnConfigSet = saveOnSet;
            }
            if (dropped > 0)
            {
                config.Save();
                Jotunn.Logger.LogInfo("[VillagePlebs] Carried " + carried + " setting(s) over from old section names in the config file.");
            }
        }

        private static string SectionName(string section) => Regex.Replace(section ?? "", @"^\d+\s*-\s*", "");

        private static int SectionNumber(string section)
        {
            Match match = Regex.Match(section ?? "", @"^(\d+)\s*-");
            return match.Success ? int.Parse(match.Groups[1].Value) : -1;
        }
    }
}
