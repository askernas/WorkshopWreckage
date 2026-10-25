using System.Collections.Generic;
using System.Linq;

namespace WorkshopWreckage
{
    /// <summary>The mounting slots of a robot chassis (WORKSHOP_WRECKAGE_MODIFY_ROBOT_MODULAR_COMPONENTS_SPEC §3):
    /// every chassis has a head, a left arm and a right arm; a Hoverbot also carries a propeller.</summary>
    public enum ModuleSlot { Head, ArmL, ArmR, Prop }

    /// <summary>One physical, uniquely identified detachable module. Its look (style and paint) belongs to
    /// its origin robot and never changes with the robot it is mounted on; a future update may also give
    /// a module its own small ability, keyed by its id.</summary>
    public sealed class RobotModule
    {
        public readonly string id, origin;
        public readonly ModuleSlot slot;
        /// <summary>"factory" (its origin robot is not built yet: the legacy part items govern), "robot:&lt;id&gt;"
        /// (mounted in that chassis' slot of its kind) or "bench:&lt;n&gt;" (a spare slot at the workbench).</summary>
        public string location = RobotModules.FactoryLoc;

        public RobotModule(string id, string origin, ModuleSlot slot) { this.id = id; this.origin = origin; this.slot = slot; }
        /// <summary>The paint scheme it wears: always its origin robot's.</summary>
        public string Scheme => RobotModules.SchemeOf(origin);
        public string MountedOn => location.StartsWith("robot:") ? location.Substring(6) : null;
        public int BenchSlot => location.StartsWith("bench:") ? int.Parse(location.Substring(6)) : -1;
        public string Name => $"{RobotModules.OriginName(origin)} {RobotModules.SlotName(slot)}";
    }

    /// <summary>The finite module supply and where every module is (spec §10): 7 heads, 7 left arms, 7 right
    /// arms and 3 Hoverbot propellers, each in exactly one place. Pure data: the robots and the workbench UI
    /// decide when an edit is allowed (the robot must be docked); every edit here validates first and then
    /// moves modules in one step, so nothing is ever duplicated or lost.
    ///
    /// Legacy mapping (saves and the build pipeline from before modules existed): the robot-part items in
    /// robot_parts.json stay what they were, the history of how a chassis was first assembled. When a chassis
    /// first activates its own modules leave the "factory" and are mounted on it (Spiderbot's single
    /// "gripping jaws" item stands for both of its arm modules; the crafted Hoverbots #2 and #3 have no part
    /// items). An old save maps every operational robot to its own factory modules.</summary>
    public class RobotModules
    {
        public const string FactoryLoc = "factory";
        /// <summary>"build:&lt;robot&gt;": a spare taken off a built robot and fitted to a chassis still
        /// being assembled on the bench; it goes on that robot when it is activated.</summary>
        public const string BuildPrefix = "build:";
        public const int Version = 1;
        /// <summary>Spare-module slots on the workbench: room for every module of three robots.</summary>
        public const int BenchSlots = 12;
        /// <summary>The seven chassis that take modules (the stationary Sorting Arm does not).</summary>
        public static readonly string[] Chassis = { "retrobot", "unibot", "spiderbot", "caterpillarbot", "hoverbot", "hoverbot_2", "hoverbot_3" };

        public readonly List<RobotModule> all = new();
        readonly Dictionary<string, RobotModule> _byId = new();
        readonly HashSet<string> _built = new();
        /// <summary>Own modules left at the factory by a chassis built with another robot's head or arm in
        /// that slot: their part item is still loose, and goes on the next robot built.</summary>
        readonly HashSet<string> _waiting = new();
        /// <summary>Every time a chassis became complete (first build or repair), for future achievements.</summary>
        public readonly List<BuildNote> builds = new();
        bool _transferred, _allSeven;

        /// <summary>A robot became complete: when, and how its modules were matched.</summary>
        public struct BuildNote
        {
            public string robot, scheme;
            public bool first, coordinated, matchesChassis, original;
            public double playTime;
        }

        public RobotModules()
        {
            foreach (var c in Chassis)
                foreach (var s in SlotsOf(c))
                {
                    var m = new RobotModule(IdOf(s, c), c, s);
                    all.Add(m);
                    _byId[m.id] = m;
                }
        }

        // ------------------------------------------------------------------ vocabulary

