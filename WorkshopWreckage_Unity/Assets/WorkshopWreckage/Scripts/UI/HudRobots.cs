using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.UI;

namespace WorkshopWreckage
{
    /// <summary>Robot screens: the Robot Workbench (construction, with a live 3D preview of
    /// the robot on its mat), the panel shown while you hold an unfinished robot, and
    /// Robot Management (status, base vs upgraded capabilities, permanent limitations,
    /// the type's upgrade tree, pause / follow / assignment).</summary>
    public partial class Hud
    {
        RectTransform _wbTabs, _wbActions, _rbTabs, _rbActions, _rbUpgrades;
        Text _wbHeader, _wbPartsA, _wbPartsB, _rbInfo, _rbHeader;
        RawImage _wbPreview;
        Camera _previewCam;
        RenderTexture _previewRT;
        string _robotSel;
        float _wbRefreshT, _robotRefreshT;

        static string Hex(Color c) => "#" + ColorUtility.ToHtmlStringRGB(c);
        static string Cap(string s) => string.IsNullOrEmpty(s) ? s : char.ToUpper(s[0]) + s.Substring(1);

        RectTransform Row(Transform parent, float height, float spacing = 8)
        {
            var go = new GameObject("Row", typeof(RectTransform), typeof(HorizontalLayoutGroup), typeof(LayoutElement));
            go.transform.SetParent(parent, false);
            go.GetComponent<LayoutElement>().preferredHeight = height;
            var h = go.GetComponent<HorizontalLayoutGroup>();
            h.spacing = spacing; h.childControlWidth = true; h.childForceExpandWidth = false; h.childControlHeight = true; h.childForceExpandHeight = true;
            return (RectTransform)go.transform;
        }

        RectTransform Col(Transform parent, float width)
        {
            var go = new GameObject("Col", typeof(RectTransform), typeof(VerticalLayoutGroup), typeof(LayoutElement));
            go.transform.SetParent(parent, false);
            go.GetComponent<LayoutElement>().preferredWidth = width;
            var v = go.GetComponent<VerticalLayoutGroup>();
            v.spacing = 6; v.childControlWidth = true; v.childForceExpandWidth = true; v.childControlHeight = true; v.childForceExpandHeight = false;
            return (RectTransform)go.transform;
        }

        Text Block(Transform parent, int size, float height)
        {
            var t = Para(parent, "", size, Cream, height);
            t.alignment = TextAnchor.UpperLeft;
            return t;
        }

        void BuildRobotScreens()
        {
            var wb = Screen("workbench", 1500);
            Title(wb, "ROBOT WORKBENCH", 34);
            _wbTabs = Row(wb, 40, 6);
            var mid = Row(wb, 440, 14);
            var pv = new GameObject("Preview", typeof(RectTransform), typeof(RawImage), typeof(LayoutElement));
            pv.transform.SetParent(mid, false);
            pv.GetComponent<LayoutElement>().preferredWidth = 440;
            _wbPreview = pv.GetComponent<RawImage>();
            var right = Col(mid, 1010);
            _wbHeader = Block(right, 18, 120);
            var parts = Row(right, 310, 12);
            _wbPartsA = Block(Col(parts, 500), 16, 310);
            _wbPartsB = Block(Col(parts, 500), 16, 310);
            _wbActions = Row(wb, 46, 10);
            Para(wb, "Robot parts are delivered by hand: carry them here and click the bench.  [F] at the bench: pick the robot up and turn it in your hands.", 14, Muted, 22);

            var rs = Screen("robots", 1500);
            Title(rs, "ROBOT MANAGEMENT", 34);
            Para(rs, "R: Spiderbot collects the part you aim at (else the nearest robot works on it)  ·  T: work / follow  ·  H: hand your parts to a robot  ·  J: work zone", 14, Muted, 22);
            _rbTabs = Row(rs, 40, 6);
            var body = Row(rs, 520, 16);
            _rbInfo = Block(Col(body, 700), 16, 520);
            var up = Col(body, 740);
            _rbHeader = Block(up, 17, 26);
            _rbUpgrades = (RectTransform)new GameObject("Upgrades", typeof(RectTransform)).transform;
            _rbUpgrades.SetParent(up, false);
            Stack(_rbUpgrades);
            _rbActions = Row(rs, 46, 10);
            MakeButton(rs, "Back", () => ShowScreenRaw(_backTo));
        }

