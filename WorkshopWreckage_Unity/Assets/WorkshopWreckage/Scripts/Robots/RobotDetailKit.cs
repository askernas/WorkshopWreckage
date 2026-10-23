using System.Collections.Generic;
using UnityEngine;

namespace WorkshopWreckage
{
    /// <summary>Shared detailing kit for the toy robots (the visual language of the
    /// reference sheets): weathered toy paints, lathe profiles for discs / bezels /
    /// domes / balls, slatted grilles, the family head (rounded riveted box, bezel
    /// eyes, red ear discs, antennas with red balls, optional beacon and mouth) and
    /// the segmented arm with gear elbow and red pincer claw.</summary>
    public abstract partial class RobotModel
    {
        // ------------------------------------------------------------ materials
        protected static Material ToyYellow => EnvMaterials.ToyPaint(new Color(0.8f, 0.55f, 0.12f));
        protected static Material ToyYellowDeep => EnvMaterials.ToyPaint(new Color(0.68f, 0.45f, 0.09f));
        protected static Material ToyGreen => EnvMaterials.ToyPaint(new Color(0.33f, 0.38f, 0.18f));
        protected static Material ToyGreenDeep => EnvMaterials.ToyPaint(new Color(0.26f, 0.3f, 0.14f));
        protected static Material ToyRed => EnvMaterials.ToyPaint(new Color(0.68f, 0.1f, 0.06f));
        protected static Material ToyRedDeep => EnvMaterials.ToyPaint(new Color(0.52f, 0.07f, 0.05f));
        protected static Material Gunmetal => EnvMaterials.Plain(new Color(0.11f, 0.1f, 0.095f), 0.75f, 0.42f);

        // ------------------------------------------------------------ rotations
        protected static readonly Quaternion FwdRot = Quaternion.FromToRotation(Vector3.up, Vector3.forward);   // lathe axis out of the front
        protected static readonly Quaternion BackRot = Quaternion.FromToRotation(Vector3.up, Vector3.back);
        protected static readonly Quaternion DownRot = Quaternion.FromToRotation(Vector3.up, Vector3.down);
        protected static readonly Quaternion RightRot = Quaternion.FromToRotation(Vector3.up, Vector3.right);
        protected static Quaternion SideRot(float s) => Quaternion.FromToRotation(Vector3.up, new Vector3(s, 0, 0));

        // ------------------------------------------------------------ lathe profiles (radius, height)
        protected static Vector2[] Disc(float r, float h, float bevel) => new[]
        {
            new Vector2(0, 0), new Vector2(r - bevel, 0), new Vector2(r, bevel), new Vector2(r, h - bevel), new Vector2(r - bevel, h), new Vector2(0, h),
        };

        protected static Vector2[] Ring(float rOut, float rIn, float h) => new[]
        {
            new Vector2(rIn, 0), new Vector2(rOut * 0.97f, 0), new Vector2(rOut, h * 0.25f), new Vector2(rOut, h * 0.75f), new Vector2(rOut * 0.95f, h), new Vector2(rIn, h), new Vector2(rIn, 0),
        };

        protected static Vector2[] Dome(float r, float h, int steps = 7)
        {
            var p = new List<Vector2>();
            for (int i = 0; i <= steps; i++)
            {
                float a = i / (float)steps * Mathf.PI / 2;
                p.Add(new Vector2(Mathf.Cos(a) * r, Mathf.Sin(a) * h));
            }
            return p.ToArray();
        }

        protected static Vector2[] Ball(float r, int steps = 10)
        {
            var p = new List<Vector2>();
            for (int i = 0; i <= steps; i++)
            {
                float a = -Mathf.PI / 2 + i / (float)steps * Mathf.PI;
                p.Add(new Vector2(Mathf.Cos(a) * r, Mathf.Sin(a) * r + r));
            }
            return p.ToArray();
        }

        // ------------------------------------------------------------ seams and bolts
        static Material _seam;
        /// <summary>The dark line of a panel seam (a thin, slightly proud strip reads as a groove).</summary>
        protected static Material SeamMat => _seam ??= EnvMaterials.Plain(new Color(0.06f, 0.05f, 0.04f), 0.2f, 0.25f);

