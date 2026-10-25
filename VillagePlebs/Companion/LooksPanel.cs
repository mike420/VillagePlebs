using System;
using System.Reflection;
using HarmonyLib;
using MagicMike.ModSettings;
using TMPro;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.UI;
using static VillagePlebs.Shared.Translations;

namespace VillagePlebs
{
    internal sealed class LooksPanel
    {
        public readonly long Id;
        public Looks.Look Current;

        private static readonly Vector3 PreviewPos = new Vector3(10060f, 5000f, 10000f);
        private const float DragSensitivity = 0.4f;
        private const float Margin = 8f, ColumnGap = 8f, ChoicesShare = 0.31f, ButtonsShare = 0.25f;
        private const float SectionGap = 10f, HeaderHeight = 24f, ButtonRowHeight = 42f, PickerRowHeight = 36f, SliderHeight = 24f;

        private static readonly MethodInfo UpdateVisuals = AccessTools.Method(typeof(VisEquipment), "UpdateVisuals");
        private static readonly FieldInfo CurrentModelIndex = AccessTools.Field(typeof(VisEquipment), "m_currentModelIndex");

        private RenderTexture _rt;
        private Camera _cam;
        private GameObject _clone, _lightRig;
        private int _cloneModel = -1;
        private float _rotation, _pan, _zoom, _zoomTarget, _zoomSpeed;
        private Vector2 _lastMouse;
        private bool _dragging;
        private Light _faceLight;
        private SkinnedMeshRenderer _eyes;
        private const float PanPerPixel = 0.004f, LowestPan = -1.4f, HighestPan = 0.4f;

        private GameObject _beardGroup, _vanillaLook, _einherjarLook, _muscleSection, _bodyShapeGroup, _einherjarBeardGroup;
        private RectTransform _lookContent;
        private RawImage _previewImg;
        private Button _maleBtn, _femaleBtn, _hairReset;
        private TMP_Text _hairName, _beardName, _einherjarHairName, _einherjarBeardName;
        private TMP_Text[] _tattooDesigns = Array.Empty<TMP_Text>(), _tattooInks = Array.Empty<TMP_Text>();
        private Slider _skinSlider, _hairToneSlider, _hairShadeSlider;
        private HsvPicker _hairPicker;
        private Image _hairSwatch;

        private const float ColourPickerHeight = 112f, ResetSize = 20f;
        private static readonly Color PreviewSolid = new Color(Theme.Field.r, Theme.Field.g, Theme.Field.b, 1f);

        private readonly Humanoid _companion;
        private float _openedAt = -1f;
        private bool _portraitChecked;
        private static readonly Color PortraitBack = new Color(0.16f, 0.2f, 0.19f, 1f);
        private int _worn;

        private static bool _showArmour = true;
        private RectTransform _armourBox;

        public LooksPanel(Humanoid c)
        {
            _companion = c;
            Id = Companion.IdOf(c);
            Current = Looks.Of(c);
            if (string.IsNullOrEmpty(Current.BrLook)) Current.BrLook = BodyReplacerLook.DefaultLook();
            if (Current.BrTattoos == null) Current.BrTattoos = "";
            if (Current.BrHair == null) Current.BrHair = "";
        }

        public void Build(RectTransform area, string about, bool mine, Action apply)
        {
            _hairPicker?.Destroy();
            _hairPicker = null;
            Vector2 size = area.sizeDelta;
            float spare = size.x - Margin * 2f - ColumnGap * 2f, colH = size.y - Margin * 2f;
            float choicesW = spare * ChoicesShare, buttonsW = spare * ButtonsShare, previewW = spare - choicesW - buttonsW;
            float x = Margin;
            RectTransform left = Column("VP_LooksChoices", area, x, choicesW); x += choicesW + ColumnGap;
            RectTransform middle = Column("VP_LooksButtons", area, x, buttonsW); x += buttonsW + ColumnGap;
            RectTransform right = Column("VP_LooksPreview", area, x, previewW);
            BuildChoices(left);
            BuildButtons(middle, about, mine, apply);
            BuildPreview(right, previewW, colH);
            Refresh();
        }

        private static RectTransform Column(string name, Transform parent, float x, float w)
        {
            Image box = Widgets.Box(parent, name, Theme.Field, Theme.FieldOutline);
            return Widgets.Place(box.gameObject, new Vector2(0f, 0f), new Vector2(0f, 1f), new Vector2(x, Margin), new Vector2(x + w, -Margin));
        }

