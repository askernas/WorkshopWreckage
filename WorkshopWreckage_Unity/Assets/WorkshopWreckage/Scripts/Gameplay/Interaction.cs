using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace WorkshopWreckage
{
    public struct TargetInfo
    {
        public string title, sub, hint;
        public int tone;   // 1 good, -1 bad, 0 neutral
    }

    /// <summary>Aiming, pickup with settling, inspection, carrying and storing
    /// (port of InteractionController.gd). All actions change real ItemWorld state.</summary>
    public class Interaction : MonoBehaviour
    {
        public const float BaseReach = 2.7f;
        public float reachBonus;                      // from skills (progression.json: reach_m)
        public float Reach => BaseReach + reachBonus;
        const float BaseRepeat = 0.13f;
        float Repeat => BaseRepeat / (1f + (float)(game.progression?.Effect("pickup_speed") ?? 0));   // Quick Hands
        static readonly Color OutlineItem = new(1f, 0.8f, 0.35f), Good = new(0.45f, 1f, 0.55f), Bad = new(1f, 0.4f, 0.3f);

        public Game game;
        public bool enabledInput = true;
        public string targetType = "none";
        public SortingArm targetArm;   // targetType "arm": one standing on the floor
        public int targetItem = -1;
        /// <summary>Robot part afterglow (playtest 2026-10-26): a robot part blinks once after the crosshair leaves it,
        /// so it is easier to spot again. It was the skill "part_afterglow" (playtest 2026-10-28), retired in the skill
        /// revamp (2026-10-30): it can no longer be bought, so this is off; Part Sense's 2 s glow replaces it. The
        /// Settings preference (PlayerPrefs "ww_part_afterglow") is kept but hidden.</summary>
        public static bool Afterglow = PlayerPrefs.GetInt("ww_part_afterglow", 1) == 1;
        public bool AfterglowOn => Afterglow && game.progression != null && game.progression.Has("part_afterglow");
        public const float AfterglowTime = 0.9f;
        static readonly Color AfterglowColor = new(0.35f, 0.85f, 1f);
        public int afterglowId = -1;
        float _afterglowT;
        int _lastHover = -1;
        Material _afterglowMat;
        public string targetDest;
        /// <summary>targetType "station": the robot part in the parts pool tray under the crosshair (-1: none).</summary>
        public int targetPoolPart = -1;
        public StorageUnit targetUnit;
        public bool inspecting;
        /// <summary>The unfinished robot taken off the workbench to look at (null = none).</summary>
        public Robot inspectRobot;
        public Vector3 targetPoint;
        public event Action<Robot> RobotInspectChanged;
        public int inspectWorldId = -1;
        public double lastActionMicros;
        public readonly HashSet<int> busy = new();
        public string targetSocket;
        /// <summary>The pegboard slot aimed at (targetType "peg").</summary>
        public string targetPeg;
        public event Action<TargetInfo> TargetChanged;
        public event Action<string, bool> Feedback;
        public event Action<bool, ItemDefinition> InspectChanged;

        GameObject _outline, _destBox, _ghost;
        Material _outlineMat, _destMat, _ghostMat;
        float _repeatT;

        ItemWorld World => game.world;
        Inventory Inv => game.inventory;
        StorageSystem Storage => game.storage;

        public void Init(Game g)
        {
            game = g;
            _outlineMat = new Material(EnvMaterials.Outline);
            _outline = new GameObject("Outline");
            _outline.AddComponent<MeshFilter>();
            var mr = _outline.AddComponent<MeshRenderer>();
            mr.shadowCastingMode = ShadowCastingMode.Off;
            _outline.SetActive(false);
            _afterglowMat = new Material(EnvMaterials.Outline);
            _afterglowMat.SetColor("_Color", AfterglowColor);
            _destMat = EnvMaterials.NewGhost();
            _destBox = GameObject.CreatePrimitive(PrimitiveType.Cube);
            Destroy(_destBox.GetComponent<Collider>());
            _destBox.GetComponent<MeshRenderer>().sharedMaterial = _destMat;
            _destBox.GetComponent<MeshRenderer>().shadowCastingMode = ShadowCastingMode.Off;
            _destBox.SetActive(false);
            _ghostMat = EnvMaterials.NewGhost();
            _ghost = new GameObject("Ghost");
            _ghost.AddComponent<MeshFilter>();
            _ghost.AddComponent<MeshRenderer>().shadowCastingMode = ShadowCastingMode.Off;
            _ghost.SetActive(false);
        }

        public void ResetState()
        {
            if (inspecting) EndInspect();
            busy.Clear();
            targetType = "none";
        }

        static void SetMatrix(Transform t, Matrix4x4 m)
        {
            t.position = m.GetColumn(3);
            t.rotation = m.rotation;
            t.localScale = m.lossyScale;
        }

        // ---------------------------------------------------------------- per frame

        void Update()
        {
            if (game == null || game.world == null) return;
            DrawAfterglow();
            var kb = Keyboard.current;
            var mouse = Mouse.current;
            if (!enabledInput) { HideHover(); return; }
            bool lmbDown = mouse != null && mouse.leftButton.wasPressedThisFrame && PlayerController.ClickReady;
            bool lmbHeld = mouse != null && mouse.leftButton.isPressed;
            float wheel = mouse != null ? mouse.scroll.ReadValue().y : 0;
            if (inspecting)
            {
                HideHover();
                if (lmbDown && inspectWorldId >= 0) { int id = inspectWorldId; EndInspect(); Pickup(id, false); }
                else if (mouse != null && mouse.rightButton.wasPressedThisFrame && inspectRobot != null) EndInspect();
                else if (kb.fKey.wasPressedThisFrame) EndInspect();
                else if (kb.rKey.wasPressedThisFrame) game.held.autoRotate = !game.held.autoRotate;
                if (wheel != 0) game.held.Zoom(wheel > 0 ? 0.15f : -0.15f);
                float turn = (kb.qKey.isPressed ? 1 : 0) - (kb.eKey.isPressed ? 1 : 0);
                if (turn != 0) game.held.RotateBy(new Vector2(-turn * 240f * Time.deltaTime, 0));
                if (mouse != null && PlayerController.LookReady) game.held.RotateBy(mouse.delta.ReadValue() * new Vector2(1, -1));
                return;
            }
            // two hands on the Part Radar (PartRadar reads its own keys): no parts in or out
            if (game.radar != null && game.radar.isOut) { HideHover(); return; }
            UpdateTarget();
            // two hands on the Sorting Arm: no parts in or out until it is mounted or set down
            if (game.CarriedArm is SortingArm carriedArm)
            {
                // one action: on a mount it is mounted, anywhere else it is set down there
                if (lmbDown)
                {
                    if (targetType == "socket") Primary();
                    else SetArmDown(carriedArm, true);
                }
                if (kb.zKey.wasPressedThisFrame) SetArmDown(carriedArm, false);
                return;
            }
            _repeatT -= Time.deltaTime;
            if (lmbDown) { _repeatT = Repeat * 2.5f; Primary(); }   // one part per placement: the one in hand
            else if (lmbHeld && _repeatT <= 0) { _repeatT = Repeat; Primary(true); }
            if (kb.gKey.wasPressedThisFrame) DepositAll();
            // X take off / V apply: only in workbench mode now (playtest 2026-10-28), on the bench screen
            if (kb.fKey.wasPressedThisFrame) BeginInspect();
            if (kb.zKey.wasPressedThisFrame) DropHeld();
            if (PickerActive)
            {
                // at a robot on its mat the wheel and Q / E scroll through the found parts that fit it
                if (wheel < 0 || kb.eKey.wasPressedThisFrame) Pick(1);
                if (wheel > 0 || kb.qKey.wasPressedThisFrame) Pick(-1);
            }
            else
            {
                if (wheel < 0) Inv.Cycle(1);
                if (wheel > 0) Inv.Cycle(-1);
            }
            game.player.turnKeysTaken = PickerActive;
            // the number keys use skills (PlayerTools); the wheel picks the slot
        }

        (Vector3 o, Vector3 d) Ray()
        {
            var c = game.player.cam.transform;
            return (c.position, c.forward);
        }

        public void UpdateTarget()
        {
            var (o, dir) = Ray();
            var hits = Physics.RaycastAll(o, dir, Reach, Layers.AimMask, QueryTriggerInteraction.Collide);
            Array.Sort(hits, (a, b) => a.distance.CompareTo(b.distance));
            RaycastHit? hit = null;
            float envT = Reach;
            foreach (var h in hits)
            {
                if (h.collider.GetComponent<AimPassThrough>() != null || h.collider.transform.parent?.GetComponent<AimPassThrough>() != null) continue;
                hit ??= h;
                // only solid geometry hides items: destination volumes must not, or
                // unsorted parts lying on shelves and pallets could never be aimed at
                if (h.collider.gameObject.layer == Layers.Env) { envT = h.distance; break; }
            }
            var (pid, pdist) = game.index.Pick(o, dir, envT + 0.03f, busy);
            targetType = "none";
            targetItem = -1;
            targetDest = null;
            targetUnit = null;
            targetPeg = null;
            targetPoolPart = -1;
            // a pile in front of a drawer must not stop you storing into it: while you hold a part
            // that belongs right there, aiming through the pile (within 0.8 m of it) selects the drawer
            if (pid >= 0 && hit.HasValue && Inv.HeldId() >= 0 && hit.Value.distance - pdist < 0.8f
                && hit.Value.collider.GetComponent<StorageTarget>() is StorageTarget aimed && !aimed.station && aimed.unit != null && aimed.destId != null
                && game.db.boxes.ContainsKey(aimed.destId) && Storage.Check(aimed.destId, Inv.HeldId()) == StorageSystem.Result.Ok)
                pid = -1;
            // a Sorting Arm standing on the floor, in front of whatever lies behind it
            targetArm = null;
            if (game.CarriedArm == null)
            {
                float best = Mathf.Min(envT, pid >= 0 ? pdist : Reach);
                foreach (var r in game.robots)
                    if (r.arm is SortingArm a && a.place == SortingArm.Place.Floor && r.Operational && a.FloorBounds().IntersectRay(new Ray(o, dir), out float d) && d < best) { best = d; targetArm = a; }
            }
            if (targetArm != null) targetType = "arm";
            else if (pid >= 0) { targetType = "item"; targetItem = pid; }
            else if (PegAim(o, dir, envT, hit) is string peg) { targetType = "peg"; targetPeg = peg; }
            else if (hit.HasValue)
            {
                var st = hit.Value.collider.GetComponent<StorageTarget>();
                if (st != null && st.station)
                {
                    targetType = "station";
                    targetPoint = game.workbench.BenchPoint(o, dir, hit.Value.point);
                    if (game.workbench.AtPool(targetPoint)) targetPoolPart = game.workbench.PoolPartAt(o, dir);
                }
                else if (st != null && st.destId == "TRAY") targetType = "tray";
                else if (st != null && st.destId == "EXIT") targetType = "exit";
                else if (st != null && st.destId.StartsWith("SOCKET:")) { targetType = "socket"; targetSocket = st.destId.Substring(7); }
                else if (st != null) { targetType = "dest"; targetDest = st.destId; targetUnit = st.unit; }
            }
            RefreshPicker();
            NoteHover();
            ShowHover();
            TargetChanged?.Invoke(Describe());
        }

        // ---------------------------------------------------------------- the part picker (playtest 2026-10-28)

        /// <summary>Found parts that fit the robot aimed at on its mat (hands and parts pool, one of each kind).</summary>
        public readonly List<int> fitChoices = new();
        int _pickId = -1, _pickIdx, _pickHeld = -1;
        RobotWorkbench.Project _pickProj;
        /// <summary>The part the picker shows (LMB tries it on), or -1.</summary>
        public int PickedPart => fitChoices.Count > 0 && _pickIdx < fitChoices.Count ? fitChoices[_pickIdx] : -1;
        public bool PickerActive => targetType == "station" && fitChoices.Count > 0;

        void RefreshPicker()
        {
            fitChoices.Clear();
            // playtest 2026-10-28: robots are only changed in workbench mode (LMB on the bench), so no in-world picker
            _pickProj = null;
        }

        /// <summary>Scrolls the picker by one, round the list.</summary>
        public void Pick(int step)
        {
            if (fitChoices.Count == 0) return;
            _pickIdx = ((_pickIdx + step) % fitChoices.Count + fitChoices.Count) % fitChoices.Count;
            _pickId = fitChoices[_pickIdx];
            Sfx.Play("ui_click", null, -12);
            TargetChanged?.Invoke(Describe());
        }

        /// <summary>The pegboard slot under the crosshair (docs/PEGBOARD.md), unless something nearer is in the way.
        /// Carrying a board tool, its own slot wins whenever the crosshair is near it; carrying a set piece,
        /// whenever the crosshair is on its holder (the holder's whole region, mounted pieces included).</summary>
        string PegAim(Vector3 o, Vector3 dir, float envT, RaycastHit? hit)
        {
            var pb = game.builder?.pegboard;
            if (pb == null) return null;
            int held = Inv.HeldId();
            string prefer = held >= 0 && World.DefOf(held).IsPegboardTool ? World.DefOf(held).PegSlot : null;
            var slot = pb.SlotUnderRay(new Ray(o, dir), Mathf.Min(envT + 0.15f, Reach), out float d, prefer);
            if (slot == null || hit.HasValue && hit.Value.distance < d - 0.12f) return null;
            return slot;
        }

        /// <summary>The tool in hand if it belongs in this slot, else -1. The inventory is never searched for a tool
        /// that fits (placement rules 2026-10-30, docs/PEGBOARD.md).</summary>
        int HeldToolFor(string slot)
        {
            int held = Inv.HeldId();
            return held >= 0 && World.DefOf(held).storageBox == Pegboard.PegboardConfig.DestPrefix + slot ? held : -1;
        }

        /// <summary>The crosshair left a robot part lying in the world: it blinks once.</summary>
        void NoteHover()
        {
            int now = targetType == "item" ? targetItem : -1;
            if (now >= 0 && now == afterglowId) afterglowId = -1;   // back on it: the normal highlight shows
            if (_lastHover >= 0 && _lastHover != now && AfterglowOn && _lastHover < World.Count
                && World.instances[_lastHover].state == ItemState.InWorld && World.DefOf(_lastHover).IsRobotPart)
            { afterglowId = _lastHover; _afterglowT = Time.time; }
            _lastHover = now;
        }

        /// <summary>One blink: the rim swells and fades over AfterglowTime.</summary>
        void DrawAfterglow()
        {
            if (afterglowId < 0) return;
            float k = (Time.time - _afterglowT) / AfterglowTime;
            if (!AfterglowOn || k >= 1 || afterglowId >= World.Count || World.instances[afterglowId].state != ItemState.InWorld) { afterglowId = -1; return; }
            float s = Mathf.Sin(k * Mathf.PI);
            _afterglowMat.SetFloat("_Width", 2.5f * s);
            _afterglowMat.SetFloat("_Glow", 9f * s);
            var mesh = game.renderer.WorldMeshFor(afterglowId);
            var rp = new RenderParams(_afterglowMat) { shadowCastingMode = ShadowCastingMode.Off };
            for (int n = 0; n < mesh.subMeshCount; n++) Graphics.RenderMesh(rp, mesh, n, World.instances[afterglowId].transform);
        }

        void HideHover()
        {
            _outline.SetActive(false);
            _destBox.SetActive(false);
            _ghost.SetActive(false);
        }

        void ShowHover()
        {
            HideHover();
            game.builder?.pegboard?.Highlight(targetType == "peg" && !game.builder.pegboard.IsOccupied(targetPeg) && HeldToolFor(targetPeg) >= 0 ? targetPeg : null);
            if (targetType == "item")
            {
                var inst = World.instances[targetItem];
                var mesh = game.renderer.WorldMeshFor(targetItem);
                _outline.GetComponent<MeshFilter>().sharedMesh = mesh;
                var mats = new Material[mesh.subMeshCount];
                for (int i = 0; i < mats.Length; i++) mats[i] = _outlineMat;
                _outline.GetComponent<MeshRenderer>().sharedMaterials = mats;
                SetMatrix(_outline.transform, inst.transform);
                _outlineMat.SetColor("_Color", OutlineItem);
                _outline.SetActive(true);
            }
            else if (targetType == "dest") ShowDestHover(targetDest);
        }

        void ShowDestHover(string dest)
        {
            int held = Inv.HeldId();
            bool ok = held >= 0 && Storage.Check(dest, held) == StorageSystem.Result.Ok;
            var col = ok ? Good : held >= 0 ? Bad : OutlineItem;
            _destMat.SetColor("_Color", new Color(col.r, col.g, col.b, 0.28f));
            var d = game.db.boxes[dest];
            var u = targetUnit;
            if (u.type == "drawer_cabinet")
            {
                int slot = u.SlotOf(dest);
                var m = u.FrontWorld(slot);
                _destBox.transform.position = m.GetColumn(3);
                _destBox.transform.rotation = u.transform.rotation;
                _destBox.transform.localScale = new Vector3(u.front.x + 0.01f, u.front.y + 0.01f, 0.035f);
            }
            else
            {
                var inner = StorageUnit.Inner(d);
                var kind = Json.Str(d, "kind");
                float hgt = kind == "shelf_section" || kind == "floor_pallet" ? Mathf.Min(inner.y, 0.3f) : inner.y;
                var m = u.DestWorld(dest) * Matrix4x4.Translate(new Vector3(0, hgt / 2, 0));
                _destBox.transform.position = m.GetColumn(3);
                _destBox.transform.rotation = u.transform.rotation;
                _destBox.transform.localScale = new Vector3(inner.x + 0.03f, hgt + 0.02f, inner.z + 0.03f);
            }
            _destBox.SetActive(true);
            if (ok && game.db.IsOpenKind(dest))
            {
                var mesh = game.renderer.WorldMeshFor(held);
                _ghost.GetComponent<MeshFilter>().sharedMesh = mesh;
                var mats = new Material[mesh.subMeshCount];
                for (int i = 0; i < mats.Length; i++) mats[i] = _ghostMat;
                _ghost.GetComponent<MeshRenderer>().sharedMaterials = mats;
                _ghostMat.SetColor("_Color", new Color(0.5f, 1f, 0.6f, 0.5f));
                SetMatrix(_ghost.transform, game.StoragePlacement(dest, held, Storage.Stored(dest).Count));
                _ghost.SetActive(true);
            }
        }

        public TargetInfo Describe()
        {
            int held = Inv.HeldId();
            if (game.CarriedArm != null && targetType != "socket")
                return new TargetInfo { title = "Carrying the Sorting Arm", sub = "Both hands — nothing else can be picked up",
                    hint = "[LMB] Set it down here  ·  aim at an overhead rail mount (green lamp) to mount it", tone = 0 };
            switch (targetType)
            {
                case "arm":
                    return Inv.Parts > 0
                        ? new TargetInfo { title = "Sorting Arm", sub = targetArm.Describe(), hint = "It takes both hands — put away the parts you carry first", tone = -1 }
                        : new TargetInfo { title = "Sorting Arm", sub = targetArm.Describe(), hint = "[LMB] Pick it up (both hands)", tone = 1 };
                case "item":
                {
                    var def = World.DefOf(targetItem);
                    bool known = game.progress.IsDiscovered(def.id);
                    return new TargetInfo { title = known ? def.displayName : def.shape + "  (unidentified)", sub = def.CategoryName,
                        hint = !Inv.HasRoomFor(targetItem) ? (Inv.isBattery(targetItem) ? "Battery pockets full" : "Hands full — sort what you carry") : "[LMB] Pick up    [F] Inspect" };
                }
                case "dest":
                {
                    var title = game.db.BoxLabel(targetDest) + "  ·  " + AssignedName(targetDest);
                    int strays = game.StraysIn(targetDest).Count;
                    var sub = $"{Storage.Stored(targetDest).Count} / {game.AssignedCount(targetDest)} stored" + (strays > 0 ? $"  ·  {strays} stray part{(strays == 1 ? "" : "s")} in the way" : "");
                    if (held < 0) return new TargetInfo { title = title, sub = sub, hint = "" };
                    var r = Storage.Check(targetDest, held);
                    if (r == StorageSystem.Result.Ok) return new TargetInfo { title = title, sub = sub, hint = "[LMB] Store here", tone = 1 };
                    return new TargetInfo { title = title, sub = sub, hint = Storage.ResultText(r, targetDest, held), tone = -1 };
                }
                case "peg":
                {
                    var pb = game.builder.pegboard;
                    var tool = pb.cfg.toolById[pb.cfg.slotById[targetPeg].tool];
                    var title = "Tool board  ·  " + tool.name;
                    var sub = $"{pb.Hung} / {pb.Total} tools hung" + (pb.IsOccupied(targetPeg) ? "  ·  in its place" : "  ·  this slot is empty");
                    if (pb.IsOccupied(targetPeg)) return new TargetInfo { title = title, sub = sub, hint = "" };
                    if (pb.WaitsForHolder(targetPeg) && HeldToolFor(targetPeg) >= 0)
                        return new TargetInfo { title = title, sub = sub, hint = MountHolderFirst(targetPeg), tone = -1 };
                    if (HeldToolFor(targetPeg) >= 0)
                    {
                        var on = pb.HolderOf(targetPeg) is string h ? " on the " + pb.cfg.toolById[pb.cfg.slotById[h].tool].name.ToLowerInvariant() : "";
                        return new TargetInfo { title = title, sub = sub, hint = "[LMB] " + pb.cfg.S("interaction", "prompt", "Hang {tool}").Replace("{tool}", tool.name) + on, tone = 1 };
                    }
                    if (held >= 0)
                        return new TargetInfo { title = title, sub = sub, hint = pb.cfg.S("interaction", "wrong_prompt", "{tool} doesn't belong here").Replace("{tool}", World.DefOf(held).displayName), tone = -1 };
                    return new TargetInfo { title = title, sub = sub, hint = $"Find the {tool.name.ToLowerInvariant()} to hang it here", tone = 0 };
                }
                case "tray":
                {
                    int n = Inv.items.Count(i => !World.DefOf(i).IsRobotPart);
                    return new TargetInfo { title = "Sorting Tray", sub = $"{game.TrayCount} parts waiting · the robots sort what you put here",
                        hint = n > 0 ? $"[LMB] Put the held part in    [G] Put all {n} in" : "Drop any parts here for the robots", tone = n > 0 ? 1 : 0 };
                }
                case "exit":
                    return new TargetInfo { title = "Workshop door", sub = "Out to the yard — leaving ends the game", hint = "[LMB] Leave the workshop", tone = 0 };
                case "socket":
                {
                    if (!game.sockets.TryGetValue(targetSocket, out var sk)) break;
                    var arm = sk.occupant;
                    string sub = $"Serves {sk.Label}" + (arm != null ? $"  ·  {arm.Describe()}" : "  ·  empty");
                    if (game.CarriedArm != null)
                        return arm == null ? new TargetInfo { title = "Sorting Arm socket " + sk.id, sub = sub, hint = "[LMB] Mount the Sorting Arm here", tone = 1 }
                                           : new TargetInfo { title = "Sorting Arm socket " + sk.id, sub = sub, hint = "Taken — find an empty socket (green lamp)", tone = -1 };
                    return new TargetInfo { title = "Sorting Arm socket " + sk.id, sub = sub,
                        hint = arm != null ? "[LMB] Detach the arm and carry it" : "Build a Sorting Arm at the Robot Workbench to mount here", tone = 0 };
                }
                case "station":
                {
                    var wb = game.workbench;
                    var p = wb.ActiveOnMat(MatAt(targetPoint));
                    var parts = StationParts();
                    int fitted = wb.FittedIds(p).Count;
                    string sub = $"{p.name}: {RobotWorkbench.StatusText(wb.StatusOf(p))}  ·  {wb.InstalledCount(p)}/{wb.Required(p)} installed"
                        + (fitted > 0 ? $" ({fitted} fitted, not committed)" : "") + (wb.Unlocked(p) ? "" : $"  ·  {wb.LockReason(p)}");
                    // playtest 2026-10-28: LMB anywhere on the bench opens workbench mode (blueprints, then the parts);
                    // what you carry for the robots goes into the parts box first, so it is there to fit
                    string open = "[LMB] Open the Robot Workbench" + (parts.Count > 0 ? $" (puts the {parts.Count} part{(parts.Count == 1 ? "" : "s")} you carry in the parts box)" : "");
                    if (wb.AtPool(targetPoint))
                        return targetPoolPart >= 0
                            ? new TargetInfo { title = World.DefOf(targetPoolPart).displayName, sub = $"In the parts box  ·  {wb.FitsText(targetPoolPart)}", hint = open, tone = 1 }
                            : new TargetInfo { title = "Robot Workbench  ·  parts box", sub = sub, hint = open, tone = 1 };
                    string look = wb.RobotOf(p) is Robot r && r.mode == Robot.Mode.Assembling && r.gameObject.activeSelf ? "    [F] Pick up and look" : "";
                    return new TargetInfo { title = "Robot Workbench", sub = sub, hint = open + look, tone = 1 };
                }
            }
            return new TargetInfo();
        }

        string AssignedName(string dest)
        {
            var names = game.db.order.Where(id => game.db.definitions[id].storageBox == dest)
                .Select(id => game.progress.IsDiscovered(id) ? game.db.definitions[id].displayName : "unidentified part").ToList();
            return names.Count > 0 ? string.Join(", ", names) : "empty slot";
        }

        // ---------------------------------------------------------------- actions

        /// <summary>The held part if it is a robot part (-1: none).</summary>
        int HeldRobotPart() => Inv.HeldId() is int h && h >= 0 && World.DefOf(h).IsRobotPart ? h : -1;

        /// <summary>repeat: LMB held down (fitting and taking out of the pool happen once per click).</summary>
        public void Primary(bool repeat = false)
        {
            switch (targetType)
            {
                case "item": Pickup(targetItem, true); break;
                case "arm": targetArm?.PickUp(); break;
                case "dest":
                    int held = Inv.HeldId();
                    // held down, the next part in the order is stored only if it fits too: no reject per repeat
                    if (held >= 0 && repeat && Storage.Check(targetDest, held) != StorageSystem.Result.Ok) break;
                    if (held >= 0) Deposit(targetDest, held);
                    else if (targetUnit.type == "drawer_cabinet") OpenDrawer(targetUnit, targetDest);
                    break;
                case "station":
                {
                    var wb = game.workbench;
                    // playtest 2026-10-28: a click on the bench opens workbench mode; robots are changed only there.
                    // Carried robot parts, materials and batteries go into the parts box first.
                    if (repeat) break;
                    DepositAll();
                    game.OpenWorkbench(wb.ActiveOnMat(MatAt(targetPoint)).id);
                    break;
                }
                case "tray":
                    if (Inv.HeldId() >= 0) DropInTray(Inv.HeldId());
                    break;
                case "peg":
                {
                    var pb = game.builder.pegboard;
                    if (pb.IsOccupied(targetPeg)) break;
                    int tool = HeldToolFor(targetPeg);   // only the tool in hand; the next one comes up in the inventory order
                    if (repeat && (tool < 0 || pb.WaitsForHolder(targetPeg))) break;   // held down: no reject per repeat
                    if (tool >= 0) HangTool(tool);
                    else if (Inv.HeldId() >= 0)
                    {
                        Feedback?.Invoke(pb.cfg.S("interaction", "wrong_prompt", "{tool} doesn't belong here").Replace("{tool}", World.DefOf(Inv.HeldId()).displayName), false);
                        Sfx.Play("reject", null, -4);
                    }
                    break;
                }
                case "exit":
                    game.SetState("exit");
                    break;
                case "socket":
                    if (!game.sockets.TryGetValue(targetSocket, out var sk)) break;
                    if (game.CarriedArm is SortingArm ca)
                    {
                        string why = ca.Mount(sk);
                        Feedback?.Invoke(why == "" ? $"Sorting Arm mounted on {sk.id} — it sorts the staging piles below {sk.Label}" : why, why == "");
                    }
                    else if (sk.occupant != null) sk.occupant.PickUp();
                    break;
            }
        }

        public bool Pickup(int id, bool animate)
        {
            long t0 = System.Diagnostics.Stopwatch.GetTimestamp();
            var inst = World.instances[id];
            if (inst.state != ItemState.InWorld) return false;
            if (game.CarriedArm != null)
            {
                Feedback?.Invoke("Both hands on the Sorting Arm — mount it or set it down first", false);
                return false;
            }
            if (!Inv.HasRoomFor(id))
            {
                Feedback?.Invoke(Inv.isBattery(id) ? $"Your battery pockets are full ({Inv.Batteries}/{Inv.batteryPockets})"
                                                   : $"Your hands are full ({Inv.Parts}/{Inv.capacity}) — store what you carry first", false);
                Sfx.Play("reject", null, -6);
                return false;
            }
            var from = inst.transform;
            var def = World.DefOf(id);
            game.index.Remove(id);
            World.SetState(id, ItemState.Carried, "player");
            Inv.Add(id);
            Inv.Select(def.id);
            if (animate) StartCoroutine(Fly(id, from, game.held.HandWorld(), 0.16f, null));
            game.tools?.OnManualPickup(id);   // Handful: nearby copies come along
            SettleNeighbours(from, def);
            if (!def.IsRobotPart) Sfx.Play(ItemDatabase.Extents(def)[0] > 0.24f ? "pickup_heavy" : "pickup", from.GetColumn(3), -2);   // a robot part has its own (Game)
            lastActionMicros = (System.Diagnostics.Stopwatch.GetTimestamp() - t0) * 1e6 / System.Diagnostics.Stopwatch.Frequency;
            return true;
        }

        public bool Deposit(string dest, int id)
        {
            var r = Storage.Check(dest, id);
            if (r != StorageSystem.Result.Ok)
            {
                Feedback?.Invoke(Storage.ResultText(r, dest, id), false);
                Sfx.Play("reject", null, -4);
                return false;
            }
            var def = World.DefOf(id);
            Inv.Remove(id);
            Storage.Deposit(dest, id);
            var d = game.db.boxes[dest];
            var unit = game.units[Json.Str(d, "unit")];
            if (unit.type == "drawer_cabinet")
            {
                OpenDrawer(unit, dest);
                StartCoroutine(Fly(id, game.held.HandWorld(), unit.FrontWorld(unit.SlotOf(dest)) * Matrix4x4.Translate(new Vector3(0, 0.05f, -0.25f)) * Matrix4x4.Scale(Vector3.one * def.worldScale), 0.18f, null));
            }
            else
            {
                game.renderer.SetHidden(id, true);
                busy.Add(id);
                StartCoroutine(Fly(id, game.held.HandWorld(), World.instances[id].transform, 0.2f, () => { busy.Remove(id); game.renderer.SetHidden(id, false); }));
            }
            Sfx.Play("drop_drawer", World.instances[id].Position, -3);
            Feedback?.Invoke($"Stored: {def.displayName}  →  {Json.Str(d, "slot")}", true);
            unit.RefreshLabels();
            return true;
        }

        /// <summary>Puts a carried part into the sorting tray: it lies there unsorted
        /// until a robot picks it up (robots take tray parts first).</summary>
        /// <summary>Where a part lands in the sorting tray: on its bottom, or on the parts already there.</summary>
        public Matrix4x4 TrayMatrix(ItemDefinition def)
        {
            var r = game.TrayRectU;
            var p = new Vector3(UnityEngine.Random.Range(r.xMin + 0.1f, r.xMax - 0.1f), 0, UnityEngine.Random.Range(r.yMin + 0.1f, r.yMax - 0.1f));
            var e = ItemDatabase.Extents(def);
            float rest = WorkshopLayout.TrayFloor;
            foreach (var o in game.index.QuerySphere(p + Vector3.up * 0.1f, Mathf.Max(e[0], e[1]) * 0.5f + 0.05f))
                if (World.instances[o].state == ItemState.InWorld && !busy.Contains(o)) rest = Mathf.Max(rest, game.index.WorldBounds(o).max.y);
            return Matrix4x4.TRS(new Vector3(p.x, rest + e[2] * 0.5f + 0.003f, p.z),
                Quaternion.Euler(UnityEngine.Random.Range(-10f, 10f) + (def.restMode == RestMode.Side ? 90 : 0), UnityEngine.Random.Range(0, 360f), def.restMode == RestMode.Long ? 90 : 0), Vector3.one * def.worldScale);
        }

        /// <summary>Hangs a carried board tool in its own slot (only the player hangs tools; it can only go
        /// where its silhouette is): stored in the slot's destination, drawn by the board from then on.</summary>
        public bool HangTool(int id)
        {
            var def = World.DefOf(id);
            var pb = game.builder?.pegboard;
            if (pb == null || !def.IsPegboardTool) return false;
            var dest = def.storageBox;
            // a set's piece goes on its holder's place, so the holder has to be up first (docs/PEGBOARD.md "Tool sets")
            if (pb.WaitsForHolder(def.PegSlot)) { Feedback?.Invoke(MountHolderFirst(def.PegSlot), false); Sfx.Play("reject", null, -4); return false; }
            var r = Storage.Check(dest, id);
            if (r != StorageSystem.Result.Ok) { Feedback?.Invoke(Storage.ResultText(r, dest, id), false); Sfx.Play("reject", null, -4); return false; }
            Inv.Remove(id);
            Storage.Deposit(dest, id);   // the board hangs it (Game: Deposited)
            Sfx.Play(pb.cfg.S("interaction", "sound", "peg_hang"), pb.SlotCentre(def.PegSlot), -3);
            Feedback?.Invoke($"Hung: {def.displayName}  ({pb.Hung}/{pb.Total} on the tool board)", true);
            return true;
        }

        /// <summary>"Mount the metric socket holder first." for a set piece's slot.</summary>
        string MountHolderFirst(string slot)
        {
            var cfg = game.builder.pegboard.cfg;
            var holder = cfg.toolById[cfg.slotById[cfg.slotById[slot].parent].tool];
            return $"Mount the {holder.name.ToLowerInvariant()} first.";
        }

        public bool DropInTray(int id, bool feedback = true)
        {
            var def = World.DefOf(id);
            if (def.IsRobotPart) { if (feedback) Feedback?.Invoke("Robot parts go to the Robot Workbench — carry them there yourself", false); return false; }
            if (def.IsPegboardTool) { if (feedback) Feedback?.Invoke("Tools go on the tool board — only you can hang them", false); return false; }
            var m = TrayMatrix(def);
            var p = (Vector3)m.GetColumn(3);
            var from = game.held.HandWorld();
            Inv.Remove(id);
            World.SetTransform(id, m);
            World.SetState(id, ItemState.InWorld, "tray");
            game.index.Insert(id);
            busy.Add(id);
            game.renderer.SetHidden(id, true);
            StartCoroutine(Fly(id, from, m, 0.25f, () => { busy.Remove(id); game.renderer.SetHidden(id, false); }));
            Sfx.Play("drop_floor", p, -6);
            if (feedback) Feedback?.Invoke($"In the sorting tray: {def.displayName}", true);
            return true;
        }

        /// <summary>The player hands a carried part over at the Robot Workbench (in person:
        /// this is only reachable while aiming at the bench).</summary>
        public bool DepositStation(int id)
        {
            var (ok, text) = game.workbench.Deliver(id);
            Feedback?.Invoke(text, ok);
            if (!ok) Sfx.Play("reject", null, -4);
            return ok;
        }

        /// <summary>Workbench mat (robot type index) under a world point on the bench.</summary>
        public int MatAt(Vector3 world)
        {
            var l = game.workbench.transform.InverseTransformPoint(world);
            float x = Batch.G(l).x;
            return Mathf.Clamp(Mathf.FloorToInt((x + RobotWorkbench.Bench.x / 2) / (RobotWorkbench.Bench.x / RobotWorkbench.Types.Length)), 0, RobotWorkbench.Types.Length - 1);
        }

        /// <summary>F at the workbench: lift the unfinished robot off its mat and turn it in
        /// your hands (mouse); you stay at the bench until you put it back (F / RMB / Esc).</summary>
        public bool BeginRobotInspect(Robot r)
        {
            if (r == null || r.mode != Robot.Mode.Assembling || !r.gameObject.activeSelf) return false;
            inspecting = true;
            inspectWorldId = -1;
            inspectRobot = r;
            game.held.BeginInspectObject(r.ModelTransform, r.Height);
            game.player.lookEnabled = false;
            game.player.inputEnabled = false;
            Sfx.Play("inspect", null, -8);
            RobotInspectChanged?.Invoke(r);
            return true;
        }

        void Report((bool ok, string text) r)
        {
            if (!string.IsNullOrEmpty(r.text)) Feedback?.Invoke(r.text, r.ok);
            if (!r.ok) Sfx.Play("reject", null, -4);
        }

        /// <summary>X at a robot on the bench: its last fitted (uncommitted) part comes off into your hands.</summary>
        void TakeOffLast()
        {
            if (targetType != "station" || game.workbench.AtPool(targetPoint)) return;
            var wb = game.workbench;
            var p = wb.ActiveOnMat(MatAt(targetPoint));
            var f = wb.FittedIds(p);
            if (f.Count == 0) { Feedback?.Invoke($"Nothing on {p.name} is loose: applied parts stay on", false); return; }
            Report(wb.TakeOff(p, f[^1]));
        }

        /// <summary>V at a robot on the bench: Apply (every fitted part locked in place).</summary>
        public void ApplyAimed()
        {
            if (targetType != "station" || game.workbench.AtPool(targetPoint)) return;
            var wb = game.workbench;
            var p = wb.ActiveOnMat(MatAt(targetPoint));
            int n = wb.Commit(p);
            if (n == 0) { Feedback?.Invoke($"No fitted parts on {p.name} to apply", false); return; }
            Feedback?.Invoke($"{p.name}: {n} part{(n == 1 ? "" : "s")} applied ({wb.InstalledCount(p)}/{wb.Required(p)})" + (wb.CanActivate(p) ? " — ready to activate" : ""), true);
        }

        /// <summary>G: hand everything over at the workbench, empty into the sorting tray (separate work stations).
        /// A storage place and the pegboard have no G (placement rules 2026-10-30): one click places the part or tool
        /// in hand; Automatic Storage Delivery stores several parts.</summary>
        public void DepositAll()
        {
            if (targetType == "dest")
                Feedback?.Invoke("No Store all: [LMB] stores the part in your hand (Automatic Storage Delivery stores several)", false);
            else if (targetType == "peg")
                Feedback?.Invoke("No Hang all: [LMB] hangs the tool in your hand", false);
            else if (targetType == "station")
            {
                int n = StationParts().Count(DepositStation);
                if (n > 1) Feedback?.Invoke($"Delivered {n} parts to the workbench", true);
            }
            else if (targetType == "tray")
            {
                int n = Inv.items.ToList().Count(id => DropInTray(id, false));
                if (n > 0) Feedback?.Invoke($"{n} parts in the sorting tray — the robots will sort them", true);
            }
        }

        /// <summary>The placement check shared by a click and Automatic Storage Delivery: a part in hand, the crosshair
        /// on a storage place within reach (the aim ray is reach long), and that place takes the part in hand (its
        /// own place, room, nothing in the way: StorageSystem.Check). Nothing else in the inventory is looked at.</summary>
        public StorageSystem.Result CanPlaceHeld(out int held)
        {
            held = Inv.HeldId();
            if (held < 0 || targetType != "dest" || string.IsNullOrEmpty(targetDest)) return StorageSystem.Result.UnknownBox;
            return Storage.Check(targetDest, held);
        }

        /// <summary>For Automatic Storage Delivery: the part in hand first, then other carried parts of the same kind
        /// wherever they are in the inventory, at most `max` and no more than `dest` has room for.</summary>
        public List<int> SameKindAsHeld(string dest, int held, int max)
        {
            int def = World.instances[held].defId;
            var l = new List<int> { held };
            l.AddRange(Inv.items.Where(id => id != held && World.instances[id].defId == def));
            int room = game.db.BoxCapacity(dest) - Storage.Stored(dest).Count;
            return l.Take(Mathf.Max(0, Mathf.Min(max, room))).ToList();
        }

        /// <summary>What the workbench would take from your hands: robot parts, and recipe
        /// materials for a crafted robot under construction (the held one first).</summary>
        public List<int> StationParts()
        {
            // robot parts and recipe materials, and Part Radar parts and spent batteries (a battery with charge is kept)
            var o = Inv.items.Where(id => game.workbench.ProjectFor(id) != null
                                          || World.DefOf(id).IsToolItem && (!World.DefOf(id).IsRadarBattery || game.radar.Charges(id) == 0)).ToList();
            int held = Inv.HeldId();
            if (o.Remove(held)) o.Insert(0, held);
            return o;
        }

        /// <summary>Sets the carried Sorting Arm down folded on the floor: where the crosshair meets
        /// the floor (aimed, within reach), else just in front of the player; never on the player's
        /// feet, inside furniture or under a unit.</summary>
        public void SetArmDown(SortingArm arm, bool aimed)
        {
            var (o, dir) = Ray();
            var me = game.player.transform.position;
            var flat = Vector3.ProjectOnPlane(dir, Vector3.up);
            flat = flat.sqrMagnitude > 1e-4f ? flat.normalized : game.player.transform.forward;
            var want = me + flat * 0.8f;
            if (aimed && dir.y < -0.05f && -o.y / dir.y <= Reach + 0.5f) want = o + dir * (-o.y / dir.y);   // the floor plane
            var away = Vector3.ProjectOnPlane(want - me, Vector3.up);
            if (away.magnitude < 0.6f) want = me + (away.sqrMagnitude > 1e-4f ? away.normalized : flat) * 0.6f;
            var p = game.SafeDropPoint(new Vector3(want.x, 0, want.z), me);
            // blocked in front (a bench, a unit): the open floor beside or behind the player, never at the feet
            foreach (float turn in new[] { 45f, -45f, 90f, -90f, 135f, -135f, 180f })
            {
                if (Vector3.ProjectOnPlane(p - me, Vector3.up).magnitude >= 0.5f) break;
                var q = game.SafeDropPoint(me + Quaternion.Euler(0, turn, 0) * flat * 0.8f, me);
                if (Vector3.ProjectOnPlane(q - me, Vector3.up).magnitude > Vector3.ProjectOnPlane(p - me, Vector3.up).magnitude) p = q;
            }
            arm.SetDown(new Vector3(p.x, 0, p.z), Quaternion.LookRotation(-flat, Vector3.up));
            Sfx.Play("servo", p, -6);
            Feedback?.Invoke("Sorting Arm set down — click it to pick it up again", true);
        }

        public void DropHeld()
        {
            int id = Inv.HeldId();
            if (id < 0) return;
            var def = World.DefOf(id);
            var (o, dir) = Ray();
            // never onto a unit's top or into furniture: open floor (or a low deck: bench, pallet,
            // tray, open shelf up to 1.25 m) on the way back towards the player
            Vector3 p = game.SafeDropPoint(o + dir * 1.2f, game.player.transform.position);
            var b = MeshLibrary.UnitBounds(def.geometryKey).size * def.worldScale;
            var rot = Quaternion.Euler(0, UnityEngine.Random.value * 360, 0);
            float halfH = b.y / 2;
            if (def.restMode == RestMode.Long) { rot *= Quaternion.Euler(0, 0, 90); halfH = b.x / 2; }
            else if (def.restMode == RestMode.Side) { rot *= Quaternion.Euler(90, 0, 0); halfH = b.z / 2; }   // falls flat, not onto an edge
            // let go just above whatever already lies there; it falls and tumbles onto the pile (PileSim)
            float top = Mathf.Max(p.y, game.piles.TopAt(p, Mathf.Max(b.x, b.z) * 0.5f));
            rot = Quaternion.Euler(UnityEngine.Random.Range(-20f, 20f), 0, UnityEngine.Random.Range(-20f, 20f)) * rot;
            var m = Matrix4x4.TRS(new Vector3(p.x, top + Mathf.Max(b.x, b.y, b.z) * 0.5f + 0.02f, p.z), rot, Vector3.one * def.worldScale);
            Inv.Remove(id);
            World.SetTransform(id, m);
            World.SetState(id, ItemState.InWorld, "world");
            game.index.Insert(id);
            game.renderer.SetHidden(id, true);
            busy.Add(id);
            var push = new Vector3(dir.x, 0, dir.z).normalized * 0.06f;
            StartCoroutine(Fly(id, game.held.HandWorld(), m, 0.25f, () =>
            {
                busy.Remove(id);
                game.renderer.SetHidden(id, false);
                Sfx.Play("drop_floor", p, -4);
                if (World.instances[id].state == ItemState.InWorld) game.piles.Drop(id, push);
            }));
        }

        // ---------------------------------------------------------------- inspection

        public void BeginInspect()
        {
            if (targetType == "station") { BeginRobotInspect(game.workbench.RobotOf(game.workbench.ActiveOnMat(MatAt(targetPoint)))); return; }
            ItemDefinition def = null;
            if (targetType == "item")
            {
                inspectWorldId = targetItem;
                def = World.DefOf(inspectWorldId);
                game.renderer.SetHidden(inspectWorldId, true);
                busy.Add(inspectWorldId);
            }
            else if (Inv.HeldId() >= 0) { inspectWorldId = -1; def = World.DefOf(Inv.HeldId()); }
            if (def == null) return;
            inspecting = true;
            game.progress.Discover(def.id);
            game.held.BeginInspect(def);
            game.player.lookEnabled = false;
            game.player.inputEnabled = false;
            Sfx.Play("inspect", null, -8);
            InspectChanged?.Invoke(true, def);
        }

        public void EndInspect()
        {
            if (!inspecting) return;
            inspecting = false;
            if (inspectRobot != null)
            {
                game.held.EndInspectObject();
                inspectRobot.RefreshAssembly();
                inspectRobot = null;
                RobotInspectChanged?.Invoke(null);
            }
            if (inspectWorldId >= 0) { busy.Remove(inspectWorldId); game.renderer.SetHidden(inspectWorldId, false); }
            inspectWorldId = -1;
            game.held.EndInspect();
            game.player.lookEnabled = true;
            game.player.inputEnabled = true;
            InspectChanged?.Invoke(false, null);
        }

        // ---------------------------------------------------------------- helpers

        /// <summary>Tool pulls (magnet, Supermagnet, sweep) leave a glowing trail.</summary>
        public void StartFly(int id, Matrix4x4 from, Matrix4x4 to, float dur) => StartCoroutine(Fly(id, from, to, dur, null, true));

        static Material _trailMat;

        readonly Dictionary<int, int> _flight = new();   // instance -> its latest flight (a part caught again mid-air)
        int _flights;

        /// <summary>A part flies from one place to another. The world renderer draws it on the way, so it looks just
        /// as it does where it lands (its world mesh, skin and wear: playtest 2026-10-28, a dropped part showed the
        /// old look in the air); the pegboard's tools, drawn elsewhere, fly as a stand-in model.</summary>
        IEnumerator Fly(int id, Matrix4x4 from, Matrix4x4 to, float dur, Action done, bool trail = false)
        {
            var def = World.DefOf(id);
            int flight = _flight[id] = ++_flights;
            bool own = game.renderer.Fly(id, from);
            var go = own ? new GameObject("Flight_" + def.id) : HeldView.MakeModel(def);
            if (trail)
            {
                var tr = go.AddComponent<TrailRenderer>();
                if (_trailMat == null) { _trailMat = EnvMaterials.NewGhost(); _trailMat.SetColor("_Color", new Color(0.45f, 0.8f, 1f, 0.6f)); }   // additive glow
                tr.sharedMaterial = _trailMat;
                tr.time = 0.25f;
                tr.startWidth = 0.035f;
                tr.endWidth = 0f;
                tr.startColor = new Color(0.6f, 0.9f, 1f, 0.9f);
                tr.endColor = new Color(0.3f, 0.6f, 1f, 0f);
                tr.shadowCastingMode = ShadowCastingMode.Off;
            }
            var p0 = (Vector3)from.GetColumn(3); var p1 = (Vector3)to.GetColumn(3);
            var q0 = from.rotation; var q1 = to.rotation;
            var s0 = from.lossyScale; var s1 = to.lossyScale;
            float lift = Mathf.Clamp(Vector3.Distance(p0, p1) * 0.25f, 0.05f, 0.35f);
            float t = 0;
            while (t < 1)
            {
                t = Mathf.Min(1, t + Time.deltaTime / dur);
                float e = t * t * (3 - 2 * t);
                go.transform.position = Vector3.Lerp(p0, p1, e) + Vector3.up * Mathf.Sin(Mathf.PI * t) * lift;
                go.transform.rotation = Quaternion.Slerp(q0, q1, e);
                go.transform.localScale = Vector3.Lerp(s0, s1, e);
                if (own && _flight.GetValueOrDefault(id) == flight) game.renderer.Fly(id, go.transform.localToWorldMatrix);
                yield return null;
            }
            if (own && _flight.GetValueOrDefault(id) == flight) { _flight.Remove(id); game.renderer.Fly(id, null); }
            Destroy(go);
            done?.Invoke();
        }

        /// <summary>Nothing floats: after a part is taken away, what rested on or against it
        /// tumbles down onto real support (PileSim); parts on those follow.</summary>
        public void SettleNeighbours(Matrix4x4 removed, ItemDefinition removedDef) =>
            game.piles.Disturb(removed.GetColumn(3), HeldView.Radius(removedDef));

        /// <summary>Height the part would rest on: the furniture surface or floor below it,
        /// or the top of a part under its footprint (parts already falling count at their
        /// new height; a lower neighbour it already leans into holds it where it is).</summary>
        public float SupportUnder(int id, Bounds b, Dictionary<int, float> drops)
        {
            float s = game.SurfaceBelow(b.center, b.min.y + 0.03f);
            var core = new Rect(b.center.x - b.extents.x * 0.6f, b.center.z - b.extents.z * 0.6f, b.extents.x * 1.2f, b.extents.z * 1.2f);
            foreach (var m in game.index.QuerySphere(new Vector3(b.center.x, b.min.y - 0.1f, b.center.z), Mathf.Max(b.extents.x, b.extents.z) + 0.25f))
            {
                if (m == id || World.instances[m].state != ItemState.InWorld) continue;
                var mb = game.index.WorldBounds(m);
                if (drops.TryGetValue(m, out var d)) mb.center -= Vector3.up * d;
                else if (busy.Contains(m)) continue;
                if (mb.center.y >= b.center.y - 0.005f) continue;   // beside or above, not under
                if (!core.Overlaps(new Rect(mb.min.x, mb.min.z, mb.size.x, mb.size.z))) continue;
                s = Mathf.Max(s, Mathf.Min(mb.max.y, b.min.y));
            }
            return s;
        }

        void OpenDrawer(StorageUnit unit, string dest)
        {
            var d = game.db.boxes[dest];
            var contents = new List<(Mesh, Material[], Matrix4x4)>();
            var ids = Storage.Stored(dest);
            for (int k = 0; k < ids.Count; k++)
            {
                var def = World.DefOf(ids[k]);
                var e = MeshLibrary.Get(def.geometryKey, MeshLibrary.LodInspect);
                contents.Add((e.mesh, ItemAppearance.SlotMaterials(e, ItemAppearance.For(def)), unit.TrayContentLocal(def, k, d)));
            }
            unit.OpenDrawer(unit.SlotOf(dest), contents);
        }
    }

    /// <summary>Held item in the lower right and the LOD 0 inspection model
    /// (port of HeldItemView.gd), with depth-of-field while inspecting.</summary>
    public class HeldView : MonoBehaviour
    {
        static readonly Vector3 HandPos = new(0.24f, -0.2f, 0.46f), InspectPos = new(0, 0, 0.38f);
        const float HandSize = 0.05f, InspectSize = 0.11f;
        public Camera cam;
        public bool autoRotate = true;
        GameObject _hand, _inspect;
        Transform _pivot;
        int _handDef = -1;
        float _zoom = 1, _t;
        Volume _dof;

        public static float Radius(ItemDefinition def) => MeshLibrary.UnitBounds(def.geometryKey).size.magnitude * 0.5f * def.worldScale;

        public static GameObject MakeModel(ItemDefinition def)
        {
            var e = MeshLibrary.Get(def.geometryKey, MeshLibrary.LodInspect);
            var go = new GameObject("Model_" + def.id);
            go.AddComponent<MeshFilter>().sharedMesh = e.mesh;
            var mr = go.AddComponent<MeshRenderer>();
            mr.sharedMaterials = ItemAppearance.SlotMaterials(e, ItemAppearance.For(def));
            mr.shadowCastingMode = ShadowCastingMode.Off;
            return go;
        }

        public void Init(Camera c)
        {
            cam = c;
            transform.SetParent(c.transform, false);
            _pivot = new GameObject("InspectPivot").transform;
            _pivot.SetParent(transform, false);
            _pivot.localPosition = InspectPos;
            var key = _key = new GameObject("InspectKey").AddComponent<Light>();
            key.type = LightType.Point;
            key.range = 0.8f;
            key.intensity = 0.8f;
            key.transform.SetParent(_pivot, false);
            key.transform.localPosition = new Vector3(0.18f, 0.2f, -0.1f);
            var vgo = new GameObject("InspectDOF");
            vgo.transform.SetParent(transform, false);
            _dof = vgo.AddComponent<Volume>();
            _dof.isGlobal = true;
            _dof.priority = 50;
            _dof.weight = 0;
            var p = ScriptableObject.CreateInstance<VolumeProfile>();
            var dof = p.Add<DepthOfField>(true);
            dof.mode.Override(DepthOfFieldMode.Gaussian);
            dof.gaussianStart.Override(0.6f);
            dof.gaussianEnd.Override(1.0f);
            dof.gaussianMaxRadius.Override(1.5f);
            _dof.profile = p;
        }

        public Matrix4x4 HandWorld() => cam.transform.localToWorldMatrix * Matrix4x4.Translate(HandPos);

        public void ShowHeld(ItemDefinition def)
        {
            if (def == null) { if (_hand) Destroy(_hand); _hand = null; _handDef = -1; return; }
            if (def.id == _handDef) { if (_hand) _hand.SetActive(_inspect == null); return; }
            if (_hand) Destroy(_hand);
            _handDef = def.id;
            _hand = MakeModel(def);
            _hand.transform.SetParent(transform, false);
            float r = Mathf.Max(0.001f, Radius(def) / def.worldScale);
            var rot = Quaternion.Euler(30, -35, 0) * (def.restMode == RestMode.Long ? Quaternion.Euler(0, 0, -63) : Quaternion.identity);
            _hand.transform.localRotation = rot;
            _hand.transform.localScale = Vector3.one * (HandSize / r);
            _hand.transform.localPosition = HandPos + new Vector3(0, -0.08f, 0);
            _hand.SetActive(_inspect == null);
        }

        public void BeginInspect(ItemDefinition def)
        {
            _zoom = 1;
            autoRotate = true;
            if (_inspect) Destroy(_inspect);
            _inspect = MakeModel(def);
            _inspect.transform.SetParent(_pivot, false);
            float r = Mathf.Max(0.001f, Radius(def) / def.worldScale);
            _inspect.transform.localScale = Vector3.one * (InspectSize / r);
            _pivot.localRotation = Quaternion.Euler(-30, 0, def.restMode == RestMode.Long ? -35 : 0);
            _pivot.localScale = Vector3.one * 0.3f;
            // a robot part keeps the robot's own materials, which the close key bleaches (playtest 2026-10-29):
            // lit like a whole robot in the hands
            if (MeshLibrary.Get(def.geometryKey, MeshLibrary.LodInspect).materials != null) RobotKey();
            if (_hand) _hand.SetActive(false);
            _dof.weight = 1;
        }

        public void EndInspect()
        {
            if (_inspect) Destroy(_inspect);
            _inspect = null;
            PartKey();
            if (_hand) _hand.SetActive(true);
            _dof.weight = 0;
        }

        void RobotKey()
        {
            _key.transform.localPosition = new Vector3(0.45f, 0.5f, -0.45f);
            _key.range = 2.2f;
            _key.intensity = 0.5f;
        }

        void PartKey()
        {
            _key.transform.localPosition = new Vector3(0.18f, 0.2f, -0.1f);
            _key.range = 0.8f;
            _key.intensity = 0.8f;
        }

        Light _key;
        Transform _object, _objectParent;
        Vector3 _objectPos, _objectScale;
        Quaternion _objectRot;

        /// <summary>Holds a whole scene object (an unfinished robot) in the inspection
        /// pivot, about 17 cm tall, facing you; EndInspectObject puts it back exactly.</summary>
        public void BeginInspectObject(Transform obj, float height)
        {
            _zoom = 1;
            autoRotate = false;
            _object = obj;
            _objectParent = obj.parent;
            _objectPos = obj.localPosition; _objectRot = obj.localRotation; _objectScale = obj.localScale;
            _pivot.localRotation = Quaternion.Euler(12, 180, 0);
            _pivot.localScale = Vector3.one * 0.3f;
            float k = 0.17f / Mathf.Max(0.05f, height);
            // a whole robot is bigger than a part: keep the key light well clear of it
            RobotKey();
            obj.SetParent(_pivot, false);
            obj.localScale = _objectScale * k;
            obj.localRotation = Quaternion.identity;
            obj.localPosition = new Vector3(0, -0.085f, 0);
            if (_hand) _hand.SetActive(false);
            _dof.weight = 1;
        }

        /// <summary>Nothing to rebuild (the robot rebuilds its own geometry in place).</summary>
        public void RefreshObject() { }

        public void EndInspectObject()
        {
            if (_object == null) return;
            _object.SetParent(_objectParent, false);
            _object.localPosition = _objectPos; _object.localRotation = _objectRot; _object.localScale = _objectScale;
            _object = null;
            PartKey();
            if (_hand) _hand.SetActive(true);
            _dof.weight = 0;
        }

        public void RotateBy(Vector2 d)
        {
            _pivot.Rotate(Vector3.up, -d.x * 0.57f, Space.World);
            _pivot.Rotate(cam.transform.right, d.y * 0.57f, Space.World);
            autoRotate = false;
        }

        public void Zoom(float step) => _zoom = Mathf.Clamp(_zoom + step, 0.6f, 2.2f);

        void Update()
        {
            _t += Time.deltaTime;
            if (_inspect != null || _object != null)
            {
                if (autoRotate) _pivot.Rotate(Vector3.up, Time.deltaTime * 34f, Space.World);
                _pivot.localScale = Vector3.Lerp(_pivot.localScale, Vector3.one * _zoom, Mathf.Min(1, Time.deltaTime * 10));
            }
            else if (_hand != null)
                _hand.transform.localPosition = Vector3.Lerp(_hand.transform.localPosition, HandPos + new Vector3(0, Mathf.Sin(_t * 1.6f) * 0.004f, 0), Mathf.Min(1, Time.deltaTime * 8));
        }
    }
}
