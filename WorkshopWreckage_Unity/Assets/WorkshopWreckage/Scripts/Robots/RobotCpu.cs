using System.Collections.Generic;
using UnityEngine;

namespace WorkshopWreckage
{
    /// <summary>QA: what the robots' decisions cost on the main thread (performance pass 2026-11-01). Each piece
    /// of decision work in one frame is noted by kind; one over LogMs (--robotcpu=N, default 20) is logged as
    /// [robotcpu]. The balance run (--balance) writes the summary into its report as robot_cpu.</summary>
    public static class RobotCpu
    {
        public static float LogMs = 20;
        class Stat { public int n, over4, over8, over16, over33; public double sum, max; }
        static readonly Dictionary<string, Stat> _stats = new();

        public static void Note(string robot, string kind, double ms, string detail = "")
        {
            if (!_stats.TryGetValue(kind, out var s)) _stats[kind] = s = new Stat();
            s.n++; s.sum += ms; if (ms > s.max) s.max = ms;
            if (ms > 4) s.over4++;
            if (ms > 8) s.over8++;
            if (ms > 16) s.over16++;
            if (ms > 33) s.over33++;
            if (ms > LogMs) Debug.Log($"[robotcpu] {robot} {kind} {ms:0.0} ms{(detail != "" ? ": " + detail : "")}");
        }

        public static Dictionary<string, object> Summary()
        {
            var d = new Dictionary<string, object>();
            foreach (var (k, s) in _stats)
                d[k] = new Dictionary<string, object>
                {
                    ["count"] = (double)s.n, ["avg_ms"] = System.Math.Round(s.sum / Mathf.Max(1, s.n), 2), ["max_ms"] = System.Math.Round(s.max, 1),
                    ["over_4ms"] = (double)s.over4, ["over_8ms"] = (double)s.over8, ["over_16ms"] = (double)s.over16, ["over_33ms"] = (double)s.over33,
                };
            d["plan"] = new Dictionary<string, object> { ["used"] = (double)Robot.planUsed, ["fallback"] = (double)Robot.planFallback, ["skipped"] = (double)Robot.planSkipped };
            return d;
        }
    }
}
