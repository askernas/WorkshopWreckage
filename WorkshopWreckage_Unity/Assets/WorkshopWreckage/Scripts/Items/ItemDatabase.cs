using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;

namespace WorkshopWreckage
{
    /// <summary>Runtime catalog + storage layout from shared/data, with the same
    /// validation rules as the Godot implementation. Counts are never adjusted;
    /// an invalid database refuses to produce a world.</summary>
    public class ItemDatabase
    {
        static readonly string[] KnownFamilies = { "screw", "nut", "washer", "gear", "spring", "bearing", "shaft", "elec", "spec", "mach", "robot" };
        public static readonly string[] Kinds = { "drawer", "cardboard_box", "metal_bin", "wooden_crate", "shelf_section", "floor_pallet" };
        static readonly string[] OpenKinds = { "cardboard_box", "metal_bin", "wooden_crate", "shelf_section", "floor_pallet" };
        static readonly Dictionary<string, string> KindNames = new()
        {
            ["drawer"] = "drawer", ["cardboard_box"] = "cardboard box", ["metal_bin"] = "metal bin",
            ["wooden_crate"] = "wooden crate", ["shelf_section"] = "shelf section", ["floor_pallet"] = "floor pallet",
        };
        const float FitTolerance = 1.05f;

        public Dictionary<string, object> config;
        public readonly Dictionary<int, ItemDefinition> definitions = new();
        public readonly List<int> order = new();
        public readonly List<Dictionary<string, object>> units = new();
        public readonly Dictionary<string, Dictionary<string, object>> boxes = new();
        public readonly Dictionary<string, Dictionary<string, object>> stations = new();
        public readonly List<string> errors = new();
        public long catalogHash;
        readonly Dictionary<string, int> _unitIndex = new();

        public bool IsValid => errors.Count == 0;

        public static ItemDatabase LoadDefault()
        {
            var db = new ItemDatabase();
            try
            {
                var cfg = Shared.ReadObject(Shared.DataPath("world_config.json"));
                var catText = File.ReadAllText(Shared.DataPath("item_catalog.json"));
                var stoText = File.ReadAllText(Shared.DataPath("storage_layout.json"));
                db.Load(cfg, Json.Obj(Json.Parse(catText)), Json.Obj(Json.Parse(stoText)));
                db.catalogHash = Fnv(catText) ^ Fnv(stoText);
            }
            catch (System.Exception e)
            {
                db.errors.Add("Could not read data files: " + e.Message);
            }
            return db;
        }

        public void Load(Dictionary<string, object> cfg, Dictionary<string, object> cat, Dictionary<string, object> sto)
        {
            config = cfg;
            float vs = (float)Json.Num(config, "item_visual_scale", 2);
            foreach (var raw in Json.Arr(cat["items"]))
            {
                var def = ItemDefinition.FromDict(Json.Obj(raw), vs, errors);
                if (definitions.ContainsKey(def.id)) { errors.Add($"Duplicate item id {def.id}"); continue; }
                definitions[def.id] = def;
                order.Add(def.id);
            }
            foreach (var u in Json.Arr(sto["units"]))
            {
                var d = Json.Obj(u);
                _unitIndex[Json.Str(d, "id")] = units.Count;
                units.Add(d);
            }
            foreach (var b in Json.Arr(sto["destinations"]))
            {
                var d = Json.Obj(b);
                var id = Json.Str(d, "id");
                if (boxes.ContainsKey(id)) errors.Add($"Duplicate storage destination id '{id}'");
                boxes[id] = d;
            }
            foreach (var s in Json.Arr(sto.GetValueOrDefault("stations")))
            {
                var d = Json.Obj(s);
                stations[Json.Str(d, "id")] = d;
            }
        }

        /// <summary>Deterministic 64-bit FNV-1a (string.GetHashCode is not stable across runs).</summary>
        public static long Fnv(string s)
        {
            ulong h = 14695981039346656037UL;
            foreach (char c in s) { h ^= c; h *= 1099511628211UL; }
            return (long)h;
        }

        public ItemDefinition Get(int id) => definitions.TryGetValue(id, out var d) ? d : null;

        public int BoxCapacity(string id)
        {
            if (boxes.TryGetValue(id, out var b)) return Json.Int(b, "capacity", Json.Int(config, "default_box_capacity", 10));
            if (stations.TryGetValue(id, out var s)) return Json.Int(s, "capacity");
            return 0;
        }

        public Dictionary<string, object> Unit(string id) => _unitIndex.TryGetValue(id, out var i) ? units[i] : null;

        public List<Dictionary<string, object>> DestinationsOf(string unitId)
            => boxes.Values.Where(b => Json.Str(b, "unit") == unitId).ToList();

        public bool IsOpenKind(string id) => boxes.TryGetValue(id, out var b) && OpenKinds.Contains(Json.Str(b, "kind"));

        public Dictionary<string, List<int>> Assignments()
        {
            var o = new Dictionary<string, List<int>>();
            foreach (var id in order)
            {
                var box = definitions[id].storageBox;
                if (!o.TryGetValue(box, out var l)) o[box] = l = new List<int>();
                l.Add(id);
            }
            return o;
        }

        public List<int> RobotPartIds(string robot = "")
            => order.Where(id => definitions[id].IsRobotPart && (robot == "" || definitions[id].craftingPurpose == "robot:" + robot)).ToList();

        public int TotalSpawn() => order.Sum(id => definitions[id].spawnCount);

        /// <summary>World extents of a definition's model, largest first (needs meshes loaded).</summary>
        public static float[] Extents(ItemDefinition def)
        {
            var s = MeshLibrary.UnitBounds(def.geometryKey).size * def.worldScale;
            var a = new[] { s.x, s.y, s.z };
            System.Array.Sort(a);
            System.Array.Reverse(a);
            return a;
        }

