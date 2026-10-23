using BepInEx;
using BepInEx.Configuration;
using HarmonyLib;
using Jotunn.Extensions;
using Jotunn.Utils;
using System;
using UnityEngine;

namespace VillagePlebs
{
    [BepInPlugin(PluginGUID, PluginName, PluginVersion)]
    [BepInDependency("com.jotunn.jotunn", BepInDependency.DependencyFlags.HardDependency)]
    [BepInDependency("MagicMike.BodyReplacer", BepInDependency.DependencyFlags.SoftDependency)]
    [NetworkCompatibility(CompatibilityLevel.EveryoneMustHaveMod, VersionStrictness.Minor)]
    public partial class VillagePlebsPlugin : BaseUnityPlugin
    {
        public const string PluginGUID = "MagicMike.VillagePlebs";
        public const string PluginName = "VillagePlebs";
        public const string PluginVersion = "1.0.0";
        public const string PluginAuthor = "MagicMike";
        public const string PluginLink = "https://discord.gg/mnr36G6J4x";

        internal static VillagePlebsPlugin Instance;
        internal static ConfigEntry<bool> ShowDecisions;
        internal static ConfigEntry<float> EngageRange, RespawnSeconds, BaseHealth, BaseStamina, StartingSkill, EatBelow, MmoExp;
        internal static ConfigEntry<KeyboardShortcut> MenuKey, CommandKey;
        internal static ConfigEntry<AwayMode> WhileAway;
        internal static ConfigEntry<int> MaxCompanions, MaxBought, CompanionPrice;
        internal static ConfigEntry<string> BagPanelSpot;

        private Harmony _harmony;
        internal Harmony Harmony => _harmony;

        internal void SaveSettings() => Config.Save();

