using System.Collections.Generic;
using System.Linq;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using static VillagePlebs.Words;
using Object = UnityEngine.Object;

namespace VillagePlebs
{
    // The chart that opens with the Sail order: the world map (as far as you have explored it), the ship, and the course to wherever you click.
    // Drag to move it, scroll to zoom. Set sail sends the companion off; closing it any other way brings it back down from the helm.
    // Opened again while it is under way, it changes the course instead: somewhere new, or home, or it keeps going as it was.
    // Down the side, your docks: click one (in the list or on the chart) to sail there, or name the spot you clicked and add it as one.
    internal static class SeaMap
    {
        private const float Side = 250f;
        private static RectTransform _dockList;
        private static readonly List<RectTransform> DockMarks = new List<RectTransform>();
        private static List<Docks.Dock> _docks = new List<Docks.Dock>();
        private static Docks.Dock _dock;
        private static string _newDock = "";
        private static Button _addDock, _removeDock;

        private static RectTransform _window, _view, _shipMark, _destMark, _youMark;
        private static RawImage _map;
        private static Material _material;
        private static ChartLine _line;
        private static TMP_Text _status;
        private static Button _sail, _homeButton;
        private static Humanoid _who;
        private static ZDO _body;
        private static long _id;
        private static Ship _ship;
        private static Vector3 _center;
        private static float _zoom = 0.08f, _width, _height;
        private static Vector3? _dest;
        private static SeaRoute _route;
        private static bool _planning, _underway, _toHome;
        private static string _why, _shown;
        private static int _ask;

        public static bool IsOpen => _window != null;
        public static bool IsOpenFor(Humanoid c) => IsOpen && _who == c;
        internal static SeaRoute Route => _route;

        private static float Span => Minimap.instance.m_textureSize * Minimap.instance.m_pixelSize;
        private static float PerPixel => _zoom * Span / _height;

        // At the helm, before setting sail.
        public static void Open(Player p, Humanoid c, Ship ship)
        {
            if (Minimap.instance == null || !GameUi.Ready()) { Sailing.StepDown(c, ship); return; }
            Build(c, Companion.Zdo(c), ship, false);
        }

        // Away by ship (under way, or waiting aboard): a new course. Returns what to tell you if it cannot open.
        public static string OpenCourse(Player p, Humanoid c)
        {
            if (Minimap.instance == null || !GameUi.Ready()) return null;
            if (Sailing.ShipAt(c, out _) == null) return P("villagepleb_sail_busy", Companion.NameOf(c));
            Build(c, Companion.Zdo(c), Sailing.ShipOf(c), true);
            return null;
        }

        // The same for one too far away to be loaded (chosen on the command wheel), from its record.
        public static string OpenCourse(Player p, ZDO body)
        {
            if (Minimap.instance == null || !GameUi.Ready() || body == null) return null;
            string name = body.GetString(Keys.Name, "");
            if (body.GetLong(Keys.Master, 0L) != p.GetPlayerID()) return P("villagepleb_menu_owner_orders");
            if (!Sailing.Away(body) || Sailing.ShipAt(body, out _) == null) return P("villagepleb_sail_busy", name);
            Build(null, body, Sailing.ShipOf(body), true);
            return null;
        }

        private static string WhoName => _who != null ? Companion.NameOf(_who) : _body?.GetString(Keys.Name, "") ?? "";
        private static Vector3? HomeAt => _who != null ? (Work.HasHome(_who) ? Work.Center(_who) : (Vector3?)null) : Remote.Home(_body);
        private static bool Alive => _who != null ? !_who.IsDead() : _body != null && ZDOMan.instance != null && ZDOMan.instance.GetZDO(_body.m_uid) != null;

