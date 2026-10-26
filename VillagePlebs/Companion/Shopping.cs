using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using HarmonyLib;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using static VillagePlebs.Words;
using Object = UnityEngine.Object;

namespace VillagePlebs
{
    // The Go shopping errand: you tick what to buy from the traders you have found, it pays from the coins in its bag, and it is gone
    // (out of sight, taking no orders) for a quarter of an hour, then turns up again where it left with what it bought.
    internal static class Shopping
    {
        private const string BackKey = "vp_shop_back", ListKey = "vp_shop_list", FromKey = "vp_shop_from";
        private const double Trip = 15 * 60;
        public const string Coins = "$item_coins";

        // The traders' camps by the location the game places them in; any other trader is found by its name in a camp's.
        private static readonly Dictionary<string, string> Camps = new Dictionary<string, string> { ["Haldor"] = "Vendor_BlackForest", ["Hildir"] = "Hildir_camp", ["BogWitch"] = "BogWitch_Camp" };
        private static readonly System.Reflection.MethodInfo Explored = AccessTools.Method(typeof(Minimap), "IsExplored");

        public static bool Away(Component c) => Away(Companion.Zdo(c));
        public static bool Away(ZDO z) => z != null && z.GetLong(BackKey, 0L) != 0L;

        private static double Now => ZNet.instance != null ? ZNet.instance.GetTimeSeconds() : 0.0;

        // ---- the traders you have found ----

        private static List<Trader> _found = new List<Trader>();
        private static float _nextFound;

        public static List<Trader> Found()
        {
            if (Time.time < _nextFound) return _found;
            _nextFound = Time.time + 5f;
            _found = new List<Trader>();
            if (ZNetScene.instance == null || ZoneSystem.instance == null || Minimap.instance == null) return _found;
            var icons = new Dictionary<Vector3, string>();
            try { ZoneSystem.instance.GetLocationIcons(icons); } catch { return _found; }
            foreach (GameObject prefab in ZNetScene.instance.m_prefabs)
            {
                Trader t = prefab != null ? prefab.GetComponent<Trader>() : null;
                if (t == null || t.m_items == null || t.m_items.Count == 0) continue;
                string camp = Camps.TryGetValue(prefab.name, out string c) ? c : prefab.name;
                if (icons.Any(kv => kv.Value.IndexOf(camp, StringComparison.OrdinalIgnoreCase) >= 0 && Seen(kv.Key))) _found.Add(t);
            }
            return _found;
        }

        private static bool Seen(Vector3 at) => Explored == null || (bool)Explored.Invoke(Minimap.instance, new object[] { at });

        public static bool AnyTrader() => Found().Count > 0;

        // What a trader sells now (bosses beaten as the game counts them for its own shop). Never a companion: those are only hired in person.
        public static List<Trader.TradeItem> Wares(Trader t) =>
            t.m_items.Where(i => i != null && i.m_prefab != null && !Trading.IsOffered(i) && (string.IsNullOrEmpty(i.m_requiredGlobalKey) || ZoneSystem.instance.GetGlobalKey(i.m_requiredGlobalKey))).ToList();

        public static int CoinsOf(Humanoid c) => c != null ? c.GetInventory().CountItems(Coins) : 0;

        // ---- the errand ----

        public static string Open(BrainState st)
        {
            Humanoid me = st?.Body;
            if (me == null) return P("villagepleb_errands_not_here");
            if (!AnyTrader()) return P("villagepleb_shop_no_traders");
            if (VillagePlebsPlugin.MenuOpen) VillagePlebsPlugin.CloseFromEscape();
            ShopPanel.Open(me);
            return null;
        }

        // Pays and goes. picks: each a trader's item, bought once (its usual stack).
        public static bool Go(Humanoid c, List<Trader.TradeItem> picks)
        {
            if (c == null || picks.Count == 0 || Away(c)) return false;
            int cost = picks.Sum(i => i.m_price);
            if (CoinsOf(c) < cost) return false;
            BrainState st = Brain.Claim(c, out string why);
            if (st == null) { VillagePlebsPlugin.Tell(why); return false; }
            string list = string.Join(",", picks.Select(i => i.m_prefab.gameObject.name + ":" + i.m_stack.ToString(CultureInfo.InvariantCulture)));
            long back = (long)((Now + Trip) * 1000.0);
            Vector3 from = c.transform.position;
            if (!Companion.Write(c, z => { z.Set(BackKey, back); z.Set(ListKey, list); z.Set(FromKey, from); })) return false;
            c.GetInventory().RemoveItem(Coins, cost);
            Companion.SaveBag(c);
            st.Task = null;
            st.Farm = null;
            Talk.Say(c, P("villagepleb_shop_say_off"));
            Journal.Add(c, P("villagepleb_shop_journal_off", cost));
            Activity.Log(c, $"went shopping: {list} for {cost} coins");
            VillagePlebsPlugin.Tell(P("villagepleb_shop_tell_off", Companion.NameOf(c), cost));
            return true;
        }