        private void BuildChoices(RectTransform col)
        {
            GameObject view = new GameObject("Scroll", typeof(RectTransform), typeof(Image), typeof(RectMask2D));
            view.transform.SetParent(col, false);
            RectTransform viewRect = Widgets.Place(view, Vector2.zero, Vector2.one, Vector2.zero, new Vector2(-(GameUi.BarWidth + 4f), 0f));
            view.GetComponent<Image>().color = Color.clear;
            GameObject content = Widgets.Child(view.transform, "Content");
            _lookContent = (RectTransform)content.transform;
            _lookContent.anchorMin = new Vector2(0f, 1f);
            _lookContent.anchorMax = new Vector2(1f, 1f);
            _lookContent.pivot = new Vector2(0.5f, 1f);
            _lookContent.anchoredPosition = Vector2.zero;
            _lookContent.sizeDelta = Vector2.zero;
            Stack(content, SectionGap, new RectOffset(8, 4, 6, 10));
            content.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            ScrollRect scroll = view.AddComponent<ScrollRect>();
            scroll.content = _lookContent;
            scroll.viewport = viewRect;
            scroll.vertical = true;
            scroll.horizontal = false;
            scroll.movementType = ScrollRect.MovementType.Clamped;
            scroll.scrollSensitivity = GameUi.ScrollSpeed;
            scroll.inertia = false;
            GameUi.AddScrollbar(scroll, col, 6f);
            Transform list = content.transform;

            Transform genderRow = Row(Section(list, T("villagepleb_looks_gender"), "Gender"), ButtonRowHeight);
            _maleBtn = ToggleButton(genderRow, T("villagepleb_looks_male"), () => SetModel(0));
            _femaleBtn = ToggleButton(genderRow, T("villagepleb_looks_female"), () => SetModel(1));
            _skinSlider = MakeSlider(Section(list, T("villagepleb_looks_skin"), "SkinTone"), Current.Skin, v => { Current.Skin = v; ApplyToClone(); });

            _vanillaLook = Stack(Widgets.Child(list, "VanillaLook"), SectionGap, new RectOffset(0, 0, 0, 0));
            Transform vanilla = _vanillaLook.transform;
            _hairName = PickerRow(Section(vanilla, T("villagepleb_looks_hair"), "HairStyle"), () => StepHair(-1), () => StepHair(1));
            _beardGroup = Section(vanilla, T("villagepleb_looks_beard"), "BeardStyle").gameObject;
            _beardName = PickerRow(_beardGroup.transform, () => StepBeard(-1), () => StepBeard(1));
            _hairToneSlider = MakeSlider(Section(vanilla, T("villagepleb_looks_hair_tone"), "HairTone"), Current.HairTone, v => { Current.HairTone = v; ApplyToClone(); });
            _hairShadeSlider = MakeSlider(Section(vanilla, T("villagepleb_looks_hair_shade"), "HairShade"), Current.HairShade, v => { Current.HairShade = v; ApplyToClone(); });

            if (BodyReplacerLook.Available) BuildEinherjarLook(list);
        }

        private void BuildEinherjarLook(Transform list)
        {
            _einherjarLook = Stack(Widgets.Child(list, "EinherjarLook"), SectionGap, new RectOffset(0, 0, 0, 0));
            Transform einherjar = _einherjarLook.transform;
            _einherjarHairName = PickerRow(Section(einherjar, T("villagepleb_looks_hair"), "EinherjarHair"), () => StepEinherjarHair(-1), () => StepEinherjarHair(1));
            if (BodyReplacerLook.BeardNames().Length > 0)
            {
                _einherjarBeardGroup = Section(einherjar, T("villagepleb_looks_beard"), "EinherjarBeard").gameObject;
                _einherjarBeardName = PickerRow(_einherjarBeardGroup.transform, () => StepEinherjarBeard(-1), () => StepEinherjarBeard(1));
            }
            BuildHairColour(Section(einherjar, T("villagepleb_looks_hair_colour"), "EinherjarHairColour"));
            EinherjarSlider(Section(einherjar, T("villagepleb_looks_eyes"), "EyeColour"), "EyeHue", 0f, 1f);
            EinherjarSlider(Section(einherjar, T("villagepleb_looks_eye_rich"), "EyeRich"), "EyeSaturation", 0f, 1f);
            EinherjarSlider(Section(einherjar, T("villagepleb_looks_eye_shade"), "EyeShade"), "EyeValue", 0f, 1f);
            EinherjarSlider(Section(einherjar, T("villagepleb_looks_eye_glow"), "EyeGlow"), "Glow", 0f, 3f);
            EinherjarSlider(Section(einherjar, T("villagepleb_looks_iris"), "IrisSize"), "IrisSize", 0.5f, 1.2f);
            _bodyShapeGroup = Stack(Widgets.Child(einherjar, "BodyShape"), SectionGap, new RectOffset(0, 0, 0, 0));
            EinherjarSlider(Section(_bodyShapeGroup.transform, T("villagepleb_looks_bust"), "Bust"), "BreastSize", -100f, 100f);
            EinherjarSlider(Section(_bodyShapeGroup.transform, T("villagepleb_looks_hips"), "Hips"), "ButtSize", -100f, 100f);
            _muscleSection = Section(einherjar, T("villagepleb_looks_muscle"), "Muscle").gameObject;
            MakeSlider(_muscleSection.transform, (Current.BrMuscle >= 0f ? Current.BrMuscle : BodyReplacerLook.MuscleDefault(Current.Model)) / 100f,
                v => { Current.BrMuscle = v * 100f; ApplyToClone(); });
            if (BodyReplacerLook.HasTattoos) BuildTattoos(einherjar);
        }

        private void BuildTattoos(Transform einherjar)
        {
            string[] areas = BodyReplacerLook.TattooAreas();
            _tattooDesigns = new TMP_Text[areas.Length];
            _tattooInks = new TMP_Text[areas.Length];
            for (int i = 0; i < areas.Length; i++)
            {
                int area = i;
                Transform section = Section(einherjar, T("villagepleb_looks_tattoo_area", areas[area]), "Tattoo" + area);
                _tattooDesigns[area] = PickerRow(section, () => StepTattoo(area, -1, 0), () => StepTattoo(area, 1, 0));
                _tattooInks[area] = PickerRow(section, () => StepTattoo(area, 0, -1), () => StepTattoo(area, 0, 1));
            }
        }

        private void StepTattoo(int area, int designBy, int inkBy)
        {
            int designs = BodyReplacerLook.TattooDesignNames(area).Length + 1, inks = BodyReplacerLook.TattooInkNames().Length;
            if (inks == 0) return;
            int design = (BodyReplacerLook.TattooDesign(Current.BrTattoos, area) + designBy + designs) % designs;
            int ink = (BodyReplacerLook.TattooInk(Current.BrTattoos, area) + inkBy + inks) % inks;
            Current.BrTattoos = BodyReplacerLook.SetTattoo(Current.BrTattoos, area, design, ink);
            Refresh();
            ApplyToClone();
        }