        // ------------------------------------------------------------------ workbench

        void PreviewCamera(bool on)
        {
            if (_previewCam == null)
            {
                _previewRT = new RenderTexture(880, 880, 24) { name = "WorkbenchPreview" };
                var go = new GameObject("WorkbenchPreviewCamera");
                _previewCam = go.AddComponent<Camera>();
                _previewCam.targetTexture = _previewRT;
                _previewCam.fieldOfView = 30;
                _previewCam.nearClipPlane = 0.05f;
                _previewCam.farClipPlane = 30;
                _wbPreview.texture = _previewRT;
            }
            _previewCam.enabled = on;
        }

        void RefreshWorkbench()
        {
            var wb = _g.workbench;
            if (wb == null || _g.world == null) return;
            var p = wb.P(wb.selected) ?? wb.projects[0];
            PreviewCamera(true);
            // frame the robot on its mat, from the front and a little above
            var (spot, _) = wb.MatSpot(p.mat);
            var front = wb.Front;
            var r = wb.RobotOf(p);
            var at = (r != null && r.gameObject.activeSelf && r.mode == Robot.Mode.Assembling ? r.transform.position : spot) + Vector3.up * 0.13f;
            _previewCam.transform.position = at + front * 0.95f + Vector3.up * 0.3f + Vector3.Cross(Vector3.up, front) * 0.25f;
            _previewCam.transform.LookAt(at);

            foreach (Transform c in _wbTabs) Destroy(c.gameObject);
            foreach (var q in wb.projects.Where(q => !q.crafted || wb.Operational(wb.P(q.after)) || wb.Operational(q)))
            {
                var s = wb.StatusOf(q);
                string mark = s == RobotWorkbench.Status.Operational ? "  ✓" : s == RobotWorkbench.Status.AwaitingBlueprint ? $"  (lv {q.level})" : $"  {wb.InstalledCount(q)}/{wb.Required(q)}";
                var b = MakeButton(_wbTabs, (q == p ? "▶ " : "") + q.name + mark, () => { wb.selected = q.id; RefreshWorkbench(); });
                b.GetComponent<LayoutElement>().preferredWidth = 200;
                b.GetComponentInChildren<Text>().fontSize = 17;
            }

            var st = wb.StatusOf(p);
            int lvl = _g.progression.level;
            string bp = wb.Unlocked(p) ? $"Blueprint: <color={Hex(GoodC)}>unlocked</color> (level {p.level})  ·  you are level {lvl}"
                                       : $"Blueprint: <color={Hex(BadC)}>locked</color> — {wb.LockReason(p)}  ·  you are level {lvl}";
            float pct = wb.Completion(p);
            int bars = Mathf.RoundToInt(pct * 24);
            _wbHeader.text = $"<size=26><b><color={Hex(Brass)}>{p.name}</color></b></size>    <color={Hex(st == RobotWorkbench.Status.Operational || st == RobotWorkbench.Status.ReadyForActivation ? GoodC : Cream)}>{RobotWorkbench.StatusText(st)}</color>\n"
                + bp + "\n"
                + $"Assembly  <color={Hex(GoodC)}>{new string('■', bars)}</color><color=#5a4a38>{new string('■', 24 - bars)}</color>  {pct * 100:0}%  ({wb.InstalledCount(p)} / {wb.Required(p)})\n"
                + $"<color={Hex(Muted)}>{wb.Summary(p)}</color>";

            var lines = new List<string>();
            if (p.crafted)
            {
                var miss = wb.MaterialsMissing(p);
                lines.Add($"<b>Recipe</b> <color={Hex(Muted)}>(any part of the category; built in parts count as accounted for in their sets)</color>");
                foreach (var kv in p.materials)
                {
                    int carried = _g.inventory.items.Count(i => _g.world.DefOf(i).CategoryName == kv.Key && !_g.world.DefOf(i).IsRobotPart);
                    int done = kv.Value - miss[kv.Key];
                    lines.Add($"{kv.Key}:  <color={Hex(done >= kv.Value ? GoodC : Cream)}>{done} / {kv.Value} built in</color>" + (carried > 0 && done < kv.Value ? $"   <color={Hex(Brass)}>you carry {carried}</color>" : ""));
                }
            }
            else
            {
                var have = wb.InstalledKeys(p);
                foreach (var id in wb.PartIds(p))
                {
                    var key = Robot.PartKey(_g.world.DefOf(id));
                    var ps = wb.StateOf(id);
                    string why = ps == RobotWorkbench.PartState.Delivered ? wb.CanInstall(p, id) : "";
                    string txt = ps switch
                    {
                        RobotWorkbench.PartState.Installed => $"<color={Hex(GoodC)}>installed</color>",
                        RobotWorkbench.PartState.Delivered => $"<color={Hex(Brass)}>in the holding tray</color>" + (why != "" ? $" <color={Hex(Muted)}>— {why.ToLower()}</color>" : " — <b>ready to install</b>"),
                        RobotWorkbench.PartState.Carried => $"<color={Hex(Cream)}>in your hands — deliver it</color>",
                        RobotWorkbench.PartState.Discovered => "<color=#d98a4a>discovered, still in the workshop</color>",
                        _ => $"<color=#8a7a6a>missing — not found yet</color>",
                    };
                    lines.Add($"{Cap(key)}:  {txt}");
                }
            }
            int half = (lines.Count + 1) / 2;
            _wbPartsA.text = string.Join("\n", lines.Take(half));
            _wbPartsB.text = string.Join("\n", lines.Skip(half));

            foreach (Transform c in _wbActions) Destroy(c.gameObject);
            int installable = p.crafted ? 0 : wb.DeliveredIds(p).Count(id => wb.CanInstall(p, id) == "");
            var ib = MakeButton(_wbActions, installable > 0 ? $"Install delivered parts ({installable})" : "Install delivered parts", () =>
            {
                int n = wb.InstallAll(p);
                OnFeedback(n > 0 ? $"Installed {n} part{(n == 1 ? "" : "s")} on {p.name}" : wb.Summary(p), n > 0);
                RefreshWorkbench();
            });
            ib.interactable = installable > 0;
            ib.GetComponent<LayoutElement>().preferredWidth = 360;
            var ab = MakeButton(_wbActions, $"Activate {p.name}", () =>
            {
                if (wb.Activate(p)) { _g.SetState("play"); Toast($"{p.name} is starting up…", true); }
            });
            ab.interactable = wb.CanActivate(p);
            ab.GetComponent<LayoutElement>().preferredWidth = 300;
            if (Unlocks.DebugMode && !wb.Operational(p))
            {
                var db = MakeButton(_wbActions, "DEBUG: unlock now", () =>
                {
                    string msg = wb.DebugUnlock(p);
                    if (wb.RobotOf(p) is Robot r && r.mode == Robot.Mode.Activating) { _g.SetState("play"); Toast(msg, true); }
                    else { OnFeedback(msg, wb.Complete(p)); RefreshWorkbench(); }
                });
                db.GetComponent<LayoutElement>().preferredWidth = 240;
                db.GetComponentInChildren<Text>().color = new Color(1f, 0.55f, 0.35f);
            }
            var cb = MakeButton(_wbActions, "Close", () => _g.SetState("play"));
            cb.GetComponent<LayoutElement>().preferredWidth = 200;
        }

