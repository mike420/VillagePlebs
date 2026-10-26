using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using Object = UnityEngine.Object;
using static VillagePlebs.Shared.Translations;

namespace VillagePlebs
{
    public static class Menu
    {
        public static bool IsOpen => VillagePlebsPlugin.MenuOpen;
        public static void Close() => VillagePlebsPlugin.CloseFromEscape();
    }

    public partial class VillagePlebsPlugin
    {
        private enum Tab { Status, Stats, Missions, Orders, Home, Gear, Looks, Companions }

        internal static bool MenuOpen;
        internal static float MenuClosedAt = -1f;
        private Humanoid _shown;
        private Tab _tab = Tab.Status;
        private bool _renaming, _focusField, _menuWarned;
        private Action _pending;
        private string _nameField = "", _note = "";
        private long _forgetArmed;
        private int _hirePrice = -1;
        private string _hireCoin;
        private LooksPanel _looks;

        private const float WindowW = 880f, Pad = 26f, CardPad = 14f, CardGap = 10f;
        private static readonly Color HealthRed = new Color(0.9f, 0.25f, 0.18f), StaminaYellow = new Color(1f, 0.78f, 0.22f), CarryBrown = new Color(0.72f, 0.52f, 0.3f), EitrPurple = new Color(0.62f, 0.46f, 1f);

        private RectTransform _window, _list, _card, _oldWindow;
        private CanvasGroup _fresh;
        private float _lastRebuild;
        private long _jobFor;
        private string _jobShown, _jobWaiting, _stopShown, _stopWaiting;
        private float _stopSince;
        private float _jobSince;
        private RectTransform _jobPanel, _jobBar;
        private TMP_Text _jobText;
        private Button _jobStop;
        private const float JobPanelH = 56f, JobGap = 8f;
        private TMP_InputField _field;
        private string _drawn = "", _scrollKey = "";
        private readonly Dictionary<string, float> _scrolls = new Dictionary<string, float>();
        private bool _restoreAgain;
        private float _nextCheck, _nextLive, _openedAt;
        private readonly List<Action> _live = new List<Action>();
        private bool _make;
        private readonly StringBuilder _sig = new StringBuilder();
        private Transform _in;
        private float _y, _left, _width, _listY, _cardW;

        internal void OpenHire(Player player, int price, string coin) => OpenMenuFor(player, null, price, coin);

        internal void OpenMenuFor(Player player, Humanoid companion, int hirePrice = -1, string hireCoin = null)
        {
            if (!GameUi.Ready()) return;
            if (InventoryGui.IsVisible()) InventoryGui.instance.Hide();
            if (!MenuOpen) _openedAt = Time.unscaledTime;
            _shown = companion;
            if (companion != null && Companion.IsMine(companion, player)) Companion.LastChosen = Companion.IdOf(companion);
            _hirePrice = companion == null ? hirePrice : -1;
            _hireCoin = hireCoin;
            _nameField = companion != null ? Companion.NameOf(companion) : _hirePrice >= 0 ? Trading.FreeName(player)
                : (player.m_customData.TryGetValue("vp_name", out string n) ? n : "Rádvar");
            _note = companion == null ? "" : (Companion.IsMine(companion, player) ? "" : T("villagepleb_menu_others", Companion.Zdo(companion).GetString(Keys.MasterName, "") is string m && m.Length > 0 ? m : T("villagepleb_menu_someone")));
            _renaming = false;
            MenuOpen = true;
            GameUi.IsOpen = () => MenuOpen;
            GameUi_EscapeCloses.Close = CloseFromEscape;
            try { Rebuild(); }
            catch (Exception e) { MenuFailed(e); }
        }

        private void MenuFailed(Exception e)
        {
            if (!_menuWarned) { _menuWarned = true; Warn("Companion menu: " + e); }
            CloseMenu();
        }

        internal static void CloseFromEscape() => Instance?.CloseMenu();

        private void CloseMenu()
        {
            bool was = MenuOpen;
            MenuOpen = false;
            _shown = null;
            _pending = null;
            _renaming = false;
            if (was && _hirePrice >= 0) Note("Hiring a companion cancelled: nothing paid");
            _hirePrice = -1;
            if (_window != null) Object.Destroy(_window.gameObject);
            if (_oldWindow != null) Object.Destroy(_oldWindow.gameObject);
            DropJobPanel();
            _window = _list = _card = _oldWindow = null;
            _fresh = null;
            _field = null;
            _drawn = "";
            _live.Clear();
            _looks?.Teardown();
            _looks = null;
            if (was) { MenuClosedAt = Time.unscaledTime; UITooltip.HideTooltip(); }
        }

        private void UpdateMenu(Player player)
        {
            if (!MenuOpen) return;
            Action run = _pending;
            _pending = null;
            run?.Invoke();
            if (!MenuOpen) return;
            if (_window == null) { CloseMenu(); return; }
            if (_shown != null && _shown.IsDead()) { CloseMenu(); return; }
            else if (global::Menu.IsVisible() || InventoryGui.IsVisible()) { CloseMenu(); return; }
            if (!GameUi.Typing && Time.unscaledTime > _openedAt + 0.2f && ZInput.GetButtonDown("Use")) { CloseMenu(); return; }
            if (_restoreAgain) { _restoreAgain = false; RestoreScrolls(); Reveal(); }
            KeepRunning();
            TickJob();
            TickMenu();
            TickLooks();
        }

        private bool Mine => _shown != null && Companion.IsMine(_shown, Player.m_localPlayer);
        private bool Commandable => _shown != null && Companion.CanCommand(_shown, Player.m_localPlayer);

        private string NoOrders => _shown != null && Sailing.Away(_shown) ? T("villagepleb_sail_busy", Companion.NameOf(_shown))
            : _shown != null && Shopping.Away(_shown) ? T("villagepleb_shop_busy", Companion.NameOf(_shown)) : T("villagepleb_menu_owner_orders");

        private void Change(Action<ZDO> change)
        {
            if (!Commandable) { _note = NoOrders; return; }
            if (!Companion.Write(_shown, change)) _note = T("villagepleb_menu_busy");
        }

        internal static bool TypingInMenu => MenuOpen && GameUi.Typing;

        private void TickMenu()
        {
            float now = Time.unscaledTime;
            if (now >= _nextCheck && !GameUi.Typing && !Input.GetMouseButton(0))
            {
                bool asked = _nextCheck <= 0f;
                _nextCheck = now + 0.25f;
                try
                {
                    Draw(false);
                    if (_sig.ToString() != _drawn && (asked || now - _lastRebuild >= 1f)) Rebuild();
                }
                catch (Exception e) { MenuFailed(e); return; }
            }
            if (now >= _nextLive)
            {
                _nextLive = now + 0.1f;
                try { foreach (Action a in _live) a(); }
                catch (Exception) { _nextCheck = 0f; }
            }
        }

        private void Rebuild()
        {
            Draw(false);
            string sig = _sig.ToString();
            Draw(true);
            _drawn = sig;
            _lastRebuild = Time.unscaledTime;
        }

        private float _nextClaim;
        private readonly Dictionary<long, float> _ranAt = new Dictionary<long, float>();

        // Whether what only the game running it knows (meals, fights, plans) is ours to show: run by this game now, or within the
        // last few seconds, so a companion handed back and forth between games doesn't make the menu change shape each time.
        private bool Here(Humanoid c)
        {
            if (c == null) return false;
            long id = Companion.IdOf(c);
            ZNetView view = c.GetComponent<ZNetView>();
            if (view != null && view.IsValid() && view.IsOwner()) { _ranAt[id] = Time.unscaledTime; return true; }
            return _ranAt.TryGetValue(id, out float at) && Time.unscaledTime - at < 15f;
        }

        // While its menu is open your game runs it, wherever it is (as errands do), so what only the game running it knows (its meals,
        // fights, plans) stays in the menu rather than coming and going as the companion is handed between games.
        private void KeepRunning()
        {
            if (_shown == null || !Commandable || Time.unscaledTime < _nextClaim) return;
            _nextClaim = Time.unscaledTime + 1f;
            ZNetView view = _shown.GetComponent<ZNetView>();
            if (view != null && view.IsValid() && !view.IsOwner()) Brain.Claim(_shown, out _);
        }

        // What it is doing, in a panel of its own above the menu: built once per companion and updated in place, never redrawn with the menu.
        private void JobPanel(Humanoid c, float w, float y)
        {
            long id = Companion.IdOf(c);
            if (_jobPanel == null || _jobFor != id)
            {
                DropJobPanel();
                _jobFor = id;
                _jobShown = _jobWaiting = _stopShown = _stopWaiting = null;
                Image box = MagicMike.ModSettings.Widgets.Box(GameUi.Canvas, "VillagePlebsJob", MagicMike.ModSettings.Theme.Panel, MagicMike.ModSettings.Theme.PanelOutline, 2f);
                _jobPanel = (RectTransform)box.transform;
                _jobPanel.anchorMin = _jobPanel.anchorMax = _jobPanel.pivot = new Vector2(0.5f, 0.5f);
                float textW = w - Pad * 2f - 186f;
                _jobText = Line(_jobPanel, Pad, 8f, textW, 16f, GameUi.Text);
                _jobBar = GameUi.Bar(_jobPanel, new Vector2(Pad, -34f), new Vector2(Mathf.Min(textW, 400f), 10f), 0f, GameUi.Good, null);
                _jobStop = GameUi.Button(_jobPanel, T("villagepleb_menu_stop_job"), new Vector2(w - Pad - 170f, -13f), new Vector2(170f, 30f), StopJob, true, 15f);
            }
            _jobPanel.sizeDelta = new Vector2(w, JobPanelH);
            _jobPanel.anchoredPosition = new Vector2(0f, y);
            TickJob();
        }

        private void DropJobPanel()
        {
            if (_jobPanel != null) Object.Destroy(_jobPanel.gameObject);
            _jobPanel = _jobBar = null;
            _jobText = null;
            _jobStop = null;
            _jobFor = 0L;
        }

        // A new line only shows once it has held for a moment, so the gaps between one step and the next don't flash up.
        private void TickJob()
        {
            Humanoid c = _shown;
            if (_jobPanel == null || c == null) return;
            BrainState st = Here(c) ? Brain.Get(c) : null;
            float done = -1f;
            string text = st == null ? T("villagepleb_menu_shows_elsewhere") : T("villagepleb_menu_job", Words.Show(Work.JobText(st, out done)));
            text = $"<b>{Companion.NameOf(c)}</b>  ·  {text}";
            if (text != _jobWaiting) { _jobWaiting = text; _jobSince = Time.unscaledTime; }
            if (_jobShown == null || text != _jobShown && Time.unscaledTime - _jobSince >= 1f) { _jobShown = text; Set(_jobText, text); }
            bool measured = done >= 0f && text == _jobShown;
            if (_jobBar.gameObject.activeSelf != measured) _jobBar.gameObject.SetActive(measured);
            if (measured) GameUi.SetBar(_jobBar, done);
            bool ordered = st != null && Work.OnOrder(st), mission = st != null && !ordered && Companion.OrderOf(c) == VillagePlebs.Order.Gather && Missions.Current(c) != null;
            // The button holds too: it only comes, goes or changes its words once that has lasted a moment.
            string button = !Mine || !(ordered || mission) ? "" : mission ? "villagepleb_menu_drop_mission" : "villagepleb_menu_stop_job";
            if (button != _stopWaiting) { _stopWaiting = button; _stopSince = Time.unscaledTime; }
            if (_stopShown == null || button != _stopShown && Time.unscaledTime - _stopSince >= 1f)
            {
                _stopShown = button;
                _jobStop.gameObject.SetActive(button.Length > 0);
                if (button.Length > 0) Set(GameUi.Label(_jobStop), T(button));
            }
        }

        private void StopJob()
        {
            _pending = () =>
            {
                BrainState bs = Brain.Claim(_shown, out string why);
                if (bs == null) { _note = Words.Show(why); return; }
                if (Work.OnOrder(bs)) Work.StopJob(bs);
                else Missions.StopCurrent(bs);
            };
            _nextCheck = 0f;
        }

        // A rebuilt window waits unseen for a frame while its scroll settles, then takes the old one's place, so a redraw never shows.
        private void Reveal()
        {
            if (_fresh != null) { _fresh.alpha = 1f; _fresh.interactable = _fresh.blocksRaycasts = true; _fresh = null; }
            if (_oldWindow != null) { Object.Destroy(_oldWindow.gameObject); _oldWindow = null; }
        }

