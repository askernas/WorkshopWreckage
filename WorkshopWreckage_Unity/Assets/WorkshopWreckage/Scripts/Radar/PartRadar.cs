using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.InputSystem;

namespace WorkshopWreckage
{
    /// <summary>The Part Radar (resources/part radar/Workshop_Wreckage_Part_Radar_v1.2.md): a two-handed
    /// receiver the player builds at the Robot Workbench from one of each of eight salvaged component
    /// types. Out (Y), a pulse (LMB) spends one charge of the loaded battery and locks onto one loose
    /// robot component for three seconds: the amber LED lights while the radar points within ±5° of it,
    /// and blinks within 2 m. The wheel picks what it looks for (between pulses, free), L swaps the
    /// battery. Batteries never recharge; their charges are seeded per world and saved.
    /// Parts and batteries are tool items (ItemDefinition.IsToolItem): no robot's component, no
    /// ordinary part, no level progress.</summary>
    public class PartRadar : MonoBehaviour
    {
        public const string Owner = "tool:part_radar";
        public const string Station = "RS-part_radar";

        /// <summary>shared/data/tools/part_radar.json (spec §7), validated; derived totals, never stored.</summary>
        public class Config
        {
            public float duration = 3f, tolerance = 5f, closeRange = 2f, blinkHz = 4f, swapTime = 1.75f;
            public int maxCharges = 4, perPulse = 1;
            public readonly List<(int charges, int count)> distribution = new();
            public readonly Dictionary<string, int> required = new(), spawnCounts = new();
            public readonly List<string> errors = new();
            public int Batteries => distribution.Sum(d => d.count);
            public int Pulses => distribution.Sum(d => d.charges * d.count) / Mathf.Max(1, perPulse);

            public static Config Load()
            {
                var c = new Config();
                try
                {
                    var o = Shared.ReadObject(Shared.DataPath("tools/part_radar.json"));
                    var a = Json.Obj(o["assembly"]);
                    var rc = Json.Obj(a["requiredComponents"]);
                    foreach (var k in rc.Keys) c.required[k] = Json.Int(rc, k);
                    var sc = Json.Obj(a["componentSpawnCounts"]);
                    foreach (var k in sc.Keys) c.spawnCounts[k] = Json.Int(sc, k);
                    var s = Json.Obj(o["scanning"]);
                    c.duration = (float)Json.Num(s, "durationSeconds", 3);
                    c.tolerance = (float)Json.Num(s, "horizontalToleranceDegrees", 5);
                    c.closeRange = (float)Json.Num(s, "closeRangeMeters", 2);
                    c.blinkHz = (float)Json.Num(s, "blinkFrequencyHz", 4);
                    var b = Json.Obj(o["batterySystem"]);
                    c.maxCharges = Json.Int(b, "maxChargesPerBattery", 4);
                    c.perPulse = Json.Int(b, "chargesPerActivation", 1);
                    c.swapTime = (float)Json.Num(b, "replacementDurationSeconds", 1.75);
                    foreach (var x in Json.Arr(b["spawnDistribution"]))
                    {
                        var d = Json.Obj(x);
                        c.distribution.Add((Json.Int(d, "charges"), Json.Int(d, "count")));
                    }
                }
                catch (System.Exception e) { c.errors.Add("could not read it: " + e.Message); }
                foreach (var k in PartRadarModel.Parts)
                    if (c.required.GetValueOrDefault(k) != 1) c.errors.Add($"requiredComponents.{k} must be 1");
                foreach (var k in c.required.Keys)
                    if (c.spawnCounts.GetValueOrDefault(k) < 1) c.errors.Add($"componentSpawnCounts.{k} must be at least 1");
                if (c.distribution.Select(d => d.charges).Distinct().Count() != c.distribution.Count) c.errors.Add("spawnDistribution: charge counts must be unique");
                foreach (var d in c.distribution)
                    if (d.charges < 1 || d.charges > c.maxCharges || d.count < 0) c.errors.Add($"spawnDistribution: {d.count} × {d.charges} is out of range (1..{c.maxCharges} charges)");
                if (c.perPulse < 1) c.errors.Add("chargesPerActivation must be at least 1");
                if (c.Pulses < 1) c.errors.Add("the battery distribution gives no usable pulse");
                if (c.duration <= 0 || c.tolerance <= 0 || c.closeRange <= 0 || c.blinkHz <= 0) c.errors.Add("scanning values must be positive");
                return c;
            }
        }

