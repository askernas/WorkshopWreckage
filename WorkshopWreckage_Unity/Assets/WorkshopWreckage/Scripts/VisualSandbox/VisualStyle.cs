using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using UnityEngine;

namespace WorkshopWreckage.Sandbox
{
    /// <summary>One visual style preset (shared/data/visual_sandbox/styles/*.json): offsets on top of the game's
    /// current look, so every value 0 is the look as it ships (BASELINE). Item values drive the item shader's art
    /// globals (WorkshopArtDirectionProfile sets their baseline), contrast the sandbox's grading, shadow softness the
    /// sandbox's lights; meshes and the game's own files are never touched.</summary>
    public class VisualStyle
    {
        /// <summary>A tunable value: JSON key, label, slider range, and whether it does anything yet.</summary>
        public class Param
        {
            public string key, label, note;
            public float min, max;
            public bool live = true;
        }

        public static readonly Param[] Params =
        {
            new() { key = "saturation", label = "Saturation", min = -0.6f, max = 0.6f },
            new() { key = "contrast", label = "Contrast", min = -30, max = 30 },
            new() { key = "roughness", label = "Roughness", min = -0.4f, max = 0.4f },
            new() { key = "metallic", label = "Metallic response", min = -0.8f, max = 0.8f },
            new() { key = "specular", label = "Specular / gloss", min = -0.8f, max = 1.0f },
            new() { key = "edge_highlight", label = "Edge highlights", min = -0.5f, max = 1.0f },
            new() { key = "bevel_appearance", label = "Painted bevels", min = 0, max = 1, note = "shader bevel from the face edge distance (skinned parts)" },
            new() { key = "bevel_width_mm", label = "Painted bevel width (mm)", min = 0.5f, max = 6 },
            new() { key = "normal_strength", label = "Normal strength", min = -1, max = 1.5f },
            new() { key = "ambient_occlusion", label = "Cavity AO (wash)", min = -0.5f, max = 1.0f },
            new() { key = "shadow_softness", label = "Shadow softness", min = -1, max = 1, note = "-1 hard, 0 as is, +1 softest and lighter" },
            new() { key = "stylized_lighting", label = "Stylized light (rim, ambient)", min = -0.5f, max = 1.0f },
            new() { key = "paint_wear", label = "Paint wear", min = -0.6f, max = 1.0f },
            new() { key = "outline", label = "Outline (placeholder)", min = 0, max = 1, live = false, note = "no outline shader yet: stored only" },
            new() { key = "outline_width_px", label = "Outline width px (placeholder)", min = 0, max = 4, live = false },
            // Variant B "Playful Industrial" terms (WWItem.shader _WWLab*): 0 = off, as in the game
            new() { key = "paint_smoothness", label = "Paint smoothness", min = 0, max = 0.8f, note = "absolute (robots 0.52); 0 = as authored" },
            new() { key = "roughness_variation", label = "Roughness variation", min = 0, max = 0.3f },
            new() { key = "metal_response", label = "Metal response", min = 0, max = 1, note = "0 = the items' gloss damping, 1 = full PBR as the robots" },
            new() { key = "rust", label = "Rust (old chips, creases)", min = 0, max = 1 },
            new() { key = "env_reflection", label = "Room reflection", min = 0, max = 1.5f, note = "the room's ambient in the reflection direction" },
            new() { key = "edge_polish", label = "Metal edge polish", min = 0, max = 1 },
            new() { key = "light_shaping", label = "Light shaping", min = 0, max = 1, note = "shadow detail and highlight control on the preset's lighting" },
            new() { key = "geometry_quality", label = "Geometry quality", min = 0, max = 2, note = "0 world meshes; 1 Variant B shape meshes from 2.5 cm (hero from 15 cm); 2 from 1.5 cm (hero from 10 cm); rebuilds meshes" },
        };

        /// <summary>The controls Variant B's compact panel shows (the rest stay under "all sliders").</summary>
        public static readonly string[] VariantBKeys = { "paint_smoothness", "roughness_variation", "metal_response", "paint_wear", "rust", "edge_highlight", "edge_polish", "ambient_occlusion", "env_reflection", "light_shaping", "contrast", "geometry_quality" };

        /// <summary>Variant B's defaults (Reset), the starting point of styles/variant_b.json.</summary>
        public static readonly Dictionary<string, float> VariantBDefaults = new()
        {
            ["saturation"] = 0.05f, ["contrast"] = 4, ["edge_highlight"] = 0.3f, ["paint_wear"] = 0.4f, ["ambient_occlusion"] = 0.15f,
            ["paint_smoothness"] = 0.5f, ["roughness_variation"] = 0.1f, ["metal_response"] = 0.85f, ["rust"] = 0.45f,
            ["env_reflection"] = 0.6f, ["edge_polish"] = 0.6f, ["light_shaping"] = 0.6f, ["geometry_quality"] = 1,
        };

