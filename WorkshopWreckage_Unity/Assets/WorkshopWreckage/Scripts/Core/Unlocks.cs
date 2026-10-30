using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;

namespace WorkshopWreckage
{
    /// <summary>Every level at which something unlocks (robot blueprints, player skills,
    /// respec), from shared/data/unlocks.json. Test phase: an edited copy placed next to
    /// the exe (or the Unity project folder in the editor) wins, so levels can be tuned
    /// without a rebuild. A release build (WWBuild.BuildWindowsRelease, define WW_RELEASE)
    /// compiles the file into the game and ignores external copies and debug mode.</summary>
    public static class Unlocks
    {
        static Dictionary<string, object> _d;

        /// <summary>Where the values came from (shown in the log and the F3 overlay).</summary>
        public static string Source { get; private set; }

        static Dictionary<string, object> D
        {
            get
            {
                if (_d != null) return _d;
#if WW_RELEASE
                Source = "built-in";
                _d = Json.Obj(Json.Parse(UnlocksBaked.Text));
#else
                var external = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "unlocks.json"));
                Source = File.Exists(external) ? external : Shared.DataPath("unlocks.json");
                _d = Shared.ReadObject(Source);
#endif
                Debug.Log($"[unlocks] {Source}");
                return _d;
            }
        }

        static int Level(string group, string id, int fallback)
            => Json.Obj(D.GetValueOrDefault(group)) is Dictionary<string, object> g && g.ContainsKey(id) ? Json.Int(g, id, fallback) : fallback;

        /// <summary>Blueprint level of a robot project (robots.json id or extra unit id).</summary>
        public static int Robot(string id, int fallback = 1) => Level("robots", id, fallback);
        public static int Skill(string id, int fallback = 1) => Level("skills", id, fallback);
        /// <summary>Earliest level for rank `rank` (0-based) of a robot upgrade, by its stat ("robot_upgrades":
        /// a number for every rank or a list per rank); 1 if not listed.</summary>
        public static int UpgradeLevel(string stat, int rank)
        {
            if (Json.Obj(D.GetValueOrDefault("robot_upgrades")) is not Dictionary<string, object> g || !g.TryGetValue(stat, out var v)) return 1;
            if (v is List<object> l) return l.Count == 0 ? 1 : (int)(double)l[System.Math.Min(rank, l.Count - 1)];
            return (int)(double)v;
        }
        public static int Respec => Json.Int(D, "respec", 12);

        /// <summary>What the tool pegboard unlocks ("pegboard_rewards", read by Pegboard.PegboardRewards), or null.</summary>
        public static Dictionary<string, object> PegboardRewards => Json.Obj(D.GetValueOrDefault("pegboard_rewards"));

        /// <summary>A usable skill's tuning ("skill_tuning": slot key, cooldowns, per-level values), or null.</summary>
        public static Dictionary<string, object> SkillTuning(string id) => Json.Obj(Json.Obj(D.GetValueOrDefault("skill_tuning"))?.GetValueOrDefault(id));

        /// <summary>The key that opens a menu screen straight from play ("menu_keys"), e.g. K for skills.</summary>
        public static UnityEngine.InputSystem.Key MenuKey(string screen, UnityEngine.InputSystem.Key fallback)
            => Json.Obj(D.GetValueOrDefault("menu_keys")) is Dictionary<string, object> m && Enum.TryParse<UnityEngine.InputSystem.Key>(Json.Str(m, screen), true, out var k) ? k : fallback;

        /// <summary>Test-phase cheats (the Robot Workbench's "unlock now"): debug_mode in the
        /// config or --debug on the command line. Always off in a release build.</summary>
        public static bool DebugMode
        {
            get
            {
#if WW_RELEASE
                return false;
#else
                return Json.Bool(D, "debug_mode") || Environment.GetCommandLineArgs().Contains("--debug");
#endif
            }
        }
    }
}
