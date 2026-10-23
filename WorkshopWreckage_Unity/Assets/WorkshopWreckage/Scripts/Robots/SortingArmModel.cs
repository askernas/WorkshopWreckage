using UnityEngine;

namespace WorkshopWreckage
{
    /// <summary>The Sorting Arm Mk I after resources/robot_arm.png, in the robot family's look:
    /// a mustard toy-enamel industrial arm (bolted panel plates, seams, rust, looping hoses, the
    /// stencilled "A-01"), big red joint caps with bolt rings and brass hubs, a red drum turret
    /// with amber indicators, and a big hooked red / gunmetal pincer; it hangs from a bolted
    /// docking coupler on a cabinet socket. Built at true
    /// size (≈2 m reach from the socket) and shown at 0.42 scale on the bench and while
    /// carried (folded, standing on its coupler). Pose is driven by SortingArm through
    /// analytic two-bone IK in the shoulder plane; the elbow always bends outward (away
    /// from the cabinet). Parts: docking coupler, shoulder turret, control unit, upper arm,
    /// motor cartridge, elbow servo, forearm, wrist rotator, sensor head, left finger,
    /// right finger, cable harness.</summary>
    public class SortingArmModel : RobotModel
    {
        public const float L1 = 0.85f, L2 = 0.8f, ShoulderDrop = 0.18f, GripLen = 0.17f;
        Transform _shoulder, _upper, _elbow, _wrist, _fingerL, _fingerR;
        public override float ModelHeight => 2.0f;

        protected override void CreatePivots()
        {
            _shoulder = Pivot("Shoulder", body, new Vector3(0, -0.06f, 0));
            _upper = Pivot("Upper", _shoulder, new Vector3(0, -(ShoulderDrop - 0.06f), 0));
            _elbow = Pivot("Elbow", _upper, new Vector3(0, -L1, 0));
            _wrist = Pivot("Wrist", _elbow, new Vector3(0, -L2, 0));
            _fingerL = Pivot("FingerL", _wrist, new Vector3(-0.04f, -0.1f, 0));
            _fingerR = Pivot("FingerR", _wrist, new Vector3(0.04f, -0.1f, 0));
            carry = Pivot("Grip", _wrist, new Vector3(0, -GripLen, 0));
            head = _wrist;
            Mount(body, "shoulder turret", Vector3.down, 0.07f);
            Mount(_shoulder, "control unit", Vector3.right, 0.04f);
            Mount(_upper, "upper arm", Vector3.down, 0.06f);
            Mount(_upper, "motor cartridge", Vector3.right, 0.04f);
            Mount(_elbow, "forearm", Vector3.down, 0.05f);
            Mount(_wrist, "sensor head", Vector3.forward, 0.035f);
            Mount(_fingerL, "left finger", Vector3.down, 0.03f);
            Mount(_fingerR, "right finger", Vector3.down, 0.03f);
            Fold(1);
        }

        const string Cpl = "docking coupler", Tur = "shoulder turret", Ctl = "control unit", Up = "upper arm", Mot = "motor cartridge",
            Elb = "elbow servo", Fore = "forearm", Wr = "wrist rotator", Sens = "sensor head", Cab = "cable harness";

        /// <summary>A big joint: gunmetal axle across X, red disc caps both sides with a bolt ring,
        /// gunmetal collar and brass hub (shoulder and elbow).</summary>
        void Joint(Batch b, string part, float r, float halfW)
        {
            b.Lathe(new Vector3(-halfW, 0, 0), Disc(r * 0.72f, halfW * 2, 0.006f), M(part, Gunmetal), RightRot, 24);
            foreach (var s in new[] { -1f, 1f })
            {
                b.Lathe(new Vector3(s * (halfW - 0.035f), 0, 0), Disc(r, 0.045f, 0.012f), M(part, ToyRed), SideRot(s), 32);
                b.Lathe(new Vector3(s * (halfW + 0.01f), 0, 0), Disc(r * 0.5f, 0.012f, 0.003f), M(part, Gunmetal), SideRot(s), 20);
                b.Lathe(new Vector3(s * (halfW + 0.022f), 0, 0), Dome(r * 0.3f, 0.02f), M(part, Brass), SideRot(s), 16);
                for (int k = 0; k < 8; k++)
                {
                    float a = k * Mathf.PI / 4 + 0.2f;
                    Bolt(b, new Vector3(s * (halfW + 0.01f), Mathf.Sin(a) * r * 0.74f, Mathf.Cos(a) * r * 0.74f), new Vector3(s, 0, 0), 0.01f, M(part, Gunmetal));
                }
            }
        }

