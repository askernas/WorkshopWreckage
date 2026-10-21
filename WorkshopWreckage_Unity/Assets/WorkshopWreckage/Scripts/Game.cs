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
    /// Command line: --shot=a.png;b.png --cam=x,y,z,yaw,pitch;...  --items=256|1024|4096
    /// --presort=0.35  --play  --hud  --bench=out.json  --autoplay=out_dir</summary>
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
        public RepairBay repairBay;
        public readonly Dictionary<string, StorageUnit> units = new();
        public int itemCount = 4096;
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

            db = ItemDatabase.LoadDefault();
            layout = WorkshopLayout.Load();
            if (db.IsValid) db.Validate();
            hud = new GameObject("UI").AddComponent<Hud>();
            hud.transform.SetParent(transform, false);
            hud.Init(this);
            if (!db.IsValid) { hud.ShowFatal("The item data files are invalid:\n\n" + string.Join("\n", db.errors.Take(25))); return; }
            _assignments = db.Assignments();

            builder = new GameObject("Workshop").AddComponent<WorkshopBuilder>();
            builder.transform.SetParent(transform, false);
            builder.Build(layout);
            BuildStorage();
            repairBay = new GameObject("RepairBay").AddComponent<RepairBay>();
            repairBay.transform.SetParent(transform, false);
            repairBay.transform.position = Shared.Pos(layout.repairBayPos.x, layout.repairBayPos.y, layout.repairBayPos.z);
            repairBay.transform.rotation = Shared.YRot(layout.repairBayRot);

            player = new GameObject("Player").AddComponent<PlayerController>();
            player.transform.SetParent(transform, false);
            player.Init();
            player.stepped = () => Sfx.Play("step", null, -16, 0.15f);
            held = new GameObject("HeldView").AddComponent<HeldView>();
            held.Init(player.cam);
            interaction = new GameObject("Interaction").AddComponent<Interaction>();
            interaction.transform.SetParent(transform, false);
            interaction.Init(this);
            interaction.TargetChanged += hud.OnTarget;
            interaction.Feedback += hud.OnFeedback;
            interaction.InspectChanged += hud.ShowInspect;
            repairBay.Init(this);

            if (args.TryGetValue("items", out var n)) itemCount = int.Parse(n);
            if (!StartWorld(Json.Int(db.config, "default_seed", 1912), null)) return;
            startupMs = Time.realtimeSinceStartupAsDouble * 1000;   // process start to interactive
            StartCoroutine(ProbeLater());
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

        public bool StartWorld(int seed, Dictionary<string, object> save)
        {
            var errors = new List<string>();
            var w = ItemWorld.LoadReference(db, seed, itemCount, layout, errors);
            if (w == null || errors.Count > 0) { hud.ShowFatal("World could not be loaded:\n\n" + string.Join("\n", errors.Take(20))); return false; }
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
            repairBay.Refresh();
            foreach (var u in units.Values) { u.labelProvider = LabelFor; u.RefreshLabels(); }
            interaction.ResetState();
            progress.Milestone += (key, text) => { hud.Toast(text, key.StartsWith("zone") || key == "first_pickup"); Sfx.Play("milestone", null, -6); };
            progress.DefinitionCompleted += id => { hud.Toast($"Set complete: {db.Get(id).displayName}  (all {world.TotalOf(id)})", true); Sfx.Play("complete", null, -4); };
            progress.Discovered += id => { _labelsDirty.Add(db.Get(id).storageBox); Sfx.Play("discover", null, -12); };
            inventory.Changed += () => { hud.MarkHotbarDirty(); var hid = inventory.HeldId(); held.ShowHeld(hid >= 0 ? world.DefOf(hid) : null); };
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

        public void SaveGame(string slot)
        {
            if (SaveSystem.Write(slot, SaveSystem.Serialize(this)))
            {
                Sfx.Play("save", null, -6);
                hud.Toast($"Game saved ({slot})");
            }
        }

        public void LoadGame(string slot)
        {
            var d = SaveSystem.Read(slot);
            var problem = SaveSystem.Check(d, db);
            if (problem != "") { hud.OnFeedback(problem, false); hud.SetStatus(problem); return; }
            itemCount = Json.Int(d, "item_count", 4096);
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
            repairBay.Refresh();
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
            if (world == null || Automated) return;
            var kb = Keyboard.current;
            if (kb == null) return;
            if (kb.escapeKey.wasPressedThisFrame)
            {
                if (state == "play") { if (interaction.inspecting) interaction.EndInspect(); else SetState("paused"); }
                else if (state == "paused" || state == "inventory") { if (!hud.BackFromSubScreen()) SetState("play"); }
                else if (state == "menu") hud.BackFromSubScreen();
            }
            else if (kb.tabKey.wasPressedThisFrame && (state == "play" || state == "inventory")) SetState(state == "play" ? "inventory" : "play");
            else if (kb.f5Key.wasPressedThisFrame && state == "play") SaveGame("quick");
            else if (kb.f9Key.wasPressedThisFrame && (state == "play" || state == "paused")) LoadGame("quick");
            if (state == "play" && Cursor.lockState != CursorLockMode.Locked && Mouse.current.leftButton.wasPressedThisFrame) Cursor.lockState = CursorLockMode.Locked;
        }

        public void SetState(string s)
        {
            state = s;
            bool playing = s == "play";
            player.inputEnabled = playing && !interaction.inspecting;
            player.lookEnabled = playing && !interaction.inspecting;
            interaction.enabledInput = playing;
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