        private void Draw(bool make)
        {
            _make = make;
            _sig.Length = 0;
            if (_tab == Tab.Companions && Trading.MostCompanions <= 1) _tab = Tab.Status;
            Humanoid c = _shown;
            bool companion = c != null;
            Vector2 screen = GameUi.ScreenSize;
            float jobSpace = companion ? JobPanelH + JobGap : 0f;
            float w = Mathf.Min(WindowW, screen.x - 20f), h = Mathf.Min(companion ? 760f : 620f, screen.y - 200f - jobSpace);
            string key = (companion ? Companion.IdOf(c).ToString() : "list") + "/" + _tab;
            if (key != _liveKey) { _liveKey = key; _liveHeights.Clear(); }
            _liveIndex = 0;
            _sig.Append(key).Append('|').Append((int)w).Append('x').Append((int)h);
            string message = string.IsNullOrEmpty(_note) ? "" : Words.Show(_note);
            _sig.Append("|N").Append(message);
            if (make)
            {
                _scrolls.Clear();
                if (_window != null && key == _scrollKey)
                    foreach (ScrollRect s in _window.GetComponentsInChildren<ScrollRect>(true))
                        if (s.content != null) _scrolls[PathIn(_window, s.transform)] = s.content.anchoredPosition.y;
                _scrollKey = key;
                if (_oldWindow != null) Object.Destroy(_oldWindow.gameObject);
                _oldWindow = _window;
                UITooltip.HideTooltip();
                _live.Clear();
                _field = null;
                _list = _card = null;
                _window = GameUi.Window("VillagePlebsMenu", new Vector2(w, h));
                _fresh = null;
                if (_oldWindow != null)
                {
                    _fresh = _window.gameObject.AddComponent<CanvasGroup>();
                    _fresh.alpha = 0f;
                    _fresh.interactable = _fresh.blocksRaycasts = false;
                }
                if (message.Length > 0) h = MessagePanel(message, w, h, screen.y - jobSpace);
                _window.anchoredPosition -= new Vector2(0f, jobSpace / 2f);
                if (companion) JobPanel(c, w, _window.anchoredPosition.y + h / 2f + JobGap + JobPanelH / 2f);
                else DropJobPanel();
            }
            _in = _window; _left = Pad; _width = w - Pad * 2f;

            float top = companion ? Header(c, w) : ListHeader(w);
            Vector2 area = new Vector2(w - Pad * 2f + 12f, h - top - Pad + 8f);
            bool looks = companion && _tab == Tab.Looks;
            if (make && !looks)
            {
                RectTransform box = GameUi.Rect(_window, "List", new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(Pad - 6f, -top), area);
                _list = GameUi.Scroll(box, 8f);
                _cardW = _list.sizeDelta.x - 2f;
            }
            _listY = 0f;
            if (!companion) DrawCompanions();
            else
                switch (_tab)
                {
                    case Tab.Status: DrawStatus(c); break;
                    case Tab.Stats: DrawStats(c); break;
                    case Tab.Missions: DrawMissions(c); break;
                    case Tab.Orders: DrawOrders(c); break;
                    case Tab.Home: DrawHome(c); break;
                    case Tab.Gear: DrawGear(c); break;
                    case Tab.Looks: DrawLooks(c, new Vector2(Pad - 6f, -top), area); break;
                    case Tab.Companions: DrawCompanions(); break;
                }
            if (make && _list != null)
            {
                float content = Mathf.Max(0f, _listY - CardGap);
                _list.sizeDelta = new Vector2(_list.sizeDelta.x, content);
            }
            if (make) { RestoreScrolls(); _restoreAgain = true; }
            if (make)
            {
                MagicMike.ModSettings.Widgets.SnapAll(_window);
                if (_focusField && _field != null) { _field.Select(); _field.ActivateInputField(); }
                _focusField = false;
            }
        }

        private void RestoreScrolls()
        {
            if (_window == null || _scrolls.Count == 0) return;
            Canvas.ForceUpdateCanvases();
            foreach (ScrollRect s in _window.GetComponentsInChildren<ScrollRect>(true))
            {
                if (s.content == null || !_scrolls.TryGetValue(PathIn(_window, s.transform), out float y)) continue;
                RectTransform view = s.viewport != null ? s.viewport : (RectTransform)s.transform;
                float room = Mathf.Max(0f, s.content.rect.height - view.rect.height);
                s.StopMovement();
                s.content.anchoredPosition = new Vector2(s.content.anchoredPosition.x, Mathf.Clamp(y, 0f, room));
            }
        }

        private static string PathIn(Transform root, Transform t)
        {
            var path = new StringBuilder();
            for (; t != null && t != root; t = t.parent)
            {
                int same = 0;
                Transform parent = t.parent;
                if (parent != null)
                    for (int i = 0; i < t.GetSiblingIndex(); i++) if (parent.GetChild(i).name == t.name) same++;
                path.Insert(0, "/" + t.name + "#" + same);
            }
            return path.ToString();
        }

        private float MessagePanel(string message, float w, float h, float screenH)
        {
            const float gap = 8f, padX = 18f, padY = 12f;
            Image panel = MagicMike.ModSettings.Widgets.Box(_window, "Messages", MagicMike.ModSettings.Theme.Panel, MagicMike.ModSettings.Theme.PanelOutline, 2f);
            var r = (RectTransform)panel.transform;
            r.anchorMin = r.anchorMax = r.pivot = new Vector2(0f, 1f);
            TMP_Text t = GameUi.Label(r, message, new Vector2(padX, -padY), new Vector2(w - padX * 2f, 24f), 17f, GameUi.Warn);
            float textH = t.GetPreferredValues(message, w - padX * 2f, 0f).y;
            ((RectTransform)t.transform).sizeDelta = new Vector2(w - padX * 2f, textH);
            float panelH = textH + padY * 2f;
            h = Mathf.Max(Mathf.Min(h, screenH - 40f - panelH - gap), Mathf.Min(h, 360f));
            _window.sizeDelta = new Vector2(w, h);
            _window.anchoredPosition = new Vector2(0f, (panelH + gap) / 2f);
            r.anchoredPosition = new Vector2(0f, -h - gap);
            r.sizeDelta = new Vector2(w, panelH);
            return h;
        }

        private void Title(string text, float w)
        {
            text = Words.Show(text);
            _sig.Append("|T").Append(text);
            if (_make) GameUi.TitleBar(_window, text, () => _pending = CloseMenu);
        }

        private float ListHeader(float w)
        {
            Title(T("villagepleb_menu_companions"), w);
            _y = 70f;
            return _y + 6f;
        }

        private void DrawCompanions()
        {
            Player me = Player.m_localPlayer;
            List<Profile> mine = me != null ? Profile.Here(me) : new List<Profile>();
            bool hiring = _hirePrice >= 0;
            if (hiring) HireCard(mine);

            foreach (Profile prof in mine)
            {
                BeginCard();
                Humanoid here = Companion.All().FirstOrDefault(h => Companion.IdOf(h) == prof.Id);
                if (here != null)
                {
                    float d = me != null ? Vector3.Distance(me.transform.position, here.transform.position) : 0f;
                    var buttons = new List<(string, bool, Action)>();
                    if (d > 15f) buttons.Add((T("villagepleb_menu_call"), false, () => { Humanoid h2 = here; _pending = () => { if (Home.Follow(h2)) Brain.TeleportBehind(h2, Player.m_localPlayer, "villagepleb_brain_was_called_to"); }; }));
                    buttons.Add((T("villagepleb_menu_open"), true, () => { Humanoid h2 = here; _pending = () => { _tab = Tab.Status; OpenMenuFor(Player.m_localPlayer, h2); }; }));
                    HeadingRow(prof.Name, buttons.ToArray());
                    long hid = prof.Id;
                    bool armed = _forgetArmed == hid;
                    if (armed) Note(T("villagepleb_menu_release_hint"), GameUi.Warn);
                    WideBtn(armed ? T("villagepleb_menu_release_again", prof.Name) : T("villagepleb_menu_release", prof.Name), false, () =>
                    {
                        if (_forgetArmed != hid) { _forgetArmed = hid; return; }
                        _pending = () =>
                        {
                            _forgetArmed = 0L;
                            Humanoid h3 = Companion.All().FirstOrDefault(h => Companion.IdOf(h) == hid);
                            if (h3 == null) return;
                            bool wasShown = _shown == h3;
                            if (!Companion.Release(h3, Player.m_localPlayer, out string why)) { _note = Words.Show(why); return; }
                            _note = "";
                            if (wasShown) { _shown = null; _tab = Tab.Status; }
                        };
                    }, 30f, armed ? GameUi.Warn : (Color?)null);
                }
                else if (prof.Dead)
                {
                    Heading(prof.Name);
                    Profile p0 = prof;
                    LiveNote(() => T(p0.HasBed ? "villagepleb_menu_fallen_bed" : "villagepleb_menu_fallen_you", Mathf.Max(0f, (float)(p0.DiedAt + MainConfig.respawnSeconds.Value - ZNet.instance.GetTimeSeconds())).ToString("0")), GameUi.Warn);
                    Note(T("villagepleb_menu_things_in_tomb"), GameUi.Warn);
                    WideBtn(T("villagepleb_menu_wake_now", prof.Name), true, () => { Profile p2 = prof; _pending = () => { Humanoid c = Home.Respawn(Player.m_localPlayer, p2); if (c != null) OpenMenuFor(Player.m_localPlayer, c); else if (p2.Dead || Remote.InWorld(p2.Id) == null) _note = T("villagepleb_menu_asking_host", p2.Name); }; });
                }
                else
                {
                    ZDO z = Remote.Find(prof.Id);
                    if (z != null) HeadingRow(prof.Name, (T("villagepleb_menu_call"), true, () => { ZDO z2 = z; _pending = () => { _note = Remote.Call(z2, Player.m_localPlayer, out string why) ? T("villagepleb_menu_on_the_way", prof.Name) : Words.Show(why ?? ""); }; }));
                    else Heading(prof.Name);
                    if (z != null)
                    {
                        Vector3 at = Remote.Position(z);
                        string doing = Remote.Status(z);
                        string where = me != null ? Words.Show(Remote.Where(at, me.transform.position)) : "";
                        if (Remote.AtHome(z)) where = where.Length > 0 ? T("villagepleb_menu_where_at_home", where) : T("villagepleb_menu_at_home");
                        string order = T(Remote.OrderOf(z) == VillagePlebs.Order.Gather ? "villagepleb_menu_away_living" : Remote.OrderOf(z) == VillagePlebs.Order.Follow ? "villagepleb_menu_away_left_behind" : Remote.OrderOf(z) == VillagePlebs.Order.Guard ? "villagepleb_menu_away_guarding" : "villagepleb_menu_away_staying");
                        Note($"{Capital(where)} ({order}).", GameUi.Text);
                        if (!string.IsNullOrEmpty(doing)) Note(T("villagepleb_menu_last_doing", Words.Show(doing)), GameUi.Good);
                        foreach (string line in Remote.Journal(z, 2)) Note("· " + Words.Show(line), GameUi.Dim);
                    }
                    else if (Remote.InWorld(prof.Id) == false)
                    {
                        Vector3? tomb = Remote.TombOf(prof.Id);
                        Note(T(prof.HasBed ? "villagepleb_menu_lost_bed" : "villagepleb_menu_lost_you", prof.Name) + " "
                             + (tomb != null && me != null ? T("villagepleb_menu_lost_tomb_at", Words.Show(Remote.Where(tomb.Value, me.transform.position))) : T("villagepleb_menu_lost_tomb")), GameUi.Warn);
                        WideBtn(T("villagepleb_menu_bring_back", prof.Name), true, () => { Profile p2 = prof; _pending = () => { Humanoid c = Home.Respawn(Player.m_localPlayer, p2); if (c != null) OpenMenuFor(Player.m_localPlayer, c); else if (p2.Dead || Remote.InWorld(p2.Id) == null) _note = T("villagepleb_menu_asking_host", p2.Name); }; });
                        Note(T("villagepleb_menu_forget_hint"), GameUi.Dim);
                        WideBtn(_forgetArmed == prof.Id ? T("villagepleb_menu_forget_again", prof.Name) : T("villagepleb_menu_forget", prof.Name), false, () =>
                        {
                            long id = prof.Id;
                            if (_forgetArmed == id) _pending = () => { Profile.Forget(Player.m_localPlayer, id); _forgetArmed = 0L; };
                            else _forgetArmed = id;
                        }, 32f, _forgetArmed == prof.Id ? GameUi.Warn : (Color?)null);
                    }
                    else
                        Note(T("villagepleb_menu_last_seen", Words.Show(Remote.Where(prof.LastSeen, me != null ? me.transform.position : prof.LastSeen))), GameUi.Text);
                }
                EndCard();
            }

            if (hiring) return;
            BeginCard();
            if (mine.Count >= MainConfig.maxCompanions.Value)
            {
                if (mine.Count < Trading.MostCompanions) Note(T("villagepleb_menu_buy_more", MainConfig.maxCompanions.Value, MainConfig.companionPrice.Value, Trading.MostCompanions - mine.Count), GameUi.Dim);
                else Note(T("villagepleb_menu_most_allowed", mine.Count), GameUi.Dim);
                EndCard();
                return;
            }
            Heading(T(mine.Count == 0 ? "villagepleb_menu_summon_title" : "villagepleb_menu_summon_another_title"));
            Note(T("villagepleb_menu_summon_about"), GameUi.Dim);
            _y += 4f;
            NameField(null);
            if (mine.Count > 0) Note(T("villagepleb_menu_summon_new_warn"), GameUi.Warn);
            WideBtn(T(mine.Count == 0 ? "villagepleb_menu_summon" : "villagepleb_menu_summon_new"), true, () =>
                _pending = () =>
                {
                    string wanted = string.IsNullOrEmpty(_nameField?.Trim()) ? "Rádvar" : _nameField.Trim();
                    Profile same = Profile.Here(Player.m_localPlayer).FirstOrDefault(pr => string.Equals(pr.Name, wanted, StringComparison.OrdinalIgnoreCase));
                    if (same != null) { _note = T("villagepleb_menu_same_name", same.Name); return; }
                    Player p = Player.m_localPlayer;
                    Humanoid c = p != null ? Companion.Summon(p, (_nameField ?? "").Trim()) : null;
                    if (c == null) return;
                    _tab = Tab.Looks;
                    OpenMenuFor(p, c);
                    string took = Home.SetUp(c);
                    Talk.Tell(c, took != null ? Words.P("villagepleb_menu_hello_took", took) : Words.P("villagepleb_menu_hello"));
                }, 38f);
            EndCard();
        }