        public static bool IsHover(string robotId) => robotId.StartsWith("hoverbot");
        public static bool IsChassis(string robotId) => System.Array.IndexOf(Chassis, robotId) >= 0;
        public static IEnumerable<ModuleSlot> SlotsOf(string robotId)
        {
            yield return ModuleSlot.Head; yield return ModuleSlot.ArmL; yield return ModuleSlot.ArmR;
            if (IsHover(robotId)) yield return ModuleSlot.Prop;
        }
        public static string SlotKey(ModuleSlot s) => s switch { ModuleSlot.Head => "head", ModuleSlot.ArmL => "arm_l", ModuleSlot.ArmR => "arm_r", _ => "prop" };
        public static string SlotName(ModuleSlot s) => s switch { ModuleSlot.Head => "head", ModuleSlot.ArmL => "left arm", ModuleSlot.ArmR => "right arm", _ => "propeller" };
        public static string IdOf(ModuleSlot s, string origin) => SlotKey(s) + "." + origin;
        /// <summary>The locked paint schemes: Retrobot blue, Unibot green, Spiderbot violet, Caterpillarbot
        /// yellow and the three Hoverbot factory skins.</summary>
        public static string SchemeOf(string robotId) => robotId switch
        {
            "retrobot" => "blue",
            "unibot" => "green",
            "spiderbot" => "violet",
            "caterpillarbot" => "yellow",
            _ when IsHover(robotId) => ModuleSkin.HoverbotFactory(robotId).id,
            _ => robotId,
        };
        public static string OriginName(string robotId) => robotId switch
        {
            "retrobot" => "Retrobot", "unibot" => "Unibot", "spiderbot" => "Spiderbot", "caterpillarbot" => "Caterpillarbot",
            "hoverbot" => "Hoverbot", "hoverbot_2" => "Hoverbot #2", "hoverbot_3" => "Hoverbot #3", _ => robotId,
        };
        /// <summary>Can this module go into that chassis' slot? Heads and arms fit every chassis (left arms
        /// only on the left), propellers only Hoverbots.</summary>
        public static bool Fits(RobotModule m, string robotId, ModuleSlot slot)
            => m.slot == slot && IsChassis(robotId) && (slot != ModuleSlot.Prop || IsHover(robotId));

        // ------------------------------------------------------------------ queries

        public RobotModule Get(string id) => id != null && _byId.TryGetValue(id, out var m) ? m : null;
        public bool Built(string robotId) => _built.Contains(robotId);
        public RobotModule At(string robotId, ModuleSlot s)
        {
            string loc = "robot:" + robotId;
            foreach (var m in all) if (m.slot == s && m.location == loc) return m;
            return null;
        }
        public IEnumerable<RobotModule> FittedOn(string robotId) => all.Where(m => m.MountedOn == robotId);
        public IEnumerable<RobotModule> Spares => all.Where(m => m.BenchSlot >= 0).OrderBy(m => m.BenchSlot);
        /// <summary>The spare fitted to that slot of an unbuilt chassis' assembly, if any.</summary>
        public RobotModule ReservedFor(string robotId, ModuleSlot s) => all.FirstOrDefault(m => m.slot == s && m.location == BuildPrefix + robotId);
        public IEnumerable<RobotModule> Reserved => all.Where(m => m.location.StartsWith(BuildPrefix));

        /// <summary>Why that spare cannot go into the assembly of an unbuilt chassis, or "".</summary>
        public string CanReserve(string moduleId, string robotId)
        {
            var m = Get(moduleId);
            if (m == null || m.BenchSlot < 0) return "That module is not on the workbench";
            if (Built(robotId)) return "That robot is already built: fit it under Modify robots";
            if (!SlotsOf(robotId).Contains(m.slot) || !Fits(m, robotId, m.slot)) return "It does not fit that robot";
            if (ReservedFor(robotId, m.slot) != null) return $"Its {SlotName(m.slot)} is already chosen";
            return "";
        }

        public bool Reserve(string moduleId, string robotId)
        {
            if (CanReserve(moduleId, robotId) != "") return false;
            Get(moduleId).location = BuildPrefix + robotId;
            return true;
        }

        /// <summary>Takes a reserved spare back off the assembly onto a free spare slot.</summary>
        public string CanUnreserve(string robotId, ModuleSlot s)
            => ReservedFor(robotId, s) == null ? $"No spare {SlotName(s)} is fitted" : FreeBenchSlot() < 0 ? "The workbench spare slots are full" : "";

