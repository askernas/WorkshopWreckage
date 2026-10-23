using UnityEngine;

namespace WorkshopWreckage
{
    /// <summary>Unibot after resources/unibot.png: an olive-green single-wheeled
    /// balancing courier. Big rounded riveted head with one large lamp eye in a bolted
    /// bezel, red ear discs, antennas from the top corners to red balls, a slatted back
    /// vent; rounded torso with slatted chest grille and slot, red shoulder discs and
    /// segmented arms with red pincers; a dark undercarriage with brass gyro
    /// stabilisers and springs; an olive fork down to a fat treaded tyre with a red
    /// bolted hub. Leans into acceleration and wobbles to keep its balance.
    /// Parts: head shell, optical eye, antenna pair, torso, left arm, right arm,
    /// gyro stabilizer, wheel hub, tire.</summary>
    public class UnibotModel : RobotModel
    {
        Transform _wheel, _lean, _leanGeo, _armL, _armR, _gyro;
        float _spin, _lean01, _lastSpeed;
        public override float ModelHeight => 1.2f;
        const float WheelR = 0.215f, Axle = 0.215f, TorsoY = 0.58f, ShoulderY = 0.63f, NeckY = 0.71f;

        protected override void CreatePivots()
        {
            _wheel = Pivot("Wheel", body, new Vector3(0, Axle, 0));
            _lean = Pivot("Lean", body, new Vector3(0, Axle, 0));                 // everything above the axle leans
            _leanGeo = Pivot("LeanGeo", _lean, new Vector3(0, -Axle, 0));
            _gyro = Pivot("Gyro", _leanGeo, new Vector3(0, 0.43f, 0));
            _armL = Pivot("ArmL", _leanGeo, new Vector3(-0.19f, ShoulderY, 0));
            _armR = Pivot("ArmR", _leanGeo, new Vector3(0.19f, ShoulderY, 0));
            head = Pivot("Head", _leanGeo, new Vector3(0, NeckY, 0));
            carry = Pivot("Carry", _leanGeo, new Vector3(0, 0.49f, 0.36f));
            Mount(head, "head shell", Vector3.up, 0.05f);
            Mount(_armL, "left arm", Vector3.left);
            Mount(_armR, "right arm", Vector3.right);
            Mount(_gyro, "gyro stabilizer", Vector3.right, 0.035f);
            Mount(Pivot("ForkMount", _leanGeo, new Vector3(0, 0.43f, 0)), "wheel hub", Vector3.down, 0.05f);
            Mount(_wheel, "tire", Vector3.right, 0.05f);
        }

        protected override Vector3? ClampPoint => new Vector3(0, 0.56f, Has("torso") ? 0.115f : 0.03f);

        /// <summary>Without the torso: spine, shoulder axle and the undercarriage plate frame.</summary>
        protected override void BuildFrame()
        {
            if (Has("torso")) return;
            var b = new Batch();
            b.RoundedBox(new Vector3(0, 0.57f, 0), new Vector3(0.05f, 0.24f, 0.05f), 0.01f, Gunmetal, 2);
            b.RoundedBox(new Vector3(0, ShoulderY, 0), new Vector3(0.38f, 0.03f, 0.03f), 0.008f, Gunmetal, 2);
            b.RoundedBox(new Vector3(0, 0.455f, 0), new Vector3(0.21f, 0.02f, 0.16f), 0.006f, Gunmetal, 2);
            b.RoundedBox(new Vector3(0, NeckY - 0.01f, 0), new Vector3(0.08f, 0.02f, 0.08f), 0.006f, Brass, 2);
            b.Build(_leanGeo, "G_frame", true);
        }

        protected override void BuildGeometry()
        {
            BuildWheel();
            BuildBody();
            {
                var b = new Batch();   // brass gyro drums that spin
                foreach (var s in new[] { -1f, 1f })
                {
                    b.Lathe(new Vector3(s * 0.095f, 0, 0), Disc(0.045f, 0.05f, 0.008f), M("gyro stabilizer", Brass), SideRot(s), 18);
                    b.Lathe(new Vector3(s * 0.145f, 0, 0), Disc(0.03f, 0.012f, 0.003f), M("gyro stabilizer", Gunmetal), SideRot(s), 14);
                    for (int k = 0; k < 6; k++)
                    {
                        float a = k * Mathf.PI / 3;
                        b.Box(new Vector3(s * 0.12f, Mathf.Sin(a) * 0.045f, Mathf.Cos(a) * 0.045f), new Vector3(0.04f, 0.008f, 0.008f), M("gyro stabilizer", Gunmetal), false, Batch.R(Vector3.right, a));
                    }
                }
                Done(b, _gyro);
            }
            foreach (var (arm, s, part) in new[] { (_armL, -1f, "left arm"), (_armR, 1f, "right arm") })
            {
                DetailedArm(arm, s, part, part, ToyGreen, ToyGreenDeep, ToyRed, 1.15f, 1.35f);
            }
            DetailedHead(head, ToyGreen, new Vector3(0.36f, 0.32f, 0.32f), 1, 0.078f,
                "head shell", "optical eye", null, "antenna pair", "antenna pair", "head shell", "head shell", null, false, false);
        }

