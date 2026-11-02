using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.Rendering;

namespace WorkshopWreckage
{
    /// <summary>An oriented box (world) for the Sorting Arm's clearance QA.</summary>
    public struct Obb
    {
        public Vector3 c, ax, ay, az, h;

        /// <summary>A box in a pivot's local frame (centre, half extents).</summary>
        public static Obb Of(Transform t, Vector3 c, Vector3 h) =>
            new() { c = t.TransformPoint(c), ax = t.right, ay = t.up, az = t.forward, h = h * t.lossyScale.x };

        /// <summary>An axis-aligned box in a unit's local frame, given in Godot axes (min / max corners).</summary>
        public static Obb In(Transform t, Vector3 minG, Vector3 maxG)
        {
            var c = (minG + maxG) / 2;
            var h = (maxG - minG) / 2;
            return new Obb { c = t.TransformPoint(Batch.G(c)), ax = t.right, ay = t.up, az = t.forward, h = new Vector3(Mathf.Abs(h.x), Mathf.Abs(h.y), Mathf.Abs(h.z)) };
        }

        public static Obb World(Vector3 min, Vector3 max) => new() { c = (min + max) / 2, ax = Vector3.right, ay = Vector3.up, az = Vector3.forward, h = (max - min) / 2 };

        /// <summary>Separating-axis gap (m) between two boxes: positive = at least that far apart,
        /// negative = overlapping (by about that much).</summary>
        public static float Gap(in Obb a, in Obb b)
        {
            var d = b.c - a.c;
            float best = float.MinValue;
            var ah = a.h; var bh = b.h;
            Vector3 a0 = a.ax, a1 = a.ay, a2 = a.az, b0 = b.ax, b1 = b.ay, b2 = b.az;
            void Axis(Vector3 l)
            {
                float m = l.sqrMagnitude;
                if (m < 1e-8f) return;
                l /= Mathf.Sqrt(m);
                float ra = ah.x * Mathf.Abs(Vector3.Dot(a0, l)) + ah.y * Mathf.Abs(Vector3.Dot(a1, l)) + ah.z * Mathf.Abs(Vector3.Dot(a2, l));
                float rb = bh.x * Mathf.Abs(Vector3.Dot(b0, l)) + bh.y * Mathf.Abs(Vector3.Dot(b1, l)) + bh.z * Mathf.Abs(Vector3.Dot(b2, l));
                float g = Mathf.Abs(Vector3.Dot(d, l)) - ra - rb;
                if (g > best) best = g;
            }
            Axis(a0); Axis(a1); Axis(a2); Axis(b0); Axis(b1); Axis(b2);
            Axis(Vector3.Cross(a0, b0)); Axis(Vector3.Cross(a0, b1)); Axis(Vector3.Cross(a0, b2));
            Axis(Vector3.Cross(a1, b0)); Axis(Vector3.Cross(a1, b1)); Axis(Vector3.Cross(a1, b2));
            Axis(Vector3.Cross(a2, b0)); Axis(Vector3.Cross(a2, b1)); Axis(Vector3.Cross(a2, b2));
            return best;
        }
    }

    /// <summary>The overhead Sorting Arm mount of one drawer cabinet or shelving unit
    /// (storage_layout.json sockets): a ceiling-hung I-beam rail along the unit's front, a trolley
    /// running on it and a lift mast sliding through the trolley; the arm's coupler bolts under the
    /// mast's carriage plate. The trolley (x) and the mast (height) put the arm in front of any
    /// destination of the unit; parked, the mast is up and the arm folds above the unit at one end
    /// of the rail. Nothing touches the unit and nothing hangs in front of its fronts. The trolley
    /// lamp: green = ready, amber = arm working, dark = arm idle / paused.</summary>
    public class ArmSocket : MonoBehaviour
    {
        public string id, unit, side;
        public List<string> dests;
        public StorageUnit su;
        public bool cabinet;
        public float xMin, xMax, parkX, hMin, hPark, pz, yb, mastLen, baseYaw, top;   // unit-local (Godot axes), m
        public float cx, cH;                                                       // the carriage now
        public Transform trolley, mast;
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

        /// <summary>"DC2 (36 drawers)" for UI.</summary>
        public string Label => $"{unit} ({dests.Count} {(cabinet ? "drawers" : "containers")})";

        /// <summary>The coupler (the arm's root, world) with the carriage at (x, H).</summary>
        public Vector3 Point(float x, float H) => su.transform.TransformPoint(Batch.G(x, H, pz));
        public Vector3 point => Point(cx, cH);
        /// <summary>The arm's root rotation: forward = out of the unit, over the aisle.</summary>
        public Quaternion facing => su.transform.rotation * Quaternion.LookRotation(Batch.G(0, 0, 1), Vector3.up);
        public Vector3 Front => su.transform.TransformDirection(Batch.G(0, 0, 1));

        public void SetCarriage(float x, float H)
        {
            cx = x; cH = H;
            trolley.localPosition = new Vector3(x, 0, 0);
            mast.localPosition = new Vector3(x, H, 0);
        }

        /// <summary>The carriage that puts the arm's swing plane through `wrist` (world), with the
        /// shoulder Lift above it (clamped to the mast's travel and the rail).</summary>
        public (float x, float H) CarriageFor(Vector3 wrist)
        {
            var l = su.transform.InverseTransformPoint(wrist);
            return (Mathf.Clamp(l.x, xMin, xMax), Mathf.Clamp(l.y + Lift, hMin, hPark));
        }
        public const float Lift = 1.95f;   // the arm works nearly stretched: a low target never bows the elbow out into the aisle

        /// <summary>The parked wrist (world) with the carriage at x, mast up.</summary>
        public Vector3 ParkWrist(float x) => Point(x, hPark) + Vector3.down * SortingArmModel.ShoulderDrop + Front * SortingArmModel.ParkOut;
        public float XOf(Vector3 world) => Mathf.Clamp(su.transform.InverseTransformPoint(world).x, xMin, xMax);

        // ---------------------------------------------------------------- QA geometry

        /// <summary>The mount's boxes with the carriage where it is now: rail, hangers, trolley, mast.</summary>
        public void MountBoxes(List<Obb> into)
        {
            var t = su.transform;
            float zb = pz - BeamBack, w = su.size.x;
            into.Add(Obb.In(t, new Vector3(-w / 2, yb, zb - 0.06f), new Vector3(w / 2, yb + BeamH, zb + 0.06f)));
            foreach (var hx in Hangers(w))
                into.Add(Obb.In(t, new Vector3(hx - 0.03f, yb + BeamH, zb - 0.09f), new Vector3(hx + 0.03f, ceiling, zb + 0.09f)));
            // trolley: saddle over the beam, bridge under it to the collar round the mast
            into.Add(Obb.In(t, new Vector3(cx - 0.11f, yb - 0.07f, zb - 0.1f), new Vector3(cx + 0.11f, yb + BeamH + 0.08f, zb + 0.1f)));
            into.Add(Obb.In(t, new Vector3(cx - 0.07f, yb - 0.07f, zb), new Vector3(cx + 0.07f, yb + 0.03f, pz + 0.085f)));
            // mast column and carriage plate
            into.Add(Obb.In(t, new Vector3(cx - 0.04f, cH + 0.04f, pz - 0.04f), new Vector3(cx + 0.04f, cH + mastLen, pz + 0.04f)));
            into.Add(Obb.In(t, new Vector3(cx - 0.09f, cH, pz - 0.2f), new Vector3(cx + 0.09f, cH + 0.045f, pz + 0.2f)));
        }