        public RobotModule Unreserve(string robotId, ModuleSlot s)
        {
            if (CanUnreserve(robotId, s) != "") return null;
            var m = ReservedFor(robotId, s);
            m.location = "bench:" + FreeBenchSlot();
            return m;
        }
        public int FreeBenchSlot()
        {
            for (int n = 0; n < BenchSlots; n++)
                if (!all.Any(m => m.BenchSlot == n)) return n;
            return -1;
        }
        /// <summary>The slots of a built chassis that have no module.</summary>
        public IEnumerable<ModuleSlot> Missing(string robotId) => SlotsOf(robotId).Where(s => At(robotId, s) == null);
        /// <summary>Has every required module (a chassis not built yet is governed by its part items).</summary>
        public bool Complete(string robotId) => !Built(robotId) || !Missing(robotId).Any();
        /// <summary>Are its modules its own factory set (or is it not built yet)? Then the robot draws exactly
        /// as before modules existed.</summary>
        public bool Factory(string robotId) => !Built(robotId) || SlotsOf(robotId).All(s => At(robotId, s)?.origin == robotId);

        /// <summary>All mounted modules wear one paint scheme.</summary>
        public bool Coordinated(string robotId) => FittedOn(robotId).Select(m => m.Scheme).Distinct().Count() == 1;

        // ------------------------------------------------------------------ building

        /// <summary>A chassis came online for the first time: its own modules leave the factory and are
        /// mounted on it. Returns false if it was already built (nothing changes).</summary>
        public bool OnFirstActivation(string robotId, double playTime, IReadOnlyDictionary<ModuleSlot, string> donors = null)
        {
            if (!IsChassis(robotId) || !_built.Add(robotId)) return false;
            foreach (var s in SlotsOf(robotId))
            {
                // the slot's module: its own, the one of the robot whose head or arm item was fitted, or a
                // spare taken off a built robot and fitted during assembly
                string from = donors != null && donors.TryGetValue(s, out var d) ? d : robotId;
                var m = ReservedFor(robotId, s) ?? Get(IdOf(s, from));
                if (m != null && m.origin != robotId) from = m.origin;
                if (m != null && (m.location == FactoryLoc || m.location == BuildPrefix + robotId)) { m.location = "robot:" + robotId; _waiting.Remove(m.id); }
                if (from != robotId && Get(IdOf(s, robotId)) is RobotModule own && own.location == FactoryLoc) _waiting.Add(own.id);
            }
            NoteBuilt(robotId, true, playTime);
            return true;
        }

        /// <summary>A chassis made operational directly (a loaded or QA-built robot): its own modules are
        /// mounted silently, no event (loading never counts as an action). A save's module data still wins.</summary>
        public void EnsureBuilt(string robotId)
        {
            if (!IsChassis(robotId) || !_built.Add(robotId)) return;
            foreach (var m in all)
                if (m.origin == robotId && m.location == FactoryLoc) m.location = "robot:" + robotId;
        }

        /// <summary>Records a completed build (first assembly, or an incomplete robot made whole again) and
        /// raises RobotBuilt. Only a note for future achievements: whether its modules share one paint scheme
        /// (colour coordinated), whether that scheme is the chassis' own and whether they are all its factory
        /// originals.</summary>
        void NoteBuilt(string robotId, bool first, double playTime)
        {
            bool coordinated = Coordinated(robotId);
            string scheme = coordinated ? FittedOn(robotId).First().Scheme : "mixed";
            var note = new BuildNote
            {
                robot = robotId, scheme = scheme, first = first, coordinated = coordinated,
                matchesChassis = coordinated && scheme == SchemeOf(robotId),
                original = FittedOn(robotId).All(m => m.origin == robotId), playTime = playTime,
            };
            builds.Add(note);
            int flags = (first ? 1 : 0) | (coordinated ? 2 : 0) | (note.matchesChassis ? 4 : 0) | (note.original ? 8 : 0);
            GameEvents.Raise(GameEventKind.RobotBuilt, "player", TypeOf(robotId), -1, scheme, robotId, flags);
        }

        static string TypeOf(string robotId) => IsHover(robotId) ? "hoverbot" : robotId;

        /// <summary>Raises AllRobotsOperational the first time all seven chassis are working at once.</summary>
        public void CheckAllOperational(System.Func<string, bool> operational)
        {
            if (_allSeven || !Chassis.All(c => operational(c) && Complete(c))) return;
            _allSeven = true;
            GameEvents.Raise(GameEventKind.AllRobotsOperational, "player", count: Chassis.Length);
        }

