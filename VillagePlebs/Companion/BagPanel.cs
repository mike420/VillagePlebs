using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using static VillagePlebs.Shared.Translations;

namespace VillagePlebs
{
    internal static class BagPanel
    {
        private static readonly AccessTools.FieldRef<InventoryGui, InventoryGrid> ContainerGrid = AccessTools.FieldRefAccess<InventoryGui, InventoryGrid>("m_containerGrid");
        private static readonly AccessTools.FieldRef<InventoryGui, ItemDrop.ItemData> DragItem = AccessTools.FieldRefAccess<InventoryGui, ItemDrop.ItemData>("m_dragItem");
        private static readonly AccessTools.FieldRef<InventoryGui, Container> CurrentContainer = AccessTools.FieldRefAccess<InventoryGui, Container>("m_currentContainer");

        private static RectTransform _panel;
        private static InventoryGrid _grid;
        private static TMP_Text _name, _weight;
        private static Humanoid _shown;
        private static (Vector2 screen, bool crafting, bool chest) _placedFor;
        private static UIGroupHandler _group;
        private static InventoryGui _gui;
        private static readonly AccessTools.FieldRef<InventoryGui, int> ActiveGroup = AccessTools.FieldRefAccess<InventoryGui, int>("m_activeGroup");
        private static readonly System.Reflection.MethodInfo SetActiveGroup = AccessTools.Method(typeof(InventoryGui), "SetActiveGroup", new[] { typeof(int), typeof(bool) });
        private static bool _warned, _saidWhy, _buildWarned;
        private const float Reach = 100f;

        public static void Tick(InventoryGui gui)
        {
            try
            {
                Humanoid c = Pick(gui);
                if (!InventoryGui.IsVisible()) _saidWhy = false;
                else if (c == null && !_saidWhy)
                {
                    _saidWhy = true;
                    string why = WhyNot(gui);
                    if (why != null) VillagePlebsPlugin.Instance?.Note("Companion bag panel not shown: " + why);
                }
                if (c == null) { Hide(); return; }
                if (_panel == null && !Build(gui))
                {
                    if (!_buildWarned) { _buildWarned = true; VillagePlebsPlugin.Instance?.Warn("Companion bag panel: the game's chest panel was not found, so it can't be made"); }
                    return;
                }
                if (!_panel.gameObject.activeSelf) _panel.gameObject.SetActive(true);
                _gui = gui;
                Join(gui);
                if (_shown != c) { _shown = c; _placedFor = default; }
                _grid.UpdateInventory(c.GetInventory(), null, DragItem(gui));
                if (_name != null) _name.text = T("villagepleb_bagpanel_title", Companion.NameOf(c));
                if (_weight != null) _weight.text = $"{Carry.Weight(c):0}/{Carry.Max(c):0}";
                var now = (new Vector2(Screen.width, Screen.height), gui.m_crafting != null && gui.m_crafting.gameObject.activeInHierarchy, ChestOpen(gui));
                if (_placedFor != now) { Place(gui); _placedFor = now; }
            }
            catch (Exception e)
            {
                if (!_warned) { _warned = true; VillagePlebsPlugin.Instance?.Warn("Companion bag panel: " + e); }
                Hide();
            }
        }

        private static Humanoid Pick(InventoryGui gui)
        {
            Player p = Player.m_localPlayer;
            if (p == null || !InventoryGui.IsVisible()) return null;
            Container open = CurrentContainer(gui);
            return Companion.All().Where(c => Companion.IsMine(c, p) && !c.IsDead() && c.GetComponent<ZNetView>().IsOwner() && Vector3.Distance(c.transform.position, p.transform.position) < Reach
                                             && (open == null || open.gameObject != c.gameObject))
                .OrderBy(c => Vector3.Distance(c.transform.position, p.transform.position)).FirstOrDefault();
        }

        private static string WhyNot(InventoryGui gui)
        {
            Player p = Player.m_localPlayer;
            if (p == null) return null;
            Humanoid c = Companion.All().Where(h => Companion.IsMine(h, p)).OrderBy(h => Vector3.Distance(h.transform.position, p.transform.position)).FirstOrDefault();
            float d = c != null ? Vector3.Distance(c.transform.position, p.transform.position) : float.MaxValue;
            if (d > Reach + 50f) return null;
            Container open = CurrentContainer(gui);
            string why = c.IsDead() ? "it is dead" : !c.GetComponent<ZNetView>().IsOwner() ? "another game runs it (its bag can only be changed there)"
                       : d >= Reach ? $"it is further than {Reach:0} m" : open != null && open.gameObject == c.gameObject ? "its bag is the chest you have open" : "unknown";
            return $"your nearest companion, {Companion.NameOf(c)}, is {d:0} m away: {why}";
        }