        public float ceiling;
        List<(Obb box, string what)> _furniture;

        /// <summary>Everything else the arm must miss: this unit's own geometry (carcass and drawer
        /// fronts; shelf frames, beams, decks, containers, header label), neighbouring units, their
        /// mounts, the room's walls, floor and ceiling.</summary>
        public List<(Obb box, string what)> Furniture(Game g)
        {
            if (_furniture != null) return _furniture;
            var f = new List<(Obb, string)>();
            var t = su.transform;
            float w = su.size.x, d = su.size.z, h = su.size.y;
            if (cabinet)
                f.Add((Obb.In(t, new Vector3(-w / 2, 0, -d / 2), new Vector3(w / 2, top, d / 2 + 0.035f)), unit + " carcass and drawer fronts"));
            else
            {
                foreach (var sx in new[] { -1f, 1f })
                    f.Add((Obb.In(t, new Vector3(sx * (w / 2 - 0.08f), 0, -d / 2), new Vector3(sx * w / 2, h, d / 2)), unit + " end frame"));
                foreach (var lo in Json.Arr(su.unit["levels"]))
                {
                    float lv = Json.F(lo);
                    foreach (var sz in new[] { -1f, 1f })
                        f.Add((Obb.In(t, new Vector3(-w / 2, lv - 0.1f, sz * (d / 2 - 0.065f)), new Vector3(w / 2, lv, sz * (d / 2 - 0.015f))), $"{unit} beam {lv:0.00}"));
                    f.Add((Obb.In(t, new Vector3(-w / 2, lv - 0.025f, -d / 2 + 0.03f), new Vector3(w / 2, lv, d / 2 - 0.03f)), $"{unit} deck {lv:0.00}"));
                }
                var levels = Json.Arr(su.unit["levels"]);
                foreach (var dd in su.dests)
                {
                    string kind = Json.Str(dd, "kind");
                    if (kind == "shelf_section") continue;
                    float lv = Json.F(levels[Json.Int(dd, "level")]), x = -w / 2 + (float)Json.Num(dd, "x");
                    var inner = StorageUnit.Inner(dd);
                    float rim = lv + RimOffset(kind) + inner.y;
                    f.Add((Obb.In(t, new Vector3(x - inner.x / 2 - 0.012f, lv, -inner.z / 2 - 0.012f), new Vector3(x + inner.x / 2 + 0.012f, rim, inner.z / 2 + 0.012f)), Json.Str(dd, "id")));
                }
                f.Add((Obb.In(t, new Vector3(-0.8f, h + 0.005f, d / 2 - 0.01f), new Vector3(0.8f, h + 0.195f, d / 2 + 0.01f)), unit + " header label"));
            }
            var me = t.position;
            foreach (var o in g.units.Values)
            {
                if (o == su || (o.transform.position - me).magnitude > su.size.x / 2 + o.size.x / 2 + 2.5f) continue;
                float oh = o.size.y + (o.type == "shelving" ? 0.2f : 0.06f);
                f.Add((Obb.In(o.transform, new Vector3(-o.size.x / 2, 0, -o.size.z / 2), new Vector3(o.size.x / 2, oh, o.size.z / 2 + 0.035f)), "unit " + o.id));
            }
            foreach (var s in g.sockets.Values)
            {
                if (s == this || (s.su.transform.position - me).magnitude > su.size.x / 2 + s.su.size.x / 2 + 2.5f) continue;
                var mb = new List<Obb>();
                s.MountBoxes(mb);
                foreach (var b in mb) f.Add((b, "mount " + s.id));
            }
            var lo3 = new Vector3(Mathf.Min(g.layout.roomMin.x, g.layout.roomMax.x), 0, Mathf.Min(-g.layout.roomMin.z, -g.layout.roomMax.z));
            var hi3 = new Vector3(Mathf.Max(g.layout.roomMin.x, g.layout.roomMax.x), g.layout.roomMax.y, Mathf.Max(-g.layout.roomMin.z, -g.layout.roomMax.z));
            f.Add((Obb.World(new Vector3(lo3.x - 1, -1, lo3.z - 1), new Vector3(hi3.x + 1, 0, hi3.z + 1)), "floor"));
            f.Add((Obb.World(new Vector3(lo3.x - 1, hi3.y, lo3.z - 1), new Vector3(hi3.x + 1, hi3.y + 1, hi3.z + 1)), "ceiling"));
            f.Add((Obb.World(new Vector3(lo3.x - 1, 0, lo3.z - 1), new Vector3(lo3.x, hi3.y, hi3.z + 1)), "west wall"));
            f.Add((Obb.World(new Vector3(hi3.x, 0, lo3.z - 1), new Vector3(hi3.x + 1, hi3.y, hi3.z + 1)), "east wall"));
            f.Add((Obb.World(new Vector3(lo3.x - 1, 0, lo3.z - 1), new Vector3(hi3.x + 1, hi3.y, lo3.z)), "south wall"));
            f.Add((Obb.World(new Vector3(lo3.x - 1, 0, hi3.z), new Vector3(hi3.x + 1, hi3.y, hi3.z + 1)), "north wall"));
            return _furniture = f;
        }

        /// <summary>An opened drawer of this cabinet (tray and front pulled out 0.26).</summary>
        public Obb OpenTray(string box)
        {
            int slot = su.SlotOf(box);
            var c = su.FrontCenterG(slot);
            return Obb.In(su.transform, new Vector3(c.x - su.front.x / 2, c.y - su.front.y / 2, su.size.z / 2), new Vector3(c.x + su.front.x / 2, c.y + su.front.y / 2, su.size.z / 2 + 0.275f));
        }

        public static float RimOffset(string kind) => kind == "wooden_crate" ? 0.025f : kind == "shelf_section" ? 0 : 0.012f;

        public const float BeamH = 0.14f, BeamBack = 0.15f;
        public static IEnumerable<float> Hangers(float w) => w > 2 ? new[] { -w / 2 + 0.08f, 0, w / 2 - 0.08f } : new[] { -w / 2 + 0.08f, w / 2 - 0.08f };
    }