        void BuildWheel()
        {
            var b = new Batch();
            const string Tire = "tire", Hub = "wheel hub";
            // fat tyre: rounded cross-section revolved around the axle (X)
            // a fat balloon tyre (rounded shoulders), shallow staggered tread: a tyre, not a cog
            var tire = new[] { new Vector2(0.135f, -0.083f), new Vector2(0.18f, -0.086f), new Vector2(0.203f, -0.074f), new Vector2(0.213f, -0.045f), new Vector2(0.213f, 0.045f), new Vector2(0.203f, 0.074f), new Vector2(0.18f, 0.086f), new Vector2(0.135f, 0.083f) };
            b.Lathe(Vector3.zero, tire, M(Tire, Rubber), RightRot, 40);
            for (int k = 0; k < 30; k++)
            {
                float a = k * Mathf.PI / 15;
                foreach (var x in new[] { -0.036f, 0.036f })
                {
                    float aa = a + (x > 0 ? Mathf.PI / 30 : 0);
                    b.RoundedBox(new Vector3(x, Mathf.Sin(aa) * 0.214f, Mathf.Cos(aa) * 0.214f), new Vector3(0.064f, 0.008f, 0.026f), 0.003f, M(Tire, Rubber), 1, Batch.R(Vector3.right, -aa + Mathf.PI / 2));   // lying flat on the tread
                }
            }
            foreach (var s in new[] { -1f, 1f })
            {
                b.Lathe(new Vector3(s * 0.05f, 0, 0), Disc(0.135f, 0.028f, 0.008f), M(Hub, ToyRed), SideRot(s), 32);
                b.Lathe(new Vector3(s * 0.078f, 0, 0), Disc(0.055f, 0.012f, 0.004f), M(Hub, Gunmetal), SideRot(s), 20);
                b.Lathe(new Vector3(s * 0.09f, 0, 0), Dome(0.035f, 0.018f), M(Hub, Brass), SideRot(s), 16);
                for (int k = 0; k < 6; k++)
                {
                    float a = k * Mathf.PI / 3;
                    Bolt(b, new Vector3(s * 0.078f, Mathf.Sin(a) * 0.095f, Mathf.Cos(a) * 0.095f), new Vector3(s, 0, 0), 0.009f, M(Hub, Gunmetal));
                }
            }
            Done(b, _wheel);
        }