        private static void Build(Humanoid c, ZDO body, Ship ship, bool underway)
        {
            Close();
            _who = c; _body = body; _id = Sailing.IdOf(body); _ship = ship; _underway = underway; _dest = null; _route = null; _planning = false; _toHome = false; _why = null; _shown = null;
            _center = Here() ?? (c != null ? c.transform.position : body.GetPosition());

            Vector2 screen = GameUi.ScreenSize;
            float w = Mathf.Min(1180f, screen.x - 80f), h = Mathf.Min(820f, screen.y - 80f);
            _window = GameUi.Window("VillagePlebs_SeaMap", new Vector2(w, h));
            GameUi.TitleBar(_window, Show(P(underway ? "villagepleb_sail_course_title" : "villagepleb_sail_map_title", WhoName)), Cancel);
            _width = w - 32f - Side - 12f;
            _height = h - 70f - 96f;
            _dock = null;
            _newDock = "";
            _zoom = Mathf.Clamp(2400f / Span, Minimap.instance.m_minZoom, Minimap.instance.m_maxZoom);

            GameUi.Box(_window, new Vector2(13f, -67f), new Vector2(_width + 6f, _height + 6f));
            _view = GameUi.Rect(_window, "Chart", new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(16f, -70f), new Vector2(_width, _height));
            _view.gameObject.AddComponent<RectMask2D>();
            _map = _view.gameObject.AddComponent<RawImage>();
            _material = Object.Instantiate(Minimap.instance.m_mapImageLarge.material);
            _map.material = _material;
            _map.texture = Minimap.instance.m_mapTexture;
            _map.raycastTarget = true;
            _view.gameObject.AddComponent<ChartInput>();

            RectTransform lines = GameUi.Rect(_view, "Course", new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(_width, _height));
            _line = lines.gameObject.AddComponent<ChartLine>();
            _line.raycastTarget = false;
            _youMark = Marker("You", Minimap.instance.m_largeMarker != null ? Minimap.instance.m_largeMarker.GetComponent<Image>()?.sprite : null, 28f, Color.white);
            _shipMark = Marker("Ship", Minimap.instance.m_largeShipMarker != null ? Minimap.instance.m_largeShipMarker.GetComponent<Image>()?.sprite : null, 36f, Color.white);
            _destMark = Marker("Destination", Pin(Minimap.PinType.Icon3), 30f, new Color(1f, 0.55f, 0.35f));
            BuildDocks(16f + _width + 12f);

            float by = -(70f + _height + 18f);
            if (underway)
            {
                _status = GameUi.Label(_window, "", new Vector2(16f, by), new Vector2(w - 632f, 64f), 18f, GameUi.Text, null, TextAlignmentOptions.MidlineLeft);
                _homeButton = GameUi.Button(_window, Show(P("villagepleb_sail_home")), new Vector2(w - 592f, by - 6f), new Vector2(180f, 46f), SailHome, HomeAt != null, 18f);
                _sail = GameUi.Button(_window, Show(P("villagepleb_sail_change")), new Vector2(w - 404f, by - 6f), new Vector2(200f, 46f), SetSail, false, 20f);
                GameUi.Button(_window, Show(P(body != null && !Sailing.Underway(body) ? "villagepleb_sail_stay_aboard" : "villagepleb_sail_keep_going")), new Vector2(w - 196f, by - 6f), new Vector2(180f, 46f), Cancel, true, 18f);
            }
            else
            {
                _status = GameUi.Label(_window, "", new Vector2(16f, by), new Vector2(w - 632f, 64f), 18f, GameUi.Text, null, TextAlignmentOptions.MidlineLeft);
                _homeButton = GameUi.Button(_window, Show(P("villagepleb_sail_home")), new Vector2(w - 600f, by - 6f), new Vector2(180f, 46f), SailHome, HomeAt != null, 18f);
                _sail = GameUi.Button(_window, Show(P("villagepleb_sail_set_sail")), new Vector2(w - 412f, by - 6f), new Vector2(200f, 46f), SetSail, false, 20f);
                GameUi.Button(_window, Show(P("villagepleb_sail_step_down")), new Vector2(w - 204f, by - 6f), new Vector2(188f, 46f), Cancel, true, 18f);
            }

            GameUi.IsOpen = () => IsOpen;
            GameUi_EscapeCloses.Close = Cancel;
            SeaChart.Ensure();
            Refresh();
        }