        // ------------------------------------------------------------------ edits (atomic)

        /// <summary>Why the module in that slot cannot go to the workbench, or "".</summary>
        public string CanRemove(string robotId, ModuleSlot s)
        {
            if (!Built(robotId)) return "That robot is not built yet";
            if (At(robotId, s) == null) return $"No {SlotName(s)} is fitted";
            if (FreeBenchSlot() < 0) return "The workbench spare slots are full";
            return "";
        }

        /// <summary>Takes a module off a robot into a free spare slot.</summary>
        public RobotModule Remove(string robotId, ModuleSlot s)
        {
            if (CanRemove(robotId, s) != "") return null;
            var m = At(robotId, s);
            bool wasComplete = Complete(robotId);
            m.location = "bench:" + FreeBenchSlot();
            GameEvents.Raise(GameEventKind.ModuleRemoved, "player", TypeOf(robotId), -1, m.id, robotId);
            if (wasComplete) GameEvents.Raise(GameEventKind.RobotIncomplete, "player", TypeOf(robotId), -1, m.id, robotId);
            return m;
        }

        /// <summary>Why that spare cannot be fitted to the robot, or "".</summary>
        public string CanInstall(string moduleId, string robotId)
        {
            var m = Get(moduleId);
            if (m == null || m.BenchSlot < 0) return "That module is not on the workbench";
            if (!Built(robotId)) return "That robot is not built yet";
            if (m.slot == ModuleSlot.Prop && !IsHover(robotId)) return "Propellers only fit a Hoverbot";
            if (!Fits(m, robotId, m.slot)) return "It does not fit that robot";
            return "";
        }

        /// <summary>Fits a spare. If the slot is taken, the module that was there takes the spare's place on
        /// the workbench in the same step (an exchange, never a silent replacement).</summary>
        public bool Install(string moduleId, string robotId, double playTime)
        {
            if (CanInstall(moduleId, robotId) != "") return false;
            var m = Get(moduleId);
            var old = At(robotId, m.slot);
            bool wasComplete = Complete(robotId);
            string benchLoc = m.location;
            m.location = "robot:" + robotId;
            if (old != null) old.location = benchLoc;
            GameEvents.Raise(GameEventKind.ModuleFitted, "player", TypeOf(robotId), -1, m.id, robotId);
            if (old != null) GameEvents.Raise(GameEventKind.ModuleRemoved, "player", TypeOf(robotId), -1, old.id, robotId);
            NoteBenchTransfer(m, robotId);
            if (!wasComplete && Complete(robotId)) NoteBuilt(robotId, false, playTime);
            return true;
        }

        /// <summary>Why a module still at the factory (its head or arm item found, not on any robot) cannot
        /// be fitted straight to a built robot, or "".</summary>
        public string CanFitFactory(string moduleId, string robotId, ModuleSlot s)
        {
            var m = Get(moduleId);
            if (m == null || m.location != FactoryLoc) return "That part is already on a robot";
            if (!Built(robotId)) return "That robot is not built yet";
            if (!Fits(m, robotId, s)) return "It does not fit that robot";
            if (At(robotId, s) != null && FreeBenchSlot() < 0) return "The workbench spare slots are full: take something off first";
            return "";
        }

        /// <summary>Fits a factory module (playtest 2026-10-25: found heads and arms go straight on a docked
        /// robot). A module already in the slot goes to a spare slot in the same step.</summary>
        public bool FitFactory(string moduleId, string robotId, ModuleSlot s, double playTime)
        {
            if (CanFitFactory(moduleId, robotId, s) != "") return false;
            var m = Get(moduleId);
            var old = At(robotId, s);
            bool wasComplete = Complete(robotId);
            if (old != null) old.location = "bench:" + FreeBenchSlot();
            m.location = "robot:" + robotId;
            _waiting.Remove(m.id);
            GameEvents.Raise(GameEventKind.ModuleFitted, "player", TypeOf(robotId), -1, m.id, robotId);
            if (old != null) GameEvents.Raise(GameEventKind.ModuleRemoved, "player", TypeOf(robotId), -1, old.id, robotId);
            NoteBenchTransfer(m, robotId);
            if (!wasComplete && Complete(robotId)) NoteBuilt(robotId, false, playTime);
            return true;
        }