        // Its brain, while it is out: nothing else happens, and once the time is up it is back with its shopping.
        public static bool Tick(BrainState st)
        {
            Humanoid me = st.Body;
            ZDO z = Companion.Zdo(me);
            long back = z?.GetLong(BackKey, 0L) ?? 0L;
            if (back == 0L) return false;
            double left = back / 1000.0 - Now;
            if (left > 0.0)
            {
                st.Ai.StopMoving();
                Hide(me, true);
                Brain.Status(st, P("villagepleb_shop_status", Mathf.Max(1, Mathf.CeilToInt((float)left / 60f))));
                return true;
            }
            Return(st);
            return false;
        }

        private static void Return(BrainState st)
        {
            Humanoid me = st.Body;
            ZDO z = Companion.Zdo(me);
            string list = z.GetString(ListKey, "");
            Vector3 from = z.GetVec3(FromKey, me.transform.position);
            if (!Companion.Write(me, w => { w.Set(BackKey, 0L); w.Set(ListKey, ""); })) return;
            me.transform.position = from;
            if (me.GetComponent<Rigidbody>() is Rigidbody b) { b.position = from; b.linearVelocity = Vector3.zero; }
            Hide(me, false);
            var got = new List<string>();
            foreach (string part in list.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries))
            {
                string[] f = part.Split(':');
                GameObject prefab = ZNetScene.instance?.GetPrefab(f[0]);
                ItemDrop drop = prefab != null ? prefab.GetComponent<ItemDrop>() : null;
                if (drop == null || f.Length < 2 || !int.TryParse(f[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out int n) || n <= 0) continue;
                // Into its bag; what does not fit is set down at its feet.
                int left = n;
                while (left > 0)
                {
                    int one = Mathf.Min(left, Mathf.Max(1, drop.m_itemData.m_shared.m_maxStackSize));
                    if (!me.GetInventory().AddItem(prefab, one))
                    {
                        ItemDrop.ItemData copy = drop.m_itemData.Clone();
                        copy.m_dropPrefab = prefab;
                        ItemDrop.DropItem(copy, one, from + me.transform.forward * 0.8f + Vector3.up * 0.5f, Quaternion.identity);
                    }
                    left -= one;
                }
                got.Add(n > 1 ? $"{n} {drop.m_itemData.m_shared.m_name}" : drop.m_itemData.m_shared.m_name);
            }
            Companion.SaveBag(me);
            string what = got.Count > 0 ? string.Join(", ", got) : P("villagepleb_shop_nothing");
            Talk.Say(me, P("villagepleb_shop_say_back", what));
            Journal.Add(me, P("villagepleb_shop_journal_back", what));
            Activity.Log(me, "back from shopping with " + Localization.instance.Localize(what));
            Player p = Player.m_localPlayer;
            if (p != null && Companion.MasterId(me) == p.GetPlayerID()) VillagePlebsPlugin.Tell(P("villagepleb_shop_tell_back", Companion.NameOf(me), what));
        }

        // ---- out of sight while it is away (on every game: the brain only runs on one) ----

        private static readonly Dictionary<Humanoid, List<Renderer>> HiddenLooks = new Dictionary<Humanoid, List<Renderer>>();
        private static readonly Dictionary<Humanoid, List<Collider>> HiddenBody = new Dictionary<Humanoid, List<Collider>>();
        private static float _nextLook;

        // Whether each companion was away when this game last looked: one that turns up already away (you just came near) vanishes
        // without a puff.
        private static readonly Dictionary<Humanoid, bool> Was = new Dictionary<Humanoid, bool>();
        private static EffectList _puff;

