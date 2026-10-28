using System.Collections.Generic;
using System.Globalization;
using UnityEngine;
using UnityEngine.Rendering;

namespace WorkshopWreckage.Sandbox
{
    /// <summary>Variant B geometry (sandbox only): the round part families rebuilt in C# with the shape language
    /// of the robots. The game's shared meshes are untouched; MeshLibrary asks this builder only for LOD
    /// <see cref="Lod"/> / <see cref="LodHero"/>, which only the sandbox's geometry quality requests.
    ///
    /// Same unit space, outer envelope and centring as the shared world meshes (so physics, positions and
    /// bounds of the pile and gallery stay as they are), but:
    /// - 32-72 segments around instead of 8-14 (selective: tiny parts keep the world mesh, big ones get LodHero);
    /// - crease-angle normals: curved walls, arcs and round-overs shade smooth, flat faces, chamfers and the
    ///   corners of hexes / squares stay crisp (and SkinUV keeps flat caps as their own surfaces);
    /// - real bevels: rounded or chamfered rims on every prism, lathe and box, 30-degree crowns on nuts and
    ///   bolt heads, chamfered bores;
    /// - gears: curved flanks, filleted roots and rounded tips (10-24 outline points per tooth instead of 4),
    ///   round chamfered bores, cast rims with a recessed web (20-31 teeth) or five spokes (32+) and a hub.
    /// Built in Godot space (the coordinates the shared meshes were authored in), mirrored on Z like
    /// MeshLibrary.Load, cached per key and LOD by MeshLibrary.
    ///
    /// Versions (the LOD number carries it, so both stay cached side by side and BP1 never changes):
    /// - version 1 = BP1 (iteration 2, tag visual-bp1): LOD 2 standard, 3 hero; every spur / ratchet gear of 32+
    ///   teeth spoked, 20-31 webbed;
    /// - version 2 = iteration 3: LOD 4 compact (small parts: about 60 % of the segments), 5 standard, 6 hero;
    ///   gears keep their solid identity (webbed from 20 teeth) and only the designs in <see cref="SpokedV2"/> are
    ///   spoked (about 1 gear in 6 in the catalogue); LOD 7-9 = the same with every gear of 24+ teeth spoked
    ///   (QA: what spokes cost).</summary>
    public static class ShapeMeshes
    {
        public const int Lod = 2, LodHero = 3;
        public const int TierCompact = 0, TierStandard = 1, TierHero = 2;

        /// <summary>The MeshLibrary LOD for a geometry version (1 = BP1, 2 = iteration 3), tier and the QA all-spoked
        /// gear rule (version 2 only). Version 1 has no compact tier (it draws those parts standard, as BP1 did).</summary>
        public static int LodOf(int version, int tier, bool allSpoked = false) =>
            version <= 1 ? (tier == TierHero ? LodHero : Lod) : 4 + tier + (allSpoked ? 3 : 0);

        /// <summary>Iteration 3: the gear designs that are cast with spokes (a sprocket's lightening windows); every
        /// other gear stays a solid disc as in the game. 8 of the 50 gear definitions in the standard catalogue.</summary>
        public static readonly HashSet<string> SpokedV2 = new() { "gear:spur:t32", "gear:sprocket:t32", "gear:sprocket:t40" };

        static readonly HashSet<string> Families = new() { "screw", "nut", "washer", "gear", "spring", "bearing", "shaft", "elec", "spec", "mach" };
        static readonly float[] BearingWidths = { 0.28f, 0.35f, 0.45f };

        /// <summary>Keys this builder improves: the procedural part families and the robot-themed salvage parts
        /// (those get more segments only). Never robot parts or pegboard tools.</summary>
        public static bool Has(string key)
        {
            if (key.StartsWith("robot:") || Pegboard.ToolFactory.Has(key)) return false;
            if (SalvageMeshes.Has(key)) return true;
            int c = key.IndexOf(':');
            return c > 0 && Families.Contains(key.Substring(0, c));
        }

        public static MeshLibrary.Entry Build(string key, int lod)
        {
            int version = lod >= 4 ? 2 : 1, tier = version == 1 ? (lod >= LodHero ? TierHero : TierStandard) : (lod - 4) % 3;
            bool allSpoked = lod >= 7;
            // salvage parts: more segments only (24 compact, 32 standard, 48 hero, as BP1's 2 / 3)
            if (SalvageMeshes.Has(key)) return SalvageMeshes.Build(key, tier == TierCompact ? MeshLibrary.LodInspect : tier + 1);
            var g = new G(tier);
            var parts = key.Split(':');
            string sub = parts.Length > 1 ? parts[1] : "";
            switch (parts[0])
            {
                case "screw": Screw(g, sub, Param(parts, "a", 3)); break;
                case "nut": Nut(g, sub); break;
                case "washer": Washer(g, sub); break;
                case "gear": { int t = (int)Param(parts, "t", 16); Gear(g, sub, t, Spoked(key, sub, t, version, allSpoked)); break; }
                case "spring": Spring(g, sub, Param(parts, "c", 8), Param(parts, "l", 2)); break;
                case "bearing": Bearing(g, sub, BearingWidths[Mathf.Clamp((int)Param(parts, "w", 1), 0, 2)]); break;
                case "shaft": Shaft(g, sub, Param(parts, "a", 5)); break;
                case "elec": Electronic(g, sub); break;
                case "spec": Specialty(g, sub); break;
                case "mach": Machine(g, sub); break;
                default: g.Box(Vector3.zero, Vector3.one); break;
            }
            return g.Finish(key + "_shape" + lod);
        }

        static float Param(string[] parts, string prefix, float fallback)
        {
            foreach (var p in parts)
                if (p.Length > prefix.Length && p.StartsWith(prefix) && float.TryParse(p.Substring(prefix.Length), NumberStyles.Float, CultureInfo.InvariantCulture, out var v))
                    return v;
            return fallback;
        }

        static Vector4 C(float x, float y, float r = 0, float steps = 0) => new(x, y, r, steps);
        static readonly Color White = new(1, 1, 1, 0), Dark = new(0.12f, 0.12f, 0.13f, 0), Bolt = new(0.55f, 0.55f, 0.56f, 0), Silver = new(0.7f, 0.7f, 0.72f, 0);
        const float Tau = Mathf.PI * 2;

        // ================================================================== screws (shank diameter 1, along +Y, head on top)

        static void Screw(G g, string sub, float a)
        {
            int segs = g.S(20, 32), hs = g.S(36, 56);
            const float pitch = 0.17f;
            float threadEnd = sub != "hex_bolt" || a <= 4 ? a : a * 0.6f;
            switch (sub)
            {
                case "carriage":
                    g.Shank(0.5f, 0, a - 0.45f, pitch, segs);
                    g.BBox(new Vector3(0, a - 0.22f, 0), new Vector3(0.98f, 0.45f, 0.98f), 0.05f);
                    g.Lathe(g.Dome(1.1f, a, 0.48f, 0.04f), hs);
                    return;
                case "countersunk":
                    g.Shank(0.5f, 0, a - 0.5f, pitch, segs);
                    g.Lathe(g.RP(false, C(0, a - 0.55f), C(0.5f, a - 0.55f), C(1f, a, 0.03f, 1), C(1f, a + 0.05f, 0.03f), C(0, a + 0.05f)), hs);
                    SlotCross(g, a + 0.05f, 1.7f, true);
                    return;
            }
            if (threadEnd < a)
            {
                g.Shank(0.5f, 0, threadEnd, pitch, segs);
                g.Lathe(new List<Vector2> { new(0.5f, threadEnd), new(0.5f, a) }, segs);
            }
            else g.Shank(0.5f, 0, a, pitch, segs);
            switch (sub)
            {
                case "hex_bolt":
                    g.Crown(6, 0.87f, 0.87f * 0.866f * 0.97f, a, a + 0.65f, true, false, 0, 0, g.S(48, 72));
                    g.Lathe(g.RP(false, C(0.5f, a - 0.04f), C(0.8f, a - 0.04f, 0.02f, 1), C(0.8f, a)), hs);   // washer face
                    break;
                case "socket_cap":
                    g.Lathe(g.RP(false, C(0, a), C(0.75f, a, 0.03f, 1), C(0.75f, a + 1f, 0.14f), C(0, a + 1f)), hs);
                    g.color = new Color(0.08f, 0.08f, 0.09f, 0); g.slot = 2;
                    g.Prism(PolyRing(6, 0.3f, 6), a + 1f, a + 1.005f, 0, 1);
                    g.slot = 0; g.color = White;
                    break;
                case "pan_head":
                    g.Lathe(g.RP(false, C(0, a), C(0.95f, a, 0.03f, 1), C(0.95f, a + 0.2f)), hs);
                    g.Lathe(g.Dome(0.95f, a + 0.2f, 0.42f, 0), hs);
                    SlotCross(g, a + 0.62f, 1.1f, true);
                    break;
                case "thumb":
                    g.Lathe(new List<Vector2> { new(0.65f, a), new(0.65f, a + 0.25f) }, hs);
                    int k = g.S(128, 192);
                    var o = new Vector2[k];
                    for (int i = 0; i < k; i++) { float t = Tau * i / k; o[i] = new Vector2(Mathf.Cos(t), Mathf.Sin(t)) * (1.2f + 0.05f * Mathf.Cos(16 * t)); }
                    g.Prism(o, a + 0.25f, a + 0.85f, 0.08f, g.S(3, 4));
                    break;
            }
        }

        static void SlotCross(G g, float y, float len, bool cross)
        {
            g.slot = 2; g.color = new Color(0.07f, 0.07f, 0.08f, 0);
            g.Box(new Vector3(0, y, 0), new Vector3(len, 0.012f, 0.16f));
            if (cross) g.Box(new Vector3(0, y, 0), new Vector3(0.16f, 0.012f, len));
            g.slot = 0; g.color = White;
        }

        // ================================================================== nuts (thread diameter 1)

        static void Nut(G g, string sub)
        {
            int n = g.S(48, 72), bs = g.S(28, 40);
            float hexRc = 0.92f * 0.866f * 0.97f;
            var thread = new Vector3(0, 1.2f, 0.17f);
            switch (sub)
            {
                case "square":
                    g.Crown(4, 1.13f, 1.13f * 0.7071f * 1.06f, 0, 0.8f, true, true, 0.56f, 0.56f, n, Mathf.PI / 4);
                    g.Bore(0.5f, 0.06f, 0, 0.8f, true, true, bs, thread);
                    break;
                case "wing":
                    g.Lathe(g.RP(false, C(0.5f, 0.15f), C(0.75f, 0.15f, 0.04f), C(0.75f, 0.9f, 0.12f), C(0.56f, 0.9f)), n);
                    g.Lathe(g.RP(false, C(0.56f, 0), C(0.95f, 0, 0.03f, 1), C(0.95f, 0.15f, 0.06f), C(0.75f, 0.15f)), n);
                    g.Bore(0.5f, 0.06f, 0, 0.9f, true, true, bs, thread);
                    var wing = g.RoundRect(0, 0, 0.45f, 0.45f, 0.12f, 0.36f, 0.36f, 0.12f);   // the top outer corner rounder
                    for (int s = -1; s <= 1; s += 2)
                    {
                        g.xf = Matrix4x4.TRS(new Vector3(s * 1.15f, 0.65f, 0), Quaternion.AngleAxis(s * 0.25f * Mathf.Rad2Deg, Vector3.forward), Vector3.one)
                             * Matrix4x4.Rotate(Quaternion.AngleAxis(-90, Vector3.right));
                        var w = new Vector2[wing.Length];
                        for (int i = 0; i < w.Length; i++) w[i] = new Vector2(wing[i].x * s, wing[i].y);
                        g.Prism(w, -0.11f, 0.11f, 0.05f, g.S(2, 3));
                    }
                    g.xf = Matrix4x4.identity;
                    break;
                case "flange":
                    g.Crown(6, 0.92f, hexRc, 0.15f, 0.85f, true, false, 0.56f, 0, n);
                    g.Lathe(g.RP(false, C(0.56f, 0), C(1.2f, 0, 0.03f, 1), C(1.2f, 0.15f, 0.07f), C(0.8f, 0.15f)), n);
                    g.Bore(0.5f, 0.06f, 0, 0.85f, true, true, bs, thread);
                    break;
                case "acorn":
                    g.Crown(6, 0.92f, hexRc, 0, 0.65f, false, true, 0.72f, 0.56f, n);
                    g.Lathe(g.RP(false, C(0.72f, 0.65f), C(0.72f, 0.8f)), n);
                    g.Lathe(g.Dome(0.72f, 0.8f, 0.44f, 0), n);
                    g.Lathe(new List<Vector2> { new(0, 0.6f), new(0.5f, 0.6f), new(0.5f, 0.06f), new(0.56f, 0) }, bs, thread);
                    break;
                case "nyloc":
                    g.Crown(6, 0.92f, hexRc, 0, 0.75f, false, true, 0.5f, 0.56f, n);
                    g.Lathe(g.RP(false, C(0.5f, 0.75f), C(0.8f, 0.75f), C(0.8f, 1f, 0.1f), C(0.68f, 1.12f, 0.04f), C(0.5f, 1.12f)), n);
                    g.Bore(0.5f, 0.06f, 0, 0.75f, false, true, bs, thread);
                    g.color = new Color(0.85f, 0.85f, 0.95f, 0); g.slot = 2;
                    g.Lathe(new List<Vector2> { new(0.5f, 1.12f), new(0.42f, 1.1f), new(0.42f, 0.78f), new(0.5f, 0.75f) }, bs);
                    g.slot = 0; g.color = White;
                    break;
                default:
                    g.Crown(6, 0.92f, hexRc, 0, 0.8f, true, true, 0.56f, 0.56f, n);
                    g.Bore(0.5f, 0.06f, 0, 0.8f, true, true, bs, thread);
                    break;
            }
        }