        private void HireCard(List<Profile> mine)
        {
            _sig.Append("|hire").Append(_hirePrice);
            BeginCard();
            Heading(T("villagepleb_menu_hire_title"));
            Note(T("villagepleb_menu_summon_about"), GameUi.Dim);
            _y += 4f;
            NameField(null);
            int free = MainConfig.maxCompanions.Value - mine.Count;
            if (free > 0) Note(T("villagepleb_trade_free_left", free), GameUi.Warn);
            WideBtn(T("villagepleb_menu_hire", _hirePrice), true, () =>
                _pending = () =>
                {
                    string wanted = string.IsNullOrEmpty(_nameField?.Trim()) ? "Rádvar" : _nameField.Trim();
                    Profile same = Profile.Here(Player.m_localPlayer).FirstOrDefault(pr => string.Equals(pr.Name, wanted, StringComparison.OrdinalIgnoreCase));
                    if (same != null) { _note = T("villagepleb_menu_same_name", same.Name); return; }
                    Player p = Player.m_localPlayer;
                    string why = null;
                    Humanoid c = p != null ? Trading.Hire(p, wanted, _hireCoin, _hirePrice, out why) : null;
                    if (c == null) { _note = why ?? ""; return; }
                    _hirePrice = -1;
                    _tab = Tab.Looks;
                    OpenMenuFor(p, c);
                    string took = Home.SetUp(c);
                    Talk.Tell(c, took != null ? Words.P("villagepleb_menu_hello_took", took) : Words.P("villagepleb_menu_hello"));
                }, 38f);
            EndCard();
        }

        private void NameField(Action submit)
        {
            _sig.Append("|f");
            if (_make)
            {
                GameUi.Label(_in, Loc("$menu_name"), new Vector2(_left, -_y - 8f), new Vector2(84f, 24f), 17f, GameUi.Text).fontStyle = FontStyles.Bold;
                _field = GameUi.TextField(_in, new Vector2(_left + 90f, -_y), new Vector2(Mathf.Min(320f, _width - 90f), 38f), _nameField, 24, v => _nameField = v,
                    submit == null ? (Action<string>)null : _ => submit());
            }
            _y += 46f;
        }

        private float Header(Humanoid c, float w)
        {
            Player pl = Player.m_localPlayer;
            long id = Companion.IdOf(c);
            string name = Companion.NameOf(c);
            float closeX = w - Pad - 110f;
            _sig.Append("|H").Append(name).Append(_renaming).Append(Mine);
            if (_make) GameUi.HeaderBox(_window, new Vector2(12f, -10f), new Vector2(w - 24f, 160f), false);

            // Up to five of your plebs side by side: name, portrait, Rename. The one shown is framed; a click on another opens it.
            List<Profile> mine = pl != null ? Profile.Here(pl) : new List<Profile>();
            List<Profile> row = mine.Where(x => x.Id == id).Concat(mine.Where(x => x.Id != id)).Take(5).OrderBy(x => mine.IndexOf(x)).ToList();
            if (row.Count == 0) row.Add(new Profile { Id = id, Name = name });
            float cw = (closeX - 12f - Pad) / 5f;
            for (int i = 0; i < row.Count; i++)
            {
                Profile prof = row[i];
                Humanoid there = prof.Id == id ? c : Companion.All().FirstOrDefault(h => Companion.IdOf(h) == prof.Id);
                string shownName = there != null ? Companion.NameOf(there) : prof.Name;
                bool chosen = prof.Id == id;
                // Away by ship (sailing, or waiting aboard somewhere): says so where its Rename button would be.
                ZDO rec = there != null ? Companion.Zdo(there) : Remote.Find(prof.Id);
                bool sailing = Sailing.Away(rec), shopping = Shopping.Away(rec);
                float px = Pad + i * cw, mid = px + cw / 2f;
                _sig.Append("|p").Append(prof.Id).Append(shownName).Append(there != null).Append(sailing).Append(shopping);
                if (chosen && _renaming)
                {
                    if (_make) _field = GameUi.TextField(_window, new Vector2(px + 4f, -12f), new Vector2(cw - 8f, 34f), _nameField, 24, v => _nameField = v, _ => SaveName(), 18f);
                    Btn(Loc("$menu_manualsave"), mid - 56f, 136f, 54f, 24f, SaveName, true, size: 13f);
                    Btn(Loc("$menu_cancel"), mid + 2f, 136f, 54f, 24f, () => { _renaming = false; _nameField = Companion.NameOf(_shown); }, size: 13f);
                }
                else
                {
                    if (_make)
                    {
                        TMP_Text t = GameUi.Label(_window, shownName, new Vector2(px, -12f), new Vector2(cw, 34f), 26f, GameUi.Warm, GameUi.Norse, TextAlignmentOptions.Center);
                        t.fontStyle = FontStyles.Bold;
                        t.textWrappingMode = TextWrappingModes.NoWrap;
                        t.overflowMode = TextOverflowModes.Ellipsis;
                    }
                    Humanoid target = there;
                    if (sailing || shopping)
                    {
                        if (_make) GameUi.Label(_window, T(sailing ? "villagepleb_menu_away_sailing" : "villagepleb_menu_out_shopping"), new Vector2(mid - 60f, -136f), new Vector2(120f, 24f), 15f, GameUi.Gold, null, TextAlignmentOptions.Center);
                    }
                    else Btn(Loc("$hud_rename"), mid - 55f, 136f, 110f, 24f, () =>
                    {
                        if (target == _shown) { _renaming = true; _focusField = true; _nameField = Companion.NameOf(_shown); return; }
                        _pending = () => { OpenMenuFor(pl, target); _renaming = true; _focusField = true; _nameField = Companion.NameOf(target); };
                    }, enabled: target != null, size: 13f);
                }
                if (!_make) continue;
                // The one shown has a green border round its portrait, as on the command wheel.
                if (chosen)
                {
                    RectTransform ring = GameUi.Rect(_window, "Chosen", new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(mid - 44f, -45f), new Vector2(88f, 88f));
                    Image border = ring.gameObject.AddComponent<Image>();
                    border.color = GameUi.Good;
                    border.raycastTarget = false;
                }
                RawImage face = GameUi.Picture(_window, new Vector2(mid - 39f, -50f), new Vector2(78f, 78f), Portraits.Get(prof.Id));
                TMP_Text initial = GameUi.Label(face.transform.parent, shownName.Length > 0 ? shownName.Substring(0, 1).ToUpperInvariant() : "?", Vector2.zero, new Vector2(78f, 78f), 40f,
                    GameUi.Dim, GameUi.Norse, TextAlignmentOptions.Center);
                initial.transform.SetSiblingIndex(0);
                initial.enabled = face.texture == null;
                long pid = prof.Id;
                _live.Add(() =>
                {
                    Texture tex = Portraits.Get(pid);
                    if (face.texture != tex) face.texture = tex;
                    face.enabled = tex != null;
                    initial.enabled = tex == null;
                });
                if (!chosen && there != null)
                {
                    Image frame = face.transform.parent.GetComponent<Image>() ?? face.transform.parent.gameObject.AddComponent<Image>();
                    frame.raycastTarget = true;
                    Button open = face.transform.parent.gameObject.AddComponent<Button>();
                    open.transition = Selectable.Transition.None;
                    Humanoid pick = there;
                    open.onClick.AddListener(() => { _pending = () => OpenMenuFor(pl, pick); _nextCheck = 0f; });
                }
            }

            Btn(Loc("$menu_close"), closeX, 18f, 110f, 36f, () => _pending = CloseMenu);
            int plebs = mine.Count;
            _sig.Append("|P").Append(plebs).Append('/').Append(Trading.MostCompanions);
            if (_make) Line(_window, closeX, 60f, 110f, 16f, GameUi.Dim, TextAlignmentOptions.Center).text = T("villagepleb_menu_pleb_count", plebs, Trading.MostCompanions);
            _y = 180f;
            LiveBar(() => c.GetHealth() / Mathf.Max(1f, c.GetMaxHealth()), () => $"{Loc("$se_health")}  {c.GetHealth():0} / {c.GetMaxHealth():0}", HealthRed);
            LiveBar(() => Stamina.Get(c) / Mathf.Max(1f, Stamina.Max(c)), () => $"{Loc("$se_stamina")}  {Stamina.Get(c):0} / {Stamina.Max(c):0}", StaminaYellow);
            LiveBar(() => Mathf.Clamp01(Carry.Weight(c) / Mathf.Max(1f, Carry.Max(c))), () => $"{T("villagepleb_menu_carrying")}  {Carry.Weight(c):0} / {Carry.Max(c):0}", CarryBrown);
            bool eitr = Eitr.Max(c) > 0f;
            _sig.Append(eitr);
            if (eitr) LiveBar(() => Eitr.Get(c) / Mathf.Max(1f, Eitr.Max(c)), () => $"{Loc("$se_eitr")}  {Eitr.Get(c):0} / {Eitr.Max(c):0}", EitrPurple);

            _y += 4f;
            var tabs = ((Tab[])Enum.GetValues(typeof(Tab))).Where(t => t != Tab.Companions || Trading.MostCompanions > 1).ToArray();
            float tw = (_width - 6f * (tabs.Length - 1)) / tabs.Length;
            for (int i = 0; i < tabs.Length; i++)
            {
                Tab t = tabs[i];
                TabBtn(Named("villagepleb_menu_tab_", t), _left + i * (tw + 6f), _y, tw, 38f, _tab == t, () => _tab = t, tabs.Length > 7 ? 15f : 17f);
            }
            _y += 42f;
            if (_make) GameUi.Braid(_window, new Vector2(0f, -_y - 4f), _width);
            return _y + 14f;
        }

        private void SaveName()
        {
            string n = (_nameField ?? "").Trim();
            if (n.Length == 0) return;
            _pending = () => { Change(z => z.Set(Keys.Name, n)); Player.m_localPlayer.m_customData["vp_name"] = n; _renaming = false; };
        }