        // The game's own sounds and dust where it goes and where it comes back.
        private static void Puff(Vector3 at)
        {
            if (ZNetScene.instance == null) return;
            if (_puff == null)
                _puff = new EffectList
                {
                    m_effectPrefabs = new[] { "sfx_FireAddFuel", "vfx_SawDust" }.Select(n => ZNetScene.instance.GetPrefab(n)).Where(g => g != null)
                        .Select(g => new EffectList.EffectData { m_prefab = g, m_enabled = true }).ToArray(),
                };
            _puff.Create(at, Quaternion.identity);
        }

        public static void Update()
        {
            ShopPanel.Tick();
            if (Time.time < _nextLook) return;
            _nextLook = Time.time + 0.5f;
            foreach (Humanoid c in Companion.All().ToList())
            {
                bool away = Away(c);
                if (Was.TryGetValue(c, out bool was) && was != away) Puff(c.transform.position);
                Was[c] = away;
                Hide(c, away);
            }
            foreach (Humanoid gone in Was.Keys.Where(k => k == null).ToList()) Was.Remove(gone);
            foreach (Humanoid gone in HiddenLooks.Keys.Where(k => k == null).ToList()) { HiddenLooks.Remove(gone); HiddenBody.Remove(gone); }
        }

        public static bool IsHidden(Character c) => c is Humanoid h && HiddenLooks.ContainsKey(h);

        private static void Hide(Humanoid c, bool hide)
        {
            if (c == null) return;
            if (hide)
            {
                if (!HiddenLooks.TryGetValue(c, out List<Renderer> looks)) HiddenLooks[c] = looks = new List<Renderer>();
                if (!HiddenBody.TryGetValue(c, out List<Collider> body)) HiddenBody[c] = body = new List<Collider>();
                // Again each time: gear it puts on makes new parts.
                foreach (Renderer r in c.GetComponentsInChildren<Renderer>()) if (r.enabled) { r.enabled = false; looks.Add(r); }
                foreach (Collider col in c.GetComponentsInChildren<Collider>()) if (col.enabled) { col.enabled = false; body.Add(col); }
                c.m_aiSkipTarget = true;
                if (c.GetComponent<Rigidbody>() is Rigidbody rb && !rb.isKinematic) { rb.linearVelocity = Vector3.zero; rb.isKinematic = true; }
                // Its name and health bar too, if they were already up over it when it left (a new one is never made while it is away).
                EnemyHud.instance?.RemoveCharacterHud(c);
                return;
            }
            if (!HiddenLooks.ContainsKey(c)) return;
            foreach (Renderer r in HiddenLooks[c]) if (r != null) r.enabled = true;
            if (HiddenBody.TryGetValue(c, out List<Collider> cols)) foreach (Collider col in cols) if (col != null) col.enabled = true;
            HiddenLooks.Remove(c);
            HiddenBody.Remove(c);
            c.m_aiSkipTarget = false;
            if (c.GetComponent<Rigidbody>() is Rigidbody b2) b2.isKinematic = false;
        }

        public static void Forget()
        {
            foreach (Humanoid c in HiddenLooks.Keys.ToList()) Hide(c, false);
            HiddenLooks.Clear();
            HiddenBody.Clear();
        }
    }

    // The panel: what the found traders sell, a tick for each, the coins in its bag and what the ticked things cost.
    internal static class ShopPanel
    {
        private static RectTransform _window;
        private static Humanoid _who;
        private static TMP_Text _coins, _cost;
        private static Button _buy;
        private static readonly List<Trader.TradeItem> Picked = new List<Trader.TradeItem>();
        private static string _shown;

        public static bool IsOpen => _window != null;

