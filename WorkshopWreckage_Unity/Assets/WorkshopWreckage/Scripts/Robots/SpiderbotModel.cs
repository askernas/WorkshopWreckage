using UnityEngine;

namespace WorkshopWreckage
{
    /// <summary>Spiderbot after resources/spideybot.png, upgraded to the family standard (robot family
    /// phase 3, 2026-10-24): an olive-green hexapod in the family finish with red-and-brass joints.
    /// Riveted rounded body with slatted front and side grilles, a carry handle on top and a dark belly
    /// dome; the family head (two lamp eyes, mouth grille, the canonical ear cones and antennas, the
    /// beacon); six articulated legs splayed like a real spider's (red ball hip, chunky olive thigh rising
    /// steeply to a high red knee with brass caps, olive and heavy red shin, an ankle that keeps the dark
    /// puck foot flat); two short olive family arms with red grippers under the front as its jaws, which
    /// carry its find.
    /// Its own locomotion (Layer B): a tripod gait whose planted feet stay where they stand (two-bone leg
    /// IK from the hips; the stride and the turning arc are matched to the ground speed and turn rate),
    /// the swinging tripod lifting and reaching ahead; the body crouches on planted legs to pick up.
    /// Manipulation and attention come from the family layer (Layer A).
    /// Parts: head (with its eye pair and antennas), body shell, belly dome, front/middle/rear
    /// left/right leg, left/right arm (the jaws), servo controller, battery pack, spinneret.</summary>
    public class SpiderbotModel : RobotModel
    {
        static readonly (string part, float x, float z, int group)[] Legs =
        {
            ("front left leg", -1, -0.13f, 0), ("middle left leg", -1, 0f, 1), ("rear left leg", -1, 0.13f, 0),
            ("front right leg", 1, -0.13f, 1), ("middle right leg", 1, 0f, 0), ("rear right leg", 1, 0.13f, 1),
        };
        readonly Transform[] _hip = new Transform[6], _knee = new Transform[6], _foot = new Transform[6];
        Transform _jawL, _jawR, _shell;
        public override float ModelHeight => 0.82f;
        const float HipY = 0.3f, JawX = 0.11f;
        // the leg in its own vertical plane (outward u, up y), model units: hip to knee, knee to ankle
        static readonly Vector2 Thigh = new(0.15f, 0.14f), Shin = new(0.072f, -0.39f);
        /// <summary>The ankle's height above the sole (the puck foot).</summary>
        const float SoleDrop = 0.04f;

        protected override Material Skin(Material m) => FamilySkin(m, FamilyViolet);   // muted violet
        protected override Material BoltSkin(Material m) => FamilyBoltSkin(m);

        protected override void CreatePivots()
        {
            _shell = Pivot("Shell", body, Vector3.zero);
            for (int i = 0; i < 6; i++)
            {
                var (_, x, z, _) = Legs[i];
                _hip[i] = Pivot("Hip" + i, _shell, new Vector3(x * 0.19f, HipY, -z));   // Unity z = -layout z
                _knee[i] = Pivot("Knee" + i, _hip[i], new Vector3(x * Thigh.x, Thigh.y, 0));   // high knees, as on the sheet
                _foot[i] = Pivot("Foot" + i, _knee[i], new Vector3(x * Shin.x, Shin.y, 0));
            }
            // the jaws: short family arms under the front, close together so the grippers meet on a part
            _jawL = Pivot("JawL", _shell, new Vector3(-JawX, 0.31f, 0.2f));
            _jawR = Pivot("JawR", _shell, new Vector3(JawX, 0.31f, 0.2f));
            _jawL.localScale = _jawR.localScale = Vector3.one * 0.62f;
            head = Pivot("Head", _shell, new Vector3(0, 0.435f, 0.03f));
            carry = Pivot("Carry", _shell, new Vector3(0, 0.46f, -0.15f));   // on its back, behind the head
            for (int i = 0; i < 6; i++) Mount(_hip[i], Legs[i].part, new Vector3(Legs[i].x, 0, 0), 0.035f);
            Mount(head, "head", Vector3.up, 0.045f);
            Mount(_jawL, "left arm", new Vector3(0, 0, -1), 0.025f);
            Mount(_jawR, "right arm", new Vector3(0, 0, -1), 0.025f);
        }