        private void DrawStatus(Humanoid c)
        {
            BrainState st = Brain.Get(c);
            bool here = Here(c);

            BeginCard(T("villagepleb_menu_food_rest"));
            if (!here) Note(T("villagepleb_menu_shows_elsewhere"), GameUi.Dim);
            else
            {
                List<Food.Meal> meals = Food.Meals(c);
                float s = GameUi.CellSpace, top = _y;
                for (int i = 0; i < 3; i++)
                {
                    if (!_make) continue;
                    InventoryElement e = GameUi.Slot(_in, new Vector2(_left + i * s, -top), null);
                    int k = i;
                    void Show()
                    {
                        List<Food.Meal> now = Food.Meals(c);
                        Food.Meal m = k < now.Count ? now[k] : null;
                        // Just the minutes left: no bar over them (a meal's own durability bar is hidden too).
                        GameUi.Fill(e, m?.Item, m != null ? T("villagepleb_menu_minutes_short", Mathf.CeilToInt(m.Time / 60f)) : null);
                        if (e.m_durability.gameObject.activeSelf) e.m_durability.gameObject.SetActive(false);
                    }
                    Show();
                    _live.Add(Show);
                }
                LiveText(() =>
                {
                    List<Food.Meal> now = Food.Meals(c);
                    return now.Count == 0 ? Tint(T("villagepleb_menu_hungry", MainConfig.baseHealth.Value.ToString("0"), MainConfig.baseStamina.Value.ToString("0")), GameUi.Warn)
                         : T("villagepleb_menu_meals", string.Join(", ", now.Select(m => T("villagepleb_menu_meal", Loc(m.Item.m_shared.m_name), Mathf.CeilToInt(m.Time / 60f)))));
                }, GameUi.Text, indent: 3f * s + 10f);
                _y = Mathf.Max(_y, top + 64f) + 8f;
                LiveText(() => Rest.IsRested(c) ? Tint(T("villagepleb_menu_rested", Mathf.CeilToInt(Rest.Left(c) / 60f)), GameUi.Good) : Tint(T("villagepleb_menu_not_rested"), GameUi.Dim), GameUi.Text);
            }
            EndCard();

            BeginCard(T("villagepleb_menu_effects"));
            var effects = here ? c.GetSEMan().GetStatusEffects().Where(se => se != null && se.m_icon != null).ToList() : new List<StatusEffect>();
            if (!here) Note(T("villagepleb_menu_shows_elsewhere"), GameUi.Dim);
            else if (effects.Count == 0) Note(T("villagepleb_menu_no_effects"), GameUi.Dim);
            else
            {
                foreach (StatusEffect se in effects.Take(9)) _sig.Append("|e").Append(se.m_name);
                if (_make)
                {
                    int i = 0;
                    foreach (StatusEffect se in effects.Take(9))
                    {
                        float ex = _left + i++ * 62f;
                        GameUi.Icon(_in, new Vector2(ex + 9f, -_y), new Vector2(40f, 40f), se.m_icon);
                        TMP_Text time = Line(_in, ex, _y + 42f, 58f, 14f, GameUi.Text, TextAlignmentOptions.Center);
                        StatusEffect effect = se;
                        void Show() { float left = effect.GetRemaningTime(); Set(time, left > 0f ? $"{Mathf.FloorToInt(left / 60f)}:{Mathf.FloorToInt(left % 60f):00}" : ""); }
                        Show();
                        _live.Add(Show);
                    }
                    _y += 66f;
                }
                Note(string.Join(", ", effects.Select(se => Loc(se.m_name))), GameUi.Dim);
            }
            EndCard();

            BeginCard(T("villagepleb_menu_recently"));
            LiveText(() => st.History.Count == 0 ? Tint(T("villagepleb_menu_nothing_yet"), GameUi.Dim) : string.Join("\n", st.History.Take(8).Select(l => Capital(Words.Show(l)))), GameUi.Text);
            EndCard();

            DrawJournal(c);
        }

        private void OrderButtons(Humanoid c)
        {
            Order order = Companion.OrderOf(c);
            float half = (_width - 8f) / 2f;
            Btn(T("villagepleb_menu_come_with_me"), _left, _y, half, 42f, () => _pending = () => GiveOrder(h => Home.Follow(h)), order == Order.Follow, size: 19f);
            Btn(T("villagepleb_menu_go_home"), _left + half + 8f, _y, half, 42f, () => _pending = () => GiveOrder(h => Home.GoHome(h), true), order == Order.Gather, size: 19f);
            _y += 48f;
            float third = (_width - 12f) / 3f, bx = _left;
            Btn(T("villagepleb_menu_stay_here"), bx, _y, third, 32f, () => _pending = () => Change(z => z.Set(Keys.Order, (int)VillagePlebs.Order.Stay)), order == Order.Stay, size: 16f);
            Btn(T("villagepleb_menu_guard_spot"), bx + third + 6f, _y, third, 32f,
                () => _pending = () => Change(z => { z.Set(Keys.Order, (int)VillagePlebs.Order.Guard); z.Set(Keys.Post, c.transform.position); }), order == Order.Guard, size: 16f);
            Btn(T("villagepleb_menu_come_here"), bx + (third + 6f) * 2f, _y, third, 32f, () => _pending = ComeHere, size: 16f);
            _y += 38f;
            Btn(T(Sailing.Away(c) ? "villagepleb_sail_change" : "villagepleb_sail_order"), bx, _y, _width, 32f, () => _pending = () =>
            {
                Player p = Player.m_localPlayer;
                Humanoid h = _shown;
                if (!Mine) { _note = NoOrders; return; }
                CloseMenu();
                string why = Sailing.Offer(p, h);
                if (!string.IsNullOrEmpty(why)) Tell(why);
            }, size: 16f);
            _y += 38f;
            Note(T("villagepleb_menu_hold_key", MainConfig.menuKey.Value), GameUi.Dim);
        }

        // whileAway: an order it takes even away by ship (Go home: it sails home).
        private void GiveOrder(Func<Humanoid, bool> order, bool whileAway = false)
        {
            if (!Commandable && !(whileAway && Mine && Sailing.Away(_shown))) { _note = NoOrders; return; }
            if (!order(_shown)) _note = T("villagepleb_menu_busy");
        }

        private void DrawStats(Humanoid c)
        {
            BrainState st = Brain.Get(c);
            bool here = Here(c);

            if (EpicMmo.On) DrawLevel(c);

            BeginCard(T("villagepleb_menu_fighting_card"));
            string Block()
            {
                ItemDrop.ItemData sh = Companion.Shield(c), me = Companion.BestMelee(c);
                float skill = Skill.Get(c, Skills.SkillType.Blocking) / 100f;
                return sh != null ? $"{sh.GetBlockPower(skill):0}" : me != null ? $"{me.GetBlockPower(skill):0}" : "-";
            }
            Stats((Loc("$item_armor"), null, () => $"{Companion.Armor(c):0}"),
                  (T("villagepleb_menu_block"), null, Block),
                  (T("villagepleb_menu_kills"), null, () => Companion.Zdo(c)?.GetInt(Keys.Kills, 0).ToString() ?? "-"),
                  (T("villagepleb_menu_fights"), null, () => Here(c) ? Brain.Get(c).Fights.ToString() : "-"));
            LiveText(() =>
            {
                ItemDrop.ItemData melee = Companion.BestMelee(c), bow = Companion.BestRanged(c), shield = Companion.Shield(c);
                var lines = new List<string>
                {
                    melee != null ? T("villagepleb_menu_damage_hit", Loc(melee.m_shared.m_name), Damage(c, melee), Words.Mid(Loc("$skill_" + melee.m_shared.m_skillType.ToString().ToLower())))
                                  : Tint(T("villagepleb_menu_no_weapon"), GameUi.Warn),
                };
                if (bow != null) lines.Add(T("villagepleb_menu_damage_shot", Loc(bow.m_shared.m_name), Damage(c, bow)));
                lines.Add(Tint(shield != null ? T("villagepleb_menu_blocks_shield", Words.Mid(Loc(shield.m_shared.m_name))) : melee != null ? T("villagepleb_menu_blocks_weapon") : T("villagepleb_menu_blocks_nothing"), GameUi.Dim));
                BrainState bs = Here(c) ? Brain.Get(c) : null;
                if (bs != null && bs.Parries + bs.Blocks > 0) lines.Add(T("villagepleb_menu_session_parries", bs.Parries, bs.Blocks));
                var feared = Journal.Tally(c).Where(kv => kv.Key.StartsWith("killedby:")).Select(kv => Words.Mid(Thing(kv.Key.Substring(9)))).Distinct().ToList();
                if (feared.Count > 0) lines.Add(Tint(T("villagepleb_menu_careful_with", string.Join(", ", feared)), GameUi.Dim));
                return string.Join("\n", lines.Select(l => Words.Show(l)));
            }, GameUi.Text);
            EndCard();

            BeginCard(T("villagepleb_menu_last_fights"));
            if (!here) Note(T("villagepleb_menu_shows_elsewhere"), GameUi.Dim);
            else LiveText(() => st.FightLog.Count == 0 ? Tint(T("villagepleb_menu_none_this_session"), GameUi.Dim) : string.Join("\n", st.FightLog.Select(f => Words.Show(f))), GameUi.Text);
            EndCard();

            BeginCard(T("villagepleb_menu_resistances"));
            HitData.DamageModifiers mods = Gear.Modifiers(c);
            var lines = new List<string>();
            foreach (HitData.DamageType t in new[] { HitData.DamageType.Blunt, HitData.DamageType.Slash, HitData.DamageType.Pierce, HitData.DamageType.Fire, HitData.DamageType.Frost, HitData.DamageType.Lightning, HitData.DamageType.Poison, HitData.DamageType.Spirit })
            {
                HitData.DamageModifier mod = mods.GetModifier(t);
                if (mod != HitData.DamageModifier.Normal)
                    lines.Add($"{Loc("$inventory_" + t.ToString().ToLowerInvariant())}: {Words.Mid(mod == HitData.DamageModifier.Ignore ? T("villagepleb_menu_mod_ignore") : Loc("$inventory_" + mod.ToString().ToLowerInvariant()))}");
            }
            Note(lines.Count > 0 ? string.Join("   ", lines) : T("villagepleb_menu_no_resistances"), lines.Count > 0 ? GameUi.Text : GameUi.Dim);
            EndCard();

            BeginCard(Loc("$inventory_skills"));
            Note(T("villagepleb_menu_skills_about"), GameUi.Dim);
            var skills = Skill.Types().Select(t => new KeyValuePair<Skills.SkillType, float>(t, Skill.Get(c, t)))
                .OrderByDescending(kv => kv.Value).ThenBy(kv => Loc("$skill_" + kv.Key.ToString().ToLower())).ToList();
            foreach (var kv in skills) _sig.Append('|').Append((int)kv.Key).Append(kv.Value >= 1f ? '+' : '-');
            if (_make)
            {
                const float rowH = 44f, gap = 24f;
                float colW = (_width - gap) / 2f;
                for (int i = 0; i < skills.Count; i++)
                {
                    float sx = _left + (i % 2) * (colW + gap), sy = _y + (i / 2) * rowH;
                    Skills.SkillType type = skills[i].Key;
                    TMP_Text name = Line(_in, sx, sy, colW - 50f, 16f, skills[i].Value >= 1f ? GameUi.Text : GameUi.Dim);
                    name.text = Loc("$skill_" + type.ToString().ToLower());
                    TMP_Text level = Line(_in, sx + colW - 48f, sy, 48f, 17f, GameUi.Gold, TextAlignmentOptions.TopRight);
                    RectTransform bar = GameUi.Bar(_in, new Vector2(sx, -sy - 23f), new Vector2(colW, 12f), skills[i].Value / 100f, GameUi.Gold, null);
                    RectTransform next = GameUi.Rect(_in, "Next", new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(sx + 2f, -sy - 37f), new Vector2(0f, 3f));
                    Image nextImage = next.gameObject.AddComponent<Image>();
                    nextImage.color = GameUi.Good;
                    nextImage.raycastTarget = false;
                    void Show()
                    {
                        float v = Skill.Get(c, type), progress = here ? Skill.Progress(c, type) : 0f;
                        Set(level, v.ToString("0"));
                        GameUi.SetBar(bar, v / 100f);
                        var size = new Vector2((colW - 4f) * Mathf.Clamp01(progress), 3f);
                        if (next.sizeDelta != size) next.sizeDelta = size;
                    }
                    Show();
                    _live.Add(Show);
                }
                _y += (skills.Count + 1) / 2 * rowH + 4f;
            }
            Note(T("villagepleb_menu_skills_legend"), GameUi.Dim);
            EndCard();
        }