        private void StepEinherjarBeard(int by)
        {
            int count = BodyReplacerLook.BeardNames().Length;
            if (count == 0) return;
            Current.BrBeard = (Current.BrBeard + by + count) % count;
            Refresh();
            ApplyToClone();
        }

        private void EinherjarSlider(Transform section, string field, float min, float max) =>
            MakeSlider(section, Mathf.InverseLerp(min, max, BodyReplacerLook.Get(Current.BrLook, field)), v => SetEinherjarField(field, Mathf.Lerp(min, max, v)));

        private void SetEinherjarField(string field, float value)
        {
            Current.BrLook = BodyReplacerLook.Set(Current.BrLook, field, value);
            ApplyToClone();
        }

        // The style this body shows, among the ones it offers (each body has its own list), then BodyReplacer's added hairstyles
        private int EinherjarHairIndex(int[] ids, string[] added)
        {
            int i = string.IsNullOrEmpty(Current.BrHair) ? -1 : Array.IndexOf(added, Current.BrHair);
            if (i >= 0) return ids.Length + i;
            return Mathf.Max(0, Array.IndexOf(ids, BodyReplacerLook.ShownHairStyle(Current.Model, Mathf.RoundToInt(BodyReplacerLook.Get(Current.BrLook, "HairStyle")))));
        }

        private void StepEinherjarHair(int by)
        {
            int[] ids = BodyReplacerLook.HairStyleIds(Current.Model);
            string[] added = BodyReplacerLook.CustomHairs(Current.Model);
            int count = ids.Length + added.Length;
            if (count == 0) return;
            int next = (EinherjarHairIndex(ids, added) + by + count) % count;
            if (next < ids.Length)
            {
                Current.BrHair = "";
                SetEinherjarField("HairStyle", ids[next]);
            }
            else
            {
                Current.BrHair = added[next - ids.Length];
                ApplyToClone();
            }
            Refresh();
        }

        private void BuildHairColour(Transform section)
        {
            RectTransform header = (RectTransform)section.GetChild(0);
            _hairSwatch = Widgets.Box(header, "Swatch", Color.white, new Color(0f, 0f, 0f, 0.8f), 1.5f);
            _hairSwatch.raycastTarget = false;
            Widgets.Place(_hairSwatch.gameObject, new Vector2(1f, 0.5f), new Vector2(1f, 0.5f), new Vector2(-26f, -7f), new Vector2(-12f, 7f));
            _hairReset = Widgets.IconButton<ResetIcon>(header, 3f);
            Widgets.Place(_hairReset.gameObject, new Vector2(1f, 0.5f), new Vector2(1f, 0.5f), new Vector2(-(ResetSize + 34f), -ResetSize / 2f), new Vector2(-34f, ResetSize / 2f));
            _hairReset.onClick.AddListener(ResetEinherjarHair);
            _hairPicker = new HsvPicker(section, ColourPickerHeight) { OnChanged = SetEinherjarHairColour };
        }

        private void SetEinherjarHairColour(float hue, float saturation, float value)
        {
            string look = BodyReplacerLook.Set(Current.BrLook, "HairHue", hue);
            look = BodyReplacerLook.Set(look, "HairSaturation", saturation);
            look = BodyReplacerLook.Set(look, "HairValue", value);
            Current.BrLook = BodyReplacerLook.Set(look, "CustomHair", 1f);
            Refresh();
            ApplyToClone();
        }

        private void ResetEinherjarHair()
        {
            string defaults = BodyReplacerLook.DefaultLook(), look = Current.BrLook;
            foreach (string field in new[] { "HairHue", "HairSaturation", "HairValue" }) look = BodyReplacerLook.Set(look, field, BodyReplacerLook.Get(defaults, field));
            Current.BrLook = BodyReplacerLook.Set(look, "CustomHair", 0f);
            Refresh();
            ApplyToClone();
        }

        private void BuildButtons(RectTransform col, string about, bool mine, Action apply)
        {
            const float btnH = 44f;
            Button random = Widgets.Button(col, T("villagepleb_menu_randomise"), 18f);
            Widgets.Place(random.gameObject, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(12f, -8f - btnH), new Vector2(-12f, -8f));
            random.onClick.AddListener(() => SetAll(Looks.Random()));
            Button likeMe = Widgets.Button(col, T("villagepleb_menu_look_like_me"), 18f);
            Widgets.Place(likeMe.gameObject, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(12f, -16f - btnH * 2f), new Vector2(-12f, -16f - btnH));
            likeMe.onClick.AddListener(() => SetAll(Looks.Like(Player.m_localPlayer)));

            Button applyBtn = Widgets.Button(col, T("villagepleb_looks_apply"), 20f);
            Widgets.Place(applyBtn.gameObject, new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(12f, 8f), new Vector2(-12f, 8f + btnH));
            applyBtn.interactable = mine;
            applyBtn.onClick.AddListener(() => apply());

            RectTransform area = Widgets.Place(Widgets.Child(col, "About"), Vector2.zero, Vector2.one, new Vector2(12f, btnH + 16f), new Vector2(-4f, -24f - btnH * 2f));
            GameObject view = new GameObject("View", typeof(RectTransform), typeof(Image), typeof(RectMask2D));
            view.transform.SetParent(area, false);
            RectTransform viewRect = Widgets.Place(view, Vector2.zero, Vector2.one, Vector2.zero, new Vector2(-(GameUi.BarWidth + 6f), 0f));
            view.GetComponent<Image>().color = Color.clear;
            TMP_Text text = Widgets.Label(view.transform, about, 17f, Theme.Text, TextAlignmentOptions.TopLeft);
            text.textWrappingMode = TextWrappingModes.Normal;
            text.overflowMode = TextOverflowModes.Overflow;
            RectTransform textRect = text.rectTransform;
            textRect.anchorMin = new Vector2(0f, 1f);
            textRect.anchorMax = new Vector2(1f, 1f);
            textRect.pivot = new Vector2(0.5f, 1f);
            textRect.anchoredPosition = Vector2.zero;
            textRect.sizeDelta = Vector2.zero;
            text.gameObject.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            ScrollRect scroll = view.AddComponent<ScrollRect>();
            scroll.content = textRect;
            scroll.viewport = viewRect;
            scroll.horizontal = false;
            scroll.movementType = ScrollRect.MovementType.Clamped;
            scroll.scrollSensitivity = GameUi.ScrollSpeed;
            scroll.inertia = false;
            GameUi.AddScrollbar(scroll, area, 0f);
        }