        /// <summary>A panel seam on a surface from a to b (Godot-local), lying flat on the surface
        /// with the given normal; `lift` keeps it above curved (faceted) surfaces.</summary>
        protected void SeamLine(Batch b, string part, Vector3 a, Vector3 bb, Vector3 normal, float w = 0.0042f, float lift = 0.0006f)
        {
            var m = M(part, SeamMat);
            var d = bb - a;
            float len = d.magnitude;
            if (m == null || len < 1e-4f) return;
            var n = normal.normalized;
            var rot = Quaternion.LookRotation(Batch.G(d / len), Batch.G(n));
            b.Box((a + bb) / 2 + n * lift, new Vector3(w, 0.0024f, len + w), m, false, rot);
        }

        /// <summary>A hex bolt on a washer (the concept sheets' fastener), same call shape as
        /// Batch.Rivet: Godot-local point on the surface, outward direction, size, material.</summary>
        protected void Bolt(Batch b, Vector3 c, Vector3 outward, float r, Material m)
        {
            if (m == null) return;
            m = BoltSkin(m);
            var n = outward.normalized;
            var rot = Quaternion.FromToRotation(Vector3.up, Batch.G(n));
            b.Lathe(c - n * 0.001f, new[] { new Vector2(r * 1.45f, 0), new Vector2(r * 1.45f, r * 0.22f), new Vector2(0, r * 0.22f) }, m, rot, 10);
            b.Lathe(c + n * (r * 0.2f), new[] { new Vector2(r, 0), new Vector2(r, r * 0.5f), new Vector2(r * 0.72f, r * 0.74f), new Vector2(0, r * 0.74f) }, m, rot, 6, true);
        }

        static Material _stain;
        /// <summary>A rust stain layer (WWStain: multiplies the lit paint under it).</summary>
        protected static Material StainMat
        {
            get
            {
                if (_stain == null)
                {
                    _stain = new Material(Resources.Load<Shader>("WWStain")) { name = "RustStain" };
                    _stain.SetColor("_Color", new Color(0.84f, 0.68f, 0.52f));   // orange-brown rust, not red
                }
                return _stain;
            }
        }

        /// <summary>A stable 0..1 hash of a model-space point: wear lands irregularly, but the same
        /// on every build.</summary>
        protected static float Hash(Vector3 p, float salt = 0)
        {
            float h = Mathf.Sin(Vector3.Dot(p, new Vector3(127.1f, 311.7f, 74.7f)) + salt * 17.3f) * 43758.5453f;
            return h - Mathf.Floor(h);
        }

        /// <summary>A rust streak running down a vertical surface from p (Godot-local): four
        /// overlapping stain strips from the same source, wide and short to narrow and long, so the
        /// multiplied tint is darkest where it bleeds out of the bolt or seam and fades to its tail.
        /// `stain` replaces the rust tint (a robot's own grease or grime).</summary>
        protected void RustStreak(Batch b, string part, Vector3 p, Vector3 normal, float length, float w = 0.016f, Material stain = null)
        {
            if (assembling) return;   // stains are not drawn on ghosts
            var n = normal.normalized;
            var m = M(part, stain ?? StainMat);
            if (m == null || length < 0.012f || Mathf.Abs(n.y) > 0.3f) return;
            var rot = Quaternion.LookRotation(Vector3.down, Batch.G(n));
            float[] wf = { 1.1f, 0.85f, 0.62f, 0.4f }, lf = { 0.22f, 0.5f, 0.78f, 1f };
            float drift = (Hash(p, 9) - 0.5f) * w * 0.5f;   // tails wander a little
            var side = Vector3.Cross(Vector3.up, n).normalized;
            for (int i = 0; i < 4; i++)
            {
                float len = length * lf[i];
                b.Box(p + Vector3.down * (len / 2) + side * (drift * i / 3f) + n * 0.0006f, new Vector3(w * wf[i], 0.001f, len), m, false, rot);
            }
        }

