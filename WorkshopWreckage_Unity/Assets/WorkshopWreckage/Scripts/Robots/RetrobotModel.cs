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
        static Material Yellow => ToyYellow;
        static Material YellowDeep => ToyYellowDeep;

        /// <summary>QA (--retrolook=legacy): the finish before the 2026-10-23 polish pass, for
        /// side-by-side comparisons and as a fallback.</summary>
        public static bool LegacyLook;
        static Dictionary<Material, Material> _skin;

        /// <summary>The polished finish (RobotFinish): enamel with construction-led wear, and the
        /// bare metals told apart. Retrobot only; the other robots keep the shared kit materials.</summary>
        protected override Material Skin(Material m)
        {
            if (LegacyLook || m == null) return m;
            _skin ??= new Dictionary<Material, Material>
            {
                [ToyYellow] = RobotFinish.Paint(new Color(0.8f, 0.55f, 0.12f)),
                [ToyYellowDeep] = RobotFinish.Paint(new Color(0.68f, 0.45f, 0.09f)),
                [ToyRed] = RobotFinish.Paint(new Color(0.68f, 0.1f, 0.06f)),
                [Gunmetal] = RobotFinish.JointSteel,
                [Dark] = RobotFinish.Blackened,
                [Brass] = RobotFinish.BearingBrass,
                [Rubber] = RobotFinish.Rubber,
                [StainMat] = RobotFinish.OilStain,
            };
            return _skin.TryGetValue(m, out var s) ? s : m;
        }

        protected override Material BoltSkin(Material m) => !LegacyLook && m == RobotFinish.JointSteel ? RobotFinish.BoltSteel : m;

        /// <summary>Piston rods: bright shaft steel (gunmetal in the legacy look).</summary>
        static Material Shaft => LegacyLook ? Gunmetal : RobotFinish.ShaftSteel;
        const float HipY = 0.40f, ShoulderY = 0.62f, NeckY = 0.68f, ShoulderX = 0.238f, LegX = 0.14f;
        const float Flare = 11f;   // rest pose: the arms hang slightly out from the body, as on the sheet

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

        protected override void BuildGeometry()
        {
            BuildTorso();
            foreach (var (leg, s) in new[] { (legL, -1f), (legR, 1f) }) BuildLeg(leg, s);
            foreach (var (arm, s, part) in new[] { (armL, -1f, ArmL), (armR, 1f, ArmR) })
            {
                DetailedArm(arm, s, part, part, Yellow, YellowDeep, ToyRed, 1.12f, 1.3f);
            }
            DetailedHead(head, Yellow, new Vector3(0.36f, 0.32f, 0.32f), 2, 0.048f, Head, Head, null, Head, Head, Head, Head, Head, true, true);
        }

        void BuildTorso()
        {
            var b = new Batch();
            string P = Torso;
            // pelvis: dark mechanical block with hip axle and brass bolts
            b.RoundedBox(new Vector3(0, 0.415f, 0), new Vector3(0.21f, 0.07f, 0.16f), 0.02f, M(P, Gunmetal), 2);
            b.Lathe(new Vector3(-0.18f, 0.405f, 0), Disc(0.03f, 0.36f, 0.006f), M(P, Dark), RightRot, 12);   // hip axle
            b.Lathe(new Vector3(-0.215f, ShoulderY, 0), Disc(0.026f, 0.43f, 0.005f), M(P, Gunmetal), RightRot, 12);   // shoulder axle
            foreach (var x in new[] { -0.07f, 0f, 0.07f }) b.Rivet(new Vector3(x, 0.42f, -0.08f), new Vector3(0, 0, -1), 0.009f, M(P, Brass));
            // body
            b.RoundedBox(new Vector3(0, 0.54f, 0), new Vector3(0.3f, 0.25f, 0.23f), 0.035f, M(P, Yellow), 3);
            b.RoundedBox(new Vector3(0, 0.665f, 0), new Vector3(0.22f, 0.02f, 0.17f), 0.008f, M(P, YellowDeep), 2);   // collar
            Grille(b, P, new Vector3(0, 0.585f, -0.117f), 0.15f, 0.075f, 6, false, -1);
            b.RoundedBox(new Vector3(0, 0.468f, -0.118f), new Vector3(0.12f, 0.05f, 0.012f), 0.006f, M(P, YellowDeep), 2);   // lower plate
            b.RoundedBox(new Vector3(0, 0.468f, -0.125f), new Vector3(0.065f, 0.012f, 0.006f), 0.003f, M(P, Dark), 1);       // slot
            foreach (var x in new[] { -0.128f, 0.128f })
                foreach (var y in new[] { 0.44f, 0.64f })
                    Bolt(b, new Vector3(x, y, -0.115f), new Vector3(0, 0, -1), 0.008f, M(P, Gunmetal));
            foreach (var x in new[] { -0.052f, 0.052f }) Bolt(b, new Vector3(x, 0.468f, -0.124f), new Vector3(0, 0, -1), 0.006f, M(P, Gunmetal));
            foreach (var n in new[] { Vector3.left, Vector3.right, new Vector3(0, 0, 1) }) PanelFrame(b, P, new Vector3(0, 0.54f, 0), new Vector3(0.3f, 0.25f, 0.23f), n, 0.047f);
            SeamLine(b, P, new Vector3(-0.11f, 0.625f, -0.116f), new Vector3(0.11f, 0.625f, -0.116f), new Vector3(0, 0, -1));   // the chest plate's top seam
            Grille(b, P, new Vector3(0, 0.56f, 0.117f), 0.15f, 0.08f, 6, false, 1);   // back vent
            // brass pipes down the back into the pelvis, dark hoses between the legs in front
            foreach (var x in new[] { -0.075f, 0.075f })
            {
                Pipe(b, P, new[] { new Vector3(x, 0.462f, 0.108f), new Vector3(x, 0.445f, 0.13f), new Vector3(x, 0.422f, 0.11f), new Vector3(x, 0.405f, 0.076f) }, 0.009f, Brass, Gunmetal);
                Pipe(b, P, new[] { new Vector3(x, 0.432f, -0.104f), new Vector3(x * 1.15f, 0.405f, -0.122f), new Vector3(x * 0.7f, 0.39f, -0.078f) }, 0.0075f, Rubber, Brass);
            }
            // neck: stacked dark rings
            for (int k = 0; k < 3; k++) b.Lathe(new Vector3(0, 0.672f + k * 0.012f, 0), Disc(0.05f - k * 0.004f, 0.012f, 0.003f), M(P, Gunmetal), null, 16);
            Done(b, upperGeo);
        }

        void BuildLeg(Transform leg, float s)
        {
            var b = new Batch();
            string P = Legs;
            Material Y = M(P, Yellow), YD = M(P, YellowDeep), G = M(P, Gunmetal), Br = M(P, Brass), R = M(P, ToyRed);
            b.Lathe(new Vector3(-0.06f, -0.02f, 0), Disc(0.05f, 0.12f, 0.01f), G, RightRot, 18);   // hip joint (axle along X)
            // thigh: a chunky banded block
            b.RoundedBox(new Vector3(0, -0.082f, 0), new Vector3(0.13f, 0.09f, 0.125f), 0.026f, Y, 2);
            b.RoundedBox(new Vector3(0, -0.044f, 0), new Vector3(0.136f, 0.016f, 0.13f), 0.006f, YD, 2);
            foreach (var z in new[] { -0.03f, 0.03f }) Bolt(b, new Vector3(s * 0.066f, -0.085f, z), new Vector3(s, 0, 0), 0.0065f, G);
            // knee: dark axle, big red discs (front and outside) with brass caps
            b.Lathe(new Vector3(-0.056f, -0.14f, 0), Disc(0.046f, 0.112f, 0.008f), G, RightRot, 18);
            foreach (var side in new[] { -1f, 1f })
            {
                b.Lathe(new Vector3(side * 0.056f, -0.14f, 0), Disc(0.04f, 0.02f, 0.006f), R, SideRot(side), 18);
                b.Lathe(new Vector3(side * 0.076f, -0.14f, 0), Dome(0.016f, 0.01f), Br, SideRot(side), 10);
            }
            b.Lathe(new Vector3(0, -0.14f, -0.05f), Disc(0.036f, 0.018f, 0.005f), R, FwdRot, 18);
            b.Lathe(new Vector3(0, -0.14f, -0.068f), Dome(0.013f, 0.008f), Br, FwdRot, 10);
            // shin: yellow front plate with rivets, pistons and springs behind
            b.RoundedBox(new Vector3(0, -0.218f, -0.018f), new Vector3(0.118f, 0.1f, 0.072f), 0.018f, Y, 2);
            foreach (var x in new[] { -0.04f, 0.04f }) Bolt(b, new Vector3(x, -0.235f, -0.055f), new Vector3(0, 0, -1), 0.006f, G);
            foreach (var dx in new[] { -0.028f, 0.028f })
            {
                b.Lathe(new Vector3(dx, -0.28f, 0.026f), Disc(0.013f, 0.13f, 0.002f), M(P, Shaft), null, 10);
                b.Lathe(new Vector3(dx, -0.205f, 0.026f), Disc(0.018f, 0.022f, 0.003f), Br, null, 10);
            }
            b.Lathe(new Vector3(-0.048f, -0.29f, 0), Disc(0.032f, 0.096f, 0.006f), G, RightRot, 14);   // ankle
            Pipe(b, P, new[] { new Vector3(s * 0.045f, -0.07f, 0.062f), new Vector3(s * 0.05f, -0.14f, 0.086f), new Vector3(s * 0.048f, -0.2f, 0.05f) }, 0.0075f, Rubber, Brass);   // hose round the knee
            // foot: a big boot on a wide dark sole plate, toe vent, red heel
            b.RoundedBox(new Vector3(0, -0.336f, -0.04f), new Vector3(0.15f, 0.078f, 0.26f), 0.028f, Y, 3);
            b.RoundedBox(new Vector3(0, -0.3f, -0.065f), new Vector3(0.116f, 0.02f, 0.15f), 0.008f, YD, 2);
            Grille(b, P, new Vector3(0, -0.34f, -0.171f), 0.094f, 0.036f, 4, false, -1);
            b.RoundedBox(new Vector3(0, -0.388f, -0.04f), new Vector3(0.168f, 0.026f, 0.28f), 0.008f, G, 2);
            b.RoundedBox(new Vector3(0, -0.399f, -0.04f), new Vector3(0.158f, 0.008f, 0.27f), 0.003f, M(P, Rubber), 1);
            b.RoundedBox(new Vector3(0, -0.345f, 0.088f), new Vector3(0.13f, 0.052f, 0.03f), 0.01f, R, 2);
            foreach (var x in new[] { -0.058f, 0.058f }) Bolt(b, new Vector3(x, -0.31f, -0.168f), new Vector3(0, 0, -1), 0.006f, G);
            foreach (var z in new[] { -0.13f, -0.04f, 0.05f }) Bolt(b, new Vector3(s * 0.076f, -0.336f, z), new Vector3(s, 0, 0), 0.006f, G);
            Done(b, leg);
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
            float pitch = -68 * a.armHold - 40 * a.armLift - 35 * a.armReach - 16 * (1 - Mathf.Max(a.armHold, a.armReach));
            float inward = 10 * Mathf.Max(a.armHold, a.armReach);
            armL.localRotation = Quaternion.Euler(pitch - swing * 1.1f * empty, 0, inward - Flare * empty);
            armR.localRotation = Quaternion.Euler(pitch + swing * 1.1f * empty, 0, -inward + Flare * empty);
            PoseElbows(a, 38);
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