        private void BuildPreview(RectTransform col, float colW, float colH)
        {
            GameObject go = new GameObject("PreviewImg", typeof(RectTransform));
            go.transform.SetParent(col, false);
            Widgets.Fill(go);
            _previewImg = go.AddComponent<RawImage>();
            _previewImg.color = Color.white;
            _previewImg.raycastTarget = true;
            int width = Mathf.Max(64, Mathf.RoundToInt(colW) * 4), height = Mathf.Max(64, Mathf.RoundToInt(colH) * 4);
            if (_rt != null && (_rt.width != width || _rt.height != height)) { _rt.Release(); UnityEngine.Object.Destroy(_rt); _rt = null; }
            if (_rt == null) _rt = new RenderTexture(width, height, 24, RenderTextureFormat.ARGB32) { antiAliasing = 4, filterMode = FilterMode.Trilinear };
            _previewImg.texture = _rt;
            TMP_Text hint = Widgets.Label(col, T("villagepleb_looks_preview_hint"), 14f, Theme.Muted, TextAlignmentOptions.Center);
            hint.textWrappingMode = TextWrappingModes.Normal;
            Widgets.Place(hint.gameObject, new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(6f, 4f), new Vector2(-6f, 42f));
            const float tick = 22f;
            _armourBox = Widgets.Place(Widgets.Child(col, "ShowArmour"), new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(8f, -8f - tick), new Vector2(-8f, -8f));
            GameUi.Check(_armourBox, Vector2.zero, tick, _showArmour, on => { _showArmour = on; ApplyToClone(); });
            TMP_Text armourLabel = Widgets.Label(_armourBox, T("villagepleb_looks_show_armour"), 16f, Theme.Text, TextAlignmentOptions.MidlineLeft);
            Widgets.Place(armourLabel.gameObject, Vector2.zero, Vector2.one, new Vector2(tick + 8f, 0f), Vector2.zero);
            if (_cam == null)
            {
                var camGO = new GameObject("VP_LooksPreviewCam");
                UnityEngine.Object.DontDestroyOnLoad(camGO);
                _cam = camGO.AddComponent<Camera>();
                _cam.clearFlags = CameraClearFlags.Color;
                _cam.backgroundColor = Color.clear;
                _cam.fieldOfView = 30f;
                _cam.nearClipPlane = 0.1f;
                _cam.farClipPlane = 10f;
                _cam.depth = -2f;
                _cam.enabled = false;
                _cam.cullingMask = CharacterMask();
            }
            _cam.targetTexture = _rt;
            if (_clone == null) MakeClone();
        }

        public void UpdatePerFrame()
        {
            if (_previewImg == null || !_previewImg.gameObject.activeInHierarchy || _cam == null) return;
            if (_openedAt < 0f) _openedAt = Time.unscaledTime;
            // Opened on the look it already has: its picture is brought up to date from this preview.
            if (!_portraitChecked && _clone != null && Time.unscaledTime - _openedAt > 0.8f)
            {
                _portraitChecked = true;
                if (Same(Current, Looks.Of(_companion))) Portraits.Set(Id, Snapshot());
            }
            if (_clone != null && WornHash() != _worn) ApplyToClone();
            bool over = RectTransformUtility.RectangleContainsScreenPoint(_previewImg.rectTransform, Input.mousePosition, null);
            if (!_dragging && Input.GetMouseButtonDown(0) && over && !RectTransformUtility.RectangleContainsScreenPoint(_armourBox, Input.mousePosition, null))
            {
                _dragging = true;
                _lastMouse = Input.mousePosition;
            }
            if (_dragging)
            {
                if (Input.GetMouseButton(0))
                {
                    Vector2 delta = (Vector2)Input.mousePosition - _lastMouse;
                    _rotation = (_rotation + delta.x * DragSensitivity) % 360f;
                    _pan = Mathf.Clamp(_pan - delta.y * PanPerPixel, LowestPan, HighestPan);
                    _lastMouse = Input.mousePosition;
                }
                else _dragging = false;
            }
            float wheel = over ? Input.mouseScrollDelta.y : 0f;
            if (wheel != 0f) _zoomTarget = Mathf.Clamp01(_zoomTarget + Mathf.Clamp(wheel, -1f, 1f) * 0.25f);
            _zoom = Mathf.SmoothDamp(_zoom, _zoomTarget, ref _zoomSpeed, 0.12f, float.PositiveInfinity, Time.unscaledDeltaTime);
            if (_zoomTarget <= 0f && _zoom < 0.002f) _zoom = _zoomSpeed = 0f;
            float t = _zoom * _zoom * (3f - 2f * _zoom);

            Vector3 face = t > 0f ? EyeCentre() : Vector3.zero;
            Vector3 target = Vector3.Lerp(PreviewPos + Vector3.up * 0.9f, face, t) + Vector3.up * _pan;
            float a = _rotation * Mathf.Deg2Rad, distance = Mathf.Lerp(4.3f, 1.1f, t);
            var round = new Vector3(Mathf.Sin(a), 0f, Mathf.Cos(a));
            _cam.transform.position = target + (round + Vector3.up * Mathf.Lerp(0.3f, 0.05f, t)) * distance;
            _cam.transform.LookAt(target);
            if (_faceLight != null)
            {
                _faceLight.enabled = t > 0.02f;
                _faceLight.intensity = 1.1f * t;
                _faceLight.transform.position = face + round * 0.55f + Vector3.up * (0.18f + _pan);
            }

            Color ambient = RenderSettings.ambientLight;
            float intensity = RenderSettings.ambientIntensity;
            AmbientMode mode = RenderSettings.ambientMode;
            try
            {
                RenderSettings.ambientMode = AmbientMode.Flat;
                RenderSettings.ambientLight = new Color(0.45f, 0.4f, 0.35f);
                RenderSettings.ambientIntensity = 1.2f;
                _cam.backgroundColor = BodyReplacerLook.Drawn(_clone) ? PreviewSolid : Color.clear;
                _cam.Render();
            }
            finally
            {
                RenderSettings.ambientMode = mode;
                RenderSettings.ambientLight = ambient;
                RenderSettings.ambientIntensity = intensity;
            }
        }

