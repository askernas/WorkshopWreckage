using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;
using UnityEngine.InputSystem;

namespace WorkshopWreckage
{
    /// <summary>Entry point and game controller (port of Godot main.gd). Created
    /// automatically after the first scene loads, so the scene needs no setup.
    ///
    /// Command line: --preset=standard|large|massive  --seed=N  --shot=a.png;b.png
    /// --cam=x,y,z,yaw,pitch;...  --items=N (benchmark subset)  --presort=0.35  --play  --hud
    /// --bench=out.json  --autoplay=out_dir</summary>
    public class Game : MonoBehaviour
    {
        public ItemDatabase db;
        public WorkshopLayout layout;
        public ItemWorld world;
        public WorkshopBuilder builder;
        public ItemRenderer renderer;
        public ItemSpatialIndex index;
        public StorageSystem storage;
        public Inventory inventory;
        public ProgressTracker progress;
        public PlayerController player;
        public HeldView held;
        public Interaction interaction;
        public Hud hud;
        public RobotWorkbench workbench;
        public readonly List<Robot> robots = new();
        public Robot retrobot => robots.Find(r => r.id == "retrobot");
        public readonly HashSet<int> robotReserved = new();   // parts a robot is on its way to: no one else takes them
        public DebrisField debris;
        public PlayerTools tools;
        /// <summary>Per-preset multipliers (world_presets.json "scaling"): bigger worlds get stronger robots and tools.</summary>
        public Dictionary<string, object> presetScaling = new();
        public float Scale(string key) => (float)Json.Num(presetScaling, key, 1);
        public Objectives objectives;
        public readonly Dictionary<string, StorageUnit> units = new();
        public int itemCount = -1;   // -1 = the preset's full population
        public string presetId = "standard";
        int _layoutSeed = int.MinValue;
        public string state = "menu";
        public readonly Dictionary<string, string> args = new();
        public double startupMs;
        Dictionary<string, List<int>> _assignments;
        readonly HashSet<string> _labelsDirty = new();
        public static Game Instance;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Boot()
        {
            if (Instance != null) return;
            var go = new GameObject("WorkshopWreckage");
            Instance = go.AddComponent<Game>();
        }

        void Start()
        {
            var sw = System.Diagnostics.Stopwatch.StartNew();
            foreach (var a in Environment.GetCommandLineArgs())
            {
                if (!a.StartsWith("--")) continue;
                var kv = a.Substring(2).Split(new[] { '=' }, 2);
                args[kv[0]] = kv.Length > 1 ? kv[1] : "1";
            }
            // remove template scene content (camera, light, volume)
            foreach (var cam in FindObjectsByType<Camera>()) Destroy(cam.gameObject);
            foreach (var l in FindObjectsByType<Light>()) Destroy(l.gameObject);
            Physics.IgnoreLayerCollision(Layers.Player, Layers.Target);
            Sfx.Init(transform);
            // tests, screenshots and benchmarks run silently (--mute does it for normal runs too)
            if (args.ContainsKey("mute") || Automated) { AudioListener.volume = 0; Sfx.masterVolume = 0; }

            if (args.TryGetValue("preset", out var pr)) presetId = pr;
            bool valid = LoadPreset(presetId);
            Debug.Log($"[boot] preset {presetId} loaded, valid {valid} ({sw.ElapsedMilliseconds} ms)");
            hud = new GameObject("UI").AddComponent<Hud>();
            hud.transform.SetParent(transform, false);
            hud.Init(this);
            if (!valid) { hud.ShowFatal("The item data files are invalid:\n\n" + string.Join("\n", db.errors.Take(25))); return; }
            workbench = new GameObject("RobotWorkbench").AddComponent<RobotWorkbench>();
            workbench.transform.SetParent(transform, false);

            player = new GameObject("Player").AddComponent<PlayerController>();
            player.transform.SetParent(transform, false);
            player.Init();
            player.stepped = () => Sfx.Play("step", null, -16, 0.15f);
            held = new GameObject("HeldView").AddComponent<HeldView>();
            held.Init(player.cam);
            interaction = new GameObject("Interaction").AddComponent<Interaction>();
            interaction.transform.SetParent(transform, false);
            interaction.Init(this);
            tools = new GameObject("PlayerTools").AddComponent<PlayerTools>();
            tools.transform.SetParent(transform, false);
            tools.Init(this);
            interaction.TargetChanged += hud.OnTarget;
            interaction.Feedback += hud.OnFeedback;
            interaction.InspectChanged += hud.ShowInspect;
            interaction.RobotInspectChanged += hud.ShowRobotInspect;
            workbench.Init(this);

            if (args.TryGetValue("items", out var n)) itemCount = int.Parse(n);
            int seed = args.TryGetValue("seed", out var sd) ? int.Parse(sd) : Json.Int(db.config, "default_seed", 1912);
            if (!StartWorld(seed, null)) return;
            startupMs = Time.realtimeSinceStartupAsDouble * 1000;   // process start to interactive
            StartCoroutine(ProbeLater());
            if (args.TryGetValue("robotview", out var rv)) RobotView(rv);
            if (args.TryGetValue("gallery", out var gal)) BuildGallery(gal, args.TryGetValue("lod", out var lodArg) ? int.Parse(lodArg) : MeshLibrary.LodWorld);
            if (args.TryGetValue("presort", out var ps)) Presort(float.Parse(ps, System.Globalization.CultureInfo.InvariantCulture));
            if (args.ContainsKey("shot")) { SetState("play"); StartCoroutine(TakeShots()); }
            else if (args.TryGetValue("bench", out var bp)) { SetState("play"); StartCoroutine(new Benchmark().Run(this, bp)); }
            else if (args.TryGetValue("autoplay", out var ap)) { SetState("play"); StartCoroutine(new Autoplay().Run(this, ap)); }
            else if (args.ContainsKey("play")) SetState("play");
            else SetState("menu");
        }

