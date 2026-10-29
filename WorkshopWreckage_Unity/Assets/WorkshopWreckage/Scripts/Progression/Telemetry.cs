using System.Collections.Generic;
using System.Linq;

namespace WorkshopWreckage
{
    /// <summary>Balancing telemetry (engine independent; the caller supplies play
    /// time in seconds). Answers: items sorted per minute, manual vs robot-assisted
    /// sorting, time collecting vs depositing, skill usage, level progression and
    /// workshop completion time — per preset, so Standard/Large/Massive can be compared.</summary>
    public class Telemetry
    {
        public readonly string preset;
        public readonly int seed, totalItems;
        public int sortedManual, sortedRobot, pickups, wrongDeposits;
        public double collectingS, depositingS, idleS;
        public double completedAtS = -1;
        readonly Dictionary<string, int> _skillUses = new();
        readonly List<object> _levels = new(), _samples = new(), _skills = new();
        double _lastSample;
        string _activity = "idle";

        public Telemetry(string preset, int seed, int totalItems)
        {
            this.preset = preset; this.seed = seed; this.totalItems = totalItems;
        }

        /// <summary>Accumulates time in the current activity: "collecting" while the
        /// inventory has room and the player is picking, "depositing" while carrying
        /// toward storage, otherwise "idle".</summary>
        public void Tick(double dt, string activity)
        {
            _activity = activity;
            if (activity == "collecting") collectingS += dt;
            else if (activity == "depositing") depositingS += dt;
            else idleS += dt;
        }

        public void Pickup() => pickups++;
        public void WrongDeposit() => wrongDeposits++;

        public void Sorted(bool robot, double t, int sortedTotal)
        {
            if (robot) sortedRobot++; else sortedManual++;
            if (sortedTotal >= totalItems && completedAtS < 0) completedAtS = t;
        }

        public void SkillUsed(string skill) => _skillUses[skill] = _skillUses.GetValueOrDefault(skill) + 1;
        public void SkillUnlocked(string skill, double t) => _skills.Add(new Dictionary<string, object> { ["skill"] = skill, ["t"] = t });
        public void LevelReached(int level, double t, int sortedTotal) => _levels.Add(new Dictionary<string, object> { ["level"] = (double)level, ["t"] = t, ["sorted"] = (double)sortedTotal });

        /// <summary>Call every frame; records a sample every `interval` seconds.</summary>
        public void MaybeSample(double t, double interval, int sortedTotal, int level)
        {
            if (t - _lastSample < interval) return;
            _lastSample = t;
            _samples.Add(new Dictionary<string, object> { ["t"] = t, ["sorted"] = (double)sortedTotal, ["level"] = (double)level, ["activity"] = _activity });
        }

        public double SortedPerMinute(double t) => t <= 0 ? 0 : (sortedManual + sortedRobot) / (t / 60.0);

        public Dictionary<string, object> ToDict(double t) => new()
        {
            ["preset"] = preset, ["seed"] = (double)seed, ["total_items"] = (double)totalItems, ["play_time_s"] = t,
            ["sorted_manual"] = (double)sortedManual, ["sorted_robot"] = (double)sortedRobot, ["pickups"] = (double)pickups,
            ["wrong_deposits"] = (double)wrongDeposits, ["sorted_per_minute"] = SortedPerMinute(t),
            ["collecting_s"] = collectingS, ["depositing_s"] = depositingS, ["idle_s"] = idleS, ["completed_at_s"] = completedAtS,
            ["skill_uses"] = _skillUses.ToDictionary(kv => kv.Key, kv => (object)(double)kv.Value),
            ["skills_unlocked"] = _skills, ["levels"] = _levels, ["samples"] = _samples,
        };
    }
}