        Game g;
        public Config cfg;
        /// <summary>Built: one of each component type is in the radar (Installed, owner Owner).</summary>
        public bool assembled;
        /// <summary>In the player's hands (Y).</summary>
        public bool isOut;
        /// <summary>The loaded battery's instance id (Installed, owner Owner), or -1.</summary>
        public int battery = -1;
        /// <summary>Remaining charges of every battery in this world (instance id -> charges): seeded once, then only spent.</summary>
        public readonly Dictionary<int, int> charges = new();
        /// <summary>What it looks for: "any", "head", "arm", "propeller", "type:&lt;robot type&gt;" or "part:&lt;definition id&gt;".</summary>
        public string target = "any";

        int _locked = -1;
        float _pulseT, _swapT, _press, _dial, _dialGoal, _raise;
        int _swapIn = -1;
        PartRadarModel _view;
        bool _ledOn;
        float _flash;   // 1 = a bright flash, fading fast (each time the LED comes on, and as a pulse goes out)
        const float LedLight = 6f, FlashLight = 30f;
        List<int> _parts;
        ItemWorld _partsWorld;

        public bool Pulsing => _pulseT > 0;
        public bool Swapping => _swapT > 0;
        /// <summary>QA: the part a pulse is locked on (-1 none) and the LED light's intensity now.</summary>
        public int Locked => _locked;
        public float LightNow => _view != null && _view.ledLight != null && _view.ledLight.enabled ? _view.ledLight.intensity : 0;

        public void Init(Game game)
        {
            g = game;
            cfg = Config.Load();
            foreach (var e in cfg.errors) Debug.LogError("[radar] part_radar.json: " + e);
        }

        // ---------------------------------------------------------------- world state

        public int Charges(int id) => charges.TryGetValue(id, out var n) ? n : 0;

        /// <summary>A new world or a load: the radar as the save has it (nothing assembled in a new world).
        /// Batteries the save doesn't know get their seeded starting charge; a pulse in progress is gone, unrefunded.</summary>
        public void Load(Dictionary<string, object> save)
        {
            Put(false);
            _locked = -1;
            _pulseT = _swapT = 0;
            _swapIn = -1;
            charges.Clear();
            var d = save != null ? Json.Obj(save.GetValueOrDefault("radar")) : null;
            assembled = d != null && Json.Bool(d, "assembled");
            battery = d != null ? Json.Int(d, "battery", -1) : -1;
            target = d != null ? Json.Str(d, "target", "any") : "any";
            var saved = d != null ? Json.Obj(d.GetValueOrDefault("charges")) : null;
            if (saved != null)
                foreach (var k in saved.Keys)
                    if (int.TryParse(k, out int id) && id < g.world.Count && g.world.DefOf(id).IsRadarBattery)
                        charges[id] = Mathf.Clamp(Json.Int(saved, k), 0, cfg.maxCharges);
            SeedCharges();
            if (battery >= 0 && (battery >= g.world.Count || !g.world.DefOf(battery).IsRadarBattery || g.world.instances[battery].owner != Owner)) battery = -1;
            if (assembled && Built().Count < PartRadarModel.Parts.Length) assembled = false;   // the save lost its parts (catalogue change)
        }

        public Dictionary<string, object> ToDict() => new()
        {
            ["assembled"] = assembled, ["battery"] = (double)battery, ["target"] = target,
            ["charges"] = charges.ToDictionary(kv => kv.Key.ToString(), kv => (object)(double)kv.Value),
        };

