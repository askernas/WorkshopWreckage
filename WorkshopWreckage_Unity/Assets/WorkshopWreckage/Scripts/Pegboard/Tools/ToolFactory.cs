using System;
using System.Collections.Generic;
using UnityEngine;

namespace WorkshopWreckage.Pegboard
{
    /// <summary>Every pegboard tool model, built in code (Pegboard/Tools/*.cs, one file per family). A model is
    /// an item mesh like any other: MeshLibrary asks here for geometry keys "tool:&lt;model&gt;", so the same mesh
    /// lies in the workshop, sits in the player's hands and hangs on the board. Models are built at length 1
    /// (ToolMesh conventions); a tool's size is its item scale (tool_definitions.json size_m). Which part takes
    /// the definition's own colour (slot 0) is the model's choice: the handle of a screwdriver, the paint of a
    /// clamp, the steel of a spanner.</summary>
    public static partial class ToolFactory
    {
        public const string Prefix = "tool:";
        static readonly Dictionary<string, Action<ToolMesh>> Models = new();
        static readonly Dictionary<string, (MeshLibrary.Entry entry, List<(Vector3 at, float kind)> hooks)> _built = new();

        static ToolFactory()
        {
            RegisterWrenches();
            RegisterScrewdrivers();
            RegisterPliers();
            RegisterHammers();
            RegisterSockets();
            RegisterMeasuring();
            RegisterCutting();
            RegisterSpecialty();
        }

        static void Add(string model, Action<ToolMesh> build) => Models[Prefix + model] = build;

        public static bool Has(string key) => key != null && Models.ContainsKey(key);
        public static IEnumerable<string> Keys => Models.Keys;

        /// <summary>The item mesh for a geometry key (MeshLibrary caches it per lod).</summary>
        public static MeshLibrary.Entry Build(string key, int lod) => Get(key, lod).entry;

        /// <summary>Where the board's pegs hold this model (unit space, centred with the mesh).</summary>
        public static List<(Vector3 at, float kind)> Hooks(string key) => Get(key, MeshLibrary.LodWorld).hooks;

        static readonly Dictionary<string, Mesh> _collision = new();

        /// <summary>The model without its bevels, for the pile physics' convex collider (PileSim): the chamfers only
        /// catch the light, and a hull cooked from them is far denser (3-4x the settle cost, measured 2026-10-28).</summary>
        public static Mesh CollisionMesh(string key)
        {
            if (_collision.TryGetValue(key, out var m)) return m;
            var b = new ToolMesh { seg = 10, bevel = 0 };
            Models[key](b);
            return _collision[key] = b.Finish(key.Replace(':', '_') + "_col").entry.mesh;
        }

        static (MeshLibrary.Entry entry, List<(Vector3 at, float kind)> hooks) Get(string key, int lod)
        {
            var k = key + "|" + lod;
            if (_built.TryGetValue(k, out var r)) return r;
            var b = new ToolMesh { seg = lod == MeshLibrary.LodInspect ? 20 : 10 };
            Models[key](b);
            r = b.Finish(key.Replace(':', '_') + "_lod" + lod);
            _built[k] = r;
            return r;
        }

        // shared colours (accents, vertex colour); the definition's own colour comes in on slot 0 (White)
        internal static readonly Color White = Color.white,
            Steel = new(0.6f, 0.61f, 0.63f, 0), Chrome = new(0.78f, 0.79f, 0.81f, 0), DarkSteel = new(0.22f, 0.23f, 0.25f, 0),
            Rubber = new(0.06f, 0.06f, 0.07f, 0), Wood = new(0.58f, 0.4f, 0.22f, 0), WoodDark = new(0.36f, 0.22f, 0.12f, 0),
            Red = new(0.68f, 0.11f, 0.07f, 0), Yellow = new(0.86f, 0.64f, 0.1f, 0), Blue = new(0.14f, 0.3f, 0.6f, 0),
            Brass = new(0.68f, 0.52f, 0.24f, 0), Vial = new(0.45f, 0.9f, 0.3f, 0), Paper = new(0.85f, 0.82f, 0.7f, 0);

        const int Own = ToolMesh.Own, Acc = ToolMesh.Accent;

        /// <summary>A turned handle along +Y from y0 to y1: rounded ends, a waist, optional flutes (as raised ribs).</summary>
        static void Handle(ToolMesh b, float y0, float y1, float r, int slot, Color col, bool flutes = true)
        {
            float l = y1 - y0;
            b.Lathe(new Vector3(0, y0, 0), new[]
            {
                new Vector2(r * 0.55f, 0), new Vector2(r * 0.85f, l * 0.03f), new Vector2(r, l * 0.12f), new Vector2(r * 0.92f, l * 0.55f),
                new Vector2(r, l * 0.85f), new Vector2(r * 0.85f, l * 0.97f), new Vector2(r * 0.45f, l),
            }, slot, col);
            if (!flutes) return;
            for (int i = 0; i < 6; i++)
            {
                float a = i * Mathf.PI / 3;
                var d = new Vector3(Mathf.Cos(a), 0, Mathf.Sin(a));
                b.Cyl(d * r * 0.86f + Vector3.up * (y0 + l * 0.16f), d * r * 0.86f + Vector3.up * (y0 + l * 0.8f), r * 0.16f, slot, col, 6);
            }
        }
    }
}
