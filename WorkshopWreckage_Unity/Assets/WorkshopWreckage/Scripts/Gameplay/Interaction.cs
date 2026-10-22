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
        public int targetItem = -1;
        public string targetDest;
        public StorageUnit targetUnit;
        public bool inspecting;
        public int inspectWorldId = -1;
        public double lastActionMicros;
        public readonly HashSet<int> busy = new();
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
            var kb = Keyboard.current;
            var mouse = Mouse.current;
            if (!enabledInput) { HideHover(); return; }
            bool lmbDown = mouse != null && mouse.leftButton.wasPressedThisFrame;
            bool lmbHeld = mouse != null && mouse.leftButton.isPressed;
            float wheel = mouse != null ? mouse.scroll.ReadValue().y : 0;
            if (inspecting)
            {
                HideHover();
                if (lmbDown && inspectWorldId >= 0) { int id = inspectWorldId; EndInspect(); Pickup(id, false); }
                else if (kb.fKey.wasPressedThisFrame) EndInspect();
                else if (kb.rKey.wasPressedThisFrame) game.held.autoRotate = !game.held.autoRotate;
                if (wheel != 0) game.held.Zoom(wheel > 0 ? 0.15f : -0.15f);
                float turn = (kb.qKey.isPressed ? 1 : 0) - (kb.eKey.isPressed ? 1 : 0);
                if (turn != 0) game.held.RotateBy(new Vector2(-turn * 240f * Time.deltaTime, 0));
                if (mouse != null && Cursor.lockState == CursorLockMode.Locked) game.held.RotateBy(mouse.delta.ReadValue() * new Vector2(1, -1));
                return;
            }
            UpdateTarget();
            _repeatT -= Time.deltaTime;
            if (lmbDown) { _repeatT = Repeat * 2.5f; Primary(); }
            else if (lmbHeld && _repeatT <= 0) { _repeatT = Repeat; Primary(); }
            if (kb.gKey.wasPressedThisFrame) DepositAll();
            if (kb.fKey.wasPressedThisFrame) BeginInspect();
            if (kb.zKey.wasPressedThisFrame) DropHeld();
            if (wheel < 0) Inv.Cycle(1);
            if (wheel > 0) Inv.Cycle(-1);
            for (int k = 0; k < 9; k++)
            {
                if (((UnityEngine.InputSystem.Controls.KeyControl)kb[(Key)((int)Key.Digit1 + k)]).wasPressedThisFrame)
                {
                    var s = Inv.Stacks();
                    if (k < s.Count) Inv.Select(s[k].defId);
                }
            }
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
            var (pid, _) = game.index.Pick(o, dir, envT + 0.03f, busy);
            targetType = "none";
            targetItem = -1;
            targetDest = null;
            targetUnit = null;
            if (pid >= 0) { targetType = "item"; targetItem = pid; }
            else if (hit.HasValue)
            {
                var st = hit.Value.collider.GetComponent<StorageTarget>();
                if (st != null && st.station) targetType = "station";
                else if (st != null) { targetType = "dest"; targetDest = st.destId; targetUnit = st.unit; }
            }
            ShowHover();
            TargetChanged?.Invoke(Describe());
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
            if (targetType == "item")
            {
                var inst = World.instances[targetItem];
                var mesh = game.renderer.WorldMeshFor(targetItem);
                _outline.GetComponent<MeshFilter>().sharedMesh = mesh;
                var mats = new Material[mesh.subMeshCount];
                for (int i = 0; i < mats.Length; i++) mats[i] = _outlineMat;
                _outline.GetComponent<MeshRenderer>().sharedMaterials = mats;
                SetMatrix(_outline.transform, inst.transform);
                var e = ItemDatabase.Extents(World.DefOf(targetItem));
                _outlineMat.SetFloat("_Grow", Mathf.Clamp(0.006f / Mathf.Max(0.002f, e[2] * 0.5f), 0.04f, 0.5f));
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
            switch (targetType)
            {
                case "item":
                {
                    var def = World.DefOf(targetItem);
                    bool known = game.progress.IsDiscovered(def.id);
                    return new TargetInfo { title = known ? def.displayName : def.shape + "  (unidentified)", sub = def.CategoryName,
                        hint = Inv.IsFull ? "Hands full — sort what you carry" : "[LMB] Pick up    [F] Inspect" };
                }
                case "dest":
                {
                    var title = game.db.BoxLabel(targetDest) + "  ·  " + AssignedName(targetDest);
                    var sub = $"{Storage.Stored(targetDest).Count} / {game.db.BoxCapacity(targetDest)} stored";
                    if (held < 0) return new TargetInfo { title = title, sub = sub, hint = "" };
                    var r = Storage.Check(targetDest, held);
                    if (r == StorageSystem.Result.Ok)
                    {
                        int n = MatchingCarried(targetDest).Count;
                        return new TargetInfo { title = title, sub = sub, hint = "[LMB] Store here" + (n > 1 ? $"    [G] Store all {n}" : ""), tone = 1 };
                    }
                    return new TargetInfo { title = title, sub = sub, hint = Storage.ResultText(r, targetDest, held), tone = -1 };
                }
                case "station":
                {
                    var parts = StationParts();
                    return new TargetInfo { title = "Robot Repair Bay", sub = $"{game.progress.robotPartsFound} robot parts found",
                        hint = parts.Count > 0 ? $"[LMB] Place robot part    [G] Place all ({parts.Count})" : "Bring robot components here", tone = parts.Count > 0 ? 1 : 0 };
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

        public void Primary()
        {
            switch (targetType)
            {
                case "item": Pickup(targetItem, true); break;
                case "dest":
                    int held = Inv.HeldId();
                    if (held >= 0) Deposit(targetDest, held);
                    else if (targetUnit.type == "drawer_cabinet") OpenDrawer(targetUnit, targetDest);
                    break;
                case "station":
                    var p = StationParts();
                    if (p.Count > 0) DepositStation(p[0]);
                    break;
            }
        }

        public bool Pickup(int id, bool animate)
        {
            long t0 = System.Diagnostics.Stopwatch.GetTimestamp();
            var inst = World.instances[id];
            if (inst.state != ItemState.InWorld) return false;
            if (Inv.IsFull)
            {
                Feedback?.Invoke($"Your hands are full ({Inv.Count}/{Inv.capacity}) — store what you carry first", false);
                Sfx.Play("reject", null, -6);
                return false;
            }
            var from = inst.transform;
            var def = World.DefOf(id);
            game.index.Remove(id);
            World.SetState(id, ItemState.Carried, "player");
            Inv.Add(id);
            Inv.Select(def.id);
            if (animate) StartCoroutine(Fly(def, from, game.held.HandWorld(), 0.16f, null));
            game.tools?.OnManualPickup(id);   // Handful: nearby copies come along
            SettleNeighbours(from, def);
            Sfx.Play(ItemDatabase.Extents(def)[0] > 0.24f ? "pickup_heavy" : "pickup", from.GetColumn(3), -2);
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
                StartCoroutine(Fly(def, game.held.HandWorld(), unit.FrontWorld(unit.SlotOf(dest)) * Matrix4x4.Translate(new Vector3(0, 0.05f, -0.25f)) * Matrix4x4.Scale(Vector3.one * def.worldScale), 0.18f, null));
            }
            else
            {
                game.renderer.SetHidden(id, true);
                busy.Add(id);
                StartCoroutine(Fly(def, game.held.HandWorld(), World.instances[id].transform, 0.2f, () => { busy.Remove(id); game.renderer.SetHidden(id, false); }));
            }
            Sfx.Play("drop_drawer", World.instances[id].Position, -3);
            Feedback?.Invoke($"Stored: {def.displayName}  →  {Json.Str(d, "slot")}", true);
            unit.RefreshLabels();
            return true;
        }

        public void DepositStation(int id)
        {
            var def = World.DefOf(id);
            var r = Storage.Check(def.storageBox, id);
            if (r != StorageSystem.Result.Ok) { Feedback?.Invoke(Storage.ResultText(r, def.storageBox, id), false); return; }
            Inv.Remove(id);
            Storage.Deposit(def.storageBox, id);
            game.repairBay.Refresh(true);
            Sfx.Play(Storage.Stored(def.storageBox).Count == game.db.BoxCapacity(def.storageBox) ? "complete" : "drop_drawer", game.repairBay.transform.position);
            Feedback?.Invoke("Robot part placed: " + def.displayName, true);
        }

        void DepositAll()
        {
            if (targetType == "dest")
            {
                var ids = MatchingCarried(targetDest);
                if (ids.Count == 0) { int held = Inv.HeldId(); if (held >= 0) Deposit(targetDest, held); return; }
                int n = ids.Count(id => Deposit(targetDest, id));
                if (n > 1) Feedback?.Invoke($"Stored {n} parts", true);
            }
            else if (targetType == "station")
                foreach (var id in StationParts()) DepositStation(id);
        }

        List<int> MatchingCarried(string dest)
        {
            var l = Inv.items.Where(id => World.DefOf(id).storageBox == dest).ToList();
            int room = game.db.BoxCapacity(dest) - Storage.Stored(dest).Count;
            return l.Take(Mathf.Max(0, room)).ToList();
        }

        List<int> StationParts() => Inv.items.Where(id => World.DefOf(id).IsRobotPart).ToList();

        public void DropHeld()
        {
            int id = Inv.HeldId();
            if (id < 0) return;
            var def = World.DefOf(id);
            var (o, dir) = Ray();
            var aim = o + dir * 1.2f;
            Vector3 p = Physics.Raycast(aim + Vector3.up * 0.5f, Vector3.down, out var hit, 3.5f, 1 << Layers.Env) ? hit.point : new Vector3(aim.x, 0, aim.z);
            var b = MeshLibrary.UnitBounds(def.geometryKey).size * def.worldScale;
            var rot = Quaternion.Euler(0, UnityEngine.Random.value * 360, 0);
            float halfH = b.y / 2;
            if (def.restMode == RestMode.Long) { rot *= Quaternion.Euler(0, 0, 90); halfH = b.x / 2; }
            var m = Matrix4x4.TRS(p + Vector3.up * halfH, rot, Vector3.one * def.worldScale);
            Inv.Remove(id);
            World.SetTransform(id, m);
            World.SetState(id, ItemState.InWorld, "world");
            game.index.Insert(id);
            game.renderer.SetHidden(id, true);
            busy.Add(id);
            StartCoroutine(Fly(def, game.held.HandWorld(), m, 0.25f, () => { busy.Remove(id); game.renderer.SetHidden(id, false); Sfx.Play("drop_floor", p, -4); }));
        }

        // ---------------------------------------------------------------- inspection

        public void BeginInspect()
        {
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
            if (inspectWorldId >= 0) { busy.Remove(inspectWorldId); game.renderer.SetHidden(inspectWorldId, false); }
            inspectWorldId = -1;
            game.held.EndInspect();
            game.player.lookEnabled = true;
            game.player.inputEnabled = true;
            InspectChanged?.Invoke(false, null);
        }

        // ---------------------------------------------------------------- helpers

        /// <summary>Tool pulls (magnet, Supermagnet, sweep) leave a glowing trail.</summary>
        public void StartFly(ItemDefinition def, Matrix4x4 from, Matrix4x4 to, float dur) => StartCoroutine(Fly(def, from, to, dur, null, true));

        static Material _trailMat;

        IEnumerator Fly(ItemDefinition def, Matrix4x4 from, Matrix4x4 to, float dur, Action done, bool trail = false)
        {
            var go = HeldView.MakeModel(def);
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
                yield return null;
            }
            Destroy(go);
            done?.Invoke();
        }

        public void SettleNeighbours(Matrix4x4 removed, ItemDefinition removedDef)
        {
            float rRemoved = HeldView.Radius(removedDef);
            float drop = ItemDatabase.Extents(removedDef)[2] * 0.8f;
            var ro = (Vector3)removed.GetColumn(3);
            int settled = 0;
            foreach (var nid in game.index.QuerySphere(ro, rRemoved * 1.6f + 0.05f))
            {
                if (busy.Contains(nid)) continue;
                var n = World.instances[nid];
                float dy = n.Position.y - ro.y;
                if (dy <= 0.004f) continue;
                float hd = new Vector2(n.Position.x - ro.x, n.Position.z - ro.z).magnitude;
                float reach = rRemoved + HeldView.Radius(World.DefOf(nid));
                if (hd > reach * 0.85f) continue;
                float amount = Mathf.Min(drop * (1 - hd / reach), dy);
                float floorY = ItemDatabase.Extents(World.DefOf(nid))[2] * 0.5f;
                float targetY = Mathf.Max(floorY, n.Position.y - amount);
                if (targetY >= n.Position.y - 0.002f) continue;
                busy.Add(nid);
                settled++;
                StartCoroutine(Settle(nid, n.transform, targetY, 0.22f + UnityEngine.Random.value * 0.08f));
            }
            if (settled > 0) Sfx.Play("settle", ro, -10);
        }

        IEnumerator Settle(int id, Matrix4x4 start, float targetY, float dur)
        {
            float t = 0;
            float y0 = start.m13;
            while (t < 1)
            {
                t = Mathf.Min(1, t + Time.deltaTime / dur);
                var m = start;
                m.m13 = Mathf.Lerp(y0, targetY, t * t);
                World.SetTransform(id, m);
                yield return null;
            }
            busy.Remove(id);
            game.index.Insert(id);
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
            var key = new GameObject("InspectKey").AddComponent<Light>();
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
            if (_hand) _hand.SetActive(false);
            _dof.weight = 1;
        }

        public void EndInspect()
        {
            if (_inspect) Destroy(_inspect);
            _inspect = null;
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
            if (_inspect != null)
            {
                if (autoRotate) _pivot.Rotate(Vector3.up, Time.deltaTime * 34f, Space.World);
                _pivot.localScale = Vector3.Lerp(_pivot.localScale, Vector3.one * _zoom, Mathf.Min(1, Time.deltaTime * 10));
            }
            else if (_hand != null)
                _hand.transform.localPosition = Vector3.Lerp(_hand.transform.localPosition, HandPos + new Vector3(0, Mathf.Sin(_t * 1.6f) * 0.004f, 0), Mathf.Min(1, Time.deltaTime * 8));
        }
    }

    /// <summary>The robot repair bay: collects robot components per robot (port of RepairBay.gd).</summary>
    public class RepairBay : MonoBehaviour
    {
        public static readonly string[] Robots = { "retrobot", "unibot", "spiderbot", "caterpillarbot", "hoverbot" };
        static readonly Vector3 Bench = new(2.9f, 0.86f, 0.9f);
        Game _game;
        readonly Dictionary<string, Transform> _mats = new();
        TextAtlas _labels;

        public void Init(Game g)
        {
            _game = g;
            var b = new Batch();
            var top = EnvMaterials.Wood(new Color(0.42f, 0.3f, 0.2f));
            var steel = EnvMaterials.Metal(new Color(0.3f, 0.33f, 0.3f), 0.4f);
            b.Box(new Vector3(0, Bench.y - 0.03f, 0), new Vector3(Bench.x, 0.06f, Bench.z), top);
            foreach (var sx in new[] { -1f, 1f })
                foreach (var sz in new[] { -1f, 1f })
                    b.Box(new Vector3(sx * (Bench.x / 2 - 0.06f), (Bench.y - 0.06f) / 2, sz * (Bench.z / 2 - 0.06f)), new Vector3(0.07f, Bench.y - 0.06f, 0.07f), steel);
            b.Box(new Vector3(0, 1.45f, -Bench.z / 2 + 0.02f), new Vector3(Bench.x, 1.1f, 0.04f), EnvMaterials.Wood(new Color(0.3f, 0.2f, 0.13f)));
            var title = TextAtlas.RenderSingle("ROBOT REPAIR BAY", 1024, 128, new Color(0.3f, 0.2f, 0.13f), new Color(0.82f, 0.63f, 0.32f), 72, true);
            b.Quad(new Vector3(0, 1.85f, -Bench.z / 2 + 0.045f), new Vector2(1.6f, 0.2f), EnvMaterials.Atlas(title), new Rect(0, 0, 1, 1));
            _labels = new TextAtlas(Robots.Length, new Vector2Int(200, 80), new Color(0.3f, 0.2f, 0.13f));
            var lm = EnvMaterials.Atlas(_labels.texture);
            float w = Bench.x / Robots.Length;
            for (int i = 0; i < Robots.Length; i++)
            {
                float x = -Bench.x / 2 + w * (i + 0.5f);
                b.Box(new Vector3(x, Bench.y + 0.003f, 0.05f), new Vector3(w - 0.06f, 0.006f, Bench.z - 0.2f), EnvMaterials.Plain(new Color(0.14f, 0.24f, 0.18f), 0, 0.1f));
                b.Quad(new Vector3(x, 1.55f, -Bench.z / 2 + 0.045f), new Vector2(w - 0.12f, 0.42f), EnvMaterials.Blueprint(1), new Rect(0, 0, 1, 1));
                b.Quad(new Vector3(x, 1.2f, -Bench.z / 2 + 0.046f), new Vector2(0.4f, 0.16f), lm, _labels.CellUV(i));
                var holder = new GameObject("Mat_" + Robots[i]).transform;
                holder.SetParent(transform, false);
                holder.localPosition = Batch.G(x, Bench.y + 0.006f, 0.05f);
                _mats[Robots[i]] = holder;
            }
            b.colliders.Add((Batch.G(0, Bench.y / 2, 0), new Vector3(Bench.x, Bench.y, Bench.z), Quaternion.identity));
            b.Build(transform, "RepairBay");
            var tgo = new GameObject("Target_RepairBay") { layer = Layers.Target };
            tgo.transform.SetParent(transform, false);
            tgo.transform.localPosition = Batch.G(0, Bench.y + 0.15f, 0);
            tgo.AddComponent<BoxCollider>().size = new Vector3(Bench.x, 0.3f, Bench.z);
            tgo.AddComponent<StorageTarget>().station = true;
        }

        /// <summary>Bench-local (Godot space) x of a robot's mat.</summary>
        public static float MatX(int i) => -Bench.x / 2 + Bench.x / Robots.Length * (i + 0.5f);

        /// <summary>animate: true when the player just installed a part (plays the
        /// activation sequence if it completed a robot).</summary>
        public void Refresh(bool animate = false)
        {
            foreach (var r in _game.robots) r.RefreshAssembly(animate);
            for (int i = 0; i < Robots.Length; i++)
            {
                var robot = Robots[i];
                var ids = _game.storage.Stored("RS-" + robot);
                int total = _game.db.RobotPartIds(robot).Count;
                _labels.Set(i, $"{ids.Count}/{total}", robot.ToUpper(), ids.Count == total ? "READY" : "parts", true);
                var holder = _mats[robot];
                foreach (Transform c in holder) Destroy(c.gameObject);
                if (_game.robots.Exists(r => r.id == robot)) continue;   // installed on the robot itself
                for (int k = 0; k < ids.Count; k++)
                {
                    var def = _game.world.DefOf(ids[k]);
                    var mi = HeldView.MakeModel(def);
                    mi.transform.SetParent(holder, false);
                    var rot = Quaternion.Euler(0, -0.4f * k * Mathf.Rad2Deg, 0) * (def.restMode == RestMode.Long ? Quaternion.Euler(0, 0, -90) : Quaternion.identity);
                    mi.transform.localRotation = rot;
                    mi.transform.localScale = Vector3.one * def.worldScale * 0.8f;
                    mi.transform.localPosition = Batch.G(-0.18f + (k % 4) * 0.12f, 0.04f, -0.2f + (k / 4) * 0.12f);
                }
            }
            _labels.Redraw();
        }
    }
}
