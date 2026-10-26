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
    /// Extra Hoverbots and Sorting Arms (robots.json extra_units) are built from their own kits
    /// (robot_parts.json, all in the world from the start) on their type's mat once the unit before
    /// them is operational; their delivered parts wait in the type's holding tray until then
    /// (playtest 2026-10-26). A unit with "materials" is still crafted from ordinary parts by category.
    /// Heads and arms are interchangeable (as modules are on a built robot): another
    /// robot's head or arm can fill a chassis' head or arm slot during assembly, and
    /// then stands for that chassis' own head or arm (with its small fittings) until
    /// the robot is built. Every head and arm is one complete item (its eyes, antennas and
    /// gripper come with it), so a found head or arm fits any robot.
    /// Every part is one instance in exactly one place; all state lives in the item
    /// states / owners plus the small dictionary in ToDict, so saves rebuild it exactly.</summary>
    public class RobotWorkbench : MonoBehaviour
    {
        public static readonly string[] Types = { "retrobot", "unibot", "spiderbot", "caterpillarbot", "hoverbot", "sorting_arm" };
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

        public static readonly Vector3 Bench = new(3.45f, 0.86f, 0.9f);   // six mats (the Sorting Arm is the sixth)
        const float RobotZ = -0.04f, TrayZ = 0.31f;
        public readonly List<Project> projects = new();
        public string selected = "retrobot";
        readonly HashSet<string> _announced = new();
        Game _g;
        readonly Dictionary<int, Transform> _trays = new();
        TextAtlas _labels;
        Renderer[] _boards = new Renderer[Types.Length];
        public event System.Action Changed;

        public static string StationOf(string type) => "RS-" + type;
        public static string OwnerOf(string robot) => "assembly:" + robot;
        /// <summary>Owner of a head or arm item that became a module fitted straight to a built robot.</summary>
        public const string ModulePrefix = "module:";

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
                var p = new Project { id = Types[i], type = Types[i], name = Json.Str(c, "name", Types[i]), level = Unlocks.Robot(Types[i]), mat = i, requires = req };
                projects.Add(p);
                string after = p.id;
                foreach (var e in Json.Arr(c.GetValueOrDefault("extra_units")))
                {
                    var ed = Json.Obj(e);
                    var x = new Project
                    {
                        id = Json.Str(ed, "id"), type = Types[i], name = Json.Str(ed, "name"), level = Unlocks.Robot(Json.Str(ed, "id"), p.level), mat = i,
                        crafted = ed.ContainsKey("materials"), after = after, requires = req,
                    };
                    foreach (var kv in Json.Obj(ed.GetValueOrDefault("materials")) ?? new Dictionary<string, object>()) x.materials[kv.Key] = (int)(double)kv.Value;
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
        public bool Unlocked(Project p) => _debugUnlocked.Contains(p.id)
            || _g.progression != null && _g.progression.level >= p.level && (p.after == null || Operational(P(p.after)));

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

        public List<int> PartIds(Project p) => p.crafted ? new List<int>() : _g.db.RobotPartIds(p.id).SelectMany(d => _g.world.IdsOf(d)).ToList();

        /// <summary>The robot type of a unit ("hoverbot_2" -> "hoverbot").</summary>
        public string TypeOf(string unit) => P(unit)?.type ?? unit;

        // ---------------------------------------------------------------- interchangeable Sorting Arm parts

        /// <summary>Types whose units are three copies of one kit (the Sorting Arms, playtest 2026-10-26): any
        /// unit's part goes on whichever unit of the type is being built, in the same place. The parts pool
        /// between the units; how a part works does not change.</summary>
        static readonly HashSet<string> PooledTypes = new() { "sorting_arm" };
        public static bool IsPooled(Project p) => p != null && !p.crafted && PooledTypes.Contains(p.type);

        /// <summary>Every unit's kit of p's type (world ids).</summary>
        public List<int> PoolIds(Project p) => projects.Where(q => q.type == p.type && !q.crafted).SelectMany(PartIds).ToList();

        /// <summary>A pooled unit's parts, one per part key: the one installed on it, else the copy nearest to
        /// going on (delivered, carried, found, missing; its own kit first).</summary>
        List<int> PooledNeeded(Project p)
        {
            var owner = OwnerOf(p.id);
            var o = new List<int>();
            foreach (var gr in PoolIds(p).GroupBy(id => Robot.PartKey(_g.world.DefOf(id))))
            {
                var on = gr.Where(id => _g.world.instances[id].state == ItemState.Installed && _g.world.instances[id].owner == owner).ToList();
                if (on.Count > 0) { o.Add(on[0]); continue; }
                var free = gr.Where(id => _g.world.instances[id].state != ItemState.Installed)
                    .OrderBy(id => StateOf(id) switch { PartState.Delivered => 0, PartState.Carried => 1, PartState.Discovered => 2, _ => 3 })
                    .ThenBy(id => OriginOf(id) == p.id ? 0 : 1).ToList();
                o.Add(free.Count > 0 ? free[0] : gr.FirstOrDefault(id => OriginOf(id) == p.id));
            }
            return o;
        }

        // ---------------------------------------------------------------- interchangeable heads and arms

        /// <summary>The part items that make up each chassis' head and arm modules, the main item first
        /// (the one that can go on another robot; the others are its small fittings, which a donor
        /// module brings along). Design-v1: every head and arm is one complete item, so each list is
        /// just that item; the lists stay so a future module can carry fittings again.</summary>
        static readonly Dictionary<string, Dictionary<ModuleSlot, string[]>> ModuleParts = new()
        {
            ["retrobot"] = new() { [ModuleSlot.Head] = new[] { "head" }, [ModuleSlot.ArmL] = new[] { "left arm" }, [ModuleSlot.ArmR] = new[] { "right arm" } },
            ["unibot"] = new() { [ModuleSlot.Head] = new[] { "head shell" }, [ModuleSlot.ArmL] = new[] { "left arm" }, [ModuleSlot.ArmR] = new[] { "right arm" } },
            ["spiderbot"] = new() { [ModuleSlot.Head] = new[] { "head" }, [ModuleSlot.ArmL] = new[] { "left arm" }, [ModuleSlot.ArmR] = new[] { "right arm" } },
            ["caterpillarbot"] = new() { [ModuleSlot.Head] = new[] { "head" }, [ModuleSlot.ArmL] = new[] { "left arm" }, [ModuleSlot.ArmR] = new[] { "right arm" } },
            ["hoverbot"] = new() { [ModuleSlot.Head] = new[] { "head" }, [ModuleSlot.ArmL] = new[] { "left arm" }, [ModuleSlot.ArmR] = new[] { "right arm" } },
        };

        /// <summary>The robot (unit) a part item was made for.</summary>
        public string OriginOf(int id) => _g.world.DefOf(id).craftingPurpose.Replace("robot:", "");

        /// <summary>Which head/arm slot of `type` the part key belongs to (main item or fitting).</summary>
        static ModuleSlot? SlotOfKey(string type, string key)
        {
            if (ModuleParts.TryGetValue(type, out var m))
                foreach (var kv in m) if (System.Array.IndexOf(kv.Value, key) >= 0) return kv.Key;
            return null;
        }

        /// <summary>The slot this item can fill on any chassis, if it is a head or arm main item.</summary>
        public ModuleSlot? MainSlotOf(int id)
        {
            var d = _g.world.DefOf(id);
            if (!d.IsRobotComponent) return null;
            string o = OriginOf(id), key = Robot.PartKey(d);
            if (ModuleParts.TryGetValue(TypeOf(o), out var m)) foreach (var kv in m) if (kv.Value[0] == key) return kv.Key;
            return null;
        }

        static string MainKey(string type, ModuleSlot s) => ModuleParts[type][s][0];
        static bool TakesDonors(Project p, ModuleSlot s) => !p.crafted && ModuleParts.TryGetValue(p.type, out var m) && m.ContainsKey(s);

        /// <summary>Head and arm main items of every chassis (world ids), cached per world.</summary>
        List<int> Mains()
        {
            if (_mainsWorld == _g.world) return _mains;
            _mainsWorld = _g.world;
            _mains = _g.db.RobotPartIds().SelectMany(d => _g.world.IdsOf(d)).Where(id => MainSlotOf(id) != null).ToList();
            return _mains;
        }
        List<int> _mains;
        ItemWorld _mainsWorld;

        /// <summary>Another robot's head or arm installed in that slot of p (-1: none).</summary>
        public int DonorIn(Project p, ModuleSlot s)
        {
            if (!TakesDonors(p, s)) return -1;
            var owner = OwnerOf(p.id);
            foreach (var id in Mains())
            {
                var i = _g.world.instances[id];
                if (i.state == ItemState.Installed && i.owner == owner && OriginOf(id) != p.id && MainSlotOf(id) == s) return id;
            }
            return -1;
        }

        /// <summary>The origin robot of the head/arm fitted in each slot during assembly (null = its own):
        /// another robot's part item, or a spare module taken off a built robot.</summary>
        public string DonorOrigin(Project p, ModuleSlot s) => p == null ? null : DonorIn(p, s) is int d && d >= 0 ? OriginOf(d) : Reserved(p, s)?.origin;

        /// <summary>A spare module (stripped off a built robot) fitted to that slot of p's assembly.</summary>
        public RobotModule Reserved(Project p, ModuleSlot s) => p != null && TakesDonors(p, s) ? _g.modules?.ReservedFor(p.id, s) : null;
        public IEnumerable<RobotModule> ReservedOn(Project p) => new[] { ModuleSlot.Head, ModuleSlot.ArmL, ModuleSlot.ArmR }.Select(s => Reserved(p, s)).Where(m => m != null);

        /// <summary>Why that spare module cannot go into p's assembly now, or "".</summary>
        public string CanFitSpare(Project p, RobotModule m)
        {
            if (p.crafted || !TakesDonors(p, m.slot)) return $"{p.name} has no {RobotModules.SlotName(m.slot)} to change";
            if (!Unlocked(p)) return LockReason(p);
            if (Operational(p)) return $"{p.name} is already operational: use Modify robots";
            if (SlotFilled(p, m.slot)) return $"{p.name} already has a {RobotModules.SlotName(m.slot)}";
            var key = MainKey(p.type, m.slot);
            if (p.requires.TryGetValue(key, out var req) && req.Where(r => !InstalledKeys(p).Contains(r)).ToList() is var need && need.Count > 0)
                return "Needs the " + string.Join(" and ", need) + " first";
            return _g.modules?.CanReserve(m.id, p.id) ?? "No modules";
        }

        public bool FitSpare(Project p, RobotModule m)
        {
            if (CanFitSpare(p, m) != "" || !_g.modules.Reserve(m.id, p.id)) return false;
            var r = RobotOf(p);
            r?.RefreshAssembly();
            r?.InstallPop(MainKey(p.type, m.slot));
            Sfx.Play(Complete(p) ? "complete" : "servo", r != null ? r.transform.position : transform.position, -6);
            Refresh();
            return true;
        }

        /// <summary>Takes a fitted spare back off p's assembly onto the bench's spare slots.</summary>
        public string CanRemoveSpare(Project p, ModuleSlot s)
        {
            if (Reserved(p, s) == null) return $"No spare {RobotModules.SlotName(s)} is fitted";
            if (Operational(p)) return $"{p.name} is already operational: use Modify robots";
            var key = MainKey(p.type, s);
            var on = p.requires.Where(kv => kv.Value.Contains(key) && InstalledKeys(p).Contains(kv.Key)).Select(kv => kv.Key).ToList();
            if (on.Count > 0) return $"The {string.Join(" and ", on)} is fitted to it";
            return _g.modules.CanUnreserve(p.id, s);
        }

        public RobotModule RemoveSpare(Project p, ModuleSlot s)
        {
            if (CanRemoveSpare(p, s) != "") return null;
            var m = _g.modules.Unreserve(p.id, s);
            RobotOf(p)?.RefreshAssembly();
            Sfx.Play("servo", RobotOf(p) is Robot r ? r.transform.position : transform.position, -6);
            Refresh();
            return m;
        }

        /// <summary>Slot -> donor origin for every slot of p holding another robot's head or arm.</summary>
        public Dictionary<ModuleSlot, string> DonorOrigins(Project p)
        {
            var o = new Dictionary<ModuleSlot, string>();
            foreach (var s in new[] { ModuleSlot.Head, ModuleSlot.ArmL, ModuleSlot.ArmR })
                if (DonorOrigin(p, s) is string d) o[s] = d;
            return o;
        }

        /// <summary>An own part of p that another robot's head or arm now stands in for.</summary>
        public bool Displaced(Project p, int id) => SlotOfKey(p.type, Robot.PartKey(_g.world.DefOf(id))) is ModuleSlot s && (DonorIn(p, s) >= 0 || Reserved(p, s) != null || Gone(p, s));

        /// <summary>Its own head or arm was fitted to another robot before it was built (Modify robots): the
        /// slot needs nothing; it is built without it unless another head or arm is fitted.</summary>
        bool Gone(Project p, ModuleSlot s) => TakesDonors(p, s) && _g.modules is RobotModules mods && !mods.Built(p.id)
            && mods.Get(RobotModules.IdOf(s, p.id)) is RobotModule m && m.location != RobotModules.FactoryLoc && m.location != RobotModules.BuildPrefix + p.id;

        /// <summary>Found heads and arms not on any robot that could go in that slot: in the player's hands or a
        /// holding tray, their module still at the factory.</summary>
        public IEnumerable<(int id, RobotModule m)> LooseModules(ModuleSlot s)
        {
            if (_g.modules == null) yield break;
            foreach (var id in Mains())
            {
                if (MainSlotOf(id) != s) continue;
                var i = _g.world.instances[id];
                bool have = (i.state == ItemState.Carried && i.owner == "player") || (i.state == ItemState.Stored && i.owner.StartsWith("box:RS-"));
                if (have && _g.modules.Get(RobotModules.IdOf(s, OriginOf(id))) is RobotModule m && m.location == RobotModules.FactoryLoc) yield return (id, m);
            }
        }

        /// <summary>Fits a found head or arm item to a built robot as its module; the item goes with it.</summary>
        public bool FitLoose(Robot r, int id, ModuleSlot s)
        {
            var (_, m) = LooseModules(s).FirstOrDefault(x => x.id == id);
            if (m == null || !_g.modules.FitFactory(m.id, r.id, s, _g.playTime)) return false;
            var i = _g.world.instances[id];
            if (i.state == ItemState.Carried) _g.inventory.Remove(id);
            else _g.storage.Remove(i.owner.Substring(4), id);
            _g.world.SetState(id, ItemState.Installed, ModulePrefix + m.id);
            Refresh();
            return true;
        }

        /// <summary>The parts p is built from now: its own, less those a donor head or arm stands in for,
        /// plus the donors.</summary>
        public List<int> NeededIds(Project p)
        {
            if (p.crafted) return new List<int>();
            if (IsPooled(p)) return PooledNeeded(p);
            var o = PartIds(p).Where(id => !Displaced(p, id)).ToList();
            foreach (var s in new[] { ModuleSlot.Head, ModuleSlot.ArmL, ModuleSlot.ArmR })
                if (DonorIn(p, s) is int d && d >= 0) o.Add(d);
            return o;
        }

        /// <summary>The part key an item takes on p (a donor head or arm takes the slot's main key).</summary>
        public string KeyOn(Project p, int id)
            => OriginOf(id) != p.id && MainSlotOf(id) is ModuleSlot s && TakesDonors(p, s) ? MainKey(p.type, s) : Robot.PartKey(_g.world.DefOf(id));

        /// <summary>Is that head or arm slot of p filled (its own main item or a donor)?</summary>
        bool SlotFilled(Project p, ModuleSlot s)
        {
            if (DonorIn(p, s) >= 0 || Reserved(p, s) != null) return true;
            var owner = OwnerOf(p.id);
            return PartIds(p).Any(id => _g.world.instances[id].state == ItemState.Installed && _g.world.instances[id].owner == owner && Robot.PartKey(_g.world.DefOf(id)) == MainKey(p.type, s));
        }

        /// <summary>Another robot's head or arm in its own holding tray that p could take now: its own
        /// robot cannot use it at the moment (blueprint locked, already built, or its slot filled).</summary>
        IEnumerable<int> DonorCandidates(Project p)
        {
            if (p.crafted || !ModuleParts.ContainsKey(p.type)) yield break;
            foreach (var id in Mains())
            {
                string o = OriginOf(id);
                if (o == p.id || _g.world.instances[id].state != ItemState.Stored) continue;
                var own = P(o);
                if (own != null && CanInstall(own, id) == "") continue;
                if (CanInstall(p, id) == "") yield return id;
            }
        }

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
            if (p.crafted || IsPooled(p)) { foreach (var i in _g.world.instances) if (i.state == ItemState.Installed && i.owner == owner) o.Add(i.id); }
            else foreach (var id in NeededIds(p)) if (_g.world.instances[id].state == ItemState.Installed && _g.world.instances[id].owner == owner) o.Add(id);
            return o;
        }

        /// <summary>Parts in the trays that can go on p now (its own, and other robots' heads and arms).</summary>
        public int InstallableCount(Project p) => DeliveredIds(p).Count(id => CanInstall(p, id) == "") + DonorCandidates(p).Count();

        /// <summary>Its own delivered parts (the units of a type share one holding tray; a pooled type: all of them).</summary>
        public List<int> DeliveredIds(Project p) => p.crafted ? new List<int>() : _g.storage.Stored(StationOf(p.type)).Where(id => OriginOf(id) == p.id || IsPooled(p)).ToList();

        public int Required(Project p) => p.crafted ? p.materials.Values.Sum() : NeededIds(p).Count;
        public int InstalledCount(Project p) => InstalledIds(p).Count;
        public int InstalledCount(string id) => P(id) is Project p ? InstalledCount(p) : 0;
        public bool Complete(Project p) => Required(p) > 0 && InstalledCount(p) >= Required(p);
        public float Completion(Project p) => Required(p) == 0 ? 0 : (float)InstalledCount(p) / Required(p);

        /// <summary>Part keys shown on the robot model (crafted: the build order up to the installed fraction).</summary>
        public HashSet<string> InstalledKeys(Project p)
        {
            if (!p.crafted) return InstalledIds(p).Select(i => KeyOn(p, i)).Concat(ReservedOn(p).Select(m => MainKey(p.type, m.slot))).ToHashSet();
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
            bool installable = DeliveredIds(p).Any(id => CanInstall(p, id) == "") || DonorCandidates(p).Any();
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
                bits.Add($"{p.name}: {InstalledCount(p)} of {Required(p)} parts built in. Still needed: {string.Join(", ", miss)}. Materials come from completed destinations ({StockFor(p).Count} available in stock).");
                return string.Join(" ", bits);
            }
            var ids = NeededIds(p);
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
            string origin = OriginOf(id);
            if (i.state != ItemState.Stored || i.owner != "box:" + StationOf(TypeOf(origin))) return "Deliver it to the workbench first";
            if (IsPooled(p) && TypeOf(origin) == p.type)
            {
                // any Sorting Arm's part goes on the arm being built, in the same place
                if (InstalledKeys(p).Contains(KeyOn(p, id))) return $"{p.name} already has a {KeyOn(p, id)}";
            }
            else if (origin != p.id)
            {
                // another robot's head or arm: any chassis takes it in a free slot of its kind
                if (MainSlotOf(id) is not ModuleSlot s || !TakesDonors(p, s)) return $"It is {RobotModules.OriginName(origin)}'s part";
                if (SlotFilled(p, s)) return $"{p.name} already has a {RobotModules.SlotName(s)}";
            }
            else if (SlotOfKey(p.type, Robot.PartKey(_g.world.DefOf(id))) is ModuleSlot own && DonorOrigin(p, own) is string d)
                return $"{p.name} has {RobotModules.OriginName(d)}'s {RobotModules.SlotName(own)} instead";
            var key = KeyOn(p, id);
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
            _g.storage.Remove(StationOf(TypeOf(OriginOf(id))), id);
            _g.world.SetState(id, ItemState.Installed, OwnerOf(p.id));
            var r = RobotOf(p);
            r?.RefreshAssembly();
            if (animate && r != null) r.InstallPop(KeyOn(p, id));
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
                foreach (var id in DeliveredIds(p).Concat(DonorCandidates(p).ToList()))
                    if (Install(p, id, animate)) { n++; more = true; }
            }
            return n;
        }

        /// <summary>What happens when the player hands this part over at the bench (null =
        /// the workbench doesn't take it).</summary>
        public Project ProjectFor(int id)
        {
            var def = _g.world.DefOf(id);
            if (def.IsToolItem) return null;
            if (def.IsRobotPart) return P(def.craftingPurpose.Replace("robot:", ""));
            if (!_g.progress.IsComplete(def.id)) return null;   // design v1.0 §3.4: only stock of a completed destination
            return projects.FirstOrDefault(p => p.crafted && OnBench(p) && Unlocked(p) && MaterialsMissing(p).GetValueOrDefault(def.CategoryName) > 0);
        }

        /// <summary>Recipe stock: parts lying in destinations that are already complete (their level
        /// point is paid and stays paid), of the categories the recipe still needs.</summary>
        public List<int> StockFor(Project p)
        {
            var o = new List<int>();
            if (!p.crafted) return o;
            foreach (var kv in MaterialsMissing(p))
            {
                int left = kv.Value;
                foreach (var d in _g.progress.completedDefs.OrderBy(d => d))
                {
                    if (left <= 0) break;
                    var def = _g.db.Get(d);
                    if (def == null || def.CategoryName != kv.Key) continue;
                    foreach (var id in _g.storage.Stored(def.storageBox).ToList())
                    {
                        if (left <= 0) break;
                        o.Add(id); left--;
                    }
                }
            }
            return o;
        }

        /// <summary>The confirmed bench build: takes the recipe's materials out of completed
        /// destinations' stock (nothing unsorted is ever teleported). Returns how many went in.</summary>
        public int BuildFromStock(Project p)
        {
            if (!p.crafted || !Unlocked(p) || !OnBench(p) || Operational(p)) return 0;
            int n = 0;
            foreach (var id in StockFor(p))
            {
                var box = _g.world.DefOf(id).storageBox;
                _g.storage.Remove(box, id);
                _g.world.SetState(id, ItemState.Installed, OwnerOf(p.id));
                n++;
            }
            if (n > 0) { RobotOf(p)?.RefreshAssembly(); Sfx.Play(Complete(p) ? "complete" : "servo", transform.position, -6); }
            Refresh();
            return n;
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
            if (def.IsToolItem) return _g.radar.Deliver(id);   // the Part Radar bin
            if (def.IsRobotPart)
            {
                if (ProjectFor(id) == null) return (false, "Unknown robot part");
                _g.inventory.Remove(id);
                return Receive(id);
            }
            var p = ProjectFor(id);
            if (p == null) return (false, def.IsRobotPart ? "Unknown robot part"
                : !_g.progress.IsComplete(def.id) ? $"Not yet — recipes only use parts of a completed destination ({_g.progress.AcceptedOf(def.id)}/{_g.world.TotalOf(def.id)} {def.displayName} stored)"
                : "The workbench takes robot parts (and recipe materials for a robot under construction)");
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
            return Receive(id);
        }

        /// <summary>Where a robot (Robot Part Retrieval) hands a robot part over: on the floor in front of
        /// the mat of the part's robot type.</summary>
        public Vector3 DropOffSpot(int id)
        {
            var p = P(TypeOf(OriginOf(id))) ?? projects[0];
            var (sp, _) = MatSpot(p.mat);
            var s = sp + Front * (Bench.z * 0.5f + 0.35f);
            return new Vector3(s.x, 0, s.z);
        }

        /// <summary>A robot part arrives at the workbench (the player's inventory or a robot's hands; `by` is who
        /// brought it): into its type's holding tray, then onto its robot if it can go on now.</summary>
        public (bool ok, string text) Receive(int id, string by = "player")
        {
            var def = _g.world.DefOf(id);
            var p = ProjectFor(id);
            if (p == null || !def.IsRobotPart) return (false, "Unknown robot part");
            _g.storage.Deposit(StationOf(p.type), id, by);
            if (Install(p, id)) return (true, $"Installed on {p.name}: {def.displayName}  ({InstalledCount(p)}/{Required(p)})");
            // a Sorting Arm part goes on whichever arm is on the mat (the kits are copies)
            if (IsPooled(p))
                foreach (var q in projects.Where(q => q.type == p.type && q != p && OnBench(q)))
                    if (Install(q, id)) return (true, $"Installed on {q.name}: {def.displayName}  ({InstalledCount(q)}/{Required(q)})");
            // a head or arm its own robot cannot take now goes on the robot being built (the one
            // selected at the bench first), as any robot takes any head and arms
            if (MainSlotOf(id) != null)
                foreach (var q in projects.OrderBy(q => q.id == selected ? 0 : 1).ThenBy(q => q.mat))
                    if (q != p && OnBench(q) && Install(q, id))
                        return (true, $"Fitted to {q.name}: {def.displayName} as its {RobotModules.SlotName(MainSlotOf(id).Value)}  ({InstalledCount(q)}/{Required(q)})");
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

        /// <summary>Debug mode (Unlocks.DebugMode) only: unlocks the blueprint regardless of
        /// level, installs every part (crafted: recipe materials taken from the floor),
        /// makes a crafted robot's predecessor operational and starts the activation.
        /// Returns the feedback text.</summary>
        public string DebugUnlock(Project p)
        {
            if (!Unlocks.DebugMode) return "Debug mode is off";
            if (Operational(p)) return $"{p.name} is already operational";
            if (p.after != null && !Operational(P(p.after)))
            {
                var prev = P(p.after);
                DebugUnlock(prev);
                RobotOf(prev)?.SetOperational();
            }
            _debugUnlocked.Add(p.id);
            if (p.crafted) FillRecipe(p);
            else ForceInstall(p);
            if (!Complete(p)) return $"{p.name}: not enough loose parts to finish it ({InstalledCount(p)}/{Required(p)})";
            return Activate(p) ? $"DEBUG: {p.name} unlocked and starting up" : $"DEBUG: {p.name} unlocked, assembly complete";
        }

        readonly HashSet<string> _debugUnlocked = new();

        /// <summary>QA (debug unlock, benchmark): builds a crafted robot's recipe from loose
        /// ordinary parts of the right categories.</summary>
        public void FillRecipe(Project p)
        {
            foreach (var kv in MaterialsMissing(p))
                foreach (var i in _g.world.instances.Where(i => i.state == ItemState.InWorld && _g.world.DefOf(i.id).CategoryName == kv.Key).Take(kv.Value).ToList())
                {
                    _g.index.Remove(i.id);
                    _g.world.SetState(i.id, ItemState.Installed, OwnerOf(p.id));
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

        /// <summary>After the robots are loaded: a further unit that was crafted from ordinary parts in a
        /// save from before the kits (playtest 2026-10-26) is built from its kit now, wherever those parts
        /// lie, so it stays operational and its kit isn't left lying around for a robot that exists.</summary>
        public void InstallKitsOfBuiltUnits()
        {
            foreach (var p in projects.Where(p => !p.crafted && p.after != null && Operational(p) && !Complete(p)))
            {
                foreach (var id in PartIds(p))
                {
                    var i = _g.world.instances[id];
                    if (i.state == ItemState.Installed) continue;
                    if (i.state == ItemState.InWorld) _g.index.Remove(id);
                    else if (i.state == ItemState.Carried && i.owner == "player") _g.inventory.Remove(id);
                    else if (i.state == ItemState.Stored) _g.storage.Remove(i.owner.Substring(4), id);
                    else if (i.state == ItemState.Carried) continue;   // in a robot's hands: it delivers it
                    _g.world.SetState(id, ItemState.Installed, OwnerOf(p.id));
                }
                RobotOf(p)?.RefreshAssembly();
            }
        }

        public void ResetState()
        {
            _announced.Clear();
            _debugUnlocked.Clear();
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

        /// <summary>Where a robot summoned for Modify Robot parks (floor, world), facing the bench: two rows beyond
        /// the charging mats, 4 + 3 spots, one per chassis, so summoned robots never crowd or intersect.</summary>
        public (Vector3 pos, Quaternion rot) ParkSpot(string robotId)
        {
            int i = Mathf.Max(0, System.Array.IndexOf(RobotModules.Chassis, robotId));
            int row = i < 4 ? 0 : 1, n = row == 0 ? 4 : 3, col = row == 0 ? i : i - 4;
            float x = (Bench.x - 0.4f) * ((col + 0.5f) / n - 0.5f);
            var p = transform.TransformPoint(Batch.G(x, 0, 1.9f + row * 0.6f));
            return (new Vector3(p.x, 0, p.z), Quaternion.LookRotation(-Front));
        }

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
                var ids = _g.storage.Stored(StationOf(Types[i]));   // every unit's parts of that type
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