        public void Teardown()
        {
            _hairPicker?.Destroy();
            _hairPicker = null;
            if (_lightRig != null) UnityEngine.Object.Destroy(_lightRig);
            if (_clone != null) UnityEngine.Object.Destroy(_clone);
            if (_cam != null) UnityEngine.Object.Destroy(_cam.gameObject);
            if (_rt != null) { _rt.Release(); UnityEngine.Object.Destroy(_rt); }
            _lightRig = _clone = null;
            _cloneModel = -1;
            _faceLight = null;
            _eyes = null;
            _cam = null;
            _rt = null;
            _previewImg = null;
        }

        private static bool Same(Looks.Look a, Looks.Look b) =>
            a.Model == b.Model && a.Hair == b.Hair && a.Beard == b.Beard && Mathf.Approximately(a.Skin, b.Skin) && Mathf.Approximately(a.HairTone, b.HairTone)
            && Mathf.Approximately(a.HairShade, b.HairShade) && (a.BrLook ?? "") == (b.BrLook ?? "") && Mathf.Approximately(a.BrMuscle, b.BrMuscle) && (a.BrTattoos ?? "") == (b.BrTattoos ?? "") && a.BrBeard == b.BrBeard && (a.BrHair ?? "") == (b.BrHair ?? "");

        // Head and shoulders of the preview body, from the front, in the preview's studio light: the companion's portrait.
        public Texture2D Snapshot(int size = 160)
        {
            if (_cam == null || _clone == null) return null;
            RenderTexture rt = RenderTexture.GetTemporary(size, size, 24, RenderTextureFormat.ARGB32);
            RenderTexture before = RenderTexture.active, target = _cam.targetTexture;
            Vector3 pos = _cam.transform.position;
            Quaternion rot = _cam.transform.rotation;
            Color back = _cam.backgroundColor;
            bool faceOn = _faceLight != null && _faceLight.enabled;
            float faceIntensity = _faceLight != null ? _faceLight.intensity : 0f;
            Vector3 facePos = _faceLight != null ? _faceLight.transform.position : Vector3.zero;
            Color ambient = RenderSettings.ambientLight;
            float intensity = RenderSettings.ambientIntensity;
            AmbientMode mode = RenderSettings.ambientMode;
            try
            {
                Vector3 face = EyeCentre();
                _cam.transform.position = face + new Vector3(0f, 0.05f, 1f) * 0.95f;
                _cam.transform.LookAt(face - Vector3.up * 0.06f);
                if (_faceLight != null) { _faceLight.enabled = true; _faceLight.intensity = 1.1f; _faceLight.transform.position = face + new Vector3(0f, 0.18f, 0.55f); }
                RenderSettings.ambientMode = AmbientMode.Flat;
                RenderSettings.ambientLight = new Color(0.45f, 0.4f, 0.35f);
                RenderSettings.ambientIntensity = 1.2f;
                _cam.backgroundColor = PortraitBack;
                _cam.targetTexture = rt;
                _cam.Render();
                RenderTexture.active = rt;
                var tex = new Texture2D(size, size, TextureFormat.RGBA32, false) { name = "vp_portrait" };
                tex.ReadPixels(new Rect(0f, 0f, size, size), 0, 0);
                tex.Apply();
                return tex;
            }
            finally
            {
                RenderTexture.active = before;
                _cam.targetTexture = target;
                _cam.transform.SetPositionAndRotation(pos, rot);
                _cam.backgroundColor = back;
                if (_faceLight != null) { _faceLight.enabled = faceOn; _faceLight.intensity = faceIntensity; _faceLight.transform.position = facePos; }
                RenderSettings.ambientMode = mode;
                RenderSettings.ambientLight = ambient;
                RenderSettings.ambientIntensity = intensity;
                RenderTexture.ReleaseTemporary(rt);
            }
        }

        private static int CharacterMask()
        {
            int character = LayerMask.NameToLayer("character"), net = LayerMask.NameToLayer("character_net");
            int mask = 1 << (character < 0 ? 9 : character);
            if (net >= 0) mask |= 1 << net;
            return mask;
        }