        IEnumerator ProbeLater()
        {
            yield return null;
            yield return null;
            builder.RefreshProbe();
        }

        public bool Automated => args.ContainsKey("shot") || args.ContainsKey("bench") || args.ContainsKey("autoplay");

        // ---------------------------------------------------------------- world lifecycle

        /// <summary>Loads and validates a world-size preset's data; the workshop
        /// itself is built per seed by BuildWorkshop.</summary>
        public bool LoadPreset(string preset)
        {
            db = ItemDatabase.LoadPreset(preset);
            if (db.IsValid) db.Validate();
            if (!db.IsValid) return false;
            presetId = preset;
            presetScaling = Json.Obj(ItemDatabase.Presets().Find(p => p.id == preset).cfg?.GetValueOrDefault("scaling")) ?? new Dictionary<string, object>();
            _assignments = db.Assignments();
            _layoutSeed = int.MinValue;
            return true;
        }

        /// <summary>Generates the procedural workshop for the current preset and
        /// seed and (re)builds room, furniture and storage units.</summary>
        bool BuildWorkshop(int seed)
        {
            _lampBase.Clear();
            _machineRunning = false; _machineSpin = 0;
            var l = WorkshopLayout.Generate(db, seed);
            if (l.placementErrors.Count > 0) { hud.ShowFatal("The workshop layout is invalid:\n\n" + string.Join("\n", l.placementErrors.Take(20))); return false; }
            layout = l;
            _layoutSeed = seed;
            if (builder != null) Destroy(builder.gameObject);
            foreach (var u in units.Values) Destroy(u.gameObject);
            units.Clear();
            builder = new GameObject("Workshop").AddComponent<WorkshopBuilder>();
            builder.transform.SetParent(transform, false);
            Debug.Log("[boot] layout generated");
            builder.Build(layout);
            var trayTarget = new GameObject("Target_Tray") { layer = Layers.Target };
            trayTarget.transform.SetParent(builder.transform, false);
            trayTarget.transform.position = Shared.Pos(layout.trayRect.center.x, 0.15f, layout.trayRect.center.y);
            trayTarget.AddComponent<BoxCollider>().size = new Vector3(layout.trayRect.width, 0.3f, layout.trayRect.height);
            trayTarget.AddComponent<StorageTarget>().destId = "TRAY";
            Debug.Log("[boot] workshop built");
            BuildStorage();
            Debug.Log("[boot] storage built");
            workbench.transform.position = Shared.Pos(layout.repairBayPos.x, layout.repairBayPos.y, layout.repairBayPos.z);
            workbench.transform.rotation = Shared.YRot(layout.repairBayRot);
            if (startupMs > 0) StartCoroutine(ProbeLater());
            return true;
        }

