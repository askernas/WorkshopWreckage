using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.Rendering;

namespace WorkshopWreckage
{
    /// <summary>A fixed socket for a Sorting Arm on top of one half of a drawer cabinet
    /// (storage_layout.json sockets). The fixture is always visible: a steel bracket with
    /// hazard stripes, a power contact and a lamp (green = ready, amber = arm mounted).</summary>
    public class ArmSocket : MonoBehaviour
    {
        public string id, unit, side;
        public List<string> dests;
        public Vector3 point;          // where the arm's coupler hangs (world)
        public Quaternion facing;      // the arm's root rotation (forward = out of the cabinet)
        public Renderer lamp;
        public SortingArm occupant;
        static Material _ready, _busy, _off;

        public void SetLamp()
        {
            _ready ??= EnvMaterials.Plain(new Color(0.3f, 1f, 0.4f), 0, 0.4f, 3f);
            _busy ??= EnvMaterials.Plain(new Color(1f, 0.62f, 0.15f), 0, 0.4f, 3f);
            _off ??= EnvMaterials.Plain(new Color(0.2f, 0.25f, 0.2f), 0, 0.5f);
            if (lamp != null) lamp.sharedMaterial = occupant == null ? _ready : occupant.Working ? _busy : _off;
        }

        /// <summary>"DC2 columns 1-3" for UI.</summary>
        public string Label => $"{unit} columns {(side == "L" ? "1–3" : "4–6")}";
    }

    /// <summary>The Stationary Sorting Arm (Robot Workshop redesign, Phase C). A Robot with
    /// strategy "stationary": assembled at the Robot Workbench like the others, then it
    /// stands folded on the floor in front of the bench until the player carries it
    /// (two-handed, no pocket slot) to an empty socket. Mounted, it sorts only parts lying
    /// in the staging piles below its own 18 drawers: it reserves a part (the shared
    /// robot reservation set), lifts it with the gripper, opens the drawer and stores it
    /// through StorageSystem.Deposit — the same storage, set and XP rules as any robot.
    /// A full or blocked drawer leaves the part staged. Detaching waits for the current
    /// cycle. Hoverbot leaves staged parts of a working arm's drawers alone.</summary>
    public class SortingArm : MonoBehaviour
    {
        public enum Place { Bench, Carried, Mounted }
        public Place place = Place.Bench;
        public ArmSocket socket;
        public string idleReason = "";
        public int heldId = -1;
        Robot _r;
        Game _g;
        SortingArmModel _m;
        bool _busy, _detach;
        float _idleT, _yaw;
        public const float BenchScale = 0.42f, CarryScale = 0.3f;

        public bool Working => place == Place.Mounted && _r.Active && !_r.paused;
        public HashSet<string> Served => socket != null ? _served ??= socket.dests.ToHashSet() : new HashSet<string>();
        HashSet<string> _served;

        public void Init(Robot r, Game g, SortingArmModel m)
        {
            _r = r; _g = g; _m = m;
        }

        // ---------------------------------------------------------------- placement

        /// <summary>Folded on the floor in front of the bench (after activation, recovery or load).</summary>
        public void ToBench()
        {
            CancelCycle();
            Unmount();
            place = Place.Bench;
            transform.SetParent(_g.transform, true);
            transform.SetPositionAndRotation(_r.home, _r.homeRot);
            Show(BenchScale, true);
            _m.Fold(1);
        }

        public bool PickUp()
        {
            if (!_r.Operational || place == Place.Carried) return false;
            if (_g.CarriedArm != null) { _g.hud.Toast("You are already carrying a Sorting Arm"); return false; }
            if (place == Place.Mounted && _busy) { _detach = true; _g.hud.Toast($"{_r.displayName}: finishing this part, then it's yours"); return true; }
            CancelCycle();
            Unmount();
            place = Place.Carried;
            Show(CarryScale, true);
            _m.Fold(1);
            Sfx.Play("servo", transform.position, -6);
            _g.hud.Toast("Carrying the Sorting Arm — aim at a free socket on top of a drawer cabinet and click to mount it.  [Z] sets it down at the workbench.", true);
            return true;
        }

