using UnityEngine;

namespace WorkshopWreckage
{
    /// <summary>Unibot after resources/unibot.png, built from the family component catalogue
    /// (RobotFamilyKit), keeping its own big riveted head and face with one lamp eye, fitted with the
    /// family's canonical ear cones and antennas; family arms with the two-stage telescopic forearms and the articulated red grippers;
    /// an olive pressed-steel torso with a framed grille and slot, bolted shoulder flanges and a dark
    /// undercarriage with brass gyro stabilisers and springs. Its own locomotion (Layer B): an olive
    /// fork down to a fat treaded tyre on a red bolted hub. It rides upright when still, leans forward
    /// with its speed and a little more into acceleration (back when braking), and banks into turns like
    /// a cyclist; the wheel turns by the distance actually travelled. Manipulation and attention come
    /// from the family layer (Layer A).
    /// Parts: head shell, optical eye, antenna pair, torso, left arm, right arm, gyro stabilizer,
    /// wheel hub, tire.</summary>
    public class UnibotModel : RobotModel
    {
        Transform _wheel, _lean, _leanGeo, _armL, _armR, _gyro;
        public override float ModelHeight => 1.2f;
        const float WheelR = 0.215f, Axle = 0.215f, TorsoY = 0.58f, ShoulderY = 0.63f, NeckY = 0.71f, TorsoW = 0.27f, TorsoD = 0.21f, TorsoH = 0.21f;

        protected override Material Skin(Material m) => FamilySkin(m);
        protected override Material BoltSkin(Material m) => FamilyBoltSkin(m);

        protected override void CreatePivots()
        {
            _wheel = Pivot("Wheel", body, new Vector3(0, Axle, 0));
            _lean = Pivot("Lean", body, new Vector3(0, Axle, 0));                 // everything above the axle leans
            _leanGeo = Pivot("LeanGeo", _lean, new Vector3(0, -Axle, 0));
            _gyro = Pivot("Gyro", _leanGeo, new Vector3(0, 0.43f, 0));
            _armL = Pivot("ArmL", _leanGeo, new Vector3(-0.19f, ShoulderY, 0));
            _armR = Pivot("ArmR", _leanGeo, new Vector3(0.19f, ShoulderY, 0));
            _armL.localScale = _armR.localScale = Vector3.one * 1.08f;               // the concept's long arms
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
            Material G = Skin(Gunmetal), Br = Skin(Brass);
            b.RoundedBox(new Vector3(0, 0.57f, 0), new Vector3(0.05f, 0.24f, 0.05f), 0.01f, G, 2);
            b.RoundedBox(new Vector3(0, ShoulderY, 0), new Vector3(0.3f, 0.03f, 0.03f), 0.008f, G, 2);
            b.RoundedBox(new Vector3(0, 0.455f, 0), new Vector3(0.21f, 0.02f, 0.16f), 0.006f, G, 2);
            b.RoundedBox(new Vector3(0, NeckY - 0.01f, 0), new Vector3(0.08f, 0.02f, 0.08f), 0.006f, Br, 2);
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
                FamilyArm(arm, s, part, new ArmSpec { paint = ToyGreen, paintDeep = ToyGreenDeep, red = ToyRed, clawScale = 1.08f, twist = 20f });
            // Unibot keeps its own head design (the user's choice, 2026-10-24): the earlier detailed head
            DetailedHead(head, ToyGreen, new Vector3(0.36f, 0.32f, 0.32f), 1, 0.078f,
                "head shell", "optical eye", null, "antenna pair", "antenna pair", "head shell", "head shell", null, false, false, 1f, true);   // with the canonical ear cones and antennas
        }

        void BuildWheel()
        {
            var b = new Batch();
            const string Tire = "tire", HubP = "wheel hub";
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
                b.Lathe(new Vector3(s * 0.05f, 0, 0), Disc(0.135f, 0.028f, 0.008f), M(HubP, ToyRed), SideRot(s), 32);
                Hub(b, HubP, X(s * 0.078f), s, 0.06f, 6, 0.008f);   // the family bearing hub on the wheel
            }
            Done(b, _wheel);
        }

