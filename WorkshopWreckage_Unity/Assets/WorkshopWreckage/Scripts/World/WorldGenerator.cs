using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace WorkshopWreckage
{
    /// <summary>Collision-aware procedural packing on a heightfield
    /// (PileSurface). Items are dropped one at a time: resting height comes from the
    /// heightfield under the footprint, the item tilts with the local slope, steep
    /// spots make it slide downhill and its top is stamped back. Layout space.</summary>
    public class PileSurface
    {
        const float MaxSlope = 0.7f, Repose = 0.95f;
        const float ToolSlope = 0.45f;   // long, flat tools lie nearly level on a pile (about 24 degrees at most), never propped up
        public readonly Rect rect;
        public readonly float baseY, cell;
        readonly int w, h;
        readonly float[] heights;
        public readonly List<Rect> blockers = new();
        /// <summary>Spots a part may never rest at (layout centre, radius): inside closed
        /// containers. Checked before anything is stamped, so nothing floats on a rejected spot.</summary>
        public System.Func<Vector3, float, bool> forbidden;

        public PileSurface(Rect r, float y, float cellSize = 0.012f)
        {
            rect = r; baseY = y;
            cell = Mathf.Max(cellSize, Mathf.Sqrt(r.width * r.height / 6_000_000f));   // keep huge floors bounded
            w = Mathf.Max(2, Mathf.CeilToInt(r.width / cell));
            h = Mathf.Max(2, Mathf.CeilToInt(r.height / cell));
            heights = new float[w * h];
            System.Array.Fill(heights, baseY);
        }

        public float Sample(Vector2 p)
        {
            int ix = Mathf.Clamp((int)((p.x - rect.x) / cell), 0, w - 1);
            int iz = Mathf.Clamp((int)((p.y - rect.y) / cell), 0, h - 1);
            return heights[iz * w + ix];
        }

        float SampleWide(Vector2 p, float r)
        {
            float m = Sample(p);
            if (r > cell)
            {
                m = Mathf.Max(m, Sample(p + new Vector2(r, 0)));
                m = Mathf.Max(m, Sample(p - new Vector2(r, 0)));
                m = Mathf.Max(m, Sample(p + new Vector2(0, r)));
                m = Mathf.Max(m, Sample(p - new Vector2(0, r)));
            }
            return m;
        }

        void StampDisc(Vector2 p, float r, System.Func<Vector2, float> top)
        {
            int x0 = Mathf.Clamp((int)((p.x - r - rect.x) / cell), 0, w - 1), x1 = Mathf.Clamp((int)((p.x + r - rect.x) / cell), 0, w - 1);
            int z0 = Mathf.Clamp((int)((p.y - r - rect.y) / cell), 0, h - 1), z1 = Mathf.Clamp((int)((p.y + r - rect.y) / cell), 0, h - 1);
            float r2 = r * r;
            for (int iz = z0; iz <= z1; iz++)
                for (int ix = x0; ix <= x1; ix++)
                {
                    var c = new Vector2(rect.x + (ix + 0.5f) * cell, rect.y + (iz + 0.5f) * cell);
                    if ((c - p).sqrMagnitude > r2) continue;
                    float t = top(c);
                    int k = iz * w + ix;
                    if (t > heights[k]) heights[k] = t;
                }
        }

        public bool IsFree(Vector2 p, float margin)
        {
            if (p.x < rect.xMin + margin || p.y < rect.yMin + margin || p.x > rect.xMax - margin || p.y > rect.yMax - margin) return false;
            foreach (var b in blockers)
                if (p.x > b.xMin - margin * 0.5f && p.x < b.xMax + margin * 0.5f && p.y > b.yMin - margin * 0.5f && p.y < b.yMax + margin * 0.5f) return false;
            return true;
        }

        Vector2 SettlePoint(Vector2 p, float probe)
        {
            float d = Mathf.Max(0.03f, probe);
            for (int step = 0; step < 10; step++)
            {
                var g = new Vector2((Sample(p + new Vector2(d, 0)) - Sample(p - new Vector2(d, 0))) / (2 * d),
                                    (Sample(p + new Vector2(0, d)) - Sample(p - new Vector2(0, d))) / (2 * d));
                if (g.magnitude < Repose) break;
                p -= g.normalized * 0.02f;
            }
            return p;
        }

        /// <summary>Returns the Unity world matrix, or null if the spot is unusable.
        /// half = world half extents of the item's local AABB (layout axes).</summary>
        public Matrix4x4? Place(ItemDefinition def, Vector3 half, Vector2 at, float yaw, float roll, float scale, float maxY)
        {
            at = SettlePoint(at, Mathf.Max(half.x, half.z) * 0.8f);
            if (Sample(at) > maxY) return null;
            if (def.restMode == RestMode.Side)   // local Z up: placed as a flat part of height half.z, then turned onto that face
            {
                var m = PlaceFlat(new Vector3(half.x, half.z, half.y), at, yaw, scale, maxY, ToolSlope);
                // mostly the front (-Z, the side that faces the room on the board) up, now and then the back
                return m == null ? null : m.Value * Matrix4x4.Rotate(Quaternion.Euler(roll < Mathf.PI * 1.5f ? 90 : -90, 0, 0));
            }
            return def.restMode == RestMode.Long ? PlaceLong(half, at, yaw, roll, scale, maxY) : PlaceFlat(half, at, yaw, scale, maxY);
        }

        Matrix4x4? PlaceLong(Vector3 half, Vector2 at, float yaw, float roll, float scale, float maxY)
        {
            float L = half.y, rho = Mathf.Max(half.x, half.z);
            var dir = new Vector2(Mathf.Cos(yaw), Mathf.Sin(yaw));
            if (!IsFree(at, rho) || !IsFree(at + dir * L, rho) || !IsFree(at - dir * L, rho)) return null;
            int n = Mathf.Clamp((int)(2 * L / cell) + 1, 3, 24);
            var ts = new float[n];
            var hs = new float[n];
            for (int i = 0; i < n; i++)
            {
                ts[i] = Mathf.Lerp(-L, L, i / (float)(n - 1));
                hs[i] = SampleWide(at + dir * ts[i], rho * 0.6f);
            }
            int li = 0, ri = n - 1;
            for (int i = 0; i < n; i++)
            {
                if (ts[i] < 0 && hs[i] > hs[li]) li = i;
                if (ts[i] >= 0 && hs[i] > hs[ri]) ri = i;
            }
            float m = ri != li ? Mathf.Clamp((hs[ri] - hs[li]) / (ts[ri] - ts[li]), -MaxSlope, MaxSlope) : 0;
            float c = float.NegativeInfinity;
            for (int i = 0; i < n; i++) c = Mathf.Max(c, hs[i] - m * ts[i]);
            float pitch = Mathf.Atan(m);
            var axis = new Vector3(dir.x * Mathf.Cos(pitch), Mathf.Sin(pitch), dir.y * Mathf.Cos(pitch)).normalized;
            var x = Vector3.Cross(Vector3.up, axis).normalized;
            var z = Vector3.Cross(x, axis);
            // basis * rotation(UP, roll)
            float cr = Mathf.Cos(roll), sr = Mathf.Sin(roll);
            var bx = x * cr - z * sr;
            var bz = x * sr + z * cr;
            var center = new Vector3(at.x, c + rho / Mathf.Cos(pitch), at.y);
            if (center.y > maxY) return null;
            if (forbidden != null && (forbidden(center, rho) || forbidden(new Vector3(at.x + dir.x * L, center.y, at.y + dir.y * L), rho) || forbidden(new Vector3(at.x - dir.x * L, center.y, at.y - dir.y * L), rho))) return null;
            var perp = new Vector2(-dir.y, dir.x);
            StampDisc(at, L + rho * 0.5f, q =>
            {
                float t = Vector2.Dot(q - at, dir);
                if (Mathf.Abs(t) > L || Mathf.Abs(Vector2.Dot(q - at, perp)) > rho) return float.NegativeInfinity;
                return c + m * t + 2 * rho;
            });
            return Shared.Matrix(bx * scale, axis * scale, bz * scale, center);
        }

        Matrix4x4? PlaceFlat(Vector3 half, Vector2 at, float yaw, float scale, float maxY, float maxSlope = MaxSlope)
        {
            float R = Mathf.Max(half.x, half.z), T = half.y;
            if (!IsFree(at, R)) return null;
            float rr = R * 0.85f;
            var offs = new Vector2[9];
            var hs = new float[9];
            for (int k = 0; k < 9; k++)
            {
                offs[k] = k == 0 ? Vector2.zero : new Vector2(Mathf.Cos(Mathf.PI * 2 * (k - 1) / 8), Mathf.Sin(Mathf.PI * 2 * (k - 1) / 8)) * rr;
                hs[k] = Sample(at + offs[k]);
            }
            float gx = Mathf.Clamp((hs[1] - hs[5]) / (2 * rr), -maxSlope, maxSlope);
            float gz = Mathf.Clamp((hs[3] - hs[7]) / (2 * rr), -maxSlope, maxSlope);
            float gm = Mathf.Sqrt(gx * gx + gz * gz);
            if (maxSlope < MaxSlope && gm > maxSlope) { gx *= maxSlope / gm; gz *= maxSlope / gm; }   // tools: the whole tilt, not each axis
            float c = float.NegativeInfinity;
            for (int k = 0; k < 9; k++) c = Mathf.Max(c, hs[k] - gx * offs[k].x - gz * offs[k].y);
            var normal = new Vector3(-gx, 1, -gz).normalized;
            var d = new Vector3(Mathf.Cos(yaw), 0, Mathf.Sin(yaw));
            var x = (d - normal * Vector3.Dot(d, normal)).normalized;
            var z = Vector3.Cross(x, normal);
            var center = new Vector3(at.x, c + T / normal.y, at.y);
            if (center.y > maxY) return null;
            if (forbidden != null && forbidden(center, R)) return null;
            StampDisc(at, R, q => { var o = q - at; return c + gx * o.x + gz * o.y + 2 * T; });
            return Shared.Matrix(x * scale, normal * scale, z * scale, center);
        }
    }

    /// <summary>Builds the initial ItemWorld from a validated database, the
    /// procedural layout and a seed. Instance ids
    /// follow catalog order (definition, then copy), independent of the seed, so
    /// saves always map back; the seed only decides where things end up.</summary>
    public class WorldGenerator
    {
        public readonly List<string> errors = new();
        public double lastGenerationMs;
        /// <summary>QA: the pile site each instance was placed in (-1: floor overflow, start area).</summary>
        public int[] pileOf;
        /// <summary>See PileSurface.forbidden (set by the game from the built storage units).</summary>
        public System.Func<Vector3, float, bool> forbidden;
        System.Random _rng;
        WorkshopLayout _layout;
        Vector2 _patch;   // this seed's offset into the patch noise

        static readonly Dictionary<string, float> MaxHeight = new() { ["spread"] = 0.3f, ["heap"] = 0.8f, ["feature"] = 0.9f, ["spill"] = 0.6f, ["cluster"] = 0.45f, ["scatter"] = 0.35f };

        /// <summary>limit &gt; 0 builds a deterministic subset (benchmark mode only).</summary>
        public ItemWorld Generate(ItemDatabase db, WorkshopLayout layout, int seed, int limit = -1)
        {
            var sw = System.Diagnostics.Stopwatch.StartNew();
            errors.Clear();
            if (!db.IsValid) { errors.AddRange(db.errors); errors.Add("World generation refused: the item database is invalid."); return null; }
            if (layout.placementErrors.Count > 0) { errors.AddRange(layout.placementErrors); return null; }
            var world = new ItemWorld(db) { seed = seed, layoutHash = ItemDatabase.Fnv(db.presetId + ":" + seed) };
            foreach (var defId in db.order)
                for (int c = 0; c < db.Get(defId).spawnCount; c++) world.Add(defId, Matrix4x4.identity);
            if (world.Count != db.WorldTotal)
            {
                errors.Add($"Expanded {world.Count} instances, expected {db.WorldTotal}");
                return null;
            }
            if (limit > 0 && limit < world.Count)
            {
                var full = world;
                world = new ItemWorld(db) { seed = seed, layoutHash = full.layoutHash };
                for (int k = 0; k < limit; k++) world.Add(full.instances[(int)((long)k * full.Count / limit)].defId, Matrix4x4.identity);
            }

            _rng = new System.Random((int)(ItemDatabase.Fnv("workshop-wreckage:" + seed) & 0x7fffffff));
            _layout = layout;
            _patch = new Vector2(Range(0, 1000), Range(0, 1000));
            var surfaces = new List<PileSurface>();
            foreach (var s in layout.surfaces)
            {
                var surf = new PileSurface(s.rect, s.y) { forbidden = forbidden };
                if (s.floor) { surf.blockers.AddRange(layout.floorBlockers); surf.blockers.AddRange(layout.keepClear); }
                foreach (var o in s.obstacles) surf.blockers.Add(o.rect);
                surfaces.Add(surf);
            }
            // the open spots near the start stay clear (no strays, no long part bridging over them) until
            // Retrobot's parts and the starter set go down, last of all
            int clear0 = surfaces[0].blockers.Count;
            foreach (var c in layout.retroSpots) surfaces[0].blockers.Add(new Rect(c - Vector2.one * 0.3f, Vector2.one * 0.6f));
            surfaces[0].blockers.Add(new Rect(layout.starterSpot - Vector2.one * (StarterRing + 0.35f), Vector2.one * (StarterRing + 0.35f) * 2));
            int clearN = surfaces[0].blockers.Count - clear0;

            // robot parts, Part Radar pieces and pegboard tools are part of the mix like everything else
            // (playtest 2026-10-29), except Retrobot's parts on the open spots by the start
            var robotIds = world.instances.Where(i => db.Get(i.defId).IsRobotPart).Select(i => i.id).ToList();
            var pegIds = world.instances.Where(i => db.Get(i.defId).IsPegboardTool).Select(i => i.id).ToList();
            var retro = robotIds.Where(id => world.DefOf(id).craftingPurpose == "robot:retrobot")
                .OrderBy(id => world.DefOf(id).shape == "Torso" ? 0 : 1).ThenBy(id => id).ToList();
            var retroPlaced = retro.Take(layout.retroSpots.Count).ToList();
            var groups = new Dictionary<int, List<int>>();
            foreach (var inst in world.instances)
            {
                if (retroPlaced.Contains(inst.id)) continue;
                if (!groups.TryGetValue(inst.defId, out var g)) groups[inst.defId] = g = new List<int>();
                g.Add(inst.id);
            }
            var caps = Capacities(layout, groups.Values.Sum(g => g.Count));
            var assign = Distribute(layout, caps, groups, db);
            // the starter assortment: one copy each of a few different large parts in plain
            // sight by the spawn (their other copies stay in the mix)
            var starter = StarterAssortment(db, world, assign);
            foreach (var a in assign) a.RemoveAll(starter.Contains);

            // heaps foundation-first: big parts go down before small ones settle between them
            for (int p = 0; p < layout.piles.Count; p++)
                if (layout.piles[p].kind == "heap")
                    assign[p] = assign[p].Select(id => (k: Radius(world.DefOf(id)) * Range(0.55f, 1.45f), id)).OrderByDescending(t => t.k).Select(t => t.id).ToList();

            var leftovers = new List<int>();
            pileOf = Enumerable.Repeat(-1, world.Count).ToArray();
            for (int p = 0; p < layout.piles.Count; p++)
            {
                var pile = layout.piles[p];
                bool first = true;
                foreach (var id in assign[p])
                {
                    bool centered = first && pile.kind == "feature";
                    first = false;
                    if (!Place(world, surfaces[pile.surface], layout.surfaces[pile.surface], pile, id, layout, centered)) leftovers.Add(id);
                    else pileOf[id] = p;
                }
            }
            var floorPile = new PileSite
            {
                kind = "scatter",
                rect = new Rect(layout.roomMin.x + 1, layout.roomMin.z + 1, layout.roomMax.x - layout.roomMin.x - 2, layout.roomMax.z - layout.roomMin.z - 2),
            };
            foreach (var id in leftovers)
            {
                bool ok = false;
                for (int attempt = 0; attempt < 40 && !ok; attempt++) ok = Place(world, surfaces[0], layout.surfaces[0], floorPile, id, layout);
                if (!ok) errors.Add($"Could not place instance {id}");
            }
            // last of all, so nothing lands on top: Retrobot's parts, one per open spot near
            // the start, and the starter set as a small tidy cluster
            surfaces[0].blockers.RemoveRange(clear0, clearN);
            var nearStart = new PileSite { kind = "cluster", center = layout.starterSpot, sigma = new Vector2(0.6f, 0.6f), maxHeight = 0.3f };
            for (int k = 0; k < starter.Count; k++)
            {
                // a ring, each part on its own patch of floor, nothing on top
                float a = k * Mathf.PI * 2 / starter.Count + 0.3f;
                var site = new PileSite { kind = "feature", center = layout.starterSpot + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * StarterRing, sigma = new Vector2(0.06f, 0.06f), maxHeight = 0.3f };
                if (!Place(world, surfaces[0], layout.surfaces[0], site, starter[k], layout, true) && !PlaceAnywhere(world, surfaces[0], layout, nearStart, starter[k])
                    && !PlaceAnywhere(world, surfaces[0], layout, floorPile, starter[k]))
                    errors.Add($"Could not place instance {starter[k]}");
            }
            starterIds = starter;
            // the spots nearest straight ahead of the spawn first: the chassis gets the most visible one
            var sp = new Vector2(layout.spawn.x, layout.spawn.z);
            var spots = layout.retroSpots.OrderBy(s => Mathf.Abs(Mathf.DeltaAngle(-90f, Mathf.Atan2(s.y - sp.y, s.x - sp.x) * Mathf.Rad2Deg))).ToList();
            for (int k = 0; k < retroPlaced.Count; k++)
            {
                var site = new PileSite { kind = "feature", center = spots[k], sigma = new Vector2(0.05f, 0.05f) };
                if (!Place(world, surfaces[0], layout.surfaces[0], site, retroPlaced[k], layout, true) && !PlaceAnywhere(world, surfaces[0], layout, floorPile, retroPlaced[k]))
                    errors.Add($"Could not place instance {retroPlaced[k]}");
            }
            foreach (var id in pegIds) world.instances[id].zone = -1;   // the board's tools earn no zone-restored XP
            // the tools lie on their widest face: their local Z (the thinnest axis) within 30 degrees of vertical
            int flat = pegIds.Count(id => Mathf.Abs(((Vector3)world.instances[id].transform.GetColumn(2)).normalized.y) > 0.86f);
            Debug.Log($"[worldgen] pegboard tools lying on their widest face: {flat}/{pegIds.Count}");
            lastGenerationMs = sw.Elapsed.TotalMilliseconds;
            return errors.Count == 0 ? world : null;
        }

        bool PlaceAnywhere(ItemWorld world, PileSurface floor, WorkshopLayout layout, PileSite site, int id)
        {
            for (int attempt = 0; attempt < 40; attempt++) if (Place(world, floor, layout.surfaces[0], site, id, layout)) return true;
            return false;
        }

        /// <summary>QA: centres (Unity space) of the n most crowded 1 m floor cells, at least 2 m apart: where the
        /// tests and screenshots go for "lots of parts" now that the floor has no heaps.</summary>
        public static List<Vector3> CrowdedFloorSpots(ItemWorld w, int n)
        {
            var cells = w.instances.Where(i => i.state == ItemState.InWorld && i.Position.y < 0.4f)
                .GroupBy(i => (Mathf.FloorToInt(i.Position.x), Mathf.FloorToInt(i.Position.z)))
                .OrderByDescending(c => c.Count()).Select(c => new Vector3(c.Key.Item1 + 0.5f, 0, c.Key.Item2 + 0.5f));
            var o = new List<Vector3>();
            foreach (var c in cells)
            {
                if (o.Count >= n) break;
                if (o.All(q => (q - c).magnitude >= 2f)) o.Add(c);
            }
            return o;
        }

        /// <summary>Instance ids of the starter assortment (one copy each of different parts).</summary>
        public List<int> starterIds = new();

        const int StarterCount = 6;
        const float StarterRing = 0.65f;

        /// <summary>One copy each of up to six different large, recognisable parts (gears,
        /// bearings, electronics, machine parts... a different category where possible), never a
        /// definition's only or last copies: the rest stay mixed into the piles. Seeded.</summary>
        List<int> StarterAssortment(ItemDatabase db, ItemWorld world, List<List<int>> assign)
        {
            var big = db.order.Select(db.Get)
                .Where(d => !d.IsRobotPart && !d.IsPegboardTool && world.TotalOf(d.id) >= 3 && d.category >= 3 && Radius(d) >= 0.08f && Radius(d) <= 0.24f)
                .ToList();
            Shuffle(big);
            var o = new List<int>();
            var cats = new HashSet<int>();
            foreach (var pass in new[] { true, false })
                foreach (var d in big)
                {
                    if (o.Count >= StarterCount) break;
                    if (pass && !cats.Add(d.category)) continue;
                    if (o.Any(id => world.instances[id].defId == d.id)) continue;
                    o.Add(world.IdsOf(d.id)[0]);
                }
            return o;
        }

        /// <summary>Spreads every definition's copies over 2-3 neighbouring piles: a primary pile
        /// picked by its remaining room times the square root of the pile's category bias (heaps
        /// still lean to machine parts, clusters to fasteners, but nothing becomes one-kind), then
        /// the nearest piles with room. Each pile is shuffled, so no runs of one part. Seeded.</summary>
        List<List<int>> Distribute(WorkshopLayout layout, int[] caps, Dictionary<int, List<int>> groups, ItemDatabase db)
        {
            int n = layout.piles.Count;
            var o = Enumerable.Range(0, n).Select(_ => new List<int>()).ToList();
            var room = (int[])caps.Clone();
            var centers = layout.piles.Select(p => p.kind == "scatter" ? p.rect.center : p.center).ToArray();
            var keys = groups.Keys.ToList();
            Shuffle(keys);
            keys = keys.OrderByDescending(k => groups[k].Count).ToList();   // big sets first, while there is room near their pile
            var w = new float[n];
            foreach (var key in keys)
            {
                var ids = groups[key];
                int cat = db.Get(key).category;
                float total = 0;
                for (int p = 0; p < n; p++) { w[p] = room[p] * Mathf.Sqrt(layout.piles[p].bias.GetValueOrDefault(cat, 1f)); total += w[p]; }
                if (total <= 0) for (int p = 0; p < n; p++) total += w[p] = room[p];
                float x = (float)_rng.NextDouble() * total;
                int primary = 0;
                while (primary < n - 1 && (x >= w[primary] || room[primary] == 0)) { x -= w[primary]; primary++; }
                int spread = Mathf.Min(ids.Count, Mathf.Clamp(ids.Count / 4 + 1, 2, 3));
                var near = Enumerable.Range(0, n).Where(p => p != primary && room[p] > 0)
                    .OrderBy(p => (centers[p] - centers[primary]).sqrMagnitude).Take(3).ToList();
                Shuffle(near);
                var chosen = new List<int> { primary };
                chosen.AddRange(near.Take(spread - 1));
                for (int c = 0, k = 0; c < ids.Count; k++)
                {
                    int p = chosen[k % chosen.Count];
                    if (room[p] <= 0)
                    {
                        // full: the nearest pile that still has room
                        p = Enumerable.Range(0, n).Where(q => room[q] > 0).OrderBy(q => (centers[q] - centers[primary]).sqrMagnitude).DefaultIfEmpty(-1).First();
                        if (p < 0) p = primary;
                        chosen[k % chosen.Count] = p;
                    }
                    o[p].Add(ids[c++]);
                    room[p]--;
                }
            }
            foreach (var l in o) Shuffle(l);
            return o;
        }

        static float Radius(ItemDefinition d)
        {
            var s = MeshLibrary.UnitBounds(d.geometryKey).size * d.worldScale;
            return Mathf.Max(s.x, s.y, s.z) * 0.5f;
        }

        float Range(float a, float b) => a + (float)_rng.NextDouble() * (b - a);

        /// <summary>Anywhere in a..b, densest in the middle: 40 % evenly, the rest the mean of two draws
        /// (triangular), so the middle gets about four times the parts of the edges.</summary>
        const float PatchSize = 2.4f;   // metres per patch-noise unit

        float Centred(float a, float b) => _rng.NextDouble() < 0.4 ? Range(a, b) : (Range(a, b) + Range(a, b)) * 0.5f;

        float Gauss(float sigma)
        {
            double u1 = 1.0 - _rng.NextDouble(), u2 = _rng.NextDouble();
            return (float)(System.Math.Sqrt(-2.0 * System.Math.Log(u1)) * System.Math.Cos(2 * System.Math.PI * u2)) * sigma;
        }

        void Shuffle<T>(List<T> a)
        {
            for (int i = a.Count - 1; i > 0; i--) { int j = _rng.Next(i + 1); (a[i], a[j]) = (a[j], a[i]); }
        }

        static int[] Capacities(WorkshopLayout layout, int total)
        {
            float sum = layout.piles.Sum(p => p.share);
            var caps = layout.piles.Select(p => (int)(p.share / sum * total)).ToArray();
            int assigned = caps.Sum(), i = 0;
            while (assigned < total) { caps[i++ % caps.Length]++; assigned++; }
            return caps;
        }

        Vector2 SamplePosition(PileSite pile, float spread)
        {
            switch (pile.kind)
            {
                case "scatter":
                    return new Vector2(Range(pile.rect.xMin, pile.rect.xMax), Range(pile.rect.yMin, pile.rect.yMax));
                case "spread":
                    // patchy: dense and thin areas a few metres across; walkways mostly clear
                    var at = Vector2.zero;
                    for (int k = 0; k < 40; k++)
                    {
                        at = new Vector2(Centred(pile.rect.xMin, pile.rect.xMax), Centred(pile.rect.yMin, pile.rect.yMax));
                        float n = Mathf.Clamp01((Mathf.PerlinNoise(_patch.x + at.x / PatchSize, _patch.y + at.y / PatchSize) - 0.3f) / 0.4f);
                        float keep = (0.1f + 0.9f * n * n) * (_layout.OnWalkway(at) ? 0.06f : 1f);
                        if (_rng.NextDouble() < keep) break;
                    }
                    return at;
                case "spill":
                    float ang = pile.dir + Gauss(pile.spread * 0.5f * spread);
                    float dist = Mathf.Abs(Gauss(pile.length * 0.45f * spread)) + 0.05f;
                    return pile.center + new Vector2(Mathf.Cos(ang), Mathf.Sin(ang)) * dist;
                default:
                    return pile.center + new Vector2(Gauss(pile.sigma.x * spread), Gauss(pile.sigma.y * spread));
            }
        }

        bool Place(ItemWorld world, PileSurface surf, PileSurfaceDef sdef, PileSite pile, int id, WorkshopLayout layout, bool centered = false)
        {
            var inst = world.instances[id];
            var def = world.DefOf(id);
            var b = MeshLibrary.UnitBounds(def.geometryKey).size;
            var half = new Vector3(b.x, b.y, b.z) * 0.5f * def.worldScale;
            float budget = pile.maxHeight > 0 ? pile.maxHeight : MaxHeight.GetValueOrDefault(pile.kind, 0.8f);
            float maxY = surf.baseY + Mathf.Min(budget, sdef.maxHeight);
            for (int attempt = 0; attempt < 14; attempt++)
            {
                float spread = 1 + attempt * 0.18f;
                var at = centered && attempt == 0 ? pile.center : SamplePosition(pile, spread);
                float capY = maxY, reach = Mathf.Max(half.x, half.y, half.z);
                foreach (var (r, y) in sdef.ceilings)
                    if (at.x > r.xMin - reach && at.x < r.xMax + reach && at.y > r.yMin - reach && at.y < r.yMax + reach) capY = Mathf.Min(capY, y - reach);
                if (capY <= surf.baseY) continue;
                var m = surf.Place(def, half, at, Range(0, Mathf.PI * 2), Range(0, Mathf.PI * 2), def.worldScale, capY);
                if (m == null) continue;
                inst.transform = m.Value;
                inst.zone = layout.ZoneAt(inst.Position);
                return true;
            }
            return false;
        }
    }
}
