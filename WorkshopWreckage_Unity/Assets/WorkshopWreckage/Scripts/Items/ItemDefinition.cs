using System.Collections.Generic;
using UnityEngine;

namespace WorkshopWreckage
{
    /// <summary>How a part lies: Flat on its local Y (up), Long on its side along local Y, Side on its widest face
    /// (local Z up: the pegboard tools, built flat in the XY plane).</summary>
    public enum RestMode { Long, Flat, Side }

    /// <summary>One kind of part, loaded from shared/data/item_catalog.json.</summary>
    public class ItemDefinition
    {
        public static readonly string[] CategoryNames =
        {
            "Screws", "Nuts", "Washers", "Gears", "Springs", "Bearings", "Shafts & Pins",
            "Electronics", "Specialty Parts", "Machine Parts", "Hand Tools", "Robot Parts",   // Robot Parts stays last (IsRobotPart)
        };

        public int id;
        public int category;
        public string subcategory, shape, material, finish, displayName;
        public float diameterMm, lengthMm, threadPitchMm, weightG;
        public string threadSize;
        public bool magnetic;
        public int rarity;
        public string storageCategory, storageBox, craftingPurpose;
        public int spawnCount;
        public Color variantColor = Color.white;
        public string geometryKey;
        public float meshScaleMm, visualScale, worldScale;
        /// <summary>world_scale as the catalogue has it (mesh_scale_mm × visual_scale), and the component
        /// rescale factor on top (ComponentScale): worldScale = baseWorldScale × componentScale.</summary>
        public float baseWorldScale, componentScale = 1f;
        /// <summary>Within the robots' 80 % of definitions (ComponentScale); not a promise that a robot can lift it.</summary>
        public bool robotCarry = true;
        public RestMode restMode;

        public string CategoryName => CategoryNames[category];
        public bool IsRobotPart => category == CategoryNames.Length - 1;
        /// <summary>A Part Radar component or battery (shared/data/tools/part_radar.json): category Robot Parts
        /// (no drawer, delivered to the Robot Workbench) but no robot's component, no ordinary part.</summary>
        public bool IsToolItem => craftingPurpose != null && craftingPurpose.StartsWith("tool:");
        /// <summary>A pegboard hand tool (shared/data/pegboard): one of a kind, hung by the player in its slot on the
        /// tool board (destination PB-&lt;slot&gt;); counts toward restored, gives no XP and no level.</summary>
        public bool IsPegboardTool => craftingPurpose != null && craftingPurpose.StartsWith("pegboard:");
        public string PegSlot => IsPegboardTool ? craftingPurpose.Substring("pegboard:".Length) : "";
        /// <summary>A robot's own component (robot_parts.json): a robot part that is not a tool item.</summary>
        public bool IsRobotComponent => IsRobotPart && !IsToolItem;
        /// <summary>The tool item's type ("radar_housing", ..., "battery"); "" for anything else.</summary>
        public string ToolType => IsToolItem ? craftingPurpose.Substring(craftingPurpose.LastIndexOf(':') + 1) : "";
        public bool IsRadarBattery => ToolType == "battery";
        public bool IsThreaded => !string.IsNullOrEmpty(threadSize);

        public static ItemDefinition FromDict(Dictionary<string, object> d, float defaultVisualScale, List<string> errors)
        {
            foreach (var key in new[] { "id", "name", "category", "geometry_key", "material", "spawn_count", "storage_box", "mesh_scale_mm" })
                if (!d.ContainsKey(key)) errors.Add($"Item {Json.Str(d, "id", "?")}: missing required field '{key}'");
            var def = new ItemDefinition
            {
                id = Json.Int(d, "id", -1),
                displayName = Json.Str(d, "name"),
                subcategory = Json.Str(d, "subcategory"),
                shape = Json.Str(d, "shape"),
                geometryKey = Json.Str(d, "geometry_key"),
                material = Json.Str(d, "material", "steel"),
                finish = Json.Str(d, "finish", "plain"),
                diameterMm = (float)Json.Num(d, "diameter_mm"),
                lengthMm = (float)Json.Num(d, "length_mm"),
                meshScaleMm = (float)Json.Num(d, "mesh_scale_mm", 1),
                threadSize = Json.Str(d, "thread_size"),
                threadPitchMm = (float)Json.Num(d, "thread_pitch_mm"),
                weightG = (float)Json.Num(d, "weight_g"),
                magnetic = Json.Bool(d, "magnetic"),
                rarity = Json.Int(d, "rarity"),
                storageBox = Json.Str(d, "storage_box"),
                spawnCount = Json.Int(d, "spawn_count"),
                craftingPurpose = Json.Str(d, "crafting_purpose"),
                restMode = Json.Str(d, "rest_mode", "flat") switch { "long" => RestMode.Long, "side" => RestMode.Side, _ => RestMode.Flat },
            };
            def.category = System.Array.IndexOf(CategoryNames, Json.Str(d, "category"));
            if (def.category < 0)
            {
                errors.Add($"Item {def.id}: unknown category '{Json.Str(d, "category")}'");
                def.category = 0;
            }
            def.storageCategory = Json.Str(d, "storage_category", def.CategoryName);
            if (ColorUtility.TryParseHtmlString(Json.Str(d, "color", "#ffffff"), out var c)) def.variantColor = c;
            def.visualScale = (float)Json.Num(d, "visual_scale", def.IsRobotPart ? 1.0 : defaultVisualScale);
            def.worldScale = def.baseWorldScale = def.meshScaleMm / 1000f * def.visualScale;
            return def;
        }
    }

    /// <summary>Installed: built into a robot at the Robot Workbench (a unique robot
    /// part, or an ordinary part consumed by a crafting recipe; owner "assembly:&lt;robot&gt;").
    /// Like Stored it counts as accounted for in its definition's collection.</summary>
    public enum ItemState { InWorld = 0, Carried = 1, Stored = 2, Installed = 3 }

    /// <summary>One physical, individually tracked part.</summary>
    public class ItemInstance
    {
        public int id;
        public int defId;
        public Matrix4x4 transform;   // Unity world matrix including visual scale
        public ItemState state;
        public string owner = "world";
        public int zone = -1;
        public bool moved;
        public Vector3 Position => transform.GetColumn(3);
    }
}
