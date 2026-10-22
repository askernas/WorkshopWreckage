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
        public static int Respec => Json.Int(D, "respec", 12);

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