        public bool StartWorld(int seed, Dictionary<string, object> save)
        {
            if (_layoutSeed != seed && !BuildWorkshop(seed)) return false;
            var gen = new WorldGenerator();
            int total = Json.Int(db.config, "total_items", 4096);
            var w = gen.Generate(db, layout, seed, itemCount > 0 && itemCount < total ? itemCount : -1);
            if (w == null) { hud.ShowFatal("World could not be generated:\n\n" + string.Join("\n", gen.errors.Take(20))); return false; }
            Debug.Log($"[world] preset {presetId}, seed {seed}: {w.Count} items in {gen.lastGenerationMs:0} ms ({layout.piles.Count} pile sites, {layout.surfaces.Count} surfaces, {units.Count} storage units)");
            world = w;
            if (renderer != null) Destroy(renderer.gameObject);
            renderer = new GameObject("Items").AddComponent<ItemRenderer>();
            renderer.transform.SetParent(transform, false);
            storage = new StorageSystem(db, world) { placement = StoragePlacement };
            inventory = new Inventory(world);
            if (save != null) SaveSystem.ApplyItems(save, world);
            progress = new ProgressTracker(world, layout.zones.Select(z => z.name).ToList());
            if (save != null)
            {
                foreach (var kv in Json.Obj(save["storage"]))
                {
                    var ids = Json.Arr(kv.Value).Select(x => (int)(double)x).ToList();
                    storage.contents[kv.Key] = ids;
                    for (int k = 0; k < ids.Count; k++) world.instances[ids[k]].transform = StoragePlacement(kv.Key, ids[k], k);
                }
                var inv = Json.Obj(save["inventory"]);
                foreach (var x in Json.Arr(inv["items"])) inventory.items.Add((int)(double)x);
                inventory.heldDef = Json.Int(inv, "held_def", -1);
                progress.Load(Json.Obj(save["progress"]));
            }
            renderer.Build(world);
            index = new ItemSpatialIndex(world);
            debris = new DebrisField(world, index, Shared.Pos(layout.roomMin.x, 0, layout.roomMax.z), Shared.Pos(layout.roomMax.x, 0, layout.roomMin.z), FurnitureFootprints());
            player.debris = debris;
            interaction.ResetState();
            tools.ResetState();
            StartProgression(seed, save);
            workbench.PrepareOrders();
            workbench.ResetState();
            SpawnRobots();
            workbench.Load(save);
            foreach (var u in units.Values) { u.labelProvider = LabelFor; u.RefreshLabels(); }
            objectives = new Objectives(this);
            if (save != null) objectives.Load(save.GetValueOrDefault("objectives"));
            else objectives.Tick(1);   // nothing is done yet; evaluates silently
            objectives.Completed += o => { hud.Toast("Objective complete: " + o.text, true); Sfx.Play("milestone", null, -4); };
            if (save != null && save.TryGetValue("robots", out var ro))
                foreach (var kv in Json.Obj(ro)) robots.Find(r => r.id == kv.Key)?.LoadState(Json.Obj(kv.Value));
            foreach (var r in robots) r.RefreshAssembly();   // which crafted Hoverbot is on the bench depends on the others
            workbench.Refresh();
            progress.Milestone += (key, text) => { hud.Toast(text, key.StartsWith("zone") || key == "first_pickup"); Sfx.Play("milestone", null, -6); };
            progress.DefinitionCompleted += id => { hud.Toast($"Set complete: {db.Get(id).displayName}  (all {world.TotalOf(id)})", true); Sfx.Play("complete", null, -4); };
            progress.Discovered += id => { _labelsDirty.Add(db.Get(id).storageBox); Sfx.Play("discover", null, -12); };
            int lastCount = inventory.items.Count;
            inventory.Changed += () =>
            {
                hud.MarkHotbarDirty();
                var hid = inventory.HeldId();
                held.ShowHeld(hid >= 0 ? world.DefOf(hid) : null);
                if (inventory.items.Count > lastCount) { _activity = "collecting"; telemetry.Pickup(); }
                else if (inventory.items.Count < lastCount) _activity = "depositing";
                lastCount = inventory.items.Count;
            };
            held.ShowHeld(inventory.HeldId() >= 0 ? world.DefOf(inventory.HeldId()) : null);
            hud.MarkHotbarDirty();
            if (save != null && save.TryGetValue("player", out var po))
            {
                var pl = Json.Obj(po);
                var pa = Json.Arr(pl["pos"]);
                player.Teleport(new Vector3(Json.F(pa[0]), Json.F(pa[1]), Json.F(pa[2])));
                player.SetView((float)Json.Num(pl, "yaw"), (float)Json.Num(pl, "pitch"));
            }
            else
            {
                player.Teleport(Shared.Pos(layout.spawn.x, layout.spawn.y, layout.spawn.z));
                player.SetView(layout.spawnYaw, -12 * Mathf.Deg2Rad);
            }
            return true;
        }

        float _lightT;

        /// <summary>Workshop restoration made visible: each zone's lamps glow brighter as
        /// the zone is restored (up to +45% at 100%), easing towards the new level.</summary>
        void RestorationLighting()
        {
            _lightT -= Time.deltaTime;
            bool step = _lightT <= 0;
            if (step) _lightT = 0.25f;
            var rcfg = Json.Obj(_progressionCfg?.GetValueOrDefault("restoration")) ?? new Dictionary<string, object>();
            float boost = (float)Json.Num(rcfg, "lamp_boost_at_100", 0.45);
            foreach (var l in builder.lamps)
            {
                if (l == null) continue;
                if (!_lampBase.TryGetValue(l, out var b)) _lampBase[l] = b = (l.intensity, layout.ZoneAt(l.transform.position));
                if (!step) continue;
                float want = b.intensity * (1f + boost * progress.ZonePercent(b.zone) / 100f);
                l.intensity = Mathf.MoveTowards(l.intensity, want, 0.25f * 0.4f);
            }
            // the lathe runs again once its zone is restored enough
            if (builder.machineSpindle == null) return;
            int mz = layout.ZoneAt(builder.machineSpindle.position);
            bool running = progress.ZonePercent(mz) >= (float)Json.Num(rcfg, "machine_zone_percent", 60);
            if (running && !_machineRunning) { hud.Toast("The lathe runs again!", true); Sfx.Play("complete", builder.machineSpindle.position, -4); if (builder.machineHum.clip == null) { builder.machineHum.clip = Sfx.Clip("loop_machine"); builder.machineHum.Play(); } }
            _machineRunning = running;
            _machineSpin = Mathf.MoveTowards(_machineSpin, running ? 1 : 0, Time.deltaTime * 0.5f);
            builder.machineSpindle.localRotation *= Quaternion.Euler(_machineSpin * 900f * Time.deltaTime, 0, 0);
            builder.machineHandwheel.localRotation *= Quaternion.Euler(_machineSpin * 40f * Time.deltaTime, 0, 0);
            builder.machineLamp.intensity = 1.6f * _machineSpin;
            builder.machineHum.volume = 0.35f * _machineSpin * Sfx.masterVolume;
        }

