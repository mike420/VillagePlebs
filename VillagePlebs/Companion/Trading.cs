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
    internal static class Trading
    {
        private static Trader.TradeItem _offered;

        private static readonly string[] Names = { "Rádvar", "Sigrid", "Halvar", "Astrid", "Gunnar", "Ingrid", "Leif", "Ragna", "Torvald", "Ylva", "Eirik", "Solveig" };

        public static int MostCompanions => VillagePlebsPlugin.MaxCompanions.Value + VillagePlebsPlugin.MaxBought.Value;

        public static bool ForSale(Player p) =>
            p != null && VillagePlebsPlugin.MaxBought.Value > 0 && Profile.Here(p).Count < MostCompanions;

        public static bool IsOffered(Trader.TradeItem item) => item != null && item == _offered;

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
            if (_offered.m_icon == null)
                _offered.m_icon = Icon() ?? ObjectDB.instance?.GetItemPrefab("ArmorRagsChest")?.GetComponent<ItemDrop>()?.m_itemData.GetIcon();
            _offered.m_price = VillagePlebsPlugin.CompanionPrice.Value;
            int free = VillagePlebsPlugin.MaxCompanions.Value - Profile.Here(p).Count;
            _offered.m_tooltip = T("villagepleb_trade_tooltip") + (free > 0 ? "\n\n<color=orange>" + T("villagepleb_trade_free_left", free) + "</color>" : "");
            return _offered;
        }

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
                    MethodInfo load = Type.GetType("UnityEngine.ImageConversion, UnityEngine.ImageConversionModule")?.GetMethod("LoadImage", new[] { typeof(Texture2D), typeof(byte[]) });
                    if (load == null || !(bool)load.Invoke(null, new object[] { tex, bytes.ToArray() })) return null;
                    return Sprite.Create(tex, new Rect(0, 0, tex.width, tex.height), new Vector2(0.5f, 0.5f));
                }
            }
            catch (Exception e) { VillagePlebsPlugin.Instance?.Warn("Traders' companion icon: " + e.Message); return null; }
        }

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

        public static string FreeName(Player p)
        {
            var taken = new HashSet<string>(Profile.Here(p).Select(pr => pr.Name), StringComparer.OrdinalIgnoreCase);
            string name = Names.FirstOrDefault(n => !taken.Contains(n));
            for (int i = 2; name == null; i++) if (!taken.Contains(Names[0] + " " + i)) name = Names[0] + " " + i;
            return name;
        }
    }

    [HarmonyPatch(typeof(Trader), "GetAvailableItems")]
    internal static class Trader_GetAvailableItems_Companion
    {
        private static void Postfix(Trader __instance, List<Trader.TradeItem> __result)
        {
            if (__result != null && Trading.ForSale(Player.m_localPlayer))
                __result.Add(Trading.Offered(Player.m_localPlayer));
        }
    }

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