        private static bool Build(InventoryGui gui)
        {
            InventoryGrid source = ContainerGrid(gui);
            if (gui.m_container == null || source == null) return false;
            GameObject copy = UnityEngine.Object.Instantiate(gui.m_container.gameObject, gui.m_container.parent, false);
            copy.name = "VillagePlebs_Bag";
            _panel = copy.GetComponent<RectTransform>();
            foreach (Button b in copy.GetComponentsInChildren<Button>(true)) UnityEngine.Object.Destroy(b.gameObject);
            _grid = copy.GetComponentInChildren<InventoryGrid>(true);
            if (_grid == null) { UnityEngine.Object.Destroy(copy); _panel = null; return false; }
            if (_grid.m_gridRoot != null) foreach (Transform cell in _grid.m_gridRoot) UnityEngine.Object.Destroy(cell.gameObject);
            _grid.m_onSelected = Wire<Action<InventoryGrid, ItemDrop.ItemData, Vector2i, InventoryGrid.Modifier>>(gui, "OnSelectedItem");
            _grid.m_onReleased = Wire<Action<InventoryGrid, ItemDrop.ItemData, Vector2i>>(gui, "OnReleasedItem");
            _grid.m_onEnter = Wire<Action<InventoryGrid, Vector2i>>(gui, "OnEnterElement");
            _grid.OnSetTouchSelection = Wire<Action<InventoryGrid>>(gui, "SetTouchSelection");
            _grid.CanDropDragOntoItem = Wire<Func<ItemDrop.ItemData, bool>>(gui, "CanDropDragOntoItem") ?? (item => true);
            _grid.m_onRightClick = TakeToYou;
            UIGroupHandler group = _grid.m_uiGroup;
            if (group == null || !group.transform.IsChildOf(copy.transform))
            {
                group = copy.GetComponent<UIGroupHandler>() ?? copy.AddComponent<UIGroupHandler>();
                group.m_groupPriority = source.m_uiGroup != null ? source.m_uiGroup.m_groupPriority : 0;
                _grid.m_uiGroup = group;
            }
            group.SetActive(false);
            _group = group;
            _name = Find(copy, gui.m_container, gui.m_containerName);
            _weight = Find(copy, gui.m_container, gui.m_containerWeight);
            float extra = (Gear.Rows - Gear.BagRows) * _grid.m_elementSpace + InventoryGrid_UpdateGui_Bag.Gap;
            _panel.sizeDelta += new Vector2(0f, extra);
            InventoryGrid_UpdateGui_Bag.Forget();
            copy.AddComponent<BagPanelDrag>();
            copy.SetActive(true);
            VillagePlebsPlugin.Instance?.Note("Companion bag panel made below the inventory");
            return true;
        }

        private static T Wire<T>(InventoryGui gui, string method) where T : Delegate
        {
            System.Reflection.MethodInfo m = AccessTools.Method(typeof(InventoryGui), method);
            return m != null ? (T)Delegate.CreateDelegate(typeof(T), gui, m, false) : null;
        }

        private static TMP_Text Find(GameObject copy, RectTransform original, TMP_Text text)
        {
            if (text == null) return null;
            string path = "";
            for (Transform t = text.transform; t != null && t != original.transform; t = t.parent) path = path.Length == 0 ? t.name : t.name + "/" + path;
            Transform found = copy.transform.Find(path);
            return found != null ? found.GetComponent<TMP_Text>() : null;
        }

        private static void TakeToYou(InventoryGrid grid, ItemDrop.ItemData item, Vector2i pos)
        {
            Player p = Player.m_localPlayer;
            if (item == null || p == null || _shown == null) return;
            if (!Companion.Take(_shown, p, item, out string why) && why != null) p.Message(MessageHud.MessageType.Center, Words.Show(why));
        }

        private static bool ChestOpen(InventoryGui gui) => CurrentContainer(gui) != null && gui.m_container != null && gui.m_container.gameObject.activeInHierarchy;