        protected override Vector3? ClampPoint => new Vector3(0, 0.31f, Has("body shell") ? 0.21f : 0.1f);

        /// <summary>Without the body shell: the chassis plate with its hip rails.</summary>
        protected override void BuildFrame()
        {
            if (Has("body shell")) return;
            var b = new Batch();
            b.RoundedBox(new Vector3(0, 0.27f, 0), new Vector3(0.26f, 0.025f, 0.3f), 0.006f, Gunmetal, 2);
            foreach (var z in new[] { -0.13f, 0f, 0.13f }) b.RoundedBox(new Vector3(0, HipY, z), new Vector3(0.36f, 0.025f, 0.03f), 0.006f, Gunmetal, 2);
            b.RoundedBox(new Vector3(0, 0.32f, 0.02f), new Vector3(0.08f, 0.06f, 0.1f), 0.008f, Brass, 2);   // the drive block
            b.Build(_shell, "G_frame", true);
        }

        protected override void BuildGeometry()
        {
            {
                var b = new Batch();
                const string P = "body shell";
                b.RoundedBox(new Vector3(0, 0.33f, 0), new Vector3(0.36f, 0.2f, 0.34f), 0.045f, M(P, ToyGreen), 4);
                b.RoundedBox(new Vector3(0, 0.432f, 0.02f), new Vector3(0.28f, 0.012f, 0.26f), 0.005f, M(P, ToyGreenDeep), 2);   // top plate
                Grille(b, P, new Vector3(0, 0.335f, -0.172f), 0.17f, 0.085f, 6, false, -1);
                foreach (var s in new[] { -1f, 1f })
                {
                    // side grille (on the side face, built as a thin plate)
                    b.RoundedBox(new Vector3(s * 0.181f, 0.32f, 0.02f), new Vector3(0.012f, 0.06f, 0.13f), 0.004f, M(P, Gunmetal), 2);
                    for (int k = 0; k < 4; k++) b.RoundedBox(new Vector3(s * 0.187f, 0.298f + k * 0.015f, 0.02f), new Vector3(0.006f, 0.006f, 0.12f), 0.002f, M(P, Brass), 1);
                }
                foreach (var x in new[] { -0.155f, 0.155f })
                    foreach (var z in new[] { -0.15f, 0.15f })
                        Bolt(b, new Vector3(x, 0.43f, z), Vector3.up, 0.008f, M(P, Gunmetal));
                foreach (var x in new[] { -0.15f, -0.05f, 0.05f, 0.15f }) Bolt(b, new Vector3(x, 0.262f, -0.17f), new Vector3(0, 0, -1), 0.007f, M(P, Gunmetal));
                // panel seams: a belt round the body and the back plate
                foreach (var s in new[] { -1f, 1f })
                {
                    SeamLine(b, P, new Vector3(s * 0.181f, 0.385f, -0.13f), new Vector3(s * 0.181f, 0.385f, 0.13f), new Vector3(s, 0, 0));
                    SeamLine(b, P, new Vector3(s * 0.181f, 0.27f, -0.13f), new Vector3(s * 0.181f, 0.27f, 0.13f), new Vector3(s, 0, 0));
                }
                PanelFrame(b, P, new Vector3(0, 0.33f, 0), new Vector3(0.36f, 0.2f, 0.34f), new Vector3(0, 0, -1), 0.047f);
                PanelFrame(b, P, new Vector3(0, 0.33f, 0), new Vector3(0.36f, 0.2f, 0.34f), new Vector3(0, 0, 1), 0.052f);
                // carry handle behind the head
                foreach (var x in new[] { -0.07f, 0.07f }) b.Lathe(new Vector3(x, 0.435f, 0.12f), Disc(0.01f, 0.045f, 0.002f), M(P, Gunmetal), null, 8);
                b.RoundedBox(new Vector3(0, 0.482f, 0.12f), new Vector3(0.16f, 0.016f, 0.02f), 0.006f, M(P, Gunmetal), 2);
                // belly dome and its ring
                b.Lathe(new Vector3(0, 0.235f, 0), Dome(0.13f, 0.09f, 8), M("belly dome", Gunmetal), DownRot, 28);
                b.Lathe(new Vector3(0, 0.226f, 0), Disc(0.14f, 0.012f, 0.003f), M("belly dome", Brass), null, 28);
                // battery pack and servo controller on the back
                b.RoundedBox(new Vector3(0, 0.33f, 0.18f), new Vector3(0.14f, 0.08f, 0.03f), 0.01f, M("battery pack", Gunmetal), 2);
                foreach (var x in new[] { -0.04f, 0f, 0.04f }) b.RoundedBox(new Vector3(x, 0.33f, 0.197f), new Vector3(0.022f, 0.06f, 0.006f), 0.003f, M("battery pack", Brass), 1);
                b.RoundedBox(new Vector3(0.13f, 0.35f, 0.178f), new Vector3(0.05f, 0.04f, 0.022f), 0.008f, M("servo controller", ToyRed), 2);
                // the spinneret under the battery pack: a brass nozzle out of the rear
                b.Lathe(new Vector3(0, 0.262f, 0.17f), Disc(0.03f, 0.02f, 0.005f), M("spinneret", Gunmetal), BackRot, 16);
                b.Lathe(new Vector3(0, 0.262f, 0.19f), Dome(0.018f, 0.016f), M("spinneret", Brass), BackRot, 12);
                // hip sockets
                foreach (var (_, x, z, _) in Legs) b.Lathe(new Vector3(x * 0.17f, HipY, z), Disc(0.045f, 0.03f, 0.006f), M("body shell", Gunmetal), SideRot(x), 18);
                Done(b, _shell);
            }
            for (int i = 0; i < 6; i++)
            {
                var (part, x, _, _) = Legs[i];
                var bh = new Batch();   // hip ball + a chunky thigh rising steeply outwards
                bh.Lathe(new Vector3(0, -0.045f, 0), Ball(0.045f), M(part, ToyRed), null, 18);
                bh.Lathe(new Vector3(x * 0.04f, 0, 0), Dome(0.022f, 0.012f), M(part, Brass), SideRot(x), 12);
                var tilt = Batch.R(Vector3.forward, x * 0.75f);
                bh.RoundedBox(new Vector3(x * 0.075f, 0.07f, 0), new Vector3(0.18f, 0.066f, 0.076f), 0.02f, M(part, ToyGreen), 2, tilt);
                bh.RoundedBox(new Vector3(x * 0.075f, 0.07f, 0), new Vector3(0.13f, 0.07f, 0.082f), 0.006f, M(part, ToyGreenDeep), 1, tilt);   // band
                foreach (var t in new[] { 0.045f, 0.105f }) Bolt(bh, new Vector3(x * t, t * 0.93f, -0.04f), new Vector3(0, 0, -1), 0.0055f, M(part, Gunmetal));
                {
                    var dir = new Vector3(x * Mathf.Cos(0.75f), Mathf.Sin(0.75f), 0);
                    var nrm = new Vector3(-x * Mathf.Sin(0.75f), Mathf.Cos(0.75f), 0);
                    var top = new Vector3(x * 0.075f, 0.07f, 0.022f) + nrm * 0.033f;
                    Pipe(bh, part, new[] { top - dir * 0.08f + nrm * 0.006f, top + nrm * 0.02f, top + dir * 0.075f + nrm * 0.006f }, 0.0065f, Rubber, Brass);
                }
                Done(bh, _hip[i]);
                var bk = new Batch();   // big knee joint, olive upper shin, heavy red lower shin, puck foot
                bk.Lathe(new Vector3(0, 0, -0.046f), Disc(0.052f, 0.092f, 0.01f), M(part, ToyRed), BackRot, 22);
                bk.Lathe(new Vector3(0, 0, -0.048f), Dome(0.024f, 0.014f), M(part, Brass), FwdRot, 14);
                bk.Lathe(new Vector3(0, 0, 0.048f), Dome(0.024f, 0.014f), M(part, Brass), BackRot, 14);
                var shin = Batch.R(Vector3.forward, -x * 0.16f);
                bk.RoundedBox(new Vector3(x * 0.016f, -0.1f, 0), new Vector3(0.07f, 0.1f, 0.074f), 0.02f, M(part, ToyGreen), 2, shin);
                Bolt(bk, new Vector3(x * 0.016f, -0.1f, -0.038f), new Vector3(0, 0, -1), 0.0055f, M(part, Gunmetal));
                bk.RoundedBox(new Vector3(x * 0.036f, -0.165f, 0), new Vector3(0.082f, 0.018f, 0.084f), 0.006f, M(part, Gunmetal), 1, shin);
                bk.RoundedBox(new Vector3(x * 0.055f, -0.275f, 0), new Vector3(0.08f, 0.2f, 0.082f), 0.022f, M(part, ToyRed), 2, shin);
                // a second joint mid-shin (red disc, brass caps, gunmetal band), bolts and a hose down the leg, as on the sheet
                var mid = new Vector3(x * 0.05f, -0.245f, 0);
                bk.Lathe(mid + new Vector3(0, 0, -0.046f), Disc(0.034f, 0.092f, 0.007f), M(part, ToyRedDeep), BackRot, 18);
                foreach (var zs in new[] { -1f, 1f }) bk.Lathe(mid + new Vector3(0, 0, zs * 0.046f), Dome(0.016f, 0.01f), M(part, Brass), zs < 0 ? FwdRot : BackRot, 12);
                bk.RoundedBox(new Vector3(x * 0.062f, -0.31f, 0), new Vector3(0.086f, 0.014f, 0.088f), 0.005f, M(part, Gunmetal), 1, shin);
                foreach (var y in new[] { -0.2f, -0.34f }) Bolt(bk, new Vector3(x * (0.055f + (y + 0.275f) * -0.16f), y, -0.042f), new Vector3(0, 0, -1), 0.0055f, M(part, Gunmetal));
                Pipe(bk, part, new[] { new Vector3(x * 0.03f, -0.03f, 0.05f), new Vector3(x * 0.05f, -0.15f, 0.052f), new Vector3(x * 0.07f, -0.26f, 0.046f), new Vector3(x * 0.078f, -0.36f, 0.036f) }, 0.006f, Rubber, Brass);
                Done(bk, _knee[i]);
                // the ankle (on the shin's axis) keeps the puck foot flat on the ground
                var bf = new Batch();
                bf.Lathe(Vector3.zero, Disc(0.024f, 0.022f, 0.004f), M(part, Gunmetal), null, 12);
                bf.Lathe(new Vector3(0, -SoleDrop, 0), Disc(0.056f, 0.034f, 0.008f), M(part, Rubber), null, 22);
                bf.Lathe(new Vector3(0, -0.01f, 0), Disc(0.046f, 0.007f, 0.002f), M(part, Brass), null, 20);
                Done(bf, _foot[i]);
            }
            // the gripping jaws: two short family arms under the front, the grippers meet below the face
            foreach (var (jaw, sd) in new[] { (_jawL, -1f), (_jawR, 1f) })
                BuildArmModule(jaw, sd, sd < 0 ? "left arm" : "right arm", new ArmSpec { paint = ToyGreen, paintDeep = ToyGreenDeep, red = ToyRed, clawScale = 1.15f, twist = 20f });
            BuildHeadModule(() => FamilyHead(head, "head", new HeadSpec
            {
                w = 0.34f, h = 0.27f, d = 0.28f, eyes = 2, eyeX = 0.08f, eyeScale = 0.95f, eyeY = 0.52f, earY = 0.5f, paint = ToyGreen,
                beacon = true, antennas = true, mouth = true, eyePart = "eye pair", antennaPart = "antenna pair",
            }));
        }

