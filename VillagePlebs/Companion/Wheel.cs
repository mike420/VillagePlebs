using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using static VillagePlebs.Words;
using Object = UnityEngine.Object;

namespace VillagePlebs
{
    // The command wheel (Wheel Key, left shift + Q): its orders and errands round the outside, how it fights on the inner ring.
    // Hold the key and let go over a wedge to choose, or tap it and click; Esc, right-click or the key again closes it.
    internal static class Wheel
    {
        private class Slice
        {
            public string Group, Label, About, Icon;
            public bool Inner;
            public Func<Humanoid, bool> Active;
            public Action<Player, Humanoid> Do;
            public float Start, Size;
            public RectTransform Body, Glyph, Dot;
            public Image Fill;
            public float Grow;
        }

        private const float OuterR = 340f, OuterHole = 228f, InnerR = 212f, InnerHole = 112f;
        private const float GroupGap = 12f, Gap = 1.6f, OuterGrow = 1.08f, InnerGrow = 1.06f, OpenTime = 0.2f, HoldToPick = 0.25f;
        private const float OuterIcon = 72f, InnerIcon = 64f, WheelScale = 0.85f, BoxWidth = 460f, BoxPad = 8f;

        private static readonly Color Base = new Color(0.14f, 0.11f, 0.08f, 0.9f);
        private static readonly Color Lit = new Color(0.34f, 0.27f, 0.17f, 0.95f);
        private static readonly Color Current = new Color(0.22f, 0.3f, 0.12f, 0.92f);
        private static readonly Color Ink = new Color(0.85f, 0.8f, 0.65f, 1f);
        private static readonly Color Bright = new Color(1f, 0.95f, 0.8f, 1f);
        private static readonly Color Good = new Color(0.55f, 0.85f, 0.45f, 1f);

        private static GameObject _canvas;
        private static Canvas _canvasComp;
        private static RectTransform _root;
        private static CanvasGroup _fade;
        private static TMP_Text _title, _sub, _about, _name;
        private static RectTransform _box;
        private static readonly List<Slice> Slices = new List<Slice>();
        private static readonly Dictionary<string, Sprite> Icons = new Dictionary<string, Sprite>();
        private static Sprite _outerRing, _innerRing, _dot;
        private static Humanoid _target;
        private static Slice _hover;
        private static float _openedAt;
        private static bool _held;

        public static bool Open => _target != null;

        public static void Tick(Player p)
        {
            if (Open) { Update(p); return; }
            if (p == null || VillagePlebsPlugin.MenuOpen || Home.AssignFor != null || VillagePlebsPlugin.TypingOrBusy() || !MainConfig.wheelKey.Value.IsDown()) return;
            Humanoid c = Target(p);
            if (c == null) { VillagePlebsPlugin.Tell(P("villagepleb_pointing_none_near")); return; }
            Pop(c);
        }

        private static Humanoid Target(Player p)
        {
            GameObject hover = p.GetHoverObject();
            Humanoid looked = hover != null ? hover.GetComponentInParent<Humanoid>() : null;
            if (looked != null && Companion.Is(looked) && !looked.IsDead() && Companion.CanCommand(looked, p)) return looked;
            return Companion.All().Where(c => !c.IsDead() && Companion.CanCommand(c, p))
                .OrderBy(c => Vector3.Distance(c.transform.position, p.transform.position)).FirstOrDefault();
        }

        private static void Pop(Humanoid c)
        {
            if (_canvas == null) Build();
            _target = c;
            _openedAt = Time.unscaledTime;
            _held = true;
            _hover = null;
            foreach (Slice s in Slices) s.Grow = 0f;
            _name.text = Companion.NameOf(c);
            _canvas.SetActive(true);
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
        }

        public static void Close()
        {
            _target = null;
            _hover = null;
            if (_canvas != null) _canvas.SetActive(false);
        }

        private static void Update(Player p)
        {
            if (p == null || _target == null || _target.IsDead() || _canvas == null || VillagePlebsPlugin.MenuOpen || VillagePlebsPlugin.TypingOrBusy()) { Close(); return; }
            float t = Mathf.Clamp01((Time.unscaledTime - _openedAt) / OpenTime);
            _root.localScale = Vector3.one * WheelScale * Mathf.LerpUnclamped(0.75f, 1f, EaseOutBack(t));
            _fade.alpha = t;
            if (Input.GetKeyDown(KeyCode.Escape) || Input.GetMouseButtonDown(1)) { Close(); return; }

            _hover = Hovered();
            if (_held && !MainConfig.wheelKey.Value.IsPressed())
            {
                _held = false;
                if (Time.unscaledTime - _openedAt > HoldToPick) { if (_hover != null) Pick(p, _hover); else Close(); return; }
            }
            else if (!_held && MainConfig.wheelKey.Value.IsDown()) { Close(); return; }
            if (Input.GetMouseButtonDown(0) && _hover != null) { Pick(p, _hover); return; }
            Paint();
        }

