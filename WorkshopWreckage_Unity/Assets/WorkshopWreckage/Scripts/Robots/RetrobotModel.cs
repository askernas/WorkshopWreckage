using System.Collections.Generic;
using UnityEngine;

namespace WorkshopWreckage
{
    /// <summary>Retrobot after resources/retrobot.png, modelled from its proportions
    /// (front / side / back): a weathered mustard-yellow pressed-tin toy. Big rounded
    /// head with riveted edges, two round lamp eyes in thick bezels, a slatted mouth,
    /// red ear discs with antennas rising behind them to red balls, a red beacon dome,
    /// a rounded torso with a slatted chest grille and a slotted plate, red shoulder
    /// discs, segmented arms with gear joints and red pincer claws, a dark mechanical
    /// pelvis, thighs, red knee discs, piston shins and big chunky feet with toe vents,
    /// rubber soles and red heels. Slatted vents on the back of head and torso.
    /// Parts: head, torso, left arm, right arm, leg assembly.</summary>
    public class RetrobotModel : RobotModel
    {
        public const string Head = "head", Torso = "torso", ArmL = "left arm", ArmR = "right arm", Legs = "leg assembly";
        public Transform upper, upperGeo, armL, armR, legL, legR;
        public override float ModelHeight => 1.06f;
        static Material Yellow => EnvMaterials.ToyPaint(new Color(0.8f, 0.55f, 0.12f));
        static Material YellowDeep => EnvMaterials.ToyPaint(new Color(0.68f, 0.45f, 0.09f));
        static new Material Red => EnvMaterials.ToyPaint(new Color(0.68f, 0.1f, 0.06f));
        static Material Gunmetal => EnvMaterials.Plain(new Color(0.11f, 0.1f, 0.095f), 0.75f, 0.42f);
        const float HipY = 0.40f, ShoulderY = 0.62f, NeckY = 0.68f, ShoulderX = 0.222f, LegX = 0.15f;
        static readonly Quaternion Fwd = Quaternion.FromToRotation(Vector3.up, Vector3.forward);   // lathe axis -> facing out of the front
        static readonly Quaternion Back = Quaternion.FromToRotation(Vector3.up, Vector3.back);

        protected override void CreatePivots()
        {
            legL = Pivot("LegL", body, new Vector3(-LegX, HipY, 0));
            legR = Pivot("LegR", body, new Vector3(LegX, HipY, 0));
            upper = Pivot("Upper", body, new Vector3(0, HipY, 0));          // bends forward at the hips
            upperGeo = Pivot("UpperGeo", upper, new Vector3(0, -HipY, 0));
            armL = Pivot("ArmL", upperGeo, new Vector3(-ShoulderX, ShoulderY, 0));
            armR = Pivot("ArmR", upperGeo, new Vector3(ShoulderX, ShoulderY, 0));
            head = Pivot("Head", upperGeo, new Vector3(0, NeckY, 0));
            carry = Pivot("Carry", upperGeo, new Vector3(0, 0.47f, 0.4f));  // between the claws with the arms held forward
            Mount(head, Head, Vector3.up, 0.05f);
            Mount(armL, ArmL, Vector3.left);
            Mount(armR, ArmR, Vector3.right);
            Mount(legL, Legs, Vector3.down, 0.045f);
            Mount(legR, Legs, Vector3.down, 0.045f);
        }

        protected override Vector3? ClampPoint => new Vector3(0, 0.5f, Has(Torso) ? 0.15f : 0.035f);

        /// <summary>Without the torso: the bare spine with shoulder and hip bars.</summary>
        protected override void BuildFrame()
        {
            if (Has(Torso)) return;
            var b = new Batch();
            b.RoundedBox(new Vector3(0, (HipY + NeckY) / 2, 0), new Vector3(0.05f, NeckY - HipY, 0.05f), 0.01f, Gunmetal, 2);
            b.RoundedBox(new Vector3(0, ShoulderY, 0), new Vector3(ShoulderX * 2, 0.035f, 0.035f), 0.008f, Gunmetal, 2);
            b.RoundedBox(new Vector3(0, HipY, 0), new Vector3(LegX * 2 + 0.05f, 0.045f, 0.06f), 0.01f, Gunmetal, 2);
            b.RoundedBox(new Vector3(0, 0.53f, -0.03f), new Vector3(0.09f, 0.07f, 0.035f), 0.008f, Brass, 2);   // the little drive box on the spine
            b.Build(upperGeo, "G_frame", true);
        }

