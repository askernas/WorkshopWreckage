using System.Collections.Generic;
using UnityEngine;

namespace WorkshopWreckage
{
    /// <summary>The robot family's standard component catalogue (docs/ROBOT_FAMILY_DESIGN_AND_ANIMATION_STANDARD.md):
    /// the parts one fictional manufacturer reuses across its models, first built for Retrobot (F4-F7).
    /// Family finish (FamilySkin), hardware (Nut, Hub, Gear, FramedGrille), wear (GreaseRun, GrimeBand),
    /// the family head module (FamilyHead) and the family arm with its articulated red gripper (FamilyArm,
    /// PoseClaws). A model opts in by calling these; robots that don't keep their older kit (DetailedHead,
    /// DetailedArm). Coordinates are layout-local (front = -Z), as everywhere in the robot models.</summary>
    public abstract partial class RobotModel
    {
        // ------------------------------------------------------------ finish

        public static readonly Color FamilyYellow = new(0.8f, 0.55f, 0.12f), FamilyYellowDeep = new(0.68f, 0.45f, 0.09f), FamilyRed = new(0.68f, 0.1f, 0.06f);
        /// <summary>Deep red of the family paint (Hoverbot's lower panels, arm plates).</summary>
        public static readonly Color FamilyRedDeep = new(0.55f, 0.08f, 0.05f);
        /// <summary>Variant body paint (Unibot's olive), same finish and wear.</summary>
        public static readonly Color FamilyGreen = new(0.33f, 0.38f, 0.18f), FamilyGreenDeep = new(0.26f, 0.3f, 0.14f);
        /// <summary>Body paints of the robots that don't wear the kit's yellow or olive (FamilySkin(m, paint)):
        /// Retrobot's industrial blue #326A9B, Spiderbot's muted violet #76529B, the Sorting Arm's orange #D97724.</summary>
        public static readonly Color FamilyBlue = new(0.196f, 0.416f, 0.608f), FamilyViolet = new(0.51f, 0.35f, 0.6f), FamilyOrange = new(0.85f, 0.36f, 0.04f);
        static Dictionary<Material, Material> _familySkin;

        /// <summary>The family finish (RobotFinish on WWRobot.shader): the kit's paints and metals mapped to
        /// enamel with construction-led wear and told-apart steels. A model opts in with
        /// `protected override Material Skin(Material m) => FamilySkin(m);`.</summary>
        protected static Material FamilySkin(Material m)
        {
            if (m == null) return null;
            _familySkin ??= new Dictionary<Material, Material>
            {
                [ToyYellow] = RobotFinish.Paint(FamilyYellow, 1f, 0.55f, 0.55f, 0.85f, 0.18f, 0.45f, 0.1f),
                [ToyYellowDeep] = RobotFinish.Paint(FamilyYellowDeep, 1f, 0.55f, 0.55f, 0.85f, 0.25f, 0.35f, 0.1f),
                [ToyRed] = RobotFinish.Paint(FamilyRed, 1f, 0.5f, 0.5f, 0.8f, 0.2f, 0.2f, 0.08f),
                [ToyRedDeep] = RobotFinish.Paint(FamilyRedDeep, 1f, 0.5f, 0.5f, 0.8f, 0.25f, 0.2f, 0.08f),
                [ToyGreen] = RobotFinish.Paint(FamilyGreen, 1f, 0.55f, 0.55f, 0.85f, 0.18f, 0.4f, 0.1f),
                [ToyGreenDeep] = RobotFinish.Paint(FamilyGreenDeep, 1f, 0.55f, 0.55f, 0.85f, 0.25f, 0.35f, 0.1f),
                [Gunmetal] = RobotFinish.JointSteel,
                [Dark] = RobotFinish.Blackened,
                [Brass] = RobotFinish.BearingBrass,
                [Rubber] = RobotFinish.Rubber,
                [StainMat] = RobotFinish.OilStain,
            };
            return _familySkin.TryGetValue(m, out var s) ? s : m;
        }

        /// <summary>The family finish in a robot's own body colour: the kit's body paints (yellow, olive) come
        /// out in `paint` and their deep trim tones in a darker shade of it; red, metals and wear stay the
        /// family's. Shared components (FamilyArm, FamilyHead) are built from the kit paints, so the same arm
        /// is blue on Retrobot and violet on Spiderbot without a material of its own. A model opts in with
        /// `protected override Material Skin(Material m) => FamilySkin(m, FamilyBlue);`.</summary>
        protected static Material FamilySkin(Material m, Color paint)
        {
            if (m == ToyYellow || m == ToyGreen) return RobotFinish.Paint(paint, 1f, 0.55f, 0.55f, 0.85f, 0.18f, 0.45f, 0.1f);
            if (m == ToyYellowDeep || m == ToyGreenDeep) return RobotFinish.Paint(DeepTone(paint), 1f, 0.55f, 0.55f, 0.85f, 0.25f, 0.35f, 0.1f);
            return FamilySkin(m);
        }

        /// <summary>The darker trim tone of a body paint (as the family's deep yellow is to its yellow).</summary>
        public static Color DeepTone(Color c) => new(c.r * 0.82f, c.g * 0.8f, c.b * 0.78f);