        public string Mount(ArmSocket s)
        {
            if (place != Place.Carried) return "Carry a Sorting Arm here first";
            if (s.occupant != null) return "That socket is taken";
            place = Place.Mounted;
            socket = s;
            _served = null;
            s.occupant = this;
            transform.SetParent(_g.transform, true);
            transform.SetPositionAndRotation(s.point, s.facing);
            Show(1f, false);
            _yaw = 0;
            _m.Park(_yaw);
            s.SetLamp();
            Sfx.Play("complete", s.point, -4);
            return "";
        }

        void Unmount()
        {
            if (socket != null) { socket.occupant = null; socket.SetLamp(); }
            socket = null;
            _served = null;
        }

        void Show(float scale, bool standing)
        {
            _m.transform.localScale = Vector3.one * scale;
            _m.transform.localPosition = Vector3.zero;
            _m.transform.localRotation = Quaternion.identity;
            _m.SetStanding(standing);
        }

        // ---------------------------------------------------------------- per frame

        public static double cpuMs;
        static readonly System.Diagnostics.Stopwatch _sw = new();

        void LateUpdate()
        {
            _sw.Restart();
            LateUpdateInner();
            cpuMs += _sw.Elapsed.TotalMilliseconds;
        }

        void LateUpdateInner()
        {
            if (_g == null || _g.world == null || !_r.Operational) return;
            if (place == Place.Carried)
            {
                var cam = _g.player.cam.transform;
                var flat = Vector3.ProjectOnPlane(cam.forward, Vector3.up).normalized;
                transform.position = cam.position + flat * 0.5f + Vector3.down * 0.62f + Vector3.Cross(Vector3.up, flat) * 0.18f;
                transform.rotation = Quaternion.LookRotation(flat, Vector3.up);
                return;
            }
            if (place != Place.Mounted || _g.state == "paused" || _g.state == "menu") return;
            if (socket != null) socket.SetLamp();
            if (_busy) return;
            if (_detach) { _detach = false; PickUp(); return; }
            if (!Working) { idleReason = _r.paused ? "paused" : ""; return; }
            if ((_idleT -= Time.deltaTime) > 0) return;
            int job = NextJob();
            if (job < 0) { _idleT = 0.75f; _m.Park(_yaw = Mathf.MoveTowardsAngle(_yaw, 0, 30 * Time.deltaTime)); return; }
            StartCoroutine(Cycle(job));
        }

        /// <summary>The next staged part for one of its own drawers: lying in the staging pile
        /// below the drawer (robot-staged, or a pile the player made there), not reserved,
        /// not in anyone's hands, its drawer not blocked by strays and not full.</summary>
        public int NextJob()
        {
            var served = Served;
            int best = -1;
            float bestD = float.MaxValue;
            var full = new Dictionary<string, bool>();
            foreach (var box in served)
            {
                bool ok = _g.StraysIn(box).Count == 0 && _g.storage.Stored(box).Count < _g.db.BoxCapacity(box);
                full[box] = !ok;
            }
            if (full.Values.All(f => f)) { idleReason = "its drawers are full or blocked"; return -1; }
            foreach (var box in served)
            {
                if (full[box]) continue;
                var spot = _r.StagingSpot(box);
                if (!spot.HasValue) continue;
                foreach (var id in _g.index.QuerySphere(spot.Value + Vector3.up * 0.15f, 0.55f))
                {
                    var i = _g.world.instances[id];
                    if (i.state != ItemState.InWorld || _g.robotReserved.Contains(id) || _g.interaction.busy.Contains(id)) continue;
                    var def = _g.world.DefOf(id);
                    if (!def.robotCarry || def.IsRobotPart || def.storageBox != box || i.Position.y > 0.7f || ItemDatabase.Extents(def)[0] > _r.MaxItemSize) continue;   // Wide Gripper raises the size
                    bool staged = i.owner == "stage:" + box || Vector3.ProjectOnPlane(i.Position - spot.Value, Vector3.up).magnitude <= 0.5f;
                    if (!staged) continue;
                    float d = (i.Position - socket.point).sqrMagnitude;
                    if (d < bestD) { bestD = d; best = id; }
                }
            }
            idleReason = best < 0 ? "no staged parts below its drawers" : "";
            return best;
        }