        private static void Place(InventoryGui gui)
        {
            var player = new Vector3[4];
            gui.m_player.GetWorldCorners(player);
            var mine = new Vector3[4];
            _panel.GetWorldCorners(mine);
            float width = mine[2].x - mine[1].x, height = mine[1].y - mine[0].y;
            if (Saved() is Vector2 spot)
            {
                Vector2 to = Clamp(new Vector2(spot.x * Screen.width, spot.y * Screen.height), width, height);
                _panel.position += new Vector3(to.x, to.y, mine[1].z) - mine[1];
                return;
            }
            float gap = (player[2].x - player[1].x) * 0.13f;
            Rect chest = WorldRect(gui.m_container);
            Vector2 topLeft = new Vector2(chest.xMin, chest.yMax);
            if (ChestOpen(gui))
            {
                topLeft = new Vector2(chest.xMin, chest.yMin - gap * 0.15f);
                if (topLeft.y - height < 0f)
                {
                    topLeft = new Vector2(player[2].x + gap, player[2].y);
                    Rect? craft = gui.m_crafting != null && gui.m_crafting.gameObject.activeInHierarchy ? WorldRect(gui.m_crafting) : (Rect?)null;
                    if (craft is Rect c && topLeft.x + width > c.xMin - gap * 0.3f && topLeft.y - height < c.yMax) topLeft = new Vector2(c.xMin, c.yMin - gap * 0.3f);
                }
            }
            _panel.position += new Vector3(topLeft.x, topLeft.y, mine[1].z) - mine[1];
        }

        private static Vector2? Saved()
        {
            string[] parts = (MainConfig.bagPanelSpot?.Value ?? "").Split(',');
            if (parts.Length != 2) return null;
            var ci = System.Globalization.CultureInfo.InvariantCulture;
            var any = System.Globalization.NumberStyles.Float;
            return float.TryParse(parts[0].Trim(), any, ci, out float x) && float.TryParse(parts[1].Trim(), any, ci, out float y) ? new Vector2(x, y) : (Vector2?)null;
        }

        private static Vector2 Clamp(Vector2 topLeft, float width, float height) =>
            new Vector2(Mathf.Clamp(topLeft.x, 0f, Mathf.Max(0f, Screen.width - width)), Mathf.Clamp(topLeft.y, Mathf.Min(height, Screen.height), Screen.height));

        internal static bool CanDrag(PointerEventData e)
        {
            if (_panel == null || _grid == null || (_gui != null && DragItem(_gui) != null)) return false;
            return !RectTransformUtility.RectangleContainsScreenPoint((RectTransform)_grid.transform, e.pressPosition, e.pressEventCamera);
        }

        internal static void Move(Vector2 delta)
        {
            var mine = new Vector3[4];
            _panel.GetWorldCorners(mine);
            Vector2 to = Clamp(new Vector2(mine[1].x, mine[1].y) + delta, mine[2].x - mine[1].x, mine[1].y - mine[0].y);
            _panel.position += new Vector3(to.x, to.y, mine[1].z) - mine[1];
        }

        internal static void SaveSpot()
        {
            var mine = new Vector3[4];
            _panel.GetWorldCorners(mine);
            var ci = System.Globalization.CultureInfo.InvariantCulture;
            MainConfig.bagPanelSpot.Value = (mine[1].x / Screen.width).ToString("0.####", ci) + ", " + (mine[1].y / Screen.height).ToString("0.####", ci);
        }

        internal static void ResetSpot()
        {
            MainConfig.bagPanelSpot.Value = "";
            _placedFor = default;
        }

        private static Rect WorldRect(RectTransform r)
        {
            var c = new Vector3[4];
            r.GetWorldCorners(c);
            return Rect.MinMaxRect(c[0].x, c[0].y, c[2].x, c[2].y);
        }

        private static void Join(InventoryGui gui)
        {
            if (_group == null || Array.IndexOf(gui.m_uiGroups, _group) >= 0) return;
            gui.m_uiGroups = gui.m_uiGroups.Concat(new[] { _group }).ToArray();
            _group.SetActive(false);
        }