        /// <summary>True when the chassis owns the mount on top of the head (Hoverbot's propeller): any head
        /// fitted to it leaves its own top accessory (beacon, lamp) off, so the two never overlap.</summary>
        protected virtual bool HeadTopTaken => false;

        /// <summary>Bright steel of the kit (shafts, hub faces, grille slats, hinge pins).</summary>
        protected virtual Material BrightSteel => RobotFinish.ShaftSteel;
        /// <summary>The grease and dirt stain of the kit's wear.</summary>
        protected virtual Material GreaseMat => RobotFinish.Grease;

        // ------------------------------------------------------------ hardware

        protected static Vector2[] Hex(float r, float h) => new[] { new Vector2(r, 0), new Vector2(r, h), new Vector2(0, h) };
        protected static Vector3 X(float s) => new Vector3(s, 0, 0);

        /// <summary>A hex nut (axis given by the rotation) standing on a surface at c.</summary>
        protected static void Nut(Batch b, Vector3 c, Quaternion rot, float r, float h, Material m) => b.Lathe(c, Hex(r, h), m, rot, 6, true);

        /// <summary>A bearing hub on a face pointing along X (side s): a bright steel face plate, a
        /// ring of bolts, a centre nut with a brass cap.</summary>
        protected void Hub(Batch b, string P, Vector3 c, float s, float r, int bolts, float boltR = 0.0055f)
        {
            float h = r * 0.26f;
            b.Lathe(c, Disc(r, h, r * 0.1f), M(P, BrightSteel), SideRot(s), 24);
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
        protected void Gear(Batch b, string P, Vector3 c, float r, float w, int teeth)
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
        /// gunmetal frame and bright steel slats (horizontal, or vertical for a mouth).</summary>
        protected void FramedGrille(Batch b, string P, Vector3 c, float w, float h, int slats, bool vertical, float zSign)
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
                b.ChamferBox(p, size, 0.0015f, M(P, BrightSteel));
            }
        }

        /// <summary>The beacon lamp: a red cylinder with a rounded top (lathe profile, bottom open).</summary>
        protected static Vector2[] BeaconLamp(float r, float h, float round)
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
        protected static Vector2[] EarFrustum(float rBase, float rOuter, float depth) => new[]
        {
            new Vector2(rBase, 0), new Vector2(rBase, 0.004f), new Vector2(rOuter + 0.004f, depth - 0.008f), new Vector2(rOuter, depth - 0.003f),
            new Vector2(rOuter - 0.004f, depth), new Vector2(rOuter - 0.01f, depth), new Vector2(rOuter - 0.012f, depth - 0.004f), new Vector2(0, depth - 0.004f),
        };

        // ------------------------------------------------------------ wear

        /// <summary>A grease run leaking down a vertical face from a joint or fastener.</summary>
        protected void GreaseRun(Batch b, string P, Vector3 p, Vector3 n, float len, float w = 0.013f) => RustStreak(b, P, p, n, len, w, GreaseMat);