        private void MakeClone()
        {
            if (!NewBody()) return;
            _lightRig = new GameObject("VP_LooksPreviewLights");
            UnityEngine.Object.DontDestroyOnLoad(_lightRig);
            _lightRig.transform.position = PreviewPos;
            int mask = CharacterMask();
            AddLight("Key", new Vector3(1.5f, 2.5f, 3.5f), 2f, new Color(1f, 0.92f, 0.82f), 15f, mask);
            AddLight("Fill", new Vector3(-2.5f, 1.5f, 3f), 1.2f, new Color(0.9f, 0.92f, 1f), 15f, mask);
            AddLight("Rim", new Vector3(0f, 3f, -2.5f), 1.2f, new Color(0.95f, 0.88f, 0.78f), 15f, mask);
            AddLight("Bottom", new Vector3(0f, -0.5f, 3f), 0.5f, new Color(0.85f, 0.82f, 0.78f), 10f, mask);
            _faceLight = AddLight("Face", Vector3.zero, 0f, new Color(1f, 0.95f, 0.88f), 1.6f, mask);
            _faceLight.enabled = false;
            ApplyToClone();
        }

        private bool NewBody()
        {
            GameObject prefab = ZNetScene.instance?.GetPrefab("Player");
            if (prefab == null) { VillagePlebsPlugin.Instance?.Warn("Looks preview: the Player prefab was not found"); return false; }
            ZNetView.m_forceDisableInit = true;
            try { _clone = UnityEngine.Object.Instantiate(prefab, PreviewPos, Quaternion.identity); }
            finally { ZNetView.m_forceDisableInit = false; }
            BodyReplacerLook.TreatAsCreature(_clone);
            Rigidbody body = _clone.GetComponent<Rigidbody>();
            if (body != null) UnityEngine.Object.Destroy(body);
            foreach (MonoBehaviour b in _clone.GetComponentsInChildren<MonoBehaviour>(true)) if (!(b is VisEquipment)) b.enabled = false;
            foreach (Renderer r in _clone.GetComponentsInChildren<Renderer>(true)) r.enabled = true;
            foreach (Animator anim in _clone.GetComponentsInChildren<Animator>()) StandStill(anim);
            _cloneModel = Current.Model;
            return true;
        }

        private Vector3 EyeCentre()
        {
            if (_eyes == null || !_eyes.gameObject.activeInHierarchy)
            {
                _eyes = null;
                foreach (SkinnedMeshRenderer r in _clone.GetComponentsInChildren<SkinnedMeshRenderer>())
                    if (r.name.EndsWith("_EYES", StringComparison.Ordinal)) { _eyes = r; break; }
            }
            if (_eyes != null) return _eyes.bounds.center;
            Transform eye = Utils.FindChild(_clone.transform, "EyePos");
            return eye != null ? eye.position : PreviewPos + Vector3.up * 1.65f;
        }

        private Light AddLight(string name, Vector3 pos, float intensity, Color color, float range, int mask)
        {
            var go = new GameObject(name);
            go.transform.SetParent(_lightRig.transform, false);
            go.transform.localPosition = pos;
            Light light = go.AddComponent<Light>();
            light.type = LightType.Point;
            light.intensity = intensity;
            light.color = color;
            light.range = range;
            light.shadows = LightShadows.None;
            light.cullingMask = mask;
            return light;
        }

        private static void StandStill(Animator anim)
        {
            anim.updateMode = AnimatorUpdateMode.Normal;
            anim.cullingMode = AnimatorCullingMode.AlwaysAnimate;
            anim.applyRootMotion = false;
            anim.enabled = true;
            anim.speed = 1f;
            anim.Rebind();
            anim.Update(0f);
            foreach (AnimatorControllerParameter p in anim.parameters)
            {
                if (p.type == AnimatorControllerParameterType.Bool && Array.IndexOf(new[] { "emote_sit", "attach_bed", "sleeping", "sit", "sitting", "crouch", "inBed", "in_bed" }, p.name) >= 0) anim.SetBool(p.name, false);
                if (p.type == AnimatorControllerParameterType.Trigger && Array.IndexOf(new[] { "emote_stop", "wakeup", "wake" }, p.name) >= 0) anim.SetTrigger(p.name);
            }
            foreach (string state in new[] { "Idle", "idle", "Movement", "Move", "Base Layer.Idle", "Base Layer.Movement" })
                if (anim.HasState(0, Animator.StringToHash(state))) anim.Play(Animator.StringToHash(state), 0, 0f);
            for (int i = 0; i < 20; i++) anim.Update(0.1f);
            anim.speed = 0f;
        }

        private void ApplyToClone()
        {
            VisEquipment vis = _clone != null ? _clone.GetComponent<VisEquipment>() : null;
            if (vis == null) return;
            if (_cloneModel != Current.Model)
            {
                _clone.SetActive(false);
                UnityEngine.Object.Destroy(_clone);
                _clone = null;
                if (!NewBody()) return;
                vis = _clone.GetComponent<VisEquipment>();
                if (vis == null) return;
            }
            _eyes = null;
            BodyReplacerLook.Apply(_clone, Current.BrLook, Current.BrMuscle);
            BodyReplacerLook.ApplyTattoos(_clone, Current.BrTattoos);
            BodyReplacerLook.ApplyBeard(_clone, Current.BrBeard);
            BodyReplacerLook.ApplyCustomHair(_clone, Current.BrHair);
            CurrentModelIndex?.SetValue(vis, -1);
            vis.SetModel(Current.Model);
            vis.SetHairItem(Current.Hair);
            vis.SetBeardItem(Current.Model == 0 ? Current.Beard : 0);
            vis.SetSkinColor(Looks.SkinColor(Current.Skin));
            vis.SetHairColor(Looks.HairColor(Current.HairTone, Current.HairShade));
            ZDO z = Companion.Zdo(_companion);
            ZDO armour = _showArmour ? z : null;
            vis.SetHelmetItem(Worn(armour, ZDOVars.s_helmetItem));
            vis.SetChestItem(Worn(armour, ZDOVars.s_chestItem));
            vis.SetLegItem(Worn(armour, ZDOVars.s_legItem));
            vis.SetShoulderItem(Worn(armour, ZDOVars.s_shoulderItem), Worn(armour, ZDOVars.s_shoulderItemVariant), Worn(armour, ZDOVars.s_shoulderItemQuality));
            vis.SetUtilityItem(Worn(armour, ZDOVars.s_utilityItem));
            vis.SetRightItem(Worn(z, ZDOVars.s_rightItem), Worn(z, ZDOVars.s_rightItemQuality));
            vis.SetLeftItem(Worn(z, ZDOVars.s_leftItem), Worn(z, ZDOVars.s_leftItemVariant), Worn(z, ZDOVars.s_leftItemQuality));
            vis.SetRightBackItem(Worn(z, ZDOVars.s_rightBackItem), Worn(z, ZDOVars.s_rightBackItemQuality));
            vis.SetLeftBackItem(Worn(z, ZDOVars.s_leftBackItem), Worn(z, ZDOVars.s_leftBackItemVariant), Worn(z, ZDOVars.s_leftBackItemQuality));
            _worn = WornHash();
            UpdateVisuals?.Invoke(vis, null);
            Animator anim = _clone.GetComponentInChildren<Animator>(true);
            if (anim != null) { anim.Update(0f); anim.speed = 0f; }
            int layer = LayerMask.NameToLayer("character");
            foreach (Transform t in _clone.GetComponentsInChildren<Transform>(true)) t.gameObject.layer = layer < 0 ? 9 : layer;
        }

