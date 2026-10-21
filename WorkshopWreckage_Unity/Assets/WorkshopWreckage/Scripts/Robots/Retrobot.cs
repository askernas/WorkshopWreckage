using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace WorkshopWreckage
{
    /// <summary>Retrobot (design brief §13.1): bipedal helper assembled from 5 parts at
    /// the repair bay. Inactive until every part is installed; then a short activation
    /// sequence plays and he gets to work on his own: he walks (slower than the
    /// player) to the nearest unsorted part he can reach, bends down and picks it up,
    /// carries it in his hands — he always knows where it belongs — walks to its
    /// destination, stores it, and chugs on to the next one, one part at a time.
    /// R (aiming at a part): work on that kind of part next. T: work / follow me.
    /// Animation: feet step whenever he moves or turns and come to rest parallel;
    /// empty arms swing opposite the legs; carrying arms hold the part forward.</summary>
    public class Retrobot : MonoBehaviour
    {
        public enum Mode { Assembling, Activating, Working, Following }
        enum Step { Choose, ToItem, Picking, ToDest, Depositing }

        public const string Id = "retrobot";
        public const string Owner = "robot:" + Id;
        public Mode mode = Mode.Assembling;
        Step _step = Step.Choose;
        public int carriedId = -1;
        public int targetId = -1;
        public int preferDef = -1;
        public string status = "";
        public int delivered;
        public int staged;                             // parts left in a pile below a destination he can't reach
        public int lastStagedId = -1;
        public bool IsPiledBelowDestination(int id)
        {
            var inst = g.world.instances[id];
            var spot = StagingSpot(g.world.DefOf(id).storageBox);
            return inst.state == ItemState.InWorld && spot.HasValue && Flat(inst.Position - spot.Value).magnitude <= 0.45f && !Pickable(inst);
        }

        Game g;
        NavGrid _nav;
        RetrobotModel _model;
        List<Vector3> _path;
        int _pathIdx;
        float _speedNow, _turnRate, _phase, _gait, _repathT, _idleT;
        float _bend, _armHold, _armReach, _armLift;         // animation channels, 0..1
        float _walk, _turn, _reach, _maxPickH, _followDist, _radius, _pickTime, _depositTime, _height, _maxItem, _stepH;
        const float ModelHeight = 1.05f;               // the model is authored at ~1 m and scaled to `height`
        bool _destReachable;
        readonly Dictionary<string, Vector3?> _stagingSpot = new();
        readonly HashSet<int> _unreachable = new();
        GameObject _carryModel;
        string _destBox;
        StorageUnit _destUnit;
        Vector3 _destPoint, _approach;

        public bool Active => mode == Mode.Working || mode == Mode.Following;
        public string StepName => _step.ToString();
        public float Speed => _speedNow;
        public float WalkSpeed => _walk;
        public bool IsReserved(int id) => id == targetId;

        public static Dictionary<string, object> Config()
        {
            var d = Shared.ReadObject(Shared.DataPath("robots.json"));
            return Json.Obj(Json.Obj(d["robots"])[Id]);
        }

        public void Init(Game game, Vector3 home, Quaternion rot)
        {
            g = game;
            var c = Config();
            _walk = (float)Json.Num(c, "walk_speed", 1.1);
            _turn = (float)Json.Num(c, "turn_speed_deg", 240);
            _reach = (float)Json.Num(c, "reach", 0.8);
            _height = (float)Json.Num(c, "height", 0.3);
            _maxPickH = (float)Json.Num(c, "reach_height", 0.35);
            _maxItem = (float)Json.Num(c, "max_item_size", 0.33);
            _stepH = (float)Json.Num(c, "heap_step_height", 0.18);
            _followDist = (float)Json.Num(c, "follow_distance", 1.6);
            _radius = (float)Json.Num(c, "radius", 0.24);
            _pickTime = (float)Json.Num(c, "pick_time", 1.1);
            _depositTime = (float)Json.Num(c, "deposit_time", 0.9);
            _nav = new NavGrid(g.layout, _radius, _height);
            _nav.RefreshItems(g.world, _stepH);
            transform.SetPositionAndRotation(home, rot);
            _model = new GameObject("Model").AddComponent<RetrobotModel>();
            _model.transform.SetParent(transform, false);
            _model.Init();
            _model.transform.localScale = Vector3.one * (_height / ModelHeight);
            RefreshAssembly(false);
        }

        // ---------------------------------------------------------------- assembly

        public HashSet<string> Installed()
        {
            var o = new HashSet<string>();
            foreach (var id in g.storage.Stored("RS-" + Id)) o.Add(PartOf(g.world.DefOf(id)));
            return o;
        }

        public static string PartOf(ItemDefinition d)
        {
            var n = d.displayName.ToLowerInvariant();
            if (n.Contains("head")) return RetrobotModel.Head;
            if (n.Contains("torso")) return RetrobotModel.Torso;
            if (n.Contains("left arm")) return RetrobotModel.ArmL;
            if (n.Contains("right arm")) return RetrobotModel.ArmR;
            return RetrobotModel.Legs;
        }

        /// <summary>Called whenever the station contents change. Installing the final
        /// part plays the activation sequence (silent activation when loading a save).</summary>
        public void RefreshAssembly(bool animate)
        {
            var have = Installed();
            if (mode == Mode.Assembling) _model.Build(have);
            bool complete = RetrobotModel.Parts.All(have.Contains);
            status = complete ? "" : $"{have.Count}/{RetrobotModel.Parts.Length} parts installed";
            if (!complete || mode != Mode.Assembling) return;
            if (animate) StartCoroutine(Activate());
            else { _model.SetEyes(true); mode = Mode.Working; AdoptCarried(); }
        }

        IEnumerator Activate()
        {
            mode = Mode.Activating;
            Sfx.Play("robot_boot", transform.position + Vector3.up * 0.6f, -2, 0);
            float[] flicker = { 0.12f, 0.08f, 0.18f, 0.06f, 0.1f, 0.3f };
            for (int i = 0; i < flicker.Length; i++) { _model.SetEyes(i % 2 == 1); yield return new WaitForSeconds(flicker[i]); }
            _model.SetEyes(true);
            for (int i = 0; i < 4; i++)
            {
                _model.SetBeacon(true);
                Sfx.Play("servo", transform.position + Vector3.up * 0.8f, -10);
                yield return new WaitForSeconds(0.14f);
                _model.SetBeacon(false);
                yield return new WaitForSeconds(0.1f);
            }
            for (float t = 0; t < 1.2f; t += Time.deltaTime)
            {
                _model.head.localRotation = Quaternion.Euler(0, Mathf.Sin(t * 7) * 35 * (1 - t / 1.2f), 0);
                _model.armL.localRotation = Quaternion.Euler(-Mathf.Sin(t * 5) * 50, 0, 0);
                _model.armR.localRotation = Quaternion.Euler(Mathf.Sin(t * 5) * 50, 0, 0);
                yield return null;
            }
            for (float t = 0; t < 0.4f; t += Time.deltaTime)
            {
                _model.body.localPosition = new Vector3(0, Mathf.Sin(t / 0.4f * Mathf.PI) * 0.12f, 0);
                yield return null;
            }
            _model.body.localPosition = Vector3.zero;
            Sfx.Play("complete", transform.position + Vector3.up, -2);
            mode = Mode.Working;
            _step = Step.Choose;
            g.progression?.OnRobotRepaired(Id);
            g.hud.Toast("Retrobot is online and starts sorting!  R: work on the part you aim at  ·  T: work / follow me", true);
        }

        /// <summary>After loading: a part saved as carried by Retrobot is his again.</summary>
        void AdoptCarried()
        {
            foreach (var inst in g.world.instances)
                if (inst.state == ItemState.Carried && inst.owner == Owner)
                {
                    if (carriedId < 0) { carriedId = inst.id; ShowCarried(inst.id); _step = Step.ToDest; _path = null; }
                    else { g.world.SetState(inst.id, ItemState.InWorld, "world"); g.index.Insert(inst.id); }   // older saves: extra parts go back on the floor
                }
        }

        // ---------------------------------------------------------------- commands

        public string CommandFocus(int defId)
        {
            if (!Active) return $"Retrobot isn't assembled yet ({Installed().Count}/{RetrobotModel.Parts.Length} parts at the repair bay)";
            if (g.db.Get(defId).IsRobotPart) return "Retrobot leaves robot parts to you";
            preferDef = defId;
            if (mode != Mode.Working) mode = Mode.Working;
            if (carriedId < 0 && _step != Step.Picking) { _step = Step.Choose; targetId = -1; _path = null; }
            Sfx.Play("servo", transform.position + Vector3.up * 0.8f, -6);
            return $"Retrobot: on it — {g.db.Get(defId).displayName}";
        }

        public void ToggleFollow()
        {
            if (!Active) return;
            mode = mode == Mode.Working ? Mode.Following : Mode.Working;
            _path = null;
            if (mode == Mode.Following && carriedId < 0) { targetId = -1; _step = Step.Choose; }
            g.hud.Toast(mode == Mode.Following ? "Retrobot: following you" : "Retrobot: back to sorting");
        }

        // ---------------------------------------------------------------- update

        void Update()
        {
            if (g == null || g.world == null) return;
            float dt = Time.deltaTime;
            var rot0 = transform.rotation;
            if (mode == Mode.Working) UpdateWork(dt);
            else if (mode == Mode.Following) UpdateFollow(dt);
            else Brake(dt);
            _turnRate = Quaternion.Angle(rot0, transform.rotation) / Mathf.Max(dt, 1e-4f);
            Animate(dt);
        }

        void UpdateFollow(float dt)
        {
            if (carriedId >= 0) { UpdateWork(dt); return; }   // finishes the delivery first
            var p = g.player.transform.position;
            float d = Flat(p - transform.position).magnitude;
            if (d < _followDist) { Brake(dt); FaceTowards(p, dt); return; }
            _repathT -= dt;
            if (_path == null || _repathT <= 0)
            {
                _repathT = 0.5f;
                var goal = _nav.NearestFree(p - Flat(p - transform.position).normalized * (_followDist * 0.7f), 1.2f);
                _path = goal.HasValue ? _nav.Path(transform.position, goal.Value) : null;
                _pathIdx = 0;
            }
            if (_path != null) Steer(dt); else Brake(dt);
        }

        void UpdateWork(float dt)
        {
            switch (_step)
            {
                case Step.Choose:
                    Brake(dt);
                    _idleT -= dt;
                    if (_idleT <= 0) ChooseNext();
                    break;
                case Step.ToItem:
                    if (targetId < 0 || g.world.instances[targetId].state != ItemState.InWorld) { _step = Step.Choose; _path = null; break; }   // the player took it
                    var ip = g.world.instances[targetId].Position;
                    if (Flat(ip - transform.position).magnitude <= _reach * 0.75f || (Steer(dt) && Flat(ip - transform.position).magnitude <= _reach + 0.1f))
                    {
                        _step = Step.Picking;
                        StartCoroutine(PickUp(targetId));
                    }
                    else if (_pathIdx >= _path.Count) { _unreachable.Add(targetId); targetId = -1; _step = Step.Choose; }
                    break;
                case Step.ToDest:
                    if (_path == null && !PlanDelivery()) { DropCarried("Retrobot can't reach where that part belongs"); break; }
                    if (Steer(dt))
                    {
                        _step = Step.Depositing;
                        StartCoroutine(Deposit());
                    }
                    break;
                default:
                    Brake(dt);
                    break;
            }
        }

        /// <summary>Nearest unsorted part he can reach (preferring the kind he was asked for).</summary>
        void ChooseNext()
        {
            var me = transform.position;
            _nav.RefreshItems(g.world, _stepH);
            IEnumerable<ItemInstance> pool = g.world.instances;
            if (preferDef >= 0)
            {
                var pref = g.world.IdsOf(preferDef).Select(i => g.world.instances[i]).Where(Pickable).ToList();
                if (pref.Count > 0) pool = pref; else preferDef = -1;
            }
            int tries = 0;
            foreach (var inst in pool.Where(Pickable).OrderBy(i => (i.Position - me).sqrMagnitude))
            {
                if (++tries > 6) break;   // keep the frame cheap; continue next frame
                var goal = _nav.NearestFree(inst.Position, _reach * 0.7f) ?? _nav.NearestFree(inst.Position, _reach);
                var path = goal.HasValue ? _nav.Path(me, goal.Value) : null;
                if (path == null) { _unreachable.Add(inst.id); continue; }
                targetId = inst.id;
                _path = path;
                _pathIdx = 0;
                _step = Step.ToItem;
                return;
            }
            _idleT = 0.5f;
            if (tries == 0) status = "nothing left he can reach";
        }

        /// <summary>He only picks up parts lying within his reach height that are no
        /// bigger than he is, and never parts he already piled below their destination.</summary>
        bool Pickable(ItemInstance i)
        {
            if (i.state != ItemState.InWorld || i.Position.y > _maxPickH || _unreachable.Contains(i.id)) return false;
            var def = g.db.Get(i.defId);
            if (def.IsRobotPart || g.interaction.busy.Contains(i.id) || ItemDatabase.Extents(def)[0] > _maxItem) return false;
            var spot = StagingSpot(def.storageBox);
            if (spot == null || ReachableDest(def.storageBox)) return true;
            return Flat(i.Position - spot.Value).magnitude > 0.45f;   // already waiting in his pile below a high destination
        }

        bool ReachableDest(string box) => g.db.boxes.TryGetValue(box, out var d) && Json.Num(d, "elevation_m") <= _maxPickH;

        /// <summary>Floor point in front of a destination (where he stands / piles parts).</summary>
        Vector3? StagingSpot(string box)
        {
            if (_stagingSpot.TryGetValue(box, out var v)) return v;
            Vector3? o = null;
            if (g.db.boxes.TryGetValue(box, out var d) && g.units.TryGetValue(Json.Str(d, "unit"), out var unit))
            {
                var p = unit.DestPoint(d);
                var front = Flat(unit.transform.TransformDirection(new Vector3(0, 0, -1))).normalized;   // units face Godot +Z
                float depth = (float)Json.Num(g.db.Unit(Json.Str(d, "unit")), "depth", 0.6);
                o = new Vector3(p.x, 0, p.z) + front * (depth * 0.5f + 0.14f);
            }
            _stagingSpot[box] = o;
            return o;
        }

        IEnumerator PickUp(int id)
        {
            var ip = g.world.instances[id].Position;
            float h = Mathf.Clamp01(1 - ip.y / 0.9f);          // lower parts need a deeper bend
            for (float t = 0; t < _pickTime * 0.45f; t += Time.deltaTime)
            {
                FaceTowards(ip, Time.deltaTime * 2);
                float u = t / (_pickTime * 0.45f);
                _bend = Mathf.SmoothStep(0, 0.35f + 0.65f * h, u);
                _armReach = Mathf.SmoothStep(0, 1, u);
                yield return null;
            }
            var inst = g.world.instances[id];
            if (inst.state == ItemState.InWorld)
            {
                var from = inst.transform;
                g.index.Remove(id);
                g.world.SetState(id, ItemState.Carried, Owner);
                g.interaction.SettleNeighbours(from, g.world.DefOf(id));
                carriedId = id;
                ShowCarried(id);
                Sfx.Play("pickup", ip, -4);
            }
            float b0 = _bend;
            for (float t = 0; t < _pickTime * 0.55f; t += Time.deltaTime)
            {
                float u = t / (_pickTime * 0.55f);
                _bend = Mathf.Lerp(b0, 0, Mathf.SmoothStep(0, 1, u));
                _armReach = 1 - u;
                yield return null;
            }
            _bend = 0; _armReach = 0;
            targetId = -1;
            _path = null;
            _step = carriedId >= 0 ? Step.ToDest : Step.Choose;
        }

        bool PlanDelivery()
        {
            var box = g.world.DefOf(carriedId).storageBox;
            if (!g.db.boxes.TryGetValue(box, out var d) || !g.units.TryGetValue(Json.Str(d, "unit"), out var unit)) return false;
            _destBox = box;
            _destUnit = unit;
            _destPoint = unit.DestPoint(d);
            _destReachable = ReachableDest(box);
            var front = Flat(unit.transform.TransformDirection(new Vector3(0, 0, -1))).normalized;   // units face Godot +Z
            var want = StagingSpot(box).Value + front * (_radius + 0.12f);
            var a = _nav.NearestFree(want, 0.5f) ?? _nav.NearestFree(want, 1.5f);
            if (a == null) return false;
            _approach = a.Value;
            _path = _nav.Path(transform.position, _approach);
            _pathIdx = 0;
            return _path != null;
        }

        IEnumerator Deposit()
        {
            if (!_destReachable) { yield return StageBelow(); yield break; }
            float high = Mathf.Clamp01((_destPoint.y - 0.5f) / 1.4f);   // reach up for high shelves and drawers
            for (float t = 0; t < _depositTime * 0.5f; t += Time.deltaTime)
            {
                FaceTowards(_destPoint, Time.deltaTime * 2);
                float u = Mathf.SmoothStep(0, 1, t / (_depositTime * 0.5f));
                _armLift = u * (0.4f + 0.6f * high);
                yield return null;
            }
            var id = carriedId;
            var r = g.storage.Deposit(_destBox, id, Owner);
            if (r == StorageSystem.Result.Ok)
            {
                HideCarried();
                carriedId = -1;
                delivered++;
                _destUnit.RefreshLabels();
                Sfx.Play("drop_drawer", _destPoint, -5);
            }
            float l0 = _armLift;
            for (float t = 0; t < _depositTime * 0.5f; t += Time.deltaTime)
            {
                _armLift = Mathf.Lerp(l0, 0, t / (_depositTime * 0.5f));
                yield return null;
            }
            _armLift = 0;
            _path = null;
            if (carriedId >= 0) DropCarried("Retrobot: that destination is full");
            _step = Step.Choose;
            _idleT = 0.15f;
        }

        /// <summary>Destination too high for him: he sets the part down in a little pile
        /// on the floor in front of it. It stays UNSORTED until someone taller stores it.</summary>
        IEnumerator StageBelow()
        {
            var spot = StagingSpot(_destBox).Value;
            for (float t = 0; t < _depositTime * 0.5f; t += Time.deltaTime)
            {
                FaceTowards(_destPoint, Time.deltaTime * 2);
                _bend = Mathf.SmoothStep(0, 0.6f, t / (_depositTime * 0.5f));
                _armReach = _bend / 0.6f;
                yield return null;
            }
            int id = carriedId;
            var def = g.world.DefOf(id);
            var e = ItemDatabase.Extents(def);
            int nearby = g.index.QuerySphere(spot, 0.16f).Count;
            var jitter = new Vector3(Random.Range(-0.1f, 0.1f), 0, Random.Range(-0.1f, 0.1f));
            float y = e[2] * 0.5f + Mathf.Min(nearby, 5) * e[2] * 0.45f;
            var rot = Quaternion.Euler(Random.Range(-12f, 12f), Random.Range(0, 360f), def.restMode == RestMode.Long ? 90 + Random.Range(-10f, 10f) : Random.Range(-12f, 12f));
            HideCarried();
            g.world.SetTransform(id, Matrix4x4.TRS(spot + jitter + Vector3.up * y, rot, Vector3.one * def.worldScale));
            g.world.SetState(id, ItemState.InWorld, "world");
            g.index.Insert(id);
            carriedId = -1;
            staged++;
            lastStagedId = id;
            Sfx.Play("drop_floor", spot, -6);
            for (float t = 0; t < _depositTime * 0.5f; t += Time.deltaTime)
            {
                float u = t / (_depositTime * 0.5f);
                _bend = Mathf.Lerp(0.6f, 0, u);
                _armReach = 1 - u;
                yield return null;
            }
            _bend = 0; _armReach = 0;
            _path = null;
            _step = Step.Choose;
            _idleT = 0.15f;
        }

        void DropCarried(string why)
        {
            if (carriedId < 0) { _step = Step.Choose; return; }
            int id = carriedId;
            var def = g.world.DefOf(id);
            var p = transform.position + transform.forward * 0.35f;
            var b = MeshLibrary.UnitBounds(def.geometryKey).size * def.worldScale;
            var m = Matrix4x4.TRS(new Vector3(p.x, Mathf.Min(b.x, b.y, b.z) * 0.5f, p.z), Quaternion.Euler(0, Random.Range(0, 360f), def.restMode == RestMode.Long ? 90 : 0), Vector3.one * def.worldScale);
            HideCarried();
            g.world.SetTransform(id, m);
            g.world.SetState(id, ItemState.InWorld, "world");
            g.index.Insert(id);
            _unreachable.Add(id);
            carriedId = -1;
            _path = null;
            _step = Step.Choose;
            if (why != "") g.hud.Toast(why);
        }

        // ---------------------------------------------------------------- movement

        static Vector3 Flat(Vector3 v) => new(v.x, 0, v.z);

        /// <summary>Follows the current path; returns true on arrival.</summary>
        bool Steer(float dt)
        {
            if (_path == null) { Brake(dt); return true; }
            while (_pathIdx < _path.Count && Flat(_path[_pathIdx] - transform.position).magnitude < 0.1f) _pathIdx++;
            if (_pathIdx >= _path.Count) { Brake(dt); return true; }
            var to = Flat(_path[_pathIdx] - transform.position);
            FaceTowards(transform.position + to, dt);
            float facing = Vector3.Dot(transform.forward, to.normalized);
            float target = _walk * Mathf.Clamp01(facing * 1.4f) * Mathf.Clamp01(to.magnitude * 2.5f + (_pathIdx < _path.Count - 1 ? 1 : 0));
            _speedNow = Mathf.MoveTowards(_speedNow, target, dt * 2.5f);
            transform.position += transform.forward * (_speedNow * dt);
            return false;
        }

        void Brake(float dt) => _speedNow = Mathf.MoveTowards(_speedNow, 0, dt * 3f);

        void FaceTowards(Vector3 p, float dt)
        {
            var d = Flat(p - transform.position);
            if (d.sqrMagnitude < 1e-4f) return;
            transform.rotation = Quaternion.RotateTowards(transform.rotation, Quaternion.LookRotation(d), _turn * dt);
        }

        // ---------------------------------------------------------------- animation

        void Animate(float dt)
        {
            if (mode == Mode.Assembling || mode == Mode.Activating) return;
            // gait: steps whenever he moves or turns on the spot; settles to parallel legs
            float moving = Mathf.Max(_speedNow / _walk, Mathf.Clamp01(_turnRate / 120f) * 0.6f);
            _gait = Mathf.MoveTowards(_gait, moving > 0.05f ? Mathf.Max(0.45f, moving) : 0, dt * 3f);
            _phase += dt * Mathf.Lerp(5f, 8.5f, Mathf.Clamp01(moving)) * (_gait > 0 ? 1 : 0);
            if (_gait == 0) _phase = Mathf.MoveTowards(_phase, Mathf.Round(_phase / Mathf.PI) * Mathf.PI, dt * 6);   // finish the step
            float s = Mathf.Sin(_phase);
            float swing = s * 28 * _gait;
            float knee = _bend * 35;
            _model.legL.localRotation = Quaternion.Euler(swing - knee, 0, 0);
            _model.legR.localRotation = Quaternion.Euler(-swing - knee, 0, 0);
            _model.body.localPosition = new Vector3(0, Mathf.Abs(Mathf.Cos(_phase)) * 0.022f * _gait - _bend * 0.08f, 0);
            _model.body.localRotation = Quaternion.Euler(0, 0, s * 3 * _gait);   // little side-to-side waddle

            // upper body: bends forward to pick things up, leans a bit when walking
            _model.upper.localRotation = Quaternion.Euler(_bend * 40 + 5 * _gait, 0, 0);

            // arms: swing opposite the legs when empty, hold the part forward when carrying
            _armHold = Mathf.MoveTowards(_armHold, carriedId >= 0 ? 1 : 0, dt * 3f);
            float empty = 1 - Mathf.Max(_armHold, _armReach);
            float holdPitch = -68 * _armHold - 40 * _armLift;
            float reachPitch = -35 * _armReach;
            float lArm = holdPitch + reachPitch + (-swing * 1.1f) * empty;
            float rArm = holdPitch + reachPitch + (swing * 1.1f) * empty;
            float inward = 10 * Mathf.Max(_armHold, _armReach);
            _model.armL.localRotation = Quaternion.Euler(lArm, 0, inward);
            _model.armR.localRotation = Quaternion.Euler(rArm, 0, -inward);

            // head: watches where he is going; looks at the player when idle
            float headYaw = 0, headPitch = _bend * 10;
            if (_gait < 0.05f && _armReach == 0 && _armLift == 0)
            {
                var toP = g.player.transform.position - transform.position;
                headYaw = Mathf.Clamp(Vector3.SignedAngle(transform.forward, Flat(toP), Vector3.up), -60, 60);
            }
            _model.head.localRotation = Quaternion.Slerp(_model.head.localRotation, Quaternion.Euler(headPitch + Mathf.Sin(_phase * 2) * 2.5f * _gait, headYaw, 0), dt * 5);

            // the carried part rides in the hands (raised with the arms when depositing)
            if (_carryModel != null)
                _carryModel.transform.localPosition = new Vector3(0, 0.05f * _armLift, 0.06f * _armLift);
        }

        void ShowCarried(int id)
        {
            HideCarried();
            var def = g.world.DefOf(id);
            _carryModel = HeldView.MakeModel(def);
            _carryModel.transform.SetParent(_model.carry, false);
            var size = MeshLibrary.UnitBounds(def.geometryKey).size * def.worldScale;
            float max = Mathf.Max(size.x, size.y, size.z);
            float modelScale = _height / ModelHeight;
            _carryModel.transform.localScale = Vector3.one * def.worldScale / modelScale;   // true size in his hands
            _carryModel.transform.localRotation = def.restMode == RestMode.Long ? Quaternion.Euler(0, 0, 90) : Quaternion.identity;
        }

        void HideCarried()
        {
            if (_carryModel != null) Destroy(_carryModel);
            _carryModel = null;
        }

        public string Describe()
        {
            switch (mode)
            {
                case Mode.Assembling: return "Retrobot: " + status;
                case Mode.Activating: return "Retrobot: starting up…";
                case Mode.Following: return carriedId >= 0 ? "Retrobot: delivering, then following" : "Retrobot: following you";
            }
            return _step switch
            {
                Step.ToItem => "Retrobot: fetching a part",
                Step.Picking => "Retrobot: picking up",
                Step.ToDest or Step.Depositing => carriedId >= 0 ? $"Retrobot: storing {g.world.DefOf(carriedId).displayName}" : "Retrobot: storing",
                _ => status == "" ? "Retrobot: looking for parts" : "Retrobot: " + status,
            };
        }
    }
}