        static Vector3 CatmullRom(Vector3 p0, Vector3 p1, Vector3 p2, Vector3 p3, float t) =>
            0.5f * (2 * p1 + (p2 - p0) * t + (2 * p0 - 5 * p1 + 4 * p2 - p3) * t * t + (3 * p1 - p0 - 3 * p2 + p3) * t * t * t);

        /// <summary>A hose or pipe along a smooth curve through Godot-local points, with ferrules
        /// (`ends`) where it plugs in. Keep it within one pivot so it never tears when animating.</summary>
        protected void Pipe(Batch b, string part, Vector3[] pts, float r, Material m, Material ends = null, int perSpan = 5)
        {
            var mm = M(part, m);
            if (mm == null || pts.Length < 2) return;
            var path = new List<Vector3>();
            for (int i = 0; i < pts.Length - 1; i++)
                for (int s = 0; s < perSpan; s++)
                    path.Add(CatmullRom(pts[Mathf.Max(0, i - 1)], pts[i], pts[i + 1], pts[Mathf.Min(pts.Length - 1, i + 2)], s / (float)perSpan));
            path.Add(pts[^1]);
            for (int k = 0; k < path.Count - 1; k++)
            {
                var d = path[k + 1] - path[k];
                float len = d.magnitude;
                if (len < 1e-4f) continue;
                b.Cylinder((path[k] + path[k + 1]) / 2, r, len + r * 0.7f, mm, Quaternion.FromToRotation(Vector3.up, Batch.G(d / len)), 10);
            }
            var me = M(part, ends);
            if (me == null) return;
            foreach (var (p, q) in new[] { (path[0], path[1]), (path[^1], path[^2]) })
            {
                var d = (q - p).normalized;
                b.Cylinder(p + d * r * 1.2f, r * 1.5f, r * 2.6f, me, Quaternion.FromToRotation(Vector3.up, Batch.G(d)), 10);
            }
        }

        /// <summary>A bolted plate on one face of an axis-aligned box (Godot-local centre and size,
        /// normal one of ±X, ±Y, ±Z): a seam rectangle inset from the edges (keep it past the
        /// bevel) and hex bolts just inside it, at the corners and spaced along the sides.</summary>
        protected void PanelFrame(Batch b, string part, Vector3 c, Vector3 size, Vector3 normal, float inset, float boltR = 0.0065f, float spacing = 0.075f, float seamW = 0.0042f, float maxStreak = 9f)
        {
            var n = normal.normalized;
            var an = new Vector3(Mathf.Abs(n.x), Mathf.Abs(n.y), Mathf.Abs(n.z));
            var u = an.x > 0.5f ? Vector3.forward : Vector3.right;
            var v = Vector3.Cross(an, u);
            v = new Vector3(Mathf.Abs(v.x), Mathf.Abs(v.y), Mathf.Abs(v.z));
            float hu = Vector3.Dot(size, u) / 2 - inset, hv = Vector3.Dot(size, v) / 2 - inset;
            if (hu < 0.012f || hv < 0.012f) return;
            var fc = c + n * (Vector3.Dot(size, an) / 2);
            float[] su = { -1, 1, 1, -1 }, sv = { -1, -1, 1, 1 };
            Vector3 P(int k, float du, float dv) => fc + u * (su[k % 4] * du) + v * (sv[k % 4] * dv);
            for (int k = 0; k < 4; k++) SeamLine(b, part, P(k, hu, hv), P(k + 1, hu, hv), n, seamW);
            float bu = hu - 0.012f, bv = hv - 0.012f;
            if (bu < 0.006f || bv < 0.006f) return;
            var m = M(part, Gunmetal);
            bool wall = an.y < 0.5f;
            float floorY = c.y - size.y / 2 + inset * 0.7f;   // streaks stop short of the bottom bevel
            for (int k = 0; k < 4; k++)
            {
                Vector3 p0 = P(k, bu, bv), p1 = P(k + 1, bu, bv);
                int cnt = Mathf.Max(1, Mathf.RoundToInt((p1 - p0).magnitude / spacing));
                for (int i = 0; i < cnt; i++)
                {
                    var p = Vector3.Lerp(p0, p1, i / (float)cnt);
                    Bolt(b, p, n, boltR, m);
                    if (wall && Hash(p) < 0.5f) RustStreak(b, part, p, n, Mathf.Min(maxStreak, (p.y - floorY) * (0.35f + 0.6f * Hash(p, 1))), boltR * 2.6f);
                }
            }
            if (!wall) return;
            for (int i = 0; i < 2; i++)   // rust bleeding out of the top seam
            {
                float h = Hash(fc, i + 2);
                if (h > 0.7f) continue;
                var p = fc + u * (Mathf.Lerp(-hu, hu, Hash(fc, i + 5)) * 0.85f) + v * hv;
                RustStreak(b, part, p, n, Mathf.Min(maxStreak, (p.y - floorY) * (0.25f + 0.5f * h)), 0.02f);
            }
        }

