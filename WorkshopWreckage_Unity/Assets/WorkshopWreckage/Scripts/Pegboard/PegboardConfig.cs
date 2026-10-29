using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;

namespace WorkshopWreckage.Pegboard
{
    /// <summary>shared/data/pegboard/: workshop_pegboard.json (board, frame, panel, lights, silhouettes, highlight,
    /// interaction, racks, sections, small tools, completion), tool_definitions.json (one entry per tool) and
    /// tool_slots.json (one slot per tool, each in a section; a set's pieces in child slots on their holder).
    /// Everything the board looks like and does is read from here; nothing is hard-coded in the builder except
    /// shapes.</summary>
    public class PegboardConfig
    {
        public class Tool
        {
            public string id, name, model, material, finish, color;
            public int itemId;
            public float size;      // metres: size_m, times the small-tool scale for a set's pieces and holders
            public bool small;      // a set's piece or holder (scaled by small_tools.scale)
            public string GeometryKey => ToolFactory.Prefix + model;
        }

        public class Slot
        {
            public string id, section, tool;
            public Vector2 pos;     // metres on the panel, (0,0) = its centre (not used by a child slot)
            public float angle;     // degrees counter-clockwise
            public string parent;   // a child slot: the holder's slot; its tool goes on the holder's anchor
            public int anchor = -1; // a child slot: which of the holder's anchors (the order of the children)
            public readonly List<string> children = new();   // a holder's child slots, in anchor order
            public bool IsChild => parent != null;
            public string Destination => DestPrefix + id;
        }

        /// <summary>A coherent area of the board (docs/PEGBOARD.md "Sections"): a sign over it, its tools under it.
        /// Completing one can unlock a robot blueprint (unlocks.json robot_pegboard), never anything here.</summary>
        public class BoardSection
        {
            public string id, name;
            public float x0, x1;    // the area across the panel (metres, panel centre 0)
            public readonly List<string> slots = new();
        }

        public const string Unit = "PB", DestPrefix = "PB-", Kind = "peg_slot", Category = "Hand Tools", PurposePrefix = "pegboard:";

        public Dictionary<string, object> raw;
        public readonly List<Tool> tools = new();
        public readonly List<Slot> slots = new();
        public readonly Dictionary<string, Tool> toolById = new();
        public readonly Dictionary<string, Slot> slotById = new();
        public readonly List<BoardSection> sections = new();
        public readonly Dictionary<string, BoardSection> sectionById = new();
        public readonly List<string> errors = new();
        /// <summary>Size factor of the sets' pieces and holders (small_tools.scale; --smallscale= overrides it for a test).</summary>
        public float smallScale = 1;

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
            c.smallScale = c.F("small_tools", "scale", 1);
            foreach (var a in System.Environment.GetCommandLineArgs())
                if (a.StartsWith("--smallscale=") && float.TryParse(a.Substring(13), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var ss)) c.smallScale = ss;
            if (c.smallScale <= 0) { c.errors.Add($"small_tools.scale must be positive (is {c.smallScale})"); c.smallScale = 1; }