        // ================================================================== washers (thread diameter 1)

        static void Washer(G g, string sub)
        {
            int n = g.S(56, 80);
            switch (sub)
            {
                case "split":
                {
                    var path = new List<Vector3>();
                    int steps = g.S(28, 40);
                    for (int i = 0; i <= steps; i++)
                    {
                        float ang = (Tau - 0.35f) * i / steps;
                        path.Add(new Vector3(Mathf.Cos(ang) * 0.72f, 0.12f + 0.12f * i / steps, Mathf.Sin(ang) * 0.72f));
                    }
                    g.Tube(path, 0.14f, g.S(10, 14));
                    break;
                }
                case "fender":
                    g.Lathe(g.RP(true, C(0.54f, 0, 0.02f, 1), C(1.6f, 0, 0.03f), C(1.6f, 0.12f, 0.05f), C(0.54f, 0.12f, 0.025f, 1)), n + 16);
                    break;
                case "toothed":
                    g.Prism(GearOutline("spur", 16, 0.85f, 1.05f, g), 0, 0.14f, 0.025f, 1, 0.54f, 0.02f, g.S(32, 48));
                    break;
                case "square":
                {
                    float h = 1.3f * 0.7071f;
                    g.Prism(g.RoundRect(0, 0, h, h, 0.14f, 0.14f, 0.14f, 0.14f), 0, 0.2f, 0.04f, g.S(2, 3), 0.54f, 0.025f, g.S(32, 48));
                    break;
                }
                default:
                    g.Lathe(g.RP(true, C(0.54f, 0, 0.02f, 1), C(1f, 0, 0.035f), C(1f, 0.18f, 0.07f), C(0.54f, 0.18f, 0.03f, 1)), n);
                    break;
            }
        }

        // ================================================================== gears (outer diameter 1)

        /// <summary>One closed gear outline: per tooth a filleted root land, a curved rising flank, a rounded tip
        /// and the falling flank (sprockets: pointed teeth over round roller pockets; ratchets: a long ramp and a
        /// sharp drop). The tip radius is the old one, so the bounds don't change.</summary>
        static Vector2[] GearOutline(string sub, int teeth, float root, float tip, G g)
        {
            float h = tip - root, period = Tau / teeth;
            int mul = Mathf.Clamp(Mathf.RoundToInt(28f / teeth), 1, 3);
            int nf = g.S(3, 4) * mul, nt = g.S(2, 3) * mul, nr = g.S(2, 3) * mul;
            float S(float t) => Mathf.Lerp(t, t * t * (3 - 2 * t), 0.65f);
            (float f0, float f1, int kind, int n)[] segs = sub switch
            {
                "sprocket" => new[] { (0f, 0.33f, 1, nf), (0.33f, 0.42f, 2, nt), (0.42f, 0.75f, 3, nf), (0.75f, 1f, 4, nr + 1) },
                "ratchet" => new[] { (0f, 0.78f, 5, nf * 2), (0.78f, 0.82f, 2, nt), (0.82f, 0.86f, 6, 1), (0.86f, 1f, 0, nr) },
                _ => new[] { (0f, 0.22f, 1, nf), (0.22f, 0.5f, 2, nt), (0.5f, 0.72f, 3, nf), (0.72f, 1f, 0, nr) },
            };
            var o = new List<Vector2>();
            for (int k = 0; k < teeth; k++)
                foreach (var s in segs)
                    for (int i = 0; i < s.n; i++)
                    {
                        float t = (float)i / s.n, f = Mathf.Lerp(s.f0, s.f1, t), r = s.kind switch
                        {
                            1 => root + h * S(t),
                            2 => tip,
                            3 => root + h * S(1 - t),
                            4 => root - 0.18f * h * Mathf.Sin(Mathf.PI * t),        // roller pocket
                            5 => root + h * (1 - Mathf.Pow(1 - t, 1.7f)),             // ratchet ramp
                            6 => tip,                                                // the drop (next point is the root)
                            _ => root,
                        };
                        float ang = (k + f) * period;
                        o.Add(new Vector2(Mathf.Cos(ang), Mathf.Sin(ang)) * r);
                    }
            return o.ToArray();
        }

        /// <summary>Whether a gear is cast with spokes: BP1 (version 1) spokes every spur / ratchet of 32+ teeth;
        /// iteration 3 only <see cref="SpokedV2"/> (or, for the QA comparison, every spur, ratchet and sprocket of
        /// 24+ teeth).</summary>
        public static bool Spoked(string key, string sub, int teeth, int version, bool allSpoked = false)
        {
            if (sub == "pinion" || sub == "bevel") return false;
            if (version <= 1) return sub != "sprocket" && teeth >= 32;
            return allSpoked ? teeth >= 24 : SpokedV2.Contains(key);
        }

        static void Gear(G g, string sub, int teeth, bool spoked)
        {
            float module = 1f / (teeth + 2), tip = 0.5f, root = 0.5f - 2.25f * module;
            float thick = Mathf.Clamp(6 * module, 0.08f, 0.32f);
            float boreR = teeth >= 16 ? 0.12f : 0.16f, boreCh = 0.025f;
            var outer = GearOutline(sub, teeth, root, tip, g);
            int bs = g.S(36, 56), st = g.S(1, 2);
            float bev = Mathf.Min(0.32f * module, 0.2f * thick);
            switch (sub)
            {
                case "sprocket":
                    if (!spoked)
                    {
                        g.Prism(outer, 0, thick * 0.5f, Mathf.Min(bev, thick * 0.12f), st, boreR, boreCh * 0.6f, bs);
                        return;
                    }
                    {
                        // a lightened sprocket plate: the toothed rim, five windows between spokes a shade thinner than
                        // the plate (no coplanar faces where they run into the rim), the hub at full thickness
                        float ts = thick * 0.5f, rin = root - Mathf.Max(2.6f * module, 0.055f), hb = Mathf.Max(boreR + 0.08f, 0.2f);
                        g.Prism(outer, 0, ts, Mathf.Min(bev, ts * 0.12f), st, rin, Mathf.Min(ts * 0.15f, 0.02f), g.S(64, 96));
                        Spokes(g, hb, rin, ts * 0.1f, ts * 0.9f, 0.085f);
                        g.Lathe(g.RP(false, C(boreR + boreCh * 0.6f, 0), C(hb, 0, ts * 0.15f), C(hb, ts, ts * 0.15f), C(boreR + boreCh * 0.6f, ts)), bs);
                        g.Bore(boreR, boreCh * 0.6f, 0, ts, true, true, bs);
                    }
                    return;
                case "pinion":
                    g.Prism(outer, 0, thick * 1.8f, bev, st, boreR, boreCh, bs, top: true);
                    g.Lathe(g.RP(false, C(0.3f, thick * 1.8f), C(0.3f, thick * 2.6f, 0.06f), C(boreR + boreCh, thick * 2.6f)), bs);
                    g.Bore(boreR, boreCh, thick * 1.8f, thick * 2.6f, true, false, bs);
                    return;
                case "bevel":
                    g.Prism(outer, 0, thick * 0.6f, bev, st, boreR, boreCh, bs, top: false);
                    g.Loft(outer, 1, thick * 0.6f, 0.82f, thick * 1.2f, boreR, boreCh, bs);
                    return;
            }
            if (teeth < 20)
            {
                g.Prism(outer, 0, thick, bev, st, boreR, boreCh, bs);
                return;
            }
            // cast gear: toothed rim, recessed web (or spokes) and a full-thickness hub; the envelope is the old disc
            float rimIn = root - Mathf.Max(2.2f * module, 0.05f), hub = Mathf.Max(boreR + 0.09f, 0.21f);
            float rb = Mathf.Min(thick * 0.18f, 0.03f);
            g.Prism(outer, 0, thick, bev, st, rimIn, rb, g.S(64, 96));
            if (spoked)
            {
                // five spokes (an odd count reads as cast), each a rounded bar half the thickness
                Spokes(g, hub, rimIn, thick * 0.26f, thick * 0.74f, 0.075f);
                g.Lathe(g.RP(false, C(boreR + boreCh, 0), C(hub, 0, thick * 0.12f), C(hub, thick, thick * 0.12f), C(boreR + boreCh, thick)), bs);
                g.Bore(boreR, boreCh, 0, thick, true, true, bs);
            }
            else
            {
                // web at half the thickness, filleted into the hub
                float wy0 = thick * 0.28f, wy1 = thick * 0.72f, f = Mathf.Min(thick * 0.18f, 0.03f), ro = thick * 0.12f;
                g.Lathe(g.RP(false,
                    C(boreR + boreCh, 0), C(hub, 0, ro), C(hub, wy0, f), C(rimIn + 0.002f, wy0),
                    C(rimIn + 0.002f, wy1), C(hub, wy1, f), C(hub, thick, ro), C(boreR + boreCh, thick)), bs);
                g.Bore(boreR, boreCh, 0, thick, true, true, bs);
            }
        }

        /// <summary>Five rounded bars from the hub to the rim (overlapping both a little), between heights y0 and y1.</summary>
        static void Spokes(G g, float hub, float rimIn, float y0, float y1, float w)
        {
            float len = rimIn - hub + 0.04f;
            var bar = g.RoundRect(0, 0, len * 0.5f, w * 0.5f, w * 0.45f, w * 0.45f, w * 0.45f, w * 0.45f);
            for (int k = 0; k < 5; k++)
            {
                float ang = Tau * k / 5 + 0.3f, mid = (hub + rimIn) * 0.5f;
                g.xf = Matrix4x4.TRS(new Vector3(Mathf.Cos(ang) * mid, 0, Mathf.Sin(ang) * mid), Quaternion.AngleAxis(-ang * Mathf.Rad2Deg, Vector3.up), Vector3.one);
                g.Prism(bar, y0, y1, (y1 - y0) * 0.3f, g.S(2, 3));
            }
            g.xf = Matrix4x4.identity;
        }

        // ================================================================== springs (coil outer diameter 1, along +Y)