        // 7-segment glyphs (a top, b top right, c bottom right, d bottom, e bottom left, f top left, g middle)
        static readonly Dictionary<char, string> Glyphs = new()
        {
            ['A'] = "abcefg", ['-'] = "g", ['0'] = "abcdef", ['1'] = "bc", ['2'] = "abged", ['3'] = "abgcd",
            ['4'] = "fgbc", ['5'] = "afgcd", ['6'] = "afgedc", ['7'] = "abc", ['8'] = "abcdefg", ['9'] = "abcdfg",
        };

        /// <summary>Stencilled lettering (blocky 7-segment glyphs) on an axis-aligned face: text
        /// runs along `right` from `origin` (the first glyph's bottom-left), letters stand along
        /// `up`; `normal` = right x up points out of the face. Worn dark paint.</summary>
        protected void Stencil(Batch b, string part, Vector3 origin, Vector3 right, Vector3 up, Vector3 normal, float h, string text)
        {
            var m = M(part, Dark);
            if (m == null) return;
            float w = h * 0.58f, t = h * 0.15f;
            Vector3 Abs(Vector3 v) => new Vector3(Mathf.Abs(v.x), Mathf.Abs(v.y), Mathf.Abs(v.z));
            for (int i = 0; i < text.Length; i++)
            {
                if (!Glyphs.TryGetValue(text[i], out var segs)) continue;
                var o = origin + right * (i * h * 0.82f);
                foreach (var sg in segs)
                {
                    // segment centre (in glyph units) and whether it is horizontal
                    var (cx, cy, horiz) = sg switch
                    {
                        'a' => (w / 2, h - t / 2, true), 'g' => (w / 2, h / 2, true), 'd' => (w / 2, t / 2, true),
                        'b' => (w - t / 2, h * 0.75f, false), 'c' => (w - t / 2, h * 0.25f, false),
                        'f' => (t / 2, h * 0.75f, false), _ => (t / 2, h * 0.25f, false),
                    };
                    float sx = horiz ? w : t, sy = horiz ? t : h / 2 + t / 2;
                    if (Hash(o + up * cy + right * cx, 4) < 0.18f) sx *= 0.6f;   // worn stencil: a few segments partly gone
                    b.Box(o + right * cx + up * cy + normal * 0.0012f, Abs(right) * sx + Abs(up) * sy + Abs(normal) * 0.002f, m);
                }
            }
        }

        // ------------------------------------------------------------ details

        /// <summary>A recessed slatted grille on a face (Godot-local centre on the
        /// surface; zSign -1 = front face, +1 = back face).</summary>
        protected void Grille(Batch b, string part, Vector3 c, float w, float h, int slats, bool vertical, float zSign)
        {
            b.RoundedBox(c, new Vector3(w, h, 0.012f), 0.004f, M(part, Gunmetal), 2);
            for (int k = 0; k < slats; k++)
            {
                float t = (k + 0.5f) / slats - 0.5f;
                var p = c + (vertical ? new Vector3(t * w * 0.9f, 0, 0) : new Vector3(0, t * h * 0.85f, 0)) + new Vector3(0, 0, zSign * 0.006f);
                b.RoundedBox(p, vertical ? new Vector3(w * 0.045f, h * 0.8f, 0.006f) : new Vector3(w * 0.9f, h * 0.06f, 0.006f), 0.002f, M(part, Brass), 1);
            }
        }