        public string id = "baseline", name = "BASELINE";
        public readonly Dictionary<string, float> v = new();

        public float this[string key]
        {
            get => v.TryGetValue(key, out var x) ? x : Default(key);
            set => v[key] = value;
        }

        public static float Default(string key) => key == "bevel_width_mm" ? 2f : key == "outline_width_px" ? 1f : 0f;

        public static VisualStyle Load(string path, string id, string name)
        {
            var s = new VisualStyle { id = id, name = name };
            if (!File.Exists(path)) return s;
            var d = Shared.ReadObject(path);
            s.name = Json.Str(d, "name", name);
            foreach (var p in Params) if (d.ContainsKey(p.key)) s.v[p.key] = (float)Json.Num(d, p.key);
            return s;
        }

        /// <summary>One value per line, so the presets are easy to edit and diff by hand.</summary>
        public void Save(string path)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            var sb = new StringBuilder("{\n");
            sb.Append("  \"name\": \"").Append(name).Append("\",\n");
            sb.Append("  \"_note\": \"offsets on the game's current look: 0 everywhere = BASELINE (docs/VISUAL_SANDBOX.md)\"");
            foreach (var p in Params) sb.Append(",\n  \"").Append(p.key).Append("\": ").Append(this[p.key].ToString("0.###", CultureInfo.InvariantCulture));
            sb.Append("\n}\n");
            File.WriteAllText(path, sb.ToString());
        }

