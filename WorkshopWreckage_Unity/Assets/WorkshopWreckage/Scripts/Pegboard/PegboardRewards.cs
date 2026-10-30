using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace WorkshopWreckage.Pegboard
{
    /// <summary>What the tool pegboard unlocks (unlocks.json "pegboard_rewards", docs/PEGBOARD.md "Rewards"): robot
    /// blueprints, the Part Radar blueprint and the Area Sweep bonus. The only conditions are pegboard milestones (the
    /// first tool on its place, a section complete, any n sections, the whole board), never another robot. A reward is
    /// granted once, for good, and saved: taking a tool off the board again never takes it back. A blueprint only lets
    /// the player build the thing; its parts and the bench are unchanged.</summary>
    public class PegboardRewards
    {
        public class Milestone
        {
            public string id, section;
            public int tools, sections;
            public bool all;
            public List<string> unlocks = new(), notice = new();
        }

        List<string> _start;
        List<Milestone> _milestones;
        float _sweepRadius = 0.5f, _sweepCooldown = 300f;
        int _sweepMax = 40;
        readonly HashSet<string> _granted = new();
        readonly HashSet<string> _warned = new();

        /// <summary>Rewards open from the start (Retrobot).</summary>
        public List<string> Start { get { Read(); return _start; } }
        public List<Milestone> Milestones { get { Read(); return _milestones; } }
        public IReadOnlyCollection<string> Granted => _granted;

        /// <summary>The Area Sweep the full board gives (no skill bought): radius, cooldown and item limit.</summary>
        public float SweepRadius { get { Read(); return _sweepRadius; } }
        public float SweepCooldown { get { Read(); return _sweepCooldown; } }
        public int SweepMaxItems { get { Read(); return _sweepMax; } }

        // read on first use: the config comes from Unlocks, which needs the Unity data path
        void Read()
        {
            if (_milestones != null) return;
            var d = Unlocks.PegboardRewards ?? new Dictionary<string, object>();
            _start = Json.Arr(d.GetValueOrDefault("start")).Select(x => x.ToString()).ToList();
            _milestones = new List<Milestone>();
            foreach (var o in Json.Arr(d.GetValueOrDefault("milestones")))
            {
                var m = Json.Obj(o);
                if (m == null) continue;
                var w = Json.Obj(m.GetValueOrDefault("when")) ?? new Dictionary<string, object>();
                _milestones.Add(new Milestone
                {
                    id = Json.Str(m, "id"),
                    section = Json.Str(w, "section", null),
                    tools = Json.Int(w, "tools_placed", 0),
                    sections = Json.Int(w, "sections_completed", 0),
                    all = Json.Bool(w, "all_sections"),
                    unlocks = Json.Arr(m.GetValueOrDefault("unlocks")).Select(x => x.ToString()).ToList(),
                    notice = Json.Arr(m.GetValueOrDefault("notice")).Select(x => x.ToString()).ToList(),
                });
            }
            if (Json.Obj(d.GetValueOrDefault("area_sweep")) is Dictionary<string, object> s)
            {
                _sweepRadius = (float)Json.Num(s, "radius_m", _sweepRadius);
                _sweepCooldown = (float)Json.Num(s, "cooldown_s", _sweepCooldown);
                _sweepMax = Json.Int(s, "max_items", _sweepMax);
            }
        }

        /// <summary>Open now: from the start or granted by the board.</summary>
        public bool Has(string reward) => Start.Contains(reward) || _granted.Contains(reward);

        /// <summary>The milestone that grants a reward (null: none, e.g. Retrobot from the start).</summary>
        public Milestone For(string reward) => Start.Contains(reward) ? null : Milestones.FirstOrDefault(m => m.unlocks.Contains(reward));

        public bool Met(Milestone m, WorkshopPegboard pb)
        {
            if (pb == null) return false;
            if (m.tools > 0) return pb.Hung >= m.tools;
            if (m.section != null)
            {
                if (pb.cfg.sectionById.ContainsKey(m.section)) return pb.SectionComplete(m.section);
                if (_warned.Add(m.id)) Debug.LogWarning($"[unlocks] pegboard_rewards: milestone '{m.id}' names unknown section '{m.section}' - never met");
                return false;
            }
            if (m.all) return pb.cfg.sections.Count > 0 && pb.SectionsComplete == pb.cfg.sections.Count;
            if (m.sections > 0) return pb.SectionsComplete >= Mathf.Min(m.sections, pb.cfg.sections.Count);
            return false;
        }

        /// <summary>Grants every reward whose milestone is met and that isn't granted yet. Returns the new ones by
        /// milestone, so one placement that meets several milestones grants all of them, each once.</summary>
        public List<(Milestone m, List<string> rewards)> Evaluate(WorkshopPegboard pb)
        {
            var o = new List<(Milestone, List<string>)>();
            foreach (var m in Milestones)
            {
                if (!Met(m, pb)) continue;
                var fresh = m.unlocks.Where(r => !Start.Contains(r) && _granted.Add(r)).ToList();
                if (fresh.Count > 0) o.Add((m, fresh));
            }
            return o;
        }

        /// <summary>(have, need, unit) toward a milestone.</summary>
        public (int have, int need, string unit) Progress(Milestone m, WorkshopPegboard pb)
        {
            if (pb == null || m == null) return (0, 0, "");
            int n = pb.cfg.sections.Count;
            if (m.tools > 0) return (Mathf.Min(pb.Hung, m.tools), m.tools, m.tools == 1 ? "tool placed" : "tools placed");
            if (m.section != null) return (pb.SectionHung(m.section), pb.SectionTotal(m.section), "tools restored");
            if (m.all) return (pb.SectionsComplete, n, "sections");
            return (Mathf.Min(pb.SectionsComplete, m.sections), Mathf.Min(m.sections, n), "sections");
        }

        /// <summary>"Pliers & Clamps", "any 3 pegboard sections complete", ...</summary>
        public string RequirementText(Milestone m, WorkshopPegboard pb)
        {
            if (m == null) return "";
            if (m.tools > 0) return m.tools == 1 ? "a tool in its place on the pegboard" : $"{m.tools} tools in their places on the pegboard";
            if (m.section != null) return pb != null && pb.cfg.sectionById.TryGetValue(m.section, out var s) ? s.name : m.section;
            if (m.all) return "every pegboard section complete";
            return $"any {m.sections} pegboard sections complete";
        }

        // ---------------------------------------------------------------- save

        public List<object> ToList() => _granted.OrderBy(x => x).Select(x => (object)x).ToList();

        /// <summary>Restores the granted rewards. False for a save from before the rewards (2026-10-29): the caller then
        /// keeps what that save had already unlocked (GrantAll) before the board is evaluated.</summary>
        public bool Load(Dictionary<string, object> save)
        {
            _granted.Clear();
            if (save == null) return true;
            if (!save.TryGetValue("pegboard_rewards", out var v)) return false;
            foreach (var x in Json.Arr(v)) _granted.Add(x.ToString());
            return true;
        }

        public void GrantAll(IEnumerable<string> rewards) { foreach (var r in rewards) if (!Start.Contains(r)) _granted.Add(r); }

        public void Reset() => _granted.Clear();
    }
}