        static void Spring(G g, string sub, float c, float l)
        {
            float rw = 0.5f / c, rc = 0.5f - rw;
            int sides = g.S(8, 12), perTurn = g.S(16, 22);
            var path = new List<Vector3>();
            switch (sub)
            {
                case "extension":
                {
                    float hook = 0.45f, body = Mathf.Max(l - 2 * hook, rw * 4);
                    float turns = Mathf.Min(Mathf.Max(2, body / (rw * 2.05f)), 14);
                    Helix(path, rc, -body * 0.5f, body * 0.5f, turns, perTurn, false);
                    g.Tube(path, rw, sides);
                    for (int s = -1; s <= 1; s += 2)
                    {
                        var hp = new List<Vector3>();
                        for (int i = 0; i <= perTurn; i++)
                        {
                            float ang = Mathf.PI * i / perTurn;
                            hp.Add(new Vector3(rc * Mathf.Cos(ang), s * (body * 0.5f + hook * 0.5f + Mathf.Sin(ang) * hook * 0.5f), 0));
                        }
                        g.Tube(hp, rw, sides);
                    }
                    break;
                }
                case "torsion":
                {
                    float turns = Mathf.Clamp(l * 0.5f / (rw * 2.05f), 2, 8), h = turns * rw * 2.05f;
                    Helix(path, rc, -h * 0.5f, h * 0.5f, turns, perTurn, false);
                    g.Tube(path, rw, sides);
                    float leg = Mathf.Max(l, 1) * 0.9f;
                    g.Tube(new List<Vector3> { path[0], path[0] + new Vector3(0, 0, leg) }, rw, sides);
                    g.Tube(new List<Vector3> { path[^1], path[^1] + new Vector3(-leg, 0, 0) }, rw, sides);
                    break;
                }
                default:
                {
                    float turns = Mathf.Clamp(l / Mathf.Max(rw * 5, 0.18f), 3, 14);
                    Helix(path, rc, -l * 0.5f, l * 0.5f, turns, perTurn, true);
                    g.Tube(path, rw, sides);
                    break;
                }
            }
        }

        static void Helix(List<Vector3> path, float r, float y0, float y1, float turns, int perTurn, bool closedEnds)
        {
            int steps = Mathf.CeilToInt(turns * perTurn);
            for (int i = 0; i <= steps; i++)
            {
                float t = (float)i / steps, y = Mathf.Lerp(y0, y1, t);
                if (closedEnds)
                {
                    float e = 1 / turns;
                    if (t < e) y = Mathf.Lerp(y0, y0 + (y1 - y0) * e * 0.4f, t / e);
                    else if (t > 1 - e) y = Mathf.Lerp(y1 - (y1 - y0) * e * 0.4f, y1, (t - (1 - e)) / e);
                    else y = Mathf.Lerp(y0 + (y1 - y0) * e * 0.4f, y1 - (y1 - y0) * e * 0.4f, (t - e) / (1 - 2 * e));
                }
                float ang = Tau * turns * t;
                path.Add(new Vector3(Mathf.Cos(ang) * r, y, Mathf.Sin(ang) * r));
            }
        }

        // ================================================================== bearings (outer diameter 1, axis Y)

        static void Bearing(G g, string sub, float w)
        {
            int n = g.S(56, 80);
            const float bore = 0.21f;
            float ro = Mathf.Min(0.035f, w * 0.12f), ri = ro * 0.6f;
            if (sub == "thrust")
            {
                float t = w * 0.3f;
                g.Lathe(Ring(g, bore, 0.5f, 0, t, ro, ri), n);
                g.Lathe(Ring(g, bore, 0.5f, w - t, w, ro, ri), n);
                if (g.hero) Balls(g, 0.355f, w * 0.5f, t * 1.4f, 14);
                else
                {
                    g.color = new Color(0.5f, 0.5f, 0.5f, 0);
                    g.Lathe(Ring(g, 0.3f, 0.42f, t, w - t, 0.012f, 0.012f), g.S(32, 48));
                    g.color = White;
                }
                return;
            }
            g.Lathe(Ring(g, 0.41f, 0.5f, 0, w, ro, ri), n);
            g.Lathe(Ring(g, bore, 0.3f, 0, w, ri, ro), n);
            if (sub == "flanged") g.Lathe(g.RP(false, C(0.5f, 0), C(0.58f, 0, 0.01f, 1), C(0.58f, w * 0.18f, 0.025f), C(0.5f, w * 0.18f)), n);
            if (g.hero)
            {
                float ballR = Mathf.Min(0.06f, w * 0.38f);
                if (sub == "roller")
                    for (int i = 0; i < 12; i++)
                    {
                        float ang = Tau * i / 12;
                        g.xf = Matrix4x4.Translate(new Vector3(Mathf.Cos(ang) * 0.355f, 0, Mathf.Sin(ang) * 0.355f));
                        g.Lathe(g.RP(false, C(0, w * 0.12f), C(0.045f, w * 0.12f, 0.012f), C(0.045f, w * 0.88f, 0.012f), C(0, w * 0.88f)), 12);
                        g.xf = Matrix4x4.identity;
                    }
                else Balls(g, 0.355f, w * 0.5f, ballR, 10);
                g.color = new Color(0.6f, 0.6f, 0.62f, 0);
                g.Lathe(Ring(g, 0.3f, 0.41f, w * 0.86f, w * 0.92f, 0.008f, 0.008f), n);   // shield on one face only
                g.color = White;
            }
            else
            {
                // sealed: recessed shields on both faces
                g.color = new Color(0.5f, 0.5f, 0.52f, 0);
                g.Lathe(Ring(g, 0.3f, 0.41f, w * 0.08f, w * 0.92f, 0.01f, 0.01f), g.S(40, 56));
                g.color = White;
            }
        }

        /// <summary>A rectangular ring section revolved: outer corners rounded by `ro`, bore corners by `ri`.</summary>
        static List<Vector2> Ring(G g, float rIn, float rOut, float y0, float y1, float ro, float ri) =>
            g.RP(true, C(rIn, y0, ri), C(rOut, y0, ro), C(rOut, y1, ro), C(rIn, y1, ri));

        static void Balls(G g, float r, float y, float ballR, int count)
        {
            for (int i = 0; i < count; i++)
            {
                float ang = Tau * i / count;
                g.Sphere(new Vector3(Mathf.Cos(ang) * r, y, Mathf.Sin(ang) * r), ballR, 6, 10);
            }
        }

        // ================================================================== shafts and pins (diameter 1, along +Y)

        static void Shaft(G g, string sub, float a)
        {
            int segs = g.S(24, 36);
            switch (sub)
            {
                case "threaded_rod":
                    g.Lathe(g.RP(false, C(0, 0), C(0.5f, 0, 0.08f, 1), C(0.5f, a, 0.08f, 1), C(0, a)), segs, new Vector3(0.05f, a - 0.05f, 0.2f));
                    break;
                case "dowel_pin":
                    g.Lathe(g.RP(false, C(0, 0), C(0.5f, 0, 0.14f), C(0.5f, a, 0.14f), C(0, a)), segs);
                    break;
                case "clevis_pin":
                    g.Lathe(g.RP(false, C(0, 0), C(0.5f, 0, 0.09f), C(0.5f, a, 0.03f), C(0.8f, a, 0.02f, 1), C(0.8f, a + 0.3f, 0.1f), C(0, a + 0.3f)), segs);
                    g.color = new Color(0.1f, 0.1f, 0.1f, 0); g.slot = 2;
                    g.xf = Matrix4x4.TRS(new Vector3(0, 0.35f, 0), Quaternion.AngleAxis(90, Vector3.forward), Vector3.one);
                    g.Lathe(g.RP(false, C(0, -0.51f), C(0.12f, -0.51f, 0.03f), C(0.12f, 0.51f, 0.03f), C(0, 0.51f)), 12);
                    g.xf = Matrix4x4.identity;
                    g.slot = 0; g.color = White;
                    break;
                case "spring_pin":
                    g.Lathe(g.RP(true, C(0.36f, 0), C(0.45f, 0), C(0.5f, 0.08f, 0.02f), C(0.5f, a - 0.08f, 0.02f), C(0.45f, a), C(0.36f, a)), segs);
                    g.slot = 2; g.color = new Color(0.06f, 0.06f, 0.06f, 0);
                    g.Box(new Vector3(0.49f, a * 0.5f, 0), new Vector3(0.03f, a * 0.98f, 0.08f));
                    g.slot = 0; g.color = White;
                    break;
                default:
                    g.Lathe(g.RP(false, C(0, 0), C(0.5f, 0, 0.08f, 1),
                        C(0.5f, a * 0.15f), C(0.43f, a * 0.15f, 0.015f), C(0.43f, a * 0.15f + 0.12f, 0.015f), C(0.5f, a * 0.15f + 0.12f),
                        C(0.5f, a, 0.08f, 1), C(0, a)), segs);
                    g.slot = 2; g.color = new Color(0.12f, 0.12f, 0.13f, 0);
                    g.Box(new Vector3(0.47f, a * 0.6f, 0), new Vector3(0.08f, a * 0.4f, 0.25f));
                    g.slot = 0; g.color = White;
                    break;
            }
        }

        // ================================================================== electronics (nominal size)