        bool _machineRunning;
        float _machineSpin;
        public bool MachineRunning => _machineRunning;

        readonly Dictionary<Light, (float intensity, int zone)> _lampBase = new();

        /// <summary>Model QA (--robotview=retrobot): the robot fully assembled, frozen in its
        /// rest pose 1.2 m north of the spawn, facing south, with a soft studio key / fill /
        /// rim light. Cameras (Godot poses): front spawnX,0.2,spawnZ-0.7,0,-4 ·
        /// side spawnX+0.5,0.2,spawnZ-1.2,90,-4 · back spawnX,0.2,spawnZ-1.7,180,-4.</summary>
        void RobotView(string id)
        {
            var r = robots.Find(x => x.id == id);
            if (r == null) return;
            workbench.ForceInstall(workbench.P(id));
            r.SetOperational();
            r.enabled = false;
            var pos = Shared.Pos(layout.spawn.x, 0, layout.spawn.z - 1.2f);
            r.transform.SetPositionAndRotation(pos, Quaternion.LookRotation(Vector3.back));   // faces south, towards the camera
            foreach (var (off, col, inten) in new[] { (new Vector3(0.45f, 0.55f, -0.5f), new Color(1f, 0.93f, 0.82f), 0.6f), (new Vector3(-0.5f, 0.35f, -0.4f), new Color(0.75f, 0.82f, 1f), 0.25f), (new Vector3(0.1f, 0.6f, 0.6f), new Color(1f, 0.9f, 0.75f), 0.45f) })
            {
                var lg = new GameObject("StudioLight").AddComponent<Light>();
                lg.type = LightType.Point; lg.range = 2.5f; lg.color = col; lg.intensity = inten;
                lg.transform.position = pos + off;
            }
        }

        /// <summary>QA tool (--gallery=gear [--lod=0|1]): every mesh whose geometry key starts
        /// with the prefix, in a grid 2.5 m north of the spawn, each scaled to 25 cm.
        /// Camera: --cam=spawnX,1.75,spawnZ,0,0 (Godot pose).</summary>
        void BuildGallery(string prefix, int lod)
        {
            var keys = db.order.Select(d => db.Get(d)).GroupBy(d => d.geometryKey).Where(gk => gk.Key.StartsWith(prefix)).OrderBy(gk => gk.Key).ToList();
            var root = new GameObject("Gallery").transform;
            root.SetParent(transform, false);
            int cols = 10;
            for (int k = 0; k < keys.Count; k++)
            {
                var def = keys[k].First();
                var e = MeshLibrary.Get(def.geometryKey, lod);
                var go = new GameObject(def.geometryKey);
                go.AddComponent<MeshFilter>().sharedMesh = e.mesh;
                go.AddComponent<MeshRenderer>().sharedMaterials = ItemAppearance.SlotMaterials(e, ItemAppearance.For(def));
                go.transform.SetParent(root, false);
                var size = e.bounds.size;
                go.transform.localScale = Vector3.one * (0.25f / Mathf.Max(size.x, size.y, size.z));
                go.transform.position = Shared.Pos(layout.spawn.x - 1.6f + (k % cols) * 0.35f, 2.35f - (k / cols) * 0.35f, layout.spawn.z - 2.5f);
                go.transform.rotation = Quaternion.Euler(-35, 25, 0);
                Debug.Log($"[gallery] {k}: {def.geometryKey}");
            }
        }

        /// <summary>Unity-XZ footprints of storage units, benches and other furniture:
        /// parts lying there rest on furniture, not on the floor.</summary>
        List<Rect> FurnitureFootprints()
        {
            var rects = new List<Rect>(layout.floorBlockers);
            foreach (var f in layout.furniture)
            {
                if (Json.Str(f, "type") != "bench") continue;
                var p = WorkshopLayout.V(f["pos"]); var s = WorkshopLayout.V(f["size"]);
                rects.Add(new Rect(p.x - s.x / 2, p.z - s.z / 2, s.x, s.z));
            }
            return rects.Select(r => new Rect(r.xMin, -r.yMax, r.width, r.height)).ToList();   // Godot z -> Unity z
        }