        void BuildBody()
        {
            var b = new Batch();
            // fork: olive arms from the hub up into the undercarriage, with dark bearings
            foreach (var s in new[] { -1f, 1f })
            {
                b.RoundedBox(new Vector3(s * 0.125f, Axle + 0.11f, 0.01f), new Vector3(0.03f, 0.25f, 0.06f), 0.012f, M("wheel hub", ToyGreen), 2);
                b.Lathe(new Vector3(s * 0.105f, Axle, 0), Disc(0.035f, 0.03f, 0.006f), M("wheel hub", Gunmetal), SideRot(s), 16);
                b.Rivet(new Vector3(s * 0.141f, Axle + 0.16f, 0.01f), new Vector3(s, 0, 0), 0.008f, M("wheel hub", Brass));
                // springs between fork and body
                for (int k = 0; k < 6; k++)
                    b.Lathe(new Vector3(s * 0.07f, 0.4f + k * 0.012f, 0.05f), Disc(0.014f, 0.006f, 0.002f), M("gyro stabilizer", Brass), null, 10);
            }
            // undercarriage
            b.RoundedBox(new Vector3(0, 0.455f, 0), new Vector3(0.21f, 0.05f, 0.16f), 0.015f, M("torso", Gunmetal), 2);
            foreach (var x in new[] { -0.07f, 0f, 0.07f }) b.Rivet(new Vector3(x, 0.455f, -0.081f), new Vector3(0, 0, -1), 0.008f, M("torso", Brass));
            // torso
            b.RoundedBox(new Vector3(0, TorsoY, 0), new Vector3(0.27f, 0.21f, 0.21f), 0.03f, M("torso", ToyGreen), 3);
            Grille(b, "torso", new Vector3(0, 0.6f, -0.106f), 0.14f, 0.075f, 6, false, -1);
            b.RoundedBox(new Vector3(0, 0.533f, -0.108f), new Vector3(0.08f, 0.016f, 0.008f), 0.004f, M("torso", Dark), 1);
            PanelFrame(b, "torso", new Vector3(0, TorsoY, 0), new Vector3(0.27f, 0.21f, 0.21f), new Vector3(0, 0, -1), 0.04f);
            Grille(b, "torso", new Vector3(0, 0.6f, 0.106f), 0.15f, 0.08f, 6, false, 1);
            foreach (var n in new[] { Vector3.left, Vector3.right, new Vector3(0, 0, 1) }) PanelFrame(b, "torso", new Vector3(0, TorsoY, 0), new Vector3(0.27f, 0.21f, 0.21f), n, 0.042f);
            b.Lathe(new Vector3(-0.19f, ShoulderY, 0), Disc(0.024f, 0.38f, 0.005f), M("torso", Gunmetal), RightRot, 12);   // shoulder axle
            foreach (var s in new[] { -1f, 1f })
            {
                Pipe(b, "torso", new[] { new Vector3(s * 0.07f, 0.545f, 0.1f), new Vector3(s * 0.07f, 0.505f, 0.128f), new Vector3(s * 0.07f, 0.46f, 0.1f), new Vector3(s * 0.07f, 0.442f, 0.078f) }, 0.009f, Brass, Gunmetal);
                Pipe(b, "torso", new[] { new Vector3(s * 0.133f, 0.505f, 0.03f), new Vector3(s * 0.158f, 0.465f, 0.05f), new Vector3(s * 0.13f, 0.43f, 0.04f) }, 0.0075f, Rubber, Brass);
            }
            for (int k = 0; k < 3; k++) b.Lathe(new Vector3(0, 0.685f + k * 0.009f, 0), Disc(0.05f - k * 0.004f, 0.009f, 0.003f), M("torso", Gunmetal), null, 16);
            Done(b, _leanGeo);
        }

        public override void Animate(RobotAnim a)
        {
            float s = transform.lossyScale.x;
            float v = a.speed01;
            _spin += a.speed * a.dt / Mathf.Max(1e-4f, WheelR * s) * Mathf.Rad2Deg;
            _wheel.localRotation = Quaternion.Euler(_spin, 0, 0);
            float accel = (v - _lastSpeed) / Mathf.Max(a.dt, 1e-4f);
            _lastSpeed = v;
            _lean01 = Mathf.Lerp(_lean01, Mathf.Clamp(v * 8 + accel * 2.5f, -14, 22), a.dt * 4);
            float wobble = Mathf.Sin(a.time * 3.1f) * 2.2f + Mathf.Sin(a.time * 7.3f) * 0.8f;
            _lean.localRotation = Quaternion.Euler(_lean01 + wobble, 0, Mathf.Sin(a.time * 2.3f) * 1.5f + a.turnRate * 0.03f);
            _gyro.localRotation = Quaternion.Euler(a.time * 720f, 0, 0);
            if (a.gesture > 0) _lean.localRotation *= Quaternion.Euler(0, Mathf.SmoothStep(0, 360, a.gesture), Mathf.Sin(a.gesture * Mathf.PI * 4) * 6);   // nervous pirouette
            float empty = 1 - Mathf.Max(a.armHold, a.armReach);
            float pitch = -70 * a.armHold - 40 * a.armLift - 45 * a.armReach - 16 * (1 - Mathf.Max(a.armHold, a.armReach));
            float flap = Mathf.Sin(a.time * 2.7f) * 8 * empty;
            _armL.localRotation = Quaternion.Euler(pitch + flap, 0, 8 * a.armHold - 20 * empty);   // at rest the arms swing wide beside the wheel
            _armR.localRotation = Quaternion.Euler(pitch - flap, 0, -8 * a.armHold + 20 * empty);
            PoseElbows(a, 38);
            LookHead(a);
        }
    }
}
