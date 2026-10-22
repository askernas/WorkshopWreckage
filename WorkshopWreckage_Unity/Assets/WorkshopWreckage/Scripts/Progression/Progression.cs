using System;
using System.Collections.Generic;
using System.Linq;

namespace WorkshopWreckage
{
    /// <summary>One skill from progression.json.</summary>
    public class SkillDef
    {
        public string id, name, group, status;
        public int unlockLevel, cost;
        public List<string> requires = new();
        public Dictionary<string, double> effects = new();
        public bool Active => status == "active";
    }

    /// <summary>One robot upgrade from progression.json robot_upgrades (model level:
    /// every robot of that type shares it).</summary>
    public class RobotUpgradeDef
    {
        public string id, robot, name, description, stat, mode, status;
        public int maxRank;
        public List<int> costPerRank = new();
        public double effectPerRank;
        public Dictionary<string, double> also = new(), alsoPercent = new();
        public bool Active => status == "active";
        public int CostOf(int rank) => costPerRank.Count == 0 ? 1 : costPerRank[Math.Min(rank, costPerRank.Count - 1)];
    }

    /// <summary>Player XP, levels and skills. Engine independent (no UnityEngine):
    /// all numbers come from shared/data/progression.json, the game only reports
    /// events. Anti-exploit: sorting XP is granted once per instance, discovery once
    /// per definition, every other reward once per key; pickups and drops never
    /// grant XP, so juggling items earns nothing.</summary>
    public class Progression
    {
        public readonly int maxLevel;
        readonly double _curveBase, _curveExp, _presetScale;
        readonly int _pointsPerLevel;
        readonly Dictionary<int, int> _bonusPoints = new();
        readonly Dictionary<string, double> _rewards = new();
        public readonly List<SkillDef> skills = new();
        public readonly Dictionary<string, object> supermagnet;

        public int level = 1;
        public long xp;               // total earned
        public int skillPoints;
        public readonly HashSet<string> unlocked = new();
        readonly HashSet<int> _sortRewarded = new(), _discoverRewarded = new();
        readonly HashSet<string> _onceRewarded = new();

        public event Action<int> LeveledUp;                 // new level
        public event Action<long, string> XpGained;         // amount, reason

        public Progression(Dictionary<string, object> cfg, string preset)
        {
            var lv = Json.Obj(cfg["levels"]);
            maxLevel = Json.Int(lv, "max_level", 25);
            var curve = Json.Obj(lv["xp_curve"]);
            _curveBase = Json.Num(curve, "base", 60);
            _curveExp = Json.Num(curve, "exponent", 1.5);
            _pointsPerLevel = Json.Int(lv, "skill_points_per_level", 1);
            foreach (var kv in Json.Obj(lv.GetValueOrDefault("bonus_skill_points")) ?? new Dictionary<string, object>())
                _bonusPoints[int.Parse(kv.Key)] = (int)(double)kv.Value;
            foreach (var kv in Json.Obj(cfg["xp_rewards"])) _rewards[kv.Key] = (double)kv.Value;
            _presetScale = Json.Num(Json.Obj(cfg.GetValueOrDefault("preset_xp_scale")) ?? new Dictionary<string, object>(), preset, 1.0);
            foreach (var o in Json.Arr(cfg["skills"]))
            {
                var d = Json.Obj(o);
                var s = new SkillDef
                {
                    id = Json.Str(d, "id"), name = Json.Str(d, "name"), group = Json.Str(d, "group"), status = Json.Str(d, "status", "planned"),
                    unlockLevel = Unlocks.Skill(Json.Str(d, "id")), cost = Json.Int(d, "cost", 1),
                    requires = Json.Arr(d.GetValueOrDefault("requires")).Select(x => x.ToString()).ToList(),
                };
                foreach (var kv in Json.Obj(d["effects"])) s.effects[kv.Key] = (double)kv.Value;
                skills.Add(s);
            }
            supermagnet = Json.Obj(cfg.GetValueOrDefault("supermagnet")) ?? new Dictionary<string, object>();
            foreach (var o in Json.Arr(cfg.GetValueOrDefault("robot_upgrades")))
            {
                var d = Json.Obj(o);
                var u = new RobotUpgradeDef
                {
                    id = Json.Str(d, "id"), robot = Json.Str(d, "robot"), name = Json.Str(d, "name"), description = Json.Str(d, "description"),
                    stat = Json.Str(d, "stat"), mode = Json.Str(d, "mode", "add"), status = Json.Str(d, "status", "planned"),
                    maxRank = Json.Int(d, "max_rank", 1), effectPerRank = Json.Num(d, "effect_per_rank"),
                    costPerRank = Json.Arr(d.GetValueOrDefault("cost_per_rank")).Select(x => (int)(double)x).ToList(),
                };
                foreach (var kv in Json.Obj(d.GetValueOrDefault("also")) ?? new Dictionary<string, object>()) u.also[kv.Key] = (double)kv.Value;
                foreach (var kv in Json.Obj(d.GetValueOrDefault("also_percent")) ?? new Dictionary<string, object>()) u.alsoPercent[kv.Key] = (double)kv.Value;
                robotUpgrades.Add(u);
            }
            var rs = Json.Obj(cfg.GetValueOrDefault("respec")) ?? new Dictionary<string, object>();
            respecLevel = Json.Str(rs, "status", "planned") == "active" ? Unlocks.Respec : int.MaxValue;
        }