        /// <summary>A banded, bolted, seamed box-section limb from y = top down to y = bottom.</summary>
        void Limb(Batch b, string part, float top, float bottom, float w, float d)
        {
            var c = new Vector3(0, (top + bottom) / 2, 0);
            var size = new Vector3(w, top - bottom, d);
            b.RoundedBox(c, size, 0.024f, M(part, ToyYellow), 3);
            foreach (var y in new[] { top - 0.025f, bottom + 0.025f })
                b.RoundedBox(new Vector3(0, y, 0), new Vector3(w + 0.006f, 0.022f, d + 0.006f), 0.007f, M(part, ToyYellowDeep), 2);
            foreach (var n in new[] { Vector3.left, Vector3.right })
            {
                PanelFrame(b, part, c, size, n, 0.032f, 0.011f, 0.12f, 0.006f, 0.07f);   // short drips: the limbs tilt in every pose
                SeamLine(b, part, c + n * (w / 2) + new Vector3(0, 0, -d / 2 + 0.032f), c + n * (w / 2) + new Vector3(0, 0, d / 2 - 0.032f), n, 0.006f);
            }
            foreach (var zs in new[] { -1f, 1f })   // front and back: a seam down the middle
                SeamLine(b, part, new Vector3(0, top - 0.05f, zs * d / 2), new Vector3(0, bottom + 0.05f, zs * d / 2), new Vector3(0, 0, zs), 0.006f);
        }