        private void BindConfig()
        {
            ConfigFile cfg = Config;
            MenuKey = cfg.BindConfigInOrder("General", "Menu Key", defaultValue: new KeyboardShortcut(KeyCode.J), "Tap: your companion's menu (or the summon panel). Hold: all your companions near you come with you, or go home. E on a companion opens its menu too.", sectionOrder: true, settingOrder: true);
            BagPanelSpot = cfg.Bind("General", "Bag Panel Position", "", new ConfigDescription("Where the companion bag panel was last dragged to: its top-left corner, as fractions of the screen across and up. Empty: placed by the inventory. Right-click the panel to clear it.", null, new ConfigurationManagerAttributes { Browsable = false }));
            CommandKey = cfg.BindConfigInOrder("General", "Command Key", defaultValue: new KeyboardShortcut(KeyCode.H), "Point at something and press it: an enemy (they attack it), a tree, rock or plant (it works it), your chest (it puts its things in), its tombstone, a free bed (its bed), the ground (it waits there), or the sky (it comes back).", sectionOrder: true, settingOrder: true);
            MaxCompanions = cfg.BindConfigInOrder("Companion", "Max Companions", defaultValue: 1, "How many companions each player can have for free (summoned from the menu). More are bought from traders: Max Bought Companions. Synced from the server.", synced: true, sectionOrder: true, settingOrder: true, acceptableValues: new AcceptableValueRange<int>(1, 10));
            MaxBought = cfg.BindConfigInOrder("Companion", "Max Bought Companions", defaultValue: 2, "How many more companions each player can buy from any trader (Haldor, Hildir, the Bog Witch) on top of their free ones (Max Companions). Buying one lets you name them; the coins are taken when they join you. 0: none for sale. Synced from the server.", synced: true, sectionOrder: true, settingOrder: true, acceptableValues: new AcceptableValueRange<int>(0, 6));
            CompanionPrice = cfg.BindConfigInOrder("Companion", "Companion Price", defaultValue: 500, "What a trader asks for a companion, in coins. Synced from the server.", synced: true, sectionOrder: true, settingOrder: true, acceptableValues: new AcceptableValueRange<int>(0, 10000));
            WhileAway = cfg.BindConfigInOrder("Companion", "While Away", defaultValue: AwayMode.Mild, "What a companion living at home does while nobody is near. It always catches up on its work when you come back. Mild: it also fights off a few creatures and keeps their drops, and never falls. Real: those fights can go badly and it can fall. Off: work only. Synced from the server.", synced: true, sectionOrder: true, settingOrder: true);
            EngageRange = cfg.BindConfigInOrder("Companion", "Fight Range", defaultValue: 12f, "It fights enemies that come this close (in metres) to it or to you. Also in its menu: Orders.", sectionOrder: true, settingOrder: true, acceptableValues: new AcceptableValueRange<float>(4f, 50f));
            ShowDecisions = cfg.BindConfigInOrder("Companion", "Show Decisions", defaultValue: true, "Show what it decides in a fight above its head. Also in its menu: Brain.", sectionOrder: true, settingOrder: true);
            RespawnSeconds = cfg.BindConfigInOrder("Companion", "Respawn Seconds", defaultValue: 30f, "Seconds after falling before it wakes in its bed (or beside you). Synced from the server.", synced: true, sectionOrder: true, settingOrder: true, acceptableValues: new AcceptableValueRange<float>(5f, 600f));
            BaseHealth = cfg.BindConfigInOrder("Companion", "Base Health", defaultValue: 25f, "Its health before food (a player has 25). Synced from the server.", synced: true, sectionOrder: true, settingOrder: true, acceptableValues: new AcceptableValueRange<float>(5f, 500f));
            BaseStamina = cfg.BindConfigInOrder("Companion", "Base Stamina", defaultValue: 75f, "Its stamina before food (a player has 75). Synced from the server.", synced: true, sectionOrder: true, settingOrder: true, acceptableValues: new AcceptableValueRange<float>(10f, 500f));
            EatBelow = cfg.BindConfigInOrder("Companion", "Eat Below Percent", defaultValue: 10f, "It eats a food it is already under again once its time left drops below this percent (the game allows it from 50). Lower saves food: a meal lasts 80% of its time instead of 50%, and its effect weakens a little towards the end. Synced from the server.", synced: true, sectionOrder: true, settingOrder: true, acceptableValues: new AcceptableValueRange<float>(1f, 50f));
            StartingSkill = cfg.BindConfigInOrder("Companion", "Starting Skill", defaultValue: 0f, "The skill level a new companion starts at (a new player: 0). Synced from the server.", synced: true, sectionOrder: true, settingOrder: true, acceptableValues: new AcceptableValueRange<float>(0f, 100f));
            MmoExp = cfg.BindConfigInOrder("EpicMMO", "Companion Exp", defaultValue: 1f, "With EpicMMO System installed: the experience a companion earns from its kills and its work (chopping, mining, picking), as a multiple of what a player gets for them (EpicMMO's RateExp counts too). 0: companions don't level up, and their levels give nothing. Their kills count for their player as a tame's do (EpicMMO's \"Tames give XP on Mob kill\"). Synced from the server.", synced: true, sectionOrder: true, settingOrder: true, acceptableValues: new AcceptableValueRange<float>(0f, 10f));
        }

        private void Awake()
        {
            VillagePlebs.Shared.Translations.Load(this);
            Instance = this;
            BindConfig();
            MagicMike.ModSettings.ModSettings.Init(this);

            _harmony = new Harmony(PluginGUID);
            _harmony.PatchAll();
            if (ZNetScene.instance != null) { Prefab.Register(ZNetScene.instance); StarLevels.Hook(); }
            Net.Start();

            Logger.LogInfo($"{PluginName} {PluginVersion} loaded (menu: {MenuKey.Value}, commands: {CommandKey.Value})");
            if (Player.m_localPlayer != null && Chat.instance != null)
                Chat.instance.AddString("[Mod]", VillagePlebs.Shared.Translations.T("villagepleb_plugin_reloaded", PluginName, PluginVersion), Talker.Type.Normal);
        }