        void BuildBody()
        {
            var b = new Batch();
            const string P = "torso";
            float front = -TorsoD / 2;
            // fork: olive arms from the hub up into the undercarriage, bolted bearing hubs at the axle
            foreach (var s in new[] { -1f, 1f })
            {
                b.ChamferBox(new Vector3(s * 0.125f, Axle + 0.11f, 0.01f), new Vector3(0.03f, 0.25f, 0.06f), 0.01f, M("wheel hub", ToyGreen), null, true);
                Hub(b, "wheel hub", new Vector3(s * 0.142f, Axle, 0), s, 0.032f, 4, 0.005f);
                Bolt(b, new Vector3(s * 0.141f, Axle + 0.16f, 0.01f), X(s), 0.007f, M("wheel hub", Gunmetal));
                for (int k = 0; k < 6; k++)   // springs between fork and body
                    b.Lathe(new Vector3(s * 0.07f, 0.4f + k * 0.012f, 0.05f), Disc(0.014f, 0.006f, 0.002f), M("gyro stabilizer", Brass), null, 10);
                GreaseRun(b, "wheel hub", new Vector3(s * 0.14f, Axle + 0.21f, 0.035f), X(s), 0.06f);
            }
            // undercarriage: a dark steel housing with a drive gear in front
            b.ChamferBox(new Vector3(0, 0.455f, 0), new Vector3(0.21f, 0.05f, 0.16f), 0.012f, M(P, Gunmetal), null, true);
            b.Lathe(new Vector3(0, 0.455f, -0.08f), Disc(0.026f, 0.012f, 0.003f), M(P, Gunmetal), FwdRot, 18);
            Nut(b, new Vector3(0, 0.455f, -0.092f), FwdRot, 0.01f, 0.005f, M(P, Gunmetal));
            foreach (var x in new[] { -0.07f, 0.07f }) Bolt(b, new Vector3(x, 0.455f, -0.081f), new Vector3(0, 0, -1), 0.007f, M(P, Gunmetal));
            // torso: soft-edged pressed steel, a raised front panel, a framed grille and the slot below it
            b.ChamferBox(new Vector3(0, TorsoY, 0), new Vector3(TorsoW, TorsoH, TorsoD), 0.026f, M(P, ToyGreen), null, true);
            b.ChamferBox(new Vector3(0, TorsoY + 0.004f, front - 0.004f), new Vector3(TorsoW - 0.045f, TorsoH - 0.05f, 0.012f), 0.007f, M(P, ToyGreen), null, true);
            b.ChamferBox(new Vector3(0, TorsoY - TorsoH / 2 + 0.011f, 0), new Vector3(TorsoW + 0.008f, 0.02f, TorsoD + 0.008f), 0.007f, M(P, ToyGreenDeep), null, true);   // bottom rim band
            FramedGrille(b, P, new Vector3(0, 0.605f, front - 0.01f), 0.13f, 0.07f, 6, false, -1);
            b.ChamferBox(new Vector3(0, 0.538f, front - 0.014f), new Vector3(0.09f, 0.03f, 0.008f), 0.004f, M(P, ToyGreenDeep), null, true);
            b.ChamferBox(new Vector3(0, 0.538f, front - 0.019f), new Vector3(0.06f, 0.01f, 0.004f), 0.001f, M(P, Dark));
            foreach (var x in new[] { -0.1f, 0.1f })
                foreach (var y in new[] { 0.505f, 0.655f })
                    Bolt(b, new Vector3(x, y, front - 0.01f), new Vector3(0, 0, -1), 0.0062f, M(P, Gunmetal));
            // shoulder flanges on the sides, bolted; the shoulder drums turn against them
            foreach (var s in new[] { -1f, 1f })
            {
                b.ChamferBox(new Vector3(s * (TorsoW / 2 + 0.004f), ShoulderY, 0), new Vector3(0.008f, 0.09f, 0.1f), 0.006f, M(P, Gunmetal));
                foreach (var y in new[] { -0.032f, 0.032f })
                    foreach (var z in new[] { -0.036f, 0.036f })
                        Bolt(b, new Vector3(s * (TorsoW / 2 + 0.008f), ShoulderY + y, z), X(s), 0.005f, M(P, Gunmetal));
                GreaseRun(b, P, new Vector3(s * TorsoW / 2, ShoulderY - 0.048f, 0.02f), X(s), 0.07f, 0.012f);
            }
            // back: bolted panel, framed vent; sides: bolted panels
            foreach (var n in new[] { Vector3.left, Vector3.right, new Vector3(0, 0, 1) }) PanelFrame(b, P, new Vector3(0, TorsoY, 0), new Vector3(TorsoW, TorsoH, TorsoD), n, 0.04f);
            FramedGrille(b, P, new Vector3(0, 0.6f, -front + 0.001f), 0.14f, 0.075f, 6, false, 1);
            GrimeBand(b, P, new Vector3(0, TorsoY - TorsoH / 2 + 0.001f, front - 0.004f), new Vector3(0, 0, -1), Vector3.right, TorsoW, 0.02f);
            foreach (var s in new[] { -1f, 1f })
            {
                Pipe(b, P, new[] { new Vector3(s * 0.07f, 0.545f, 0.1f), new Vector3(s * 0.07f, 0.505f, 0.128f), new Vector3(s * 0.07f, 0.46f, 0.1f), new Vector3(s * 0.07f, 0.442f, 0.078f) }, 0.009f, Brass, Gunmetal);
                Pipe(b, P, new[] { new Vector3(s * 0.133f, 0.505f, 0.03f), new Vector3(s * 0.158f, 0.465f, 0.05f), new Vector3(s * 0.13f, 0.43f, 0.04f) }, 0.0075f, Rubber, Brass);
            }
            // neck: a dark collar and a bright steel ring the head turns on
            b.Lathe(new Vector3(0, TorsoY + TorsoH / 2, 0), Disc(0.05f, 0.01f, 0.002f), M(P, Gunmetal), null, 20);
            b.Lathe(new Vector3(0, TorsoY + TorsoH / 2 + 0.01f, 0), Disc(0.038f, 0.012f, 0.002f), M(P, BrightSteel), null, 20);
            Done(b, _leanGeo);
        }