        // ---- docks ----

        private static void BuildDocks(float x)
        {
            GameUi.Box(_window, new Vector2(x - 3f, -67f), new Vector2(Side + 6f, _height + 6f));
            TMP_Text head = GameUi.Label(_window, Show(P("villagepleb_sail_docks")), new Vector2(x + 8f, -76f), new Vector2(Side - 16f, 28f), 20f, GameUi.Warm, GameUi.Norse, TextAlignmentOptions.Center);
            head.fontStyle = FontStyles.Bold;
            _dockList = GameUi.Rect(_window, "Docks", new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(x + 8f, -110f), new Vector2(Side - 16f, _height - 40f - 150f));
            float bottom = -70f - _height;
            GameUi.TextField(_window, new Vector2(x + 8f, bottom + 146f), new Vector2(Side - 16f, 36f), "", 24, v => _newDock = v, null, 16f, Show(P("villagepleb_sail_dock_placeholder")));
            _addDock = GameUi.Button(_window, Show(P("villagepleb_sail_dock_add")), new Vector2(x + 8f, bottom + 102f), new Vector2(Side - 16f, 38f), AddDock, false, 17f);
            _removeDock = GameUi.Button(_window, Show(P("villagepleb_sail_dock_remove")), new Vector2(x + 8f, bottom + 56f), new Vector2(Side - 16f, 38f), RemoveDock, false, 17f);
            ListDocks();
        }

        // The list, nearest the ship first, and a marker on the chart for each.
        private static void ListDocks()
        {
            if (_dockList == null) return;
            foreach (Transform t in _dockList.Cast<Transform>().ToList()) Object.Destroy(t.gameObject);
            foreach (RectTransform m in DockMarks) if (m != null) Object.Destroy(m.gameObject);
            DockMarks.Clear();
            Vector3 from = Here() ?? _center;
            _docks = Docks.All(Player.m_localPlayer).OrderBy(d => Vector3.Distance(new Vector3(d.At.x, 0f, d.At.z), new Vector3(from.x, 0f, from.z))).ToList();
            float rowH = 38f, w = _dockList.sizeDelta.x;
            int rows = Mathf.Max(1, Mathf.FloorToInt(_dockList.sizeDelta.y / rowH));
            if (_docks.Count == 0)
                GameUi.Label(_dockList, Show(P("villagepleb_sail_no_docks")), Vector2.zero, new Vector2(w, 80f), 15f, GameUi.Dim);
            for (int i = 0; i < _docks.Count && i < rows; i++)
            {
                Docks.Dock d = _docks[i];
                float km = Vector3.Distance(new Vector3(d.At.x, 0f, d.At.z), new Vector3(from.x, 0f, from.z)) / 1000f;
                GameUi.Tab(_dockList, $"{d.Name}  <size=70%>{km:0.0} km</size>", new Vector2(0f, -i * rowH), new Vector2(w, rowH - 4f), _dock == d, () => PickDock(d), 16f);
            }
            foreach (Docks.Dock d in _docks)
            {
                RectTransform m = Marker("Dock", Wheel.Icon("dock_icon") ?? Pin(Minimap.PinType.Icon4), 30f, Color.white);
                TMP_Text label = GameUi.Label(m, d.Name, new Vector2(96f, 0f), new Vector2(160f, 22f), 15f, GameUi.Warm, null, TextAlignmentOptions.MidlineLeft, new Vector2(0.5f, 0.5f));
                label.textWrappingMode = TextWrappingModes.NoWrap;
                m.name = d.Name;
                DockMarks.Add(m);
            }
        }