        private static void ReleaseForReload()
        {
            foreach (Humanoid c in System.Linq.Enumerable.ToList(Companion.All()))
            {
                ZNetView view = c != null ? c.GetComponent<ZNetView>() : null;
                if (view == null || !view.IsValid() || !view.IsOwner()) continue;
                try
                {
                    Companion.SaveBag(c);
                    if (c.GetComponentInParent<Ship>() != null) continue;
                    if (c.GetComponent<Rigidbody>() is Rigidbody body) { body.isKinematic = false; body.detectCollisions = true; }
                    if (c.GetComponent<ZSyncAnimation>() is ZSyncAnimation anim)
                    { anim.SetBool("attach_bed", false); anim.SetBool("attach_chair", false); anim.SetBool("emote_sit", false); }
                }
                catch (System.Exception e) { UnityEngine.Debug.LogWarning(PluginName + ": letting a companion go before unloading: " + e.Message); }
            }
        }

        private void OnDestroy()
        {
            CloseMenu();
            Net.Stop();
            VillagePlebs.Shared.ServerCheck.Stop();
            Activity.Flush(true);
            Home.Stop();
            Portraits.Stop();
            ReleaseForReload();
            EpicMmo.Forget();
            Idle.Forget();
            Brain.Forget();
            Stamina.Forget();
            Food.Forget();
            Eitr.Forget();
            Rest.Forget();
            Skill.Forget();
            Weather.Forget();
            Ride.Forget();
            Talk.Forget();
            Goals.Forget();
            Steer.Forget();
            Passing.Forget();
            Defense.Forget();
            Banter.Forget();
            Marks.Forget();
            Words.Forget();
            BagPanel.Destroy();
            Container_Load_Companion.Forget();
            _harmony?.UnpatchSelf();
            Prefab.Unregister();
            DestroyMenuResources();
            VillagePlebs.Shared.Translations.Unload();
            if (Instance == this) Instance = null;
        }

        private void Update()
        {
            VillagePlebs.Shared.Translations.Tick();
            VillagePlebs.Shared.ServerCheck.Tick();
            Player player = Player.m_localPlayer;
            Net.Update(player);
            Passing.Tick();
            if (player == null) { if (MenuOpen) CloseMenu(); return; }
            Companion.KeepOwnership(player);
            Home.Tick(player);
            Portraits.Tick();
            UpdateMenu(player);
            MenuKeyPressed(player);
            if (!MenuOpen && Home.AssignFor == null && !TypingOrBusy() && CommandKey.Value.IsDown()) Pointing.Command(player);
            Marks.Tick();
        }

        private float _keyDownAt = -1f;
        private bool _keyHeld;

        private void MenuKeyPressed(Player player)
        {
            if (MenuOpen) { if (MenuKey.Value.IsDown() && !GameUi.Typing) CloseMenu(); _keyDownAt = -1f; return; }
            if (Home.AssignFor != null || TypingOrBusy()) { _keyDownAt = -1f; return; }
            if (MenuKey.Value.IsDown()) { _keyDownAt = Time.time; _keyHeld = false; return; }
            if (_keyDownAt < 0f) return;
            if (MenuKey.Value.IsPressed())
            {
                if (!_keyHeld && Time.time - _keyDownAt > 0.45f) { _keyHeld = true; Home.ToggleAll(player); }
                return;
            }
            if (!_keyHeld) OpenMenuFor(player, Companion.MineNear(player, 100f));
            _keyDownAt = -1f;
        }

        internal static bool TypingOrBusy() =>
            (Chat.instance != null && Chat.instance.HasFocus()) || Console.IsVisible() || TextInput.IsVisible() || global::Menu.IsVisible() ||
            InventoryGui.IsVisible() || Minimap.IsOpen() || StoreGui.IsVisible() || Hud.IsPieceSelectionVisible();

        internal void Note(string text) => Logger.LogInfo(text);
        internal void Warn(string text) => Logger.LogWarning(text);

        internal static void Tell(string text)
        {
            if (Player.m_localPlayer != null) Player.m_localPlayer.Message(MessageHud.MessageType.TopLeft, Words.Show(text));
        }

        internal static string Mask(string key) =>
            string.IsNullOrEmpty(key) ? "(none)" : (key.Length <= 4 ? "••••" : new string('•', Math.Min(12, key.Length - 4)) + key.Substring(key.Length - 4));
    }
}