        /// <summary>Why the two robots cannot exchange the modules in that slot, or "".</summary>
        public string CanSwap(string robotA, string robotB, ModuleSlot s)
        {
            if (robotA == robotB) return "Pick two different robots";
            if (!Built(robotA) || !Built(robotB)) return "Both robots must be built";
            if (s == ModuleSlot.Prop && (!IsHover(robotA) || !IsHover(robotB))) return "Propellers only move between Hoverbots";
            if (At(robotA, s) == null && At(robotB, s) == null) return $"Neither robot has a {SlotName(s)}";
            return "";
        }

        /// <summary>Exchanges the modules in one slot kind between two robots in one step (either side may be
        /// empty: then it is a plain transfer).</summary>
        public bool Swap(string robotA, string robotB, ModuleSlot s, double playTime)
        {
            if (CanSwap(robotA, robotB, s) != "") return false;
            var a = At(robotA, s);
            var b = At(robotB, s);
            bool aWas = Complete(robotA), bWas = Complete(robotB);
            if (a != null) a.location = "robot:" + robotB;
            if (b != null) b.location = "robot:" + robotA;
            if (a != null) GameEvents.Raise(GameEventKind.ModuleExchanged, "player", TypeOf(robotB), -1, a.id, robotB);
            if (b != null) GameEvents.Raise(GameEventKind.ModuleExchanged, "player", TypeOf(robotA), -1, b.id, robotA);
            if (!_transferred) { _transferred = true; GameEvents.Raise(GameEventKind.FirstModuleTransfer, "player", TypeOf(robotA), -1, (a ?? b).id, robotB); }
            foreach (var (r, was) in new[] { (robotA, aWas), (robotB, bWas) })
            {
                if (was && !Complete(r)) GameEvents.Raise(GameEventKind.RobotIncomplete, "player", TypeOf(r), -1, "", r);
                else if (!was && Complete(r)) NoteBuilt(r, false, playTime);
            }
            return true;
        }

        /// <summary>A spare fitted to a robot other than its origin also counts as a cross-robot transfer.</summary>
        void NoteBenchTransfer(RobotModule m, string robotId)
        {
            if (_transferred || m.origin == robotId) return;
            _transferred = true;
            GameEvents.Raise(GameEventKind.FirstModuleTransfer, "player", TypeOf(robotId), -1, m.id, robotId);
        }

        // ------------------------------------------------------------------ conservation

        /// <summary>The invariants of spec §10, as a list of violations (empty when sound): the exact supply,
        /// every module in exactly one legal place, no slot or spare slot holding two, factory modules only
        /// for unbuilt origins.</summary>
        public List<string> Audit()
        {
            var e = new List<string>();
            int Count(ModuleSlot s) => all.Count(m => m.slot == s);
            if (Count(ModuleSlot.Head) != 7 || Count(ModuleSlot.ArmL) != 7 || Count(ModuleSlot.ArmR) != 7 || Count(ModuleSlot.Prop) != 3)
                e.Add($"supply is {Count(ModuleSlot.Head)}/{Count(ModuleSlot.ArmL)}/{Count(ModuleSlot.ArmR)}/{Count(ModuleSlot.Prop)} heads/left/right/propellers, not 7/7/7/3");
            if (all.Select(m => m.id).Distinct().Count() != all.Count || _byId.Count != all.Count) e.Add("duplicate module ids");
            foreach (var m in all)
            {
                if (m.location == FactoryLoc) { if (Built(m.origin) && !_waiting.Contains(m.id)) e.Add($"{m.id} still at the factory though {m.origin} is built"); }
                else if (m.MountedOn is string r) { if (!Fits(m, r, m.slot)) e.Add($"{m.id} illegally mounted on {r}"); else if (!Built(r)) e.Add($"{m.id} mounted on unbuilt {r}"); }
                else if (m.location.StartsWith(BuildPrefix)) { var b = m.location.Substring(BuildPrefix.Length); if (Built(b) || !Fits(m, b, m.slot)) e.Add($"{m.id} reserved for {b}, which cannot take it"); }
                else if (m.BenchSlot >= BenchSlots) e.Add($"{m.id} in a spare slot that does not exist");
                else if (m.BenchSlot < 0) e.Add($"{m.id} has an unknown location '{m.location}'");
            }
            foreach (var g in all.Where(m => m.location != FactoryLoc).GroupBy(m => m.MountedOn != null || m.location.StartsWith(BuildPrefix) ? m.location + "/" + m.slot : m.location))
                if (g.Count() > 1) e.Add($"{g.Key} holds {string.Join(", ", g.Select(m => m.id))}");
            return e;
        }

