using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace WorkshopWreckage.EditorTools
{
    /// <summary>Authors the per-preset data (shared/data/presets/&lt;id&gt;/) from
    /// world_presets.json: picks definitions from the procedural parameter space
    /// once for the largest preset (so Standard ⊂ Large ⊂ Massive with identical ids
    /// and sizes), then per preset assigns spawn counts summing exactly to the
    /// configured total, chooses a physically fitting destination kind per
    /// definition and packs destinations into drawer cabinets, shelving and pallet zones.
    /// CLI: Unity.exe -batchmode -quit -projectPath . -executeMethod WorkshopWreckage.EditorTools.CatalogAuthoring.AuthorAll</summary>
    public static class CatalogAuthoring
    {
        static readonly string[] Letters = { "A", "B", "C", "D", "E", "F", "G", "H", "I", "J", "K", "L" };

        [MenuItem("Workshop Wreckage/Author Catalogs (all presets)")]
        public static void AuthorAll()
        {
            var cfg = Shared.ReadObject(Shared.DataPath("world_presets.json"));
            var presets = Json.Obj(cfg["presets"]);
            var a = Json.Obj(cfg["authoring"]);
            var cat = new ProceduralCatalog();
            var rng = new System.Random(Json.Int(a, "seed", 1));

            // ---- pick the definitions for the largest preset, with tiers --------------
            int maxDefs = presets.Values.Max(p => Json.Int(Json.Obj(p), "ordinary_definitions"));
            var tierOf = new Dictionary<int, int>();
            var picked = new List<ItemDefinition>();
            var shares = Json.Obj(a["category_shares"]);
            var orderedPresets = presets.OrderBy(p => Json.Int(Json.Obj(p.Value), "tier")).ToList();
            foreach (var kv in shares)
            {
                int c = System.Array.IndexOf(ItemDefinition.CategoryNames, kv.Key);
                double share = (double)kv.Value;
                int n = Mathf.RoundToInt((float)(share * maxDefs));
                var chosen = Choose(cat, c, n, rng);
                // tiers: the first share*standard go to the smallest preset, and so on
                int start = 0;
                foreach (var p in orderedPresets)
                {
                    int upto = Mathf.Min(chosen.Count, Mathf.RoundToInt((float)(share * Json.Int(Json.Obj(p.Value), "ordinary_definitions"))));
                    for (int k = start; k < upto; k++) tierOf[chosen[k]] = Json.Int(Json.Obj(p.Value), "tier");
                    start = Mathf.Max(start, upto);
                }
                for (int k = start; k < chosen.Count; k++) tierOf[chosen[k]] = orderedPresets.Count - 1;
                var defs = chosen.Select(cat.Get).ToList();
                AssignVisualScale(defs, Json.Arr(Json.Obj(a["visual_size_bands"])[kv.Key]), rng);
                picked.AddRange(defs);
            }
            var robots = Shared.ReadObject(Shared.DataPath("robot_parts.json"));

            foreach (var p in orderedPresets)
            {
                var pc = Json.Obj(p.Value);
                int tier = Json.Int(pc, "tier");
                var defs = picked.Where(d => tierOf[d.id] <= tier).OrderBy(d => d.category).ThenBy(d => SortKey(cat, d.id)).ToList();
                int target = Json.Int(pc, "total_items") - Json.Int(pc, "robot_part_items");
                var prng = new System.Random(Json.Int(a, "seed", 1) * 31 + tier);
                AssignSpawns(defs, a, target, prng);
                var (units, dests) = Pack(defs, a);
                Write(p.Key, pc, a, defs, robots, units, dests, tierOf);
                Debug.Log($"[author] {p.Key}: {defs.Count} definitions, {defs.Sum(d => d.spawnCount)} ordinary items, {units.Count} units, {dests.Count} destinations");
            }
            if (Application.isBatchMode) EditorApplication.Exit(0);
        }

        /// <summary>Loads every preset like the game does (database validation, layout
        /// and world generation) and reports problems. Exit code 1 on any error.
        /// CLI: -executeMethod WorkshopWreckage.EditorTools.CatalogAuthoring.ValidateAll</summary>
        [MenuItem("Workshop Wreckage/Validate all presets")]
        public static void ValidateAll()
        {
            int failures = 0;
            foreach (var (id, pc) in ItemDatabase.Presets())
            {
                var db = ItemDatabase.LoadPreset(id);
                if (db.IsValid) db.Validate();
                foreach (var e in db.errors.Take(30)) Debug.LogError($"[validate] {id}: {e}");
                if (!db.IsValid) { failures++; continue; }
                foreach (int seed in new[] { Json.Int(db.config, "default_seed", 1912), 7 })
                {
                    var sw = System.Diagnostics.Stopwatch.StartNew();
                    var layout = WorkshopLayout.Generate(db, seed);
                    double layoutMs = sw.Elapsed.TotalMilliseconds;
                    var gen = new WorldGenerator();
                    var world = gen.Generate(db, layout, seed);
                    foreach (var e in gen.errors.Take(30)) Debug.LogError($"[validate] {id} seed {seed}: {e}");
                    if (world == null) { failures++; continue; }
                    // containment: every item and storage unit must be inside the room
                    var lo = layout.roomMin; var hi = layout.roomMax;
                    bool Inside(Vector3 g, float m) => g.x > lo.x + m && g.x < hi.x - m && g.z > lo.z + m && g.z < hi.z - m && g.y > -0.05f && g.y < hi.y;
                    int outside = world.instances.Count(i => !Inside(new Vector3(i.Position.x, i.Position.y, -i.Position.z), 0));
                    foreach (var kv in layout.units)
                    {
                        var p = WorkshopLayout.V(kv.Value["pos"]);
                        float r = (float)Json.Num(kv.Value, "depth") / 2 - 0.01f;
                        if (!Inside(p, r)) { Debug.LogError($"[validate] {id} seed {seed}: storage unit {kv.Key} is outside the room at {p}"); failures++; }
                    }
                    if (outside > 0) { Debug.LogError($"[validate] {id} seed {seed}: {outside} items outside the room"); failures++; }
                    var bySurface = new Dictionary<string, int>();
                    foreach (var inst in world.instances)
                    {
                        float y = inst.Position.y;
                        string k = y < 0.3f ? "floor" : y < 1.0f ? "low (pallet/bench/shelf)" : "high (shelf)";
                        bySurface[k] = bySurface.GetValueOrDefault(k) + 1;
                    }
                    Debug.Log($"[validate] {id} seed {seed}: OK — {world.Count} items, {layout.units.Count} units, {layout.piles.Count} pile sites, " +
                              $"{layout.surfaces.Count} surfaces, room {layout.roomMax.x - layout.roomMin.x}×{layout.roomMax.z - layout.roomMin.z} m, " +
                              $"layout {layoutMs:0} ms, world {gen.lastGenerationMs:0} ms; heights: " + string.Join(", ", bySurface.Select(kv => $"{kv.Key} {kv.Value}")));
                }
            }
            if (Application.isBatchMode) EditorApplication.Exit(failures == 0 ? 0 : 1);
        }

        // ------------------------------------------------------------------ choosing

        static string SortKey(ProceduralCatalog cat, int id) => string.Join(",", cat.Decode(id).Select(x => x.ToString("000")));

        static readonly Dictionary<int, int[]> MaterialWeights = new()
        {
            [-1] = new[] { 40, 16, 14, 4, 6, 6, 8, 10 },
            [4] = new[] { 60, 25, 4, 1, 0, 4, 6, 8 },
            [5] = new[] { 55, 30, 6, 0, 0, 0, 4, 10 },
            [3] = new[] { 36, 8, 26, 6, 8, 8, 6, 8 },
            [9] = new[] { 30, 4, 8, 2, 12, 22, 10, 12 },
        };

        static int Weighted(System.Random r, int[] w)
        {
            int total = w.Sum(), x = r.Next(total);
            for (int i = 0; i < w.Length; i++) { if (x < w[i]) return i; x -= w[i]; }
            return w.Length - 1;
        }

        static List<int> Choose(ProceduralCatalog cat, int c, int count, System.Random r)
        {
            var o = new List<int>();
            var used = new HashSet<int>();
            var axes = ProceduralCatalog.Axes[c];
            int ma = ProceduralCatalog.MaterialAxis[c];
            int guard = 0;
            while (o.Count < count && guard++ < count * 400)
            {
                var idx = axes.Select(n => r.Next(n)).ToArray();
                if (ma >= 0)
                {
                    int m = Weighted(r, MaterialWeights.TryGetValue(c, out var mw) ? mw : MaterialWeights[-1]);
                    idx[ma] = m;
                    if (ProceduralCatalog.Materials[m] == "steel") idx[ma + 1] = Weighted(r, new[] { 55, 20, 12, 13 });
                    else if (ProceduralCatalog.Materials[m] != "painted") idx[ma + 1] = r.NextDouble() < 0.75 || m >= 6 ? 0 : 3;
                }
                int id = cat.Encode(c, idx);
                if (!used.Add(id) || !cat.IsPlausible(cat.Get(id))) continue;
                o.Add(id);
            }
            return o;
        }

        static void AssignVisualScale(List<ItemDefinition> defs, List<object> band, System.Random r)
        {
            var reals = defs.Select(d => { var b = MeshLibrary.UnitBounds(d.geometryKey).size; return Mathf.Max(b.x, b.y, b.z) * d.meshScaleMm; }).ToList();
            float rmin = reals.Min(), rmax = reals.Max(), lo = Json.F(band[0]), hi = Json.F(band[1]);
            for (int k = 0; k < defs.Count; k++)
            {
                float t = rmax <= rmin ? 0.5f : Mathf.Log(reals[k] / rmin) / Mathf.Log(rmax / rmin);
                float target = Mathf.Exp(Mathf.Lerp(Mathf.Log(lo), Mathf.Log(hi), t)) * (0.92f + 0.16f * (float)r.NextDouble());
                defs[k].visualScale = target * 1000f / reals[k];
                defs[k].worldScale = defs[k].meshScaleMm / 1000f * defs[k].visualScale;
            }
        }

        static void AssignSpawns(List<ItemDefinition> defs, Dictionary<string, object> a, int target, System.Random r)
        {
            var ranges = Json.Obj(a["spawn_ranges"]);
            var high = Json.Arr(a["high_quantity_categories"]).Select(x => x.ToString()).ToHashSet();
            int maxDefault = Json.Int(a, "max_spawn_default", 10), maxHigh = Json.Int(a, "max_spawn_high_quantity", 25), min = Json.Int(a, "min_spawn", 3);
            foreach (var d in defs)
            {
                var rg = Json.Arr(ranges.TryGetValue(d.CategoryName, out var o) ? o : high.Contains(d.CategoryName) ? ranges["high_quantity"] : ranges["default"]);
                d.spawnCount = r.Next((int)Json.F(rg[0]), (int)Json.F(rg[1]) + 1);
            }
            int diff = target - defs.Sum(d => d.spawnCount), guard = 0;
            while (diff != 0 && guard++ < 1000000)
            {
                var d = defs[r.Next(defs.Count)];
                int hi = high.Contains(d.CategoryName) ? maxHigh : maxDefault;
                if (diff > 0 && d.spawnCount < hi) { d.spawnCount++; diff--; }
                else if (diff < 0 && d.spawnCount > min) { d.spawnCount--; diff++; }
            }
            if (diff != 0) Debug.LogError($"[author] could not reach {target} items within the per-definition limits (off by {diff})");
        }

        // ------------------------------------------------------------------ storage

        static Vector3 Inner(Dictionary<string, object> a, string kind) => WorkshopLayout.V(Json.Obj(Json.Obj(a["kind_specs"])[kind])["inner"]);

        static string ChooseKind(ItemDefinition d, Dictionary<string, object> a)
        {
            var e = ItemDatabase.Extents(d);
            float clearance = (float)Json.Num(Json.Obj(a["shelving"]), "clearance");
            foreach (var ko in Json.Arr(Json.Obj(a["destination_preferences"])[d.CategoryName]))
            {
                var kind = ko.ToString();
                if (kind == "floor_pallet") return kind;
                if (kind == "shelf_section")
                {
                    if (SlotWidth(d, kind, a) <= (float)Json.Num(a, "max_section_width") && e[2] <= clearance - 0.04f) return kind;
                    continue;
                }
                var inner = Inner(a, kind);
                var dims = new[] { inner.x, inner.y, inner.z }.OrderByDescending(x => x).ToArray();
                if (e[0] > dims[0] || e[1] > dims[1] || e[2] > dims[2]) continue;
                if (d.spawnCount * e[0] * e[1] * e[2] > inner.x * inner.y * inner.z * (float)Json.Num(a, "packing")) continue;
                return kind;
            }
            return "shelf_section";
        }

        static float SlotWidth(ItemDefinition d, string kind, Dictionary<string, object> a)
        {
            if (kind != "shelf_section") return (float)Json.Num(Json.Obj(Json.Obj(a["kind_specs"])[kind]), "slot_width");
            var e = ItemDatabase.Extents(d);
            float depth = Inner(a, "shelf_section").y;
            if (e[0] <= depth)
            {
                int perRow = Mathf.Max(1, (int)(depth / Mathf.Max(e[1], 0.01f)));
                int cols = Mathf.CeilToInt((float)d.spawnCount / perRow);
                return Mathf.Clamp(cols * e[1] * 1.1f + 0.05f, 0.3f, 1.2f);
            }
            return Mathf.Clamp(e[0] + 0.06f, 0.3f, 1.4f);
        }

        static (List<Dictionary<string, object>> units, List<Dictionary<string, object>> dests) Pack(List<ItemDefinition> defs, Dictionary<string, object> a)
        {
            var units = new List<Dictionary<string, object>>();
            var dests = new List<Dictionary<string, object>>();
            int cap = Json.Int(a, "default_box_capacity", 10), capLarge = Json.Int(a, "large_box_capacity", 25);
            var drawers = new List<ItemDefinition>();
            var shelf = new List<(ItemDefinition d, string kind, float w)>();
            var pallets = new List<ItemDefinition>();
            foreach (var d in defs)
            {
                var kind = ChooseKind(d, a);
                if (kind == "drawer") drawers.Add(d);
                else if (kind == "floor_pallet") pallets.Add(d);
                else shelf.Add((d, kind, SlotWidth(d, kind, a)));
            }
            // drawer cabinets
            var dc = Json.Obj(a["drawer_cabinet"]);
            int rows = Json.Int(dc, "rows", 6), cols = Json.Int(dc, "cols", 6), per = rows * cols;
            int nCab = Mathf.CeilToInt(drawers.Count / (float)per);
            var dInner = Inner(a, "drawer");
            for (int c = 0; c < nCab; c++)
            {
                var cats = new List<object>();
                for (int slot = 0; slot < per; slot++)
                {
                    int k = c * per + slot, row = slot / cols, col = slot % cols;
                    var code = Letters[row] + (col + 1);
                    var id = $"DC{c + 1}-{code}";
                    var dest = new Dictionary<string, object>
                    {
                        ["id"] = id, ["unit"] = $"DC{c + 1}", ["kind"] = "drawer", ["slot"] = code, ["level"] = (double)(rows - 1 - row),
                        ["row"] = (double)row, ["col"] = (double)col, ["width"] = (double)dInner.x, ["depth"] = (double)dInner.y, ["height"] = (double)dInner.z,
                        ["elevation_m"] = System.Math.Round(0.15 + (rows - 1 - row) * 0.27, 2), ["capacity"] = (double)cap,
                    };
                    if (k < drawers.Count)
                    {
                        drawers[k].storageBox = id;
                        if (drawers[k].spawnCount > cap) dest["capacity"] = (double)capLarge;
                        if (!cats.Contains(drawers[k].CategoryName)) cats.Add(drawers[k].CategoryName);
                    }
                    dests.Add(dest);
                }
                units.Add(new Dictionary<string, object>
                {
                    ["id"] = $"DC{c + 1}", ["type"] = "drawer_cabinet", ["rows"] = (double)rows, ["cols"] = (double)cols,
                    ["width"] = dc["width"], ["depth"] = dc["depth"], ["height"] = dc["height"], ["categories"] = cats, ["label"] = string.Join(" · ", cats).ToUpper(),
                });
            }
            // shelving, first-fit across the current unit's levels
            var sh = Json.Obj(a["shelving"]);
            float shW = (float)Json.Num(sh, "width");
            var levels = Json.Arr(sh["levels"]).Select(Json.F).ToList();
            float clearance = (float)Json.Num(sh, "clearance");
            int shN = 0;
            var levelX = new List<float>();
            var levelPos = new List<int>();
            Dictionary<string, object> curUnit = null;
            foreach (var (d, kind, w) in shelf)
            {
                int level = -1;
                for (int l = 0; l < levelX.Count; l++) if (levelX[l] + w <= shW - 0.03f) { level = l; break; }
                if (level < 0)
                {
                    shN++;
                    level = 0;
                    levelX = levels.Select(_ => 0.03f).ToList();
                    levelPos = levels.Select(_ => 0).ToList();
                    curUnit = new Dictionary<string, object>
                    {
                        ["id"] = $"SH{shN}", ["type"] = "shelving", ["width"] = (double)shW, ["depth"] = sh["depth"],
                        ["height"] = (double)(levels[^1] + clearance), ["levels"] = levels.Select(x => (object)(double)x).ToList(), ["categories"] = new List<object>(),
                    };
                    units.Add(curUnit);
                }
                float x = levelX[level];
                var inner = Inner(a, kind);
                var code = (level + 1) + Letters[levelPos[level]];
                var id = $"SH{shN}-{code}";
                dests.Add(new Dictionary<string, object>
                {
                    ["id"] = id, ["unit"] = $"SH{shN}", ["kind"] = kind, ["slot"] = code, ["level"] = (double)level, ["x"] = System.Math.Round(x + w / 2, 3),
                    ["width"] = System.Math.Round(kind != "shelf_section" ? inner.x : w - 0.04f, 3), ["depth"] = (double)inner.y,
                    ["height"] = (double)(kind != "shelf_section" ? inner.z : clearance - 0.04f), ["elevation_m"] = (double)levels[level],
                    ["capacity"] = (double)Mathf.Max(cap, d.spawnCount), ["slot_width"] = System.Math.Round(w, 3),
                });
                d.storageBox = id;
                var cats = (List<object>)curUnit["categories"];
                if (!cats.Contains(d.CategoryName)) cats.Add(d.CategoryName);
                curUnit["label"] = string.Join(" · ", cats).ToUpper();
                levelX[level] = x + w + 0.03f;
                levelPos[level]++;
            }
            // pallet zones
            var pz = Json.Obj(a["pallet_zone"]);
            int pCols = Json.Int(pz, "cols", 4), pRows = Json.Int(pz, "rows", 2), perZone = pCols * pRows;
            float pitch = (float)Json.Num(pz, "pitch");
            var pInner = Inner(a, "floor_pallet");
            for (int zi = 0; zi < Mathf.CeilToInt(pallets.Count / (float)perZone); zi++)
                units.Add(new Dictionary<string, object>
                {
                    ["id"] = $"PZ{zi + 1}", ["type"] = "pallet_zone", ["width"] = (double)(pCols * pitch), ["depth"] = (double)(pRows * pitch), ["height"] = 0.9,
                    ["categories"] = new List<object> { "Machine Parts" }, ["label"] = "HEAVY PARTS",
                });
            for (int k = 0; k < pallets.Count; k++)
            {
                int zi = k / perZone, pk = k % perZone;
                var id = $"PZ{zi + 1}-P{pk + 1}";
                dests.Add(new Dictionary<string, object>
                {
                    ["id"] = id, ["unit"] = $"PZ{zi + 1}", ["kind"] = "floor_pallet", ["slot"] = $"P{pk + 1}", ["level"] = 0.0,
                    ["x"] = (double)(pitch * (pk % pCols + 0.5f)), ["row"] = (double)(pk / pCols), ["width"] = (double)pInner.x, ["depth"] = (double)pInner.y,
                    ["height"] = (double)pInner.z, ["elevation_m"] = 0.0, ["capacity"] = (double)Mathf.Max(cap, pallets[k].spawnCount), ["slot_width"] = (double)pitch,
                });
                pallets[k].storageBox = id;
            }
            return (units, dests);
        }

        // ------------------------------------------------------------------ output

        static Dictionary<string, object> DefDict(ItemDefinition d, int tier) => new()
        {
            ["id"] = (double)d.id, ["name"] = d.displayName, ["category"] = d.CategoryName, ["subcategory"] = d.subcategory, ["shape"] = d.shape,
            ["geometry_key"] = d.geometryKey, ["material"] = d.material, ["finish"] = d.finish, ["color"] = "#" + ColorUtility.ToHtmlStringRGB(d.variantColor).ToLower(),
            ["diameter_mm"] = (double)d.diameterMm, ["length_mm"] = (double)d.lengthMm, ["mesh_scale_mm"] = (double)d.meshScaleMm,
            ["visual_scale"] = System.Math.Round(d.visualScale, 3), ["thread_size"] = d.threadSize ?? "", ["thread_pitch_mm"] = (double)d.threadPitchMm,
            ["weight_g"] = (double)d.weightG, ["magnetic"] = d.magnetic, ["rarity"] = (double)d.rarity, ["storage_category"] = d.CategoryName,
            ["storage_box"] = d.storageBox, ["spawn_count"] = (double)d.spawnCount, ["crafting_purpose"] = d.craftingPurpose ?? "",
            ["rest_mode"] = d.restMode == RestMode.Long ? "long" : "flat", ["tier"] = (double)tier,
        };

        static void Write(string id, Dictionary<string, object> pc, Dictionary<string, object> a, List<ItemDefinition> defs, Dictionary<string, object> robots,
            List<Dictionary<string, object>> units, List<Dictionary<string, object>> dests, Dictionary<int, int> tierOf)
        {
            var dir = Path.Combine(Shared.Root, "data", "presets", id);
            Directory.CreateDirectory(dir);
            var config = new Dictionary<string, object>
            {
                ["version"] = 2.0, ["preset"] = id, ["label"] = pc["label"], ["default_seed"] = pc["default_seed"],
                ["total_items"] = pc["total_items"], ["ordinary_items"] = (double)(Json.Int(pc, "total_items") - Json.Int(pc, "robot_part_items")),
                ["robot_part_items"] = pc["robot_part_items"], ["ordinary_definitions"] = pc["ordinary_definitions"],
                ["max_spawn_default"] = a["max_spawn_default"], ["max_spawn_high_quantity"] = a["max_spawn_high_quantity"],
                ["high_quantity_categories"] = a["high_quantity_categories"], ["default_box_capacity"] = a["default_box_capacity"],
                ["large_box_capacity"] = a["large_box_capacity"], ["item_visual_scale"] = 2.0, ["room"] = pc["room"], ["aisle_rows"] = pc["aisle_rows"],
            };
            var items = defs.Select(d => (object)DefDict(d, tierOf[d.id])).ToList();
            items.AddRange(Json.Arr(robots["items"]));
            File.WriteAllText(Path.Combine(dir, "world_config.json"), Json.Write(config));
            File.WriteAllText(Path.Combine(dir, "item_catalog.json"), Json.Write(new Dictionary<string, object> { ["version"] = 3.0, ["preset"] = id, ["items"] = items }));
            File.WriteAllText(Path.Combine(dir, "storage_layout.json"), Json.Write(new Dictionary<string, object>
            {
                ["version"] = 3.0, ["preset"] = id, ["units"] = units.Cast<object>().ToList(), ["destinations"] = dests.Cast<object>().ToList(), ["stations"] = robots["stations"],
            }));
        }
    }
}