        // ------------------------------------------------------------------ robot upgrades

        public readonly List<RobotUpgradeDef> robotUpgrades = new();
        public readonly Dictionary<string, int> upgradeRanks = new();
        public readonly int respecLevel;
        public event Action<string> RobotUpgraded;   // robot type

        public RobotUpgradeDef Upgrade(string id) => robotUpgrades.FirstOrDefault(u => u.id == id);
        public int RankOf(string upgradeId) => upgradeRanks.GetValueOrDefault(upgradeId);
        public int RanksBought => upgradeRanks.Values.Sum();

        /// <summary>Empty string if the next rank can be bought now, otherwise the reason.</summary>
        public string CanBuyUpgrade(string id, bool robotOperational)
        {
            var u = Upgrade(id);
            if (u == null) return "Unknown upgrade";
            if (!u.Active) return "Coming in a later milestone";
            if (!robotOperational) return "Build the robot first";
            int r = RankOf(id);
            if (r >= u.maxRank) return "Maximum rank";
            if (skillPoints < u.CostOf(r)) return $"Needs {u.CostOf(r)} skill point{(u.CostOf(r) == 1 ? "" : "s")}";
            return "";
        }

        public bool BuyUpgrade(string id, bool robotOperational)
        {
            if (CanBuyUpgrade(id, robotOperational) != "") return false;
            var u = Upgrade(id);
            skillPoints -= u.CostOf(RankOf(id));
            upgradeRanks[id] = RankOf(id) + 1;
            RobotUpgraded?.Invoke(u.robot);
            return true;
        }

        /// <summary>Respec: every point spent on robot upgrades back (from respecLevel).</summary>
        public int RefundRobotUpgrades()
        {
            if (level < respecLevel) return 0;
            int refund = 0;
            foreach (var kv in upgradeRanks)
            {
                var u = Upgrade(kv.Key);
                for (int r = 0; r < kv.Value; r++) refund += u?.CostOf(r) ?? 0;
            }
            var types = upgradeRanks.Keys.Select(k => Upgrade(k)?.robot).Where(t => t != null).Distinct().ToList();
            upgradeRanks.Clear();
            skillPoints += refund;
            foreach (var t in types) RobotUpgraded?.Invoke(t);
            return refund;
        }

        /// <summary>A robot type's stat after its upgrades: add (base + ranks * effect),
        /// percent (base * (1 + ranks * effect)) or rank (ranks * effect), including the
        /// secondary "also" effects of other upgrades, capped by the permanent `limit`.</summary>
        public double RobotStat(string robot, string stat, double baseValue, double limit = double.PositiveInfinity)
        {
            double add = 0, pct = 0;
            foreach (var u in robotUpgrades)
            {
                if (u.robot != robot || !u.Active) continue;
                int r = RankOf(u.id);
                if (r == 0) continue;
                if (u.stat == stat) { if (u.mode == "percent") pct += r * u.effectPerRank; else add += r * u.effectPerRank; }
                if (u.also.TryGetValue(stat, out var a)) add += r * a;
                if (u.alsoPercent.TryGetValue(stat, out var p)) pct += r * p;
            }
            double v = (baseValue + add) * (1 + pct);
            return Math.Min(v, limit);
        }