        /// <summary>Spec §8: the configured charge classes, shuffled by the world seed over the batteries
        /// in id order, so the distribution is reproducible but where the full ones lie is not.</summary>
        void SeedCharges()
        {
            var ids = g.world.instances.Where(i => g.world.DefOf(i.id).IsRadarBattery).Select(i => i.id).OrderBy(i => i).ToList();
            var pool = cfg.distribution.SelectMany(d => Enumerable.Repeat(d.charges, d.count)).ToList();
            if (pool.Count != ids.Count) Debug.LogError($"[radar] {ids.Count} batteries in the world, the distribution has {pool.Count}");
            if (pool.Count == 0) pool.Add(1);
            var rng = new System.Random((int)(ItemDatabase.Fnv("part-radar-batteries:" + g.world.seed) & 0x7fffffff));
            for (int k = pool.Count - 1; k > 0; k--) { int j = rng.Next(k + 1); (pool[k], pool[j]) = (pool[j], pool[k]); }
            for (int k = 0; k < ids.Count; k++)
                if (!charges.ContainsKey(ids[k])) charges[ids[k]] = pool[k % pool.Count];
        }

        /// <summary>The component instances built into the radar.</summary>
        List<int> Built() => g.world.instances.Where(i => i.state == ItemState.Installed && i.owner == Owner && !g.world.DefOf(i.id).IsRadarBattery).Select(i => i.id).ToList();

        /// <summary>Component types delivered so far (in the bin or built in).</summary>
        public HashSet<string> TypesDelivered()
        {
            var o = Built().Select(id => g.world.DefOf(id).ToolType).ToHashSet();
            foreach (var id in g.storage.Stored(Station)) if (!g.world.DefOf(id).IsRadarBattery) o.Add(g.world.DefOf(id).ToolType);
            return o;
        }

        // ---------------------------------------------------------------- the workbench

        /// <summary>The Part Radar plans (skill "part_radar_plans", 2 skill points, playtest 2026-10-26): without
        /// them its parts only collect in the robot parts pool.</summary>
        public bool HasPlans => g.progression != null && g.progression.Has("part_radar_plans");

        /// <summary>Names of the parts not yet in the pool.</summary>
        public List<string> MissingNames()
        {
            var have = TypesDelivered();
            return PartRadarModel.Parts.Where(p => !have.Contains(p)).Select(Name).ToList();
        }

        /// <summary>Why it can't be built now ("" = it can).</summary>
        public string WhyNotBuilt()
        {
            if (assembled) return "The Part Radar is already built";
            var missing = MissingNames();
            if (missing.Count > 0) return $"The Part Radar still needs: {string.Join(", ", missing)}";
            return HasPlans ? "" : "Buy the Part Radar plans first (2 skill points, [K])";
        }

        /// <summary>Builds the radar if the plans are bought and one of each part is in the pool (after buying the plans).</summary>
        public bool TryAssemble()
        {
            if (assembled || !HasPlans || TypesDelivered().Count < PartRadarModel.Parts.Length) return false;
            Assemble();
            g.hud.Toast("PART RADAR ASSEMBLED  ·  [Y] take it out", true);
            return true;
        }