        // ------------------------------------------------------------ Layer B: the tripod gait on planted feet

        /// <summary>Longest stride (model units of foot travel per step): a quick little skitter.</summary>
        const float MaxStride = 0.28f;
        /// <summary>Slowest stepping (rad/s of phase), also the rate for turning on the spot.</summary>
        const float MinRate = 16f;
        /// <summary>How high a swinging foot lifts (model units).</summary>
        const float StepLift = 0.075f;

        /// <summary>The front legs fan forward, the rear legs back (deg about the vertical at the hip).</summary>
        static float Splay(int i) => Legs[i].z < 0 ? -Legs[i].x * 35 : Legs[i].z > 0 ? Legs[i].x * 35 : 0;

        /// <summary>Spiderbot's cadence: each half cycle the planted tripod's feet travel back by exactly the
        /// ground the body covers, at most MaxStride; quicker steps beyond that.</summary>
        public override float StepRate(float moving01, float speed)
        {
            float s = Mathf.Max(1e-4f, transform.lossyScale.y);
            return Mathf.Max(MinRate, speed / s * Mathf.PI / MaxStride);
        }

        /// <summary>A leg's ankle at rest (shell space): the modelled pose turned by the splay.</summary>
        Vector3 AnkleHome(int i)
        {
            float x = Legs[i].x;
            return _hip[i].localPosition + Quaternion.Euler(0, Splay(i), 0) * new Vector3(x * (Thigh.x + Shin.x), Thigh.y + Shin.y, 0);
        }