    /// <summary>The Stationary Sorting Arm (Robot Workshop redesign, Phase C). A Robot with
    /// strategy "stationary": assembled at the Robot Workbench like the others; once built it
    /// comes straight into the player's hands (both of them: nothing else is carried meanwhile,
    /// so only with empty pockets, else it waits folded in front of the bench). One click does
    /// it all: on a free overhead mount above a drawer cabinet or a shelving unit it is mounted,
    /// anywhere else it is set down folded on the floor, and a click on it picks it up again. Mounted, it sorts the parts lying in the staging piles in front of that
    /// unit: it reserves a part (the shared robot reservation set), the mount's trolley and lift
    /// bring the arm down in front of the pile, it lifts the part, carries it to its drawer (opened)
    /// or container and lets go there, storing it through StorageSystem.Deposit — the same storage,
    /// set and XP rules as any robot. A full or blocked destination leaves the part staged.
    /// Detaching waits for the current cycle; the arm can be carried to any other mount. Hoverbot
    /// leaves staged parts of a working arm's unit alone.</summary>
    public class SortingArm : MonoBehaviour
    {
        public enum Place { Floor, Carried, Mounted }
        public Place place = Place.Floor;
        public ArmSocket socket;
        public string idleReason = "";
        public int heldId = -1;
        Robot _r;
        Game _g;
        SortingArmModel _m;
        bool _busy, _detach;
        float _idleT;
        Vector3 _w, _d;   // the wrist target and grip direction now (world)
        public const float BenchScale = 0.42f, CarryScale = 0.3f;
        const float GripLen = SortingArmModel.GripLen;

        public bool Working => place == Place.Mounted && _r.Active && !_r.paused;
        public HashSet<string> Served => socket != null ? _served ??= socket.dests.ToHashSet() : new HashSet<string>();
        HashSet<string> _served;

        public void Init(Robot r, Game g, SortingArmModel m)
        {
            _r = r; _g = g; _m = m;
        }

        // ---------------------------------------------------------------- placement

        /// <summary>Folded on the floor in front of the bench (after activation, recovery or load).</summary>
        public void ToBench() => SetDown(_r.home, _r.homeRot);

        /// <summary>Folded, standing on the floor at `p` (set down by the player, or the bench spot).</summary>
        public void SetDown(Vector3 p, Quaternion rot)
        {
            CancelCycle();
            Unmount();
            place = Place.Floor;
            transform.SetParent(_g.transform, true);
            transform.SetPositionAndRotation(p, rot);
            Show(BenchScale, true);
            _m.Fold(1);
        }

        /// <summary>Where the crosshair finds the arm standing on the floor: its folded model's box.</summary>
        public Bounds FloorBounds()
        {
            var b = new Bounds(transform.position, Vector3.zero);
            foreach (var r in GetComponentsInChildren<Renderer>()) if (r.enabled) b.Encapsulate(r.bounds);
            return b;
        }

        /// <summary>Into the player's hands: both of them, so only with nothing else carried.</summary>
        public bool PickUp()
        {
            if (!_r.Operational || place == Place.Carried) return false;
            if (_g.CarriedArm != null) { _g.hud.Toast("You are already carrying a Sorting Arm"); return false; }
            if (_g.inventory.Parts > 0) { _g.hud.Toast("The Sorting Arm takes both hands — put away the parts you carry first"); return false; }
            if (place == Place.Mounted && _busy) { _detach = true; _g.hud.Toast($"{_r.displayName}: finishing this move, then it's yours"); return true; }
            CancelCycle();
            Unmount();
            place = Place.Carried;
            Show(CarryScale, true);
            _m.Fold(1);
            Sfx.Play("servo", transform.position, -6);
            _g.hud.Toast("Carrying the Sorting Arm — click a free overhead rail mount (green lamp) above a drawer cabinet or a shelf to mount it; click anywhere else to set it down there.", true);
            return true;
        }

        public string Mount(ArmSocket s)
        {
            if (place != Place.Carried) return "Carry a Sorting Arm here first";
            if (s.occupant != null) return "That mount is taken";
            place = Place.Mounted;
            socket = s;
            _served = null;
            s.occupant = this;
            transform.SetParent(_g.transform, true);
            Show(1f, false);
            Pose(s.ParkWrist(s.cx), ParkDir);   // bolts on wherever the carriage stands, then homes when idle
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

        Vector3 ParkDir => (socket.Front + Vector3.up).normalized;

        /// <summary>Puts the wrist at `w` with the gripper along `d`: the carriage moves the arm's
        /// swing plane through the target and the lift sets the shoulder height, the IK does the rest.</summary>
        void Pose(Vector3 w, Vector3 d)
        {
            var (x, H) = socket.CarriageFor(w);
            socket.SetCarriage(x, H);
            transform.SetPositionAndRotation(socket.point, socket.facing);
            _m.ReachPlanar(w, d, socket.baseYaw, SortingArmModel.ClawRoll);
            _w = w; _d = d;
        }

        // ---------------------------------------------------------------- per frame

        public static double cpuMs;
        static readonly System.Diagnostics.Stopwatch _sw = new();

        void LateUpdate()
        {
            if (Game.Booting) return;   // the game is still being built (loading splash)
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
            if (job >= 0) { StartCoroutine(Cycle(job)); return; }
            _idleT = 0.75f;
            if (Mathf.Abs(socket.cx - socket.parkX) > 0.005f || Mathf.Abs(socket.cH - socket.hPark) > 0.005f) StartCoroutine(Home());
        }

        /// <summary>Idle: along the rail to the parking end, folded above the unit.</summary>
        IEnumerator Home()
        {
            _busy = true;
            var to = socket.ParkWrist(socket.parkX);
            yield return Run(new Seg("home", _w, to, _d, ParkDir, 0.6f));
            _busy = false;
        }

        /// <summary>The next staged part for one of its unit's destinations: lying in the staging
        /// pile in front of it (robot-staged, or a pile the player made there), not reserved, not in
        /// anyone's hands, where the pincer can get at it; its destination not blocked by strays
        /// and not full.</summary>
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
            if (full.Values.All(f => f)) { idleReason = "its unit is full or blocked"; return -1; }
            foreach (var box in served)
            {
                if (full[box]) continue;
                var spot = _r.StagingSpot(box);
                if (!spot.HasValue) continue;
                foreach (var id in _g.index.QuerySphere(spot.Value + Vector3.up * 0.15f, StagedReach + 0.2f))
                {
                    var i = _g.world.instances[id];
                    if (_g.robotReserved.Contains(id) || _g.interaction.busy.Contains(id)) continue;
                    var def = _g.world.DefOf(id);
                    if (def.storageBox != box || !CanReach(i)) continue;
                    float d = (i.Position - socket.point).magnitude - _r.FocusBonus(def.id);   // Set Awareness: nearly complete destinations first
                    if (d < bestD) { bestD = d; best = id; }
                }
            }
            idleReason = best < 0 ? "no staged parts in front of its unit" : "";
            return best;
        }