        /// <summary>Configuration problems (shown like catalog errors; never silently fixed).</summary>
        public List<string> Validate()
        {
            var e = new List<string>();
            var ids = new HashSet<string>();
            foreach (var s in skills)
            {
                if (!ids.Add(s.id)) e.Add($"Skill '{s.id}' is defined twice");
                if (s.unlockLevel < 1 || s.unlockLevel > maxLevel) e.Add($"Skill '{s.id}': unlock_level {s.unlockLevel} outside 1..{maxLevel}");
                if (s.cost < 0) e.Add($"Skill '{s.id}': negative cost");
            }
            foreach (var s in skills)
                foreach (var r in s.requires)
                {
                    var req = skills.FirstOrDefault(x => x.id == r);
                    if (req == null) e.Add($"Skill '{s.id}' requires unknown skill '{r}'");
                    else if (req.unlockLevel > s.unlockLevel) e.Add($"Skill '{s.id}' (level {s.unlockLevel}) requires '{r}' which unlocks later (level {req.unlockLevel})");
                }
            var uids = new HashSet<string>();
            foreach (var u in robotUpgrades)
            {
                if (!uids.Add(u.id)) e.Add($"Robot upgrade '{u.id}' is defined twice");
                if (u.maxRank < 1) e.Add($"Robot upgrade '{u.id}': max_rank must be at least 1");
                if (u.costPerRank.Count != u.maxRank) e.Add($"Robot upgrade '{u.id}': cost_per_rank needs {u.maxRank} entries");
                if (!new[] { "add", "percent", "rank" }.Contains(u.mode)) e.Add($"Robot upgrade '{u.id}': unknown mode '{u.mode}'");
            }
            int points = Enumerable.Range(2, maxLevel - 1).Sum(PointsAt);
            int total = skills.Sum(s => s.cost);
            if (points < total * 0.6) e.Add($"Only {points} skill points over {maxLevel} levels for {total} points of skills");
            return e;
        }

        int PointsAt(int lvl) => _pointsPerLevel + _bonusPoints.GetValueOrDefault(lvl);

        /// <summary>XP needed to advance from `lvl` to `lvl + 1`.</summary>
        public long XpForLevel(int lvl) => lvl >= maxLevel ? long.MaxValue : (long)Math.Round(_curveBase * Math.Pow(lvl, _curveExp));

        public long XpAtLevelStart(int lvl)
        {
            long s = 0;
            for (int l = 1; l < lvl; l++) s += XpForLevel(l);
            return s;
        }

        public long TotalXpForMax => XpAtLevelStart(maxLevel);

        /// <summary>XP from ordinary collection work alone when `fraction` of the workshop's
        /// parts are sorted and the same fraction of its sets completed (balance check:
        /// every preset must reach every blueprint level this way).</summary>
        public long CollectionXp(int ordinaryItems, int ordinaryDefs, double fraction)
            => (long)((ordinaryItems * (Reward("sort_item") + Reward("complete_definition_per_copy")) + ordinaryDefs * (Reward("complete_definition") + Reward("discover_definition"))) * fraction);
        /// <summary>XP from sorting alone after `fraction` of all parts, sorted in a random broad
        /// order (the real rules: per-part XP, discovery, partial set credit, set completion; the
        /// same model as tools/audit/progression_sim.py style A without objectives or zones).</summary>
        public long SimulatedCollectionXp(IList<int> copiesPerDef, double fraction, int seed = 1)
        {
            var order = new List<int>();
            for (int d = 0; d < copiesPerDef.Count; d++) for (int k = 0; k < copiesPerDef[d]; k++) order.Add(d);
            var rng = new System.Random(seed);
            for (int i = order.Count - 1; i > 0; i--) { int j = rng.Next(i + 1); (order[i], order[j]) = (order[j], order[i]); }
            var have = new int[copiesPerDef.Count];
            double x = 0, share = HalfShare;
            int n = (int)(order.Count * fraction);
            for (int i = 0; i < n; i++)
            {
                int d = order[i], total = copiesPerDef[d];
                if (have[d] == 0) x += Reward("discover_definition");
                x += Reward("sort_item");
                have[d]++;
                double set = SetXp(total);
                if (total >= 2 && share > 0 && have[d] == (total + 1) / 2 && have[d] < total) x += set * share;
                if (have[d] == total) x += total >= 2 && share > 0 ? set * (1 - share) : set;
            }
            return (long)x;
        }

