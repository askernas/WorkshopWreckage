using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace WorkshopWreckage
{
    /// <summary>The objective chain from shared/data/objectives.json: the HUD shows the
    /// first unfinished objective with its progress; finishing one grants the
    /// objective XP once and moves on. Several can complete at once (e.g. after a load).</summary>
    public class Objectives
    {
        public class Def
        {
            public string id, text, type, robot;
            public int value;
        }

        readonly Game g;
        public readonly List<Def> list = new();
        public readonly HashSet<string> done = new();
        float _t;
        public event System.Action<Def> Completed;

        public Objectives(Game game)
        {
            g = game;
            foreach (var o in Json.Arr(Shared.ReadObject(Shared.DataPath("objectives.json"))["objectives"]))
            {
                var d = Json.Obj(o);
                list.Add(new Def { id = Json.Str(d, "id"), text = Json.Str(d, "text"), type = Json.Str(d, "type"), robot = Json.Str(d, "robot", null), value = Json.Int(d, "value", 1) });
            }
        }

        public Def Current => list.FirstOrDefault(o => !done.Contains(o.id));

        /// <summary>Current progress towards an objective (compare with value).</summary>
        public int Progress(Def o)
        {
            var p = g.progress;
            switch (o.type)
            {
                // the pegboard's tools count toward restored % only, never toward an objective's XP (docs/PEGBOARD.md)
                case "collected": return p.CollectedTotal - g.world.instances.Count(i => i.state != ItemState.InWorld && g.world.DefOf(i.id).IsPegboardTool);
                case "sorted": return p.sortedTotal - (g.builder?.pegboard?.Hung ?? 0);
                case "robot_parts": return g.workbench.InstalledCount(o.robot);
                case "robot_active": return g.robots.Any(r => r.id == o.robot && r.Operational) ? 1 : 0;
                case "robots_active": return g.robots.Count(r => r.Operational);
                case "sets_completed": return g.db.order.Count(d => !g.db.Get(d).IsRobotPart && !g.db.Get(d).IsPegboardTool && p.IsComplete(d));
                case "zone_percent": return Mathf.FloorToInt(Enumerable.Range(0, g.layout.zones.Count).Max(z => p.ZonePercent(z)));
                case "skills": return g.progression.unlocked.Count + g.progression.RanksBought;
                case "level": return g.progression.level;
            }
            return 0;
        }

        public void Tick(float dt)
        {
            _t -= dt;
            if (_t > 0) return;
            _t = 0.5f;
            for (int guard = 0; guard < list.Count; guard++)
            {
                var c = Current;
                if (c == null || Progress(c) < c.value) return;
                done.Add(c.id);
                g.progression.OnObjective(c.id);
                Completed?.Invoke(c);
            }
        }

        /// <summary>HUD text for the current objective, e.g. "Store parts where they belong (37/100)".</summary>
        public string HudText()
        {
            var c = Current;
            if (c == null) return "All objectives complete — keep restoring the workshop!";
            return c.value > 1 ? $"{c.text}  ({Mathf.Min(Progress(c), c.value)}/{c.value})" : c.text;
        }

        public List<object> ToList() => done.Select(x => (object)x).ToList();

        public void Load(object o)
        {
            done.Clear();
            foreach (var x in Json.Arr(o)) done.Add(x.ToString());
        }
    }
}
