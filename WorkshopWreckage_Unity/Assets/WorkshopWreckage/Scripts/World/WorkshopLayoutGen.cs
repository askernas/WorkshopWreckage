using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace WorkshopWreckage
{
    /// <summary>A surface unsorted parts can rest on (Godot space: XZ rect, height y).</summary>
    public class PileSurfaceDef
    {
        public string name;
        public Rect rect;
        public float y;
        public float maxHeight = 1.25f;            // above y
        public readonly List<(Rect rect, float top)> obstacles = new();   // containers already on the surface
        public readonly List<(Rect rect, float y)> ceilings = new();      // low clearance (under benches)
        public bool floor;
    }

    /// <summary>A pile site: where and how a share of the unsorted parts is placed.</summary>
    public class PileSite
    {
        public int surface;
        public string kind;                        // heap, spill, cluster, feature, scatter
        public Vector2 center, sigma = new(0.5f, 0.5f);
        public float dir, length = 1f, spread = 0.7f;
        public Rect rect;
        public float share;
        public float maxHeight = -1;
        public readonly Dictionary<int, float> bias = new();
    }

    /// <summary>Procedural workshop layout for a world preset (replaces the layout
    /// imported from the Godot reference). Everything is in Godot space (+X east,
    /// -Z north, +Y up) so the existing builders convert it unchanged. Room size,
    /// aisle rows and pile density come from the preset's world_config.json.</summary>
    public class WorkshopLayout
    {
        const float UnitGap = 0.06f;
        public Vector3 roomMin, roomMax, spawn;
        public float spawnYaw;
        public readonly List<Dictionary<string, object>> furniture = new();
        public readonly List<Dictionary<string, object>> windows = new();
        public readonly List<Vector3> lamps = new();
        public readonly List<(string name, Rect rect)> zones = new();
        public readonly Dictionary<string, Dictionary<string, object>> units = new();
        public readonly List<Rect> floorBlockers = new();
        public readonly List<PileSurfaceDef> surfaces = new();
        public readonly List<PileSite> piles = new();
        public readonly List<string> placementErrors = new();
        public readonly Dictionary<string, List<int>> robotPartPiles = new();
        public Vector3 repairBayPos;
        public Rect trayRect;                                     // sorting tray (Godot XZ); kept clear of generated debris
        public readonly List<Rect> keepClear = new();             // no starting piles here, but walkable
        public float repairBayRot;
        readonly List<Dictionary<string, object>> _runs = new();

        static List<object> L(params float[] v) => v.Select(x => (object)(double)x).ToList();
        public static Vector3 V(object o) { var a = Json.Arr(o); return new Vector3(Json.F(a[0]), Json.F(a[1]), Json.F(a[2])); }

        void F(string type, Vector3 pos, Vector3 size, float rot = 0, Dictionary<string, object> extra = null)
        {
            var d = new Dictionary<string, object> { ["type"] = type, ["pos"] = L(pos.x, pos.y, pos.z), ["size"] = L(size.x, size.y, size.z), ["rot"] = (double)rot };
            if (extra != null) foreach (var kv in extra) d[kv.Key] = kv.Value;
            furniture.Add(d);
        }

        void Block(Vector2 c, Vector2 s) => floorBlockers.Add(new Rect(c - s * 0.5f, s));

        public static WorkshopLayout Generate(ItemDatabase db, int seed)
        {
            var l = new WorkshopLayout();
            l.Build(db, seed);
            return l;
        }

        void Build(ItemDatabase db, int seed)
        {
            var room = Json.Arr(db.config.GetValueOrDefault("room", L(22, 15)));
            float W = Json.F(room[0]), D = Json.F(room[1]);
            int aisleRows = Json.Int(db.config, "aisle_rows", 1);
            roomMin = new Vector3(-W / 2, 0, -D / 2);
            roomMax = new Vector3(W / 2, 5.6f, D / 2);
            spawn = new Vector3(0.6f, 0, D / 2 - 3.9f);
            spawnYaw = 0;
            var rng = new System.Random(seed * 7919 + (int)(W * 100));

            // --- fixed furniture, anchored to walls ---------------------------------
            float north = -D / 2, south = D / 2, west = -W / 2, east = W / 2;
            F("bench", new Vector3(0, 0, north + 0.6f), new Vector3(6, 0.94f, 0.9f));
            foreach (var x in new[] { -2.9f, 2.9f }) Block(new Vector2(x, north + 0.6f), new Vector2(0.14f, 0.9f));
            F("pegboard", new Vector3(0, 1.25f, north + 0.03f), new Vector3(5.4f, 1.5f, 0.04f));
            F("bench", new Vector3(0.9f, 0, -0.75f), new Vector3(3.2f, 0.94f, 1.3f), 0, new Dictionary<string, object> { ["shelf"] = true });
            foreach (var x in new[] { -0.6f, 2.4f }) foreach (var z in new[] { -1.3f, -0.2f }) Block(new Vector2(x, z), new Vector2(0.14f, 0.14f));
            F("sign", new Vector3(0, 3.7f, north + 0.05f), new Vector3(4.6f, 1.3f, 0.1f));
            F("clock", new Vector3(3.8f, 3.75f, north + 0.05f), new Vector3(0.9f, 0.9f, 0.08f));
            F("machine", new Vector3(-0.5f, 0, south - 0.9f), new Vector3(2.4f, 1.2f, 0.9f));
            Block(new Vector2(-0.5f, south - 0.9f), new Vector2(2.5f, 1.0f));
            F("door", new Vector3(west + 3.6f, 0, south - 0.03f), new Vector3(1.3f, 2.4f, 0.1f));
            F("barrel", new Vector3(west + 4.7f, 0, south - 0.6f), new Vector3(0.6f, 0.9f, 0.6f));
            Block(new Vector2(west + 4.7f, south - 0.6f), new Vector2(0.65f, 0.65f));
            F("stool", new Vector3(-1.3f, 0, north + 1.7f), new Vector3(0.4f, 0.65f, 0.4f));
            Block(new Vector2(-1.3f, north + 1.7f), new Vector2(0.4f, 0.4f));
            F("blueprint", new Vector3(east - 0.05f, 3.6f, -1.0f), new Vector3(0.02f, 0.8f, 1.1f), -Mathf.PI / 2);
            F("blueprint", new Vector3(west + 6.4f, 3.3f, south - 0.05f), new Vector3(1.0f, 0.75f, 0.02f), Mathf.PI);
            repairBayPos = new Vector3(west + 1.55f, 0, south - 0.5f);
            repairBayRot = Mathf.PI;
            Block(new Vector2(repairBayPos.x, repairBayPos.z), new Vector2(3.0f, 1.0f));
            // sorting tray in front of the repair bay: drop any parts here and the robots sort them
            trayRect = new Rect(west + 2.3f, south - 2.85f, 1.3f, 0.8f);   // clear of the west pallet run and the door
            F("sorting_tray", new Vector3(trayRect.center.x, 0, trayRect.center.y), new Vector3(trayRect.width, 0.12f, trayRect.height));
            keepClear.Add(new Rect(trayRect.position - Vector2.one * 0.5f, trayRect.size + Vector2.one));
            keepClear.Add(new Rect(repairBayPos.x - 1.6f, repairBayPos.z - 2.0f, 3.2f, 1.6f));   // robots' home spots

            int nWin = Mathf.Max(3, Mathf.RoundToInt(D / 5f));
            for (int k = 0; k < nWin; k++)
            {
                float z = Mathf.Lerp(north + 3f, south - 3f, nWin == 1 ? 0.5f : k / (float)(nWin - 1));
                windows.Add(new Dictionary<string, object> { ["z"] = (double)z, ["width"] = 1.8, ["y0"] = 1.0, ["y1"] = 4.4 });
                F("plant", new Vector3(west + 0.25f, 1.0f, z - 0.55f), new Vector3(0.3f, 0.4f, 0.3f));
            }
            lamps.Add(new Vector3(-1.8f, 3, north + 0.9f));
            lamps.Add(new Vector3(1.8f, 3, north + 0.9f));
            lamps.Add(new Vector3(0.9f, 3, -0.75f));
            lamps.Add(new Vector3(repairBayPos.x + 0.4f, 2.7f, repairBayPos.z - 1.3f));   // over the repair bay and Retrobot
            // an even grid from wall to wall (lamp range 7 m), so corners of big rooms aren't dark
            int nx = Mathf.Max(2, Mathf.CeilToInt((W - 3f) / 5f) + 1), nz = Mathf.Max(2, Mathf.CeilToInt((D - 5f) / 5f) + 1);
            for (int ix = 0; ix < nx; ix++)
                for (int iz = 0; iz < nz; iz++)
                {
                    var lp = new Vector3(Mathf.Lerp(west + 2f, east - 2f, ix / (float)(nx - 1)), 3, Mathf.Lerp(north + 3.2f, south - 2f, iz / (float)(nz - 1)));
                    if (lamps.All(l => new Vector2(l.x - lp.x, l.z - lp.z).magnitude > 2.6f)) lamps.Add(lp);
                }

            // --- storage runs: walls first, then freestanding aisles ----------------
            // Godot-space directions (Unity's Vector3.forward/back have the opposite Z meaning)
            var toEast = new Vector3(1, 0, 0); var toWest = new Vector3(-1, 0, 0);
            var toSouth = new Vector3(0, 0, 1); var toNorth = new Vector3(0, 0, -1);
            Run(new Vector3(west + 0.05f, 0, north), toEast, toSouth, -3.4f - (west + 0.05f), "drawer_cabinet", "shelving");
            Run(new Vector3(3.4f, 0, north), toEast, toSouth, east - 0.5f - 3.4f, "drawer_cabinet", "shelving");
            Run(new Vector3(east, 0, north + 0.75f), toSouth, toWest, D - 1.5f, "shelving", "drawer_cabinet", "pallet_zone");
            Run(new Vector3(east - 0.75f, 0, south), toWest, toNorth, east - 0.75f - 0.75f, "shelving", "pallet_zone");
            Run(new Vector3(west, 0, north + 0.2f), toSouth, toEast, D - 3.2f, "pallet_zone");
            float x0 = 3.2f, len = east - x0 - 2.2f;
            for (int r = 0; r < aisleRows; r++)
            {
                float z = south - 3.5f - r * 3.4f;
                if (z < north + 3.6f) break;
                // back-to-back pair: one run faces north, the other south
                Run(new Vector3(x0, 0, z), toEast, toNorth, len, "shelving", "drawer_cabinet");
                Run(new Vector3(x0 + len, 0, z), toWest, toSouth, len, "shelving", "drawer_cabinet");
            }
            var used = new float[_runs.Count];
            var leftover = new List<Dictionary<string, object>>();
            foreach (var u in db.units)
            {
                string id = Json.Str(u, "id"), type = Json.Str(u, "type");
                float w = (float)Json.Num(u, "width", 1), d = (float)Json.Num(u, "depth", 0.5);
                bool placed = false;
                for (int i = 0; i < _runs.Count && !placed; i++)
                {
                    var run = _runs[i];
                    if (!((List<string>)run["types"]).Contains(type) || used[i] + w > (float)run["length"] + 0.001f) continue;
                    var dir = (Vector3)run["dir"];
                    var n = (Vector3)run["normal"];
                    var c = (Vector3)run["start"] + dir * (used[i] + w / 2) + n * (d / 2 + 0.02f);
                    PlaceUnit(db, u, c, n, Mathf.Abs(dir.x) > 0.5f);
                    used[i] += w + UnitGap;
                    placed = true;
                }
                if (!placed) leftover.Add(u);
            }
            // whatever the runs could not hold becomes freestanding islands on open floor
            foreach (var u in leftover)
            {
                if (PlaceIsland(db, u)) continue;
                placementErrors.Add($"No room for storage unit {Json.Str(u, "id")} ({Json.Str(u, "type")}, {Json.Num(u, "width"):0.00} m) in a {W}×{D} m workshop");
            }

            // --- surfaces ----------------------------------------------------------
            var floor = new PileSurfaceDef { name = "floor", rect = new Rect(west + 0.1f, north + 0.1f, W - 0.2f, D - 0.2f), y = 0, floor = true, maxHeight = 1.25f };
            floor.ceilings.Add((new Rect(-3.0f, north + 0.15f, 6.0f, 0.9f), 0.7f));       // under the north bench
            floor.ceilings.Add((new Rect(-0.7f, -1.4f, 3.2f, 1.3f), 0.19f));              // under the island's lower shelf
            surfaces.Insert(0, floor);
            int benchN = surfaces.Count;
            surfaces.Add(new PileSurfaceDef { name = "north_bench", rect = new Rect(-2.9f, north + 0.2f, 5.8f, 0.82f), y = 0.94f, maxHeight = 0.45f });
            surfaces.Add(new PileSurfaceDef { name = "island", rect = new Rect(-0.6f, -1.35f, 3.0f, 1.2f), y = 0.94f, maxHeight = 0.45f });
            surfaces.Add(new PileSurfaceDef { name = "lathe", rect = new Rect(-1.6f, south - 1.12f, 2.2f, 0.42f), y = 0.92f, maxHeight = 0.3f });
            surfaces.Add(new PileSurfaceDef { name = "island_shelf", rect = new Rect(-0.6f, -1.3f, 3.0f, 1.1f), y = 0.24f, maxHeight = 0.5f });

            // --- pile sites ----------------------------------------------------------
            var shares = new Dictionary<string, float> { ["heap"] = 0.40f, ["spill"] = 0.14f, ["cluster"] = 0.12f, ["feature"] = 0.06f, ["scatter"] = 0.03f, ["bench"] = 0.06f, ["shelf"] = 0.13f, ["pallet"] = 0.06f };
            float area = W * D;
            var counts = new Dictionary<string, int>
            {
                ["heap"] = Mathf.Max(3, Mathf.RoundToInt(area / 95f)), ["spill"] = Mathf.Max(4, Mathf.RoundToInt(area / 80f)),
                ["cluster"] = Mathf.Max(4, Mathf.RoundToInt(area / 80f)), ["feature"] = Mathf.Max(4, Mathf.RoundToInt(area / 85f)),
            };
            var spots = FreeFloorSpots(rng, counts.Values.Sum());
            int si = 0;
            var fasteners = new Dictionary<int, float> { [0] = 6, [1] = 6, [2] = 6, [9] = 0.1f, [3] = 0.4f, [6] = 0.3f };
            foreach (var kind in new[] { "heap", "feature", "spill", "cluster" })
            {
                for (int k = 0; k < counts[kind] && si < spots.Count; k++, si++)
                {
                    var c = spots[si];
                    var p = new PileSite { surface = 0, kind = kind, center = c, share = shares[kind] / counts[kind] };
                    switch (kind)
                    {
                        case "heap":
                            p.sigma = new Vector2(0.5f + (float)rng.NextDouble() * 0.15f, 0.45f + (float)rng.NextDouble() * 0.15f);
                            p.bias[9] = 1.4f; p.bias[3] = 1.3f;
                            break;
                        case "feature":
                            p.sigma = new Vector2(0.42f, 0.42f);
                            p.bias[9] = 3f;
                            break;
                        case "spill":
                            p.dir = (float)rng.NextDouble() * Mathf.PI * 2;
                            p.length = 0.8f + (float)rng.NextDouble() * 0.4f;
                            p.spread = 0.7f;
                            var origin = c - new Vector2(Mathf.Cos(p.dir), Mathf.Sin(p.dir)) * 0.55f;
                            string[] kinds = { "tipped_crate", "tipped_bin", "tipped_box", "overturned_chest" };
                            var kindT = kinds[rng.Next(kinds.Length)];
                            var size = kindT switch { "tipped_crate" => new Vector3(0.75f, 0.52f, 0.58f), "tipped_bin" => new Vector3(0.5f, 0.35f, 0.6f), "tipped_box" => new Vector3(0.48f, 0.38f, 0.44f), _ => new Vector3(1.0f, 0.8f, 0.55f) };
                            F(kindT, new Vector3(origin.x, 0, origin.y), size, -p.dir + Mathf.PI / 2);
                            Block(origin, new Vector2(0.8f, 0.8f));
                            break;
                        default:
                            p.sigma = new Vector2(0.28f, 0.24f);
                            foreach (var kv in fasteners) p.bias[kv.Key] = kv.Value;
                            break;
                    }
                    piles.Add(p);
                }
            }
            piles.Add(new PileSite { surface = 0, kind = "scatter", rect = new Rect(west + 1.2f, north + 1.2f, W - 2.4f, D - 2.4f), share = shares["scatter"] });
            piles.Last().bias[9] = 0.2f;
            for (int s = benchN; s < benchN + 4; s++)
            {
                var r = surfaces[s].rect;
                var p = new PileSite { surface = s, kind = "cluster", center = r.center, sigma = new Vector2(r.width * 0.25f, r.height * 0.3f), share = shares["bench"] / 4f, maxHeight = surfaces[s].maxHeight };
                p.bias[7] = 3; p.bias[9] = 0.05f;
                piles.Add(p);
            }
            var shelfSurfaces = Enumerable.Range(0, surfaces.Count).Where(i => surfaces[i].name.StartsWith("shelf")).ToList();
            var palletSurfaces = Enumerable.Range(0, surfaces.Count).Where(i => surfaces[i].name.StartsWith("pallet")).ToList();
            foreach (var s in shelfSurfaces)
            {
                var p = new PileSite { surface = s, kind = "scatter", rect = surfaces[s].rect, share = shares["shelf"] / Mathf.Max(1, shelfSurfaces.Count), maxHeight = surfaces[s].maxHeight };
                p.bias[9] = 0.3f;
                piles.Add(p);
            }
            foreach (var s in palletSurfaces)
            {
                var p = new PileSite { surface = s, kind = "heap", center = surfaces[s].rect.center, sigma = surfaces[s].rect.size * 0.25f, share = shares["pallet"] / Mathf.Max(1, palletSurfaces.Count), maxHeight = 0.7f };
                p.bias[9] = 2.5f; p.bias[3] = 1.5f;
                piles.Add(p);
            }
            float total = piles.Sum(p => p.share);
            foreach (var p in piles) p.share /= total;

            // robot parts: Retrobot near the start (early access), the rest anywhere on the floor
            var floorPiles = Enumerable.Range(0, piles.Count).Where(i => piles[i].surface == 0 && piles[i].kind != "scatter").ToList();
            var nearSpawn = floorPiles.OrderBy(i => Vector2.Distance(piles[i].center, new Vector2(spawn.x, spawn.z))).Take(4).ToList();
            robotPartPiles["retrobot"] = nearSpawn;
            foreach (var robot in new[] { "unibot", "spiderbot", "caterpillarbot", "hoverbot" })
                robotPartPiles[robot] = floorPiles.OrderBy(_ => rng.Next()).Take(4).ToList();

            // --- zones -----------------------------------------------------------------
            zones.Add(("North Workbench", new Rect(west, north, W, 3.0f)));
            zones.Add(("Window Bay", new Rect(west, north + 3f, W * 0.27f, D - 3f)));
            zones.Add(("Shelving Aisles", new Rect(east - W * 0.3f, north + 3f, W * 0.3f, D - 3f)));
            zones.Add(("Central Floor", new Rect(west + W * 0.27f, north + 3f, W * 0.43f, D - 3f)));
        }

        void Run(Vector3 start, Vector3 dir, Vector3 normal, float length, params string[] types)
        {
            if (length <= 0.5f) return;
            _runs.Add(new Dictionary<string, object> { ["start"] = start, ["dir"] = dir, ["normal"] = normal, ["length"] = length, ["types"] = types.ToList() });
        }

        /// <summary>Registers a unit at centre c whose front faces normal n.</summary>
        void PlaceUnit(ItemDatabase db, Dictionary<string, object> u, Vector3 c, Vector3 n, bool alongX)
        {
            float w = (float)Json.Num(u, "width"), d = (float)Json.Num(u, "depth");
            float rot = Mathf.Atan2(n.x, n.z);
            units[Json.Str(u, "id")] = new Dictionary<string, object> { ["pos"] = L(c.x, c.y, c.z), ["rot_y"] = (double)rot, ["width"] = (double)w, ["depth"] = (double)d, ["type"] = Json.Str(u, "type") };
            Block(new Vector2(c.x, c.z), (alongX ? new Vector2(w, d) : new Vector2(d, w)) + new Vector2(0.12f, 0.12f));
            AddUnitSurfaces(db, u, c, rot, alongX);
        }

        /// <summary>Finds open floor for a freestanding unit, keeping a walkway around it.</summary>
        bool PlaceIsland(ItemDatabase db, Dictionary<string, object> u)
        {
            const float Walkway = 1.15f;
            float w = (float)Json.Num(u, "width"), d = (float)Json.Num(u, "depth");
            var foot = new Vector2(w, d);
            var room = new Rect(roomMin.x, roomMin.z, roomMax.x - roomMin.x, roomMax.z - roomMin.z);
            for (float z = roomMin.z + 3.4f; z < roomMax.z - 2.0f; z += 0.25f)
                for (float x = roomMax.x - 1.5f; x > roomMin.x + 1.5f; x -= 0.25f)
                {
                    var c = new Vector2(x, z);
                    var grown = new Rect(c - foot / 2 - Vector2.one * Walkway, foot + Vector2.one * 2 * Walkway);
                    if (grown.xMin < room.xMin + 0.2f || grown.xMax > room.xMax - 0.2f || grown.yMin < room.yMin + 0.2f || grown.yMax > room.yMax - 0.2f) continue;
                    if (Vector2.Distance(c, new Vector2(spawn.x, spawn.z)) < 2.4f) continue;
                    if (floorBlockers.Any(b => b.Overlaps(grown))) continue;
                    PlaceUnit(db, u, new Vector3(x, 0, z), z < 0 ? Vector3.forward : Vector3.back, true);
                    return true;
                }
            return false;
        }

        /// <summary>Shelf decks and pallet tops become surfaces for misplaced (unsorted) parts.</summary>
        void AddUnitSurfaces(ItemDatabase db, Dictionary<string, object> u, Vector3 c, float rot, bool alongX)
        {
            string type = Json.Str(u, "type"), id = Json.Str(u, "id");
            float w = (float)Json.Num(u, "width"), d = (float)Json.Num(u, "depth");
            Rect ToWorld(float lx0, float lz0, float lx1, float lz1)
            {
                // unit-local (x along unit, z toward its front) -> Godot world, rotations are multiples of 90°
                Vector2 P(float lx, float lz)
                {
                    float cs = Mathf.Cos(rot), sn = Mathf.Sin(rot);
                    return new Vector2(c.x + lx * cs + lz * sn, c.z - lx * sn + lz * cs);
                }
                var a = P(lx0, lz0); var b = P(lx1, lz1);
                return Rect.MinMaxRect(Mathf.Min(a.x, b.x), Mathf.Min(a.y, b.y), Mathf.Max(a.x, b.x), Mathf.Max(a.y, b.y));
            }
            if (type == "shelving")
            {
                var levels = Json.Arr(u["levels"]).Select(Json.F).ToList();
                float clearance = (float)Json.Num(u, "height") - levels[^1];
                var dests = db.DestinationsOf(id);
                for (int lv = 0; lv < levels.Count; lv++)
                {
                    var s = new PileSurfaceDef { name = $"shelf_{id}_{lv}", rect = ToWorld(-w / 2 + 0.08f, -d / 2 + 0.06f, w / 2 - 0.08f, d / 2 - 0.06f), y = levels[lv], maxHeight = clearance - 0.12f };
                    foreach (var dd in dests.Where(x => Json.Int(x, "level") == lv))
                    {
                        string kind = Json.Str(dd, "kind");
                        if (kind == "shelf_section") continue;
                        float x = -w / 2 + (float)Json.Num(dd, "x"), hw = (float)Json.Num(dd, "width") / 2 + 0.03f, hd = (float)Json.Num(dd, "depth") / 2 + 0.03f;
                        float top = (float)Json.Num(dd, "height") + 0.03f;
                        s.obstacles.Add((ToWorld(x - hw, -hd, x + hw, hd), levels[lv] + top));
                    }
                    surfaces.Add(s);
                }
            }
            else if (type == "pallet_zone")
                surfaces.Add(new PileSurfaceDef { name = $"pallet_{id}", rect = ToWorld(-w / 2 + 0.05f, -d / 2 + 0.05f, w / 2 - 0.05f, d / 2 - 0.05f), y = 0.14f, maxHeight = 0.75f });
        }

        /// <summary>Well-spread free floor positions (Poisson-disc style, deterministic).</summary>
        List<Vector2> FreeFloorSpots(System.Random rng, int wanted)
        {
            var o = new List<Vector2>();
            float minDist = Mathf.Sqrt((roomMax.x - roomMin.x) * (roomMax.z - roomMin.z) / Mathf.Max(1, wanted)) * 0.8f;
            for (int attempt = 0; attempt < wanted * 300 && o.Count < wanted; attempt++)
            {
                var p = new Vector2(Mathf.Lerp(roomMin.x + 1.6f, roomMax.x - 1.6f, (float)rng.NextDouble()), Mathf.Lerp(roomMin.z + 1.8f, roomMax.z - 1.6f, (float)rng.NextDouble()));
                if (Vector2.Distance(p, new Vector2(spawn.x, spawn.z)) < 1.8f) continue;
                if (floorBlockers.Any(b => new Rect(b.position - Vector2.one * 0.9f, b.size + Vector2.one * 1.8f).Contains(p))) continue;
                if (keepClear.Any(b => new Rect(b.position - Vector2.one * 0.9f, b.size + Vector2.one * 1.8f).Contains(p))) continue;
                if (o.Any(q => Vector2.Distance(p, q) < minDist)) continue;
                o.Add(p);
                if (attempt > wanted * 150) minDist *= 0.97f;
            }
            return o;
        }

        /// <summary>Zone index for a Unity-space position (zones are Godot XZ rects).</summary>
        public int ZoneAt(Vector3 unityPos)
        {
            var q = new Vector2(unityPos.x, -unityPos.z);
            for (int i = 0; i < zones.Count; i++) if (zones[i].rect.Contains(q)) return i;
            return zones.Count - 1;
        }
    }

}
