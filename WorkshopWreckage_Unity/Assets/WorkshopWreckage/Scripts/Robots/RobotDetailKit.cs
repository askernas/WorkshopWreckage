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
            b.RoundedBox(new Vector3(0, H / 2, 0), size, Mathf.Min(0.04f, H * 0.14f), M(shell, paint), 3);
            int nTop = Mathf.Max(3, Mathf.RoundToInt(W / 0.06f));
            for (int k = 0; k < nTop; k++)
            {
                float t = Mathf.Lerp(-W / 2 + 0.04f, W / 2 - 0.04f, k / (float)(nTop - 1));
                b.Rivet(new Vector3(t, H - 0.022f, front), new Vector3(0, 0, -1), 0.0075f, M(shell, Gunmetal));
                b.Rivet(new Vector3(t, 0.022f, front), new Vector3(0, 0, -1), 0.0075f, M(shell, Gunmetal));
            }
            foreach (var x in new[] { -W / 2 + 0.022f, W / 2 - 0.022f })
                for (int k = 1; k < 4; k++) b.Rivet(new Vector3(x, 0.022f + k * (H - 0.044f) / 4, front), new Vector3(0, 0, -1), 0.0075f, M(shell, Gunmetal));
            // eyes
            float eyeY = mouth ? H * 0.56f : H * 0.52f;
            var eyeXs = eyeCount == 1 ? new[] { 0f } : new[] { -W * 0.236f, W * 0.236f };
            string EyePart(float x) => x > 0 && eyeRight != null ? eyeRight : eyeL;
            foreach (var x in eyeXs)
            {
                b.Lathe(new Vector3(x, eyeY, front + 0.002f), Ring(eyeR * 1.54f, eyeR * 1.04f, 0.03f), M(EyePart(x), Gunmetal), FwdRot, 28);
                b.Lathe(new Vector3(x, eyeY, front - 0.004f), Ring(eyeR * 1.17f, eyeR * 0.96f, 0.03f), M(EyePart(x), Brass), FwdRot, 28);
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
            Done(b, pivot);

            foreach (var x in eyeXs)
                if (Has(EyePart(x)))
                    eyes.Add(Separate(pivot, "G_eye", lb => lb.Lathe(new Vector3(x, eyeY, front - 0.004f), Dome(eyeR, eyeR * 0.46f, 8), EyeOffMat, FwdRot, 28)));
            if (beacon != null && Has(beacon))
                beacons.Add(Separate(pivot, "G_beacon", lb => lb.Lathe(new Vector3(0, H + 0.016f, 0), Dome(0.044f, 0.05f, 8), BeaconOffMat, null, 24)));
        }

        /// <summary>Segmented arm hanging from its shoulder pivot (Godot-local, scale k):
        /// red shoulder disc with brass hub, paint shoulder cap, upper arm, gear-toothed
        /// elbow, cuffed forearm, wrist joint and a red pincer claw opening sideways.</summary>
        protected void DetailedArm(Batch b, float s, string part, string clawPart, Material paint, Material paintDeep, Material red, float k = 1f)
        {
            Vector3 V(float x, float y, float z) => new Vector3(x, y, z) * k;
            b.Lathe(V(s * 0.012f, 0, 0), Disc(0.062f * k, 0.04f * k, 0.01f * k), M(part, red), SideRot(s), 22);
            b.Lathe(V(s * 0.052f, 0, 0), Dome(0.022f * k, 0.012f * k), M(part, Brass), SideRot(s), 12);
            b.RoundedBox(V(-s * 0.012f, -0.03f, 0), V(0.085f, 0.09f, 0.11f), 0.022f * k, M(part, paint), 2);
            b.RoundedBox(V(0, -0.105f, 0), V(0.08f, 0.1f, 0.085f), 0.02f * k, M(part, paint), 2);
            b.Lathe(V(-0.047f, -0.168f, 0), Disc(0.04f * k, 0.094f * k, 0.006f * k), M(part, Gunmetal), RightRot, 16);
            for (int t = 0; t < 10; t++)
            {
                float a = t * Mathf.PI / 5;
                b.Box(V(0, -0.168f + Mathf.Sin(a) * 0.044f, Mathf.Cos(a) * 0.044f), V(0.06f, 0.013f, 0.013f), M(part, Gunmetal), false, Batch.R(Vector3.right, a));
            }
            b.Lathe(V(s * 0.047f, -0.168f, 0), Dome(0.016f * k, 0.009f * k), M(part, Brass), SideRot(s), 10);
            b.RoundedBox(V(0, -0.245f, 0), V(0.095f, 0.12f, 0.1f), 0.024f * k, M(part, paint), 2);
            b.RoundedBox(V(0, -0.205f, 0), V(0.1f, 0.018f, 0.104f), 0.006f * k, M(part, paintDeep), 2);
            b.Rivet(V(s * 0.048f, -0.25f, 0), new Vector3(s, 0, 0), 0.008f * k, M(part, Gunmetal));
            b.Lathe(V(-0.036f, -0.315f, 0), Disc(0.032f * k, 0.072f * k, 0.005f * k), M(clawPart, Gunmetal), RightRot, 14);
            foreach (var f in new[] { -1f, 1f })
            {
                b.RoundedBox(V(f * 0.032f, -0.36f, 0), V(0.03f, 0.085f, 0.055f), 0.01f * k, M(clawPart, red), 2, Batch.R(Vector3.forward, f * 0.32f));
                b.RoundedBox(V(f * 0.044f, -0.418f, 0), V(0.027f, 0.055f, 0.052f), 0.009f * k, M(clawPart, red), 2, Batch.R(Vector3.forward, -f * 0.4f));
                b.RoundedBox(V(f * 0.027f, -0.42f, 0), V(0.008f, 0.04f, 0.04f), 0.003f * k, M(clawPart, Dark), 1, Batch.R(Vector3.forward, -f * 0.4f));
                b.Rivet(V(f * 0.046f, -0.345f, -0.028f), new Vector3(0, 0, -1), 0.006f * k, M(clawPart, Brass));
            }
        }
    }
}