        static Quaternion Side(float s) => Quaternion.FromToRotation(Vector3.up, new Vector3(s, 0, 0));

        static Vector2[] Disc(float r, float h, float bevel) => new[]
        {
            new Vector2(0, 0), new Vector2(r - bevel, 0), new Vector2(r, bevel), new Vector2(r, h - bevel), new Vector2(r - bevel, h), new Vector2(0, h),
        };

        static Vector2[] Ring(float rOut, float rIn, float h) => new[]
        {
            new Vector2(rIn, 0), new Vector2(rOut * 0.97f, 0), new Vector2(rOut, h * 0.25f), new Vector2(rOut, h * 0.75f), new Vector2(rOut * 0.95f, h), new Vector2(rIn, h), new Vector2(rIn, 0),
        };

        static Vector2[] Dome(float r, float h, int steps = 7)
        {
            var p = new List<Vector2>();
            for (int i = 0; i <= steps; i++)
            {
                float a = i / (float)steps * Mathf.PI / 2;
                p.Add(new Vector2(Mathf.Cos(a) * r, Mathf.Sin(a) * h));
            }
            return p.ToArray();
        }

        static Vector2[] Ball(float r, int steps = 10)
        {
            var p = new List<Vector2>();
            for (int i = 0; i <= steps; i++)
            {
                float a = -Mathf.PI / 2 + i / (float)steps * Mathf.PI;
                p.Add(new Vector2(Mathf.Cos(a) * r, Mathf.Sin(a) * r + r));
            }
            return p.ToArray();
        }

        /// <summary>A recessed slatted grille on a face (Godot-local centre on the surface,
        /// z = outward sign: -1 front, +1 back).</summary>
        void Grille(Batch b, string part, Vector3 c, float w, float h, int slats, bool vertical, float zSign)
        {
            b.RoundedBox(c, new Vector3(w, h, 0.012f), 0.004f, M(part, Gunmetal), 2);
            for (int k = 0; k < slats; k++)
            {
                float t = (k + 0.5f) / slats - 0.5f;
                var p = c + (vertical ? new Vector3(t * w * 0.9f, 0, 0) : new Vector3(0, t * h * 0.85f, 0)) + new Vector3(0, 0, zSign * 0.006f);
                b.RoundedBox(p, vertical ? new Vector3(w * 0.045f, h * 0.8f, 0.006f) : new Vector3(w * 0.9f, h * 0.06f, 0.006f), 0.002f, M(part, Brass), 1);
            }
        }

        protected override void BuildGeometry()
        {
            BuildTorso();
            foreach (var (leg, s) in new[] { (legL, -1f), (legR, 1f) }) BuildLeg(leg, s);
            foreach (var (arm, s, part) in new[] { (armL, -1f, ArmL), (armR, 1f, ArmR) }) BuildArm(arm, s, part);
            BuildHead();
        }

