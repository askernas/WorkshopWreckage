using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.UI;
using UnityEngine.Profiling;
using UnityEngine.UI;
using Unity.Profiling;

namespace WorkshopWreckage
{
    /// <summary>HUD and menus, built in uGUI.</summary>
    public partial class Hud : MonoBehaviour
    {
        static readonly Color Bg = new(0.11f, 0.08f, 0.06f, 0.94f), Brass = new(0.82f, 0.63f, 0.32f), BrassDim = new(0.55f, 0.42f, 0.22f),
            Cream = new(0.96f, 0.91f, 0.8f), Muted = new(0.72f, 0.66f, 0.56f), GoodC = new(0.55f, 0.85f, 0.45f), BadC = new(0.95f, 0.42f, 0.32f);
        /// <summary>The target box has no panel behind it, so its sub line sits straight on the world.
        /// Muted washes out on a light surface (the lathe, a chrome heap); this keeps it below the
        /// title without disappearing.</summary>
        static readonly Color MutedOnWorld = new(0.85f, 0.80f, 0.70f);

        Game _g;
        Canvas _canvas;
        RectTransform _hud, _menus;
        Text _objective, _target, _level, _title, _sub, _hint, _feedback, _progress, _zone, _heldName, _heldCount, _heldDest, _carried, _inspectText, _debug, _status;
        Image _cross;
        RectTransform _toasts, _hotbar, _heldPanel, _inspect, _skillBar;
        readonly List<(string id, Text text, Image fill)> _skillSlots = new();
        string _skillBarKey = "";
        float _feedbackT;
        bool _hotbarDirty = true;
        readonly Dictionary<string, RectTransform> _screens = new();
        string _screen = "", _backTo = "pause";
        InputField _seed;
        RectTransform _saveList, _loadList, _logBody;
        InputField _saveName;
        Text _saveHeader, _loadHeader;
        Button _saveOver, _saveNew;
        Text _logText;
        Button _continue;
        readonly List<float> _frames = new();
        ProfilerRecorder _drawCalls;

        public void Init(Game g)
        {
            _g = g;
            _canvas = gameObject.AddComponent<Canvas>();
            _canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            _canvas.sortingOrder = 10;
            var scaler = gameObject.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920, 1080);
            scaler.matchWidthOrHeight = 1;
            gameObject.AddComponent<GraphicRaycaster>();
            if (FindAnyObjectByType<EventSystem>() == null)
            {
                var es = new GameObject("EventSystem", typeof(EventSystem), typeof(InputSystemUIInputModule));
                es.transform.SetParent(transform, false);
            }
            _hud = Rect("HUD", transform, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
            _menus = Rect("Menus", transform, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
            BuildHud();
            BuildMenus();
            _drawCalls = ProfilerRecorder.StartNew(ProfilerCategory.Render, "Draw Calls Count");
        }

        void OnDestroy() => _drawCalls.Dispose();

        // ------------------------------------------------------------------ builders

        static RectTransform Rect(string name, Transform parent, Vector2 aMin, Vector2 aMax, Vector2 offMin, Vector2 offMax)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var r = (RectTransform)go.transform;
            r.anchorMin = aMin; r.anchorMax = aMax; r.offsetMin = offMin; r.offsetMax = offMax;
            return r;
        }

        /// <summary>Pins a rect to an anchor with a pixel offset and size (y down).</summary>
        static RectTransform At(string name, Transform parent, Vector2 anchor, Vector2 offset, Vector2 size)
        {
            var r = Rect(name, parent, new Vector2(anchor.x, 1 - anchor.y), new Vector2(anchor.x, 1 - anchor.y), Vector2.zero, Vector2.zero);
            r.pivot = new Vector2(0, 1);
            r.anchoredPosition = new Vector2(offset.x, -offset.y);
            r.sizeDelta = size;
            return r;
        }

        static Image Panel(RectTransform r, Color bg, Color border)
        {
            var img = r.gameObject.AddComponent<Image>();
            img.color = border;
            var inner = Rect("bg", r, Vector2.zero, Vector2.one, new Vector2(2, 2), new Vector2(-2, -2));
            inner.gameObject.AddComponent<Image>().color = bg;
            inner.gameObject.AddComponent<LayoutElement>().ignoreLayout = true;
            inner.SetAsFirstSibling();
            return img;
        }

        /// <summary>onWorld: text with no panel behind it. A drop shadow only darkens one side and
        /// vanishes against a light background, so those labels get an outline on all four instead.</summary>
        static Text Label(Transform parent, string name, int size, Color c, TextAnchor anchor, Vector2 aMin, Vector2 aMax, Vector2 offMin, Vector2 offMax, bool display = false, bool onWorld = false)
        {
            var r = Rect(name, parent, aMin, aMax, offMin, offMax);
            var t = r.gameObject.AddComponent<Text>();
            t.font = display ? TextAtlas.Display : TextAtlas.Font;
            t.fontSize = size;
            t.color = c;
            t.alignment = anchor;
            t.horizontalOverflow = HorizontalWrapMode.Wrap;
            t.verticalOverflow = VerticalWrapMode.Overflow;
            t.raycastTarget = false;
            if (onWorld)
            {
                var ol = r.gameObject.AddComponent<Outline>();
                ol.effectColor = new Color(0, 0, 0, 0.9f);
                ol.effectDistance = new Vector2(1.4f, 1.4f);   // thin enough that 16 pt glyphs stay open
            }
            else
            {
                var sh = r.gameObject.AddComponent<Shadow>();
                sh.effectColor = new Color(0, 0, 0, 0.75f);
                sh.effectDistance = new Vector2(1.5f, -1.5f);
            }
            return t;
        }

        static Text Fill(Transform parent, string name, int size, Color c, TextAnchor a = TextAnchor.UpperLeft)
            => Label(parent, name, size, c, a, Vector2.zero, Vector2.one, new Vector2(14, 10), new Vector2(-14, -10));

        void BuildHud()
        {
            var cross = At("Cross", _hud, new Vector2(0.5f, 0.5f), new Vector2(-3, -3), new Vector2(6, 6));
            _cross = cross.gameObject.AddComponent<Image>();
            _cross.color = Cream;
            var tbox = At("Target", _hud, new Vector2(0.5f, 0.5f), new Vector2(-620, 34), new Vector2(1240, 110));
            _title = Label(tbox, "title", 22, Cream, TextAnchor.UpperCenter, new Vector2(0, 1), Vector2.one, new Vector2(0, -32), Vector2.zero, false, true);
            _sub = Label(tbox, "sub", 16, MutedOnWorld, TextAnchor.UpperCenter, new Vector2(0, 1), Vector2.one, new Vector2(0, -56), new Vector2(0, -32), false, true);
            _hint = Label(tbox, "hint", 18, Brass, TextAnchor.UpperCenter, new Vector2(0, 1), Vector2.one, new Vector2(0, -84), new Vector2(0, -56), false, true);
            // Each line owns a fixed band, so a wrapped second line overflows onto the line below:
            // a long destination title landed on top of "N / M stored". They stay on one line and
            // grow sideways instead, which there is room for — the box is centred on screen.
            _title.horizontalOverflow = _sub.horizontalOverflow = HorizontalWrapMode.Overflow;
            var fb = At("Feedback", _hud, new Vector2(0.5f, 0.5f), new Vector2(-500, -90), new Vector2(1000, 40));
            _feedback = Fill(fb, "t", 22, GoodC, TextAnchor.MiddleCenter);
            _toasts = At("Log", _hud, Vector2.zero, new Vector2(20, 18), new Vector2(640, 220));
            var vl = _toasts.gameObject.AddComponent<VerticalLayoutGroup>();
            vl.spacing = 2; vl.childControlHeight = false; vl.childForceExpandHeight = false; vl.childAlignment = TextAnchor.UpperLeft;
            var tl = At("Progress", _hud, new Vector2(1, 0), new Vector2(-620, 20), new Vector2(600, 136));
            Panel(tl, new Color(0.1f, 0.07f, 0.05f, 0.72f), BrassDim);
            Text Row(string n, int size, Color c, int top) => Label(tl, n, size, c, TextAnchor.UpperLeft, new Vector2(0, 1), Vector2.one, new Vector2(14, -top - 24), new Vector2(-10, -top));
            // design v1.0 §3.3: the level line (completed destinations) first, restoration second, never mixed
            _level = Row("l", 20, Brass, 8);
            _progress = Row("p", 16, Cream, 33);
            _target = Row("t", 15, Cream, 56);
            _zone = Row("z", 15, Muted, 79);
            _objective = Row("o", 15, Cream, 104);
            _heldPanel = At("Held", _hud, Vector2.one, new Vector2(-580, -230), new Vector2(560, 130));
            Panel(_heldPanel, new Color(0.1f, 0.07f, 0.05f, 0.8f), BrassDim);
            _heldName = Label(_heldPanel, "n", 20, Cream, TextAnchor.UpperLeft, Vector2.zero, Vector2.one, new Vector2(14, 0), new Vector2(-10, -10));
            _heldCount = Label(_heldPanel, "c", 16, Muted, TextAnchor.UpperLeft, Vector2.zero, Vector2.one, new Vector2(14, 0), new Vector2(-10, -40));
            _heldDest = Label(_heldPanel, "d", 16, Brass, TextAnchor.UpperLeft, Vector2.zero, Vector2.one, new Vector2(14, 0), new Vector2(-10, -64));
            _carried = Label(_heldPanel, "k", 15, Muted, TextAnchor.UpperLeft, Vector2.zero, Vector2.one, new Vector2(14, 0), new Vector2(-10, -92));
            _hotbar = At("Hotbar", _hud, new Vector2(0.5f, 1), new Vector2(-700, -60), new Vector2(1400, 44));
            _skillBar = At("SkillBar", _hud, new Vector2(0.5f, 1), new Vector2(-500, -106), new Vector2(1000, 40));
            var sbl = _skillBar.gameObject.AddComponent<HorizontalLayoutGroup>();
            sbl.spacing = 8; sbl.childAlignment = TextAnchor.MiddleCenter; sbl.childControlWidth = true; sbl.childForceExpandWidth = false;
            var hl = _hotbar.gameObject.AddComponent<HorizontalLayoutGroup>();
            hl.spacing = 6; hl.childAlignment = TextAnchor.MiddleCenter; hl.childControlWidth = true; hl.childForceExpandWidth = false;
            _inspect = At("Inspect", _hud, new Vector2(1, 0.5f), new Vector2(-520, -270), new Vector2(480, 540));
            Panel(_inspect, Bg, BrassDim);
            _inspectText = Fill(_inspect, "t", 18, Cream);
            _inspectText.supportRichText = true;
            _inspect.gameObject.SetActive(false);
            var dbg = At("Debug", _hud, new Vector2(1, 0), new Vector2(-440, 20), new Vector2(420, 300));
            _debug = Fill(dbg, "t", 15, new Color(0.75f, 1f, 0.75f));
            dbg.gameObject.SetActive(false);
        }