        public Dictionary<string, object> ToDict()
        {
            var d = new Dictionary<string, object> { ["id"] = id, ["name"] = name };
            foreach (var p in Params) d[p.key] = (double)this[p.key];
            return d;
        }
    }

    /// <summary>Applies a VisualStyle: the item shader's globals as the game's baseline plus the style's offsets,
    /// the sandbox lighting's contrast and shadows. Holds the four presets (BASELINE, EXPERIMENT A-C).</summary>
    public class VisualStyleController
    {
        public static readonly (string id, string name)[] Slots = { ("baseline", "BASELINE"), ("variant_b", "VARIANT B"), ("experiment_a", "EXPERIMENT A"), ("experiment_b", "EXPERIMENT B"), ("experiment_c", "EXPERIMENT C") };
        public const int VariantB = 1;

        static readonly string[] ArtGlobals = { "_WWArtSat", "_WWArtRough", "_WWArtMetal", "_WWArtGloss", "_WWArtEdge", "_WWArtWash", "_WWArtEdgeWear", "_WWArtRim", "_WWArtAmbient" };
        readonly Dictionary<string, float> _baseline = new();
        public readonly List<VisualStyle> presets = new();
        public int current;
        public static string Dir => Shared.DataPath("visual_sandbox/styles");
        public VisualStyle Style => presets[current];

        public VisualStyleController()
        {
            // the game's look as WorkshopArtDirectionProfile left it: the zero point of every offset
            foreach (var g in ArtGlobals) _baseline[g] = Shader.GetGlobalFloat(g);
            Reload();
        }

        /// <summary>The experiments from their files (a missing file starts as a copy of BASELINE and is written).</summary>
        public void Reload()
        {
            presets.Clear();
            presets.Add(new VisualStyle());   // BASELINE is the game itself, never a file
            for (int k = 1; k < Slots.Length; k++)
            {
                var path = Path.Combine(Dir, Slots[k].id + ".json");
                var s = VisualStyle.Load(path, Slots[k].id, Slots[k].name);
                if (!File.Exists(path))
                {
                    if (k == VariantB) foreach (var kv in VisualStyle.VariantBDefaults) s[kv.Key] = kv.Value;
                    s.Save(path);
                }
                presets.Add(s);
            }
        }

        public bool CanEdit => current > 0;

        public void Save() { if (CanEdit) Style.Save(Path.Combine(Dir, Style.id + ".json")); }

        public void Revert()
        {
            if (!CanEdit) return;
            var (id, name) = Slots[current];
            presets[current] = VisualStyle.Load(Path.Combine(Dir, id + ".json"), id, name);
        }

        /// <summary>The current experiment back to BASELINE (all offsets 0), not saved until Save.</summary>
        public void Zero() { if (CanEdit) Style.v.Clear(); }

        /// <summary>Variant B back to its built-in defaults (not saved until Save).</summary>
        public void ResetVariantB()
        {
            var s = presets[VariantB];
            s.v.Clear();
            foreach (var kv in VisualStyle.VariantBDefaults) s[kv.Key] = kv.Value;
        }

        /// <summary>The style's geometry quality, 0-2 (whole steps).</summary>
        public int GeometryQuality => Mathf.Clamp(Mathf.RoundToInt(Style["geometry_quality"]), 0, 2);

        /// <summary>For ItemRenderer.WorldLod. Quality 1-2: the procedural part families get the Variant B shape meshes
        /// (ShapeMeshes: 32-72 segments, crease normals, bevels, rebuilt gears) from 2.5 cm (1.5 cm at 2), the hero
        /// build from 15 cm (10 cm at 2); smaller parts keep the world mesh (nothing tiny is subdivided). Robot parts
        /// and the pegboard's tools never change. Anything else that is round gets the 14-16 segment inspect mesh as
        /// in iteration 1.</summary>
        public static int LodFor(string key, float size, int quality)
        {
            if (quality <= 0 || key.StartsWith("robot:") || Pegboard.ToolFactory.Has(key)) return MeshLibrary.LodWorld;
            if (ShapeMeshes.Has(key))
            {
                if (size < (quality == 1 ? 0.025f : 0.015f)) return MeshLibrary.LodWorld;
                return size >= (quality == 1 ? 0.15f : 0.10f) ? ShapeMeshes.LodHero : ShapeMeshes.Lod;
            }
            if (size < (quality == 1 ? 0.06f : 0.03f) || SalvageMeshes.Has(key)) return MeshLibrary.LodWorld;
            if (!_round.TryGetValue(key, out var round))
            {
                round = false;
                if (File.Exists(Shared.MeshPath(key.Replace(":", "_") + "__lod0.obj.gz")))
                {
                    int lo = MeshLibrary.Get(key, MeshLibrary.LodWorld).mesh.vertexCount, hi = MeshLibrary.Get(key, MeshLibrary.LodInspect).mesh.vertexCount;
                    round = hi > lo * 1.3f;
                }
                _round[key] = round;
            }
            return round ? MeshLibrary.LodInspect : MeshLibrary.LodWorld;
        }
        static readonly Dictionary<string, bool> _round = new();

        public void Apply(SandboxLighting lighting)
        {
            var s = Style;
            float B(string g) => _baseline[g];
            Shader.SetGlobalFloat("_WWArtSat", B("_WWArtSat") + s["saturation"]);
            Shader.SetGlobalFloat("_WWArtRough", B("_WWArtRough") + s["roughness"]);
            Shader.SetGlobalFloat("_WWArtMetal", B("_WWArtMetal") + s["metallic"]);
            Shader.SetGlobalFloat("_WWArtGloss", B("_WWArtGloss") + s["specular"]);
            Shader.SetGlobalFloat("_WWArtEdge", Mathf.Max(0, B("_WWArtEdge") + s["edge_highlight"]));
            Shader.SetGlobalFloat("_WWArtWash", Mathf.Max(0, B("_WWArtWash") + s["ambient_occlusion"]));
            Shader.SetGlobalFloat("_WWArtEdgeWear", Mathf.Max(0, B("_WWArtEdgeWear") + s["paint_wear"]));
            Shader.SetGlobalFloat("_WWArtRim", Mathf.Max(0, B("_WWArtRim") + s["stylized_lighting"]));
            Shader.SetGlobalFloat("_WWArtAmbient", B("_WWArtAmbient") + s["stylized_lighting"] * 0.5f);
            // sandbox-only shader terms (WWItem.shader, _WWLab*): 0 in the game, where nothing sets them
            Shader.SetGlobalFloat("_WWLabBevel", s["bevel_appearance"]);
            Shader.SetGlobalFloat("_WWLabBevelW", s["bevel_width_mm"] / 1000f);
            Shader.SetGlobalFloat("_WWLabNormal", s["normal_strength"]);
            // Variant B terms: the gate is on while any of them is
            float ps = s["paint_smoothness"], rv = s["roughness_variation"], mr = s["metal_response"], ru = s["rust"], er = s["env_reflection"], ep = s["edge_polish"];
            Shader.SetGlobalFloat("_WWLabB", ps + rv + mr + ru + ep > 0 ? 1 : 0);
            Shader.SetGlobalFloat("_WWLabPaintSmooth", ps);
            Shader.SetGlobalFloat("_WWLabRoughVar", rv);
            Shader.SetGlobalFloat("_WWLabMetalResp", mr);
            Shader.SetGlobalFloat("_WWLabRust", ru);
            Shader.SetGlobalFloat("_WWLabEnvRefl", er);
            Shader.SetGlobalFloat("_WWLabEdgePolish", ep);
            lighting?.ApplyStyle(s["contrast"], s["shadow_softness"], s["light_shaping"]);
        }
    }
}
