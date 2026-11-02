using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace WorkshopWreckage
{
    /// <summary>The skill panel (K; docs/SKILL_PANEL.md): pages turned with ◀ ▶ (also ← → / Q E / shoulder buttons),
    /// PLAYER SKILLS first, then one page of upgrades per robot type. Left: the page's skills, each with its level
    /// as a row of gears (black = bought, grey = not yet). Right: the shared-points note and the hovered or selected
    /// skill: what it does now, what the next level does, and its cost (or MAX LEVEL). Buying works as before
    /// (one click buys the next level); nothing here changes costs, values or rules.</summary>
    public partial class Hud
    {
        /// <summary>One skill row on the current page.</summary>
        class SkillRowUi
        {
            public Button button;
            public Text name, level, chip;
            public RectTransform plate;
            public readonly List<Image> gears = new();
            public Image mark;
        }

        /// <summary>A skill or robot upgrade as the panel shows it, read fresh from the progression each time.</summary>
        class SkillEntry
        {
            public string name, unit = "Level", why = "";
            public int have, max, cost;
            public bool Max => have >= max;
            public System.Func<bool> buy;
            public System.Func<List<string>> now, next;
            public string blurb = "", bought = "";
        }

        // the robot pages in this order; a type with upgrades not listed here comes after them (by id)
        static readonly string[] RobotPageOrder = { "retrobot", "unibot", "caterpillarbot", "spiderbot", "hoverbot", "sorting_arm" };
        static readonly Color GearOn = new(0.08f, 0.08f, 0.1f, 1f), GearOff = new(0.5f, 0.51f, 0.53f, 0.6f), TagPaper = new(0.88f, 0.86f, 0.78f, 0.96f);
        static Sprite _gearSprite;

        readonly List<SkillRowUi> _skRows = new();
        RectTransform _skRowsRoot;
        ScrollRect _skScroll;
        Text _skTitle, _skPageNo, _skPoints, _skNote, _skShared, _skDetail, _skStatus;
        Button _skPrev, _skNext;
        int _skPage, _skFocus;
        string _skBuilt;   // the page whose rows exist

        void BuildSkillScreen()
        {
            var sk = Screen("skills", 1180);
            var head = HBox("Pages", sk, 64, 16);
            _skPrev = PageArrow(head, "◀", () => SkillPageStep(-1));
            _skTitle = Title(head, "PLAYER SKILLS", 38);
            _skTitle.GetComponent<LayoutElement>().flexibleWidth = 1;
            _skNext = PageArrow(head, "▶", () => SkillPageStep(1));
            _skPageNo = Para(sk, "", 14, Muted, 18);
            _skPoints = Para(sk, "", 24, Brass, 34);

            var body = HBox("Body", sk, 620, 20);
            body.GetComponent<HorizontalLayoutGroup>().childForceExpandHeight = true;
            // left: the page's skills
            var left = new GameObject("Skills", typeof(RectTransform), typeof(VerticalLayoutGroup), typeof(LayoutElement)).GetComponent<RectTransform>();
            left.SetParent(body, false);
            var lle = left.GetComponent<LayoutElement>();
            lle.preferredWidth = lle.minWidth = 660; lle.flexibleWidth = 0;
            var lv = left.GetComponent<VerticalLayoutGroup>();
            lv.spacing = 6; lv.childControlHeight = true; lv.childControlWidth = true; lv.childForceExpandHeight = false;
            _skNote = Para(left, "", 16, Brass, 46);
            _skNote.alignment = TextAnchor.MiddleLeft;
            var view = new GameObject("View", typeof(RectTransform), typeof(Image), typeof(RectMask2D), typeof(LayoutElement)).GetComponent<RectTransform>();
            view.SetParent(left, false);
            view.GetComponent<Image>().color = new Color(0, 0, 0, 0.001f);   // catches the scroll wheel
            view.GetComponent<LayoutElement>().flexibleHeight = 1;
            _skRowsRoot = Rect("Rows", view, new Vector2(0, 1), new Vector2(1, 1), Vector2.zero, Vector2.zero);
            _skRowsRoot.pivot = new Vector2(0.5f, 1);
            var rv = _skRowsRoot.gameObject.AddComponent<VerticalLayoutGroup>();
            rv.spacing = 3; rv.childControlHeight = true; rv.childControlWidth = true; rv.childForceExpandHeight = false;
            _skRowsRoot.gameObject.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            _skScroll = view.gameObject.AddComponent<ScrollRect>();
            _skScroll.viewport = view; _skScroll.content = _skRowsRoot;
            _skScroll.horizontal = false; _skScroll.movementType = ScrollRect.MovementType.Clamped; _skScroll.scrollSensitivity = 30;

            // right: the shared points, then the hovered / selected skill
            var right = new GameObject("Info", typeof(RectTransform), typeof(Image), typeof(VerticalLayoutGroup), typeof(LayoutElement)).GetComponent<RectTransform>();
            right.SetParent(body, false);
            var rle = right.GetComponent<LayoutElement>();
            rle.preferredWidth = rle.minWidth = 400; rle.flexibleWidth = 1;   // fixed widths: the texts' own preferred widths must not squeeze the rows
            right.GetComponent<Image>().color = new Color(0.03f, 0.08f, 0.15f, 0.85f);
            var iv = right.GetComponent<VerticalLayoutGroup>();
            iv.padding = new RectOffset(18, 18, 14, 14); iv.spacing = 10; iv.childControlHeight = true; iv.childControlWidth = true; iv.childForceExpandHeight = false;
            _skShared = Para(right, "", 15, Muted, 124);
            _skShared.alignment = TextAnchor.UpperLeft;
            var rule = new GameObject("Rule", typeof(RectTransform), typeof(Image), typeof(LayoutElement));
            rule.transform.SetParent(right, false);
            rule.GetComponent<Image>().color = new Color(UiStyle.Ink.r, UiStyle.Ink.g, UiStyle.Ink.b, 0.35f);
            rule.GetComponent<LayoutElement>().preferredHeight = 1;
            _skDetail = Para(right, "", 17, Cream, 420);
            _skDetail.alignment = TextAnchor.UpperLeft;
            _skDetail.GetComponent<LayoutElement>().flexibleHeight = 1;

            _skStatus = Para(sk, "", 19, GoodC, 28);
            MakeButton(sk, "Back", () => ShowScreenRaw(_backTo));
        }

        RectTransform HBox(string name, Transform parent, float height, float spacing)
        {
            var r = new GameObject(name, typeof(RectTransform), typeof(HorizontalLayoutGroup), typeof(LayoutElement)).GetComponent<RectTransform>();
            r.SetParent(parent, false);
            var h = r.GetComponent<HorizontalLayoutGroup>();
            h.spacing = spacing; h.childControlWidth = true; h.childControlHeight = true; h.childForceExpandWidth = false; h.childForceExpandHeight = true;
            h.childAlignment = TextAnchor.MiddleCenter;
            r.GetComponent<LayoutElement>().preferredHeight = height;
            return r;
        }

        Button PageArrow(Transform parent, string glyph, System.Action cb)
        {
            var b = MakeButton(parent, glyph, cb);
            var le = b.GetComponent<LayoutElement>();
            le.preferredWidth = 64; le.flexibleWidth = 0;
            b.GetComponentInChildren<Text>().fontSize = 26;
            var nav = b.navigation; nav.mode = Navigation.Mode.None; b.navigation = nav;   // ← → turn pages; the arrows are for the mouse
            return b;
        }

        // ------------------------------------------------------------------ pages

        /// <summary>The panel's pages: "player", then each robot type that has upgrades.</summary>
        public List<string> SkillPages()
        {
            var pr = _g.progression;
            var types = pr == null ? new List<string>() : pr.robotUpgrades.Select(u => u.robot).Distinct()
                .OrderBy(t => System.Array.IndexOf(RobotPageOrder, t) is int i && i >= 0 ? i : 100).ThenBy(t => t).ToList();
            types.Insert(0, "player");
            return types;
        }

        static string RobotTypeName(string type) => Robot.AllConfig().ContainsKey(type) ? Json.Str(Json.Obj(Robot.AllConfig()[type]), "name") : type;

        public int SkillPageIndex => _skPage;
        public string SkillPageTitle => _skTitle != null ? _skTitle.text : "";
        public string SkillPointsText => _skPoints != null ? _skPoints.text : "";
        public string SkillDetailText => _skDetail != null ? _skDetail.text : "";
        public string SkillSharedText => _skShared != null ? _skShared.text : "";
        public string SkillStatusText => _skStatus != null ? _skStatus.text : "";
        public string SkillNoteText => _skNote != null && _skNote.gameObject.activeSelf ? _skNote.text : "";
        public bool SkillPrevEnabled => _skPrev != null && _skPrev.interactable;
        public bool SkillNextEnabled => _skNext != null && _skNext.interactable;
        public int SkillFocusRow => _skFocus;

        /// <summary>The rows as drawn: name, bought gears (black), gears in all, the state chip.</summary>
        public List<(string name, int on, int gears, string chip, string level)> SkillRowsShown()
            => _skRows.Select(r => (r.name.text, r.gears.Count(g => g.color == GearOn), r.gears.Count, r.chip.text, r.level.text)).ToList();

        public GameObject SkillRowObject(int row) => row >= 0 && row < _skRows.Count ? _skRows[row].button.gameObject : null;
        public Button SkillArrow(int dir) => dir < 0 ? _skPrev : _skNext;

        public void SkillPageStep(int dir)
        {
            int n = SkillPages().Count;
            int to = Mathf.Clamp(_skPage + dir, 0, n - 1);
            if (to == _skPage) return;
            _skPage = to;
            _skFocus = 0;
            _skStatus.text = "";
            RefreshSkills();
            SelectSkillRow(0);
        }

        /// <summary>The panel opens on the page and row it was left at, with that row selected for keyboard / controller.</summary>
        void OpenSkillPanel()
        {
            if (_skStatus != null) _skStatus.text = "";
            RefreshSkills();
            SelectSkillRow(_skFocus);
        }

        public void ShowSkillPage(string id)
        {
            int i = SkillPages().IndexOf(id);
            if (i < 0) return;
            _skPage = i; _skFocus = 0; _skStatus.text = "";
            RefreshSkills();
        }

        /// <summary>← → (or Q / E, the shoulder buttons) turn the page while the panel is open.</summary>
        void UpdateSkillKeys()
        {
            int d = 0;
            var kb = Keyboard.current;
            if (kb != null && (kb.leftArrowKey.wasPressedThisFrame || kb.qKey.wasPressedThisFrame)) d = -1;
            if (kb != null && (kb.rightArrowKey.wasPressedThisFrame || kb.eKey.wasPressedThisFrame)) d = 1;
            var gp = Gamepad.current;
            if (gp != null && (gp.leftShoulder.wasPressedThisFrame || gp.dpad.left.wasPressedThisFrame)) d = -1;
            if (gp != null && (gp.rightShoulder.wasPressedThisFrame || gp.dpad.right.wasPressedThisFrame)) d = 1;
            if (d != 0) SkillPageStep(d);
        }

        // ------------------------------------------------------------------ content

        List<SkillEntry> SkillEntries(string page)
        {
            var pr = _g.progression;
            var o = new List<SkillEntry>();
            if (page == "player")
            {
                foreach (var chain in SkillChains(pr.skills.Where(s => !s.Retired).ToList()))
                {
                    int have = chain.TakeWhile(s => pr.unlocked.Contains(s.id)).Count();
                    var next = have < chain.Count ? chain[have] : null;
                    var c = chain;
                    var blurb = chain[0].description ?? "";
                    o.Add(new SkillEntry
                    {
                        name = RankBase(chain[0].name), have = have, max = chain.Count, cost = next?.cost ?? 0,
                        why = next == null ? "" : pr.CanUnlock(next.id), blurb = blurb,
                        buy = () => next != null && _g.UnlockSkill(next.id),
                        bought = next == null ? "" : $"Learned {next.name}",
                        now = () => have == 0 ? new List<string>() : SkillLines(c, have),
                        next = () => next == null ? new List<string>() : SkillLines(c, have + 1),
                    });
                }
                return o;
            }
            var units = _g.robots.Where(r => r.type == page && r.Operational).ToList();
            var rb = units.FirstOrDefault() ?? _g.robots.FirstOrDefault(r => r.type == page);
            foreach (var u in pr.robotUpgrades.Where(u => u.robot == page))
            {
                int rank = pr.RankOf(u.id);
                var up = u;
                o.Add(new SkillEntry
                {
                    name = u.name, unit = "Rank", have = rank, max = u.maxRank, cost = u.CostOf(rank),
                    why = rank >= u.maxRank ? "" : pr.CanBuyUpgrade(u.id, units.Count > 0), blurb = u.description ?? "",
                    buy = () => _g.BuyRobotUpgrade(up.id),
                    bought = $"{u.name} rank {rank + 1}",
                    now = () => UpgradeLines(pr, rb, up, rank, false),
                    next = () => rank >= up.maxRank ? new List<string>() : UpgradeLines(pr, rb, up, rank + 1, true),
                });
            }
            return o;
        }

        /// <summary>A robot upgrade at rank r: "Speed: 0.50 m/s" per stat it touches, or the behaviour of that rank.</summary>
        List<string> UpgradeLines(Progression pr, Robot rb, RobotUpgradeDef u, int r, bool next)
        {
            if (u.mode == "rank") return r == 0 ? new List<string>() : new List<string> { Cap(RankPreview(u.stat, r, rb)) };
            if (rb == null) return new List<string>();
            return UpgradeChanges(pr, rb, u, next ? r : pr.RankOf(u.id) + 1).Select(c => $"{Cap(c.label)}: {(next ? c.after + c.note : c.now)}").ToList();
        }

        /// <summary>Rebuilds the current page (rows only when the page changed; otherwise in place, keeping the
        /// keyboard selection), the points line and the detail.</summary>
        void RefreshSkills()
        {
            var pr = _g.progression;
            if (pr == null || _skTitle == null) return;
            var pages = SkillPages();
            _skPage = Mathf.Clamp(_skPage, 0, pages.Count - 1);
            string page = pages[_skPage];
            var entries = SkillEntries(page);
            bool robot = page != "player";
            string rname = robot ? RobotTypeName(page) : "";
            _skTitle.text = robot ? $"{rname.ToUpperInvariant()} UPGRADES" : "PLAYER SKILLS";
            _skPageNo.text = $"page {_skPage + 1} / {pages.Count}   ·   " + string.Join("  ", pages.Select((p, i) => i == _skPage ? "●" : "○")) + "   ·   ← → or Q / E to turn pages";
            _skPrev.interactable = _skPage > 0;
            _skNext.interactable = _skPage < pages.Count - 1;
            _skPoints.text = $"<b>{pr.skillPoints}</b> skill point{(pr.skillPoints == 1 ? "" : "s")} to spend   ·   {pr.LevelText}";
            _skShared.text = "<b>Skill points are shared between player skills and robot upgrades.</b> A point spent on any page comes out of the same pool.\n"
                + $"Each level-up gives 1 point: about {pr.PointBudget} in a whole playthrough, for {pr.BuyableSteps} skill levels and robot upgrades. You can't max everything: choose.";

            string note = "";
            if (!robot && migrationNotice) note = "Your save was moved to the new skill system: level recomputed from completed storage places, all points back to spend.";
            if (robot)
            {
                int built = _g.robots.Count(r => r.type == page && r.Operational);
                note = built == 0 ? $"{rname} is not built yet. Build it at the Robot Workbench to buy these upgrades; you can plan them now."
                     : built > 1 ? $"Every upgrade here applies to all {built} {rname}s." : "";
            }
            _skNote.text = note;
            _skNote.gameObject.SetActive(note != "");

            if (_skBuilt != page || _skRows.Count != entries.Count)
            {
                foreach (Transform c in _skRowsRoot) { c.gameObject.SetActive(false); Destroy(c.gameObject); }
                _skRows.Clear();
                for (int i = 0; i < entries.Count; i++) _skRows.Add(MakeSkillRow(i, entries[i].max));
                for (int i = 0; i < _skRows.Count; i++)
                {
                    var nav = new Navigation { mode = Navigation.Mode.Explicit };
                    if (i > 0) nav.selectOnUp = _skRows[i - 1].button;
                    if (i < _skRows.Count - 1) nav.selectOnDown = _skRows[i + 1].button;
                    _skRows[i].button.navigation = nav;
                }
                _skBuilt = page;
                _skScroll.verticalNormalizedPosition = 1;
            }
            for (int i = 0; i < entries.Count; i++) FillSkillRow(_skRows[i], entries[i], i == _skFocus);
            if (entries.Count == 0) _skDetail.text = "No upgrades for this robot yet.";
            else ShowSkillDetail(entries[Mathf.Clamp(_skFocus, 0, entries.Count - 1)]);
        }

        SkillRowUi MakeSkillRow(int index, int max)
        {
            var row = new SkillRowUi();
            row.button = MakeButton(_skRowsRoot, "", () => SkillRowClick(index));
            row.button.GetComponent<LayoutElement>().preferredHeight = 32;
            var cols = row.button.colors;
            cols.selectedColor = new Color(0.36f, 0.24f, 0.1f, 1f);   // keyboard / controller selection, quieter than the hover amber
            row.button.colors = cols;
            var t = row.button.transform;
            row.name = row.button.GetComponentInChildren<Text>();
            var nr = row.name.rectTransform;
            nr.anchorMin = Vector2.zero; nr.anchorMax = Vector2.one; nr.offsetMin = new Vector2(16, 0); nr.offsetMax = new Vector2(-372, 0);
            row.name.alignment = TextAnchor.MiddleLeft; row.name.fontSize = 19;
            row.level = Label(t, "Level", 16, UiStyle.InkDim, TextAnchor.MiddleRight, new Vector2(1, 0), Vector2.one, new Vector2(-370, 0), new Vector2(-300, 0));
            row.plate = Rect("Gears", t, new Vector2(1, 0.5f), new Vector2(1, 0.5f), Vector2.zero, Vector2.zero);
            row.plate.pivot = new Vector2(1, 0.5f);
            row.plate.sizeDelta = new Vector2(8 + max * 20, 24);
            row.plate.anchoredPosition = new Vector2(-82, 0);
            var pi = row.plate.gameObject.AddComponent<Image>();
            pi.color = TagPaper; pi.raycastTarget = false;
            for (int g = 0; g < max; g++)
            {
                var gr = Rect("Gear", row.plate, new Vector2(0, 0.5f), new Vector2(0, 0.5f), Vector2.zero, Vector2.zero);
                gr.sizeDelta = new Vector2(18, 18);
                gr.anchoredPosition = new Vector2(4 + 9 + g * 20, 0);
                var gi = gr.gameObject.AddComponent<Image>();
                gi.sprite = GearSprite; gi.raycastTarget = false; gi.color = GearOff;
                row.gears.Add(gi);
            }
            row.chip = Label(t, "Chip", 15, UiStyle.InkDim, TextAnchor.MiddleCenter, new Vector2(1, 0), Vector2.one, new Vector2(-76, 0), new Vector2(-6, 0));
            var mr = Rect("Mark", t, Vector2.zero, new Vector2(0, 1), new Vector2(2, 3), new Vector2(7, -3));
            row.mark = mr.gameObject.AddComponent<Image>();
            row.mark.color = UiStyle.Amber; row.mark.raycastTarget = false;
            var hook = row.button.gameObject.AddComponent<SkillRowHook>();
            hook.hovered = () => SkillFocus(index);
            hook.selected = () => { SkillFocus(index); KeepVisible(index); };
            return row;
        }

        void FillSkillRow(SkillRowUi row, SkillEntry e, bool focus)
        {
            row.name.text = e.name;
            row.level.text = $"{e.have} / {e.max}";
            for (int g = 0; g < row.gears.Count; g++) row.gears[g].color = g < e.have ? GearOn : GearOff;
            bool lacksPoints = e.why.StartsWith("Needs");
            row.chip.text = e.Max ? "MAX" : e.why == "" || lacksPoints ? $"{e.cost} pt" : "LOCKED";
            row.chip.color = e.Max ? GoodC : e.why == "" ? UiStyle.Amber : lacksPoints ? BadC : UiStyle.InkDim;
            row.name.color = e.why == "" || e.Max ? UiStyle.Ink : UiStyle.InkDim;
            row.mark.enabled = focus;
        }

        /// <summary>The detail column follows the hovered or selected row.</summary>
        public void SkillFocus(int index)
        {
            if (index < 0 || index >= _skRows.Count) return;
            _skFocus = index;
            var entries = SkillEntries(SkillPages()[_skPage]);
            for (int i = 0; i < _skRows.Count; i++) _skRows[i].mark.enabled = i == index;
            if (index < entries.Count) ShowSkillDetail(entries[index]);
        }

        void SelectSkillRow(int index)
        {
            if (EventSystem.current != null && index < _skRows.Count) EventSystem.current.SetSelectedGameObject(_skRows[index].button.gameObject);
        }

        /// <summary>A row reached with the keyboard or a controller scrolls into view.</summary>
        void KeepVisible(int index)
        {
            var view = (RectTransform)_skScroll.viewport;
            float content = _skRowsRoot.rect.height, h = view.rect.height;
            if (content <= h + 1) return;
            var r = (RectTransform)_skRows[index].button.transform;
            float top = -r.anchoredPosition.y - r.rect.height / 2, bottom = top + r.rect.height;
            float scroll = (1 - _skScroll.verticalNormalizedPosition) * (content - h);
            if (top < scroll) scroll = top;
            else if (bottom > scroll + h) scroll = bottom - h;
            _skScroll.verticalNormalizedPosition = 1 - Mathf.Clamp01(scroll / (content - h));
        }

        void ShowSkillDetail(SkillEntry e)
        {
            string green = Hex(GoodC), dim = Hex(UiStyle.InkDim), brass = Hex(Brass), bad = Hex(BadC);
            var s = $"<size=26><b>{e.name.ToUpperInvariant()}</b></size>\n<color={brass}>{e.unit} {e.have} / {e.max}</color>\n";
            var now = e.now();
            bool blurbIsEffect = now.Count == 1 && now[0] == e.blurb || e.next().Count == 1 && e.next()[0] == e.blurb;
            if (e.blurb != "" && !blurbIsEffect) s += $"<color={dim}>{e.blurb}</color>\n";
            s += "\n<b>Current</b>\n";
            s += now.Count == 0 ? $"<color={dim}>{(e.unit == "Rank" ? "Not bought yet." : "Not learned yet.")}</color>\n" : string.Join("\n", now) + "\n";
            if (e.Max)
            {
                s += $"\n<color={green}><b>MAX LEVEL REACHED</b></color>";
            }
            else
            {
                var next = e.next();
                s += $"\n<b>Next {e.unit.ToLowerInvariant()} ({e.have + 1})</b>\n";
                s += next.Count == 0 ? "" : string.Join("\n", next.Select(l => now.Contains(l) ? $"<color={dim}>{l}</color>" : $"<color={green}>{l}</color>")) + "\n";
                var pr = _g.progression;
                s += "\n<b>Cost</b>\n";
                if (e.why == "") s += $"{e.cost} skill point{(e.cost == 1 ? "" : "s")}   <color={dim}>(you have {pr.skillPoints})</color>\n<color={Hex(UiStyle.Amber)}>Click to {(e.have == 0 ? (e.unit == "Rank" ? "buy" : "learn") : "upgrade")}.</color>";
                else if (e.why.StartsWith("Needs")) s += $"{e.cost} skill point{(e.cost == 1 ? "" : "s")}   <color={bad}>not enough skill points (you have {pr.skillPoints})</color>";
                else s += $"{e.cost} skill point{(e.cost == 1 ? "" : "s")}   <color={bad}>locked: {e.why}</color>";
            }
            _skDetail.text = s;
        }

        /// <summary>A row clicked: buys the next level, or says why not (no points, locked, maxed).</summary>
        public void SkillRowClick(int index)
        {
            var entries = SkillEntries(SkillPages()[_skPage]);
            if (index < 0 || index >= entries.Count) return;
            var e = entries[index];
            _skFocus = index;
            bool ok = false;
            if (e.Max) _skStatus.text = $"{e.name}: max level reached";
            else if (e.why.StartsWith("Needs")) _skStatus.text = $"Not enough skill points: {e.name} needs {e.cost}, you have {_g.progression.skillPoints}";
            else if (e.why != "") _skStatus.text = $"{e.name} is locked: {e.why}";
            else if (e.buy()) { ok = true; _skStatus.text = e.bought; }
            else _skStatus.text = $"{e.name} could not be bought";
            _skStatus.color = ok ? GoodC : BadC;
            if (!ok) Sfx.Play("reject", null, -4);
            RefreshSkills();
        }

        /// <summary>A small gear (white, tinted per level), made in code once: eight teeth and a hub hole.</summary>
        static Sprite GearSprite
        {
            get
            {
                if (_gearSprite != null) return _gearSprite;
                const int n = 64;
                var t = new Texture2D(n, n, TextureFormat.RGBA32, true) { filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp, name = "SkillGear" };
                var px = new Color[n * n];
                for (int y = 0; y < n; y++)
                    for (int x = 0; x < n; x++)
                    {
                        float dx = (x + 0.5f) / n * 2 - 1, dy = (y + 0.5f) / n * 2 - 1;
                        float r = Mathf.Sqrt(dx * dx + dy * dy), a = Mathf.Atan2(dy, dx);
                        float tooth = Mathf.SmoothStep(0, 1, (Mathf.Cos(a * 8) - 0.15f) * 3f);
                        float outer = Mathf.Lerp(0.72f, 0.97f, tooth);
                        float aa = 1.5f / n * 2;
                        float alpha = Mathf.Clamp01((outer - r) / aa) * Mathf.Clamp01((r - 0.3f) / aa);
                        px[y * n + x] = new Color(1, 1, 1, alpha);
                    }
                t.SetPixels(px);
                t.Apply(true);
                return _gearSprite = Sprite.Create(t, new Rect(0, 0, n, n), new Vector2(0.5f, 0.5f), 100);
            }
        }

        /// <summary>What a skill does at `level` (1-based), one fact per line, for the detail column.</summary>
        List<string> SkillLines(List<SkillDef> chain, int level)
        {
            string Cd(string id) => $"Cooldown: {PlayerTools.Tuned(id, "cooldown_s", level, 0):0} s";
            var s = chain[Mathf.Clamp(level, 1, chain.Count) - 1];
            if (s.effects.ContainsKey("part_sense"))
            {
                int n = (int)PlayerTools.Tuned("part_sense", "parts", level, 1);
                return new List<string> { $"Range: {PlayerTools.Tuned("part_sense", "range_m", level, 2):0} m", $"The nearest {(n == 1 ? "robot part glows" : $"{n} robot parts glow")} for {PlayerTools.Tuned("part_sense", "duration_s", level, 2):0} s", Cd("part_sense") };
            }
            if (s.effects.ContainsKey("inventory_capacity")) return new List<string> { $"Carries {Game.BaseInventory + chain.Take(level).Sum(x => (int)x.effects["inventory_capacity"])} parts" };
            if (s.effects.ContainsKey("sort_inventory")) return new List<string> { "Sorts the inventory by storage area", Cd("sort_inventory") };
            if (s.effects.ContainsKey("storage_delivery"))
            {
                int n = (int)PlayerTools.Tuned("storage_delivery", "max_items", level, level);
                return new List<string> { $"Stores up to {n} matching part{(n == 1 ? "" : "s")} into the storage place you aim at", Cd("storage_delivery") };
            }
            if (s.effects.ContainsKey("magnet_level"))
                return new List<string> { $"Pulls up to {PlayerTools.Tuned("magnetic_glove", "pull_items", level, level):0} part(s)", $"Range: {PlayerTools.Tuned("magnetic_glove", "range_m", level, 1.5f):0.0} m", Cd("magnetic_glove") };
            if (s.effects.ContainsKey("battery_pockets")) return new List<string> { $"+{s.effects["battery_pockets"]:0} battery pockets" };
            return new List<string> { SkillBlurb(s) };
        }
    }

    /// <summary>Hover (mouse) and selection (keyboard, controller) on a skill row: both drive the detail column.</summary>
    public class SkillRowHook : MonoBehaviour, IPointerEnterHandler, ISelectHandler
    {
        public System.Action hovered, selected;
        public void OnPointerEnter(PointerEventData e) => hovered?.Invoke();
        public void OnSelect(BaseEventData e) => selected?.Invoke();
    }
}
