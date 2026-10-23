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
            var n = outward.normalized;
            var rot = Quaternion.FromToRotation(Vector3.up, Batch.G(n));
            b.Lathe(c - n * 0.001f, new[] { new Vector2(r * 1.45f, 0), new Vector2(r * 1.45f, r * 0.22f), new Vector2(0, r * 0.22f) }, m, rot, 10);
            b.Lathe(c + n * (r * 0.2f), new[] { new Vector2(r, 0), new Vector2(r, r * 0.5f), new Vector2(r * 0.72f, r * 0.74f), new Vector2(0, r * 0.74f) }, m, rot, 6, true);
        }

        /// <summary>A bolted plate on one face of an axis-aligned box (Godot-local centre and size,
        /// normal one of ±X, ±Y, ±Z): a seam rectangle inset from the edges (keep it past the
        /// bevel) and hex bolts just inside it, at the corners and spaced along the sides.</summary>
        protected void PanelFrame(Batch b, string part, Vector3 c, Vector3 size, Vector3 normal, float inset, float boltR = 0.0065f, float spacing = 0.075f)
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
            for (int k = 0; k < 4; k++) SeamLine(b, part, P(k, hu, hv), P(k + 1, hu, hv), n);
            float bu = hu - 0.012f, bv = hv - 0.012f;
            if (bu < 0.006f || bv < 0.006f) return;
            var m = M(part, Gunmetal);
            for (int k = 0; k < 4; k++)
            {
                Vector3 p0 = P(k, bu, bv), p1 = P(k + 1, bu, bv);
                int cnt = Mathf.Max(1, Mathf.RoundToInt((p1 - p0).magnitude / spacing));
                for (int i = 0; i < cnt; i++) Bolt(b, Vector3.Lerp(p0, p1, i / (float)cnt), n, boltR, m);
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
            bool antennasBehindEars, bool mouth, float earScale = 1f)
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
            foreach (var (s, ear) in new[] { (-1f, earL), (1f, earR) })
            {
                b.Lathe(new Vector3(s * (W / 2 - 0.005f), earY, 0), Disc(er * 0.72f, 0.012f, 0.003f), M(ear, Gunmetal), SideRot(s), 20);
                b.Lathe(new Vector3(s * (W / 2 + 0.006f), earY, 0), Disc(er, 0.04f, 0.01f), M(ear, ToyRed), SideRot(s), 26);
                b.Lathe(new Vector3(s * (W / 2 + 0.046f), earY, 0), Disc(er * 0.4f, 0.01f, 0.003f), M(ear, Gunmetal), SideRot(s), 16);
                b.Lathe(new Vector3(s * (W / 2 + 0.056f), earY, 0), Dome(er * 0.23f, 0.01f), M(ear, Brass), SideRot(s), 12);
            }
            // antennas
            foreach (var (s, ant) in new[] { (-1f, antL), (1f, antR) })
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

        /// <summary>Segmented arm hanging from its shoulder pivot (Godot-local, scale k), chunky
        /// like the concept sheets: a big red shoulder disc with a gunmetal collar and brass hub,
        /// a banded upper arm, a gear-toothed elbow with red joint discs and brass caps, a wide
        /// riveted gauntlet with cuff bands, a wrist axle, and a red pincer claw (palm block,
        /// knuckled fingers with dark pads and brass pivots) opening sideways.</summary>
        protected void DetailedArm(Batch b, float s, string part, string clawPart, Material paint, Material paintDeep, Material red, float k = 1f)
        {
            Vector3 V(float x, float y, float z) => new Vector3(x, y, z) * k;
            Material P = M(part, paint), PD = M(part, paintDeep), G = M(part, Gunmetal), Br = M(part, Brass), Rd = M(part, red);
            Material CG = M(clawPart, Gunmetal), CR = M(clawPart, red), CB = M(clawPart, Brass), CD = M(clawPart, Dark);
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
            // forearm: a wide gauntlet with cuff bands and rivets
            b.RoundedBox(V(0, -0.238f, 0), V(0.114f, 0.12f, 0.114f), 0.028f * k, P, 2);
            b.RoundedBox(V(0, -0.195f, 0), V(0.12f, 0.02f, 0.12f), 0.007f * k, PD, 2);
            b.RoundedBox(V(0, -0.288f, 0), V(0.12f, 0.016f, 0.12f), 0.006f * k, PD, 2);
            foreach (var z in new[] { -0.03f, 0.03f }) Bolt(b, V(s * 0.058f, -0.24f, z), new Vector3(s, 0, 0), 0.007f * k, G);
            foreach (var x in new[] { -0.032f, 0.032f }) Bolt(b, V(x, -0.24f, -0.058f), new Vector3(0, 0, -1), 0.0065f * k, G);
            // wrist axle and palm block
            b.Lathe(V(-0.04f, -0.31f, 0), Disc(0.032f * k, 0.08f * k, 0.005f * k), CG, RightRot, 16);
            foreach (var side in new[] { -1f, 1f }) b.Lathe(V(side * 0.04f, -0.31f, 0), Dome(0.014f * k, 0.008f * k), CB, SideRot(side), 10);
            b.RoundedBox(V(0, -0.336f, 0), V(0.088f, 0.03f, 0.07f), 0.01f * k, CG, 2);
            // pincer: two knuckled red fingers
            foreach (var f in new[] { -1f, 1f })
            {
                b.RoundedBox(V(f * 0.036f, -0.378f, 0), V(0.034f, 0.08f, 0.06f), 0.011f * k, CR, 2, Batch.R(Vector3.forward, f * 0.3f));
                b.RoundedBox(V(f * 0.047f, -0.437f, 0), V(0.03f, 0.06f, 0.056f), 0.01f * k, CR, 2, Batch.R(Vector3.forward, -f * 0.42f));
                b.RoundedBox(V(f * 0.03f, -0.44f, 0), V(0.009f, 0.044f, 0.044f), 0.003f * k, CD, 1, Batch.R(Vector3.forward, -f * 0.42f));
                b.Rivet(V(f * 0.04f, -0.362f, -0.031f), new Vector3(0, 0, -1), 0.007f * k, CB);
                b.Rivet(V(f * 0.046f, -0.41f, -0.029f), new Vector3(0, 0, -1), 0.006f * k, CB);
            }
        }
    }
}