        public long XpIntoLevel => xp - XpAtLevelStart(level);
        public float LevelFraction => level >= maxLevel ? 1f : (float)XpIntoLevel / XpForLevel(level);

        // ------------------------------------------------------------------ events

        public void OnSorted(int instanceId, bool robotAssisted)
        {
            if (!_sortRewarded.Add(instanceId)) return;
            Grant(Reward(robotAssisted ? "sort_item_robot_assisted" : "sort_item"), "sorted");
        }

        double HalfShare => _rewards.GetValueOrDefault("half_definition_share");   // a fraction: not preset-scaled
        double SetXp(int copies) => Reward("complete_definition") + Reward("complete_definition_per_copy") * copies;

        /// <summary>Partial set credit: half of a set's copies stored pays half_definition_share
        /// of its completion XP (once); the rest comes with the completion.</summary>
        public void OnDefinitionHalf(int defId, int copies)
        {
            double share = HalfShare;
            if (share <= 0 || copies < 2 || _onceRewarded.Contains("def:" + defId) || !_onceRewarded.Add("half:" + defId)) return;
            Grant(SetXp(copies) * share, "half a set");
        }

        public void OnDefinitionCompleted(int defId, int copies)
        {
            if (!_onceRewarded.Add("def:" + defId)) return;
            double share = _onceRewarded.Contains("half:" + defId) ? HalfShare : 0;
            Grant(SetXp(copies) * (1 - share), "set complete");
        }

        public void OnZoneRestored(int zone, int percentStep)
        {
            if (!_onceRewarded.Add($"zone:{zone}:{percentStep}")) return;
            Grant(Reward("zone_restored_" + percentStep), "zone restored");
        }

        public void OnDiscovered(int defId, bool robotPart)
        {
            if (!_discoverRewarded.Add(defId)) return;
            Grant(Reward(robotPart ? "discover_robot_part" : "discover_definition"), robotPart ? "robot part" : "discovery");
        }

        public void OnRobotRepaired(string robot)
        {
            if (_onceRewarded.Add("robot:" + robot)) Grant(Reward("robot_repaired"), "robot repaired");
        }

        public void OnObjective(string key)
        {
            if (_onceRewarded.Add("obj:" + key)) Grant(Reward("objective_completed"), "objective");
        }

        double Reward(string key) => _rewards.GetValueOrDefault(key) * _presetScale;

        // fractional XP accumulates so small scaled rewards are never lost
        double _fraction;

        void Grant(double amount, string reason)
        {
            _fraction += amount;
            long whole = (long)Math.Floor(_fraction);
            if (whole <= 0) return;
            _fraction -= whole;
            xp += whole;
            XpGained?.Invoke(whole, reason);
            while (level < maxLevel && xp >= XpAtLevelStart(level + 1))
            {
                level++;
                skillPoints += PointsAt(level);
                LeveledUp?.Invoke(level);
            }
        }

        // ------------------------------------------------------------------ skills

        public SkillDef Skill(string id) => skills.FirstOrDefault(s => s.id == id);

        /// <summary>Empty string if the skill can be unlocked now, otherwise the reason.</summary>
        public string CanUnlock(string id)
        {
            var s = Skill(id);
            if (s == null) return "Unknown skill";
            if (unlocked.Contains(id)) return "Already unlocked";
            if (level < s.unlockLevel) return $"Requires level {s.unlockLevel}";
            var missing = s.requires.Where(r => !unlocked.Contains(r)).ToList();
            if (missing.Count > 0) return "Requires " + string.Join(", ", missing.Select(r => Skill(r)?.name ?? r));
            if (skillPoints < s.cost) return $"Needs {s.cost} skill point{(s.cost == 1 ? "" : "s")}";
            return "";
        }