        /// <summary>A tool item handed over at the Robot Workbench: components go into the robot parts pool
        /// (the radar is built as soon as one of each type is there and the plans are bought; spares stay in
        /// the pool for good), a spent battery is disposed of there; a battery with charge left is kept.</summary>
        public (bool ok, string text) Deliver(int id)
        {
            var def = g.world.DefOf(id);
            if (def.IsRadarBattery && Charges(id) > 0) return (false, $"That battery still has {Charges(id)} of {cfg.maxCharges} charges — keep it for the Part Radar");
            if (g.storage.Check(Station, id) != StorageSystem.Result.Ok) return (false, "The robot parts pool has no room for it");
            g.inventory.Remove(id);
            g.storage.Deposit(Station, id);
            Sfx.Play("drop_drawer", g.workbench.transform.position, -4);
            g.workbench.Refresh();
            if (def.IsRadarBattery) return (true, "Spent radar battery disposed of at the workbench");
            if (assembled) return (true, $"Spare radar part: {def.displayName} — kept in the robot parts pool");
            var have = TypesDelivered();
            string plans = HasPlans ? "" : "  ·  buy the Part Radar plans (2 skill points, [K]) to build it";
            if (have.Count < PartRadarModel.Parts.Length)
            {
                var missing = PartRadarModel.Parts.Where(p => !have.Contains(p)).Select(Name);
                return (true, $"Part Radar: {def.shape} → robot parts pool  ({have.Count}/{PartRadarModel.Parts.Length})  ·  still missing: {string.Join(", ", missing)}{plans}");
            }
            if (!HasPlans) return (true, $"Part Radar: all {PartRadarModel.Parts.Length} parts are in the robot parts pool{plans}");
            Assemble();
            return (true, "PART RADAR ASSEMBLED  ·  [Y] take it out");
        }

        static string Name(string type) => type switch
        {
            PartRadarModel.Housing => "housing", PartRadarModel.HandleL => "left handle", PartRadarModel.HandleR => "right handle",
            PartRadarModel.Button => "pulse button", PartRadarModel.Dial => "selector dial", PartRadarModel.Led => "LED module",
            PartRadarModel.AntennaL => "left antenna", PartRadarModel.AntennaR => "right antenna", _ => type,
        };

        /// <summary>One delivered instance of each type (the lowest id) goes into the radar for good.</summary>
        void Assemble()
        {
            foreach (var type in PartRadarModel.Parts)
            {
                int id = g.storage.Stored(Station).Where(i => g.world.DefOf(i).ToolType == type).DefaultIfEmpty(-1).Min();
                if (id < 0) return;
                g.storage.Remove(Station, id);
                g.world.SetState(id, ItemState.Installed, Owner);
            }
            assembled = true;
            Sfx.Play("complete", g.workbench.transform.position, -4);
            g.hud.ShowTutorial("PART RADAR BUILT!",
                "[Y] take it out or put it away.  [LMB] send a pulse: it spends one battery charge and listens for one robot part for three seconds — the amber light shines while you face it, and blinks when you are within two metres.  [Wheel] choose what it looks for.  [L] load or swap a battery (batteries never recharge).");
            Debug.Log("[radar] assembled");
        }

        // ---------------------------------------------------------------- targets

        List<int> Parts()
        {
            if (_partsWorld != g.world) { _partsWorld = g.world; _parts = g.db.RobotPartIds().SelectMany(d => g.world.IdsOf(d)).ToList(); }
            return _parts;
        }

        /// <summary>A robot component still loose in the workshop (not carried, delivered or installed).</summary>
        bool Eligible(int id)
        {
            var i = g.world.instances[id];
            return i.state == ItemState.InWorld && g.world.DefOf(id).IsRobotComponent && !g.robotReserved.Contains(id) && !g.interaction.busy.Contains(id);
        }

        /// <summary>Category membership from the registry: the workbench's head/arm slots, the robot type, the definition.</summary>
        bool Matches(string key, int id)
        {
            var wb = g.workbench;
            if (key == "any") return true;
            if (key == "head") return wb.MainSlotOf(id) == ModuleSlot.Head;
            if (key == "arm") return wb.MainSlotOf(id) is ModuleSlot s && (s == ModuleSlot.ArmL || s == ModuleSlot.ArmR);
            if (key == "propeller") return g.world.DefOf(id).shape.StartsWith("Propeller");
            if (key.StartsWith("type:")) return wb.TypeOf(wb.OriginOf(id)) == key.Substring(5);
            if (key.StartsWith("part:")) return g.world.instances[id].defId.ToString() == key.Substring(5);
            if (key.StartsWith("pkey:") && key.Substring(5).Split('|') is var pk && pk.Length == 2)   // any copy of a pooled kit's part
                return wb.TypeOf(wb.OriginOf(id)) == pk[0] && Robot.PartKey(g.world.DefOf(id)) == pk[1];
            return false;
        }