        /// <summary>One robot per workbench project (crafted Hoverbots included, hidden
        /// until their turn on the bench). Its home — the charging mat it returns to — is
        /// on the floor in front of its workbench mat.</summary>
        void SpawnRobots()
        {
            foreach (var r in robots) if (r != null) Destroy(r.gameObject);
            robots.Clear();
            robotReserved.Clear();
            var bench = workbench.transform;
            var front = workbench.Front;
            foreach (var p in workbench.projects)
            {
                int extra = workbench.projects.Where(x => x.type == p.type).ToList().IndexOf(p);
                var r = new GameObject("Robot_" + p.id).AddComponent<Robot>();
                r.transform.SetParent(transform, false);
                var home = bench.TransformPoint(Batch.G(RobotWorkbench.MatX(p.mat) - extra * 0.3f, 0, (p.type == "caterpillarbot" ? 1.05f : 0.85f) + extra * 0.35f));
                r.Init(this, p.type, p.id, new Vector3(home.x, 0, home.z), Quaternion.LookRotation(front));
                robots.Add(r);
            }
        }

        /// <summary>Opens the Robot Workbench screen on a project.</summary>
        public void OpenWorkbench(string projectId)
        {
            workbench.selected = projectId;
            SetState("workbench");
        }

        /// <summary>The active robot closest to the player (commands go to it).</summary>
        public Robot NearestActiveRobot()
        {
            var p = player.transform.position;
            return robots.Where(r => r.Active).OrderBy(r => (r.transform.position - p).sqrMagnitude).FirstOrDefault();
        }

        /// <summary>H (Automatic Storage Delivery): the nearest free robot comes over,
        /// takes what you carry and stores it.</summary>
        public string RobotHandover()
        {
            string msg;
            if (!progression.Has("auto_delivery")) msg = "You need the Automatic Storage Delivery skill (Esc → Skills)";
            else if (inventory.Count == 0) msg = "You aren't carrying anything";
            else
            {
                var p = player.transform.position;
                // prefer a free robot; otherwise the nearest one comes right after its delivery
                // whoever can be here soonest: travel time, plus a penalty while busy; not robots about to charge
                var r = robots.Where(x => x.Active && !x.handoverPending && x.Battery >= 0.2f)
                    .OrderBy(x => Vector3.Distance(x.transform.position, p) / Mathf.Max(0.1f, x.WalkSpeed) + (x.Free ? 0 : 15 + x.carried.Count * 2)).FirstOrDefault();
                msg = r != null && r.RequestHandover() ? (r.Free ? $"{r.displayName} is coming for your parts" : $"{r.displayName} is coming for your parts after its delivery") : "No robot is running yet";
            }
            hud.OnFeedback(msg, msg.Contains("coming"));
            return msg;
        }

        /// <summary>J (Task Coordination): the nearest robot works only around the point
        /// you aim at; J again on the same spot releases it. Limited by command slots.</summary>
        public string RobotZone()
        {
            string msg;
            int slots = (int)progression.Effect("robot_command_slots");
            var cam = player.cam.transform;
            if (slots <= 0) msg = "You need the Robot Task Coordination skill (Esc → Skills)";
            else if (!Physics.Raycast(cam.position, cam.forward, out var hit, 30f, 1 << Layers.Env)) msg = "Aim at the floor where the robot should work";
            else
            {
                var p = new Vector3(hit.point.x, 0, hit.point.z);
                var zoned = robots.FirstOrDefault(r => r.zoneCenter.HasValue && (r.zoneCenter.Value - p).magnitude < 1.5f);
                if (zoned != null) { zoned.AssignZone(null); msg = $"{zoned.displayName} works the whole workshop again"; }
                else if (robots.Count(r => r.zoneCenter.HasValue) >= slots) msg = $"All {slots} command slot{(slots == 1 ? "" : "s")} in use (J on a zone to release it)";
                else
                {
                    var r = robots.Where(x => x.Active && !x.zoneCenter.HasValue).OrderBy(x => (x.transform.position - p).sqrMagnitude).FirstOrDefault();
                    if (r == null) msg = "No free robot to assign";
                    else { r.AssignZone(p); msg = $"{r.displayName} now works within {r.zoneRadius:0} m of this spot"; telemetry?.SkillUsed("robot_coordination"); }
                }
            }
            hud.OnFeedback(msg, msg.Contains("now works") || msg.Contains("again"));
            return msg;
        }

        /// <summary>Sorting tray footprint in Unity XZ.</summary>
        public Rect TrayRectU => new(layout.trayRect.xMin, -layout.trayRect.yMax, layout.trayRect.width, layout.trayRect.height);
        public Vector3 TrayCenter => Shared.Pos(layout.trayRect.center.x, 0, layout.trayRect.center.y);
        public bool InTray(Vector3 p) => TrayRectU.Contains(new Vector2(p.x, p.z));
        public int TrayCount => index.QuerySphere(TrayCenter, 1f).Count(i => InTray(world.instances[i].Position));