        static void Electronic(G g, string sub)
        {
            int segs = g.S(24, 32), lead = 8;
            switch (sub)
            {
                case "resistor":
                {
                    g.Lathe(g.RP(false, C(0, -0.6f), C(0.22f, -0.6f, 0.1f), C(0.33f, -0.48f, 0.06f), C(0.33f, -0.32f, 0.06f), C(0.27f, -0.22f, 0.04f),
                        C(0.27f, 0.22f, 0.04f), C(0.33f, 0.32f, 0.06f), C(0.33f, 0.48f, 0.06f), C(0.22f, 0.6f, 0.1f), C(0, 0.6f)), segs);
                    g.slot = 2;
                    var bands = new[] { new Color(0.45f, 0.25f, 0.1f), new Color(0.05f, 0.05f, 0.05f), new Color(0.8f, 0.1f, 0.05f), new Color(0.85f, 0.65f, 0.2f) };
                    var ys = new[] { -0.42f, -0.15f, 0.05f, 0.4f };
                    for (int k = 0; k < 4; k++)
                    {
                        g.color = bands[k];
                        float r = Mathf.Abs(ys[k]) > 0.3f ? 0.335f : 0.275f;
                        g.Lathe(Ring(g, r - 0.01f, r + 0.006f, ys[k] - 0.05f, ys[k] + 0.05f, 0.004f, 0), segs);
                    }
                    g.slot = 1; g.color = White;
                    g.Cylinder(0.04f, -1.5f, -0.55f, lead);
                    g.Cylinder(0.04f, 0.55f, 1.5f, lead);
                    g.slot = 0;
                    break;
                }
                case "capacitor":
                    g.Lathe(g.RP(false, C(0, -0.8f), C(0.5f, -0.8f, 0.05f), C(0.5f, 0.65f), C(0.46f, 0.7f, 0.02f), C(0.5f, 0.75f), C(0.5f, 0.8f, 0.04f), C(0, 0.8f)), segs + 4);
                    g.slot = 2; g.color = new Color(0.82f, 0.82f, 0.86f);
                    g.BBox(new Vector3(0.5f, -0.05f, 0), new Vector3(0.016f, 1.3f, 0.26f), 0.004f);   // the stripe
                    g.slot = 1; g.color = White;
                    g.xf = Matrix4x4.Translate(new Vector3(0.18f, 0, 0)); g.Cylinder(0.035f, -1.6f, -0.8f, lead);
                    g.xf = Matrix4x4.Translate(new Vector3(-0.18f, 0, 0)); g.Cylinder(0.035f, -1.4f, -0.8f, lead);
                    g.xf = Matrix4x4.identity; g.slot = 0;
                    break;
                case "transistor":
                {
                    var d = new List<Vector2>();
                    int k = g.S(20, 28);
                    for (int i = 0; i <= k; i++) { float ang = -Mathf.PI * 0.1f + Mathf.PI * 1.2f * i / k; d.Add(new Vector2(Mathf.Cos(ang) * 0.32f, Mathf.Sin(ang) * 0.32f - 0.08f)); }
                    g.Prism(d.ToArray(), 0, 0.7f, 0.04f, g.S(2, 3));
                    g.slot = 1;
                    foreach (var x in new[] { -0.18f, 0f, 0.18f }) g.Box(new Vector3(x, -0.45f, 0), new Vector3(0.05f, 0.9f, 0.03f));
                    g.slot = 0;
                    break;
                }
                case "ic_chip":
                    g.BBox(Vector3.zero, new Vector3(1.6f, 0.3f, 0.75f), 0.035f);
                    g.slot = 2; g.color = new Color(0.35f, 0.35f, 0.36f);
                    g.Cylinder(0.05f, 0.15f, 0.154f, 12, new Vector3(-0.62f, 0, 0.2f));
                    g.slot = 1; g.color = White;
                    for (int k = 0; k < 8; k++)
                    {
                        float x = -0.65f + 1.3f * (k + 0.5f) / 8;
                        for (int s = -1; s <= 1; s += 2)
                        {
                            g.Box(new Vector3(x, -0.05f, s * 0.43f), new Vector3(0.09f, 0.05f, 0.12f));
                            g.Box(new Vector3(x, -0.2f, s * 0.49f), new Vector3(0.09f, 0.3f, 0.04f));
                        }
                    }
                    g.slot = 0;
                    break;
                case "led":
                    g.Lathe(g.RP(false, C(0, 0), C(0.34f, 0, 0.02f, 1), C(0.34f, 0.08f, 0.02f, 1), C(0.29f, 0.08f), C(0.29f, 0.5f)), segs);
                    g.Lathe(g.Dome(0.29f, 0.5f, 0.28f, 0), segs);
                    g.slot = 1;
                    g.Box(new Vector3(-0.1f, -0.6f, 0), new Vector3(0.04f, 1.2f, 0.04f));
                    g.Box(new Vector3(0.1f, -0.5f, 0), new Vector3(0.04f, 1f, 0.04f));
                    g.slot = 0;
                    break;
                case "relay":
                    g.BBox(Vector3.zero, new Vector3(1f, 0.75f, 0.8f), 0.06f);
                    g.slot = 2; g.color = new Color(0.92f, 0.92f, 0.9f);
                    g.Box(new Vector3(0, 0.376f, 0), new Vector3(0.6f, 0.004f, 0.3f));
                    g.slot = 1; g.color = White;
                    foreach (var p in new[] { new Vector2(-0.35f, -0.25f), new Vector2(-0.35f, 0.25f), new Vector2(0.35f, -0.25f), new Vector2(0.35f, 0.25f), Vector2.zero })
                        g.Box(new Vector3(p.x, -0.5f, p.y), new Vector3(0.05f, 0.3f, 0.05f));
                    g.slot = 0;
                    break;
                case "toggle_switch":
                    g.BBox(new Vector3(0, -0.3f, 0), new Vector3(0.7f, 0.6f, 0.55f), 0.05f);
                    g.slot = 1;
                    g.Shank(0.22f, 0, 0.45f, 0.06f, segs, false);
                    g.Crown(6, 0.4f, 0.4f * 0.866f * 0.97f, 0.05f, 0.17f, true, true, 0.22f, 0.22f, g.S(36, 48));
                    g.xf = Matrix4x4.TRS(new Vector3(0, 0.45f, 0), Quaternion.AngleAxis(0.35f * Mathf.Rad2Deg, Vector3.forward), Vector3.one);
                    g.Lathe(g.RP(false, C(0, 0), C(0.07f, 0), C(0.07f, 0.48f, 0.03f), C(0.09f, 0.55f, 0.03f), C(0, 0.58f)), 12);
                    g.xf = Matrix4x4.identity;
                    foreach (var x in new[] { -0.2f, 0f, 0.2f }) g.Box(new Vector3(x, -0.75f, 0), new Vector3(0.08f, 0.3f, 0.03f));
                    g.slot = 0;
                    break;
                case "potentiometer":
                    g.Lathe(g.RP(false, C(0, -0.15f), C(0.6f, -0.15f, 0.03f, 1), C(0.6f, 0.2f), C(0, 0.2f)), segs + 8);
                    g.slot = 1;
                    g.Lathe(g.RP(false, C(0, 0.2f), C(0.62f, 0.2f), C(0.62f, 0.25f, 0.015f, 1), C(0.22f, 0.25f)), segs + 8);
                    g.Shank(0.22f, 0.25f, 0.45f, 0.06f, segs, false);
                    g.Lathe(g.RP(false, C(0, 0.45f), C(0.12f, 0.45f), C(0.12f, 1.1f, 0.04f), C(0, 1.1f)), segs);
                    foreach (var x in new[] { -0.3f, 0f, 0.3f }) g.Box(new Vector3(x, -0.2f, 0.62f), new Vector3(0.12f, 0.04f, 0.3f));
                    g.slot = 0;
                    break;
                default:
                    g.BBox(Vector3.zero, Vector3.one, 0.06f);
                    break;
            }
        }

        // ================================================================== specialty parts (nominal size)

        static void Specialty(G g, string sub)
        {
            int n = g.S(56, 80);
            switch (sub)
            {
                case "cam":
                {
                    var o = new Vector2[n];
                    for (int i = 0; i < n; i++) { float ang = Tau * i / n; o[i] = new Vector2(Mathf.Cos(ang), Mathf.Sin(ang)) * (0.32f + 0.18f * Mathf.Pow(0.5f + 0.5f * Mathf.Cos(ang), 2)); }
                    g.Prism(o, 0, 0.3f, 0.035f, g.S(2, 3), 0.1f, 0.015f, g.S(28, 40));
                    g.Lathe(g.RP(true, C(0.115f, 0.3f), C(0.18f, 0.3f), C(0.18f, 0.45f, 0.035f), C(0.115f, 0.45f)), n);
                    g.Bore(0.1f, 0.015f, 0.3f, 0.45f, true, false, g.S(28, 40));
                    break;
                }
                case "pulley":
                    g.Lathe(g.RP(true, C(0.1f, 0, 0.015f, 1), C(0.5f, 0, 0.025f), C(0.5f, 0.06f, 0.02f), C(0.36f, 0.22f, 0.04f),
                        C(0.5f, 0.38f, 0.02f), C(0.5f, 0.44f, 0.025f), C(0.1f, 0.44f, 0.015f, 1)), n);
                    break;
                case "coupling":
                    g.Lathe(Ring(g, 0.18f, 0.5f, 0, 1.6f, 0.07f, 0.025f), n);
                    g.slot = 2; g.color = new Color(0.1f, 0.1f, 0.1f);
                    foreach (var y in new[] { 0.3f, 1.3f })
                    {
                        g.xf = Matrix4x4.TRS(new Vector3(0.5f, y, 0), Quaternion.AngleAxis(90, Vector3.forward), Vector3.one);
                        g.Cylinder(0.09f, -0.01f, 0.01f, 16);
                    }
                    g.xf = Matrix4x4.identity;
                    g.Box(new Vector3(0, 0.8f, 0.5f), new Vector3(0.06f, 0.04f, 0.02f));
                    g.slot = 0; g.color = White;
                    break;
                case "hinge":
                    g.BBox(new Vector3(0, 0, -0.42f), new Vector3(1.6f, 0.05f, 0.8f), 0.018f);
                    g.BBox(new Vector3(0, 0.05f, 0.42f), new Vector3(1.6f, 0.05f, 0.8f), 0.018f);
                    g.xf = Matrix4x4.TRS(new Vector3(0, 0.05f, 0), Quaternion.AngleAxis(90, Vector3.forward), Vector3.one);
                    g.Lathe(g.RP(false, C(0, -0.8f), C(0.08f, -0.8f, 0.02f), C(0.08f, 0.8f, 0.02f), C(0, 0.8f)), g.S(24, 32));
                    g.xf = Matrix4x4.identity;
                    g.slot = 2; g.color = new Color(0.12f, 0.1f, 0.08f);
                    foreach (var p in new[] { new Vector2(-0.5f, -0.45f), new Vector2(0.5f, -0.45f), new Vector2(-0.5f, 0.45f), new Vector2(0.5f, 0.45f) })
                        g.Cylinder(0.08f, 0, 0.002f, 16, new Vector3(p.x, p.y < 0 ? 0.026f : 0.076f, p.y));
                    g.slot = 0; g.color = White;
                    break;
                case "knob":
                {
                    int k = g.S(160, 240);
                    var o = new Vector2[k];
                    for (int i = 0; i < k; i++) { float ang = Tau * i / k; o[i] = new Vector2(Mathf.Cos(ang), Mathf.Sin(ang)) * (0.485f + 0.015f * Mathf.Cos(20 * ang)); }
                    g.Prism(o, 0.08f, 0.4f, 0, 1, 0, 0, 0, top: false, bottom: false);
                    g.Lathe(g.RP(false, C(0, 0), C(0.42f, 0, 0.02f), C(0.5f, 0.08f), C(0.46f, 0.08f)), n);
                    g.Lathe(g.RP(false, C(0.47f, 0.4f), C(0.49f, 0.4f, 0.03f), C(0.4f, 0.45f, 0.06f), C(0, 0.46f)), n);
                    break;
                }
                case "u_joint":
                    for (int s = -1; s <= 1; s += 2)
                    {
                        g.xf = Matrix4x4.Rotate(Quaternion.AngleAxis(s > 0 ? 0 : 90, Vector3.up));
                        float y0 = s * 0.35f, y1 = s * 0.8f;
                        g.Lathe(g.RP(false, C(0, Mathf.Min(y0, y1)), C(0.3f, Mathf.Min(y0, y1), 0.06f), C(0.3f, Mathf.Max(y0, y1), 0.06f), C(0, Mathf.Max(y0, y1))), g.S(40, 56));
                        foreach (var side in new[] { -1f, 1f }) g.BBox(new Vector3(side * 0.28f, s * 0.18f, 0), new Vector3(0.12f, 0.42f, 0.3f), 0.035f);
                    }
                    g.xf = Matrix4x4.Rotate(Quaternion.AngleAxis(90, Vector3.forward));
                    g.Lathe(g.RP(false, C(0, -0.36f), C(0.08f, -0.36f, 0.02f), C(0.08f, 0.36f, 0.02f), C(0, 0.36f)), 16);
                    g.xf = Matrix4x4.Rotate(Quaternion.AngleAxis(90, Vector3.right));
                    g.Lathe(g.RP(false, C(0, -0.36f), C(0.08f, -0.36f, 0.02f), C(0.08f, 0.36f, 0.02f), C(0, 0.36f)), 16);
                    g.xf = Matrix4x4.identity;
                    break;
                case "flywheel":
                    g.Lathe(g.RP(true, C(0.08f, 0, 0.012f, 1), C(0.16f, 0, 0.02f), C(0.16f, 0.12f, 0.03f), C(0.4f, 0.12f, 0.03f), C(0.4f, 0, 0.02f), C(0.5f, 0, 0.035f),
                        C(0.5f, 0.42f, 0.035f), C(0.4f, 0.42f, 0.02f), C(0.4f, 0.3f, 0.03f), C(0.16f, 0.3f, 0.03f), C(0.16f, 0.42f, 0.02f), C(0.08f, 0.42f, 0.012f, 1)), n);
                    break;
                default:
                    g.BBox(Vector3.zero, Vector3.one, 0.06f);
                    break;
            }
        }

        // ================================================================== large machine components (nominal size 1)