        /// <summary>What the selector offers now: the generic kinds and the robot types with a part still
        /// loose, then each loose part of the robot selected at the workbench.</summary>
        public List<(string key, string label)> Targets()
        {
            var loose = Parts().Where(Eligible).ToList();
            var o = new List<(string, string)>();
            void Kind(string key, string label) { if (loose.Any(id => Matches(key, id))) o.Add((key, label)); }
            Kind("any", "Any robot part");
            Kind("head", "Any robot head");
            Kind("arm", "Any robot arm");
            Kind("propeller", "Any propeller");
            foreach (var t in RobotWorkbench.Types) Kind("type:" + t, $"Any {g.workbench.P(t)?.name ?? t} part");
            var p = g.workbench.P(g.workbench.selected);
            if (RobotWorkbench.IsPooled(p))
                foreach (var key in g.workbench.PoolIds(p).Where(Eligible).Select(id => Robot.PartKey(g.world.DefOf(id))).Distinct())
                    o.Add(($"pkey:{p.type}|{key}", $"Any {g.workbench.P(p.type)?.name ?? p.type} {key}"));
            else if (p != null)
                foreach (var id in g.workbench.PartIds(p).Where(Eligible))
                    o.Add(("part:" + g.world.instances[id].defId, g.world.DefOf(id).displayName));
            if (o.Count == 0) o.Add(("any", "Any robot part"));
            return o;
        }

        public string TargetLabel()
        {
            if (target == "any") return "Any robot part";
            var t = Targets().FirstOrDefault(x => x.key == target);
            return t.label ?? "—";
        }

        void CycleTarget(int dir)
        {
            var list = Targets();
            int k = list.FindIndex(x => x.key == target);
            k = k < 0 ? 0 : ((k + dir) % list.Count + list.Count) % list.Count;
            target = list[k].key;
            _dialGoal = -35f + 40f * (k % 6);
            Sfx.Play("ui_click", null, -10, 0.15f);
            g.hud.OnFeedback("Part Radar target: " + list[k].label, true);
        }

        /// <summary>Spec §5: a specific part is that instance; a kind is its nearest loose instance now.</summary>
        int Resolve()
        {
            var me = g.player.transform.position;
            int best = -1;
            float bd = float.MaxValue;
            foreach (var id in Parts())
            {
                if (!Eligible(id) || !Matches(target, id)) continue;
                float d = (g.world.instances[id].Position - me).sqrMagnitude;
                if (d < bd) { bd = d; best = id; }
            }
            return best;
        }

        // ---------------------------------------------------------------- in hand

        /// <summary>Takes the radar out (or puts it away): both hands on it, no parts in or out meanwhile.</summary>
        public void Put(bool on)
        {
            if (on == isOut) return;
            isOut = on;
            if (on)
            {
                var go = new GameObject("PartRadarView");
                _view = go.AddComponent<PartRadarModel>();
                _view.Init();
                _view.Build(new HashSet<string>(PartRadarModel.Parts.Append("")));
                foreach (var c in go.GetComponentsInChildren<Collider>()) Destroy(c);
                foreach (var r in go.GetComponentsInChildren<Renderer>()) r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                _view.AddLedLight();
                _ledOn = false; _flash = 0;
                go.transform.SetParent(g.player.cam.transform, false);
                _raise = 0;
                Pose();
                g.held.ShowHeld(null);
                Sfx.Play("servo", null, -14);
            }
            else
            {
                EndPulse();
                if (_swapT > 0) { _swapT = 0; _swapIn = -1; }   // the hatch closes on the old battery
                if (_view != null) Destroy(_view.gameObject);
                _view = null;
                if (g?.inventory != null)
                {
                    var hid = g.inventory.HeldId();
                    g.held.ShowHeld(hid >= 0 ? g.world.DefOf(hid) : null);
                }
            }
        }

