using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace WorkshopWreckage.Pegboard
{
    /// <summary>shared/data/pegboard/: workshop_pegboard.json (board, frame, panel, lights, silhouettes, highlight,
    /// interaction, racks, delivery, completion), tool_definitions.json (one entry per tool) and tool_slots.json
    /// (one slot per tool). Everything the board looks like and does is read from here; nothing is hard-coded
    /// in the builder except shapes.</summary>
    public class PegboardConfig
    {
        public class Tool
        {
            public string id, name, model, material, finish, color;
            public int itemId;
            public float size;
            public string GeometryKey => ToolFactory.Prefix + model;
        }

        public class Slot
        {
            public string id, zone, tool;
            public Vector2 pos;     // metres on the panel, (0,0) = its centre
            public float angle;     // degrees counter-clockwise
            public string Destination => DestPrefix + id;
        }

        public const string Unit = "PB", DestPrefix = "PB-", Kind = "peg_slot", Category = "Hand Tools", PurposePrefix = "pegboard:";

        public Dictionary<string, object> raw;
        public readonly List<Tool> tools = new();
        public readonly List<Slot> slots = new();
        public readonly Dictionary<string, Tool> toolById = new();
        public readonly Dictionary<string, Slot> slotById = new();
        public readonly List<string> errors = new();

        // board
        public float width, height, panelInset, topBand, depth, wallOffset, mountY;
        public float PanelW => width - 2 * panelInset;
        public float PanelH => height - topBand - panelInset;
        /// <summary>The panel's centre in the board frame (y from the bottom of the frame).</summary>
        public Vector2 PanelCentre => new(0, panelInset + PanelH / 2);

        /// <summary>z of the panel's back and front faces (board frame: the root on the wall surface, -z into the room;
        /// a dark backing sheet on the wall shows through the holes).</summary>
        public float PanelBack => -(wallOffset + F("panel", "backing_gap", 0.025f));
        public float PanelFront => PanelBack - F("panel", "thickness", 0.006f);

        public Dictionary<string, object> Section(string name) => raw != null && raw.TryGetValue(name, out var o) ? Json.Obj(o) ?? new() : new();
        public float F(string section, string key, float fallback) => (float)Json.Num(Section(section), key, fallback);
        public Color C(string section, string key, Color fallback) => ColorUtility.TryParseHtmlString(Json.Str(Section(section), key, ""), out var c) ? c : fallback;
        public string S(string section, string key, string fallback) => Json.Str(Section(section), key, fallback);

        static PegboardConfig _cached;
        /// <summary>The shipped configuration (cached; Reload() rereads the files, e.g. in the preview).</summary>
        public static PegboardConfig Load() => _cached ??= Read();
        public static PegboardConfig Reload() => _cached = Read();

        public static string Path(string file) => Shared.DataPath("pegboard/" + file);
        public static bool Present => File.Exists(Path("workshop_pegboard.json"));

        static PegboardConfig Read()
        {
            var c = new PegboardConfig();
            if (!Present) { c.errors.Add("shared/data/pegboard/workshop_pegboard.json is missing"); return c; }
            c.raw = Shared.ReadObject(Path("workshop_pegboard.json"));
            var b = c.Section("board");
            c.width = (float)Json.Num(b, "width", 6);
            c.height = (float)Json.Num(b, "height", 1.6);
            c.panelInset = (float)Json.Num(b, "panel_inset", 0.1);
            c.topBand = (float)Json.Num(b, "top_band", 0.2);
            c.depth = (float)Json.Num(b, "depth", 0.1);
            c.wallOffset = (float)Json.Num(b, "wall_offset", 0.03);
            c.mountY = (float)Json.Num(b, "mount_y", 1.02);

            foreach (var o in Json.Arr(Shared.ReadObject(Path("tool_definitions.json"))["tools"]))
            {
                var d = Json.Obj(o);
                var t = new Tool
                {
                    id = Json.Str(d, "id"), name = Json.Str(d, "name"), model = Json.Str(d, "model"), itemId = Json.Int(d, "item_id", -1),
                    size = (float)Json.Num(d, "size_m", 0.3), material = Json.Str(d, "material", "steel"), finish = Json.Str(d, "finish", "plain"),
                    color = Json.Str(d, "color", "#c0c0c0"),
                };
                if (c.toolById.ContainsKey(t.id)) { c.errors.Add($"tool '{t.id}' is defined twice"); continue; }
                if (!ToolFactory.Has(t.GeometryKey)) c.errors.Add($"tool '{t.id}': unknown model '{t.model}'");
                if (t.itemId < 0) c.errors.Add($"tool '{t.id}': no item_id");
                c.tools.Add(t);
                c.toolById[t.id] = t;
            }
            var used = new HashSet<string>();
            foreach (var o in Json.Arr(Shared.ReadObject(Path("tool_slots.json"))["slots"]))
            {
                var d = Json.Obj(o);
                var p = Json.Arr(d.GetValueOrDefault("pos"));
                var s = new Slot
                {
                    id = Json.Str(d, "id"), zone = Json.Str(d, "zone"), tool = Json.Str(d, "tool"),
                    pos = p.Count >= 2 ? new Vector2(Json.F(p[0]), Json.F(p[1])) : Vector2.zero, angle = (float)Json.Num(d, "angle"),
                };
                if (c.slotById.ContainsKey(s.id)) { c.errors.Add($"slot '{s.id}' is defined twice"); continue; }
                if (!c.toolById.ContainsKey(s.tool)) c.errors.Add($"slot '{s.id}': unknown tool '{s.tool}'");
                if (!used.Add(s.tool)) c.errors.Add($"tool '{s.tool}' has two slots");
                c.slots.Add(s);
                c.slotById[s.id] = s;
            }
            foreach (var t in c.tools) if (!used.Contains(t.id)) c.errors.Add($"tool '{t.id}' has no slot");
            foreach (var e in c.errors) Debug.LogWarning("[pegboard] " + e);
            return c;
        }

        public Slot SlotOfTool(string toolId)
        {
            foreach (var s in slots) if (s.tool == toolId) return s;
            return null;
        }

        /// <summary>The tool as a catalogue item (merged into every preset by Author Catalogs, and parsed by the
        /// preview): one copy, its own definition, its slot as its storage box.</summary>
        /// <summary>"combo_wrench" -> "Combo Wrench".</summary>
        public static string ShapeName(string model)
        {
            var words = model.Split('_');
            for (int i = 0; i < words.Length; i++) if (words[i].Length > 0) words[i] = char.ToUpperInvariant(words[i][0]) + words[i].Substring(1);
            return string.Join(" ", words);
        }

        public Dictionary<string, object> ItemDict(Tool t)
        {
            var s = SlotOfTool(t.id);
            var bounds = MeshLibrary.UnitBounds(t.GeometryKey).size * t.size;
            float len = Mathf.Max(bounds.x, bounds.y, bounds.z), dia = Mathf.Min(bounds.x, bounds.y);
            return new Dictionary<string, object>
            {
                ["id"] = (double)t.itemId,
                ["name"] = t.name,
                ["category"] = Category,
                ["subcategory"] = "pegboard",
                ["shape"] = ShapeName(t.model),   // shown on the hotbar like the catalogue's "Hex Bolt"
                ["geometry_key"] = t.GeometryKey,
                ["material"] = t.material,
                ["finish"] = t.finish,
                ["color"] = t.color,
                ["diameter_mm"] = (double)Mathf.Round(dia * 1000),
                ["length_mm"] = (double)Mathf.Round(len * 1000),
                ["mesh_scale_mm"] = (double)(t.size * 1000),
                ["visual_scale"] = 1.0,
                ["thread_size"] = "",
                ["thread_pitch_mm"] = 0.0,
                ["weight_g"] = (double)Mathf.Round(len * dia * 4000) * 10,
                ["magnetic"] = t.material != "electronic",
                ["rarity"] = 2.0,
                ["storage_category"] = Category,
                ["storage_box"] = s != null ? s.Destination : "",
                ["spawn_count"] = 1.0,
                ["crafting_purpose"] = PurposePrefix + (s != null ? s.id : t.id),
                ["rest_mode"] = "flat",
            };
        }

        /// <summary>Unity-local transform (board frame) of a tool hung in its slot: centre on the panel, the
        /// tool's back resting on the panel's front face.</summary>
        public Matrix4x4 SlotMatrix(Slot s, float scale)
        {
            var key = toolById[s.tool].GeometryKey;
            var bb = MeshLibrary.UnitBounds(key);
            float front = PanelFront - 0.003f;
            var pc = PanelCentre;
            var at = new Vector3(pc.x + s.pos.x, pc.y + s.pos.y, front - bb.extents.z * scale);
            return Matrix4x4.TRS(at, Quaternion.Euler(0, 0, s.angle), Vector3.one * scale);
        }
    }
}
