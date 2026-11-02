using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Art = WorkshopWreckage.WorkshopArtDirectionProfile;

namespace WorkshopWreckage
{
    /// <summary>Art pass, stage B (docs/ART_DIRECTION_PASS.md): static set dressing that only adds to the look.
    /// Ceiling ducts and pipe runs, large blueprint sheets and enamel signs high on the walls (above every
    /// storage name board), and worn yellow lines on the floor in front of shelving and cabinets.
    /// Nothing here has a collider or casts a shadow, nothing is lit by a new light, and everything is
    /// merged per material. Each group has a switch in art_direction.json ("dressing"); the whole set
    /// is off below half stylization. QA: --nodressing.</summary>
    public static class WorkshopDressing
    {
        const float BandBottom = 3.15f, BandTop = 4.4f;   // wall band above the storage name boards, below the ceiling pipes
        const int Far = 128;   // paint texture size for ceiling metal, seen from 3 m and more (each colour bakes its own; 512 cost ~80 ms each at boot)

        public static Transform Build(Game g)
        {
            if (!Art.B("dressing", "enabled")) return null;
            var root = new GameObject("WorkshopDressing").transform;
            root.SetParent(g.transform, false);
            var L = g.layout;
            if (Art.B("dressing", "ceiling")) Ceiling(L, root);
            if (Art.B("dressing", "wall_sheets")) WallSheets(g, root);
            if (Art.B("dressing", "signs")) Signs(L, root);
            if (Art.B("dressing", "floor_lines")) FloorLines(L, root);
            return root;
        }

        // ---------------------------------------------------------------- ceiling