        private static Slice Hovered()
        {
            float scale = (_canvasComp != null ? _canvasComp.scaleFactor : 1f) * Mathf.Max(0.01f, _root.localScale.x);
            Vector2 m = ((Vector2)Input.mousePosition - new Vector2(Screen.width, Screen.height) * 0.5f) / scale;
            float d = m.magnitude;
            if (d < InnerHole * 0.85f) return null;
            bool inner = d < (InnerR + OuterHole) * 0.5f;
            float a = Mathf.Repeat(Mathf.Atan2(m.x, m.y) * Mathf.Rad2Deg, 360f);
            return Slices.Where(s => s.Inner == inner).OrderBy(s => Mathf.Abs(Mathf.DeltaAngle(a, s.Start + s.Size * 0.5f))).FirstOrDefault();
        }

        private static void Pick(Player p, Slice s)
        {
            Humanoid c = _target;
            Close();
            if (c == null) return;
            if (!Companion.CanCommand(c, p)) { VillagePlebsPlugin.Tell(P("villagepleb_menu_owner_orders")); return; }
            s.Do(p, c);
        }

        private static void Paint()
        {
            Slice h = _hover;
            float dt = Time.unscaledDeltaTime;
            foreach (Slice s in Slices)
            {
                bool active = s.Active != null && s.Active(_target);
                s.Grow = Mathf.MoveTowards(s.Grow, s == h ? 1f : 0f, dt * 9f);
                float e = s.Grow * s.Grow * (3f - 2f * s.Grow);
                s.Body.localScale = Vector3.one * Mathf.Lerp(1f, s.Inner ? InnerGrow : OuterGrow, e);
                s.Fill.color = Color.Lerp(active ? Current : Base, Lit, e);
                // The wedge grows from the middle of the wheel, so its icon and dot move out with it by the same amount.
                float swell = Mathf.Lerp(1f, s.Inner ? InnerGrow : OuterGrow, e);
                var dir = new Vector2(Mathf.Sin((s.Start + s.Size * 0.5f) * Mathf.Deg2Rad), Mathf.Cos((s.Start + s.Size * 0.5f) * Mathf.Deg2Rad));
                float mid = (s.Inner ? (InnerR + InnerHole) : (OuterR + OuterHole)) * 0.5f, icon = s.Inner ? InnerIcon : OuterIcon;
                s.Glyph.anchoredPosition = dir * mid * swell;
                s.Glyph.localScale = Vector3.one * (1f + 0.25f * e);
                s.Dot.gameObject.SetActive(active);
                s.Dot.anchoredPosition = dir * (mid + icon * 0.5f + 9f) * swell;
            }
            _box.gameObject.SetActive(h != null);
            if (h == null) return;
            bool current = h.Active != null && h.Active(_target), about = h.About != null;
            _title.text = Show(h.Label);
            _sub.text = Show(h.Group) + (current ? "  ·  " + Show(P("villagepleb_wheel_current")) : "");
            _sub.color = current ? Good : Ink;
            _about.text = about ? Show(h.About) : "";
            // The box sits on a line above the wheel and grows upwards to fit what it says.
            float aboutH = about ? Mathf.Ceil(_about.GetPreferredValues(_about.text, BoxWidth - BoxPad * 3f, 0f).y) : 0f;
            float height = BoxPad + 28f + 20f + (about ? 6f + aboutH : 0f) + BoxPad, top = height * 0.5f - BoxPad;
            _box.sizeDelta = new Vector2(BoxWidth, height);
            _title.rectTransform.anchoredPosition = new Vector2(0f, top - 14f);
            _sub.rectTransform.anchoredPosition = new Vector2(0f, top - 38f);
            _about.rectTransform.sizeDelta = new Vector2(BoxWidth - BoxPad * 3f, aboutH);
            _about.rectTransform.anchoredPosition = new Vector2(0f, top - 54f - aboutH * 0.5f);
        }

        private static float EaseOutBack(float t)
        {
            const float c1 = 1.70158f, c3 = c1 + 1f;
            return 1f + c3 * Mathf.Pow(t - 1f, 3f) + c1 * Mathf.Pow(t - 1f, 2f);
        }

        // ---- what is on it ----