        // ------------------------------------------------------------ Layer B: the wheel and the balance

        float _spin, _gyroAng, _leanDeg, _leanVel, _bank, _lastV;
        Vector3 _lastPos; bool _hasLast;
        RobotAnim _fa;

        /// <summary>Unibot's head: quick, looks into a turn and at its work, scans a little while rolling.</summary>
        static readonly HeadAttention Attention = new() { aheadMax = 30f, scanDeg = 5f, scanRate = 0.9f, follow = 6f, watchWork = true };

        /// <summary>The arms' pitch (deg): held forward with a load, raised to lift, angled down and forward
        /// to reach (the body's lean brings the claws to the floor).</summary>
        static float ArmPitch(float hold, float lift, float reach) => Mathf.Lerp(-70 * hold - 40 * lift - 16 * (1 - hold), -14, reach);

        public override void ResetPose() { _hasLast = false; _leanDeg = _leanVel = _bank = 0; }

        protected override void PoseForDepth(float depth) => PoseBody(_fa, depth);

        /// <summary>The body above the axle and the arms for one frame at a pick-up depth: leaning forward
        /// over the wheel to reach down (further for a part lower than the wheel's contact), plus the
        /// balance lean and bank from the locomotion.</summary>
        void PoseBody(RobotAnim a, float depth)
        {
            float bend = a.bend * depth;
            float reach = Mathf.Min(1, a.armReach * depth);
            float lean = _leanDeg + 26f * Ease(bend) + 16f * Mathf.Clamp01(bend - 1);
            float still = (1 - Mathf.Clamp01(a.speed01 * 4)) * (1 - Mathf.Clamp01(bend));
            float settle = 0.35f * Mathf.Sin(a.time * 1.9f) * still;   // a faint active stabilisation, never a wobble
            _lean.localRotation = Quaternion.Euler(lean + settle, 0, _bank);
            float hold = Mathf.Max(a.armHold, reach), empty = 1 - hold;
            float pitch = ArmPitch(a.armHold, a.armLift, reach);
            famArmPitch = pitch;
            float flap = Mathf.Sin(a.time * 2.7f) * 3f * empty * Mathf.Clamp01(a.speed01 * 2);   // arms trail a little when rolling
            _armL.localRotation = Quaternion.Euler(pitch + flap, 0, 8 * hold - 20 * empty);   // at rest the arms swing wide beside the wheel
            _armR.localRotation = Quaternion.Euler(pitch - flap, 0, -8 * hold + 20 * empty);
            PoseElbows(a, 38);
        }

        public override void Animate(RobotAnim a)
        {
            float sc = Mathf.Max(1e-4f, transform.lossyScale.y);
            if (a.dt > 0)
            {
                float dt = Mathf.Min(a.dt, 0.05f);   // a long frame (or a QA pose) never upsets the spring
                // the wheel turns by the distance actually travelled (forward or back), so it never slides
                var p = transform.position;
                if (_hasLast) _spin += Vector3.Dot(p - _lastPos, transform.forward) / (WheelR * sc) * Mathf.Rad2Deg;
                _lastPos = p; _hasLast = true;
                // balance: upright when still, leaning forward with speed and a little more into acceleration
                // (back when braking); a damped spring, so it settles with one small corrective overshoot
                float accel = (a.speed - _lastV) / dt; _lastV = a.speed;
                float target = 15f * Mathf.Clamp01(a.speed01) + Mathf.Clamp(accel * 4f, -6f, 6f) - 2f * a.armHold * (1 - a.bend);
                const float k = 80f, damp = 2f * 0.6f * 8.94f;   // stiffness, damping (zeta 0.6)
                _leanVel += ((target - _leanDeg) * k - _leanVel * damp) * dt;
                _leanDeg += _leanVel * dt;
                // banking into turns like a cyclist: more the faster and tighter it turns
                float bankTarget = Mathf.Clamp(-a.turnRate * a.speed * 0.22f, -18f, 18f);
                _bank = Mathf.Lerp(_bank, bankTarget, dt * 5f);
                _gyroAng += dt * (600f + Mathf.Abs(_leanVel) * 25f);
            }
            _wheel.localRotation = Quaternion.Euler(_spin, 0, 0);
            _gyro.localRotation = Quaternion.Euler(_gyroAng, 0, 0);
            _fa = a;
            PoseBody(a, 1);
            FloorGuard(a);
            PoseTelescopes(0);
            float hold = Mathf.Max(a.armHold, a.armReach);
            FamilyAim(a, ArmPitch(a.armHold, a.armLift, a.armReach), 8 * hold);
            FamilyGrip(a, 8 * hold);
            FamilyClaws(a);
            if (a.gesture > 0) _lean.localRotation *= Quaternion.Euler(0, Mathf.SmoothStep(0, 360, a.gesture), Mathf.Sin(a.gesture * Mathf.PI * 4) * 6);   // nervous pirouette
            FamilyLookHead(a, Mathf.Clamp01(a.speed01 * 2), Attention);
        }
    }
}