        private static int Worn(ZDO z, int key) => z?.GetInt(key, 0) ?? 0;

        private static readonly int[] WornKeys =
        {
            ZDOVars.s_helmetItem, ZDOVars.s_chestItem, ZDOVars.s_legItem, ZDOVars.s_shoulderItem, ZDOVars.s_shoulderItemVariant, ZDOVars.s_shoulderItemQuality,
            ZDOVars.s_utilityItem, ZDOVars.s_rightItem, ZDOVars.s_rightItemQuality, ZDOVars.s_leftItem, ZDOVars.s_leftItemVariant, ZDOVars.s_leftItemQuality,
            ZDOVars.s_rightBackItem, ZDOVars.s_rightBackItemQuality, ZDOVars.s_leftBackItem, ZDOVars.s_leftBackItemVariant, ZDOVars.s_leftBackItemQuality,
        };

        private int WornHash()
        {
            ZDO z = Companion.Zdo(_companion);
            int h = 17;
            foreach (int key in WornKeys) h = h * 31 + Worn(z, key);
            return h;
        }

        private void SetModel(int model)
        {
            Current.Model = model;
            Refresh();
            ApplyToClone();
        }

        private void StepHair(int by) { Current.Hair = Looks.Step(Current.Hair, Looks.Hairs(), by); Refresh(); ApplyToClone(); }
        private void StepBeard(int by) { Current.Beard = Looks.Step(Current.Beard, Looks.Beards(), by); Refresh(); ApplyToClone(); }

        private void SetAll(Looks.Look look)
        {
            look.BrLook = Current.BrLook;
            look.BrMuscle = Current.BrMuscle;
            look.BrTattoos = Current.BrTattoos;
            look.BrBeard = Current.BrBeard;
            look.BrHair = Current.BrHair;
            Current = look;
            Refresh();
            ApplyToClone();
        }

        private void Refresh()
        {
            SetToggle(_maleBtn, Current.Model == 0);
            SetToggle(_femaleBtn, Current.Model == 1);
            bool einherjar = BodyReplacerLook.Replaces(Current.Model);
            if (_vanillaLook != null) _vanillaLook.SetActive(!einherjar);
            if (_einherjarLook != null) _einherjarLook.SetActive(einherjar);
            if (_beardGroup != null) _beardGroup.SetActive(Current.Model == 0);
            if (_einherjarBeardGroup != null) _einherjarBeardGroup.SetActive(BodyReplacerLook.HasBeards(Current.Model));
            if (_muscleSection != null) _muscleSection.SetActive(BodyReplacerLook.HasMuscle(Current.Model));
            if (_bodyShapeGroup != null) _bodyShapeGroup.SetActive(BodyReplacerLook.HasBodyShape(Current.Model));
            if (_hairName != null) _hairName.text = Words.Show(Looks.StyleName(Current.Hair, Looks.Hairs()));
            if (_beardName != null) _beardName.text = Words.Show(Looks.StyleName(Current.Beard, Looks.Beards()));
            _skinSlider?.SetValueWithoutNotify(Current.Skin);
            _hairToneSlider?.SetValueWithoutNotify(Current.HairTone);
            _hairShadeSlider?.SetValueWithoutNotify(Current.HairShade);
            if (_einherjarHairName != null)
            {
                string[] names = BodyReplacerLook.HairStyleNames(Current.Model);
                string[] addedNames = BodyReplacerLook.CustomHairNames(Current.Model);
                int shown = EinherjarHairIndex(BodyReplacerLook.HairStyleIds(Current.Model), BodyReplacerLook.CustomHairs(Current.Model));
                _einherjarHairName.text = shown < names.Length ? names[shown] : shown - names.Length < addedNames.Length ? addedNames[shown - names.Length] : "";
            }
            if (_einherjarBeardName != null)
            {
                string[] beards = BodyReplacerLook.BeardNames();
                _einherjarBeardName.text = Current.BrBeard > 0 && Current.BrBeard < beards.Length ? beards[Current.BrBeard] : Localization.instance.Localize("$menu_none");
            }
            if (_tattooDesigns.Length > 0)
            {
                string[] inks = BodyReplacerLook.TattooInkNames();
                for (int area = 0; area < _tattooDesigns.Length; area++)
                {
                    string[] designs = BodyReplacerLook.TattooDesignNames(area);
                    int design = BodyReplacerLook.TattooDesign(Current.BrTattoos, area), ink = BodyReplacerLook.TattooInk(Current.BrTattoos, area);
                    _tattooDesigns[area].text = design > 0 && design <= designs.Length ? designs[design - 1] : Localization.instance.Localize("$menu_none");
                    _tattooInks[area].text = design > 0 && ink >= 0 && ink < inks.Length ? inks[ink] : "";
                }
            }
            if (_hairPicker != null)
            {
                float hue = BodyReplacerLook.Get(Current.BrLook, "HairHue"), saturation = BodyReplacerLook.Get(Current.BrLook, "HairSaturation"), value = BodyReplacerLook.Get(Current.BrLook, "HairValue");
                _hairPicker.Set(hue, saturation, value);
                _hairSwatch.color = Color.HSVToRGB(hue, saturation, value);
                _hairReset.gameObject.SetActive(BodyReplacerLook.Get(Current.BrLook, "CustomHair") > 0f);
            }
            if (_lookContent != null) LayoutRebuilder.MarkLayoutForRebuild(_lookContent);
        }

