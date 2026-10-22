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
    /// <summary>
    /// Windows styled like the Mod Settings panel (MagicMike.ModSettings: its Theme colours and Widgets): a flat panel with an outline,
    /// field-coloured boxes, its buttons, tick boxes, text fields and scrollbars, its header bars and its font. The item cells (icons,
    /// durability, quality, tooltips) are still the inventory's own, so an item shows as the game shows it.
    ///
    /// A window with it:
    ///
    ///   internal static class MyWindow
    ///   {
    ///       static RectTransform _window, _list; static string _shown = ""; static float _closedAt = -1f, _openedAt;
    ///       internal static bool IsOpen => _window != null;
    ///
    ///       internal static void Open()                 // from your piece's Interact (return false if Time.unscaledTime < _closedAt + 0.25f)
    ///       {
    ///           if (!GameUi.Ready()) return;
    ///           GameUi.IsOpen = () => IsOpen;           // turns on the input patches below
    ///           if (InventoryGui.IsVisible()) InventoryGui.instance.Hide();
    ///           _openedAt = Time.unscaledTime;
    ///           Build();
    ///       }
    ///       internal static void Close()
    ///       {
    ///           if (_window != null) Object.Destroy(_window.gameObject);
    ///           _window = null; _closedAt = Time.unscaledTime; UITooltip.HideTooltip();
    ///       }
    ///       internal static void Tick()                 // from your plugin's Update
    ///       {
    ///           if (_window == null) return;
    ///           bool useAgain = Time.unscaledTime > _openedAt + 0.2f && ZInput.GetButtonDown("Use");   // E closes it, as it does a chest
    ///           if (Player.m_localPlayer == null || Player.m_localPlayer.IsDead() || Menu.IsVisible() || useAgain || tooFarAway) { Close(); return; }
    ///           if (Signature() != _shown) Build();     // build again only when what it shows changed
    ///       }
    ///       static void Build()
    ///       {
    ///           float scrolled = _list != null ? _list.anchoredPosition.y : 0f;
    ///           if (_window != null) Object.Destroy(_window.gameObject);
    ///           _shown = Signature();
    ///           _window = GameUi.Window("MyWindow", new Vector2(860f, 580f));
    ///           GameUi.TitleBar(_window, "Title", Close);
    ///           RectTransform box = GameUi.Box(_window, new Vector2(24f, -84f), new Vector2(470f, 466f));
    ///           _list = GameUi.Scroll(box, 8f);
    ///           ... GameUi.Cell(_list, position, item, null, item == selected, () => Select(item)) for each item; then size _list ...
    ///           GameUi.Button(_window, "Do it", new Vector2(510f, -502f), new Vector2(320f, 48f), () => pending = true);
    ///           _list.anchoredPosition = new Vector2(0f, scrolled);   // keep the scroll position
    ///       }
    ///   }
    ///
    /// Never change the player's inventory from a button's click: set a flag and do it in Tick (the window is being drawn).
    /// In your plugin's OnDestroy: MyWindow.Close(); GameUi.Destroy(); (a hot reload must not leave the window or canvas behind).
    /// </summary>
    internal static class GameUi
    {
        /// <summary>Your canvas's name: change it to your mod's (a hot reload finds and removes the old copy's by this name).</summary>
        private const string CanvasName = "VillagePlebsCanvas";

        /// <summary>Whether one of your windows is open: the input patches below act only while it is.</summary>
        internal static Func<bool> IsOpen = () => false;

        /// <summary>The fonts: all the Mod Settings panel's (Averia); kept as three so callers can still say which they mean.</summary>
        internal static TMP_FontAsset Norse => Widgets.Font;
        internal static TMP_FontAsset Serif => Widgets.Font;
        internal static TMP_FontAsset Sans => Widgets.Font;
        private static GameObject _cell;
        private static float _cellSpace = 70f;

        // The Mod Settings panel's colours: titles, text, dim notes, gold (picked, lit); warnings and good news keep their own
        internal static readonly Color Warm = MagicMike.ModSettings.Theme.TextBright, Text = MagicMike.ModSettings.Theme.Text, Dim = MagicMike.ModSettings.Theme.Muted;
        internal static readonly Color Gold = MagicMike.ModSettings.Theme.Accent, Warn = new Color(1f, 0.5f, 0.32f), Good = new Color(0.6f, 0.88f, 0.5f);

        private static Canvas _canvas;

        /// <summary>Your own screen layer, made like the inventory's (same scaling, so the player's interface scale applies), drawn above it.</summary>
        internal static Transform Canvas
        {
            get
            {
                if (_canvas != null) { Match(); return _canvas.transform; }
                InventoryGui gui = InventoryGui.instance;
                if (gui == null) return null;
                // a hot reload leaves the old copy's canvas behind (its types differ from ours): find it by name
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

        /// <summary>Every CanvasScaler field from the inventory's canvas (the game changes scaleFactor when the player changes the GUI scale).</summary>
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

        /// <summary>
        /// The screen's size in the canvas's units (what the player's interface scale leaves for a window): size a tall window to fit it,
        /// Mathf.Min(wanted, ScreenSize.y - 40). Worked out from the scaler's settings, so it is right on the frame the canvas is made.
        /// </summary>
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

        /// <summary>
        /// Finds the inventory's item cell (the one part still the game's own), under InventoryGui. The game makes it again on every world
        /// change, so it is looked up again then (Unity's null check notices the old one is gone). False, with a warning in the log, if a
        /// game update moved it.
        /// </summary>
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

        /// <summary>The distance between two of the inventory's cells (a cell is about 64 wide): lay grids out by it.</summary>
        internal static float CellSpace => _cellSpace;

        // ---- building blocks ----------------------------------------------------------------------------------------------------

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

        /// <summary>Puts something made by Widgets at a top-left position and size, as Rect does.</summary>
        private static RectTransform At(Component c, Vector2 pos, Vector2 size)
        {
            var r = (RectTransform)c.transform;
            r.anchorMin = r.anchorMax = new Vector2(0f, 1f);
            r.pivot = new Vector2(0f, 1f);
            r.anchoredPosition = pos;
            r.sizeDelta = size;
            return r;
        }

        /// <summary>A window: the Mod Settings panel, centred on the screen. Positions inside it are from its top-left corner (y down is negative).</summary>
        internal static RectTransform Window(string name, Vector2 size)
        {
            Image panel = Widgets.Box(Canvas, name, MagicMike.ModSettings.Theme.Panel, MagicMike.ModSettings.Theme.PanelOutline, 2f);
            var r = (RectTransform)panel.transform;
            r.anchorMin = r.anchorMax = r.pivot = new Vector2(0.5f, 0.5f);
            r.anchoredPosition = Vector2.zero;
            r.sizeDelta = size;
            return r;
        }

        /// <summary>
        /// The Mod Settings panel's title bar across a window's top (12 to 58 from it, inset 12 at the sides): the header colour, an accent
        /// strip, the title in bright bold, and its drawn close button (none when <paramref name="close"/> is null).
        /// </summary>
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

        /// <summary>The Mod Settings panel's drawn close button, at the right of a bar.</summary>
        internal static Button CloseButton(Transform bar, Action close)
        {
            Button x = Widgets.IconButton<CloseIcon>(bar, 9f);
            Widgets.Place(x.gameObject, new Vector2(1f, 0.5f), new Vector2(1f, 0.5f), new Vector2(-46f, -15f), new Vector2(-12f, 15f));
            x.navigation = new Navigation { mode = Navigation.Mode.None };
            x.onClick.AddListener(() => close());
            return x;
        }

        /// <summary>
        /// A section header, as the Mod Settings panel draws them: a dark bar with an accent strip and its title in bright bold. pos is its
        /// top-left corner.
        /// </summary>
        internal static TMP_Text HeaderBar(Transform parent, string title, Vector2 pos, Vector2 size, float fontSize = 16f)
        {
            RectTransform bar = HeaderBox(parent, pos, size);
            TMP_Text t = Widgets.Label(bar, title, fontSize, MagicMike.ModSettings.Theme.TextBright, TextAlignmentOptions.MidlineLeft, bold: true);
            Widgets.Place(t.gameObject, Vector2.zero, Vector2.one, new Vector2(12f, 0f), new Vector2(-6f, 0f));
            return t;
        }

        /// <summary>A header's dark bar alone (its accent strip on the left), to put things on.</summary>
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

        /// <summary>A box set apart from the panel (where a list or a card goes): the Mod Settings panel's field colour, outlined.</summary>
        internal static RectTransform Box(Transform parent, Vector2 pos, Vector2 size)
        {
            Image box = Widgets.Box(parent, "Box", MagicMike.ModSettings.Theme.Field, MagicMike.ModSettings.Theme.FieldOutline);
            return At(box, pos, size);
        }

        /// <summary>A thin dividing line (pos is its centre, from the parent's top middle).</summary>
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

        /// <summary>
        /// Text in the panel's font. Rich text works. For text that may wrap, size it to fit before placing what comes under it:
        /// label.GetPreferredValues(text, width, 0).y.
        /// </summary>
        internal static TMP_Text Label(Transform parent, string text, Vector2 pos, Vector2 size, float fontSize, Color color, TMP_FontAsset font = null,
                                       TextAlignmentOptions align = TextAlignmentOptions.TopLeft, Vector2? anchor = null)
        {
            Vector2 a = anchor ?? new Vector2(0f, 1f);
            RectTransform r = Rect(parent, "Text", a, a, pos, size);
            // (off while the text is made and given its font: added to an object that is on, TextMeshPro wakes at once with no font, looks for its
            // default one, which the game doesn't have, and says so in the log every time)
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

        /// <summary>A button, the Mod Settings panel's (its colours, outline and click sound).</summary>
        internal static Button Button(Transform parent, string text, Vector2 pos, Vector2 size, Action click, bool enabled = true, float fontSize = 18f)
        {
            Button b = Make(parent, text, pos, size, fontSize);
            b.interactable = enabled;
            Label(b).color = enabled ? Text : Dim;
            b.onClick.AddListener(() => click());
            return b;
        }

        /// <summary>A tab, as the Mod Settings panel's: the chosen one lit (and not clickable), its label bright.</summary>
        internal static Button Tab(Transform parent, string text, Vector2 pos, Vector2 size, bool chosen, Action click, float fontSize = 18f)
        {
            Button b = Make(parent, text, pos, size, fontSize);
            if (chosen) b.colors = MagicMike.ModSettings.Theme.Colors(MagicMike.ModSettings.Theme.Selected, MagicMike.ModSettings.Theme.Selected, MagicMike.ModSettings.Theme.Selected, MagicMike.ModSettings.Theme.Selected);
            b.interactable = !chosen;
            Label(b).color = chosen ? Warm : Text;
            b.onClick.AddListener(() => click());
            return b;
        }

        /// <summary>A button's label (to change its text or colour after making it).</summary>
        internal static TMP_Text Label(Button b) => b.GetComponentsInChildren<TMP_Text>(true).FirstOrDefault(t => t.gameObject.activeSelf);

        private static Button Make(Transform parent, string text, Vector2 pos, Vector2 size, float fontSize)
        {
            Button b = Widgets.Button(parent, text, fontSize);
            At(b, pos, size);
            b.navigation = new Navigation { mode = Navigation.Mode.None };   // (the arrow keys are not for it)
            TMP_Text label = Label(b);
            // a longer word (German, Russian) shrinks a little to fit rather than being cut short
            label.enableAutoSizing = true;
            label.fontSizeMax = fontSize;
            label.fontSizeMin = Mathf.Max(10f, fontSize * 0.7f);
            return b;
        }

        /// <summary>A tick box, the Mod Settings panel's. <paramref name="changed"/> runs with its new state when it is clicked.</summary>
        internal static Toggle Check(Transform parent, Vector2 pos, float size, bool on, Action<bool> changed)
        {
            Toggle t = Widgets.Toggle(parent);
            At(t, pos, new Vector2(size, size));
            t.navigation = new Navigation { mode = Navigation.Mode.None };
            t.SetIsOnWithoutNotify(on);
            t.onValueChanged.AddListener(v => changed(v));
            return t;
        }

        /// <summary>A picture (an item's or a trophy's icon: ItemDrop.m_itemData.GetIcon()), kept in proportion.</summary>
        internal static Image Icon(Transform parent, Vector2 pos, Vector2 size, Sprite sprite)
        {
            var img = Rect(parent, "Icon", new Vector2(0f, 1f), new Vector2(0f, 1f), pos, size).gameObject.AddComponent<Image>();
            img.sprite = sprite;
            img.preserveAspect = true;
            img.raycastTarget = false;
            img.enabled = sprite != null;
            return img;
        }

        /// <summary>
        /// A compact item with an amount ("156" beside a coin): its icon and the number in readable text, with the game's item tooltip on
        /// the icon. For costs and rewards inside a line, where a whole inventory cell is too big. Returns the width it took.
        /// </summary>
        internal static float Chip(Transform parent, Vector2 pos, ItemDrop.ItemData item, string amount, float size = 28f, float fontSize = 17f)
        {
            Image icon = Icon(parent, pos, new Vector2(size, size), item.GetIcon());
            icon.raycastTarget = true;   // (the tooltip needs the pointer)
            UITooltip cellTip = _cell != null ? _cell.GetComponent<InventoryElement>()?.m_tooltip : null;
            if (cellTip != null)
            {
                UITooltip tip = icon.gameObject.AddComponent<UITooltip>();
                tip.m_tooltipPrefab = cellTip.m_tooltipPrefab;   // (the inventory's tooltip window)
                tip.Set(item.m_shared.m_name, item.GetTooltip());
            }
            TMP_Text label = Label(parent, amount, new Vector2(pos.x + size + 3f, pos.y - (size - fontSize * 1.3f) / 2f), new Vector2(80f, size), fontSize, Text);
            label.textWrappingMode = TextWrappingModes.NoWrap;
            float w = label.GetPreferredValues(amount, 200f, 0f).x;
            ((RectTransform)label.transform).sizeDelta = new Vector2(w + 2f, size);
            return size + 3f + w + 12f;
        }

        /// <summary>
        /// A progress bar: a groove (the panel's slider track) filled to <paramref name="fraction"/> in a colour (the game's gold: Gold; done: Good), with
        /// text in the middle ("3/6") that reads on the fill and on the groove alike.
        /// </summary>
        internal static RectTransform Bar(Transform parent, Vector2 pos, Vector2 size, float fraction, Color fill, string text, float fontSize = 15f)
        {
            RectTransform groove = At(Widgets.Box(parent, "Bar", MagicMike.ModSettings.Theme.Track, MagicMike.ModSettings.Theme.FieldOutline), pos, size);
            RectTransform bar = Rect(groove, "Fill", new Vector2(0f, 0f), new Vector2(0f, 0f), new Vector2(2f, 2f), new Vector2(0f, 0f));
            bar.anchorMax = new Vector2(0f, 1f);   // (full height, its width the fraction)
            bar.offsetMax = new Vector2(2f + (size.x - 4f) * Mathf.Clamp01(fraction), -2f);
            var img = bar.gameObject.AddComponent<Image>();
            img.color = new Color(fill.r * 0.8f, fill.g * 0.8f, fill.b * 0.8f, 0.9f);
            img.raycastTarget = false;
            if (!string.IsNullOrEmpty(text))
            {
                // a dark copy under it, a pixel down and right: readable on the gold and on the dark groove
                foreach (bool shadow in new[] { true, false })
                {
                    TMP_Text t = Label(groove, text, new Vector2(shadow ? 1f : 0f, shadow ? -1f : 0f), size, fontSize, shadow ? new Color(0f, 0f, 0f, 0.85f) : Color.white,
                                       Sans, TextAlignmentOptions.Center);
                    t.textWrappingMode = TextWrappingModes.NoWrap;
                }
            }
            return groove;
        }

        /// <summary>
        /// Changes a bar made by Bar in place (a value that changes all the time: health, a timer), without building the window again.
        /// Only what changed is touched (moving a RectTransform makes the canvas lay out again).
        /// </summary>
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

        /// <summary>A picture from a texture (a render texture: a portrait) in a box's frame; change it later through the RawImage.</summary>
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

        /// <summary>The player is typing in one of your text fields: the patches below keep the game's keys (map, inventory) off what is typed.</summary>
        internal static bool Typing
        {
            get
            {
                if (!IsOpen()) return false;
                foreach (TMP_InputField f in Fields) if (f != null && f.isFocused) return true;
                return false;
            }
        }

        /// <summary>
        /// A one-line text field, the Mod Settings panel's. <paramref name="changed"/> runs as the text changes, <paramref name="submit"/> on
        /// Enter. Don't build the window again while the player is typing (<see cref="Typing"/>): the field would lose the keyboard.
        /// </summary>
        internal static TMP_InputField TextField(Transform parent, Vector2 pos, Vector2 size, string text, int limit, Action<string> changed,
                                                 Action<string> submit = null, float fontSize = 18f, string placeholder = "")
        {
            Fields.RemoveAll(f => f == null);
            TMP_InputField field = Widgets.Input(parent, fontSize, placeholder ?? "");
            At(field, pos, size);
            field.navigation = new Navigation { mode = Navigation.Mode.None };
            field.restoreOriginalTextOnEscape = false;   // (Escape closes the window)
            field.characterLimit = limit;
            field.SetTextWithoutNotify(text ?? "");
            field.onValueChanged.AddListener(v => changed(v));
            if (submit != null) field.onSubmit.AddListener(v => submit(v));
            Fields.Add(field);
            return field;
        }

        /// <summary>An empty inventory cell (a slot with nothing in it), as Cell makes them.</summary>
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
            if (e.m_tooltip != null) e.m_tooltip.enabled = false;   // (nothing to tell)
            return e;
        }

        /// <summary>
        /// Where HUD things go (a panel that stays on screen while playing): under the game's HUD, so it hides with it (the player's
        /// hide-HUD key, cutscenes, the loading screen) and scales with it. Null before a world is loaded. Position from the top-left:
        /// anchor (0, 1), anchoredPosition (x, -y).
        /// </summary>
        internal static Transform HudRoot => Hud.instance != null && Hud.instance.m_rootObject != null ? Hud.instance.m_rootObject.transform : null;

        /// <summary>
        /// One of the inventory's own item cells (InventoryGui.m_playerGrid.m_elementPrefab), showing an item as the inventory does: icon,
        /// durability bar, quality, the equipped mark, and the game's tooltip on hover. <paramref name="amount"/> replaces the stack text
        /// (a cost, a range like "2-3"); <paramref name="selected"/> shows the selection frame; click runs on a left click (null: none).
        /// pos is the cell's top-left corner; lay cells out CellSpace apart.
        /// </summary>
        internal static InventoryElement Cell(Transform parent, Vector2 pos, ItemDrop.ItemData item, string amount, bool selected, Action click)
        {
            GameObject go = Object.Instantiate(_cell, parent, false);
            go.name = "Cell";
            go.SetActive(true);
            // only clicks: dragging belongs to the inventory's own grid (its handlers aren't wired here) and gamepad bindings to the original
            foreach (UIDragHandler drag in go.GetComponentsInChildren<UIDragHandler>(true)) Object.Destroy(drag);
            foreach (UIGamePad pad in go.GetComponentsInChildren<UIGamePad>(true)) Object.Destroy(pad);
            var r = (RectTransform)go.transform;
            r.anchorMin = r.anchorMax = new Vector2(0f, 1f);
            r.pivot = new Vector2(0f, 1f);
            r.anchoredPosition = pos;

            // what InventoryGrid.UpdateGui does for one cell
            InventoryElement e = go.GetComponent<InventoryElement>();
            e.Initialize(0, 0);
            Transform binding = go.transform.Find("binding");   // (the hotbar's 1-8)
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

        /// <summary>
        /// A scrolling area filling a box (inset from its edge), with the Mod Settings panel's scrollbar: returns the content to put things
        /// in. Set the content's height to what you put in it (content.sizeDelta = new Vector2(content.sizeDelta.x, height)); the mouse
        /// wheel scrolls it (with the patch below).
        /// </summary>
        internal static RectTransform Scroll(RectTransform box, float inset)
        {
            // the crafting screen's recipe list's scroll speed (the game's mouse wheel moves a ScrollRect only a little per notch, so a guessed
            // sensitivity scrolls far too slowly)
            InventoryGui gui = InventoryGui.instance;
            ScrollRect like = gui != null ? gui.GetComponentsInChildren<ScrollRect>(true).FirstOrDefault(s => s.content == gui.m_recipeListRoot) : null;
            const float barW = 12f;

            RectTransform view = Rect(box, "View", new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(inset, -inset),
                                      box.sizeDelta - new Vector2(inset * 2f + barW + 4f, inset * 2f));
            view.gameObject.AddComponent<RectMask2D>();
            var hit = view.gameObject.AddComponent<Image>();   // (clear: catches the mouse wheel over the gaps between things)
            hit.color = Color.clear;
            RectTransform content = Rect(view, "Content", new Vector2(0f, 1f), new Vector2(0f, 1f), Vector2.zero, new Vector2(view.sizeDelta.x, 0f));
            ScrollRect scroll = view.gameObject.AddComponent<ScrollRect>();
            scroll.content = content;
            scroll.viewport = view;
            scroll.horizontal = false;
            scroll.movementType = ScrollRect.MovementType.Clamped;
            scroll.scrollSensitivity = like != null && like.scrollSensitivity > 0f ? like.scrollSensitivity : 300f;
            scroll.inertia = false;

            Image track = Widgets.Box(box, "Scrollbar", MagicMike.ModSettings.Theme.Track);
            var br = (RectTransform)track.transform;
            br.anchorMin = new Vector2(1f, 0f); br.anchorMax = new Vector2(1f, 1f); br.pivot = new Vector2(1f, 0.5f);
            br.sizeDelta = new Vector2(barW, -inset * 2f);
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
            scroll.verticalScrollbarVisibility = ScrollRect.ScrollbarVisibility.AutoHide;   // only when there's more than fits
            return content;
        }
    }

    // ---- while a window is open: the game must not play underneath it ---------------------------------------------------------------

    [HarmonyPatch(typeof(PlayerController), "TakeInput")]
    internal static class GameUi_PlayerControllerTakeInput
    {
        private static void Postfix(ref bool __result) { if (GameUi.IsOpen()) __result = false; }   // no walking, attacking, building
    }

    [HarmonyPatch(typeof(Player), "TakeInput")]
    internal static class GameUi_PlayerTakeInput
    {
        private static void Postfix(ref bool __result) { if (GameUi.IsOpen()) __result = false; }
    }

    [HarmonyPatch(typeof(GameCamera), "UpdateMouseCapture")]
    internal static class GameUi_UpdateMouseCapture
    {
        // a free cursor (the game's would lock it every frame, and snap it to the centre on Linux)
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
        private static void Postfix(ref float __result) { if (GameUi.IsOpen()) __result = 0f; }   // the wheel scrolls the window, not the camera
    }

    // (global::Menu: this mod has a class of its own named Menu)
    [HarmonyPatch(typeof(global::Menu), "Update")]
    internal static class GameUi_EscapeCloses
    {
        /// <summary>What Escape closes: set it to your window's Close.</summary>
        internal static Action Close = () => { };

        // Escape closes the window, and only that: the pause menu doesn't open too
        private static bool Prefix()
        {
            if (!GameUi.IsOpen()) return true;
            if (!(ZInput.GetKeyDown(KeyCode.Escape) || ZInput.GetButtonDown("JoyMenu"))) return true;
            Close();
            return false;
        }
    }

    // ---- typing in one of your text fields: the game's keys must not act on what is typed ---------------------------------------------

    [HarmonyPatch(typeof(Minimap), nameof(Minimap.InTextInput))]
    internal static class GameUi_InTextInput
    {
        // the game's own "is the player typing" check: the map and other mods then leave their keys alone
        private static void Postfix(ref bool __result) { if (GameUi.Typing) __result = true; }
    }

    [HarmonyPatch(typeof(InventoryGui), "Update")]
    internal static class GameUi_InventoryKey
    {
        // Tab would open the inventory under the window: instead it closes the window, as it closes a chest (what Escape closes), and
        // while typing in a text box it does nothing. Here, before the inventory looks, whichever runs first in the frame.
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
