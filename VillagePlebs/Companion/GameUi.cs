using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using MagicMike.ModSettings;
using TMPro;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace VillagePlebs
{
    internal static class GameUi
    {
        private const string CanvasName = "VillagePlebsCanvas";

        internal static Func<bool> IsOpen = () => false;

        internal static TMP_FontAsset Norse => Widgets.Font;
        internal static TMP_FontAsset Serif => Widgets.Font;
        internal static TMP_FontAsset Sans => Widgets.Font;
        private static GameObject _cell;
        private static float _cellSpace = 70f;

        internal static readonly Color Warm = MagicMike.ModSettings.Theme.TextBright, Text = MagicMike.ModSettings.Theme.Text, Dim = MagicMike.ModSettings.Theme.Muted;
        internal static readonly Color Gold = MagicMike.ModSettings.Theme.Accent, Warn = new Color(1f, 0.5f, 0.32f), Good = new Color(0.6f, 0.88f, 0.5f);

        private static Canvas _canvas;

        internal static Transform Canvas
        {
            get
            {
                if (_canvas != null) { Match(); return _canvas.transform; }
                InventoryGui gui = InventoryGui.instance;
                if (gui == null) return null;
                foreach (Transform old in gui.transform.parent.Cast<Transform>().Where(t => t.name == CanvasName).ToList()) Object.Destroy(old.gameObject);
                Canvas like = gui.GetComponentInChildren<Canvas>(true) ?? gui.GetComponentInParent<Canvas>();
                var go = new GameObject(CanvasName, typeof(RectTransform));
                go.transform.SetParent(gui.transform.parent, false);
                _canvas = go.AddComponent<Canvas>();
                _canvas.renderMode = RenderMode.ScreenSpaceOverlay;
                _canvas.sortingOrder = (like != null ? like.sortingOrder : 0) + 5;
                go.AddComponent<CanvasScaler>();
                go.AddComponent<GraphicRaycaster>();
                Match();
                return _canvas.transform;
            }
        }

        private static void Match()
        {
            InventoryGui gui = InventoryGui.instance;
            CanvasScaler mine = _canvas != null ? _canvas.GetComponent<CanvasScaler>() : null;
            CanvasScaler theirs = gui != null ? gui.GetComponentInChildren<CanvasScaler>(true) ?? gui.GetComponentInParent<CanvasScaler>() : null;
            if (mine == null || theirs == null) return;
            mine.uiScaleMode = theirs.uiScaleMode; mine.referenceResolution = theirs.referenceResolution; mine.screenMatchMode = theirs.screenMatchMode;
            mine.matchWidthOrHeight = theirs.matchWidthOrHeight; mine.scaleFactor = theirs.scaleFactor; mine.referencePixelsPerUnit = theirs.referencePixelsPerUnit;
        }

        internal static void Destroy()
        {
            if (_canvas != null) Object.Destroy(_canvas.gameObject);
            _canvas = null;
            _cell = null;
            IsOpen = () => false;
            Fields.Clear();
        }

        internal static Vector2 ScreenSize
        {
            get
            {
                Transform t = Canvas;
                CanvasScaler s = t != null ? t.GetComponent<CanvasScaler>() : null;
                var screen = new Vector2(Screen.width, Screen.height);
                float k = 1f;
                if (s != null && s.uiScaleMode == CanvasScaler.ScaleMode.ConstantPixelSize) k = s.scaleFactor;
                else if (s != null && s.uiScaleMode == CanvasScaler.ScaleMode.ScaleWithScreenSize && s.referenceResolution.x > 0f && s.referenceResolution.y > 0f)
                {
                    float w = screen.x / s.referenceResolution.x, h = screen.y / s.referenceResolution.y;
                    k = s.screenMatchMode == CanvasScaler.ScreenMatchMode.Expand ? Mathf.Min(w, h)
                      : s.screenMatchMode == CanvasScaler.ScreenMatchMode.Shrink ? Mathf.Max(w, h)
                      : Mathf.Pow(2f, Mathf.Lerp(Mathf.Log(w, 2f), Mathf.Log(h, 2f), s.matchWidthOrHeight));
                }
                else if (_canvas != null) k = _canvas.scaleFactor;
                return screen / Mathf.Max(0.01f, k);
            }
        }

        internal static bool Ready()
        {
            if (_cell != null) return true;
            InventoryGui gui = InventoryGui.instance;
            if (gui == null) return false;
            InventoryGrid grid = gui.m_playerGrid;
            if (grid != null) { _cell = grid.m_elementPrefab; _cellSpace = grid.m_elementSpace > 1f ? grid.m_elementSpace : 70f; }
            if (_cell == null) Debug.LogWarning($"{CanvasName}: the inventory's item cell was not found where expected (a game update moved it?)");
            return _cell != null;
        }

        internal static float CellSpace => _cellSpace;

        internal static RectTransform Rect(Transform parent, string name, Vector2 anchor, Vector2 pivot, Vector2 pos, Vector2 size)
        {
            var go = new GameObject(name, typeof(RectTransform));
            var r = (RectTransform)go.transform;
            r.SetParent(parent, false);
            r.anchorMin = r.anchorMax = anchor;
            r.pivot = pivot;
            r.anchoredPosition = pos;
            r.sizeDelta = size;
            return r;
        }

        private static RectTransform At(Component c, Vector2 pos, Vector2 size)
        {
            var r = (RectTransform)c.transform;
            r.anchorMin = r.anchorMax = new Vector2(0f, 1f);
            r.pivot = new Vector2(0f, 1f);
            r.anchoredPosition = pos;
            r.sizeDelta = size;
            return r;
        }

        internal static RectTransform Window(string name, Vector2 size)
        {
            Image panel = Widgets.Box(Canvas, name, MagicMike.ModSettings.Theme.Panel, MagicMike.ModSettings.Theme.PanelOutline, 2f);
            var r = (RectTransform)panel.transform;
            r.anchorMin = r.anchorMax = r.pivot = new Vector2(0.5f, 0.5f);
            r.anchoredPosition = Vector2.zero;
            r.sizeDelta = size;
            return r;
        }

        internal static TMP_Text TitleBar(RectTransform window, string title, Action close)
        {
            Image bar = Widgets.Box(window, "TitleBar", MagicMike.ModSettings.Theme.Header);
            Widgets.Place(bar.gameObject, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(12f, -58f), new Vector2(-12f, -12f));
            Accent(bar.transform);
            TMP_Text t = Widgets.Label(bar.transform, title, 26f, MagicMike.ModSettings.Theme.TextBright, TextAlignmentOptions.Center, bold: true);
            Widgets.Place(t.gameObject, Vector2.zero, Vector2.one, new Vector2(60f, 0f), new Vector2(-60f, 0f));
            if (close != null) CloseButton(bar.transform, close);
            return t;
        }

        internal static Button CloseButton(Transform bar, Action close)
        {
            Button x = Widgets.IconButton<CloseIcon>(bar, 9f);
            Widgets.Place(x.gameObject, new Vector2(1f, 0.5f), new Vector2(1f, 0.5f), new Vector2(-46f, -15f), new Vector2(-12f, 15f));
            x.navigation = new Navigation { mode = Navigation.Mode.None };
            x.onClick.AddListener(() => close());
            return x;
        }

        internal static TMP_Text HeaderBar(Transform parent, string title, Vector2 pos, Vector2 size, float fontSize = 16f)
        {
            RectTransform bar = HeaderBox(parent, pos, size);
            TMP_Text t = Widgets.Label(bar, title, fontSize, MagicMike.ModSettings.Theme.TextBright, TextAlignmentOptions.MidlineLeft, bold: true);
            Widgets.Place(t.gameObject, Vector2.zero, Vector2.one, new Vector2(12f, 0f), new Vector2(-6f, 0f));
            return t;
        }

        internal static RectTransform HeaderBox(Transform parent, Vector2 pos, Vector2 size)
        {
            Image bar = Widgets.Box(parent, "Header", MagicMike.ModSettings.Theme.Header);
            bar.raycastTarget = false;
            RectTransform r = At(bar, pos, size);
            Accent(r);
            return r;
        }

        private static void Accent(Transform bar)
        {
            Image accent = Widgets.Box(bar, "Accent", MagicMike.ModSettings.Theme.Accent);
            accent.raycastTarget = false;
            Widgets.Place(accent.gameObject, new Vector2(0f, 0f), new Vector2(0f, 1f), Vector2.zero, new Vector2(4f, 0f));
        }

        internal static RectTransform Box(Transform parent, Vector2 pos, Vector2 size)
        {
            Image box = Widgets.Box(parent, "Box", MagicMike.ModSettings.Theme.Field, MagicMike.ModSettings.Theme.FieldOutline);
            return At(box, pos, size);
        }

        internal static void Braid(Transform parent, Vector2 pos, float width)
        {
            Image line = Widgets.Box(parent, "Line", MagicMike.ModSettings.Theme.FieldOutline);
            line.raycastTarget = false;
            var r = (RectTransform)line.transform;
            r.anchorMin = r.anchorMax = new Vector2(0.5f, 1f);
            r.pivot = new Vector2(0.5f, 0.5f);
            r.anchoredPosition = pos;
            r.sizeDelta = new Vector2(width, 2f);
        }

        internal static TMP_Text Label(Transform parent, string text, Vector2 pos, Vector2 size, float fontSize, Color color, TMP_FontAsset font = null,
                                       TextAlignmentOptions align = TextAlignmentOptions.TopLeft, Vector2? anchor = null)
        {
            Vector2 a = anchor ?? new Vector2(0f, 1f);
            RectTransform r = Rect(parent, "Text", a, a, pos, size);
            bool tWasOn = r.gameObject.activeSelf;
            r.gameObject.SetActive(false);
            var t = r.gameObject.AddComponent<TextMeshProUGUI>();
            t.font = font ?? Sans;
            r.gameObject.SetActive(tWasOn);
            t.fontSize = fontSize; t.color = color; t.alignment = align; t.richText = true;
            t.textWrappingMode = TextWrappingModes.Normal; t.overflowMode = TextOverflowModes.Overflow;
            t.text = text;
            t.raycastTarget = false;
            return t;
        }

        internal static Button Button(Transform parent, string text, Vector2 pos, Vector2 size, Action click, bool enabled = true, float fontSize = 18f)
        {
            Button b = Make(parent, text, pos, size, fontSize);
            b.interactable = enabled;
            Label(b).color = enabled ? Text : Dim;
            b.onClick.AddListener(() => click());
            return b;
        }

        internal static Button Tab(Transform parent, string text, Vector2 pos, Vector2 size, bool chosen, Action click, float fontSize = 18f)
        {
            Button b = Make(parent, text, pos, size, fontSize);
            if (chosen) b.colors = MagicMike.ModSettings.Theme.Colors(MagicMike.ModSettings.Theme.Selected, MagicMike.ModSettings.Theme.Selected, MagicMike.ModSettings.Theme.Selected, MagicMike.ModSettings.Theme.Selected);
            b.interactable = !chosen;
            Label(b).color = chosen ? Warm : Text;
            b.onClick.AddListener(() => click());
            return b;
        }

        internal static TMP_Text Label(Button b) => b.GetComponentsInChildren<TMP_Text>(true).FirstOrDefault(t => t.gameObject.activeSelf);

        private static Button Make(Transform parent, string text, Vector2 pos, Vector2 size, float fontSize)
        {
            Button b = Widgets.Button(parent, text, fontSize);
            At(b, pos, size);
            b.navigation = new Navigation { mode = Navigation.Mode.None };
            TMP_Text label = Label(b);
            label.enableAutoSizing = true;
            label.fontSizeMax = fontSize;
            label.fontSizeMin = Mathf.Max(10f, fontSize * 0.7f);
            return b;
        }

        internal static Toggle Check(Transform parent, Vector2 pos, float size, bool on, Action<bool> changed)
        {
            Toggle t = Widgets.Toggle(parent);
            At(t, pos, new Vector2(size, size));
            t.navigation = new Navigation { mode = Navigation.Mode.None };
            t.SetIsOnWithoutNotify(on);
            t.onValueChanged.AddListener(v => changed(v));
            return t;
        }

        internal static Image Icon(Transform parent, Vector2 pos, Vector2 size, Sprite sprite)
        {
            var img = Rect(parent, "Icon", new Vector2(0f, 1f), new Vector2(0f, 1f), pos, size).gameObject.AddComponent<Image>();
            img.sprite = sprite;
            img.preserveAspect = true;
            img.raycastTarget = false;
            img.enabled = sprite != null;
            return img;
        }

        internal static float Chip(Transform parent, Vector2 pos, ItemDrop.ItemData item, string amount, float size = 28f, float fontSize = 17f)
        {
            Image icon = Icon(parent, pos, new Vector2(size, size), item.GetIcon());
            icon.raycastTarget = true;
            UITooltip cellTip = _cell != null ? _cell.GetComponent<InventoryElement>()?.m_tooltip : null;
            if (cellTip != null)
            {
                UITooltip tip = icon.gameObject.AddComponent<UITooltip>();
                tip.m_tooltipPrefab = cellTip.m_tooltipPrefab;
                tip.Set(item.m_shared.m_name, item.GetTooltip());
            }
            TMP_Text label = Label(parent, amount, new Vector2(pos.x + size + 3f, pos.y - (size - fontSize * 1.3f) / 2f), new Vector2(80f, size), fontSize, Text);
            label.textWrappingMode = TextWrappingModes.NoWrap;
            float w = label.GetPreferredValues(amount, 200f, 0f).x;
            ((RectTransform)label.transform).sizeDelta = new Vector2(w + 2f, size);
            return size + 3f + w + 12f;
        }

        internal static RectTransform Bar(Transform parent, Vector2 pos, Vector2 size, float fraction, Color fill, string text, float fontSize = 15f)
        {
            RectTransform groove = At(Widgets.Box(parent, "Bar", MagicMike.ModSettings.Theme.Track, MagicMike.ModSettings.Theme.FieldOutline), pos, size);
            RectTransform bar = Rect(groove, "Fill", new Vector2(0f, 0f), new Vector2(0f, 0f), new Vector2(2f, 2f), new Vector2(0f, 0f));
            bar.anchorMax = new Vector2(0f, 1f);
            bar.offsetMax = new Vector2(2f + (size.x - 4f) * Mathf.Clamp01(fraction), -2f);
            var img = bar.gameObject.AddComponent<Image>();
            img.color = new Color(fill.r * 0.8f, fill.g * 0.8f, fill.b * 0.8f, 0.9f);
            img.raycastTarget = false;
            if (!string.IsNullOrEmpty(text))
            {
                foreach (bool shadow in new[] { true, false })
                {
                    TMP_Text t = Label(groove, text, new Vector2(shadow ? 1f : 0f, shadow ? -1f : 0f), size, fontSize, shadow ? new Color(0f, 0f, 0f, 0.85f) : Color.white,
                                       Sans, TextAlignmentOptions.Center);
                    t.textWrappingMode = TextWrappingModes.NoWrap;
                }
            }
            return groove;
        }

        internal static void SetBar(RectTransform bar, float fraction, string text = null, Color? fill = null)
        {
            if (bar == null) return;
            var f = bar.Find("Fill") as RectTransform;
            if (f != null)
            {
                var want = new Vector2(2f + (bar.sizeDelta.x - 4f) * Mathf.Clamp01(fraction), -2f);
                if (f.offsetMax != want) f.offsetMax = want;
                if (fill != null && f.GetComponent<Image>() is Image img)
                {
                    Color c = fill.Value, shade = new Color(c.r * 0.8f, c.g * 0.8f, c.b * 0.8f, 0.9f);
                    if (img.color != shade) img.color = shade;
                }
            }
            if (text != null)
                foreach (TMP_Text t in bar.GetComponentsInChildren<TMP_Text>(true))
                    if (t.text != text) t.text = text;
        }

        internal static RawImage Picture(Transform parent, Vector2 pos, Vector2 size, Texture texture, float inset = 3f)
        {
            RectTransform frame = Box(parent, pos, size);
            frame.name = "Picture";
            RectTransform r = Rect(frame, "Image", Vector2.zero, new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero);
            r.anchorMax = Vector2.one;
            r.offsetMin = new Vector2(inset, inset); r.offsetMax = new Vector2(-inset, -inset);
            var img = r.gameObject.AddComponent<RawImage>();
            img.texture = texture;
            img.enabled = texture != null;
            img.raycastTarget = false;
            return img;
        }

        private static readonly List<TMP_InputField> Fields = new List<TMP_InputField>();

        internal static bool Typing
        {
            get
            {
                if (!IsOpen()) return false;
                foreach (TMP_InputField f in Fields) if (f != null && f.isFocused) return true;
                return false;
            }
        }

        internal static TMP_InputField TextField(Transform parent, Vector2 pos, Vector2 size, string text, int limit, Action<string> changed,
                                                 Action<string> submit = null, float fontSize = 18f, string placeholder = "")
        {
            Fields.RemoveAll(f => f == null);
            TMP_InputField field = Widgets.Input(parent, fontSize, placeholder ?? "");
            At(field, pos, size);
            field.navigation = new Navigation { mode = Navigation.Mode.None };
            field.restoreOriginalTextOnEscape = false;
            field.characterLimit = limit;
            field.SetTextWithoutNotify(text ?? "");
            field.onValueChanged.AddListener(v => changed(v));
            if (submit != null) field.onSubmit.AddListener(v => submit(v));
            Fields.Add(field);
            return field;
        }

        internal static InventoryElement EmptyCell(Transform parent, Vector2 pos)
        {
            GameObject go = Object.Instantiate(_cell, parent, false);
            go.name = "Cell";
            go.SetActive(true);
            foreach (UIDragHandler drag in go.GetComponentsInChildren<UIDragHandler>(true)) Object.Destroy(drag);
            foreach (UIGamePad pad in go.GetComponentsInChildren<UIGamePad>(true)) Object.Destroy(pad);
            var r = (RectTransform)go.transform;
            r.anchorMin = r.anchorMax = new Vector2(0f, 1f);
            r.pivot = new Vector2(0f, 1f);
            r.anchoredPosition = pos;
            InventoryElement e = go.GetComponent<InventoryElement>();
            e.Initialize(0, 0);
            Transform binding = go.transform.Find("binding");
            if (binding != null && binding.GetComponent<TMP_Text>() is TMP_Text b) b.enabled = false;
            e.m_icon.enabled = false;
            e.m_durability.gameObject.SetActive(false);
            e.m_equiped.enabled = e.m_queued.enabled = e.m_noteleport.enabled = e.m_food.enabled = false;
            e.m_quality.enabled = e.m_amount.enabled = false;
            if (e.m_selected != null) e.m_selected.SetActive(false);
            if (e.m_tooltip != null) e.m_tooltip.enabled = false;
            return e;
        }

        internal static Transform HudRoot => Hud.instance != null && Hud.instance.m_rootObject != null ? Hud.instance.m_rootObject.transform : null;

        // A slot that stays put while what is in it changes: filled now and again with Fill, clicks go to whatever is in it then.
        internal static InventoryElement Slot(Transform parent, Vector2 pos, Action click)
        {
            InventoryElement e = EmptyCell(parent, pos);
            UIInputHandler input = e.GetComponentInChildren<UIInputHandler>(true);
            if (input != null && click != null) input.m_onLeftClick = _ => click();
            return e;
        }

        internal static void Fill(InventoryElement e, ItemDrop.ItemData item, string amount = null)
        {
            if (item == null)
            {
                e.m_icon.enabled = false;
                e.m_durability.gameObject.SetActive(false);
                e.m_equiped.enabled = e.m_quality.enabled = e.m_amount.enabled = false;
                if (e.m_tooltip != null) e.m_tooltip.enabled = false;
                return;
            }
            e.m_icon.enabled = true;
            Sprite icon = item.GetIcon();
            if (e.m_icon.sprite != icon) e.m_icon.sprite = icon;
            e.m_icon.color = Color.white;
            bool worn = item.m_shared.m_useDurability && item.m_durability < item.GetMaxDurability();
            if (e.m_durability.gameObject.activeSelf != worn) e.m_durability.gameObject.SetActive(worn);
            if (worn) e.m_durability.SetValue(item.GetDurabilityPercentage());
            e.m_equiped.enabled = item.m_equipped;
            e.m_quality.enabled = amount == null && item.m_shared.m_maxQuality > 1;
            if (e.m_quality.enabled) e.m_quality.text = item.m_quality.ToString();
            e.m_amount.enabled = amount != null || item.m_shared.m_maxStackSize > 1;
            string count = amount ?? $"{item.m_stack}/{item.m_shared.m_maxStackSize}";
            if (e.m_amount.text != count) e.m_amount.text = count;
            if (e.m_tooltip != null) { e.m_tooltip.enabled = true; e.m_tooltip.Set(item.m_shared.m_name, item.GetTooltip()); }
        }

        internal static InventoryElement Cell(Transform parent, Vector2 pos, ItemDrop.ItemData item, string amount, bool selected, Action click)
        {
            GameObject go = Object.Instantiate(_cell, parent, false);
            go.name = "Cell";
            go.SetActive(true);
            foreach (UIDragHandler drag in go.GetComponentsInChildren<UIDragHandler>(true)) Object.Destroy(drag);
            foreach (UIGamePad pad in go.GetComponentsInChildren<UIGamePad>(true)) Object.Destroy(pad);
            var r = (RectTransform)go.transform;
            r.anchorMin = r.anchorMax = new Vector2(0f, 1f);
            r.pivot = new Vector2(0f, 1f);
            r.anchoredPosition = pos;

            InventoryElement e = go.GetComponent<InventoryElement>();
            e.Initialize(0, 0);
            Transform binding = go.transform.Find("binding");
            if (binding != null && binding.GetComponent<TMP_Text>() is TMP_Text b) b.enabled = false;
            e.m_icon.enabled = true;
            e.m_icon.sprite = item.GetIcon();
            e.m_icon.color = Color.white;
            bool worn = item.m_shared.m_useDurability && item.m_durability < item.GetMaxDurability();
            e.m_durability.gameObject.SetActive(worn);
            if (worn) e.m_durability.SetValue(item.GetDurabilityPercentage());
            e.m_equiped.enabled = item.m_equipped;
            e.m_queued.enabled = false;
            e.m_noteleport.enabled = false;
            e.m_food.enabled = false;
            e.m_quality.enabled = amount == null && item.m_shared.m_maxQuality > 1;
            if (e.m_quality.enabled) e.m_quality.text = item.m_quality.ToString();
            e.m_amount.enabled = amount != null || item.m_shared.m_maxStackSize > 1;
            e.m_amount.text = amount ?? $"{item.m_stack}/{item.m_shared.m_maxStackSize}";
            if (e.m_selected != null) e.m_selected.SetActive(selected);
            e.m_tooltip.Set(item.m_shared.m_name, item.GetTooltip());

            UIInputHandler input = go.GetComponentInChildren<UIInputHandler>(true);
            if (input != null && click != null) input.m_onLeftClick = _ => click();
            return e;
        }

        internal static float ScrollSpeed
        {
            get
            {
                InventoryGui gui = InventoryGui.instance;
                ScrollRect like = gui != null ? gui.GetComponentsInChildren<ScrollRect>(true).FirstOrDefault(s => s.content == gui.m_recipeListRoot) : null;
                return like != null && like.scrollSensitivity > 0f ? like.scrollSensitivity : 300f;
            }
        }

        internal static RectTransform Scroll(RectTransform box, float inset)
        {
            RectTransform view = Rect(box, "View", new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(inset, -inset),
                                      box.sizeDelta - new Vector2(inset * 2f + BarWidth + 4f, inset * 2f));
            view.gameObject.AddComponent<RectMask2D>();
            var hit = view.gameObject.AddComponent<Image>();
            hit.color = Color.clear;
            RectTransform content = Rect(view, "Content", new Vector2(0f, 1f), new Vector2(0f, 1f), Vector2.zero, new Vector2(view.sizeDelta.x, 0f));
            ScrollRect scroll = view.gameObject.AddComponent<ScrollRect>();
            scroll.content = content;
            scroll.viewport = view;
            scroll.horizontal = false;
            scroll.movementType = ScrollRect.MovementType.Clamped;
            scroll.scrollSensitivity = ScrollSpeed;
            scroll.inertia = false;
            AddScrollbar(scroll, box, inset);
            return content;
        }

        internal const float BarWidth = 12f;

        internal static Scrollbar AddScrollbar(ScrollRect scroll, RectTransform parent, float inset)
        {
            Image track = Widgets.Box(parent, "Scrollbar", MagicMike.ModSettings.Theme.Track);
            var br = (RectTransform)track.transform;
            br.anchorMin = new Vector2(1f, 0f); br.anchorMax = new Vector2(1f, 1f); br.pivot = new Vector2(1f, 0.5f);
            br.sizeDelta = new Vector2(BarWidth, -inset * 2f);
            br.anchoredPosition = new Vector2(-inset * 0.5f, 0f);
            Scrollbar sb = track.gameObject.AddComponent<Scrollbar>();
            GameObject area = Widgets.Child(track.transform, "Sliding Area");
            Widgets.Fill(area, 2f);
            Image handle = Widgets.Box(area.transform, "Handle", Color.white);
            Widgets.Fill(handle.gameObject);
            sb.handleRect = (RectTransform)handle.transform;
            sb.targetGraphic = handle;
            sb.direction = Scrollbar.Direction.BottomToTop;
            sb.navigation = new Navigation { mode = Navigation.Mode.None };
            sb.colors = MagicMike.ModSettings.Theme.Colors(MagicMike.ModSettings.Theme.ScrollHandle, MagicMike.ModSettings.Theme.Accent, MagicMike.ModSettings.Theme.AccentBright, MagicMike.ModSettings.Theme.Muted);
            scroll.verticalScrollbar = sb;
            scroll.verticalScrollbarVisibility = ScrollRect.ScrollbarVisibility.AutoHide;
            return sb;
        }
    }

    [HarmonyPatch(typeof(PlayerController), "TakeInput")]
    internal static class GameUi_PlayerControllerTakeInput
    {
        private static void Postfix(ref bool __result) { if (GameUi.IsOpen()) __result = false; }
    }

    [HarmonyPatch(typeof(Player), "TakeInput")]
    internal static class GameUi_PlayerTakeInput
    {
        private static void Postfix(ref bool __result) { if (GameUi.IsOpen()) __result = false; }
    }

    [HarmonyPatch(typeof(GameCamera), "UpdateMouseCapture")]
    internal static class GameUi_UpdateMouseCapture
    {
        private static bool Prefix()
        {
            if (!GameUi.IsOpen()) return true;
            ZCursor.LockState = CursorLockMode.None;
            ZCursor.Show();
            return false;
        }
    }

    [HarmonyPatch(typeof(ZInput), nameof(ZInput.GetMouseScrollWheel))]
    internal static class GameUi_ScrollWheel
    {
        private static void Postfix(ref float __result) { if (GameUi.IsOpen()) __result = 0f; }
    }

    [HarmonyPatch(typeof(global::Menu), "Update")]
    internal static class GameUi_EscapeCloses
    {
        internal static Action Close = () => { };

        private static bool Prefix()
        {
            if (!GameUi.IsOpen()) return true;
            if (!(ZInput.GetKeyDown(KeyCode.Escape) || ZInput.GetButtonDown("JoyMenu"))) return true;
            Close();
            return false;
        }
    }

    [HarmonyPatch(typeof(Minimap), nameof(Minimap.InTextInput))]
    internal static class GameUi_InTextInput
    {
        private static void Postfix(ref bool __result) { if (GameUi.Typing) __result = true; }
    }

    [HarmonyPatch(typeof(InventoryGui), "Update")]
    internal static class GameUi_InventoryKey
    {
        private static void Prefix()
        {
            if (!GameUi.IsOpen() || !(ZInput.GetButtonDown("Inventory") || ZInput.GetButtonDown("JoyButtonY"))) return;
            UnityEngine.GameObject selected = UnityEngine.EventSystems.EventSystem.current != null ? UnityEngine.EventSystems.EventSystem.current.currentSelectedGameObject : null;
            TMPro.TMP_InputField field = selected != null ? selected.GetComponent<TMPro.TMP_InputField>() : null;
            bool typing = field != null && field.isFocused;
            ZInput.ResetButtonStatus("Inventory");
            ZInput.ResetButtonStatus("JoyButtonY");
            if (!typing) GameUi_EscapeCloses.Close();
        }
    }
}