        /// <summary>A galvanised duct and a bundle of painted pipes running north–south under the beams,
        /// between the fixture columns (clear of every fixture and pendant cord), and two pipes along
        /// the top of the north wall.</summary>
        static void Ceiling(WorkshopLayout L, Transform root)
        {
            float west = L.roomMin.x, east = L.roomMax.x, north = L.roomMin.z, south = L.roomMax.z;
            float W = east - west, D = south - north, top = L.roomMax.y;
            float beamUnder = top - 0.34f;
            var beams = Enumerable.Range(0, 7).Select(k => north + 0.6f + k * (D - 1.2f) / 6f).ToList();
            var cols = L.fixtures.Select(f => Mathf.Round(f.x * 100) / 100).Distinct().OrderBy(x => x).ToList();
            var mids = new List<float>();
            for (int k = 0; k + 1 < cols.Count; k++) mids.Add((cols[k] + cols[k + 1]) / 2);
            if (mids.Count == 0) mids.Add(0);
            bool Clear(float x) => L.lamps.All(l => Mathf.Abs(l.x - x) > 0.8f);
            float Pick(float target) => mids.Where(Clear).DefaultIfEmpty(mids[0]).OrderBy(x => Mathf.Abs(x - target)).First();
            float ductX = Pick(west + W * 0.2f), pipeX = Pick(east - W * 0.2f);
            if (Mathf.Abs(pipeX - ductX) < 1f) pipeX = mids.Where(x => Mathf.Abs(x - ductX) > 1f).DefaultIfEmpty(ductX + 2f).Last();

            var b = new Batch();
            var galv = EnvMaterials.Metal(new Color(0.64f, 0.65f, 0.66f), 0.1f, Far);   // painted look: a metallic duct goes black without a reflection probe
            var strap = EnvMaterials.Metal(new Color(0.34f, 0.34f, 0.35f), 0.2f, Far);
            // the duct: from the south wall to a riser into the ceiling short of the north wall pipes,
            // with a flange every 1.5 m and a strap to every beam it passes
            float dy = beamUnder - 0.2f - 0.17f, n0 = north + 0.45f;
            b.Box(new Vector3(ductX, dy, (n0 + south) / 2), new Vector3(0.56f, 0.34f, south - n0), galv);
            b.Box(new Vector3(ductX, (dy + top) / 2, n0 + 0.17f), new Vector3(0.56f, top - dy, 0.34f), galv);
            for (float z = n0 + 0.75f; z < south - 0.3f; z += 1.5f)
                b.Box(new Vector3(ductX, dy, z), new Vector3(0.6f, 0.38f, 0.035f), galv);
            foreach (var z in beams)
                foreach (var sx in new[] { -0.3f, 0.3f })
                    b.Box(new Vector3(ductX + sx, (dy + beamUnder) / 2 + 0.05f, z), new Vector3(0.025f, beamUnder - dy - 0.1f, 0.04f), strap);

            // the pipe bundle on trapeze hangers: fire main (red), water (green), compressed air (grey)
            var pipes = new (float dx, float r, Color c)[]
            {
                (-0.19f, 0.055f, new Color(0.5f, 0.13f, 0.09f)),
                (0.0f, 0.045f, new Color(0.2f, 0.33f, 0.22f)),
                (0.16f, 0.035f, new Color(0.46f, 0.46f, 0.45f)),
            };
            float py = beamUnder - 0.24f;
            var alongZ = Quaternion.Euler(90, 0, 0);
            foreach (var p in pipes)
            {
                var m = EnvMaterials.Metal(p.c, 0.2f, Far);
                b.Cylinder(new Vector3(pipeX + p.dx, py, (n0 + south) / 2), p.r, south - n0, m, alongZ, 12);
                b.Cylinder(new Vector3(pipeX + p.dx, (py + top) / 2, n0), p.r, top - py, m, null, 12);   // up through the ceiling
                b.Cylinder(new Vector3(pipeX + p.dx, py, n0), p.r * 1.25f, p.r * 2.5f, m, alongZ, 12);    // the bend's collar
            }
            foreach (var z in beams)
            {
                b.Box(new Vector3(pipeX - 0.02f, py - 0.075f, z), new Vector3(0.52f, 0.03f, 0.04f), strap);
                foreach (var sx in new[] { -0.27f, 0.23f })
                    b.Box(new Vector3(pipeX + sx, (py - 0.075f + beamUnder) / 2, z), new Vector3(0.016f, beamUnder - py + 0.075f, 0.016f), strap);
            }

            // two pipes along the top of the north wall, on brackets
            var alongX = Quaternion.Euler(0, 0, 90);
            b.Cylinder(new Vector3((west + east) / 2, top - 0.6f, north + 0.12f), 0.045f, W, EnvMaterials.Metal(new Color(0.2f, 0.33f, 0.22f), 0.2f, Far), alongX, 12);
            b.Cylinder(new Vector3((west + east) / 2, top - 0.46f, north + 0.1f), 0.032f, W, EnvMaterials.Metal(new Color(0.46f, 0.46f, 0.45f), 0.2f, Far), alongX, 12);
            for (float x = west + 0.8f; x < east - 0.3f; x += 1.6f)
                b.Box(new Vector3(x, top - 0.53f, north + 0.06f), new Vector3(0.04f, 0.24f, 0.12f), strap);
            if (Art.B("dressing", "ceiling_extras")) CeilingExtras(L, b, beams, ductX, pipeX, dy - 0.17f, galv, strap);
            b.Build(root, "Ceiling", shadows: false);
        }

