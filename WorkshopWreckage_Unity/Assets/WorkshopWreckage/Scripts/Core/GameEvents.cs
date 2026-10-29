using System;
using System.Collections.Generic;
using System.Linq;

namespace WorkshopWreckage
{
    /// <summary>What happened (roadmap §3A: achievement-ready from the robot phases on). Raised only at
    /// the authoritative commit point of an action, never when its animation starts.</summary>
    public enum GameEventKind
    {
        PartSorted,        // a part was accepted by its storage destination (StorageSystem.Deposit Ok)
        PartStaged,        // a robot set a part down in a destination's staging zone (still unsorted)
        PartPickedUp,      // a robot gripped a part and it became its cargo
        PartDiscovered,    // the first part of a definition was found
        RobotActivated,    // a robot (or the Sorting Arm) came online after assembly
        RobotUpgraded,     // a robot upgrade was bought (robotType = the type)
        RobotTripCompleted,// a robot emptied its load (deposited and/or staged everything it carried)
        LevelReached,      // the player reached a level (count = the level)
        ModuleFitted,      // a head / arm / propeller module was fitted to a robot (category = module id, destination = robot id)
        RobotSummoned,     // a robot was called to the workbench (Modify Robot; destination = robot id)
        ModuleRemoved,     // a module went from a robot to a workbench spare slot (category = module id, destination = robot id)
        ModuleExchanged,   // a module moved directly from one robot to another (destination = the receiving robot id)
        FirstModuleTransfer, // the first time a module was fitted to a robot other than its origin
        RobotIncomplete,   // a built robot lost a required module (it stays parked at the workbench)
        RobotBuilt,        // a chassis became complete: first build or repair. category = paint scheme ("mixed" if not
                           // colour coordinated), destination = robot id, count = flags 1 first, 2 colour coordinated,
                           // 4 the chassis' own scheme, 8 all factory originals. A note only: no achievements yet
        AllRobotsOperational, // all seven chassis were operational at the same time
    }

    /// <summary>One gameplay event: stable identifiers only (no object references), so a listener can
    /// keep it, count it or persist it. Empty strings / -1 where a field does not apply.</summary>
    public readonly struct GameEvent
    {
        public readonly GameEventKind kind;
        /// <summary>Who did it: "player", or "robot:&lt;robot id&gt;".</summary>
        public readonly string actor;
        /// <summary>The robot's chassis type (retrobot, unibot, ...) when a robot is involved.</summary>
        public readonly string robotType;
        public readonly int partDef;
        public readonly string category, destination;
        /// <summary>A further count or value (the level, a trip's part count, ...).</summary>
        public readonly int count;
        /// <summary>Unique within a save (GameStats keeps the counter), for idempotent listeners.</summary>
        public readonly long opId;

        public GameEvent(GameEventKind kind, string actor, string robotType, int partDef, string category, string destination, int count, long opId)
        {
            this.kind = kind; this.actor = actor ?? ""; this.robotType = robotType ?? ""; this.partDef = partDef;
            this.category = category ?? ""; this.destination = destination ?? ""; this.count = count; this.opId = opId;
        }

        public override string ToString() => $"{kind} actor={actor} robot={robotType} def={partDef} cat={category} dest={destination} n={count} op={opId}";
    }

    /// <summary>The single gameplay event channel (no Steam, no UI). The game raises; listeners
    /// (GameStats now, an achievement provider later) subscribe. Raising is cheap and allocation-free
    /// apart from the listeners themselves.</summary>
    public static class GameEvents
    {
        public static event Action<GameEvent> Raised;
        /// <summary>The counters that number events (set by the game for the current world).</summary>
        public static GameStats stats;

        public static void Raise(GameEventKind kind, string actor, string robotType = "", int partDef = -1, string category = "", string destination = "", int count = 0)
        {
            var e = new GameEvent(kind, actor, robotType, partDef, category, destination, count, stats != null ? stats.NextOp() : -1);
            stats?.Record(e);
            Raised?.Invoke(e);
        }
    }

    /// <summary>Durable gameplay counters for future achievements (persisted in the save as "game_stats").
    /// Only counts what the events report: totals per kind, per actor kind (player / robot type), per
    /// category and per destination for sorting. Old saves start from what the world already shows (the
    /// parts already stored count as sorted, unattributed); nothing is counted twice on reload because
    /// loading never raises events.</summary>
    public class GameStats
    {
        public const int Version = 1;
        public readonly Dictionary<string, long> counters = new();
        long _nextOp = 1;
        /// <summary>True when the counters were initialised from an old save's state rather than recorded.</summary>
        public bool seededFromLegacy;

        public long NextOp() => _nextOp++;
        public long Get(string key) => counters.TryGetValue(key, out var v) ? v : 0;
        void Add(string key, long n = 1) => counters[key] = Get(key) + n;

        public void Record(GameEvent e)
        {
            string k = e.kind.ToString();
            Add(k);
            string who = e.actor.StartsWith("robot") ? "robot:" + (e.robotType != "" ? e.robotType : "?") : e.actor;
            if (who != "") Add(k + "/" + who);
            switch (e.kind)
            {
                case GameEventKind.PartSorted:
                    if (e.category != "") Add(k + "/category:" + e.category);
                    break;
                case GameEventKind.RobotTripCompleted:
                    Add(k + "/parts/" + who, e.count);
                    break;
                case GameEventKind.RobotBuilt:
                    Add(k + ((e.count & 1) != 0 ? "/first" : "/repair"));
                    if ((e.count & 2) != 0) { Add(k + "/coordinated"); Add(k + "/coordinated/" + e.category); }
                    if ((e.count & 4) != 0) Add(k + "/coordinated_chassis");
                    if ((e.count & 8) != 0) Add(k + "/original");
                    if (e.destination != "") Add(k + "/robot:" + e.destination);
                    break;
                case GameEventKind.LevelReached:
                    counters["level_max"] = Math.Max(Get("level_max"), e.count);
                    break;
            }
        }

        public Dictionary<string, object> ToDict() => new()
        {
            ["version"] = (double)Version,
            ["next_op"] = (double)_nextOp,
            ["legacy_seeded"] = seededFromLegacy,
            ["counters"] = counters.ToDictionary(kv => kv.Key, kv => (object)(double)kv.Value),
        };

        public static GameStats FromDict(Dictionary<string, object> d)
        {
            var s = new GameStats();
            if (d == null) return s;
            s._nextOp = Math.Max(1, (long)Json.Num(d, "next_op", 1));
            s.seededFromLegacy = Json.Bool(d, "legacy_seeded");
            foreach (var kv in Json.Obj(d.GetValueOrDefault("counters")) ?? new Dictionary<string, object>()) s.counters[kv.Key] = (long)(double)kv.Value;
            return s;
        }

        /// <summary>A save from before the counters existed: the parts already in storage count as sorted
        /// (not attributed to player or robots), the reached level as the best level. Nothing else is guessed.</summary>
        public static GameStats SeedFromLegacy(int storedParts, int level)
        {
            var s = new GameStats { seededFromLegacy = true };
            if (storedParts > 0) { s.counters["PartSorted"] = storedParts; s.counters["PartSorted/unattributed"] = storedParts; }
            if (level > 1) s.counters["level_max"] = level;
            return s;
        }
    }
}