        public static void Open(Humanoid c)
        {
            if (!GameUi.Ready()) return;
            Close();
            _who = c;
            Picked.Clear();
            _shown = null;
            Vector2 screen = GameUi.ScreenSize;
            float w = Mathf.Min(760f, screen.x - 80f), h = Mathf.Min(820f, screen.y - 80f);
            _window = GameUi.Window("VillagePlebs_Shop", new Vector2(w, h));
            GameUi.TitleBar(_window, Show(P("villagepleb_shop_title", Companion.NameOf(c))), Close);

            _coins = GameUi.Label(_window, "", new Vector2(20f, -70f), new Vector2(w - 40f, 26f), 18f, GameUi.Warm);
            RectTransform box = GameUi.Box(_window, new Vector2(16f, -102f), new Vector2(w - 32f, h - 102f - 80f));
            RectTransform list = GameUi.Scroll(box, 6f);
            float y = 0f, rowW = list.sizeDelta.x;
            foreach (Trader t in Shopping.Found())
            {
                TMP_Text head = GameUi.Label(list, Localization.instance.Localize(t.m_name), new Vector2(4f, -y), new Vector2(rowW - 8f, 30f), 20f, GameUi.Gold, GameUi.Norse);
                head.fontStyle = FontStyles.Bold;
                y += 34f;
                foreach (Trader.TradeItem item in Shopping.Wares(t))
                {
                    Trader.TradeItem pick = item;
                    GameUi.Check(list, new Vector2(8f, -y - 4f), 26f, false, on => { if (on) Picked.Add(pick); else Picked.Remove(pick); });
                    GameUi.Icon(list, new Vector2(44f, -y), new Vector2(34f, 34f), item.m_prefab.m_itemData.GetIcon());
                    string name = Localization.instance.Localize(item.m_prefab.m_itemData.m_shared.m_name) + (item.m_stack > 1 ? $"  ×{item.m_stack}" : "");
                    GameUi.Label(list, name, new Vector2(88f, -y - 6f), new Vector2(rowW - 220f, 26f), 17f, GameUi.Text);
                    GameUi.Label(list, Show(P("villagepleb_shop_price", item.m_price)), new Vector2(rowW - 130f, -y - 6f), new Vector2(120f, 26f), 17f, GameUi.Warm, null, TextAlignmentOptions.TopRight);
                    y += 40f;
                }
                y += 10f;
            }
            list.sizeDelta = new Vector2(list.sizeDelta.x, y);

            float by = -(h - 64f);
            _cost = GameUi.Label(_window, "", new Vector2(20f, by + 4f), new Vector2(w - 420f, 44f), 18f, GameUi.Text, null, TextAlignmentOptions.MidlineLeft);
            _buy = GameUi.Button(_window, Show(P("villagepleb_shop_buy")), new Vector2(w - 392f, by), new Vector2(200f, 46f), Buy, false, 20f);
            GameUi.Button(_window, Show(P("villagepleb_shop_cancel")), new Vector2(w - 184f, by), new Vector2(168f, 46f), Close, true, 18f);

            GameUi.IsOpen = () => IsOpen;
            GameUi_EscapeCloses.Close = Close;
            Tick();
        }

        public static void Close()
        {
            if (_window != null) Object.Destroy(_window.gameObject);
            _window = null;
            _who = null;
            Picked.Clear();
            GameUi.IsOpen = () => VillagePlebsPlugin.MenuOpen;
            GameUi_EscapeCloses.Close = VillagePlebsPlugin.CloseFromEscape;
        }

        public static void Tick()
        {
            if (!IsOpen) return;
            if (_who == null || _who.IsDead() || Shopping.Away(_who)) { Close(); return; }
            int coins = Shopping.CoinsOf(_who), cost = Picked.Sum(i => i.m_price);
            string name = Companion.NameOf(_who);
            string shown = coins + "|" + cost + "|" + Picked.Count;
            if (shown == _shown) return;
            _shown = shown;
            _coins.text = Show(coins > 0 ? P("villagepleb_shop_coins", name, coins) : P("villagepleb_shop_no_coins", name));
            _coins.color = coins > 0 ? GameUi.Warm : GameUi.Warn;
            bool afford = cost <= coins;
            _cost.text = Show(Picked.Count == 0 ? P("villagepleb_shop_pick") : P(afford ? "villagepleb_shop_cost" : "villagepleb_shop_too_dear", cost, coins));
            _cost.color = afford ? GameUi.Text : GameUi.Warn;
            bool ready = Picked.Count > 0 && afford;
            if (_buy.interactable != ready) { _buy.interactable = ready; GameUi.Label(_buy).color = ready ? GameUi.Text : GameUi.Dim; }
        }

        private static void Buy()
        {
            Humanoid c = _who;
            var picks = Picked.ToList();
            if (Shopping.Go(c, picks)) Close();
        }
    }

    // Out shopping: no health bar or name over where it stood.
    [HarmonyPatch]
    internal static class EnemyHud_ShowHud_Shopping
    {
        private static System.Reflection.MethodBase Target;
        private static bool Prepare() => (Target = AccessTools.Method(typeof(EnemyHud), "ShowHud")) != null;
        private static System.Reflection.MethodBase TargetMethod() => Target;
        private static bool Prefix(object[] __args) => !(__args.Length > 0 && __args[0] is Character c && Shopping.IsHidden(c));
    }
}
