using System.Collections.Generic;
using UnityEngine;

namespace WorkshopWreckage
{
    /// <summary>Retrobot after resources/retrobot.png (front / side / back), rebuilt in the geometry
    /// pass of 2026-10-23 (docs/RETROBOT_GEOMETRY_AUDIT.md): an engineered retro robot of chamfered
    /// pressed-steel panels and layered steel joints, painted mustard yellow with red accents.
    /// A wide chamfered head with a raised face plate, deep layered eye housings (steel ring, brass
    /// bezel, dark tube, recessed lamp), a framed mouth grille, red ear cups with bolted steel hubs,
    /// side-mounted antennas and a beacon on a two-tier base; a taller torso with a front panel,
    /// framed chest grille and handle plate, bolted shoulder flanges and bearing collars; shoulder
    /// drums with red caps and bolted hubs; upper arms ending in a steel clevis round a geared elbow
    /// knuckle; forearms with an access plate and steel cuff; a wrist housing and faceted red
    /// two-segment fingers on hinge hubs with flush dark grip pads; a dark pelvis with geared hips;
    /// thighs, a knee axle with side hubs and the red front knee disc; shins with exposed pistons
    /// and springs behind; ankle hubs and long boots with a sloped toe and toe grille, red heel cap,
    /// side plates and a steel-and-rubber sole. Retrobot is the reference model of the family component
    /// catalogue (RobotFamilyKit: FamilyHead, FamilyArm with the articulated gripper, hardware, finish).
    /// Parts: head, torso, left arm, right arm, leg assembly.</summary>
    public class RetrobotModel : RobotModel
    {
        public const string Head = "head", Torso = "torso", ArmL = "left arm", ArmR = "right arm", Legs = "leg assembly";
        public Transform upper, upperGeo, armL, armR, legL, legR, kneeL, kneeR, ankleL, ankleR;
        public override float ModelHeight => 1.06f;
        static Material Yellow => ToyYellow;
        static Material YellowDeep => ToyYellowDeep;
        const float HipY = 0.40f, ShoulderY = 0.65f, NeckY = 0.72f, ShoulderX = 0.24f, LegX = 0.12f;
        const float Flare = 11f;   // rest pose: the arms hang slightly out from the body, as on the sheet
        // the sheet's stance: thigh 10 deg forward, shin 29 deg back, foot flat (ankle 0.115 above the floor)
        const float ThighL = 0.133f, ShinL = 0.176f, HipRest = -10f, KneeRest = 39f, AnkleRest = -29f;
        // F4: seen from the front the thighs splay out from the hips (an inverted V); the knee takes
        // the splay back out, so the shins stand upright and the boots stay flat and facing forward
        public static float HipSplay = 8f;   // QA: --retrosplay=<deg> overrides it for silhouette comparisons
        // proportions after the concept sheet: head 0.40 x 0.28 x 0.31, torso 0.31 x 0.27 x 0.25
        const float HeadW = 0.39f, HeadH = 0.28f, HeadD = 0.32f, TorsoW = 0.31f, TorsoD = 0.25f, TorsoBot = 0.43f, TorsoTop = 0.70f;

        /// <summary>QA (--retrolook=legacy): the finish before the 2026-10-23 polish pass, for
        /// side-by-side comparisons and as a fallback.</summary>
        public static bool LegacyLook;

        /// <summary>The family finish (RobotFamilyKit.FamilySkin); the legacy look keeps the kit materials.</summary>
        protected override Material Skin(Material m) => LegacyLook ? m : FamilySkin(m, FamilyBlue);   // industrial blue

        protected override Material BoltSkin(Material m) => LegacyLook ? m : FamilyBoltSkin(m);

        static Color BodyC => FamilyBlue;
        static Color RedC => FamilyRed;
        /// <summary>The boots and heel caps: the most worn paint on the robot (scuffed, chipped, dirty).</summary>
        static Material FootPaint => LegacyLook ? Yellow : RobotFinish.Paint(BodyC, 1f, 0.85f, 0.75f, 1f, 0.45f, 0.45f, 0.1f);
        static Material HeelPaint => LegacyLook ? ToyRed : RobotFinish.Paint(RedC, 1f, 0.8f, 0.7f, 1f, 0.7f, 0.2f, 0.08f);
        protected override Material GreaseMat => LegacyLook ? StainMat : RobotFinish.Grease;
        protected override Material BrightSteel => Shaft;