        public bool Unlock(string id)
        {
            if (CanUnlock(id) != "") return false;
            skillPoints -= Skill(id).cost;
            unlocked.Add(id);
            return true;
        }

        /// <summary>Sum of an effect over unlocked skills that the build implements.</summary>
        public double Effect(string name) => skills.Where(s => s.Active && unlocked.Contains(s.id)).Sum(s => s.effects.GetValueOrDefault(name));

        public bool Has(string effect) => Effect(effect) > 0;

        // ------------------------------------------------------------------ save

        public Dictionary<string, object> ToDict() => new()
        {
            ["level"] = (double)level, ["xp"] = (double)xp, ["fraction"] = _fraction, ["skill_points"] = (double)skillPoints,
            ["unlocked"] = unlocked.Select(x => (object)x).ToList(),
            ["sort_rewarded"] = _sortRewarded.Select(x => (object)(double)x).ToList(),
            ["discover_rewarded"] = _discoverRewarded.Select(x => (object)(double)x).ToList(),
            ["once"] = _onceRewarded.Select(x => (object)x).ToList(),
            ["robot_upgrades"] = upgradeRanks.ToDictionary(kv => kv.Key, kv => (object)(double)kv.Value),
        };

        public void Load(Dictionary<string, object> d)
        {
            if (d == null) return;
            level = Json.Int(d, "level", 1);
            xp = (long)Json.Num(d, "xp");
            _fraction = Json.Num(d, "fraction");
            skillPoints = Json.Int(d, "skill_points");
            unlocked.Clear(); _sortRewarded.Clear(); _discoverRewarded.Clear(); _onceRewarded.Clear();
            foreach (var x in Json.Arr(d.GetValueOrDefault("unlocked"))) unlocked.Add(x.ToString());
            foreach (var x in Json.Arr(d.GetValueOrDefault("sort_rewarded"))) _sortRewarded.Add((int)(double)x);
            foreach (var x in Json.Arr(d.GetValueOrDefault("discover_rewarded"))) _discoverRewarded.Add((int)(double)x);
            foreach (var x in Json.Arr(d.GetValueOrDefault("once"))) _onceRewarded.Add(x.ToString());
            upgradeRanks.Clear();
            foreach (var kv in Json.Obj(d.GetValueOrDefault("robot_upgrades")) ?? new Dictionary<string, object>()) upgradeRanks[kv.Key] = (int)(double)kv.Value;
        }
    }

    /// <summary>Exact-match Supermagnet selection rule (late-game tool, specified in
    /// docs/DESIGN.md). Pure function so it can be tested and reused by any front end:
    /// returns the instance ids to pull, nearest first.</summary>
    public static class Supermagnet
    {
        public struct Candidate
        {
            public int id, defId;
            public bool inWorld;          // UNSORTED and lying in the world (not carried, stored or reserved)
            public bool reservedByRobot;
            public bool robotPart;
            public float distance;
            public bool lineOfSight;
        }

        public static List<int> Select(int heldDefId, IEnumerable<Candidate> candidates, Dictionary<string, object> cfg, int inventorySpace)
        {
            float radius = (float)Json.Num(cfg, "radius_m", 6);
            int max = Json.Int(cfg, "max_items_per_activation", 25);
            if (Json.Bool(cfg, "respect_inventory_capacity", true) && !Json.Bool(cfg, "overflow_to_buffer")) max = Math.Min(max, Math.Max(0, inventorySpace));
            bool robots = Json.Bool(cfg, "include_robot_parts"), los = Json.Bool(cfg, "line_of_sight_required", true);
            return candidates
                .Where(c => c.defId == heldDefId && c.inWorld && !c.reservedByRobot && (robots || !c.robotPart) && c.distance <= radius && (!los || c.lineOfSight))
                .OrderBy(c => c.distance).Take(max).Select(c => c.id).Distinct().ToList();
        }
    }
}