        static void Machine(G g, string sub)
        {
            int n = g.S(56, 80), sm = g.S(20, 28);
            void Acc(Color c) { g.slot = 2; g.color = new Color(c.r, c.g, c.b, 0); }
            void Main() { g.slot = 0; g.color = White; }
            void BoltRing(float r, float y, int count, float size, Matrix4x4 basis)
            {
                var old = g.xf;
                for (int k = 0; k < count; k++)
                {
                    float a = Tau * k / count;
                    g.xf = old * basis * Matrix4x4.Translate(new Vector3(Mathf.Cos(a) * r, y, Mathf.Sin(a) * r));
                    g.Crown(6, size, size * 0.866f * 0.95f, 0, size * 0.8f, true, false, 0, 0, 24);
                }
                g.xf = old;
            }
            Main();
            switch (sub)
            {
                case "bracket":
                    g.BBox(new Vector3(0, 0.04f, 0), new Vector3(1f, 0.08f, 0.6f), 0.025f);
                    g.BBox(new Vector3(-0.46f, 0.45f, 0), new Vector3(0.08f, 0.82f, 0.6f), 0.025f);
                    g.xf = Matrix4x4.TRS(new Vector3(-0.3f, 0.22f, 0), Quaternion.AngleAxis(45, Vector3.forward), Vector3.one);
                    g.BBox(Vector3.zero, new Vector3(0.45f, 0.06f, 0.06f), 0.015f);
                    g.xf = Matrix4x4.identity;
                    Acc(Dark);
                    foreach (var x in new[] { -0.1f, 0.25f }) foreach (var z in new[] { -0.16f, 0.16f }) g.Cylinder(0.05f, 0.081f, 0.084f, sm, new Vector3(x, 0, z));
                    foreach (var y in new[] { 0.35f, 0.65f })
                    {
                        g.xf = Matrix4x4.TRS(new Vector3(-0.415f, y, 0), Quaternion.AngleAxis(90, Vector3.forward), Vector3.one);
                        g.Cylinder(0.05f, -0.001f, 0.002f, sm);
                    }
                    g.xf = Matrix4x4.identity;
                    break;
                case "motor_housing":
                {
                    g.xf = Matrix4x4.TRS(new Vector3(0, 0.42f, 0), Quaternion.AngleAxis(90, Vector3.forward), Vector3.one);
                    g.Lathe(g.RP(false, C(0, -0.5f), C(0.36f, -0.5f, 0.05f), C(0.36f, 0.42f), C(0, 0.42f)), n);
                    const int fins = 8;
                    for (int k = 0; k < fins; k++)
                    {
                        float y = -0.42f + 0.8f * k / (fins - 1);
                        g.Lathe(g.RP(false, C(0.36f, y - 0.022f), C(0.41f, y - 0.022f, 0.016f), C(0.41f, y + 0.022f, 0.016f), C(0.36f, y + 0.022f)), n);
                    }
                    g.Lathe(g.RP(false, C(0, 0.42f), C(0.35f, 0.42f, 0.03f), C(0.31f, 0.52f, 0.05f), C(0.12f, 0.56f, 0.04f), C(0, 0.56f)), n);
                    Acc(Silver);
                    g.Lathe(g.RP(false, C(0, 0.56f), C(0.06f, 0.56f), C(0.06f, 0.78f, 0.015f, 1), C(0, 0.78f)), sm);
                    Main();
                    g.xf = Matrix4x4.identity;
                    g.BBox(new Vector3(0, 0.05f, 0), new Vector3(0.75f, 0.1f, 0.62f), 0.03f);
                    g.BBox(new Vector3(0, 0.14f, 0), new Vector3(0.5f, 0.12f, 0.3f), 0.03f);
                    Acc(Dark);
                    g.BBox(new Vector3(0, 0.83f, 0), new Vector3(0.3f, 0.12f, 0.25f), 0.025f);
                    break;
                }
                case "gearbox":
                    g.BBox(new Vector3(0, 0.3f, 0), new Vector3(0.9f, 0.6f, 0.6f), 0.06f);
                    g.BBox(new Vector3(0, 0.63f, 0), new Vector3(0.95f, 0.06f, 0.65f), 0.02f);
                    g.BBox(new Vector3(0, 0.03f, 0), new Vector3(1f, 0.06f, 0.7f), 0.02f);
                    for (int s = -1; s <= 1; s += 2)
                    {
                        g.xf = Matrix4x4.TRS(new Vector3(s * 0.22f, 0.32f, 0), Quaternion.AngleAxis(90, Vector3.right), Vector3.one);
                        g.Lathe(g.RP(false, C(0, 0.29f), C(0.14f, 0.29f), C(0.14f, 0.38f, 0.035f), C(0, 0.38f)), n);
                        Acc(Silver);
                        float r = s < 0 ? 0.05f : 0.08f;
                        g.Lathe(g.RP(false, C(0, 0.38f), C(r, 0.38f), C(r, 0.6f, r * 0.25f, 1), C(0, 0.6f)), sm);
                        Main();
                    }
                    g.xf = Matrix4x4.identity;
                    Acc(Bolt);
                    foreach (var x in new[] { -0.42f, -0.14f, 0.14f, 0.42f })
                        foreach (var z in new[] { -0.28f, 0.28f })
                        {
                            g.xf = Matrix4x4.Translate(new Vector3(x, 0.66f, z));
                            g.Crown(6, 0.03f, 0.03f * 0.866f * 0.95f, 0, 0.03f, true, false, 0, 0, 24);
                        }
                    g.xf = Matrix4x4.identity;
                    break;
                case "valve":
                {
                    g.xf = Matrix4x4.TRS(new Vector3(0, 0.22f, 0), Quaternion.AngleAxis(90, Vector3.forward), Vector3.one);
                    g.Lathe(new List<Vector2> { new(0.16f, -0.46f), new(0.16f, 0.46f) }, n);
                    foreach (var y in new[] { -0.5f, 0.5f }) g.Lathe(Ring(g, 0.1f, 0.24f, y - 0.04f, y + 0.04f, 0.02f, 0.01f), n);
                    g.xf = Matrix4x4.identity;
                    g.Lathe(g.RP(false, C(0, 0.2f), C(0.2f, 0.2f), C(0.2f, 0.42f, 0.04f), C(0.12f, 0.5f, 0.04f), C(0, 0.5f)), n);
                    Acc(Silver);
                    g.Lathe(g.RP(false, C(0, 0.5f), C(0.03f, 0.5f), C(0.03f, 0.78f), C(0, 0.78f)), 12);
                    Acc(new Color(0.62f, 0.1f, 0.08f));
                    var wheel = new List<Vector3>();
                    int ws = g.S(40, 56);
                    for (int k = 0; k <= ws; k++) { float a = Tau * k / ws; wheel.Add(new Vector3(Mathf.Cos(a) * 0.24f, 0.78f, Mathf.Sin(a) * 0.24f)); }
                    g.Tube(wheel, 0.03f, g.S(10, 14), false);
                    for (int k = 0; k < 3; k++)
                    {
                        float a = Tau * k / 3;
                        g.Tube(new List<Vector3> { new(0, 0.78f, 0), new(Mathf.Cos(a) * 0.23f, 0.78f, Mathf.Sin(a) * 0.23f) }, 0.02f, 10, false);
                    }
                    g.Lathe(g.RP(false, C(0, 0.75f), C(0.05f, 0.75f, 0.015f), C(0.05f, 0.81f, 0.015f), C(0, 0.81f)), 16);   // hub
                    break;
                }
                case "pump_housing":
                    g.xf = Matrix4x4.TRS(new Vector3(0, 0.42f, 0), Quaternion.AngleAxis(90, Vector3.right), Vector3.one);
                    g.Lathe(g.RP(false, C(0, -0.2f), C(0.38f, -0.2f, 0.08f), C(0.38f, 0.2f, 0.08f), C(0, 0.2f)), n);
                    g.Lathe(Ring(g, 0.08f, 0.16f, 0.2f, 0.36f, 0.03f, 0.01f), n);
                    g.xf = Matrix4x4.identity;
                    g.Lathe(new List<Vector2> { new(0.12f, 0.6f), new(0.12f, 0.92f) }, n);
                    g.Lathe(Ring(g, 0.08f, 0.2f, 0.92f, 0.98f, 0.02f, 0.01f), n);
                    g.Lathe(new List<Vector2> { new(0, 0.95f), new(0.08f, 0.95f) }, sm);
                    g.BBox(new Vector3(0, 0.04f, 0), new Vector3(0.7f, 0.08f, 0.42f), 0.025f);
                    Acc(Bolt);
                    BoltRing(0.16f, 0.98f, 6, 0.035f, Matrix4x4.identity);
                    break;
                case "camshaft":
                {
                    const float a = 1.8f;
                    g.Lathe(g.RP(false, C(0, -a * 0.5f), C(0.08f, -a * 0.5f, 0.02f, 1), C(0.08f, a * 0.5f, 0.02f, 1), C(0, a * 0.5f)), sm);
                    for (int k = 0; k < 6; k++)
                    {
                        float y = -a * 0.4f + a * 0.8f * k / 5;
                        var pts = new Vector2[n];
                        for (int j = 0; j < n; j++)
                        {
                            float ang = Tau * j / n + k * 1.1f;
                            pts[j] = new Vector2(Mathf.Cos(ang), Mathf.Sin(ang)) * (0.16f + 0.1f * Mathf.Pow(0.5f + 0.5f * Mathf.Cos(ang - k * 1.1f), 3));
                        }
                        g.xf = Matrix4x4.TRS(new Vector3(0, y, 0), Quaternion.AngleAxis(90, Vector3.right), Vector3.one);
                        g.Prism(pts, -0.05f, 0.05f, 0.02f, g.S(2, 3));
                        g.xf = Matrix4x4.identity;
                    }
                    foreach (var y in new[] { -a * 0.5f + 0.08f, a * 0.5f - 0.08f })
                        g.Lathe(g.RP(false, C(0, y - 0.04f), C(0.14f, y - 0.04f, 0.02f), C(0.14f, y + 0.04f, 0.02f), C(0, y + 0.04f)), n);
                    break;
                }
                case "crankshaft":
                {
                    const float a2 = 1.8f;
                    const int throws = 3;
                    g.Lathe(g.RP(false, C(0, -a2 * 0.5f), C(0.08f, -a2 * 0.5f, 0.02f, 1), C(0.08f, -a2 * 0.5f + 0.25f), C(0, -a2 * 0.5f + 0.25f)), sm);
                    g.Lathe(g.RP(false, C(0, a2 * 0.5f - 0.25f), C(0.08f, a2 * 0.5f - 0.25f), C(0.08f, a2 * 0.5f, 0.02f, 1), C(0, a2 * 0.5f)), sm);
                    var web = g.RoundRect(0.08f, 0, 0.19f, 0.12f, 0.1f, 0.1f, 0.1f, 0.1f);
                    for (int k = 0; k < throws; k++)
                    {
                        float y0 = -a2 * 0.5f + 0.25f + k * (a2 - 0.5f) / throws, y1 = y0 + (a2 - 0.5f) / throws;
                        var off = new Vector3(Mathf.Cos(k * Tau / 3) * 0.16f, 0, Mathf.Sin(k * Tau / 3) * 0.16f);
                        foreach (var yy in new[] { y0, y1 - 0.06f })
                        {
                            g.xf = Matrix4x4.TRS(new Vector3(0, yy + 0.03f, 0), Quaternion.AngleAxis(-k * 120f, Vector3.up), Vector3.one);
                            g.Prism(web, -0.03f, 0.03f, 0.012f, g.S(2, 3));
                            g.xf = Matrix4x4.identity;
                        }
                        g.xf = Matrix4x4.Translate(off);
                        g.Lathe(new List<Vector2> { new(0.07f, y0 + 0.06f), new(0.07f, y1 - 0.06f) }, sm);
                        g.xf = Matrix4x4.identity;
                        g.Lathe(g.RP(false, C(0, y1 - 0.03f), C(0.09f, y1 - 0.03f, 0.01f, 1), C(0.09f, y1 + 0.03f, 0.01f, 1), C(0, y1 + 0.03f)), sm);
                    }
                    break;
                }
                case "pipe_elbow":
                {
                    var path = new List<Vector3>();
                    int steps = g.S(16, 24);
                    for (int k = 0; k <= steps; k++)
                    {
                        float ang = Mathf.PI * 0.5f * k / steps;
                        path.Add(new Vector3(0.45f - Mathf.Cos(ang) * 0.45f, Mathf.Sin(ang) * 0.45f, 0));
                    }
                    g.Tube(path, 0.16f, n, false);
                    g.xf = Matrix4x4.Rotate(Quaternion.AngleAxis(90, Vector3.forward));
                    g.Lathe(Ring(g, 0.1f, 0.28f, -0.05f, 0.05f, 0.025f, 0.01f), n);
                    g.xf = Matrix4x4.Translate(new Vector3(0.45f, 0.45f, 0));
                    g.Lathe(Ring(g, 0.1f, 0.28f, -0.05f, 0.05f, 0.025f, 0.01f), n);
                    g.xf = Matrix4x4.identity;
                    Acc(Bolt);
                    BoltRing(0.22f, 0.05f, 4, 0.03f, Matrix4x4.identity);
                    break;
                }
                case "lever_arm":
                {
                    const float len = 1.8f;
                    g.BBox(Vector3.zero, new Vector3(0.16f, len - 0.3f, 0.08f), 0.025f);
                    foreach (var y in new[] { -len * 0.5f + 0.15f, len * 0.5f - 0.15f })
                    {
                        g.xf = Matrix4x4.TRS(new Vector3(0, y, 0), Quaternion.AngleAxis(90, Vector3.right), Vector3.one);
                        g.Lathe(Ring(g, 0.07f, 0.17f, -0.06f, 0.06f, 0.035f, 0.012f), n);
                        g.xf = Matrix4x4.identity;
                    }
                    Acc(Dark);
                    g.Box(new Vector3(0, 0.1f, 0.041f), new Vector3(0.06f, len * 0.5f, 0.004f));
                    break;
                }
                case "sprocket_hub":
                    g.Prism(GearOutline("sprocket", 24, 0.43f, 0.5f, g), 0, 0.06f, 0.012f, 1, 0.12f, 0.01f, g.S(36, 48));
                    g.Lathe(Ring(g, 0.12f, 0.2f, -0.18f, 0.24f, 0.03f, 0.012f), n);
                    g.Lathe(Ring(g, 0.2f, 0.3f, 0.06f, 0.1f, 0.015f, 0), n);
                    Acc(Bolt);
                    BoltRing(0.25f, 0.1f, 6, 0.03f, Matrix4x4.identity);
                    break;
                default:
                    g.BBox(Vector3.zero, Vector3.one * 0.8f, 0.05f);
                    break;
            }
            g.xf = Matrix4x4.identity;
            Main();
        }