        // A dock to sail to: a free berth in it, and the course there.
        private static void PickDock(Docks.Dock d)
        {
            _dock = d;
            _dest = Docks.Berth(d.At, Sailing.Hull(_ship), _ship, Sailing.BerthsTaken(_id));
            _route = null;
            _why = null;
            _planning = false;
            _toHome = false;
            _ask++;
            ListDocks();
        }

        private static void AddDock()
        {
            if (_dest == null) return;
            Docks.Dock d = Docks.Add(Player.m_localPlayer, _newDock, _dest.Value);
            if (d == null) return;
            VillagePlebsPlugin.Tell(P("villagepleb_sail_dock_saved", d.Name));
            _dock = d;
            ListDocks();
        }

        private static void RemoveDock()
        {
            if (_dock == null) return;
            Docks.Remove(Player.m_localPlayer, _dock);
            _dock = null;
            ListDocks();
        }

        private static void Enable(Button b, bool on)
        {
            if (b == null || b.interactable == on) return;
            b.interactable = on;
            GameUi.Label(b).color = on ? GameUi.Text : GameUi.Dim;
        }

        private static RectTransform Marker(string name, Sprite sprite, float size, Color color)
        {
            RectTransform r = GameUi.Rect(_view, name, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(size, size));
            Image img = r.gameObject.AddComponent<Image>();
            img.sprite = sprite;
            img.color = color;
            img.raycastTarget = false;
            r.gameObject.SetActive(false);
            return r;
        }

        private static Sprite Pin(Minimap.PinType type)
        {
            foreach (Minimap.SpriteData icon in Minimap.instance.m_icons) if (icon.m_name == type) return icon.m_icon;
            return null;
        }

        // Where the ship is now (loaded or not), and which way it heads.
        private static Vector3? Here() => _ship != null ? _ship.transform.position : Sailing.ShipAt(_body, out _);

        private static float Heading()
        {
            if (_ship != null) return _ship.transform.eulerAngles.y;
            Sailing.ShipAt(_body, out float yaw);
            return yaw;
        }

        public static void Close()
        {
            if (_window != null) Object.Destroy(_window.gameObject);
            if (_material != null) Object.Destroy(_material);
            _window = null; _material = null; _map = null; _line = null; _who = null; _body = null; _id = 0L; _ship = null; _homeButton = null;
            _dockList = null; _addDock = _removeDock = null; _dock = null;
            DockMarks.Clear();
            GameUi.IsOpen = () => VillagePlebsPlugin.MenuOpen;
            GameUi_EscapeCloses.Close = VillagePlebsPlugin.CloseFromEscape;
        }

        // Before setting sail it gets up from the helm; away, it just goes on as it was (sailing, or waiting aboard).
        private static void Cancel()
        {
            Humanoid c = _who;
            Ship s = _ship;
            bool underway = _underway;
            Close();
            if (!underway) Sailing.StepDown(c, s);
        }

        private static void SetSail()
        {
            if (_route == null || _planning) return;
            Humanoid c = _who;
            SeaRoute route = _route;
            string dock = _toHome ? null : _dock?.Name, name = WhoName;
            bool ok = _underway ? Sailing.ChangeCourse(_body, c, route, _toHome, dock) : Sailing.SetSail(c, _ship, route, _toHome, dock);
            if (!ok) { VillagePlebsPlugin.Tell(P("villagepleb_menu_busy")); return; }
            bool underway = _underway;
            Close();
            VillagePlebsPlugin.Tell(P(underway ? "villagepleb_sail_tell_changed" : "villagepleb_sail_tell_set", name, (route.Length / 1000f).ToString("0.0")));
        }

        // Go home, given while it is away by ship.
        public static void HeadHome() { if (IsOpen) SailHome(); }

        // Plans the way home and goes as soon as it has it.
        private static void SailHome()
        {
            if (HomeAt == null) { VillagePlebsPlugin.Tell(P("villagepleb_sail_no_home", WhoName)); return; }
            _dest = HomeAt;
            _route = null;
            _why = null;
            _planning = false;
            _toHome = true;
            _dock = null;
            _ask++;
        }