        public bool Fits(ItemDefinition def, string boxId)
        {
            if (!boxes.TryGetValue(boxId, out var b)) return stations.ContainsKey(boxId);
            var inner = new[] { (float)Json.Num(b, "width"), (float)Json.Num(b, "depth"), (float)Json.Num(b, "height") };
            System.Array.Sort(inner);
            System.Array.Reverse(inner);
            var e = Extents(def);
            var kind = Json.Str(b, "kind");
            if (kind == "shelf_section" || kind == "floor_pallet")
                return e[0] <= inner[0] * FitTolerance && e[1] <= inner[1] * FitTolerance && e[2] <= (float)Json.Num(b, "height") * FitTolerance;
            return e[0] <= inner[0] * FitTolerance && e[1] <= inner[1] * FitTolerance && e[2] <= inner[2] * FitTolerance;
        }

        /// <summary>Same rules as Godot's ItemDatabase.validate().</summary>
        public void Validate()
        {
            int maxDefault = Json.Int(config, "max_spawn_default", 10);
            int maxHigh = Json.Int(config, "max_spawn_high_quantity", 25);
            var highCats = Json.Arr(config.GetValueOrDefault("high_quantity_categories")).Select(x => x.ToString()).ToHashSet();
            int ordinary = 0, robot = 0;
            var load = new Dictionary<string, int>();
            foreach (var id in order)
            {
                var d = definitions[id];
                var label = $"Item {d.id} ({d.displayName})";
                if (d.spawnCount < 1) errors.Add($"{label}: spawn_count {d.spawnCount} must be at least 1");
                if (d.IsRobotPart)
                {
                    robot += d.spawnCount;
                    if (d.spawnCount != 1) errors.Add($"{label}: robot parts are unique; spawn_count must be 1 (is {d.spawnCount})");
                }
                else
                {
                    ordinary += d.spawnCount;
                    int limit = highCats.Contains(d.CategoryName) ? maxHigh : maxDefault;
                    if (d.spawnCount > limit) errors.Add($"{label}: spawn_count {d.spawnCount} exceeds the {d.CategoryName} limit of {limit}");
                }
                if (!boxes.ContainsKey(d.storageBox) && !stations.ContainsKey(d.storageBox))
                    errors.Add($"{label}: storage destination '{d.storageBox}' does not exist");
                load[d.storageBox] = load.GetValueOrDefault(d.storageBox) + d.spawnCount;
                var fam = d.geometryKey.Split(':')[0];
                if (!KnownFamilies.Contains(fam)) errors.Add($"{label}: unknown geometry family in '{d.geometryKey}'");
                else if (!MeshLibrary.Has(d.geometryKey)) errors.Add($"{label}: mesh '{d.geometryKey}' missing from shared/meshes");
                else if (boxes.ContainsKey(d.storageBox) && !Fits(d, d.storageBox))
                    errors.Add($"{label}: does not physically fit {Json.Str(boxes[d.storageBox], "kind")} '{d.storageBox}'");
                if (d.meshScaleMm <= 0 || d.visualScale <= 0) errors.Add($"{label}: mesh_scale_mm and visual_scale must be positive");
            }
            foreach (var kv in load)
            {
                if (!boxes.ContainsKey(kv.Key) && !stations.ContainsKey(kv.Key)) continue;
                int cap = BoxCapacity(kv.Key);
                bool overflow = boxes.TryGetValue(kv.Key, out var b) && Json.Bool(b, "overflow");
                if (cap < 1) errors.Add($"Storage '{kv.Key}': capacity must be at least 1");
                else if (kv.Value > cap && !overflow) errors.Add($"Storage '{kv.Key}': capacity {cap} is lower than the {kv.Value} items assigned to it");
            }
            foreach (var b in boxes.Values)
            {
                if (!_unitIndex.ContainsKey(Json.Str(b, "unit"))) errors.Add($"Destination '{Json.Str(b, "id")}' references unknown storage unit");
                if (!Kinds.Contains(Json.Str(b, "kind"))) errors.Add($"Destination '{Json.Str(b, "id")}' has unknown kind");
            }
            int wantOrd = Json.Int(config, "ordinary_items", 4020), wantRobot = Json.Int(config, "robot_part_items", 76), wantTotal = Json.Int(config, "total_items", 4096);
            if (ordinary != wantOrd) errors.Add($"Ordinary items total {ordinary}, configuration requires {wantOrd}");
            if (robot != wantRobot) errors.Add($"Robot part items total {robot}, configuration requires {wantRobot}");
            if (ordinary + robot != wantTotal) errors.Add($"World population totals {ordinary + robot}, configuration requires exactly {wantTotal}");
        }

        public string BoxLabel(string id)
        {
            if (stations.TryGetValue(id, out var st)) return Json.Str(st, "label", id);
            if (!boxes.TryGetValue(id, out var b)) return "?";
            var u = Unit(Json.Str(b, "unit"));
            var type = Json.Str(u, "type");
            if (type == "drawer_cabinet") return $"Drawer cabinet {Json.Str(u, "id")} · drawer {Json.Str(b, "slot")}";
            if (type == "pallet_zone") return $"Pallet area {Json.Str(u, "id")} · pallet {Json.Str(b, "slot")}";
            return $"Shelving {Json.Str(u, "id")} · level {Json.Int(b, "level") + 1} · {KindNames.GetValueOrDefault(Json.Str(b, "kind"), "?")} {Json.Str(b, "slot")}";
        }
    }
}