        void Pose()
        {
            if (_view == null) return;
            // both hands on the grips, low and central (Game.RadarView "held"); it rises into view, and dips while the hatch is open
            float swap = _swapT > 0 ? Mathf.Sin(Mathf.Clamp01(1 - _swapT / cfg.swapTime) * Mathf.PI) : 0;
            _view.transform.localPosition = new Vector3(0, -0.25f - (1 - _raise) * 0.25f - swap * 0.08f, 0.56f);
            _view.transform.localRotation = Quaternion.Euler(-18 - swap * 25f, 180, swap * 12f);
            _view.Pose(_dial, _press);
            _view.SetCharges(battery >= 0 ? Charges(battery) : 0);
        }

        public void TryPulse()
        {
            if (!assembled || !isOut || Pulsing || Swapping) return;   // a press during a pulse neither restarts nor spends
            _press = 1;
            Sfx.Play("ui_click", null, -6, 0.05f);
            if (battery < 0 || Charges(battery) < cfg.perPulse) { g.hud.OnFeedback("NO RADAR BATTERY", false); Sfx.Play("reject", null, -8); return; }
            int t = Resolve();
            if (t < 0) { g.hud.OnFeedback("NO AVAILABLE TARGET", false); Sfx.Play("reject", null, -8); return; }
            charges[battery] -= cfg.perPulse;   // spent as the pulse begins, together with the lock
            _locked = t;
            _pulseT = cfg.duration;
            _flash = 1.5f;   // the pulse goes out: a big amber flash
            Sfx.Play("inspect", null, -8, 0);
            Debug.Log($"[radar] pulse: target {target}, locked {_locked} ({g.world.DefOf(_locked).displayName}), battery {battery} now {Charges(battery)}");
        }

        void EndPulse()
        {
            _pulseT = 0;
            _locked = -1;
            Led(false);
        }

        /// <summary>L: the loaded battery comes out (kept with what it has left) and the fullest one in the
        /// inventory goes in. Manual only; nothing is refilled.</summary>
        public void TrySwap()
        {
            if (!assembled || !isOut || Pulsing || Swapping) return;
            int best = g.inventory.items.Where(id => g.world.DefOf(id).IsRadarBattery && Charges(id) > 0)
                .OrderByDescending(Charges).ThenBy(id => id).DefaultIfEmpty(-1).First();
            if (best < 0) { g.hud.OnFeedback(battery >= 0 && Charges(battery) > 0 ? "No other charged battery in your inventory" : "NO RADAR BATTERY", false); Sfx.Play("reject", null, -8); return; }
            _swapIn = best;
            _swapT = cfg.swapTime;
            Sfx.Play("drawer_open", null, -10);
        }

        void FinishSwap()
        {
            int id = _swapIn;
            _swapIn = -1;
            var inst = g.world.instances[id];
            if (inst.state != ItemState.Carried || inst.owner != "player") return;
            // the new one out of the inventory first, so the old one always has room
            g.inventory.Remove(id);
            g.world.SetState(id, ItemState.Installed, Owner);
            int old = battery;
            battery = id;
            if (old >= 0)
            {
                g.world.SetState(old, ItemState.Carried, "player");
                g.inventory.Add(old);
            }
            Sfx.Play("drawer_close", null, -8);
            g.hud.OnFeedback($"Battery loaded: {Charges(id)} of {cfg.maxCharges} charges" + (old >= 0 ? $"  ·  the old one ({Charges(old)} left) is in your inventory" : ""), true);
            if (isOut) g.held.ShowHeld(null);
        }