        /// <summary>The info panel while an unfinished robot is in your hands.</summary>
        public void ShowRobotInspect(Robot r)
        {
            _inspect.gameObject.SetActive(r != null);
            _title.enabled = _sub.enabled = _hint.enabled = r == null;
            if (r == null) return;
            var wb = _g.workbench;
            var p = wb.P(r.id);
            var t = $"<size=24><b><color={Hex(Brass)}>{p.name}</color></b></size>\n<color={Hex(Muted)}>{RobotWorkbench.StatusText(wb.StatusOf(p))}  ·  {wb.InstalledCount(p)}/{wb.Required(p)} installed</color>\n\n";
            if (!wb.Unlocked(p)) t += $"<color={Hex(BadC)}>{wb.LockReason(p)}.</color>\n\n";
            if (p.crafted)
                t += "Still needed: " + string.Join(", ", wb.MaterialsMissing(p).Where(kv => kv.Value > 0).Select(kv => $"{kv.Value} {kv.Key}")) + "\n";
            else
            {
                var missing = wb.PartIds(p).Where(i => wb.StateOf(i) != RobotWorkbench.PartState.Installed).ToList();
                if (missing.Count == 0) t += $"<color={Hex(GoodC)}>Complete — activate it at the workbench.</color>\n";
                else
                {
                    t += "<b>Still to fit</b> (the ghosts show where):\n";
                    foreach (var i in missing)
                    {
                        var s = wb.StateOf(i);
                        string where = s == RobotWorkbench.PartState.Delivered ? "in the tray" : s == RobotWorkbench.PartState.Carried ? "in your hands" : s == RobotWorkbench.PartState.Discovered ? "seen in the workshop" : "not found yet";
                        t += $"  {Cap(Robot.PartKey(_g.world.DefOf(i)))}  <color={Hex(Muted)}>— {where}</color>\n";
                    }
                }
            }
            t += $"\n<color=#9a8c74>[Mouse / Q E] turn   [Wheel] zoom\n[LMB] install delivered parts   [F / RMB] put it back</color>";
            _inspectText.text = t;
        }