        /// <summary>The storage unit whose footprint contains a world point, if any.</summary>
        public StorageUnit UnitAt(Vector3 p)
        {
            foreach (var u in units.Values)
            {
                var l = u.transform.InverseTransformPoint(p);
                if (Mathf.Abs(l.x) < u.size.x * 0.5f + 0.05f && Mathf.Abs(l.z) < u.size.z * 0.5f + 0.05f) return u;
            }
            return null;
        }

        // ---------------------------------------------------------------- progression

        public Progression progression;
        public Telemetry telemetry;
        public double playTime;
        string _activity = "collecting";
        string _telemetryFile;
        Dictionary<string, object> _progressionCfg;
        public const int BaseInventory = 30;

        void StartProgression(int seed, Dictionary<string, object> save)
        {
            _progressionCfg ??= Shared.ReadObject(Shared.DataPath("progression.json"));
            progression = new Progression(_progressionCfg, presetId);
            foreach (var e in progression.Validate()) Debug.LogError("[progression] " + e);
            telemetry = new Telemetry(presetId, seed, world.Count);
            playTime = 0;
            _telemetryFile = Path.Combine(Application.persistentDataPath, "telemetry", $"{presetId}_{seed}_{DateTime.Now:yyyyMMdd_HHmmss}.json");
            if (save != null)
            {
                progression.Load(Json.Obj(save.GetValueOrDefault("progression")));
                playTime = Json.Num(save, "play_time_s");
            }
            ApplySkills();
            progression.LeveledUp += lv => workbench.CheckBlueprints(true);
            progression.RobotUpgraded += type => { foreach (var r in robots) if (r.type == type) r.ApplyUpgrades(); };
            progression.LeveledUp += lv =>
            {
                hud.Toast($"Level {lv}!  Skill point earned (Esc → Skills)", true);
                Sfx.Play("milestone", null, -4);
                telemetry.LevelReached(lv, playTime, progress.sortedTotal);
            };
            // XP is earned only for meaningful, non-repeatable progress (see progression.json)
            storage.Deposited += (box, id) =>
            {
                bool robot = storage.lastDepositor.StartsWith("robot");
                progression.OnSorted(id, robot);
                telemetry.Sorted(robot, playTime, progress.sortedTotal);
            };
            progress.DefinitionCompleted += id => progression.OnDefinitionCompleted(id, world.TotalOf(id));
            progress.Discovered += id => { progression.OnDiscovered(id, db.Get(id).IsRobotPart); if (progression.Has("reveal_totals")) progress.revealed.Add(id); };
            progress.DefinitionCompleted += id => progress.revealed.Add(id);   // a finished set shows its total
            progress.Milestone += (key, _) =>
            {
                var p = key.Split('_');   // zone_<index>_<percent>
                if (p.Length == 3 && p[0] == "zone") progression.OnZoneRestored(int.Parse(p[1]), int.Parse(p[2]));
            };
        }

        /// <summary>Applies the effects of unlocked skills that this build implements.</summary>
        public void ApplySkills()
        {
            inventory.capacity = BaseInventory + (int)progression.Effect("inventory_capacity");
            interaction.reachBonus = (float)progression.Effect("reach_m");
        }

        public bool UnlockSkill(string id)
        {
            if (!progression.Unlock(id)) return false;
            ApplySkills();
            if (progression.Has("reveal_totals")) foreach (var d in progress.discovered) progress.revealed.Add(d);   // Inventory Manifest
            telemetry.SkillUnlocked(id, playTime);
            hud.Toast("Skill unlocked: " + progression.Skill(id).name, true);
            return true;
        }

        /// <summary>Buys the next rank of a robot upgrade (its type must be operational).</summary>
        public bool BuyRobotUpgrade(string id)
        {
            var u = progression.Upgrade(id);
            if (u == null || !progression.BuyUpgrade(id, robots.Any(r => r.type == u.robot && r.Operational))) return false;
            telemetry.SkillUnlocked(id + "#" + progression.RankOf(id), playTime);
            var robotName = Robot.AllConfig().ContainsKey(u.robot) ? Json.Str(Json.Obj(Robot.AllConfig()[u.robot]), "name") : u.robot;
            hud.Toast($"{u.name} rank {progression.RankOf(id)} — every {robotName} gets it", true);
            return true;
        }