        private void DrawLevel(Humanoid c)
        {
            EpicMmo.Bonus b = EpicMmo.Of(c);
            BeginCard(T("villagepleb_mmo_card", b.Level));
            LiveBar(() => { EpicMmo.LevelOf(EpicMmo.Xp(c), out long into, out long toGo); return toGo > 0L ? into / (float)toGo : 1f; },
                    () => { EpicMmo.LevelOf(EpicMmo.Xp(c), out long into, out long toGo); return toGo > 0L ? T("villagepleb_mmo_xp", into, toGo) : T("villagepleb_mmo_top"); },
                    GameUi.Gold);
            Note(T("villagepleb_mmo_points", b.Points, b.Strength, b.Intellect, b.Body, b.Vigour), GameUi.Text);
            Note(T("villagepleb_mmo_bonus", b.Physical.ToString("0.#"), b.Magic.ToString("0.#"), b.Health.ToString("0"), b.Stamina.ToString("0"), b.PhysicalArmor.ToString("0.#"), b.MagicArmor.ToString("0.#")), GameUi.Text);
            Note(T("villagepleb_mmo_about"), GameUi.Dim);
            EndCard();
        }

        private static string Damage(Humanoid c, ItemDrop.ItemData w) =>
            $"{w.GetDamage().GetTotalDamage() * Mathf.Lerp(0.4f, 1f, Skill.Get(c, w.m_shared.m_skillType) / 100f):0}";

        private void ComeHere()
        {
            Player p = Player.m_localPlayer;
            if (p == null || _shown == null) return;
            GiveOrder(h => Home.Follow(h));
            if (Commandable && Vector3.Distance(p.transform.position, _shown.transform.position) > 15f && _shown.GetComponent<ZNetView>().IsOwner()) Brain.TeleportBehind(_shown, p);
        }

        private void DrawOrders(Humanoid c)
        {
            Order order = Companion.OrderOf(c);
            BeginCard(T("villagepleb_menu_what_it_does"));
            if (Sailing.Away(c)) Note(T("villagepleb_sail_busy_menu", Companion.NameOf(c)), GameUi.Warn);
            OrderButtons(c);
            Note(order switch
            {
                VillagePlebs.Order.Gather => T("villagepleb_menu_order_gather"),
                VillagePlebs.Order.Stay => T("villagepleb_menu_order_stay"),
                VillagePlebs.Order.Guard => T("villagepleb_menu_order_guard"),
                _ => T("villagepleb_menu_order_follow"),
            }, GameUi.Text);
            EndCard();

            // Shown whenever you may command it: pressing one takes over running it first (Brain.Claim), so it doesn't come and go
            // with which game happens to be running the companion.
            if (Commandable)
            {
                BeginCard(T("villagepleb_menu_errands"));
                Note(T("villagepleb_menu_errands_about", Companion.NameOf(c)), GameUi.Dim);
                float bw = (_width - 12f) / 3f;
                for (int k = 0; k < Errands.All.Length; k++)
                {
                    Errand pick = Errands.All[k].What;
                    // Go shopping only once a trader has been found.
                    bool can = pick != Errand.Shop || Shopping.AnyTrader();
                    _sig.Append(can);
                    Btn(Errands.All[k].Label, _left + (k % 3) * (bw + 6f), _y, bw, 32f, () => _pending = () => { BrainState bs = Brain.Claim(_shown, out string why); _note = bs != null ? Errands.Run(bs, pick) ?? "" : Words.Show(why); }, enabled: can, size: 16f);
                    if (k % 3 == 2 || k == Errands.All.Length - 1) _y += 38f;
                }
                EndCard();
            }

            BeginCard(T("villagepleb_menu_how_it_fights"));
            Style style = Companion.Chosen(c);
            TabBtn(T("villagepleb_menu_style_auto"), _left, _y, _width, 36f, style == Style.Auto, () => _pending = () => Change(z => z.Set(Keys.Style, (int)Style.Auto)));
            _y += 42f;
            Style[] styles = { Style.Aggressive, Style.Balanced, Style.Defensive, Style.Passive };
            Choices(styles.Select(s => Named("villagepleb_menu_style_", s)).ToArray(), Array.IndexOf(styles, style), i => { Style pick = styles[i]; _pending = () => Change(z => z.Set(Keys.Style, (int)pick)); });
            Note(style switch
            {
                Style.Aggressive => T("villagepleb_menu_style_aggressive_about"),
                Style.Defensive => T("villagepleb_menu_style_defensive_about"),
                Style.Passive => T("villagepleb_menu_style_passive_about"),
                Style.Balanced => T("villagepleb_menu_style_balanced_about"),
                _ => T("villagepleb_menu_style_auto_about"),
            }, GameUi.Text);
            if (style == Style.Auto) LiveText(() => string.IsNullOrEmpty(Brain.Get(c)?.AutoNote) ? "" : T("villagepleb_menu_right_now", Words.Show(Brain.Get(c).AutoNote)), GameUi.Good);
            _y += 4f;
            int retreat = Companion.RetreatOf(c);
            Stepper(T("villagepleb_menu_falls_back"), T("villagepleb_menu_percent_health", retreat), () => Change(z => z.Set(Keys.Retreat, Mathf.Clamp(retreat - 5, 0, 90))), () => Change(z => z.Set(Keys.Retreat, Mathf.Clamp(retreat + 5, 0, 90))));
            Note(T("villagepleb_menu_runs_below_half"), GameUi.Dim);
            Stepper(T("villagepleb_menu_fights_within"), T("villagepleb_menu_metres", MainConfig.engageRange.Value.ToString("0")), () => { MainConfig.engageRange.Value = Mathf.Clamp(MainConfig.engageRange.Value - 2f, 4f, 50f); SaveSettings(); },
                    () => { MainConfig.engageRange.Value = Mathf.Clamp(MainConfig.engageRange.Value + 2f, 4f, 50f); SaveSettings(); });
            Note(T("villagepleb_menu_fights_within_about"), GameUi.Dim);
            bool potions = Companion.Potions(c), protect = Companion.Protect(c);
            Check(T("villagepleb_menu_check_potions"), potions, () => _pending = () => Change(z => z.Set(Keys.Potions, !potions)));
            Check(T("villagepleb_menu_check_protect"), protect, () => _pending = () => Change(z => z.Set(Keys.Protect, !protect)));
            EndCard();

            BeginCard(T("villagepleb_menu_talking"));
            Note(T("villagepleb_menu_talking_about", Companion.NameOf(c)), GameUi.Text);
            EndCard();

        }

        private void DrawMissions(Humanoid c)
        {
            BrainState st = Brain.Get(c);
            bool living = Companion.OrderOf(c) == VillagePlebs.Order.Gather;
            BeginCard(T("villagepleb_menu_missions"));
            if (!Here(c)) Note(T("villagepleb_menu_shows_elsewhere"), GameUi.Dim);
            else
            {
                Mission Current() => Missions.All(c).LastOrDefault(m => m.Status == "active");
                float Done(Mission m) => m != null && m.StartCost > 0 ? Mathf.Clamp01(1f - m.Cost / (float)m.StartCost) : -1f;
                LiveText(() =>
                {
                    Goal goal = st.Goal;
                    if (goal == null) return Tint(T(Companion.OrderOf(c) == VillagePlebs.Order.Gather ? "villagepleb_menu_no_mission" : "villagepleb_menu_mission_at_home"), GameUi.Dim);
                    Mission m = Current();
                    return Done(m) >= 0f ? Tint(T("villagepleb_menu_mission_since", m.Day, Mathf.RoundToInt(Done(m) * 100f)), GameUi.Dim) : "";
                }, GameUi.Text);
                if (_make)
                {
                    RectTransform bar = GameUi.Bar(_in, new Vector2(_left, -_y), new Vector2(_width, 12f), 0f, GameUi.Good, null);
                    void Show()
                    {
                        float done = st.Goal != null ? Done(Current()) : -1f;
                        if (bar.gameObject.activeSelf != done >= 0f) bar.gameObject.SetActive(done >= 0f);
                        if (done >= 0f) GameUi.SetBar(bar, done);
                    }
                    Show();
                    _live.Add(Show);
                }
                _y += 18f;
                LiveText(() =>
                {
                    Goal goal = st.Goal;
                    if (goal == null) return "";
                    string what = Capital(Words.Show(goal.What));
                    var lines = new List<string> { "<b>" + Words.Show(goal.Station != null ? T("villagepleb_menu_goal_at", what, Words.Mid(Loc(goal.Station.m_name))) : what) + "</b>" };
                    if (goal.Raw.Count > 0) lines.Add(T("villagepleb_menu_gathering", Words.Show(goal.RawText())));
                    if (goal.Steps.Count > 0) lines.Add(T("villagepleb_menu_then_makes", string.Join(", ", goal.Steps.Select(x => Words.Mid(Loc(x.Key.m_item.m_itemData.m_shared.m_name))))));
                    if (goal.Smelt.Count > 0) lines.Add(Tint(T(goal.Smelt.Count == 1 ? "villagepleb_menu_smelt_one" : "villagepleb_menu_smelt_many", Capital(And(goal.Smelt.Select(Thing).ToList()))), GameUi.Dim));
                    if (goal.Ask.Count > 0) lines.Add(Tint(T("villagepleb_menu_needs_from_you", string.Join(", ", goal.Ask.Select(Thing))), GameUi.Warn));
                    var next = st.Candidates.Where(g => g != goal).Take(2).Select(g => Words.Show(g.What)).ToList();
                    if (next.Count == 1) lines.Add(Tint(T("villagepleb_menu_after_that", next[0]), GameUi.Dim));
                    else if (next.Count > 1) lines.Add(Tint(T("villagepleb_menu_after_that_two", next[0], next[1]), GameUi.Dim));
                    return string.Join("\n", lines.Select(l => Words.Show(l)));
                }, GameUi.Text);
            }
            LiveText(() =>
            {
                var past = Missions.All(c).Where(m => m.Status != "active").Reverse().Take(5).ToList();
                if (past.Count == 0) return "";
                return Tint(T("villagepleb_menu_lately"), GameUi.Dim) + "\n" + string.Join("\n", past.Select(m => m.Status == "done"
                    ? Tint(Words.Show(T("villagepleb_menu_mission_done", Words.Show(m.What), Words.Show(m.Why))), GameUi.Good)
                    : Words.Show(T("villagepleb_menu_mission_dropped", Words.Show(m.What), Words.Show(m.Why)))));
            }, GameUi.Text);
            Note(T("villagepleb_menu_missions_about"), GameUi.Dim);
            EndCard();
        }

