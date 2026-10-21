using UnityEngine;

namespace WorkshopWreckage
{
    /// <summary>Retrobot after resources/retrobot.png: weathered yellow bipedal tin toy
    /// with a boxy two-eyed head, red beacon, grilled torso, segmented arms with red
    /// claws, red knee discs and chunky feet. Parts: head, torso, left arm, right arm,
    /// leg assembly.</summary>
    public class RetrobotModel : RobotModel
    {
        public const string Head = "head", Torso = "torso", ArmL = "left arm", ArmR = "right arm", Legs = "leg assembly";
        public Transform upper, upperGeo, armL, armR, legL, legR;
        public override float ModelHeight => 1.05f;
        static Material Yellow => Paint(new Color(0.84f, 0.62f, 0.17f), 0.55f);

        protected override void CreatePivots()
        {
            legL = Pivot("LegL", body, new Vector3(-0.075f, 0.36f, 0));
            legR = Pivot("LegR", body, new Vector3(0.075f, 0.36f, 0));
            // everything above the hips bends forward around the hip pivot
            upper = Pivot("Upper", body, new Vector3(0, 0.36f, 0));
            upperGeo = Pivot("UpperGeo", upper, new Vector3(0, -0.36f, 0));
            armL = Pivot("ArmL", upperGeo, new Vector3(-0.178f, 0.6f, 0));
            armR = Pivot("ArmR", upperGeo, new Vector3(0.178f, 0.6f, 0));
            head = Pivot("Head", upperGeo, new Vector3(0, 0.675f, 0));
            carry = Pivot("Carry", upperGeo, new Vector3(0, 0.46f, 0.33f));   // between the claws when the arms hold forward
        }

        protected override void BuildGeometry()
        {
            {
                var b = new Batch();
                string P = Torso;
                b.Box(new Vector3(0, 0.39f, 0), new Vector3(0.2f, 0.06f, 0.13f), M(P, Dark));
                b.Box(new Vector3(0, 0.53f, 0), new Vector3(0.28f, 0.25f, 0.2f), M(P, Yellow));
                b.Box(new Vector3(0, 0.575f, -0.102f), new Vector3(0.135f, 0.07f, 0.008f), M(P, Dark));
                for (int k = 0; k < 4; k++) b.Box(new Vector3(0, 0.55f + k * 0.016f, -0.107f), new Vector3(0.12f, 0.006f, 0.006f), M(P, Brass));
                b.Box(new Vector3(0, 0.462f, -0.104f), new Vector3(0.1f, 0.045f, 0.012f), M(P, Yellow));
                b.Box(new Vector3(0, 0.462f, -0.111f), new Vector3(0.05f, 0.012f, 0.004f), M(P, Dark));
                b.Box(new Vector3(0, 0.56f, 0.102f), new Vector3(0.14f, 0.07f, 0.008f), M(P, Dark));
                foreach (var x in new[] { -0.12f, 0.12f })
                    foreach (var y in new[] { 0.42f, 0.64f })
                        b.Box(new Vector3(x, y, -0.101f), new Vector3(0.014f, 0.014f, 0.008f), M(P, Dark));
                b.Cylinder(new Vector3(0, 0.665f, 0), 0.042f, 0.05f, M(P, Dark));
                Done(b, upperGeo);
            }
            foreach (var leg in new[] { legL, legR })
            {
                var b = new Batch();
                string P = Legs;
                b.Cylinder(Vector3.zero, 0.036f, 0.08f, M(P, Dark), AxisX);
                b.Box(new Vector3(0, -0.065f, 0), new Vector3(0.085f, 0.1f, 0.09f), M(P, Yellow));
                b.Cylinder(new Vector3(0, -0.14f, 0), 0.04f, 0.085f, M(P, Dark), AxisX);
                b.Cylinder(new Vector3(0, -0.14f, -0.046f), 0.037f, 0.025f, M(P, Red), AxisZ);
                b.Cylinder(new Vector3(0, -0.14f, -0.06f), 0.014f, 0.01f, M(P, Brass), AxisZ);
                foreach (var dx in new[] { -0.022f, 0.022f }) b.Cylinder(new Vector3(dx, -0.225f, 0.012f), 0.014f, 0.13f, M(P, Dark));
                b.Box(new Vector3(0, -0.215f, -0.018f), new Vector3(0.075f, 0.08f, 0.05f), M(P, Yellow));
                b.Box(new Vector3(0, -0.312f, -0.03f), new Vector3(0.11f, 0.065f, 0.19f), M(P, Yellow));
                b.Box(new Vector3(0, -0.31f, -0.126f), new Vector3(0.07f, 0.024f, 0.006f), M(P, Dark));
                b.Box(new Vector3(0, -0.35f, -0.03f), new Vector3(0.122f, 0.02f, 0.205f), M(P, Rubber));
                b.Box(new Vector3(0, -0.322f, 0.068f), new Vector3(0.1f, 0.04f, 0.03f), M(P, Red));
                Done(b, leg);
            }
            foreach (var (arm, s, part) in new[] { (armL, -1f, ArmL), (armR, 1f, ArmR) })
            {
                var b = new Batch();
                ArmKit(b, s, part, Yellow);
                Done(b, arm);
            }
            HeadKit(head, Yellow, new Vector3(0.32f, 0.24f, 0.24f), 2, Head, Head, Head, Head, Head, Head, Head);
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
            LookHead(a);
        }
    }
}
