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
        /// <summary>Summoned: on its way to the workbench (Modify Robot); Docked: parked there, safe to modify
        /// (an incomplete robot stays docked until it is whole again).</summary>
        public enum Mode { Assembling, Activating, Working, Following, Summoned, Docked }
        enum Step { Choose, ToItem, Picking, ToDest, Depositing, ToPlayer, ToCharge, Charging, Gesture }

        /// <summary>id: this instance ("hoverbot_2"); type: its robots.json entry ("hoverbot").</summary>
        public string id, type, displayName, locomotion, strategy;
        public string Owner => "robot:" + id;
        /// <summary>The Sorting Arm: never navigates; SortingArm drives it once operational.</summary>
        public bool Stationary => strategy == "stationary";
        public bool Mobile => !Stationary;
        public SortingArm arm;
        public Mode mode = Mode.Assembling;
        Step _step = Step.Choose;
        public readonly List<int> carried = new();
        public int targetId = -1;
        public int preferDef = -1;
        public string status = "";
        public int delivered, staged, lastStagedId = -1;
        /// <summary>QA: parts a stowing robot put into / took out of its hold with the grippers.</summary>
        public int stows, unstows;
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
        float _reachY;   // height above the feet of the part being picked up (the claws reach down to it)
        float _aimW;     // 0..1 arms raised to the destination (aimed deposit)
        Vector3 _aimPoint;
        // the part just gripped: it starts where it lay and glides into the claws as the arms lift
        int _gripItem = -1;
        Vector3 _gripFromPos;
        Quaternion _gripFromRot;
        float _gripBlend = 1;
        float _gripGlide = 0.35f;   // share of the lift over which a gripped part glides into the claws (longer when it lay further off)
        /// <summary>QA (autoplay, watch mode): how well the hands meet the parts (world metres, seconds).</summary>
        public float maxGripGap, maxStoreGap, maxStageGap, maxAlignTime;
        public int pickCount, storeCount, stageCount, drawerStoreCount;
        public float maxDrawerGap;
        /// <summary>QA: the same, only where the robot could stand beside the target and the target lies within
        /// the arms' physical range (ArmRangeLow..ArmRangeHigh over the feet); the rest is only counted.</summary>
        public float maxPickGapInRange, maxStageGapInRange, maxDrawerGapInRange;
        public int outOfRange;
        const float ArmRangeLow = -0.06f, ArmRangeHigh = 0.32f;
        /// <summary>QA: a robot starts an interaction ("pick", "store", "stage").</summary>
        public static System.Action<Robot, string> QAMoment;
        const float DrawerSlide = 0.1f;   // a drawer opened by a small robot comes out only this far
        // grippers (models with articulated claws): open, closed on a part, and the part's width
        float _clawOpen, _clawClose, _gripW;
        bool _clawsBusy;   // a pick-up, deposit or staging drives the grippers
        /// <summary>A part the claws can't get within this distance of (in a pile, out of the arms' range)
        /// is not taken: it is deferred and another one chosen (F7: no drawing parts in from afar).</summary>
        const float ContactMax = 0.05f;
        /// <summary>QA: gripper state at the moments that matter, the carried part's distance from the
        /// grippers while carrying, deferred pick-ups.</summary>
        public float minOpenAtContact = 1, minClosedAtAttach = 1, minOpenAtRelease = 1, carrySyncMax;
        public int deferred, releases;
        public Vector3 qaTarget;
        public float SpeedQA => _speedNow;
        public float TurnRateQA => _turnRate;
        bool Grips => _model != null && _model.GripPoint.HasValue;
        /// <summary>The load rides in a cargo hold, loaded and unloaded by the grippers (Caterpillarbot).</summary>
        bool Stows => Grips && _model.StowsCargo;
        // stowing robots: the hold slot each carried part rides in (stable while others come and go), the
        // part currently in the grippers (-1: none), the arms' swing over the hold, the parts of this trip
        readonly Dictionary<int, int> _slotOf = new();
        int _clawItem = -1, _tripParts;
        float _stow, _toss;
        float _gripArc;   // > 0: the part flies (thrown into the hold): arc height over the straight line (world m)

        int SlotFor(int itemId)
        {
            if (!_slotOf.TryGetValue(itemId, out var slot))
            {
                slot = 0;
                while (_slotOf.ContainsValue(slot)) slot++;
                _slotOf[itemId] = slot;
            }
            return slot;
        }
        float _extend;   // telescopic forearms out (world m), for models without an aim solve
        float BaseReach => BaseStat("reach_height", 0.35);
        /// <summary>The reach upgrade's telescopic forearms: how far they may extend, a little more than the
        /// height gained, since the arms don't point straight up. A change re-checks which destinations the
        /// arms can physically reach.</summary>
        void SyncExtend()
        {
            if (_model == null) return;
            float max = Mathf.Max(0, _maxPickH - BaseReach) * 1.35f;
            if (Mathf.Abs(max - _model.armExtendMax) > 1e-4f) { _model.armExtendMax = max; _physical.Clear(); }
        }
        /// <summary>How far to extend (world m) to reach a point at height y over the feet.</summary>
        float ExtendFor(float y) => _model == null || _model.armExtendMax <= 0 ? 0 : Mathf.Clamp01((y - BaseReach) / Mathf.Max(0.01f, _maxPickH - BaseReach)) * _model.armExtendMax;
        readonly Dictionary<int, (float until, int tries)> _deferredUntil = new();
        /// <summary>A part the grippers couldn't get to waits before it is tried again, longer each time
        /// (not cleared with the general unreachable list, so a robot never cycles on one part).</summary>
        void Defer(int itemId)
        {
            int n = _deferredUntil.TryGetValue(itemId, out var e) ? e.tries + 1 : 1;
            _deferredUntil[itemId] = (_time + 90f * n, n);
        }
        float? _gripReachCache, _gripLowCache;
        RobotModel _gripModel;   // the caches hold for this model (a fitted head or arm rebuilds it)
        /// <summary>Where it stands to take a part from the sorting tray: the nearest free floor beside the part
        /// (further out if the tray's surroundings are crowded). A tray part is always within reach from there,
        /// however far the arms really are (a game rule: the player's tray is always emptied).</summary>
        Vector3? TrayGoal(ItemInstance inst) => _nav.NearestFree(inst.Position, _reach * 0.7f) ?? _nav.NearestFree(inst.Position, 0.65f) ?? _nav.NearestFree(inst.Position, 1.5f);

        /// <summary>Gripper models: can the claws get to a part from the standing spot the path would end at?
        /// A part lying inside a pile (the nearest free spot is further than the claws reach), or down in a
        /// hollow lower than they bend to from the heap it would stand on (playtest 2026-10-25: Retrobot
        /// walked to such parts again and again and gave up on each), is skipped when choosing, instead of
        /// walking there and deferring it.</summary>
        bool InGripRange(ItemInstance inst, Vector3 goal)
        {
            if (!Grips) return true;
            if (_gripModel != _model) { _gripModel = _model; _gripReachCache = _gripLowCache = null; }
            _gripReachCache ??= _model.GripReach(0.015f);
            _gripLowCache ??= _model.GripLow();
            float reach = (_gripReachCache ?? 0.1f) + ContactMax + 0.03f;
            bool low = _gripLowCache is float lo && inst.Position.y - Ground(goal) < lo - ContactMax;
            if (!low && Flat(inst.Position - goal).magnitude <= reach) return true;
            Defer(inst.id);
            return false;
        }
        const float RaisePickHeight = 0.08f;   // parts above this (over the feet) are reached by raising the arms

        /// <summary>Above this the arms rise to a part instead of the robot bending down: RaisePickHeight, or
        /// lower where the claws already hang lower than that (Caterpillarbot), since bending only takes them
        /// further down. Read before the reach starts, while the claws hang at rest.</summary>
        float RaiseHeight() => _model.GripPoint is Vector3 gp ? Mathf.Min(RaisePickHeight, gp.y - transform.position.y + 0.01f) : RaisePickHeight;
        bool _planAhead;        // robots.json plan_ahead: choose the next part while walking to deliver
        float _decidePause;     // robots.json decide_pause: standing still after a delivery before setting off
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
        float Walk => _walk * g.Scale("robot_speed") * (1f + (float)(g.progression?.Effect("robot_speed") ?? 0)) * (Battery <= 0.01f ? 0.4f : 1f);   // preset scaling, Shift Planner; an empty battery limps home
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
            _planAhead = Json.Bool(c, "plan_ahead");
            _decidePause = (float)Json.Num(c, "decide_pause", 0.15);
            _largeItem = (float)Json.Num(RootConfig(), "large_item_size", 0.25);
            ApplyUpgrades();
            if (!Flies && !Stationary)
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
                "sorting_arm" => new GameObject("Model").AddComponent<SortingArmModel>(),
                _ => (RobotModel)new GameObject("Model").AddComponent<RetrobotModel>(),
            };
            _model.transform.SetParent(transform, false);
            if (_model is HoverbotModel hm) hm.PaintFactory(id);   // three Hoverbots, one model, three factory skins
            _model.Init();
            _model.transform.localScale = Vector3.one * (_height / _model.ModelHeight);
            if (_model is SortingArmModel sam)
            {
                sam.SetStanding(true);   // folded on its coupler while on the bench
                arm = gameObject.AddComponent<SortingArm>();
                arm.Init(this, g, sam);
            }
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

        /// <summary>Points the model at the modules fitted to this chassis (its own, a donor's, or an empty slot)
        /// and rebuilds it when that changed. A chassis not built yet shows its own parts as always.</summary>
        public void ApplyModules()
        {
            var reg = g.modules;
            bool tracked = reg != null && RobotModules.IsChassis(id) && reg.Built(id);
            string From(ModuleSlot s)
            {
                if (!tracked && mode == Mode.Assembling) return s == ModuleSlot.Prop ? null : g.workbench?.DonorOrigin(Project, s);   // another robot's head or arm on the bench
                if (!tracked || (s == ModuleSlot.Prop && !RobotModules.IsHover(id))) return null;
                var m = reg.At(id, s);
                return m == null ? "" : m.origin == id ? null : m.origin;
            }
            var (h, l, r, p) = (From(ModuleSlot.Head), From(ModuleSlot.ArmL), From(ModuleSlot.ArmR), From(ModuleSlot.Prop));
            if (_modelFroms == (h, l, r, p)) return;
            _modelFroms = (h, l, r, p);
            (_model.headFrom, _model.armLFrom, _model.armRFrom, _model.propFrom) = (h, l, r, p);
            _model.Rebuild();
            if (Operational) _model.SetEyes(true);
        }
        (string, string, string, string) _modelFroms;
        /// <summary>QA: the module origins the model was last built with (null = own, "" = empty).</summary>
        public (string h, string l, string r, string p) ModelFroms => _modelFroms;

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
            (_model.headFrom, _model.armLFrom, _model.armRFrom) = (g.workbench.DonorOrigin(p, ModuleSlot.Head), g.workbench.DonorOrigin(p, ModuleSlot.ArmL), g.workbench.DonorOrigin(p, ModuleSlot.ArmR));
            _modelFroms = (_model.headFrom, _model.armLFrom, _model.armRFrom, _model.propFrom);
            _model.assembling = true;
            _model.mountReady = part => !p.requires.TryGetValue(part, out var req) || req.All(have.Contains);
            _model.Build(have);
            _model.SetEyes(false);
            var (pos, rot) = g.workbench.MatSpot(p.mat);
            transform.SetPositionAndRotation(pos, rot);
            if (Flies) transform.position = pos + Vector3.up * Mathf.Max(0, LowestHang());   // a Hoverbot's hanging claws rest on the mat, not in the bench
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

        /// <summary>Model QA: one neutral animation frame, the standing rest pose.</summary>
        public void PoseRest()
        {
            if (_model is SortingArmModel sam)
            {
                sam.SetStanding(true);
                sam.Pose(90, 60, 100, 20);   // the sheet's hero pose: upper arm up and out, forearm down, claw to the floor
                sam.Grip(0.8f);
                return;
            }
            _model.Animate(new RobotAnim { dt = 1, altitude = Flies ? 0.6f : 0 });
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
            g.modules?.EnsureBuilt(id);
            if (arm != null) { arm.ToBench(); return; }
            AdoptCarried();
        }

        /// <summary>How far the model reaches below the robot's origin (a Hoverbot's hanging arms), in metres.</summary>
        float LowestHang()
        {
            float low = transform.position.y;
            foreach (var r in _model.GetComponentsInChildren<Renderer>()) if (r.enabled) low = Mathf.Min(low, r.bounds.min.y);
            return transform.position.y - low;
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
            // a Hoverbot lifts off the mat first, until its hanging arms clear the bench top
            float helloAlt = 0;
            if (Flies)
            {
                var start = transform.position;
                float lift = LowestHang() + 0.12f;
                for (float t = 0; t < 0.8f; t += Time.deltaTime)
                {
                    _time += Time.deltaTime;
                    helloAlt = lift * Mathf.SmoothStep(0, 1, t / 0.8f);
                    transform.position = start + Vector3.up * helloAlt;
                    _model.Animate(new RobotAnim { dt = Time.deltaTime, gait = 0.3f, phase = t * 9, active = true, time = _time, altitude = helloAlt });
                    yield return null;
                }
                helloAlt = lift;
                transform.position = start + Vector3.up * lift;
            }
            // a little "hello": the model wiggles in its own way while it looks around
            for (float t = 0; t < 1.2f; t += Time.deltaTime)
            {
                _time += Time.deltaTime;
                _model.Animate(new RobotAnim
                {
                    dt = Time.deltaTime, gait = 0.6f * Mathf.Sin(t / 1.2f * Mathf.PI), phase = t * 9, active = true, time = _time, altitude = helloAlt,
                    lookAt = transform.position + Quaternion.Euler(0, Mathf.Sin(t * 6) * 50, 0) * transform.forward, lookWeight = 1,
                });
                yield return null;
            }
            // off the bench: a hop down to its charging mat (Hoverbot flies down); the Sorting
            // Arm waits folded in front of the bench until the player carries it to a socket
            if (arm != null)
            {
                mode = Mode.Working;
                arm.ToBench();
                Sfx.Play("complete", transform.position + Vector3.up * 0.3f, -2);
                g.progression?.OnRobotRepaired(id);
                GameEvents.Raise(GameEventKind.RobotActivated, Owner, type);
                g.hud.Toast($"{displayName} is ready — carry it to an overhead socket above a drawer cabinet", true);
                g.workbench?.Refresh();
                foreach (var r in g.robots) if (r != this && r.mode == Mode.Assembling) r.RefreshAssembly();
                yield break;
            }
            var from = transform.position;
            var to = home + (Flies ? Vector3.up * 0.6f : Vector3.zero);
            if (Flies)
            {
                // up, across and down: over the bench and the holding tray, never through them
                float apex = Mathf.Max(from.y, to.y) + 0.25f;
                var up = new Vector3(from.x, apex, from.z);
                var over = new Vector3(to.x, apex, to.z);
                var legs = new (Vector3 a, Vector3 b, float dur)[]
                {
                    (from, up, 0.5f),
                    (up, over, Mathf.Clamp(Flat(over - up).magnitude / 0.9f, 0.6f, 3f)),
                    (over, to, 0.9f),
                };
                foreach (var (a, b, legDur) in legs)
                    for (float t = 0; t < legDur; t += Time.deltaTime)
                    {
                        _time += Time.deltaTime;
                        var p = Vector3.Lerp(a, b, Mathf.SmoothStep(0, 1, t / legDur));
                        transform.position = p;
                        transform.rotation = Quaternion.RotateTowards(transform.rotation, homeRot, _turn * Time.deltaTime);
                        _altitude = p.y - Ground(p);
                        _model.Animate(new RobotAnim { dt = Time.deltaTime, gait = 0.3f, phase = t * 9, active = true, time = _time, altitude = _altitude });
                        yield return null;
                    }
                from = to;   // the settle below only finishes the heading
            }
            float dur = Flies ? 0.3f : 0.9f;
            for (float t = 0; t < dur; t += Time.deltaTime)
            {
                float u = t / dur;
                _time += Time.deltaTime;
                var p = Vector3.Lerp(from, to, Flies ? Mathf.SmoothStep(0, 1, u) : u);
                if (!Flies) p.y += Mathf.Sin(u * Mathf.PI) * 0.35f;
                transform.position = p;
                transform.rotation = Quaternion.Slerp(transform.rotation, homeRot, u);
                if (Flies) _altitude = p.y - Ground(p);
                _model.Animate(new RobotAnim { dt = Time.deltaTime, gait = Flies ? 0.3f : 0, phase = t * 9, active = true, time = _time, altitude = _altitude, armReach = Flies ? 0 : Mathf.Sin(u * Mathf.PI) });
                yield return null;
            }
            transform.SetPositionAndRotation(to, homeRot);
            if (!Flies) Sfx.Play("drop_floor", transform.position, -4);
            Sfx.Play("complete", transform.position + Vector3.up * 0.3f, -2);
            mode = Mode.Working;
            _step = Step.Choose;
            // its head, arms (and propeller) are mounted modules from now on: its own, or the other
            // robots' heads and arms it was built with
            g.modules?.OnFirstActivation(id, g.playTime, Project is RobotWorkbench.Project bp ? g.workbench.DonorOrigins(bp) : null);
            ApplyModules();
            g.progression?.OnRobotRepaired(id);
            GameEvents.Raise(GameEventKind.RobotActivated, Owner, type);
            g.hud.Toast($"{displayName} is online and starts sorting!", true);
            g.modules?.CheckAllOperational(rid => g.robots.Find(r => r.id == rid)?.Operational == true);
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
            // a save from before the carry limit: a part that is player-only now is set down at its feet, once the
            // robot stands where it was saved (LoadState; else on its first update)
            _setDownPending = carried.Count > 0;
            if (carried.Count > 0) { _step = Step.ToDest; _path = null; }
        }

        bool _setDownPending;

        void SetDownPlayerOnly()
        {
            _setDownPending = false;
            var tooBig = carried.Where(i => !g.world.DefOf(i).robotCarry).ToList();
            foreach (var i in tooBig) { SetDown(i); carried.Remove(i); }
            if (tooBig.Count == 0) return;
            Debug.Log($"[rescale] {id} sets down {tooBig.Count} player-only part(s) it carried in the save");
            if (carried.Count == 0) { _step = Step.Choose; _path = null; }
        }

        // ---------------------------------------------------------------- commands

        public string CommandFocus(int defId)
        {
            if (!Active) return $"{displayName} isn't assembled yet ({status})";
            if (g.db.Get(defId).IsRobotPart) return $"{displayName} leaves robot parts to you";
            if (!g.db.CanRobotCarry(defId)) return $"{displayName}: {g.db.Get(defId).displayName} is too big for robots — yours to sort";
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
            if (defId >= 0 && !g.db.CanRobotCarry(defId)) return $"{displayName}: {g.db.Get(defId).displayName} is too big for robots — yours to sort";
            assignedDef = defId;
            autoAssigned = auto;
            _unreachable.Clear();
            _completeT = 0;
            if (AtBench) return $"{displayName}: noted — after the workbench";
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
                ["summon"] = mode == Mode.Docked ? "docked" : mode == Mode.Summoned || _summonPending ? "summoned" : "",
                ["delivered"] = (double)delivered, ["staged"] = (double)staged,
                ["pos"] = new List<object> { (double)transform.position.x, (double)transform.position.y, (double)transform.position.z },
                ["yaw"] = (double)transform.eulerAngles.y,
            };
            if (zoneCenter.HasValue) d["zone"] = new List<object> { (double)zoneCenter.Value.x, (double)zoneCenter.Value.z };
            if (arm != null) d["arm"] = arm.ToDict();
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
            if (arm != null) { if (Operational) arm.Load(Json.Obj(d.GetValueOrDefault("arm"))); return; }
            if (!Active) return;
            var p = Json.Arr(d.GetValueOrDefault("pos"));
            if (p.Count == 3) transform.SetPositionAndRotation(new Vector3(Json.F(p[0]), Json.F(p[1]), Json.F(p[2])), Quaternion.Euler(0, (float)Json.Num(d, "yaw"), 0));
            if (Json.Bool(d, "following")) mode = Mode.Following;
            _path = null;
            // Modify Robot: a robot on its way resumes the summons (its cargo was saved with the items and is
            // set down again); a docked one is parked at its spot again
            switch (Json.Str(d, "summon"))
            {
                case "summoned": Summon(); break;
                case "docked": DockAtSpot(); break;
            }
            if (_setDownPending) SetDownPlayerOnly();
        }

        // ---------------------------------------------------------------- Modify Robot: summons and docking

        /// <summary>Summoned or docked: the robot is the workbench's (no tasks; the player modifies it there).</summary>
        public bool AtBench => mode == Mode.Summoned || mode == Mode.Docked;
        /// <summary>Moves about the floor under its own power (feet on the ground, eases apart from the others).</summary>
        public bool Roams => Active || AtBench;
        public bool Docked => mode == Mode.Docked;
        public bool SummonPending => _summonPending;
        /// <summary>Why a summoned robot is not on its way yet (e.g. no path), shown on its status line.</summary>
        public string summonStatus = "";
        bool _summonPending;
        float _summonRetryT, _summonT;

        /// <summary>Calls the robot to its parking spot at the workbench. It finishes a pick-up or delivery
        /// in progress (never half a grip), sets its cargo down, then walks or flies over and docks.</summary>
        public string Summon()
        {
            if (Stationary || arm != null) return $"{displayName} stays where it is";
            if (!Operational) return $"{displayName} isn't assembled yet";
            if (AtBench) return mode == Mode.Docked ? $"{displayName} is already docked" : $"{displayName} is already on its way";
            _summonPending = true;
            summonStatus = "";
            if (_step != Step.Picking && _step != Step.Depositing && !_climbing) BeginReturn();
            return $"{displayName}: coming to the workbench";
        }

        /// <summary>Takes back a summons before the robot docks: it goes back to work.</summary>
        public bool CancelSummon()
        {
            if (mode == Mode.Docked) return false;
            if (_summonPending) { _summonPending = false; return true; }
            if (mode != Mode.Summoned) return false;
            mode = Mode.Working;
            _step = Step.Choose;
            _path = null;
            summonStatus = "";
            return true;
        }

        /// <summary>Sends a docked robot back to work. An incomplete robot (a required module missing) stays
        /// docked: the reason comes back.</summary>
        public string ReleaseFromBench()
        {
            if (mode == Mode.Summoned) return CancelSummon() ? "" : $"{displayName} isn't at the workbench";
            if (mode != Mode.Docked) return $"{displayName} isn't at the workbench";
            if (g.modules != null && !g.modules.Complete(id))
                return $"{displayName} is incomplete: fit a {string.Join(" and a ", g.modules.Missing(id).Select(RobotModules.SlotName))} first";
            mode = Mode.Working;
            _step = Step.Choose;
            _path = null;
            _idleT = 0.3f;
            Sfx.Play("servo", transform.position + Vector3.up * 0.3f, -6);
            return "";
        }

        /// <summary>The summons takes over now: reservations and plans are dropped, a pending handover is
        /// forgotten. A walker sets its cargo down where it is; a flier first comes down (UpdateSummoned).</summary>
        void BeginReturn()
        {
            _summonPending = false;
            Release();
            if (_planner != null) { StopCoroutine(_planner); _planner = null; }
            _planReady = false;
            _plannedId = -1;
            handoverPending = false;
            if (_step == Step.Charging) _model.SetBeacon(false);
            if (carried.Count > 0 && !Flies) DropCarried("");
            mode = Mode.Summoned;
            _step = Step.Choose;
            _path = null;
            _summonRetryT = 0;
            _summonT = 0;
            summonStatus = "";
        }

        void UpdateSummoned(float dt)
        {
            _summonT += dt;
            var (spot, rot) = g.workbench.ParkSpot(id);
            if (Flies)
            {
                if (carried.Count > 0)
                {
                    // a flier never drops parts from the air: it comes down first
                    Brake(dt);
                    HoverHold(dt, _height * 0.5f + 0.15f);
                    if (_altitude < _height * 0.5f + 0.25f) DropCarried("");
                    return;
                }
                if (FlyTo(spot + Vector3.up * 0.45f, dt, true)) Dock(rot);
                return;
            }
            if (_path == null)
            {
                _summonRetryT -= dt;
                if (_summonRetryT > 0) { Brake(dt); return; }
                var goal = _nav.NearestFree(spot, 0.35f);
                _path = goal.HasValue ? _nav.Path(transform.position, goal.Value) : null;
                _pathIdx = 0;
                // no way through right now (blocked by heaps): it says so and tries again
                if (_path == null) { summonStatus = "can't reach the workbench — trying again"; _summonRetryT = 2f; Brake(dt); return; }
                summonStatus = "";
            }
            if (Steer(dt) || (_summonT > 45f && Flat(spot - transform.position).magnitude < 0.6f)) Dock(rot);
            else if (_summonT > 60f) { _path = null; _summonT = 0; }   // stuck on the way: plan again
        }

        void Dock(Quaternion rot, bool announce = true)
        {
            mode = Mode.Docked;
            _path = null;
            summonStatus = "";
            _dockRot = rot;
            if (!announce) return;   // loading never raises events
            GameEvents.Raise(GameEventKind.RobotSummoned, Owner, type, destination: id);
            Sfx.Play("servo", transform.position + Vector3.up * 0.3f, -8);
        }

        Quaternion _dockRot = Quaternion.identity;

        void UpdateDocked(float dt)
        {
            Brake(dt);
            transform.rotation = Quaternion.RotateTowards(transform.rotation, _dockRot, _turn * dt);   // faces the bench
            // a Hoverbot without its propeller can't hover: it sets down on its base
            if (Flies) HoverHold(dt, g.modules != null && g.modules.At(id, ModuleSlot.Prop) == null ? _height * 0.5f : 0.45f);
            Battery = Mathf.Min(1, Battery + 0.25f * dt / (_chargeMinutes * 60f));   // trickle-charges while it waits
        }

        /// <summary>Load / QA: parks the robot at its spot at once, docked (silently).</summary>
        public void DockAtSpot()
        {
            if (Stationary || arm != null || !Operational) return;
            Release();
            var (spot, rot) = g.workbench.ParkSpot(id);
            transform.SetPositionAndRotation(spot + Vector3.up * (Flies ? 0.45f : 0), rot);
            _summonPending = false;
            _step = Step.Choose;
            _path = null;
            Dock(rot, false);
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
                if (!g.world.DefOf(itemId).robotCarry) continue;   // player-only: the player keeps it
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
            if (!AtBench) mode = Mode.Working;
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

        /// <summary>QA (benchmark): total CPU time spent in robot updates (ms) and the worst single update.</summary>
        public static double cpuMs, worstMs;
        public static string worstWhat = "";
        static readonly System.Diagnostics.Stopwatch _sw = new();

        void Update()
        {
            _sw.Restart();
            UpdateInner();
            double ms = _sw.Elapsed.TotalMilliseconds;
            cpuMs += ms;
            if (ms > worstMs) { worstMs = ms; worstWhat = $"{id} {_step}"; }
        }

        void UpdateInner()
        {
            if (g == null || g.world == null) return;
            if (g.state == "paused" || g.state == "menu") { if (_loop != null) _loop.volume = 0; return; }   // the workshop holds still
            float dt = Time.deltaTime;
            _time += dt;
            if (Stationary) return;   // SortingArm drives the arm
            if (_setDownPending) SetDownPlayerOnly();
            var fwd0 = transform.forward;
            if (_summonPending && Active && _step != Step.Picking && _step != Step.Depositing && !_climbing) BeginReturn();
            if (mode == Mode.Working) UpdateWork(dt);
            else if (mode == Mode.Following) UpdateFollow(dt);
            else if (mode == Mode.Summoned) UpdateSummoned(dt);
            else if (mode == Mode.Docked) UpdateDocked(dt);
            else Brake(dt);
            _turnRate = Vector3.SignedAngle(fwd0, transform.forward, Vector3.up) / Mathf.Max(dt, 1e-4f);   // + = turning right
            if (!Flies && Roams && !_climbing)
            {
                // feet (wheels, tracks) on top of whatever small parts are underfoot
                var p = transform.position;
                float ground = Ground(p);
                p.y = ground > p.y ? Mathf.MoveTowards(p.y, ground, dt * 1.2f) : Mathf.MoveTowards(p.y, ground, dt * 2.5f);
                transform.position = p;
            }
            if (Roams && !Flies && !_climbing) Separate(dt);
            Animate(dt);
        }

        /// <summary>Ground robots don't walk through each other: overlapping ones ease apart
        /// (never into a blocked cell), the busy one keeps its way.</summary>
        void Separate(float dt)
        {
            foreach (var o in g.robots)
            {
                if (o == this || !o.Roams || o.Flies || o.Stationary) continue;
                var d = Flat(transform.position - o.transform.position);
                float min = _radius + o._radius + 0.04f, dist = d.magnitude;
                if (dist >= min) continue;
                var away = dist > 1e-4f ? d / dist : Quaternion.Euler(0, (id.GetHashCode() & 255) * 1.4f, 0) * Vector3.forward;
                var p = transform.position + away * Mathf.Min(min - dist, dt * 0.6f);
                if (_nav == null || _nav.Walkable(p)) transform.position = p;
            }
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
            // batteries (cosmetic but consistent, IMPLEMENTATION_AUDIT §G): drain only while moving
            // or handling a part; an idle robot trickle-charges at a quarter of the mat's rate
            bool busy = _speedNow > 0.02f || _climbing || _step == Step.Picking || _step == Step.Depositing;
            if (_step != Step.Charging)
                Battery = busy ? Mathf.Max(0, Battery - dt / (_batteryMinutes * 60f)) : Mathf.Min(1, Battery + 0.25f * dt / (_chargeMinutes * 60f));
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
                    if (_planReady && _plannedId >= 0) FaceTowards(g.world.instances[_plannedId].Position, dt);   // "deciding": looks at the next part
                    _idleT -= dt;
                    if (_idleT <= 0 && !TryCommitPlan()) ChooseNext();
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
                    else if (!Flies && _pathIdx >= _path.Count) { _unreachable.Add(targetId); Release(); _step = carried.Count > 0 ? Step.ToDest : Step.Choose; _path = null; }   // a delivery plans its own path (the spent one would "arrive" with no destination)
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
                        // deep in a heap: the nearest open floor within 4 m, and it reaches over from there
                        if (_path == null && _nav.NearestFree(pp, 4f) is Vector3 far) _path = _nav.Path(transform.position, far);
                        _pathIdx = 0;
                    }
                    if (_path == null) { Brake(dt); break; }   // keep trying until the request times out
                    if (Steer(dt) && dist < 4f) TakeFromPlayer();   // as close as it can get
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
                        if (_path == null)
                        {
                            var goal = _nav.NearestFree(this.home, 1.5f) ?? this.home;
                            _path = _nav.Path(transform.position, goal);
                            _pathIdx = 0;
                            // no charging away from the mat: it waits (trickle-charging) and tries again
                            if (_path == null) { status = "can't reach its charger"; Notice($"{displayName} can't reach its charging mat"); _step = Step.Choose; _idleT = 5f; break; }
                        }
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
                    if (_planAhead && !Flies && _planner == null && !_planReady && SlotsUsed >= _capacity && _path != null && _path.Count > 0)
                        _planner = StartCoroutine(PlanNext(_path[_path.Count - 1]));
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
        double _tNav, _tSort, _tAll;
        void ChooseNext()
        {
            var sw = System.Diagnostics.Stopwatch.StartNew();
            _tNav = _tSort = 0;
            ChooseNextInner();
            _tAll = sw.Elapsed.TotalMilliseconds;
            if (_tAll > 20) Debug.Log($"[robotcpu] {id} ChooseNext {_tAll:0} ms: nav refresh {_tNav:0}, filter+sort {_tSort:0}, paths {_tAll - _tNav - _tSort:0} ms");
        }

        void ChooseNextInner()
        {
            var me = transform.position;
            if (SlotsUsed >= _capacity) { _step = Step.ToDest; _path = null; return; }
            var swn = System.Diagnostics.Stopwatch.StartNew();
            if (!Flies) _nav.RefreshItems(g.world, _stepH);
            _tNav = swn.Elapsed.TotalMilliseconds;
            var types = carried.Select(c => g.world.instances[c].defId).ToHashSet();
            IEnumerable<ItemInstance> pool = null;
            System.Func<ItemInstance, float> score = i => (i.Position - me).sqrMagnitude;
            specialistState = "";
            _stagedNow = StagedCounts();   // the staging cap holds for every robot (never waits on a full zone)
            // parts the player put in the sorting tray come first, for every robot (playtest 2026-10-25),
            // before a specialist's assignment or a Hoverbot's staging zones; only a part the player
            // asked for by name (R) goes before them
            var tray = g.index.QuerySphere(g.TrayCenter, 1f).Select(i => g.world.instances[i]).Where(i => g.InTray(i.Position) && Pickable(i)).ToList();
            if (strategy == "assigned_definition" && (tray.Count == 0 || preferDef >= 0))
            {
                if (!ChooseAssigned(ref pool, me)) { if (carried.Count > 0) { _step = Step.ToDest; _path = null; } else _idleT = 1.5f; return; }
            }
            else
            {
                if (tray.Count > 0 && preferDef < 0) pool = tray;
                if (preferDef >= 0)
                {
                    var pref = g.world.IdsOf(preferDef).Select(i => g.world.instances[i]).Where(Pickable).ToList();
                    if (pref.Count > 0) pool = pref; else preferDef = -1;
                }
                if (pool == null && strategy == "staging_first")
                {
                    // Hoverbot: finish what the others left in the staging zones first
                    // ...except the piles below a working Sorting Arm's drawers: those are the arm's
                    var staged = g.world.instances.Where(i => i.state == ItemState.InWorld && i.owner.StartsWith("stage:") && !g.ArmServes(i.owner.Substring(6)) && Pickable(i)).ToList();
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
                if (strategy == "opportunistic")
                    // the nearest part first; one it can store itself is worth 0.4 m of walking more than one for a high
                    // destination (playtest 2026-10-25: "pick up what is nearest"); with Better Targeting, sets already
                    // started come first too
                    score = i => (i.Position - me).magnitude + (ReachableDest(g.db.Get(i.defId).storageBox) ? 0 : 0.4f)
                                 - (_targetRank > 0 && g.progress.SortedOf(i.defId) > 0 ? 1.5f : 0);
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
            int tries = 0, coveredSkips = 0;
            var prof = System.Diagnostics.Stopwatch.StartNew();
            var ordered = pool.Where(Pickable).OrderBy(score).ToList();
            _tSort = prof.Elapsed.TotalMilliseconds;
            if (TryTargets(ordered, me, types, ref tries, ref coveredSkips)) return;
            // nothing in the tray it can get at (covered, or no free spot beside it): the rest of the floor
            // instead of waiting on the tray for ever
            if (pool == tray && carried.Count == 0)
            {
                pool = g.world.instances;
                ordered = pool.Where(Pickable).OrderBy(score).ToList();
                tries = coveredSkips = 0;
                if (TryTargets(ordered, me, types, ref tries, ref coveredSkips)) return;
            }
            if (carried.Count > 0) { _step = Step.ToDest; _path = null; return; }
            _idleT = 0.5f;
            if (tries == 0) status = "nothing left it can reach";
        }

        /// <summary>Takes the first of these candidates it can walk to and grip (a bounded number of tries per
        /// frame); false if none.</summary>
        bool TryTargets(List<ItemInstance> ordered, Vector3 me, HashSet<int> types, ref int tries, ref int coveredSkips)
        {
            int maxTries = 6 + 3 * _searchRank;
            foreach (var inst in ordered)
            {
                if (carried.Count > 0 && ((inst.Position - me).magnitude > _gather || (!types.Contains(inst.defId) && types.Count >= _types) || SlotsUsed + SlotsOf(inst.id) > _capacity)) break;
                bool inTray = g.InTray(inst.Position);   // anything in the sorting tray can always be taken (TrayGoal)
                if (!Flies && !inTray && g.Covered(inst.id)) { if (++coveredSkips > 40) break; continue; }   // under other parts: wait until it is uncovered
                if (++tries > maxTries) break;   // keep the frame cheap; continue next frame
                if (Flies)
                {
                    _hoverTarget = HoverPoint(inst.Position, true);
                }
                else
                {
                    bool high = inst.Position.y > _maxPickH;
                    var goal = inTray ? TrayGoal(inst) : _nav.NearestFree(inst.Position, _reach * 0.7f) ?? _nav.NearestFree(inst.Position, high ? 0.95f : _reach);
                    if (goal.HasValue && !high && !inTray && !InGripRange(inst, goal.Value)) continue;   // a climber closes in on a high part on top (ClimbTo)
                    var path = goal.HasValue ? _nav.Path(me, goal.Value) : null;
                    if (path == null) { _unreachable.Add(inst.id); continue; }
                    _path = path;
                    _pathIdx = 0;
                }
                targetId = inst.id;
                g.robotReserved.Add(inst.id);
                _step = Step.ToItem;
                if (strategy == "assigned_definition") specialistState = carried.Count == 0 && Flat(inst.Position - me).magnitude > _scan ? "SEARCHING" : "WORKING";
                return true;
            }
            return false;
        }

        // ---------------------------------------------------------------- planning ahead (robots.json plan_ahead)

        Coroutine _planner;
        bool _planReady;
        int _plannedId = -1;
        readonly List<(float score, int id)> _planList = new();
        public static int planUsed, planFallback;   // QA: how often a planned part was taken

        /// <summary>Chooses the next part while the robot walks to deliver: the same rules and scoring as
        /// ChooseNext, from the spot where it will stand, spread over frames (items scored in slices, one
        /// path per frame) so the choice never costs a frame of its own. Reserves nothing; the result is
        /// checked again when it is used (TryCommitPlan).</summary>
        IEnumerator PlanNext(Vector3 from)
        {
            _planReady = false;
            _plannedId = -1;
            yield return null;
            _nav.RefreshItems(g.world, _stepH);
            _stagedNow = StagedCounts();
            _planList.Clear();
            var all = g.world.instances;
            for (int i = 0; i < all.Count; i++)
            {
                var inst = all[i];
                if (Pickable(inst))
                {
                    float sc = (inst.Position - from).magnitude;
                    if (strategy == "opportunistic")
                        sc += (ReachableDest(g.db.Get(inst.defId).storageBox) ? 0 : 0.4f) - (_targetRank > 0 && g.progress.SortedOf(inst.defId) > 0 ? 1.5f : 0);
                    _planList.Add((sc, inst.id));
                }
                if (i % 600 == 599) yield return null;
            }
            _planList.Sort((a, b) => a.score.CompareTo(b.score));
            int tries = 0, maxTries = 6 + 3 * _searchRank, coveredSkips = 0;
            foreach (var (_, id) in _planList)
            {
                var inst = g.world.instances[id];
                if (!Pickable(inst)) continue;
                bool inTray = g.InTray(inst.Position);
                if (!inTray && g.Covered(id)) { if (++coveredSkips > 40) break; continue; }
                if (++tries > maxTries) break;
                yield return null;   // one path per frame
                bool high = inst.Position.y > _maxPickH;
                var goal = inTray ? TrayGoal(inst) : _nav.NearestFree(inst.Position, _reach * 0.7f) ?? _nav.NearestFree(inst.Position, high ? 0.95f : _reach);
                if (goal.HasValue && (inTray || InGripRange(inst, goal.Value)) && _nav.Path(from, goal.Value) != null) { _plannedId = id; break; }
            }
            _planList.Clear();
            _planReady = true;
            _planner = null;
        }

        /// <summary>After a delivery: takes the part planned on the way if it is still a valid choice
        /// (still pickable, not reserved, not covered, no tray work or player request first) and still
        /// reachable from here (one path). Otherwise false, and the robot chooses as usual.</summary>
        bool TryCommitPlan()
        {
            if (!_planAhead) return false;
            if (_planner != null) { StopCoroutine(_planner); _planner = null; }
            bool ready = _planReady;
            int id = _plannedId;
            _planReady = false;
            _plannedId = -1;
            if (!ready || id < 0) return false;
            bool ok = carried.Count == 0 && preferDef < 0 && !handoverPending && !paused && Battery >= 0.12f;
            var inst = g.world.instances[id];
            if (ok) ok = Pickable(inst) && !g.Covered(id) && !g.index.QuerySphere(g.TrayCenter, 1f).Any(i => g.InTray(g.world.instances[i].Position) && Pickable(g.world.instances[i]));
            if (ok)
            {
                _nav.RefreshItems(g.world, _stepH);
                bool high = inst.Position.y > _maxPickH;
                var goal = _nav.NearestFree(inst.Position, _reach * 0.7f) ?? _nav.NearestFree(inst.Position, high ? 0.95f : g.InTray(inst.Position) ? 0.65f : _reach);
                var path = goal.HasValue ? _nav.Path(transform.position, goal.Value) : null;
                if (path != null)
                {
                    _path = path;
                    _pathIdx = 0;
                    targetId = id;
                    g.robotReserved.Add(id);
                    _step = Step.ToItem;
                    status = "";
                    planUsed++;
                    Debug.Log($"[robotplan] {this.id} takes the part it planned on the way ({planUsed} planned, {planFallback} chosen afresh)");
                    return true;
                }
            }
            planFallback++;
            Debug.Log($"[robotplan] {this.id} chooses afresh ({planUsed} planned, {planFallback} chosen afresh)");
            return false;
        }

        /// <summary>Spiderbot: works through its one assigned definition. Known copies in
        /// detection range first, then it searches further. When none can be reached it
        /// reports WAITING_FOR_ACCESS (and retries later) instead of looping; when every
        /// copy is accounted for, COLLECTION_COMPLETE — after a short pause it picks the
        /// nearest unfinished set itself unless the player assigns one.</summary>
        readonly HashSet<int> _failedDefs = new();
        float _failedDefsT;

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
                // (never straight back to a set it just gave up on: it would idle on it forever)
                if (_time - _failedDefsT > 180f) { _failedDefs.Clear(); _failedDefsT = _time; }
                var first = g.world.instances.Where(i => !_failedDefs.Contains(i.defId) && Pickable(i)).OrderBy(i => (i.Position - me).sqrMagnitude).FirstOrDefault();
                if (first == null) { specialistState = "WAITING_FOR_ACCESS"; return false; }
                Assign(first.defId, true);
            }
            var mine = g.world.IdsOf(assignedDef).Select(i => g.world.instances[i]).Where(Pickable).ToList();
            if (mine.Count == 0)
            {
                specialistState = "WAITING_FOR_ACCESS";
                if ((_waitT += 0.5f) > 20f) { _waitT = 0; _unreachable.Clear(); }   // try the blocked ones again later
                if (autoAssigned && _waitT > 6f) { _waitT = 0; _failedDefs.Add(assignedDef); assignedDef = -1; }   // its own pick: move on to another set
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
        /// <summary>QA: would it pick this part up now (ignoring an assignment), and if not, a hint why.</summary>
        public bool CanPick(int id) { int a = assignedDef; assignedDef = -1; bool ok = Pickable(g.world.instances[id]); assignedDef = a; return ok; }
        public string PickHint(int id) => _unreachable.Contains(id) ? "unreachable" : _deferredUntil.ContainsKey(id) ? "deferred" : g.robotReserved.Contains(id) ? "reserved" : CanPick(id) ? "pickable" : "not pickable";

        bool Pickable(ItemInstance i)
        {
            if (i.state != ItemState.InWorld || _unreachable.Contains(i.id) || g.robotReserved.Contains(i.id)) return false;
            bool tray = g.InTray(i.Position);   // always within reach (TrayGoal), whatever the work zone or assignment
            if (!tray && i.Position.y > ReachHeight) return false;
            if (_deferredUntil.Count > 0 && _deferredUntil.TryGetValue(i.id, out var deferredUntil) && _time < deferredUntil.until) return false;
            if (!tray && zoneCenter.HasValue && Flat(i.Position - zoneCenter.Value).magnitude > zoneRadius) return false;
            var def = g.db.Get(i.defId);
            if (!def.robotCarry || def.IsRobotPart || g.interaction.busy.Contains(i.id) || ItemDatabase.Extents(def)[0] > _maxItem) return false;   // player-only: the largest 20 %
            if (!tray && strategy == "assigned_definition" && assignedDef >= 0 && i.defId != assignedDef) return false;
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
        public bool ReachableDest(string box) => _canStore && g.db.boxes.TryGetValue(box, out var d) && Json.Num(d, "elevation_m") <= ReachHeight && PhysicallyStorable(box);

        readonly Dictionary<string, bool> _physical = new();
        /// <summary>Models with an aim pose (Retrobot): can the claws actually put a part there from the
        /// floor in front of the unit? The point the claws must reach (DepositAim, uncapped) against the
        /// arms' reach at that height, from where the body can stand. A container deeper in a shelf, or
        /// with its rim above the claws, is not storable: the part is staged below it instead (F7). Cached
        /// per destination (the furniture doesn't move). Other robots: the gameplay rule alone.</summary>
        bool PhysicallyStorable(string box)
        {
            if (_model == null || Flies || _canClimb) return true;
            SyncExtend();
            if (_physical.TryGetValue(box, out var v)) return v;
            if (!g.db.boxes.TryGetValue(box, out var d) || !g.units.TryGetValue(Json.Str(d, "unit"), out var unit) || !StagingSpot(box).HasValue) return true;
            var aim = AimPointFor(box, unit, 0, false);
            var reach = _model.AimReachMax(aim.y);
            if (reach == null) return true;   // no aim pose (or not built yet): not cached, asked again
            var front = UnitFront(unit);
            int slot = unit.type == "drawer_cabinet" ? unit.SlotOf(box) : -1;
            float depth = (float)Json.Num(g.db.Unit(Json.Str(d, "unit")), "depth", 0.6);
            // the face the body stands against: a drawer's front, a pallet's own edge, a shelf unit's front
            var face = slot >= 0 ? (Vector3)unit.FrontWorld(slot).GetColumn(3)
                : Json.Str(d, "kind") == "floor_pallet" ? unit.DestPoint(d) + front * (StorageUnit.Inner(d).z * 0.5f)
                : unit.transform.position + front * (depth * 0.5f);
            float need = _radius * 0.85f + 0.012f + Vector3.Dot(Flat(face - aim), front);   // stand clear of the face, reach to the aim
            v = reach.Value >= 0 && reach.Value + 0.02f >= need;
            _physical[box] = v;
            return v;
        }


        readonly Dictionary<string, (float t, bool v)> _blocked = new();
        bool Blocked(string box)
        {
            if (_blocked.TryGetValue(box, out var e) && _time - e.t < 5f) return e.v;
            bool v = g.StraysIn(box).Count > 0;
            _blocked[box] = (_time, v);
            return v;
        }

        static int _stagingCapacity = -1;
        /// <summary>Parts per staging zone (robots.json staging_capacity), read once: Pickable asks for every candidate.</summary>
        public static int StagingCapacity => _stagingCapacity >= 0 ? _stagingCapacity : _stagingCapacity = Json.Int(RootConfig(), "staging_capacity", 14);
        public int StagingCount(string box) => g.world.instances.Count(i => i.state == ItemState.InWorld && i.owner == "stage:" + box);
        Dictionary<string, int> _stagedNow;
        static int _stagedFrame = -1;
        static Dictionary<string, int> _stagedCache;
        /// <summary>Parts per staging zone; counted once per frame for all robots.</summary>
        Dictionary<string, int> StagedCounts()
        {
            if (_stagedFrame == Time.frameCount && _stagedCache != null) return _stagedCache;
            var d = new Dictionary<string, int>();
            foreach (var i in g.world.instances) if (i.state == ItemState.InWorld && i.owner.StartsWith("stage:")) d[i.owner] = d.GetValueOrDefault(i.owner) + 1;
            _stagedFrame = Time.frameCount;
            return _stagedCache = d;
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
            qaTarget = ip;
            QAMoment?.Invoke(this, g.index.QuerySphere(ip, 0.15f).Count > 4 ? "pickpile" : "pick");
            bool claws = Grips;
            if (claws) { _clawsBusy = true; _gripW = PartWidth(itemId); }
            _reachY = Mathf.Clamp(ip.y - transform.position.y, -0.08f, _maxPickH);
            // a part higher than the hanging claws (on a pile's edge, a low shelf): the arms rise to it
            // instead of the robot bending down
            bool raise = !Flies && !_climbing && _reachY > RaiseHeight() && _model.AimReach(_reachY) is float rr && rr >= 0;
            if (raise) yield return StepToGrip(ip, _model.AimReach(_reachY).Value);
            else if (!Flies && !_climbing && _model.GripReach(_reachY) is float grip) yield return StepToGrip(ip, grip);
            float h = Mathf.Clamp01(1 - (ip.y - transform.position.y) / (_height * 3f));
            // reach (grippers opening on the way), then close on the part, then lift: the same total time
            float reachT = _pickTime * (claws ? 0.4f : 0.45f), closeT = claws ? _pickTime * 0.15f : 0, liftT = _pickTime * (claws ? 0.45f : 0.55f);
            for (float t = 0; t < reachT; t += Time.deltaTime)
            {
                FaceTowards(ip, Time.deltaTime * 2);
                float u = t / reachT;
                if (claws) _clawOpen = Mathf.SmoothStep(0, 1, Mathf.Clamp01(u * 1.6f));
                else _extend = ExtendFor(ip.y - transform.position.y) * Mathf.SmoothStep(0, 1, u);
                _reachY = Mathf.Clamp(ip.y - transform.position.y, -0.08f, _maxPickH);   // the feet may settle (stepping off debris)
                if (raise) { _aimPoint = ip; _aimW = Mathf.SmoothStep(0, 1, u); }
                else
                {
                    _bend = Mathf.SmoothStep(0, 0.35f + 0.65f * h, u);
                    _armReach = Mathf.SmoothStep(0, 1, u);
                }
                if (Flies) HoverHold(Time.deltaTime, _altitude);
                yield return null;
            }
            if (raise) _aimW = 1;
            if (claws) _clawOpen = 1;   // the reach ends fully open (the loop's last frame can stop short of u = 1)
            var inst = g.world.instances[itemId];
            bool fromTray = g.InTray(inst.Position);
            if (claws && !fromTray && inst.state == ItemState.InWorld && _model.GripPoint is Vector3 cp && (cp - inst.Position).magnitude > ContactMax)
            {
                // the claws can't get to it (in a pile, past the arms' range): leave it for later, choose another
                LogHands("pick deferred", (cp - inst.Position).magnitude, inst.Position);
                deferred++;
                Defer(itemId);
                g.robotReserved.Remove(itemId);
                targetId = -1;
                float db0 = _bend, dr0 = _armReach, dw0 = _aimW, do0 = _clawOpen;
                for (float t = 0; t < liftT; t += Time.deltaTime)
                {
                    float u = Mathf.SmoothStep(0, 1, t / liftT);
                    _bend = Mathf.Lerp(db0, 0, u); _armReach = dr0 * (1 - u); _aimW = dw0 * (1 - u); _clawOpen = do0 * (1 - u);
                    yield return null;
                }
                _bend = 0; _armReach = 0; _aimW = 0; _clawOpen = 0; _reachY = 0;
                _clawsBusy = false;
                _path = null;
                _step = carried.Count > 0 ? Step.ToDest : Step.Choose;
                _idleT = 0;
                yield break;
            }
            if (claws)
            {
                minOpenAtContact = Mathf.Min(minOpenAtContact, _clawOpen);
                for (float t = 0; t < closeT; t += Time.deltaTime)   // the grippers close on the part where it lies
                {
                    float u = Mathf.SmoothStep(0, 1, t / closeT);
                    _clawClose = u; _clawOpen = 1 - u;
                    yield return null;
                }
                _clawClose = 1; _clawOpen = 0;
                minClosedAtAttach = Mathf.Min(minClosedAtAttach, _clawClose);
            }
            g.robotReserved.Remove(itemId);
            targetId = -1;
            if (inst.state == ItemState.InWorld)
            {
                var from = inst.transform;
                if (Flies) Debug.Log($"[robotflight] {id} picks {itemId} ({inst.owner}) for {g.world.DefOf(itemId).storageBox}");
                // how far the claws are from the part at the grip (QA)
                _gripGlide = 0.35f;
                if (_model.GripPoint is Vector3 gp)
                {
                    float gap = (gp - inst.Position).magnitude;
                    pickCount++;
                    if (fromTray) LogHands("tray pick", gap, inst.Position);   // always allowed: not held to the floor's 5 cm
                    else { maxGripGap = Mathf.Max(maxGripGap, gap); LogHands("pick", gap, inst.Position); }
                    _gripGlide = Mathf.Lerp(0.2f, 0.35f, Mathf.InverseLerp(0.01f, ContactMax, gap));
                }
                // the carried model takes over exactly where the part lies, then glides into the claws
                _gripItem = itemId;
                _gripFromPos = inst.Position;
                _gripFromRot = from.rotation;
                _gripBlend = 0;
                g.index.Remove(itemId);
                g.world.SetState(itemId, ItemState.Carried, Owner);
                g.interaction.SettleNeighbours(from, g.world.DefOf(itemId));
                carried.Add(itemId);
                if (Stows) _clawItem = itemId;   // in the grippers until it is stowed
                ShowCarried(itemId);
                PlaceCarried();
                Sfx.Play("pickup", ip, -4);
                var pdef = g.world.DefOf(itemId);
                GameEvents.Raise(GameEventKind.PartPickedUp, Owner, type, pdef.id, pdef.CategoryName);
            }
            float b0 = _bend;
            float w0 = _aimW, r0 = _armReach, x0 = _extend;
            for (float t = 0; t < liftT; t += Time.deltaTime)
            {
                float u = t / liftT;
                _gripBlend = Mathf.Clamp01(u / _gripGlide);
                _bend = Mathf.Lerp(b0, 0, Mathf.SmoothStep(0, 1, u));
                _armReach = r0 * (1 - u);
                _aimW = w0 * (1 - Mathf.SmoothStep(0, 1, u));
                _extend = x0 * (1 - Mathf.SmoothStep(0, 1, u));
                yield return null;
            }
            _aimW = 0; _extend = 0;
            _gripItem = -1; _gripBlend = 1;
            _bend = 0; _armReach = 0; _reachY = 0;
            if (Stows && _clawItem == itemId) yield return Stow(itemId);
            _clawsBusy = false;
            if (_climbing) yield return ClimbDown();
            _path = null;
            _step = Step.Choose;   // gather more (if allowed), else deliver
            _idleT = 0;
        }

        /// <summary>The last little shuffle before a pick-up (models with a grip pose): forward or back
        /// along its heading until the part lies where the claws come down. Never off walkable floor,
        /// never longer than a moment; the part's reachability is decided before, by the normal rules.</summary>
        IEnumerator StepToGrip(Vector3 ip, float grip, float maxTime = 1.6f, float speed = 0.6f)
        {
            float started = _time;
            _alignResult = "timeout";
            _alignResidual = 0;
            for (float t = 0; t < maxTime; t += Time.deltaTime, maxAlignTime = Mathf.Max(maxAlignTime, _time - started))
            {
                FaceTowards(ip, Time.deltaTime);
                float d = Vector3.Dot(Flat(ip - transform.position), transform.forward) - grip;
                _alignResidual = d;
                // only once it faces the target: a part off to one side can project to the right distance
                bool facing = Vector3.Angle(Flat(transform.forward), Flat(ip - transform.position)) < 10f;
                if (facing && Mathf.Abs(d) < 0.01f) { _alignResult = "aligned"; break; }
                if (!facing) { _speedNow = 0; yield return null; continue; }
                float v = Mathf.Clamp(d * 4f, -_walk * speed, _walk * speed);
                var np = transform.position + transform.forward * (v * Time.deltaTime);
                if (_nav != null && !_nav.Walkable(np) && (_nav.BlockedByItems(np) || !BodyClear(np))) { _alignResult = "blocked"; break; }
                transform.position = np;
                _speedNow = Mathf.Abs(v);
                yield return null;
            }
        }
        string _alignResult = "";
        static readonly Collider[] _overlap = new Collider[8];
        /// <summary>The robot's body (a sphere at mid height, a little inside its nav radius) would not
        /// touch furniture or walls at p: the last step up to a cabinet may go inside the nav grid's
        /// safety margin, never into the furniture itself.</summary>
        bool BodyClear(Vector3 p)
        {
            int n = Physics.OverlapSphereNonAlloc(p + Vector3.up * (_height * 0.5f), _radius * 0.85f, _overlap, 1 << Layers.Env, QueryTriggerInteraction.Ignore);
            for (int i = 0; i < n; i++) if (!_overlap[i].transform.IsChildOf(transform)) return false;
            return true;
        }
        float _alignResidual;

        void LogHands(string what, float gap, Vector3 target)
        {
            float h = target.y - transform.position.y;
            bool inRange = _alignResult != "blocked" && h >= ArmRangeLow && h <= ArmRangeHigh;
            if (!inRange) outOfRange++;
            else if (what == "pick") maxPickGapInRange = Mathf.Max(maxPickGapInRange, gap);
            else if (what == "stage") maxStageGapInRange = Mathf.Max(maxStageGapInRange, gap);
            else if (what.Contains("(drawer")) maxDrawerGapInRange = Mathf.Max(maxDrawerGapInRange, gap);
            var d = transform.InverseTransformDirection((_model.GripPoint ?? target) - target);
            Debug.Log($"[robothands] {id} {what}: claws {gap * 100:0.0} cm from the target (forward {d.z * 100:0.0}, right {d.x * 100:0.0}, up {d.y * 100:0.0} cm; target {(target.y - transform.position.y) * 100:0.0} cm above the feet; {_alignResult}, {_alignResidual * 100:0.0} cm left to step)");
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
            if (!Stows || here.Count == 0) { yield return DepositParts(here, true); yield break; }
            // a stowing robot takes each part out of its hold with the grippers and delivers it on its own
            for (int k = 0; k < here.Count; k++)
            {
                int id = here[k];
                if (!carried.Contains(id)) continue;
                yield return Unstow(id);
                yield return DepositParts(new List<int> { id }, k == here.Count - 1);
                if (carried.Contains(id) && _clawItem == id) yield return Stow(id);   // not taken (full): back into the hold
            }
        }

        /// <summary>Puts `here` (parts for the current destination) away: into the destination, or into its
        /// staging zone when it can't be reached or is blocked. `last`: the final batch of this stop.</summary>
        IEnumerator DepositParts(List<int> here, bool last)
        {
            // stray parts lying in the destination block it: leave the load in its staging zone
            if (!_destReachable || g.StraysIn(_destBox).Count > 0)
            {
                Debug.Log($"[{(Flies ? "robotflight" : "robotplan")}] {id} stages at {_destBox}: reachable {_destReachable} (elevation {Json.Num(g.db.boxes[_destBox], "elevation_m"):0.00} m of {ReachHeight:0.00}, physical {PhysicallyStorable(_destBox)}), strays {g.StraysIn(_destBox).Count}");
                yield return StageBelow(here);
                yield break;
            }
            if (!Flies && _destPoint.y > _maxPickH) yield return ClimbTo(_destPoint);   // climbs up to store it
            // aimed deposit (models with an aim pose): the claws go to the actual drawer or opening
            var aim = DepositAim();
            float? aimReach = !Flies && !_climbing ? _model.AimReach(aim.y - transform.position.y) : null;
            bool aimed = aimReach is float ar && ar >= 0;
            bool claws = aimed && Grips;
            int slot = _destUnit.type == "drawer_cabinet" ? _destUnit.SlotOf(_destBox) : -1;
            qaTarget = aim;
            QAMoment?.Invoke(this, aimed && slot >= 0 ? "drawer" : "store");
            if (claws) _clawsBusy = true;
            if (aimed) yield return StepToGrip(aim, aimReach.Value);
            bool drawer = aimed && slot >= 0;
            if (drawer) _destUnit.OpenDrawer(slot, DrawerContents(_destBox), 1.8f, DrawerSlide);   // it pulls the drawer open
            float high = Mathf.Clamp01((_destPoint.y - transform.position.y) / (_height * 2f));
            for (float t = 0; t < _depositTime * 0.5f; t += Time.deltaTime)
            {
                float u = t / (_depositTime * 0.5f);
                FaceTowards(aimed ? aim : _destPoint, Time.deltaTime * 2);
                if (aimed) { _aimPoint = aim; _aimW = Mathf.SmoothStep(0, 1, u); }
                else { _armLift = Mathf.SmoothStep(0, 0.4f + 0.6f * high, u); _extend = ExtendFor(_destPoint.y - transform.position.y) * Mathf.SmoothStep(0, 1, u); }
                if (Flies) HoverHold(Time.deltaTime, _altitude);
                yield return null;
            }
            if (aimed) { _aimW = 1; if (_model.GripPoint is Vector3 gp) { float gap = (gp - aim).magnitude; maxStoreGap = Mathf.Max(maxStoreGap, gap); storeCount++; string kind = Json.Str(g.db.boxes[_destBox], "kind"); float up = (_destPoint.y - transform.position.y) * 100, ahead = Vector3.Dot(Flat(_destPoint - transform.position), transform.forward) * 100; if (drawer) { drawerStoreCount++; maxDrawerGap = Mathf.Max(maxDrawerGap, gap); } LogHands($"store into {_destBox} ({kind}, dest point {up:0} cm up, {ahead:0} cm ahead)", gap, aim); } }
            if (claws)
            {
                for (float t = 0, ot = _depositTime * 0.12f; t < ot; t += Time.deltaTime)   // let go: the grippers open
                {
                    float u = Mathf.SmoothStep(0, 1, t / ot);
                    _clawClose = 1 - u; _clawOpen = u;
                    yield return null;
                }
                _clawClose = 0; _clawOpen = 1;
                minOpenAtRelease = Mathf.Min(minOpenAtRelease, _clawOpen); releases++;
            }
            foreach (var itemId in here)
            {
                var res = g.storage.Deposit(_destBox, itemId, Owner);
                if (Flies) Debug.Log($"[robotflight] {id} stores {itemId} into {_destBox}: {res}");
                if (res != StorageSystem.Result.Ok) continue;
                HideCarried(itemId);
                carried.Remove(itemId);
                delivered++;
                _tripParts++;
                Sfx.Play("drop_drawer", _destPoint, -5);
                if (here.Count > 1) yield return new WaitForSeconds(0.12f);
            }
            _destUnit.RefreshLabels();
            if (drawer) _destUnit.OpenDrawer(slot, DrawerContents(_destBox), 0.8f, DrawerSlide);   // the part now lies in the tray; it closes after a moment
            float l0 = _armLift, a0 = _aimW, e0 = _extend, retractT = _depositTime * (claws ? 0.38f : 0.5f);
            for (float t = 0; t < retractT; t += Time.deltaTime)
            {
                float u = t / retractT;
                _armLift = Mathf.Lerp(l0, 0, u);
                _aimW = Mathf.Lerp(a0, 0, Mathf.SmoothStep(0, 1, u));
                _extend = Mathf.Lerp(e0, 0, Mathf.SmoothStep(0, 1, u));
                if (claws) _clawOpen = 1 - Mathf.SmoothStep(0, 1, u);
                yield return null;
            }
            _armLift = 0; _aimW = 0; _extend = 0;
            if (claws) { _clawOpen = 0; _clawsBusy = false; }
            if (_climbing) yield return ClimbDown();
            _path = null;
            if (!last) yield break;
            if (carried.Any(c => g.world.DefOf(c).storageBox == _destBox)) DropCarried($"{displayName}: that destination is full");
            _step = carried.Count > 0 ? Step.ToDest : Step.Choose;
            _idleT = carried.Count > 0 ? 0.15f : _decidePause;
            TripCheck();
            MaybeGesture();
        }

        /// <summary>A trip ends when the robot has put its whole load down (stored and/or staged).</summary>
        void TripCheck()
        {
            if (carried.Count > 0 || _tripParts == 0) return;
            GameEvents.Raise(GameEventKind.RobotTripCompleted, Owner, type, count: _tripParts);
            _tripParts = 0;
        }

        /// <summary>Stowing robots: the loader arm flicks the part up overhead and lets go at the top; the part
        /// tumbles over the head in a high arc and lands in its slot in the hold, while the arm comes back down.</summary>
        IEnumerator Stow(int itemId)
        {
            _clawsBusy = true; _clawItem = itemId; _gripW = PartWidth(itemId);
            _clawClose = 1; _clawOpen = 0;
            QAMoment?.Invoke(this, "stow");
            float flickT = _pickTime * 0.3f, flightT = _pickTime * 0.37f, backT = _pickTime * 0.26f;   // about 0.6 s at Caterpillarbot's base pick time
            Sfx.Play("servo", transform.position + Vector3.up * _height, -12);
            for (float t = 0; t < flickT; t += Time.deltaTime) { float u = t / flickT; _toss = u * u * (2 - u); yield return null; }   // fastest at the release
            _toss = 1;
            // let go at the top: the part flies from the gripper into its slot
            if (_carryModels.TryGetValue(itemId, out var go)) { _gripItem = itemId; _gripFromPos = go.transform.position; _gripFromRot = go.transform.rotation; _gripBlend = 0; _gripArc = 0.6f * _height; }
            _clawItem = -1;
            for (float t = 0; t < flightT; t += Time.deltaTime)
            {
                _gripBlend = t / flightT;
                float open = Mathf.Clamp01(t / 0.06f), back = Mathf.SmoothStep(0, 1, t / backT);
                _clawClose = 1 - open; _clawOpen = open * (1 - back); _toss = 1 - back;
                yield return null;
            }
            _gripItem = -1; _gripBlend = 1; _gripArc = 0;
            _toss = 0; _clawClose = 0; _clawOpen = 0;
            Sfx.Play("drop_drawer", transform.position + Vector3.up * _height * 0.5f, -8);   // it lands in the box
            _clawsBusy = false;
            stows++;
        }

        /// <summary>Stowing robots: the grippers reach back into the hold, close on the part and bring it
        /// forward into the carry pose (ready to deliver).</summary>
        IEnumerator Unstow(int itemId)
        {
            _clawsBusy = true; _gripW = PartWidth(itemId);
            float swingT = _depositTime * 0.22f, closeT = _depositTime * 0.078f, backT = _depositTime * 0.2f;   // about 0.6 s at Caterpillarbot's base deposit time
            for (float t = 0; t < swingT; t += Time.deltaTime)
            {
                float u = Mathf.SmoothStep(0, 1, t / swingT);
                _stow = u; _clawOpen = Mathf.SmoothStep(0, 1, Mathf.Clamp01(t / swingT * 1.5f)); _clawClose = 0;
                yield return null;
            }
            _stow = 1; _clawOpen = 1;
            if (_carryModels.TryGetValue(itemId, out var go)) { _gripItem = itemId; _gripFromPos = go.transform.position; _gripFromRot = go.transform.rotation; _gripBlend = 0; }
            _clawItem = itemId;
            for (float t = 0; t < closeT; t += Time.deltaTime)
            {
                float u = Mathf.SmoothStep(0, 1, t / closeT);
                _clawOpen = 1 - u; _clawClose = u; _gripBlend = u;
                yield return null;
            }
            _gripItem = -1; _gripBlend = 1; _clawOpen = 0; _clawClose = 1;
            for (float t = 0; t < backT; t += Time.deltaTime) { _stow = 1 - Mathf.SmoothStep(0, 1, t / backT); yield return null; }
            _stow = 0;
            _clawsBusy = false;
            unstows++;
        }

        /// <summary>Where the claws put a part into its destination: over the open tray of a drawer
        /// (just in front of the drawer face), else just inside the front rim of a bin, box or crate, or
        /// just above a shelf or pallet surface near its front edge. Never above what the robot reaches.</summary>
        Vector3 DepositAim() => AimPointFor(_destBox, _destUnit, transform.position.y, true);

        Vector3 AimPointFor(string box, StorageUnit unit, float feet, bool cap)
        {
            var d = g.db.boxes[box];
            var front = UnitFront(unit);
            int slot = unit.type == "drawer_cabinet" ? unit.SlotOf(box) : -1;
            Vector3 p;
            if (slot >= 0)
                p = (Vector3)unit.FrontWorld(slot).GetColumn(3) + front * (DrawerSlide * 0.6f);
            else
            {
                var inner = StorageUnit.Inner(d);
                string kind = Json.Str(d, "kind");
                bool surface = kind == "shelf_section" || kind == "floor_pallet";
                float rise = surface ? 0.05f : Mathf.Min(inner.y, 0.25f) + 0.03f;
                float inset = kind == "floor_pallet" ? 0.03f : surface ? 0.07f : 0.05f;   // a pallet is loaded from its edge
                p = unit.DestPoint(d) + front * Mathf.Max(0, inner.z * 0.5f - inset) + Vector3.up * rise;
            }
            if (cap) p.y = Mathf.Min(p.y, feet + ReachHeight * 0.95f);
            return p;
        }

        /// <summary>A drawer's current contents for its open tray (as the player sees it).</summary>
        List<(Mesh, Material[], Matrix4x4)> DrawerContents(string box)
        {
            var d = g.db.boxes[box];
            var list = new List<(Mesh, Material[], Matrix4x4)>();
            var ids = g.storage.Stored(box);
            for (int k = 0; k < ids.Count; k++)
            {
                var def = g.world.DefOf(ids[k]);
                var e = MeshLibrary.Get(def.geometryKey, MeshLibrary.LodInspect);
                list.Add((e.mesh, ItemAppearance.SlotMaterials(e, ItemAppearance.For(def)), _destUnit.TrayContentLocal(def, k, d)));
            }
            return list;
        }

        Vector3 _climbFrom;

        /// <summary>Climber: from the floor in front of the furniture it tilts nose-up, climbs
        /// the face to the target's height and steps onto the shelf / bench top beside it.</summary>
        IEnumerator ClimbTo(Vector3 target)
        {
            var dir = Flat(target - transform.position).normalized;
            float topY = Mathf.Max(transform.position.y, target.y - 0.02f);
            // on top it stands where its claws come down on the target (the contact rule holds up there
            // too); measured now, while the model is still level
            float standOff = Grips && _model.GripReach(target.y - topY) is float gr && gr > 0.02f ? gr : _reach * 0.6f;
            // the floor approach stands back by its reach: first walk up to the unit's face, so the
            // climb starts with the feet against it instead of in mid-air in front of it
            var unit = g.UnitAt(target);
            if (unit != null)
            {
                var front = UnitFront(unit);
                float gap = Vector3.Dot(Flat(transform.position - unit.transform.position), front) - unit.size.z * 0.5f - 0.03f;
                var face = transform.position - front * Mathf.Max(0, gap);
                for (float t = 0; t < 3f && Flat(face - transform.position).magnitude > 0.01f; t += Time.deltaTime)
                {
                    float dt = Time.deltaTime;
                    FaceTowards(face - front, dt);
                    transform.position = Vector3.MoveTowards(transform.position, face, dt * _walk * 0.6f);
                    _speedNow = _walk * 0.6f;
                    yield return null;
                }
                _speedNow = 0;
                dir = -front;
                Debug.Log($"[robotclimb] {id} climbs {unit.name} from {(Vector3.Dot(Flat(transform.position - unit.transform.position), front) - unit.size.z * 0.5f) * 100:0.0} cm off its face to {topY:0.00} m");
            }
            qaTarget = target;
            QAMoment?.Invoke(this, "climb");
            _climbing = true;
            _climbFrom = transform.position;
            while (transform.position.y < topY - 0.005f)
            {
                float dt = Time.deltaTime;
                FaceTowards(transform.position + dir, dt);
                _climbPitch = Mathf.MoveTowards(_climbPitch, 75, dt * 240);
                var p = transform.position;
                p.y = Mathf.MoveTowards(p.y, topY, dt * _climbSpeed * (_climbPitch / 75f));
                transform.position = p;
                _speedNow = _climbSpeed;
                yield return null;
            }
            var stand = new Vector3(target.x, topY, target.z) - dir * standOff;
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
            bool claws = Grips;
            if (claws) _clawsBusy = true;
            // where each part will lie (the same pile rules as before), worked out first so that the
            // claws can carry the first one down to its place before letting go
            var places = new List<(Vector3 at, Quaternion rot)>();
            for (int k = 0; k < ids.Count; k++)
            {
                var def = g.world.DefOf(ids[k]);
                var e = ItemDatabase.Extents(def);
                int nearby = g.index.QuerySphere(spot, 0.16f).Count + k;
                // a full zone overflows into a wider loose pile around the same spot
                float spread = 0.1f + 0.012f * Mathf.Max(0, StagingCount(_destBox) - StagingCapacity / 2);
                var jitter = new Vector3(Random.Range(-spread, spread), 0, Random.Range(-spread, spread));
                // a wide pile never spreads under the unit: the spot slides out to open floor
                var at = g.SafeDropPoint(spot + jitter, spot + UnitFront(_destUnit) * 0.4f);
                jitter = new Vector3(at.x, 0, at.z) - spot;
                float y = at.y + e[2] * 0.5f + Mathf.Min(nearby, 5) * e[2] * 0.45f;
                places.Add((spot + jitter + Vector3.up * y, Quaternion.Euler(Random.Range(-12f, 12f), Random.Range(0, 360f), def.restMode == RestMode.Long ? 90 + Random.Range(-10f, 10f) : Random.Range(-12f, 12f))));
            }
            var first = places.Count > 0 ? places[0].at : spot;
            qaTarget = first;
            QAMoment?.Invoke(this, "stage");
            _reachY = Mathf.Clamp(first.y - transform.position.y, -0.08f, _maxPickH);
            bool raise = !Flies && !_climbing && _reachY > RaisePickHeight && _model.AimReach(_reachY) is float sr && sr >= 0;   // onto a tall pile
            if (raise) yield return StepToGrip(first, _model.AimReach(_reachY).Value);
            else if (!Flies && !_climbing && _model.GripReach(_reachY) is float grip) yield return StepToGrip(first, grip);
            for (float t = 0; t < _depositTime * 0.5f; t += Time.deltaTime)
            {
                FaceTowards(first, Time.deltaTime * 2);
                float u = Mathf.SmoothStep(0, 1, t / (_depositTime * 0.5f));
                _reachY = Mathf.Clamp(first.y - transform.position.y, -0.08f, _maxPickH);   // the feet may settle (stepping off debris)
                if (raise) { _aimPoint = first; _aimW = u; }
                else { _bend = u; _armReach = u; }
                yield return null;
            }
            if (raise) _aimW = 1; else { _bend = 1; _armReach = 1; }
            if (claws && places.Count > 0 && _model.GripPoint is Vector3 cgp && (cgp - first).magnitude > 0.015f)
            {
                // the claws came down a little off the planned spot (pile height, settling feet): the part is
                // set down where they are, as long as that is still in the staging zone
                var def0 = g.world.DefOf(ids[0]);
                var e0 = ItemDatabase.Extents(def0);
                var sp = g.SafeDropPoint(new Vector3(cgp.x, 0, cgp.z), spot + UnitFront(_destUnit) * 0.4f);
                if (Flat(sp - spot).magnitude <= 0.35f)
                {
                    int near0 = g.index.QuerySphere(sp, 0.16f).Count;
                    places[0] = (new Vector3(sp.x, sp.y + e0[2] * 0.5f + Mathf.Min(near0, 5) * e0[2] * 0.45f, sp.z), places[0].rot);
                    first = places[0].at;
                }
            }
            if (_model.GripPoint is Vector3 sgp) { float gap = (sgp - first).magnitude; maxStageGap = Mathf.Max(maxStageGap, gap); stageCount++; LogHands("stage", gap, first); }
            if (claws)
            {
                for (float t = 0, ot = _depositTime * 0.12f; t < ot; t += Time.deltaTime)   // let go on the spot: the grippers open
                {
                    float u = Mathf.SmoothStep(0, 1, t / ot);
                    _clawClose = 1 - u; _clawOpen = u;
                    yield return null;
                }
                _clawClose = 0; _clawOpen = 1;
                minOpenAtRelease = Mathf.Min(minOpenAtRelease, _clawOpen); releases++;
            }
            for (int k = 0; k < ids.Count; k++)
            {
                int itemId = ids[k];
                var def = g.world.DefOf(itemId);
                var (at, rot) = places[k];
                HideCarried(itemId);
                carried.Remove(itemId);
                g.world.SetTransform(itemId, Matrix4x4.TRS(at, rot, Vector3.one * def.worldScale));
                g.world.SetState(itemId, ItemState.InWorld, "stage:" + _destBox);   // the destination's staging zone (still UNSORTED)
                g.index.Insert(itemId);
                g.piles.Drop(itemId);   // let go: it settles into the staging pile
                staged++;
                _tripParts++;
                lastStagedId = itemId;
                Sfx.Play("drop_floor", spot, -6);
                GameEvents.Raise(GameEventKind.PartStaged, Owner, type, def.id, def.CategoryName, _destBox);
            }
            float sb0 = _bend, sr0 = _armReach, sw0 = _aimW, recoverT = _depositTime * (claws ? 0.38f : 0.5f);
            for (float t = 0; t < recoverT; t += Time.deltaTime)
            {
                float u = t / recoverT;
                _bend = Mathf.Lerp(sb0, 0, Mathf.SmoothStep(0, 1, u));
                _armReach = sr0 * (1 - u);
                _aimW = sw0 * (1 - Mathf.SmoothStep(0, 1, u));
                if (claws) _clawOpen = 1 - Mathf.SmoothStep(0, 1, u);
                yield return null;
            }
            _bend = 0; _armReach = 0; _aimW = 0; _reachY = 0;
            if (claws) { _clawOpen = 0; _clawsBusy = false; }
            _path = null;
            _step = carried.Count > 0 ? Step.ToDest : Step.Choose;
            _idleT = carried.Count > 0 ? 0.15f : _decidePause;
            TripCheck();
        }

        /// <summary>Puts one carried part down on the open floor in front of the robot (the caller takes it out of carried).</summary>
        void SetDown(int itemId)
        {
            var def = g.world.DefOf(itemId);
            var p = g.SafeDropPoint(transform.position + transform.forward * 0.3f + new Vector3(Random.Range(-0.1f, 0.1f), 0, Random.Range(-0.1f, 0.1f)), transform.position);
            p.y = Mathf.Max(p.y, Ground(p));   // never under a shelf: open floor towards the robot
            var b = MeshLibrary.UnitBounds(def.geometryKey).size * def.worldScale;
            var m = Matrix4x4.TRS(new Vector3(p.x, p.y + Mathf.Min(b.x, b.y, b.z) * 0.5f, p.z), Quaternion.Euler(0, Random.Range(0, 360f), def.restMode == RestMode.Long ? 90 : 0), Vector3.one * def.worldScale);
            HideCarried(itemId);
            g.world.SetTransform(itemId, m);
            g.world.SetState(itemId, ItemState.InWorld, "world");
            g.index.Insert(itemId);
            g.piles.Drop(itemId, transform.forward * 0.05f, 0.04f);
            _unreachable.Add(itemId);
        }

        void DropCarried(string why)
        {
            if (Flies) Debug.Log($"[robotflight] {id} drops {string.Join(",", carried)}: {why}");
            foreach (var itemId in carried.ToList()) SetDown(itemId);
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
            float climb = Climb(p.y, wantY, dt, 1.1f);
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
            p.y = Climb(p.y, ground + alt, dt, 0.8f);
            transform.position = p;
            _altitude = p.y - ground;
        }

        float _vy;
        /// <summary>A flier's height change: the vertical speed eases in and out (limited acceleration,
        /// slowing down on the way in) instead of starting and stopping at full rate.</summary>
        float Climb(float y, float want, float dt, float vmax)
        {
            float desired = Mathf.Clamp((want - y) * 2.2f, -vmax, vmax);
            _vy = Mathf.MoveTowards(_vy, desired, dt * 2.5f);
            float ny = y + _vy * dt;
            if ((want - y) * (want - ny) <= 0) { ny = want; _vy = 0; }   // arrived: no overshoot
            return ny;
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
            SyncExtend();
            _model.transform.localRotation = Quaternion.Euler(-_climbPitch, 0, 0);   // clinging to a vertical face
            float moving = Mathf.Max(_speedNow / _walk, Mathf.Clamp01(Mathf.Abs(_turnRate) / 120f) * 0.6f);
            _gait = Mathf.MoveTowards(_gait, moving > 0.05f ? Mathf.Max(0.45f, moving) : 0, dt * 3f);
            _phase += dt * _model.StepRate(Mathf.Clamp01(moving), _speedNow) * (_gait > 0 ? 1 : 0);
            if (_gait == 0) _phase = Mathf.MoveTowards(_phase, Mathf.Round(_phase / Mathf.PI) * Mathf.PI, dt * 6);   // finish the step: legs come to rest together
            bool inHands = Stows ? _clawItem >= 0 : carried.Count > 0;   // a stowing robot's load rides in its hold
            _armHold = Mathf.MoveTowards(_armHold, inHands ? 1 : 0, dt * 3f);
            if (!_clawsBusy)
            {
                // holding a load: grippers closed on it; otherwise they ease back to their rest
                _clawClose = Mathf.MoveTowards(_clawClose, inHands ? 1 : 0, dt * 4f);
                _clawOpen = Mathf.MoveTowards(_clawOpen, 0, dt * 4f);
                _gripW = inHands ? PartWidth(Stows ? _clawItem : carried[0]) : 0;
            }
            Sounds(moving);
            bool idle = _gait < 0.05f && _armReach == 0 && _armLift == 0 && _aimW == 0;
            _model.Animate(new RobotAnim
            {
                dt = dt, speed01 = _speedNow / _walk, speed = _speedNow, gait = _gait, phase = _phase, turnRate = _turnRate, bend = _bend,
                armReach = _armReach, armHold = _armHold, armLift = _armLift, altitude = _altitude, active = true, time = _time, reachHeight = _reachY,
                aim = _aimPoint, aimWeight = _aimW, clawOpen = _clawOpen, clawClose = _clawClose, gripWidth = _gripW,
                armExtend = _extend, stow = _stow, toss = _toss, focus = qaTarget, focusWeight = _clawsBusy ? 1 : 0, lookAhead = PathAhead(0.6f) ?? Vector3.zero, lookAheadWeight = _path != null && _pathIdx < _path.Count && !_clawsBusy ? 1 : 0,
                lookAt = g.player.transform.position, lookWeight = idle ? 1 : 0,
                gesture = _step == Step.Gesture ? Mathf.Clamp01(_gestureT / GestureTime) : 0,
            });
            PlaceCarried();
            int held = Stows ? _clawItem : carried.Count > 0 ? carried[0] : -1;
            if (held >= 0 && _armHold > 0.99f && _gripBlend >= 1 && _model.GripPoint is Vector3 gp && _carryModels.TryGetValue(held, out var cm))
                carrySyncMax = Mathf.Max(carrySyncMax, (cm.transform.position - gp).magnitude);   // QA: the load stays in the grippers
        }

        /// <summary>The point `dist` metres further along the current path (no allocations), or null.</summary>
        Vector3? PathAhead(float dist)
        {
            if (_path == null || _pathIdx >= _path.Count) return null;
            var p = transform.position;
            for (int i = _pathIdx; i < _path.Count; i++)
            {
                var q = _path[i];
                float d = Flat(q - p).magnitude;
                if (d >= dist) return p + Flat(q - p) * (dist / Mathf.Max(d, 1e-4f));
                dist -= d;
                p = q;
            }
            return _path[_path.Count - 1];
        }

        /// <summary>Width of a part across the grippers (world m): its middle extent, within the grippers' span.</summary>
        float PartWidth(int itemId) => Mathf.Clamp(ItemDatabase.Extents(g.world.DefOf(itemId))[1], 0.015f, 0.14f);

        /// <summary>Puts the carried models in the carry slots. While the claws reach down (the moment of
        /// a pick-up, setting a load down on the floor) the parts ride with the claws (the model's grip
        /// point) instead, so a part never jumps between the floor and the chest.</summary>
        void PlaceCarried()
        {
            int k = 0;
            var grip = _model.GripPoint;
            foreach (var itemId in carried)
                if (_carryModels.TryGetValue(itemId, out var go)) PlaceCarriedModel(go, Stows ? SlotFor(itemId) : k++, Stows ? _capacity : carried.Count, grip, itemId);
        }

        /// <summary>One carried model: in its carry slot; with the claws while they reach down or are
        /// raised to a destination (the grip point); and, just after a grip, gliding from where the part
        /// lay into the claws (so it never jumps).</summary>
        void PlaceCarriedModel(GameObject go, int k, int n, Vector3? grip, int itemId = -1)
        {
            var (parent, local) = _model.CarrySlot(k, n);
            if (go.transform.parent != parent) go.transform.SetParent(parent, false);
            go.transform.localPosition = local + new Vector3(0, 0.05f * _armLift, 0.06f * _armLift);
            go.transform.localRotation = itemId >= 0 && g.world.DefOf(itemId).restMode == RestMode.Long ? Quaternion.Euler(0, 0, 90) : Quaternion.identity;
            // models with grippers hold the load between them all the time (it moves with the hands); a
            // stowing model only the part in its grippers (the rest ride in the hold)
            float w = Mathf.Max(Mathf.SmoothStep(0, 1, _armReach), Mathf.Max(Mathf.SmoothStep(0, 1, _aimW), _armHold));
            if (Stows)
            {
                bool inClaws = itemId >= 0 ? itemId == _clawItem : _qaInClaws;
                w = inClaws ? 1 : 0;
                // during the swing over the hold only the loader gripper holds it (the other lets go)
                if (inClaws && grip.HasValue && _model.StowGripPoint is Vector3 sg) grip = Vector3.Lerp(grip.Value, sg, Mathf.SmoothStep(0, 1, Mathf.Clamp01(Mathf.Max(_stowQA ?? _stow, _tossQA ?? _toss)) * 4f));
            }
            if (grip.HasValue && w > 0)
            {
                var (_, l0) = _model.CarrySlot(0, n);
                go.transform.position = Vector3.Lerp(go.transform.position, grip.Value + (Stows ? Vector3.zero : parent.TransformVector(local - l0)), w);
            }
            if (itemId == _gripItem && _gripBlend < 1 && _gripArc > 0)
            {
                // thrown: a ballistic arc (even across, parabolic up), tumbling end over end twice
                float u = _gripBlend;
                go.transform.SetPositionAndRotation(Vector3.Lerp(_gripFromPos, go.transform.position, u) + Vector3.up * (_gripArc * 4 * u * (1 - u)),
                    Quaternion.AngleAxis(-720 * u, transform.right) * Quaternion.Slerp(_gripFromRot, go.transform.rotation, u));
            }
            else if (itemId >= 0 && itemId == _gripItem && _gripBlend < 1)
            {
                float b = Mathf.SmoothStep(0, 1, _gripBlend);
                go.transform.SetPositionAndRotation(Vector3.Lerp(_gripFromPos, go.transform.position, b), Quaternion.Slerp(_gripFromRot, go.transform.rotation, b));
            }
        }

        GameObject _qaPart;
        bool _qaInClaws = true;
        float? _stowQA, _tossQA;
        Quaternion? _qaYaw0;
        /// <summary>QA (--robotpose / --robotseq, studio renders): poses the real model through its own
        /// Animate, with a part in its claws where the pose carries one. name: idle, walk (t = stride
        /// cycle), turn, bend, carry, carrywalk, pickup and deposit (t = 0..1 through the sequence).</summary>
        public void PoseQA(string name, float t = 0)
        {
            var a = new RobotAnim { dt = 1 };
            bool part = false, onFloor = false, simulated = false;
            _qaInClaws = true; _stowQA = null; _tossQA = null; _gripArc = 0; _gripBlend = 1;
            float S(float x) => Mathf.SmoothStep(0, 1, Mathf.Clamp01(x));
            switch (name)
            {
                case "walk": a.gait = 1; a.speed = _walk; a.speed01 = 1; a.phase = t * Mathf.PI * 2; break;
                case "turn":
                {
                    // simulated over time: the robot turns on the spot at its own turn rate while the
                    // feet step (frame-by-frame, so the planted boot can be seen holding its heading)
                    _qaYaw0 ??= transform.rotation;
                    _model.ResetPose();
                    float rate = 150f, cadence = _model.StepRate(0, 0), dt = 1 / 60f, ph = 0;
                    int frames = Mathf.RoundToInt(t * 2 * Mathf.PI / cadence * 2 / dt);   // two stride cycles over t = 0..1
                    var b = new RobotAnim { dt = dt, gait = 0.6f, turnRate = rate };
                    for (int f = 0; f <= frames; f++)
                    {
                        transform.rotation = _qaYaw0.Value * Quaternion.Euler(0, rate * dt * f, 0);
                        b.phase = ph;
                        _model.Animate(b);
                        ph += cadence * dt;
                    }
                    return;
                }
                case "bend": a.bend = 1; a.armReach = 1; a.reachHeight = 0.015f; break;
                case "carry": a.armHold = 1; a.clawClose = 1; part = true; break;
                case "carrywalk": a.armHold = 1; a.clawClose = 1; a.gait = 1; a.speed = _walk; a.speed01 = 1; a.phase = Mathf.PI / 2 + t * Mathf.PI * 2; part = true; break;
                case "roll":
                case "bank":
                case "carryroll":
                case "carrybank":
                {
                    // simulated over time (balance and wheel are dynamic): accelerate, cruise, brake to a stop
                    // ("roll"), or roll round a curve ("bank"); with a load for the carry variants. The robot is
                    // put back where it stood, the pose stays.
                    _model.ResetPose();
                    var p0 = transform.position; var r0 = transform.rotation;
                    bool load = name.StartsWith("carry"), curve = name.EndsWith("bank");
                    int frames = Mathf.RoundToInt(t * 150);
                    float sdt = 1 / 60f, ph = 0;
                    for (int f = 0; f <= frames; f++)
                    {
                        float tt = f / 150f;
                        float v = curve ? _walk * Mathf.SmoothStep(0, 1, tt / 0.15f)
                            : tt < 0.3f ? _walk * Mathf.SmoothStep(0, 1, tt / 0.3f) : tt < 0.6f ? _walk : tt < 0.8f ? _walk * (1 - Mathf.SmoothStep(0, 1, (tt - 0.6f) / 0.2f)) : 0;
                        float turn = curve ? 150f * Mathf.SmoothStep(0, 1, (tt - 0.1f) / 0.2f) : 0;
                        transform.position += transform.forward * (v * sdt);
                        if (curve) transform.rotation *= Quaternion.Euler(0, turn * sdt, 0);
                        ph += _model.StepRate(v / _walk, v) * sdt;
                        _model.Animate(new RobotAnim { dt = sdt, speed = v, speed01 = v / _walk, gait = v > 0.02f ? 1 : 0, turnRate = turn, phase = ph, time = f * sdt,
                            armHold = load ? 1 : 0, clawClose = load ? 1 : 0, gripWidth = load ? 0.05f : 0 });
                    }
                    transform.SetPositionAndRotation(p0, curve ? r0 * Quaternion.Euler(0, -25f, 0) : r0);   // a curve is shown from where it is most visible
                    _armHold = load ? 1 : 0; _armReach = 0; _aimW = 0;
                    simulated = true; part = load;
                    break;
                }
                case "telescope":
                    // the reach upgrade at full rank: raising a part to a drawer t of the way from 0.30 to 0.58 m up
                    _model.armExtendMax = (0.62f - BaseReach) * 1.35f;
                    a.armHold = 1; a.clawClose = 1; part = true; a.aimWeight = 1;
                    a.aim = transform.position + Flat(transform.forward).normalized * 0.12f + Vector3.up * Mathf.Lerp(0.3f, 0.58f, t);
                    break;
                case "telescopelift":
                    // models without an aim solve: arms lifted, sleeves out t of the way
                    a.armHold = 1; a.armLift = 1; a.armExtend = (0.62f - BaseReach) * 1.35f * t; part = true;
                    break;
                case "stow":
                    // stowing models: the part comes up in the grippers, swings back over the hold and drops in
                    part = true; a.gripWidth = 0.05f;
                    if (t < 0.6f) { a.armHold = 1; a.clawClose = 1; a.stow = S(t / 0.6f); }
                    else if (t < 0.72f) { float u = S((t - 0.6f) / 0.12f); a.stow = 1; a.clawClose = 1 - u; a.clawOpen = u; _qaInClaws = u < 0.5f; }
                    else { float u = S((t - 0.72f) / 0.28f); a.stow = 1 - u; a.clawOpen = 1 - u; _qaInClaws = false; }
                    break;
                case "toss":
                    // stowing models: the flick overhead, the release and the part's flight into the hold
                    part = true; a.gripWidth = 0.05f;
                    if (t < 0.45f) { float u = t / 0.45f; a.armHold = 1; a.clawClose = 1; a.toss = u * u * (2 - u); }
                    else
                    {
                        float f = (t - 0.45f) / 0.55f, back = S(Mathf.Clamp01(f / 0.7f));
                        a.toss = 1 - back; a.clawOpen = 1 - back; _qaInClaws = false;
                        if (_model.TossReleasePoint() is Vector3 rp) { _gripItem = -1; _gripFromPos = rp; _gripFromRot = transform.rotation; _gripBlend = Mathf.Min(f, 0.999f); _gripArc = 0.6f * _height; }
                    }
                    break;
                case "hold":
                    // stowing models: travelling with a load in the hold, arms at rest
                    part = true; _qaInClaws = false; a.gait = 1; a.speed = _walk; a.speed01 = 1; a.phase = t * Mathf.PI * 2;
                    break;
                case "clawsopen": a.clawOpen = 1; break;
                case "clawsclosed": a.clawClose = 1; break;
                case "pickup":
                    // approach (a last short step), crouch and reach with the grippers opening, close on the
                    // part where it lies, lift into the carry pose
                    part = true; onFloor = t < 0.62f;
                    if (t < 0.2f) { a.gait = 0.5f; a.speed = 0.12f; a.speed01 = 0.2f; a.phase = t * 24; }
                    else if (t < 0.5f) { float v = S((t - 0.2f) / 0.3f); a.bend = v; a.armReach = v; a.reachHeight = 0.015f; a.clawOpen = S((t - 0.2f) / 0.16f); }
                    else if (t < 0.62f) { float u = S((t - 0.5f) / 0.12f); a.bend = 1; a.armReach = 1; a.reachHeight = 0.015f; a.clawOpen = 1 - u; a.clawClose = u; }
                    else { float w = (t - 0.62f) / 0.38f; a.bend = 1 - S(w); a.armReach = 1 - w; a.armHold = Mathf.Clamp01(w * 2.5f); a.reachHeight = 0.015f; a.clawClose = 1; }
                    break;
                case "deposit":
                    // a low drawer in front (0.18 m up): raise the part to it, open the grippers, arms back down
                    a.armHold = 1; a.aim = transform.position + transform.forward * 0.2f + Vector3.up * 0.18f;
                    a.aimWeight = t < 0.5f ? S(t / 0.4f) : 1 - S((t - 0.5f) / 0.5f); part = t < 0.5f;
                    a.clawClose = t < 0.4f ? 1 : 1 - S((t - 0.4f) / 0.1f); a.clawOpen = t < 0.4f ? 0 : t < 0.5f ? S((t - 0.4f) / 0.1f) : 1 - S((t - 0.5f) / 0.5f);
                    break;
                case "stage":
                    // setting a part down on the floor in front, opening the grippers, standing up
                    a.armHold = 1; a.reachHeight = 0.015f;
                    if (t < 0.45f) { a.bend = S(t / 0.45f); a.armReach = a.bend; a.clawClose = 1; part = true; }
                    else if (t < 0.55f) { float u = S((t - 0.45f) / 0.1f); a.bend = 1; a.armReach = 1; a.clawClose = 1 - u; a.clawOpen = u; part = true; onFloor = true; }
                    else { a.bend = 1 - S((t - 0.55f) / 0.45f); a.armReach = a.bend; a.armHold = 0; a.clawOpen = 1 - S((t - 0.55f) / 0.45f); part = true; onFloor = true; }
                    break;
            }
            if (part || a.armHold > 0) a.gripWidth = 0.05f;
            if (a.stow > 0) _stowQA = a.stow;
            if (a.toss > 0) _tossQA = a.toss;
            if (!simulated)
            {
                _armReach = a.armReach; _armLift = a.armLift; _armHold = a.armHold; _aimW = a.aimWeight; _aimPoint = a.aim; _extend = a.armExtend;
                _model.Animate(a);
            }
            if (part && _qaPart == null)
            {
                var def = g.db.order.Select(d => g.db.Get(d)).First(d => !d.IsRobotPart && ItemDatabase.Extents(d)[0] < 0.07f && ItemDatabase.Extents(d)[0] > 0.035f);
                _qaPart = HeldView.MakeModel(def);
                PlaceCarriedModel(_qaPart, 0, 1, null);
                _qaPart.transform.localScale = Vector3.one * def.worldScale / (_height / _model.ModelHeight);
                foreach (var tr in _qaPart.GetComponentsInChildren<Transform>(true)) tr.gameObject.layer = gameObject.layer;
            }
            if (_qaPart != null)
            {
                _qaPart.SetActive(part);
                if (part) PlaceCarriedModel(_qaPart, 0, 1, _model.GripPoint);
                if (part && onFloor)
                {
                    // the part lies on the floor where the claws come down (it is only lifted once gripped)
                    var fwd = Flat(transform.forward).normalized;
                    _qaPart.transform.SetPositionAndRotation(transform.position + fwd * (_model.GripReach(0.015f) ?? 0.1f) + Vector3.up * 0.015f, Quaternion.identity);
                }
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
            _slotOf.Remove(itemId);
            if (_clawItem == itemId) _clawItem = -1;
            if (_carryModels.TryGetValue(itemId, out var go)) { Destroy(go); _carryModels.Remove(itemId); }
        }

        public string Describe()
        {
            if (arm != null && Operational) return arm.Describe();
            switch (mode)
            {
                case Mode.Assembling: return $"{displayName}: {status}";
                case Mode.Activating: return $"{displayName}: starting up…";
                case Mode.Following: return carried.Count > 0 ? $"{displayName}: delivering, then following" : $"{displayName}: following you";
                case Mode.Summoned: return $"{displayName}: {(summonStatus != "" ? summonStatus : "coming to the workbench")}";
                case Mode.Docked: return g.modules != null && !g.modules.Complete(id)
                    ? $"{displayName}: incomplete — parked at the workbench (missing {string.Join(", ", g.modules.Missing(id).Select(RobotModules.SlotName))})"
                    : $"{displayName}: docked at the workbench";
            }
            if (_summonPending) return $"{displayName}: finishing up, then coming to the workbench";
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
