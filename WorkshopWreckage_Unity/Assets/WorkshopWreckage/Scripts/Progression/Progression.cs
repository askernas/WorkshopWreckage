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
                    unlockLevel = Json.Int(d, "unlock_level", 1), cost = Json.Int(d, "cost", 1),
                    requires = Json.Arr(d.GetValueOrDefault("requires")).Select(x => x.ToString()).ToList(),
                };
                foreach (var kv in Json.Obj(d["effects"])) s.effects[kv.Key] = (double)kv.Value;
                skills.Add(s);
            }
            supermagnet = Json.Obj(cfg.GetValueOrDefault("supermagnet")) ?? new Dictionary<string, object>();
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
        public long XpIntoLevel => xp - XpAtLevelStart(level);
        public float LevelFraction => level >= maxLevel ? 1f : (float)XpIntoLevel / XpForLevel(level);

        // ------------------------------------------------------------------ events

        public void OnSorted(int instanceId, bool robotAssisted)
        {
            if (!_sortRewarded.Add(instanceId)) return;
            Grant(Reward(robotAssisted ? "sort_item_robot_assisted" : "sort_item"), "sorted");
        }

        public void OnDefinitionCompleted(int defId, int copies)
        {
            if (!_onceRewarded.Add("def:" + defId)) return;
            Grant(Reward("complete_definition") + Reward("complete_definition_per_copy") * copies, "set complete");
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