        /// <summary>Builds geometry into its own child (so its material can switch, e.g. eyes).</summary>
        protected Renderer Separate(Transform parent, string name, System.Action<Batch> build)
        {
            var b = new Batch();
            build(b);
            var holder = new GameObject(name).transform;
            holder.SetParent(parent, false);
            b.Build(holder, "G_" + name, true);
            return holder.GetComponentInChildren<MeshRenderer>();
        }

        /// <summary>The family head (around a pivot at its bottom centre): rounded riveted
        /// box, one or two lamp eyes in thick dark bezels with brass rings, optional
        /// slatted mouth, red ear discs with collars and brass hubs, two antennas with red
        /// balls (rising behind the ears, or from the top corners), optional beacon dome,
        /// slatted vent on the back.</summary>
        protected void DetailedHead(Transform pivot, Material paint, Vector3 size, int eyeCount, float eyeR,
            string shell, string eyeL, string eyeRight, string antL, string antR, string earL, string earR, string beacon,
            bool antennasBehindEars, bool mouth, float earScale = 1f, bool familyEars = false)
        {
            var b = new Batch();
            float W = size.x, H = size.y, D = size.z, front = -D / 2;
            float bev = Mathf.Min(0.058f, H * 0.19f), inset = bev * 0.8f;   // soft toy-like edges
            b.RoundedBox(new Vector3(0, H / 2, 0), size, bev, M(shell, paint), 4);
            int nTop = Mathf.Max(3, Mathf.RoundToInt(W / 0.06f));
            for (int k = 0; k < nTop; k++)
            {
                float t = Mathf.Lerp(-W / 2 + inset + 0.02f, W / 2 - inset - 0.02f, k / (float)(nTop - 1));
                Bolt(b, new Vector3(t, H - inset, front), new Vector3(0, 0, -1), 0.0075f, M(shell, Gunmetal));
                if (Hash(new Vector3(t, H, W)) < 0.35f) RustStreak(b, shell, new Vector3(t, H - inset, front), new Vector3(0, 0, -1), H * (0.12f + 0.2f * Hash(new Vector3(t, H, W), 3)), 0.018f);
                Bolt(b, new Vector3(t, inset, front), new Vector3(0, 0, -1), 0.0075f, M(shell, Gunmetal));
            }
            foreach (var x in new[] { -W / 2 + inset, W / 2 - inset })
                for (int k = 1; k < 4; k++) Bolt(b, new Vector3(x, inset + k * (H - 2 * inset) / 4, front), new Vector3(0, 0, -1), 0.0075f, M(shell, Gunmetal));
            // eyes
            float eyeY = mouth ? H * 0.56f : H * 0.52f;
            var eyeXs = eyeCount == 1 ? new[] { 0f } : new[] { -W * 0.236f, W * 0.236f };
            string EyePart(float x) => x > 0 && eyeRight != null ? eyeRight : eyeL;
            foreach (var x in eyeXs)
            {
                b.Lathe(new Vector3(x, eyeY, front + 0.01f), Ring(eyeR * 1.54f, eyeR * 1.04f, 0.042f), M(EyePart(x), Gunmetal), FwdRot, 32);
                b.Lathe(new Vector3(x, eyeY, front - 0.012f), Ring(eyeR * 1.2f, eyeR * 0.96f, 0.03f), M(EyePart(x), Brass), FwdRot, 32);
                b.Lathe(new Vector3(x, eyeY, front - 0.004f), Ring(eyeR * 0.98f, eyeR * 0.7f, 0.012f), M(EyePart(x), Dark), FwdRot, 28);   // dark glass round the lamp
                if (eyeCount == 1)   // the big single eye has an extra bolted outer ring
                    for (int k = 0; k < 8; k++)
                    {
                        float a = k * Mathf.PI / 4;
                        b.Rivet(new Vector3(x + Mathf.Cos(a) * eyeR * 1.32f, eyeY + Mathf.Sin(a) * eyeR * 1.32f, front - 0.028f), new Vector3(0, 0, -1), 0.006f, M(EyePart(x), Brass));
                    }
            }
            if (mouth) Grille(b, shell, new Vector3(0, H * 0.2f, front - 0.001f), W * 0.32f, 0.04f, 7, true, -1);
            // ears
            float earY = H * 0.5f, er = Mathf.Min(0.07f, H * 0.22f) * earScale;
            if (familyEars) FamilyEarsAndAntennas(b, earL, antL, antR, W, H, earY);   // the canonical cone housings and antennas
            else foreach (var (s, ear) in new[] { (-1f, earL), (1f, earR) })
            {
                b.Lathe(new Vector3(s * (W / 2 - 0.005f), earY, 0), Disc(er * 0.72f, 0.012f, 0.003f), M(ear, Gunmetal), SideRot(s), 20);
                b.Lathe(new Vector3(s * (W / 2 + 0.006f), earY, 0), Disc(er, 0.04f, 0.01f), M(ear, ToyRed), SideRot(s), 26);
                b.Lathe(new Vector3(s * (W / 2 + 0.046f), earY, 0), Disc(er * 0.4f, 0.01f, 0.003f), M(ear, Gunmetal), SideRot(s), 16);
                b.Lathe(new Vector3(s * (W / 2 + 0.056f), earY, 0), Dome(er * 0.23f, 0.01f), M(ear, Brass), SideRot(s), 12);
            }
            // antennas
            if (!familyEars) foreach (var (s, ant) in new[] { (-1f, antL), (1f, antR) })
            {
                var basePos = antennasBehindEars ? new Vector3(s * (W / 2 + 0.02f), earY + er * 0.8f, 0.03f) : new Vector3(s * W * 0.36f, H - 0.004f, 0.02f);
                b.Lathe(basePos, Disc(0.014f, 0.02f, 0.003f), M(ant, Gunmetal), null, 12);
                float rodH = antennasBehindEars ? H + 0.05f - basePos.y : 0.1f;
                b.Lathe(basePos + new Vector3(0, 0.02f, 0), Disc(0.0065f, rodH, 0.001f), M(ant, Gunmetal), null, 8);
                b.Lathe(basePos + new Vector3(0, 0.015f + rodH, 0), Ball(0.027f), M(ant, ToyRed), null, 16);
            }
            if (beacon != null) b.Lathe(new Vector3(0, H - 0.002f, 0), Disc(0.055f, 0.02f, 0.005f), M(beacon, Gunmetal), null, 22);
            // back vent and slot
            Grille(b, shell, new Vector3(0, H * 0.56f, -front + 0.001f), W * 0.53f, H * 0.34f, 7, false, 1);
            b.RoundedBox(new Vector3(0, H * 0.27f, -front + 0.004f), new Vector3(0.05f, 0.012f, 0.006f), 0.003f, M(shell, Dark), 1);
            // bolted plates on the sides (framing the ears), the top and the back
            var hc = new Vector3(0, H / 2, 0);
            foreach (var n in new[] { Vector3.left, Vector3.right, Vector3.up, new Vector3(0, 0, 1) }) PanelFrame(b, shell, hc, size, n, bev + 0.012f);
            Done(b, pivot);

            foreach (var x in eyeXs)
                if (Has(EyePart(x)))
                    eyes.Add(Separate(pivot, "G_eye", lb => lb.Lathe(new Vector3(x, eyeY, front - 0.006f), Dome(eyeR * 0.74f, eyeR * 0.42f, 8), EyeOffMat, FwdRot, 28)));
            if (beacon != null && Has(beacon))
                beacons.Add(Separate(pivot, "G_beacon", lb => lb.Lathe(new Vector3(0, H + 0.016f, 0), Dome(0.044f, 0.05f, 8), BeaconOffMat, null, 24)));
        }