        void BuildTorso()
        {
            var b = new Batch();
            string P = Torso;
            // pelvis: dark mechanical block with hip axle and brass bolts
            b.RoundedBox(new Vector3(0, 0.415f, 0), new Vector3(0.21f, 0.07f, 0.16f), 0.02f, M(P, Gunmetal), 2);
            b.Lathe(new Vector3(-0.18f, 0.405f, 0), Disc(0.03f, 0.36f, 0.006f), M(P, Dark), Quaternion.FromToRotation(Vector3.up, Vector3.right), 12);   // hip axle
            b.Lathe(new Vector3(-0.2f, ShoulderY, 0), Disc(0.026f, 0.4f, 0.005f), M(P, Gunmetal), Quaternion.FromToRotation(Vector3.up, Vector3.right), 12);   // shoulder axle
            foreach (var x in new[] { -0.07f, 0f, 0.07f }) b.Rivet(new Vector3(x, 0.42f, -0.08f), new Vector3(0, 0, -1), 0.009f, M(P, Brass));
            // body
            b.RoundedBox(new Vector3(0, 0.54f, 0), new Vector3(0.27f, 0.25f, 0.22f), 0.035f, M(P, Yellow), 3);
            b.RoundedBox(new Vector3(0, 0.665f, 0), new Vector3(0.2f, 0.02f, 0.16f), 0.008f, M(P, YellowDeep), 2);   // collar
            Grille(b, P, new Vector3(0, 0.585f, -0.112f), 0.15f, 0.075f, 6, false, -1);
            b.RoundedBox(new Vector3(0, 0.468f, -0.113f), new Vector3(0.12f, 0.05f, 0.012f), 0.006f, M(P, YellowDeep), 2);   // lower plate
            b.RoundedBox(new Vector3(0, 0.468f, -0.12f), new Vector3(0.065f, 0.012f, 0.006f), 0.003f, M(P, Dark), 1);       // slot
            foreach (var x in new[] { -0.115f, 0.115f })
                foreach (var y in new[] { 0.44f, 0.64f })
                    b.Rivet(new Vector3(x, y, -0.11f), new Vector3(0, 0, -1), 0.008f, M(P, Gunmetal));
            foreach (var x in new[] { -0.052f, 0.052f }) b.Rivet(new Vector3(x, 0.468f, -0.119f), new Vector3(0, 0, -1), 0.006f, M(P, Gunmetal));
            foreach (var y in new[] { 0.45f, 0.54f, 0.63f })
                foreach (var s in new[] { -1f, 1f }) b.Rivet(new Vector3(s * 0.135f, y, -0.05f), new Vector3(s, 0, 0), 0.007f, M(P, Gunmetal));
            Grille(b, P, new Vector3(0, 0.56f, 0.112f), 0.15f, 0.08f, 6, false, 1);   // back vent
            // neck: stacked dark rings
            for (int k = 0; k < 3; k++) b.Lathe(new Vector3(0, 0.672f + k * 0.012f, 0), Disc(0.05f - k * 0.004f, 0.012f, 0.003f), M(P, Gunmetal), null, 16);
            Done(b, upperGeo);
        }