        protected override void BuildGeometry()
        {
            {
                var b = new Batch();   // docking coupler: gunmetal backing plate, bolted mustard plinth, amber lights, red panel
                b.RoundedBox(new Vector3(0, -0.012f, 0), new Vector3(0.42f, 0.024f, 0.36f), 0.006f, M(Cpl, Gunmetal), 2);
                var pc = new Vector3(0, -0.058f, 0);
                var ps = new Vector3(0.36f, 0.07f, 0.3f);
                b.RoundedBox(pc, ps, 0.018f, M(Cpl, ToyYellow), 3);
                foreach (var x in new[] { -0.15f, 0.15f })
                    foreach (var z in new[] { -0.12f, 0.12f })
                        Bolt(b, new Vector3(x, -0.093f, z), Vector3.down, 0.012f, M(Cpl, Gunmetal));
                foreach (var x in new[] { -0.14f, 0.14f })
                    b.Cylinder(new Vector3(x, 0.012f, 0.09f), 0.016f, 0.03f, M(Cpl, Dark));   // locking pins
                b.RoundedBox(new Vector3(0, -0.058f, -0.151f), new Vector3(0.14f, 0.03f, 0.006f), 0.004f, M(Cpl, Gunmetal), 1);
                b.RoundedBox(new Vector3(0, -0.058f, -0.155f), new Vector3(0.11f, 0.014f, 0.004f), 0.003f, M(Cpl, Glow), 1);   // amber light slot
                b.RoundedBox(new Vector3(0.181f, -0.058f, 0), new Vector3(0.008f, 0.042f, 0.16f), 0.004f, M(Cpl, ToyRed), 1);   // red side panel
                for (int k = 0; k < 3; k++) b.Box(new Vector3(-0.03f + k * 0.03f, -0.058f, 0.153f), new Vector3(0.012f, 0.012f, 0.004f), M(Cpl, Brass));   // power contacts
                SeamLine(b, Cpl, new Vector3(-0.181f, -0.058f, -0.12f), new Vector3(-0.181f, -0.058f, 0.12f), Vector3.left, 0.006f);
                Done(b, body);
            }
            if (Has(Cpl)) beacons.Add(Prim(PrimitiveType.Sphere, body, new Vector3(0.13f, -0.06f, -0.152f), Vector3.one * 0.026f, Quaternion.identity, BeaconOffMat));
            {
                var b = new Batch();   // shoulder turret: flange, red drum with a bolt ring and amber light; control unit with a hose
                b.Lathe(Vector3.zero, Disc(0.135f, 0.02f, 0.004f), M(Tur, Gunmetal), DownRot, 32);
                b.Lathe(new Vector3(0, -0.02f, 0), Disc(0.1f, 0.016f, 0.003f), M(Tur, Gunmetal), DownRot, 28);
                b.Lathe(new Vector3(0, -0.034f, 0), Disc(0.13f, 0.086f, 0.016f), M(Tur, ToyRed), DownRot, 36);
                b.Lathe(new Vector3(0, -0.118f, 0), Disc(0.133f, 0.01f, 0.003f), M(Tur, Brass), DownRot, 36);
                for (int k = 0; k < 12; k++)
                {
                    float a = k * Mathf.PI / 6 + 0.13f;
                    var dir = new Vector3(Mathf.Sin(a), 0, Mathf.Cos(a));
                    Bolt(b, dir * 0.13f + new Vector3(0, -0.048f, 0), dir, 0.01f, M(Tur, Gunmetal));
                    if (Hash(dir, 6) < 0.4f) RustStreak(b, Tur, dir * 0.131f + new Vector3(0, -0.052f, 0), dir, 0.04f + 0.02f * Hash(dir, 7), 0.02f);
                }
                b.RoundedBox(new Vector3(0, -0.085f, -0.13f), new Vector3(0.05f, 0.02f, 0.008f), 0.004f, M(Tur, Glow), 1);
                var cc = new Vector3(0.19f, -0.065f, 0);
                var cs = new Vector3(0.1f, 0.11f, 0.17f);
                b.RoundedBox(cc, cs, 0.016f, M(Ctl, ToyYellow), 3);
                PanelFrame(b, Ctl, cc, cs, Vector3.right, 0.02f, 0.009f, 0.1f, 0.005f);
                foreach (var y in new[] { -0.045f, -0.085f }) b.RoundedBox(new Vector3(0.2f, y, -0.086f), new Vector3(0.03f, 0.012f, 0.005f), 0.003f, M(Ctl, Glow), 1);
                Pipe(b, Ctl, new[] { new Vector3(0.15f, -0.01f, 0.07f), new Vector3(0.13f, 0.02f, 0.11f), new Vector3(0.09f, -0.01f, 0.13f), new Vector3(0.06f, -0.06f, 0.11f) }, 0.014f, Rubber, Brass);
                Done(b, _shoulder);
            }
            {
                var b = new Batch();   // upper arm: shoulder joint, the limb with "A-01", motor cartridge, looping hoses
                Joint(b, Up, 0.11f, 0.105f);
                Limb(b, Up, -0.1f, -L1 + 0.09f, 0.15f, 0.17f);
                Stencil(b, Up, new Vector3(0.0765f, -0.3f, 0.045f), Vector3.down, new Vector3(0, 0, -1), Vector3.right, 0.09f, "A-01");   // reads from the shoulder towards the elbow
                b.Lathe(new Vector3(-0.1f, -0.13f, 0.01f), Disc(0.042f, 0.2f, 0.006f), M(Mot, Gunmetal), DownRot, 20);
                foreach (var y in new[] { -0.16f, -0.21f, -0.26f }) b.Lathe(new Vector3(-0.1f, y, 0.01f), Disc(0.046f, 0.012f, 0.003f), M(Mot, Brass), DownRot, 20);
                b.Lathe(new Vector3(-0.1f, -0.33f, 0.01f), Dome(0.042f, 0.022f), M(Mot, ToyRed), DownRot, 20);
                Pipe(b, Cab, new[] { new Vector3(-0.03f, -0.07f, 0.075f), new Vector3(-0.05f, -0.17f, 0.125f), new Vector3(-0.035f, -0.42f, 0.1f), new Vector3(-0.03f, -0.68f, 0.082f), new Vector3(-0.02f, -0.77f, 0.06f) }, 0.014f, Rubber, Brass);
                Pipe(b, Cab, new[] { new Vector3(0.035f, -0.07f, 0.075f), new Vector3(0.07f, -0.16f, 0.12f), new Vector3(0.05f, -0.3f, 0.085f) }, 0.011f, Rubber, Brass);
                Done(b, _upper);
            }
            {
                var b = new Batch();   // elbow joint and the forearm
                Joint(b, Elb, 0.1f, 0.095f);
                Limb(b, Fore, -0.095f, -L2 + 0.045f, 0.13f, 0.15f);
                b.Lathe(new Vector3(0, -L2 + 0.05f, 0), Disc(0.07f, 0.03f, 0.006f), M(Fore, Gunmetal), DownRot, 24);   // end collar into the wrist
                Pipe(b, Cab, new[] { new Vector3(0.02f, -0.07f, 0.07f), new Vector3(0.03f, -0.35f, 0.08f), new Vector3(0.02f, -0.66f, 0.068f), new Vector3(0.01f, -0.72f, 0.05f) }, 0.012f, Rubber, Brass);
                Done(b, _elbow);
            }
            {
                var b = new Batch();   // wrist: collar, red rotator drum, gunmetal palm with bolts, sensor housing
                b.Lathe(new Vector3(0, 0.03f, 0), Disc(0.06f, 0.03f, 0.006f), M(Wr, Gunmetal), DownRot, 24);
                b.Lathe(new Vector3(0, 0f, 0), Disc(0.062f, 0.05f, 0.012f), M(Wr, ToyRed), DownRot, 28);
                b.Lathe(new Vector3(0, -0.05f, 0), Disc(0.064f, 0.008f, 0.002f), M(Wr, Brass), DownRot, 28);
                b.Lathe(new Vector3(0, -0.058f, 0), Disc(0.05f, 0.012f, 0.003f), M(Wr, Gunmetal), DownRot, 24);
                b.RoundedBox(new Vector3(0, -0.085f, 0), new Vector3(0.16f, 0.036f, 0.085f), 0.01f, M(Wr, Gunmetal), 2);
                foreach (var x in new[] { -0.06f, 0.06f }) Bolt(b, new Vector3(x, -0.085f, -0.043f), new Vector3(0, 0, -1), 0.008f, M(Wr, Gunmetal));
                b.RoundedBox(new Vector3(0, -0.04f, -0.07f), new Vector3(0.075f, 0.05f, 0.04f), 0.01f, M(Sens, ToyYellow), 2);
                Done(b, _wrist);
            }
            if (Has(Sens)) eyes.Add(Separate(_wrist, "G_sensor", lb => lb.RoundedBox(new Vector3(0, -0.04f, -0.091f), new Vector3(0.05f, 0.016f, 0.006f), 0.004f, EyeOffMat, 1)));
            foreach (var (pivot, part, fs) in new[] { (_fingerL, "left finger", -1f), (_fingerR, "right finger", 1f) })
            {
                var b = new Batch();   // a big hooked finger: red base opening out, brass knuckles, gunmetal tip curling in, teeth
                const float F = 1.6f;
                b.RoundedBox(new Vector3(fs * 0.012f, -0.05f, 0) * F, new Vector3(0.042f, 0.1f, 0.066f) * F, 0.013f * F, M(part, ToyRed), 2, Batch.R(Vector3.forward, fs * 0.22f));
                b.Rivet(new Vector3(fs * 0.012f, -0.012f, -0.034f) * F, new Vector3(0, 0, -1), 0.011f * F, M(part, Brass));
                b.Rivet(new Vector3(fs * 0.024f, -0.1f, -0.034f) * F, new Vector3(0, 0, -1), 0.01f * F, M(part, Brass));
                b.RoundedBox(new Vector3(fs * 0.012f, -0.14f, 0) * F, new Vector3(0.036f, 0.085f, 0.06f) * F, 0.011f * F, M(part, Gunmetal), 2, Batch.R(Vector3.forward, -fs * 0.5f));
                for (int t = 0; t < 3; t++)
                    b.Box(new Vector3(-fs * (0.006f + t * 0.004f), -0.07f - t * 0.03f, 0) * F, new Vector3(0.014f, 0.01f, 0.05f) * F, M(part, Dark), false, Batch.R(Vector3.forward, fs * 0.2f));
                RustStreak(b, part, new Vector3(fs * 0.034f, -0.03f, 0) * F, new Vector3(fs, 0, 0), 0.08f, 0.024f);
                Done(b, pivot);
            }
        }