        /// <summary>Bright steel: shafts, piston rods, hub faces, grille slats (gunmetal in the legacy look).</summary>
        static Material Shaft => LegacyLook ? Gunmetal : RobotFinish.ShaftSteel;

        protected override void CreatePivots()
        {
            legL = Pivot("LegL", body, new Vector3(-LegX, HipY, 0));
            legR = Pivot("LegR", body, new Vector3(LegX, HipY, 0));
            kneeL = Pivot("Knee", legL, new Vector3(0, -ThighL, 0));
            kneeR = Pivot("Knee", legR, new Vector3(0, -ThighL, 0));
            ankleL = Pivot("Ankle", kneeL, new Vector3(0, -ShinL, 0));
            ankleR = Pivot("Ankle", kneeR, new Vector3(0, -ShinL, 0));
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

        protected override Vector3? ClampPoint => new Vector3(0, 0.52f, Has(Torso) ? 0.15f : 0.035f);

        /// <summary>Without the torso: the bare spine with shoulder and hip bars.</summary>
        protected override void BuildFrame()
        {
            if (Has(Torso)) return;
            var b = new Batch();
            Material G = Skin(Gunmetal), Br = Skin(Brass);
            b.RoundedBox(new Vector3(0, (HipY + NeckY) / 2, 0), new Vector3(0.05f, NeckY - HipY, 0.05f), 0.01f, G, 2);
            b.RoundedBox(new Vector3(0, ShoulderY, 0), new Vector3(ShoulderX * 2 - 0.08f, 0.035f, 0.035f), 0.008f, G, 2);
            b.RoundedBox(new Vector3(0, HipY, 0), new Vector3(LegX * 2 - 0.05f, 0.045f, 0.06f), 0.01f, G, 2);
            b.RoundedBox(new Vector3(0, 0.55f, -0.03f), new Vector3(0.09f, 0.07f, 0.035f), 0.008f, Br, 2);   // the little drive box on the spine
            b.Build(upperGeo, "G_frame", true);
        }

        protected override void BuildGeometry()
        {
            BuildTorso();
            foreach (var (leg, knee, ankle, s) in new[] { (legL, kneeL, ankleL, -1f), (legR, kneeR, ankleR, 1f) }) BuildLeg(leg, knee, ankle, s);
            foreach (var (arm, s, part) in new[] { (armL, -1f, ArmL), (armR, 1f, ArmR) })
                BuildArmModule(arm, s, part, new ArmSpec { paint = Yellow, paintDeep = YellowDeep, red = ToyRed, clawScale = 1.08f, twist = 20f });
            BuildHeadModule(() => FamilyHead(head, Head, new HeadSpec { w = HeadW, h = HeadH, d = HeadD, eyeX = 0.097f, eyeY = 0.46f, earY = 0.47f, paint = Yellow, beacon = true, antennas = true, mouth = true }));
        }

        // ------------------------------------------------------------ torso

        void BuildTorso()
        {
            var b = new Batch();
            string P = Torso;
            float cy = (TorsoBot + TorsoTop) / 2, H = TorsoTop - TorsoBot, front = -TorsoD / 2;
            // pelvis: a wide, deep dark steel housing under the torso; the hips turn inside it on one
            // axle, so the legs come out from beneath the body instead of being bolted to its sides
            b.ChamferBox(new Vector3(0, 0.403f, 0.005f), new Vector3(0.27f, 0.08f, 0.2f), 0.018f, M(P, Gunmetal), null, true);
            b.Lathe(new Vector3(-LegX + 0.05f, HipY, 0), Disc(0.022f, 2 * LegX - 0.1f, 0.004f), M(P, Shaft), RightRot, 14);   // hip axle (centre; the legs carry the stubs)
            b.Lathe(new Vector3(0, 0.4f, -0.095f), Disc(0.032f, 0.016f, 0.004f), M(P, Gunmetal), FwdRot, 20);
            Nut(b, new Vector3(0, 0.4f, -0.111f), FwdRot, 0.011f, 0.006f, M(P, Gunmetal));
            foreach (var x in new[] { -0.075f, 0.075f }) Bolt(b, new Vector3(x, 0.415f, -0.095f), new Vector3(0, 0, -1), 0.007f, M(P, Gunmetal));
            // body shell: chamfered pressed steel, with a raised front panel
            b.ChamferBox(new Vector3(0, cy, 0), new Vector3(TorsoW, H, TorsoD), 0.028f, M(P, Yellow), null, true);
            b.ChamferBox(new Vector3(0, cy + 0.008f, front - 0.004f), new Vector3(TorsoW - 0.05f, H - 0.06f, 0.012f), 0.007f, M(P, Yellow), null, true);
            b.ChamferBox(new Vector3(0, TorsoBot + 0.012f, 0), new Vector3(TorsoW + 0.008f, 0.022f, TorsoD + 0.008f), 0.008f, M(P, YellowDeep), null, true);   // bottom rim band
            FramedGrille(b, P, new Vector3(0, 0.625f, front - 0.01f), 0.14f, 0.085f, 6, false, -1);
            // lower handle plate with its slot
            b.ChamferBox(new Vector3(0, 0.515f, front - 0.014f), new Vector3(0.1f, 0.045f, 0.01f), 0.004f, M(P, YellowDeep), null, true);
            b.ChamferBox(new Vector3(0, 0.515f, front - 0.02f), new Vector3(0.064f, 0.014f, 0.004f), 0.001f, M(P, Dark));
            foreach (var x in new[] { -0.04f, 0.04f }) Bolt(b, new Vector3(x, 0.515f, front - 0.019f), new Vector3(0, 0, -1), 0.0055f, M(P, Gunmetal));
            SeamLine(b, P, new Vector3(-0.12f, 0.572f, front - 0.0102f), new Vector3(0.12f, 0.572f, front - 0.0102f), new Vector3(0, 0, -1));
            foreach (var x in new[] { -0.112f, 0.112f })
                foreach (var y in new[] { 0.488f, 0.668f })
                    Bolt(b, new Vector3(x, y, front - 0.01f), new Vector3(0, 0, -1), 0.0065f, M(P, Gunmetal));
            // sides: corner bolts and the shoulder mounts (a bolted flange and a bearing collar)
            foreach (var s in new[] { -1f, 1f })
            {
                foreach (var y in new[] { 0.455f, 0.57f })
                    foreach (var z in new[] { -0.09f, 0.09f })
                        Bolt(b, new Vector3(s * TorsoW / 2, y, z), X(s), 0.0065f, M(P, Gunmetal));
                b.ChamferBox(new Vector3(s * (TorsoW / 2 + 0.005f), ShoulderY, 0), new Vector3(0.01f, 0.1f, 0.11f), 0.008f, M(P, Gunmetal));
                foreach (var y in new[] { -0.036f, 0.036f })
                    foreach (var z in new[] { -0.04f, 0.04f })
                        Bolt(b, new Vector3(s * (TorsoW / 2 + 0.01f), ShoulderY + y, z), X(s), 0.0055f, M(P, Gunmetal));
                b.Lathe(new Vector3(s * (TorsoW / 2 + 0.01f), ShoulderY, 0), Disc(0.034f, ShoulderX - TorsoW / 2 - 0.06f, 0.004f), M(P, Gunmetal), SideRot(s), 18);
            }
            foreach (var s in new[] { -1f, 1f })
                foreach (var z in new[] { -0.035f, 0.02f })
                    GreaseRun(b, P, new Vector3(s * TorsoW / 2, ShoulderY - 0.052f, z), X(s), 0.1f, 0.014f);
            GrimeBand(b, P, new Vector3(0, TorsoBot + 0.001f, front - 0.004f), new Vector3(0, 0, -1), Vector3.right, TorsoW, 0.022f);
            foreach (var s in new[] { -1f, 1f }) GrimeBand(b, P, new Vector3(s * (TorsoW / 2 + 0.004f), TorsoBot + 0.001f, 0), X(s), Vector3.forward, TorsoD, 0.022f);
            // back: bolted plate, vent and a lower handle plate; two cable runs into the pelvis
            PanelFrame(b, P, new Vector3(0, cy, 0), new Vector3(TorsoW, H, TorsoD), new Vector3(0, 0, 1), 0.034f);
            FramedGrille(b, P, new Vector3(0, 0.615f, -front + 0.001f), 0.15f, 0.09f, 6, false, 1);
            b.ChamferBox(new Vector3(0, 0.505f, -front + 0.005f), new Vector3(0.1f, 0.04f, 0.01f), 0.004f, M(P, YellowDeep), null, true);
            b.ChamferBox(new Vector3(0, 0.505f, -front + 0.01f), new Vector3(0.064f, 0.012f, 0.004f), 0.001f, M(P, Dark));
            foreach (var x in new[] { -0.07f, 0.07f })
                Pipe(b, P, new[] { new Vector3(x, 0.465f, -front), new Vector3(x, 0.445f, -front + 0.018f), new Vector3(x * 0.9f, 0.42f, 0.1f), new Vector3(x * 0.8f, 0.405f, 0.08f) }, 0.008f, Rubber, Brass);
            // top: collar plate and the neck (stacked steel rings the head turns on)
            b.ChamferBox(new Vector3(0, TorsoTop + 0.004f, 0), new Vector3(0.2f, 0.01f, 0.15f), 0.004f, M(P, Gunmetal));
            b.Lathe(new Vector3(0, TorsoTop + 0.008f, 0), Disc(0.048f, 0.008f, 0.002f), M(P, Gunmetal), null, 20);
            b.Lathe(new Vector3(0, TorsoTop + 0.016f, 0), Disc(0.036f, 0.01f, 0.002f), M(P, Shaft), null, 20);
            Done(b, upperGeo);
        }

        // ------------------------------------------------------------ legs

        /// <summary>One leg (side s) in three pivots, standing in the sheet's slightly bent stance. On
        /// the hip pivot (inside the pelvis): washer, yellow hip drum and hub, the thigh with a steel link strap down its
        /// outer side, and the knee: a load-bearing axle housing, yellow side rings round big bolted
        /// steel hubs, and the large domed red knee cap in front. On the knee pivot: the shin shell,
        /// a dark spine, two pistons with springs in front (seen from the front, as on the sheet) and
        /// one behind. On the ankle pivot: ankle hubs and the heavy rounded boot (instep housing, toe
        /// grille, red heel cap, side plate, broad steel sole on rubber), kept flat on the floor.</summary>
        void BuildLeg(Transform leg, Transform knee, Transform ankle, float s)
        {
            string P = Legs;
            var b = new Batch();
            // hip (all on the pivot axis, inside the pelvis): a thrust washer, the yellow hip drum that
            // the thigh grows out of (its top runs up under the torso), the bolted outer hub
            b.Lathe(X(-s * 0.075f), Disc(0.022f, 0.1f, 0.004f), M(P, Shaft), SideRot(s), 14);   // axle stub, splayed with the leg
            b.Lathe(X(-s * 0.055f), Disc(0.05f, 0.012f, 0.003f), M(P, Gunmetal), SideRot(s), 22);
            b.Lathe(X(-s * 0.043f), Disc(0.047f, 0.068f, 0.012f), M(P, Yellow), SideRot(s), 28);
            Hub(b, P, X(s * 0.025f), s, 0.04f, 5);
            // thigh, and a steel link strap from the hip hub to the knee hub
            b.ChamferBox(new Vector3(0, -0.068f, 0), new Vector3(0.1f, 0.076f, 0.1f), 0.016f, M(P, Yellow), null, true);
            b.ChamferBox(new Vector3(0, -0.036f, 0), new Vector3(0.106f, 0.014f, 0.106f), 0.004f, M(P, YellowDeep), null, true);
            b.ChamferBox(new Vector3(s * 0.06f, -ThighL / 2, 0), new Vector3(0.01f, ThighL, 0.026f), 0.004f, M(P, Gunmetal));
            foreach (var y in new[] { -0.02f, -ThighL + 0.02f }) Bolt(b, new Vector3(s * 0.065f, y, 0), X(s), 0.0055f, M(P, Gunmetal));
            GreaseRun(b, P, new Vector3(s * 0.05f, -0.032f, 0.022f), X(s), 0.05f);
            // knee: axle housing, yellow side rings round big steel hubs, the domed red cap in front
            var k = new Vector3(0, -ThighL, 0);
            b.Lathe(k - X(0.066f), Disc(0.05f, 0.132f, 0.008f), M(P, Gunmetal), RightRot, 24);
            foreach (var hs in new[] { -1f, 1f })
            {
                b.Lathe(k + X(hs * 0.05f), Disc(0.07f, 0.018f, 0.006f), M(P, Yellow), SideRot(hs), 28);
                Hub(b, P, k + X(hs * 0.068f), hs, 0.054f, hs == s ? 6 : 0);
            }
            b.Lathe(k + new Vector3(0, 0, -0.038f), new[] { new Vector2(0.05f, 0), new Vector2(0.05f, 0.012f), new Vector2(0.046f, 0.022f), new Vector2(0.036f, 0.03f), new Vector2(0.024f, 0.032f), new Vector2(0, 0.032f) }, M(P, ToyRed), FwdRot, 28);
            b.Lathe(k + new Vector3(0, 0, -0.068f), Disc(0.022f, 0.006f, 0.002f), M(P, Shaft), FwdRot, 18);
            Nut(b, k + new Vector3(0, 0, -0.074f), FwdRot, 0.009f, 0.005f, M(P, Gunmetal));
            Done(b, leg);

            var sh = new Batch();
            // shin: a yellow shell, a dark spine down to the ankle, pistons and springs
            sh.ChamferBox(new Vector3(0, -0.07f, -0.006f), new Vector3(0.092f, 0.07f, 0.078f), 0.014f, M(P, Yellow), null, true);
            foreach (var x in new[] { -0.024f, 0.024f }) Bolt(sh, new Vector3(x, -0.072f, -0.046f), new Vector3(0, 0, -1), 0.0055f, M(P, Gunmetal));
            sh.ChamferBox(new Vector3(0, -0.13f, 0.004f), new Vector3(0.05f, 0.09f, 0.044f), 0.008f, M(P, Gunmetal));
            GreaseRun(sh, P, new Vector3(-0.012f, -0.036f, -0.045f), new Vector3(0, 0, -1), 0.055f);
            foreach (var x in new[] { -0.03f, 0.03f })   // front pair: cylinder, rod, spring coils
            {
                sh.Lathe(new Vector3(x, -0.135f, -0.026f), Disc(0.012f, 0.03f, 0.003f), M(P, Gunmetal), null, 12);
                sh.Lathe(new Vector3(x, -0.172f, -0.026f), Disc(0.006f, 0.04f, 0.001f), M(P, Shaft), null, 10);
                for (int c = 0; c < 4; c++)
                    sh.Lathe(new Vector3(x, -0.168f + c * 0.009f, -0.026f), Disc(0.0115f, 0.0035f, 0.0012f), M(P, Dark), null, 12);
            }
            sh.Lathe(new Vector3(0, -0.11f, 0.034f), Disc(0.014f, 0.07f, 0.003f), M(P, Gunmetal), null, 12);   // rear piston
            sh.Lathe(new Vector3(0, -0.17f, 0.034f), Disc(0.007f, 0.062f, 0.001f), M(P, Shaft), null, 10);
            Done(sh, knee);

            var ft = new Batch();
            // ankle hubs, then the boot, kept flat: sole bottom 0.118 below the ankle axle
            ft.Lathe(X(-0.058f), Disc(0.036f, 0.116f, 0.005f), M(P, Gunmetal), RightRot, 20);
            foreach (var hs in new[] { -1f, 1f }) Hub(ft, P, X(hs * 0.058f), hs, 0.034f, 0);
            // the boot: a heavy rounded casing (tall, domed edges) on a broad dark sole, an instep housing
            // under the ankle, the rounded toe with the grille on its front face, a rounded red heel cap
            ft.RoundedBox(new Vector3(0, -0.026f, -0.01f), new Vector3(0.11f, 0.04f, 0.12f), 0.016f, M(P, FootPaint), 2);    // instep
            ft.RoundedBox(new Vector3(0, -0.058f, -0.075f), new Vector3(0.198f, 0.086f, 0.33f), 0.032f, M(P, FootPaint), 3);   // casing
            FramedGrille(ft, P, new Vector3(0, -0.058f, -0.2405f), 0.12f, 0.03f, 3, false, -1);
            ft.RoundedBox(new Vector3(0, -0.066f, 0.1f), new Vector3(0.19f, 0.068f, 0.06f), 0.022f, M(P, HeelPaint), 2);
            ft.ChamferBox(new Vector3(s * 0.1f, -0.058f, -0.07f), new Vector3(0.006f, 0.034f, 0.16f), 0.003f, M(P, YellowDeep), null, true);
            foreach (var z in new[] { -0.13f, -0.01f }) Bolt(ft, new Vector3(s * 0.103f, -0.058f, z), X(s), 0.006f, M(P, Gunmetal));
            ft.ChamferBox(new Vector3(0, -0.105f, -0.07f), new Vector3(0.218f, 0.022f, 0.4f), 0.008f, M(P, Gunmetal), null, true);
            ft.ChamferBox(new Vector3(0, -0.114f, -0.07f), new Vector3(0.21f, 0.008f, 0.39f), 0.003f, M(P, Rubber));
            Done(ft, ankle);
        }

        // ------------------------------------------------------------ animation (F5, F6)

        /// <summary>Largest stride swing at the hip (deg): short, deliberate steps.</summary>
        const float MaxStride = 24f;
        /// <summary>Stepping on the spot while turning (deg of lift-equivalent stride).</summary>
        const float TurnStride = 9f;

        /// <summary>Retrobot's cadence follows its speed so that the stride (at most MaxStride at the
        /// hip, a pendulum of hip height) moves the planted boot exactly with the ground: quick
        /// little steps, no sliding.</summary>
        public override float StepRate(float moving01, float speed)
        {
            float lh = HipY * Mathf.Max(1e-4f, transform.lossyScale.y);
            return Mathf.Max(6f, speed / (lh * MaxStride * Mathf.Deg2Rad));
        }

        public override void ResetPose() { _twistL = _twistR = 0; }

        // the planted boots' headings while turning on the spot (deg, relative to the body)
        float _twistL, _twistR;

        /// <summary>One leg: the turning twist about the vertical at the hip, then the hip pitch in the
        /// splayed plane; the knee undoes that pitch and the splay, then applies the summed pitch (the
        /// shin is upright from the front); the ankle keeps the boot flat.</summary>
        void PoseLeg(Transform leg, Transform knee, Transform ankle, float side, float hip, float kneePitch, float twist)
        {
            leg.localRotation = Quaternion.Euler(0, twist, 0) * Quaternion.Euler(0, 0, side * HipSplay) * Quaternion.Euler(hip, 0, 0);
            knee.localRotation = Quaternion.Euler(-hip, 0, 0) * Quaternion.Euler(0, 0, -side * HipSplay) * Quaternion.Euler(hip + kneePitch, 0, 0);
            ankle.localRotation = Quaternion.Euler(-(hip + kneePitch), 0, 0);
        }

        /// <summary>Thigh length as seen from the side (the splay shortens its drop).</summary>
        static float ThighSide => ThighL * Mathf.Cos(HipSplay * Mathf.Deg2Rad);

        /// <summary>Vertical distance hip to ankle (model units) for a hip and knee pitch.</summary>
        static float LegDrop(float hip, float knee) => ThighSide * Mathf.Cos(hip * Mathf.Deg2Rad) + ShinL * Mathf.Cos((hip + knee) * Mathf.Deg2Rad);

        /// <summary>Two-bone leg IK in the side plane: the hip and knee pitch (deg) that put the ankle
        /// at (z back, y down) from the hip. Used to keep the boots planted while the body shifts and
        /// crouches. At the rest target it returns the rest pose (hip -10, knee +39).</summary>
        static void LegIK(float z, float y, out float hip, out float knee)
        {
            float l1 = ThighSide, l2 = ShinL;
            float d = Mathf.Clamp(Mathf.Sqrt(z * z + y * y), Mathf.Abs(l1 - l2) + 1e-4f, l1 + l2 - 1e-4f);
            float phi = Mathf.Atan2(z, -y);
            float a = Mathf.Acos(Mathf.Clamp((l1 * l1 + d * d - l2 * l2) / (2 * l1 * d), -1, 1));
            float b = Mathf.Acos(Mathf.Clamp((l2 * l2 + d * d - l1 * l1) / (2 * l2 * d), -1, 1));
            hip = (phi - a) * Mathf.Rad2Deg;
            knee = (phi + b) * Mathf.Rad2Deg - hip;
        }

        /// <summary>Re-solves a leg so its ankle stays where it was while the body moves (dz back, dy up).</summary>
        static void KeepAnkle(ref float hip, ref float knee, float dz, float dy)
        {
            float h = hip * Mathf.Deg2Rad, s = (hip + knee) * Mathf.Deg2Rad;
            float z = ThighSide * Mathf.Sin(h) + ShinL * Mathf.Sin(s), y = -(ThighSide * Mathf.Cos(h) + ShinL * Mathf.Cos(s));
            LegIK(z - dz, y - dy, out hip, out knee);
        }

        /// <summary>The arm pitch (deg) for holding, lifting and reaching down. Reaching down overrides
        /// holding (setting a carried part down lowers the arms; it must not raise them).</summary>
        static float ArmPitch(float hold, float lift, float reach) => Mathf.Lerp(-68 * hold - 40 * lift - 16 * (1 - hold), -45, reach);

        /// <summary>Legs, body, upper body and arms for one frame. `depth` scales the whole pick-up pose
        /// (the floor guard lowers it so the claws stop at the part). The pick-up is a small robot
        /// reaching carefully: first a little weight shift back, then it crouches through the knees with
        /// the boots planted (leg IK), leans only moderately and reaches with the arms.</summary>
        void PoseBody(RobotAnim a, float stride, float lift, float walking, float depth)
        {
            float s = Mathf.Sin(a.phase), c = Mathf.Cos(a.phase);
            float bend = a.bend * depth;
            a.armReach = Mathf.Min(1, a.armReach * depth);
            // the pick-up choreography (model units, degrees)
            float shift = 0.012f * Ease(bend / 0.3f);                 // hips back: counterweight to the reach
            float crouch = 0.085f * Ease((bend - 0.12f) / 0.88f);     // down through the knees
            float lean = 20f * Ease((bend - 0.15f) / 0.85f);          // only as much as needed
            float deeper = Mathf.Clamp01(bend - 1);                   // a part lower than its feet: further, up to twice
            crouch += 0.1f * deeper;
            lean += 18f * deeper;
            shift += 0.012f * deeper;
            // walking: the stride swing, the lifted boot (knee and hip flex)
            float liftL = Mathf.Max(0, -c) * lift, liftR = Mathf.Max(0, c) * lift;
            float hipL = HipRest + stride * s - 12 * liftL, hipR = HipRest - stride * s - 12 * liftR;
            float kL = KneeRest + 28 * liftL, kR = KneeRest + 28 * liftR;
            // the body rides on the planted leg (exact floor contact), then shifts and crouches with the
            // boots kept where they stand
            // the F4 stance: hip height as designed without the splay (the splay sinks the hips 1.3 mm)
            float rest = ThighL * Mathf.Cos(HipRest * Mathf.Deg2Rad) + ShinL * Mathf.Cos((HipRest + KneeRest) * Mathf.Deg2Rad);
            float walkY = Mathf.Max(LegDrop(hipL, kL), LegDrop(hipR, kR)) - rest;
            if (shift > 1e-5f || crouch > 1e-5f)
            {
                KeepAnkle(ref hipL, ref kL, shift, -crouch);
                KeepAnkle(ref hipR, ref kR, shift, -crouch);
            }
            PoseLeg(legL, kneeL, ankleL, -1, hipL, kL, _twistL);
            PoseLeg(legR, kneeR, ankleR, 1, hipR, kR, _twistR);
            float idle = (1 - walking) * (1 - bend) * (1 - a.armLift) * (1 - a.aimWeight);
            float breathe = 0.0022f * Mathf.Sin(a.time * 2.3f) * idle;   // a faint idle hum
            body.localPosition = new Vector3(0, walkY - crouch + breathe, -shift);   // Godot +Z (back) is Unity -Z
            body.localRotation = Quaternion.Euler(0, 0, s * 1.5f * walking);
            upper.localRotation = Quaternion.Euler(lean + 4 * a.gait - 3 * a.armHold * (1 - bend), 0, 0);   // leans back a touch under a load
            float armSwing = s * stride * 0.9f;
            float hold = Mathf.Max(a.armHold, a.armReach);
            float empty = 1 - hold;
            float pitch = ArmPitch(a.armHold, a.armLift, a.armReach);
            famArmPitch = pitch;
            float inward = 10 * hold;
            armL.localRotation = Quaternion.Euler(pitch - armSwing * empty, 0, inward - Flare * empty);
            armR.localRotation = Quaternion.Euler(pitch + armSwing * empty, 0, -inward + Flare * empty);
            PoseElbows(a, 38);
        }

        // the frame's inputs for the floor guard's trial poses (PoseForDepth)
        RobotAnim _fa; float _fStride, _fLift, _fWalking;
        protected override void PoseForDepth(float depth) => PoseBody(_fa, _fStride, _fLift, _fWalking, depth);

        /// <summary>Retrobot's head: looks into a coming turn, scans slowly while walking.</summary>
        static readonly HeadAttention Attention = new() { aheadMax = 35f, scanDeg = 7f, scanRate = 0.57f, follow = 3.5f };

        /// <summary>Retrobot's walk: short, quick mechanical steps whose stride is matched to the ground
        /// speed; the swinging leg lifts its boot, the ankles keep both boots flat, and the body rides
        /// exactly on the planted leg. Turning on the spot, the planted boot keeps its heading while the
        /// lifted one steps round. The 8 deg thigh splay holds throughout. Picking up: weight shift,
        /// crouch, moderate lean, reach; the claws stop at the part (floor guard). Depositing: the arms
        /// rise until the claws are at the destination (aim).</summary>
        public override void Animate(RobotAnim a)
        {
            float lh = HipY * Mathf.Max(1e-4f, transform.lossyScale.y);
            float walking = Mathf.Clamp01(a.gait / 0.45f);
            float turn01 = Mathf.Clamp01(Mathf.Abs(a.turnRate) / 120f);
            float stride = a.speed > 1e-3f ? Mathf.Min(MaxStride, a.speed / (lh * StepRate(a.speed01, a.speed)) * Mathf.Rad2Deg) * walking : 0;
            float lift = Mathf.Clamp01(Mathf.Max(stride, TurnStride * turn01 * walking) / (MaxStride * 0.6f));
            // turning on the spot: the planted boot holds its heading (it twists back against the body's
            // turn), the lifted boot swings round ahead of the turn; untwists when the turn ends
            if (a.dt > 0)
            {
                float turnW = Mathf.Clamp01(Mathf.Abs(a.turnRate) / 60f) * (1 - Mathf.Clamp01(a.speed01 * 4f)) * walking;
                float dYaw = a.turnRate * a.dt * turnW;
                bool plantedL = Mathf.Cos(a.phase) > 0;
                float ahead = Mathf.Clamp(a.turnRate * 0.1f, -22f, 22f) * turnW;
                float swing = 360f * a.dt;
                if (turnW > 0.01f)
                {
                    _twistL = plantedL ? _twistL - dYaw : Mathf.MoveTowards(_twistL, ahead, swing);
                    _twistR = plantedL ? Mathf.MoveTowards(_twistR, ahead, swing) : _twistR - dYaw;
                }
                else
                {
                    _twistL = Mathf.MoveTowards(_twistL, 0, 150f * a.dt);
                    _twistR = Mathf.MoveTowards(_twistR, 0, 150f * a.dt);
                }
                _twistL = Mathf.Clamp(_twistL, -30f, 30f);
                _twistR = Mathf.Clamp(_twistR, -30f, 30f);
            }
            PoseBody(a, stride, lift, walking, 1);
            _fa = a; _fStride = stride; _fLift = lift; _fWalking = walking;
            FloorGuard(a);
            PoseTelescopes(0);
            float hold = Mathf.Max(a.armHold, a.armReach);
            FamilyAim(a, ArmPitch(a.armHold, a.armLift, a.armReach), 10 * hold);
            FamilyGrip(a, 10 * Mathf.Max(a.armHold, a.armReach));
            FamilyClaws(a);
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
            FamilyLookHead(a, walking, Attention);
        }
    }
}