        /// <summary>A band of dirt along the bottom of a face (normal n, running along u from its
        /// centre p at the bottom edge): three stacked multiply strips, darkest at the bottom.</summary>
        protected void GrimeBand(Batch b, string P, Vector3 p, Vector3 n, Vector3 u, float length, float height)
        {
            if (assembling) return;   // no dirt on ghosts
            var m = M(P, GreaseMat);
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

        // ------------------------------------------------------------ the family head module

        /// <summary>Dimensions of a family head (model units). Retrobot: 0.39 x 0.28 x 0.32, eyes +-0.097
        /// at 0.46 of the height, ear housings at 0.47.</summary>
        public struct HeadSpec
        {
            public float w, h, d, eyeX, eyeY, earY;
            public Material paint;
            public bool beacon, antennas, mouth;
            /// <summary>1 = one big central lamp eye (Unibot); 0 or 2 = two eyes (Retrobot).</summary>
            public int eyes;
            /// <summary>Eye size factor (0 = 1).</summary>
            public float eyeScale;
            /// <summary>Assembly parts of the eye(s) and the antennas (null = the head's part).</summary>
            public string eyePart, antennaPart;
            /// <summary>Assembly part of the beacon lamp (null = the head's part).</summary>
            public string beaconPart;
        }

        /// <summary>The family head (around its pivot at the bottom centre): a rounded pressed-steel shell
        /// with a raised face plate and bolts (none under the mouth), two deep lamp eyes (dark steel housing,
        /// brass bezel and reflector, recessed lamp), a framed slatted mouth, red frustum side housings with
        /// bolted steel hubs, antennas on collars with red balls, a beacon lamp on a dark base, a back vent
        /// and bolted panels. Eyes and beacon are separate renderers (they light up).</summary>
        protected void FamilyHead(Transform pivot, string P, HeadSpec s)
        {
            var b = new Batch();
            float W = s.w, H = s.h, D = s.d, front = -D / 2;
            float z0 = front - 0.01f;   // the face plate's front surface
            b.RoundedBox(new Vector3(0, H / 2, 0), new Vector3(W, H, D), 0.042f, M(P, s.paint), 3);
            b.RoundedBox(new Vector3(0, H / 2, front - 0.004f), new Vector3(W - 0.042f, H - 0.042f, 0.012f), 0.012f, M(P, s.paint), 2);
            // face fasteners: a row along the top and one in each lower corner, clear of the mouth
            for (int k = 0; k < 6; k++) Bolt(b, new Vector3(-0.145f + k * 0.058f, H - 0.034f, z0), new Vector3(0, 0, -1), 0.0062f, M(P, Gunmetal));
            foreach (var x in new[] { -W / 2 + 0.036f, W / 2 - 0.036f }) Bolt(b, new Vector3(x, 0.036f, z0), new Vector3(0, 0, -1), 0.0062f, M(P, Gunmetal));
            float eyeY = H * s.eyeY, eyeX = s.eyeX, es = s.eyeScale > 0 ? s.eyeScale : 1;
            string EP = s.eyePart ?? P, AP = s.antennaPart ?? P;
            var eyeXs = s.eyes == 1 ? new[] { 0f } : new[] { -eyeX, eyeX };
            foreach (var x in eyeXs)
            {
                b.Lathe(new Vector3(x, eyeY, z0 + 0.01f), Ring(0.07f * es, 0.05f * es, 0.044f), M(EP, Gunmetal), FwdRot, 32);
                b.Lathe(new Vector3(x, eyeY, z0 - 0.008f), Ring(0.053f * es, 0.042f * es, 0.014f), M(EP, Brass), FwdRot, 32);
                b.Lathe(new Vector3(x, eyeY, z0 - 0.001f), new[] { new Vector2(0.026f * es, 0), new Vector2(0.043f * es, 0.02f), new Vector2(0.043f * es, 0.022f) }, M(EP, Brass), FwdRot, 28, true);   // reflector
                b.Lathe(new Vector3(x, eyeY, z0 + 0.001f), Disc(0.03f * es, 0.003f, 0.001f), M(EP, Dark), FwdRot, 20);   // lamp socket
            }
            if (s.mouth) FramedGrille(b, P, new Vector3(0, 0.04f, z0), 0.104f, 0.04f, 7, true, -1);
            float earY = H * s.earY;
            FamilyEarsAndAntennas(b, P, AP, AP, W, H, earY, s.antennas);
            string BP = s.beaconPart ?? P;
            if (HeadTopTaken) s.beacon = false;   // the chassis owns the head-top mount (Hoverbot's propeller)
            if (s.beacon)
            {
                b.Lathe(new Vector3(0, H - 0.004f, 0), Disc(0.064f, 0.026f, 0.005f), M(BP, Gunmetal), null, 28);
                b.Lathe(new Vector3(0, H + 0.022f, 0), Disc(0.055f, 0.008f, 0.002f), M(BP, BrightSteel), null, 28);
            }
            FramedGrille(b, P, new Vector3(0, H * 0.56f, -front + 0.001f), 0.2f, 0.095f, 7, false, 1);
            var hc = new Vector3(0, H / 2, 0);
            foreach (var n in new[] { Vector3.left, Vector3.right, Vector3.up, new Vector3(0, 0, 1) }) PanelFrame(b, P, hc, new Vector3(W, H, D), n, 0.054f);
            Done(b, pivot);

            if (Has(EP))
                foreach (var x in eyeXs)
                    eyes.Add(Separate(pivot, "G_eye", lb => lb.Lathe(new Vector3(x, eyeY, z0 - 0.002f), Dome(0.027f * es, 0.012f * es, 8), EyeOffMat, FwdRot, 24)));
            if (Has(BP))
            {
                if (s.beacon) beacons.Add(Separate(pivot, "G_beacon", lb => lb.Lathe(new Vector3(0, H + 0.03f, 0), BeaconLamp(0.05f, 0.07f, 0.02f), BeaconOffMat, null, 28)));
            }
        }

        // ------------------------------------------------------------ the telescopic forearm (reach upgrade)

        /// <summary>How far the telescopic forearms may extend (world m), from the robot's reach upgrade.</summary>
        public float armExtendMax;
        readonly List<(Transform hand, Vector3 home, Transform tubeA, Transform tubeB, Transform collar)> _teles = new();

        /// <summary>The family's two-stage telescopic forearm: a "Hand" pivot at the forearm's end (mountG,
        /// elbow space) that carries the wrist and the gripper (build them into the returned frame, which
        /// keeps the elbow's coordinates), and two bright steel sleeves, the thicker first, with a painted
        /// collar where the second comes out. Retracted (the normal state) the sleeves are hidden inside the
        /// forearm and switched off, so a robot looks and costs the same until it extends (PoseTelescopes).</summary>
        protected Transform TelescopeHand(Transform elbow, Vector3 mountG, string P, float rA, float rB, Material band)
        {
            var home = Batch.G(mountG);
            var hand = elbow.Find("Hand");
            if (hand == null)
            {
                hand = Pivot("Hand", elbow, home);
                var ta = Pivot("TubeA", elbow, home);
                var tb = Pivot("TubeB", elbow, home);
                var co = Pivot("TeleCollar", elbow, home);
                foreach (var t in new[] { ta, tb, co }) t.gameObject.SetActive(false);
                _teles.Add((hand, home, ta, tb, co));
            }
            var frame = hand.Find("HandGeo") ?? Pivot("HandGeo", hand, -home);
            var (_, _, tubeA, tubeB, collar) = _teles.Find(x => x.hand == hand);
            // unit-length sleeves hanging along -Y (the pose scales them to the extension)
            var a = new Batch(); a.Lathe(new Vector3(0, -1, 0), new[] { new Vector2(0, 0), new Vector2(rA, 0), new Vector2(rA, 1), new Vector2(0, 1) }, M(P, BrightSteel), null, 16); Done(a, tubeA);
            var b = new Batch(); b.Lathe(new Vector3(0, -1, 0), new[] { new Vector2(0, 0), new Vector2(rB, 0), new Vector2(rB, 1), new Vector2(0, 1) }, M(P, BrightSteel), null, 14); Done(b, tubeB);
            var c = new Batch();
            c.Lathe(new Vector3(0, -0.012f, 0), Disc(rA * 1.18f, 0.012f, 0.003f), M(P, band), null, 18);
            c.Lathe(new Vector3(0, -0.016f, 0), Disc(rB * 1.25f, 0.004f, 0.001f), M(P, Gunmetal), null, 16);
            Done(c, collar);
            return frame;
        }

        /// <summary>Extends every telescopic forearm by e (model units, along the forearm): the first sleeve
        /// covers the first half, the second the rest, the hand moves out by e. With `onlyUnder`, only the
        /// forearm of that arm.</summary>
        protected void PoseTelescopes(float e, Transform onlyUnder = null)
        {
            bool on = e > 1e-4f;
            foreach (var (hand, home, ta, tb, co) in _teles)
            {
                if (onlyUnder != null && !hand.IsChildOf(onlyUnder)) continue;
                if (ta.gameObject.activeSelf != on) { ta.gameObject.SetActive(on); tb.gameObject.SetActive(on); co.gameObject.SetActive(on); }
                var down = new Vector3(0, -1, 0);
                hand.localPosition = home + down * e;
                if (!on) continue;
                ta.localPosition = home; ta.localScale = new Vector3(1, e * 0.5f, 1);
                co.localPosition = home + down * (e * 0.5f);
                tb.localPosition = home + down * (e * 0.5f); tb.localScale = new Vector3(1, e * 0.5f, 1);
            }
        }

        /// <summary>The family's canonical head-side assembly (the user's canon, 2026-10-24): on each side of a
        /// head W wide and H tall, at height earY, a dark steel flange, the red truncated-cone ear housing
        /// with a raised rim and a bolted steel bearing hub, and (with antennas) a collar on top of the housing
        /// with a steel rod up to a red ball 0.07 above the head; grease runs below. Any head uses it, the
        /// family head and the older detailed head alike.</summary>
        protected void FamilyEarsAndAntennas(Batch b, string earPart, string antPartL, string antPartR, float W, float H, float earY, bool antennas = true)
        {
            foreach (var sd in new[] { -1f, 1f })
            {
                string AP = sd < 0 ? antPartL : antPartR;
                float x0 = sd * (W / 2 - 0.004f);
                b.Lathe(new Vector3(x0, earY, 0), Disc(0.086f, 0.01f, 0.003f), M(earPart, Gunmetal), SideRot(sd), 28);
                b.Lathe(new Vector3(x0 + sd * 0.01f, earY, 0), EarFrustum(0.075f, 0.058f, 0.056f), M(earPart, ToyRed), SideRot(sd), 28, true);
                Hub(b, earPart, new Vector3(x0 + sd * 0.062f, earY, 0), sd, 0.036f, 6, 0.005f);
                if (!antennas) continue;
                var collar = new Vector3(sd * (W / 2 + 0.036f), earY + 0.064f, 0);
                b.Lathe(collar - new Vector3(0, 0.006f, 0), Disc(0.016f, 0.02f, 0.003f), M(AP, Gunmetal), null, 14);
                b.Lathe(collar + new Vector3(0, 0.014f, 0), Disc(0.012f, 0.008f, 0.002f), M(AP, BrightSteel), null, 12);
                float top = H + 0.07f;
                b.Lathe(collar + new Vector3(0, 0.022f, 0), Disc(0.0065f, top - collar.y - 0.022f - 0.02f, 0.001f), M(AP, Gunmetal), null, 10);
                b.Lathe(new Vector3(collar.x, top - 0.029f, 0), Ball(0.029f), M(AP, ToyRed), null, 18);
            }
            foreach (var sd in new[] { -1f, 1f })
                foreach (var z in new[] { -0.03f, 0.025f })
                    GreaseRun(b, earPart, new Vector3(sd * W / 2, earY - 0.08f, z), X(sd), 0.05f, 0.012f);
        }

        // ------------------------------------------------------------ the family arm and gripper

        /// <summary>A family arm: its paints and the gripper's scale and twist round the forearm.
        /// Retrobot: yellow / deep yellow / red, claw 1.08, twist 20 deg.</summary>
        public struct ArmSpec
        {
            public Material paint, paintDeep, red;
            public float clawScale, twist;
            /// <summary>Assembly part of the gripper (palm, hinges, fingers; null = the arm's part).</summary>
            public string clawPart;
        }

        /// <summary>Marker transforms between each gripper's finger tips (no geometry): the grip point,
        /// the floor guard and the QA measurements use them.</summary>
        public Transform clawTipL, clawTipR;
        readonly List<(Transform t, float fs, Vector3 axisG)> _fingers = new();

        /// <summary>Gripper finger opening (deg, + = open, - = closed), applied to every finger about its
        /// own root hinge. 0 is the modelled (neutral) pose.</summary>
        protected void PoseClaws(float deg)
        {
            foreach (var (t, fs, axis) in _fingers) t.localRotation = Batch.R(axis, fs * deg * Mathf.Deg2Rad);
        }

        /// <summary>The family arm from its shoulder pivot (side s). On the arm pivot: the shoulder drum
        /// (bearing ring, drum, red cap, bolted hub), the upper arm and the elbow clevis with its pin. On
        /// the elbow pivot: the geared knuckle, the forearm (access plate, cuff), the wrist housing and the
        /// gripper's palm and root hinges. Each red finger hangs on its own pivot at its root hinge
        /// ("FingerL" / "FingerR", see PoseClaws): two faceted segments hooking inwards with a flush matte
        /// grip strip and the knuckle hinge hub.</summary>
        protected void FamilyArm(Transform arm, float s, string P, ArmSpec spec)
        {
            _famArm[s < 0 ? 0 : 1] = arm;
            var b = new Batch();
            b.Lathe(X(-s * 0.05f), Disc(0.045f, 0.012f, 0.003f), M(P, Gunmetal), SideRot(s), 24);
            b.Lathe(X(-s * 0.038f), Disc(0.066f, 0.06f, 0.012f), M(P, spec.paint), SideRot(s), 32);
            b.Lathe(X(s * 0.022f), Disc(0.064f, 0.02f, 0.01f), M(P, spec.red), SideRot(s), 32);
            Hub(b, P, X(s * 0.042f), s, 0.04f, 6);
            b.ChamferBox(new Vector3(0, -0.103f, 0), new Vector3(0.09f, 0.08f, 0.092f), 0.014f, M(P, spec.paint), null, true);
            b.ChamferBox(new Vector3(0, -0.074f, 0), new Vector3(0.096f, 0.014f, 0.098f), 0.004f, M(P, spec.paintDeep), null, true);
            Bolt(b, new Vector3(0, -0.11f, -0.046f), new Vector3(0, 0, -1), 0.006f, M(P, Gunmetal));
            const float EY = -0.18f;
            foreach (var px in new[] { -0.04f, 0.04f })
            {
                b.ChamferBox(new Vector3(px, -0.155f, 0), new Vector3(0.012f, 0.05f, 0.056f), 0.004f, M(P, Gunmetal));
                b.Lathe(new Vector3(px - 0.006f, EY, 0), Disc(0.028f, 0.012f, 0.003f), M(P, Gunmetal), RightRot, 20);
            }
            b.Lathe(new Vector3(-0.058f, EY, 0), Disc(0.009f, 0.116f, 0.002f), M(P, BrightSteel), RightRot, 12);   // the pin
            foreach (var px in new[] { -1f, 1f }) Hub(b, P, new Vector3(px * 0.046f, EY, 0), px, 0.022f, 0);
            Done(b, arm);

            // ---- on the elbow pivot (coordinates relative to the elbow axle)
            var elbow = Elbow(arm, new Vector3(0, EY, 0), s);
            var f = new Batch();
            Gear(f, P, Vector3.zero, 0.03f, 0.062f, 12);
            f.ChamferBox(new Vector3(0, -0.032f, 0), new Vector3(0.058f, 0.04f, 0.058f), 0.008f, M(P, Gunmetal));
            f.ChamferBox(new Vector3(0, -0.094f, 0), new Vector3(0.1f, 0.104f, 0.1f), 0.016f, M(P, spec.paint), null, true);
            f.ChamferBox(new Vector3(s * 0.052f, -0.092f, 0), new Vector3(0.006f, 0.064f, 0.068f), 0.003f, M(P, spec.paintDeep), null, true);
            foreach (var z in new[] { -0.022f, 0.022f }) Bolt(f, new Vector3(s * 0.055f, -0.092f, z), X(s), 0.0055f, M(P, Gunmetal));
            foreach (var x in new[] { -0.024f, 0.024f }) Bolt(f, new Vector3(x, -0.072f, -0.05f), new Vector3(0, 0, -1), 0.0055f, M(P, Gunmetal));
            f.ChamferBox(new Vector3(0, -0.15f, 0), new Vector3(0.104f, 0.016f, 0.104f), 0.004f, M(P, Gunmetal));
            GreaseRun(f, P, new Vector3(s * 0.05f, -0.045f, 0.025f), X(s), 0.06f);
            GreaseRun(f, P, new Vector3(0.015f, -0.045f, -0.05f), new Vector3(0, 0, -1), 0.045f);
            // the hand (wrist, gripper) on the telescopic forearm's end; built in the elbow's coordinates
            var hg = TelescopeHand(elbow, new Vector3(0, -0.158f, 0), P, 0.034f, 0.025f, spec.paint);
            var tip = hg.Find("ClawTip") ?? Pivot("ClawTip", hg, Batch.G(new Vector3(0, -0.392f, 0)));
            if (s < 0) clawTipL = tip; else clawTipR = tip;
            var hb = new Batch();
            var wrist = new Vector3(0, -0.172f, 0);
            hb.Lathe(wrist - X(0.038f), Disc(0.028f, 0.076f, 0.004f), M(P, Gunmetal), RightRot, 18);
            foreach (var px in new[] { -1f, 1f }) Hub(hb, P, wrist + X(px * 0.038f), px, 0.02f, 0);
            // the gripper, twisted round the forearm and scaled about the wrist
            string CP = spec.clawPart ?? P;
            float cs = spec.clawScale;
            var tw = Quaternion.AngleAxis(s * spec.twist, Vector3.up);       // Layout-space points
            var twU = Quaternion.AngleAxis(-s * spec.twist, Vector3.up);     // the same twist for Unity-space rotations
            Vector3 C(float x, float y, float z) => wrist + tw * (new Vector3(x, y, z) * cs);
            Vector3 side = tw * Vector3.right;
            hb.ChamferBox(C(0, -0.032f, 0), new Vector3(0.094f, 0.034f, 0.062f) * cs, 0.009f * cs, M(CP, Gunmetal), twU);   // palm
            var path = new[] { new Vector2(0.034f, -0.044f), new Vector2(0.074f, -0.078f), new Vector2(0.094f, -0.128f), new Vector2(0.08f, -0.174f), new Vector2(0.04f, -0.2f) };
            float[] wd = { 0.044f, 0.04f, 0.035f, 0.028f, 0.019f }, dp = { 0.056f, 0.054f, 0.049f, 0.043f, 0.036f };
            void HingeHub(Batch hb, Vector3 p, float r, Vector3 o)
            {
                hb.Lathe(p - o + tw * new Vector3(0, 0, -0.033f * cs), Disc(r * cs, 0.066f * cs, 0.003f), M(CP, BrightSteel), twU * BackRot, 18);
                foreach (var zs in new[] { -1f, 1f })
                    hb.Lathe(p - o + tw * new Vector3(0, 0, zs * 0.033f * cs), Dome(r * 0.45f * cs, 0.006f * cs), M(CP, Brass), twU * (zs < 0 ? FwdRot : BackRot), 10);
            }
            foreach (var fs in new[] { -1f, 1f })
            {
                Vector3 V3(Vector2 p) => C(fs * p.x, p.y, 0);
                var root = V3(path[0]);
                HingeHub(hb, root, 0.018f, Vector3.zero);   // the root hinge stays on the palm
                // the finger, on its own pivot at the root hinge (geometry relative to the hinge)
                string fname = fs < 0 ? "FingerL" : "FingerR";
                var fp = hg.Find(fname);
                if (fp == null)
                {
                    fp = Pivot(fname, hg, Batch.G(root));
                    _fingers.Add((fp, fs, tw * Vector3.forward));
                }
                var fb = new Batch();
                for (int i = 0; i < path.Length - 1; i++)
                {
                    var d = (path[i + 1] - path[i]).normalized;
                    var a0 = i >= 2 ? path[i] - d * (wd[i] * 0.35f) : path[i];
                    var a1 = i >= 1 && i < path.Length - 2 ? path[i + 1] + d * (wd[i + 1] * 0.35f) : path[i + 1];
                    fb.Loft(V3(a0) - root, V3(a1) - root, new Vector2(wd[i], dp[i]) * cs, new Vector2(wd[i + 1], dp[i + 1]) * cs, 0.011f * cs, (i == path.Length - 2 ? 0.006f : 0.01f) * cs, M(CP, spec.red), side);
                }
                for (int i = 1; i < path.Length - 1; i++)
                {
                    var a = new Vector2(fs * path[i].x, path[i].y); var c = new Vector2(fs * path[i + 1].x, path[i + 1].y);
                    var dir = (c - a).normalized;
                    var inward = new Vector2(dir.y, -dir.x);
                    if (Vector2.Dot(inward, new Vector2(-fs, -0.3f)) < 0) inward = -inward;
                    var a2 = a + inward * (wd[i] / 2 + 0.0008f) + (i == 1 ? dir * 0.01f : Vector2.zero);
                    var c2 = c + inward * (wd[i + 1] / 2 + 0.0008f) - (i == path.Length - 2 ? dir * 0.008f : Vector2.zero);
                    fb.Loft(C(a2.x, a2.y, 0) - root, C(c2.x, c2.y, 0) - root, new Vector2(0.0035f, dp[i] * 0.62f) * cs, new Vector2(0.0035f, dp[i + 1] * 0.62f) * cs, 0.0008f, 0.0008f, M(CP, Rubber), side);
                }
                HingeHub(fb, V3(path[1]), 0.019f, root);   // the knuckle hinge moves with the finger
                Done(fb, fp);
            }
            Done(hb, hg);
            Done(f, elbow);
        }

        // ------------------------------------------------------------ Layer A: shared manipulation and expression
        // (docs/ROBOT_FAMILY_DESIGN_AND_ANIMATION_STANDARD.md). A family model owns its locomotion (Layer B)
        // and calls these from its Animate: pose the body and arms for a depth (PoseForDepth), FloorGuard,
        // FamilyAim, FamilyGrip, FamilyClaws, FamilyLookHead. They work on the two arms registered by
        // FamilyArm and on the gripper tip markers; nothing here knows about legs, wheels or tracks.

        readonly Transform[] _famArm = new Transform[2];
        /// <summary>The arms' current pitch (deg), as the model's body pose last set it.</summary>
        protected float famArmPitch;
        float _gripPitch = float.NaN, _gripWant, _gripInward;
        Vector3 _aimKey = new(float.NaN, 0, 0); float _aimExtMax = -1, _aimExt;
        /// <summary>Gripper finger travel (deg) fully open and fully closed from the modelled pose.</summary>
        protected const float ClawOpenDeg = 22f, ClawCloseDeg = 8f;

        protected static float Ease(float x) { x = Mathf.Clamp01(x); return x * x * (3 - 2 * x); }
        protected Vector3 FlatForward => new Vector3(transform.forward.x, 0, transform.forward.z).normalized;
        protected bool HasGrippers => clawTipL != null && clawTipR != null;
        protected float LowestClaw() => Mathf.Min(clawTipL.position.y, clawTipR.position.y);

        /// <summary>Both family arms at one pitch, mirrored sideways by `inward` (deg).</summary>
        protected void SetArmPitch(float pitch, float inward)
        {
            if (_famArm[0] != null) _famArm[0].localRotation = Quaternion.Euler(pitch, 0, inward);
            if (_famArm[1] != null) _famArm[1].localRotation = Quaternion.Euler(pitch, 0, -inward);
        }

        /// <summary>The family zinc bolt finish on top of FamilySkin.</summary>
        protected static Material FamilyBoltSkin(Material m) => m == RobotFinish.JointSteel ? RobotFinish.BoltSteel : m;

        /// <summary>The model's full body-and-arms pose for one frame at a pick-up depth (0 = upright,
        /// 1 = the normal reach down, up to 2 = further for a part lower than the feet). The floor guard
        /// calls it with trial depths; the model keeps whatever else it needs for the frame in fields.</summary>
        protected virtual void PoseForDepth(float depth) { }

        /// <summary>Floor guard: the deepest pick-up pose whose claws stay at the part's height (never below
        /// the floor); for a part lower than the full pose reaches, up to twice as deep.</summary>
        protected void FloorGuard(RobotAnim a)
        {
            if (!(a.bend > 0.01f || a.armReach > 0.01f) || !HasGrippers) return;
            float target = transform.position.y + Mathf.Max(a.reachHeight, -0.08f) + 0.008f * transform.lossyScale.y / 0.283f;
            if (a.bend > 0.9f && LowestClaw() > target + 0.004f)
            {
                float lo = 1, hi = 2f;
                PoseForDepth(hi);
                if (LowestClaw() > target) lo = hi;
                else
                    for (int it = 0; it < 6; it++)
                    {
                        float m = (lo + hi) * 0.5f;
                        PoseForDepth(m);
                        if (LowestClaw() < target) hi = m; else lo = m;
                    }
                PoseForDepth(lo);
            }
            else if (LowestClaw() < target)
            {
                float lo = 0, hi = 1;
                for (int it = 0; it < 7; it++)
                {
                    float m = (lo + hi) * 0.5f;
                    PoseForDepth(m);
                    if (LowestClaw() < target) hi = m; else lo = m;
                }
                PoseForDepth(lo);
            }
        }

        /// <summary>Aim: raise the (straight) arms until the claws are at the target's height and at least as
        /// far forward; when they can't, the telescopic forearms extend as far as needed (cached per target).
        /// `basePitch` / `inward`: the arms' pose without the aim (weighted by a.aimWeight).</summary>
        protected void FamilyAim(RobotAnim a, float basePitch, float inward)
        {
            if (a.aimWeight <= 0.001f || !HasGrippers) return;
            float SolvePitch(float ext)
            {
                PoseTelescopes(ext);
                float lo = -150f, hi = 0f;
                for (int it = 0; it < 8; it++)
                {
                    float m = (lo + hi) * 0.5f;
                    SetArmPitch(m, inward);
                    if (GripPoint.Value.y < a.aim.y) hi = m; else lo = m;
                }
                float pm = (lo + hi) * 0.5f;
                SetArmPitch(pm, inward);
                return pm;
            }
            float targetFwd = Vector3.Dot(a.aim - transform.position, FlatForward);
            bool Reached() => Mathf.Abs(GripPoint.Value.y - a.aim.y) < 0.01f && Vector3.Dot(GripPoint.Value - transform.position, FlatForward) >= targetFwd - 0.01f;
            float extMax = armExtendMax / Mathf.Max(1e-4f, transform.lossyScale.y);
            if (_aimKey != a.aim || _aimExtMax != extMax)
            {
                _aimKey = a.aim; _aimExtMax = extMax; _aimExt = 0;
                SolvePitch(0);
                if (!Reached() && extMax > 0)
                {
                    float lo = 0, hi = extMax;
                    for (int it = 0; it < 7; it++)
                    {
                        float m = (lo + hi) * 0.5f;
                        SolvePitch(m);
                        if (Reached()) hi = m; else lo = m;
                    }
                    _aimExt = hi;   // the full extension when nothing reaches (it gets as close as it can)
                }
            }
            float ew = _aimExt * Ease(a.aimWeight);
            famArmPitch = Mathf.Lerp(basePitch, SolvePitch(ew), Ease(a.aimWeight));
            PoseTelescopes(ew);
            SetArmPitch(famArmPitch, inward);
        }

        /// <summary>The grip: the arms close in until the two grippers rest against the part's sides
        /// (solved once per arm pitch and width). `restInward`: their sideways angle without a part.</summary>
        protected void FamilyGrip(RobotAnim a, float restInward)
        {
            if (!HasGrippers || a.gripWidth <= 0) return;
            float w = Ease(Mathf.Max(a.armHold, Mathf.Max(a.armReach, a.aimWeight)));
            if (w <= 0.001f) return;
            float want = a.gripWidth + 0.006f;
            if (Mathf.Abs(_gripPitch - famArmPitch) > 0.05f || Mathf.Abs(_gripWant - want) > 1e-4f)
            {
                float lo = -10f, hi = 35f;
                for (int it = 0; it < 7; it++)
                {
                    float m = (lo + hi) * 0.5f;
                    SetArmPitch(famArmPitch, m);
                    if (Vector3.Distance(clawTipL.position, clawTipR.position) > want) lo = m; else hi = m;
                }
                _gripPitch = famArmPitch; _gripWant = want; _gripInward = (lo + hi) * 0.5f;
            }
            SetArmPitch(famArmPitch, Mathf.Lerp(restInward, _gripInward, w));
        }

        /// <summary>The grippers' fingers for this frame (open / closed from the animation inputs).</summary>
        protected void FamilyClaws(RobotAnim a) => PoseClaws(ClawOpenDeg * a.clawOpen - ClawCloseDeg * a.clawClose);

        /// <summary>How a model's family head pays attention: how far it looks into a coming turn, its slow scan
        /// while moving, how quickly it follows, and whether it watches the part or the destination it is
        /// working on.</summary>
        public struct HeadAttention
        {
            public float aheadMax, scanDeg, scanRate, follow;
            public bool watchWork;
        }

        /// <summary>The family head's attention (shared hardware, per-model behaviour): idle it looks at the
        /// player; moving it looks where the path goes and scans slowly; working (if watchWork) it watches the
        /// part or the destination. `moving` 0..1 scales the scan.</summary>
        protected void FamilyLookHead(RobotAnim a, float moving, HeadAttention h)
        {
            if (head == null) return;
            if (a.lookWeight > 0 || a.dt <= 0) { LookHead(a); return; }
            float yaw = 0, pitch = 0;
            if (h.watchWork && a.focusWeight > 0)
            {
                var to = a.focus - head.position;
                var flat = new Vector3(to.x, 0, to.z);
                if (flat.sqrMagnitude > 1e-4f)
                {
                    yaw = Mathf.Clamp(Vector3.SignedAngle(FlatForward, flat, Vector3.up), -h.aheadMax, h.aheadMax);
                    pitch = Mathf.Clamp(-Mathf.Atan2(to.y, flat.magnitude) * Mathf.Rad2Deg, -25f, 30f) * 0.6f;
                }
            }
            else if (a.lookAheadWeight > 0)
            {
                var to = a.lookAhead - transform.position; to.y = 0;
                if (to.sqrMagnitude > 1e-4f) yaw = Mathf.Clamp(Vector3.SignedAngle(FlatForward, to, Vector3.up), -h.aheadMax, h.aheadMax) * a.lookAheadWeight;
            }
            yaw += Mathf.Sin(a.time * h.scanRate) * h.scanDeg * moving * (1 - Mathf.Clamp01(Mathf.Abs(yaw) / 20f));
            var want = Quaternion.Euler(a.bend * 10 + Mathf.Sin(a.phase * 2) * 2.5f * a.gait + pitch, yaw, 0);
            head.localRotation = Quaternion.Slerp(head.localRotation, want, a.dt * h.follow);
        }

        // ------------------------------------------------------------ generic gripper measurements (base behaviour)

        Vector3? FamilyGripPoint => HasGrippers ? (clawTipL.position + clawTipR.position) * 0.5f : null;

        float? FamilyGripReach(float reachHeight)
        {
            if (!HasGrippers) return null;
            SnapshotPose();
            Animate(new RobotAnim { dt = 0, bend = 1, armReach = 1, reachHeight = reachHeight });
            var gp = FamilyGripPoint.Value - transform.position;
            RestorePose();
            return Vector3.Dot(new Vector3(gp.x, 0, gp.z), FlatForward);
        }

        float? FamilyGripLow()
        {
            if (!HasGrippers) return null;
            SnapshotPose();
            Animate(new RobotAnim { dt = 0, bend = 1, armReach = 1, reachHeight = 0 });
            float y = FamilyGripPoint.Value.y - transform.position.y;
            RestorePose();
            return y;
        }

        float? FamilyAimReach(float aimHeight, float ahead)
        {
            if (!HasGrippers) return null;
            SnapshotPose();
            Animate(new RobotAnim { dt = 0, armHold = 1, aimWeight = 1, aim = transform.position + FlatForward * ahead + Vector3.up * aimHeight });
            var gp = FamilyGripPoint.Value - transform.position;
            RestorePose();
            if (Mathf.Abs(gp.y - aimHeight) > 0.012f) return -1f;   // the arms can't raise the claws to that height
            return Vector3.Dot(new Vector3(gp.x, 0, gp.z), FlatForward);
        }
    }
}