        private void DrawHome(Humanoid c)
        {
            ZDO z = Companion.Zdo(c);
            Player p = Player.m_localPlayer;
            BrainState st = Brain.Get(c);
            bool bed = z.GetBool(Keys.HasBed, false);

            BeginCard(T("villagepleb_menu_bed_chests"));
            if (bed)
            {
                Vector3 spot = z.GetVec3(Keys.BedPos, Vector3.zero);
                Note(T("villagepleb_menu_bed_is", Vector3.Distance(spot, p.transform.position).ToString("0")), GameUi.Good);
            }
            else Note(T("villagepleb_menu_no_bed"), GameUi.Warn);
            List<Container> chests = Home.Chests(c);
            if (chests.Count == 0) Note(T("villagepleb_menu_no_chests"), GameUi.Dim);
            foreach (Container ch in chests)
            {
                Container chest = ch;
                LiveText(() => chest == null ? "" : T("villagepleb_menu_chest_slots", Loc(chest.m_name), chest.GetInventory().NrOfItems(), chest.GetInventory().GetWidth() * chest.GetInventory().GetHeight()), GameUi.Text);
            }
            _y += 4f;
            if (Mine && (!bed || chests.Count == 0))
                WideBtn(T(bed ? "villagepleb_menu_take_chests" : "villagepleb_menu_find_bed"), true,
                    () => _pending = () => { string took = Home.SetUp(c); if (took != null) Talk.Tell(c, took); else _note = T(bed ? "villagepleb_menu_no_empty_chest" : "villagepleb_menu_no_free_bed"); });
            if (Mine)
            {
                float letGo = bed ? 200f : 0f;
                Btn(T("villagepleb_menu_choose_bed"), _left, _y, _width - (bed ? letGo + 6f : 0f), 34f, () => _pending = () => { Home.StartAssign(c); CloseMenu(); });
                if (bed) Btn(T("villagepleb_menu_let_bed_go"), _left + _width - letGo, _y, letGo, 34f, () => _pending = () => { Bed b = Home.BedOf(c); if (b != null) Home.ToggleBed(b, c); else Change(cz => cz.Set(Keys.HasBed, false)); });
                _y += 40f;
                Note(T("villagepleb_menu_choosing_about", Companion.NameOf(c), MainConfig.menuKey.Value, MainConfig.assignKey.Value), GameUi.Dim);
            }
            EndCard();

            BeginCard(T("villagepleb_menu_your_chests"));
            Note(T("villagepleb_menu_your_chests_about"), GameUi.Dim);
            bool stow = Work.Stows(c), pantry = Work.UsesPantry(c), armory = Armory.Allowed(c);
            Check(T("villagepleb_menu_check_stow"), stow, () => _pending = () => Change(cz => cz.Set(Work.StowKey, !stow)));
            Check(T("villagepleb_menu_check_pantry"), pantry, () => _pending = () => Change(cz => cz.Set(Work.PantryKey, !pantry)));
            Check(T("villagepleb_menu_check_armory"), armory, () => _pending = () => Change(cz => cz.Set(Armory.Key, !armory)));
            Note(T(pantry ? "villagepleb_menu_pantry_on" : "villagepleb_menu_pantry_off"), GameUi.Dim);
            EndCard();

            bool living = Companion.OrderOf(c) == VillagePlebs.Order.Gather;
            BeginCard(T("villagepleb_menu_living"));
            if (!living)
            {
                Note(T("villagepleb_menu_not_living"), GameUi.Dim);
                Btn(T("villagepleb_menu_go_home"), _left, _y, 200f, 32f, () => _pending = () => GiveOrder(h => Home.GoHome(h)), true);
                _y += 38f;
            }
            Vector3 center = Work.Center(c);
            bool homeSet = z.GetBool(Work.HomeSetKey, false);
            Note(homeSet ? T("villagepleb_menu_home_set", Vector3.Distance(center, p.transform.position).ToString("0"))
                : bed ? T("villagepleb_menu_home_bed")
                : T("villagepleb_menu_home_none", center.x.ToString("0"), center.z.ToString("0")), GameUi.Dim);
            if (Commandable)
            {
                float clear = homeSet ? 210f : 0f;
                Btn(T("villagepleb_menu_make_home"), _left, _y, _width - (homeSet ? clear + 6f : 0f), 32f,
                    () => _pending = () => { Vector3 at = Player.m_localPlayer.transform.position; Change(cz => { cz.Set(Work.HomeSpotKey, at); cz.Set(Work.HomeSetKey, true); cz.Set(Keys.Post, at); }); }, true);
                if (homeSet) Btn(T(bed ? "villagepleb_menu_back_to_bed" : "villagepleb_menu_clear"), _left + _width - clear, _y, clear, 32f, () => _pending = () => Change(cz => cz.Set(Work.HomeSetKey, false)));
                _y += 38f;
            }
            int radius = Work.RadiusOf(c);
            Stepper(T("villagepleb_menu_goes_as_far"), T("villagepleb_menu_metres", radius), () => Change(cz => cz.Set(Keys.Radius, Mathf.Clamp(radius - 5, 10, 80))), () => Change(cz => cz.Set(Keys.Radius, Mathf.Clamp(radius + 5, 10, 80))));
            EndCard();

            List<Duties.Entry> duties = Duties.Read(z);
            Duties.Entry working = Duties.Active(c);
            BeginCard(T("villagepleb_menu_duties"));
            Note(T("villagepleb_menu_duties_about"), GameUi.Text);
            bool toYours = Duties.ToYours(c);
            Choices(new[] { T("villagepleb_menu_put_yours"), T("villagepleb_menu_put_its_own") }, toYours ? 0 : 1, i => { if (Commandable) _pending = () => Change(cz => cz.Set(Duties.DestKey, i)); });
            if (duties.Any(x => x.On) && !Duties.HasPlace(c))
                Note(T(toYours ? (Work.Stows(c) ? "villagepleb_menu_duties_no_chest" : "villagepleb_menu_duties_stow_off") : "villagepleb_menu_duties_no_own_chest"), GameUi.Warn);
            else if (toYours) Note(T("villagepleb_menu_duties_counts"), GameUi.Dim);
            const float sub = 30f;
            for (int di = 0; di < duties.Count; di++)
            {
                Duties.Entry e = duties[di];
                Duty dd = e.Duty;
                _sig.Append("|#").Append(di);
                if (_make) GameUi.Label(_in, (di + 1).ToString(), new Vector2(_left, -_y - 4f), new Vector2(24f, 24f), 18f, GameUi.Gold);
                float rowH = CheckAt(Duties.Label(dd), e.On, () => { if (Commandable) _pending = () => Change(cz => { var l = Duties.Read(cz); var x = l.First(a => a.Duty == dd); x.On = !x.On; Duties.Write(cz, l); }); },
                    _left + sub, _y, _width - sub - 160f);
                Btn(T("villagepleb_menu_up"), _left + _width - 154f, _y, 72f, 30f,
                    () => _pending = () => Change(cz => { var l = Duties.Read(cz); int k = l.FindIndex(a => a.Duty == dd); if (k > 0) { var t = l[k]; l[k] = l[k - 1]; l[k - 1] = t; Duties.Write(cz, l); } }), enabled: di > 0, size: 15f);
                Btn(T("villagepleb_menu_down"), _left + _width - 78f, _y, 78f, 30f,
                    () => _pending = () => Change(cz => { var l = Duties.Read(cz); int k = l.FindIndex(a => a.Duty == dd); if (k >= 0 && k < l.Count - 1) { var t = l[k]; l[k] = l[k + 1]; l[k + 1] = t; Duties.Write(cz, l); } }), enabled: di < duties.Count - 1, size: 15f);
                _y += Mathf.Max(rowH, 30f) + 6f;
                if (!e.On) continue;
                int step = Duties.Step(dd), max = Duties.Max(dd);
                Stepper(T(toYours ? "villagepleb_menu_keep_in_yours" : "villagepleb_menu_keep_in_its"), Duties.Amount(dd, e.Target),
                    () => Change(cz => { var l = Duties.Read(cz); var x = l.First(a => a.Duty == dd); x.Target = Mathf.Clamp(x.Target - step, step, max); Duties.Write(cz, l); }),
                    () => Change(cz => { var l = Duties.Read(cz); var x = l.First(a => a.Duty == dd); x.Target = Mathf.Clamp(x.Target + step, step, max); Duties.Write(cz, l); }), sub);
                LiveText(() =>
                {
                    Duties.Entry now = Duties.Read(Companion.Zdo(c)).FirstOrDefault(x => x.Duty == dd), active = Duties.Active(c);
                    return now == null ? "" : Tint(Duties.Status(c, now, active), active != null && active.Duty == dd ? GameUi.Good : GameUi.Dim);
                }, GameUi.Text, indent: sub);
                if (dd == Duty.Wood && Work.Axe(c) == null) Note(T("villagepleb_menu_needs_axe"), GameUi.Warn, indent: sub);
                if (dd == Duty.Mining && Work.Pickaxe(c) == null) Note(T("villagepleb_menu_needs_pickaxe"), GameUi.Warn, indent: sub);
                if (dd == Duty.Food && Companion.BestRanged(c) == null && Companion.BestMelee(c) == null) Note(T("villagepleb_menu_needs_weapon"), GameUi.Warn, indent: sub);
            }
            Note(T("villagepleb_menu_duties_cook"), GameUi.Dim);
            EndCard();

            BeginCard(T("villagepleb_menu_picks_up"));
            Note(T("villagepleb_menu_picks_up_about"), GameUi.Dim);
            Loot.Kinds list = Loot.List(c);
            var kinds = new[] { Loot.Kinds.Ore, Loot.Kinds.Wood, Loot.Kinds.Stone, Loot.Kinds.Food, Loot.Kinds.Creature, Loot.Kinds.Gear, Loot.Kinds.Other };
            float colW = (_width - 16f) / 2f;
            for (int i = 0; i < kinds.Length; i += 2)
            {
                float rowH = 0f;
                for (int j = i; j < Mathf.Min(i + 2, kinds.Length); j++)
                {
                    Loot.Kinds k = kinds[j];
                    string label = Named("villagepleb_menu_loot_", k);
                    rowH = Mathf.Max(rowH, CheckAt(label, (list & k) != 0, () => _pending = () => Change(z => z.Set(Loot.ListKey, z.GetInt(Loot.ListKey, (int)Loot.Default) ^ (int)k)),
                        _left + (j - i) * (colW + 16f), _y, colW));
                }
                _y += rowH + 6f;
            }
            if (!Loot.On(c)) Note(T("villagepleb_menu_loot_off"), GameUi.Warn);
            EndCard();

            BeginCard(T("villagepleb_menu_habits"));
            bool loot = Loot.On(c);
            Check(T("villagepleb_menu_check_loot"), loot, () => _pending = () => Change(z => z.Set(Loot.Key, !loot)));
            bool autoHome = Following.AutoHome(c);
            Check(T("villagepleb_menu_check_autohome"), autoHome, () => _pending = () => Change(z => z.Set(Following.AutoHomeKey, !autoHome)));
            Check(T("villagepleb_menu_check_decisions"), MainConfig.showDecisions.Value, () => _pending = () => { MainConfig.showDecisions.Value = !MainConfig.showDecisions.Value; SaveSettings(); });
            bool chatty = Talk.Chatty(c);
            Check(T("villagepleb_menu_check_chatty"), chatty, () => _pending = () => Change(z => z.Set(Talk.ChattyKey, !chatty)));
            EndCard();

            BeginCard(T("villagepleb_menu_its_food"));
            bool frugal = VillagePlebs.Food.Frugal(c);
            Check(T("villagepleb_menu_check_frugal"), frugal, () => _pending = () => Change(cz => cz.Set(VillagePlebs.Food.FrugalKey, !frugal)));
            Note(T(frugal ? "villagepleb_menu_frugal_on" : "villagepleb_menu_frugal_off"), GameUi.Dim);
            int ration = Work.RationOf(c);
            LiveText(() => Work.UsesPantry(c) ? T("villagepleb_menu_today_took", VillagePlebs.Food.AteToday(c), Work.TakenToday(c), Work.RationOf(c)) : T("villagepleb_menu_today", VillagePlebs.Food.AteToday(c)), GameUi.Text);
            if (Work.UsesPantry(c))
                Stepper(T("villagepleb_menu_most_a_day"), T("villagepleb_menu_n_food", ration), () => Change(cz => cz.Set(Work.RationKey, Mathf.Clamp(ration - 2, 2, 60))), () => Change(cz => cz.Set(Work.RationKey, Mathf.Clamp(ration + 2, 2, 60))));
            EndCard();

            Job jobs = Work.JobsOf(c);
            Goal goal = st.Goal;
            BeginCard(T(duties.Any(x => x.On) ? "villagepleb_menu_other_jobs" : "villagepleb_menu_jobs"));
            Job auto = Work.AutoJobs(c);
            Note(jobs == Job.None
                ? (auto == Job.None && goal == null ? T("villagepleb_menu_jobs_auto_nothing") : T("villagepleb_menu_jobs_auto", JobNames(auto | Goals.JobsFor(goal, out _))))
                : T("villagepleb_menu_jobs_chosen"), GameUi.Text);
            foreach (Job job in new[] { Job.Wood, Job.Stone, Job.Ore, Job.Forage, Job.Hunt, Job.Cook, Job.Plant, Job.Harvest, Job.Loot })
            {
                bool on = (jobs & job) != 0;
                string tool = Work.ToolFor(c, job);
                string label = job switch
                {
                    Job.Wood => T("villagepleb_menu_job_wood"),
                    Job.Stone => T("villagepleb_menu_job_stone"),
                    Job.Ore => T("villagepleb_menu_job_ore"),
                    Job.Forage => T("villagepleb_menu_job_forage"),
                    Job.Hunt => T("villagepleb_menu_job_hunt"),
                    Job.Cook => T("villagepleb_menu_job_cook"),
                    Job.Plant => T("villagepleb_menu_job_plant"),
                    Job.Harvest => T("villagepleb_menu_job_harvest"),
                    _ => T("villagepleb_menu_job_loot"),
                };
                Job pick = job;
                Check(label, on, () => _pending = () => Change(cz => cz.Set(Keys.Jobs, cz.GetInt(Keys.Jobs, 0) ^ (int)pick)));
                if (job == Job.Wood || job == Job.Stone || job == Job.Ore) Note(T("villagepleb_menu_tool", Words.Show(tool)), Work.ToolMissing(c, job) ? GameUi.Warn : GameUi.Dim, indent: 74f);
            }
            Note(T("villagepleb_menu_tools_about"), GameUi.Dim);
            EndCard();

            BeginCard(T("villagepleb_menu_gathered"));
            LiveText(() => st.Gathered.Count == 0 ? Tint(T("villagepleb_menu_nothing_yet"), GameUi.Dim)
                                                  : string.Join(", ", st.Gathered.OrderByDescending(kv => kv.Value).Select(kv => $"{kv.Value} {Words.Mid(Thing(kv.Key))}")), GameUi.Text);
            EndCard();

            BeginCard(T("villagepleb_menu_away"));
            Note(MainConfig.whileAway.Value switch
            {
                AwayMode.Off => T("villagepleb_menu_away_off"),
                AwayMode.Real => T("villagepleb_menu_away_real"),
                _ => T("villagepleb_menu_away_mild"),
            }, GameUi.Text);
            Note(T("villagepleb_menu_away_setting", MainConfig.whileAway.Value), GameUi.Dim);
            EndCard();

            BeginCard(T("villagepleb_menu_if_falls"));
            Note(T("villagepleb_menu_if_falls_about", MainConfig.respawnSeconds.Value.ToString("0")), GameUi.Text);
            EndCard();
        }

