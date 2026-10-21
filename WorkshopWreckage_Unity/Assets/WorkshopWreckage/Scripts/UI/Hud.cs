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
    public class Hud : MonoBehaviour
    {
        static readonly Color Bg = new(0.11f, 0.08f, 0.06f, 0.94f), Brass = new(0.82f, 0.63f, 0.32f), BrassDim = new(0.55f, 0.42f, 0.22f),
            Cream = new(0.96f, 0.91f, 0.8f), Muted = new(0.72f, 0.66f, 0.56f), GoodC = new(0.55f, 0.85f, 0.45f), BadC = new(0.95f, 0.42f, 0.32f);

        Game _g;
        Canvas _canvas;
        RectTransform _hud, _menus;
        Text _level, _title, _sub, _hint, _feedback, _progress, _zone, _heldName, _heldCount, _heldDest, _carried, _inspectText, _debug, _status;
        Image _cross;
        RectTransform _toasts, _hotbar, _heldPanel, _inspect;
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
            _toasts = At("Toasts", _hud, new Vector2(0.5f, 0), new Vector2(-300, 20), new Vector2(600, 300));
            var vl = _toasts.gameObject.AddComponent<VerticalLayoutGroup>();
            vl.spacing = 6; vl.childControlHeight = false; vl.childForceExpandHeight = false; vl.childAlignment = TextAnchor.UpperCenter;
            var tl = At("Progress", _hud, Vector2.zero, new Vector2(20, 20), new Vector2(440, 92));
            Panel(tl, new Color(0.1f, 0.07f, 0.05f, 0.72f), BrassDim);
            _progress = Label(tl, "p", 20, Brass, TextAnchor.UpperLeft, Vector2.zero, Vector2.one, new Vector2(14, 0), new Vector2(-10, -8));
            _level = Label(tl, "l", 17, Cream, TextAnchor.MiddleLeft, Vector2.zero, Vector2.one, new Vector2(14, 2), new Vector2(-10, 4));
            _zone = Label(tl, "z", 16, Muted, TextAnchor.LowerLeft, Vector2.zero, Vector2.one, new Vector2(14, 8), new Vector2(-10, 0));
            _heldPanel = At("Held", _hud, Vector2.one, new Vector2(-580, -230), new Vector2(560, 130));
            Panel(_heldPanel, new Color(0.1f, 0.07f, 0.05f, 0.8f), BrassDim);
            _heldName = Label(_heldPanel, "n", 20, Cream, TextAnchor.UpperLeft, Vector2.zero, Vector2.one, new Vector2(14, 0), new Vector2(-10, -10));
            _heldCount = Label(_heldPanel, "c", 16, Muted, TextAnchor.UpperLeft, Vector2.zero, Vector2.one, new Vector2(14, 0), new Vector2(-10, -40));
            _heldDest = Label(_heldPanel, "d", 16, Brass, TextAnchor.UpperLeft, Vector2.zero, Vector2.one, new Vector2(14, 0), new Vector2(-10, -64));
            _carried = Label(_heldPanel, "k", 15, Muted, TextAnchor.UpperLeft, Vector2.zero, Vector2.one, new Vector2(14, 0), new Vector2(-10, -92));
            _hotbar = At("Hotbar", _hud, new Vector2(0.5f, 1), new Vector2(-700, -60), new Vector2(1400, 44));
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
            return b;
        }

        RectTransform Screen(string name, float width)
        {
            var dim = Rect(name, _menus, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
            dim.gameObject.AddComponent<Image>().color = new Color(0.03f, 0.02f, 0.01f, 0.55f);
            var panel = Rect("Panel", dim, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero);
            panel.sizeDelta = new Vector2(width, 0);
            Panel(panel, Bg, BrassDim);
            var fit = panel.gameObject.AddComponent<ContentSizeFitter>();
            fit.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            var v = panel.gameObject.AddComponent<VerticalLayoutGroup>();
            v.padding = new RectOffset(18, 18, 14, 14);
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
            t.text = text; t.font = TextAtlas.Display; t.fontSize = size; t.color = Brass; t.alignment = TextAnchor.MiddleCenter;
            t.horizontalOverflow = HorizontalWrapMode.Overflow;
            go.GetComponent<LayoutElement>().preferredHeight = size * (text.Count(c => c == '\n') + 1) * 1.25f;
            return t;
        }

        Text Para(Transform p, string text, int size, Color c, float height)
        {
            var go = new GameObject("Text", typeof(RectTransform), typeof(Text), typeof(LayoutElement));
            go.transform.SetParent(p, false);
            var t = go.GetComponent<Text>();
            t.text = text; t.font = TextAtlas.Font; t.fontSize = size; t.color = c; t.alignment = TextAnchor.MiddleCenter;
            t.supportRichText = true;
            go.GetComponent<LayoutElement>().preferredHeight = height;
            return t;
        }

        void BuildMenus()
        {
            var m = Screen("main", 560);
            Title(m, "WORKSHOP\nWRECKAGE", 56);
            Para(m, "Restore order. Rebuild the robots.", 18, Muted, 28);
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
            Para(m, "WASD move · Q/E turn · Shift sprint · Ctrl crouch · Left mouse pick up / store · G store all\nF inspect · Z drop · Wheel/1-9 select · Tab inventory · F5/F9 quick save/load · F3 stats", 14, Muted, 40);
            Para(m, "<i>Every seed generates a new workshop. Saves remember their world size and seed.</i>", 13, Muted, 20);

            var p = Screen("pause", 460);
            Title(p, "PAUSED");
            MakeButton(p, "Resume", () => _g.SetState("play"));
            MakeButton(p, "Skills", () => ShowSub("skills"));
            MakeButton(p, "Save Game", () => ShowSub("save"));
            MakeButton(p, "Load Game", () => ShowSub("load"));
            MakeButton(p, "Settings", () => ShowSub("settings"));
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

            var st = Screen("settings", 640);
            Title(st, "SETTINGS");
            SliderRow(st, "Mouse sensitivity", 0.05f, 0.6f, _g.player != null ? _g.player.sensitivity : 0.22f, v => _g.player.sensitivity = v);
            SliderRow(st, "Field of view", 60, 100, 72, v => _g.player.cam.fieldOfView = v);
            SliderRow(st, "Master volume", 0, 1, Sfx.masterVolume, v => Sfx.masterVolume = v);
            ToggleRow(st, "V-Sync", QualitySettings.vSyncCount > 0, on => QualitySettings.vSyncCount = on ? 1 : 0);
            ToggleRow(st, "Fullscreen", UnityEngine.Screen.fullScreen, on => UnityEngine.Screen.fullScreen = on);
            ToggleRow(st, "Invert mouse Y", false, on => _g.player.invertY = on);
            ToggleRow(st, "Show performance stats", false, on => _debug.transform.parent.gameObject.SetActive(on));
            MakeButton(st, "Back", () => ShowScreenRaw(_backTo));

            var inv = Screen("inventory", 1100);
            Title(inv, "WORKSHOP LOG", 32);
            var body = Para(inv, "", 17, Cream, 600);
            body.alignment = TextAnchor.UpperLeft;
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
            sl.minValue = lo; sl.maxValue = hi; sl.value = value;
            sl.onValueChanged.AddListener(v => cb(v));
        }

        void ToggleRow(Transform parent, string label, bool value, System.Action<bool> cb)
        {
            var t = DefaultControls.CreateToggle(new DefaultControls.Resources());
            t.transform.SetParent(parent, false);
            t.AddComponent<LayoutElement>().preferredHeight = 30;
            var tg = t.GetComponent<Toggle>();
            tg.isOn = value;
            tg.onValueChanged.AddListener(v => cb(v));
            var txt = t.GetComponentInChildren<Text>();
            txt.text = label; txt.font = TextAtlas.Font; txt.fontSize = 18; txt.color = Cream;
        }

        // ------------------------------------------------------------------ screens

        public void SetScreen(string state)
        {
            _hud.gameObject.SetActive(state != "menu");
            ShowScreenRaw(state == "menu" ? "main" : state == "paused" ? "pause" : state == "inventory" ? "inventory" : "");
        }

        public void SetHudVisible(bool v) => _hud.gameObject.SetActive(v);

        void ShowSub(string name)
        {
            if (_screen == "main" || _screen == "pause") _backTo = _screen;
            ShowScreenRaw(name);
        }

        void ShowScreenRaw(string name)
        {
            _screen = name;
            foreach (var kv in _screens) kv.Value.gameObject.SetActive(kv.Key == name);
            _status.text = "";
            if (name == "save" || name == "load") RefreshSlots();
            if (name == "skills") RefreshSkills();
            if (name == "inventory") RefreshLog();
            if (name == "main") _continue.interactable = LatestSlot() != "";
        }

        public bool BackFromSubScreen()
        {
            if (_screen == "save" || _screen == "load" || _screen == "settings" || _screen == "skills") { ShowScreenRaw(_backTo); return true; }
            return false;
        }

        public void SetStatus(string t) => _status.text = t;

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
            _skillHeader.text = $"Level {pr.level} / {pr.maxLevel}   ·   {pr.XpIntoLevel:N0} / {(pr.level >= pr.maxLevel ? 0 : pr.XpForLevel(pr.level)):N0} XP   ·   {pr.skillPoints} skill point{(pr.skillPoints == 1 ? "" : "s")}";
            foreach (Transform c in _skillList) Destroy(c.gameObject);
            foreach (var s in pr.skills.OrderBy(s => s.unlockLevel))
            {
                bool owned = pr.unlocked.Contains(s.id);
                string why = s.Active ? pr.CanUnlock(s.id) : "coming in a later milestone";
                string state = owned ? "UNLOCKED" : why == "" ? $"unlock ({s.cost} pt)" : why;
                var b = MakeButton(_skillList, $"Lv {s.unlockLevel,2}  {s.name}  —  {state}", () => { if (_g.UnlockSkill(s.id)) RefreshSkills(); });
                b.interactable = !owned && s.Active && why == "";
                b.GetComponent<LayoutElement>().preferredHeight = 30;
            }
        }

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
            foreach (var (defId, ids) in inv.Stacks())
            {
                var def = db.Get(defId);
                t += $"{(defId == inv.heldDef ? "▶ " : "   ")}<b>{def.displayName}</b> ×{ids.Count}   <color=#b8a890>{p.CountLabel(defId)}</color>   → {db.BoxLabel(def.storageBox)}\n";
            }
            t += $"\n<b>Workshop restored: {p.OverallPercent:0.0}%</b>  ({p.sortedTotal} of {_g.world.Count} parts sorted) · discovered part types: {p.discovered.Count}\n";
            for (int z = 0; z < _g.layout.zones.Count; z++)
                t += $"{_g.layout.zones[z].name}:  {p.ZonePercent(z):0}%  ·  {p.ZoneSorted(z)} sorted, {p.ZoneRemaining(z)} still to sort\n";
            t += $"\nRobot components found: {p.robotPartsFound} / {db.RobotPartIds().Count}\n";
            foreach (var r in RepairBay.Robots) t += $"   {char.ToUpper(r[0]) + r.Substring(1)}: {_g.storage.Stored("RS-" + r).Count} / {db.RobotPartIds(r).Count} at the repair bay\n";
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

        public void Toast(string text, bool big = false)
        {
            var r = Rect("Toast", _toasts, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
            r.sizeDelta = new Vector2(600, big ? 44 : 36);
            Panel(r, new Color(0.16f, 0.11f, 0.06f, 0.92f), Brass);
            Fill(r, "t", big ? 24 : 19, big ? Brass : Cream, TextAnchor.MiddleCenter).text = text;
            Destroy(r.gameObject, 4f);
            while (_toasts.childCount > 4) DestroyImmediate(_toasts.GetChild(0).gameObject);
        }

        public void ShowInspect(bool active, ItemDefinition def)
        {
            _inspect.gameObject.SetActive(active);
            _title.enabled = _sub.enabled = _hint.enabled = !active;
            if (!active || def == null) return;
            var p = _g.progress;
            var t = $"<size=24><b><color=#d4a352>{def.displayName}</color></b></size>\n<color=#b8a890>{def.CategoryName}  ·  {def.shape}</color>\n\n";
            if (def.IsRobotPart) t += $"A component of <b>{def.subcategory}</b>.\nBring it to the Robot Repair Bay.\n\n";
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
            if (_feedbackT > 0)
            {
                _feedbackT -= Time.deltaTime;
                var c = _feedback.color; c.a = Mathf.Clamp01(_feedbackT / 0.6f); _feedback.color = c;
            }
            if (_g == null || _g.world == null) return;
            var p = _g.progress;
            var pr = _g.progression;
            _level.text = pr == null ? "" : $"Level {pr.level}  ·  {pr.XpIntoLevel:N0}/{(pr.level >= pr.maxLevel ? pr.XpIntoLevel : pr.XpForLevel(pr.level)):N0} XP" + (pr.skillPoints > 0 ? $"  ·  {pr.skillPoints} skill pt" : "");
            _progress.text = $"Workshop restored  {p.OverallPercent:0.0}%   ({p.sortedTotal} / {_g.world.Count} sorted)";
            int z = _g.layout.ZoneAt(_g.player.transform.position);
            _zone.text = $"{_g.layout.zones[z].name}  —  {p.ZonePercent(z):0}% restored";
            UpdateHeld();
            if (Keyboard.current != null && Keyboard.current.f3Key.wasPressedThisFrame)
                _debug.transform.parent.gameObject.SetActive(!_debug.transform.parent.gameObject.activeSelf);
            if (_debug.transform.parent.gameObject.activeSelf) UpdateDebug();
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
                _heldDest.text = "→  " + _g.db.BoxLabel(def.storageBox);
                _carried.text = $"Carrying {inv.Count} / {inv.capacity}     [Wheel/1-9] switch   [Z] drop   [Tab] inventory";
            }
            if (!_hotbarDirty) return;
            _hotbarDirty = false;
            foreach (Transform c in _hotbar) Destroy(c.gameObject);
            var stacks = inv.Stacks();
            for (int i = 0; i < Mathf.Min(9, stacks.Count); i++)
            {
                var (defId, ids) = stacks[i];
                bool sel = defId == inv.heldDef;
                var r = Rect("slot", _hotbar, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
                var le = r.gameObject.AddComponent<LayoutElement>();
                le.preferredWidth = 150; le.preferredHeight = 40;
                Panel(r, sel ? new Color(0.25f, 0.17f, 0.08f, 0.9f) : new Color(0.1f, 0.07f, 0.05f, 0.75f), sel ? Brass : BrassDim);
                Fill(r, "t", 15, Cream, TextAnchor.MiddleCenter).text = $"{i + 1}  {_g.db.Get(defId).shape} ×{ids.Count}";
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