        // ---------------------------------------------------------------- posing

        /// <summary>Standing on its coupler (bench, carried) instead of hanging from a socket.</summary>
        public void SetStanding(bool standing) => body.localRotation = standing ? Quaternion.Euler(180, 0, 0) : Quaternion.identity;

        /// <summary>Folded transport / rest pose (1 = fully folded).</summary>
        public void Fold(float f)
        {
            _shoulder.localRotation = Quaternion.identity;
            _upper.localRotation = Quaternion.AngleAxis(Mathf.Lerp(0, 18, f), Vector3.right);
            _elbow.localRotation = Quaternion.AngleAxis(Mathf.Lerp(0, -165, f), Vector3.right);
            _wrist.localRotation = Quaternion.AngleAxis(Mathf.Lerp(0, -20, f), Vector3.right);
            Grip(0.5f);
        }

        /// <summary>Mounted rest: jack-knifed upward (upper arm up, forearm folded back down
        /// beside it), so the gripper waits just above the cabinet top, clear of every drawer.</summary>
        public void Park(float yawDeg)
        {
            _shoulder.localRotation = Quaternion.Euler(0, yawDeg, 0);
            _upper.localRotation = Quaternion.AngleAxis(176, Vector3.right);
            _elbow.localRotation = Quaternion.AngleAxis(-168, Vector3.right);
            _wrist.localRotation = Quaternion.AngleAxis(-8, Vector3.right);
            Grip(0.6f);
        }