        private static string JobNames(Job j) => string.Join(", ", ((Job[])Enum.GetValues(typeof(Job))).Where(x => x != Job.None && (j & x) != 0).Select(x => Named("villagepleb_menu_jobname_", x)));

        private void DrawGear(Humanoid c)
        {
            Player p = Player.m_localPlayer;
            float space = GameUi.CellSpace;

            BeginCard(T("villagepleb_menu_its_gear"));
            bool mine = Mine;
            // All its slots in one grid across the card, as many to a row as fit.
            int across = Mathf.Max(1, Mathf.FloorToInt((_width + space - 64f) / space));
            Gear.Slot[] slots = Gear.All.OrderBy(s => s.Y).ThenBy(s => s.X).ToArray();
            for (int start = 0; start < slots.Length; start += across)
            {
                for (int j = 0; j < Mathf.Min(across, slots.Length - start); j++)
                {
                    if (!_make) continue;
                    var pos = new Vector2(_left + j * space, -_y);
                    int k = start + j;
                    Gear.Slot slot = slots[k];
                    InventoryElement e = GameUi.Slot(_in, pos, mine ? () =>
                    {
                        _pending = () => { ItemDrop.ItemData now = _shown != null ? Gear.In(_shown, slot) : null; if (now != null && !Companion.Take(_shown, Player.m_localPlayer, now, out string why) && why != null) _note = Words.Show(why); };
                        _nextCheck = 0f;
                    } : (Action)null);
                    void Show() => GameUi.Fill(e, Gear.In(c, slot), quality: false);
                    Show();
                    _live.Add(Show);
                    Line(_in, pos.x - (space - 64f) / 2f, _y + 66f, space, 14f, GameUi.Dim, TextAlignmentOptions.Top).text = Words.Show(slots[k].Label);
                }
                _y += 64f + 24f;
            }
            Note(T(mine ? "villagepleb_menu_gear_take_back" : "villagepleb_menu_gear_point"), GameUi.Dim);
            EndCard();

            Inventory its = c.GetInventory();
            BeginCard();
            LiveHeading(() => T("villagepleb_menu_its_bag", Gear.BagCount(c.GetInventory()), c.GetInventory().GetWidth() * Gear.BagRows, Carry.Weight(c).ToString("0"), Carry.Max(c).ToString("0")));
            LiveText(() => Carry.Over(c) ? T("villagepleb_menu_too_heavy") : "", GameUi.Warn);
            int width = its.GetWidth(), height = Mathf.Min(Gear.BagRows, its.GetHeight());
            _sig.Append("|bag").Append(width).Append('x').Append(height);
            for (int y = 0; y < height; y++)
                for (int x = 0; x < width; x++)
                {
                    if (!_make) continue;
                    var pos = new Vector2(_left + x * space, -_y - y * space);
                    int cx = x, cy = y;
                    InventoryElement e = GameUi.Slot(_in, pos, mine ? () =>
                    {
                        _pending = () => { ItemDrop.ItemData now = _shown != null ? _shown.GetInventory().GetItemAt(cx, cy) : null; if (now != null && !Companion.Take(_shown, Player.m_localPlayer, now, out string why) && why != null) _note = Words.Show(why); };
                        _nextCheck = 0f;
                    } : (Action)null);
                    void Show() => GameUi.Fill(e, c.GetInventory().GetItemAt(cx, cy), quality: false);
                    Show();
                    _live.Add(Show);
                }
            _y += height * space + 4f;
            Note(T("villagepleb_menu_bag_about"), GameUi.Dim);
            EndCard();

            if (mine && p != null)
            {
                List<ItemDrop.ItemData> Giveable() => p.GetInventory().GetAllItems().Where(i => !p.IsItemEquiped(i) && !i.m_equipped).ToList();
                var giveable = Giveable();
                BeginCard(T("villagepleb_menu_give_gear"));
                if (giveable.Count == 0) Note(T("villagepleb_menu_give_gear_none"), GameUi.Dim);
                else
                {
                    int per = Mathf.Max(1, Mathf.FloorToInt((_width + space - 64f) / space));
                    _sig.Append("|give").Append(giveable.Count);
                    for (int i = 0; i < giveable.Count; i++)
                    {
                        if (!_make) continue;
                        int k = i;
                        InventoryElement e = GameUi.Slot(_in, new Vector2(_left + i % per * space, -_y - i / per * space), () =>
                        {
                            _pending = () => { List<ItemDrop.ItemData> now = Giveable(); if (k < now.Count && !Companion.Give(_shown, Player.m_localPlayer, now[k], out string why) && why != null) _note = Words.Show(why); };
                            _nextCheck = 0f;
                        });
                        void Show() { List<ItemDrop.ItemData> now = Giveable(); GameUi.Fill(e, k < now.Count ? now[k] : null, quality: false); }
                        Show();
                        _live.Add(Show);
                    }
                    _y += (giveable.Count + per - 1) / per * space + 4f;
                    Note(T("villagepleb_menu_click_to_give"), GameUi.Dim);
                }
                EndCard();
            }
        }

        private void OpenGear()
        {
            Player p = Player.m_localPlayer;
            if (_shown == null || p == null) return;
            if (!Mine) { _note = T("villagepleb_menu_owner_gear"); return; }
            Container gear = _shown.GetComponent<Container>();
            if (gear == null) return;
            Patches.OpeningGear = true;
            try { gear.Interact(p, false, false); }
            finally { Patches.OpeningGear = false; }
            CloseMenu();
        }

        private bool LooksChange(Action<Humanoid> change)
        {
            if (!Mine) { _note = T("villagepleb_menu_owner_looks"); return false; }
            Humanoid c = _shown;
            if (Companion.Write(c, _ => change(c))) return true;
            _note = T("villagepleb_menu_busy");
            return false;
        }

        private void DrawLooks(Humanoid c, Vector2 pos, Vector2 size)
        {
            _sig.Append("|looks").Append(Mine);
            if (!_make) return;
            if (_looks == null || _looks.Id != Companion.IdOf(c)) { _looks?.Teardown(); _looks = new LooksPanel(c); }
            RectTransform area = GameUi.Rect(_window, "VP_Looks", new Vector2(0f, 1f), new Vector2(0f, 1f), pos, size);
            _looks.Build(area, T("villagepleb_looks_about", Companion.NameOf(c)), Mine, () => _pending = ApplyLooks);
        }

        private void ApplyLooks()
        {
            Humanoid c = _shown;
            if (_looks == null || c == null || _looks.Id != Companion.IdOf(c)) return;
            Looks.Look look = _looks.Current;
            if (!LooksChange(h => Looks.Set(h, look))) return;
            Portraits.Set(Companion.IdOf(c), _looks.Snapshot());
        }

        private void TickLooks()
        {
            if (_looks == null) return;
            if (!MenuOpen || _tab != Tab.Looks || _shown == null || Companion.IdOf(_shown) != _looks.Id) { _looks.Teardown(); _looks = null; return; }
            _looks.UpdatePerFrame();
        }

        private void DrawJournal(Humanoid c)
        {
            Dictionary<string, int> Tallies() => Journal.Tally(c);
            int Tally(string k) { Dictionary<string, int> t = Tallies(); return t.TryGetValue(k, out int n) ? n : 0; }
            BeginCard(T("villagepleb_menu_saga"));
            Stats((T("villagepleb_menu_days_with_you"), null, () => Journal.DaysWithYou(c).ToString()), (T("villagepleb_menu_kills"), null, () => Tally("kills").ToString()),
                  (T("villagepleb_menu_parries"), null, () => Tally("parries").ToString()), (T("villagepleb_menu_falls"), null, () => Tally("deaths").ToString()));
            LiveText(() =>
            {
                Dictionary<string, int> tally = Tallies();
                int Count(string k) => tally.TryGetValue(k, out int n) ? n : 0;
                var lines = new List<string>();
                var slain = new Dictionary<string, int>();
                foreach (var kv in tally.Where(x => x.Key.StartsWith("kill:"))) { string who = Words.Mid(Thing(OldCreature(kv.Key.Substring(5)))); slain[who] = (slain.TryGetValue(who, out int had) ? had : 0) + kv.Value; }
                var kills = slain.OrderByDescending(kv => kv.Value).Take(6).Select(kv => $"{kv.Value} {kv.Key}").ToList();
                if (kills.Count > 0) lines.Add(T("villagepleb_menu_most_slain", string.Join(", ", kills)));
                var bosses = tally.Where(kv => kv.Key.StartsWith("boss:")).Select(kv => Thing(OldCreature(kv.Key.Substring(5)))).Distinct().ToList();
                lines.Add(bosses.Count > 0 ? Tint(Words.Show(T("villagepleb_menu_bosses", string.Join(", ", bosses))), GameUi.Good) : Tint(T("villagepleb_menu_no_boss"), GameUi.Dim));
                lines.Add(T("villagepleb_menu_made_counts", Count("made"), Count("upgraded"), Count("trips")));
                var lands = tally.Where(kv => kv.Key.StartsWith("place:") && kv.Key != "place:dungeon").Select(kv => Thing(OldLand(kv.Key.Substring(6)))).Distinct().ToList();
                int dungeons = Count("place:dungeon");
                if (lands.Count > 0) lines.Add(Tint(Words.Show(dungeons == 0 ? T("villagepleb_menu_has_seen", string.Join(", ", lands)) : T(dungeons == 1 ? "villagepleb_menu_has_seen_dungeon" : "villagepleb_menu_has_seen_dungeons", string.Join(", ", lands), dungeons)), GameUi.Dim));
                return string.Join("\n", lines.Select(l => Words.Show(l)));
            }, GameUi.Text);
            EndCard();

            BeginCard(T("villagepleb_menu_moments"));
            // Each moment with its day in a column to the left, the words wrapping under themselves.
            LiveText(() =>
            {
                var entries = Journal.Entries(c);
                if (entries.Count == 0) return Tint(T("villagepleb_menu_no_moments"), GameUi.Dim);
                return string.Join("\n", Enumerable.Reverse(entries).Take(40).Select(e =>
                {
                    int bar = e.IndexOf('|');
                    string day = bar > 0 ? e.Substring(0, bar) : "?", text = bar > 0 ? e.Substring(bar + 1) : e;
                    return Tint(Words.Show(T("villagepleb_menu_day", day)), GameUi.Dim) + "<pos=82><indent=82>" + Words.Show(text) + "</indent>";
                }));
            }, GameUi.Text);
            EndCard();
        }

        private void BeginCard(string title = null)
        {
            _sig.Append("\n[").Append(title);
            _y = CardPad;
            _left = CardPad;
            if (_cardW > 0f) _width = _cardW - CardPad * 2f;
            if (_make)
            {
                _card = GameUi.Box(_list, new Vector2(0f, -_listY), new Vector2(_cardW, 40f));
                _in = _card;
            }
            if (title != null) Heading(title);
        }