        // ------------------------------------------------------------------ save

        public Dictionary<string, object> ToDict() => new()
        {
            ["version"] = (double)Version,
            ["built"] = _built.OrderBy(x => x).Select(x => (object)x).ToList(),
            ["waiting"] = _waiting.OrderBy(x => x).Select(x => (object)x).ToList(),
            ["transferred"] = _transferred,
            ["all_seven"] = _allSeven,
            ["locations"] = all.ToDictionary(m => m.id, m => (object)m.location),
            ["builds"] = builds.Select(b => (object)new Dictionary<string, object>
            {
                ["robot"] = b.robot, ["scheme"] = b.scheme, ["first"] = b.first, ["coordinated"] = b.coordinated,
                ["matches_chassis"] = b.matchesChassis, ["original"] = b.original, ["play_time_s"] = b.playTime,
            }).ToList(),
        };

        /// <summary>Restores from a save. A save from before modules (no "modules" key) is migrated
        /// deterministically and silently (no events): each operational chassis is built with its own factory
        /// modules. Unknown ids and illegal locations are dropped back to a legal place, never duplicated.</summary>
        public void Load(Dictionary<string, object> d, IEnumerable<Robot> robots)
        {
            if (d == null)
            {
                foreach (var r in robots)
                    if (r.Operational && IsChassis(r.id) && _built.Add(r.id))
                        foreach (var m in all.Where(m => m.origin == r.id)) m.location = "robot:" + r.id;
                return;
            }
            _built.Clear();
            _waiting.Clear();
            foreach (var x in Json.Arr(d.GetValueOrDefault("built"))) if (x is string s && IsChassis(s)) _built.Add(s);
            foreach (var x in Json.Arr(d.GetValueOrDefault("waiting"))) if (x is string s && _byId.ContainsKey(s)) _waiting.Add(s);
            _transferred = Json.Bool(d, "transferred");
            _allSeven = Json.Bool(d, "all_seven");
            var locs = Json.Obj(d.GetValueOrDefault("locations")) ?? new Dictionary<string, object>();
            var taken = new HashSet<string>();
            foreach (var m in all)
            {
                string loc = locs.GetValueOrDefault(m.id) as string ?? "";
                string key = loc.StartsWith("robot:") || loc.StartsWith(BuildPrefix) ? loc + "/" + m.slot : loc;
                bool legal = loc == FactoryLoc ? !Built(m.origin) || _waiting.Contains(m.id)
                    : loc.StartsWith("robot:") ? Fits(m, loc.Substring(6), m.slot) && Built(loc.Substring(6))
                    : loc.StartsWith(BuildPrefix) ? Fits(m, loc.Substring(BuildPrefix.Length), m.slot) && !Built(loc.Substring(BuildPrefix.Length))
                    : loc.StartsWith("bench:") && int.TryParse(loc.Substring(6), out int n) && n >= 0 && n < BenchSlots;
                if (legal && (loc == FactoryLoc || taken.Add(key))) { m.location = loc; continue; }   // the factory holds any number
                // repair: its origin's slot if free, else a spare slot, else the factory for an unbuilt origin
                m.location = Built(m.origin) ? "" : FactoryLoc;
            }
            foreach (var m in all.Where(m => m.location == ""))
            {
                string home = "robot:" + m.origin;
                if (taken.Add(home + "/" + m.slot)) { m.location = home; continue; }
                int n = FreeBenchSlot();
                m.location = n >= 0 ? "bench:" + n : home;   // (cannot happen with 12 slots and 24 modules spread over 7 robots)
            }
            foreach (var b in Json.Arr(d.GetValueOrDefault("builds")))
                if (Json.Obj(b) is Dictionary<string, object> bd)
                    builds.Add(new BuildNote
                    {
                        robot = Json.Str(bd, "robot"), scheme = Json.Str(bd, "scheme"), first = Json.Bool(bd, "first"),
                        coordinated = Json.Bool(bd, "coordinated"), matchesChassis = Json.Bool(bd, "matches_chassis"),
                        original = Json.Bool(bd, "original"), playTime = Json.Num(bd, "play_time_s"),
                    });
        }
    }
}
