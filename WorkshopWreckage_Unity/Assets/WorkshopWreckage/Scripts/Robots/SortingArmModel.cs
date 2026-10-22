using UnityEngine;

namespace WorkshopWreckage
{
    /// <summary>The Sorting Arm Mk I after resources/robot_arm.png: a yellow industrial arm
    /// with red joints that hangs from a docking coupler on a cabinet socket. Built at true
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
        static Material _yellow;
        static Material Yellow => _yellow ??= Paint(new Color(0.88f, 0.64f, 0.12f), 0.45f);

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

        protected override void BuildGeometry()
        {
            {
                var b = new Batch();   // docking coupler: plate, locking pins, power contacts
                b.RoundedBox(new Vector3(0, -0.03f, 0), new Vector3(0.36f, 0.06f, 0.3f), 0.012f, M("docking coupler", Yellow), 2);
                foreach (var x in new[] { -0.14f, 0.14f })
                    b.Cylinder(new Vector3(x, 0.012f, 0.09f), 0.016f, 0.03f, M("docking coupler", Dark));
                b.Box(new Vector3(0, -0.035f, -0.152f), new Vector3(0.12f, 0.025f, 0.006f), M("docking coupler", Dark));
                for (int k = 0; k < 3; k++) b.Box(new Vector3(-0.03f + k * 0.03f, -0.035f, -0.156f), new Vector3(0.012f, 0.012f, 0.004f), M("docking coupler", Brass));
                Done(b, body);
            }
            if (Has("docking coupler")) beacons.Add(Prim(PrimitiveType.Sphere, body, new Vector3(0.13f, -0.04f, -0.15f), Vector3.one * 0.025f, Quaternion.identity, BeaconOffMat));
            {
                var b = new Batch();   // shoulder turret + control unit
                b.Cylinder(new Vector3(0, -0.06f, 0), 0.11f, 0.12f, M("shoulder turret", Red), null, 24);
                b.Cylinder(new Vector3(0, -0.005f, 0), 0.125f, 0.012f, M("shoulder turret", Dark), null, 24);
                b.Cylinder(new Vector3(0, -0.12f, 0), 0.07f, 0.02f, M("shoulder turret", Dark), AxisX, 16);
                b.RoundedBox(new Vector3(0.16f, -0.05f, 0), new Vector3(0.1f, 0.09f, 0.15f), 0.01f, M("control unit", Yellow), 2);
                b.Box(new Vector3(0.212f, -0.05f, 0), new Vector3(0.006f, 0.05f, 0.1f), M("control unit", Dark));
                Done(b, _shoulder);
            }
            {
                var b = new Batch();   // upper arm: box section with red bosses, motor cartridge, cable
                b.RoundedBox(new Vector3(0, -L1 / 2, 0), new Vector3(0.08f, L1 * 0.86f, 0.1f), 0.012f, M("upper arm", Yellow), 2);
                b.Cylinder(Vector3.zero, 0.065f, 0.12f, M("upper arm", Red), AxisX, 20);
                b.Box(new Vector3(0, -L1 / 2, -0.051f), new Vector3(0.04f, L1 * 0.5f, 0.004f), M("upper arm", Dark));
                b.Cylinder(new Vector3(0.075f, -0.22f, 0), 0.04f, 0.24f, M("motor cartridge", Dark), null, 16);
                for (int k = 0; k < 4; k++) b.Cylinder(new Vector3(0.075f, -0.14f - k * 0.05f, 0), 0.046f, 0.012f, M("motor cartridge", Yellow), null, 16);
                b.Cylinder(new Vector3(-0.052f, -L1 / 2, 0.02f), 0.012f, L1 * 0.8f, M("cable harness", Rubber), null, 8);
                Done(b, _upper);
            }
            {
                var b = new Batch();   // elbow servo + forearm
                b.Cylinder(Vector3.zero, 0.07f, 0.14f, M("elbow servo", Red), AxisX, 20);
                b.Cylinder(new Vector3(0.075f, 0, 0), 0.04f, 0.02f, M("elbow servo", Dark), AxisX, 16);
                b.RoundedBox(new Vector3(0, -L2 / 2, 0), new Vector3(0.07f, L2 * 0.86f, 0.085f), 0.01f, M("forearm", Yellow), 2);
                b.Cylinder(new Vector3(-0.045f, -L2 / 2, 0.015f), 0.011f, L2 * 0.8f, M("cable harness", Rubber), null, 8);
                Done(b, _elbow);
            }
            {
                var b = new Batch();   // wrist rotator, palm, sensor head
                b.Cylinder(new Vector3(0, -0.035f, 0), 0.048f, 0.07f, M("wrist rotator", Red), null, 18);
                b.Box(new Vector3(0, -0.085f, 0), new Vector3(0.13f, 0.03f, 0.06f), M("wrist rotator", Dark));
                b.Box(new Vector3(0, -0.05f, -0.06f), new Vector3(0.06f, 0.04f, 0.04f), M("sensor head", Yellow));
                Done(b, _wrist);
            }
            if (Has("sensor head")) eyes.Add(Prim(PrimitiveType.Cylinder, _wrist, new Vector3(0, -0.05f, -0.082f), new Vector3(0.028f, 0.004f, 0.028f), AxisZ, EyeOffMat));
            foreach (var (pivot, part) in new[] { (_fingerL, "left finger"), (_fingerR, "right finger") })
            {
                var b = new Batch();
                b.Box(new Vector3(0, -0.055f, 0), new Vector3(0.02f, 0.11f, 0.045f), M(part, Yellow));
                b.Box(new Vector3(pivot == _fingerL ? 0.012f : -0.012f, -0.09f, 0), new Vector3(0.008f, 0.05f, 0.04f), M(part, Rubber));
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

        public void Grip(float open01)
        {
            float x = 0.012f + 0.045f * Mathf.Clamp01(open01);
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