            foreach (var o in Json.Arr(Shared.ReadObject(Path("tool_definitions.json"))["tools"]))
            {
                var d = Json.Obj(o);
                var t = new Tool
                {
                    id = Json.Str(d, "id"), name = Json.Str(d, "name"), model = Json.Str(d, "model"), itemId = Json.Int(d, "item_id", -1),
                    size = (float)Json.Num(d, "size_m", 0.3), material = Json.Str(d, "material", "steel"), finish = Json.Str(d, "finish", "plain"),
                    color = Json.Str(d, "color", "#c0c0c0"), small = Json.Bool(d, "small"),
                };
                if (t.small) t.size *= c.smallScale;
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
                    id = Json.Str(d, "id"), section = Json.Str(d, "section", null), tool = Json.Str(d, "tool"), parent = Json.Str(d, "parent", null),
                    pos = p.Count >= 2 ? new Vector2(Json.F(p[0]), Json.F(p[1])) : Vector2.zero, angle = (float)Json.Num(d, "angle"),
                };
                if (c.slotById.ContainsKey(s.id)) { c.errors.Add($"slot '{s.id}' is defined twice"); continue; }
                if (!c.toolById.ContainsKey(s.tool)) c.errors.Add($"slot '{s.id}': unknown tool '{s.tool}'");
                if (!used.Add(s.tool)) c.errors.Add($"tool '{s.tool}' has two slots");
                c.slots.Add(s);
                c.slotById[s.id] = s;
            }
            foreach (var t in c.tools) if (!used.Contains(t.id)) c.errors.Add($"tool '{t.id}' has no slot");
            c.ReadSections();
            foreach (var e in c.errors) Debug.LogWarning("[pegboard] " + e);
            return c;
        }

        /// <summary>Sections and sets: every slot in exactly one section, every child slot on a holder that is not
        /// itself a child, as many children as the holder has places, a holder's children in its own section.</summary>
        void ReadSections()
        {
            foreach (var o in Json.Arr(raw.GetValueOrDefault("sections")))
            {
                var d = Json.Obj(o);
                var x = Json.Arr(d.GetValueOrDefault("x"));
                var sec = new BoardSection { id = Json.Str(d, "id"), name = Json.Str(d, "name"), x0 = x.Count >= 2 ? Json.F(x[0]) : 0, x1 = x.Count >= 2 ? Json.F(x[1]) : 0 };
                if (string.IsNullOrEmpty(sec.id) || sectionById.ContainsKey(sec.id)) { errors.Add($"section '{sec.id}' has no id or is defined twice"); continue; }
                if (sec.x1 <= sec.x0) errors.Add($"section '{sec.id}': its x range is empty");
                sections.Add(sec);
                sectionById[sec.id] = sec;
            }
            foreach (var s in slots)
            {
                if (s.parent == null) continue;
                if (!slotById.TryGetValue(s.parent, out var h)) { errors.Add($"slot '{s.id}': unknown holder slot '{s.parent}'"); s.parent = null; continue; }
                if (h.parent != null) { errors.Add($"slot '{s.id}': its holder '{h.id}' is itself on a holder"); s.parent = null; continue; }
                s.anchor = h.children.Count;
                h.children.Add(s.id);
                s.section ??= h.section;
                if (s.section != h.section) errors.Add($"slot '{s.id}' is in section '{s.section}', its holder '{h.id}' in '{h.section}'");
            }
            foreach (var s in slots)
            {
                if (s.section == null || !sectionById.TryGetValue(s.section, out var sec)) { errors.Add($"slot '{s.id}': no section or unknown section '{s.section}'"); continue; }
                sec.slots.Add(s.id);
                if (!toolById.TryGetValue(s.tool, out var t)) continue;
                int places = ToolFactory.Hooks(t.GeometryKey).Count(h => h.kind == ToolFactory.AnchorKind);
                if (s.children.Count != places)
                    errors.Add(places == 0 ? $"slot '{s.id}': {t.model} is not a holder, but {s.children.Count} slots hang on it"
                                           : $"slot '{s.id}': the holder {t.model} has {places} places, {s.children.Count} child slots");
                if (s.parent == null && (s.pos.x < sec.x0 || s.pos.x > sec.x1)) errors.Add($"slot '{s.id}' (x {s.pos.x:0.000}) is outside its section '{sec.id}' ({sec.x0}..{sec.x1})");
            }
            if (sections.Count > 0) foreach (var sec in sections) if (sec.slots.Count == 0) errors.Add($"section '{sec.id}' has no tools");
        }

        public BoardSection SectionOf(string slot) => slotById.TryGetValue(slot, out var s) && s.section != null && sectionById.TryGetValue(s.section, out var sec) ? sec : null;

        /// <summary>Board-frame transform of a child slot's tool on its holder: the tool's first hook on the holder's
        /// anchor (holder matrix in the same frame, as hung), the tool turned by its own slot angle.</summary>
        public Matrix4x4 ChildMatrix(Slot child, Matrix4x4 holder)
        {
            var anchors = ToolFactory.Hooks(toolById[slotById[child.parent].tool].GeometryKey).Where(h => h.kind == ToolFactory.AnchorKind).ToList();
            var t = toolById[child.tool];
            var at = holder.MultiplyPoint3x4(anchors[Mathf.Clamp(child.anchor, 0, anchors.Count - 1)].at);
            var hooks = ToolFactory.Hooks(t.GeometryKey);
            var rot = Quaternion.Euler(0, 0, child.angle);
            var own = hooks.Count > 0 ? rot * (hooks[0].at * t.size) : Vector3.zero;
            return Matrix4x4.TRS(at - own, rot, Vector3.one * t.size);
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
                ["rest_mode"] = "side",   // tools lie on their widest face, never on an edge or end
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
