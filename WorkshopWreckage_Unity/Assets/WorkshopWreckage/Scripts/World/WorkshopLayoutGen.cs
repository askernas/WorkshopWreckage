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
        /// <summary>Highest shelf deck (m) that gets unsorted parts: below eye height, so they stay visible and reachable.</summary>
        public const float MaxStrayShelf = 1.2f;
        public Vector3 roomMin, roomMax, spawn;
        public float spawnYaw;
        public readonly List<Dictionary<string, object>> furniture = new();
        public readonly List<Dictionary<string, object>> windows = new();
        public readonly List<Vector3> lamps = new();
        /// <summary>Overhead fluorescent fixtures (Godot space, hanging height): the workshop's main light.</summary>
        public readonly List<Vector3> fixtures = new();
        public readonly List<(string name, Rect rect)> zones = new();
        public readonly Dictionary<string, Dictionary<string, object>> units = new();
        public readonly List<Rect> floorBlockers = new();
        public readonly List<PileSurfaceDef> surfaces = new();
        /// <summary>Every shelf deck (Godot XZ rect, height): what a dropped or falling part rests on.</summary>
        public readonly List<(Rect rect, float y)> decks = new();
        public readonly List<PileSite> piles = new();
        public readonly List<string> placementErrors = new();
        public readonly Dictionary<string, List<int>> robotPartPiles = new();
        /// <summary>Open floor near the spawn: Retrobot's parts lie here in plain sight, and
        /// the starter assortment (a few different parts) lies around starterSpot.</summary>
        public readonly List<Vector2> retroSpots = new();
        public Vector2 starterSpot;
        public Vector3 repairBayPos;
        public Rect trayRect;                                     // sorting tray (Godot XZ); kept clear of generated debris
        /// <summary>Height of the sorting tray's bottom (parts in the tray rest on it).</summary>
        public const float TrayFloor = 0.04f;
        public readonly List<Rect> keepClear = new();             // no starting piles here, but walkable
        /// <summary>Clear floor in front of every storage unit (Godot XZ): no furniture or other
        /// unit may stand there (loose parts may lie there); reachable from the spawn on foot.</summary>
        public readonly Dictionary<string, Rect> access = new();
        const float AccessDepth = 0.9f;
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
            // the entry door, well clear of the Robot Workbench: nothing stands within 30 cm of
            // its frame, and a walkway in front of it is kept free
            float doorX = west + 5.6f;
            F("door", new Vector3(doorX, 0, south - 0.03f), new Vector3(1.3f, 2.4f, 0.1f));
            Block(new Vector2(doorX, south - 0.2f), new Vector2(1.3f + 0.2f + 0.6f, 0.4f));
            keepClear.Add(new Rect(doorX - 1.05f, south - 1.7f, 2.1f, 1.5f));
            F("barrel", new Vector3(west + 9.6f, 0, south - 0.6f), new Vector3(0.6f, 0.9f, 0.6f));
            Block(new Vector2(west + 9.6f, south - 0.6f), new Vector2(0.65f, 0.65f));
            F("stool", new Vector3(-1.3f, 0, north + 1.7f), new Vector3(0.4f, 0.65f, 0.4f));
            Block(new Vector2(-1.3f, north + 1.7f), new Vector2(0.4f, 0.4f));
            repairBayPos = new Vector3(west + 1.83f, 0, south - 0.5f);   // 3.45 m bench, 0.1 m from the west wall
            repairBayRot = Mathf.PI;
            Block(new Vector2(repairBayPos.x, repairBayPos.z), new Vector2(3.55f, 1.0f));
            // sorting tray in front of the repair bay: drop any parts here and the robots sort them
            // against the south wall beside the door: out of the way of the workbench and the walkways
            // against the wall (the sign hangs on the wall above it), 1.6 m along it and 0.9 m deep: a
            // robot takes any part in it from beside the rim, however deep it lies (Robot.TrayGoal).
            // Its rim is solid (a floor blocker) and its bottom is a surface parts rest on.
            trayRect = new Rect(doorX + 1.55f, south - 0.94f, 1.6f, 0.9f);
            Block(trayRect.center, trayRect.size);
            decks.Add((trayRect, TrayFloor));
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
            // rows of industrial fluorescent fixtures from wall to wall (~4.5 × 4 m apart)
            int nx = Mathf.Max(2, Mathf.RoundToInt((W - 4.4f) / 4.5f) + 1), nz = Mathf.Max(2, Mathf.RoundToInt((D - 4.6f) / 4f) + 1);
            for (int ix = 0; ix < nx; ix++)
                for (int iz = 0; iz < nz; iz++)
                    fixtures.Add(new Vector3(Mathf.Lerp(west + 2.2f, east - 2.2f, ix / (float)(nx - 1)), 3.9f, Mathf.Lerp(north + 2.4f, south - 2.2f, iz / (float)(nz - 1))));

            // --- storage runs: walls first, then freestanding aisles ----------------
            // Godot-space directions (Unity's Vector3.forward/back have the opposite Z meaning)
            var toEast = new Vector3(1, 0, 0); var toWest = new Vector3(-1, 0, 0);
            var toSouth = new Vector3(0, 0, 1); var toNorth = new Vector3(0, 0, -1);
            Run(new Vector3(west + 0.05f, 0, north), toEast, toSouth, -3.4f - (west + 0.05f), "drawer_cabinet", "shelving");
            Run(new Vector3(3.4f, 0, north), toEast, toSouth, east - 0.5f - 3.4f, "drawer_cabinet", "shelving");
            Run(new Vector3(east, 0, north + 0.75f), toSouth, toWest, D - 1.5f, "shelving", "drawer_cabinet", "pallet_zone");
            Run(new Vector3(east - 0.75f, 0, south), toWest, toNorth, east - 0.75f - 0.75f, "shelving", "pallet_zone");
            Run(new Vector3(west, 0, north + 0.2f), toSouth, toEast, D - 3.2f, "pallet_zone");
            // freestanding back-to-back aisles, with room to walk between them (AisleClear)
            float x0 = 3.2f, len = east - x0 - (1.2f + AisleClear);
            for (int r = 0; r < aisleRows; r++)
            {
                float z = south - (1.2f + AisleClear + 0.6f) - r * (1.2f + AisleClear);
                if (z < north + 0.6f + AisleClear + 0.6f) break;
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
                    if (!((List<string>)run["types"]).Contains(type)) continue;
                    var dir = (Vector3)run["dir"];
                    var n = (Vector3)run["normal"];
                    bool alongX = Mathf.Abs(dir.x) > 0.5f;
                    // where runs meet at a corner, slide along until the footprint is clear of
                    // everything already standing there (units never overlap)
                    while (used[i] + w <= (float)run["length"] + 0.001f)
                    {
                        var c = (Vector3)run["start"] + dir * (used[i] + w / 2) + n * (d / 2 + 0.02f);
                        if (Clear(Footprint(c, w, d, alongX)) && AccessOk(c, n, w, d, alongX))
                        {
                            PlaceUnit(db, u, c, n, alongX);
                            used[i] += w + UnitGap;
                            placed = true;
                            break;
                        }
                        used[i] += 0.2f;
                    }
                }
                if (!placed) leftover.Add(u);
            }
            // whatever the runs could not hold becomes freestanding islands on open floor
            foreach (var u in leftover)
            {
                if (PlaceIsland(db, u)) continue;
                placementErrors.Add($"No room for storage unit {Json.Str(u, "id")} ({Json.Str(u, "type")}, {Json.Num(u, "width"):0.00} m) in a {W}×{D} m workshop");
            }

            // no two storage units may ever overlap
            var rects = units.Select(kv => (kv.Key, r: Footprint(WorkshopLayout.V(kv.Value["pos"]), (float)Json.Num(kv.Value, "width"), (float)Json.Num(kv.Value, "depth"), Mathf.Abs(Mathf.Sin((float)Json.Num(kv.Value, "rot_y"))) < 0.5f))).ToList();
            for (int a = 0; a < rects.Count; a++)
                for (int b = a + 1; b < rects.Count; b++)
                    if (rects[a].r.Overlaps(rects[b].r)) placementErrors.Add($"Storage units {rects[a].Key} and {rects[b].Key} overlap");

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
                            // a tipped container never lands in front of storage (the parts may spill there)
                            var box = new Rect(origin - new Vector2(0.4f, 0.4f), new Vector2(0.8f, 0.8f));
                            if (access.Values.Any(a => a.Overlaps(box))) break;
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

            // open spots near the start, away from every heap: Retrobot's parts (the middle of the fan,
            // straight ahead, first: the chassis goes there) and then the starter assortment beside them
            var sp = new Vector2(spawn.x, spawn.z);
            var taken = piles.Where(p => p.kind != "scatter").Select(p => p.center).ToList();
            foreach (int k in new[] { 2, 1, 3, 0, 4 })
            {
                float a = -Mathf.PI / 2 + (k - 2) * 0.62f;   // a fan in front of the spawn (Godot -z = north = ahead)
                var s = OpenSpot(sp + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * (2.6f + 0.5f * (k % 2)), 0, 3f, taken);
                if (s == null) continue;
                retroSpots.Add(s.Value);
                taken.Add(s.Value);
            }
            taken.Add(sp);   // not under the player's feet
            starterSpot = OpenSpot(sp + new Vector2(1.5f, -0.9f), 0, 2.5f, taken) ?? sp + new Vector2(1.4f, -0.6f);

            CheckReachable();

            // --- zones -----------------------------------------------------------------
            zones.Add(("North Workbench", new Rect(west, north, W, 3.0f)));
            zones.Add(("Window Bay", new Rect(west, north + 3f, W * 0.27f, D - 3f)));
            zones.Add(("Shelving Aisles", new Rect(east - W * 0.3f, north + 3f, W * 0.3f, D - 3f)));
            zones.Add(("Central Floor", new Rect(west + W * 0.27f, north + 3f, W * 0.43f, D - 3f)));
        }

        /// <summary>Walking room between facing storage (Godot metres).</summary>
        const float AisleClear = 2.4f;

        static Rect Footprint(Vector3 c, float w, float d, bool alongX)
        {
            var s = alongX ? new Vector2(w, d) : new Vector2(d, w);
            return new Rect(new Vector2(c.x, c.z) - s / 2, s);
        }

        /// <summary>The strip of floor in front of a unit (the side its normal n faces).</summary>
        static Rect AccessRect(Vector3 c, Vector3 n, float w, float d, bool alongX)
        {
            var f = new Vector2(c.x, c.z) + new Vector2(n.x, n.z) * (d / 2 + 0.08f + AccessDepth / 2);
            var s = alongX ? new Vector2(w, AccessDepth) : new Vector2(AccessDepth, w);
            return new Rect(f - s / 2, s);
        }

        static Rect Shrink(Rect r, float m) => new(r.position + Vector2.one * m, r.size - Vector2.one * 2 * m);

        /// <summary>A unit may stand here if it blocks no other unit's front and its own front
        /// strip is free of furniture and units (and inside the room).</summary>
        bool AccessOk(Vector3 c, Vector3 n, float w, float d, bool alongX)
        {
            var foot = Shrink(Footprint(c, w, d, alongX), 0.02f);
            if (access.Values.Any(a => a.Overlaps(foot))) return false;
            var ar = Shrink(AccessRect(c, n, w, d, alongX), 0.08f);
            if (ar.xMin < roomMin.x || ar.xMax > roomMax.x || ar.yMin < roomMin.z || ar.yMax > roomMax.z) return false;
            return !floorBlockers.Any(b => b.Overlaps(ar));
        }

        /// <summary>Every unit's front strip must be reachable on foot from the spawn: a flood
        /// fill over the floor (0.1 m cells, player-sized clearance) around all blockers.</summary>
        void CheckReachable()
        {
            const float cell = 0.1f, radius = 0.22f;
            int nx = Mathf.CeilToInt((roomMax.x - roomMin.x) / cell), nz = Mathf.CeilToInt((roomMax.z - roomMin.z) / cell);
            var blocked = new bool[nx, nz];
            foreach (var b in floorBlockers)
            {
                int x0 = Mathf.Max(0, Mathf.FloorToInt((b.xMin - radius - roomMin.x) / cell)), x1 = Mathf.Min(nx - 1, Mathf.CeilToInt((b.xMax + radius - roomMin.x) / cell));
                int z0 = Mathf.Max(0, Mathf.FloorToInt((b.yMin - radius - roomMin.z) / cell)), z1 = Mathf.Min(nz - 1, Mathf.CeilToInt((b.yMax + radius - roomMin.z) / cell));
                for (int x = x0; x <= x1; x++) for (int z = z0; z <= z1; z++) blocked[x, z] = true;
            }
            for (int x = 0; x < nx; x++) for (int z = 0; z < nz; z++)
                    if (x * cell < radius || z * cell < radius || (nx - x) * cell < radius || (nz - z) * cell < radius) blocked[x, z] = true;   // walls
            var seen = new bool[nx, nz];
            var q = new Queue<(int, int)>();
            int sx = Mathf.Clamp((int)((spawn.x - roomMin.x) / cell), 0, nx - 1), sz = Mathf.Clamp((int)((spawn.z - roomMin.z) / cell), 0, nz - 1);
            if (blocked[sx, sz]) { placementErrors.Add("The spawn point is blocked"); return; }
            q.Enqueue((sx, sz)); seen[sx, sz] = true;
            while (q.Count > 0)
            {
                var (x, z) = q.Dequeue();
                foreach (var (dx, dz) in new[] { (1, 0), (-1, 0), (0, 1), (0, -1) })
                {
                    int a = x + dx, b = z + dz;
                    if (a < 0 || b < 0 || a >= nx || b >= nz || seen[a, b] || blocked[a, b]) continue;
                    seen[a, b] = true;
                    q.Enqueue((a, b));
                }
            }
            foreach (var kv in access)
            {
                var r = kv.Value;
                bool ok = false;
                for (float x = r.xMin + 0.05f; x < r.xMax && !ok; x += cell)
                    for (float z = r.yMin + 0.05f; z < r.yMax && !ok; z += cell)
                    {
                        int a = (int)((x - roomMin.x) / cell), b = (int)((z - roomMin.z) / cell);
                        if (a >= 0 && b >= 0 && a < nx && b < nz && seen[a, b]) ok = true;
                    }
                if (!ok) placementErrors.Add($"Storage unit {kv.Key} cannot be reached on foot (its front is closed off)");
            }
        }

        /// <summary>No furniture, unit or keep-clear area overlaps this footprint.</summary>
        bool Clear(Rect r)
        {
            r = new Rect(r.position + Vector2.one * 0.02f, r.size - Vector2.one * 0.04f);   // touching neighbours is fine
            return !floorBlockers.Any(b => b.Overlaps(r)) && !keepClear.Any(b => b.Overlaps(r));
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
            access[Json.Str(u, "id")] = AccessRect(c, n, w, d, alongX);
            Block(new Vector2(c.x, c.z), (alongX ? new Vector2(w, d) : new Vector2(d, w)) + new Vector2(0.12f, 0.12f));
            AddUnitSurfaces(db, u, c, rot, alongX);
        }

        /// <summary>Finds open floor for a freestanding unit, keeping a walkway around it.</summary>
        bool PlaceIsland(ItemDatabase db, Dictionary<string, object> u)
        {
            const float Walkway = 1.4f;
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
                    if (floorBlockers.Any(b => b.Overlaps(grown)) || access.Values.Any(a => a.Overlaps(grown))) continue;
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
                    decks.Add((ToWorld(-w / 2, -d / 2, w / 2, d / 2), levels[lv]));
                    // stray parts only where you can see and reach them: no higher than chest height
                    if (levels[lv] > MaxStrayShelf) continue;
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

        /// <summary>The nearest point to `want` (searching outwards up to maxR) that is clear of
        /// furniture and keep-clear areas by 0.5 m and at least 1.5 m from every pile centre.</summary>
        Vector2? OpenSpot(Vector2 want, float minR, float maxR, List<Vector2> avoid)
        {
            for (float r = minR; r <= maxR; r += 0.15f)
                for (int k = 0; k < Mathf.Max(1, (int)(r * 12)); k++)
                {
                    float a = k / Mathf.Max(1f, r * 12) * Mathf.PI * 2;
                    var p = want + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * r;
                    if (p.x < roomMin.x + 1 || p.x > roomMax.x - 1 || p.y < roomMin.z + 1 || p.y > roomMax.z - 1) continue;
                    if (floorBlockers.Concat(keepClear).Any(b => new Rect(b.position - Vector2.one * 0.5f, b.size + Vector2.one).Contains(p))) continue;
                    if (avoid.Any(q => Vector2.Distance(p, q) < 1.5f)) continue;
                    return p;
                }
            return null;
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
                if (keepClear.Any(b => new Rect(b.position - Vector2.one * 1.5f, b.size + Vector2.one * 3f).Contains(p))) continue;   // heaps keep their distance from the door, tray and robot mats
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