        /// <summary>A cable tray across the room between two beams (as far from the fixture rows and lamps as
        /// the beams allow), a few cables drooping between hangers, and two square diffusers under the duct.</summary>
        static void CeilingExtras(WorkshopLayout L, Batch b, List<float> beams, float ductX, float pipeX, float ductBottom, Material galv, Material strap)
        {
            float west = L.roomMin.x, east = L.roomMax.x, top = L.roomMax.y;
            var rows = L.fixtures.Select(f => Mathf.Round(f.z * 100) / 100).Distinct().OrderBy(z => z).ToList();
            float Clearance(float z) => rows.Select(r => Mathf.Abs(r - z)).Concat(L.lamps.Select(l => Mathf.Abs(l.z - z))).DefaultIfEmpty(9f).Min();
            var gaps = new List<float>();
            for (int k = 0; k + 1 < beams.Count; k++) gaps.Add((beams[k] + beams[k + 1]) / 2);
            float trayZ = gaps.OrderByDescending(Clearance).First();
            if (Clearance(trayZ) < 0.9f) return;   // no quiet strip in this room
            bool Free(float x) => Mathf.Abs(x - ductX) > 0.45f && Mathf.Abs(x - pipeX) > 0.4f;

            // ladder tray: two rails, rungs every 30 cm, rods to the ceiling slab; four cables in it
            float ty = 4.5f, x0 = west + 0.5f, x1 = east - 0.5f;
            var alongX = Quaternion.Euler(0, 0, 90);
            foreach (var dz in new[] { -0.15f, 0.15f })
                b.Box(new Vector3((x0 + x1) / 2, ty, trayZ + dz), new Vector3(x1 - x0, 0.07f, 0.012f), galv);
            for (float x = x0 + 0.1f; x < x1; x += 0.3f)
                b.Box(new Vector3(x, ty - 0.029f, trayZ), new Vector3(0.025f, 0.012f, 0.3f), galv);
            var rods = new List<float>();
            for (float x = x0 + 0.4f; x < x1; x += 1.8f) if (Free(x)) rods.Add(x);
            foreach (var x in rods)
                foreach (var dz in new[] { -0.17f, 0.17f })
                    b.Box(new Vector3(x, (ty + top) / 2, trayZ + dz), new Vector3(0.012f, top - ty, 0.012f), strap);
            var rubber = EnvMaterials.Plain(new Color(0.045f, 0.045f, 0.05f), 0, 0.3f);
            var cables = new (float dz, float r, Material m)[]
            {
                (-0.08f, 0.013f, rubber), (-0.04f, 0.011f, EnvMaterials.Plain(new Color(0.12f, 0.12f, 0.13f), 0, 0.3f)),
                (0.0f, 0.012f, rubber), (0.05f, 0.014f, EnvMaterials.Plain(new Color(0.42f, 0.2f, 0.06f), 0, 0.3f)),
            };
            foreach (var c in cables)
                b.Cylinder(new Vector3((x0 + x1) / 2, ty - 0.023f + c.r, trayZ + c.dz), c.r, x1 - x0, c.m, alongX, 8);

            // the odd cable that left the tray: drooping between rods, and one strung between two beams by the pipes
            for (int k = 1; k + 1 < rods.Count; k += 3)
                Droop(b, new Vector3(rods[k], ty - 0.02f, trayZ + 0.19f), new Vector3(rods[k + 1], ty - 0.02f, trayZ + 0.19f), 0.22f, 0.011f, rubber);
            int bk = beams.Count / 2;
            float cx = pipeX + 0.5f, beamUnder = top - 0.34f;
            if (Free(cx) && bk + 1 < beams.Count)
                Droop(b, new Vector3(cx, beamUnder, beams[bk]), new Vector3(cx, beamUnder, beams[bk + 1]), 0.3f, 0.012f, rubber);

            // square ceiling diffusers under the duct, between fixture rows and off the tray
            var dark = EnvMaterials.Plain(new Color(0.08f, 0.08f, 0.085f), 0, 0.2f);
            for (int k = 0; k + 1 < rows.Count; k++)
            {
                float z = (rows[k] + rows[k + 1]) / 2;
                if (Mathf.Abs(z - trayZ) < 0.8f) continue;
                b.Box(new Vector3(ductX, ductBottom - 0.06f, z), new Vector3(0.3f, 0.12f, 0.3f), galv);
                b.Box(new Vector3(ductX, ductBottom - 0.135f, z), new Vector3(0.5f, 0.03f, 0.5f), galv);
                for (int s = -2; s <= 2; s++)
                    b.Box(new Vector3(ductX, ductBottom - 0.152f, z + s * 0.08f), new Vector3(0.42f, 0.004f, 0.022f), dark);
            }
        }

        /// <summary>A cable sagging between a and c (Godot space): a parabola of cylinder segments.</summary>
        static void Droop(Batch b, Vector3 a, Vector3 c, float sag, float r, Material m, int segs = 10)
        {
            Vector3 P(float t) => Vector3.Lerp(a, c, t) + Vector3.down * sag * 4 * t * (1 - t);
            for (int k = 0; k < segs; k++)
            {
                var p0 = P(k / (float)segs); var p1 = P((k + 1) / (float)segs);
                var d = Batch.G(p1) - Batch.G(p0);
                b.Cylinder((p0 + p1) / 2, r, d.magnitude + r, m, Quaternion.FromToRotation(Vector3.up, d.normalized), 6);
            }
        }