        void Update()
        {
            if (g == null || g.world == null) return;
            if (isOut && g.state == "play" && (g.interaction.inspecting || g.CarriedArm != null)) Put(false);
            var kb = Keyboard.current;
            var mouse = Mouse.current;
            bool input = g.state == "play" && !g.Automated && g.interaction.enabledInput && !g.interaction.inspecting && kb != null;
            if (input && kb.yKey.wasPressedThisFrame)
            {
                if (!assembled) g.hud.OnFeedback($"No Part Radar yet — bring one of each of its eight parts to the Robot Workbench ({TypesDelivered().Count}/8)", false);
                else if (g.CarriedArm != null) g.hud.OnFeedback("Both hands are on the Sorting Arm", false);
                else Put(!isOut);
            }
            if (!isOut) return;
            if (input)
            {
                if (mouse != null && mouse.leftButton.wasPressedThisFrame && PlayerController.ClickReady) TryPulse();
                float wheel = mouse != null ? mouse.scroll.ReadValue().y : 0;
                if (wheel != 0 && !Pulsing) CycleTarget(wheel < 0 ? 1 : -1);
                if (kb.lKey.wasPressedThisFrame) TrySwap();
            }
            float dt = Time.deltaTime;
            _raise = Mathf.MoveTowards(_raise, 1, dt * 5);
            _press = Mathf.MoveTowards(_press, 0, dt * 6);
            _dial = Mathf.MoveTowards(_dial, _dialGoal, dt * 300);
            if (_swapT > 0 && (_swapT -= dt) <= 0) { _swapT = 0; FinishSwap(); }
            if (Pulsing)
            {
                _pulseT -= dt;
                if (_pulseT <= 0 || _locked < 0 || !Eligible(_locked)) EndPulse();   // picked up or moved off: the signal stops, no retarget
                else Led(Lit(Time.time));
            }
            _flash = Mathf.MoveTowards(_flash, 0, dt * 5);
            if (_view != null)
            {
                _view.SetLed(_ledOn || _flash > 0.25f);
                _view.SetLight((_ledOn ? LedLight : 0) + _flash * FlashLight);
            }
            Pose();
        }

        /// <summary>Spec §6.3: the horizontal bearing from the radar's tracking forward (the view's own yaw,
        /// not a tilted antenna) to the locked part within ±tolerance (inclusive) lights the LED; within the
        /// close range (3D distance, inclusive) it blinks.</summary>
        bool Lit(float time)
        {
            var cam = g.player.cam.transform;
            var to = g.world.instances[_locked].Position - cam.position;
            float diff = Mathf.Abs(Mathf.DeltaAngle(Yaw(cam.forward), Yaw(to)));
            if (diff > cfg.tolerance) return false;
            if (to.magnitude > cfg.closeRange) return true;
            return Mathf.Repeat(time * cfg.blinkHz, 1f) < 0.5f;
        }

        /// <summary>The main LED on or off; each time it comes on it flashes bright first.</summary>
        void Led(bool on)
        {
            if (on && !_ledOn) _flash = Mathf.Max(_flash, 1f);
            _ledOn = on;
        }

        static float Yaw(Vector3 v) => Mathf.Atan2(v.x, v.z) * Mathf.Rad2Deg;

        /// <summary>The HUD line while the radar is out.</summary>
        public string HudText()
        {
            string bat = battery < 0 ? "no battery" : new string('●', Charges(battery)) + new string('○', Mathf.Max(0, cfg.maxCharges - Charges(battery)));
            string state = Swapping ? "  ·  <color=#d4a352>swapping battery…</color>" : Pulsing ? $"  ·  <color=#ffaa22>PULSE {_pulseT:0.0}s</color>" : "";
            return $"<color=#b8a890>Part Radar:</color>  {TargetLabel()}   battery {bat}{state}\n<color=#9a8c74>[LMB] pulse   [Wheel] target   [L] battery   [Y] put away</color>";
        }
    }
}