        private static IEnumerable<Slice> Orders()
        {
            const string g = "villagepleb_menu_tab_orders";
            yield return new Slice { Group = P(g), Label = P("villagepleb_menu_come_with_me"), Icon = "follow", Active = c => Companion.OrderOf(c) == Order.Follow,
                                     Do = (p, c) => Say(c, Home.Follow(c), "villagepleb_home_say_behind_you") };
            yield return new Slice { Group = P(g), Label = P("villagepleb_menu_go_home"), Icon = "gohome", Active = c => Companion.OrderOf(c) == Order.Gather,
                                     Do = (p, c) => Say(c, Home.GoHome(c), "villagepleb_home_say_heading_home") };
            yield return new Slice { Group = P(g), Label = P("villagepleb_menu_stay_here"), Icon = "stay", Active = c => Companion.OrderOf(c) == Order.Stay,
                                     Do = (p, c) => Say(c, Companion.Write(c, z => z.Set(Keys.Order, (int)Order.Stay)), null) };
            yield return new Slice { Group = P(g), Label = P("villagepleb_menu_guard_spot"), Icon = "guard", Active = c => Companion.OrderOf(c) == Order.Guard,
                                     Do = (p, c) => Say(c, Companion.Write(c, z => { z.Set(Keys.Order, (int)Order.Guard); z.Set(Keys.Post, c.transform.position); }), null) };
            yield return new Slice { Group = P(g), Label = P("villagepleb_menu_come_here"), Icon = "comehere",
                                     Do = (p, c) =>
                                     {
                                         if (!Say(c, Home.Follow(c), "villagepleb_home_say_behind_you")) return;
                                         if (Vector3.Distance(p.transform.position, c.transform.position) > 15f && c.GetComponent<ZNetView>().IsOwner()) Brain.TeleportBehind(c, p);
                                     } };
        }

        private static readonly Dictionary<Errand, string> ErrandIcons = new Dictionary<Errand, string>
        {
            [Errand.PutAway] = "putaway", [Errand.Restock] = "restock", [Errand.BetterGear] = "bettergear", [Errand.RepairGear] = "repairgear", [Errand.RepairBase] = "repairbase",
            [Errand.Fires] = "fires", [Errand.Cook] = "cook", [Errand.Plant] = "plant", [Errand.Harvest] = "harvest",
        };

        private static IEnumerable<Slice> ErrandSlices() =>
            Errands.All.Select(e =>
            {
                Errand what = e.What;
                return new Slice
                {
                    Group = P("villagepleb_menu_errands"), Label = e.Label, Icon = ErrandIcons.TryGetValue(what, out string icon) ? icon : "putaway",
                    Active = what == Errand.Plant || what == Errand.Harvest ? c => Brain.Get(c)?.Farm == what : (Func<Humanoid, bool>)null,
                    Do = (p, c) =>
                    {
                        BrainState st = Brain.Claim(c, out string why);
                        if (st == null) { VillagePlebsPlugin.Tell(why); return; }
                        string said = Errands.Run(st, what);
                        if (!string.IsNullOrEmpty(said)) VillagePlebsPlugin.Tell(said);
                    },
                };
            });

        private static IEnumerable<Slice> Styles()
        {
            var styles = new[] { (Style.Auto, "auto"), (Style.Aggressive, "aggressive"), (Style.Balanced, "balanced"), (Style.Defensive, "defensive"), (Style.Passive, "passive") };
            foreach (var (style, icon) in styles)
            {
                Style pick = style;
                yield return new Slice
                {
                    Inner = true, Group = P("villagepleb_menu_how_it_fights"), Label = P("villagepleb_menu_style_" + icon), About = P("villagepleb_menu_style_" + icon + "_about"), Icon = icon,
                    Active = c => Companion.Chosen(c) == pick,
                    Do = (p, c) => Say(c, Companion.Write(c, z => z.Set(Keys.Style, (int)pick)), null),
                };
            }
        }

        private static bool Say(Humanoid c, bool ok, string line)
        {
            if (!ok) { VillagePlebsPlugin.Tell(P("villagepleb_menu_busy")); return false; }
            if (line != null) Talk.Say(c, P(line));
            return true;
        }

        // ---- building it ----