        // ================================================================== 2D helpers

        /// <summary>Regular polygon sampled at `count` evenly spaced angles (as the shared generator's poly_ring).</summary>
        static Vector2[] PolyRing(int sides, float circumradius, int count, float rot = 0)
        {
            var o = new Vector2[count];
            float apothem = circumradius * Mathf.Cos(Mathf.PI / sides), seg = Tau / sides;
            for (int i = 0; i < count; i++)
            {
                float a = Tau * i / count, phi = Mathf.Repeat(a - rot, seg) - seg * 0.5f;
                o[i] = new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * (apothem / Mathf.Cos(phi));
            }
            return o;
        }

        // ================================================================== the builder

        /// <summary>Geometry in Godot space, per material slot; Finish mirrors Z into Unity space.</summary>
        sealed class G
        {
            public readonly bool hero, compact;
            public Matrix4x4 xf = Matrix4x4.identity;
            public Color color = White;
            public int slot;
            bool _thread;
            readonly List<Vector3> _v = new(), _n = new();
            readonly List<Vector2> _uv = new();
            readonly List<Color> _c = new();
            readonly SortedDictionary<int, List<int>> _t = new();
            const float Crease = 40f;

            public G(int tier) { hero = tier == TierHero; compact = tier == TierCompact; }
            /// <summary>Segments: the standard or hero count; compact (small parts) about 60 % of the standard,
            /// never under 12 around, and one step less on small counts (profile rounds, flank points).</summary>
            public int S(int normal, int big) => hero ? big : !compact ? normal
                : normal >= 16 ? Mathf.Max(12, Mathf.RoundToInt(normal * 0.6f)) : normal >= 6 ? Mathf.Max(6, normal - 2) : Mathf.Max(1, normal - 1);

            int V(Vector3 p, Vector3 n, Vector2 uv)
            {
                _v.Add(xf.MultiplyPoint3x4(p));
                _n.Add(xf.MultiplyVector(n).normalized);
                _uv.Add(uv);
                _c.Add(new Color(color.r, color.g, color.b, _thread ? 1 : 0));   // alpha 1 = the shader's thread flag
                return _v.Count - 1;
            }

            void Tri(int a, int b, int c)
            {
                if (!_t.TryGetValue(slot, out var l)) _t[slot] = l = new List<int>();
                l.Add(a); l.Add(b); l.Add(c);
            }

            void Quad(int a, int b, int c, int d) { Tri(a, b, c); Tri(a, c, d); }

            // ---------------------------------------------------------- profiles

            /// <summary>A profile from corners (x, y, round radius, steps): radius 0 = sharp, steps 1 = a chamfer,
            /// steps 0 = a round (3 chords, 4 for hero meshes). Open profiles keep their end points; closed ones
            /// repeat the first point at the end.</summary>
            public List<Vector2> RP(bool closed, params Vector4[] c)
            {
                int n = c.Length;
                var o = new List<Vector2>();
                for (int i = 0; i < n; i++)
                {
                    var p = new Vector2(c[i].x, c[i].y);
                    float r = c[i].z;
                    if (r <= 0 || (!closed && (i == 0 || i == n - 1))) { o.Add(p); continue; }
                    var a = (Vector2)c[(i - 1 + n) % n];
                    var b = (Vector2)c[(i + 1) % n];
                    Vector2 d1 = p - a, d2 = b - p;
                    float l1 = d1.magnitude, l2 = d2.magnitude;
                    if (l1 < 1e-6f || l2 < 1e-6f) { o.Add(p); continue; }
                    d1 /= l1; d2 /= l2;
                    float turn = Mathf.Acos(Mathf.Clamp(Vector2.Dot(d1, d2), -1, 1));
                    if (turn < 1e-3f || turn > Mathf.PI - 1e-3f) { o.Add(p); continue; }
                    float t = r * Mathf.Tan(turn * 0.5f), tmax = 0.5f * Mathf.Min(l1, l2);
                    if (t > tmax) { r *= tmax / t; t = tmax; }
                    Vector2 t1 = p - d1 * t, t2 = p + d2 * t;
                    float cross = d1.x * d2.y - d1.y * d2.x;
                    var nrm = cross > 0 ? new Vector2(-d1.y, d1.x) : new Vector2(d1.y, -d1.x);
                    var ctr = t1 + nrm * r;
                    int steps = c[i].w > 0 ? (int)c[i].w : (hero ? 4 : compact ? 2 : 3);
                    float a1 = Mathf.Atan2(t1.y - ctr.y, t1.x - ctr.x), a2 = Mathf.Atan2(t2.y - ctr.y, t2.x - ctr.x);
                    float da = Mathf.DeltaAngle(a1 * Mathf.Rad2Deg, a2 * Mathf.Rad2Deg) * Mathf.Deg2Rad;
                    for (int k = 0; k <= steps; k++)
                    {
                        float ang = a1 + da * k / steps;
                        o.Add(ctr + new Vector2(Mathf.Cos(ang), Mathf.Sin(ang)) * r);
                    }
                }
                if (closed) o.Add(o[0]);
                return o;
            }

            /// <summary>A dome profile (quarter ellipse) from (r, y) up to the axis at y + h, with an optional
            /// bottom face from the axis (bottomR > 0 rounds its rim).</summary>
            public List<Vector2> Dome(float r, float y, float h, float bottomRound)
            {
                var o = new List<Vector2>();
                float b = Mathf.Clamp(bottomRound, 0, h * 0.4f);
                if (b > 0) { o.Add(new Vector2(0, y)); o.Add(new Vector2(r - b, y)); }   // flat underside, chamfered rim
                int k = S(10, 14);
                for (int i = 0; i <= k; i++)
                {
                    float t = Mathf.PI * 0.5f * i / k;
                    o.Add(new Vector2(r * Mathf.Cos(t), y + b + (h - b) * Mathf.Sin(t)));
                }
                return o;
            }

            /// <summary>A rounded rectangle outline around (cx, cz), half sizes hx, hz, corner radii (-x-z, +x-z,
            /// +x+z, -x+z).</summary>
            public Vector2[] RoundRect(float cx, float cz, float hx, float hz, float r0, float r1, float r2, float r3)
            {
                var o = RP(true, C(cx - hx, cz - hz, r0), C(cx + hx, cz - hz, r1), C(cx + hx, cz + hz, r2), C(cx - hx, cz + hz, r3));
                o.RemoveAt(o.Count - 1);
                return o.ToArray();
            }

            // ---------------------------------------------------------- lathe

            /// <summary>Revolves a (radius, y) profile around +Y (outward along the bottom, up the outside, inward
            /// along the top, down any bore). Crease-angle normals: neighbouring profile segments within 40 degrees
            /// share a normal (arcs, round-overs and curved walls shade smooth); flat (horizontal) faces never
            /// smooth into anything, so caps stay planar surfaces for the skins. thread = (y0, y1, pitch) flags
            /// cylindrical segments in that span for the shader's threads.</summary>
            public void Lathe(IList<Vector2> p, int segs, Vector3 thread = default, float phase = 0)
            {
                int m = p.Count, ns = m - 1;
                if (ns < 1) return;
                bool closed = m > 2 && (p[0] - p[m - 1]).sqrMagnitude < 1e-12f;
                var sn = new Vector2[ns];
                var ok = new bool[ns];
                var flat = new bool[ns];
                var thr = new bool[ns];
                for (int k = 0; k < ns; k++)
                {
                    var d = p[k + 1] - p[k];
                    ok[k] = d.sqrMagnitude > 1e-12f && !(p[k].x < 1e-6f && p[k + 1].x < 1e-6f);
                    sn[k] = new Vector2(d.y, -d.x).normalized;
                    flat[k] = Mathf.Abs(d.y) < 1e-6f;
                    thr[k] = thread.z > 0 && Mathf.Abs(d.x) < 1e-6f && Mathf.Min(p[k].y, p[k + 1].y) >= thread.x - 1e-4f && Mathf.Max(p[k].y, p[k + 1].y) <= thread.y + 1e-4f;
                }
                float cosC = Mathf.Cos(Crease * Mathf.Deg2Rad);
                int Nb(int k, int dir)
                {
                    for (int s = 1; s <= 2; s++)
                    {
                        int q = k + dir * s;
                        if (closed) q = (q + ns) % ns;
                        if (q < 0 || q >= ns) return -1;
                        if (ok[q]) return q;
                    }
                    return -1;
                }
                bool Smooth(int a, int b) => a >= 0 && b >= 0 && !flat[a] && !flat[b] && thr[a] == thr[b] && Vector2.Dot(sn[a], sn[b]) > cosC;
                for (int k = 0; k < ns; k++)
                {
                    if (!ok[k]) continue;
                    int pv = Nb(k, -1), nx = Nb(k, 1);
                    var n0 = Smooth(pv, k) ? (sn[pv] + sn[k]).normalized : sn[k];
                    var n1 = Smooth(k, nx) ? (sn[k] + sn[nx]).normalized : sn[k];
                    if (pv >= 0 && flat[pv] && !flat[k]) n0 = Away(n0, sn[pv]);
                    if (nx >= 0 && flat[nx] && !flat[k]) n1 = Away(n1, sn[nx]);
                    Vector2 p0 = p[k], p1 = p[k + 1];
                    _thread = thr[k];
                    float v0 = thr[k] ? p0.y / thread.z : p0.y, v1 = thr[k] ? p1.y / thread.z : p1.y;
                    for (int j = 0; j < segs; j++)
                    {
                        float a0 = phase + Tau * j / segs, a1 = phase + Tau * (j + 1) / segs;
                        var c0 = new Vector3(Mathf.Cos(a0), 0, Mathf.Sin(a0));
                        var c1 = new Vector3(Mathf.Cos(a1), 0, Mathf.Sin(a1));
                        int i0 = V(c0 * p0.x + new Vector3(0, p0.y, 0), c0 * n0.x + new Vector3(0, n0.y, 0), new Vector2((float)j / segs, v0));
                        int i1 = V(c1 * p0.x + new Vector3(0, p0.y, 0), c1 * n0.x + new Vector3(0, n0.y, 0), new Vector2((float)(j + 1) / segs, v0));
                        int i2 = V(c1 * p1.x + new Vector3(0, p1.y, 0), c1 * n1.x + new Vector3(0, n1.y, 0), new Vector2((float)(j + 1) / segs, v1));
                        int i3 = V(c0 * p1.x + new Vector3(0, p1.y, 0), c0 * n1.x + new Vector3(0, n1.y, 0), new Vector2((float)j / segs, v1));
                        Quad(i0, i1, i2, i3);
                    }
                    _thread = false;
                }
            }

            /// <summary>Keeps a fine round-over's first chord at least 15 degrees off the flat face it starts from,
            /// so SkinUV still sees the cap as its own planar surface.</summary>
            static Vector2 Away(Vector2 n, Vector2 flatN)
            {
                float ang = Vector2.SignedAngle(flatN, n);
                if (Mathf.Abs(ang) >= 15f || Mathf.Abs(ang) < 0.01f) return n;
                float r = Mathf.Sign(ang) * 15f * Mathf.Deg2Rad, cs = Mathf.Cos(r), sn = Mathf.Sin(r);
                return new Vector2(flatN.x * cs - flatN.y * sn, flatN.x * sn + flatN.y * cs);
            }

            public void Cylinder(float r, float y0, float y1, int segs, Vector3 at = default)
            {
                var old = xf;
                xf = xf * Matrix4x4.Translate(at);
                Lathe(new List<Vector2> { new(0, y0), new(r, y0), new(r, y1), new(0, y1) }, segs);
                xf = old;
            }

