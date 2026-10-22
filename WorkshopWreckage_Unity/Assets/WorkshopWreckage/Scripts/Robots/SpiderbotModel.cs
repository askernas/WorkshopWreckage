using UnityEngine;

namespace WorkshopWreckage
{
    /// <summary>Spiderbot after resources/spideybot.png: an olive-green hexapod with
    /// red-and-brass joints. Riveted rounded body with slatted front and side grilles,
    /// a carry handle on top and a dark belly dome; the family head with two bezel eyes,
    /// mouth grille, red ear discs, antennas and a red beacon; six articulated legs
    /// (red ball hip, olive thigh rising outwards, red knee with brass cap, red shin
    /// down to a dark round foot pad) walking a tripod gait; red gripping jaws under
    /// the front. Carries its finds on its back.
    /// Parts: head, eye pair, antenna pair, body shell, belly dome, front/middle/rear
    /// left/right leg, gripping jaws, servo controller, battery pack.</summary>
    public class SpiderbotModel : RobotModel
    {
        static readonly (string part, float x, float z, int group)[] Legs =
        {
            ("front left leg", -1, -0.13f, 0), ("middle left leg", -1, 0f, 1), ("rear left leg", -1, 0.13f, 0),
            ("front right leg", 1, -0.13f, 1), ("middle right leg", 1, 0f, 0), ("rear right leg", 1, 0.13f, 1),
        };
        readonly Transform[] _hip = new Transform[6], _knee = new Transform[6];
        Transform _jawL, _jawR, _shell;
        public override float ModelHeight => 0.82f;
        const float HipY = 0.3f;