        private static void Leave(InventoryGui gui)
        {
            if (gui == null || ReferenceEquals(_group, null)) return;
            int i = Array.IndexOf(gui.m_uiGroups, _group);
            if (i < 0) return;
            int active = ActiveGroup(gui);
            gui.m_uiGroups = gui.m_uiGroups.Where((g, n) => n != i).ToArray();
            if (_group != null) _group.SetActive(false);
            if (active == i) SetActiveGroup?.Invoke(gui, new object[] { 1, false });
            else if (active > i) ActiveGroup(gui) = active - 1;
        }

        private static void Hide()
        {
            _shown = null;
            Leave(_gui);
            if (_panel != null && _panel.gameObject.activeSelf) _panel.gameObject.SetActive(false);
        }

        public static bool IsBagGrid(InventoryGrid grid) => grid != null && grid == _grid;
        public static Humanoid Shown => _shown;

        public static void Destroy()
        {
            try { Leave(_gui); } catch { }
            if (_panel != null) UnityEngine.Object.Destroy(_panel.gameObject);
            _panel = null; _grid = null; _name = _weight = null; _shown = null; _group = null; _gui = null;
        }
    }

    internal sealed class BagPanelDrag : MonoBehaviour, IBeginDragHandler, IDragHandler, IEndDragHandler, IPointerClickHandler
    {
        private bool _dragging;

        public void OnBeginDrag(PointerEventData e) => _dragging = e.button == PointerEventData.InputButton.Left && BagPanel.CanDrag(e);

        public void OnDrag(PointerEventData e) { if (_dragging) BagPanel.Move(e.delta); }

        public void OnEndDrag(PointerEventData e)
        {
            if (_dragging) BagPanel.SaveSpot();
            _dragging = false;
        }

        public void OnPointerClick(PointerEventData e)
        {
            if (e.button == PointerEventData.InputButton.Right && !e.dragging && BagPanel.CanDrag(e)) BagPanel.ResetSpot();
        }
    }

    [HarmonyPatch(typeof(InventoryGui), "Update")]
    internal static class InventoryGui_Update_Bag
    {
        private static void Postfix(InventoryGui __instance) => BagPanel.Tick(__instance);
    }
}

namespace VillagePlebs
{
    [HarmonyPatch(typeof(InventoryGui), "OnSelectedItem")]
    internal static class InventoryGui_OnSelectedItem_QuickMove
    {
        private static readonly AccessTools.FieldRef<InventoryGui, GameObject> DragGo = AccessTools.FieldRefAccess<InventoryGui, GameObject>("m_dragGo");
        private static readonly AccessTools.FieldRef<InventoryGui, Container> Open = AccessTools.FieldRefAccess<InventoryGui, Container>("m_currentContainer");

        private static bool Prefix(InventoryGui __instance, InventoryGrid grid, ItemDrop.ItemData item, InventoryGrid.Modifier mod)
        {
            Humanoid c = BagPanel.Shown;
            Player p = Player.m_localPlayer;
            if (mod != InventoryGrid.Modifier.Move || item == null || c == null || p == null || Open(__instance) != null || DragGo(__instance) != null) return true;
            string why;
            if (BagPanel.IsBagGrid(grid)) { if (!Companion.Take(c, p, item, out why) && why != null) p.Message(MessageHud.MessageType.Center, Words.Show(why)); return false; }
            if (grid == __instance.m_playerGrid) { if (!Companion.Give(c, p, item, out why) && why != null) p.Message(MessageHud.MessageType.Center, Words.Show(why)); return false; }
            return true;
        }
    }
}

namespace VillagePlebs
{
    [HarmonyPatch(typeof(InventoryGui), "UpdateContainer")]
    internal static class InventoryGui_UpdateContainer_Bag
    {
        internal static bool Inside;
        private static void Prefix() => Inside = true;
        private static void Finalizer() => Inside = false;
    }

    [HarmonyPatch(typeof(InventoryGui), "SetupDragItem")]
    internal static class InventoryGui_SetupDragItem_Bag
    {
        private static readonly AccessTools.FieldRef<InventoryGui, Inventory> DragFrom = AccessTools.FieldRefAccess<InventoryGui, Inventory>("m_dragInventory");

        private static bool Prefix(InventoryGui __instance, ItemDrop.ItemData item)
        {
            if (item != null || !InventoryGui_UpdateContainer_Bag.Inside) return true;
            Inventory from = DragFrom(__instance);
            return !(BagPanel.Shown != null && from != null && from == BagPanel.Shown.GetInventory());
        }
    }
}