        /// <summary>Two-bone leg IK, knee up (as the sheet's high knees): turns the hip about the vertical
        /// towards the ankle target (shell space), then solves the hip and knee in the leg's vertical plane.
        /// The ankle keeps the puck foot level with the body. At the rest target it returns the modelled pose.</summary>
        void PoseLeg(int i, Vector3 target)
        {
            float x = Legs[i].x;
            var v = target - _hip[i].localPosition;
            float yaw = Mathf.Atan2(-x * v.z, x * v.x) * Mathf.Rad2Deg;
            float u = new Vector2(v.x, v.z).magnitude, y = v.y;
            float l1 = Thigh.magnitude, l2 = Shin.magnitude;
            float d = Mathf.Clamp(Mathf.Sqrt(u * u + y * y), Mathf.Abs(l1 - l2) + 1e-4f, l1 + l2 - 1e-4f);
            float phi = Mathf.Atan2(y, u);
            float A = Mathf.Acos(Mathf.Clamp((l1 * l1 + d * d - l2 * l2) / (2 * l1 * d), -1, 1));
            float thighDir = phi + A;
            var knee = new Vector2(Mathf.Cos(thighDir), Mathf.Sin(thighDir)) * l1;
            var ankle = new Vector2(Mathf.Cos(phi), Mathf.Sin(phi)) * d;
            float shinDir = Mathf.Atan2(ankle.y - knee.y, ankle.x - knee.x);
            float alpha = (thighDir - Mathf.Atan2(Thigh.y, Thigh.x)) * Mathf.Rad2Deg;
            float beta = (shinDir - Mathf.Atan2(Shin.y, Shin.x)) * Mathf.Rad2Deg - alpha;
            _hip[i].localRotation = Quaternion.Euler(0, yaw, x * alpha);   // + raises the leg on both sides
            _knee[i].localRotation = Quaternion.Euler(0, 0, x * beta);
            _foot[i].localRotation = Quaternion.Inverse(_hip[i].localRotation * _knee[i].localRotation);
        }