        private static void Build()
        {
            _canvas = new GameObject("VillagePlebs_Wheel", typeof(Canvas), typeof(CanvasScaler));
            Object.DontDestroyOnLoad(_canvas);
            _canvasComp = _canvas.GetComponent<Canvas>();
            _canvasComp.renderMode = RenderMode.ScreenSpaceOverlay;
            _canvasComp.sortingOrder = 32000;
            CanvasScaler scaler = _canvas.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.matchWidthOrHeight = 0.5f;

            _root = Rect("Root", _canvas.transform, new Vector2(OuterR * 2f + 200f, OuterR * 2f + 260f));
            _fade = _root.gameObject.AddComponent<CanvasGroup>();
            _fade.blocksRaycasts = false;
            _outerRing = Ring(OuterHole / OuterR);
            _innerRing = Ring(InnerHole / InnerR);
            _dot = Ring(0f);

            Slices.Clear();
            List<Slice> orders = Orders().ToList(), errands = ErrandSlices().ToList();
            float each = (360f - GroupGap * 2f) / (orders.Count + errands.Count);
            float at = -orders.Count * each * 0.5f;
            foreach (List<Slice> group in new[] { orders, errands })
            {
                foreach (Slice s in group) { s.Start = at + Gap * 0.5f; s.Size = each - Gap; at += each; }
                Heading(group[0].Group, group[0].Start + (group.Count * each - Gap) * 0.5f);
                at += GroupGap;
                Slices.AddRange(group);
            }
            List<Slice> styles = Styles().ToList();
            float step = 360f / styles.Count;
            for (int i = 0; i < styles.Count; i++) { styles[i].Start = -step * 0.5f + i * step + Gap * 0.5f; styles[i].Size = step - Gap; }
            Slices.AddRange(styles);
            foreach (Slice s in Slices) Wedge(s);

            _name = Text(_root, 26f, Bright, new Vector2(InnerHole * 1.8f, 60f), Vector2.zero);
            _box = Rect("Hover", _root, new Vector2(BoxWidth, 56f));
            _box.pivot = new Vector2(0.5f, 0f);
            _box.anchoredPosition = new Vector2(0f, OuterR + 62f);
            Image back = _box.gameObject.AddComponent<Image>();
            back.color = Base;
            back.raycastTarget = false;
            _title = Text(_box, 22f, Bright, new Vector2(BoxWidth - BoxPad * 2f, 28f), Vector2.zero);
            _sub = Text(_box, 15f, Ink, new Vector2(BoxWidth - BoxPad * 2f, 20f), Vector2.zero);
            _about = Text(_box, 14f, Ink, new Vector2(BoxWidth - BoxPad * 3f, 20f), Vector2.zero);
            _about.textWrappingMode = TextWrappingModes.Normal;
            _box.gameObject.SetActive(false);
            _canvas.SetActive(false);
        }

        private static void Wedge(Slice s)
        {
            float r = s.Inner ? InnerR : OuterR;
            s.Body = Rect("Wedge", _root, new Vector2(r * 2f, r * 2f));
            s.Body.localRotation = Quaternion.Euler(0f, 0f, -s.Start);
            s.Fill = s.Body.gameObject.AddComponent<Image>();
            s.Fill.sprite = s.Inner ? _innerRing : _outerRing;
            s.Fill.type = Image.Type.Filled;
            s.Fill.fillMethod = Image.FillMethod.Radial360;
            s.Fill.fillOrigin = (int)Image.Origin360.Top;
            s.Fill.fillClockwise = true;
            s.Fill.fillAmount = s.Size / 360f;
            s.Fill.color = Base;
            s.Fill.raycastTarget = false;
            float size = s.Inner ? InnerIcon : OuterIcon;
            s.Glyph = Rect("Icon", _root, new Vector2(size, size));
            Image icon = s.Glyph.gameObject.AddComponent<Image>();
            icon.sprite = Icon(s.Icon);
            icon.color = new Color(0.95f, 0.92f, 0.85f, 0.95f);
            icon.raycastTarget = false;
            icon.preserveAspect = true;
            s.Dot = Rect("Dot", _root, new Vector2(11f, 11f));
            Image dot = s.Dot.gameObject.AddComponent<Image>();
            dot.sprite = _dot;
            dot.color = Good;
            dot.raycastTarget = false;
            s.Dot.gameObject.SetActive(false);
        }

        private static void Heading(string text, float angle)
        {
            float a = angle * Mathf.Deg2Rad;
            TMP_Text t = Text(_root, 22f, Ink, new Vector2(220f, 30f), new Vector2(Mathf.Sin(a), Mathf.Cos(a)) * (OuterR + 30f));
            t.text = Show(text);
            t.fontStyle = FontStyles.Bold;
        }

