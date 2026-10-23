using System.Collections.Generic;
using UnityEngine;

namespace WorkshopWreckage
{
    /// <summary>The robot family's standard component catalogue (docs/ROBOT_FAMILY_DESIGN_AND_ANIMATION_STANDARD.md):
    /// the parts one fictional manufacturer reuses across its models, first built for Retrobot (F4-F7).
    /// Family finish (FamilySkin), hardware (Nut, Hub, Gear, FramedGrille), wear (GreaseRun, GrimeBand),
    /// the family head module (FamilyHead) and the family arm with its articulated red gripper (FamilyArm,
    /// PoseClaws). A model opts in by calling these; robots that don't keep their older kit (DetailedHead,
    /// DetailedArm). Coordinates are Godot-local (front = -Z), as everywhere in the robot models.</summary>
    public abstract partial class RobotModel
    {
        // ------------------------------------------------------------ finish

        public static readonly Color FamilyYellow = new(0.8f, 0.55f, 0.12f), FamilyYellowDeep = new(0.68f, 0.45f, 0.09f), FamilyRed = new(0.68f, 0.1f, 0.06f);
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
                [Gunmetal] = RobotFinish.JointSteel,
                [Dark] = RobotFinish.Blackened,
                [Brass] = RobotFinish.BearingBrass,
                [Rubber] = RobotFinish.Rubber,
                [StainMat] = RobotFinish.OilStain,
            };
            return _familySkin.TryGetValue(m, out var s) ? s : m;
        }

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
            float eyeY = H * s.eyeY, eyeX = s.eyeX;
            foreach (var x in new[] { -eyeX, eyeX })
            {
                b.Lathe(new Vector3(x, eyeY, z0 + 0.01f), Ring(0.07f, 0.05f, 0.044f), M(P, Gunmetal), FwdRot, 32);
                b.Lathe(new Vector3(x, eyeY, z0 - 0.008f), Ring(0.053f, 0.042f, 0.014f), M(P, Brass), FwdRot, 32);
                b.Lathe(new Vector3(x, eyeY, z0 - 0.001f), new[] { new Vector2(0.026f, 0), new Vector2(0.043f, 0.02f), new Vector2(0.043f, 0.022f) }, M(P, Brass), FwdRot, 28, true);   // reflector
                b.Lathe(new Vector3(x, eyeY, z0 + 0.001f), Disc(0.03f, 0.003f, 0.001f), M(P, Dark), FwdRot, 20);   // lamp socket
            }
            if (s.mouth) FramedGrille(b, P, new Vector3(0, 0.04f, z0), 0.104f, 0.04f, 7, true, -1);
            float earY = H * s.earY;
            foreach (var sd in new[] { -1f, 1f })
            {
                float x0 = sd * (W / 2 - 0.004f);
                b.Lathe(new Vector3(x0, earY, 0), Disc(0.086f, 0.01f, 0.003f), M(P, Gunmetal), SideRot(sd), 28);
                b.Lathe(new Vector3(x0 + sd * 0.01f, earY, 0), EarFrustum(0.075f, 0.058f, 0.056f), M(P, ToyRed), SideRot(sd), 28, true);
                Hub(b, P, new Vector3(x0 + sd * 0.062f, earY, 0), sd, 0.036f, 6, 0.005f);
                if (!s.antennas) continue;
                var collar = new Vector3(sd * (W / 2 + 0.036f), earY + 0.064f, 0);
                b.Lathe(collar - new Vector3(0, 0.006f, 0), Disc(0.016f, 0.02f, 0.003f), M(P, Gunmetal), null, 14);
                b.Lathe(collar + new Vector3(0, 0.014f, 0), Disc(0.012f, 0.008f, 0.002f), M(P, BrightSteel), null, 12);
                float top = H + 0.07f;
                b.Lathe(collar + new Vector3(0, 0.022f, 0), Disc(0.0065f, top - collar.y - 0.022f - 0.02f, 0.001f), M(P, Gunmetal), null, 10);
                b.Lathe(new Vector3(collar.x, top - 0.029f, 0), Ball(0.029f), M(P, ToyRed), null, 18);
            }
            foreach (var sd in new[] { -1f, 1f })
                foreach (var z in new[] { -0.03f, 0.025f })
                    GreaseRun(b, P, new Vector3(sd * W / 2, earY - 0.08f, z), X(sd), 0.05f, 0.012f);
            if (s.beacon)
            {
                b.Lathe(new Vector3(0, H - 0.004f, 0), Disc(0.064f, 0.026f, 0.005f), M(P, Gunmetal), null, 28);
                b.Lathe(new Vector3(0, H + 0.022f, 0), Disc(0.055f, 0.008f, 0.002f), M(P, BrightSteel), null, 28);
            }
            FramedGrille(b, P, new Vector3(0, H * 0.56f, -front + 0.001f), 0.2f, 0.095f, 7, false, 1);
            var hc = new Vector3(0, H / 2, 0);
            foreach (var n in new[] { Vector3.left, Vector3.right, Vector3.up, new Vector3(0, 0, 1) }) PanelFrame(b, P, hc, new Vector3(W, H, D), n, 0.054f);
            Done(b, pivot);

            if (Has(P))
            {
                foreach (var x in new[] { -eyeX, eyeX })
                    eyes.Add(Separate(pivot, "G_eye", lb => lb.Lathe(new Vector3(x, eyeY, z0 - 0.002f), Dome(0.027f, 0.012f, 8), EyeOffMat, FwdRot, 24)));
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
        /// covers the first half, the second the rest, the hand moves out by e.</summary>
        protected void PoseTelescopes(float e)
        {
            bool on = e > 1e-4f;
            foreach (var (hand, home, ta, tb, co) in _teles)
            {
                if (ta.gameObject.activeSelf != on) { ta.gameObject.SetActive(on); tb.gameObject.SetActive(on); co.gameObject.SetActive(on); }
                var down = new Vector3(0, -1, 0);
                hand.localPosition = home + down * e;
                if (!on) continue;
                ta.localPosition = home; ta.localScale = new Vector3(1, e * 0.5f, 1);
                co.localPosition = home + down * (e * 0.5f);
                tb.localPosition = home + down * (e * 0.5f); tb.localScale = new Vector3(1, e * 0.5f, 1);
            }
        }

        // ------------------------------------------------------------ the family arm and gripper

        /// <summary>A family arm: its paints and the gripper's scale and twist round the forearm.
        /// Retrobot: yellow / deep yellow / red, claw 1.08, twist 20 deg.</summary>
        public struct ArmSpec
        {
            public Material paint, paintDeep, red;
            public float clawScale, twist;
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
            float cs = spec.clawScale;
            var tw = Quaternion.AngleAxis(s * spec.twist, Vector3.up);       // Godot-space points
            var twU = Quaternion.AngleAxis(-s * spec.twist, Vector3.up);     // the same twist for Unity-space rotations
            Vector3 C(float x, float y, float z) => wrist + tw * (new Vector3(x, y, z) * cs);
            Vector3 side = tw * Vector3.right;
            hb.ChamferBox(C(0, -0.032f, 0), new Vector3(0.094f, 0.034f, 0.062f) * cs, 0.009f * cs, M(P, Gunmetal), twU);   // palm
            var path = new[] { new Vector2(0.034f, -0.044f), new Vector2(0.074f, -0.078f), new Vector2(0.094f, -0.128f), new Vector2(0.08f, -0.174f), new Vector2(0.04f, -0.2f) };
            float[] wd = { 0.044f, 0.04f, 0.035f, 0.028f, 0.019f }, dp = { 0.056f, 0.054f, 0.049f, 0.043f, 0.036f };
            void HingeHub(Batch hb, Vector3 p, float r, Vector3 o)
            {
                hb.Lathe(p - o + tw * new Vector3(0, 0, -0.033f * cs), Disc(r * cs, 0.066f * cs, 0.003f), M(P, BrightSteel), twU * BackRot, 18);
                foreach (var zs in new[] { -1f, 1f })
                    hb.Lathe(p - o + tw * new Vector3(0, 0, zs * 0.033f * cs), Dome(r * 0.45f * cs, 0.006f * cs), M(P, Brass), twU * (zs < 0 ? FwdRot : BackRot), 10);
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
                    fb.Loft(V3(a0) - root, V3(a1) - root, new Vector2(wd[i], dp[i]) * cs, new Vector2(wd[i + 1], dp[i + 1]) * cs, 0.011f * cs, (i == path.Length - 2 ? 0.006f : 0.01f) * cs, M(P, spec.red), side);
                }
                for (int i = 1; i < path.Length - 1; i++)
                {
                    var a = new Vector2(fs * path[i].x, path[i].y); var c = new Vector2(fs * path[i + 1].x, path[i + 1].y);
                    var dir = (c - a).normalized;
                    var inward = new Vector2(dir.y, -dir.x);
                    if (Vector2.Dot(inward, new Vector2(-fs, -0.3f)) < 0) inward = -inward;
                    var a2 = a + inward * (wd[i] / 2 + 0.0008f) + (i == 1 ? dir * 0.01f : Vector2.zero);
                    var c2 = c + inward * (wd[i + 1] / 2 + 0.0008f) - (i == path.Length - 2 ? dir * 0.008f : Vector2.zero);
                    fb.Loft(C(a2.x, a2.y, 0) - root, C(c2.x, c2.y, 0) - root, new Vector2(0.0035f, dp[i] * 0.62f) * cs, new Vector2(0.0035f, dp[i + 1] * 0.62f) * cs, 0.0008f, 0.0008f, M(P, Rubber), side);
                }
                HingeHub(fb, V3(path[1]), 0.019f, root);   // the knuckle hinge moves with the finger
                Done(fb, fp);
            }
            Done(hb, hg);
            Done(f, elbow);
        }
    }
}
