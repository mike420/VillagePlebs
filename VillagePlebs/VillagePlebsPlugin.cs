using BepInEx;
using HarmonyLib;
using Jotunn.Utils;
using System;
using UnityEngine;

namespace VillagePlebs
{
    [BepInPlugin(PluginGUID, PluginName, PluginVersion)]
    [BepInDependency("com.jotunn.jotunn", BepInDependency.DependencyFlags.HardDependency)]
    [BepInDependency("MagicMike.EinherjarBodies", BepInDependency.DependencyFlags.SoftDependency)]
    [NetworkCompatibility(CompatibilityLevel.EveryoneMustHaveMod, VersionStrictness.Major)]
    public partial class VillagePlebsPlugin : BaseUnityPlugin
    {
        public const string PluginGUID = "MagicMike.VillagePlebs";
        public const string PluginName = "VillagePlebs";
        public const string PluginVersion = "1.0.6";
        public const string PluginAuthor = "MagicMike";
        public const string PluginLink = "https://discord.gg/mnr36G6J4x";

        internal static VillagePlebsPlugin Instance;

        private Harmony _harmony;
        internal Harmony Harmony => _harmony;

        internal void SaveSettings() => Config.Save();

        private void Awake()
        {
            VillagePlebs.Shared.Translations.Load(this);
            Instance = this;
            MainConfig.InitConfigs(Config);
            MagicMike.ModSettings.ModSettings.Init(this);

            _harmony = new Harmony(PluginGUID);
            _harmony.PatchAll();
            if (ZNetScene.instance != null) { Prefab.Register(ZNetScene.instance); StarLevels.Hook(); }
            Net.Start();

            Logger.LogInfo($"{PluginName} {PluginVersion} loaded (menu: {MainConfig.menuKey.Value}, commands: {MainConfig.commandKey.Value})");
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
            Docks.Stop();
            Shopping.Forget();
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
            Loaded.Forget();
            Words.Forget();
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
            Sailing.Update(player, Time.deltaTime);
            Shopping.Update();
            if (player == null) { if (MenuOpen) CloseMenu(); Wheel.Close(); if (SeaMap.IsOpen) SeaMap.Close(); return; }
            Companion.KeepOwnership(player);
            Home.Tick(player);
            UpdateMenu(player);
            Wheel.Tick(player);
            MenuKeyPressed(player);
            if (!MenuOpen && !Wheel.Open && Home.AssignFor == null && !TypingOrBusy() && MainConfig.commandKey.Value.IsDown()) Pointing.Command(player);
            Marks.Tick();
        }

        private float _keyDownAt = -1f;
        private bool _keyHeld;

        private void MenuKeyPressed(Player player)
        {
            if (MenuOpen) { if (MainConfig.menuKey.Value.IsDown() && !GameUi.Typing) CloseMenu(); _keyDownAt = -1f; return; }
            if (Home.AssignFor != null || Wheel.Open || TypingOrBusy()) { _keyDownAt = -1f; return; }
            if (MainConfig.menuKey.Value.IsDown()) { _keyDownAt = Time.time; _keyHeld = false; return; }
            if (_keyDownAt < 0f) return;
            if (MainConfig.menuKey.Value.IsPressed())
            {
                if (!_keyHeld && Time.time - _keyDownAt > 0.45f) { _keyHeld = true; Home.ToggleAll(player); }
                return;
            }
            if (!_keyHeld) OpenMenuFor(player, Companion.LastOrNearest(player));
            _keyDownAt = -1f;
        }

        internal static bool TypingOrBusy() =>
            (Chat.instance != null && Chat.instance.HasFocus()) || Console.IsVisible() || TextInput.IsVisible() || global::Menu.IsVisible() ||
            InventoryGui.IsVisible() || Minimap.IsOpen() || StoreGui.IsVisible() || Hud.IsPieceSelectionVisible() || SeaMap.IsOpen || ShopPanel.IsOpen;

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