        /// <summary>Cycle speed with the Faster Servos upgrade (never past its limit).</summary>
        float CycleSpeed => Mathf.Max(0.3f, (float)(_g.progression?.RobotStat(_r.type, "cycle_speed", _r.BaseStat("cycle_speed", 1), _r.LimitOf("cycle_speed")) ?? _r.BaseStat("cycle_speed", 1)));

        IEnumerator Move(System.Func<float, (Vector3 wrist, Vector3 dir)> path, float dur)
        {
            dur /= CycleSpeed;
            for (float t = 0; t < dur; t += Time.deltaTime)
            {
                var (w, d) = path(Mathf.SmoothStep(0, 1, t / dur));
                _m.Reach(w, d);
                if (heldId >= 0) HeldFollow();
                yield return null;
                while (_g.state == "paused" || _g.state == "menu") yield return null;
            }
            var (w1, d1) = path(1);
            _m.Reach(w1, d1);
        }

        IEnumerator Cycle(int itemId)
        {
            _busy = true;
            _r.status = "sorting";
            _g.robotReserved.Add(itemId);
            var inst = _g.world.instances[itemId];
            var def = _g.world.DefOf(itemId);
            var box = def.storageBox;
            var unit = _g.units[Json.Str(_g.db.boxes[box], "unit")];
            var front = Vector3.ProjectOnPlane(socket.facing * Vector3.forward, Vector3.up).normalized;
            var restW = _m.GripPoint;
            var down = Vector3.down;
            var p0 = inst.Position;
            float clear = SortingArmModel.GripLen + 0.04f;
            var above = p0 + Vector3.up * (clear + 0.35f);
            var at = p0 + Vector3.up * clear;
            // 1-2: swing over the pile and reach down
            yield return Move(u => (Vector3.Lerp(restW, above, u), down), 0.9f);
            yield return Move(u => (Vector3.Lerp(above, at, u), down), 0.4f);
            // 3: close the gripper, lift the part (if it is still there)
            for (float t = 0; t < 0.25f / CycleSpeed; t += Time.deltaTime) { _m.Grip(1 - t / (0.25f / CycleSpeed)); yield return null; }
            _g.robotReserved.Remove(itemId);
            if (inst.state != ItemState.InWorld || _g.interaction.busy.Contains(itemId))
            {
                yield return Retract(above);
                yield break;
            }
            var from = inst.transform;
            _g.index.Remove(itemId);
            _g.world.SetState(itemId, ItemState.Carried, _r.Owner);
            _g.interaction.SettleNeighbours(from, def);
            heldId = itemId;
            _held = HeldView.MakeModel(def);
            _held.transform.localScale = Vector3.one * def.worldScale;
            HeldFollow();
            Sfx.Play("pickup", p0, -5);
            // 4: up and over to the drawer front, gripper pointing into the drawer
            var dp = unit.DestPoint(_g.db.boxes[box]);
            var into = -front;
            var drawerW = dp + front * (clear + 0.12f);
            yield return Move(u => (Vector3.Lerp(at, above, u), down), 0.35f);
            yield return Move(u => (Vector3.Lerp(above, drawerW, u), Vector3.Slerp(down, into, u)), 0.9f);
            // 5: open the drawer, place the part, close it
            var r = _g.storage.Check(box, itemId);
            if (r == StorageSystem.Result.Ok && unit.type == "drawer_cabinet") unit.OpenDrawer(unit.SlotOf(box), new List<(Mesh, Material[], Matrix4x4)>(), 0.9f);
            yield return Move(u => (Vector3.Lerp(drawerW, drawerW + into * 0.1f, u), into), 0.3f);
            for (float t = 0; t < 0.2f / CycleSpeed; t += Time.deltaTime) { _m.Grip(t / (0.2f / CycleSpeed)); yield return null; }
            if (inst.state == ItemState.Carried && inst.owner == _r.Owner)
            {
                if (_g.storage.Deposit(box, itemId, _r.Owner) == StorageSystem.Result.Ok)
                {
                    _r.delivered++;
                    Sfx.Play("drop_drawer", dp, -5);
                    unit.RefreshLabels();
                }
                else ReturnToPile(itemId, box);
            }
            DropHeldModel();
            yield return Retract(drawerW);
        }