        /// <summary>Sets the joints directly (degrees): turret yaw, upper arm, elbow and wrist pitch.
        /// Model QA / posters; play poses come from Reach, Park and Fold.</summary>
        public void Pose(float yaw, float upper, float elbow, float wrist)
        {
            _shoulder.localRotation = Quaternion.Euler(0, yaw, 0);
            _upper.localRotation = Quaternion.AngleAxis(upper, Vector3.right);
            _elbow.localRotation = Quaternion.AngleAxis(elbow, Vector3.right);
            _wrist.localRotation = Quaternion.AngleAxis(wrist, Vector3.right);
        }

        public void Grip(float open01)
        {
            float x = 0.012f + 0.07f * Mathf.Clamp01(open01);
            _fingerL.localPosition = new Vector3(-x, -0.1f, 0);
            _fingerR.localPosition = new Vector3(x, -0.1f, 0);
        }

        /// <summary>Points the wrist at `wrist` (world) with the gripper along `gripDir`
        /// (world). Unreachable targets are approached as far as the arm stretches.</summary>
        public void Reach(Vector3 wrist, Vector3 gripDir)
        {
            var t = body.InverseTransformPoint(wrist);
            var hd = new Vector3(t.x, 0, t.z);
            // the arm reaches along the shoulder's -Z; yaw towards the target
            _shoulder.localRotation = hd.sqrMagnitude > 1e-6f ? Quaternion.LookRotation(-hd.normalized, Vector3.up) : Quaternion.identity;
            var tp = _shoulder.InverseTransformPoint(wrist) - _upper.localPosition;
            float h = -tp.z, v = tp.y;
            float d = Mathf.Clamp(Mathf.Sqrt(h * h + v * v), Mathf.Abs(L1 - L2) + 0.02f, L1 + L2 - 0.002f);
            float phi = Mathf.Atan2(h, -v);
            float alpha = Mathf.Acos(Mathf.Clamp((L1 * L1 + d * d - L2 * L2) / (2 * L1 * d), -1, 1));
            float th1 = phi + alpha;   // elbow outward
            var e = new Vector2(L1 * Mathf.Sin(th1), -L1 * Mathf.Cos(th1));   // (forward, up) of the elbow
            var dirT = new Vector2(Mathf.Sin(phi), -Mathf.Cos(phi)) * d;
            var fw = dirT - e;
            float th2 = Mathf.Atan2(fw.x, -fw.y);
            var g = _shoulder.InverseTransformDirection(gripDir);
            float thw = Mathf.Atan2(-g.z, -g.y);
            _upper.localRotation = Quaternion.AngleAxis(th1 * Mathf.Rad2Deg, Vector3.right);
            _elbow.localRotation = Quaternion.AngleAxis((th2 - th1) * Mathf.Rad2Deg, Vector3.right);
            _wrist.localRotation = Quaternion.AngleAxis((thw - th2) * Mathf.Rad2Deg, Vector3.right);
        }

        /// <summary>Where the gripper holds a part (world).</summary>
        public Vector3 GripPoint => carry.position;

        public override void Animate(RobotAnim a)
        {
            // only the activation "hello" and the bench use this: a little shoulder wiggle
            if (!a.active) return;
            _shoulder.localRotation = Quaternion.Euler(0, Mathf.Sin(a.time * 4) * 25 * a.gait, 0);
        }
    }
}
