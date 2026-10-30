using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace WorkshopWreckage
{
    /// <summary>The Robot Workbench (docs/specs/robot_blueprints_and_assembly.md): a
    /// physical bench where every robot is built. Building a robot needs BOTH its
    /// blueprint (player level, robots.json blueprint_level) and its parts, carried here
    /// by the player personally:
    ///  · a unique robot part can be found, carried and delivered at any level; it goes into the one
    ///    shared robot parts pool along the front of the bench (playtest 2026-10-26: "all robot parts in
    ///    one big pile ... shared before assigned"). Inside, each part keeps its type's station
    ///    "RS-&lt;type&gt;" (state Stored), which only says what it fits;
    ///  · nothing is assigned on delivery: building at the bench takes the fitting parts out of the
    ///    pool (the same fit rules as ever: any head on any mobile robot, arms on their own side, the
    ///    rest only on their own type, any Sorting Arm's part on any Sorting Arm);
    ///  · once unlocked, pooled parts are installed (state Installed, owner
    ///    "assembly:&lt;robot&gt;") in any order: the ghost frame on the mat holds each one in its
    ///    place, so a head or arm can go on before the chassis is found (playtest 2026-10-28;
    ///    robots.json assembly.requires now only orders a crafted robot's build and removing a
    ///    fitted spare); each one visibly appears on the robot;
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
        const float RobotZ = -0.04f, TrayZ = 0.31f, BoxWall = 0.07f;
        public readonly List<Project> projects = new();
        public string selected = "retrobot";
        readonly HashSet<string> _announced = new();
        Game _g;
        Transform _pool;
        TextAtlas _labels;
        Renderer[] _boards = new Renderer[Types.Length];
        public event System.Action Changed;

        public static string StationOf(string type) => "RS-" + type;

        /// <summary>Everything in the robot parts pool: every robot type's parts and the Part Radar's.</summary>
        public List<int> PoolContents() => Types.SelectMany(t => _g.storage.Stored(StationOf(t))).Concat(_g.storage.Stored(PartRadar.Station)).ToList();

        /// <summary>Which robots a pooled part can go on, in words.</summary>
        public string FitsText(int id)
        {
            var d = _g.world.DefOf(id);
            if (d.IsToolItem) return "a Part Radar part";
            string origin = OriginOf(id);
            var p = P(origin);
            if (MainSlotOf(id) is ModuleSlot s) return $"fits any robot as its {RobotModules.SlotName(s)}";
            if (IsPooled(p)) return $"fits any {p.name.Split(' ')[0]}";
            return $"fits {p?.name ?? origin} only";
        }

        /// <summary>Another robot's head or arm in the pool that p can take now (the player's choice at the bench).</summary>
        public List<int> PoolDonors(Project p) => p.crafted || !ModuleParts.ContainsKey(p.type) ? new List<int>()
            : Mains().Where(id => OriginOf(id) != p.id && _g.world.instances[id].state == ItemState.Stored && CanInstall(p, id) == "").ToList();
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

        /// <summary>The blueprint: granted by the tool pegboard (Retrobot from the start; unlocks.json
        /// "pegboard_rewards", docs/PEGBOARD.md "Rewards") and the player level reached. Never needs another robot:
        /// units of a type share one bench mat, but whichever unit is unlocked can be built on it (ActiveOnMat). An
        /// operational robot stays unlocked whatever the rules say now.</summary>
        public bool Unlocked(Project p) => _debugUnlocked.Contains(p.id) || Operational(p)
            || _g.progression != null && _g.progression.level >= p.level && ToolsMet(p);

        public string LockReason(Project p)
        {
            var why = new List<string>();
            if (!ToolsMet(p)) why.Add($"Requires: {ToolRequirementText(p)} ({ToolProgressText(p)})");
            if (_g.progression == null || _g.progression.level < p.level) why.Add($"Blueprint unlocks at level {p.level}");
            return string.Join("  ·  ", why);
        }

        // ---------------------------------------------------------------- pegboard rewards (docs/PEGBOARD.md "Rewards")

        Pegboard.PegboardRewards Rewards => _g.pegRewards;
        Pegboard.PegboardRewards.Milestone MilestoneOf(Project p) => Rewards?.For(p.id);

        public bool HasToolRequirement(Project p) => MilestoneOf(p) != null;

        /// <summary>QA only: the robot sections of the autoplay test levels and build order with the pegboard out of
        /// the way (its own section, "pegboard", tests the rewards). Never set by the game.</summary>
        public static bool QaIgnoreToolRequirements;

        /// <summary>The board has granted this blueprint (for good, saved), or it is open from the start.</summary>
        public bool ToolsMet(Project p) => QaIgnoreToolRequirements || Rewards == null || Rewards.Has(p.id);

        /// <summary>(have, need, unit) toward the milestone that grants the blueprint: tools of the section, or sections.</summary>
        public (int have, int need, string unit) ToolProgress(Project p) => Rewards?.Progress(MilestoneOf(p), _g.builder?.pegboard) ?? (0, 0, "");

        public string ToolRequirementText(Project p) => Rewards?.RequirementText(MilestoneOf(p), _g.builder?.pegboard) ?? "";

        public string ToolProgressText(Project p) { var (h, n, u) = ToolProgress(p); return $"{h} / {n} {u}"; }

        /// <summary>The other still-locked units of p's type and what opens them ("Hoverbot #2, Hoverbot #3: Sockets &
        /// Keys (12 / 54 tools restored)"), for the bench page of the unit on the mat; "" if none.</summary>
        public string OtherUnitsText(Project p)
            => string.Join("  ·  ", projects.Where(q => q.type == p.type && q != p && !Unlocked(q) && !Operational(q)).GroupBy(MilestoneOf)
                .Where(gr => gr.Key != null).Select(gr => $"{string.Join(", ", gr.Select(q => q.name))}: {ToolRequirementText(gr.First())} ({ToolProgressText(gr.First())})"));

        /// <summary>Robot units an older save had already unlocked (announced) or built: kept when it is loaded.</summary>
        public IEnumerable<string> LegacyUnlocked() => projects.Where(p => _announced.Contains(p.id) || Operational(p)).Select(p => p.id).ToList();

        /// <summary>The project being built on its mat.</summary>
        public bool OnBench(Project p) => !Operational(p) && ActiveOnMat(p.mat) == p;

        /// <summary>The project using a mat: of the units not yet operational, one already being assembled, else the
        /// first unlocked one, else the first one (locked); once all are operational, the last.</summary>
        public Project ActiveOnMat(int mat)
        {
            Project first = null, open = null;
            int n = 0;
            foreach (var p in projects)
            {
                if (p.mat != mat || Operational(p)) continue;
                first ??= p;
                if (open == null && Unlocked(p)) open = p;
                n++;
            }
            if (first == null) return projects.Last(p => p.mat == mat);
            if (n > 1) foreach (var p in projects) if (p.mat == mat && !Operational(p) && InstalledCount(p) > 0) return p;
            return open ?? first;
        }

        public List<int> PartIds(Project p) => p.crafted ? new List<int>() : _g.db.RobotPartIds(p.id).SelectMany(d => _g.world.IdsOf(d)).ToList();

        /// <summary>The robot type of a unit ("hoverbot_2" -> "hoverbot").</summary>
        public string TypeOf(string unit) => P(unit)?.type ?? unit;

        // ---------------------------------------------------------------- interchangeable Sorting Arm parts

        /// <summary>Types whose units are copies of one kit (the Sorting Arms, playtest 2026-10-26; the Hoverbots,
        /// playtest 2026-10-28): any unit's part goes on whichever unit of the type is being built, in the same
        /// place. The parts pool between the units; how a part works does not change. A Hoverbot's head or arm
        /// from another unit still shows that unit's colours (a donor, as on any chassis).</summary>
        static readonly HashSet<string> PooledTypes = new() { "sorting_arm", "hoverbot" };
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
                if (SlotOfKey(p.type, gr.Key) is ModuleSlot s && ForeignIn(p, s)) continue;   // another type's head/arm stands in
                var on = gr.Where(id => _g.world.instances[id].state == ItemState.Installed && _g.world.instances[id].owner == owner).ToList();
                if (on.Count > 0) { o.Add(on[0]); continue; }
                var free = gr.Where(id => _g.world.instances[id].state != ItemState.Installed)
                    .OrderBy(id => StateOf(id) switch { PartState.Delivered => 0, PartState.Carried => 1, PartState.Discovered => 2, _ => 3 })
                    .ThenBy(id => OriginOf(id) == p.id ? 0 : 1).ToList();
                o.Add(free.Count > 0 ? free[0] : gr.FirstOrDefault(id => OriginOf(id) == p.id));
            }
            foreach (var s in new[] { ModuleSlot.Head, ModuleSlot.ArmL, ModuleSlot.ArmR })
                if (DonorIn(p, s) is int d && d >= 0 && TypeOf(OriginOf(d)) != p.type) o.Add(d);
            return o;
        }

        /// <summary>That head/arm slot of a pooled unit is taken care of by something outside its type's kits:
        /// another type's part, a spare module, or (its own module fitted elsewhere) nothing.</summary>
        bool ForeignIn(Project p, ModuleSlot s) => DonorIn(p, s) is int d && d >= 0 && TypeOf(OriginOf(d)) != p.type || Reserved(p, s) != null || Gone(p, s);

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
                if (o == p.id || _g.world.instances[id].state != ItemState.Stored || IsPooled(p) && TypeOf(o) == p.type) continue;   // a pooled unit's own kits are in DeliveredIds
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

        /// <summary>Parts in the pool that go on p now: its own, and other robots' heads and arms their own robot can't take.</summary>
        public int InstallableCount(Project p) => DeliveredIds(p).Count(id => CanInstall(p, id) == "") + DonorCandidates(p).Count();

        /// <summary>Its own parts in the pool (a pooled type: every unit's).</summary>
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
            if (Complete(p) && FittedIds(p).Count == 0) return Status.ReadyForActivation;
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
            if (!p.crafted && FittedIds(p).Count is int nf && nf > 0 && Complete(p)) return $"Every part is fitted — Apply {nf} tried-on part{(nf == 1 ? "" : "s")} on the bench screen, then Activate {p.name}.";
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
            if (FittedIds(p).Count is int fit && fit > 0) bits.Add($"{fit} fitted, not committed yet.");
            if (deliv > 0) bits.Add(Unlocked(p) ? $"{deliv} in the parts pool, ready to try on." : $"{deliv} waiting in the parts pool.");
            if (found > 0) bits.Add($"{found} discovered but not delivered.");
            if (missing > 0) bits.Add($"{missing} still missing.");
            return string.Join(" ", bits);
        }

        // ---------------------------------------------------------------- actions

        /// <summary>Empty string if the delivered part can be installed now, else why not.</summary>
        public string CanInstall(Project p, int id)
        {
            var why = CanFit(p, id, out int swap, false);
            return why == "" && swap >= 0 ? $"{p.name} already has a {KeyOn(p, id)} fitted" : why;
        }

        /// <summary>Empty if the part can be tried on p now, else why not. hands: straight from the player's
        /// hands (else out of the parts pool). swap: the fitted, not yet committed part in its place, which
        /// comes off for it (-1: none). No build order: the ghost frame holds a part until the chassis arrives.</summary>
        public string CanFit(Project p, int id, out int swap, bool hands = true)
        {
            swap = -1;
            if (!Unlocked(p)) return LockReason(p);
            if (Operational(p)) return $"{p.name} is already operational";
            if (Activating(p)) return $"{p.name} is starting up";
            var i = _g.world.instances[id];
            string origin = OriginOf(id);
            if (hands ? i.state != ItemState.Carried || i.owner != "player" : i.state != ItemState.Stored || i.owner != "box:" + StationOf(TypeOf(origin)))
                return hands ? "Take it in your hands first" : "Bring it to the parts pool at the workbench first";
            // a pooled type: any unit's part goes on the unit being built, in the same place;
            // another robot's head or arm: any chassis takes it in a slot of its kind
            bool ownKit = origin == p.id || IsPooled(p) && TypeOf(origin) == p.type;
            if (!ownKit && (MainSlotOf(id) is not ModuleSlot ms || !TakesDonors(p, ms))) return $"It is {RobotModules.OriginName(origin)}'s part";
            var key = KeyOn(p, id);
            var slot = SlotOfKey(p.type, key);
            if (slot is ModuleSlot rs && Reserved(p, rs) is RobotModule spare) return $"{p.name} has the spare {spare.Name} fitted: take it off first";
            int occ = InstalledIds(p).Where(x => x != id && KeyOn(p, x) == key).DefaultIfEmpty(-1).First();
            // the part in that place comes off for it, tried on or applied alike, until the robot is activated
            // (playtest 2026-10-29: an applied arm could not be changed where it was put on)
            swap = occ;
            return "";
        }

        /// <summary>Activate was pressed: the robot is starting up, and its parts are fixed from now on.</summary>
        bool Activating(Project p) => RobotOf(p) is Robot r && r.mode == Robot.Mode.Activating;

        /// <summary>Can that part of p come off (or be swapped) on the bench? Tried on or applied, until Activate.</summary>
        public bool Removable(Project p, int id) => !Operational(p) && !Activating(p) && InstalledIds(p).Contains(id);

        // ---------------------------------------------------------------- hands-on fitting (playtest 2026-10-28)

        /// <summary>Parts tried on a robot on the bench but not committed yet, in the order they went on: they
        /// sit in place (Installed) and can come off or be swapped until the player commits the robot.</summary>
        readonly List<int> _fitted = new();
        public bool IsFitted(int id) => _fitted.Contains(id);
        public List<int> FittedIds(Project p)
        {
            var owner = OwnerOf(p.id);
            return _fitted.Where(id => _g.world.instances[id].state == ItemState.Installed && _g.world.instances[id].owner == owner).ToList();
        }

        /// <summary>Tries a part on p, from the player's hands or the parts pool: if it fits it snaps into
        /// place (a fitted part already there comes off into the hands, or back into the pool).</summary>
        public (bool ok, string text) Fit(Project p, int id, bool backToPool = false)
        {
            var i = _g.world.instances[id];
            bool hands = i.state == ItemState.Carried && i.owner == "player";
            var why = CanFit(p, id, out int swap, hands);
            if (why != "") return (false, why);
            if (hands) _g.inventory.Remove(id); else _g.storage.Remove(i.owner.Substring(4), id);
            string back = swap >= 0 ? Detach(swap, backToPool) : null;
            _g.world.SetState(id, ItemState.Installed, OwnerOf(p.id));
            _fitted.Add(id);
            var r = RobotOf(p);
            r?.RefreshAssembly();
            if (r != null) r.InstallPop(KeyOn(p, id));
            Sfx.Play("servo", r != null ? r.transform.position : transform.position, -6);
            Refresh();
            var name = _g.world.DefOf(id).displayName;
            return (true, $"{name} snaps onto {p.name}" + (back != null ? $"  ·  {_g.world.DefOf(swap).displayName} {back}" : "") + "  ·  Apply, then Activate (until then any part can still be changed)");
        }

        /// <summary>Takes a part off p, tried on or applied, into the player's hands (the pool if they are full);
        /// once the robot is activated its parts stay on.</summary>
        public (bool ok, string text) TakeOff(Project p, int id, bool toPool = false)
        {
            if (!Removable(p, id)) return (false, Operational(p) || Activating(p) ? $"{p.name} is activated: its parts stay on" : "That part is not on the robot");
            string back = Detach(id, toPool);
            RobotOf(p)?.RefreshAssembly();
            Sfx.Play("drop_drawer", RobotOf(p)?.transform.position ?? transform.position, -6);
            Refresh();
            return (true, $"{_g.world.DefOf(id).displayName} off {p.name}: {back}");
        }

        string Detach(int id, bool toPool = false)
        {
            _fitted.Remove(id);
            if (!toPool && _g.inventory.HasRoomFor(id))
            {
                _g.world.SetState(id, ItemState.Carried, "player");
                _g.inventory.Add(id);
                return "back in your hands";
            }
            _g.storage.Deposit(StationOf(TypeOf(OriginOf(id))), id, "player");
            return toPool ? "back in the parts pool" : "back in the parts pool (your hands are full)";
        }

        /// <summary>Found parts that can go on p right now, from the player's hands and the parts pool, one of
        /// each kind (a head or an arm once per origin robot): what the player scrolls through at the robot on
        /// its mat (wheel / Q / E) and tries on with LMB (playtest 2026-10-28).</summary>
        public List<int> FitChoices(Project p)
        {
            var list = new List<int>();
            if (p == null || p.crafted || !Unlocked(p) || Operational(p)) return list;
            var seen = new HashSet<string>();
            foreach (var id in _g.inventory.items.Concat(PoolContents()))
            {
                var def = _g.world.DefOf(id);
                if (!def.IsRobotPart || def.craftingPurpose == null || !def.craftingPurpose.StartsWith("robot:") || !Types.Contains(TypeOf(OriginOf(id)))) continue;
                if (CanFit(p, id, out _, _g.world.instances[id].state == ItemState.Carried) != "") continue;
                if (seen.Add(KeyOn(p, id) + "|" + (MainSlotOf(id) != null ? OriginOf(id) : ""))) list.Add(id);
            }
            return list;
        }

        /// <summary>How many of the parts p is built from have been found (anything but still missing).</summary>
        public int FoundCount(Project p) => p.crafted ? InstalledCount(p) : NeededIds(p).Count(id => StateOf(id) != PartState.Missing);

        /// <summary>The bench screen's parts page (playtest 2026-10-28): every place a part goes on p, the part on
        /// it now (-1: none), the part needed there (for its found state) and the ring of found parts the player
        /// steps through with the arrows (the one on it included), each fitted at once so the preview shows it.</summary>
        public List<(string key, int on, int need, List<int> ring)> Slots(Project p)
        {
            var o = new List<(string, int, int, List<int>)>();
            if (p == null || p.crafted) return o;
            var installed = InstalledIds(p);
            var choices = FitChoices(p);
            foreach (var g in NeededIds(p).GroupBy(id => KeyOn(p, id)))
            {
                string key = g.Key;
                int on = installed.Where(x => KeyOn(p, x) == key).DefaultIfEmpty(-1).First();
                int need = g.OrderByDescending(id => (int)StateOf(id)).First();
                var ring = choices.Where(c => KeyOn(p, c) == key).ToList();
                if (on >= 0)
                {
                    // a plain part from another unit's kit is the same thing: only heads and arms differ
                    if (MainSlotOf(on) == null) ring.Clear();
                    else ring.RemoveAll(c => OriginOf(c) == OriginOf(on));
                    ring.Add(on);
                }
                o.Add((key, on, need, ring.OrderBy(OriginOf).ThenBy(c => c).ToList()));
            }
            return o;
        }

        /// <summary>The middle of the parts pool tray (the bench screen frames it for the Part Radar).</summary>
        public Vector3 PoolPoint => transform.TransformPoint(Batch.G(0, Bench.y + 0.03f, TrayZ));

        /// <summary>Locks every fitted part of p in place (Apply). Returns how many.</summary>
        public int Commit(Project p)
        {
            var ids = FittedIds(p);
            foreach (var id in ids) _fitted.Remove(id);
            if (ids.Count > 0)
            {
                var r = RobotOf(p);
                r?.InstallPop("");
                Sfx.Play(Complete(p) ? "complete" : "servo", r != null ? r.transform.position : transform.position, -6);
                Refresh();
            }
            return ids.Count;
        }

        /// <summary>A part from the parts pool into the player's hands (to try it on).</summary>
        public (bool ok, string text) TakeFromPool(int id)
        {
            var i = _g.world.instances[id];
            if (i.state != ItemState.Stored || !i.owner.StartsWith("box:RS-") || !_g.world.DefOf(id).IsRobotPart) return (false, "");
            if (!_g.inventory.HasRoomFor(id)) return (false, $"Your hands are full ({_g.inventory.Parts}/{_g.inventory.capacity})");
            _g.storage.Remove(i.owner.Substring(4), id);
            _g.world.SetState(id, ItemState.Carried, "player");
            _g.inventory.Add(id);
            _g.inventory.Select(i.defId);
            Sfx.Play("pickup", transform.position, -6);
            Refresh();
            return (true, $"{_g.world.DefOf(id).displayName} from the parts pool  ·  {FitsText(id)}: click the Robot Workbench to fit it on the bench screen");
        }

        /// <summary>The robot part in the pool tray nearest the aim ray (-1: none within a few cm).</summary>
        public int PoolPartAt(Vector3 o, Vector3 dir)
        {
            int best = -1; float bd = 0.05f;
            foreach (var (id, t) in _poolModels)
            {
                if (t == null || !_g.world.DefOf(id).IsRobotPart) continue;
                var c = t.position;
                float along = Vector3.Dot(c - o, dir);
                if (along < 0) continue;
                float d = Vector3.Distance(o + dir * along, c);
                if (d < bd) { bd = d; best = id; }
            }
            return best;
        }
        readonly List<(int id, Transform t)> _poolModels = new();

        /// <summary>A model's bounds in its parent's space (the parts box), from its meshes.</summary>
        static Bounds LocalExtent(Transform model)
        {
            var parent = model.parent;
            bool any = false;
            var b = new Bounds();
            foreach (var mf in model.GetComponentsInChildren<MeshFilter>())
            {
                if (mf.sharedMesh == null) continue;
                var m = parent.worldToLocalMatrix * mf.transform.localToWorldMatrix;
                var mb = mf.sharedMesh.bounds;
                for (int i = 0; i < 8; i++)
                {
                    var p = m.MultiplyPoint3x4(mb.center + Vector3.Scale(mb.extents, new Vector3((i & 1) == 0 ? -1 : 1, (i & 2) == 0 ? -1 : 1, (i & 4) == 0 ? -1 : 1)));
                    if (!any) { b = new Bounds(p, Vector3.zero); any = true; } else b.Encapsulate(p);
                }
            }
            return b;
        }
        /// <summary>QA: where a pool part's model lies in the tray.</summary>
        public Vector3? PoolModelPosition(int id) => _poolModels.FirstOrDefault(m => m.id == id).t is Transform t && t != null ? t.position : null;

        /// <summary>Where the aim ray meets the bench top (just above it): what the player looks at, rather than
        /// where it enters the bench's aim box (whose top lies over the tray when looking at a robot).</summary>
        public Vector3 BenchPoint(Vector3 o, Vector3 dir, Vector3 fallback)
        {
            var plane = new Plane(transform.up, transform.TransformPoint(Batch.G(0, Bench.y + 0.03f, 0)));
            return plane.Raycast(new Ray(o, dir), out float t) && t > 0 && t < 6 ? o + dir * t : fallback;
        }

        /// <summary>Is that bench point on the parts pool tray (front edge), not a mat?</summary>
        public bool AtPool(Vector3 world) => Batch.G(transform.InverseTransformPoint(world)).z > 0.22f;

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

        /// <summary>The player personally delivers a carried part. Robot parts go into the
        /// parts pool (installed later, at the bench); recipe materials are built into the
        /// crafted robot. Returns the feedback text, and whether it was accepted.</summary>
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
        /// brought it): into the shared parts pool. Nothing is assigned yet: the player builds from the pool.</summary>
        public (bool ok, string text) Receive(int id, string by = "player")
        {
            var def = _g.world.DefOf(id);
            var p = ProjectFor(id);
            if (p == null || !def.IsRobotPart) return (false, "Unknown robot part");
            _g.storage.Deposit(StationOf(p.type), id, by);
            Refresh();
            Sfx.Play("drop_drawer", transform.position, -4);
            var ready = projects.Where(q => OnBench(q) && CanInstall(q, id) == "").Select(q => q.name).Take(3).ToList();
            string next = ready.Count > 0 ? $"build it onto {string.Join(" or ", ready)} at the workbench" : FitsText(id);
            return (true, $"{def.displayName} → robot parts pool ({PoolContents().Count})  ·  {next}");
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

        public bool CanActivate(Project p) => Complete(p) && FittedIds(p).Count == 0 && !Operational(p) && Unlocked(p) && RobotOf(p) is Robot r && r.mode == Robot.Mode.Assembling;

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
                    DeliveredIds(p).Count > 0 ? $"Blueprint unlocked: {p.name} — {DeliveredIds(p).Count} of its part(s) in the parts pool can be installed" : $"Blueprint unlocked: {p.name} — bring its parts to the Robot Workbench";
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
            ["fitted"] = _fitted.Select(x => (object)x).ToList(),
        };

        /// <summary>Restores the bench. Saves from before the workbench (parts sitting in the
        /// repair-bay stations were installed there) are migrated: those parts are
        /// installed and complete robots are operational.</summary>
        public void Load(Dictionary<string, object> save)
        {
            _announced.Clear();
            _fitted.Clear();
            var d = Json.Obj(save?.GetValueOrDefault("workbench"));
            if (d != null)
            {
                selected = Json.Str(d, "selected", "retrobot");
                foreach (var x in Json.Arr(d.GetValueOrDefault("announced"))) _announced.Add(x.ToString());
                foreach (var x in Json.Arr(d.GetValueOrDefault("fitted"))) if (int.TryParse(x.ToString(), out int fid) && fid >= 0 && fid < _g.world.instances.Count) _fitted.Add(fid);
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
            _fitted.Clear();
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
            var trayTex = TextAtlas.RenderSingle("ROBOT PARTS BOX", 512, 64, new Color(0.62f, 0.5f, 0.3f), new Color(0.18f, 0.12f, 0.07f), 40, true);
            var trayLabel = EnvMaterials.Atlas(trayTex);
            // the robot parts box (playtest 2026-10-28): a deep open box along the front, every delivered part
            // tossed in on one heap
            float tw = Bench.x - 0.1f;
            b.Box(new Vector3(0, Bench.y + 0.006f, TrayZ), new Vector3(tw, 0.012f, 0.16f), dark);
            foreach (var sz in new[] { -1f, 1f }) b.Box(new Vector3(0, Bench.y + BoxWall / 2, TrayZ + sz * 0.08f), new Vector3(tw, BoxWall, 0.012f), steel);
            foreach (var sx in new[] { -1f, 1f }) b.Box(new Vector3(sx * tw / 2, Bench.y + BoxWall / 2, TrayZ), new Vector3(0.012f, BoxWall, 0.16f), steel);
            b.Box(new Vector3(0, Bench.y + BoxWall, TrayZ + 0.08f), new Vector3(tw + 0.012f, 0.008f, 0.02f), brass);   // the front lip
            foreach (var lx in new[] { -tw / 4, tw / 4 }) b.Quad(new Vector3(lx, Bench.y + BoxWall * 0.5f, TrayZ + 0.087f), new Vector2(0.4f, 0.05f), trayLabel, new Rect(0, 0, 1, 1));
            _pool = new GameObject("PartsPool").transform;
            _pool.SetParent(transform, false);
            _pool.localPosition = Batch.G(0, Bench.y + 0.012f, TrayZ);
            _labels = new TextAtlas(Types.Length, new Vector2Int(200, 80), new Color(0.3f, 0.2f, 0.13f));
            var lm = EnvMaterials.Atlas(_labels.texture);
            float w = Bench.x / Types.Length;
            for (int i = 0; i < Types.Length; i++)
            {
                float x = MatX(i);
                // assembly mat with alignment marks
                b.Box(new Vector3(x, Bench.y + 0.003f, -0.04f), new Vector3(w - 0.06f, 0.006f, 0.6f), EnvMaterials.Plain(new Color(0.14f, 0.24f, 0.18f), 0, 0.1f));
                foreach (var sx in new[] { -1f, 1f }) b.Box(new Vector3(x + sx * 0.12f, Bench.y + 0.0065f, RobotZ), new Vector3(0.008f, 0.001f, 0.4f), EnvMaterials.Plain(new Color(0.75f, 0.7f, 0.5f), 0, 0.1f));
                b.Quad(new Vector3(x, 1.2f, -Bench.z / 2 + 0.046f), new Vector2(0.4f, 0.16f), lm, _labels.CellUV(i));
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

        /// <summary>Redraws the status labels, blueprint boards and the parts pool.</summary>
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
            }
            // the parts box: every part tossed onto one untidy heap, highest in the middle (spent radar batteries are
            // disposed of, not shown). Each part lands where its id says, so the heap stays put between refreshes.
            foreach (Transform c in _pool) Destroy(c.gameObject);
            _poolModels.Clear();
            var ids = PoolContents().Where(id => !_g.world.DefOf(id).IsRadarBattery).OrderBy(id => id).ToList();
            // the heap: a height per 4 cm along the box and per half of its depth
            const float cell = 0.04f, inner = 0.068f;
            float half = (Bench.x - 0.1f) / 2 - 0.02f;
            int cells = Mathf.Max(1, Mathf.FloorToInt(2 * half / cell));
            var heap = new float[cells, 2];
            static float H(int id, int salt) { uint h = (uint)(id * 73856093) ^ (uint)(salt * 19349663); h ^= h >> 13; h *= 0x5bd1e995; h ^= h >> 15; return (h & 0xffffff) / (float)0x1000000; }
            for (int k = 0; k < ids.Count; k++)
            {
                int id = ids[k];
                var def = _g.world.DefOf(id);
                var mi = HeldView.MakeModel(def);
                mi.transform.SetParent(_pool, false);
                _poolModels.Add((id, mi.transform));
                mi.transform.localScale = Vector3.one * def.worldScale * 0.55f;
                // tossed in, lying any which way, but turned so it fits between the box's walls
                // (long parts lie nearly flat: a tipped rod would stand up out of the box)
                var lay = def.restMode == RestMode.Long ? Quaternion.Euler(0, 0, -90) : Quaternion.identity;
                mi.transform.localRotation = lay;
                float tip = 25f * Mathf.Clamp(0.06f / Mathf.Max(0.001f, LocalExtent(mi.transform).size.magnitude), 0.15f, 1f);
                var tilt = Quaternion.Euler((H(id, 1) * 2 - 1) * tip, 0, (H(id, 3) * 2 - 1) * tip) * lay;
                // the first of a few turns that fits between the walls, or else the narrowest one
                float yaw0 = H(id, 2) * 360f, best = 0, bestZ = float.MaxValue;
                foreach (float dy in new[] { 0f, 90f, 45f, 135f, 20f, 160f, 70f, 110f })
                {
                    mi.transform.localRotation = Quaternion.Euler(0, yaw0 + dy, 0) * tilt;
                    float wz = LocalExtent(mi.transform).size.z;
                    if (wz < bestZ) { bestZ = wz; best = dy; }
                    if (wz <= 2 * inner) break;
                }
                mi.transform.localRotation = Quaternion.Euler(0, yaw0 + best, 0) * tilt;
                var ext = LocalExtent(mi.transform);
                // where it lands: around the middle (three throws averaged), inside the walls, on top of what is there
                float u = (H(id, 4) + H(id, 5)) / 2f;
                float hx = ext.extents.x, hz = Mathf.Min(ext.extents.z, inner);
                float x = Mathf.Clamp((u - 0.5f) * 2.2f * half, -half + hx, half - hx);
                float z = Mathf.Clamp((H(id, 6) - 0.5f) * 2 * inner, -inner + hz, inner - hz);
                int c0 = Mathf.Clamp(Mathf.FloorToInt((x - hx + half) / cell), 0, cells - 1), c1 = Mathf.Clamp(Mathf.FloorToInt((x + hx + half) / cell), 0, cells - 1);
                int r0 = z - hz < 0 ? 0 : 1, r1 = z + hz < 0 ? 0 : 1;
                float floor = 0;
                for (int c = c0; c <= c1; c++) for (int r = r0; r <= r1; r++) floor = Mathf.Max(floor, heap[c, r]);
                // settles a little into the gaps of what is under it
                float bottom = floor * 0.7f;
                mi.transform.localPosition = new Vector3(x - ext.center.x, bottom - ext.min.y, z - ext.center.z);   // the box's own space (as LocalExtent)
                float top = bottom + ext.size.y;
                for (int c = c0; c <= c1; c++) for (int r = r0; r <= r1; r++) heap[c, r] = Mathf.Max(heap[c, r], top);
            }
            _labels.Redraw();
            Changed?.Invoke();
        }
    }
}