        IEnumerator Retract(Vector3 from)
        {
            var mid = socket.point + Vector3.down * 0.25f + socket.facing * Vector3.forward * 0.35f;
            yield return Move(u => (Vector3.Lerp(from, mid, u), Vector3.down), 0.6f);
            _m.Park(_yaw);
            _m.Grip(0.6f);
            _busy = false;
            _r.status = "";
            _idleT = 0.15f;
        }

        GameObject _held;
        void HeldFollow()
        {
            if (_held == null) return;
            _held.transform.position = _m.GripPoint;
            _held.transform.rotation = _m.carry.rotation;
        }

        void DropHeldModel()
        {
            if (_held != null) Destroy(_held);
            _held = null;
            heldId = -1;
        }

        /// <summary>A part it could not store goes back onto its staging pile (identity kept).</summary>
        void ReturnToPile(int itemId, string box)
        {
            var def = _g.world.DefOf(itemId);
            var spot = _r.StagingSpot(box) ?? socket.point;
            var e = ItemDatabase.Extents(def);
            var p = new Vector3(spot.x + Random.Range(-0.1f, 0.1f), e[2] * 0.5f, spot.z + Random.Range(-0.1f, 0.1f));
            _g.world.SetTransform(itemId, Matrix4x4.TRS(p, Quaternion.Euler(0, Random.Range(0, 360f), def.restMode == RestMode.Long ? 90 : 0), Vector3.one * def.worldScale));
            _g.world.SetState(itemId, ItemState.InWorld, "stage:" + box);
            _g.index.Insert(itemId);
        }

        void CancelCycle()
        {
            if (!_busy) return;
            StopAllCoroutines();
            _busy = false;
            foreach (var id in _g.robotReserved.ToList())
                if (heldId == id) _g.robotReserved.Remove(id);
            if (heldId >= 0)
            {
                var inst = _g.world.instances[heldId];
                if (inst.state == ItemState.Carried && inst.owner == _r.Owner) ReturnToPile(heldId, _g.world.DefOf(heldId).storageBox);
            }
            DropHeldModel();
        }

        // ---------------------------------------------------------------- save / load / UI

        public Dictionary<string, object> ToDict() => new()
        {
            ["place"] = place.ToString(), ["socket"] = place == Place.Mounted && socket != null ? socket.id : "",
        };

        /// <summary>Loads the placement. A carried arm (or one whose socket is gone / taken) is
        /// set down at the bench; a part held mid-cycle goes back to its staging pile.</summary>
        public void Load(Dictionary<string, object> d)
        {
            foreach (var inst in _g.world.instances.Where(i => i.state == ItemState.Carried && i.owner == _r.Owner).ToList())
                ReturnToPile(inst.id, _g.world.DefOf(inst.id).storageBox);
            ToBench();
            if (d == null || Json.Str(d, "place") != "Mounted") return;
            if (_g.sockets.TryGetValue(Json.Str(d, "socket"), out var s) && s.occupant == null)
            {
                place = Place.Carried;
                Mount(s);
            }
        }

        public string Describe()
        {
            switch (place)
            {
                case Place.Bench: return $"{_r.displayName}: folded at the workbench — carry it to a socket";
                case Place.Carried: return $"{_r.displayName}: in your hands";
            }
            if (_r.paused) return $"{_r.displayName} on {socket.id}: paused";
            return $"{_r.displayName} on {socket.id} ({socket.Label}): " + (_busy ? "sorting" : idleReason != "" ? "idle — " + idleReason : "ready") + $" · {_r.delivered} sorted";
        }