        /// <summary>The jaws' pitch (deg): held forward and down, ready to grab; raised in front with a part,
        /// higher to lift it into a destination; reaching goes down towards the floor ahead.</summary>
        static float ArmPitch(float hold, float lift, float reach) => Mathf.Lerp(-32 * (1 - hold) - 80 * hold - 30 * lift, -18, reach);

        /// <summary>Jaws' sideways angle at rest (deg).</summary>
        const float RestInward = 3f;

        /// <summary>Spiderbot's head: curious, quick to follow, scans more than the others, watches its work.</summary>
        static readonly HeadAttention Attention = new() { aheadMax = 40f, scanDeg = 9f, scanRate = 0.9f, follow = 5f, watchWork = true };

        RobotAnim _fa;

        protected override void PoseForDepth(float depth) => PoseBody(_fa, depth);

        /// <summary>The body, the six legs and the jaws for one frame at a pick-up depth (0 = upright,
        /// 1 = the normal crouch, up to 2 = further for a part below its feet). The planted feet never move:
        /// the body crouches and tips forward on its legs.</summary>
        void PoseBody(RobotAnim a, float depth)
        {
            float s = Mathf.Max(1e-4f, transform.lossyScale.y);
            float walking = Mathf.Clamp01(a.gait / 0.45f);
            float rate = StepRate(a.speed01, a.speed);
            float stride = a.speed / s * Mathf.PI / rate * walking;               // foot travel per half cycle
            float arc = a.turnRate * Mathf.PI / rate * walking;                   // body turn per half cycle (deg)
            float bend = a.bend * depth, reach = Mathf.Min(1, a.armReach * depth), deeper = Mathf.Clamp01(bend - 1);
            float crouch = 0.07f * Ease(bend) + 0.05f * deeper;
            float tip = 13f * Ease(bend) + 9f * deeper;
            float bob = Mathf.Abs(Mathf.Sin(a.phase)) * 0.008f * walking;   // the body rises a touch mid-step
            float idle = (1 - walking) * (1 - Mathf.Min(1, bend));
            float breathe = 0.002f * Mathf.Sin(a.time * 2.1f) * idle;
            _shell.localPosition = new Vector3(0, bob - crouch + breathe, 0);
            _shell.localRotation = Quaternion.Euler(tip, 0, Mathf.Sin(a.phase) * 1.2f * walking);
            var toShell = Quaternion.Inverse(_shell.localRotation);
            for (int i = 0; i < 6; i++)
            {
                float c = Mathf.Repeat(a.phase + (Legs[i].group == 0 ? 0 : Mathf.PI), 2 * Mathf.PI) / Mathf.PI;   // 0..1 planted, 1..2 swinging
                var home = AnkleHome(i);
                // planted: the foot travels back (and round, turning) against the body's motion; swinging: it
                // lifts and reaches forward to where the next stance starts
                Vector3 Stance(float u) => Quaternion.Euler(0, arc * (0.5f - u), 0) * home + new Vector3(0, 0, stride * (0.5f - u));
                Vector3 foot = c < 1 ? Stance(c) : Vector3.Lerp(Stance(1), Stance(0), Ease(c - 1)) + Vector3.up * (StepLift * Mathf.Sin((c - 1) * Mathf.PI) * walking);
                // the ankle target in the shell's frame (the body moved and tipped, the feet did not)
                PoseLeg(i, toShell * (foot - _shell.localPosition));
            }
            float pitch = ArmPitch(a.armHold, a.armLift, reach) + 12f * deeper;
            famArmPitch = pitch;
            float sway = Mathf.Sin(a.time * 1.7f) * 3f * (1 - Mathf.Max(a.armHold, reach)) * (1 - walking);   // the idle jaws twitch a little
            _jawL.localRotation = Quaternion.Euler(pitch + sway, 0, RestInward);
            _jawR.localRotation = Quaternion.Euler(pitch - sway, 0, -RestInward);
            PoseElbows(a, 22);
        }

        public override void Animate(RobotAnim a)
        {
            _fa = a;
            PoseBody(a, 1);
            FloorGuard(a);
            PoseTelescopes(0);
            FamilyAim(a, ArmPitch(a.armHold, a.armLift, a.armReach), RestInward);
            FamilyGrip(a, RestInward);
            FamilyClaws(a);
            if (a.gesture > 0)
            {
                // waves its front-left leg
                float w = Mathf.Sin(a.gesture * Mathf.PI);
                _hip[0].localRotation = Quaternion.Euler(-40 * w, Splay(0), -55 * w + Mathf.Sin(a.gesture * 28) * 15 * w);
                _knee[0].localRotation = Quaternion.Euler(0, 0, 20 * w);
                a.lookWeight = 1;
                LookHead(a, 45);
                return;
            }
            FamilyLookHead(a, Mathf.Clamp01(a.gait / 0.45f), Attention);
        }
    }
}