        protected override void CreatePivots()
        {
            _shell = Pivot("Shell", body, Vector3.zero);
            for (int i = 0; i < 6; i++)
            {
                var (_, x, z, _) = Legs[i];
                _hip[i] = Pivot("Hip" + i, _shell, new Vector3(x * 0.19f, HipY, -z));   // Unity z = -Godot z
                _knee[i] = Pivot("Knee" + i, _hip[i], new Vector3(x * 0.17f, 0.09f, 0));
            }
            _jawL = Pivot("JawL", _shell, new Vector3(-0.05f, 0.23f, 0.19f));
            _jawR = Pivot("JawR", _shell, new Vector3(0.05f, 0.23f, 0.19f));
            head = Pivot("Head", _shell, new Vector3(0, 0.405f, 0.03f));
            carry = Pivot("Carry", _shell, new Vector3(0, 0.43f, -0.15f));   // on its back, behind the head
            for (int i = 0; i < 6; i++) Mount(_hip[i], Legs[i].part, new Vector3(Legs[i].x, 0, 0), 0.035f);
            Mount(head, "head", Vector3.up, 0.045f);
            Mount(_jawL, "gripping jaws", new Vector3(0, 0, -1), 0.025f);
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
                b.RoundedBox(new Vector3(0, 0.32f, 0), new Vector3(0.36f, 0.16f, 0.34f), 0.035f, M(P, ToyGreen), 3);
                b.RoundedBox(new Vector3(0, 0.402f, 0.02f), new Vector3(0.3f, 0.012f, 0.28f), 0.005f, M(P, ToyGreenDeep), 2);   // top plate
                Grille(b, P, new Vector3(0, 0.315f, -0.172f), 0.15f, 0.07f, 5, false, -1);
                foreach (var s in new[] { -1f, 1f })
                {
                    // side grille (on the side face, built as a thin plate)
                    b.RoundedBox(new Vector3(s * 0.181f, 0.32f, 0.02f), new Vector3(0.012f, 0.06f, 0.13f), 0.004f, M(P, Gunmetal), 2);
                    for (int k = 0; k < 4; k++) b.RoundedBox(new Vector3(s * 0.187f, 0.298f + k * 0.015f, 0.02f), new Vector3(0.006f, 0.006f, 0.12f), 0.002f, M(P, Brass), 1);
                }
                foreach (var x in new[] { -0.155f, 0.155f })
                    foreach (var z in new[] { -0.15f, 0.15f })
                        b.Rivet(new Vector3(x, 0.4f, z), Vector3.up, 0.008f, M(P, Gunmetal));
                foreach (var x in new[] { -0.15f, -0.05f, 0.05f, 0.15f }) b.Rivet(new Vector3(x, 0.255f, -0.17f), new Vector3(0, 0, -1), 0.007f, M(P, Gunmetal));
                // carry handle behind the head
                foreach (var x in new[] { -0.07f, 0.07f }) b.Lathe(new Vector3(x, 0.405f, 0.12f), Disc(0.01f, 0.045f, 0.002f), M(P, Gunmetal), null, 8);
                b.RoundedBox(new Vector3(0, 0.452f, 0.12f), new Vector3(0.16f, 0.016f, 0.02f), 0.006f, M(P, Gunmetal), 2);
                // belly dome and its ring
                b.Lathe(new Vector3(0, 0.245f, 0), Dome(0.12f, 0.09f, 8), M("belly dome", Gunmetal), DownRot, 24);
                b.Lathe(new Vector3(0, 0.235f, 0), Disc(0.13f, 0.012f, 0.003f), M("belly dome", Brass), null, 24);
                // battery pack and servo controller on the back
                b.RoundedBox(new Vector3(0, 0.33f, 0.18f), new Vector3(0.14f, 0.08f, 0.03f), 0.01f, M("battery pack", Gunmetal), 2);
                foreach (var x in new[] { -0.04f, 0f, 0.04f }) b.RoundedBox(new Vector3(x, 0.33f, 0.197f), new Vector3(0.022f, 0.06f, 0.006f), 0.003f, M("battery pack", Brass), 1);
                b.RoundedBox(new Vector3(0.13f, 0.35f, 0.178f), new Vector3(0.05f, 0.04f, 0.022f), 0.008f, M("servo controller", ToyRed), 2);
                // hip sockets
                foreach (var (_, x, z, _) in Legs) b.Lathe(new Vector3(x * 0.17f, HipY, z), Disc(0.035f, 0.025f, 0.006f), M("body shell", Gunmetal), SideRot(x), 14);
                Done(b, _shell);
            }
            for (int i = 0; i < 6; i++)
            {
                var (part, x, _, _) = Legs[i];
                var bh = new Batch();   // hip ball + thigh rising outwards
                bh.Lathe(new Vector3(0, -0.035f, 0), Ball(0.035f), M(part, ToyRed), null, 16);
                bh.Lathe(new Vector3(x * 0.03f, 0, 0), Dome(0.018f, 0.01f), M(part, Brass), SideRot(x), 10);
                var tilt = Batch.R(Vector3.forward, x * 0.5f);
                bh.RoundedBox(new Vector3(x * 0.088f, 0.045f, 0), new Vector3(0.15f, 0.05f, 0.058f), 0.016f, M(part, ToyGreen), 2, tilt);
                bh.RoundedBox(new Vector3(x * 0.088f, 0.071f, 0), new Vector3(0.11f, 0.008f, 0.04f), 0.003f, M(part, ToyGreenDeep), 1, tilt);
                foreach (var t in new[] { 0.05f, 0.12f }) bh.Rivet(new Vector3(x * t, 0.02f + t * 0.5f, -0.029f), new Vector3(0, 0, -1), 0.005f, M(part, Gunmetal));
                Done(bh, _hip[i]);
                var bk = new Batch();   // knee joint + red shin down to the foot pad
                bk.Lathe(new Vector3(0, 0, -0.035f), Disc(0.042f, 0.07f, 0.008f), M(part, ToyRed), BackRot, 18);
                bk.Lathe(new Vector3(0, 0, -0.04f), Dome(0.02f, 0.012f), M(part, Brass), FwdRot, 12);
                bk.Lathe(new Vector3(0, 0, 0.04f), Dome(0.02f, 0.012f), M(part, Brass), BackRot, 12);
                var shin = Batch.R(Vector3.forward, -x * 0.18f);
                bk.RoundedBox(new Vector3(x * 0.022f, -0.09f, 0), new Vector3(0.055f, 0.09f, 0.058f), 0.016f, M(part, ToyGreen), 2, shin);
                bk.RoundedBox(new Vector3(x * 0.045f, -0.22f, 0), new Vector3(0.06f, 0.18f, 0.062f), 0.018f, M(part, ToyRed), 2, shin);
                bk.RoundedBox(new Vector3(x * 0.04f, -0.15f, 0), new Vector3(0.064f, 0.014f, 0.066f), 0.005f, M(part, Gunmetal), 1, shin);
                bk.Lathe(new Vector3(x * 0.06f, -0.335f, 0), Disc(0.022f, 0.025f, 0.004f), M(part, Gunmetal), null, 12);
                bk.Lathe(new Vector3(x * 0.06f, -0.39f, 0), Disc(0.048f, 0.032f, 0.008f), M(part, Rubber), null, 20);
                bk.Lathe(new Vector3(x * 0.06f, -0.36f, 0), Disc(0.04f, 0.006f, 0.002f), M(part, Brass), null, 18);
                Done(bk, _knee[i]);
            }
            foreach (var (jaw, s) in new[] { (_jawL, -1f), (_jawR, 1f) })
            {
                var b = new Batch();
                b.RoundedBox(new Vector3(0, -0.03f, -0.03f), new Vector3(0.026f, 0.06f, 0.06f), 0.008f, M("gripping jaws", ToyRed), 2, Batch.R(Vector3.right, 0.4f));
                b.RoundedBox(new Vector3(-s * 0.012f, -0.07f, -0.055f), new Vector3(0.024f, 0.032f, 0.032f), 0.008f, M("gripping jaws", ToyRed), 2);
                b.Rivet(new Vector3(s * 0.013f, -0.03f, -0.03f), new Vector3(s, 0, 0), 0.005f, M("gripping jaws", Brass));
                Done(b, jaw);
            }
            DetailedHead(head, ToyGreen, new Vector3(0.3f, 0.24f, 0.26f), 2, 0.042f,
                "head", "eye pair", null, "antenna pair", "antenna pair", "head", "head", "head", false, true, 0.95f);
        }