        // ---- screens that refresh on a timer (workbench, robots, modify) rebuild their rows,
        // but the old rows stay on screen when nothing changed: a rebuilt button under the
        // mouse would lose its highlight, and old + new rows laid out together flash
        readonly Dictionary<Transform, List<GameObject>> _oldRows = new();

        /// <summary>Takes a container's rows out of the layout before it is rebuilt; at the end
        /// of the frame (CommitRows) the old rows come back if the new ones look the same.</summary>
        void ClearChildren(Transform t)
        {
            if (_oldRows.TryGetValue(t, out var old))
            {
                // rebuilt twice in one frame: the first rebuild goes, the old rows are kept for the compare
                foreach (Transform c in t) if (!old.Contains(c.gameObject)) { c.gameObject.SetActive(false); Destroy(c.gameObject); }
                return;
            }
            old = new List<GameObject>();
            foreach (Transform c in t) { c.gameObject.SetActive(false); old.Add(c.gameObject); }
            _oldRows[t] = old;
        }

        static string RowLook(IEnumerable<GameObject> rows)
        {
            var sb = new System.Text.StringBuilder();
            foreach (var r in rows)
            {
                sb.Append('|');
                foreach (var g in r.GetComponentsInChildren<Graphic>(true))
                {
                    if (g is Text t) sb.Append(t.text).Append(t.fontSize).Append(t.color);
                    var le = g.GetComponent<LayoutElement>();
                    if (le != null) sb.Append(le.preferredWidth).Append(le.preferredHeight);
                    if (g.GetComponent<Selectable>() is Selectable s) sb.Append(s.interactable);
                }
            }
            return sb.ToString();
        }

        void CommitRows()
        {
            foreach (var (t, old) in _oldRows)
            {
                if (t == null) continue;
                var fresh = new List<GameObject>();
                foreach (Transform c in t) if (!old.Contains(c.gameObject)) fresh.Add(c.gameObject);
                bool same = RowLook(old) == RowLook(fresh);
                foreach (var g in same ? fresh : old) Destroy(g);
                if (same) { foreach (var g in fresh) g.SetActive(false); foreach (var g in old) g.SetActive(true); }
            }
            _oldRows.Clear();
        }

        void LateUpdate()
        {
            if (Game.Booting) return;   // the game is still being built (loading splash)
            if (_oldRows.Count > 0) CommitRows();
        }

        Button MakeButton(Transform parent, string text, System.Action cb)
        {
            var go = DefaultControls.CreateButton(new DefaultControls.Resources());
            go.transform.SetParent(parent, false);
            var le = go.AddComponent<LayoutElement>();
            le.preferredHeight = 44;
            var img = go.GetComponent<Image>();
            img.color = new Color(0.2f, 0.14f, 0.09f, 0.95f);
            var b = go.GetComponent<Button>();
            var cols = b.colors;
            cols.highlightedColor = new Color(1.6f, 1.5f, 1.3f);
            cols.pressedColor = new Color(2f, 1.8f, 1.4f);
            b.colors = cols;
            var t = go.GetComponentInChildren<Text>();
            t.text = text;
            t.font = TextAtlas.Font;
            t.fontSize = 22;
            t.color = Cream;
            b.onClick.AddListener(() => { Sfx.Play("ui_click"); cb(); });
            if (parent.IsChildOf(_menus)) UiStyle.InkButton(b);
            return b;
        }