        // ---------------------------------------------------------------- walls

        /// <summary>A point on a wall: position along it (Godot XZ) and the inward normal.</summary>
        static (Vector2 c, Vector2 n) OnWall(WorkshopLayout L, string wall, float t) => wall switch
        {
            "north" => (new Vector2(t, L.roomMin.z), Vector2.up),
            "east" => (new Vector2(L.roomMax.x, t), Vector2.left),
            _ => (new Vector2(t, L.roomMax.z), Vector2.down),
        };

        /// <summary>May something w wide hang high on `wall` at t? Never across the big sign, the clock,
        /// the door, the tray's floodlight, the Robot Workbench stencil, a window or an enamel sign, nor in a corner.</summary>
        static bool HighClear(WorkshopLayout L, string wall, float t, float w)
        {
            float half = w / 2 + 0.25f;
            float lo = wall == "east" ? L.roomMin.z : L.roomMin.x, hi = wall == "east" ? L.roomMax.z : L.roomMax.x;
            if (t - half < lo + 0.8f || t + half > hi - 0.8f) return false;
            if (EnamelSigns.Blocks(L, wall, t, w, BandBottom, BandTop)) return false;
            if (wall == "south")
            {
                if (Mathf.Abs(t - L.repairBayPos.x) < RobotWorkbench.Bench.x / 2 + 0.3f + half) return false;
                if (t > L.trayRect.xMin - half && t < L.trayRect.xMax + half) return false;
            }
            foreach (var f in L.furniture)
            {
                string type = Json.Str(f, "type");
                if (type != "sign" && type != "clock" && type != "door") continue;
                var p = WorkshopLayout.V(f["pos"]);
                var s = WorkshopLayout.V(f["size"]);
                bool onWall = wall == "north" ? p.z < L.roomMin.z + 0.5f : wall == "south" ? p.z > L.roomMax.z - 0.5f : p.x > L.roomMax.x - 0.5f;
                if (onWall && Mathf.Abs((wall == "east" ? p.z : p.x) - t) < s.x / 2 + half) return false;
            }
            return true;
        }

        static void Hang(Transform parent, WorkshopLayout L, string wall, float t, float y, float w, float h, Texture tex, float curl, string name, float off = 0.008f)
        {
            var (c, n) = OnWall(L, wall, t);
            var pos = new Vector3(c.x + n.x * off, y, c.y + n.y * off);
            BlueprintPosters.Sheet(parent, tex, Shared.Pos(pos.x, pos.y, pos.z), Shared.YRot(Mathf.Atan2(n.x, n.y)), w, h, curl, name);
        }

        /// <summary>Large general-arrangement sheets (1.6 m) in the high wall band: two flanking the
        /// sign on the north wall, four on the east wall, one on the south wall.</summary>
        static void WallSheets(Game g, Transform root)
        {
            var L = g.layout;
            var parent = new GameObject("LargeSheets").transform;
            parent.SetParent(root, false);
            float W = L.roomMax.x - L.roomMin.x, D = L.roomMax.z - L.roomMin.z;
            var sheets = new (string wall, float t, string type, string code, bool blue)[]
            {
                ("north", -4.2f, "caterpillarbot", "CB-04", true),
                ("north", 6.2f, "spiderbot", "SB-03", false),
                ("east", L.roomMin.z + D * 0.3f, "hoverbot", "HB-05", false),
                ("east", L.roomMax.z - D * 0.3f, "unibot", "UB-02", true),
                ("south", L.roomMax.x - W * 0.12f, "sorting_arm", "SA-06", true),   // its old spot holds an enamel sign
                // moved here from the west wall and the machine, where the enamel signs hang (2026-11-02)
                ("east", L.roomMin.z + 1.9f, "retrobot", "RB-01", true),
                ("east", L.roomMax.z - 1.9f, "caterpillarbot", "CB-04", false),
            };
            const float w = 1.6f, h = 1.2f;
            int k = 0;
            foreach (var s in sheets)
            {
                if (!HighClear(L, s.wall, s.t, w)) { Debug.Log($"[dressing] no room for the {s.type} sheet on the {s.wall} wall"); continue; }
                var tex = BlueprintPosters.RobotDrawing(g, s.type, s.code, s.blue);
                if (tex == null) continue;
                Hang(parent, L, s.wall, s.t, (BandBottom + BandTop) / 2 + (k % 2 == 0 ? 0.03f : -0.04f), w, h, tex, 0.02f, $"LargeSheet_{s.wall}_{s.type}");
                k++;
            }
        }