        // ------------------------------------------------------------------ robot management

        void RefreshRobots()
        {
            var types = _g.robots.Where(r => r.Operational).Select(r => r.type).Distinct().ToList();
            foreach (Transform c in _rbTabs) Destroy(c.gameObject);
            foreach (Transform c in _rbUpgrades) Destroy(c.gameObject);
            foreach (Transform c in _rbActions) Destroy(c.gameObject);
            if (types.Count == 0)
            {
                _rbHeader.text = "";
                _rbInfo.text = "No robots built yet.\n\nFind a robot's parts, carry them to the Robot Workbench and install them once its blueprint is unlocked (Retrobot: level 2). Each robot's upgrades appear here once it is operational.";
                return;
            }
            if (!types.Contains(_robotSel)) _robotSel = types[0];
            foreach (var t in types)
            {
                int n = _g.robots.Count(r => r.type == t && r.Operational);
                var name = _g.robots.First(r => r.type == t).displayName.Split('#')[0].Trim();
                var b = MakeButton(_rbTabs, (t == _robotSel ? "▶ " : "") + name + (n > 1 ? $" ×{n}" : ""), () => { _robotSel = t; RefreshRobots(); });
                b.GetComponent<LayoutElement>().preferredWidth = 230;
            }
            var units = _g.robots.Where(r => r.type == _robotSel && r.Operational).ToList();
            var rb = units[0];
            string F(float v, string unit, int dec = 2) => v.ToString(dec == 0 ? "0" : dec == 1 ? "0.0" : "0.00") + unit;
            string Stat(string label, float b, float now, string unit, string k, int dec = 2)
            {
                float lim = rb.LimitOf(k);
                string up = Mathf.Abs(now - b) > 1e-3f ? $"  →  <color={Hex(GoodC)}>{F(now, unit, dec)}</color>" : "";
                return $"{label}:  {F(b, unit, dec)}{up}" + (float.IsInfinity(lim) ? "" : $"  <color={Hex(Muted)}>(never above {F(lim, unit, dec)})</color>") + "\n";
            }
            var t2 = "";
            foreach (var u in units)
            {
                t2 += $"<b><color={Hex(Brass)}>{u.displayName}</color></b>  —  {u.Describe().Substring(u.displayName.Length + 2)}\n";
                t2 += $"   battery {u.Battery * 100:0}%  ·  stored {u.delivered}, staged {u.staged}" + (u.paused ? "  ·  <color=#d98a4a>PAUSED</color>" : "") + (u.zoneCenter.HasValue ? "  ·  zone" : "")
                    + (u.strategy == "assigned_definition" ? $"\n   assignment: {(u.assignedDef >= 0 ? _g.db.Get(u.assignedDef).displayName + (u.autoAssigned ? " (its own choice)" : "") : "none")}  ·  {(u.specialistState == "" ? "WORKING" : u.specialistState)}" : "") + "\n";
            }
            t2 += $"\n<b>Capabilities</b>  <color={Hex(Muted)}>base  →  upgraded</color>\n";
            t2 += Stat("Speed", rb.BaseStat("walk_speed"), rb.WalkSpeed, " m/s", "walk_speed");
            t2 += Stat("Carrying capacity", rb.BaseStat("capacity", 1), rb.Capacity, " parts", "capacity", 0);
            t2 += Stat("Part kinds per trip", rb.BaseStat("item_types", 1), rb.ItemTypes, "", "item_types", 0);
            if (rb.CanClimb) t2 += Stat("Climbs up to", rb.BaseStat("climb_height"), rb.ReachHeight, " m", "climb_height");
            else t2 += Stat("Reach height", rb.BaseStat("reach_height"), rb.ReachHeight, " m", "reach_height");
            t2 += Stat("Largest part it lifts", rb.BaseStat("max_item_size"), rb.MaxItemSize, " m", "max_item_size");
            string terrain = rb.locomotion == "hover" ? "flies over heaps and furniture" : rb.CanClimb ? $"crosses heaps up to {rb.HeapStep * 100:0} cm; climbs benches and shelving" : $"flat floor; heaps over {rb.HeapStep * 100:0} cm are obstacles";
            t2 += $"Terrain:  {terrain}\n";
            t2 += $"Storage:  {(rb.CanDirectStore ? $"stores into destinations up to {rb.ReachHeight:0.00} m, higher ones go to the staging zone" : "never stores — always leaves parts in the staging zone")}\n";
            t2 += $"\n<b>Strengths</b>\n   {string.Join("\n   ", rb.Strengths)}\n<b>Permanent limitations</b>\n   <color=#d98a4a>{string.Join("\n   ", rb.Limitations)}</color>";
            _rbInfo.text = t2;

            var pr = _g.progression;
            _rbHeader.text = $"<b>Upgrades</b>   ·   {pr.skillPoints} skill point{(pr.skillPoints == 1 ? "" : "s")} to spend" + (units.Count > 1 ? $"   ·   shared by all {units.Count}" : "");
            foreach (var u in pr.robotUpgrades.Where(u => u.robot == _robotSel))
            {
                int rank = pr.RankOf(u.id);
                string why = pr.CanBuyUpgrade(u.id, true);
                string state = rank >= u.maxRank ? "MAX" : why == "" ? $"upgrade ({u.CostOf(rank)} pt)" : why;
                var b = MakeButton(_rbUpgrades, $"{u.name}   {new string('●', rank)}{new string('○', u.maxRank - rank)}   —   {state}", () => { if (_g.BuyRobotUpgrade(u.id)) RefreshRobots(); });
                b.interactable = why == "";
                b.GetComponent<LayoutElement>().preferredHeight = 30;
                b.GetComponentInChildren<Text>().fontSize = 18;
                var d = Para(_rbUpgrades, u.description, 13, Muted, 18);
                d.alignment = TextAnchor.UpperLeft;
            }

            foreach (var u in units)
            {
                var robot = u;
                string tag = units.Count > 1 ? " " + u.displayName.Replace(rb.displayName.Split('#')[0].Trim(), "").Trim() : "";
                Btn(robot.paused ? "Resume" + tag : "Pause" + tag, 170, () => { robot.paused = !robot.paused; RefreshRobots(); });
                Btn((robot.mode == Robot.Mode.Following ? "Work" : "Follow me") + tag, 170, () => { robot.ToggleFollow(); RefreshRobots(); });
                if (robot.zoneCenter.HasValue) Btn("Clear zone" + tag, 170, () => { robot.AssignZone(null); RefreshRobots(); });
                if (robot.strategy == "assigned_definition" && robot.assignedDef >= 0 && !robot.autoAssigned) Btn("Clear assignment", 220, () => { robot.Assign(-1, false); RefreshRobots(); });
            }
            if (pr.level >= pr.respecLevel && pr.RanksBought > 0)
                Btn("Refund robot upgrades", 280, () => { int n = pr.RefundRobotUpgrades(); OnFeedback($"{n} skill points refunded", true); RefreshRobots(); });
        }

        void Btn(string text, float width, System.Action cb)
        {
            var b = MakeButton(_rbActions, text, cb);
            b.GetComponent<LayoutElement>().preferredWidth = width;
            b.GetComponentInChildren<Text>().fontSize = 18;
        }
    }
}
