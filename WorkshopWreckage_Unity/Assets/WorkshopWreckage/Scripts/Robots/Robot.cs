using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace WorkshopWreckage
{
    /// <summary>One robot (docs/specs/robots_and_levels.md), configured by
    /// shared/data/robots.json (type) and its upgrades (progression.json). Built at the
    /// Robot Workbench: it stands on its mat while parts are installed and starts work
    /// when the player activates it. Then it works on its own with its type's strategy:
    ///  · opportunistic (Retrobot): nearest reachable part, one at a time;
    ///  · courier (Unibot): fast, never stores — always leaves parts in the staging zone
    ///    in front of the destination (they stay UNSORTED);
    ///  · assigned_definition (Spiderbot): one exact part definition until the set is
    ///    done; climbs heaps, benches and shelving to reach them and to store them;
    ///  · multi_stop (Caterpillarbot): gathers several parts, then a delivery run;
    ///  · staging_first (Hoverbot): empties staging zones first, reaches every height.
    /// Destinations above a robot's reach get the part left in the staging zone (owner
    /// "stage:&lt;destination&gt;") for a robot that can reach them. Two robots never go
    /// for the same part (shared reservations, released whenever a task ends).
    /// Commands: R (aiming at a part) — Spiderbot's assignment / nearest robot works on
    /// that kind next; T — nearest robot: work / follow me.</summary>
    public class Robot : MonoBehaviour
    {
        public enum Mode { Assembling, Activating, Working, Following }
        enum Step { Choose, ToItem, Picking, ToDest, Depositing, ToPlayer, ToCharge, Charging, Gesture }

        /// <summary>id: this instance ("hoverbot_2"); type: its robots.json entry ("hoverbot").</summary>
        public string id, type, displayName, locomotion, strategy;
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
        public bool paused;
        /// <summary>Spiderbot: the exact definition it collects (-1 none); auto = it picked it itself.</summary>
        public int assignedDef = -1;
        public bool autoAssigned;
        public string specialistState = "";         // WORKING, SEARCHING, WAITING_FOR_ACCESS, COLLECTION_COMPLETE, PAUSED

        Game g;
        NavGrid _nav;
        RobotModel _model;
        List<Vector3> _path;
        int _pathIdx;
        float _speedNow, _turnRate, _phase, _gait, _repathT, _idleT, _altitude, _time, _stepT;
        Step _lastStep;
        float _bend, _armHold, _armReach, _armLift;
        int _capacity, _types;
        public int Capacity => _capacity;
        float _walk, _turn, _reach, _maxPickH, _followDist, _radius, _pickTime, _depositTime, _height, _maxItem, _stepH, _gather, _cruise;
        float _climbH, _climbSpeed, _scan, _largeItem;
        int _targetRank, _courierRank, _searchRank, _routeRank, _stagingRank, _fleetRank;
        bool _canStore = true, _canClimb, _climbing;
        float _climbPitch, _unreachableT;
        Dictionary<string, object> _cfg, _limits;
        bool Flies => locomotion == "hover";
        readonly HashSet<int> _unreachable = new();
        readonly Dictionary<int, GameObject> _carryModels = new();
        readonly Dictionary<string, Vector3?> _stagingSpot = new();
        string _destBox;
        StorageUnit _destUnit;
        Vector3 _destPoint, _hoverTarget;
        bool _destReachable;

        public bool Active => mode == Mode.Working || mode == Mode.Following;
        /// <summary>Built and activated (it has left the workbench).</summary>
        public bool Operational => mode != Mode.Assembling && mode != Mode.Activating;
        public string StepName => _step.ToString();
        public float Speed => _speedNow;
        public float WalkSpeed => _walk;
        float Walk => _walk * g.Scale("robot_speed") * (1f + (float)(g.progression?.Effect("robot_speed") ?? 0));   // preset scaling, Shift Planner
        public float Height => _height;
        public bool CanDirectStore => _canStore;
        public bool CanClimb => _canClimb;
        /// <summary>Highest point it can pick from / store into (by climbing, for climbers).</summary>
        public float ReachHeight => _canClimb ? Mathf.Max(_climbH, _maxPickH) : _maxPickH;
        public float MaxItemSize => _maxItem;
        public float HeapStep => _stepH;
        public int ItemTypes => _types;

        static Dictionary<string, object> _allConfig;
        public static Dictionary<string, object> RootConfig() => Shared.ReadObject(Shared.DataPath("robots.json"));
        public static Dictionary<string, object> AllConfig() => _allConfig ??= Json.Obj(RootConfig()["robots"]);

        public void Init(Game game, string robotType, string instanceId, Vector3 home, Quaternion rot)
        {
            g = game;
            type = robotType;
            id = instanceId;
            var c = _cfg = Json.Obj(AllConfig()[type]);
            _limits = Json.Obj(c.GetValueOrDefault("limits")) ?? new Dictionary<string, object>();
            var w = g.workbench?.P(id);
            displayName = w?.name ?? Json.Str(c, "name", id);
            locomotion = Json.Str(c, "locomotion", "biped");
            strategy = Json.Str(c, "strategy", "opportunistic");
            _height = (float)Json.Num(c, "height", 0.3);
            _reach = (float)Json.Num(c, "reach", 0.32);
            _cruise = (float)Json.Num(c, "cruise_altitude", 2.9);
            _followDist = (float)Json.Num(c, "follow_distance", 1.1);
            _radius = (float)Json.Num(c, "radius", 0.12);
            _batteryMinutes = (float)Json.Num(c, "battery_minutes", 8);
            _chargeMinutes = (float)Json.Num(c, "charge_minutes", 1);
            _canStore = Json.Bool(c, "can_direct_store", true);
            _canClimb = Json.Bool(c, "can_climb");
            _largeItem = (float)Json.Num(RootConfig(), "large_item_size", 0.25);
            ApplyUpgrades();
            if (!Flies)
            {
                _nav = new NavGrid(g.layout, _radius, _height);
                _nav.RefreshItems(g.world, _stepH);
            }
            transform.SetPositionAndRotation(home, rot);
            this.home = home;
            homeRot = rot;
            _model = type switch
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
            _model.assembling = true;
            RefreshAssembly();
        }

        /// <summary>Recomputes the stats from robots.json and the type's bought upgrades
        /// (never past the permanent limits). Called on spawn and after every purchase.</summary>
        public void ApplyUpgrades()
        {
            var c = _cfg;
            double Lim(string k) => _limits.TryGetValue(k, out var v) ? (double)v : double.PositiveInfinity;
            float S(string k, double def) => (float)(g.progression?.RobotStat(type, k, Json.Num(c, k, def), Lim(k)) ?? Json.Num(c, k, def));
            int R(string k) => Mathf.FloorToInt((float)(g.progression?.RobotStat(type, k, 0) ?? 0) + 1e-4f);
            _capacity = Mathf.Max(1, Mathf.FloorToInt(S("capacity", 1) + 1e-4f));
            _types = Mathf.Max(1, Mathf.FloorToInt(S("item_types", 1) + 1e-4f));
            _walk = S("walk_speed", 0.7);
            _turn = S("turn_speed", Json.Num(c, "turn_speed_deg", 260));
            _maxPickH = S("reach_height", 0.35);
            _climbH = S("climb_height", 0);
            _climbSpeed = S("climb_speed", 0.35);
            _maxItem = S("max_item_size", 0.33);
            _stepH = S("heap_step_height", 0.18);
            _gather = S("gather_radius", 3);
            _scan = S("scan_radius", 6);
            float pick = (float)(g.progression?.RobotStat(type, "pick_time", 1) ?? 1);   // a multiplier: faster loading also speeds unloading
            _pickTime = (float)Json.Num(c, "pick_time", 1.1) * pick;
            _depositTime = (float)Json.Num(c, "deposit_time", 0.9) * pick;
            _targetRank = R("target_selection"); _courierRank = R("courier_coordination"); _searchRank = R("search_efficiency");
            _routeRank = R("route_planning"); _stagingRank = R("staging_priority"); _fleetRank = R("fleet_coordination");
        }

        /// <summary>Base value of a stat from robots.json (for "base → upgraded" displays).</summary>
        public float BaseStat(string k, double def = 0) => (float)Json.Num(_cfg, k == "turn_speed" ? "turn_speed_deg" : k, def);
        public float LimitOf(string k) => _limits.TryGetValue(k, out var v) ? (float)(double)v : float.PositiveInfinity;
        public float ClimbSpeed => _climbSpeed;
        public float ScanRadius => _scan;
        public List<string> Strengths => Json.Arr(_cfg.GetValueOrDefault("strengths")).Select(x => x.ToString()).ToList();
        public List<string> Limitations => Json.Arr(_cfg.GetValueOrDefault("limitations")).Select(x => x.ToString()).ToList();

        // ---------------------------------------------------------------- assembly

        /// <summary>Assembly part key of a robot part, e.g. "Unibot Wheel Hub" -> "wheel hub".</summary>
        public static string PartKey(ItemDefinition d)
        {
            var n = d.displayName;
            int sp = n.IndexOf(' ');
            return (sp >= 0 ? n.Substring(sp + 1) : n).ToLowerInvariant();
        }

        RobotWorkbench.Project Project => g.workbench?.P(id);

        /// <summary>Rebuilds the model from the workbench state. While unbuilt the robot
        /// stands on its mat; missing parts show as ghosts with empty sockets.</summary>
        public void RefreshAssembly()
        {
            var p = Project;
            if (p == null || g.workbench == null) return;
            bool onBench = mode == Mode.Assembling;
            gameObject.SetActive(!onBench || g.workbench.OnBench(p));
            if (!onBench) return;
            var have = g.workbench.InstalledKeys(p);
            _model.assembling = true;
            _model.mountReady = part => !p.requires.TryGetValue(part, out var req) || req.All(have.Contains);
            _model.Build(have);
            _model.SetEyes(false);
            var (pos, rot) = g.workbench.MatSpot(p.mat);
            transform.SetPositionAndRotation(pos, rot);
            int total = g.workbench.Required(p);
            status = g.workbench.Complete(p) ? "ready for activation" : $"{g.workbench.InstalledCount(p)}/{total} parts installed";
        }

        /// <summary>A part was just installed: the robot gives a little jolt on the bench.</summary>
        public void InstallPop(string partKey)
        {
            if (gameObject.activeInHierarchy && _model.transform.parent == transform) StartCoroutine(Pop());
        }

        /// <summary>The model (for holding the unfinished robot in your hands at the workbench).</summary>
        public Transform ModelTransform => _model.transform;

        IEnumerator Pop()
        {
            var t0 = _model.transform.localPosition;
            for (float t = 0; t < 0.35f; t += Time.deltaTime)
            {
                _model.transform.localPosition = t0 + Vector3.up * Mathf.Sin(t / 0.35f * Mathf.PI) * 0.06f;
                yield return null;
            }
            _model.transform.localPosition = t0;
        }

        /// <summary>The player activates the finished robot at the workbench.</summary>
        public void BeginActivation()
        {
            if (mode != Mode.Assembling) return;
            StartCoroutine(Activate());
        }

        /// <summary>Loading a save (or a legacy save's finished robot): operational at once, at home.</summary>
        public void SetOperational()
        {
            gameObject.SetActive(true);
            _model.EndAssembly();
            _model.SetEyes(true);
            mode = Mode.Working;
            _step = Step.Choose;
            transform.SetPositionAndRotation(home, homeRot);
            if (Flies) _altitude = 0.6f;
            AdoptCarried();
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
            _model.EndAssembly();   // the clamp lets go, the ghosts are gone
            _model.SetEyes(true);
            // a little "hello": the model wiggles in its own way while it looks around
            for (float t = 0; t < 1.2f; t += Time.deltaTime)
            {
                _time += Time.deltaTime;
                _model.Animate(new RobotAnim
                {
                    dt = Time.deltaTime, gait = 0.6f * Mathf.Sin(t / 1.2f * Mathf.PI), phase = t * 9, active = true, time = _time, altitude = 0,
                    lookAt = transform.position + Quaternion.Euler(0, Mathf.Sin(t * 6) * 50, 0) * transform.forward, lookWeight = 1,
                });
                yield return null;
            }
            // off the bench: a hop down to its charging mat (Hoverbot flies down)
            var from = transform.position;
            var to = home + (Flies ? Vector3.up * 0.6f : Vector3.zero);
            float dur = Flies ? 1.6f : 0.9f;
            for (float t = 0; t < dur; t += Time.deltaTime)
            {
                float u = t / dur;
                _time += Time.deltaTime;
                var p = Vector3.Lerp(from, to, Flies ? Mathf.SmoothStep(0, 1, u) : u);
                if (!Flies) p.y += Mathf.Sin(u * Mathf.PI) * 0.35f;
                transform.position = p;
                transform.rotation = Quaternion.Slerp(transform.rotation, homeRot, u);
                if (Flies) _altitude = Mathf.Lerp(0.05f, 0.6f, u);
                _model.Animate(new RobotAnim { dt = Time.deltaTime, gait = Flies ? 0.3f : 0, phase = t * 9, active = true, time = _time, altitude = _altitude, armReach = Flies ? 0 : Mathf.Sin(u * Mathf.PI) });
                yield return null;
            }
            transform.SetPositionAndRotation(to, homeRot);
            if (!Flies) Sfx.Play("drop_floor", transform.position, -4);
            Sfx.Play("complete", transform.position + Vector3.up * 0.3f, -2);
            mode = Mode.Working;
            _step = Step.Choose;
            g.progression?.OnRobotRepaired(id);
            g.hud.Toast($"{displayName} is online and starts sorting!", true);
            g.workbench?.Refresh();
            foreach (var r in g.robots) if (r != this && r.mode == Mode.Assembling) r.RefreshAssembly();   // the next Hoverbot may take the mat
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
            if (strategy == "assigned_definition") return Assign(defId, false);
            preferDef = defId;
            mode = Mode.Working;
            if (carried.Count == 0 && _step != Step.Picking) { Release(); _step = Step.Choose; _path = null; }
            Sfx.Play("servo", transform.position + Vector3.up * 0.3f, -6);
            return $"{displayName}: on it — {g.db.Get(defId).displayName}";
        }

        /// <summary>Spiderbot: collect exactly this definition (-1 cancels). Its current
        /// carried part is finished first; a target of another kind is released.</summary>
        public string Assign(int defId, bool auto)
        {
            if (strategy != "assigned_definition") return $"{displayName} doesn't take assignments";
            if (defId >= 0 && g.db.Get(defId).IsRobotPart) return $"{displayName} leaves robot parts to you";
            assignedDef = defId;
            autoAssigned = auto;
            _unreachable.Clear();
            _completeT = 0;
            if (targetId >= 0 && g.world.instances[targetId].defId != defId) { Release(); _step = carried.Count > 0 ? Step.ToDest : Step.Choose; _path = null; }
            mode = Mode.Working;
            if (auto) return "";
            Sfx.Play("servo", transform.position + Vector3.up * 0.3f, -6);
            return defId < 0 ? $"{displayName}: assignment cleared" : $"{displayName}: on it — every {g.db.Get(defId).displayName}";
        }

        /// <summary>Saved robot state (carried parts are saved with the items, owner robot:id).</summary>
        public Dictionary<string, object> ToDict()
        {
            var d = new Dictionary<string, object>
            {
                ["operational"] = Operational || mode == Mode.Activating, ["paused"] = paused,
                ["assigned"] = (double)assignedDef, ["auto_assigned"] = autoAssigned,
                ["following"] = mode == Mode.Following, ["battery"] = (double)Battery,
                ["delivered"] = (double)delivered, ["staged"] = (double)staged,
                ["pos"] = new List<object> { (double)transform.position.x, (double)transform.position.y, (double)transform.position.z },
                ["yaw"] = (double)transform.eulerAngles.y,
            };
            if (zoneCenter.HasValue) d["zone"] = new List<object> { (double)zoneCenter.Value.x, (double)zoneCenter.Value.z };
            return d;
        }

        public void LoadState(Dictionary<string, object> d)
        {
            if (d == null) return;
            Battery = (float)Json.Num(d, "battery", 1);
            delivered = Json.Int(d, "delivered");
            staged = Json.Int(d, "staged");
            var z = Json.Arr(d.GetValueOrDefault("zone"));
            zoneCenter = z.Count == 2 ? new Vector3(Json.F(z[0]), 0, Json.F(z[1])) : null;
            paused = Json.Bool(d, "paused");
            assignedDef = Json.Int(d, "assigned", -1);
            autoAssigned = Json.Bool(d, "auto_assigned");
            if (Json.Bool(d, "operational") && !Operational) SetOperational();
            if (!Active) return;
            var p = Json.Arr(d.GetValueOrDefault("pos"));
            if (p.Count == 3) transform.SetPositionAndRotation(new Vector3(Json.F(p[0]), Json.F(p[1]), Json.F(p[2])), Quaternion.Euler(0, (float)Json.Num(d, "yaw"), 0));
            if (Json.Bool(d, "following")) mode = Mode.Following;
            _path = null;
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
        float _batteryMinutes = 8, _chargeMinutes = 1;
        public float Battery { get; private set; } = 1f;   // 0..1
        public void SetBattery(float v) => Battery = Mathf.Clamp01(v);
        float _gestureT, _sinceGesture;
        const float GestureTime = 1.6f;
        public int gestures;
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
                if (g.world.DefOf(itemId).IsRobotPart) continue;   // robot parts are the player's to deliver, personally
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
            zoneCenter = center.HasValue ? new Vector3(center.Value.x, 0, center.Value.z) : null;   // a floor point
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

        public bool Climbing => _climbing;

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
            if (!Flies && Active && !_climbing)
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
            // parts it gave up on are tried again now and then (better target selection remembers longer)
            if ((_unreachableT += dt) > (_targetRank >= 2 ? 180f : 60f)) { _unreachableT = 0; _unreachable.Clear(); }
            // paused from Robot Management: finishes what it holds, then waits
            if (paused && carried.Count == 0 && (_step == Step.Choose || _step == Step.ToItem))
            {
                Release(); _step = Step.Choose; _path = null;
                Brake(dt);
                if (Flies) HoverHold(dt, Mathf.Max(0.6f, _altitude));
                specialistState = "PAUSED";
                return;
            }
            if (_step == Step.ToItem && _stepT > 25f)
            {
                if (targetId >= 0) _unreachable.Add(targetId);
                Release(); _path = null; _step = carried.Count > 0 ? Step.ToDest : Step.Choose;
                return;
            }
            if (_step == Step.ToDest && _stepT > 60f) { DropCarried($"{displayName} got stuck and set its load down"); return; }
            if (_step == Step.ToPlayer && _stepT > 40f) { _step = Step.Choose; g.hud.Toast($"{displayName} couldn't reach you"); return; }
            // battery: drains while working; when low (and not carrying) it goes home to charge
            if (_step != Step.Charging) Battery = Mathf.Max(0, Battery - dt / (_batteryMinutes * 60f));
            if (Battery < 0.12f && carried.Count == 0 && (_step == Step.Choose || _step == Step.ToItem))
            {
                Release();
                _step = Step.ToCharge;
                _path = null;
            }
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
                    bool high = !Flies && ip.y > _maxPickH;   // only climbers choose those: they climb up from the path's end
                    bool tray = g.InTray(ip);                 // reached over the rim from outside
                    float fd = Flat(ip - transform.position).magnitude;
                    bool arrived = Flies ? FlyTo(_hoverTarget, dt, true) : ((!high && fd <= _reach * 0.75f) || (Steer(dt) && (fd <= _reach + 0.1f || (high && fd <= 1.0f) || (tray && fd <= 0.65f))));
                    if (arrived) { _step = Step.Picking; StartCoroutine(PickUp(targetId)); }
                    else if (!Flies && _pathIdx >= _path.Count) { _unreachable.Add(targetId); Release(); _step = carried.Count > 0 ? Step.ToDest : Step.Choose; }
                    break;
                }
                case Step.ToPlayer:
                {
                    var pp = g.player.transform.position;
                    float dist = Flat(pp - transform.position).magnitude;
                    if (dist < 0.9f) { TakeFromPlayer(); break; }
                    if (Flies) { FlyTo(pp - Flat(pp - transform.position).normalized * 0.6f + Vector3.up * 0.9f, dt, false); break; }
                    _repathT -= dt;
                    if (_path == null || _repathT <= 0)
                    {
                        // the player may stand among heaps (obstacles for small robots): get as close as possible
                        _repathT = 0.5f;
                        var goal = _nav.NearestFree(pp - Flat(pp - transform.position).normalized * 0.5f, 1.0f) ?? _nav.NearestFree(pp, 2.5f);
                        _path = goal.HasValue ? _nav.Path(transform.position, goal.Value) : null;
                        _pathIdx = 0;
                    }
                    if (_path == null) { Brake(dt); break; }   // keep trying until the request times out
                    if (Steer(dt) && dist < 2.5f) TakeFromPlayer();   // as close as it can get
                    break;
                }
                case Step.Gesture:
                    Brake(dt);
                    if (locomotion == "biped") FaceTowards(g.player.transform.position, dt);   // Retrobot turns to you to wave
                    if (Flies) HoverHold(dt, Mathf.Max(0.6f, _altitude));
                    _gestureT += dt;
                    if (_gestureT >= GestureTime) { _gestureT = 0; _step = Step.Choose; }
                    break;
                case Step.ToCharge:
                {
                    bool home;
                    if (Flies) home = FlyTo(this.home + Vector3.up * 0.25f, dt, true);
                    else
                    {
                        if (_path == null) { _path = _nav.Path(transform.position, this.home); _pathIdx = 0; if (_path == null) { _step = Step.Charging; break; } }
                        home = Steer(dt);
                    }
                    if (home) { _step = Step.Charging; _path = null; }
                    break;
                }
                case Step.Charging:
                    Brake(dt);
                    transform.rotation = Quaternion.RotateTowards(transform.rotation, homeRot, _turn * dt);
                    if (Flies) HoverHold(dt, 0.25f);
                    Battery = Mathf.Min(1, Battery + dt / (_chargeMinutes * 60f));
                    _model.SetBeacon(Mathf.Repeat(_time, 1.6f) < 0.2f);   // slow charging blink
                    if (Battery >= 1) { _model.SetBeacon(false); _step = Step.Choose; Sfx.Play("servo", transform.position + Vector3.up * 0.3f, -10); }
                    break;
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

        /// <summary>Cargo slots a part takes: bulky parts (longest side ≥ large_item_size) take two.</summary>
        public int SlotsOf(int itemId) => ItemDatabase.Extents(g.world.DefOf(itemId))[0] >= _largeItem ? 2 : 1;
        int SlotsUsed => carried.Sum(SlotsOf);

        float _completeT, _waitT;

        /// <summary>Picks the next part with the type's strategy (see the class summary).
        /// Once carrying, only parts close by, within the number of part kinds and the
        /// free cargo slots; then it delivers. Searches are bounded per frame.</summary>
        void ChooseNext()
        {
            var me = transform.position;
            if (SlotsUsed >= _capacity) { _step = Step.ToDest; _path = null; return; }
            if (!Flies) _nav.RefreshItems(g.world, _stepH);
            var types = carried.Select(c => g.world.instances[c].defId).ToHashSet();
            IEnumerable<ItemInstance> pool = null;
            System.Func<ItemInstance, float> score = i => (i.Position - me).sqrMagnitude;
            specialistState = "";
            _stagedNow = strategy == "courier" && _courierRank > 0 ? StagedCounts() : null;
            if (strategy == "assigned_definition")
            {
                if (!ChooseAssigned(ref pool, me)) { if (carried.Count > 0) { _step = Step.ToDest; _path = null; } else _idleT = 1.5f; return; }
            }
            else
            {
                // parts the player put in the sorting tray come first
                var tray = g.index.QuerySphere(g.TrayCenter, 1f).Select(i => g.world.instances[i]).Where(i => g.InTray(i.Position) && Pickable(i)).ToList();
                if (tray.Count > 0 && preferDef < 0) pool = tray;
                if (preferDef >= 0)
                {
                    var pref = g.world.IdsOf(preferDef).Select(i => g.world.instances[i]).Where(Pickable).ToList();
                    if (pref.Count > 0) pool = pref; else preferDef = -1;
                }
                if (pool == null && strategy == "staging_first")
                {
                    // Hoverbot: finish what the others left in the staging zones first
                    var staged = g.world.instances.Where(i => i.state == ItemState.InWorld && i.owner.StartsWith("stage:") && Pickable(i)).ToList();
                    if (_fleetRank > 0)
                    {
                        // fleet coordination: leave zones another Hoverbot is already working on
                        var taken = g.robots.Where(r => r != this && r.type == type && r.targetId >= 0).Select(r => g.world.instances[r.targetId].owner).ToHashSet();
                        var free = staged.Where(i => !taken.Contains(i.owner)).ToList();
                        if (free.Count > 0) staged = free;
                    }
                    if (staged.Count > 0)
                    {
                        pool = staged;
                        if (_stagingRank > 0)
                        {
                            var fill = staged.GroupBy(i => i.owner).ToDictionary(gr => gr.Key, gr => gr.Count());
                            score = i => (i.Position - me).magnitude - fill[i.owner] * 0.6f * _stagingRank;   // fullest zones first
                        }
                    }
                }
                pool ??= g.world.instances;
                if (strategy == "opportunistic" && _targetRank > 0)
                    score = i => (i.Position - me).magnitude - (g.progress.SortedOf(i.defId) > 0 ? 1.5f : 0);   // sets already started come first
                if (strategy == "multi_stop" && _routeRank > 0 && carried.Count > 0)
                {
                    // route planning: next pickups whose destinations lie near the ones already on board
                    var dests = carried.Select(c => StagingSpot(g.world.DefOf(c).storageBox)).Where(s => s.HasValue).Select(s => s.Value).ToList();
                    score = i =>
                    {
                        var s = StagingSpot(g.db.Get(i.defId).storageBox);
                        float near = s.HasValue && dests.Count > 0 ? dests.Min(d => Flat(d - s.Value).magnitude) : 5;
                        return (i.Position - me).magnitude + near * 0.25f * _routeRank;
                    };
                }
            }
            int tries = 0, maxTries = 6 + 3 * _searchRank;
            foreach (var inst in pool.Where(Pickable).OrderBy(score))
            {
                if (carried.Count > 0 && ((inst.Position - me).magnitude > _gather || (!types.Contains(inst.defId) && types.Count >= _types) || SlotsUsed + SlotsOf(inst.id) > _capacity)) break;
                if (++tries > maxTries) break;   // keep the frame cheap; continue next frame
                if (Flies)
                {
                    _hoverTarget = HoverPoint(inst.Position, true);
                }
                else
                {
                    bool high = inst.Position.y > _maxPickH;
                    var goal = _nav.NearestFree(inst.Position, _reach * 0.7f) ?? _nav.NearestFree(inst.Position, high ? 0.95f : g.InTray(inst.Position) ? 0.65f : _reach);
                    var path = goal.HasValue ? _nav.Path(me, goal.Value) : null;
                    if (path == null) { _unreachable.Add(inst.id); continue; }
                    _path = path;
                    _pathIdx = 0;
                }
                targetId = inst.id;
                g.robotReserved.Add(inst.id);
                _step = Step.ToItem;
                if (strategy == "assigned_definition") specialistState = carried.Count == 0 && Flat(inst.Position - me).magnitude > _scan ? "SEARCHING" : "WORKING";
                return;
            }
            if (carried.Count > 0) { _step = Step.ToDest; _path = null; return; }
            _idleT = 0.5f;
            if (tries == 0) status = "nothing left it can reach";
        }

        /// <summary>Spiderbot: works through its one assigned definition. Known copies in
        /// detection range first, then it searches further. When none can be reached it
        /// reports WAITING_FOR_ACCESS (and retries later) instead of looping; when every
        /// copy is accounted for, COLLECTION_COMPLETE — after a short pause it picks the
        /// nearest unfinished set itself unless the player assigns one.</summary>
        bool ChooseAssigned(ref IEnumerable<ItemInstance> pool, Vector3 me)
        {
            if (assignedDef >= 0 && g.progress.IsComplete(assignedDef))
            {
                if (_completeT == 0)
                {
                    var name = g.db.Get(assignedDef).displayName;
                    if (!autoAssigned || Vector3.Distance(me, g.player.transform.position) < 8) g.hud.Toast($"{displayName} finished the set: {name}", !autoAssigned);
                }
                specialistState = "COLLECTION_COMPLETE";
                _completeT += 0.5f;
                if (_completeT < 4f || !autoAssigned && _completeT < 30f) return false;
                assignedDef = -1;
            }
            if (assignedDef < 0)
            {
                // no assignment: take the nearest unfinished set it can actually reach
                var first = g.world.instances.Where(Pickable).OrderBy(i => (i.Position - me).sqrMagnitude).FirstOrDefault();
                if (first == null) { specialistState = "WAITING_FOR_ACCESS"; return false; }
                Assign(first.defId, true);
            }
            var mine = g.world.IdsOf(assignedDef).Select(i => g.world.instances[i]).Where(Pickable).ToList();
            if (mine.Count == 0)
            {
                specialistState = "WAITING_FOR_ACCESS";
                if ((_waitT += 0.5f) > 20f) { _waitT = 0; _unreachable.Clear(); }   // try the blocked ones again later
                if (autoAssigned && _waitT > 6f) { _waitT = 0; assignedDef = -1; }   // its own pick: move on to another set
                return false;
            }
            _waitT = 0;
            var near = mine.Where(i => (i.Position - me).magnitude <= _scan).ToList();
            pool = near.Count > 0 ? near : mine;
            return true;
        }

        /// <summary>A robot picks up only parts it can reach (standing, or climbing for a
        /// climber; Hoverbot not from under a heap), no bigger than it can lift, never robot
        /// parts, never parts another robot reserved; staged parts only if it can store
        /// them itself, and never parts it already left in front of a destination it can't reach.</summary>
        bool Pickable(ItemInstance i)
        {
            if (i.state != ItemState.InWorld || i.Position.y > ReachHeight || _unreachable.Contains(i.id) || g.robotReserved.Contains(i.id)) return false;
            if (zoneCenter.HasValue && Flat(i.Position - zoneCenter.Value).magnitude > zoneRadius) return false;
            var def = g.db.Get(i.defId);
            if (def.IsRobotPart || g.interaction.busy.Contains(i.id) || ItemDatabase.Extents(def)[0] > _maxItem) return false;
            if (strategy == "assigned_definition" && assignedDef >= 0 && i.defId != assignedDef) return false;
            if (Flies && g.debris != null && g.debris.At(i.Position) > i.Position.y + ItemDatabase.Extents(def)[2] * 0.5f + 0.05f) return false;   // buried
            bool staged = i.owner.StartsWith("stage:");
            if (staged && Blocked(def.storageBox)) return false;   // waits until someone clears the strays out of its destination
            if (ReachableDest(def.storageBox)) return true;
            if (staged) return false;   // waiting for a robot that can store it
            if (_stagedNow != null && _stagedNow.GetValueOrDefault("stage:" + def.storageBox) >= StagingCapacity) return false;   // that zone is full
            var spot = StagingSpot(def.storageBox);
            return spot == null || Flat(i.Position - spot.Value).magnitude > 0.45f;
        }

        /// <summary>Can it put a part into this destination itself? Never for a courier
        /// (Unibot); otherwise if the destination is within reach (climbing for a climber).</summary>
        public bool ReachableDest(string box) => _canStore && g.db.boxes.TryGetValue(box, out var d) && Json.Num(d, "elevation_m") <= ReachHeight;

        readonly Dictionary<string, (float t, bool v)> _blocked = new();
        bool Blocked(string box)
        {
            if (_blocked.TryGetValue(box, out var e) && _time - e.t < 5f) return e.v;
            bool v = g.StraysIn(box).Count > 0;
            _blocked[box] = (_time, v);
            return v;
        }

        public static int StagingCapacity => Json.Int(RootConfig(), "staging_capacity", 14);
        public int StagingCount(string box) => g.world.instances.Count(i => i.state == ItemState.InWorld && i.owner == "stage:" + box);
        Dictionary<string, int> _stagedNow;
        Dictionary<string, int> StagedCounts()
        {
            var d = new Dictionary<string, int>();
            foreach (var i in g.world.instances) if (i.state == ItemState.InWorld && i.owner.StartsWith("stage:")) d[i.owner] = d.GetValueOrDefault(i.owner) + 1;
            return d;
        }

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
            return inst.state == ItemState.InWorld && (inst.owner == "stage:" + def.storageBox || (spot.HasValue && Flat(inst.Position - spot.Value).magnitude <= 0.45f && !ReachableDest(def.storageBox)));
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
            if (!Flies && ip.y > _maxPickH) yield return ClimbTo(ip);   // Spiderbot climbs up to it
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
            if (_climbing) yield return ClimbDown();
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
            // stray parts lying in the destination block it: leave the load in its staging zone
            if (!_destReachable || g.StraysIn(_destBox).Count > 0) { yield return StageBelow(here); yield break; }
            if (!Flies && _destPoint.y > _maxPickH) yield return ClimbTo(_destPoint);   // climbs up to store it
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
            if (_climbing) yield return ClimbDown();
            _path = null;
            if (carried.Any(c => g.world.DefOf(c).storageBox == _destBox)) DropCarried($"{displayName}: that destination is full");
            _step = carried.Count > 0 ? Step.ToDest : Step.Choose;
            _idleT = 0.15f;
            MaybeGesture();
        }

        Vector3 _climbFrom;

        /// <summary>Climber: from the floor in front of the furniture it tilts nose-up, climbs
        /// the face to the target's height and steps onto the shelf / bench top beside it.</summary>
        IEnumerator ClimbTo(Vector3 target)
        {
            _climbing = true;
            _climbFrom = transform.position;
            var dir = Flat(target - transform.position).normalized;
            float topY = Mathf.Max(transform.position.y, target.y - 0.02f);
            while (transform.position.y < topY - 0.005f)
            {
                float dt = Time.deltaTime;
                FaceTowards(target, dt);
                _climbPitch = Mathf.MoveTowards(_climbPitch, 75, dt * 240);
                var p = transform.position;
                p.y = Mathf.MoveTowards(p.y, topY, dt * _climbSpeed * (_climbPitch / 75f));
                transform.position = p;
                _speedNow = _climbSpeed;
                yield return null;
            }
            var stand = new Vector3(target.x, topY, target.z) - dir * (_reach * 0.6f);
            for (float t = 0; t < 3f && Flat(stand - transform.position).magnitude > 0.02f; t += Time.deltaTime)
            {
                float dt = Time.deltaTime;
                _climbPitch = Mathf.MoveTowards(_climbPitch, 0, dt * 200);
                transform.position = Vector3.MoveTowards(transform.position, stand, dt * _walk * 0.6f);
                _speedNow = _walk * 0.6f;
                yield return null;
            }
            _speedNow = 0;
        }

        IEnumerator ClimbDown()
        {
            var edge = new Vector3(_climbFrom.x, transform.position.y, _climbFrom.z);
            for (float t = 0; t < 3f && Flat(edge - transform.position).magnitude > 0.02f; t += Time.deltaTime)
            {
                transform.position = Vector3.MoveTowards(transform.position, edge, Time.deltaTime * _walk * 0.6f);
                _speedNow = _walk * 0.6f;
                yield return null;
            }
            float ground = Ground(transform.position);
            while (transform.position.y > ground + 0.005f)
            {
                float dt = Time.deltaTime;
                _climbPitch = Mathf.MoveTowards(_climbPitch, 75, dt * 240);
                var p = transform.position;
                p.y = Mathf.MoveTowards(p.y, ground, dt * _climbSpeed * 1.4f * (_climbPitch / 75f));
                transform.position = p;
                _speedNow = _climbSpeed;
                yield return null;
            }
            for (float t = 0; t < 0.4f; t += Time.deltaTime) { _climbPitch = Mathf.MoveTowards(_climbPitch, 0, Time.deltaTime * 240); yield return null; }
            _climbPitch = 0;
            _speedNow = 0;
            _climbing = false;
        }

        /// <summary>Personality: now and then, after finishing a delivery, a short
        /// characterful pause (rare, so it never gets in the way of the work).</summary>
        void MaybeGesture()
        {
            if (_step != Step.Choose || _time - _sinceGesture < 45f || Random.value > 0.3f) return;
            _sinceGesture = _time;
            _gestureT = 0;
            _step = Step.Gesture;
            gestures++;
            Sfx.Play(locomotion == "hover" ? "discover" : "servo", transform.position + Vector3.up * 0.3f, -12, 0.15f);
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
                // a full zone overflows into a wider loose pile around the same spot
                float spread = 0.1f + 0.012f * Mathf.Max(0, StagingCount(_destBox) - StagingCapacity / 2);
                var jitter = new Vector3(Random.Range(-spread, spread), 0, Random.Range(-spread, spread));
                float y = e[2] * 0.5f + Mathf.Min(nearby, 5) * e[2] * 0.45f;
                var rot = Quaternion.Euler(Random.Range(-12f, 12f), Random.Range(0, 360f), def.restMode == RestMode.Long ? 90 + Random.Range(-10f, 10f) : Random.Range(-12f, 12f));
                HideCarried(itemId);
                carried.Remove(itemId);
                g.world.SetTransform(itemId, Matrix4x4.TRS(spot + jitter + Vector3.up * y, rot, Vector3.one * def.worldScale));
                g.world.SetState(itemId, ItemState.InWorld, "stage:" + _destBox);   // the destination's staging zone (still UNSORTED)
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
            if (why != "") Notice(why);
        }

        float _noticeT = -999;

        /// <summary>Routine robot troubles are only worth a toast when the robot is near
        /// the player, and at most once every 30 s per robot.</summary>
        void Notice(string text)
        {
            if (Vector3.Distance(transform.position, g.player.transform.position) > 8f || _time - _noticeT < 30f) return;
            _noticeT = _time;
            g.hud.Toast(text);
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
            _model.transform.localRotation = Quaternion.Euler(-_climbPitch, 0, 0);   // clinging to a vertical face
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
                gesture = _step == Step.Gesture ? Mathf.Clamp01(_gestureT / GestureTime) : 0,
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
            if (paused && carried.Count == 0) return $"{displayName}: paused";
            if (_climbing) return $"{displayName}: climbing";
            if (strategy == "assigned_definition" && carried.Count == 0 && _step == Step.Choose)
            {
                string def = assignedDef >= 0 ? g.db.Get(assignedDef).displayName : "no assignment";
                return specialistState switch
                {
                    "COLLECTION_COMPLETE" => $"{displayName}: set complete — {def}",
                    "WAITING_FOR_ACCESS" => $"{displayName}: waiting for access — the remaining {def} are out of reach",
                    _ => $"{displayName}: searching for {def}",
                };
            }
            if (_step == Step.ToPlayer) return $"{displayName}: coming to take your parts";
            if (_step == Step.ToCharge) return $"{displayName}: going to charge ({Battery * 100:0}%)";
            if (_step == Step.Charging) return $"{displayName}: charging {Battery * 100:0}%";
            string zone = zoneCenter.HasValue ? " (zone)" : "";
            return _step switch
            {
                Step.ToItem => carried.Count > 0 ? $"{displayName}: collecting ({carried.Count}/{_capacity})" : $"{displayName}: fetching a part",
                Step.Picking => $"{displayName}: picking up",
                Step.ToDest or Step.Depositing => carried.Count > 0 ? (_canStore ? $"{displayName}: delivering {carried.Count}" : $"{displayName}: taking {carried.Count} to a staging zone") : $"{displayName}: storing",
                _ => status == "" ? $"{displayName}: looking for parts" : $"{displayName}: {status}",
            } + zone;
        }
    }
}
