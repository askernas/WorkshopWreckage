using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.UI;

namespace WorkshopWreckage
{
    /// <summary>Modify Robot (WORKSHOP_WRECKAGE_MODIFY_ROBOT_MODULAR_COMPONENTS_SPEC §6-§10), opened from the Robot
    /// Workbench: summon robots to the bench (several at once), cancel, release; inspect a docked robot's
    /// head, arms and propeller, take a module off onto the bench's spare slots, fit a spare, or exchange a
    /// module directly with another docked robot. Modules keep their origin robot's locked colours on any
    /// chassis. Every refused edit says why.</summary>
    public partial class Hud
    {
        RectTransform _mdTabs, _mdSlots, _mdOptions, _mdSummon, _mdActions;
        Text _mdHeader, _mdSpares;
        RawImage _mdPreview;
        string _mdSel;
        ModuleSlot _mdSlot = ModuleSlot.Head;
        readonly HashSet<string> _mdMarked = new();
        float _mdRefreshT;

        /// <summary>QA: which robot the Modify Robot screen shows.</summary>
        public void SelectModify(string robotId, ModuleSlot slot) { _mdSel = robotId; _mdSlot = slot; }

        void BuildModifyScreen()
        {
            var s = Screen("modify", 1500);
            Title(s, "MODIFY ROBOT", 34);
            Para(s, "Summon robots to the workbench, then swap heads, arms and propellers between them. Every module keeps its own robot's colours. A robot missing a module stays docked until it is whole again.", 14, Muted, 22);
            _mdTabs = Row(s, 40, 6);
            var mid = Row(s, 420, 14);
            var pv = new GameObject("Preview", typeof(RectTransform), typeof(RawImage), typeof(LayoutElement));
            pv.transform.SetParent(mid, false);
            pv.GetComponent<LayoutElement>().preferredWidth = 380;
            _mdPreview = pv.GetComponent<RawImage>();
            var centre = Col(mid, 660);
            _mdHeader = Block(centre, 17, 78);
            _mdSlots = Col(centre, 660);
            _mdOptions = Col(centre, 660);
            _mdSpares = Block(Col(mid, 380), 15, 420);
            _mdSummon = Row(s, 40, 8);
            _mdActions = Row(s, 46, 10);
        }

        static string SchemeLabel(string scheme) => scheme switch
        {
            "violet" => "purple", "classic_red" => "classic red", "red_yellow" => "red / yellow", "white_pink" => "white / pink", _ => scheme,
        };

        string ModuleText(RobotModule m) => m == null ? $"<color={Hex(BadC)}>empty</color>" : $"{m.Name} <color={Hex(Muted)}>({SchemeLabel(m.Scheme)})</color>";

        static string StateWord(Robot r, RobotModules mods) =>
            r.Docked ? (mods.Complete(r.id) ? "docked" : "INCOMPLETE")
            : r.mode == Robot.Mode.Summoned || r.SummonPending ? "on its way"
            : r.Active ? "working" : "offline";

        /// <summary>Edits are made on docked robots only.</summary>
        static string NeedDock(Robot r) => r.Docked ? "" : $"{r.displayName} must be docked at the workbench first — summon it";

        void ModifyButton(Transform parent, string text, float width, System.Action cb, bool on = true, int size = 16)
        {
            var b = MakeButton(parent, text, cb);
            b.GetComponent<LayoutElement>().preferredWidth = width;
            b.GetComponentInChildren<Text>().fontSize = size;
            b.interactable = on;
        }

        /// <summary>Carries out an edit, or says why it can't be done.</summary>
        void ModifyEdit(string refusal, System.Func<bool> act, string done, params Robot[] touched)
        {
            if (refusal != "") { OnFeedback(refusal, false); return; }
            if (!act()) { OnFeedback("That didn't fit", false); return; }
            foreach (var r in touched) r.ApplyModules();
            Sfx.Play("servo", null, -8);
            OnFeedback(done, true);
            RefreshModify();
        }

