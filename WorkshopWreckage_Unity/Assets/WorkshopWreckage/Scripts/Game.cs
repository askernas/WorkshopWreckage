using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
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
        /// <summary>The 24 detachable heads, arms and propellers and where each one is (Modify Robot).</summary>
        public RobotModules modules;
        public readonly List<Robot> robots = new();
        public Robot retrobot => robots.Find(r => r.id == "retrobot");
        /// <summary>Sorting Arm sockets of the current layout (two per drawer cabinet).</summary>
        public readonly Dictionary<string, ArmSocket> sockets = new();
        /// <summary>The Sorting Arm in the player's hands (two-handed carry), if any.</summary>
        public SortingArm CarriedArm => robots.Select(r => r.arm).FirstOrDefault(a => a != null && a.place == SortingArm.Place.Carried);
        /// <summary>Is this destination served by a mounted, working Sorting Arm?</summary>
        public bool ArmServes(string box)
        {
            foreach (var s in sockets.Values) if (s.occupant != null && s.occupant.Working && s.dests.Contains(box)) return true;
            return false;
        }
        public readonly HashSet<int> robotReserved = new();
        /// <summary>The starter set placed in plain sight near the spawn (see WorldGenerator).</summary>
        public List<int> starterIds = new();   // parts a robot is on its way to: no one else takes them
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

            RetrobotModel.LegacyLook = args.TryGetValue("retrolook", out var rlook) && rlook == "legacy";
            if (args.TryGetValue("retrosplay", out var rsp)) RetrobotModel.HipSplay = float.Parse(rsp, System.Globalization.CultureInfo.InvariantCulture);   // QA: thigh splay (deg)   // QA: the finish before the polish pass
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

            if (args.TryGetValue("skins", out var sk)) ItemSkins.Enabled = sk != "0";   // QA: --skins=0 is the original salvage look (before / after)
            if (args.TryGetValue("items", out var n)) itemCount = int.Parse(n);
            int seed = args.TryGetValue("seed", out var sd) ? int.Parse(sd) : Json.Int(db.config, "default_seed", 1912);
            if (!StartWorld(seed, null)) return;
            startupMs = Time.realtimeSinceStartupAsDouble * 1000;   // process start to interactive
            StartCoroutine(ProbeLater());
            if (args.TryGetValue("partshadows", out var psm)) ItemRenderer.ShadowMode = Mathf.Clamp(int.Parse(psm), 0, 2);   // QA / benchmark
            // QA: item lighting terms (1 normals, 2 ambient, 3 main light, 4 additional lights)
            if (args.TryGetValue("itemdebug", out var idm)) Shader.SetGlobalFloat("_WWItemDebug", float.Parse(idm, System.Globalization.CultureInfo.InvariantCulture));
            if (args.TryGetValue("robotview", out var rv)) RobotView(rv);
            if (args.TryGetValue("robotwatch", out var rwatch)) { SetState("play"); StartCoroutine(RobotWatch(rwatch)); return; }
            if (args.TryGetValue("activatewatch", out var awatch)) { SetState("play"); StartCoroutine(ActivateWatch(awatch)); return; }
            if (args.TryGetValue("catalogue", out var catDir)) { SetState("play"); StartCoroutine(CatalogueExport.Run(this, catDir)); return; }   // QA: component catalogue (read-only)
            if (args.TryGetValue("layoutdump", out var ld)) { DumpLayout(ld); Application.Quit(); return; }
            if (args.TryGetValue("gallery", out var gal)) BuildGallery(gal, args.TryGetValue("lod", out var lodArg) ? int.Parse(lodArg) : MeshLibrary.LodWorld);
            if (args.TryGetValue("presort", out var ps)) Presort(float.Parse(ps, System.Globalization.CultureInfo.InvariantCulture));
            if (args.ContainsKey("shot")) { SetState("play"); StartCoroutine(TakeShots()); }
            else if (args.TryGetValue("menushot", out var ms)) { SetState("menu"); StartCoroutine(MenuShot(ms)); }   // QA: the real main menu (backdrop and blur)
            else if (args.ContainsKey("robotbench") && _studio != null) { SetState("play"); StartCoroutine(RobotBench()); }
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

        public bool Automated => args.ContainsKey("shot") || args.ContainsKey("bench") || args.ContainsKey("robotbench") || args.ContainsKey("autoplay");

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
            _containers = null;
            Debug.Log("[boot] storage built");
            workbench.transform.position = Shared.Pos(layout.repairBayPos.x, layout.repairBayPos.y, layout.repairBayPos.z);
            workbench.transform.rotation = Shared.YRot(layout.repairBayRot);
            if (startupMs > 0) StartCoroutine(ProbeLater());
            return true;
        }

        Transform _posters;
        WorkshopLayout _postersFor;

        /// <summary>What the last load had to convert (empty if the catalog was unchanged).</summary>
        public string migrationReport = "";

        public bool StartWorld(int seed, Dictionary<string, object> save)
        {
            migrationReport = "";
            if (_layoutSeed != seed && !BuildWorkshop(seed)) return false;
            var gen = new WorldGenerator { forbidden = InsideContainerG };
            int total = Json.Int(db.config, "total_items", 4096);
            var w = gen.Generate(db, layout, seed, itemCount > 0 && itemCount < total ? itemCount : -1);
            if (w == null) { hud.ShowFatal("World could not be generated:\n\n" + string.Join("\n", gen.errors.Take(20))); return false; }
            Debug.Log($"[world] preset {presetId}, seed {seed}: {w.Count} items in {gen.lastGenerationMs:0} ms ({layout.piles.Count} pile sites, {layout.surfaces.Count} surfaces, {units.Count} storage units)");
            world = w;
            starterIds = gen.starterIds;
            if (renderer != null) Destroy(renderer.gameObject);
            renderer = new GameObject("Items").AddComponent<ItemRenderer>();
            renderer.transform.SetParent(transform, false);
            storage = new StorageSystem(db, world) { placement = StoragePlacement, straysIn = box => StraysIn(box).Count };
            inventory = new Inventory(world);
            if (save != null && SaveSystem.NeedsMigration(save, db))
            {
                save = SaveMigration.Remap(save, world, db, out var report);
                migrationReport = report;
                Debug.Log("[save] migrated to the current catalog: " + report);
            }
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
                int hd = Json.Int(inv, "held_def", -1);
                if (hd >= 0) inventory.Select(hd);
                progress.Load(Json.Obj(save["progress"]));
            }
            renderer.Build(world);
            index = new ItemSpatialIndex(world);
            if (save != null) SettleAfterLoad();
            debris = new DebrisField(world, index, Shared.Pos(layout.roomMin.x, 0, layout.roomMax.z), Shared.Pos(layout.roomMax.x, 0, layout.roomMin.z), FurnitureFootprints());
            player.debris = debris;
            interaction.ResetState();
            tools.ResetState();
            StartProgression(seed, save);
            workbench.PrepareOrders();
            if (_postersFor != layout && !args.ContainsKey("noposters"))   // --noposters: QA (performance comparison)   // per layout: wall sheets follow the room (the drawings themselves are cached)
            {
                if (_posters != null) Destroy(_posters.gameObject);
                _posters = BlueprintPosters.Build(this);
                _postersFor = layout;
            }
            workbench.ResetState();
            modules = new RobotModules();
            SpawnRobots();
            workbench.Load(save);
            foreach (var u in units.Values) { u.labelProvider = LabelFor; u.RefreshLabels(); }
            objectives = new Objectives(this);
            if (save != null) objectives.Load(save.GetValueOrDefault("objectives"));
            else objectives.Tick(1);   // nothing is done yet; evaluates silently
            objectives.Completed += o => { hud.Toast("Objective complete: " + o.text, true); Sfx.Play("milestone", null, -4); };
            if (save != null && save.TryGetValue("robots", out var ro))
                foreach (var kv in Json.Obj(ro)) robots.Find(r => r.id == kv.Key)?.LoadState(Json.Obj(kv.Value));
            modules.Load(save != null ? Json.Obj(save.GetValueOrDefault("modules")) : null, robots);
            foreach (var e in modules.Audit()) Debug.LogError("[modules] " + e);
            foreach (var r in robots) r.ApplyModules();
            // an incomplete robot can't work: wherever it was saved, it is parked at the workbench
            foreach (var r in robots) if (r.Operational && !r.Docked && !modules.Complete(r.id)) r.DockAtSpot();
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
            if (save == null && starterIds.Count > 0)
            {
                var sd = world.DefOf(starterIds[0]);
                hud.Toast($"Right in front of you: {starterIds.Count} × {sd.displayName} — they all go in {db.BoxLabel(sd.storageBox)}", true);
            }
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

        /// <summary>Highest furniture surface (bench, shelf deck, pallet) or the floor under a
        /// Unity-space point, no higher than maxY.</summary>
        /// <summary>Is this part covered: do parts lying on top of it hide at least half of its
        /// top (bounding boxes)? Covered parts can't be grabbed until what lies on them is gone:
        /// the same for the player's tools and the ground robots (IMPLEMENTATION_AUDIT §E.2).</summary>
        public bool Covered(int id)
        {
            var b = index.WorldBounds(id);
            float area = b.size.x * b.size.z;
            if (area <= 1e-6f) return false;
            float cover = 0;
            foreach (var o in index.QuerySphere(b.center + Vector3.up * b.extents.y, Mathf.Max(b.extents.x, b.extents.z) + 0.3f))
            {
                if (o == id || world.instances[o].state != ItemState.InWorld || interaction.busy.Contains(o)) continue;
                var ob = index.WorldBounds(o);
                if (ob.min.y < b.max.y - 0.02f) continue;   // beside or below, not on top
                float ox = Mathf.Min(b.max.x, ob.max.x) - Mathf.Max(b.min.x, ob.min.x), oz = Mathf.Min(b.max.z, ob.max.z) - Mathf.Max(b.min.z, ob.min.z);
                if (ox > 0 && oz > 0) cover += ox * oz;
                if (cover >= area * 0.5f) return true;
            }
            return false;
        }

        /// <summary>A part saved while it was still falling would load hanging in the air: after a
        /// load every moved part comes down onto its real support (lowest first, so stacks settle).</summary>
        void SettleAfterLoad()
        {
            var moved = world.instances.Where(i => i.state == ItemState.InWorld && i.moved).OrderBy(i => i.Position.y).ToList();
            var none = new Dictionary<int, float>();
            int n = 0;
            foreach (var i in moved)
            {
                var b = index.WorldBounds(i.id);
                float dy = b.min.y - interaction.SupportUnder(i.id, b, none);
                if (dy < 0.012f) continue;
                var m = i.transform;
                m.m13 -= dy;
                index.Remove(i.id);
                world.SetTransform(i.id, m);
                index.Insert(i.id);
                n++;
            }
            if (n > 0) Debug.Log($"[load] {n} part(s) saved mid-fall settled");
        }

        /// <summary>Highest deck a dropped part may rest on (bench, pallet, tray, low open shelf).</summary>
        public const float MaxDropDeck = 1.25f;

        bool InBlocker(Vector3 p)
        {
            var q = new Vector2(p.x, -p.z);
            foreach (var b in layout.floorBlockers) if (b.Contains(q)) return true;
            return false;
        }

        /// <summary>Where a part may be set down near `want`: inside the room, never on top of
        /// a storage unit or inside furniture or a closed container (it slides back towards
        /// `from` until the floor is open), resting on the floor or a low deck (up to MaxDropDeck).</summary>
        public Vector3 SafeDropPoint(Vector3 want, Vector3 from)
        {
            float m = 0.25f;
            Vector3 Clamp(Vector3 v) => new(Mathf.Clamp(v.x, layout.roomMin.x + m, layout.roomMax.x - m), 0, Mathf.Clamp(v.z, -layout.roomMax.z + m, -layout.roomMin.z - m));
            var p = Clamp(new Vector3(want.x, 0, want.z));
            var f = Clamp(new Vector3(from.x, 0, from.z));
            for (int k = 0; k <= 40; k++)
            {
                var q = Vector3.Lerp(p, f, k / 40f);
                float deck = SurfaceBelow(q, Mathf.Min(want.y + 0.1f, MaxDropDeck));
                bool onDeck = deck > 0.01f;
                if ((!InBlocker(q) || onDeck) && !InsideContainer(q + Vector3.up * (deck + 0.05f), 0)) return new Vector3(q.x, deck, q.z);
            }
            return new Vector3(f.x, 0, f.z);
        }

        /// <summary>World integrity (log only, never repairs by itself): unsorted parts a player
        /// could not reach: outside the room, higher than any deck a part may rest on, inside a
        /// closed container, or on the floor under a storage unit.</summary>
        public List<(int id, string why)> UnreachableParts()
        {
            var o = new List<(int, string)>();
            if (world == null) return o;
            float maxY = WorkshopLayout.MaxStrayShelf + 0.7f;
            foreach (var i in world.instances)
            {
                if (i.state != ItemState.InWorld || interaction.busy.Contains(i.id)) continue;
                var p = i.Position;
                if (p.x < layout.roomMin.x || p.x > layout.roomMax.x || -p.z < layout.roomMin.z || -p.z > layout.roomMax.z) o.Add((i.id, "outside the room"));
                else if (p.y > maxY && !InTray(p)) o.Add((i.id, $"too high ({p.y:0.00} m)"));
                else if (InsideContainer(p, 0)) o.Add((i.id, "inside a container"));
                else if (p.y < 0.2f && UnitAt(p) != null && SurfaceBelow(p, p.y + 0.05f) < 0.01f) o.Add((i.id, "under a storage unit"));
            }
            return o;
        }

        /// <summary>Moves every unreachable part (see UnreachableParts) into the sorting tray.</summary>
        public int RecoverUnreachable()
        {
            int n = 0;
            foreach (var (id, _) in UnreachableParts())
            {
                index.Remove(id);
                world.SetTransform(id, interaction.TrayMatrix(world.DefOf(id)));
                world.SetState(id, ItemState.InWorld, "tray");
                index.Insert(id);
                n++;
            }
            return n;
        }

        public float SurfaceBelow(Vector3 p, float maxY)
        {
            float best = 0;
            var q = new Vector2(p.x, -p.z);
            foreach (var s in layout.surfaces)
                if (!s.floor && s.y <= maxY && s.y > best && s.rect.Contains(q)) best = s.y;
            foreach (var (r, y) in layout.decks)
                if (y <= maxY && y > best && r.Contains(q)) best = y;
            return best;
        }

        /// <summary>Unsorted parts of other definitions lying inside an open destination
        /// (shelf section, bin, box, crate, pallet): they block storing there.</summary>
        public List<int> StraysIn(string box)
        {
            var o = new List<int>();
            if (index == null || !db.boxes.TryGetValue(box, out var d) || !units.TryGetValue(Json.Str(d, "unit"), out var u) || u.type == "drawer_cabinet") return o;
            var m = u.DestWorld(box);
            var inner = StorageUnit.Inner(d);
            var inv = m.inverse;
            var c = m.MultiplyPoint3x4(new Vector3(0, inner.y / 2, 0));
            foreach (var id in index.QuerySphere(c, inner.magnitude * 0.5f + 0.05f))
            {
                var i = world.instances[id];
                if (i.state != ItemState.InWorld || world.DefOf(id).storageBox == box || interaction.busy.Contains(id)) continue;
                var l = inv.MultiplyPoint3x4(i.Position);
                if (Mathf.Abs(l.x) < inner.x / 2 && Mathf.Abs(l.z) < inner.z / 2 && l.y > -0.03f && l.y < inner.y + 0.05f) o.Add(id);
            }
            return o;
        }

        List<(Matrix4x4 inv, Vector3 half, Bounds aabb)> _containers;

        /// <summary>Closed containers (boxes, bins, crates): parts are never generated inside
        /// them, where they would be hidden. Godot-space centre and radius.</summary>
        /// <summary>Unity-space point inside a closed container (boxes, bins, crates)?</summary>
        public bool InsideContainer(Vector3 p, float r) => InsideContainerG(new Vector3(p.x, p.y, -p.z), r);

        bool InsideContainerG(Vector3 g, float r)
        {
            if (_containers == null)
            {
                _containers = new List<(Matrix4x4, Vector3, Bounds)>();
                foreach (var kv in db.boxes)
                {
                    var kind = Json.Str(kv.Value, "kind");
                    if (kind == "shelf_section" || kind == "floor_pallet" || kind == "drawer") continue;
                    if (!units.TryGetValue(Json.Str(kv.Value, "unit"), out var u)) continue;
                    var m = u.DestWorld(kv.Key);
                    var inner = StorageUnit.Inner(kv.Value);
                    var half = new Vector3(inner.x / 2, inner.y, inner.z / 2);
                    var c = m.MultiplyPoint3x4(new Vector3(0, inner.y / 2, 0));
                    _containers.Add((m.inverse, half, new Bounds(c, Vector3.one * (inner.magnitude + 0.1f))));
                }
            }
            var p = Shared.Pos(g.x, g.y, g.z);
            foreach (var (inv, half, aabb) in _containers)
            {
                if (!aabb.Contains(p)) continue;
                var l = inv.MultiplyPoint3x4(p);
                if (Mathf.Abs(l.x) < half.x + r * 0.5f && Mathf.Abs(l.z) < half.z + r * 0.5f && l.y > -0.05f && l.y < half.y + r * 0.5f) return true;
            }
            return false;
        }

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

        const int StudioLayer = 30;
        (Transform robot, Bounds bounds)? _studio;
        Vector3 _studioFwd = Vector3.back;

        /// <summary>Studio camera pose k (0 front, 1 side with the robot facing right, 2 back,
        /// 3 three-quarter front left, 4 three-quarter rear, then close-ups three-quarter from
        /// the front: 5 head, 6 right shoulder, elbow and forearm, 7 right wrist and claw (the
        /// lower part of the forearm pivot), 8 right hip, knee and foot, 9 the hips and pelvis from
        /// the front (a band of the whole robot), 10 the right boot, 11 the right leg in profile),
        /// framing the robot's bounds (or the close-up's own; a robot without that pivot gets the full view).</summary>
        void StudioCamera(int k)
        {
            var (rt, bb) = _studio.Value;
            k %= 13;
            float yaw = new[] { 0f, 90f, 180f, -35f, 145f, -30f, 70f, -45f, -40f, -15f, -40f, 90f, 0f }[k];
            var dir = Quaternion.AngleAxis(yaw, Vector3.up) * _studioFwd;   // from the robot towards the camera (fixed: a turning robot turns in the frame)
            if (k == 12)   // the lower body straight from the front: pelvis, thighs, knees, boots
            {
                bb.SetMinMax(bb.min, new Vector3(bb.max.x, bb.min.y + bb.size.y * 0.42f, bb.max.z));
                bb.Expand(new Vector3(-bb.size.x * 0.4f, 0, 0));
            }
            if (k == 9)
            {
                bb.SetMinMax(new Vector3(bb.min.x, bb.min.y + bb.size.y * 0.2f, bb.min.z), new Vector3(bb.max.x, bb.min.y + bb.size.y * 0.47f, bb.max.z));
                bb.Expand(new Vector3(-bb.size.x * 0.45f, 0, 0));   // the legs and pelvis, not the claws
            }
            var close = k switch { 5 => "Head", 6 => "ArmR", 7 => "ArmR/Elbow", 8 => "LegR", 10 => "LegR/Knee/Ankle", 11 => "LegR", _ => null };
            var sub = close == null ? null : rt.GetComponentsInChildren<Transform>(true).FirstOrDefault(t => PathEndsWith(t, close));
            var rs = sub != null ? sub.GetComponentsInChildren<Renderer>() : new Renderer[0];
            if (rs.Length > 0)
            {
                bb = rs[0].bounds;
                foreach (var r in rs) bb.Encapsulate(r.bounds);
                if (k == 7) bb.SetMinMax(bb.min, new Vector3(bb.max.x, bb.min.y + bb.size.y * 0.55f, bb.max.z));   // the claw end
                if (k == 6) bb.SetMinMax(new Vector3(bb.min.x, bb.max.y - bb.size.y * 0.55f, bb.min.z), bb.max);    // shoulder to forearm
            }
            float radius = Mathf.Max(bb.extents.y, Mathf.Max(bb.extents.x, bb.extents.z)) * (rs.Length > 0 ? 1.25f : 1.12f)
                * (args.TryGetValue("studiozoom", out var sz) ? float.Parse(sz, System.Globalization.CultureInfo.InvariantCulture) : 1f);   // --studiozoom: more room around the robot (hero shots)
            float dist = radius / Mathf.Tan(player.cam.fieldOfView * 0.5f * Mathf.Deg2Rad);
            var target = bb.center;
            var camPos = target + dir * dist + Vector3.up * dist * (rs.Length > 0 ? 0.25f : 0.06f);
            player.cam.transform.SetPositionAndRotation(camPos, Quaternion.LookRotation(target - camPos));
        }

        /// <summary>Model QA (--robotview=id --robotbench): shading cost with the robot filling the
        /// frame (three-quarter view), averaged over 5 s after a warm-up; logs and quits.</summary>
        IEnumerator RobotBench()
        {
            QualitySettings.vSyncCount = 0;
            Application.targetFrameRate = -1;
            player.enabled = false;
            hud.SetHudVisible(false);
            yield return null;
            builder.RefreshProbe();
            StudioCamera(3);
            var (rt, bb) = _studio.Value;
            player.cam.transform.position = Vector3.Lerp(player.cam.transform.position, bb.center, 0.45f);   // closer: the robot fills the screen
            var timings = new FrameTiming[1];
            float warm = 0;
            while (warm < 2f) { yield return null; warm += Time.unscaledDeltaTime; }
            double gpu = 0, cpu = 0, frame = 0;
            int n = 0, gpuN = 0;
            var sw = System.Diagnostics.Stopwatch.StartNew();
            double last = 0;
            while (sw.Elapsed.TotalSeconds < 5)
            {
                yield return null;
                double now = sw.Elapsed.TotalMilliseconds;
                frame += now - last; last = now; n++;
                FrameTimingManager.CaptureFrameTimings();
                if (FrameTimingManager.GetLatestTimings(1, timings) > 0 && timings[0].gpuFrameTime > 0) { gpu += timings[0].gpuFrameTime; cpu += timings[0].cpuMainThreadFrameTime; gpuN++; }
            }
            Debug.Log(string.Format(System.Globalization.CultureInfo.InvariantCulture, "[robotbench] frame {0:0.000} ms, gpu {1:0.000} ms, cpu main {2:0.000} ms over {3} frames", frame / n, gpuN > 0 ? gpu / gpuN : -1, gpuN > 0 ? cpu / gpuN : -1, n));
            Application.Quit();
        }

        /// <summary>QA (--activatewatch=hoverbot --watchdir=dir): the robot is assembled on its mat and
        /// activated; frames from the side (act_00.png ...) follow it off the bench to its charging mat.
        /// The log gives the lowest point of the model above the bench top while it is over the bench.</summary>
        IEnumerator ActivateWatch(string id)
        {
            var r = robots.Find(x => x.id == id);
            if (r == null) { Application.Quit(); yield break; }
            player.enabled = false;
            hud.SetHudVisible(false);
            workbench.ForceInstall(workbench.P(id));
            for (int i = 0; i < 5; i++) yield return null;
            string dir = args.TryGetValue("watchdir", out var wd) ? wd : Path.Combine(Application.temporaryCachePath, "activate");
            Directory.CreateDirectory(dir);
            var start = r.transform.position;
            float matY = workbench.MatSpot(workbench.P(id).mat).pos.y;
            // from the room side of the bench (the robot faces the player on its mat), a little above the top
            var fwd = r.transform.forward; fwd.y = 0; fwd.Normalize();
            var at = start + Vector3.up * 0.2f;
            var from = at + fwd * 1.3f + Vector3.Cross(Vector3.up, fwd) * 0.5f + Vector3.up * 0.25f;
            var cam = player.cam.transform;
            player.cam.fieldOfView = 60;
            cam.SetPositionAndRotation(from, Quaternion.LookRotation(at - from));
            float hang = float.MaxValue;
            foreach (var rd in r.GetComponentsInChildren<Renderer>()) if (rd.enabled) hang = Mathf.Min(hang, rd.bounds.min.y - matY);
            Debug.Log($"[activatewatch] {id}: on the mat at {start}, home {r.home}; lowest point before activation {hang * 100:0.0} cm above the mat top");
            r.BeginActivation();
            float lowest = float.MaxValue, t0 = Time.time, next = 0;
            int k = 0;
            while (r.mode == Robot.Mode.Activating && Time.time - t0 < 20)
            {
                var off = r.transform.position - start; off.y = 0;
                if (off.magnitude > 0.03f && off.magnitude < 0.45f)   // moving across, still over the bench and its tray
                    foreach (var rd in r.GetComponentsInChildren<Renderer>()) if (rd.enabled) lowest = Mathf.Min(lowest, rd.bounds.min.y - matY);
                if (Time.time >= next) { next = Time.time + 0.25f; yield return Capture(Path.Combine(dir, $"act_{k++:00}.png"), 1); }
                yield return null;
            }
            yield return Capture(Path.Combine(dir, $"act_{k++:00}.png"), 1);
            Debug.Log($"[activatewatch] {id}: mode {r.mode}, {k} frames, lowest point while crossing the bench: {lowest * 100:0.0} cm above the mat top");
            Application.Quit();
        }

        /// <summary>QA (--robotwatch=retrobot --watchdir=dir): in-game evidence of a robot at work. It is
        /// activated at once and works as usual in the workshop; a camera stays beside it, and each time
        /// it starts a pick-up, a deposit or a staging (two of each) a strip of frames is saved
        /// (pick0_00.png ...). Work in between runs at double speed; captures at normal speed.</summary>
        IEnumerator RobotWatch(string id)
        {
            var r = robots.Find(x => x.id == id);
            if (r == null) { Application.Quit(); yield break; }
            player.enabled = false;
            hud.SetHudVisible(false);
            workbench.ForceInstall(workbench.P(id));
            r.SetOperational();
            string dir = args.TryGetValue("watchdir", out var wd) ? wd : Path.Combine(Application.temporaryCachePath, "watch");
            Directory.CreateDirectory(dir);
            // the situations to film: floor pick-up, pick-up beside a pile, drawer store, other direct store,
            // staging, walking and turning with a load
            var left = new Dictionary<string, int> { ["pick"] = 1, ["pickpile"] = 1, ["drawer"] = 1, ["store"] = 1, ["stage"] = 1, ["carrywalk"] = 1, ["carryturn"] = 1 };
            if (!r.CanDirectStore) { left.Remove("drawer"); left.Remove("store"); left["stage"] = 2; }   // a courier only stages
            if (r.CanClimb) left["climb"] = 1;
            if (r.GetComponentInChildren<RobotModel>()?.StowsCargo == true) left["stow"] = 1;   // the throw into the hold
            if (args.TryGetValue("watchonly", out var only))   // --watchonly=climb,store: just these moments
                foreach (var k in left.Keys.ToList()) if (!only.Split(',').Contains(k)) left.Remove(k);
            string pending = null;
            Robot.QAMoment = (rb, what) => { if (rb == r && pending == null && left.TryGetValue(what, out var n) && n > 0) pending = what; };
            var cam = player.cam.transform;
            player.cam.fieldOfView = 42;
            int env = 1 << Layers.Env;
            // what the shot must show: the robot's hands and the interaction target (or just the robot)
            Vector3 Hands() => r.GetComponentInChildren<RobotModel>()?.GripPoint ?? r.transform.position + Vector3.up * 0.12f;
            bool Clear(Vector3 from, Vector3 to) => !Physics.Linecast(from, to, env, QueryTriggerInteraction.Ignore);
            bool Good(Vector3 spot, Vector3 a, Vector3 b) =>
                !Physics.CheckSphere(spot, 0.08f, env, QueryTriggerInteraction.Ignore) && Clear(spot, a) && Clear(spot, b) && Clear(a + (spot - a).normalized * 0.05f, spot);
            // a three-quarter view from the robot's front side first, at a short distance; then the
            // other sides; then a little further out, then higher up (always with line of sight)
            Vector3? PickSpot(Vector3 focus, Vector3 a, Vector3 b)
            {
                var fwd = r.transform.forward; fwd.y = 0; fwd.Normalize();
                foreach (var dist in new[] { 0.6f, 0.8f, 1.0f })
                    foreach (var up in new[] { 0.28f, 0.5f })
                        foreach (var yaw in new[] { 50f, -50f, 80f, -80f, 25f, -25f, 115f, -115f })
                        {
                            var spot = focus + Quaternion.AngleAxis(yaw, Vector3.up) * fwd * dist + Vector3.up * up;
                            if (Good(spot, a, b)) return spot;
                        }
                return null;
            }
            Vector3 Focus(string what) => what.StartsWith("carry") ? Hands() : Vector3.Lerp(Hands(), r.qaTarget, 0.5f);
            Vector3 B(string what) => what.StartsWith("carry") ? r.transform.position + Vector3.up * 0.15f : r.qaTarget;
            void Follow()
            {
                var t = r.transform;
                var at = t.position + Vector3.up * 0.12f;
                var want = at + t.right * 0.55f + t.forward * 0.42f + Vector3.up * 0.34f;
                cam.SetPositionAndRotation(want, Quaternion.LookRotation(at - want));
            }
            float t0 = Time.realtimeSinceStartup, carryT = 0;
            Time.timeScale = 2;
            while (left.Values.Any(v => v > 0) && Time.realtimeSinceStartup - t0 < 480)
            {
                Follow();
                // walking or turning with a load (sustained for a moment)
                if (pending == null && r.carried.Count > 0)
                {
                    carryT += Time.unscaledDeltaTime;
                    if (carryT > 0.3f && left.GetValueOrDefault("carryturn") > 0 && Mathf.Abs(r.TurnRateQA) > 60f) pending = "carryturn";
                    else if (carryT > 0.6f && left.GetValueOrDefault("carrywalk") > 0 && r.SpeedQA > 0.3f) pending = "carrywalk";
                }
                else carryT = 0;
                if (pending != null)
                {
                    string what = pending;
                    left[what]--;
                    Time.timeScale = 1;
                    var spot = PickSpot(Focus(what), Hands(), B(what));
                    int reframes = 0;
                    for (int k = 0; k < 34; k++)
                    {
                        // keep the hands and the target in view: re-check every few frames, move if blocked
                        // a moving robot (carrying): the camera travels with it, re-chosen every few frames
                        if (spot == null || (what.StartsWith("carry") && k % 3 == 0) || (k % 6 == 0 && !Good(spot.Value, Hands(), B(what))))
                        {
                            var again = PickSpot(Focus(what), Hands(), B(what));
                            if (again.HasValue && spot.HasValue) reframes++;
                            spot = again ?? spot;
                        }
                        if (what == "stow")
                        {
                            // the throw: from the side and well back, so the flight over the head into the hold is in frame
                            var t = r.transform;
                            var fwd = t.forward; fwd.y = 0; fwd.Normalize();
                            var at = t.position + Vector3.up * 0.3f;
                            var from = at + Vector3.Cross(Vector3.up, fwd) * 1.4f + Vector3.up * 0.35f;
                            cam.SetPositionAndRotation(from, Quaternion.LookRotation(at - from));
                        }
                        else if (what == "climb")
                        {
                            // a climb: from the side and a step back from the face, so the face and the
                            // robot's feet on it are both seen (the obstacle checks would fail here)
                            var t = r.transform;
                            var fwd = t.forward; fwd.y = 0; fwd.Normalize();
                            var at = t.position + Vector3.up * 0.15f;
                            var side = Vector3.Cross(Vector3.up, fwd);
                            var from = new Vector3(at.x, 0, at.z) - fwd * 0.35f + side * 1.2f + Vector3.up * 0.75f;
                            cam.SetPositionAndRotation(from, Quaternion.LookRotation(at - from));
                        }
                        else if (spot.HasValue) cam.SetPositionAndRotation(spot.Value, Quaternion.LookRotation(Focus(what) - spot.Value));
                        else Follow();
                        yield return Capture(Path.Combine(dir, $"{what}_{k:00}.png"), 1);
                        yield return new WaitForSeconds(what.StartsWith("carry") ? 0.04f : 0.05f);
                    }
                    Debug.Log($"[robotwatch] captured {what} ({(spot.HasValue ? "clear view" : "fallback follow camera")}, {reframes} re-framings)");
                    pending = null;
                    Time.timeScale = 2;
                }
                yield return null;
            }
            Time.timeScale = 1;
            Debug.Log($"[robotwatch] done: pick gap {r.maxGripGap * 100:0.0} cm, store gap {r.maxStoreGap * 100:0.0} cm, stage gap {r.maxStageGap * 100:0.0} cm, deferred {r.deferred}, longest alignment {r.maxAlignTime:0.00} s; left {string.Join(", ", left.Where(kv => kv.Value > 0).Select(kv => kv.Key))}");
            Application.Quit();
        }

        static Bounds RendererBounds(Robot r, Vector3 pos)
        {
            var bounds = new Bounds(pos + Vector3.up * 0.3f, Vector3.zero);
            bool any = false;
            foreach (var rr in r.GetComponentsInChildren<Renderer>())
            {
                if (!any) { bounds = rr.bounds; any = true; }
                else bounds.Encapsulate(rr.bounds);
            }
            return bounds;
        }

        /// <summary>Model QA: the robot's render cost (renderers, draw calls = submeshes,
        /// distinct materials, triangles, vertices) as one log line.</summary>
        static void LogModelStats(Robot r)
        {
            int renderers = 0, subs = 0, tris = 0, verts = 0, bad = 0;
            var mats = new HashSet<Material>();
            var badIn = new HashSet<string>();
            foreach (var mr in r.GetComponentsInChildren<MeshRenderer>())
            {
                var mf = mr.GetComponent<MeshFilter>();
                if (mf == null || mf.sharedMesh == null) continue;
                renderers++;
                subs += mf.sharedMesh.subMeshCount;
                tris += (int)(mf.sharedMesh.triangles.Length / 3);
                verts += mf.sharedMesh.vertexCount;
                foreach (var nv in mf.sharedMesh.normals)
                    if (float.IsNaN(nv.x + nv.y + nv.z) || nv.sqrMagnitude < 0.5f) { bad++; badIn.Add(mf.sharedMesh.name + "/" + mr.sharedMaterial.name); }
                foreach (var m in mr.sharedMaterials) mats.Add(m);
            }
            float lowest = float.MaxValue;
            foreach (var rr in r.GetComponentsInChildren<Renderer>()) lowest = Mathf.Min(lowest, rr.bounds.min.y);
            Debug.Log($"[robotstats] {r.id}: renderers {renderers}, draw calls {subs}, materials {mats.Count}, triangles {tris}, vertices {verts}, bad normals {bad}{(bad > 0 ? " in " + string.Join(", ", badIn) : "")}, lowest point {(lowest - r.transform.position.y) * 1000:0.0} mm from the floor");
        }

        static bool PathEndsWith(Transform t, string path)
        {
            var names = path.Split('/');
            for (int i = names.Length - 1; i >= 0; i--, t = t.parent)
                if (t == null || t.name != names[i]) return false;
            return true;
        }

        /// <summary>Model QA (--robotview=retrobot): the robot fully assembled, frozen in its rest
        /// pose 1.2 m north of the spawn, facing south, alone in a photo studio: only the studio
        /// layer is drawn (no parts, shelves or walls), a flat light-grey backdrop,
        /// soft key / fill / rim lights, no fog and a neutral grade, so it can be compared with
        /// the concept sheets. --shot frames it itself (front, side, back, three-quarter); --cam is ignored.</summary>
        void RobotView(string id)
        {
            var r = robots.Find(x => x.id == id);
            if (r == null) return;
            var proj = workbench.P(id);
            if (proj.crafted) workbench.FillRecipe(proj);   // a crafted Hoverbot: its recipe from loose parts
            else workbench.ForceInstall(proj);
            r.SetOperational();
            r.enabled = false;
            if (r.arm != null) r.arm.enabled = false;
            // QA (--modules=head:retrobot,arm_l:unibot,arm_r:spiderbot,prop:hoverbot_3 or <slot>:empty): fit other robots'
            // modules, exchanged through the registry exactly as at the workbench
            if (args.TryGetValue("modules", out var mods))
            {
                foreach (var spec in mods.Split(','))
                {
                    var kv = spec.Split(':');
                    var slot = (ModuleSlot)System.Array.IndexOf(new[] { "head", "arm_l", "arm_r", "prop" }, kv[0]);
                    if (kv[1] == "empty") { modules.Remove(id, slot); continue; }
                    modules.EnsureBuilt(kv[1]);
                    modules.Swap(id, kv[1], slot, 0);
                }
                foreach (var e in modules.Audit()) Debug.LogError("[modules] " + e);
                r.ApplyModules();
            }
            var pos = Shared.Pos(layout.spawn.x, 0, layout.spawn.z - 1.2f);
            r.transform.SetPositionAndRotation(pos, Quaternion.LookRotation(Vector3.back));   // faces south, towards the camera
            r.PoseRest();
            // QA (--robotpose=walk|bend): a representative articulated pose instead of the rest pose
            if (args.TryGetValue("robotpose", out var rp))
            {
                var parts = rp.Split(':');   // name or name:t (see Robot.PoseQA)
                r.PoseQA(parts[0], parts.Length > 1 ? float.Parse(parts[1], System.Globalization.CultureInfo.InvariantCulture) : (parts[0] == "walk" ? 0.25f : 0));
            }
            LogModelStats(r);
            if (args.TryGetValue("studiolight", out var sl) && sl == "workshop")
            {
                // the same framing, but in the workshop as the game draws and lights it
                player.cam.fieldOfView = 30;
                _studio = (r.transform, RendererBounds(r, pos));
                _studioFwd = r.transform.forward;
                return;
            }
            foreach (var t in r.GetComponentsInChildren<Transform>(true)) t.gameObject.layer = StudioLayer;
            // the studio: nothing else is drawn or lit
            player.cam.cullingMask = 1 << StudioLayer;
            foreach (var l in FindObjectsByType<Light>()) l.enabled = false;
            RenderSettings.fog = false;
            RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Flat;
            RenderSettings.ambientLight = new Color(0.36f, 0.36f, 0.38f);
            // seamless backdrop: the camera clears to flat studio grey (no sky, no walls)
            player.cam.clearFlags = CameraClearFlags.SolidColor;
            player.cam.backgroundColor = new Color(0.9f, 0.9f, 0.89f);
            player.cam.fieldOfView = 22;   // a long lens: little perspective distortion, like the sheets
            _studio = (r.transform, RendererBounds(r, pos));
            _studioFwd = r.transform.forward;
            var key = new GameObject("StudioKey").AddComponent<Light>();
            key.type = LightType.Directional;
            key.color = new Color(1f, 0.96f, 0.9f);
            key.intensity = 1.6f;
            key.shadows = LightShadows.Soft;
            key.cullingMask = 1 << StudioLayer;
            key.transform.rotation = Quaternion.Euler(35, 30, 0);   // from the camera's front left, above (the robot faces -z)
            foreach (var (off, col, inten) in new[] { (new Vector3(0.6f, 0.3f, -0.5f), new Color(0.82f, 0.88f, 1f), 0.5f), (new Vector3(-0.2f, 0.7f, 0.6f), new Color(1f, 0.94f, 0.84f), 0.9f) })
            {
                var lg = new GameObject("StudioLight").AddComponent<Light>();
                lg.type = LightType.Point; lg.range = 2.5f; lg.color = col; lg.intensity = inten;
                lg.cullingMask = 1 << StudioLayer;
                lg.transform.position = pos + off;
            }
            // neutral grade: judge the colours as they are
            var vgo = new GameObject("StudioGrade");
            var vol = vgo.AddComponent<Volume>();
            vol.isGlobal = true;
            vol.priority = 100;
            var prof = ScriptableObject.CreateInstance<VolumeProfile>();
            prof.Add<FilmGrain>(true).intensity.Override(0f);
            prof.Add<SplitToning>(true).balance.Override(0f);
            var st = prof.components.OfType<SplitToning>().First();
            st.shadows.Override(Color.gray); st.highlights.Override(Color.gray);
            prof.Add<Vignette>(true).intensity.Override(0.12f);
            var ca = prof.Add<ColorAdjustments>(true);
            ca.saturation.Override(0f); ca.contrast.Override(6f); ca.postExposure.Override(0.6f);
            prof.Add<WhiteBalance>(true).temperature.Override(0f);
            vol.profile = prof;
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
            foreach (var s in sockets.Values) { s.occupant = null; s.SetLamp(); }
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

        /// <summary>QA (--layoutdump=file.json): floor plan in Godot XZ — room, storage unit
        /// footprints, floor blockers, keep-clear areas, spawn and unsorted part positions.</summary>
        void DumpLayout(string path)
        {
            List<object> R(Rect r) => new() { (double)r.xMin, (double)r.yMin, (double)r.width, (double)r.height };
            var d = new Dictionary<string, object>
            {
                ["room"] = new List<object> { (double)layout.roomMin.x, (double)layout.roomMin.z, (double)layout.roomMax.x, (double)layout.roomMax.z },
                ["spawn"] = new List<object> { (double)layout.spawn.x, (double)layout.spawn.z },
                ["units"] = layout.units.ToDictionary(kv => kv.Key, kv => (object)kv.Value),
                ["blockers"] = layout.floorBlockers.Select(r => (object)R(r)).ToList(),
                ["keep_clear"] = layout.keepClear.Select(r => (object)R(r)).ToList(),
                ["items"] = world.instances.Where(i => i.state == ItemState.InWorld).Select(i => (object)new List<object> { (double)i.Position.x, (double)(-i.Position.z), (double)i.Position.y, (double)(db.Get(i.defId).IsRobotPart ? 1 : 0) }).ToList(),
            };
            File.WriteAllText(path, Json.Write(d));
        }

        /// <summary>Leaving through the workshop door ends the game (after the prompt).</summary>
        public void LeaveWorkshop(bool save)
        {
            if (save) SaveGame("quick");
            WriteTelemetry();
            if (Automated) return;   // tests never quit from here
            Application.Quit();
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
            return robots.Where(r => r.Active && r.Mobile).OrderBy(r => (r.transform.position - p).sqrMagnitude).FirstOrDefault();
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
                var r = robots.Where(x => x.Active && x.Mobile && !x.handoverPending && x.Battery >= 0.2f)
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
                    var r = robots.Where(x => x.Active && x.Mobile && !x.zoneCenter.HasValue).OrderBy(x => (x.transform.position - p).sqrMagnitude).FirstOrDefault();
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
        /// <summary>Durable gameplay counters (GameEvents), saved as "game_stats".</summary>
        public GameStats stats;
        public double playTime;
        string _activity = "collecting";
        string _telemetryFile;
        Dictionary<string, object> _progressionCfg;
        public const int BaseInventory = 10;

        /// <summary>The chassis type of an actor ("robot:&lt;owner&gt;" as robots deposit), or "".</summary>
        public string RobotTypeOf(string actor) => actor.StartsWith("robot") ? robots.FirstOrDefault(r => r.Owner == actor)?.type ?? "" : "";

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
            // gameplay counters: from the save, or (a save from before they existed) seeded from what it shows
            stats = save == null ? new GameStats()
                : save.ContainsKey("game_stats") ? GameStats.FromDict(Json.Obj(save["game_stats"]))
                : GameStats.SeedFromLegacy(storage.contents.Values.Sum(l => l.Count), progression.level);
            GameEvents.stats = stats;
            ApplySkills();
            progression.LeveledUp += lv => workbench.CheckBlueprints(true);
            progression.RobotUpgraded += type => { foreach (var r in robots) if (r.type == type) r.ApplyUpgrades(); };
            progression.RobotUpgraded += type => GameEvents.Raise(GameEventKind.RobotUpgraded, "player", type);
            progression.LeveledUp += lv => GameEvents.Raise(GameEventKind.LevelReached, "player", count: lv);
            progress.Discovered += id => GameEvents.Raise(GameEventKind.PartDiscovered, "player", "", id, db.Get(id).CategoryName);
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
                var def = world.DefOf(id);
                GameEvents.Raise(GameEventKind.PartSorted, storage.lastDepositor, RobotTypeOf(storage.lastDepositor), def.id, def.CategoryName, box);
                progression.OnSorted(id, robot);
                telemetry.Sorted(robot, playTime, progress.sortedTotal);
            };
            progress.DefinitionCompleted += id => progression.OnDefinitionCompleted(id, world.TotalOf(id));
            progress.DefinitionHalf += id => progression.OnDefinitionHalf(id, world.TotalOf(id));
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
            inventory.stacking = progression.Has("inventory_stacking");
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
            var bad = UnreachableParts();
            if (bad.Count > 0) Debug.LogWarning($"[integrity] {bad.Count} unreachable part(s) at save: " + string.Join("; ", bad.Take(8).Select(b => $"{db.Get(world.instances[b.id].defId).displayName}: {b.why}")));
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
            itemCount = count == Json.Int(db.config, "total_items", 4096) || SaveSystem.NeedsMigration(d, db) ? -1 : count;
            if (StartWorld(Json.Int(d, "seed"), d))
            {
                SetState("play");
                hud.Toast("Loaded " + slot + (migrationReport != "" ? $"  ·  converted to the current parts catalog: {migrationReport}" : ""), migrationReport != "");
                var bad = UnreachableParts();
                if (bad.Count > 0) Debug.LogWarning($"[integrity] {bad.Count} unreachable part(s) after load (Esc: Recover unreachable parts)");
            }
        }

        // ---------------------------------------------------------------- storage

        void BuildStorage()
        {
            sockets.Clear();
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
                if (su.type == "drawer_cabinet") SortingArm.BuildSockets(this, su, id);
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
            MenuBackdrop();
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
                else if (state == "paused" || state == "inventory" || state == "workbench" || state == "exit") { if (!hud.BackFromSubScreen()) SetState("play"); }
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

        Volume _menuBlur;
        float _menuT;

        /// <summary>Main menu backdrop: the camera pans slowly across the workshop from the
        /// spawn (no travel, so it never clips furniture) with the background softly blurred.</summary>
        void MenuBackdrop()
        {
            if (_menuBlur == null)
            {
                var go = new GameObject("MenuBlur");
                go.transform.SetParent(transform, false);
                _menuBlur = go.AddComponent<Volume>();
                _menuBlur.isGlobal = true;
                _menuBlur.priority = 20;
                var prof = ScriptableObject.CreateInstance<VolumeProfile>();
                var dof = prof.Add<DepthOfField>(true);
                dof.mode.Override(DepthOfFieldMode.Gaussian);
                dof.gaussianStart.Override(1.5f);
                dof.gaussianEnd.Override(9f);
                dof.gaussianMaxRadius.Override(1.2f);
                _menuBlur.profile = prof;
            }
            bool menu = state == "menu" && !Automated && layout != null;
            _menuBlur.weight = Mathf.MoveTowards(_menuBlur.weight, menu ? 1 : 0, Time.unscaledDeltaTime * 2);
            if (!menu) return;
            _menuT += Time.unscaledDeltaTime;
            player.Teleport(Shared.Pos(layout.spawn.x, 0, layout.spawn.z));
            player.SetView((Mathf.Sin(_menuT * 0.06f) * 32f) * Mathf.Deg2Rad, (-5f + Mathf.Sin(_menuT * 0.043f) * 3f) * Mathf.Deg2Rad);
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

        IEnumerator MenuShot(string path)
        {
            for (float t = 0; t < 6f; t += Time.unscaledDeltaTime) yield return null;
            yield return Capture(path, 2);
            Application.Quit();
        }

        public IEnumerator Capture(string path, int frames = 20)
        {
            for (int i = 0; i < frames; i++) yield return null;
            yield return new WaitForEndOfFrame();
            if (args.TryGetValue("res", out var res)) { CaptureStill(path, res); yield break; }
            var tex = ScreenCapture.CaptureScreenshotAsTexture();
            File.WriteAllBytes(path, tex.EncodeToPNG());
            Destroy(tex);
        }

        /// <summary>Key art / posters (--res=WxH [--ss=1|2|4]): renders the current camera off-screen at any size,
        /// independent of the window, with still-frame quality (8K shadow map, MSAA); --ss renders it that many
        /// times larger and box-filters it down for clean edges and fine detail.</summary>
        void CaptureStill(string path, string res)
        {
            var wh = res.ToLowerInvariant().Split('x');
            int w = int.Parse(wh[0]), h = int.Parse(wh[1]);
            int ss = args.TryGetValue("ss", out var s) ? Mathf.ClosestPowerOfTwo(Mathf.Clamp(int.Parse(s), 1, 4)) : 1;
            if (GraphicsSettings.currentRenderPipeline is UniversalRenderPipelineAsset urp)
            {
                urp.mainLightShadowmapResolution = 8192;
                urp.renderScale = 1;
                urp.msaaSampleCount = ss > 1 ? 1 : 8;
            }
            var cam = player.cam;
            // a half-float target: URP renders its intermediate buffers in the target's format, and an 8-bit one
            // would mean LDR rendering (no bloom on the emissive eyes and lamps)
            var rt = new RenderTexture(w * ss, h * ss, 24, RenderTextureFormat.ARGBHalf, RenderTextureReadWrite.Linear) { antiAliasing = ss > 1 ? 1 : w * h > 9000000 ? 2 : 8 };   // 8K: MSAA 2 keeps it in GPU memory
            var prev = cam.targetTexture;
            cam.targetTexture = rt;
            cam.ResetAspect();
            cam.Render();
            cam.targetTexture = prev;
            cam.ResetAspect();
            var cur = rt;
            while (cur.width > w)   // exact 2x2 box filter per halving (bilinear taps at texel corners)
            {
                var half = new RenderTexture(cur.width / 2, cur.height / 2, 0, RenderTextureFormat.ARGBHalf, RenderTextureReadWrite.Linear);
                Graphics.Blit(cur, half);
                if (cur != rt) cur.Release();
                cur = half;
            }
            var ldr = new RenderTexture(w, h, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);   // linear -> sRGB bytes
            Graphics.Blit(cur, ldr);
            if (cur != rt) cur.Release();
            cur = ldr;
            var was = RenderTexture.active;
            RenderTexture.active = cur;
            var tex = new Texture2D(w, h, TextureFormat.RGB24, false);
            tex.ReadPixels(new Rect(0, 0, w, h), 0, 0);
            tex.Apply();
            RenderTexture.active = was;
            File.WriteAllBytes(path, tex.EncodeToPNG());
            Destroy(tex);
            if (cur != rt) cur.Release();
            rt.Release();
            Debug.Log($"[still] {path}: {w}x{h}, supersampled x{ss}");
        }

        IEnumerator TakeShots()
        {
            player.enabled = false;
            hud.SetHudVisible(args.ContainsKey("hud"));
            if (args.TryGetValue("toast", out var toast)) foreach (var line in toast.Split('|')) hud.Toast(line, true);   // QA: --hud --toast=a|b
            var outs = args["shot"].Split(';');
            var cams = (args.TryGetValue("cam", out var c) ? c : "0.6,1.65,3.6,0,-18").Split(';');
            yield return null;
            builder.RefreshProbe();
            for (int i = 0; i < outs.Length; i++)
            {
                if (_studio != null && args.TryGetValue("robotseq", out var seq))
                {
                    // QA animation strip: --robotseq=name poses frame i of n (= the number of shots) from one camera (--seqcam)
                    var qr = _studio.Value.robot.GetComponent<Robot>();
                    qr.PoseQA(seq, i / (float)outs.Length);
                    _studio = (_studio.Value.robot, RendererBounds(qr, qr.transform.position));   // frame the pose as it is (raised arms, telescopes)
                    StudioCamera(args.TryGetValue("seqcam", out var sc) ? int.Parse(sc) : 3);
                }
                else if (_studio != null) StudioCamera(args.TryGetValue("studiocam", out var scam) ? int.Parse(scam) : i);   // --robotview frames the robot itself (--studiocam: one fixed view)
                else PlaceCamera(cams[Mathf.Min(i, cams.Length - 1)]);
                yield return Capture(outs[i]);
                Debug.Log("saved " + outs[i]);
            }
            Application.Quit();
        }
    }
}