        void BuildLeg(Transform leg, float s)
        {
            var b = new Batch();
            string P = Legs;
            var axisX = Quaternion.FromToRotation(Vector3.up, Vector3.right);
            b.Lathe(new Vector3(-0.055f, -0.02f, 0), Disc(0.045f, 0.11f, 0.01f), M(P, Gunmetal), axisX, 16);   // hip joint (axle along X)
            b.RoundedBox(new Vector3(0, -0.085f, 0), new Vector3(0.115f, 0.09f, 0.11f), 0.022f, M(P, Yellow), 2);                    // thigh
            foreach (var z in new[] { -0.05f, 0.05f }) b.Rivet(new Vector3(s * 0.05f, -0.085f, z * 0.5f), new Vector3(s, 0, 0), 0.006f, M(P, Gunmetal));
            // knee: dark joint with a red disc on the outside and a brass cap
            b.Lathe(new Vector3(-0.045f, -0.135f, 0), Disc(0.042f, 0.09f, 0.008f), M(P, Gunmetal), axisX, 16);
            b.Lathe(new Vector3(s * 0.045f, -0.135f, 0), Disc(0.04f, 0.022f, 0.006f), M(P, Red), Side(s), 18);
            b.Lathe(new Vector3(s * 0.067f, -0.135f, 0), Dome(0.016f, 0.01f), M(P, Brass), Side(s), 10);
            // knee disc on the front, as in the reference
            b.Lathe(new Vector3(0, -0.135f, -0.045f), Disc(0.033f, 0.018f, 0.005f), M(P, Red), Quaternion.FromToRotation(Vector3.up, Vector3.forward), 18);
            b.Lathe(new Vector3(0, -0.135f, -0.063f), Dome(0.012f, 0.008f), M(P, Brass), Quaternion.FromToRotation(Vector3.up, Vector3.forward), 10);
            // shin: yellow front plate, pistons and springs behind
            b.RoundedBox(new Vector3(0, -0.215f, -0.02f), new Vector3(0.1f, 0.1f, 0.06f), 0.016f, M(P, Yellow), 2);
            foreach (var dx in new[] { -0.022f, 0.022f })
            {
                b.Lathe(new Vector3(dx, -0.27f, 0.022f), Disc(0.012f, 0.12f, 0.002f), M(P, Gunmetal), null, 10);
                b.Lathe(new Vector3(dx, -0.2f, 0.022f), Disc(0.016f, 0.02f, 0.003f), M(P, Brass), null, 10);
            }
            b.Lathe(new Vector3(-0.04f, -0.29f, 0), Disc(0.03f, 0.08f, 0.006f), M(P, Gunmetal), axisX, 14);   // ankle
            // foot: big chunky shoe with a toe vent, rubber sole and red heel
            b.RoundedBox(new Vector3(0, -0.335f, -0.035f), new Vector3(0.13f, 0.075f, 0.235f), 0.025f, M(P, Yellow), 3);
            b.RoundedBox(new Vector3(0, -0.302f, -0.06f), new Vector3(0.1f, 0.02f, 0.13f), 0.008f, M(P, YellowDeep), 2);
            Grille(b, P, new Vector3(0, -0.338f, -0.153f), 0.08f, 0.035f, 4, false, -1);
            b.RoundedBox(new Vector3(0, -0.39f, -0.035f), new Vector3(0.14f, 0.022f, 0.245f), 0.008f, M(P, Rubber), 2);
            b.RoundedBox(new Vector3(0, -0.345f, 0.075f), new Vector3(0.12f, 0.05f, 0.03f), 0.01f, M(P, Red), 2);
            foreach (var x in new[] { -0.05f, 0.05f }) b.Rivet(new Vector3(x, -0.31f, -0.15f), new Vector3(0, 0, -1), 0.006f, M(P, Gunmetal));
            Done(b, leg);
        }

        void BuildArm(Transform arm, float s, string part)
        {
            var b = new Batch();
            var axisX = Quaternion.FromToRotation(Vector3.up, Vector3.right);
            // shoulder: big red disc facing out, brass hub, yellow cap
            b.Lathe(new Vector3(s * 0.012f, 0, 0), Disc(0.062f, 0.04f, 0.01f), M(part, Red), Side(s), 22);
            b.Lathe(new Vector3(s * 0.052f, 0, 0), Dome(0.022f, 0.012f), M(part, Brass), Side(s), 12);
            b.RoundedBox(new Vector3(-s * 0.012f, -0.03f, 0), new Vector3(0.085f, 0.09f, 0.11f), 0.022f, M(part, Yellow), 2);
            // upper arm, elbow gear, forearm
            b.RoundedBox(new Vector3(0, -0.105f, 0), new Vector3(0.08f, 0.1f, 0.085f), 0.02f, M(part, Yellow), 2);
            b.Lathe(new Vector3(-0.047f, -0.168f, 0), Disc(0.04f, 0.094f, 0.006f), M(part, Gunmetal), axisX, 16);
            for (int k = 0; k < 10; k++)   // gear teeth around the elbow
            {
                float a = k * Mathf.PI / 5;
                b.Box(new Vector3(0, -0.168f + Mathf.Sin(a) * 0.044f, Mathf.Cos(a) * 0.044f), new Vector3(0.06f, 0.013f, 0.013f), M(part, Gunmetal), false, Batch.R(Vector3.right, a));
            }
            b.Lathe(new Vector3(s * 0.047f, -0.168f, 0), Dome(0.016f, 0.009f), M(part, Brass), Side(s), 10);
            b.RoundedBox(new Vector3(0, -0.245f, 0), new Vector3(0.095f, 0.12f, 0.1f), 0.024f, M(part, Yellow), 2);
            b.RoundedBox(new Vector3(0, -0.205f, 0), new Vector3(0.1f, 0.018f, 0.104f), 0.006f, M(part, YellowDeep), 2);   // cuff band
            b.Rivet(new Vector3(s * 0.048f, -0.25f, 0), new Vector3(s, 0, 0), 0.008f, M(part, Gunmetal));
            b.Lathe(new Vector3(-0.036f, -0.315f, 0), Disc(0.032f, 0.072f, 0.005f), M(part, Gunmetal), axisX, 14);   // wrist
            // pincer: two curved red fingers opening sideways, dark inner pads
            foreach (var f in new[] { -1f, 1f })
            {
                b.RoundedBox(new Vector3(f * 0.032f, -0.36f, 0), new Vector3(0.03f, 0.085f, 0.055f), 0.01f, M(part, Red), 2, Batch.R(Vector3.forward, f * 0.32f));
                b.RoundedBox(new Vector3(f * 0.044f, -0.418f, 0), new Vector3(0.027f, 0.055f, 0.052f), 0.009f, M(part, Red), 2, Batch.R(Vector3.forward, -f * 0.4f));
                b.RoundedBox(new Vector3(f * 0.027f, -0.42f, 0), new Vector3(0.008f, 0.04f, 0.04f), 0.003f, M(part, Dark), 1, Batch.R(Vector3.forward, -f * 0.4f));
                b.Rivet(new Vector3(f * 0.046f, -0.345f, -0.028f), new Vector3(0, 0, -1), 0.006f, M(part, Brass));
            }
            Done(b, arm);
        }