        /// <summary>Can the pincer come straight down on a part lying at `p` (world): in front of the
        /// unit's base (clear of a shelf's bottom beam), inside the rail's range, within arm's reach.</summary>
        public bool Pickable(Vector3 p)
        {
            var l = socket.su.transform.InverseTransformPoint(p);
            if (-l.z - socket.su.size.z / 2 < 0.1f - SlideIn) return false;   // the wrist stays clear of the unit's lowest beam (PickWrist)
            if (l.x < socket.xMin - SlideIn || l.x > socket.xMax + SlideIn) return false;
            return InReach(PickWrist(p));
        }

        /// <summary>How far a part may lie beyond where the wrist can come down (past the rail's end, or closer
        /// to the unit than the wrist may go) and still be gripped off-centre, sliding into the claws as they
        /// lift (playtest 2026-10-29: parts staged 6-9 cm from a shelf were never taken).</summary>
        const float SlideIn = 0.1f;

        bool InReach(Vector3 wrist)
        {
            var (x, H) = socket.CarriageFor(wrist);
            float d = (wrist - (socket.Point(x, H) + Vector3.down * SortingArmModel.ShoulderDrop)).magnitude;
            return d <= SortingArmModel.L1 + SortingArmModel.L2 - 0.01f && d >= Mathf.Abs(SortingArmModel.L1 - SortingArmModel.L2) + 0.03f;
        }

        // ---------------------------------------------------------------- motion plan

        /// <summary>One straight move of the wrist (the grip direction turning over the part of the
        /// move from `turnFrom` on).</summary>
        public readonly struct Seg
        {
            public readonly string name;
            public readonly Vector3 w0, w1, d0, d1;
            public readonly float dur, turnFrom;
            public Seg(string name, Vector3 w0, Vector3 w1, Vector3 d0, Vector3 d1, float dur, float turnFrom = 0)
            {
                this.name = name; this.w0 = w0; this.w1 = w1; this.d0 = d0; this.d1 = d1; this.dur = dur; this.turnFrom = turnFrom;
            }
            public (Vector3 w, Vector3 d) At(float u) =>
                (Vector3.Lerp(w0, w1, u), Vector3.Slerp(d0, d1, Mathf.SmoothStep(0, 1, Mathf.Clamp01((u - turnFrom) / (1 - turnFrom)))));
        }

        /// <summary>The wrist right above a part lying at `p`, claw tips just off the floor, in the swing plane:
        /// a part a little beyond the carriage's travel (a wall close by) or right in front of the unit's base
        /// is gripped off-centre and slides into the claws as they lift.</summary>
        Vector3 PickWrist(Vector3 p)
        {
            var t = socket.su.transform;
            var l = t.InverseTransformPoint(p);
            l.x = Mathf.Clamp(l.x, socket.xMin, socket.xMax);
            l.z = Mathf.Min(l.z, -socket.su.size.z / 2 - 0.1f);   // clear of the unit's lowest beam
            var q = t.TransformPoint(l);
            return new(q.x, Mathf.Max(p.y + GripLen, 0.42f), q.z);
        }

        /// <summary>Where the part is let go for a destination (world): over the opened drawer's tray,
        /// just inside the drawer front; on a shelf just inside the container's front edge, above its
        /// rim (an open shelf section: low over the deck).</summary>
        public Vector3 CarryPoint(string box)
        {
            var d = _g.db.boxes[box];
            var su = socket.su;
            if (su.type == "drawer_cabinet")
            {
                // a drawer beyond the carriage's travel (a wall close by) is filled a little off-centre
                var l = su.transform.InverseTransformPoint(su.DestPoint(d));
                l.x = Mathf.Clamp(l.x, socket.xMin, socket.xMax);
                return su.transform.TransformPoint(l) + socket.Front * 0.1f + Vector3.up * 0.21f;
            }
            float lv = Json.F(Json.Arr(su.unit["levels"])[Json.Int(d, "level")]);
            float x = -su.size.x / 2 + (float)Json.Num(d, "x");
            var inner = StorageUnit.Inner(d);
            string kind = Json.Str(d, "kind");
            // the open claws (±0.2 along the front) keep clear of the end frames: an end container is
            // filled off-centre, still inside it
            x = Mathf.Clamp(Mathf.Clamp(x, -su.size.x / 2 + 0.27f, su.size.x / 2 - 0.27f), x - inner.x / 2 + 0.04f, x + inner.x / 2 - 0.04f);
            var g = kind == "shelf_section" ? new Vector3(x, lv + 0.1f, inner.z / 2 - 0.12f) : new Vector3(x, lv + ArmSocket.RimOffset(kind) + inner.y + 0.075f, inner.z / 2 - 0.06f);
            return su.transform.TransformPoint(Batch.G(Mathf.Clamp(g.x, socket.xMin, socket.xMax), g.y, g.z));
        }

        /// <summary>A full sorting cycle for a part lying at `part` going into `box`, starting from the
        /// wrist pose (w0, d0): over the pile, down, grip, up, out in front of the destination with
        /// the pincer turned level, in, let go, out, and back up to the parked pose at that spot on the
        /// rail. The cycle and the QA both run these segments.</summary>
        public List<Seg> Plan(Vector3 part, string box, Vector3 w0, Vector3 d0)
        {
            var front = socket.Front;
            var into = -front;
            var down = Vector3.down;
            var at = PickWrist(part);
            var above = at + Vector3.up * 0.35f;
            var place = CarryPoint(box) + front * GripLen;
            var approach = place + front * 0.15f;
            var outW = approach + front * 0.25f;   // the claws just clear of the opened drawer / the shelf's front
            var park = socket.ParkWrist(socket.XOf(place));
            float outD = Vector3.Dot(outW - socket.Point(socket.XOf(place), socket.hPark), front);
            var rise = outW + front * Mathf.Max(0, 0.35f - outD);
            rise.y = Mathf.Max(outW.y, park.y - 0.5f);   // straight up first (a low arm never swings out into the aisle), to below the shoulder (the elbow out, not up into the mount)
            return new List<Seg>
            {
                new("travel", w0, above, d0, down, 0.9f),
                new("descend", above, at, down, down, 0.4f),
                new("lift", at, above, down, down, 0.35f),
                new("transport", above, outW, down, into, 0.9f, 0.45f),
                new("approach", outW, approach, into, into, 0.35f),
                new("insert", approach, place, into, into, 0.3f),
                new("withdraw", place, outW, into, into, 0.45f),
                new("retract", outW, rise, into, into, 0.5f),
                new("stow", rise, park, into, ParkDir, 0.5f),
            };
        }

        /// <summary>Cycle speed with the Faster Servos upgrade (never past its limit).</summary>
        float CycleSpeed => Mathf.Max(0.3f, (float)(_g.progression?.RobotStat(_r.type, "cycle_speed", _r.BaseStat("cycle_speed", 1), _r.LimitOf("cycle_speed")) ?? _r.BaseStat("cycle_speed", 1)));