        /// <summary>Three enamel signs on dark plates, in the same band.</summary>
        static void Signs(WorkshopLayout L, Transform root)
        {
            var parent = new GameObject("Signs").transform;
            parent.SetParent(root, false);
            var signs = new (string wall, float t, string text, Color bg, Color fg)[]
            {
                ("east", (L.roomMin.z + L.roomMax.z) / 2, "EYE PROTECTION\nMUST BE WORN", new Color(0.1f, 0.24f, 0.5f), new Color(0.95f, 0.94f, 0.9f)),
                ("south", 1.8f, "KEEP AISLES\nCLEAR", new Color(0.88f, 0.66f, 0.1f), new Color(0.08f, 0.07f, 0.06f)),
                ("north", -7.0f, "SORT · STORE\nREPAIR", new Color(0.16f, 0.36f, 0.24f), new Color(0.95f, 0.94f, 0.9f)),
            };
            const float w = 0.9f, h = 0.4f;
            var b = new Batch();
            var plate = EnvMaterials.Plain(new Color(0.1f, 0.1f, 0.1f), 0.3f, 0.3f);
            foreach (var s in signs)
            {
                if (!HighClear(L, s.wall, s.t, w)) continue;
                var tex = TextAtlas.RenderSingle(s.text, 1024, 456, s.bg, s.fg, 120, true);
                float y = 3.75f;
                Hang(parent, L, s.wall, s.t, y, w, h, tex, 0, "Sign_" + s.text.Split('\n')[0], 0.017f);   // just in front of its plate
                var (c, n) = OnWall(L, s.wall, s.t);
                var size = Mathf.Abs(n.x) > 0.5f ? new Vector3(0.012f, h + 0.04f, w + 0.04f) : new Vector3(w + 0.04f, h + 0.04f, 0.012f);
                b.Box(new Vector3(c.x + n.x * 0.006f, y, c.y + n.y * 0.006f), size, plate);
            }
            b.Build(parent, "SignPlates", shadows: false);
        }

        // ---------------------------------------------------------------- floor

        /// <summary>A worn, dashed yellow line 12 cm in front of every shelving and cabinet front.</summary>
        static void FloorLines(WorkshopLayout L, Transform root)
        {
            var b = new Batch();
            var paint = EnvMaterials.Metal(new Color(0.86f, 0.64f, 0.12f), 0.75f, 256);   // 2 mm texels: plenty for a floor line
            const float dash = 0.45f, gap = 0.2f, width = 0.06f, thick = 0.004f;
            foreach (var u in L.units.Values)
            {
                string type = Json.Str(u, "type");
                if (type != "shelving" && type != "drawer_cabinet") continue;
                var p = WorkshopLayout.V(u["pos"]);
                float rot = (float)Json.Num(u, "rot_y"), w = (float)Json.Num(u, "width"), d = (float)Json.Num(u, "depth");
                var n = new Vector2(Mathf.Sin(rot), Mathf.Cos(rot));
                var along = new Vector2(n.y, -n.x);
                var front = new Vector2(p.x, p.z) + n * (d / 2 + 0.12f);
                bool alongX = Mathf.Abs(n.y) > 0.5f;
                for (float s = -w / 2 + 0.05f; s < w / 2 - 0.1f; s += dash + gap)
                {
                    float len = Mathf.Min(dash, w / 2 - 0.05f - s);
                    var c = front + along * (s + len / 2);
                    b.Box(new Vector3(c.x, thick / 2, c.y), alongX ? new Vector3(len, thick, width) : new Vector3(width, thick, len), paint);
                }
            }
            b.Build(root, "FloorLines", shadows: false);
        }
    }
}