        public static void Tick()
        {
            if (!IsOpen) return;
            if ((_who == null && _body == null) || !Alive || Player.m_localPlayer == null) { Cancel(); return; }
            if (_underway ? !Sailing.Away(_body) || Here() == null : _ship == null) { Cancel(); return; }
            if (_underway && _ship == null) _ship = Sailing.ShipOf(_body);
            if (_dest != null && _route == null && !_planning && _why == null && SeaChart.Ready) Plan();
            string status;
            if (!SeaChart.Ready) { SeaChart.Ensure(); status = P("villagepleb_sail_charting", Mathf.RoundToInt(SeaChart.Progress * 100f)); }
            else if (_dest == null) status = P(_underway ? "villagepleb_sail_pick_course" : HomeAt != null ? "villagepleb_sail_pick_or_home" : "villagepleb_sail_pick", WhoName);
            else if (_planning) status = P("villagepleb_sail_plotting");
            else if (_why != null) status = P(_why);
            else if (_route != null) status = P(_underway ? "villagepleb_sail_course_new" : "villagepleb_sail_course", (_route.Length / 1000f).ToString("0.0"), Mathf.Max(1, Mathf.RoundToInt(_route.Length / 8f / 60f)));
            else status = "";
            if (status != _shown) { _shown = status; _status.text = Show(status); }
            bool ready = _route != null && !_planning;
            if (_sail.interactable != ready) { _sail.interactable = ready; GameUi.Label(_sail).color = ready ? GameUi.Text : GameUi.Dim; }
            Enable(_addDock, _dest != null && _dock == null && !_toHome);
            Enable(_removeDock, _dock != null);
            Refresh();
        }

        private static void Plan()
        {
            Vector3? from = Here();
            if (from == null) return;
            _planning = true;
            int ask = ++_ask;
            float hull = Sailing.Hull(_ship);
            SeaChart.Plan(from.Value, _dest.Value, hull, (route, why) =>
            {
                if (ask != _ask || !IsOpen) return;
                _planning = false;
                _route = route;
                _why = route == null ? why : null;
                if (route != null && _toHome) SetSail();
            });
        }

        internal static void Clicked(Vector2 local)
        {
            // On (or right by) a dock's marker: that dock.
            foreach (Docks.Dock d in _docks)
                if (Vector2.Distance(ToLocal(d.At.x, d.At.z), local) < 16f) { PickDock(d); return; }
            if (_dock != null) { _dock = null; ListDocks(); }
            _dest = ToWorld(local);
            _route = null;
            _why = null;
            _planning = false;
            _toHome = false;
            _ask++;
        }

        internal static void Pan(Vector2 pixels)
        {
            _center -= new Vector3(pixels.x, 0f, pixels.y) * PerPixel;
            float half = Span * 0.5f;
            _center.x = Mathf.Clamp(_center.x, -half, half);
            _center.z = Mathf.Clamp(_center.z, -half, half);
        }

        internal static void Zoom(float scroll, Vector2 local)
        {
            Vector3 before = ToWorld(local);
            _zoom = Mathf.Clamp(_zoom * (scroll > 0f ? 0.8f : 1.25f), Minimap.instance.m_minZoom, Minimap.instance.m_maxZoom);
            _center += before - ToWorld(local);
        }

        internal static Vector2 ToLocal(float x, float z) => new Vector2((x - _center.x) / PerPixel, (z - _center.z) / PerPixel);
        private static Vector3 ToWorld(Vector2 local) => new Vector3(_center.x + local.x * PerPixel, 0f, _center.z + local.y * PerPixel);