        /// <summary>Segment time: its base time, or longer for long moves (~1.2 m/s at the wrist).</summary>
        static float Duration(Seg s) => Mathf.Max(s.dur, (s.w1 - s.w0).magnitude / 1.2f);

        IEnumerator Run(Seg s)
        {
            float dur = Duration(s) / CycleSpeed;
            for (float t = 0; t < dur; t += Time.deltaTime)
            {
                var (w, d) = s.At(Mathf.SmoothStep(0, 1, t / dur));
                Pose(w, d);
                if (heldId >= 0) HeldFollow();
                yield return null;
                while (_g.state == "paused" || _g.state == "menu") yield return null;
            }
            var (w1, d1) = s.At(1);
            Pose(w1, d1);
            if (heldId >= 0) HeldFollow();
        }

        /// <summary>How far from a destination's staging spot a part staged for it (a robot's overflowing
        /// pile) is still the arm's (m); a part someone else left there counts within 0.5 m.</summary>
        const float StagedReach = 1.0f;

        /// <summary>A part lying in front of its unit that the arm can take: one of its destinations', in that
        /// staging pile (staged for it, or left within 0.5 m of the spot), low, small enough, where the pincer
        /// really gets at it.</summary>
        public bool CanReach(ItemInstance i)
        {
            if (i.state != ItemState.InWorld) return false;
            var def = _g.world.DefOf(i.id);
            if (!def.robotCarry || def.IsRobotPart || def.storageBox == null || !Served.Contains(def.storageBox)) return false;
            if (i.Position.y > 0.7f || ItemDatabase.Extents(def)[0] > _r.MaxItemSize) return false;   // Wide Gripper raises the size
            var spot = _r.StagingSpot(def.storageBox);
            if (!spot.HasValue) return false;
            float off = Vector3.ProjectOnPlane(i.Position - spot.Value, Vector3.up).magnitude;
            if (!(off <= 0.5f || i.owner == "stage:" + def.storageBox && off <= StagedReach)) return false;
            return Pickable(i.Position);   // the pincer must really get there
        }

        /// <summary>QA: busy with a cycle or homing.</summary>
        public bool BusyQA => _busy;

        /// <summary>QA: why NextJob passes over a part lying near one of its destinations' staging spots
        /// ("" = it would take it).</summary>
        public string WhyNot(int id)
        {
            var i = _g.world.instances[id];
            var def = _g.world.DefOf(id);
            var box = def.storageBox;
            if (box == null || !Served.Contains(box)) return "not this unit's";
            if (_g.StraysIn(box).Count > 0) return "destination blocked by strays";
            if (_g.storage.Stored(box).Count >= _g.db.BoxCapacity(box)) return "destination full";
            var spot = _r.StagingSpot(box);
            if (!spot.HasValue) return "no staging spot";
            if (i.state != ItemState.InWorld) return "not lying";
            if (_g.robotReserved.Contains(id)) return "reserved";
            if (_g.interaction.busy.Contains(id)) return "busy";
            if (!def.robotCarry || def.IsRobotPart) return "not for robots";
            if (i.Position.y > 0.7f) return "higher than 0.7 m";
            if (ItemDatabase.Extents(def)[0] > _r.MaxItemSize) return "too big";
            float off = Vector3.ProjectOnPlane(i.Position - spot.Value, Vector3.up).magnitude;
            if (!(off <= 0.5f || i.owner == "stage:" + box && off <= StagedReach)) return $"outside the staging pile ({off:0.00} m from the spot, owner {i.owner})";
            if (!_g.index.QuerySphere(spot.Value + Vector3.up * 0.15f, StagedReach + 0.2f).Contains(id)) return "not in the spatial index";
            if (!Pickable(i.Position))
            {
                var l = socket.su.transform.InverseTransformPoint(i.Position);
                float clear = -l.z - socket.su.size.z / 2;
                if (clear < 0.1f - SlideIn) return $"the pincer can't get there: too close to the unit ({clear * 100:0} cm out, {off:0.00} m from the spot)";
                if (l.x < socket.xMin - SlideIn || l.x > socket.xMax + SlideIn) return $"the pincer can't get there: beyond the rail (x {l.x:0.00} of {socket.xMin:0.00}..{socket.xMax:0.00})";
                var wr = PickWrist(i.Position);
                var (cx, cH) = socket.CarriageFor(wr);
                float d = (wr - (socket.Point(cx, cH) + Vector3.down * SortingArmModel.ShoulderDrop)).magnitude;
                return $"the pincer can't get there: out of reach ({d:0.00} m, arm {SortingArmModel.L1 + SortingArmModel.L2:0.00} m; {clear * 100:0} cm out from the unit, {off:0.00} m from the spot)";
            }
            return "";
        }

        IEnumerator Cycle(int itemId)
        {
            _busy = true;
            _r.status = "sorting";
            _g.robotReserved.Add(itemId);
            var inst = _g.world.instances[itemId];
            var def = _g.world.DefOf(itemId);
            var box = def.storageBox;
            var unit = socket.su;
            var p0 = inst.Position;
            var plan = Plan(p0, box, _w, _d);
            Seg S(string n) => plan.First(s => s.name == n);
            // over the pile and down
            yield return Run(S("travel"));
            yield return Run(S("descend"));
            // close the pincer, lift the part (if it is still there)
            for (float t = 0; t < 0.25f / CycleSpeed; t += Time.deltaTime) { _m.Grip(0.6f * (1 - t / (0.25f / CycleSpeed))); yield return null; }
            _g.robotReserved.Remove(itemId);
            if (inst.state != ItemState.InWorld || _g.interaction.busy.Contains(itemId))
            {
                yield return Run(S("lift"));
                yield return Run(new Seg("retract", _w, socket.ParkWrist(socket.cx), _d, ParkDir, 0.8f));
                Done();
                yield break;
            }
            var from = inst.transform;
            _g.index.Remove(itemId);
            _g.world.SetState(itemId, ItemState.Carried, _r.Owner);
            _g.interaction.SettleNeighbours(from, def);
            heldId = itemId;
            _r.stats.Picked(itemId, Time.time);
            _held = HeldView.MakeModel(def);
            _held.transform.localScale = Vector3.one * def.worldScale;
            _heldOff = Vector3.ProjectOnPlane(p0 - _m.GripPoint, Vector3.up);
            HeldFollow();
            Sfx.Play("pickup", p0, -5);
            // up, out in front of the destination with the pincer level, in
            yield return Run(S("lift"));
            yield return Run(S("transport"));
            yield return Run(S("approach"));
            var r = _g.storage.Check(box, itemId);
            if (r == StorageSystem.Result.Ok && unit.type == "drawer_cabinet") unit.OpenDrawer(unit.SlotOf(box), new List<(Mesh, Material[], Matrix4x4)>(), 1.5f);
            yield return Run(S("insert"));
            // let go: into storage
            for (float t = 0; t < 0.2f / CycleSpeed; t += Time.deltaTime) { _m.Grip(Release * t / (0.2f / CycleSpeed)); yield return null; }
            if (inst.state == ItemState.Carried && inst.owner == _r.Owner)
            {
                if (_g.storage.Deposit(box, itemId, _r.Owner) == StorageSystem.Result.Ok)
                {
                    _r.delivered++;
                    Sfx.Play(unit.type == "drawer_cabinet" ? "drop_drawer" : "drop_floor", CarryPoint(box), -5);
                    unit.RefreshLabels();
                }
                else ReturnToPile(itemId, box);
            }
            DropHeldModel();
            yield return Run(S("withdraw"));
            yield return Run(S("retract"));
            yield return Run(S("stow"));
            Done();
        }