        // ---------------------------------------------------------------- socket fixtures

        /// <summary>Builds the fixed sockets of one drawer cabinet (from storage_layout.json).</summary>
        public static void BuildSockets(Game g, StorageUnit su, string unitId)
        {
            foreach (var kv in g.db.sockets.Where(s => s.Value.unit == unitId))
            {
                var (unit, side, dests) = kv.Value;
                var fronts = dests.Select(d => su.FrontCenterG(Json.Int(g.db.boxes[d], "row") * su.cols + Json.Int(g.db.boxes[d], "col"))).ToList();
                float x = fronts.Average(f => f.x), zf = fronts.Max(f => f.z);
                float top = (float)Json.Num(g.db.Unit(unitId), "height", 2.0);
                var go = new GameObject("Socket_" + kv.Key);
                go.transform.SetParent(su.transform, false);
                var b = new Batch();
                var steel = EnvMaterials.Plain(new Color(0.3f, 0.31f, 0.33f), 0.9f, 0.45f);
                var yellow = EnvMaterials.Plain(new Color(0.9f, 0.68f, 0.12f), 0.3f, 0.5f);
                var black = EnvMaterials.Plain(new Color(0.06f, 0.06f, 0.06f), 0f, 0.6f);
                b.Box(new Vector3(x, top + 0.02f, zf - 0.12f), new Vector3(0.42f, 0.04f, 0.3f), steel);           // plate on the cabinet top
                b.Box(new Vector3(x, top + 0.02f, zf + 0.06f), new Vector3(0.3f, 0.04f, 0.14f), steel);           // overhang
                b.Box(new Vector3(x, top - 0.005f, zf + 0.1f), new Vector3(0.2f, 0.012f, 0.12f), black);         // coupler seat
                for (int k = 0; k < 6; k++)
                    b.Box(new Vector3(x - 0.125f + k * 0.05f, top + 0.02f, zf + 0.132f), new Vector3(0.025f, 0.04f, 0.006f), k % 2 == 0 ? yellow : black);
                foreach (var dx in new[] { -0.07f, 0.07f }) b.Cylinder(new Vector3(x + dx, top - 0.02f, zf + 0.1f), 0.012f, 0.03f, steel);   // locking pins
                b.Box(new Vector3(x + 0.17f, top + 0.06f, zf - 0.2f), new Vector3(0.06f, 0.06f, 0.06f), steel);   // power box
                b.Build(go.transform, "SocketGeo", true);
                var s = go.AddComponent<ArmSocket>();
                s.id = kv.Key; s.unit = unit; s.side = side; s.dests = dests;
                s.point = su.transform.TransformPoint(Batch.G(x, top - 0.01f, zf + 0.1f));
                s.facing = su.transform.rotation * Quaternion.LookRotation(Batch.G(0, 0, 1), Vector3.up);
                var lamp = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                Object.Destroy(lamp.GetComponent<Collider>());
                lamp.transform.SetParent(go.transform, false);
                lamp.transform.localPosition = Batch.G(x + 0.17f, top + 0.1f, zf - 0.2f);
                lamp.transform.localScale = Vector3.one * 0.035f;
                s.lamp = lamp.GetComponent<Renderer>();
                s.lamp.shadowCastingMode = ShadowCastingMode.Off;
                s.SetLamp();
                // aim target (the Target layer, like storage destinations)
                var tgt = new GameObject("SocketTarget");
                tgt.layer = Layers.Target;
                tgt.transform.SetParent(go.transform, false);
                tgt.transform.localPosition = Batch.G(x, top + 0.04f, zf - 0.02f);
                var col = tgt.AddComponent<BoxCollider>();
                col.isTrigger = true;
                col.size = new Vector3(0.5f, 0.2f, 0.42f);
                var st = tgt.AddComponent<StorageTarget>();
                st.unit = su;
                st.destId = "SOCKET:" + kv.Key;
                g.sockets[kv.Key] = s;
            }
        }
    }
}