        public override void Animate(RobotAnim a)
        {
            // tripod gait: one group lifts and swings forward while the other pushes back
            for (int i = 0; i < 6; i++)
            {
                var (_, x, _, group) = Legs[i];
                float ph = a.phase + (group == 0 ? 0 : Mathf.PI);
                float swing = Mathf.Sin(ph) * 22 * a.gait;
                float lift = Mathf.Max(0, Mathf.Cos(ph)) * 22 * a.gait;
                _hip[i].localRotation = Quaternion.Euler(0, swing * -x, x * lift);   // +lift raises the foot on both sides
                _knee[i].localRotation = Quaternion.Euler(0, 0, x * (lift * 0.6f + a.bend * 25));
            }
            if (a.gesture > 0)
            {
                // waves its front-left leg
                float w = Mathf.Sin(a.gesture * Mathf.PI);
                _hip[0].localRotation = Quaternion.Euler(-40 * w, 0, -55 * w + Mathf.Sin(a.gesture * 28) * 15 * w);
            }
            _shell.localPosition = new Vector3(0, Mathf.Abs(Mathf.Sin(a.phase)) * 0.012f * a.gait - a.bend * 0.06f, 0);
            _shell.localRotation = Quaternion.Euler(a.bend * 12, 0, Mathf.Sin(a.phase) * 1.5f * a.gait);
            float jaw = 25 * Mathf.Max(a.armReach, a.armLift) + 6 * Mathf.Sin(a.time * 1.7f) * (1 - a.gait);
            _jawL.localRotation = Quaternion.Euler(0, jaw, 0);
            _jawR.localRotation = Quaternion.Euler(0, -jaw, 0);
            LookHead(a, 45);
        }
    }
}