        private static RectTransform Rect(string name, Transform parent, Vector2 size)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var rt = (RectTransform)go.transform;
            rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0.5f, 0.5f);
            rt.sizeDelta = size;
            return rt;
        }

        private static TMP_Text Text(Transform parent, float size, Color color, Vector2 box, Vector2 at)
        {
            RectTransform rt = Rect("Text", parent, box);
            rt.anchoredPosition = at;
            // Asleep while the font goes on, or TextMeshPro looks for its own default font first and warns that it isn't there.
            rt.gameObject.SetActive(false);
            var t = rt.gameObject.AddComponent<TextMeshProUGUI>();
            t.font = GameUi.Norse;
            t.fontSize = size;
            t.color = color;
            t.alignment = TextAlignmentOptions.Center;
            t.textWrappingMode = TextWrappingModes.NoWrap;
            t.raycastTarget = false;
            rt.gameObject.SetActive(true);
            return t;
        }

        // A soft-edged white ring with the given hole, filled in wedges and tinted per slice.
        private static Sprite Ring(float hole)
        {
            const int n = 512;
            var tex = new Texture2D(n, n, TextureFormat.RGBA32, false) { filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp };
            var px = new Color[n * n];
            float c = (n - 1) * 0.5f, outer = c - 1f, inner = outer * hole, edge = 2f;
            for (int y = 0; y < n; y++)
                for (int x = 0; x < n; x++)
                {
                    float d = Mathf.Sqrt((x - c) * (x - c) + (y - c) * (y - c));
                    float a = Mathf.Clamp01((outer - d) / edge) * Mathf.Clamp01((d - inner) / edge);
                    float shade = Mathf.Lerp(1f, 0.82f, Mathf.Clamp01((d - inner) / Mathf.Max(1f, outer - inner)));
                    px[y * n + x] = new Color(1f, 1f, 1f, a * shade);
                }
            tex.SetPixels(px);
            tex.Apply();
            return Sprite.Create(tex, new Rect(0f, 0f, n, n), new Vector2(0.5f, 0.5f), 100f);
        }

        private static Sprite Icon(string name)
        {
            if (Icons.TryGetValue(name, out Sprite cached)) return cached;
            Sprite sprite = null;
            try
            {
                using (Stream stream = typeof(Wheel).Assembly.GetManifestResourceStream("wheel/" + name + ".png"))
                using (var bytes = new MemoryStream())
                {
                    if (stream != null)
                    {
                        stream.CopyTo(bytes);
                        var tex = new Texture2D(2, 2, TextureFormat.RGBA32, true) { name = "villagepleb_wheel_" + name };
                        MethodInfo load = Type.GetType("UnityEngine.ImageConversion, UnityEngine.ImageConversionModule")?.GetMethod("LoadImage", new[] { typeof(Texture2D), typeof(byte[]) });
                        if (load != null && (bool)load.Invoke(null, new object[] { tex, bytes.ToArray() }))
                        {
                            tex.filterMode = FilterMode.Trilinear;
                            sprite = Sprite.Create(tex, new Rect(0f, 0f, tex.width, tex.height), new Vector2(0.5f, 0.5f), 100f);
                        }
                    }
                }
            }
            catch (Exception e) { VillagePlebsPlugin.Instance?.Warn($"Wheel icon {name}: {e.Message}"); }
            Icons[name] = sprite;
            return sprite;
        }
    }

    // While the wheel is up the game treats it like its own radial menu: no looking round, attacking or jumping.
    [HarmonyPatch(typeof(Hud), nameof(Hud.InRadial))]
    internal static class Hud_InRadial_Wheel
    {
        private static void Postfix(ref bool __result) { if (Wheel.Open) __result = true; }
    }

    [HarmonyPatch(typeof(Hud), "UpdateCrosshair")]
    internal static class Hud_UpdateCrosshair_Wheel
    {
        private static void Postfix(Hud __instance)
        {
            if (!Wheel.Open) return;
            __instance.m_crosshair.gameObject.SetActive(false);
            __instance.m_hoverName.text = "";
        }
    }

    // The wheel key shares Q with the game's auto-run: holding it for the wheel doesn't set you running.
    [HarmonyPatch(typeof(ZInput), nameof(ZInput.GetButton))]
    internal static class ZInput_GetButton_Wheel
    {
        private static void Postfix(string name, ref bool __result) { if (__result && name == "AutoRun" && (Wheel.Open || MainConfig.wheelKey.Value.IsPressed())) __result = false; }
    }

    [HarmonyPatch(typeof(ZInput), nameof(ZInput.GetButtonDown))]
    internal static class ZInput_GetButtonDown_Wheel
    {
        private static void Postfix(string name, ref bool __result) { if (__result && name == "AutoRun" && (Wheel.Open || MainConfig.wheelKey.Value.IsPressed())) __result = false; }
    }
}