        private static GameObject Stack(GameObject go, float spacing, RectOffset padding)
        {
            VerticalLayoutGroup layout = go.AddComponent<VerticalLayoutGroup>();
            layout.spacing = spacing;
            layout.padding = padding;
            layout.childControlWidth = true;
            layout.childControlHeight = true;
            layout.childForceExpandWidth = true;
            layout.childForceExpandHeight = false;
            return go;
        }

        private static void Height(GameObject go, float h)
        {
            LayoutElement element = go.GetComponent<LayoutElement>() ?? go.AddComponent<LayoutElement>();
            element.minHeight = h;
            element.preferredHeight = h;
        }

        private static Transform Section(Transform parent, string text, string name)
        {
            GameObject section = Stack(Widgets.Child(parent, name), 4f, new RectOffset(0, 0, 0, 0));
            Image bar = Widgets.Box(section.transform, name + "Label", Theme.Header);
            bar.raycastTarget = false;
            Height(bar.gameObject, HeaderHeight);
            Image accent = Widgets.Box(bar.transform, "Accent", Theme.Accent);
            accent.raycastTarget = false;
            Widgets.Place(accent.gameObject, new Vector2(0f, 0f), new Vector2(0f, 1f), Vector2.zero, new Vector2(4f, 0f));
            TMP_Text label = Widgets.Label(bar.transform, text, 15f, Theme.TextBright, TextAlignmentOptions.MidlineLeft, bold: true);
            Widgets.Place(label.gameObject, Vector2.zero, Vector2.one, new Vector2(12f, 0f), new Vector2(-6f, 0f));
            return section.transform;
        }

        private static Transform Row(Transform parent, float h)
        {
            GameObject row = Widgets.Child(parent, "Row");
            Height(row, h);
            HorizontalLayoutGroup layout = row.AddComponent<HorizontalLayoutGroup>();
            layout.spacing = 4f;
            layout.childControlWidth = true;
            layout.childControlHeight = true;
            layout.childForceExpandWidth = true;
            layout.childForceExpandHeight = true;
            return row.transform;
        }

        private static Button ToggleButton(Transform parent, string label, Action onClick)
        {
            Button button = Widgets.Button(parent, label, 17f);
            button.gameObject.AddComponent<LayoutElement>().flexibleWidth = 1f;
            button.onClick.AddListener(() => onClick());
            return button;
        }

        private static void SetToggle(Button btn, bool on)
        {
            if (btn == null) return;
            btn.colors = on ? Theme.Colors(Theme.Selected, Theme.Selected, Theme.ButtonPressed, Theme.ButtonDisabled)
                            : Theme.Colors(Theme.Button, Theme.ButtonHover, Theme.ButtonPressed, Theme.ButtonDisabled);
            TMP_Text text = btn.GetComponentInChildren<TMP_Text>(true);
            if (text != null) text.color = on ? Theme.TextBright : Theme.Text;
        }

        private static TMP_Text PickerRow(Transform parent, Action prev, Action next)
        {
            GameObject row = Widgets.Child(parent, "PickerRow");
            Height(row, PickerRowHeight);
            HorizontalLayoutGroup layout = row.AddComponent<HorizontalLayoutGroup>();
            layout.spacing = 4f;
            layout.childAlignment = TextAnchor.MiddleCenter;
            layout.childForceExpandWidth = false;
            layout.childForceExpandHeight = true;
            layout.childControlWidth = true;
            layout.childControlHeight = true;
            Button p = Widgets.Button(row.transform, "<", 20f);
            LayoutElement pe = p.gameObject.AddComponent<LayoutElement>();
            pe.minWidth = pe.preferredWidth = PickerRowHeight;
            p.onClick.AddListener(() => prev());
            Image nameBox = Widgets.Box(row.transform, "Name", Theme.Track, Theme.FieldOutline);
            nameBox.raycastTarget = false;
            nameBox.gameObject.AddComponent<LayoutElement>().flexibleWidth = 1f;
            TMP_Text name = Widgets.Label(nameBox.transform, "", 16f, Theme.Text, TextAlignmentOptions.Center);
            Widgets.Fill(name.gameObject, 4f);
            Button n = Widgets.Button(row.transform, ">", 20f);
            LayoutElement ne = n.gameObject.AddComponent<LayoutElement>();
            ne.minWidth = ne.preferredWidth = PickerRowHeight;
            n.onClick.AddListener(() => next());
            return name;
        }

        private static Slider MakeSlider(Transform parent, float value, Action<float> onChange)
        {
            Slider slider = Widgets.Slider(parent);
            Height(slider.gameObject, SliderHeight);
            slider.wholeNumbers = false;
            slider.minValue = 0f;
            slider.maxValue = 1f;
            slider.value = value;
            slider.onValueChanged.AddListener(v => onChange(v));
            return slider;
        }
    }
}
