using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace WorkshopWreckage
{
    /// <summary>One of the five toy robots (design brief §13–16), configured by
    /// shared/data/robots.json. Inactive until every assembly part is installed at
    /// its repair-bay station; then an activation sequence plays and it works on its
    /// own: it gathers unsorted parts it can reach (up to its capacity and number of
    /// part types), always knowing where each belongs, and delivers them. Ground
    /// robots path-find on the floor grid; Hoverbot flies. Destinations above a
    /// robot's reach get the part piled on the floor in front of them (still
    /// unsorted) — a robot that can reach them (Hoverbot) picks those piles up later.
    /// Two robots never go for the same part (shared reservations).
    /// Commands: R (aiming at a part) — nearest robot works on that kind next;
    /// T — nearest robot: work / follow me.</summary>
    public class Robot : MonoBehaviour
    {
        public enum Mode { Assembling, Activating, Working, Following }
        enum Step { Choose, ToItem, Picking, ToDest, Depositing, ToPlayer }

        public string id, displayName, locomotion;
        public string Owner => "robot:" + id;
        public Mode mode = Mode.Assembling;
        Step _step = Step.Choose;
        public readonly List<int> carried = new();
        public int targetId = -1;
        public int preferDef = -1;
        public string status = "";
        public int delivered, staged, lastStagedId = -1;
        public Vector3 home;
        public Vector3? zoneCenter;                    // assigned work zone (Task Coordination skill)
        public float zoneRadius = 3f;
        public Quaternion homeRot;

        Game g;
        NavGrid _nav;
        RobotModel _model;
        List<Vector3> _path;
        int _pathIdx;
        float _speedNow, _turnRate, _phase, _gait, _repathT, _idleT, _altitude, _time, _stepT;
        Step _lastStep;
        float _bend, _armHold, _armReach, _armLift;
        int _capacity, _types;
        float _walk, _turn, _reach, _maxPickH, _followDist, _radius, _pickTime, _depositTime, _height, _maxItem, _stepH, _gather, _cruise;
        bool Flies => locomotion == "hover";
        readonly HashSet<int> _unreachable = new();
        readonly Dictionary<int, GameObject> _carryModels = new();
        readonly Dictionary<string, Vector3?> _stagingSpot = new();
        string _destBox;
        StorageUnit _destUnit;
        Vector3 _destPoint, _hoverTarget;
        bool _destReachable;

        public bool Active => mode == Mode.Working || mode == Mode.Following;
        public string StepName => _step.ToString();
        public float Speed => _speedNow;
        public float WalkSpeed => _walk;
        float Walk => _walk * (1f + (float)(g.progression?.Effect("robot_speed") ?? 0));   // Shift Planner
        public float Height => _height;
        public int StationCapacity => g.db.BoxCapacity("RS-" + id);

        public static Dictionary<string, object> AllConfig() => Json.Obj(Shared.ReadObject(Shared.DataPath("robots.json"))["robots"]);

        public void Init(Game game, string robotId, Vector3 home, Quaternion rot)
        {
            g = game;
            id = robotId;
            var c = Json.Obj(AllConfig()[id]);
            displayName = Json.Str(c, "name", id);
            locomotion = Json.Str(c, "locomotion", "biped");
            _height = (float)Json.Num(c, "height", 0.3);
            _capacity = Json.Int(c, "capacity", 1);
            _types = Json.Int(c, "item_types", 1);
            _walk = (float)Json.Num(c, "walk_speed", 0.7);
            _turn = (float)Json.Num(c, "turn_speed_deg", 260);
            _reach = (float)Json.Num(c, "reach", 0.32);
            _maxPickH = (float)Json.Num(c, "reach_height", 0.35);
            _maxItem = (float)Json.Num(c, "max_item_size", 0.33);
            _stepH = (float)Json.Num(c, "heap_step_height", 0.18);
            _gather = (float)Json.Num(c, "gather_radius", 3);
            _cruise = (float)Json.Num(c, "cruise_altitude", 2.9);
            _pickTime = (float)Json.Num(c, "pick_time", 1.1);
            _depositTime = (float)Json.Num(c, "deposit_time", 0.9);
            _followDist = (float)Json.Num(c, "follow_distance", 1.1);
            _radius = (float)Json.Num(c, "radius", 0.12);
            if (!Flies)
            {
                _nav = new NavGrid(g.layout, _radius, _height);
                _nav.RefreshItems(g.world, _stepH);
            }
            transform.SetPositionAndRotation(home, rot);
            this.home = home;
            homeRot = rot;
            _model = id switch
            {
                "unibot" => new GameObject("Model").AddComponent<UnibotModel>(),
                "spiderbot" => new GameObject("Model").AddComponent<SpiderbotModel>(),
                "caterpillarbot" => new GameObject("Model").AddComponent<CaterpillarbotModel>(),
                "hoverbot" => new GameObject("Model").AddComponent<HoverbotModel>(),
                _ => (RobotModel)new GameObject("Model").AddComponent<RetrobotModel>(),
            };
            _model.transform.SetParent(transform, false);
            _model.Init();
            _model.transform.localScale = Vector3.one * (_height / _model.ModelHeight);
            // locomotion sound: steps for walkers, a motor loop for wheels / tracks / rotor
            _loopName = locomotion switch { "wheel" => "loop_wheel", "tracks" => "loop_tracks", "hover" => "loop_rotor", _ => null };
            if (_loopName != null)
            {
                _loop = gameObject.AddComponent<AudioSource>();
                _loop.spatialBlend = 1; _loop.minDistance = 0.6f; _loop.maxDistance = 14; _loop.loop = true; _loop.volume = 0;
                _loop.rolloffMode = AudioRolloffMode.Linear;
            }
            RefreshAssembly(false);
        }

        // ---------------------------------------------------------------- assembly

        /// <summary>Assembly part key of a robot part, e.g. "Unibot Wheel Hub" -> "wheel hub".</summary>
        public static string PartKey(ItemDefinition d)
        {
            var n = d.displayName;
            int sp = n.IndexOf(' ');
            return (sp >= 0 ? n.Substring(sp + 1) : n).ToLowerInvariant();
        }

        public HashSet<string> Installed()
        {
            var o = new HashSet<string>();
            foreach (var i in g.storage.Stored("RS-" + id)) o.Add(PartKey(g.world.DefOf(i)));
            return o;
        }

        public int InstalledCount => g.storage.Stored("RS-" + id).Count;

        /// <summary>Called whenever the station contents change. Installing the final
        /// part plays the activation sequence (silent activation when loading a save).</summary>
        public void RefreshAssembly(bool animate)
        {
            var have = Installed();
            if (mode == Mode.Assembling) _model.Build(have);
            int total = g.db.RobotPartIds(id).Count;
            bool complete = InstalledCount >= total && total > 0;
            status = complete ? "" : $"{InstalledCount}/{total} parts installed";
            if (!complete || mode != Mode.Assembling) return;
            if (animate) StartCoroutine(Activate());
            else { _model.SetEyes(true); mode = Mode.Working; if (Flies) _altitude = 0.6f; AdoptCarried(); }
        }

        IEnumerator Activate()
        {
            mode = Mode.Activating;
            Sfx.Play("robot_boot", transform.position + Vector3.up * 0.3f, -2, 0);
            float[] flicker = { 0.12f, 0.08f, 0.18f, 0.06f, 0.1f, 0.3f };
            for (int i = 0; i < flicker.Length; i++) { _model.SetEyes(i % 2 == 1); yield return new WaitForSeconds(flicker[i]); }
            _model.SetEyes(true);
            for (int i = 0; i < 4; i++)
            {
                _model.SetBeacon(true);
                Sfx.Play("servo", transform.position + Vector3.up * 0.3f, -10);
                yield return new WaitForSeconds(0.14f);
                _model.SetBeacon(false);
                yield return new WaitForSeconds(0.1f);
            }
            // a little "hello": the model wiggles in its own way while it looks around
            for (float t = 0; t < 1.4f; t += Time.deltaTime)
            {
                _time += Time.deltaTime;
                if (Flies) _altitude = Mathf.SmoothStep(0, 0.6f, t / 1.4f);
                _model.Animate(new RobotAnim
                {
                    dt = Time.deltaTime, gait = 0.6f * Mathf.Sin(t / 1.4f * Mathf.PI), phase = t * 9, active = true, time = _time, altitude = _altitude,
                    lookAt = transform.position + Quaternion.Euler(0, Mathf.Sin(t * 6) * 50, 0) * transform.forward, lookWeight = 1,
                });
                if (Flies) transform.position = new Vector3(transform.position.x, Ground(transform.position) + _altitude, transform.position.z);
                yield return null;
            }
            Sfx.Play("complete", transform.position + Vector3.up * 0.3f, -2);
            mode = Mode.Working;
            _step = Step.Choose;
            g.progression?.OnRobotRepaired(id);
            g.hud.Toast($"{displayName} is online and starts sorting!", true);
        }

        /// <summary>After loading: parts saved as carried by this robot are its again.</summary>
        void AdoptCarried()
        {
            foreach (var inst in g.world.instances)
                if (inst.state == ItemState.Carried && inst.owner == Owner && !carried.Contains(inst.id))
                {
                    carried.Add(inst.id);
                    ShowCarried(inst.id);
                }
            if (carried.Count > 0) { _step = Step.ToDest; _path = null; }
        }

        // ---------------------------------------------------------------- commands

        public string CommandFocus(int defId)
        {
            if (!Active) return $"{displayName} isn't assembled yet ({status})";
            if (g.db.Get(defId).IsRobotPart) return $"{displayName} leaves robot parts to you";
            preferDef = defId;
            mode = Mode.Working;
            if (carried.Count == 0 && _step != Step.Picking) { Release(); _step = Step.Choose; _path = null; }
            Sfx.Play("servo", transform.position + Vector3.up * 0.3f, -6);
            return $"{displayName}: on it — {g.db.Get(defId).displayName}";
        }

        /// <summary>Automatic Storage Delivery: the robot comes over, takes what the
        /// player carries (up to its capacity) and stores it.</summary>
        public bool RequestHandover()
        {
            if (!Active) return false;
            mode = Mode.Working;
            if (carried.Count > 0 || _step == Step.Picking || _step == Step.Depositing) { handoverPending = true; return true; }   // after the current delivery
            Release();
            _step = Step.ToPlayer;
            _path = null;
            return true;
        }

        public bool handoverPending;
        AudioSource _loop;
        string _loopName;
        float _lastStepPhase;

        public bool Free => Active && carried.Count == 0 && _step != Step.Picking && _step != Step.Depositing && _step != Step.ToPlayer;

        void TakeFromPlayer()
        {
            int took = 0;
            foreach (var itemId in g.inventory.items.ToList())
            {
                if (carried.Count >= _capacity) break;
                g.inventory.Remove(itemId);
                g.world.SetState(itemId, ItemState.Carried, Owner);
                carried.Add(itemId);
                ShowCarried(itemId);
                took++;
            }
            _path = null;
            _step = carried.Count > 0 ? Step.ToDest : Step.Choose;
            Sfx.Play("pickup", transform.position + Vector3.up * 0.3f, -4);
            g.hud.Toast(took > 0 ? $"{displayName} takes {took} part{(took == 1 ? "" : "s")} to storage" : $"{displayName}: nothing to carry");
            g.telemetry?.SkillUsed("auto_delivery");
        }

        /// <summary>Task Coordination: restrict work to a zone (null clears it).</summary>
        public void AssignZone(Vector3? center)
        {
            zoneCenter = center;
            _unreachable.Clear();
            if (_step == Step.ToItem && targetId >= 0 && center.HasValue && Flat(g.world.instances[targetId].Position - center.Value).magnitude > zoneRadius)
            {
                Release();
                _step = carried.Count > 0 ? Step.ToDest : Step.Choose;
                _path = null;
            }
            mode = Mode.Working;
        }

        public void ToggleFollow()
        {
            if (!Active) return;
            mode = mode == Mode.Working ? Mode.Following : Mode.Working;
            _path = null;
            if (mode == Mode.Following && carried.Count == 0) { Release(); _step = Step.Choose; }
            g.hud.Toast(mode == Mode.Following ? $"{displayName}: following you" : $"{displayName}: back to sorting");
        }

        void Release()
        {
            if (targetId >= 0) g.robotReserved.Remove(targetId);
            targetId = -1;
        }

        void OnDestroy()
        {
            if (g != null && targetId >= 0) g.robotReserved.Remove(targetId);
        }

        // ---------------------------------------------------------------- update

        void Update()
        {
            if (g == null || g.world == null) return;
            if (g.state == "paused" || g.state == "menu") { if (_loop != null) _loop.volume = 0; return; }   // the workshop holds still
            float dt = Time.deltaTime;
            _time += dt;
            var fwd0 = transform.forward;
            if (mode == Mode.Working) UpdateWork(dt);
            else if (mode == Mode.Following) UpdateFollow(dt);
            else Brake(dt);
            _turnRate = Vector3.SignedAngle(fwd0, transform.forward, Vector3.up) / Mathf.Max(dt, 1e-4f);   // + = turning right
            if (!Flies && Active)
            {
                // feet (wheels, tracks) on top of whatever small parts are underfoot
                var p = transform.position;
                float ground = Ground(p);
                p.y = ground > p.y ? Mathf.MoveTowards(p.y, ground, dt * 1.2f) : Mathf.MoveTowards(p.y, ground, dt * 2.5f);
                transform.position = p;
            }
            Animate(dt);
        }

        float Ground(Vector3 p) => g.debris != null ? g.debris.Under(p, _radius * 0.5f) : 0;

        void UpdateFollow(float dt)
        {
            if (carried.Count > 0) { UpdateWork(dt); return; }   // finish the delivery first
            var p = g.player.transform.position;
            float d = Flat(p - transform.position).magnitude;
            if (d < _followDist) { Brake(dt); FaceTowards(p, dt); if (Flies) HoverHold(dt, 1.6f); return; }
            if (Flies) { FlyTo(p - Flat(p - transform.position).normalized * _followDist * 0.8f + Vector3.up * 1.6f, dt, false); return; }
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
            // recovery: a trip that takes far too long is abandoned (blocked path, unreachable spot)
            if (_step != _lastStep) { _lastStep = _step; _stepT = 0; }
            _stepT += dt;
            if (_step == Step.ToItem && _stepT > 25f)
            {
                if (targetId >= 0) _unreachable.Add(targetId);
                Release(); _path = null; _step = carried.Count > 0 ? Step.ToDest : Step.Choose;
                return;
            }
            if (_step == Step.ToDest && _stepT > 60f) { DropCarried($"{displayName} got stuck and set its load down"); return; }
            if (_step == Step.ToPlayer && _stepT > 40f) { _step = Step.Choose; return; }
            switch (_step)
            {
                case Step.Choose:
                    if (handoverPending && carried.Count == 0) { handoverPending = false; _step = Step.ToPlayer; _path = null; break; }
                    Brake(dt);
                    if (Flies) HoverHold(dt, Mathf.Max(0.6f, _altitude));
                    _idleT -= dt;
                    if (_idleT <= 0) ChooseNext();
                    break;
                case Step.ToItem:
                {
                    if (targetId < 0 || g.world.instances[targetId].state != ItemState.InWorld) { Release(); _step = carried.Count > 0 ? Step.ToDest : Step.Choose; _path = null; break; }
                    var ip = g.world.instances[targetId].Position;
                    bool arrived = Flies ? FlyTo(_hoverTarget, dt, true) : (Flat(ip - transform.position).magnitude <= _reach * 0.75f || (Steer(dt) && Flat(ip - transform.position).magnitude <= _reach + 0.1f));
                    if (arrived) { _step = Step.Picking; StartCoroutine(PickUp(targetId)); }
                    else if (!Flies && _pathIdx >= _path.Count) { _unreachable.Add(targetId); Release(); _step = carried.Count > 0 ? Step.ToDest : Step.Choose; }
                    break;
                }
                case Step.ToPlayer:
                {
                    var pp = g.player.transform.position;
                    if (Flat(pp - transform.position).magnitude < 0.9f) { TakeFromPlayer(); break; }
                    if (Flies) { FlyTo(pp - Flat(pp - transform.position).normalized * 0.6f + Vector3.up * 0.9f, dt, false); break; }
                    _repathT -= dt;
                    if (_path == null || _repathT <= 0)
                    {
                        _repathT = 0.5f;
                        var goal = _nav.NearestFree(pp - Flat(pp - transform.position).normalized * 0.5f, 1.0f);
                        _path = goal.HasValue ? _nav.Path(transform.position, goal.Value) : null;
                        _pathIdx = 0;
                        if (_path == null) { _step = Step.Choose; g.hud.Toast($"{displayName} can't get to you"); }
                    }
                    if (_path != null) Steer(dt);
                    break;
                }
                case Step.ToDest:
                    if (carried.Count == 0) { _step = Step.Choose; break; }
                    if (_path == null && !PlanDelivery()) { DropCarried($"{displayName} can't reach where that part belongs"); break; }
                    if (Flies ? FlyTo(_hoverTarget, dt, true) : Steer(dt)) { _step = Step.Depositing; StartCoroutine(Deposit()); }
                    break;
                default:
                    Brake(dt);
                    if (Flies) HoverHold(dt, _altitude);
                    break;
            }
        }

        // ---------------------------------------------------------------- choosing

        /// <summary>Picks the next part: nearest first; once carrying, only parts close
        /// by and within the allowed number of part types (then delivers).</summary>
        void ChooseNext()
        {
            var me = transform.position;
            if (carried.Count >= _capacity) { _step = Step.ToDest; _path = null; return; }
            if (!Flies) _nav.RefreshItems(g.world, _stepH);
            var types = carried.Select(c => g.world.instances[c].defId).ToHashSet();
            IEnumerable<ItemInstance> pool = g.world.instances;
            if (preferDef >= 0)
            {
                var pref = g.world.IdsOf(preferDef).Select(i => g.world.instances[i]).Where(Pickable).ToList();
                if (pref.Count > 0) pool = pref; else preferDef = -1;
            }
            int tries = 0;
            foreach (var inst in pool.Where(Pickable).OrderBy(i => (i.Position - me).sqrMagnitude))
            {
                if (carried.Count > 0 && ((inst.Position - me).magnitude > _gather || (!types.Contains(inst.defId) && types.Count >= _types))) break;
                if (++tries > 6) break;   // keep the frame cheap; continue next frame
                if (Flies)
                {
                    _hoverTarget = HoverPoint(inst.Position, true);
                }
                else
                {
                    var goal = _nav.NearestFree(inst.Position, _reach * 0.7f) ?? _nav.NearestFree(inst.Position, _reach);
                    var path = goal.HasValue ? _nav.Path(me, goal.Value) : null;
                    if (path == null) { _unreachable.Add(inst.id); continue; }
                    _path = path;
                    _pathIdx = 0;
                }
                targetId = inst.id;
                g.robotReserved.Add(inst.id);
                _step = Step.ToItem;
                return;
            }
            if (carried.Count > 0) { _step = Step.ToDest; _path = null; return; }
            _idleT = 0.5f;
            if (tries == 0) status = "nothing left it can reach";
        }

        /// <summary>A robot picks up only parts within its reach height that are no
        /// bigger than its size limit, never robot parts, never parts another robot
        /// reserved, and never parts it already piled below a destination it can't reach.</summary>
        bool Pickable(ItemInstance i)
        {
            if (i.state != ItemState.InWorld || i.Position.y > _maxPickH || _unreachable.Contains(i.id) || g.robotReserved.Contains(i.id)) return false;
            if (zoneCenter.HasValue && Flat(i.Position - zoneCenter.Value).magnitude > zoneRadius) return false;
            var def = g.db.Get(i.defId);
            if (def.IsRobotPart || g.interaction.busy.Contains(i.id) || ItemDatabase.Extents(def)[0] > _maxItem) return false;
            if (ReachableDest(def.storageBox)) return true;
            var spot = StagingSpot(def.storageBox);
            return spot == null || Flat(i.Position - spot.Value).magnitude > 0.45f;   // already waiting in a pile below a high destination
        }

        public bool ReachableDest(string box) => g.db.boxes.TryGetValue(box, out var d) && Json.Num(d, "elevation_m") <= _maxPickH;

        /// <summary>Floor point in front of a destination (where ground robots stand and pile parts).</summary>
        public Vector3? StagingSpot(string box)
        {
            if (_stagingSpot.TryGetValue(box, out var v)) return v;
            Vector3? o = null;
            if (g.db.boxes.TryGetValue(box, out var d) && g.units.TryGetValue(Json.Str(d, "unit"), out var unit))
            {
                var p = unit.DestPoint(d);
                o = new Vector3(p.x, 0, p.z) + UnitFront(unit) * ((float)Json.Num(g.db.Unit(Json.Str(d, "unit")), "depth", 0.6) * 0.5f + 0.14f);
            }
            _stagingSpot[box] = o;
            return o;
        }

        static Vector3 UnitFront(StorageUnit unit) => Flat(unit.transform.TransformDirection(new Vector3(0, 0, -1))).normalized;   // units face Godot +Z

        public bool IsPiledBelowDestination(int itemId)
        {
            var inst = g.world.instances[itemId];
            var def = g.world.DefOf(itemId);
            var spot = StagingSpot(def.storageBox);
            return inst.state == ItemState.InWorld && spot.HasValue && Flat(inst.Position - spot.Value).magnitude <= 0.45f && !ReachableDest(def.storageBox);
        }

        /// <summary>Where Hoverbot hovers to reach a point: in front of a storage unit
        /// for parts on shelves / destinations, directly above parts on the floor.</summary>
        Vector3 HoverPoint(Vector3 p, bool pickup)
        {
            var unit = g.UnitAt(p);
            float below = _height * 0.55f;   // claws hang below the saucer
            if (unit == null) return new Vector3(p.x, p.y + below, p.z);
            var front = UnitFront(unit);
            float depth = (float)Json.Num(g.db.Unit(unit.name.Replace("Unit_", "")), "depth", 0.6);
            var c = unit.transform.position;
            float along = Vector3.Dot(Flat(p - c), front);
            return new Vector3(p.x, p.y + below * 0.4f, p.z) + front * (depth * 0.5f - along + _radius + 0.08f);
        }

        // ---------------------------------------------------------------- pick / deliver

        IEnumerator PickUp(int itemId)
        {
            var ip = g.world.instances[itemId].Position;
            float h = Mathf.Clamp01(1 - (ip.y - transform.position.y) / (_height * 3f));
            for (float t = 0; t < _pickTime * 0.45f; t += Time.deltaTime)
            {
                FaceTowards(ip, Time.deltaTime * 2);
                float u = t / (_pickTime * 0.45f);
                _bend = Mathf.SmoothStep(0, 0.35f + 0.65f * h, u);
                _armReach = Mathf.SmoothStep(0, 1, u);
                if (Flies) HoverHold(Time.deltaTime, _altitude);
                yield return null;
            }
            var inst = g.world.instances[itemId];
            g.robotReserved.Remove(itemId);
            targetId = -1;
            if (inst.state == ItemState.InWorld)
            {
                var from = inst.transform;
                g.index.Remove(itemId);
                g.world.SetState(itemId, ItemState.Carried, Owner);
                g.interaction.SettleNeighbours(from, g.world.DefOf(itemId));
                carried.Add(itemId);
                ShowCarried(itemId);
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
            _path = null;
            _step = Step.Choose;   // gather more (if allowed), else deliver
            _idleT = 0;
        }

        /// <summary>Plans a trip to the nearest destination of the carried parts.</summary>
        bool PlanDelivery()
        {
            var me = transform.position;
            var boxes = carried.Select(c => g.world.DefOf(c).storageBox).Distinct()
                .Where(b => g.db.boxes.ContainsKey(b) && g.units.ContainsKey(Json.Str(g.db.boxes[b], "unit")))
                .OrderBy(b => ((StagingSpot(b) ?? me) - me).sqrMagnitude).ToList();
            if (boxes.Count == 0) return false;
            var box = boxes[0];
            var d = g.db.boxes[box];
            _destBox = box;
            _destUnit = g.units[Json.Str(d, "unit")];
            _destPoint = _destUnit.DestPoint(d);
            _destReachable = ReachableDest(box);
            if (Flies)
            {
                _hoverTarget = HoverPoint(_destPoint + Vector3.up * 0.05f, false);
                _path = new List<Vector3>();
                return true;
            }
            var want = StagingSpot(box).Value + UnitFront(_destUnit) * (_radius + 0.12f);
            var a = _nav.NearestFree(want, 0.5f) ?? _nav.NearestFree(want, 1.5f);
            if (a == null) return false;
            _path = _nav.Path(me, a.Value);
            _pathIdx = 0;
            return _path != null;
        }

        IEnumerator Deposit()
        {
            var here = carried.Where(c => g.world.DefOf(c).storageBox == _destBox).ToList();
            if (!_destReachable) { yield return StageBelow(here); yield break; }
            float high = Mathf.Clamp01((_destPoint.y - transform.position.y) / (_height * 2f));
            for (float t = 0; t < _depositTime * 0.5f; t += Time.deltaTime)
            {
                FaceTowards(_destPoint, Time.deltaTime * 2);
                _armLift = Mathf.SmoothStep(0, 0.4f + 0.6f * high, t / (_depositTime * 0.5f));
                if (Flies) HoverHold(Time.deltaTime, _altitude);
                yield return null;
            }
            foreach (var itemId in here)
            {
                if (g.storage.Deposit(_destBox, itemId, Owner) != StorageSystem.Result.Ok) continue;
                HideCarried(itemId);
                carried.Remove(itemId);
                delivered++;
                Sfx.Play("drop_drawer", _destPoint, -5);
                if (here.Count > 1) yield return new WaitForSeconds(0.12f);
            }
            _destUnit.RefreshLabels();
            float l0 = _armLift;
            for (float t = 0; t < _depositTime * 0.5f; t += Time.deltaTime)
            {
                _armLift = Mathf.Lerp(l0, 0, t / (_depositTime * 0.5f));
                yield return null;
            }
            _armLift = 0;
            _path = null;
            if (carried.Any(c => g.world.DefOf(c).storageBox == _destBox)) DropCarried($"{displayName}: that destination is full");
            _step = carried.Count > 0 ? Step.ToDest : Step.Choose;
            _idleT = 0.15f;
        }

        /// <summary>Destination too high: the parts go in a little pile on the floor in
        /// front of it. They stay UNSORTED until someone who can reach stores them.</summary>
        IEnumerator StageBelow(List<int> ids)
        {
            var spot = StagingSpot(_destBox).Value;
            for (float t = 0; t < _depositTime * 0.5f; t += Time.deltaTime)
            {
                FaceTowards(_destPoint, Time.deltaTime * 2);
                _bend = Mathf.SmoothStep(0, 0.6f, t / (_depositTime * 0.5f));
                _armReach = _bend / 0.6f;
                yield return null;
            }
            foreach (var itemId in ids)
            {
                var def = g.world.DefOf(itemId);
                var e = ItemDatabase.Extents(def);
                int nearby = g.index.QuerySphere(spot, 0.16f).Count;
                var jitter = new Vector3(Random.Range(-0.1f, 0.1f), 0, Random.Range(-0.1f, 0.1f));
                float y = e[2] * 0.5f + Mathf.Min(nearby, 5) * e[2] * 0.45f;
                var rot = Quaternion.Euler(Random.Range(-12f, 12f), Random.Range(0, 360f), def.restMode == RestMode.Long ? 90 + Random.Range(-10f, 10f) : Random.Range(-12f, 12f));
                HideCarried(itemId);
                carried.Remove(itemId);
                g.world.SetTransform(itemId, Matrix4x4.TRS(spot + jitter + Vector3.up * y, rot, Vector3.one * def.worldScale));
                g.world.SetState(itemId, ItemState.InWorld, "world");
                g.index.Insert(itemId);
                staged++;
                lastStagedId = itemId;
                Sfx.Play("drop_floor", spot, -6);
            }
            for (float t = 0; t < _depositTime * 0.5f; t += Time.deltaTime)
            {
                float u = t / (_depositTime * 0.5f);
                _bend = Mathf.Lerp(0.6f, 0, u);
                _armReach = 1 - u;
                yield return null;
            }
            _bend = 0; _armReach = 0;
            _path = null;
            _step = carried.Count > 0 ? Step.ToDest : Step.Choose;
            _idleT = 0.15f;
        }

        void DropCarried(string why)
        {
            foreach (var itemId in carried.ToList())
            {
                var def = g.world.DefOf(itemId);
                var p = transform.position + transform.forward * 0.3f + new Vector3(Random.Range(-0.1f, 0.1f), 0, Random.Range(-0.1f, 0.1f));
                p.y = Ground(p);
                var b = MeshLibrary.UnitBounds(def.geometryKey).size * def.worldScale;
                var m = Matrix4x4.TRS(new Vector3(p.x, p.y + Mathf.Min(b.x, b.y, b.z) * 0.5f, p.z), Quaternion.Euler(0, Random.Range(0, 360f), def.restMode == RestMode.Long ? 90 : 0), Vector3.one * def.worldScale);
                HideCarried(itemId);
                g.world.SetTransform(itemId, m);
                g.world.SetState(itemId, ItemState.InWorld, "world");
                g.index.Insert(itemId);
                _unreachable.Add(itemId);
            }
            carried.Clear();
            _path = null;
            _step = Step.Choose;
            if (why != "") g.hud.Toast(why);
        }

        // ---------------------------------------------------------------- movement

        static Vector3 Flat(Vector3 v) => new(v.x, 0, v.z);

        /// <summary>Follows the current ground path; returns true on arrival.</summary>
        bool Steer(float dt)
        {
            if (_path == null) { Brake(dt); return true; }
            while (_pathIdx < _path.Count && Flat(_path[_pathIdx] - transform.position).magnitude < 0.1f) _pathIdx++;
            if (_pathIdx >= _path.Count) { Brake(dt); return true; }
            var to = Flat(_path[_pathIdx] - transform.position);
            FaceTowards(transform.position + to, dt);
            float facing = Vector3.Dot(transform.forward, to.normalized);
            // tracked and wheeled robots turn on the spot before driving off
            float align = locomotion == "tracks" ? Mathf.Clamp01((facing - 0.7f) * 3.3f) : Mathf.Clamp01(facing * 1.4f);
            float target = Walk * align * Mathf.Clamp01(to.magnitude * 2.5f + (_pathIdx < _path.Count - 1 ? 1 : 0));
            _speedNow = Mathf.MoveTowards(_speedNow, target, dt * (locomotion == "wheel" ? 4f : 2.5f));
            transform.position += transform.forward * (_speedNow * dt);
            return false;
        }

        /// <summary>Hoverbot: climbs to cruise altitude to cross the room, flies straight,
        /// descends at the target. Returns true when it hovers at the target.</summary>
        bool FlyTo(Vector3 target, float dt, bool precise)
        {
            var p = transform.position;
            var flat = Flat(target - p);
            float dist = flat.magnitude;
            float ground = Ground(p);
            target.y = Mathf.Max(target.y, Ground(target) + _height * 0.5f);   // never below what it can hover over
            float wantY = dist > 0.8f ? Mathf.Max(_cruise, target.y) : target.y;
            float climb = Mathf.MoveTowards(p.y, wantY, dt * 1.1f);
            bool high = Mathf.Abs(p.y - wantY) < 0.25f || dist < 0.8f;
            float target01 = high ? Mathf.Clamp01(dist * 1.5f) : 0.15f;
            _speedNow = Mathf.MoveTowards(_speedNow, Walk * target01, dt * 2f);
            if (dist > 0.02f)
            {
                FaceTowards(target, dt);
                p += flat.normalized * Mathf.Min(dist, _speedNow * dt);
            }
            p.y = Mathf.Max(climb, ground + _height * 0.5f);
            transform.position = p;
            _altitude = p.y - ground;
            return dist < (precise ? 0.06f : 0.3f) && Mathf.Abs(p.y - target.y) < 0.08f;
        }

        void HoverHold(float dt, float alt)
        {
            var p = transform.position;
            float ground = Ground(p);
            p.y = Mathf.MoveTowards(p.y, ground + alt, dt * 0.8f);
            transform.position = p;
            _altitude = p.y - ground;
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
            float moving = Mathf.Max(_speedNow / _walk, Mathf.Clamp01(Mathf.Abs(_turnRate) / 120f) * 0.6f);
            _gait = Mathf.MoveTowards(_gait, moving > 0.05f ? Mathf.Max(0.45f, moving) : 0, dt * 3f);
            _phase += dt * Mathf.Lerp(5f, 8.5f, Mathf.Clamp01(moving)) * (_gait > 0 ? 1 : 0);
            if (_gait == 0) _phase = Mathf.MoveTowards(_phase, Mathf.Round(_phase / Mathf.PI) * Mathf.PI, dt * 6);   // finish the step: legs come to rest together
            _armHold = Mathf.MoveTowards(_armHold, carried.Count > 0 ? 1 : 0, dt * 3f);
            Sounds(moving);
            bool idle = _gait < 0.05f && _armReach == 0 && _armLift == 0;
            _model.Animate(new RobotAnim
            {
                dt = dt, speed01 = _speedNow / _walk, speed = _speedNow, gait = _gait, phase = _phase, turnRate = _turnRate, bend = _bend,
                armReach = _armReach, armHold = _armHold, armLift = _armLift, altitude = _altitude, active = true, time = _time,
                lookAt = g.player.transform.position, lookWeight = idle ? 1 : 0,
            });
            int k = 0;
            foreach (var itemId in carried)
                if (_carryModels.TryGetValue(itemId, out var go))
                {
                    var (parent, local) = _model.CarrySlot(k++, carried.Count);
                    if (go.transform.parent != parent) go.transform.SetParent(parent, false);
                    go.transform.localPosition = local + new Vector3(0, 0.05f * _armLift, 0.06f * _armLift);
                }
        }

        void Sounds(float moving)
        {
            if (_loop != null)
            {
                if (_loop.clip == null) { _loop.clip = Sfx.Clip(_loopName); if (_loop.clip != null) { _loop.time = Random.value * 0.9f; _loop.Play(); } }
                float want = locomotion == "hover" ? 0.22f + 0.12f * moving : 0.25f * Mathf.Clamp01(moving * 1.3f);
                _loop.volume = Mathf.MoveTowards(_loop.volume, want * Sfx.masterVolume, Time.deltaTime);
                _loop.pitch = locomotion == "hover" ? 1f + 0.08f * moving : 0.85f + 0.3f * Mathf.Clamp01(moving);
            }
            else if (_gait > 0.1f)
            {
                // a tiny tick on every footfall (twice per stride; three ticks for the hexapod's tripods)
                float step = Mathf.Floor(_phase / Mathf.PI);
                if (step != _lastStepPhase)
                {
                    _lastStepPhase = step;
                    Sfx.Play(locomotion == "hexapod" ? "spider_step" : "robot_step", transform.position, -22, 0.2f);
                }
            }
        }

        void ShowCarried(int itemId)
        {
            var def = g.world.DefOf(itemId);
            var go = HeldView.MakeModel(def);
            var (parent, local) = _model.CarrySlot(carried.Count - 1, carried.Count);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = local;
            go.transform.localScale = Vector3.one * def.worldScale / (_height / _model.ModelHeight);   // true size
            go.transform.localRotation = def.restMode == RestMode.Long ? Quaternion.Euler(0, 0, 90) : Quaternion.identity;
            _carryModels[itemId] = go;
        }

        void HideCarried(int itemId)
        {
            if (_carryModels.TryGetValue(itemId, out var go)) { Destroy(go); _carryModels.Remove(itemId); }
        }

        public string Describe()
        {
            switch (mode)
            {
                case Mode.Assembling: return $"{displayName}: {status}";
                case Mode.Activating: return $"{displayName}: starting up…";
                case Mode.Following: return carried.Count > 0 ? $"{displayName}: delivering, then following" : $"{displayName}: following you";
            }
            if (_step == Step.ToPlayer) return $"{displayName}: coming to take your parts";
            string zone = zoneCenter.HasValue ? " (zone)" : "";
            return _step switch
            {
                Step.ToItem => carried.Count > 0 ? $"{displayName}: collecting ({carried.Count}/{_capacity})" : $"{displayName}: fetching a part",
                Step.Picking => $"{displayName}: picking up",
                Step.ToDest or Step.Depositing => carried.Count > 0 ? $"{displayName}: delivering {carried.Count}" : $"{displayName}: storing",
                _ => status == "" ? $"{displayName}: looking for parts" : $"{displayName}: {status}",
            } + zone;
        }
    }
}