        void WriteTelemetry()
        {
            if (telemetry == null || Automated) return;
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(_telemetryFile));
                File.WriteAllText(_telemetryFile, Json.Write(telemetry.ToDict(playTime)));
            }
            catch (Exception e) { Debug.LogWarning("Telemetry not written: " + e.Message); }
        }

        void OnApplicationQuit() => WriteTelemetry();

        /// <summary>Starts a new world, switching world-size preset first if needed.</summary>
        public bool NewGame(string preset, int seed)
        {
            if (preset != presetId || db == null || !db.IsValid)
            {
                if (!LoadPreset(preset)) { hud.ShowFatal("The item data files are invalid:\n\n" + string.Join("\n", db.errors.Take(25))); return false; }
            }
            itemCount = -1;
            return StartWorld(seed, null);
        }

        // Saving never silently replaces a different world: the first attempt to
        // overwrite a slot that holds another preset/seed only warns.
        string _overwriteArmed;
        float _overwriteArmedAt;

        public void SaveGame(string slot)
        {
            var existing = SaveSystem.Read(slot);
            if (existing != null && !Automated && !SaveSystem.SameWorld(existing, this) && !(_overwriteArmed == slot && Time.unscaledTime - _overwriteArmedAt < 8))
            {
                _overwriteArmed = slot;
                _overwriteArmedAt = Time.unscaledTime;
                var msg = $"Slot '{slot}' holds a different workshop ({SaveSystem.WorldLabel(existing)}). Save again within 8 s to overwrite it.";
                hud.OnFeedback(msg, false);
                hud.SetStatus(msg);
                return;
            }
            _overwriteArmed = null;
            if (SaveSystem.Write(slot, SaveSystem.Serialize(this)))
            {
                Sfx.Play("save", null, -6);
                hud.Toast($"Game saved ({slot})");
                WriteTelemetry();
            }
        }

        public void LoadGame(string slot)
        {
            var d = SaveSystem.Read(slot);
            if (d == null) { hud.SetStatus("No save in that slot."); return; }
            var preset = Json.Str(d, "preset", "standard");
            if (preset != presetId && !LoadPreset(preset)) { hud.ShowFatal($"The '{preset}' preset data is invalid:\n\n" + string.Join("\n", db.errors.Take(25))); return; }
            var problem = SaveSystem.Check(d, db);
            if (problem != "") { hud.OnFeedback(problem, false); hud.SetStatus(problem); return; }
            int count = Json.Int(d, "item_count", -1);
            itemCount = count == Json.Int(db.config, "total_items", 4096) ? -1 : count;
            if (StartWorld(Json.Int(d, "seed"), d)) { SetState("play"); hud.Toast("Loaded " + slot); }
        }

        // ---------------------------------------------------------------- storage

        void BuildStorage()
        {
            foreach (var u in db.units)
            {
                var id = Json.Str(u, "id");
                if (!layout.units.TryGetValue(id, out var pl)) continue;
                var dests = db.DestinationsOf(id);
                dests.Sort((a, b) => (Json.Int(a, "level") * 1000 + Json.Int(a, "row") * 100 + Json.Num(a, "x")).CompareTo(Json.Int(b, "level") * 1000 + Json.Int(b, "row") * 100 + Json.Num(b, "x")));
                var go = new GameObject("Unit_" + id);
                go.transform.SetParent(transform, false);
                var p = WorkshopLayout.V(pl["pos"]);
                go.transform.position = Shared.Pos(p.x, p.y, p.z);
                go.transform.rotation = Shared.YRot((float)Json.Num(pl, "rot_y"));
                var su = go.AddComponent<StorageUnit>();
                su.Setup(u, dests);
                units[id] = su;
            }
        }

        public Matrix4x4 StoragePlacement(string box, int id, int index)
        {
            if (!db.boxes.TryGetValue(box, out var d)) return Matrix4x4.identity;
            var u = units[Json.Str(d, "unit")];
            if (u.type == "drawer_cabinet") return Matrix4x4.identity;
            return u.DestWorld(box) * StorageArrangement.SlotLocal(world.DefOf(id), index, StorageUnit.Inner(d));
        }

        (string, string, string, bool) LabelFor(string dest)
        {
            var d = db.boxes[dest];
            var code = Json.Str(d, "slot");
            if (!_assignments.TryGetValue(dest, out var defs)) return (code, "", "", false);
            bool known = false;
            string name = "";
            foreach (var id in defs) if (progress != null && progress.IsDiscovered(id)) { known = true; name = db.Get(id).displayName; }
            int stored = storage != null ? storage.Stored(dest).Count : 0;
            return (code, name, stored > 0 || known ? $"{stored}/{db.BoxCapacity(dest)}" : "", known);
        }

        public void Presort(float fraction)
        {
            var rng = new System.Random(99);
            foreach (var inst in world.instances)
            {
                if (rng.NextDouble() < fraction)
                {
                    index.Remove(inst.id);
                    storage.Deposit(world.DefOf(inst.id).storageBox, inst.id);
                    progress.Discover(inst.defId);
                }
            }
            workbench.Refresh();
            foreach (var u in units.Values) u.RefreshLabels();
        }

        void Update()
        {
            if (_labelsDirty.Count > 0 && storage != null)
            {
                var us = _labelsDirty.Where(db.boxes.ContainsKey).Select(b => Json.Str(db.boxes[b], "unit")).Distinct().ToList();
                foreach (var u in us) if (units.TryGetValue(u, out var su)) su.RefreshLabels();
                _labelsDirty.Clear();
            }
            if (world != null && progress != null && builder != null) RestorationLighting();
            if (world != null && objectives != null && state == "play") objectives.Tick(Time.deltaTime);
            if (world != null && telemetry != null && state == "play")
            {
                playTime += Time.deltaTime;
                telemetry.Tick(Time.deltaTime, inventory.items.Count == 0 ? "collecting" : _activity);
                telemetry.MaybeSample(playTime, Json.Num(Json.Obj(_progressionCfg["telemetry"]), "sample_interval_s", 60), progress.sortedTotal, progression.level);
            }
            if (world == null || Automated) return;
            var kb = Keyboard.current;
            if (kb == null) return;
            if (kb.escapeKey.wasPressedThisFrame)
            {
                if (state == "play") { if (interaction.inspecting) interaction.EndInspect(); else SetState("paused"); }
                else if (state == "paused" || state == "inventory" || state == "workbench") { if (!hud.BackFromSubScreen()) SetState("play"); }
                else if (state == "menu") hud.BackFromSubScreen();
            }
            else if (kb.tabKey.wasPressedThisFrame && (state == "play" || state == "inventory")) SetState(state == "play" ? "inventory" : "play");
            else if (kb.f5Key.wasPressedThisFrame && state == "play") SaveGame("quick");
            else if (kb.rKey.wasPressedThisFrame && state == "play" && !interaction.inspecting)
            {
                // the collection specialist takes assignments; otherwise the nearest robot focuses on that kind
                var rb = robots.FirstOrDefault(r => r.Active && r.strategy == "assigned_definition") ?? NearestActiveRobot();
                if (rb == null) hud.OnFeedback("No robot is running yet: build one at the Robot Workbench", false);
                else if (interaction.targetType == "item")
                {
                    var msg = rb.CommandFocus(world.instances[interaction.targetItem].defId);
                    hud.OnFeedback(msg, msg.Contains(": on it"));
                }
                else hud.OnFeedback($"Aim at a part, then press R to send {rb.displayName} for it", false);
            }
            else if (kb.tKey.wasPressedThisFrame && state == "play") NearestActiveRobot()?.ToggleFollow();
            else if (kb.hKey.wasPressedThisFrame && state == "play") RobotHandover();
            else if (kb.jKey.wasPressedThisFrame && state == "play") RobotZone();
            else if (kb.f9Key.wasPressedThisFrame && (state == "play" || state == "paused")) LoadGame("quick");
            if (state == "play" && Cursor.lockState != CursorLockMode.Locked && Mouse.current.leftButton.wasPressedThisFrame) Cursor.lockState = CursorLockMode.Locked;
        }

        public void SetState(string s)
        {
            state = s;
            bool playing = s == "play";
            if (!playing && interaction.inspecting && s != "paused") interaction.EndInspect();
            player.inputEnabled = playing && !interaction.inspecting;
            player.lookEnabled = playing && !interaction.inspecting;
            interaction.enabledInput = playing && !Automated;   // automated runs drive interaction directly; real mouse clicks must not interfere
            hud.SetScreen(s);
            if (!Automated)
            {
                Cursor.lockState = playing ? CursorLockMode.Locked : CursorLockMode.None;
                Cursor.visible = !playing;
            }
        }

        // ---------------------------------------------------------------- screenshots

        public void PlaceCamera(string spec)
        {
            var p = spec.Split(',').Select(x => float.Parse(x, System.Globalization.CultureInfo.InvariantCulture)).ToArray();
            player.Teleport(Shared.Pos(p[0], p[1] - PlayerController.Eye, p[2]));
            player.SetView(p[3] * Mathf.Deg2Rad, p[4] * Mathf.Deg2Rad);
            player.head.localPosition = new Vector3(0, PlayerController.Eye, 0);
        }

        public IEnumerator Capture(string path, int frames = 20)
        {
            for (int i = 0; i < frames; i++) yield return null;
            yield return new WaitForEndOfFrame();
            var tex = ScreenCapture.CaptureScreenshotAsTexture();
            File.WriteAllBytes(path, tex.EncodeToPNG());
            Destroy(tex);
        }

        IEnumerator TakeShots()
        {
            player.enabled = false;
            hud.SetHudVisible(args.ContainsKey("hud"));
            var outs = args["shot"].Split(';');
            var cams = (args.TryGetValue("cam", out var c) ? c : "0.6,1.65,3.6,0,-18").Split(';');
            yield return null;
            builder.RefreshProbe();
            for (int i = 0; i < outs.Length; i++)
            {
                PlaceCamera(cams[Mathf.Min(i, cams.Length - 1)]);
                yield return Capture(outs[i]);
                Debug.Log("saved " + outs[i]);
            }
            Application.Quit();
        }
    }
}
