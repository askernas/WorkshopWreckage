using UnityEngine;

namespace WorkshopWreckage
{
    /// <summary>Spiderbot after resources/spideybot.png: olive-green hexapod with red
    /// leg joints and shins, round foot pads, a belly dome, a two-eyed head with red
    /// beacon, and red gripping jaws under the front. Exactly six articulated legs in
    /// a tripod gait (front-left, middle-right, rear-left alternate with the others).
    /// Carries its finds on its back. Parts: head, eye pair, antenna pair, body shell,
    /// belly dome, front/middle/rear left/right leg, gripping jaws, servo controller,
    /// battery pack.</summary>
    public class SpiderbotModel : RobotModel
    {
        static readonly (string part, float x, float z, int group)[] Legs =
        {
            ("front left leg", -1, -0.13f, 0), ("middle left leg", -1, 0f, 1), ("rear left leg", -1, 0.13f, 0),
            ("front right leg", 1, -0.13f, 1), ("middle right leg", 1, 0f, 0), ("rear right leg", 1, 0.13f, 1),
        };
        readonly Transform[] _hip = new Transform[6], _knee = new Transform[6];
        Transform _jawL, _jawR, _shell;
        public override float ModelHeight => 0.78f;
        static Material Green => Paint(new Color(0.36f, 0.42f, 0.2f), 0.5f);
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
            _jawL = Pivot("JawL", _shell, new Vector3(-0.05f, 0.23f, 0.2f));
            _jawR = Pivot("JawR", _shell, new Vector3(0.05f, 0.23f, 0.2f));
            head = Pivot("Head", _shell, new Vector3(0, 0.39f, 0.02f));
            carry = Pivot("Carry", _shell, new Vector3(0, 0.42f, -0.17f));   // on its back, behind the head
        }

        protected override void BuildGeometry()
        {
            {
                var b = new Batch();
                b.Box(new Vector3(0, 0.31f, 0), new Vector3(0.36f, 0.16f, 0.36f), M("body shell", Green));
                b.Box(new Vector3(0, 0.31f, -0.182f), new Vector3(0.14f, 0.06f, 0.008f), M("body shell", Dark));
                for (int k = 0; k < 4; k++) b.Box(new Vector3(0, 0.29f + k * 0.014f, -0.187f), new Vector3(0.13f, 0.005f, 0.005f), M("body shell", Brass));
                b.Box(new Vector3(0, 0.395f, 0.03f), new Vector3(0.1f, 0.012f, 0.03f), M("body shell", Dark));   // carry handle
                foreach (var x in new[] { -0.16f, 0.16f })
                    foreach (var z in new[] { -0.16f, 0.16f })
                        b.Box(new Vector3(x, 0.38f, z), new Vector3(0.014f, 0.01f, 0.014f), M("body shell", Dark));
                b.Cylinder(new Vector3(0, 0.215f, 0), 0.11f, 0.06f, M("belly dome", Dark), null, 18);
                b.Cylinder(new Vector3(0, 0.185f, 0), 0.075f, 0.03f, M("belly dome", Dark), null, 18);
                b.Box(new Vector3(0, 0.33f, 0.185f), new Vector3(0.12f, 0.07f, 0.02f), M("battery pack", Dark));
                b.Box(new Vector3(0.12f, 0.345f, 0.18f), new Vector3(0.05f, 0.04f, 0.02f), M("servo controller", Brass));
                Done(b, _shell);
            }
            for (int i = 0; i < 6; i++)
            {
                var (part, x, _, _) = Legs[i];
                var bh = new Batch();   // hip joint + thigh rising outwards
                bh.Cylinder(Vector3.zero, 0.04f, 0.05f, M(part, Red), AxisX);
                bh.Box(new Vector3(x * 0.085f, 0.045f, 0), new Vector3(0.17f, 0.05f, 0.055f), M(part, Green), false, Batch.R(Vector3.forward, x * 0.5f));
                Done(bh, _hip[i]);
                var bk = new Batch();   // knee + shin down to the foot pad
                bk.Cylinder(Vector3.zero, 0.042f, 0.06f, M(part, Red), AxisZ);
                bk.Cylinder(new Vector3(0, 0, 0), 0.018f, 0.065f, M(part, Brass), AxisZ);
                bk.Box(new Vector3(x * 0.03f, -0.17f, 0), new Vector3(0.06f, 0.29f, 0.06f), M(part, Red), false, Batch.R(Vector3.forward, -x * 0.18f));
                bk.Cylinder(new Vector3(x * 0.06f, -0.375f, 0), 0.045f, 0.03f, M(part, Dark));
                Done(bk, _knee[i]);
            }
            foreach (var (jaw, s) in new[] { (_jawL, -1f), (_jawR, 1f) })
            {
                var b = new Batch();
                b.Box(new Vector3(0, -0.03f, -0.03f), new Vector3(0.025f, 0.06f, 0.06f), M("gripping jaws", Red), false, Batch.R(Vector3.right, 0.4f));
                b.Box(new Vector3(-s * 0.012f, -0.07f, -0.05f), new Vector3(0.022f, 0.03f, 0.03f), M("gripping jaws", Red));
                Done(b, jaw);
            }
            HeadKit(head, Green, new Vector3(0.3f, 0.22f, 0.24f), 2, "head", "eye pair", "antenna pair", "antenna pair", "head", "head", "head");
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