        void BuildHead()
        {
            var b = new Batch();
            string P = Head;
            const float W = 0.36f, H = 0.32f, D = 0.32f;
            float front = -D / 2;
            b.RoundedBox(new Vector3(0, H / 2, 0), new Vector3(W, H, D), 0.04f, M(P, Yellow), 3);
            // riveted seam around the face
            for (int k = 0; k < 6; k++)
            {
                float t = -0.14f + k * 0.056f;
                b.Rivet(new Vector3(t, H - 0.022f, front), new Vector3(0, 0, -1), 0.0075f, M(P, Gunmetal));
                b.Rivet(new Vector3(t, 0.022f, front), new Vector3(0, 0, -1), 0.0075f, M(P, Gunmetal));
            }
            foreach (var x in new[] { -W / 2 + 0.022f, W / 2 - 0.022f })
                for (int k = 1; k < 4; k++) b.Rivet(new Vector3(x, 0.022f + k * 0.07f, front), new Vector3(0, 0, -1), 0.0075f, M(P, Gunmetal));
            // eyes: thick dark bezels with brass inner rings (lenses are separate, they light up)
            foreach (var x in new[] { -0.085f, 0.085f })
            {
                b.Lathe(new Vector3(x, 0.175f, front + 0.002f), Ring(0.074f, 0.05f, 0.03f), M(P, Gunmetal), Fwd, 28);
                b.Lathe(new Vector3(x, 0.175f, front - 0.004f), Ring(0.056f, 0.046f, 0.03f), M(P, Brass), Fwd, 28);
            }
            // mouth grille with vertical slats
            Grille(b, P, new Vector3(0, 0.065f, front - 0.001f), 0.115f, 0.04f, 7, true, -1);
            // ears: red discs with bevelled rims, dark collars and brass hubs
            foreach (var s in new[] { -1f, 1f })
            {
                b.Lathe(new Vector3(s * (W / 2 - 0.005f), 0.16f, 0), Disc(0.05f, 0.012f, 0.003f), M(P, Gunmetal), Side(s), 20);
                b.Lathe(new Vector3(s * (W / 2 + 0.006f), 0.16f, 0), Disc(0.07f, 0.04f, 0.01f), M(P, Red), Side(s), 26);
                b.Lathe(new Vector3(s * (W / 2 + 0.046f), 0.16f, 0), Disc(0.028f, 0.01f, 0.003f), M(P, Gunmetal), Side(s), 16);
                b.Lathe(new Vector3(s * (W / 2 + 0.056f), 0.16f, 0), Dome(0.016f, 0.01f), M(P, Brass), Side(s), 12);
                // antenna rising behind the ear to a red ball
                b.Lathe(new Vector3(s * 0.2f, 0.215f, 0.03f), Disc(0.014f, 0.02f, 0.003f), M(P, Gunmetal), null, 12);
                b.Lathe(new Vector3(s * 0.2f, 0.235f, 0.03f), Disc(0.0065f, 0.14f, 0.001f), M(P, Gunmetal), null, 8);
                b.Lathe(new Vector3(s * 0.2f, 0.37f, 0.03f), Ball(0.027f), M(P, Red), null, 16);
            }
            // beacon on top: dark base ring and a red dome (the dome lights up)
            b.Lathe(new Vector3(0, H - 0.002f, 0), Disc(0.055f, 0.02f, 0.005f), M(P, Gunmetal), null, 22);
            // back: big slatted vent and a small slot
            Grille(b, P, new Vector3(0, 0.18f, -front + 0.001f), 0.19f, 0.11f, 7, false, 1);
            b.RoundedBox(new Vector3(0, 0.085f, -front + 0.004f), new Vector3(0.05f, 0.012f, 0.006f), 0.003f, M(P, Dark), 1);
            Done(b, head);

            if (!Has(P)) return;
            // lenses and beacon dome as their own renderers so they can switch on at activation
            foreach (var x in new[] { -0.085f, 0.085f })
                eyes.Add(Separate("G_eye", lb => lb.Lathe(new Vector3(x, 0.175f, front - 0.004f), Dome(0.048f, 0.022f, 8), EyeOffMat, Fwd, 28)));
            beacons.Add(Separate("G_beacon", lb => lb.Lathe(new Vector3(0, H + 0.016f, 0), Dome(0.044f, 0.05f, 8), BeaconOffMat, null, 24)));
        }