        void RefreshModify()
        {
            var mods = _g.modules;
            if (mods == null || _g.world == null) return;
            var robots = RobotModules.Chassis.Where(mods.Built).Select(id => _g.robots.First(r => r.id == id)).ToList();
            ClearChildren(_mdTabs);
            ClearChildren(_mdSlots);
            ClearChildren(_mdOptions);
            ClearChildren(_mdSummon);
            ClearChildren(_mdActions);
            if (robots.Count == 0)
            {
                _mdHeader.text = "No robots built yet.";
                _mdSpares.text = "";
                ModifyButton(_mdActions, "Back", 200, () => ShowScreenRaw("workbench"), true, 20);
                return;
            }
            _mdMarked.RemoveWhere(id => !robots.Any(r => r.id == id && !r.AtBench && !r.SummonPending));
            var sel = robots.FirstOrDefault(r => r.id == _mdSel) ?? robots.FirstOrDefault(r => r.Docked) ?? robots[0];
            _mdSel = sel.id;
            foreach (var r in robots)
            {
                var robot = r;
                string st = StateWord(r, mods);
                ModifyButton(_mdTabs, (r == sel ? "▶ " : "") + r.displayName + $"  <size=13>{st}</size>", 200, () => { _mdSel = robot.id; RefreshModify(); });
            }

            // the selected robot in the preview, from in front of it
            PreviewCamera(true);
            _mdPreview.texture = _previewRT;
            var at = sel.transform.position + Vector3.up * Mathf.Max(0.18f, sel.Height * 0.55f);
            _previewCam.transform.position = at + sel.transform.forward * 1.05f + Vector3.up * 0.32f + sel.transform.right * 0.3f;
            _previewCam.transform.LookAt(at);

            bool complete = mods.Complete(sel.id);
            _mdHeader.text = $"<size=24><b><color={Hex(Brass)}>{sel.displayName}</color></b></size>    "
                + (sel.Docked ? complete ? $"<color={Hex(GoodC)}>docked — ready to modify</color>" : $"<color={Hex(BadC)}>incomplete — missing {string.Join(", ", mods.Missing(sel.id).Select(RobotModules.SlotName))}</color>"
                              : $"<color={Hex(Muted)}>{sel.Describe().Substring(sel.displayName.Length + 2)}</color>")
                + $"\n<color={Hex(Muted)}>{(!complete ? "not working until it is whole" : mods.Coordinated(sel.id) ? "colour coordinated" : "mixed colours")}  ·  click a slot to choose what goes in it</color>";

            // its slots
            var slots = RobotModules.SlotsOf(sel.id).ToList();
            if (!slots.Contains(_mdSlot)) _mdSlot = slots[0];
            foreach (var slot in slots)
            {
                var sl = slot;
                var m = mods.At(sel.id, slot);
                var row = Row(_mdSlots, 32, 8);
                ModifyButton(row, (slot == _mdSlot ? "▶ " : "") + Cap(RobotModules.SlotName(slot)), 140, () => { _mdSlot = sl; RefreshModify(); });
                var t = Para(row, ModuleText(m), 16, Cream, 32);
                t.alignment = TextAnchor.MiddleLeft;
                t.gameObject.GetComponent<LayoutElement>().preferredWidth = 380;
                if (m != null)
                    ModifyButton(row, "Take off", 120, () => ModifyEdit(NeedDock(sel) is var nd && nd != "" ? nd : mods.CanRemove(sel.id, sl),
                        () => mods.Remove(sel.id, sl) != null, $"{m.Name} is on the bench", sel));
            }

            // what can go in the chosen slot: a spare from the bench, or another docked robot's module
            var fits = mods.Spares.Where(x => RobotModules.Fits(x, sel.id, _mdSlot)).ToList();
            var others = robots.Where(r => r != sel && RobotModules.SlotsOf(r.id).Contains(_mdSlot) && mods.At(r.id, _mdSlot) is RobotModule om && RobotModules.Fits(om, sel.id, _mdSlot)).ToList();
            var head = Para(_mdOptions, $"<b>For its {RobotModules.SlotName(_mdSlot)}:</b>" + (fits.Count + others.Count == 0 ? $"  <color={Hex(Muted)}>nothing on the bench fits; take one off another docked robot</color>" : ""), 15, Cream, 22);
            head.alignment = TextAnchor.MiddleLeft;
            RectTransform line = null;
            int n = 0;
            void Option(string text, System.Action cb)
            {
                if (n++ % 3 == 0) line = Row(_mdOptions, 30, 8);
                ModifyButton(line, text, 210, cb, true, 14);
            }
            foreach (var x in fits)
            {
                var spare = x;
                Option($"Fit {spare.Name}", () => ModifyEdit(NeedDock(sel) is var nd && nd != "" ? nd : mods.CanInstall(spare.id, sel.id),
                    () => mods.Install(spare.id, sel.id, _g.playTime), $"{spare.Name} fitted to {sel.displayName}", sel));
            }
            foreach (var o in others)
            {
                var other = o;
                var om = mods.At(o.id, _mdSlot);
                Option($"Swap for {o.displayName}'s {RobotModules.SlotName(_mdSlot)}", () => ModifyEdit(NeedDock(sel) is var nd && nd != "" ? nd : NeedDock(other) is var no && no != "" ? no : mods.CanSwap(sel.id, other.id, _mdSlot),
                    () => mods.Swap(sel.id, other.id, _mdSlot, _g.playTime), $"{sel.displayName} and {other.displayName} exchanged {RobotModules.SlotName(_mdSlot)}s", sel, other));
            }

            // the bench's spare slots
            var spares = mods.Spares.ToList();
            var sp = $"<b>Spare modules on the bench</b>  <color={Hex(Muted)}>{spares.Count} / {RobotModules.BenchSlots}</color>\n";
            for (int i = 0; i < RobotModules.BenchSlots; i++)
            {
                var x = spares.FirstOrDefault(m => m.BenchSlot == i);
                sp += $"{i + 1,2}.  " + (x == null ? $"<color={Hex(Muted)}>—</color>" : ModuleText(x)) + "\n";
            }
            foreach (var x in mods.Reserved)
                sp += $"<color={Hex(Muted)}>fitted to {RobotModules.OriginName(x.location.Substring(RobotModules.BuildPrefix.Length))}'s assembly:</color> {ModuleText(x)}\n";
            sp += $"<color={Hex(Muted)}>A spare can also go on a robot still being built: Robot Workbench → its page → Fit spare.</color>";
            _mdSpares.text = sp;

            // summons: mark several, then call them all
            var away = robots.Where(r => !r.AtBench && !r.SummonPending && r.Active).ToList();
            var lab = Para(_mdSummon, "Summon:", 16, Cream, 40);
            lab.gameObject.GetComponent<LayoutElement>().preferredWidth = 90;
            foreach (var r in away)
            {
                var robot = r;
                ModifyButton(_mdSummon, (_mdMarked.Contains(r.id) ? "☑ " : "☐ ") + r.displayName, 160, () => { if (!_mdMarked.Remove(robot.id)) _mdMarked.Add(robot.id); RefreshModify(); }, true, 15);
            }
            ModifyButton(_mdSummon, _mdMarked.Count > 0 ? $"Summon marked ({_mdMarked.Count})" : "Summon all", 200, () =>
            {
                var call = _mdMarked.Count > 0 ? away.Where(r => _mdMarked.Contains(r.id)).ToList() : away;
                foreach (var r in call) r.Summon();
                _mdMarked.Clear();
                OnFeedback(call.Count > 0 ? $"{string.Join(", ", call.Select(r => r.displayName))} {(call.Count == 1 ? "is" : "are")} coming to the workbench" : "Every robot is already here", call.Count > 0);
                RefreshModify();
            }, true, 15);

            bool coming = sel.mode == Robot.Mode.Summoned || sel.SummonPending;
            if (coming) ModifyButton(_mdActions, $"Cancel {sel.displayName}'s summons", 300, () => { sel.CancelSummon(); OnFeedback($"{sel.displayName}: back to work", true); RefreshModify(); });
            else if (!sel.AtBench && sel.Active) ModifyButton(_mdActions, $"Summon {sel.displayName}", 260, () => { OnFeedback(sel.Summon(), true); RefreshModify(); });
            ModifyButton(_mdActions, $"Release {sel.displayName}", 260, () =>
            {
                string why = sel.ReleaseFromBench();
                OnFeedback(why == "" ? $"{sel.displayName}: back to work" : why, why == "");
                RefreshModify();
            }, sel.Docked, 18);
            var ready = robots.Where(r => r.Docked && mods.Complete(r.id)).ToList();
            ModifyButton(_mdActions, $"Release all complete ({ready.Count})", 280, () =>
            {
                foreach (var r in ready) r.ReleaseFromBench();
                OnFeedback($"{ready.Count} robot{(ready.Count == 1 ? "" : "s")} back to work", ready.Count > 0);
                RefreshModify();
            }, ready.Count > 0, 18);
            ModifyButton(_mdActions, "Back", 160, () => ShowScreenRaw("workbench"), true, 20);
        }
    }
}
