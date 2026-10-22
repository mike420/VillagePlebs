using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using UnityEngine;
using static VillagePlebs.Shared.Translations;

namespace VillagePlebs
{
    /// <summary>
    /// Companions past the free ones (Max Companions, summoned from the menu) are bought from any trader (Haldor, Hildir, the Bog Witch, a
    /// mod's), for Companion Price coins each, up to Max Bought Companions more. Sold while there's room for one: a player who buys one
    /// before summoning the free ones has used up a free one with it (the most anyone has is the two together). The store lists it as a
    /// trade with no item (its icon, name and tooltip are the trade's own). Buying it closes the store and opens the hire card in the
    /// companion menu (its name; Menu.HireCard); nothing is paid until it's hired there (Hire): the companion comes to the player's side,
    /// and only then are the coins taken. Then its Looks tab opens, as after a free summon.
    /// </summary>
    internal static class Trading
    {
        private static Trader.TradeItem _offered;

        // Names offered for a hired one (any can be typed instead)
        private static readonly string[] Names = { "Rádvar", "Sigrid", "Halvar", "Astrid", "Gunnar", "Ingrid", "Leif", "Ragna", "Torvald", "Ylva", "Eirik", "Solveig" };

        /// <summary>The most companions a player can have: the free ones and the bought ones.</summary>
        public static int MostCompanions => VillagePlebsPlugin.MaxCompanions.Value + VillagePlebsPlugin.MaxBought.Value;

        /// <summary>A trader sells one: there's room for another (and any are for sale at all).</summary>
        public static bool ForSale(Player p) =>
            p != null && VillagePlebsPlugin.MaxBought.Value > 0 && Profile.Here(p).Count < MostCompanions;

        public static bool IsOffered(Trader.TradeItem item) => item != null && item == _offered;

        /// <summary>The trade in the store's list (its price as the setting is now).</summary>
        public static Trader.TradeItem Offered(Player p)
        {
            if (_offered == null)
                _offered = new Trader.TradeItem
                {
                    m_stack = 1,
                    m_name = "$villagepleb_trade_name",
                    m_requiredGlobalKey = "",
                    m_buyKey = "",
                    m_incrementKey = "",
                    m_buyPlayerEffects = new EffectList(),
                };
            if (_offered.m_icon == null)   // (a gift box; without it, the rag tunic's: a village pleb's)
                _offered.m_icon = Icon() ?? ObjectDB.instance?.GetItemPrefab("ArmorRagsChest")?.GetComponent<ItemDrop>()?.m_itemData.GetIcon();
            _offered.m_price = VillagePlebsPlugin.CompanionPrice.Value;
            // free ones still to summon: buying now uses one of them up (the most anyone has is the free and bought together)
            int free = VillagePlebsPlugin.MaxCompanions.Value - Profile.Here(p).Count;
            _offered.m_tooltip = T("villagepleb_trade_tooltip") + (free > 0 ? "\n\n<color=orange>" + T("villagepleb_trade_free_left", free) + "</color>" : "");
            return _offered;
        }

        /// <summary>The gift box built into the mod (Assets\gift_box.png). Null when it can't be read.</summary>
        private static Sprite Icon()
        {
            try
            {
                using (Stream stream = typeof(Trading).Assembly.GetManifestResourceStream("gift_box.png"))
                using (var bytes = new MemoryStream())
                {
                    if (stream == null) return null;
                    stream.CopyTo(bytes);
                    var tex = new Texture2D(2, 2, TextureFormat.RGBA32, false) { name = "villagepleb_gift_box" };
                    // (the game's ImageConversion, found as it runs: its module is built for a newer .NET than ours compiles against)
                    MethodInfo load = Type.GetType("UnityEngine.ImageConversion, UnityEngine.ImageConversionModule")?.GetMethod("LoadImage", new[] { typeof(Texture2D), typeof(byte[]) });
                    if (load == null || !(bool)load.Invoke(null, new object[] { tex, bytes.ToArray() })) return null;
                    return Sprite.Create(tex, new Rect(0, 0, tex.width, tex.height), new Vector2(0.5f, 0.5f));
                }
            }
            catch (Exception e) { VillagePlebsPlugin.Instance?.Warn("Traders' companion icon: " + e.Message); return null; }
        }