        Renderer Separate(string name, System.Action<Batch> build)
        {
            var b = new Batch();
            build(b);
            var holder = new GameObject(name).transform;
            holder.SetParent(head, false);
            b.Build(holder, "G_" + name, true);
            return holder.GetComponentInChildren<MeshRenderer>();
        }

        public override void Animate(RobotAnim a)
        {
            float s = Mathf.Sin(a.phase);
            float swing = s * 28 * a.gait;
            float knee = a.bend * 35;
            legL.localRotation = Quaternion.Euler(swing - knee, 0, 0);
            legR.localRotation = Quaternion.Euler(-swing - knee, 0, 0);
            body.localPosition = new Vector3(0, Mathf.Abs(Mathf.Cos(a.phase)) * 0.022f * a.gait - a.bend * 0.08f, 0);
            body.localRotation = Quaternion.Euler(0, 0, s * 3 * a.gait);
            upper.localRotation = Quaternion.Euler(a.bend * 40 + 5 * a.gait, 0, 0);
            float empty = 1 - Mathf.Max(a.armHold, a.armReach);
            float pitch = -68 * a.armHold - 40 * a.armLift - 35 * a.armReach;
            float inward = 10 * Mathf.Max(a.armHold, a.armReach);
            armL.localRotation = Quaternion.Euler(pitch - swing * 1.1f * empty, 0, inward);
            armR.localRotation = Quaternion.Euler(pitch + swing * 1.1f * empty, 0, -inward);
            if (a.gesture > 0)
            {
                // a cheerful wave with the right arm, head tilted
                float w = Mathf.Sin(a.gesture * Mathf.PI);
                armR.localRotation = Quaternion.Euler(-150 * w, 0, -25 * w + Mathf.Sin(a.gesture * 30) * 20 * w);
                a.lookWeight = 1;
                LookHead(a);
                head.localRotation *= Quaternion.Euler(0, 0, 12 * w);
                return;
            }
            LookHead(a);
        }
    }
}
