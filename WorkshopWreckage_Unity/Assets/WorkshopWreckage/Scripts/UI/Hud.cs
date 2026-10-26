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
    /// <summary>HUD and menus (port of Godot hud.gd + menus.gd), built in uGUI.</summary>
    public partial class Hud : MonoBehaviour
    {
        static readonly Color Bg = new(0.11f, 0.08f, 0.06f, 0.94f), Brass = new(0.82f, 0.63f, 0.32f), BrassDim = new(0.55f, 0.42f, 0.22f),
            Cream = new(0.96f, 0.91f, 0.8f), Muted = new(0.72f, 0.66f, 0.56f), GoodC = new(0.55f, 0.85f, 0.45f), BadC = new(0.95f, 0.42f, 0.32f);

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

        /// <summary>Pins a rect to an anchor with a pixel offset and size (y down, like Godot).</summary>
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

        static Text Label(Transform parent, string name, int size, Color c, TextAnchor anchor, Vector2 aMin, Vector2 aMax, Vector2 offMin, Vector2 offMax, bool display = false)
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
            var sh = r.gameObject.AddComponent<Shadow>();
            sh.effectColor = new Color(0, 0, 0, 0.75f);
            sh.effectDistance = new Vector2(1.5f, -1.5f);
            return t;
        }

        static Text Fill(Transform parent, string name, int size, Color c, TextAnchor a = TextAnchor.UpperLeft)
            => Label(parent, name, size, c, a, Vector2.zero, Vector2.one, new Vector2(14, 10), new Vector2(-14, -10));

        void BuildHud()
        {
            var cross = At("Cross", _hud, new Vector2(0.5f, 0.5f), new Vector2(-3, -3), new Vector2(6, 6));
            _cross = cross.gameObject.AddComponent<Image>();
            _cross.color = Cream;
            var tbox = At("Target", _hud, new Vector2(0.5f, 0.5f), new Vector2(-400, 34), new Vector2(800, 110));
            _title = Label(tbox, "title", 22, Cream, TextAnchor.UpperCenter, new Vector2(0, 1), Vector2.one, new Vector2(0, -32), Vector2.zero);
            _sub = Label(tbox, "sub", 16, Muted, TextAnchor.UpperCenter, new Vector2(0, 1), Vector2.one, new Vector2(0, -56), new Vector2(0, -32));
            _hint = Label(tbox, "hint", 18, Brass, TextAnchor.UpperCenter, new Vector2(0, 1), Vector2.one, new Vector2(0, -84), new Vector2(0, -56));
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
            _continue = MakeButton(m, "Continue", () => _g.LoadGame(LatestSlot()));
            // world size: cycles through shared/data/world_presets.json
            var presets = ItemDatabase.Presets();
            if (presets.Count == 0) presets.Add((_g.presetId, _g.db.config));
            int pi = Mathf.Max(0, presets.FindIndex(x => x.id == _g.presetId));
            string PresetText() => $"World size:  {Json.Str(presets[pi].cfg, "label", presets[pi].id)}  ({Json.Int(presets[pi].cfg, "total_items"):N0} parts)";
            Button presetBtn = null;
            presetBtn = MakeButton(m, PresetText(), () =>
            {
                if (presets.Count == 0) return;
                pi = (pi + 1) % presets.Count;
                presetBtn.GetComponentInChildren<Text>().text = PresetText();
            });
            var seedRow = new GameObject("Seed", typeof(RectTransform), typeof(HorizontalLayoutGroup), typeof(LayoutElement));
            seedRow.transform.SetParent(m, false);
            seedRow.GetComponent<LayoutElement>().preferredHeight = 40;
            Para(seedRow.transform, "Workshop seed", 18, Cream, 40).alignment = TextAnchor.MiddleLeft;
            var inGo = DefaultControls.CreateInputField(new DefaultControls.Resources());
            inGo.transform.SetParent(seedRow.transform, false);
            _seed = inGo.GetComponent<InputField>();
            _seed.text = Json.Int(_g.db.config, "default_seed", 1912).ToString();
            _seed.textComponent.font = TextAtlas.Font;
            MakeButton(m, "New Game", () => { if (_g.NewGame(presets[pi].id, int.TryParse(_seed.text, out var s) ? s : 1912)) _g.SetState("play"); });
            MakeButton(m, "Load Game", () => ShowSub("load"));
            MakeButton(m, "Settings", () => ShowSub("settings"));
            MakeButton(m, "Quit", Application.Quit);
            _mainTitleBits.Add(Para(m, "WASD move · Q/E turn · Shift sprint · Ctrl crouch · Left mouse pick up / store · G store all\nF inspect · Z drop · Wheel select · 1-9 skills (glove, scan, supermagnet, sweep) · K skills · U robots · R/T/H/J robots · Tab log · F5/F9 save/load", 14, Muted, 40).gameObject);
            _mainTitleBits.Add(Para(m, "<i>Every seed generates a new workshop. Saves remember their world size and seed.</i>", 13, Muted, 20).gameObject);

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
            MakeButton(p, "Main Menu", () => _g.SetState("menu"));
            MakeButton(p, "Quit", Application.Quit);

            var s = Screen("save", 720);
            Title(s, "SAVE GAME");
            _saveList = Rect("List", s, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
            Stack(_saveList);
            MakeButton(s, "Back", () => ShowScreenRaw(_backTo));
            var l = Screen("load", 720);
            Title(l, "LOAD GAME");
            _loadList = Rect("List", l, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
            Stack(_loadList);
            MakeButton(l, "Back", () => ShowScreenRaw(_backTo));

            var sk = Screen("skills", 1000);
            Title(sk, "SKILLS");
            _skillHeader = Para(sk, "", 18, Brass, 28);
            _skillList = Rect("List", sk, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
            Stack(_skillList);
            MakeButton(sk, "Back", () => ShowScreenRaw(_backTo));

            BuildRobotScreens();

            var ex = Screen("exit", 620);
            Title(ex, "LEAVE THE WORKSHOP?", 34);
            Para(ex, "Stepping out through the door ends the game.\nYour workshop is saved to the quick slot if you choose to save.", 17, Muted, 56);
            MakeButton(ex, "Save and leave", () => _g.LeaveWorkshop(true));
            MakeButton(ex, "Leave without saving", () => _g.LeaveWorkshop(false));
            MakeButton(ex, "Stay in the workshop", () => _g.SetState("play"));

            var st = Screen("settings", 640);
            Title(st, "SETTINGS");
            SliderRow(st, "Mouse sensitivity", 0.05f, 0.6f, _g.player != null ? _g.player.sensitivity : 0.22f, v => _g.player.sensitivity = v);
            SliderRow(st, "Field of view", 60, 100, 72, v => _g.player.cam.fieldOfView = _g.player.fov = v);
            SliderRow(st, "Master volume", 0, 1, Sfx.MasterSetting, Sfx.SetMaster);
            SliderRow(st, "Sound effects", 0, 1, Sfx.SfxSetting, Sfx.SetSfx);
            ToggleRow(st, "V-Sync", QualitySettings.vSyncCount > 0, on => QualitySettings.vSyncCount = on ? 1 : 0);
            ToggleRow(st, "Fullscreen", UnityEngine.Screen.fullScreen, on => UnityEngine.Screen.fullScreen = on);
            ToggleRow(st, "Invert mouse Y", false, on => _g.player.invertY = on);
            ToggleRow(st, "Robot part afterglow (blinks once after you look away)", Interaction.Afterglow, on =>
            {
                Interaction.Afterglow = on;
                PlayerPrefs.SetInt("ww_part_afterglow", on ? 1 : 0);
                PlayerPrefs.Save();
            });
            ToggleRow(st, "Show performance stats", false, on => _debug.transform.parent.gameObject.SetActive(on));
            {
                // part shadows: the main GPU cost on Massive; "off" for slower PCs
                Button sb = null;
                sb = MakeButton(st, "Part shadows: " + ItemRenderer.ShadowModeNames[ItemRenderer.ShadowMode], () =>
                {
                    ItemRenderer.ShadowMode = (ItemRenderer.ShadowMode + 1) % 3;
                    PlayerPrefs.SetInt("ww_part_shadows", ItemRenderer.ShadowMode);
                    sb.GetComponentInChildren<Text>().text = "Part shadows: " + ItemRenderer.ShadowModeNames[ItemRenderer.ShadowMode];
                });
            }
            MakeButton(st, "Back", () => ShowScreenRaw(_backTo));

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

        void SliderRow(Transform parent, string label, float lo, float hi, float value, System.Action<float> cb)
        {
            var row = new GameObject(label, typeof(RectTransform), typeof(HorizontalLayoutGroup), typeof(LayoutElement));
            row.transform.SetParent(parent, false);
            row.GetComponent<LayoutElement>().preferredHeight = 32;
            Para(row.transform, label, 18, Cream, 32).alignment = TextAnchor.MiddleLeft;
            var s = DefaultControls.CreateSlider(new DefaultControls.Resources());
            s.transform.SetParent(row.transform, false);
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
            System.Action<float> show = v => val.text = lo == 0f && hi == 1f ? $"{Mathf.RoundToInt(v * 100)} %" : $"{v:0.##}";
            sl.minValue = lo; sl.maxValue = hi; sl.value = value;
            show(sl.value);
            sl.onValueChanged.AddListener(v => { show(v); cb(v); });
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

        void ToggleRow(Transform parent, string label, bool value, System.Action<bool> cb)
        {
            var t = DefaultControls.CreateToggle(new DefaultControls.Resources());
            t.transform.SetParent(parent, false);
            t.AddComponent<LayoutElement>().preferredHeight = 30;
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

        void ShowScreenRaw(string name)
        {
            _screen = name;
            foreach (var kv in _screens) kv.Value.gameObject.SetActive(kv.Key == name);
            _status.text = "";
            if (name == "save" || name == "load") RefreshSlots();
            if (name == "skills") RefreshSkills();
            if (name == "robots") RefreshRobots();
            if (name == "workbench") RefreshWorkbench();
            else if (name == "modify") RefreshModify();
            else if (_previewCam != null) PreviewCamera(false);
            if (name == "inventory") RefreshLog();
            if (name == "main") _continue.interactable = LatestSlot() != "";
        }

        public bool BackFromSubScreen()
        {
            if (_screen == "save" || _screen == "load" || _screen == "settings" || _screen == "skills" || _screen == "robots") { ShowScreenRaw(_backTo); return true; }
            if (_screen == "modify") { ShowScreenRaw("workbench"); return true; }
            return false;
        }

        public void SetStatus(string t) => _status.text = t;
        public void OpenScreen(string name) => ShowSub(name);

        void RefreshSlots()
        {
            foreach (var (list, saving) in new[] { (_saveList, true), (_loadList, false) })
            {
                foreach (Transform c in list) Destroy(c.gameObject);
                foreach (var slot in SaveSystem.Slots)
                {
                    if (saving && slot == "quick") continue;
                    var b = MakeButton(list, SaveSystem.Describe(slot), () =>
                    {
                        if (saving) { _g.SaveGame(slot); RefreshSlots(); }
                        else _g.LoadGame(slot);
                    });
                    b.interactable = saving || System.IO.File.Exists(SaveSystem.PathOf(slot));
                }
            }
        }

        Text _skillHeader;
        RectTransform _skillList;

        /// <summary>Skill tree from progression.json. Planned skills are listed (so the
        /// progression is visible) but cannot be unlocked until they are implemented.</summary>
        void RefreshSkills()
        {
            var pr = _g.progression;
            if (pr == null) return;
            _skillHeader.text = $"{pr.LevelText}   ·   {pr.skillPoints} skill point{(pr.skillPoints == 1 ? "" : "s")}";
            foreach (Transform c in _skillList) Destroy(c.gameObject);
            // a skill with ranks is one row: its rank now and the next one to buy (not a line per rank)
            foreach (var chain in SkillChains(pr.skills).OrderBy(c => (c.FirstOrDefault(s => !pr.unlocked.Contains(s.id)) ?? c[^1]).unlockLevel))   // by the level of the next rank
            {
                int have = chain.TakeWhile(s => pr.unlocked.Contains(s.id)).Count();
                var next = have < chain.Count ? chain[have] : null;
                string why = next == null ? "" : next.Active ? pr.CanUnlock(next.id) : "coming in a later milestone";
                string name = RankBase(chain[0].name), label;
                if (chain.Count == 1)
                    label = $"Lv {next?.unlockLevel ?? chain[0].unlockLevel,2}  {name}  —  {(next == null ? "UNLOCKED" : why == "" ? $"unlock ({next.cost} pt)" : why)}";
                else
                {
                    string rank = have == 0 ? "not learned" : $"rank {have} / {chain.Count}";
                    string step = next == null ? "MAX" : $"{(have == 0 ? "learn" : "upgrade")} to rank {have + 1} ({next.cost} pt){(RankBase(next.name) != name ? $": {next.name}" : "")}";
                    label = $"Lv {next?.unlockLevel ?? chain[^1].unlockLevel,2}  {name}  ·  {rank}  —  {(next == null || why == "" ? step : $"rank {have + 1}: {why}")}";
                }
                var b = MakeButton(_skillList, label, () => { if (next != null && _g.UnlockSkill(next.id)) RefreshSkills(); });
                b.interactable = next != null && next.Active && why == "";
                b.GetComponent<LayoutElement>().preferredHeight = 30;
            }
            RespecButton(_skillList, RefreshSkills);
            if (pr.level < pr.respecLevel) Para(_skillList, $"Respec (every skill and robot upgrade back at what it cost) opens at level {pr.respecLevel}.", 13, Muted, 18);
        }

        /// <summary>The skills grouped into rank chains: a skill whose only requirement is the previous skill
        /// and which raises one of the same effects is its next rank (Magnetic Glove I-X, Robot Part Sense I-V,
        /// Deeper Pockets → Tool Belt).</summary>
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

        static string LatestSlot()
        {
            string best = "";
            System.DateTime bestT = System.DateTime.MinValue;
            foreach (var s in SaveSystem.Slots)
            {
                var p = SaveSystem.PathOf(s);
                if (System.IO.File.Exists(p) && System.IO.File.GetLastWriteTime(p) > bestT) { bestT = System.IO.File.GetLastWriteTime(p); best = s; }
            }
            return best;
        }

        void RefreshLog()
        {
            var inv = _g.inventory; var p = _g.progress; var db = _g.db;
            var t = $"<b>Carrying {inv.Count} / {inv.capacity}</b>\n";
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
            foreach (var pj in _g.workbench.projects.Where(x => !x.crafted))
                t += $"   {pj.name}: {_g.workbench.InstalledCount(pj)} / {_g.workbench.Required(pj)} installed, {_g.workbench.DeliveredIds(pj).Count} in the holding tray  ·  {RobotWorkbench.StatusText(_g.workbench.StatusOf(pj))}\n";
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
            _feedback.text = text;
            _feedback.color = good ? GoodC : BadC;
            _feedbackT = 2.2f;
        }

        /// <summary>A line in the message log (top left, no box): newest at the bottom, fading out after 8 s.</summary>
        public void Toast(string text, bool big = false)
        {
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
            if (def.IsRadarBattery) t += "A worn battery for the Part Radar. Its charge shows once it is in your inventory; it never recharges.\n\n";
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
                    : "→  " + (def.IsToolItem ? "Robot Workbench (Part Radar)" : _g.db.BoxLabel(def.storageBox));
                _carried.text = $"Carrying {inv.Count} / {inv.capacity}     [Wheel] switch   [Z] drop   [Tab] inventory";
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
