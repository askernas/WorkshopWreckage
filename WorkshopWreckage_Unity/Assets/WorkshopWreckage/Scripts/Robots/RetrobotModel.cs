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
    /// side plates and a steel-and-rubber sole. Retrobot builds its own head and arms; the shared
    /// family kit (DetailedHead / DetailedArm) is left to the other robots.
    /// Parts: head, torso, left arm, right arm, leg assembly.</summary>
    public class RetrobotModel : RobotModel
    {
        public const string Head = "head", Torso = "torso", ArmL = "left arm", ArmR = "right arm", Legs = "leg assembly";
        public Transform upper, upperGeo, armL, armR, legL, legR;
        public override float ModelHeight => 1.06f;
        static Material Yellow => ToyYellow;
        static Material YellowDeep => ToyYellowDeep;
        const float HipY = 0.40f, ShoulderY = 0.65f, NeckY = 0.72f, ShoulderX = 0.24f, LegX = 0.15f;
        const float Flare = 11f;   // rest pose: the arms hang slightly out from the body, as on the sheet
        // proportions after the concept sheet: head 0.40 x 0.28 x 0.31, torso 0.31 x 0.27 x 0.25
        const float HeadW = 0.40f, HeadH = 0.28f, HeadD = 0.31f, TorsoW = 0.31f, TorsoD = 0.25f, TorsoBot = 0.43f, TorsoTop = 0.70f;

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

        /// <summary>Bright steel: shafts, piston rods, hub faces, grille slats (gunmetal in the legacy look).</summary>
        static Material Shaft => LegacyLook ? Gunmetal : RobotFinish.ShaftSteel;

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
            foreach (var (leg, s) in new[] { (legL, -1f), (legR, 1f) }) BuildLeg(leg, s);
            foreach (var (arm, s, part) in new[] { (armL, -1f, ArmL), (armR, 1f, ArmR) }) BuildArm(arm, s, part);
            BuildHead();
        }

        // ------------------------------------------------------------ mechanical building blocks

        static Vector2[] Hex(float r, float h) => new[] { new Vector2(r, 0), new Vector2(r, h), new Vector2(0, h) };
        static Vector3 X(float s) => new Vector3(s, 0, 0);

        /// <summary>A hex nut (axis given by the rotation) standing on a surface at c.</summary>
        static void Nut(Batch b, Vector3 c, Quaternion rot, float r, float h, Material m) => b.Lathe(c, Hex(r, h), m, rot, 6, true);

        /// <summary>A bearing hub on a face pointing along X (side s): a bright steel face plate, a
        /// ring of bolts, a centre nut with a brass cap.</summary>
        void Hub(Batch b, string P, Vector3 c, float s, float r, int bolts, float boltR = 0.0055f)
        {
            float h = r * 0.26f;
            b.Lathe(c, Disc(r, h, r * 0.1f), M(P, Shaft), SideRot(s), 24);
            var face = c + X(s * h);
            for (int i = 0; i < bolts; i++)
            {
                float a = (i + 0.5f) * Mathf.PI * 2 / bolts;
                Bolt(b, face + new Vector3(0, Mathf.Cos(a), Mathf.Sin(a)) * (r * 0.7f), X(s), boltR, M(P, Gunmetal));
            }
            Nut(b, face, SideRot(s), r * 0.32f, r * 0.18f, M(P, Gunmetal));
            b.Lathe(face + X(s * r * 0.18f), Dome(r * 0.17f, r * 0.12f), M(P, Brass), SideRot(s), 10);
        }

        /// <summary>A spur gear on an axis along X: a dark steel disc ringed by chamfered teeth.</summary>
        void Gear(Batch b, string P, Vector3 c, float r, float w, int teeth)
        {
            var m = M(P, Gunmetal);
            b.Lathe(c - X(w / 2), Disc(r * 0.88f, w, r * 0.06f), m, RightRot, 24);
            for (int t = 0; t < teeth; t++)
            {
                float a = t * Mathf.PI * 2 / teeth;
                b.ChamferBox(c + new Vector3(0, Mathf.Sin(a), Mathf.Cos(a)) * (r * 0.9f), new Vector3(w * 0.9f, r * 0.2f, r * 0.24f), r * 0.04f, m, Batch.R(Vector3.right, a));
            }
        }

        /// <summary>A framed, recessed grille on a front (zSign -1) or back (+1) face: dark recess, a
        /// gunmetal frame and bright steel slats (horizontal, or vertical for the mouth).</summary>
        void FramedGrille(Batch b, string P, Vector3 c, float w, float h, int slats, bool vertical, float zSign)
        {
            var o = new Vector3(0, 0, zSign);
            float f = Mathf.Min(w, h) * 0.14f;
            b.ChamferBox(c + o * 0.002f, new Vector3(w - f, h - f, 0.006f), 0.001f, M(P, Dark));
            foreach (var sy in new[] { -1f, 1f })
            {
                b.ChamferBox(c + new Vector3(0, sy * (h / 2 - f / 2), 0) + o * 0.005f, new Vector3(w, f, 0.01f), f * 0.3f, M(P, Gunmetal));
                b.ChamferBox(c + new Vector3(sy * (w / 2 - f / 2), 0, 0) + o * 0.005f, new Vector3(f, h, 0.01f), f * 0.3f, M(P, Gunmetal));
            }
            float iw = w - 2 * f, ih = h - 2 * f;
            for (int k = 0; k < slats; k++)
            {
                float t = (k + 0.5f) / slats - 0.5f;
                var p = c + (vertical ? new Vector3(t * iw, 0, 0) : new Vector3(0, t * ih, 0)) + o * 0.004f;
                var size = vertical ? new Vector3(iw / slats * 0.42f, ih * 0.92f, 0.006f) : new Vector3(iw * 0.96f, ih / slats * 0.42f, 0.006f);
                b.ChamferBox(p, size, 0.0015f, M(P, Shaft));
            }
        }

        // ------------------------------------------------------------ torso

        void BuildTorso()
        {
            var b = new Batch();
            string P = Torso;
            float cy = (TorsoBot + TorsoTop) / 2, H = TorsoTop - TorsoBot, front = -TorsoD / 2;
            // pelvis: a dark steel block with the hip axle through it and a drive gear in front
            b.ChamferBox(new Vector3(0, 0.405f, 0), new Vector3(0.2f, 0.065f, 0.17f), 0.012f, M(P, Gunmetal));
            b.Lathe(new Vector3(-0.115f, HipY, 0), Disc(0.024f, 0.23f, 0.004f), M(P, Shaft), RightRot, 14);   // hip axle
            b.Lathe(new Vector3(0, 0.4f, -0.085f), Disc(0.03f, 0.016f, 0.004f), M(P, Gunmetal), FwdRot, 18);
            Nut(b, new Vector3(0, 0.4f, -0.101f), FwdRot, 0.011f, 0.006f, M(P, Gunmetal));
            foreach (var x in new[] { -0.07f, 0.07f }) Bolt(b, new Vector3(x, 0.405f, -0.085f), new Vector3(0, 0, -1), 0.007f, M(P, Gunmetal));
            // body shell: chamfered pressed steel, with a raised front panel
            b.ChamferBox(new Vector3(0, cy, 0), new Vector3(TorsoW, H, TorsoD), 0.022f, M(P, Yellow));
            b.ChamferBox(new Vector3(0, cy + 0.008f, front - 0.004f), new Vector3(TorsoW - 0.05f, H - 0.06f, 0.012f), 0.007f, M(P, Yellow));
            b.ChamferBox(new Vector3(0, TorsoBot + 0.012f, 0), new Vector3(TorsoW + 0.008f, 0.022f, TorsoD + 0.008f), 0.008f, M(P, YellowDeep));   // bottom rim band
            FramedGrille(b, P, new Vector3(0, 0.625f, front - 0.01f), 0.14f, 0.085f, 6, false, -1);
            // lower handle plate with its slot
            b.ChamferBox(new Vector3(0, 0.515f, front - 0.014f), new Vector3(0.1f, 0.045f, 0.01f), 0.004f, M(P, YellowDeep));
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
            // back: bolted plate, vent and a lower handle plate; two cable runs into the pelvis
            PanelFrame(b, P, new Vector3(0, cy, 0), new Vector3(TorsoW, H, TorsoD), new Vector3(0, 0, 1), 0.034f);
            FramedGrille(b, P, new Vector3(0, 0.615f, -front + 0.001f), 0.15f, 0.09f, 6, false, 1);
            b.ChamferBox(new Vector3(0, 0.505f, -front + 0.005f), new Vector3(0.1f, 0.04f, 0.01f), 0.004f, M(P, YellowDeep));
            b.ChamferBox(new Vector3(0, 0.505f, -front + 0.01f), new Vector3(0.064f, 0.012f, 0.004f), 0.001f, M(P, Dark));
            foreach (var x in new[] { -0.07f, 0.07f })
                Pipe(b, P, new[] { new Vector3(x, 0.465f, -front), new Vector3(x, 0.445f, -front + 0.018f), new Vector3(x * 0.9f, 0.42f, 0.1f), new Vector3(x * 0.8f, 0.405f, 0.08f) }, 0.008f, Rubber, Brass);
            // top: collar plate and the neck (stacked steel rings the head turns on)
            b.ChamferBox(new Vector3(0, TorsoTop + 0.004f, 0), new Vector3(0.2f, 0.01f, 0.15f), 0.004f, M(P, Gunmetal));
            b.Lathe(new Vector3(0, TorsoTop + 0.008f, 0), Disc(0.048f, 0.008f, 0.002f), M(P, Gunmetal), null, 20);
            b.Lathe(new Vector3(0, TorsoTop + 0.016f, 0), Disc(0.036f, 0.01f, 0.002f), M(P, Shaft), null, 20);
            Done(b, upperGeo);
        }

        // ------------------------------------------------------------ head

        void BuildHead()
        {
            var b = new Batch();
            string P = Head;
            float W = HeadW, H = HeadH, D = HeadD, front = -D / 2;
            float z0 = front - 0.009f;   // the face plate's front surface
            // shell: chamfered box with a raised face plate
            b.ChamferBox(new Vector3(0, H / 2, 0), new Vector3(W, H, D), 0.03f, M(P, Yellow));
            b.ChamferBox(new Vector3(0, H / 2, front - 0.004f), new Vector3(W - 0.034f, H - 0.034f, 0.01f), 0.006f, M(P, Yellow));
            // face fasteners: a row along the top and one in each lower corner, clear of the mouth
            for (int k = 0; k < 6; k++) Bolt(b, new Vector3(-0.15f + k * 0.06f, H - 0.03f, z0), new Vector3(0, 0, -1), 0.006f, M(P, Gunmetal));
            foreach (var x in new[] { -W / 2 + 0.034f, W / 2 - 0.034f }) Bolt(b, new Vector3(x, 0.034f, z0), new Vector3(0, 0, -1), 0.006f, M(P, Gunmetal));
            // eyes: deep layered housings (dark steel ring, brass bezel, dark tube, recessed lamp)
            float eyeY = H * 0.58f, eyeX = W * 0.26f;
            foreach (var x in new[] { -eyeX, eyeX })
            {
                b.Lathe(new Vector3(x, eyeY, z0 + 0.012f), Ring(0.074f, 0.052f, 0.032f), M(P, Gunmetal), FwdRot, 32);
                b.Lathe(new Vector3(x, eyeY, z0 + 0.004f), Ring(0.055f, 0.043f, 0.016f), M(P, Brass), FwdRot, 32);
                b.Lathe(new Vector3(x, eyeY, z0 + 0.03f), Ring(0.044f, 0.038f, 0.034f), M(P, Dark), FwdRot, 28);
                b.Lathe(new Vector3(x, eyeY, z0 + 0.016f), Disc(0.04f, 0.004f, 0.001f), M(P, Dark), FwdRot, 24);   // lamp back plate
            }
            // mouth: a framed grille with vertical slats, low and centred
            FramedGrille(b, P, new Vector3(0, 0.046f, z0), 0.124f, 0.044f, 7, true, -1);
            // ears: red cups with bolted steel hubs on a dark flange
            float earY = H * 0.54f;
            foreach (var s in new[] { -1f, 1f })
            {
                b.Lathe(new Vector3(s * (W / 2 - 0.002f), earY, 0.0f), Disc(0.06f, 0.012f, 0.003f), M(P, Gunmetal), SideRot(s), 24);
                b.Lathe(new Vector3(s * (W / 2 + 0.01f), earY, 0.0f), Disc(0.072f, 0.046f, 0.013f), M(P, ToyRed), SideRot(s), 32);
                Hub(b, P, new Vector3(s * (W / 2 + 0.056f), earY, 0.0f), s, 0.046f, 6);
                // antenna: a bracket on the head side behind the ear, a steel rod, a red ball
                var bracket = new Vector3(s * (W / 2 + 0.012f), H * 0.84f, 0.06f);
                b.ChamferBox(bracket, new Vector3(0.024f, 0.034f, 0.034f), 0.005f, M(P, Gunmetal));
                b.Lathe(bracket + new Vector3(0, 0.017f, 0), Disc(0.01f, 0.012f, 0.002f), M(P, Shaft), null, 12);
                float rodH = H + 0.085f - bracket.y - 0.03f;
                b.Lathe(bracket + new Vector3(0, 0.029f, 0), Disc(0.0055f, rodH, 0.001f), M(P, Gunmetal), null, 8);
                b.Lathe(bracket + new Vector3(0, 0.029f + rodH - 0.004f, 0), Ball(0.022f), M(P, ToyRed), null, 16);
            }
            // beacon base: a wide dark ring and a bright steel collar
            b.Lathe(new Vector3(0, H, 0), Disc(0.054f, 0.014f, 0.003f), M(P, Gunmetal), null, 24);
            b.Lathe(new Vector3(0, H + 0.014f, 0), Disc(0.046f, 0.008f, 0.002f), M(P, Shaft), null, 24);
            // back vent and the bolted plates on the top, sides and back
            FramedGrille(b, P, new Vector3(0, H * 0.58f, -front + 0.001f), 0.2f, 0.095f, 7, false, 1);
            var hc = new Vector3(0, H / 2, 0);
            foreach (var n in new[] { Vector3.left, Vector3.right, Vector3.up, new Vector3(0, 0, 1) }) PanelFrame(b, P, hc, new Vector3(W, H, D), n, 0.042f);
            Done(b, head);

            if (Has(Head))
            {
                foreach (var x in new[] { -eyeX, eyeX })
                    eyes.Add(Separate(head, "G_eye", lb => lb.Lathe(new Vector3(x, eyeY, z0 + 0.012f), Dome(0.036f, 0.02f, 8), EyeOffMat, FwdRot, 28)));
                beacons.Add(Separate(head, "G_beacon", lb => lb.Lathe(new Vector3(0, H + 0.022f, 0), Dome(0.04f, 0.046f, 8), BeaconOffMat, null, 24)));
            }
        }

        // ------------------------------------------------------------ arms

        /// <summary>One arm from its shoulder pivot (side s). On the arm pivot: the shoulder drum
        /// (bearing ring, yellow drum, red cap, bolted hub) turning on the torso's collar, the upper
        /// arm and the elbow clevis with its pin. On the elbow pivot: the geared knuckle, the forearm,
        /// the wrist housing and the claw (twisted round the forearm so it opens towards the front and
        /// the side): two faceted red fingers on hinge hubs, with flush dark grip pads.</summary>
        void BuildArm(Transform arm, float s, string P)
        {
            var b = new Batch();
            // shoulder drum, centred on the pivot axis (X), so pitching the arm never moves it
            b.Lathe(X(-s * 0.05f), Disc(0.045f, 0.012f, 0.003f), M(P, Gunmetal), SideRot(s), 24);
            b.Lathe(X(-s * 0.038f), Disc(0.066f, 0.06f, 0.012f), M(P, Yellow), SideRot(s), 32);
            b.Lathe(X(s * 0.022f), Disc(0.064f, 0.02f, 0.01f), M(P, ToyRed), SideRot(s), 32);
            Hub(b, P, X(s * 0.042f), s, 0.04f, 6);
            // upper arm: a chamfered block under the drum, with a darker band
            b.ChamferBox(new Vector3(0, -0.103f, 0), new Vector3(0.09f, 0.08f, 0.092f), 0.014f, M(P, Yellow));
            b.ChamferBox(new Vector3(0, -0.074f, 0), new Vector3(0.096f, 0.014f, 0.098f), 0.004f, M(P, YellowDeep));
            Bolt(b, new Vector3(0, -0.11f, -0.046f), new Vector3(0, 0, -1), 0.006f, M(P, Gunmetal));
            // elbow clevis: two steel plates with rounded lugs either side of the knuckle, the pin through
            const float EY = -0.18f;
            foreach (var px in new[] { -0.04f, 0.04f })
            {
                b.ChamferBox(new Vector3(px, -0.155f, 0), new Vector3(0.012f, 0.05f, 0.056f), 0.004f, M(P, Gunmetal));
                b.Lathe(new Vector3(px - 0.006f, EY, 0), Disc(0.028f, 0.012f, 0.003f), M(P, Gunmetal), RightRot, 20);
            }
            b.Lathe(new Vector3(-0.058f, EY, 0), Disc(0.009f, 0.116f, 0.002f), M(P, Shaft), RightRot, 12);   // the pin
            foreach (var px in new[] { -1f, 1f }) Hub(b, P, new Vector3(px * 0.046f, EY, 0), px, 0.022f, 0);
            Done(b, arm);

            // ---- on the elbow pivot (coordinates relative to the elbow axle)
            var elbow = Elbow(arm, new Vector3(0, EY, 0), s);
            var f = new Batch();
            Gear(f, P, Vector3.zero, 0.03f, 0.062f, 12);   // the knuckle between the clevis plates
            // forearm: a narrow neck between the plates, the chamfered forearm, an access plate, a cuff
            f.ChamferBox(new Vector3(0, -0.032f, 0), new Vector3(0.058f, 0.04f, 0.058f), 0.008f, M(P, Gunmetal));
            f.ChamferBox(new Vector3(0, -0.094f, 0), new Vector3(0.1f, 0.104f, 0.1f), 0.016f, M(P, Yellow));
            f.ChamferBox(new Vector3(s * 0.052f, -0.092f, 0), new Vector3(0.006f, 0.064f, 0.068f), 0.003f, M(P, YellowDeep));
            foreach (var z in new[] { -0.022f, 0.022f }) Bolt(f, new Vector3(s * 0.055f, -0.092f, z), X(s), 0.0055f, M(P, Gunmetal));
            foreach (var x in new[] { -0.024f, 0.024f }) Bolt(f, new Vector3(x, -0.072f, -0.05f), new Vector3(0, 0, -1), 0.0055f, M(P, Gunmetal));
            f.ChamferBox(new Vector3(0, -0.15f, 0), new Vector3(0.104f, 0.016f, 0.104f), 0.004f, M(P, Gunmetal));
            // wrist housing with steel hubs
            var wrist = new Vector3(0, -0.172f, 0);
            f.Lathe(wrist - X(0.038f), Disc(0.028f, 0.076f, 0.004f), M(P, Gunmetal), RightRot, 18);
            foreach (var px in new[] { -1f, 1f }) Hub(f, P, wrist + X(px * 0.038f), px, 0.02f, 0);
            // claw, twisted round the forearm and scaled about the wrist
            const float cs = 1.28f;
            var tw = Quaternion.AngleAxis(s * 20f, Vector3.up);       // Godot-space points
            var twU = Quaternion.AngleAxis(-s * 20f, Vector3.up);     // the same twist for Unity-space rotations
            Vector3 C(float x, float y, float z) => wrist + tw * (new Vector3(x, y, z) * cs);
            Vector3 side = tw * Vector3.right;
            f.ChamferBox(C(0, -0.03f, 0), new Vector3(0.09f, 0.03f, 0.058f) * cs, 0.008f * cs, M(P, Gunmetal), twU);   // palm
            foreach (var fs in new[] { -1f, 1f })
            {
                Vector2 root = new(fs * 0.034f, -0.042f), knuckle = new(fs * 0.07f, -0.094f), tip = new(fs * 0.02f, -0.15f);
                Vector3 V3(Vector2 p) => C(p.x, p.y, 0);
                // faceted segments, tapering to the tip
                f.Loft(V3(root), V3(knuckle), new Vector2(0.04f, 0.052f) * cs, new Vector2(0.036f, 0.05f) * cs, 0.01f * cs, 0.01f * cs, M(P, ToyRed), side);
                f.Loft(V3(knuckle), V3(tip), new Vector2(0.036f, 0.05f) * cs, new Vector2(0.02f, 0.036f) * cs, 0.01f * cs, 0.006f * cs, M(P, ToyRed), side);
                // flush grip pads along the inner faces (a continuous matte contact surface)
                foreach (var (a, c, wa, wc) in new[] { (root, knuckle, 0.04f, 0.036f), (knuckle, tip, 0.036f, 0.02f) })
                {
                    var dir = (c - a).normalized;
                    var inward = new Vector2(dir.y, -dir.x);
                    if (Vector2.Dot(inward, new Vector2(-fs, 0)) < 0) inward = -inward;
                    var a2 = a + dir * 0.008f + inward * (wa / 2 + 0.001f);
                    var c2 = c - dir * 0.006f + inward * (wc / 2 + 0.001f);
                    f.Loft(V3(a2), V3(c2), new Vector2(0.004f, 0.036f) * cs, new Vector2(0.004f, 0.028f) * cs, 0.001f, 0.001f, M(P, Rubber), side);
                }
                // hinge hubs at the root and the knuckle (axis across the claw)
                foreach (var (p, r) in new[] { (root, 0.016f), (knuckle, 0.017f) })
                {
                    f.Lathe(V3(p) + tw * new Vector3(0, 0, -0.03f * cs), Disc(r * cs, 0.06f * cs, 0.003f), M(P, Shaft), twU * BackRot, 16);
                    foreach (var zs in new[] { -1f, 1f })
                        f.Lathe(V3(p) + tw * new Vector3(0, 0, zs * 0.03f * cs), Dome(r * 0.45f * cs, 0.006f * cs), M(P, Brass), twU * (zs < 0 ? FwdRot : BackRot), 10);
                }
            }
            Done(f, elbow);
        }

        // ------------------------------------------------------------ legs

        /// <summary>One leg from its hip pivot (side s): geared hip on a bearing housing, thigh, knee
        /// axle with side hubs and the red front disc, shin with pistons and springs behind, ankle
        /// hubs and the boot (sloped toe with a grille, red heel cap, side plate, steel and rubber sole).</summary>
        void BuildLeg(Transform leg, float s)
        {
            var b = new Batch();
            string P = Legs;
            // hip: bearing housing against the pelvis, the gear, an outer hub (all on the pivot axis)
            b.Lathe(X(-s * 0.05f), Disc(0.045f, 0.03f, 0.004f), M(P, Gunmetal), SideRot(s), 22);
            Gear(b, P, Vector3.zero, 0.05f, 0.04f, 14);
            Hub(b, P, X(s * 0.02f), s, 0.04f, 5);
            // thigh
            b.ChamferBox(new Vector3(0, -0.085f, 0), new Vector3(0.11f, 0.09f, 0.11f), 0.016f, M(P, Yellow));
            b.ChamferBox(new Vector3(0, -0.05f, 0), new Vector3(0.116f, 0.014f, 0.116f), 0.004f, M(P, YellowDeep));
            foreach (var z in new[] { -0.028f, 0.028f }) Bolt(b, new Vector3(s * 0.055f, -0.09f, z), X(s), 0.0058f, M(P, Gunmetal));
            // knee: axle housing, steel side hubs, the red front disc with a steel centre
            const float KY = -0.15f;
            b.Lathe(new Vector3(-0.06f, KY, 0), Disc(0.044f, 0.12f, 0.006f), M(P, Gunmetal), RightRot, 22);
            foreach (var hs in new[] { -1f, 1f }) Hub(b, P, new Vector3(hs * 0.06f, KY, 0), hs, 0.042f, hs == s ? 5 : 0);
            b.Lathe(new Vector3(0, KY, -0.048f), Disc(0.036f, 0.016f, 0.005f), M(P, ToyRed), FwdRot, 26);
            b.Lathe(new Vector3(0, KY, -0.064f), Disc(0.018f, 0.006f, 0.002f), M(P, Shaft), FwdRot, 16);
            b.Lathe(new Vector3(0, KY, -0.07f), Dome(0.009f, 0.006f), M(P, Brass), FwdRot, 10);
            // shin: a yellow front shell; pistons with springs exposed behind
            b.ChamferBox(new Vector3(0, -0.22f, -0.012f), new Vector3(0.1f, 0.1f, 0.075f), 0.014f, M(P, Yellow));
            foreach (var x in new[] { -0.026f, 0.026f }) Bolt(b, new Vector3(x, -0.235f, -0.05f), new Vector3(0, 0, -1), 0.0055f, M(P, Gunmetal));
            foreach (var x in new[] { -0.024f, 0.024f })
            {
                b.Lathe(new Vector3(x, -0.215f, 0.036f), Disc(0.013f, 0.045f, 0.003f), M(P, Gunmetal), null, 12);   // cylinder
                b.Lathe(new Vector3(x, -0.283f, 0.036f), Disc(0.0065f, 0.07f, 0.001f), M(P, Shaft), null, 10);     // rod
                for (int k = 0; k < 5; k++)   // spring coils round the rod
                    b.Lathe(new Vector3(x, -0.275f + k * 0.011f, 0.036f), Disc(0.0125f, 0.004f, 0.0015f), M(P, Dark), null, 12);
            }
            // ankle
            const float AY = -0.295f;
            b.Lathe(new Vector3(-0.055f, AY, 0), Disc(0.034f, 0.11f, 0.005f), M(P, Gunmetal), RightRot, 20);
            foreach (var hs in new[] { -1f, 1f }) Hub(b, P, new Vector3(hs * 0.055f, AY, 0), hs, 0.03f, 0);
            // boot: ankle housing, body, sloped toe with a grille, red heel cap, side plate, sole
            b.ChamferBox(new Vector3(0, -0.336f, 0.005f), new Vector3(0.2f, 0.084f, 0.22f), 0.02f, M(P, Yellow));
            b.Loft(new Vector3(0, -0.338f, -0.1f), new Vector3(0, -0.355f, -0.19f), new Vector2(0.196f, 0.08f), new Vector2(0.19f, 0.046f), 0.018f, 0.012f, M(P, Yellow));
            FramedGrille(b, P, new Vector3(0, -0.355f, -0.191f), 0.12f, 0.032f, 3, false, -1);
            b.ChamferBox(new Vector3(0, -0.342f, 0.13f), new Vector3(0.19f, 0.07f, 0.05f), 0.012f, M(P, ToyRed));
            b.ChamferBox(new Vector3(s * 0.101f, -0.338f, 0.0f), new Vector3(0.006f, 0.05f, 0.12f), 0.003f, M(P, YellowDeep));
            foreach (var z in new[] { -0.04f, 0.04f }) Bolt(b, new Vector3(s * 0.104f, -0.338f, z), X(s), 0.006f, M(P, Gunmetal));
            b.ChamferBox(new Vector3(0, -0.388f, -0.025f), new Vector3(0.214f, 0.02f, 0.36f), 0.006f, M(P, Gunmetal));
            b.ChamferBox(new Vector3(0, -0.398f, -0.025f), new Vector3(0.206f, 0.01f, 0.35f), 0.003f, M(P, Rubber));
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