        private static void Place(RectTransform mark, Vector3? at, float yaw = float.NaN)
        {
            if (at == null) { mark.gameObject.SetActive(false); return; }
            Vector2 p = ToLocal(at.Value.x, at.Value.z);
            bool inside = Mathf.Abs(p.x) < _width * 0.5f + 20f && Mathf.Abs(p.y) < _height * 0.5f + 20f;
            mark.gameObject.SetActive(inside);
            mark.anchoredPosition = p;
            if (!float.IsNaN(yaw)) mark.localEulerAngles = new Vector3(0f, 0f, -yaw);
        }

        private static void Refresh()
        {
            Minimap mm = Minimap.instance;
            if (mm == null || _map == null) return;
            if (_map.texture != mm.m_mapTexture) _map.texture = mm.m_mapTexture;
            float mx = _center.x / Span + 0.5f, my = _center.z / Span + 0.5f;
            var uv = new Rect(0f, 0f, _zoom * _width / _height, _zoom) { center = new Vector2(mx, my) };
            _map.uvRect = uv;
            _material.SetFloat("_zoom", _zoom);
            _material.SetFloat("_pixelSize", 200f / _zoom);
            _material.SetVector("_mapCenter", _center);
            Place(_shipMark, Here(), Heading());
            Place(_youMark, Player.m_localPlayer.transform.position, Player.m_localPlayer.transform.eulerAngles.y);
            Place(_destMark, _route != null ? _route.End : _dest);
            for (int i = 0; i < DockMarks.Count && i < _docks.Count; i++) Place(DockMarks[i], _docks[i].At);
            _line.SetVerticesDirty();
        }
    }

    internal sealed class ChartInput : MonoBehaviour, IPointerClickHandler, IDragHandler, IScrollHandler
    {
        public void OnPointerClick(PointerEventData e)
        {
            if (!e.dragging && e.button == PointerEventData.InputButton.Left && Local(e, out Vector2 p)) SeaMap.Clicked(p);
        }

        public void OnDrag(PointerEventData e)
        {
            float k = transform.lossyScale.x;
            SeaMap.Pan(e.delta / (k > 0f ? k : 1f));
        }

        public void OnScroll(PointerEventData e)
        {
            if (Mathf.Abs(e.scrollDelta.y) > 0.01f && Local(e, out Vector2 p)) SeaMap.Zoom(e.scrollDelta.y, p);
        }

        private bool Local(PointerEventData e, out Vector2 p)
        {
            var r = (RectTransform)transform;
            bool ok = RectTransformUtility.ScreenPointToLocalPointInRectangle(r, e.position, e.pressEventCamera ?? e.enterEventCamera, out p);
            p -= r.rect.center;
            return ok;
        }
    }

    // The planned course drawn over the chart.
    internal sealed class ChartLine : MaskableGraphic
    {
        private static readonly Color32 Shade = new Color32(0, 0, 0, 150), Ink = new Color32(255, 225, 150, 255);

        protected override void OnPopulateMesh(VertexHelper vh)
        {
            vh.Clear();
            SeaRoute r = SeaMap.Route;
            if (r == null || r.Count < 2) return;
            var pts = new List<Vector2>(r.Count);
            for (int i = 0; i < r.Count; i++) pts.Add(SeaMap.ToLocal(r.X[i], r.Z[i]));
            foreach (var (width, color) in new[] { (4f, Shade), (2f, Ink) })
                for (int i = 1; i < pts.Count; i++)
                {
                    Vector2 a = pts[i - 1], b = pts[i], d = b - a;
                    if (d.sqrMagnitude < 0.01f) continue;
                    d.Normalize();
                    Vector2 side = new Vector2(-d.y, d.x) * width;
                    a -= d * width; b += d * width;
                    int at = vh.currentVertCount;
                    vh.AddVert(a - side, color, Vector2.zero); vh.AddVert(a + side, color, Vector2.zero);
                    vh.AddVert(b + side, color, Vector2.zero); vh.AddVert(b - side, color, Vector2.zero);
                    vh.AddTriangle(at, at + 1, at + 2); vh.AddTriangle(at, at + 2, at + 3);
                }
        }
    }
}
