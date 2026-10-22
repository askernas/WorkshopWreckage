using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace WorkshopWreckage
{
    /// <summary>The Robot Workbench (docs/specs/robot_blueprints_and_assembly.md): a
    /// physical bench where every robot is built. Building a robot needs BOTH its
    /// blueprint (player level, robots.json blueprint_level) and its parts, carried here
    /// by the player personally:
    ///  · a unique robot part can be found, carried and delivered at any level; until
    ///    the blueprint unlocks it waits in that robot's holding tray (state Stored in
    ///    station "RS-&lt;type&gt;");
    ///  · once unlocked, delivered parts are installed (state Installed, owner
    ///    "assembly:&lt;robot&gt;"), in any order except real mechanical dependencies
    ///    (robots.json assembly.requires); each one visibly appears on the robot;
    ///  · when every part is installed the player activates the robot (it hops off the
    ///    bench and starts work).
    /// Extra Hoverbots are crafted on the Hoverbot mat from ordinary parts (recipes by
    /// category in robots.json extra_units) once the previous Hoverbot is operational.
    /// Every part is one instance in exactly one place; all state lives in the item
    /// states / owners plus the small dictionary in ToDict, so saves rebuild it exactly.</summary>
    public class RobotWorkbench : MonoBehaviour
    {
        public static readonly string[] Types = { "retrobot", "unibot", "spiderbot", "caterpillarbot", "hoverbot" };
        public enum Status { NotStarted, AwaitingBlueprint, ReadyToAssemble, PartiallyAssembled, ReadyForActivation, Operational }
        public enum PartState { Missing, Discovered, Carried, Delivered, Installed }

        public class Project
        {
            public string id, type, name;
            public int level, mat;
            public bool crafted;
            public string after;                                  // crafted: the robot that must be operational first
            public Dictionary<string, int> materials = new();     // crafted: category -> count
            public Dictionary<string, List<string>> requires = new();
            public List<string> partOrder = new();                // part keys in a valid build order
        }

        public static readonly Vector3 Bench = new(2.9f, 0.86f, 0.9f);
        const float RobotZ = -0.04f, TrayZ = 0.31f;
        public readonly List<Project> projects = new();
        public string selected = "retrobot";
        readonly HashSet<string> _announced = new();
        Game _g;
        readonly Dictionary<int, Transform> _trays = new();
        TextAtlas _labels;
        Renderer[] _boards = new Renderer[5];
        public event System.Action Changed;

        public static string StationOf(string type) => "RS-" + type;
        public static string OwnerOf(string robot) => "assembly:" + robot;

        // ---------------------------------------------------------------- setup

        public void Init(Game g)
        {
            _g = g;
            LoadProjects();
            BuildBench();
        }

        void LoadProjects()
        {
            projects.Clear();
            var cfg = Robot.AllConfig();
            for (int i = 0; i < Types.Length; i++)
            {
                if (!cfg.TryGetValue(Types[i], out var o)) continue;
                var c = Json.Obj(o);
                var req = new Dictionary<string, List<string>>();
                foreach (var kv in Json.Obj(Json.Obj(c.GetValueOrDefault("assembly"))?.GetValueOrDefault("requires")) ?? new Dictionary<string, object>())
                    req[kv.Key] = Json.Arr(kv.Value).Select(x => x.ToString()).ToList();
                var p = new Project { id = Types[i], type = Types[i], name = Json.Str(c, "name", Types[i]), level = Json.Int(c, "blueprint_level", 1), mat = i, requires = req };
                projects.Add(p);
                string after = p.id;
                foreach (var e in Json.Arr(c.GetValueOrDefault("extra_units")))
                {
                    var ed = Json.Obj(e);
                    var x = new Project
                    {
                        id = Json.Str(ed, "id"), type = Types[i], name = Json.Str(ed, "name"), level = Json.Int(ed, "level", p.level), mat = i,
                        crafted = true, after = after, requires = req,
                    };
                    foreach (var kv in Json.Obj(ed["materials"])) x.materials[kv.Key] = (int)(double)kv.Value;
                    projects.Add(x);
                    after = x.id;
                }
            }
        }

        /// <summary>Called once the item database is known (part keys come from the catalog).</summary>
        public void PrepareOrders()
        {
            foreach (var p in projects)
            {
                var keys = _g.db.RobotPartIds(p.type).Select(d => Robot.PartKey(_g.db.Get(d))).ToList();
                // build order for crafted robots: every part after its prerequisites (stable)
                var order = new List<string>();
                var left = new List<string>(keys);
                for (int guard = 0; left.Count > 0 && guard < 100; guard++)
                    foreach (var k in left.ToList())
                        if (!p.requires.TryGetValue(k, out var r) || r.All(order.Contains)) { order.Add(k); left.Remove(k); }
                order.AddRange(left);
                p.partOrder = order;
            }
        }

        public Project P(string id) => projects.FirstOrDefault(p => p.id == id);
        public Robot RobotOf(Project p) => _g.robots.FirstOrDefault(r => r.id == p.id);

        // ---------------------------------------------------------------- queries

        public bool Operational(Project p) => RobotOf(p)?.Operational ?? false;

        /// <summary>The blueprint: player level reached (and, for a crafted robot, the
        /// previous unit of its type operational — they share one bench mat).</summary>
        public bool Unlocked(Project p) => _g.progression != null && _g.progression.level >= p.level && (p.after == null || Operational(P(p.after)));

        public string LockReason(Project p)
        {
            if (p.after != null && !Operational(P(p.after))) return $"Needs {P(p.after).name} operational first";
            if (_g.progression == null || _g.progression.level < p.level) return $"Blueprint unlocks at level {p.level}";
            return "";
        }

        /// <summary>A crafted project is on the bench once its predecessor has left the mat.</summary>
        public bool OnBench(Project p) => !Operational(p) && (p.after == null || Operational(P(p.after)));

        /// <summary>The project currently using a mat (the first one not yet operational).</summary>
        public Project ActiveOnMat(int mat) => projects.Where(p => p.mat == mat).FirstOrDefault(p => !Operational(p)) ?? projects.Last(p => p.mat == mat);

        public List<int> PartIds(Project p) => p.crafted ? new List<int>() : _g.db.RobotPartIds(p.type).SelectMany(d => _g.world.IdsOf(d)).ToList();

        public PartState StateOf(int id)
        {
            var i = _g.world.instances[id];
            switch (i.state)
            {
                case ItemState.Installed: return PartState.Installed;
                case ItemState.Stored: return PartState.Delivered;
                case ItemState.Carried: return PartState.Carried;
            }
            return _g.progress.IsDiscovered(i.defId) ? PartState.Discovered : PartState.Missing;
        }

        public List<int> InstalledIds(Project p)
        {
            var owner = OwnerOf(p.id);
            var o = new List<int>();
            if (p.crafted) { foreach (var i in _g.world.instances) if (i.state == ItemState.Installed && i.owner == owner) o.Add(i.id); }
            else foreach (var id in PartIds(p)) if (_g.world.instances[id].state == ItemState.Installed && _g.world.instances[id].owner == owner) o.Add(id);
            return o;
        }

        public List<int> DeliveredIds(Project p) => p.crafted ? new List<int>() : _g.storage.Stored(StationOf(p.type)).ToList();

        public int Required(Project p) => p.crafted ? p.materials.Values.Sum() : PartIds(p).Count;
        public int InstalledCount(Project p) => InstalledIds(p).Count;
        public int InstalledCount(string id) => P(id) is Project p ? InstalledCount(p) : 0;
        public bool Complete(Project p) => Required(p) > 0 && InstalledCount(p) >= Required(p);
        public float Completion(Project p) => Required(p) == 0 ? 0 : (float)InstalledCount(p) / Required(p);

        /// <summary>Part keys shown on the robot model (crafted: the build order up to the installed fraction).</summary>
        public HashSet<string> InstalledKeys(Project p)
        {
            if (!p.crafted) return InstalledIds(p).Select(i => Robot.PartKey(_g.world.DefOf(i))).ToHashSet();
            int k = Mathf.FloorToInt(Completion(p) * p.partOrder.Count + 1e-4f);
            return p.partOrder.Take(k).ToHashSet();
        }

        /// <summary>Crafted recipe: how many more parts of each category are still needed.</summary>
        public Dictionary<string, int> MaterialsMissing(Project p)
        {
            var have = InstalledIds(p).GroupBy(i => _g.world.DefOf(i).CategoryName).ToDictionary(gr => gr.Key, gr => gr.Count());
            return p.materials.ToDictionary(kv => kv.Key, kv => Mathf.Max(0, kv.Value - have.GetValueOrDefault(kv.Key)));
        }

        public Status StatusOf(Project p)
        {
            if (Operational(p)) return Status.Operational;
            if (!Unlocked(p)) return Status.AwaitingBlueprint;
            if (Complete(p)) return Status.ReadyForActivation;
            bool installable = DeliveredIds(p).Any(id => CanInstall(p, id) == "");
            if (installable) return Status.ReadyToAssemble;
            return InstalledCount(p) == 0 ? Status.NotStarted : Status.PartiallyAssembled;
        }

        public static string StatusText(Status s) => s switch
        {
            Status.NotStarted => "Not started",
            Status.AwaitingBlueprint => "Awaiting blueprint",
            Status.ReadyToAssemble => "Ready to assemble",
            Status.PartiallyAssembled => "Partially assembled",
            Status.ReadyForActivation => "Ready for activation",
            _ => "Operational",
        };

        /// <summary>One line that says what is holding construction up.</summary>
        public string Summary(Project p)
        {
            var s = StatusOf(p);
            if (s == Status.Operational) return $"{p.name} is operational.";
            if (s == Status.ReadyForActivation) return "Assembly complete — activate the robot.";
            var bits = new List<string>();
            if (!Unlocked(p)) bits.Add(LockReason(p) + ".");
            if (p.crafted)
            {
                var miss = MaterialsMissing(p).Where(kv => kv.Value > 0).Select(kv => $"{kv.Value} {kv.Key}");
                bits.Add($"{p.name}: {InstalledCount(p)} of {Required(p)} parts built in. Still needed: {string.Join(", ", miss)}.");
                return string.Join(" ", bits);
            }
            var ids = PartIds(p);
            int inst = 0, deliv = 0, found = 0, missing = 0;
            foreach (var id in ids)
                switch (StateOf(id))
                {
                    case PartState.Installed: inst++; break;
                    case PartState.Delivered: deliv++; break;
                    case PartState.Missing: missing++; break;
                    default: found++; break;
                }
            bits.Add($"{p.name}: {inst} of {ids.Count} components installed.");
            if (deliv > 0) bits.Add(Unlocked(p) ? $"{deliv} delivered, waiting to be installed." : $"{deliv} waiting in the holding tray.");
            if (found > 0) bits.Add($"{found} discovered but not delivered.");
            if (missing > 0) bits.Add($"{missing} still missing.");
            return string.Join(" ", bits);
        }

        // ---------------------------------------------------------------- actions

        /// <summary>Empty string if the delivered part can be installed now, else why not.</summary>
        public string CanInstall(Project p, int id)
        {
            if (!Unlocked(p)) return LockReason(p);
            if (Operational(p)) return $"{p.name} is already operational";
            var i = _g.world.instances[id];
            if (i.state != ItemState.Stored || i.owner != "box:" + StationOf(p.type)) return "Deliver it to the workbench first";
            var key = Robot.PartKey(_g.world.DefOf(id));
            if (p.requires.TryGetValue(key, out var req))
            {
                var have = InstalledKeys(p);
                var need = req.Where(r => !have.Contains(r)).ToList();
                if (need.Count > 0) return "Needs the " + string.Join(" and ", need) + " first";
            }
            return "";
        }

        public bool Install(Project p, int id, bool animate = true)
        {
            if (CanInstall(p, id) != "") return false;
            _g.storage.Remove(StationOf(p.type), id);
            _g.world.SetState(id, ItemState.Installed, OwnerOf(p.id));
            var r = RobotOf(p);
            r?.RefreshAssembly();
            if (animate && r != null) r.InstallPop(Robot.PartKey(_g.world.DefOf(id)));
            Sfx.Play(Complete(p) ? "complete" : "servo", r != null ? r.transform.position : transform.position, -6);
            Refresh();
            return true;
        }

        /// <summary>Installs every delivered part that can go in (repeats, so a part whose
        /// prerequisite was installed in the same pass follows it).</summary>
        public int InstallAll(Project p, bool animate = true)
        {
            int n = 0;
            for (bool more = true; more;)
            {
                more = false;
                foreach (var id in DeliveredIds(p))
                    if (Install(p, id, animate)) { n++; more = true; }
            }
            return n;
        }

        /// <summary>What happens when the player hands this part over at the bench (null =
        /// the workbench doesn't take it).</summary>
        public Project ProjectFor(int id)
        {
            var def = _g.world.DefOf(id);
            if (def.IsRobotPart) return P(def.craftingPurpose.Replace("robot:", ""));
            return projects.FirstOrDefault(p => p.crafted && OnBench(p) && Unlocked(p) && MaterialsMissing(p).GetValueOrDefault(def.CategoryName) > 0);
        }

        /// <summary>The player personally delivers a carried part. Robot parts go to the
        /// holding tray and are installed at once if the blueprint allows; recipe
        /// materials are built into the crafted robot. Returns the feedback text, and
        /// whether it was accepted.</summary>
        public (bool ok, string text) Deliver(int id)
        {
            var inst = _g.world.instances[id];
            var def = _g.world.DefOf(id);
            if (inst.state != ItemState.Carried || inst.owner != "player") return (false, "Carry the part here yourself");
            var p = ProjectFor(id);
            if (p == null) return (false, def.IsRobotPart ? "Unknown robot part" : "The workbench takes robot parts (and recipe materials for a Hoverbot under construction)");
            _g.inventory.Remove(id);
            if (!def.IsRobotPart)
            {
                _g.world.SetState(id, ItemState.Installed, OwnerOf(p.id));
                var r = RobotOf(p);
                r?.RefreshAssembly();
                Refresh();
                Sfx.Play(Complete(p) ? "complete" : "servo", transform.position, -6);
                return (true, $"Built into {p.name}: {def.displayName}  ({InstalledCount(p)}/{Required(p)})");
            }
            _g.storage.Deposit(StationOf(p.type), id);
            if (Install(p, id)) return (true, $"Installed on {p.name}: {def.displayName}  ({InstalledCount(p)}/{Required(p)})");
            Refresh();
            Sfx.Play("drop_drawer", transform.position, -4);
            string why = CanInstall(p, id);
            return (true, $"{def.displayName} is in the holding tray — {why}");
        }

        /// <summary>QA and tests only (--robotview, autoplay): installs a robot's parts
        /// wherever they are, ignoring blueprint and delivery rules.</summary>
        public void ForceInstall(Project p, int count = int.MaxValue)
        {
            foreach (var id in PartIds(p).Take(count))
            {
                var i = _g.world.instances[id];
                if (i.state == ItemState.Installed) continue;
                if (i.state == ItemState.InWorld) _g.index.Remove(id);
                if (i.state == ItemState.Carried && i.owner == "player") _g.inventory.Remove(id);
                if (i.state == ItemState.Stored) _g.storage.Remove(StationOf(p.type), id);
                _g.world.SetState(id, ItemState.Installed, OwnerOf(p.id));
            }
            RobotOf(p)?.RefreshAssembly();
            Refresh();
        }

        public bool CanActivate(Project p) => Complete(p) && !Operational(p) && Unlocked(p) && RobotOf(p) is Robot r && r.mode == Robot.Mode.Assembling;

        public bool Activate(Project p)
        {
            if (!CanActivate(p)) return false;
            RobotOf(p).BeginActivation();
            return true;
        }

        /// <summary>Level ups: newly unlocked blueprints are announced once.</summary>
        public void CheckBlueprints(bool announce)
        {
            foreach (var p in projects)
            {
                if (!Unlocked(p) || !_announced.Add(p.id)) continue;
                if (!announce) continue;
                var bits = p.crafted ? $"Blueprint: {p.name} — bring its materials to the Robot Workbench" :
                    DeliveredIds(p).Count > 0 ? $"Blueprint unlocked: {p.name} — {DeliveredIds(p).Count} part(s) in the holding tray can be installed" : $"Blueprint unlocked: {p.name} — bring its parts to the Robot Workbench";
                _g.hud.Toast(bits, true);
                Sfx.Play("milestone", null, -6);
            }
            Refresh();
        }

        // ---------------------------------------------------------------- save

        public Dictionary<string, object> ToDict() => new()
        {
            ["selected"] = selected,
            ["announced"] = _announced.Select(x => (object)x).ToList(),
        };

        /// <summary>Restores the bench. Saves from before the workbench (parts sitting in the
        /// repair-bay stations were installed there) are migrated: those parts are
        /// installed and complete robots are operational.</summary>
        public void Load(Dictionary<string, object> save)
        {
            _announced.Clear();
            var d = Json.Obj(save?.GetValueOrDefault("workbench"));
            if (d != null)
            {
                selected = Json.Str(d, "selected", "retrobot");
                foreach (var x in Json.Arr(d.GetValueOrDefault("announced"))) _announced.Add(x.ToString());
            }
            else if (save != null)
            {
                foreach (var p in projects.Where(p => !p.crafted))
                    foreach (var id in DeliveredIds(p).ToList())
                    {
                        _g.storage.Remove(StationOf(p.type), id);
                        _g.world.SetState(id, ItemState.Installed, OwnerOf(p.id));
                    }
                foreach (var p in projects.Where(p => !p.crafted && Complete(p))) RobotOf(p)?.SetOperational();
            }
            foreach (var r in _g.robots) r.RefreshAssembly();
            CheckBlueprints(false);
        }

        public void ResetState()
        {
            _announced.Clear();
            selected = "retrobot";
        }

        // ---------------------------------------------------------------- bench

        /// <summary>Bench-local (Godot space) x of a robot type's mat.</summary>
        public static float MatX(int i) => -Bench.x / 2 + Bench.x / Types.Length * (i + 0.5f);

        /// <summary>Where a robot under construction stands on its mat (world), facing the player side.</summary>
        public (Vector3 pos, Quaternion rot) MatSpot(int mat)
        {
            var p = transform.TransformPoint(Batch.G(MatX(mat), Bench.y + 0.008f, RobotZ));
            return (p, Quaternion.LookRotation(Front));
        }

        public Vector3 Front => Vector3.ProjectOnPlane(transform.TransformDirection(Batch.G(0, 0, 1)), Vector3.up).normalized;

        void BuildBench()
        {
            var b = new Batch();
            var top = EnvMaterials.Wood(new Color(0.42f, 0.3f, 0.2f));
            var steel = EnvMaterials.Metal(new Color(0.3f, 0.33f, 0.3f), 0.4f);
            var dark = EnvMaterials.Plain(new Color(0.16f, 0.17f, 0.18f), 0.8f, 0.45f);
            var brass = EnvMaterials.Plain(new Color(0.6f, 0.46f, 0.24f), 1f, 0.55f);
            b.Box(new Vector3(0, Bench.y - 0.03f, 0), new Vector3(Bench.x, 0.06f, Bench.z), top);
            b.Box(new Vector3(0, Bench.y - 0.075f, Bench.z / 2 - 0.02f), new Vector3(Bench.x, 0.03f, 0.04f), steel);   // front apron
            foreach (var sx in new[] { -1f, 1f })
                foreach (var sz in new[] { -1f, 1f })
                    b.Box(new Vector3(sx * (Bench.x / 2 - 0.06f), (Bench.y - 0.06f) / 2, sz * (Bench.z / 2 - 0.06f)), new Vector3(0.07f, Bench.y - 0.06f, 0.07f), steel);
            b.Box(new Vector3(0, 0.16f, 0), new Vector3(Bench.x - 0.15f, 0.025f, Bench.z - 0.15f), EnvMaterials.Wood(new Color(0.34f, 0.24f, 0.16f)));   // lower shelf
            // back board with the blueprint drawing board and a tool rail
            b.Box(new Vector3(0, 1.45f, -Bench.z / 2 + 0.02f), new Vector3(Bench.x, 1.1f, 0.04f), EnvMaterials.Wood(new Color(0.3f, 0.2f, 0.13f)));
            b.Box(new Vector3(0, 1.98f, -Bench.z / 2 + 0.06f), new Vector3(Bench.x + 0.04f, 0.05f, 0.08f), steel);
            b.Box(new Vector3(0, 1.0f, -Bench.z / 2 + 0.05f), new Vector3(Bench.x - 0.1f, 0.025f, 0.04f), steel);   // tool rail
            for (int k = 0; k < 9; k++)   // spanners and screwdrivers on the rail
            {
                float x = -1.25f + k * 0.31f;
                b.Box(new Vector3(x, 0.93f + (k % 3) * 0.01f, -Bench.z / 2 + 0.075f), new Vector3(0.018f, 0.13f + (k % 3) * 0.03f, 0.008f), k % 2 == 0 ? steel : dark);
                b.Box(new Vector3(x, 1.0f, -Bench.z / 2 + 0.075f), new Vector3(0.03f, 0.03f, 0.012f), brass);
            }
            var title = TextAtlas.RenderSingle("ROBOT WORKBENCH", 1024, 128, new Color(0.3f, 0.2f, 0.13f), new Color(0.82f, 0.63f, 0.32f), 72, true);
            b.Quad(new Vector3(0, 1.88f, -Bench.z / 2 + 0.045f), new Vector2(1.6f, 0.2f), EnvMaterials.Atlas(title), new Rect(0, 0, 1, 1));
            var trayTex = TextAtlas.RenderSingle("HOLDING TRAY", 512, 64, new Color(0.62f, 0.5f, 0.3f), new Color(0.18f, 0.12f, 0.07f), 40, true);
            var trayLabel = EnvMaterials.Atlas(trayTex);
            _labels = new TextAtlas(Types.Length, new Vector2Int(200, 80), new Color(0.3f, 0.2f, 0.13f));
            var lm = EnvMaterials.Atlas(_labels.texture);
            float w = Bench.x / Types.Length;
            for (int i = 0; i < Types.Length; i++)
            {
                float x = MatX(i);
                // assembly mat with alignment marks
                b.Box(new Vector3(x, Bench.y + 0.003f, -0.04f), new Vector3(w - 0.06f, 0.006f, 0.6f), EnvMaterials.Plain(new Color(0.14f, 0.24f, 0.18f), 0, 0.1f));
                foreach (var sx in new[] { -1f, 1f }) b.Box(new Vector3(x + sx * 0.12f, Bench.y + 0.0065f, RobotZ), new Vector3(0.008f, 0.001f, 0.4f), EnvMaterials.Plain(new Color(0.75f, 0.7f, 0.5f), 0, 0.1f));
                // holding tray at the front of the mat
                b.Box(new Vector3(x, Bench.y + 0.006f, TrayZ), new Vector3(w - 0.1f, 0.012f, 0.16f), dark);
                foreach (var sz in new[] { -1f, 1f }) b.Box(new Vector3(x, Bench.y + 0.02f, TrayZ + sz * 0.08f), new Vector3(w - 0.1f, 0.03f, 0.012f), steel);
                foreach (var sx in new[] { -1f, 1f }) b.Box(new Vector3(x + sx * (w / 2 - 0.05f), Bench.y + 0.02f, TrayZ), new Vector3(0.012f, 0.03f, 0.16f), steel);
                b.Quad(new Vector3(x, Bench.y + 0.022f, TrayZ + 0.087f), new Vector2(0.2f, 0.025f), trayLabel, new Rect(0, 0, 1, 1));
                b.Quad(new Vector3(x, 1.2f, -Bench.z / 2 + 0.046f), new Vector2(0.4f, 0.16f), lm, _labels.CellUV(i));
                var tray = new GameObject("Tray_" + Types[i]).transform;
                tray.SetParent(transform, false);
                tray.localPosition = Batch.G(x, Bench.y + 0.012f, TrayZ);
                _trays[i] = tray;
            }
            b.colliders.Add((Batch.G(0, Bench.y / 2, 0), new Vector3(Bench.x, Bench.y, Bench.z), Quaternion.identity));
            b.Build(transform, "RobotWorkbench");
            // blueprint boards: separate renderers so a locked board can switch to the locked drawing
            for (int i = 0; i < Types.Length; i++)
            {
                var bb = new Batch();
                bb.Quad(new Vector3(MatX(i), 1.55f, -Bench.z / 2 + 0.045f), new Vector2(w - 0.12f, 0.42f), EnvMaterials.Blueprint(2), new Rect(0, 0, 1, 1));
                var holder = new GameObject("Board_" + Types[i]).transform;
                holder.SetParent(transform, false);
                bb.Build(holder, "Blueprint", false);
                _boards[i] = holder.GetComponentInChildren<Renderer>();
            }
            var tgo = new GameObject("Target_Workbench") { layer = Layers.Target };
            tgo.transform.SetParent(transform, false);
            tgo.transform.localPosition = Batch.G(0, Bench.y + 0.15f, 0);
            tgo.AddComponent<BoxCollider>().size = new Vector3(Bench.x, 0.3f, Bench.z);
            tgo.AddComponent<StorageTarget>().station = true;
        }

        /// <summary>Redraws the status labels, blueprint boards and holding trays.</summary>
        public void Refresh()
        {
            if (_g?.world == null || _labels == null) return;
            for (int i = 0; i < Types.Length; i++)
            {
                var p = ActiveOnMat(i);
                if (p == null) continue;
                var s = StatusOf(p);
                string word = s switch { Status.AwaitingBlueprint => "LOCKED", Status.ReadyForActivation => "READY", Status.Operational => "ONLINE", _ => "BUILD" };
                string code = s == Status.AwaitingBlueprint && _g.progression != null && _g.progression.level < p.level ? "LV " + p.level : $"{InstalledCount(p)}/{Required(p)}";
                _labels.Set(i, code, p.name.ToUpper(), word, s != Status.AwaitingBlueprint);
                if (_boards[i] != null) _boards[i].sharedMaterial = EnvMaterials.Blueprint(Unlocked(p) ? 1 : 2);
                var tray = _trays[i];
                foreach (Transform c in tray) Destroy(c.gameObject);
                var ids = _g.storage.Stored(StationOf(Types[i]));
                for (int k = 0; k < ids.Count; k++)
                {
                    var def = _g.world.DefOf(ids[k]);
                    var mi = HeldView.MakeModel(def);
                    mi.transform.SetParent(tray, false);
                    var rot = Quaternion.Euler(0, -23f * k, 0) * (def.restMode == RestMode.Long ? Quaternion.Euler(0, 0, -90) : Quaternion.identity);
                    mi.transform.localRotation = rot;
                    mi.transform.localScale = Vector3.one * def.worldScale * 0.55f;
                    mi.transform.localPosition = Batch.G(-0.2f + (k % 7) * 0.066f, 0.025f + (k / 14) * 0.03f, -0.04f + (k / 7 % 2) * 0.075f);
                }
            }
            _labels.Redraw();
            Changed?.Invoke();
        }
    }
}
