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
        public Transform upper, upperGeo, armL, armR, legL, legR, kneeL, kneeR, ankleL, ankleR;
        public override float ModelHeight => 1.06f;
        static Material Yellow => ToyYellow;
        static Material YellowDeep => ToyYellowDeep;
        const float HipY = 0.40f, ShoulderY = 0.65f, NeckY = 0.72f, ShoulderX = 0.24f, LegX = 0.12f;
        const float Flare = 11f;   // rest pose: the arms hang slightly out from the body, as on the sheet
        // the sheet's stance: thigh 10 deg forward, shin 29 deg back, foot flat (ankle 0.115 above the floor)
        const float ThighL = 0.133f, ShinL = 0.176f, HipRest = -10f, KneeRest = 39f, AnkleRest = -29f;
        // proportions after the concept sheet: head 0.40 x 0.28 x 0.31, torso 0.31 x 0.27 x 0.25
        const float HeadW = 0.39f, HeadH = 0.28f, HeadD = 0.32f, TorsoW = 0.31f, TorsoD = 0.25f, TorsoBot = 0.43f, TorsoTop = 0.70f;

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
                [ToyYellow] = RobotFinish.Paint(YellowC, 1f, 0.55f, 0.55f, 0.85f, 0.18f, 0.45f, 0.1f),
                [ToyYellowDeep] = RobotFinish.Paint(YellowDeepC, 1f, 0.55f, 0.55f, 0.85f, 0.25f, 0.35f, 0.1f),
                [ToyRed] = RobotFinish.Paint(RedC, 1f, 0.5f, 0.5f, 0.8f, 0.2f, 0.2f, 0.08f),
                [Gunmetal] = RobotFinish.JointSteel,
                [Dark] = RobotFinish.Blackened,
                [Brass] = RobotFinish.BearingBrass,
                [Rubber] = RobotFinish.Rubber,
                [StainMat] = RobotFinish.OilStain,
            };
            return _skin.TryGetValue(m, out var s) ? s : m;
        }

        protected override Material BoltSkin(Material m) => !LegacyLook && m == RobotFinish.JointSteel ? RobotFinish.BoltSteel : m;

        static readonly Color YellowC = new(0.8f, 0.55f, 0.12f), YellowDeepC = new(0.68f, 0.45f, 0.09f), RedC = new(0.68f, 0.1f, 0.06f);
        /// <summary>The boots and heel caps: the most worn paint on the robot (scuffed, chipped, dirty).</summary>
        static Material FootPaint => LegacyLook ? Yellow : RobotFinish.Paint(YellowC, 1f, 0.85f, 0.75f, 1f, 0.45f, 0.45f, 0.1f);
        static Material HeelPaint => LegacyLook ? ToyRed : RobotFinish.Paint(RedC, 1f, 0.8f, 0.7f, 1f, 0.7f, 0.2f, 0.08f);
        static Material Grease => LegacyLook ? StainMat : RobotFinish.Grease;

        /// <summary>A grease run leaking down a vertical face from a joint or fastener.</summary>
        void GreaseRun(Batch b, string P, Vector3 p, Vector3 n, float len, float w = 0.013f) => RustStreak(b, P, p, n, len, w, Grease);

        /// <summary>A band of dirt along the bottom of a face (normal n, running along u from its
        /// centre p at the bottom edge): three stacked multiply strips, darkest at the bottom.</summary>
        void GrimeBand(Batch b, string P, Vector3 p, Vector3 n, Vector3 u, float length, float height)
        {
            if (assembling) return;   // no dirt on ghosts
            var m = M(P, Grease);
            if (m == null) return;
            var rot = Quaternion.LookRotation(Batch.G(u.normalized), Batch.G(n.normalized));
            var up = Vector3.Cross(u.normalized, n.normalized);
            if (up.y < 0) up = -up;
            for (int i = 0; i < 3; i++)
            {
                float h = height * (1 - i * 0.32f);
                b.Box(p + up * (h / 2) + n.normalized * 0.0006f, new Vector3(h, 0.001f, length * (1 - i * 0.08f)), m, false, rot);
            }
        }

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
            // pelvis: a wide, deep dark steel housing under the torso; the hips turn inside it on one
            // axle, so the legs come out from beneath the body instead of being bolted to its sides
            b.ChamferBox(new Vector3(0, 0.403f, 0.005f), new Vector3(0.27f, 0.08f, 0.2f), 0.018f, M(P, Gunmetal), null, true);
            b.Lathe(new Vector3(-LegX - 0.03f, HipY, 0), Disc(0.022f, 2 * LegX + 0.06f, 0.004f), M(P, Shaft), RightRot, 14);   // hip axle
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

        // ------------------------------------------------------------ head

        /// <summary>The beacon lamp: a red cylinder with a rounded top (lathe profile, bottom open).</summary>
        static Vector2[] BeaconLamp(float r, float h, float round)
        {
            var p = new List<Vector2> { new(r, 0), new(r, h - round) };
            for (int i = 1; i <= 5; i++)
            {
                float a = i / 5f * Mathf.PI / 2;
                p.Add(new Vector2(r - round + Mathf.Cos(a) * round, h - round + Mathf.Sin(a) * round));
            }
            p.Add(new Vector2(0, h));
            return p.ToArray();
        }

        /// <summary>The head-side housing (the "ear"): a tapered red frustum from a wide base on the
        /// head to a smaller outer face with a raised rim (lathe profile, axis outwards).</summary>
        static Vector2[] EarFrustum(float rBase, float rOuter, float depth) => new[]
        {
            new Vector2(rBase, 0), new Vector2(rBase, 0.004f), new Vector2(rOuter + 0.004f, depth - 0.008f), new Vector2(rOuter, depth - 0.003f),
            new Vector2(rOuter - 0.004f, depth), new Vector2(rOuter - 0.01f, depth), new Vector2(rOuter - 0.012f, depth - 0.004f), new Vector2(0, depth - 0.004f),
        };

        void BuildHead()
        {
            var b = new Batch();
            string P = Head;
            float W = HeadW, H = HeadH, D = HeadD, front = -D / 2;
            float z0 = front - 0.01f;   // the face plate's front surface
            // shell: a rounded pressed-steel box (softer corners, as on the sheet) with a raised face plate
            b.RoundedBox(new Vector3(0, H / 2, 0), new Vector3(W, H, D), 0.042f, M(P, Yellow), 3);
            b.RoundedBox(new Vector3(0, H / 2, front - 0.004f), new Vector3(W - 0.042f, H - 0.042f, 0.012f), 0.012f, M(P, Yellow), 2);
            // face fasteners: a row along the top and one in each lower corner, clear of the mouth
            for (int k = 0; k < 6; k++) Bolt(b, new Vector3(-0.145f + k * 0.058f, H - 0.034f, z0), new Vector3(0, 0, -1), 0.0062f, M(P, Gunmetal));
            foreach (var x in new[] { -W / 2 + 0.036f, W / 2 - 0.036f }) Bolt(b, new Vector3(x, 0.036f, z0), new Vector3(0, 0, -1), 0.0062f, M(P, Gunmetal));
            // eyes: a thick dark steel housing, a warm brass bezel, a polished reflector cone narrowing into
            // the head and a small lamp set deep inside it
            float eyeY = H * 0.46f, eyeX = 0.097f;
            foreach (var x in new[] { -eyeX, eyeX })
            {
                b.Lathe(new Vector3(x, eyeY, z0 + 0.01f), Ring(0.07f, 0.05f, 0.044f), M(P, Gunmetal), FwdRot, 32);
                b.Lathe(new Vector3(x, eyeY, z0 - 0.008f), Ring(0.053f, 0.042f, 0.014f), M(P, Brass), FwdRot, 32);
                b.Lathe(new Vector3(x, eyeY, z0 - 0.001f), new[] { new Vector2(0.026f, 0), new Vector2(0.043f, 0.02f), new Vector2(0.043f, 0.022f) }, M(P, Brass), FwdRot, 28, true);   // reflector
                b.Lathe(new Vector3(x, eyeY, z0 + 0.001f), Disc(0.03f, 0.003f, 0.001f), M(P, Dark), FwdRot, 20);   // lamp socket
            }
            // mouth: a framed grille with vertical slats, low and centred
            FramedGrille(b, P, new Vector3(0, 0.04f, z0), 0.104f, 0.04f, 7, true, -1);
            // head-side housings: steel flange, red frustum, raised rim, bolted steel bearing plate;
            // the antenna rises from a collar on top of each housing
            float earY = H * 0.47f;
            foreach (var s in new[] { -1f, 1f })
            {
                float x0 = s * (W / 2 - 0.004f);
                b.Lathe(new Vector3(x0, earY, 0), Disc(0.086f, 0.01f, 0.003f), M(P, Gunmetal), SideRot(s), 28);
                b.Lathe(new Vector3(x0 + s * 0.01f, earY, 0), EarFrustum(0.075f, 0.058f, 0.056f), M(P, ToyRed), SideRot(s), 28, true);
                Hub(b, P, new Vector3(x0 + s * 0.062f, earY, 0), s, 0.036f, 6, 0.005f);
                var collar = new Vector3(s * (W / 2 + 0.036f), earY + 0.064f, 0);
                b.Lathe(collar - new Vector3(0, 0.006f, 0), Disc(0.016f, 0.02f, 0.003f), M(P, Gunmetal), null, 14);
                b.Lathe(collar + new Vector3(0, 0.014f, 0), Disc(0.012f, 0.008f, 0.002f), M(P, Shaft), null, 12);
                float top = H + 0.07f;
                b.Lathe(collar + new Vector3(0, 0.022f, 0), Disc(0.0065f, top - collar.y - 0.022f - 0.02f, 0.001f), M(P, Gunmetal), null, 10);
                b.Lathe(new Vector3(collar.x, top - 0.029f, 0), Ball(0.029f), M(P, ToyRed), null, 18);
            }
            foreach (var s in new[] { -1f, 1f })
                foreach (var z in new[] { -0.03f, 0.025f })
                    GreaseRun(b, P, new Vector3(s * W / 2, earY - 0.08f, z), X(s), 0.05f, 0.012f);
            // beacon: a dark mounting base and a steel collar; the red lamp itself is separate (it lights up)
            b.Lathe(new Vector3(0, H - 0.004f, 0), Disc(0.064f, 0.026f, 0.005f), M(P, Gunmetal), null, 28);
            b.Lathe(new Vector3(0, H + 0.022f, 0), Disc(0.055f, 0.008f, 0.002f), M(P, Shaft), null, 28);
            // back vent and the bolted plates on the top, sides and back
            FramedGrille(b, P, new Vector3(0, H * 0.56f, -front + 0.001f), 0.2f, 0.095f, 7, false, 1);
            var hc = new Vector3(0, H / 2, 0);
            foreach (var n in new[] { Vector3.left, Vector3.right, Vector3.up, new Vector3(0, 0, 1) }) PanelFrame(b, P, hc, new Vector3(W, H, D), n, 0.054f);
            Done(b, head);

            if (Has(Head))
            {
                foreach (var x in new[] { -eyeX, eyeX })
                    eyes.Add(Separate(head, "G_eye", lb => lb.Lathe(new Vector3(x, eyeY, z0 - 0.002f), Dome(0.027f, 0.012f, 8), EyeOffMat, FwdRot, 24)));
                beacons.Add(Separate(head, "G_beacon", lb => lb.Lathe(new Vector3(0, H + 0.03f, 0), BeaconLamp(0.05f, 0.07f, 0.02f), BeaconOffMat, null, 28)));
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
            b.ChamferBox(new Vector3(0, -0.103f, 0), new Vector3(0.09f, 0.08f, 0.092f), 0.014f, M(P, Yellow), null, true);
            b.ChamferBox(new Vector3(0, -0.074f, 0), new Vector3(0.096f, 0.014f, 0.098f), 0.004f, M(P, YellowDeep), null, true);
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
            f.ChamferBox(new Vector3(0, -0.094f, 0), new Vector3(0.1f, 0.104f, 0.1f), 0.016f, M(P, Yellow), null, true);
            f.ChamferBox(new Vector3(s * 0.052f, -0.092f, 0), new Vector3(0.006f, 0.064f, 0.068f), 0.003f, M(P, YellowDeep), null, true);
            foreach (var z in new[] { -0.022f, 0.022f }) Bolt(f, new Vector3(s * 0.055f, -0.092f, z), X(s), 0.0055f, M(P, Gunmetal));
            foreach (var x in new[] { -0.024f, 0.024f }) Bolt(f, new Vector3(x, -0.072f, -0.05f), new Vector3(0, 0, -1), 0.0055f, M(P, Gunmetal));
            f.ChamferBox(new Vector3(0, -0.15f, 0), new Vector3(0.104f, 0.016f, 0.104f), 0.004f, M(P, Gunmetal));
            GreaseRun(f, P, new Vector3(s * 0.05f, -0.045f, 0.025f), X(s), 0.06f);
            GreaseRun(f, P, new Vector3(0.015f, -0.045f, -0.05f), new Vector3(0, 0, -1), 0.045f);
            // wrist housing with steel hubs
            var wrist = new Vector3(0, -0.172f, 0);
            f.Lathe(wrist - X(0.038f), Disc(0.028f, 0.076f, 0.004f), M(P, Gunmetal), RightRot, 18);
            foreach (var px in new[] { -1f, 1f }) Hub(f, P, wrist + X(px * 0.038f), px, 0.02f, 0);
            // claw, twisted round the forearm and scaled about the wrist
            const float cs = 1.08f;
            var tw = Quaternion.AngleAxis(s * 20f, Vector3.up);       // Godot-space points
            var twU = Quaternion.AngleAxis(-s * 20f, Vector3.up);     // the same twist for Unity-space rotations
            Vector3 C(float x, float y, float z) => wrist + tw * (new Vector3(x, y, z) * cs);
            Vector3 side = tw * Vector3.right;
            f.ChamferBox(C(0, -0.032f, 0), new Vector3(0.094f, 0.034f, 0.062f) * cs, 0.009f * cs, M(P, Gunmetal), twU);   // palm
            foreach (var fs in new[] { -1f, 1f })
            {
                // the finger's spine: a short thick proximal segment out to the knuckle, then a long distal
                // segment that curves down and hooks in (C-shaped grip), tapering towards the tip
                var path = new[] { new Vector2(0.034f, -0.044f), new Vector2(0.074f, -0.078f), new Vector2(0.094f, -0.128f), new Vector2(0.08f, -0.174f), new Vector2(0.04f, -0.2f) };
                float[] wd = { 0.044f, 0.04f, 0.035f, 0.028f, 0.019f }, dp = { 0.056f, 0.054f, 0.049f, 0.043f, 0.036f };
                Vector3 V3(Vector2 p) => C(fs * p.x, p.y, 0);
                for (int i = 0; i < path.Length - 1; i++)
                {
                    // the distal pieces overlap a little at the bends, so the finger reads as one curved hook
                    var d = (path[i + 1] - path[i]).normalized;
                    var a0 = i >= 2 ? path[i] - d * (wd[i] * 0.35f) : path[i];
                    var a1 = i >= 1 && i < path.Length - 2 ? path[i + 1] + d * (wd[i + 1] * 0.35f) : path[i + 1];
                    f.Loft(V3(a0), V3(a1), new Vector2(wd[i], dp[i]) * cs, new Vector2(wd[i + 1], dp[i + 1]) * cs, 0.011f * cs, (i == path.Length - 2 ? 0.006f : 0.01f) * cs, M(P, ToyRed), side);
                }
                // a continuous, flush matte grip strip along the inside of the distal segment
                for (int i = 1; i < path.Length - 1; i++)
                {
                    var a = new Vector2(fs * path[i].x, path[i].y); var c = new Vector2(fs * path[i + 1].x, path[i + 1].y);
                    var dir = (c - a).normalized;
                    var inward = new Vector2(dir.y, -dir.x);
                    if (Vector2.Dot(inward, new Vector2(-fs, -0.3f)) < 0) inward = -inward;
                    var a2 = a + inward * (wd[i] / 2 + 0.0008f) + (i == 1 ? dir * 0.01f : Vector2.zero);
                    var c2 = c + inward * (wd[i + 1] / 2 + 0.0008f) - (i == path.Length - 2 ? dir * 0.008f : Vector2.zero);
                    f.Loft(C(a2.x, a2.y, 0), C(c2.x, c2.y, 0), new Vector2(0.0035f, dp[i] * 0.62f) * cs, new Vector2(0.0035f, dp[i + 1] * 0.62f) * cs, 0.0008f, 0.0008f, M(P, Rubber), side);
                }
                // hinge hubs at the root and the knuckle (axis across the claw)
                foreach (var (p, r) in new[] { (path[0], 0.018f), (path[1], 0.019f) })
                {
                    f.Lathe(V3(p) + tw * new Vector3(0, 0, -0.033f * cs), Disc(r * cs, 0.066f * cs, 0.003f), M(P, Shaft), twU * BackRot, 18);
                    foreach (var zs in new[] { -1f, 1f })
                        f.Lathe(V3(p) + tw * new Vector3(0, 0, zs * 0.033f * cs), Dome(r * 0.45f * cs, 0.006f * cs), M(P, Brass), twU * (zs < 0 ? FwdRot : BackRot), 10);
                }
            }
            Done(f, elbow);
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

        public override void Animate(RobotAnim a)
        {
            float s = Mathf.Sin(a.phase);
            float swing = s * 28 * a.gait;
            // bending down to pick something up: thigh forward, knee folds, ankle keeps the foot flat
            float bend = a.bend;
            legL.localRotation = Quaternion.Euler(HipRest + swing - 30 * bend, 0, 0);
            legR.localRotation = Quaternion.Euler(HipRest - swing - 30 * bend, 0, 0);
            kneeL.localRotation = kneeR.localRotation = Quaternion.Euler(KneeRest + 60 * bend, 0, 0);
            ankleL.localRotation = ankleR.localRotation = Quaternion.Euler(AnkleRest - 30 * bend, 0, 0);
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