        /// <summary>The family arm (scale k) from its shoulder pivot, chunky like the concept
        /// sheets, in two pieces: the shoulder (big red disc, gunmetal collar, brass hub), a banded
        /// upper arm and the geared elbow stay on the arm pivot; the riveted gauntlet, the wrist and
        /// a big red pincer (scale k * clawScale: palm block, two hooked fingers curling inwards with
        /// dark teeth and brass pivots) hang from an elbow pivot that rests bent forward, the
        /// "ready" stance (see PoseElbows).</summary>
        protected void DetailedArm(Transform arm, float s, string part, string clawPart, Material paint, Material paintDeep, Material red, float k = 1f, float clawScale = 1.35f)
        {
            Vector3 V(float x, float y, float z) => new Vector3(x, y, z) * k;
            Material P = M(part, paint), PD = M(part, paintDeep), G = M(part, Gunmetal), Br = M(part, Brass), Rd = M(part, red);
            Material CG = M(clawPart, Gunmetal), CR = M(clawPart, red), CB = M(clawPart, Brass), CD = M(clawPart, Dark);
            var b = new Batch();
            // shoulder
            b.Lathe(V(-s * 0.012f, 0, 0), Disc(0.05f * k, 0.034f * k, 0.006f * k), G, SideRot(s), 20);
            b.Lathe(V(s * 0.02f, 0, 0), Disc(0.074f * k, 0.042f * k, 0.012f * k), Rd, SideRot(s), 26);
            b.Lathe(V(s * 0.062f, 0, 0), Disc(0.036f * k, 0.008f * k, 0.002f * k), G, SideRot(s), 16);
            b.Lathe(V(s * 0.07f, 0, 0), Dome(0.024f * k, 0.014f * k), Br, SideRot(s), 14);
            // upper arm
            b.RoundedBox(V(0, -0.085f, 0), V(0.1f, 0.1f, 0.104f), 0.026f * k, P, 2);
            b.RoundedBox(V(0, -0.05f, 0), V(0.106f, 0.02f, 0.11f), 0.008f * k, PD, 2);
            foreach (var x in new[] { -0.028f, 0.028f }) Bolt(b, V(x, -0.095f, -0.052f), new Vector3(0, 0, -1), 0.0065f * k, G);
            // elbow: gunmetal axle with gear teeth, red joint discs and brass caps on both sides
            b.Lathe(V(-0.056f, -0.162f, 0), Disc(0.042f * k, 0.112f * k, 0.006f * k), G, RightRot, 18);
            for (int t = 0; t < 12; t++)
            {
                float a = t * Mathf.PI / 6;
                b.Box(V(0, -0.162f + Mathf.Sin(a) * 0.046f, Mathf.Cos(a) * 0.046f), V(0.07f, 0.013f, 0.013f), G, false, Batch.R(Vector3.right, a));
            }
            foreach (var side in new[] { -1f, 1f })
            {
                b.Lathe(V(side * 0.056f, -0.162f, 0), Disc(0.036f * k, 0.012f * k, 0.003f * k), Rd, SideRot(side), 18);
                b.Lathe(V(side * 0.068f, -0.162f, 0), Dome(0.016f * k, 0.01f * k), Br, SideRot(side), 10);
            }
            Done(b, arm);

            // ---- forearm and claw, on the elbow pivot (coordinates relative to the elbow axle)
            var elbow = Elbow(arm, V(0, -0.162f, 0), s);
            var f = new Batch();
            Vector3 E(float x, float y, float z) => V(x, y + 0.162f, z);
            float cs = clawScale, kc = k * cs;
            var wrist = E(0, -0.31f, 0);
            // the claw is twisted round the forearm so its opening shows from the front and the side
            var tw = Quaternion.AngleAxis(s * 40f, Vector3.up);       // Godot-space points
            var twU = Quaternion.AngleAxis(-s * 40f, Vector3.up);     // the same twist for Unity-space rotations
            Vector3 C(float x, float y, float z) => wrist + tw * ((E(x, y, z) - wrist) * cs);   // claw points, scaled about the wrist
            Vector3 CV(float x, float y, float z) => new Vector3(x, y, z) * kc;           // claw sizes
            // a hose on the back, from the elbow into the gauntlet
            Pipe(f, part, new[] { E(s * 0.03f, -0.13f, 0.07f), E(s * 0.034f, -0.19f, 0.082f), E(s * 0.026f, -0.252f, 0.058f) }, 0.0075f * k, Rubber, Brass);
            // forearm: a wide gauntlet with cuff bands and bolts
            f.RoundedBox(E(0, -0.238f, 0), V(0.118f, 0.12f, 0.118f), 0.028f * k, P, 2);
            f.RoundedBox(E(0, -0.195f, 0), V(0.124f, 0.02f, 0.124f), 0.007f * k, PD, 2);
            f.RoundedBox(E(0, -0.288f, 0), V(0.124f, 0.016f, 0.124f), 0.006f * k, PD, 2);
            foreach (var z in new[] { -0.03f, 0.03f }) Bolt(f, E(s * 0.06f, -0.24f, z), new Vector3(s, 0, 0), 0.007f * k, G);
            foreach (var x in new[] { -0.032f, 0.032f }) Bolt(f, E(x, -0.24f, -0.06f), new Vector3(0, 0, -1), 0.0065f * k, G);
            // the hand (wrist, claw) on a telescopic forearm end (extended only with a reach upgrade)
            var hg = TelescopeHand(elbow, E(0, -0.296f, 0), part, 0.036f * k, 0.027f * k, paint);
            var hb = new Batch();
            // wrist axle
            hb.Lathe(E(-0.042f, -0.31f, 0), Disc(0.034f * k, 0.084f * k, 0.005f * k), CG, RightRot, 16);
            foreach (var side in new[] { -1f, 1f }) hb.Lathe(E(side * 0.042f, -0.31f, 0), Dome(0.015f * k, 0.008f * k), CB, SideRot(side), 10);
            // claw: palm block and two hooked fingers (base out, knuckle, tip curling in), dark teeth
            hb.RoundedBox(C(0, -0.334f, 0), CV(0.094f, 0.032f, 0.072f), 0.011f * kc, CG, 2, twU);
            hb.Lathe(C(0, -0.352f, -0.036f), Disc(0.012f * kc, 0.006f * kc, 0.002f * kc), CB, twU * BackRot, 10);
            foreach (var fs in new[] { -1f, 1f })
            {
                hb.RoundedBox(C(fs * 0.036f, -0.375f, 0), CV(0.034f, 0.07f, 0.062f), 0.011f * kc, CR, 2, twU * Batch.R(Vector3.forward, fs * 0.42f));   // base, opening out
                hb.RoundedBox(C(fs * 0.056f, -0.425f, 0), CV(0.03f, 0.05f, 0.058f), 0.01f * kc, CR, 2, twU * Batch.R(Vector3.forward, fs * 0.05f));     // knuckle
                hb.RoundedBox(C(fs * 0.044f, -0.47f, 0), CV(0.026f, 0.05f, 0.054f), 0.009f * kc, CR, 2, twU * Batch.R(Vector3.forward, -fs * 0.6f));    // tip, curling in
                for (int t = 0; t < 3; t++)   // teeth on the inner edge
                    hb.Box(C(fs * (0.034f - t * 0.002f), -0.415f - t * 0.022f, 0), CV(0.012f, 0.008f, 0.04f), CD, false, twU * Batch.R(Vector3.forward, fs * 0.2f));
                hb.Rivet(C(fs * 0.04f, -0.36f, -0.032f), tw * new Vector3(0, 0, -1), 0.0075f * kc, CB);
                hb.Rivet(C(fs * 0.056f, -0.425f, -0.03f), tw * new Vector3(0, 0, -1), 0.0065f * kc, CB);
            }
            Done(hb, hg);
            Done(f, elbow);
        }
    }
}