        /// <summary>How far the claws open to let go: enough to free the part, narrow enough to stay
        /// clear of the neighbouring containers and the shelf's end frames.</summary>
        public const float Release = 0.3f;
        static float GripIn(string seg) => seg is "lift" or "transport" or "approach" or "insert" ? 0 : seg is "withdraw" or "retract" or "stow" ? Release : 0.6f;

        void Done()
        {
            _m.Grip(0.6f);
            _busy = false;
            _r.status = "";
            _idleT = 0.15f;
        }

        GameObject _held;
        Vector3 _heldOff;   // a part gripped off-centre, sliding into the claws

        void HeldFollow()
        {
            if (_held == null) return;
            _heldOff = Vector3.MoveTowards(_heldOff, Vector3.zero, 0.12f * CycleSpeed * Time.deltaTime);
            _held.transform.position = _m.GripPoint + _heldOff;
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
            var spot = _r.StagingSpot(box) ?? transform.position;
            var e = ItemDatabase.Extents(def);
            var p = new Vector3(spot.x + Random.Range(-0.1f, 0.1f), e[2] * 0.5f, spot.z + Random.Range(-0.1f, 0.1f));
            _g.world.SetTransform(itemId, Matrix4x4.TRS(p, Quaternion.Euler(0, Random.Range(0, 360f), def.restMode == RestMode.Long ? 90 : 0), Vector3.one * def.worldScale));
            _g.world.SetState(itemId, ItemState.InWorld, "stage:" + box);
            _g.index.Insert(itemId);
            _g.piles.Drop(itemId, default, 0.04f);
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

        // ---------------------------------------------------------------- QA

        public struct ReachReport
        {
            public int targets, poses;
            public float worstErr, mountClear, furnClear, maxJump;
            public string errAt, mountAt, furnAt, jumpAt;
            public override string ToString() =>
                $"{targets} targets, {poses} poses: wrist error {worstErr * 100:0.0} cm ({errAt}), mount clearance {mountClear * 100:0.0} cm ({mountAt}), " +
                $"furniture/wall clearance {furnClear * 100:0.0} cm ({furnAt}), largest joint step {maxJump:0.0}° ({jumpAt})";
        }

        /// <summary>QA (autoplay): runs the whole cycle plan for every destination it serves (from the
        /// parking end of the rail, over the staging pile and four pile offsets, to the drawer or
        /// container and back up), sampling each segment, and reports the worst wrist error (where the
        /// pincer falls short), the smallest gap between the arm's limb boxes and its mount (rail,
        /// trolley, mast), the smallest gap between the limbs, coupler and turret and everything else
        /// (the unit itself, an opened drawer, neighbours and their mounts, walls, floor, ceiling) and
        /// the largest joint step between samples (a snap). Re-parks the arm where it was.</summary>
        public ReachReport ReachTest(int samples = 16)
        {
            var rep = new ReachReport { worstErr = 0, mountClear = float.MaxValue, furnClear = float.MaxValue, maxJump = 0 };
            var furn = socket.Furniture(_g);
            var limbs = new List<Obb>();
            var mount = new List<Obb>();
            float cx0 = socket.cx;
            Quaternion[] prev = null;
            string prevAt = "";
            void Probe(string what, Vector3 w, Vector3 d, Obb? tray)
            {
                Pose(w, d);
                rep.poses++;
                float err = (_m.head.position - w).magnitude;
                if (err > rep.worstErr) { rep.worstErr = err; rep.errAt = what; }
                limbs.Clear(); mount.Clear();
                _m.LimbBoxes(limbs, false);
                socket.MountBoxes(mount);
                for (int i = 0; i < limbs.Count; i++)
                    for (int j = 0; j < mount.Count; j++)
                    {
                        float gap = Obb.Gap(limbs[i], mount[j]);
                        if (gap < rep.mountClear) { rep.mountClear = gap; rep.mountAt = $"{what}, limb {i} / mount part {j}"; }
                    }
                limbs.Clear();
                _m.LimbBoxes(limbs, true);
                for (int i = 0; i < limbs.Count; i++)
                {
                    var l = limbs[i];
                    foreach (var (b, n) in furn)
                    {
                        float gap = Obb.Gap(l, b);
                        if (gap < rep.furnClear) { rep.furnClear = gap; rep.furnAt = $"{what} / {n} (limb {i})"; }
                    }
                    if (tray.HasValue)
                    {
                        float gap = Obb.Gap(l, tray.Value);
                        if (gap < rep.furnClear) { rep.furnClear = gap; rep.furnAt = what + " / opened drawer"; }
                    }
                }
                var rots = _m.JointRots;
                if (prev != null)
                    for (int k = 0; k < 3; k++)
                    {
                        float a = Quaternion.Angle(prev[k], rots[k]);
                        if (a > rep.maxJump) { rep.maxJump = a; rep.jumpAt = prevAt + " → " + what; }
                    }
                prev = rots; prevAt = what;
            }
            void Sweep(Seg s, string box, Obb? tray)
            {
                _m.Grip(GripIn(s.name));
                int n = Mathf.Max(samples, Mathf.CeilToInt(Duration(s) * 30));   // a step is at most 1/30 s of motion: a snap shows, a long move does not
                for (int k = 0; k <= n; k++)
                {
                    var (w, d) = s.At(Mathf.SmoothStep(0, 1, k / (float)n));
                    Probe($"{box} {s.name}", w, d, tray);
                }
            }
            var front = socket.Front;
            var side = socket.su.transform.TransformDirection(Vector3.right);
            foreach (var box in socket.dests)
            {
                var spot = _r.StagingSpot(box);
                if (!spot.HasValue) continue;
                rep.targets++;
                prev = null;
                var pile = spot.Value + Vector3.up * 0.03f;
                Obb? tray = socket.cabinet ? socket.OpenTray(box) : null;
                foreach (var s in Plan(pile, box, socket.ParkWrist(socket.parkX), ParkDir))
                    Sweep(s, box, s.name is "approach" or "insert" or "withdraw" ? tray : null);
                // the rest of the pile: either side (inside the rail's range), further out, closer in
                float inner = 0.1f - (-socket.su.transform.InverseTransformPoint(pile).z - socket.su.size.z / 2);
                foreach (var o in new[] { side * 0.2f, -side * 0.2f, front * 0.25f, front * Mathf.Max(inner, -0.15f) })
                {
                    var p = pile + o;
                    if (!Pickable(p)) continue;
                    prev = null;
                    var plan = Plan(p, box, socket.ParkWrist(socket.parkX), ParkDir);
                    Sweep(plan[1], box + " pile", null);
                    Sweep(plan[2], box + " pile", null);
                }
            }
            prev = null;
            _m.Grip(0.6f);
            Probe("parked", socket.ParkWrist(socket.parkX), ParkDir, null);
            Pose(socket.ParkWrist(cx0), ParkDir);
            return rep;
        }

        /// <summary>QA: every renderer of the arm and its mount's moving parts (world bounds).</summary>
        public IEnumerable<Renderer> VisibleParts() =>
            GetComponentsInChildren<Renderer>().Concat(socket.trolley.GetComponentsInChildren<Renderer>()).Concat(socket.mast.GetComponentsInChildren<Renderer>());

        /// <summary>QA (screenshots): holds the pose at fraction u of one segment of the cycle for
        /// `box` from its staging spot (a cabinet drawer opened for the insert). Pause the arm first.</summary>
        public void QaPose(string box, string seg, float u = 1)
        {
            ParkNow();
            var plan = Plan(_r.StagingSpot(box).Value + Vector3.up * 0.03f, box, _w, _d);
            var (w, d) = plan.First(s => s.name == seg).At(u);
            Pose(w, d);
            if (socket.cabinet && seg is "approach" or "insert" or "withdraw") socket.su.OpenDrawer(socket.su.SlotOf(box), new List<(Mesh, Material[], Matrix4x4)>(), 6f);
        }

        /// <summary>QA: home to the parking end at once (no animation).</summary>
        public void ParkNow()
        {
            CancelCycle();
            StopAllCoroutines();
            _busy = false;
            Pose(socket.ParkWrist(socket.parkX), ParkDir);
            _m.Grip(0.6f);
        }

        // ---------------------------------------------------------------- save / load / UI

        public Dictionary<string, object> ToDict() => new()
        {
            ["place"] = place.ToString(), ["socket"] = place == Place.Mounted && socket != null ? socket.id : "",
            ["floor"] = new List<object> { (double)transform.position.x, (double)transform.position.y, (double)transform.position.z, (double)transform.eulerAngles.y },
        };

        /// <summary>Loads the placement. An arm on the floor comes back where it stood; a carried arm
        /// (or one whose mount is gone / taken, or an older save's "Bench") is set down at the bench; a
        /// part held mid-cycle goes back to its staging pile. A mounted arm comes back parked at its
        /// mount's parking end.</summary>
        public void Load(Dictionary<string, object> d)
        {
            foreach (var inst in _g.world.instances.Where(i => i.state == ItemState.Carried && i.owner == _r.Owner).ToList())
                ReturnToPile(inst.id, _g.world.DefOf(inst.id).storageBox);
            ToBench();
            if (d != null && Json.Str(d, "place") == "Floor" && d.TryGetValue("floor", out var fo) && Json.Arr(fo) is var f && f.Count == 4)
                SetDown(new Vector3(Json.F(f[0]), Json.F(f[1]), Json.F(f[2])), Quaternion.Euler(0, Json.F(f[3]), 0));
            if (d == null || Json.Str(d, "place") != "Mounted") return;
            if (_g.sockets.TryGetValue(Json.Str(d, "socket"), out var s) && s.occupant == null)
            {
                place = Place.Carried;
                s.SetCarriage(s.parkX, s.hPark);
                Mount(s);
            }
        }

        public string Describe()
        {
            switch (place)
            {
                case Place.Floor: return $"{_r.displayName}: folded on the floor — pick it up and carry it to an overhead mount";
                case Place.Carried: return $"{_r.displayName}: in your hands";
            }
            if (_r.paused) return $"{_r.displayName} on {socket.id}: paused";
            return $"{_r.displayName} on {socket.id} ({socket.Label}): " + (_busy && _r.status == "sorting" ? "sorting" : idleReason != "" ? "idle — " + idleReason : "ready") + $" · {_r.delivered} sorted";
        }

        // ---------------------------------------------------------------- mount fixtures

        // unit-local (Godot axes): the mast stands MountFwd in front of the unit's front (clear of a
        // neighbouring shelf, which stands 0.2 further out than a cabinet); parked, the coupler is
        // ParkGap above the unit's top so the folded arm (lowest point ~0.5 under the coupler) clears it
        const float MountFwd = 0.4f, LowCoupler = 2.1f, Collar = 0.07f;

        /// <summary>Builds the overhead mount of one drawer cabinet or shelving unit (from
        /// storage_layout.json): the static rail (I-beam, ceiling hangers, end stops) as one batch,
        /// the trolley and the mast as two moving batches. No colliders, nothing rests on the unit.</summary>
        public static void BuildSockets(Game g, StorageUnit su, string unitId)
        {
            foreach (var kv in g.db.sockets.Where(s => s.Value.unit == unitId))
            {
                var (unit, side, dests) = kv.Value;
                bool cab = su.type == "drawer_cabinet";
                float w = su.size.x, d = su.size.z;
                float top = cab ? su.size.y + 0.06f : su.size.y;                   // cabinet header board / shelf uprights
                float hp = top + (cab ? 0.6f : 0.62f);                             // parked coupler height
                float pz = d / 2 + MountFwd, yb = hp + 0.12f, zb = pz - ArmSocket.BeamBack;
                float hmin = LowCoupler, mastLen = yb + ArmSocket.BeamH + 0.1f - hmin;
                float ceiling = g.layout.roomMax.y;
                var xs = dests.Select(id => cab ? su.FrontCenterG(su.SlotOf(id)).x : -w / 2 + (float)Json.Num(g.db.boxes[id], "x")).ToList();
                // which ends have a wall close by: the arm's upper arm side (0.37 out) faces away from it,
                // and the rail runs a little past the end destinations only where there is room
                float WallGap(float sx)
                {
                    var o = su.transform.TransformPoint(Batch.G(sx * w / 2, 1, 0));
                    var dir = su.transform.TransformDirection(new Vector3(sx, 0, 0));
                    float best = 99;
                    foreach (var (lo, hi) in new[] { (g.layout.roomMin, g.layout.roomMax) })
                    {
                        float x0 = Mathf.Min(lo.x, hi.x), x1 = Mathf.Max(lo.x, hi.x), z0 = Mathf.Min(lo.z, hi.z, -lo.z, -hi.z), z1 = Mathf.Max(lo.z, hi.z, -lo.z, -hi.z);
                        if (dir.x > 0.5f) best = x1 - o.x; else if (dir.x < -0.5f) best = o.x - x0;
                        else if (dir.z > 0.5f) best = z1 - o.z; else if (dir.z < -0.5f) best = o.z - z0;
                    }
                    return best;
                }
                float gapL = WallGap(-1), gapR = WallGap(1);
                float xMin = xs.Min() - (gapL > 0.3f ? 0.1f : 0), xMax = xs.Max() + (gapR > 0.3f ? 0.1f : 0);
                xMin = Mathf.Max(xMin, -w / 2 + 0.13f); xMax = Mathf.Min(xMax, w / 2 - 0.13f);
                // the arm's coupler (0.23 to either side of the carriage) never reaches a wall
                xMin = Mathf.Max(xMin, -w / 2 - gapL + 0.24f); xMax = Mathf.Min(xMax, w / 2 + gapR - 0.24f);
                // baseYaw 180: the upper arm runs on the unit's +x side of the swing plane, 0: on its -x side
                float baseYaw = gapR >= gapL ? 180 : 0;
                float parkX = Mathf.Clamp(0, xMin, xMax);   // parked mid-rail: an idle carriage keeps clear of the neighbouring units' mounts
                var go = new GameObject("Mount_" + kv.Key);
                go.transform.SetParent(su.transform, false);
                var steel = EnvMaterials.Plain(new Color(0.3f, 0.31f, 0.33f), 0.9f, 0.45f);
                var yellow = EnvMaterials.Plain(new Color(0.9f, 0.68f, 0.12f), 0.3f, 0.5f);
                {
                    // the rail: an I-beam over the unit's front, hung from the ceiling, yellow end stops
                    var b = new Batch();
                    float bl = w - 0.02f;
                    b.Box(new Vector3(0, yb + 0.008f, zb), new Vector3(bl, 0.016f, 0.12f), steel);                                   // bottom flange
                    b.Box(new Vector3(0, yb + ArmSocket.BeamH / 2, zb), new Vector3(bl, ArmSocket.BeamH - 0.03f, 0.012f), steel);     // web
                    b.Box(new Vector3(0, yb + ArmSocket.BeamH - 0.008f, zb), new Vector3(bl, 0.016f, 0.12f), steel);                  // top flange
                    b.Box(new Vector3(0, yb + 0.004f, zb + 0.061f), new Vector3(bl, 0.008f, 0.003f), yellow);                         // hazard edge
                    foreach (var sx in new[] { -1f, 1f })
                        b.Box(new Vector3(sx * (bl / 2 - 0.02f), yb - 0.03f, zb), new Vector3(0.04f, 0.06f, 0.1f), yellow);          // end stops
                    foreach (var hx in ArmSocket.Hangers(w))
                    {
                        float rodLen = ceiling - (yb + ArmSocket.BeamH);
                        b.Box(new Vector3(hx, yb + ArmSocket.BeamH + 0.02f, zb), new Vector3(0.06f, 0.04f, 0.16f), steel);           // beam clamp
                        b.Cylinder(new Vector3(hx, yb + ArmSocket.BeamH + rodLen / 2, zb), 0.014f, rodLen, steel);                  // threaded rod
                        b.Box(new Vector3(hx, ceiling - 0.01f, zb), new Vector3(0.16f, 0.02f, 0.16f), steel);                        // ceiling plate
                    }
                    b.Build(go.transform, "MountRail", true);
                }
                var tr = new GameObject("Trolley").transform;
                tr.SetParent(go.transform, false);
                {
                    // trolley: a saddle riding the beam's top flange, side cheeks, a bridge under the beam
                    // to the guide collar the mast slides through, the power box on top
                    var b = new Batch();
                    b.Box(new Vector3(0, yb + ArmSocket.BeamH + 0.03f, zb), new Vector3(0.22f, 0.04f, 0.18f), steel);
                    foreach (var sz in new[] { -1f, 1f })
                        b.Box(new Vector3(0, yb + 0.06f, zb + sz * 0.085f), new Vector3(0.2f, 0.2f, 0.012f), steel);
                    b.Box(new Vector3(0, yb - 0.04f, (zb + pz) / 2), new Vector3(2 * Collar, 0.04f, pz - zb + 0.06f), steel);
                    b.Box(new Vector3(0, yb - 0.02f, pz), new Vector3(2 * Collar, 0.1f, 2 * Collar), steel);
                    b.Box(new Vector3(0, yb + ArmSocket.BeamH + 0.09f, zb), new Vector3(0.12f, 0.08f, 0.1f), steel);
                    b.Build(tr, "MountTrolley", true);
                }
                var ms = new GameObject("Mast").transform;
                ms.SetParent(go.transform, false);
                {
                    // the lift mast: a square column with a yellow-banded foot, the carriage plate the
                    // coupler bolts under
                    var b = new Batch();
                    b.Box(new Vector3(0, 0.045f + mastLen / 2, pz), new Vector3(0.08f, mastLen, 0.08f), steel);
                    b.Box(new Vector3(0, mastLen + 0.05f, pz), new Vector3(0.1f, 0.02f, 0.1f), steel);
                    b.Box(new Vector3(0, 0.1f, pz), new Vector3(0.084f, 0.06f, 0.084f), yellow);
                    b.Box(new Vector3(0, 0.02f, pz), new Vector3(0.18f, 0.04f, 0.4f), steel);
                    b.Box(new Vector3(0, 0.02f, pz + 0.201f), new Vector3(0.14f, 0.012f, 0.004f), yellow);
                    b.Build(ms, "MountMast", true);
                }
                var s = go.AddComponent<ArmSocket>();
                s.id = kv.Key; s.unit = unit; s.side = side; s.dests = dests; s.su = su; s.cabinet = cab;
                s.xMin = xMin; s.xMax = xMax; s.parkX = parkX; s.hMin = hmin; s.hPark = hp; s.pz = pz; s.yb = yb; s.mastLen = mastLen;
                s.baseYaw = baseYaw; s.top = top; s.ceiling = ceiling; s.trolley = tr; s.mast = ms;
                var lamp = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                Object.Destroy(lamp.GetComponent<Collider>());
                lamp.transform.SetParent(tr, false);
                lamp.transform.localPosition = Batch.G(0, yb - 0.02f, pz + Collar + 0.012f);   // on the collar, facing the aisle
                lamp.transform.localScale = Vector3.one * 0.035f;
                s.lamp = lamp.GetComponent<Renderer>();
                s.lamp.shadowCastingMode = ShadowCastingMode.Off;
                s.SetLamp();
                // aim target (the Target layer, like storage destinations): the carriage and the arm's turret
                var tgt = new GameObject("SocketTarget");
                tgt.layer = Layers.Target;
                tgt.transform.SetParent(ms, false);
                tgt.transform.localPosition = Batch.G(0, -0.15f, pz);
                var col = tgt.AddComponent<BoxCollider>();
                col.isTrigger = true;
                col.size = new Vector3(0.5f, 0.5f, 0.46f);
                var st = tgt.AddComponent<StorageTarget>();
                st.unit = su;
                st.destId = "SOCKET:" + kv.Key;
                s.SetCarriage(parkX, hp);
                g.sockets[kv.Key] = s;
            }
        }
    }
}