        RectTransform Screen(string name, float width)
        {
            var dim = Rect(name, _menus, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
            dim.gameObject.AddComponent<Image>().color = new Color(0.03f, 0.02f, 0.01f, 0.55f);
            var panel = Rect("Panel", dim, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero);
            panel.sizeDelta = new Vector2(width, 0);
            Panel(panel, Bg, BrassDim);
            UiStyle.Blueprint(panel);   // the look: every menu is a taped blueprint sheet
            var fit = panel.gameObject.AddComponent<ContentSizeFitter>();
            fit.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            var v = panel.gameObject.AddComponent<VerticalLayoutGroup>();
            v.padding = new RectOffset(30, 30, 26, 26);
            v.spacing = 10;
            v.childControlHeight = true; v.childControlWidth = true; v.childForceExpandHeight = false;
            _screens[name] = dim;
            dim.gameObject.SetActive(false);
            return panel;
        }

        Text Title(Transform p, string text, int size = 40)
        {
            var go = new GameObject("Title", typeof(RectTransform), typeof(Text), typeof(LayoutElement));
            go.transform.SetParent(p, false);
            var t = go.GetComponent<Text>();
            t.text = text; t.font = TextAtlas.Display; t.fontSize = size; t.color = p.IsChildOf(_menus) ? UiStyle.Ink : Brass; t.alignment = TextAnchor.MiddleCenter;
            t.horizontalOverflow = HorizontalWrapMode.Overflow;
            go.GetComponent<LayoutElement>().preferredHeight = size * (text.Count(c => c == '\n') + 1) * 1.25f;
            return t;
        }

        Text Para(Transform p, string text, int size, Color c, float height)
        {
            var go = new GameObject("Text", typeof(RectTransform), typeof(Text), typeof(LayoutElement));
            go.transform.SetParent(p, false);
            var t = go.GetComponent<Text>();
            // on blueprint paper the warm text colours become ink
            if (p.IsChildOf(_menus)) c = c == Cream ? UiStyle.Ink : c == Muted ? UiStyle.InkDim : c;
            t.text = text; t.font = TextAtlas.Font; t.fontSize = size; t.color = c; t.alignment = TextAnchor.MiddleCenter;
            t.supportRichText = true;
            go.GetComponent<LayoutElement>().preferredHeight = height;
            return t;
        }

        RectTransform _mainPanel;
        readonly List<GameObject> _mainTitleBits = new();

        /// <summary>The title splash is the main menu's backdrop (playtest 2026-10-26): the menu drops below
        /// the painted title, without its own title, help lines or dimming. Off: the menu as it always was.</summary>
        public void SplashBackdrop(bool on)
        {
            if (_mainPanel == null) return;
            foreach (var go in _mainTitleBits) go.SetActive(!on);
            var low = new Vector2(0.5f, 0f);
            _mainPanel.anchorMin = _mainPanel.anchorMax = _mainPanel.pivot = on ? low : new Vector2(0.5f, 0.5f);
            _mainPanel.anchoredPosition = on ? new Vector2(0, 48) : Vector2.zero;
            _screens["main"].GetComponent<Image>().color = new Color(0.03f, 0.02f, 0.01f, on ? 0f : 0.55f);
        }

        void BuildMenus()
        {
            var m = Screen("main", 560);
            _mainPanel = m;
            _mainTitleBits.Add(Title(m, "WORKSHOP\nWRECKAGE", 60).gameObject);
            _mainTitleBits.Add(Para(m, "EST. 1952   ·   SALVAGE & ROBOTICS   ·   WORKSHOP No. 7", 13, Brass, 20).gameObject);
            _mainTitleBits.Add(Para(m, "Restore order. Rebuild the robots.", 18, Muted, 28).gameObject);
            // New Game first, then Continue and Load (playtest 2026-10-29); world size and seed are on the New Game screen
            MakeButton(m, "New Game", () => ShowSub("newgame"));
            _continue = MakeButton(m, "Continue", () => _g.LoadGame(LatestSlot()?.slot));
            MakeButton(m, "Load Game", () => ShowSub("load"));
            MakeButton(m, "Settings", () => ShowSub("settings"));
            MakeButton(m, "Quit", Application.Quit);
            _mainTitleBits.Add(Para(m, "WASD move · Q/E turn · Shift sprint · Ctrl crouch · Left mouse pick up / store · G store all\nF inspect · Z drop · Wheel select · 1-9 skills (glove, scan, supermagnet, sweep, sense, sort, delivery) · K skills · U robots · R/T/J robots · H request pickup · Tab log · F5/F9 save/load", 14, Muted, 40).gameObject);

            var ng = Screen("newgame", 560);
            Title(ng, "NEW GAME", 34);
            // world size: cycles through shared/data/world_presets.json
            var presets = ItemDatabase.Presets();
            if (presets.Count == 0) presets.Add((_g.presetId, _g.db.config));
            int pi = Mathf.Max(0, presets.FindIndex(x => x.id == _g.presetId));
            string PresetText() => $"World size:  {Json.Str(presets[pi].cfg, "label", presets[pi].id)}  ({Json.Int(presets[pi].cfg, "total_items"):N0} parts)";
            Button presetBtn = null;
            presetBtn = MakeButton(ng, PresetText(), () =>
            {
                if (presets.Count == 0) return;
                pi = (pi + 1) % presets.Count;
                presetBtn.GetComponentInChildren<Text>().text = PresetText();
            });
            var seedRow = new GameObject("Seed", typeof(RectTransform), typeof(HorizontalLayoutGroup), typeof(LayoutElement));
            seedRow.transform.SetParent(ng, false);
            seedRow.GetComponent<LayoutElement>().preferredHeight = 40;
            Para(seedRow.transform, "Workshop seed", 18, Cream, 40).alignment = TextAnchor.MiddleLeft;
            var inGo = DefaultControls.CreateInputField(new DefaultControls.Resources());
            inGo.transform.SetParent(seedRow.transform, false);
            _seed = inGo.GetComponent<InputField>();
            _seed.text = _g.NewSeed().ToString();
            _seed.textComponent.font = TextAtlas.Font;
            Para(ng, "<i>Every seed generates a new workshop. Saves remember their world size and seed.</i>", 13, Muted, 20);
            MakeButton(ng, "Start", () =>
            {
                if (!_g.NewGame(presets[pi].id, int.TryParse(_seed.text, out var s) ? s : _g.NewSeed())) return;
                _seed.text = _g.NewSeed().ToString();   // the next new game: a fresh seed (Game.FixedSeed off)
                _g.SetState("play");
            });
            MakeButton(ng, "Back", BackOut);

            var p = Screen("pause", 460);
            Title(p, "PAUSED");
            MakeButton(p, "Resume", () => _g.SetState("play"));
            MakeButton(p, "Skills", () => ShowSub("skills"));
            MakeButton(p, "Robots", () => ShowSub("robots"));
            MakeButton(p, "Save Game", () => ShowSub("save"));
            MakeButton(p, "Load Game", () => ShowSub("load"));
            MakeButton(p, "Settings", () => ShowSub("settings"));
            {
                // safety net: parts that ended up where nobody can reach them go to the sorting tray
                float armed = -99;
                Button rb = null;
                rb = MakeButton(p, "Recover unreachable parts", () =>
                {
                    var bad = _g.UnreachableParts();
                    var label = rb.GetComponentInChildren<Text>();
                    if (bad.Count == 0) { OnFeedback("Every unsorted part can be reached: nothing to recover", true); return; }
                    if (Time.unscaledTime - armed > 6) { armed = Time.unscaledTime; label.text = $"Move {bad.Count} part{(bad.Count == 1 ? "" : "s")} to the sorting tray? Click again"; return; }
                    int n = _g.RecoverUnreachable();
                    label.text = "Recover unreachable parts";
                    armed = -99;
                    OnFeedback($"{n} part{(n == 1 ? "" : "s")} moved to the sorting tray", true);
                });
            }
            MakeButton(p, "Main Menu", () => { _g.Autosave("main menu"); _g.SetState("menu"); });
            MakeButton(p, "Quit", Application.Quit);   // Game.OnApplicationQuit autosaves

            BuildSaveScreens();

            BuildSkillScreen();   // HudSkills.cs

            BuildRobotScreens();

            var ex = Screen("exit", 620);
            Title(ex, "LEAVE THE WORKSHOP?", 34);
            Para(ex, "Stepping out through the door ends the game.\nSaving keeps your workshop in your current save (or the quicksave).", 17, Muted, 56);
            MakeButton(ex, "Save and leave", () => _g.LeaveWorkshop(true));
            MakeButton(ex, "Leave without saving", () => _g.LeaveWorkshop(false));
            MakeButton(ex, "Stay in the workshop", () => _g.SetState("play"));

            BuildSettings();

            var inv = Screen("inventory", 1100);
            Title(inv, "WORKSHOP LOG", 32);
            var body = Para(inv, "", 17, Cream, 600);
            body.alignment = TextAnchor.UpperLeft;
            body.resizeTextForBestFit = true;   // the endgame audit can be long: shrink rather than spill
            body.resizeTextMinSize = 11;
            body.resizeTextMaxSize = 17;
            _logText = body;
            Para(inv, "[Tab] close", 16, Muted, 24);

            var stGo = At("Status", _menus, new Vector2(0.5f, 1), new Vector2(-500, -60), new Vector2(1000, 30));
            _status = Fill(stGo, "t", 18, Brass, TextAnchor.MiddleCenter);
        }

        static void Stack(RectTransform r)
        {
            var v = r.gameObject.AddComponent<VerticalLayoutGroup>();
            v.spacing = 8; v.childControlHeight = true; v.childControlWidth = true; v.childForceExpandHeight = false;
            r.gameObject.AddComponent<LayoutElement>();
            var f = r.gameObject.AddComponent<ContentSizeFitter>();
            f.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
        }

        /// <summary>Returns a setter that moves the slider without calling back (refreshing after a preset).</summary>
        System.Action<float> SliderRow(Transform parent, string label, float lo, float hi, float value, System.Action<float> cb, System.Func<float, string> format = null)
        {
            var row = new GameObject(label, typeof(RectTransform), typeof(HorizontalLayoutGroup), typeof(LayoutElement));
            row.transform.SetParent(parent, false);
            row.GetComponent<LayoutElement>().preferredHeight = 32;
            row.GetComponent<LayoutElement>().flexibleHeight = 0;   // a short page must not stretch the rows
            var rh = row.GetComponent<HorizontalLayoutGroup>();
            rh.spacing = 12;
            rh.childControlWidth = rh.childControlHeight = true;
            rh.childForceExpandWidth = false; rh.childForceExpandHeight = true;
            var lab = Para(row.transform, label, 18, Cream, 32);
            lab.alignment = TextAnchor.MiddleLeft;
            lab.GetComponent<LayoutElement>().preferredWidth = 190;
            lab.GetComponent<LayoutElement>().flexibleWidth = 0;
            var s = DefaultControls.CreateSlider(new DefaultControls.Resources());
            s.transform.SetParent(row.transform, false);
            s.AddComponent<LayoutElement>().flexibleWidth = 1;
            var sl = s.GetComponent<Slider>();
            // the default controls have no sprites: every piece is the same white box, so the value can't be
            // seen (playtest 2026-10-26). A dark track, an amber fill, a light handle and the value in words.
            s.transform.Find("Background").GetComponent<Image>().color = ControlTrack;
            s.transform.Find("Fill Area/Fill").GetComponent<Image>().color = UiStyle.Amber;
            var handle = s.transform.Find("Handle Slide Area/Handle").GetComponent<Image>();
            handle.color = UiStyle.Ink;
            sl.targetGraphic = handle;
            sl.colors = ControlColors();
            var val = Para(row.transform, "", 18, UiStyle.Amber, 32);
            val.alignment = TextAnchor.MiddleRight;
            val.GetComponent<LayoutElement>().preferredWidth = 70;
            val.GetComponent<LayoutElement>().flexibleWidth = 0;
            System.Action<float> show = v => val.text = format != null ? format(v) : lo == 0f && hi == 1f ? $"{Mathf.RoundToInt(v * 100)} %" : $"{v:0.##}";
            sl.minValue = lo; sl.maxValue = hi; sl.value = value;
            show(sl.value);
            sl.onValueChanged.AddListener(v => { show(v); cb(v); });
            return v => { sl.SetValueWithoutNotify(v); show(v); };
        }

        static readonly Color ControlTrack = new(0.04f, 0.1f, 0.17f, 1f);

        /// <summary>Hover and press tints that read on blueprint paper.</summary>
        static ColorBlock ControlColors()
        {
            var c = ColorBlock.defaultColorBlock;
            c.normalColor = Color.white;
            c.highlightedColor = new Color(1f, 0.9f, 0.7f);
            c.selectedColor = new Color(1f, 0.9f, 0.7f);
            c.pressedColor = new Color(0.85f, 0.75f, 0.55f);
            return c;
        }

        /// <summary>Returns a setter that flips the toggle without calling back.</summary>
        System.Action<bool> ToggleRow(Transform parent, string label, bool value, System.Action<bool> cb)
        {
            var t = DefaultControls.CreateToggle(new DefaultControls.Resources());
            t.transform.SetParent(parent, false);
            var tle = t.AddComponent<LayoutElement>();
            tle.preferredHeight = 32; tle.flexibleHeight = 0;
            var tg = t.GetComponent<Toggle>();
            // a dark box with an amber check, and ON / OFF in the label (the sprite-less default is a white
            // check on a white box: on and off looked the same)
            var box = t.transform.Find("Background").GetComponent<Image>();
            box.color = ControlTrack;
            box.rectTransform.sizeDelta = new Vector2(22, 22);
            var check = t.transform.Find("Background/Checkmark").GetComponent<Image>();
            check.color = UiStyle.Amber;
            check.rectTransform.anchorMin = Vector2.zero; check.rectTransform.anchorMax = Vector2.one;
            check.rectTransform.offsetMin = new Vector2(4, 4); check.rectTransform.offsetMax = new Vector2(-4, -4);
            tg.colors = ControlColors();
            var txt = t.GetComponentInChildren<Text>();
            txt.font = TextAtlas.Font; txt.fontSize = 18; txt.color = UiStyle.Ink; txt.supportRichText = true;
            txt.rectTransform.offsetMin = new Vector2(32, txt.rectTransform.offsetMin.y);
            System.Action<bool> show = on => txt.text = $"{label}   <b><color=#{(on ? "FFBD61" : "8FA3B8")}>{(on ? "ON" : "OFF")}</color></b>";
            tg.isOn = value;
            show(value);
            tg.onValueChanged.AddListener(v => { show(v); cb(v); });
            return v => { tg.SetIsOnWithoutNotify(v); show(v); };
        }

        // ------------------------------------------------------------------ settings

        readonly List<System.Action> _settingsRefresh = new();
        System.Action<string> _settingsTab;
        (int w, int h)? _resPicked;

        static readonly FullScreenMode[] DisplayModes = { FullScreenMode.FullScreenWindow, FullScreenMode.ExclusiveFullScreen, FullScreenMode.Windowed };
        static readonly string[] DisplayModeNames = { "Borderless fullscreen", "Exclusive fullscreen", "Windowed" };

        /// <summary>Settings in three tabs (playtest 2026-10-26: "a proper graphics and audio settings"):
        /// Graphics (a quality preset over display and image), Audio (four volumes) and Controls. Every value
        /// applies at once and is saved (GameSettings, Sfx).</summary>
        void BuildSettings()
        {
            var st = Screen("settings", 1120);
            Title(st, "SETTINGS");
            var tabs = HRow(st, 44, 12);
            var pages = new Dictionary<string, RectTransform>();
            var tabButtons = new Dictionary<string, Button>();
            _settingsTab = name =>
            {
                foreach (var kv in pages) kv.Value.gameObject.SetActive(kv.Key == name);
                foreach (var kv in tabButtons) kv.Value.GetComponentInChildren<Text>().text = kv.Key == name ? $"[ {kv.Key.ToUpperInvariant()} ]" : kv.Key;
            };
            foreach (var n in new[] { "Graphics", "Audio", "Controls" }) tabButtons[n] = MakeButton(tabs, n, () => _settingsTab(n));

            // graphics: display on the left, image on the right
            var (gp, gl, gr) = Columns(st, "Graphics");
            pages["Graphics"] = gp;
            Para(gl, "DISPLAY", 18, Brass, 26);
            ChoiceRow(gl, "Quality preset", () => GameSettings.PresetNames[GameSettings.Preset],
                d => GameSettings.SetPreset(_g, (GameSettings.Preset + d + GameSettings.Custom) % GameSettings.Custom));   // Custom only by changing a value
            ChoiceRow(gl, "Display mode", () => DisplayModeNames[DisplayModeIndex()], d =>
            {
                UnityEngine.Screen.fullScreenMode = DisplayModes[(DisplayModeIndex() + d + 3) % 3];
            });
            ChoiceRow(gl, "Resolution", () => { var r = CurrentRes(); return $"{r.w} x {r.h}"; }, d =>
            {
                var list = Resolutions();
                if (list.Count == 0) return;
                var cur = CurrentRes();
                int i = list.FindIndex(r => r.w == cur.w && r.h == cur.h);
                if (i < 0) i = list.FindLastIndex(r => r.w * r.h <= cur.w * cur.h);
                i = Mathf.Clamp(i + d, 0, list.Count - 1);
                _resPicked = list[i];
                UnityEngine.Screen.SetResolution(list[i].w, list[i].h, UnityEngine.Screen.fullScreenMode);
            });
            Reg(ToggleRow(gl, "V-Sync", GameSettings.VSync, on => { GameSettings.VSync = on; GameSettings.Changed(_g); RefreshSettings(); }), () => GameSettings.VSync);
            ChoiceRow(gl, "Frame rate limit", () => GameSettings.VSync ? "V-Sync" : GameSettings.FpsCaps[GameSettings.FpsCap] < 0 ? "Unlimited" : $"{GameSettings.FpsCaps[GameSettings.FpsCap]} fps", d =>
            {
                GameSettings.FpsCap = (GameSettings.FpsCap + d + GameSettings.FpsCaps.Length) % GameSettings.FpsCaps.Length;
                GameSettings.Changed(_g);
            });
            Reg(SliderRow(gl, "Render scale", 0.5f, 1f, GameSettings.RenderScale, v =>
            {
                GameSettings.RenderScale = Mathf.Round(v * 20) / 20f;
                GameSettings.Changed(_g);
                RefreshPreset();
            }, v => $"{Mathf.RoundToInt(Mathf.Round(v * 20) * 5)} %"), () => GameSettings.RenderScale);
            ChoiceRow(gl, "Anti-aliasing", () => GameSettings.AaNames[GameSettings.Aa], d =>
            {
                GameSettings.Aa = (GameSettings.Aa + d + GameSettings.AaNames.Length) % GameSettings.AaNames.Length;
                GameSettings.Changed(_g);
            });
            Para(gr, "IMAGE", 18, Brass, 26);
            ChoiceRow(gr, "Shadows", () => GameSettings.ShadowNames[GameSettings.Shadows], d =>
            {
                GameSettings.Shadows = (GameSettings.Shadows + d + GameSettings.ShadowNames.Length) % GameSettings.ShadowNames.Length;
                GameSettings.Changed(_g);
            });
            // part shadows: the main GPU cost on Massive; "off" for slower PCs
            ChoiceRow(gr, "Part shadows", () => ItemRenderer.ShadowModeNames[GameSettings.PartShadows], d =>
            {
                GameSettings.PartShadows = (GameSettings.PartShadows + d + 3) % 3;
                GameSettings.Changed(_g);
            });
            Reg(SliderRow(gr, "Brightness", -1f, 1f, GameSettings.Brightness, v => { GameSettings.Brightness = v; GameSettings.Changed(_g); },
                v => Mathf.Abs(v) < 0.005f ? "0" : $"{v:+0.00;-0.00}"), () => GameSettings.Brightness);
            Reg(ToggleRow(gr, "Bloom (glowing eyes and lamps)", GameSettings.Bloom, on => { GameSettings.Bloom = on; GameSettings.Changed(_g); }), () => GameSettings.Bloom);
            ChoiceRow(gr, "Film grain", () => GameSettings.GrainNames[GameSettings.Grain], d =>
            {
                GameSettings.Grain = (GameSettings.Grain + d + GameSettings.GrainNames.Length) % GameSettings.GrainNames.Length;
                GameSettings.Changed(_g);
            });
            Reg(ToggleRow(gr, "Vignette", GameSettings.Vignette, on => { GameSettings.Vignette = on; GameSettings.Changed(_g); }), () => GameSettings.Vignette);
            Reg(ToggleRow(gr, "Light shafts and dust", GameSettings.Shafts, on => { GameSettings.Shafts = on; GameSettings.Changed(_g); RefreshPreset(); }), () => GameSettings.Shafts);

            // audio
            var (ap, al, ar) = Columns(st, "Audio");
            pages["Audio"] = ap;
            Para(al, "VOLUME", 18, Brass, 26);
            Reg(SliderRow(al, "Master", 0, 1, Sfx.MasterSetting, Sfx.SetMaster), () => Sfx.MasterSetting);
            Reg(SliderRow(al, "Workshop", 0, 1, Sfx.SfxSetting, Sfx.SetSfx), () => Sfx.SfxSetting);
            Reg(SliderRow(al, "Robots", 0, 1, Sfx.RobotSetting, Sfx.SetRobots), () => Sfx.RobotSetting);
            Reg(SliderRow(al, "Interface", 0, 1, Sfx.UiSetting, Sfx.SetUi), () => Sfx.UiSetting);
            Para(ar, "WHAT YOU HEAR", 18, Brass, 26);
            var what = Para(ar, "<b>Master</b>  everything at once\n\n<b>Workshop</b>  parts, drawers, footsteps, the lathe, the magnets\n\n" +
                "<b>Robots</b>  motors, rotors, tracks, steps, servos, boot-ups\n\n<b>Interface</b>  clicks, sorted-box chimes, finds, saves, the reject buzz", 17, Muted, 190);
            what.alignment = TextAnchor.UpperLeft;

            // controls
            var (cp, cl, cr) = Columns(st, "Controls");
            pages["Controls"] = cp;
            Para(cl, "CAMERA", 18, Brass, 26);
            Reg(SliderRow(cl, "Mouse sensitivity", 0.05f, 0.6f, GameSettings.Sensitivity, v => { GameSettings.Sensitivity = v; GameSettings.ControlsChanged(_g); }), () => GameSettings.Sensitivity);
            Reg(SliderRow(cl, "Field of view", 60, 100, GameSettings.Fov, v => { GameSettings.Fov = Mathf.Round(v); GameSettings.ControlsChanged(_g); }, v => $"{Mathf.RoundToInt(v)}"), () => GameSettings.Fov);
            Reg(ToggleRow(cl, "Invert mouse Y", GameSettings.InvertY, on => { GameSettings.InvertY = on; GameSettings.ControlsChanged(_g); }), () => GameSettings.InvertY);
            Para(cr, "HELP", 18, Brass, 26);
            // (the "Robot part afterglow" setting is gone with the Part Afterglow skill: skill revamp 2026-10-30)
            Reg(ToggleRow(cr, "Show performance stats  [F3]", false, on => _debug.transform.parent.gameObject.SetActive(on)), () => _debug.transform.parent.gameObject.activeSelf);

            var bottom = HRow(st, 44, 12);
            MakeButton(bottom, "Reset to defaults", () =>
            {
                GameSettings.ResetDefaults(_g);
                Sfx.SetMaster(1f); Sfx.SetSfx(0.8f); Sfx.SetRobots(0.8f); Sfx.SetUi(0.8f);
                Interaction.Afterglow = true;
                PlayerPrefs.SetInt("ww_part_afterglow", 1);
                PlayerPrefs.Save();
                RefreshSettings();
            });
            MakeButton(bottom, "Back", BackOut);
            _settingsTab("Graphics");
        }

        /// <summary>QA: the settings sheet on one tab (--menusub=settings:Audio).</summary>
        public void SettingsTab(string t) => _settingsTab?.Invoke(t);

        void RefreshSettings()
        {
            if (_resPicked.HasValue && _resPicked.Value.w == UnityEngine.Screen.width && _resPicked.Value.h == UnityEngine.Screen.height) _resPicked = null;
            foreach (var r in _settingsRefresh) r();
        }

        /// <summary>A slider or toggle that can make the preset Custom: the rows show it (sliders and toggles
        /// don't refresh themselves while dragged or clicked).</summary>
        void RefreshPreset() => RefreshSettings();

        void Reg(System.Action<float> set, System.Func<float> get) => _settingsRefresh.Add(() => set(get()));
        void Reg(System.Action<bool> set, System.Func<bool> get) => _settingsRefresh.Add(() => set(get()));

        static int DisplayModeIndex()
        {
            var m = UnityEngine.Screen.fullScreenMode;
            return m == FullScreenMode.ExclusiveFullScreen ? 1 : m == FullScreenMode.FullScreenWindow ? 0 : 2;
        }

        /// <summary>The window size (the size just picked until the window has it).</summary>
        (int w, int h) CurrentRes() => _resPicked ?? (UnityEngine.Screen.width, UnityEngine.Screen.height);

        static List<(int w, int h)> Resolutions() =>
            UnityEngine.Screen.resolutions.Select(r => (r.width, r.height)).Where(r => r.width >= 1024).Distinct().OrderBy(r => r.width * r.height).ToList();

        RectTransform HRow(Transform parent, float height, float spacing)
        {
            var row = new GameObject("Row", typeof(RectTransform), typeof(HorizontalLayoutGroup), typeof(LayoutElement));
            row.transform.SetParent(parent, false);
            row.GetComponent<LayoutElement>().preferredHeight = height;
            var h = row.GetComponent<HorizontalLayoutGroup>();
            h.spacing = spacing;
            h.childControlWidth = h.childControlHeight = true;
            h.childForceExpandWidth = h.childForceExpandHeight = true;
            return (RectTransform)row.transform;
        }

        /// <summary>A settings page: two columns of rows, tall enough that the sheet keeps its size between tabs.</summary>
        (RectTransform page, RectTransform left, RectTransform right) Columns(Transform parent, string name)
        {
            var page = new GameObject(name, typeof(RectTransform), typeof(HorizontalLayoutGroup), typeof(LayoutElement));
            page.transform.SetParent(parent, false);
            page.GetComponent<LayoutElement>().minHeight = 8 * 40;
            var h = page.GetComponent<HorizontalLayoutGroup>();
            h.spacing = 44;
            h.childControlWidth = h.childControlHeight = true;
            h.childForceExpandWidth = true; h.childForceExpandHeight = false;
            RectTransform Col(string n)
            {
                var go = new GameObject(n, typeof(RectTransform), typeof(VerticalLayoutGroup), typeof(LayoutElement));
                go.transform.SetParent(page.transform, false);
                var v = go.GetComponent<VerticalLayoutGroup>();
                v.spacing = 8;
                v.childControlWidth = v.childControlHeight = true;
                v.childForceExpandWidth = true; v.childForceExpandHeight = false;
                go.GetComponent<LayoutElement>().flexibleWidth = 1;
                return (RectTransform)go.transform;
            }
            return ((RectTransform)page.transform, Col("Left"), Col("Right"));
        }

        /// <summary>A setting with a few named values: label, &lt; value &gt;.</summary>
        void ChoiceRow(Transform parent, string label, System.Func<string> value, System.Action<int> step)
        {
            var row = new GameObject(label, typeof(RectTransform), typeof(HorizontalLayoutGroup), typeof(LayoutElement));
            row.transform.SetParent(parent, false);
            row.GetComponent<LayoutElement>().preferredHeight = 32;
            row.GetComponent<LayoutElement>().flexibleHeight = 0;
            var h = row.GetComponent<HorizontalLayoutGroup>();
            h.spacing = 6;
            h.childControlWidth = h.childControlHeight = true;
            h.childForceExpandWidth = false; h.childForceExpandHeight = true;
            var lab = Para(row.transform, label, 18, Cream, 32);
            lab.alignment = TextAnchor.MiddleLeft;
            lab.GetComponent<LayoutElement>().flexibleWidth = 1;
            void Arrow(string a, int d)
            {
                var b = MakeButton(row.transform, a, () => { step(d); RefreshSettings(); });
                var le = b.GetComponent<LayoutElement>();
                le.preferredWidth = 36; le.preferredHeight = 32;
            }
            Arrow("<", -1);
            var val = Para(row.transform, "", 18, UiStyle.Amber, 32);
            val.GetComponent<LayoutElement>().preferredWidth = 230;
            Arrow(">", 1);
            _settingsRefresh.Add(() => val.text = value());
            val.text = value();
        }

        // ------------------------------------------------------------------ screens

        public void SetScreen(string state)
        {
            _hud.gameObject.SetActive(state != "menu");
            ShowScreenRaw(state == "menu" ? "main" : state == "paused" ? "pause" : state == "inventory" ? "inventory" : state == "workbench" ? "workbench" : state == "exit" ? "exit" : "");
        }

        public void SetHudVisible(bool v) => _hud.gameObject.SetActive(v);

        /// <summary>QA: opens a sub-screen (settings, save, ...) as its menu button does.</summary>
        public void OpenSub(string name) => ShowSub(name);

        void ShowSub(string name)
        {
            if (_screen == "main" || _screen == "pause") _backTo = _screen;
            _direct = null;
            ShowScreenRaw(name);
        }

        string _direct;
        /// <summary>The screen opened straight from play with its own key (K skills, U robots), or null.</summary>
        public string DirectScreen => _direct != null && _screen == _direct ? _direct : null;
        public void OpenDirect(string name) { _backTo = "pause"; ShowScreenRaw(name); _direct = name; }

        /// <summary>What every Back button does. A screen opened straight from play with its own key
        /// returns to the game: the pause menu it would otherwise fall back to was never on screen,
        /// so landing there is a detour. Escape already does this (Game reads DirectScreen).</summary>
        void BackOut()
        {
            if (DirectScreen != null) _g.SetState("play");
            else ShowScreenRaw(_backTo);
        }

        void ShowScreenRaw(string name)
        {
            _screen = name;
            foreach (var kv in _screens) kv.Value.gameObject.SetActive(kv.Key == name);
            _status.text = "";
            if (name == "save" || name == "load") RefreshSlots();
            if (name == "skills") OpenSkillPanel();
            if (name == "robots") RefreshRobots();
            if (name == "workbench") RefreshWorkbench();
            else if (name == "modify") RefreshModify();
            else if (_previewCam != null) PreviewCamera(false);
            if (name == "inventory") RefreshLog();
            if (name == "settings") RefreshSettings();
            if (name == "main")
            {
                var latest = LatestSlot();
                _continue.interactable = latest != null;
                _continue.GetComponentInChildren<Text>().text = latest != null ? $"Continue  —  {latest.name}" : "Continue";
            }
        }

        public bool BackFromSubScreen()
        {
            if (_screen == "save" || _screen == "load" || _screen == "settings" || _screen == "skills" || _screen == "robots" || _screen == "newgame") { BackOut(); return true; }
            if (_screen == "modify") { ShowScreenRaw("workbench"); return true; }
            return false;
        }

        public void SetStatus(string t) => _status.text = t;
        public void OpenScreen(string name) => ShowSub(name);

        // ------------------------------------------------------------------ save / load screens

        /// <summary>Save and Load (playtest 2026-10-28: "a surefire load and save, the ability to name your saves,
        /// and also to delete old saves"). Save: over this game's own save in one click, or as a new save under a
        /// typed name; every save listed with Overwrite and Delete. Load: every save, newest first, with Load and
        /// Delete. Overwriting another save and deleting ask once more on the button itself (no pop-up).</summary>
        void BuildSaveScreens()
        {
            var s = Screen("save", 980);
            Title(s, "SAVE GAME");
            _saveHeader = Para(s, "", 16, Muted, 24);
            _saveOver = MakeButton(s, "Save", () => { if (_g.currentSave != null && _g.SaveGame(_g.currentSave)) RefreshSlots(); });
            var row = HRow(s, 46);
            var lab = Para(row, "New save:", 18, Cream, 46);
            lab.alignment = TextAnchor.MiddleLeft;
            lab.GetComponent<LayoutElement>().preferredWidth = 120;
            var inGo = DefaultControls.CreateInputField(new DefaultControls.Resources());
            inGo.transform.SetParent(row, false);
            inGo.AddComponent<LayoutElement>().flexibleWidth = 1;
            _saveName = inGo.GetComponent<InputField>();
            _saveName.characterLimit = 48;
            _saveName.textComponent.font = TextAtlas.Font;
            _saveName.textComponent.fontSize = 20;
            var ph = (Text)_saveName.placeholder;
            ph.font = TextAtlas.Font; ph.fontSize = 20; ph.text = "Name this save…";
            _saveNew = Armed(row, "Save as new", SaveAsNew, () => SameNamed() is SaveSystem.SaveInfo same ? $"Overwrite '{same.name}'? Click again" : null);
            Fixed(_saveNew, 230);
            Para(s, "Your saves", 16, Muted, 22);
            _saveList = ScrollList(s, 380);
            MakeButton(s, "Back", BackOut);

            var l = Screen("load", 980);
            Title(l, "LOAD GAME");
            _loadHeader = Para(l, "", 16, Muted, 24);
            _loadList = ScrollList(l, 470);
            Para(l, "Deleted saves are kept in the saves\\deleted folder. Your game is autosaved every 5 minutes, before loading and on quitting.", 13, Muted, 20);
            var lrow = HRow(l, 44);
            MakeButton(lrow, "Open saves folder", () => { System.IO.Directory.CreateDirectory(SaveSystem.Dir); Application.OpenURL("file:///" + SaveSystem.Dir.Replace('\\', '/')); }).GetComponent<LayoutElement>().flexibleWidth = 1;
            MakeButton(lrow, "Back", BackOut).GetComponent<LayoutElement>().flexibleWidth = 1;
        }

        RectTransform HRow(Transform parent, float height)
        {
            var row = new GameObject("Row", typeof(RectTransform), typeof(HorizontalLayoutGroup), typeof(LayoutElement));
            row.transform.SetParent(parent, false);
            row.GetComponent<LayoutElement>().preferredHeight = height;
            var h = row.GetComponent<HorizontalLayoutGroup>();
            h.spacing = 10; h.childControlWidth = h.childControlHeight = true; h.childForceExpandWidth = false; h.childForceExpandHeight = true;
            return (RectTransform)row.transform;
        }

        static void Fixed(Button b, float width)
        {
            var le = b.GetComponent<LayoutElement>();
            le.preferredWidth = width; le.flexibleWidth = 0;
        }

        /// <summary>A list that scrolls (mouse wheel or drag) inside a box of the given height.</summary>
        RectTransform ScrollList(Transform parent, float height)
        {
            var sv = new GameObject("Scroll", typeof(RectTransform), typeof(Image), typeof(ScrollRect), typeof(LayoutElement));
            sv.transform.SetParent(parent, false);
            sv.GetComponent<LayoutElement>().preferredHeight = height;
            sv.GetComponent<Image>().color = new Color(0, 0, 0, 0.14f);
            var vp = Rect("Viewport", sv.transform, Vector2.zero, Vector2.one, new Vector2(6, 6), new Vector2(-6, -6));
            vp.gameObject.AddComponent<RectMask2D>();
            var content = Rect("Content", vp, new Vector2(0, 1), Vector2.one, Vector2.zero, Vector2.zero);
            content.pivot = new Vector2(0.5f, 1);
            Stack(content);
            var sr = sv.GetComponent<ScrollRect>();
            sr.viewport = vp; sr.content = content; sr.horizontal = false;
            sr.movementType = ScrollRect.MovementType.Clamped; sr.scrollSensitivity = 40;
            return content;
        }

        /// <summary>A button that, while `confirm` returns a question, asks it on its own label first and acts on
        /// the second click within 5 s (the label goes back after that).</summary>
        Button Armed(Transform parent, string text, System.Action act, System.Func<string> confirm)
        {
            float armed = -99;
            Button b = null;
            b = MakeButton(parent, text, () =>
            {
                var label = b.GetComponentInChildren<Text>();
                var q = confirm();
                if (q != null && Time.unscaledTime - armed > 5)
                {
                    armed = Time.unscaledTime;
                    label.text = q;
                    StartCoroutine(Unarm(label, text, armed, () => armed));
                    return;
                }
                armed = -99;
                label.text = text;
                act();
            });
            return b;
        }

        System.Collections.IEnumerator Unarm(Text label, string text, float at, System.Func<float> armedNow)
        {
            yield return new WaitForSecondsRealtime(5);
            if (label != null && armedNow() == at) label.text = text;
        }

        SaveSystem.SaveInfo SameNamed()
        {
            var n = _saveName.text.Trim();
            return n == "" ? null : SaveSystem.List(_g.Automated).FirstOrDefault(x => x.Ok && string.Equals(x.name, n, System.StringComparison.OrdinalIgnoreCase));
        }

        void SaveAsNew()
        {
            var n = _saveName.text.Trim();
            if (n == "") n = "Workshop " + System.DateTime.Now.ToString("d MMM HH:mm", System.Globalization.CultureInfo.InvariantCulture);
            var same = SameNamed();
            if (_g.SaveGame(same?.slot ?? SaveSystem.NewSlotId(_g.Automated), n)) { _saveName.text = ""; RefreshSlots(); }
        }

        void RefreshSlots()
        {
            var saves = SaveSystem.List(_g.Automated);
            bool inGame = _g.world != null && _g.progress != null && _backTo == "pause";
            var cur = _g.currentSave;
            _saveHeader.text = cur != null
                ? $"This game: <b>{_g.currentSaveName}</b>{(_g.Unsaved ? $"  ·  {Mins(_g.UnsavedSeconds)} of play not saved yet" : "  ·  saved")}"
                : "This game has not been saved under a name yet: name it below.";
            _saveOver.gameObject.SetActive(cur != null);
            if (cur != null) _saveOver.GetComponentInChildren<Text>().text = $"Save  —  update '{_g.currentSaveName}'";
            _loadHeader.text = saves.Count == 0 ? "No saves yet." : $"{saves.Count} save{(saves.Count == 1 ? "" : "s")}, newest first" + (inGame && _g.Unsaved ? "  ·  loading keeps this game's unsaved play in Autosave" : "");
            foreach (var (list, saving) in new[] { (_saveList, true), (_loadList, false) })
            {
                foreach (Transform c in list) Destroy(c.gameObject);
                foreach (var sv in saves)
                {
                    if (saving && sv.slot == _g.AutosaveSlot) continue;   // the autosave is the game's to write
                    SaveRow(list, sv, saving, sv.slot == cur && inGame);
                }
                if (saves.Count == 0) Para(list, saving ? "No saves yet." : "No saves yet: start a New Game, then Save from the pause menu (Esc).", 16, Muted, 30);
            }
        }

        static string Mins(double s) => s < 90 ? $"{s:0} s" : $"{s / 60:0} min";

        void SaveRow(RectTransform list, SaveSystem.SaveInfo sv, bool saving, bool current)
        {
            var row = HRow(list, 60);
            row.name = "Save " + sv.slot;
            row.gameObject.AddComponent<Image>().color = current ? new Color(1f, 0.85f, 0.5f, 0.16f) : new Color(1, 1, 1, 0.05f);
            row.GetComponent<HorizontalLayoutGroup>().padding = new RectOffset(12, 6, 4, 4);
            var t = Para(row, $"<b>{sv.name}</b>{(current ? "   <i>(this game)</i>" : "")}\n<size=13>{SaveSystem.Describe(sv)}</size>", 19, Cream, 52);
            t.alignment = TextAnchor.MiddleLeft;
            t.GetComponent<LayoutElement>().flexibleWidth = 1;
            Button main;
            if (saving)
                main = Armed(row, "Overwrite", () => { if (_g.SaveGame(sv.slot, sv.name)) RefreshSlots(); }, () => current ? null : "Confirm?");
            else
            {
                main = MakeButton(row, "Load", () => _g.LoadGame(sv.slot));
                main.interactable = sv.Ok || sv.fromBackup;
            }
            Fixed(main, saving ? 140 : 110);
            var del = Armed(row, "Delete", () =>
            {
                if (!SaveSystem.Delete(sv.slot)) { SetStatus($"'{sv.name}' could not be deleted: {SaveSystem.LastError}"); return; }
                if (sv.slot == _g.currentSave) _g.currentSave = _g.currentSaveName = null;
                SetStatus($"Deleted '{sv.name}'");
                RefreshSlots();
            }, () => "Confirm?");
            Fixed(del, 120);
        }

        /// <summary>QA: types a name into the save screen's name field.</summary>
        public void QaSaveName(string n) => _saveName.text = n;

        /// <summary>QA: clicks the button whose label starts with `label` on the open screen (in the row of save
        /// `slot`, if given), as a mouse click would. False if there is none.</summary>
        public bool QaClick(string label, string slot = null)
        {
            if (!_screens.TryGetValue(_screen ?? "", out var scr)) return false;
            Transform root = scr;
            if (slot != null) root = scr.GetComponentsInChildren<RectTransform>().FirstOrDefault(r => r.name == "Save " + slot);
            var b = root?.GetComponentsInChildren<Button>().FirstOrDefault(x => x.interactable && x.GetComponentInChildren<Text>().text.StartsWith(label));
            if (b == null) return false;
            b.onClick.Invoke();
            return true;
        }

        /// <summary>QA: every text in save `slot`'s row on the open screen (null: no such row).</summary>
        public string QaRowText(string slot)
        {
            if (!_screens.TryGetValue(_screen ?? "", out var scr)) return null;
            var row = scr.GetComponentsInChildren<RectTransform>().FirstOrDefault(r => r.name == "Save " + slot);
            return row == null ? null : string.Join(" | ", row.GetComponentsInChildren<Text>().Select(x => x.text));
        }

        /// <summary>Set when a save was migrated to the new skill system (Game.MigrateSkills): the skill menu says so.</summary>
        public bool migrationNotice;

        /// <summary>Redraws the skill or robot screen if it is open (after the F8 dev respec).</summary>
        public void RefreshOpenScreen()
        {
            if (_screen == "skills") RefreshSkills();
            else if (_screen == "robots") RefreshRobots();
        }

        /// <summary>A one-line description of a single skill (its data description, else its effects).</summary>
        static string SkillBlurb(SkillDef s)
        {
            if (!string.IsNullOrEmpty(s.description)) return s.description;
            return s.id switch
            {
                "quick_hands" => "faster pickup",
                "inventory_manifest" => "shows how many of each part exist",
                "long_reach" => $"+{s.effects.GetValueOrDefault("reach_m"):0.0} m reach",
                "bulk_pickup" => "picking a part also takes copies around it",
                "scanner_1" => "V / 2: highlights loose copies of the part in hand and its storage place",
                "scanner_2" => "wider scan that also shows what remains",
                "supermagnet" => "X / 3: pulls loose copies of the held part",
                "robot_coordination" => "J: a robot works around a spot you aim at",
                "robot_coordination_2" => "more command slots, faster robots",
                _ => string.Join(", ", s.effects.Select(kv => $"{kv.Key.Replace('_', ' ')} {kv.Value:0.##}")),
            };
        }

        /// <summary>The skills grouped into rank chains: a skill whose only requirement is the previous skill
        /// and which raises one of the same effects is its next level (Magnetic Glove I-X, Part Sense I-X,
        /// Deeper Pockets I-X, Sort Inventory I-X, Automatic Storage Delivery I-X).</summary>
        static List<List<SkillDef>> SkillChains(List<SkillDef> skills)
        {
            SkillDef NextOf(SkillDef a)
            {
                var n = skills.Where(b => b.requires.Count == 1 && b.requires[0] == a.id && b.effects.Keys.Intersect(a.effects.Keys).Any()).ToList();
                return n.Count == 1 ? n[0] : null;
            }
            var ranks = new HashSet<string>(skills.Select(NextOf).Where(n => n != null).Select(n => n.id));
            var chains = new List<List<SkillDef>>();
            foreach (var s in skills.Where(s => !ranks.Contains(s.id)))
            {
                var c = new List<SkillDef> { s };
                for (var n = NextOf(s); n != null && !c.Contains(n); n = NextOf(n)) c.Add(n);
                chains.Add(c);
            }
            return chains;
        }

        /// <summary>A rank's name without its numeral ("Magnetic Glove IV" → "Magnetic Glove").</summary>
        static string RankBase(string name) => System.Text.RegularExpressions.Regex.Replace(name, @"\s+[IVX]+$", "");

        /// <summary>The newest save that loads (Continue), or null.</summary>
        SaveSystem.SaveInfo LatestSlot() => SaveSystem.List(_g.Automated).FirstOrDefault(s => s.Ok || s.fromBackup);

        void RefreshLog()
        {
            var inv = _g.inventory; var p = _g.progress; var db = _g.db;
            var t = $"<b>Carrying {inv.Parts} / {inv.capacity}</b>{(inv.Batteries > 0 ? $"   ·   batteries {inv.Batteries} / {inv.batteryPockets}" : "")}\n";
            foreach (var slot in inv.Stacks())
            {
                var (defId, ids) = slot;
                var def = db.Get(defId);
                t += $"{(inv.IsHeldSlot(slot) ? "▶ " : "   ")}<b>{def.displayName}</b> ×{ids.Count}   <color=#b8a890>{p.CountLabel(defId)}</color>   → {db.BoxLabel(def.storageBox)}\n";
            }
            t += $"\n<b>Workshop restored: {p.OverallPercent:0.0}%</b>  ({p.sortedTotal} of {_g.world.Count} parts sorted) · discovered part types: {p.discovered.Count}\n";
            for (int z = 0; z < _g.layout.zones.Count; z++)
                t += $"{_g.layout.zones[z].name}:  {p.ZonePercent(z):0}%  ·  {p.ZoneSorted(z)} sorted, {p.ZoneRemaining(z)} still to sort\n";
            t += $"\nRobot components found: {p.robotPartsFound} / {db.RobotPartIds().Count}\n";
            if (_g.builder?.pegboard is Pegboard.WorkshopPegboard pb)
                t += $"Tool board: {pb.Hung} / {pb.Total} tools hung  ·  {pb.Missing} missing  ·  {pb.Percent:0}%\n";
            foreach (var pj in _g.workbench.projects.Where(x => !x.crafted))
                t += $"   {pj.name}: {_g.workbench.InstalledCount(pj)} / {_g.workbench.Required(pj)} installed, {_g.workbench.DeliveredIds(pj).Count} in the parts pool  ·  {RobotWorkbench.StatusText(_g.workbench.StatusOf(pj))}\n";
            t += InventoryAudit.Report(_g);
            _logText.text = t;
        }

        // ------------------------------------------------------------------ HUD updates

        public void OnTarget(TargetInfo info)
        {
            _title.text = info.title ?? "";
            _sub.text = info.sub ?? "";
            _hint.text = info.hint ?? "";
            _hint.color = info.tone == 0 ? Brass : info.tone > 0 ? GoodC : BadC;
            _cross.color = info.tone == 0 ? Cream : info.tone > 0 ? GoodC : BadC;
            _cross.rectTransform.sizeDelta = string.IsNullOrEmpty(info.title) ? new Vector2(5, 5) : new Vector2(9, 9);
        }

        public void OnFeedback(string text, bool good)
        {
            Debug.Log($"[feedback] {(good ? "ok" : "no")}: {text}");   // what the player was told (playtest diagnosis)
            _feedback.text = text;
            _feedback.color = good ? GoodC : BadC;
            _feedbackT = 2.2f;
        }

        /// <summary>A line in the message log (top left, no box): newest at the bottom, fading out after 8 s.</summary>
        /// <summary>QA: every message-log line of this session (newest last).</summary>
        public readonly List<string> toastLog = new();

        public void Toast(string text, bool big = false)
        {
            toastLog.Add(text);
            var r = Rect("Line", _toasts, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
            var t = Label(r, "t", 15, big ? Brass : Cream, TextAnchor.UpperLeft, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
            t.supportRichText = true;
            t.text = big ? $"<b>{text}</b>" : text;
            float lines = t.cachedTextGeneratorForLayout.GetPreferredHeight(t.text, t.GetGenerationSettings(new Vector2(640, 0))) / t.pixelsPerUnit;
            r.sizeDelta = new Vector2(640, Mathf.Max(19, lines + 1));
            StartCoroutine(FadeLine(t, 7f, 1f));
            while (_toasts.childCount > 8) DestroyImmediate(_toasts.GetChild(0).gameObject);
        }

        System.Collections.IEnumerator FadeLine(Text t, float hold, float fade)
        {
            yield return new WaitForSecondsRealtime(hold);
            if (t == null) yield break;
            t.CrossFadeAlpha(0, fade, true);
            yield return new WaitForSecondsRealtime(fade);
            if (t != null) Destroy(t.transform.parent.gameObject);
        }

        RectTransform _tutorial;
        float _tutorialT;

        /// <summary>A tutorial card below the aim text: stays 12 s, [Enter] dismisses it.
        /// Not modal, so play and the cursor are untouched.</summary>
        public void ShowTutorial(string title, string body)
        {
            if (_tutorial != null) Destroy(_tutorial.gameObject);
            _tutorial = At("Tutorial", _hud, new Vector2(0.5f, 1), new Vector2(-330, -330), new Vector2(660, 165));   // between the aim text and the hotbar
            Panel(_tutorial, new Color(0.16f, 0.11f, 0.06f, 0.94f), Brass);
            Label(_tutorial, "h", 26, Brass, TextAnchor.UpperCenter, Vector2.zero, Vector2.one, new Vector2(18, 0), new Vector2(-18, -14)).text = $"<b>{title}</b>";
            Label(_tutorial, "b", 18, Cream, TextAnchor.MiddleCenter, Vector2.zero, Vector2.one, new Vector2(24, 22), new Vector2(-24, -50)).text = body;
            Label(_tutorial, "k", 13, Muted, TextAnchor.LowerCenter, Vector2.zero, Vector2.one, new Vector2(18, 8), new Vector2(-18, 0)).text = "[Enter] dismiss";
            _tutorialT = 12f;
        }

        public bool TutorialShowing => _tutorial != null;

        public void DismissTutorial()
        {
            if (_tutorial != null) Destroy(_tutorial.gameObject);
            _tutorial = null;
        }

        public void ShowInspect(bool active, ItemDefinition def)
        {
            _inspect.gameObject.SetActive(active);
            _title.enabled = _sub.enabled = _hint.enabled = !active;
            if (!active || def == null) return;
            var p = _g.progress;
            var t = $"<size=24><b><color=#d4a352>{def.displayName}</color></b></size>\n<color=#b8a890>{def.CategoryName}  ·  {def.shape}</color>\n\n";
            if (def.IsRadarBattery) t += "A worn battery for the Part Radar. It goes in your battery pockets (no room in your hands); its charge shows on the radar. It never recharges: a spent one is thrown away.\n\n";
            else if (def.IsPegboardTool) t += "A hand tool from the <b>tool board</b> above the workbench.\nHang it on its own outline yourself: the robots leave the tools to you.\n\n";
            else if (def.IsToolItem) t += "A component of the <b>Part Radar</b>.\nCarry it to the Robot Workbench: one of each of the eight parts builds the radar.\n\n";
            else if (def.IsRobotPart) t += $"A component of <b>{def.subcategory}</b>.\nCarry it to the Robot Workbench yourself.\n\n";
            else
            {
                var mat = def.material == "oxidized" ? "Rusty steel" : char.ToUpper(def.material[0]) + def.material.Substring(1);
                t += $"Material:   {mat} (by eye)\nFinish:   {def.finish.Replace('_', ' ')}\nSize:   about {Mathf.Round(Mathf.Max(def.diameterMm, def.lengthMm))} mm\n";
                t += "Magnetic:   <color=#888888>needs Magnetic Collector</color>\n";
                if (def.IsThreaded) t += "Thread:   <color=#888888>needs Precision Scanner</color>\n";
                t += $"Weight:   about {Mathf.Round(def.weightG)} g\n\n";
            }
            t += p.CountLabel(def.id) + "\n";
            t += $"<color=#d4a352>Destination:</color>  {_g.db.BoxLabel(def.storageBox)}\n\n";
            t += "<color=#9a8c74>[Mouse / Q E] rotate   [Wheel] zoom   [R] auto-spin\n[LMB] collect   [F] close</color>";
            _inspectText.text = t;
        }

        public void ShowFatal(string text)
        {
            var r = At("Fatal", transform, Vector2.zero, new Vector2(100, 100), new Vector2(1200, 700));
            Panel(r, Bg, BadC);
            Fill(r, "t", 18, BadC).text = text;
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
            Debug.LogError("[fatal] " + text);
            if (Game.Instance != null && Game.Instance.Automated) Application.Quit(2);
        }

        public void MarkHotbarDirty() => _hotbarDirty = true;

        void Update()
        {
            if (Game.Booting) return;   // the game is still being built (loading splash)
            if (_screen == "skills") UpdateSkillKeys();
            if (_screen == "robots" && (_robotRefreshT -= Time.unscaledDeltaTime) <= 0) { _robotRefreshT = 1; RefreshRobots(); }
            if (_screen == "workbench" && (_wbRefreshT -= Time.unscaledDeltaTime) <= 0) { _wbRefreshT = 1; RefreshWorkbench(); }
            if (_screen == "modify" && (_mdRefreshT -= Time.unscaledDeltaTime) <= 0) { _mdRefreshT = 0.5f; RefreshModify(); }
            if (_feedbackT > 0)
            {
                _feedbackT -= Time.deltaTime;
                var c = _feedback.color; c.a = Mathf.Clamp01(_feedbackT / 0.6f); _feedback.color = c;
            }
            if (_tutorial != null && ((_tutorialT -= Time.unscaledDeltaTime) <= 0 || (Keyboard.current != null && Keyboard.current.enterKey.wasPressedThisFrame)))
                DismissTutorial();
            if (_g == null || _g.world == null) return;
            var p = _g.progress;
            var pr = _g.progression;
            if (_g.objectives != null) _objective.text = "<color=#b8a890>Objective:</color>  " + _g.objectives.HudText();
            _level.text = pr == null ? "" : pr.LevelText + (pr.skillPoints > 0 ? $"  ·  {pr.skillPoints} skill pt" : "");
            _progress.text = $"Workshop  {p.OverallPercent:0.0}% parts restored   ({p.sortedTotal} / {_g.world.Count})";
            _target.text = _g.radar != null && _g.radar.isOut ? _g.radar.HudText() : ActiveTarget();
            int z = _g.layout.ZoneAt(_g.player.transform.position);
            _zone.text = $"{_g.layout.zones[z].name}  —  {p.ZonePercent(z):0}% restored" + RobotStatus();
            UpdateHeld();
            UpdateSkillBar();
            if (Keyboard.current != null && Keyboard.current.f3Key.wasPressedThisFrame)
                _debug.transform.parent.gameObject.SetActive(!_debug.transform.parent.gameObject.activeSelf);
            if (_debug.transform.parent.gameObject.activeSelf) UpdateDebug();
        }

        /// <summary>The destination of the held part: "Active target  M6 hex bolt  9 / 10" (the ledger count).</summary>
        string ActiveTarget()
        {
            int d = _g.inventory.heldDef;
            var def = d >= 0 ? _g.db.Get(d) : null;
            if (def == null || def.IsRobotPart) return "";
            int have = _g.progress.AcceptedOf(d), total = _g.world.TotalOf(d);
            return $"<color=#b8a890>Active target:</color>  {def.displayName}  {have} / {total}" + (_g.progress.IsComplete(d) ? "  <color=#8fd18f>COMPLETED</color>" : "");
        }

        string RobotStatus()
        {
            var near = _g.NearestActiveRobot() ?? _g.retrobot;
            if (near == null) return "";
            int active = _g.robots.Count(r => r.Active);
            if (!near.Operational) return "";
            return "     " + near.Describe() + (active > 1 ? $"  (+{active - 1} robots)" : "");
        }

        /// <summary>The learned usable skills at the bottom: number key, name, a recharge bar and countdown.</summary>
        void UpdateSkillBar()
        {
            var t = _g.tools;
            var learned = PlayerTools.Usable.Where(u => t.Level(u.id) > 0).OrderBy(u => t.Slot(u.id)).ToList();
            string key = string.Join(",", learned.Select(u => u.id + t.Level(u.id) + ":" + t.Slot(u.id)));
            if (key != _skillBarKey)
            {
                _skillBarKey = key;
                foreach (Transform c in _skillBar) Destroy(c.gameObject);
                _skillSlots.Clear();
                foreach (var u in learned)
                {
                    var r = Rect("skill", _skillBar, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
                    var le = r.gameObject.AddComponent<LayoutElement>();
                    le.preferredWidth = 230; le.preferredHeight = 36;
                    Panel(r, new Color(0.1f, 0.07f, 0.05f, 0.8f), BrassDim);
                    var fill = Rect("cool", r, Vector2.zero, new Vector2(0, 1), Vector2.zero, Vector2.zero).gameObject.AddComponent<Image>();
                    fill.color = new Color(0.55f, 0.35f, 0.12f, 0.55f);
                    var label = Fill(r, "t", 15, Cream, TextAnchor.MiddleCenter);
                    // five or more slots share the bar: a long name shrinks instead of wrapping its countdown
                    label.horizontalOverflow = HorizontalWrapMode.Wrap; label.resizeTextForBestFit = true; label.resizeTextMinSize = 10; label.resizeTextMaxSize = 15;
                    _skillSlots.Add((u.id, label, fill));
                }
            }
            foreach (var (id, text, fill) in _skillSlots)
            {
                float left = t.CooldownLeft(id), full = Mathf.Max(0.01f, t.CooldownOf(id));
                text.text = $"<b>{t.Slot(id)}</b>  {t.NameOf(id)}" + (left > 0 ? $"  <color=#b8a890>{Mathf.CeilToInt(left)} s</color>" : "");
                fill.rectTransform.anchorMax = new Vector2(Mathf.Clamp01(left / full), 1);
            }
        }

        void UpdateHeld()
        {
            var inv = _g.inventory;
            int id = inv.HeldId();
            _heldPanel.gameObject.SetActive(id >= 0);
            if (id >= 0)
            {
                var def = _g.world.DefOf(id);
                bool known = _g.progress.IsDiscovered(def.id);
                _heldName.text = $"In hand:  {(known ? def.displayName : def.shape)}  ×{inv.IdsOf(def.id).Count}";
                _heldCount.text = def.CategoryName + "  ·  " + _g.progress.CountLabel(def.id);
                _heldDest.text = def.IsRadarBattery ? $"Battery charge  {_g.radar.Charges(id)} / {_g.radar.cfg.maxCharges}" + (_g.radar.assembled ? "   [Y] radar, [L] load" : "")
                    : "→  " + (def.IsToolItem ? "Robot Workbench (Part Radar)" : def.IsPegboardTool ? "the tool board above the workbench" : _g.db.BoxLabel(def.storageBox));
                _carried.text = $"Carrying {inv.Parts} / {inv.capacity}{(inv.Batteries > 0 ? $"  ·  batteries {inv.Batteries} / {inv.batteryPockets}" : "")}     [Wheel] switch   [Z] drop   [Tab] inventory";
            }
            if (!_hotbarDirty) return;
            _hotbarDirty = false;
            foreach (Transform c in _hotbar) Destroy(c.gameObject);
            var stacks = inv.Stacks();
            int shown = Mathf.Min(15, stacks.Count);
            for (int i = 0; i < shown; i++)
            {
                var (defId, ids) = stacks[i];
                bool sel = inv.IsHeldSlot(stacks[i]);
                var r = Rect("slot", _hotbar, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
                var le = r.gameObject.AddComponent<LayoutElement>();
                le.preferredWidth = Mathf.Min(150, 1390f / Mathf.Max(1, shown) - 6); le.preferredHeight = 40;
                Panel(r, sel ? new Color(0.25f, 0.17f, 0.08f, 0.9f) : new Color(0.1f, 0.07f, 0.05f, 0.75f), sel ? Brass : BrassDim);
                Fill(r, "t", shown > 9 ? 13 : 15, Cream, TextAnchor.MiddleCenter).text = _g.db.Get(defId).shape + (ids.Count > 1 ? $" ×{ids.Count}" : "");
            }
        }

        void UpdateDebug()
        {
            _frames.Add(Time.unscaledDeltaTime);
            if (_frames.Count > 240) _frames.RemoveAt(0);
            var sorted = _frames.OrderBy(x => x).ToList();
            float worst = sorted.Count > 10 ? sorted[(int)(sorted.Count * 0.99f)] : Time.unscaledDeltaTime;
            _debug.text = $"FPS {1f / Time.smoothDeltaTime:0}   (1% low {1f / Mathf.Max(worst, 1e-4f):0})\nFrame {Time.unscaledDeltaTime * 1000:0.00} ms\n" +
                $"Draw calls {_drawCalls.LastValue}\nItems visible {_g.renderer.VisibleCount} / {_g.world.Count}   ({_g.renderer.GroupCount} meshes)\n" +
                $"Pick {_g.index.LastPickMicros:0} µs, {_g.index.lastPickCandidates} candidates\nLast action {_g.interaction.lastActionMicros:0} µs\n" +
                $"Managed mem {System.GC.GetTotalMemory(false) / 1048576} MB   Unity reserved {Profiler.GetTotalReservedMemoryLong() / 1048576} MB\nRobot nav / tasks: n/a (Milestone 3)";
        }
    }
}