        private static TMP_Text _measure;

        // How tall each live note has been on this page: it keeps that room when its words get shorter or go, so a line that comes and
        // goes (its "right now", a fresh list) only lays the menu out again the first time it grows.
        private readonly List<float> _liveHeights = new List<float>();
        private string _liveKey = "";
        private int _liveIndex;

        // How tall some words come out at this width: a note that changes its words only lays the menu out again when this changes.
        private static float Measure(string text, float w, float size, float spacing)
        {
            if (_measure == null)
            {
                _measure = GameUi.Label(GameUi.Canvas, "", new Vector2(-20000f, 20000f), new Vector2(10f, 10f), size, Color.clear);
                _measure.gameObject.name = "VillagePlebsMeasure";
            }
            _measure.fontSize = size;
            _measure.paragraphSpacing = spacing;
            return Mathf.Ceil(_measure.GetPreferredValues(text, w, 0f).y);
        }

        private static string Tint(string text, Color c) => string.IsNullOrEmpty(text) ? text : $"<color=#{ColorUtility.ToHtmlStringRGBA(c)}>{text}</color>";

        // A note whose words change while the menu is open: rewritten in place, like the bars, rather than drawing the menu again.
        private void LiveText(Func<string> text, Color color, float size = 17f, float indent = 0f, float spacing = 5f)
        {
            float w = _width - indent;
            string now = Words.Show(text() ?? "");
            float h = now.Length == 0 ? 0f : Measure(now, w, size, spacing);
            int slot = _liveIndex++;
            if (slot < _liveHeights.Count) h = _liveHeights[slot] = Mathf.Max(h, _liveHeights[slot]);
            else _liveHeights.Add(h);
            _sig.Append("|L").Append(h);
            if (_make)
            {
                TMP_Text t = GameUi.Label(_in, now, new Vector2(_left + indent, -_y), new Vector2(w, Mathf.Max(h, 1f)), size, color);
                t.paragraphSpacing = spacing;
                void Show() { string s = Words.Show(text() ?? ""); if (t.text != s) t.text = s; }
                _live.Add(Show);
            }
            if (h > 0f) _y += h + 5f;
        }

        private void LiveHeading(Func<string> title)
        {
            _sig.Append("|H");
            if (_make)
            {
                TMP_Text t = GameUi.HeaderBar(_in, Words.Show(title()), new Vector2(_left, -_y), new Vector2(_width, 32f), 18f);
                _live.Add(() => Set(t, title()));
            }
            _y += 40f;
        }

        private void EndCard()
        {
            _sig.Append(']');
            float h = _y + CardPad - 5f;
            if (_make) _card.sizeDelta = new Vector2(_cardW, h);
            _listY += h + CardGap;
        }

        private void Heading(string title, float indent = 0f, bool advance = true, float width = -1f)
        {
            title = Words.Show(title);
            _sig.Append("|h").Append(title);
            if (_make) GameUi.HeaderBar(_in, title, new Vector2(_left + indent, -_y), new Vector2(width > 0f ? width : _width - indent, 32f), 18f);
            if (advance) _y += 40f;
        }

        private void HeadingRow(string title, params (string label, bool lit, Action click)[] buttons)
        {
            float x = _left + _width;
            for (int i = buttons.Length - 1; i >= 0; i--)
            {
                x -= 130f;
                Btn(buttons[i].label, x, _y, 130f, 32f, buttons[i].click, buttons[i].lit, size: 16f);
                x -= 8f;
            }
            Heading(title, 0f, false, x - _left - 8f);
            _y += 38f;
        }

        private void Note(string text, Color color, float size = 17f, float indent = 0f, bool bold = false)
        {
            text = Words.Show(text);
            _sig.Append("|n").Append(color.GetHashCode()).Append(text);
            if (!_make) return;
            float w = _width - indent;
            TMP_Text t = GameUi.Label(_in, text, new Vector2(_left + indent, -_y), new Vector2(w, 24f), size, color);
            if (bold) t.fontStyle = FontStyles.Bold;
            float h = t.GetPreferredValues(text, w, 0f).y;
            ((RectTransform)t.transform).sizeDelta = new Vector2(w, h);
            _y += h + 5f;
        }

        private void LiveNote(Func<string> text, Color color, float size = 17f)
        {
            _sig.Append("|l");
            if (!_make) return;
            TMP_Text t = Line(_in, _left, _y, _width, size, color);
            void Show() => Set(t, text());
            Show();
            _live.Add(Show);
            _y += size * 1.35f + 5f;
        }

        private static TMP_Text Line(Transform parent, float x, float y, float w, float size, Color color, TextAlignmentOptions align = TextAlignmentOptions.TopLeft)
        {
            TMP_Text t = GameUi.Label(parent, "", new Vector2(x, -y), new Vector2(w, size * 1.8f), size, color, null, align);
            t.textWrappingMode = TextWrappingModes.NoWrap;
            t.overflowMode = TextOverflowModes.Ellipsis;
            return t;
        }

        private static void Set(TMP_Text t, string text) { text = Words.Show(text); if (t.text != text) t.text = text; }

        private Button Btn(string label, float x, float y, float w, float h, Action click, bool lit = false, bool enabled = true, float size = 17f, Color? color = null)
        {
            label = Words.Show(label);
            _sig.Append("|b").Append(label).Append(lit ? '*' : '.').Append(enabled ? '1' : '0');
            if (!_make) return null;
            Button b = GameUi.Button(_in, label, new Vector2(x, -y), new Vector2(w, h), () => { click(); _nextCheck = 0f; }, enabled, size);
            if (enabled && (lit || color != null)) GameUi.Label(b).color = color ?? GameUi.Gold;
            return b;
        }

        private void WideBtn(string label, bool lit, Action click, float h = 34f, Color? color = null)
        {
            Btn(label, _left, _y, _width, h, click, lit, color: color);
            _y += h + 6f;
        }

        private void TabBtn(string label, float x, float y, float w, float h, bool chosen, Action click, float size = 17f)
        {
            label = Words.Show(label);
            _sig.Append("|t").Append(label).Append(chosen ? '*' : '.');
            if (_make) GameUi.Tab(_in, label, new Vector2(x, -y), new Vector2(w, h), chosen, () => { click(); _nextCheck = 0f; }, size);
        }

        private void Choices(string[] labels, int chosen, Action<int> pick, float h = 34f)
        {
            float bw = (_width - 6f * (labels.Length - 1)) / labels.Length;
            for (int i = 0; i < labels.Length; i++)
            {
                int k = i;
                TabBtn(labels[i], _left + i * (bw + 6f), _y, bw, h, i == chosen, () => pick(k));
            }
            _y += h + 6f;
        }

        private float CheckAt(string label, bool on, Action toggle, float x, float y, float w)
        {
            label = Words.Show(label);
            _sig.Append("|k").Append(on ? '1' : '0').Append(label);
            if (!_make) return 0f;
            const float bw = 26f, bh = 30f;
            GameUi.Check(_in, new Vector2(x, -y - (bh - bw) / 2f), bw, on, _ => { toggle(); _nextCheck = 0f; });
            float tw = w - bw - 10f;
            TMP_Text t = GameUi.Label(_in, label, new Vector2(x + bw + 10f, -y), new Vector2(tw, bh), 17f, GameUi.Text);
            float th = t.GetPreferredValues(label, tw, 0f).y;
            var tr = (RectTransform)t.transform;
            tr.sizeDelta = new Vector2(tw, th);
            if (th < bh) tr.anchoredPosition = new Vector2(x + bw + 10f, -y - (bh - th) / 2f);
            t.raycastTarget = true;
            Button words = t.gameObject.AddComponent<Button>();
            words.transition = Selectable.Transition.None;
            words.navigation = new Navigation { mode = Navigation.Mode.None };
            words.onClick.AddListener(() => { toggle(); _nextCheck = 0f; });
            return Mathf.Max(bh, th);
        }

        private void Check(string label, bool on, Action toggle, float indent = 0f)
        {
            float h = CheckAt(label, on, toggle, _left + indent, _y, _width - indent);
            _y += h + 6f;
        }

        private void Stepper(string label, string value, Action down, Action up, float indent = 0f)
        {
            label = Words.Show(label); value = Words.Show(value);
            _sig.Append("|s").Append(label).Append(value);
            float x = _left + indent, lw = Mathf.Max(120f, Mathf.Min(300f, _width - indent - 250f));
            if (_make)
            {
                TMP_Text t = Line(_in, x, _y + 5f, lw, 17f, GameUi.Text);
                t.text = label;
                t.fontStyle = FontStyles.Bold;
            }
            Btn("-", x + lw + 8f, _y, 40f, 30f, () => _pending = down, size: 20f);
            if (_make) Line(_in, x + lw + 52f, _y + 5f, 146f, 17f, GameUi.Gold, TextAlignmentOptions.Top).text = value;
            Btn("+", x + lw + 202f, _y, 40f, 30f, () => _pending = up, size: 20f);
            _y += 36f;
        }

        private void Stats(params (string label, string value, Func<string> live)[] stats)
        {
            for (int i = 0; i < stats.Length; i++) stats[i] = (Words.Show(stats[i].label), Words.Show(stats[i].value), stats[i].live);
            foreach (var s in stats) _sig.Append("|=").Append(s.label).Append(s.live == null ? s.value : "");
            if (!_make) return;
            float cw = _width / stats.Length;
            for (int i = 0; i < stats.Length; i++)
            {
                var s = stats[i];
                TMP_Text value = Line(_in, _left + i * cw, _y, cw - 6f, 26f, GameUi.Gold);
                value.fontStyle = FontStyles.Bold;
                if (s.live != null) { Func<string> f = s.live; void Show() => Set(value, f()); Show(); _live.Add(Show); }
                else value.text = s.value;
                Line(_in, _left + i * cw, _y + 36f, cw - 6f, 15f, GameUi.Dim).text = s.label;
            }
            _y += 62f;
        }

        private void LiveBar(Func<float> fraction, Func<string> text, Color color, float height = 24f)
        {
            _sig.Append("|r");
            if (_make)
            {
                RectTransform bar = GameUi.Bar(_in, new Vector2(_left, -_y), new Vector2(_width, height), fraction(), color, Words.Show(text()), 15f);
                _live.Add(() => GameUi.SetBar(bar, fraction(), Words.Show(text())));
            }
            _y += height + 5f;
        }

        private StringBuilder AppendItem(ItemDrop.ItemData i)
        {
            if (i == null) return _sig.Append("|-");
            return _sig.Append('|').Append(i.m_shared.m_name).Append(',').Append(i.m_stack).Append(',').Append(i.m_quality)
                .Append(',').Append((int)(i.GetDurabilityPercentage() * 20f)).Append(',').Append(i.m_equipped);
        }

        private static string Named(string prefix, Enum value) => T(prefix + value.ToString().ToLowerInvariant());

        private static string Loc(string s) => Localization.instance.Localize(s);

        private static string OldCreature(string s)
        {
            if (string.IsNullOrEmpty(s) || s.IndexOf('$') >= 0 || Words.Packed(s) || Localization.instance == null) return s;
            string token = "$enemy_" + s.ToLowerInvariant().Replace(" ", "");
            string shown = Localization.instance.Localize(token);
            return shown.StartsWith("[") ? s : token;
        }

        private static string OldLand(string s) => s switch
        {
            "place" => "$biome_meadows", "forest" => "$biome_blackforest", "swamp" => "$biome_swamp", "mountain" => "$biome_mountain",
            "plains" => "$biome_plains", "mist" => "$biome_mistlands", "ash land" => "$biome_ashlands", "frozen north" => "$biome_deepnorth",
            "sea" => "$biome_ocean", _ => s,
        };

        private static string Thing(string s) { string t = Words.Show(s); return t != null && t.IndexOf('$') >= 0 ? Loc(t) : t; }
        private static string And(IList<string> items) =>
            items.Count == 0 ? "" : items.Count == 1 ? items[0] : T("villagepleb_menu_and", string.Join(", ", items.Take(items.Count - 1)), items[items.Count - 1]);

        private static string Capital(string s) => string.IsNullOrEmpty(s) ? s : char.ToUpper(s[0]) + s.Substring(1);

        private void DestroyMenuResources() => GameUi.Destroy();
    }
}