            /// <summary>A round bore from y1 down to y0 with chamfered mouths.</summary>
            public void Bore(float r, float ch, float y0, float y1, bool chTop, bool chBottom, int segs, Vector3 thread = default)
            {
                var p = new List<Vector2>();
                if (chTop) { p.Add(new Vector2(r + ch, y1)); p.Add(new Vector2(r, y1 - ch)); } else p.Add(new Vector2(r, y1));
                if (chBottom) { p.Add(new Vector2(r, y0 + ch)); p.Add(new Vector2(r + ch, y0)); } else p.Add(new Vector2(r, y0));
                Lathe(p, segs, thread);
            }

            /// <summary>Threaded shank (shader threads, no ridges) with a chamfered tip.</summary>
            public void Shank(float r, float y0, float y1, float pitch, int segs, bool tip = true)
            {
                var p = new List<Vector2> { new(0, y0) };
                float t0 = y0;
                if (tip) { p.Add(new Vector2(r * 0.75f, y0)); t0 = y0 + r * 0.25f; p.Add(new Vector2(r, t0)); }
                else p.Add(new Vector2(r, y0));
                p.Add(new Vector2(r, y1));
                p.Add(new Vector2(0, y1));
                Lathe(p, segs, new Vector3(t0, y1, pitch));
            }

            public void Sphere(Vector3 center, float r, int rings, int segs)
            {
                var old = xf;
                xf = xf * Matrix4x4.Translate(center);
                for (int k = 0; k < rings; k++)
                {
                    float b0 = -Mathf.PI / 2 + Mathf.PI * k / rings, b1 = -Mathf.PI / 2 + Mathf.PI * (k + 1) / rings;
                    var q0 = new Vector2(Mathf.Cos(b0), Mathf.Sin(b0));
                    var q1 = new Vector2(Mathf.Cos(b1), Mathf.Sin(b1));
                    for (int j = 0; j < segs; j++)
                    {
                        float a0 = Tau * j / segs, a1 = Tau * (j + 1) / segs;
                        var c0 = new Vector3(Mathf.Cos(a0), 0, Mathf.Sin(a0));
                        var c1 = new Vector3(Mathf.Cos(a1), 0, Mathf.Sin(a1));
                        Vector3 N(Vector3 c, Vector2 q) => c * q.x + new Vector3(0, q.y, 0);
                        Quad(V(N(c0, q0) * r, N(c0, q0), Vector2.zero), V(N(c1, q0) * r, N(c1, q0), Vector2.zero),
                             V(N(c1, q1) * r, N(c1, q1), Vector2.zero), V(N(c0, q1) * r, N(c0, q1), Vector2.zero));
                    }
                }
                xf = old;
            }

            // ---------------------------------------------------------- prisms

            /// <summary>Per-vertex wall data of a closed outline: outward edge normals, which vertices shade smooth
            /// (edges within the crease angle), the smooth vertex normal and its miter (for insets).</summary>
            static void Outline(IList<Vector2> o, out Vector2[] en, out bool[] sm, out Vector2[] vn, out Vector2[] mit)
            {
                int n = o.Count;
                float area = 0;
                for (int i = 0; i < n; i++) { var p = o[i]; var q = o[(i + 1) % n]; area += p.x * q.y - q.x * p.y; }
                float side = area > 0 ? 1 : -1;
                en = new Vector2[n];
                for (int i = 0; i < n; i++)
                {
                    var e = o[(i + 1) % n] - o[i];
                    en[i] = e.sqrMagnitude > 1e-14f ? new Vector2(e.y, -e.x).normalized * side : (i > 0 ? en[i - 1] : Vector2.right);
                }
                float cosC = Mathf.Cos(Crease * Mathf.Deg2Rad);
                sm = new bool[n]; vn = new Vector2[n]; mit = new Vector2[n];
                for (int i = 0; i < n; i++)
                {
                    Vector2 pa = en[(i - 1 + n) % n], pb = en[i], s = pa + pb;
                    sm[i] = Vector2.Dot(pa, pb) > cosC;
                    vn[i] = s.sqrMagnitude > 1e-8f ? s.normalized : pb;
                    mit[i] = vn[i] / Mathf.Max(Vector2.Dot(vn[i], pb), 0.35f);
                }
            }

            /// <summary>Extrudes a closed star-shaped outline (x, z) from y0 to y1. `bev` rounds (steps 2+) or
            /// chamfers (steps 1) the rims: the band runs from the wall to the cap, inset along the miter. Band
            /// normals stay 15 degrees off the wall and the cap, so the rims catch light as their own crisp
            /// edge while caps stay flat surfaces. boreR > 0 cuts a round chamfered bore (zipped to the caps).</summary>
            public void Prism(IList<Vector2> o, float y0, float y1, float bev, int steps, float boreR = 0, float boreCh = 0, int boreSegs = 0,
                bool top = true, bool bottom = true)
            {
                int n = o.Count;
                Outline(o, out var en, out var sm, out var vn, out var mit);
                float b = Mathf.Min(bev, 0.45f * (y1 - y0));
                if (b < 1e-6f) steps = 0;
                Vector2 Off(int i, float d) => o[i] - mit[i] * d;
                Vector2 WN(int edge, int vtx) => sm[vtx] ? vn[vtx] : en[edge];
                float yb = y0 + (bottom && steps > 0 ? b : 0), yt = y1 - (top && steps > 0 ? b : 0);
                for (int i = 0; i < n; i++)
                {
                    int j = (i + 1) % n;
                    Vector3 ni = new(WN(i, i).x, 0, WN(i, i).y), nj = new(WN(i, j).x, 0, WN(i, j).y);
                    Quad(V(new Vector3(o[i].x, yb, o[i].y), ni, new Vector2(0, yb)), V(new Vector3(o[j].x, yb, o[j].y), nj, new Vector2(1, yb)),
                         V(new Vector3(o[j].x, yt, o[j].y), nj, new Vector2(1, yt)), V(new Vector3(o[i].x, yt, o[i].y), ni, new Vector2(0, yt)));
                }
                for (int side = 0; side < 2; side++)
                {
                    bool up = side == 1;
                    if (up ? !top : !bottom) continue;
                    float sg = up ? 1 : -1, y = up ? y1 : y0;
                    if (steps > 0)
                        for (int k = 0; k < steps; k++)
                        {
                            float f0 = Mathf.PI * 0.5f * k / steps, f1 = Mathf.PI * 0.5f * (k + 1) / steps;
                            float in0 = b * (1 - Mathf.Cos(f0)), in1 = b * (1 - Mathf.Cos(f1));
                            float ya = y - sg * (b - b * Mathf.Sin(f0)), yc = y - sg * (b - b * Mathf.Sin(f1));
                            float m0 = steps == 1 ? Mathf.PI / 4 : Mathf.Clamp(f0, 0.26f, 1.31f), m1 = steps == 1 ? Mathf.PI / 4 : Mathf.Clamp(f1, 0.26f, 1.31f);
                            for (int i = 0; i < n; i++)
                            {
                                int j = (i + 1) % n;
                                Vector2 hi = WN(i, i), hj = WN(i, j);
                                Vector3 Nn(Vector2 h, float f) => new(h.x * Mathf.Cos(f), sg * Mathf.Sin(f), h.y * Mathf.Cos(f));
                                Vector2 a = Off(i, in0), bq = Off(j, in0), c = Off(j, in1), d = Off(i, in1);
                                Quad(V(new Vector3(a.x, ya, a.y), Nn(hi, m0), a), V(new Vector3(bq.x, ya, bq.y), Nn(hj, m0), bq),
                                     V(new Vector3(c.x, yc, c.y), Nn(hj, m1), c), V(new Vector3(d.x, yc, d.y), Nn(hi, m1), d));
                            }
                        }
                    var ring = new Vector2[n];
                    for (int i = 0; i < n; i++) ring[i] = Off(i, steps > 0 ? b : 0);
                    var nrm = new Vector3(0, sg, 0);
                    if (boreR > 0) Zip(ring, Circle(boreR + boreCh, boreSegs), y, nrm);
                    else Fan(ring, y, nrm);
                }
                if (boreR > 0) Bore(boreR, boreCh, y0, y1, top && boreCh > 0, bottom && boreCh > 0, boreSegs);
            }

            /// <summary>Tapered extrusion (the bevel gear's tooth cone): the outline scaled s0 at y0 to s1 at y1,
            /// top cap zipped to a chamfered bore.</summary>
            public void Loft(IList<Vector2> o, float s0, float y0, float s1, float y1, float boreR, float boreCh, int boreSegs)
            {
                int n = o.Count;
                Outline(o, out var en, out var sm, out var vn, out _);
                float h = y1 - y0;
                Vector3 N(int edge, int vtx)
                {
                    var n2 = sm[vtx] ? vn[vtx] : en[edge];
                    return new Vector3(n2.x, -(s1 - s0) / h * Vector2.Dot(n2, o[vtx]), n2.y).normalized;
                }
                for (int i = 0; i < n; i++)
                {
                    int j = (i + 1) % n;
                    Quad(V(new Vector3(o[i].x * s0, y0, o[i].y * s0), N(i, i), Vector2.zero), V(new Vector3(o[j].x * s0, y0, o[j].y * s0), N(i, j), Vector2.zero),
                         V(new Vector3(o[j].x * s1, y1, o[j].y * s1), N(i, j), Vector2.zero), V(new Vector3(o[i].x * s1, y1, o[i].y * s1), N(i, i), Vector2.zero));
                }
                var ring = new Vector2[n];
                for (int i = 0; i < n; i++) ring[i] = o[i] * s1;
                Zip(ring, Circle(boreR + boreCh, boreSegs), y1, Vector3.up);
                Bore(boreR, boreCh, y0, y1, true, false, boreSegs);
            }

            /// <summary>Hex (or square) nut body with the 30-degree chamfer crown: the corners are cut by a cone,
            /// which leaves the classic arcs on each flat. innerTop/innerBottom: radius of the round opening the
            /// caps are zipped to (a bore mouth or what sits on top), 0 = solid cap.</summary>
            public void Crown(int sides, float R, float rc, float y0, float y1, bool cTop, bool cBottom, float innerTop, float innerBottom, int count, float rot = 0)
            {
                var o = PolyRing(sides, R, count, rot);
                int n = o.Length;
                Outline(o, out var en, out var sm, out var vn, out _);
                const float tan30 = 0.57735f, sn = 0.5f, cs = 0.8660254f;
                var yt = new float[n]; var yb = new float[n]; var cin = new Vector2[n]; var dir = new Vector2[n];
                float h = y1 - y0;
                for (int i = 0; i < n; i++)
                {
                    float r = o[i].magnitude, cut = Mathf.Max(0, r - rc) * tan30;
                    dir[i] = o[i] / r;
                    cin[i] = dir[i] * Mathf.Min(r, rc);
                    yt[i] = cTop ? y1 - Mathf.Min(cut, h * 0.4f) : y1;
                    yb[i] = cBottom ? y0 + Mathf.Min(cut, h * 0.4f) : y0;
                }
                Vector3 WN(int edge, int vtx) { var w = sm[vtx] ? vn[vtx] : en[edge]; return new Vector3(w.x, 0, w.y); }
                for (int i = 0; i < n; i++)
                {
                    int j = (i + 1) % n;
                    Quad(V(new Vector3(o[i].x, yb[i], o[i].y), WN(i, i), Vector2.zero), V(new Vector3(o[j].x, yb[j], o[j].y), WN(i, j), Vector2.zero),
                         V(new Vector3(o[j].x, yt[j], o[j].y), WN(i, j), Vector2.zero), V(new Vector3(o[i].x, yt[i], o[i].y), WN(i, i), Vector2.zero));
                    if (cTop)
                        Quad(V(new Vector3(o[i].x, yt[i], o[i].y), new Vector3(dir[i].x * sn, cs, dir[i].y * sn), Vector2.zero),
                             V(new Vector3(o[j].x, yt[j], o[j].y), new Vector3(dir[j].x * sn, cs, dir[j].y * sn), Vector2.zero),
                             V(new Vector3(cin[j].x, y1, cin[j].y), new Vector3(dir[j].x * sn, cs, dir[j].y * sn), Vector2.zero),
                             V(new Vector3(cin[i].x, y1, cin[i].y), new Vector3(dir[i].x * sn, cs, dir[i].y * sn), Vector2.zero));
                    if (cBottom)
                        Quad(V(new Vector3(o[i].x, yb[i], o[i].y), new Vector3(dir[i].x * sn, -cs, dir[i].y * sn), Vector2.zero),
                             V(new Vector3(o[j].x, yb[j], o[j].y), new Vector3(dir[j].x * sn, -cs, dir[j].y * sn), Vector2.zero),
                             V(new Vector3(cin[j].x, y0, cin[j].y), new Vector3(dir[j].x * sn, -cs, dir[j].y * sn), Vector2.zero),
                             V(new Vector3(cin[i].x, y0, cin[i].y), new Vector3(dir[i].x * sn, -cs, dir[i].y * sn), Vector2.zero));
                }
                int ic = Mathf.Max(16, n / 2);
                if (innerTop > 0) Zip(cTop ? cin : o, Circle(innerTop, ic), y1, Vector3.up); else Fan(cTop ? cin : o, y1, Vector3.up);
                if (innerBottom > 0) Zip(cBottom ? cin : o, Circle(innerBottom, ic), y0, Vector3.down); else Fan(cBottom ? cin : o, y0, Vector3.down);
            }