        /// <summary>
        /// The companion hired (the hire card's Hire): beside the player first, and the coins taken after, so one that can't come costs
        /// nothing. Null: it didn't come (why: what to tell the player, or null when Summon already did).
        /// </summary>
        public static Humanoid Hire(Player p, string name, string coin, int price, out string why)
        {
            why = null;
            if (!ForSale(p)) { why = T("villagepleb_trade_most"); return null; }
            Inventory inv = p.GetInventory();
            if (price > 0 && (string.IsNullOrEmpty(coin) || inv.CountItems(coin) < price)) { why = T("villagepleb_trade_need_coins", price); return null; }
            Humanoid c = Companion.Summon(p, name);
            if (c == null) return null;
            if (price > 0) inv.RemoveItem(coin, price);
            VillagePlebsPlugin.Instance?.Note($"{p.GetPlayerName()} hired the companion {Companion.NameOf(c)} for {price} coins");
            VillagePlebsPlugin.Tell(T("villagepleb_trade_joined", Companion.NameOf(c)));
            return c;
        }

        /// <summary>A name for a new one, not one of the player's companions already has.</summary>
        public static string FreeName(Player p)
        {
            var taken = new HashSet<string>(Profile.Here(p).Select(pr => pr.Name), StringComparer.OrdinalIgnoreCase);
            string name = Names.FirstOrDefault(n => !taken.Contains(n));
            for (int i = 2; name == null; i++) if (!taken.Contains(Names[0] + " " + i)) name = Names[0] + " " + i;
            return name;
        }
    }

    // ---- every trader's list: a companion, while the player can buy one ----

    [HarmonyPatch(typeof(Trader), "GetAvailableItems")]
    internal static class Trader_GetAvailableItems_Companion
    {
        private static void Postfix(Trader __instance, List<Trader.TradeItem> __result)
        {
            if (__result != null && Trading.ForSale(Player.m_localPlayer))
                __result.Add(Trading.Offered(Player.m_localPlayer));
        }
    }

    // ---- buying it: the store closes and the hire card opens; nothing is paid yet ----

    [HarmonyPatch(typeof(StoreGui), "BuySelectedItem")]
    internal static class StoreGui_BuySelectedItem_Companion
    {
        internal static readonly AccessTools.FieldRef<StoreGui, Trader.TradeItem> Selected = AccessTools.FieldRefAccess<StoreGui, Trader.TradeItem>("m_selectedItem");
        internal static readonly Func<StoreGui, Trader.TradeItem, bool> CanAfford =
            AccessTools.MethodDelegate<Func<StoreGui, Trader.TradeItem, bool>>(AccessTools.Method(typeof(StoreGui), "CanAfford"));

        private static bool Prefix(StoreGui __instance)
        {
            Trader.TradeItem item = Selected(__instance);
            if (!Trading.IsOffered(item)) return true;
            Player p = Player.m_localPlayer;
            if (p == null || !CanAfford(__instance, item)) return false;
            if (!Trading.ForSale(p)) { VillagePlebsPlugin.Tell(T("villagepleb_trade_most")); return false; }
            if (__instance.m_coinPrefab == null || VillagePlebsPlugin.Instance == null) return false;
            string coin = __instance.m_coinPrefab.m_itemData.m_shared.m_name;
            __instance.Hide();
            VillagePlebsPlugin.Instance.OpenHire(p, item.m_price, coin);
            return false;
        }
    }

    // ---- its Buy button: the game wants a free inventory slot for anything it sells, but a companion doesn't go in the inventory ----

    [HarmonyPatch(typeof(StoreGui), "UpdateBuyButton")]
    internal static class StoreGui_UpdateBuyButton_Companion
    {
        private static void Postfix(StoreGui __instance)
        {
            Trader.TradeItem item = StoreGui_BuySelectedItem_Companion.Selected(__instance);
            if (!Trading.IsOffered(item) || !StoreGui_BuySelectedItem_Companion.CanAfford(__instance, item)) return;
            __instance.m_buyButton.interactable = true;
            __instance.m_buyButton.GetComponent<UITooltip>().m_text = "";
        }
    }
}