            static Vector2[] Circle(float r, int count)
            {
                var o = new Vector2[count];
                for (int i = 0; i < count; i++) { float a = Tau * i / count; o[i] = new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * r; }
                return o;
            }

            void Fan(IList<Vector2> ring, float y, Vector3 nrm)
            {
                var c = Vector2.zero;
                foreach (var q in ring) c += q;
                c /= ring.Count;
                int ci = V(new Vector3(c.x, y, c.y), nrm, c);
                var ids = new int[ring.Count];
                for (int i = 0; i < ring.Count; i++) ids[i] = V(new Vector3(ring[i].x, y, ring[i].y), nrm, ring[i]);
                for (int i = 0; i < ring.Count; i++) Tri(ci, ids[i], ids[(i + 1) % ring.Count]);
            }

            /// <summary>Triangulates the flat ring between two closed loops around the axis (an outline and a
            /// circle with any point counts) by walking both in angle order.</summary>
            void Zip(IList<Vector2> a, IList<Vector2> b, float y, Vector3 nrm)
            {
                int na = a.Count, nb = b.Count;
                int[] ia = new int[na], ib = new int[nb];
                for (int i = 0; i < na; i++) ia[i] = V(new Vector3(a[i].x, y, a[i].y), nrm, a[i]);
                for (int i = 0; i < nb; i++) ib[i] = V(new Vector3(b[i].x, y, b[i].y), nrm, b[i]);
                float Ang(Vector2 p) => Mathf.Repeat(Mathf.Atan2(p.y, p.x), Tau);
                int sa = 0, sb = 0;
                for (int i = 1; i < na; i++) if (Ang(a[i]) < Ang(a[sa])) sa = i;
                for (int i = 1; i < nb; i++) if (Ang(b[i]) < Ang(b[sb])) sb = i;
                // unwrapped angles from each start, ending at the start + one turn
                float UA(int k) => k >= na ? Ang(a[sa]) + Tau : Ang(a[(sa + k) % na]) + (Ang(a[(sa + k) % na]) < Ang(a[sa]) - 1e-5f ? Tau : 0);
                float UB(int k) => k >= nb ? Ang(b[sb]) + Tau : Ang(b[(sb + k) % nb]) + (Ang(b[(sb + k) % nb]) < Ang(b[sb]) - 1e-5f ? Tau : 0);
                int x = 0, z = 0;
                while (x < na || z < nb)
                {
                    bool advA = z >= nb || (x < na && UA(x + 1) <= UB(z + 1));
                    if (advA) { Tri(ia[(sa + x) % na], ia[(sa + x + 1) % na], ib[(sb + z) % nb]); x++; }
                    else { Tri(ia[(sa + x) % na], ib[(sb + z + 1) % nb], ib[(sb + z) % nb]); z++; }
                }
            }

            // ---------------------------------------------------------- boxes and tubes

            public void Box(Vector3 c, Vector3 size)
            {
                var h = size * 0.5f;
                for (int axis = 0; axis < 3; axis++)
                    for (int sg = -1; sg <= 1; sg += 2)
                    {
                        var n = Vector3.zero; n[axis] = sg;
                        var u = Vector3.zero; u[(axis + 1) % 3] = h[(axis + 1) % 3];
                        var v = Vector3.zero; v[(axis + 2) % 3] = h[(axis + 2) % 3];
                        var cc = c + n * h[axis];
                        Quad(V(cc - u - v, n, new Vector2(0, 0)), V(cc + u - v, n, new Vector2(1, 0)), V(cc + u + v, n, new Vector2(1, 1)), V(cc - u + v, n, new Vector2(0, 1)));
                    }
            }

            /// <summary>Box with 45-degree chamfers on every edge and corner (the robots' ChamferBox language):
            /// the faces stay flat and crisp, the chamfers catch the light.</summary>
            public void BBox(Vector3 c, Vector3 size, float bev)
            {
                var h = size * 0.5f;
                float b = Mathf.Min(bev, 0.45f * Mathf.Min(size.x, Mathf.Min(size.y, size.z)));
                if (b < 1e-5f) { Box(c, size); return; }
                var ins = h - Vector3.one * b;
                Vector3 E(int i) { var e = Vector3.zero; e[i] = 1; return e; }
                for (int a = 0; a < 3; a++)
                    for (int sa = -1; sa <= 1; sa += 2)
                    {
                        int u = (a + 1) % 3, w = (a + 2) % 3;
                        var fc = c + E(a) * sa * h[a];
                        var n = E(a) * sa;
                        Quad(V(fc - E(u) * ins[u] - E(w) * ins[w], n, Vector2.zero), V(fc + E(u) * ins[u] - E(w) * ins[w], n, Vector2.zero),
                             V(fc + E(u) * ins[u] + E(w) * ins[w], n, Vector2.zero), V(fc - E(u) * ins[u] + E(w) * ins[w], n, Vector2.zero));
                    }
                for (int a = 0; a < 3; a++)
                    for (int bb = a + 1; bb < 3; bb++)
                    {
                        int w = 3 - a - bb;
                        for (int sa = -1; sa <= 1; sa += 2)
                            for (int sb = -1; sb <= 1; sb += 2)
                            {
                                var n = (E(a) * sa + E(bb) * sb).normalized;
                                var p1 = c + E(a) * sa * h[a] + E(bb) * sb * ins[bb];
                                var p2 = c + E(a) * sa * ins[a] + E(bb) * sb * h[bb];
                                var ww = E(w) * ins[w];
                                Quad(V(p1 - ww, n, Vector2.zero), V(p1 + ww, n, Vector2.zero), V(p2 + ww, n, Vector2.zero), V(p2 - ww, n, Vector2.zero));
                            }
                    }
                for (int sx = -1; sx <= 1; sx += 2)
                    for (int sy = -1; sy <= 1; sy += 2)
                        for (int sz = -1; sz <= 1; sz += 2)
                        {
                            var n = new Vector3(sx, sy, sz).normalized;
                            Tri(V(c + new Vector3(sx * h.x, sy * ins.y, sz * ins.z), n, Vector2.zero),
                                V(c + new Vector3(sx * ins.x, sy * h.y, sz * ins.z), n, Vector2.zero),
                                V(c + new Vector3(sx * ins.x, sy * ins.y, sz * h.z), n, Vector2.zero));
                        }
            }

            /// <summary>Sweeps a circle along a polyline (parallel-transport frames), smooth all round.</summary>
            public void Tube(IList<Vector3> path, float radius, int sides, bool caps = true)
            {
                int count = path.Count;
                if (count < 2) return;
                var tan = new Vector3[count];
                for (int k = 0; k < count; k++) tan[k] = (path[Mathf.Min(k + 1, count - 1)] - path[Mathf.Max(k - 1, 0)]).normalized;
                var normal = Vector3.Cross(tan[0], Vector3.up);
                if (normal.sqrMagnitude < 1e-6f) normal = Vector3.Cross(tan[0], Vector3.right);
                normal.Normalize();
                var rings = new List<int[]>();
                var first = new Vector3[sides]; var last = new Vector3[sides];
                float dist = 0;
                for (int k = 0; k < count; k++)
                {
                    var t = tan[k];
                    normal = (normal - t * Vector3.Dot(normal, t)).normalized;
                    var bin = Vector3.Cross(t, normal);
                    if (k > 0) dist += Vector3.Distance(path[k], path[k - 1]);
                    var ring = new int[sides + 1];
                    for (int s = 0; s <= sides; s++)
                    {
                        float a = Tau * s / sides;
                        var dir = normal * Mathf.Cos(a) + bin * Mathf.Sin(a);
                        var p = path[k] + dir * radius;
                        ring[s] = V(p, dir, new Vector2((float)s / sides, dist));
                        if (s < sides) { if (k == 0) first[s] = p; if (k == count - 1) last[s] = p; }
                    }
                    rings.Add(ring);
                }
                for (int k = 0; k < count - 1; k++)
                    for (int s = 0; s < sides; s++) Quad(rings[k][s], rings[k][s + 1], rings[k + 1][s + 1], rings[k + 1][s]);
                if (!caps) return;
                for (int e = 0; e < 2; e++)
                {
                    int k = e == 0 ? 0 : count - 1;
                    var t = e == 0 ? -tan[k] : tan[k];
                    var pts = e == 0 ? first : last;
                    int ci = V(path[k], t, Vector2.zero);
                    var ids = new int[sides];
                    for (int s = 0; s < sides; s++) ids[s] = V(pts[s], t, Vector2.zero);
                    for (int s = 0; s < sides; s++) Tri(ci, ids[s], ids[(s + 1) % sides]);
                }
            }

            // ---------------------------------------------------------- finish

            /// <summary>Mirrors Z into Unity space (as MeshLibrary.Load), winds every triangle to face along its
            /// vertex normals, drops degenerate ones, centres the bounds like the shared meshes and adds the
            /// skin coordinates.</summary>
            public MeshLibrary.Entry Finish(string name)
            {
                for (int i = 0; i < _v.Count; i++)
                {
                    _v[i] = new Vector3(_v[i].x, _v[i].y, -_v[i].z);
                    _n[i] = new Vector3(_n[i].x, _n[i].y, -_n[i].z);
                }
                var subs = new List<(int slot, List<int> tris)>();
                foreach (var kv in _t)
                {
                    var src = kv.Value;
                    var dst = new List<int>(src.Count);
                    for (int k = 0; k < src.Count; k += 3)
                    {
                        int a = src[k], b = src[k + 1], c = src[k + 2];
                        var face = Vector3.Cross(_v[b] - _v[a], _v[c] - _v[a]);
                        if (face.sqrMagnitude < 1e-16f) continue;
                        if (Vector3.Dot(face, _n[a] + _n[b] + _n[c]) < 0) (b, c) = (c, b);
                        dst.Add(a); dst.Add(b); dst.Add(c);
                    }
                    if (dst.Count > 0) subs.Add((kv.Key, dst));
                }
                var mesh = new Mesh { name = name };
                if (_v.Count > 65000) mesh.indexFormat = IndexFormat.UInt32;
                Vector3 mn = Vector3.positiveInfinity, mx = Vector3.negativeInfinity;
                foreach (var s in subs) foreach (var i in s.tris) { mn = Vector3.Min(mn, _v[i]); mx = Vector3.Max(mx, _v[i]); }
                var off = -(mn + mx) * 0.5f;
                for (int i = 0; i < _v.Count; i++) _v[i] += off;
                mesh.SetVertices(_v);
                mesh.SetNormals(_n);
                mesh.SetUVs(0, _uv);
                mesh.SetColors(_c);
                mesh.subMeshCount = subs.Count;
                var slots = new int[subs.Count];
                for (int s = 0; s < subs.Count; s++)
                {
                    mesh.SetTriangles(subs[s].tris, s, false);
                    slots[s] = subs[s].slot;
                }
                mesh.RecalculateBounds();
                SkinUV.Apply(mesh, slots);
                mesh.UploadMeshData(false);
                return new MeshLibrary.Entry { mesh = mesh, slots = slots, bounds = mesh.bounds };
            }
        }
    }
}
