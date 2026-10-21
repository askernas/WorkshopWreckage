using UnityEngine;

namespace WorkshopWreckage
{
    /// <summary>Unibot after resources/unibot.png: olive-green single-wheeled balancing
    /// courier with one big lamp eye, red ear discs, antennas, a grilled torso, claw
    /// arms, brass gyro stabilizers and a fat black tyre with a red hub. It leans into
    /// acceleration and keeps wobbling to balance. Parts: head shell, optical eye,
    /// antenna pair, torso, left arm, right arm, gyro stabilizer, wheel hub, tire.</summary>
    public class UnibotModel : RobotModel
    {
        Transform _wheel, _lean, _leanGeo, _armL, _armR, _gyro;
        float _spin, _lean01, _lastSpeed;
        public override float ModelHeight => 0.98f;
        static Material Green => Paint(new Color(0.36f, 0.42f, 0.2f), 0.5f);
        const float WheelR = 0.17f;

        protected override void CreatePivots()
        {
            _wheel = Pivot("Wheel", body, new Vector3(0, WheelR, 0));
            _lean = Pivot("Lean", body, new Vector3(0, WheelR, 0));
            _leanGeo = Pivot("LeanGeo", _lean, new Vector3(0, -WheelR, 0));
            _gyro = Pivot("Gyro", _leanGeo, new Vector3(0, 0.36f, 0));
            _armL = Pivot("ArmL", _leanGeo, new Vector3(-0.15f, 0.55f, 0));
            _armR = Pivot("ArmR", _leanGeo, new Vector3(0.15f, 0.55f, 0));
            head = Pivot("Head", _leanGeo, new Vector3(0, 0.6f, 0));
            carry = Pivot("Carry", _leanGeo, new Vector3(0, 0.44f, 0.3f));
        }

        protected override void BuildGeometry()
        {
            {
                var b = new Batch();   // tyre + hub spin together
                for (int k = 0; k < 10; k++)
                {
                    float a = k * Mathf.PI / 5;
                    b.Box(new Vector3(0, Mathf.Sin(a) * WheelR, Mathf.Cos(a) * WheelR), new Vector3(0.11f, 0.03f, 0.105f), M("tire", Rubber), false, Batch.R(Vector3.right, -a));
                }
                b.Cylinder(Vector3.zero, WheelR * 0.97f, 0.1f, M("tire", Rubber), AxisX, 20);
                foreach (var x in new[] { -1f, 1f })
                {
                    b.Cylinder(new Vector3(x * 0.052f, 0, 0), WheelR * 0.62f, 0.012f, M("wheel hub", Red), AxisX, 20);
                    b.Cylinder(new Vector3(x * 0.06f, 0, 0), 0.03f, 0.012f, M("wheel hub", Brass), AxisX);
                    for (int k = 0; k < 6; k++)
                    {
                        float a = k * Mathf.PI / 3;
                        b.Cylinder(new Vector3(x * 0.06f, Mathf.Sin(a) * 0.075f, Mathf.Cos(a) * 0.075f), 0.008f, 0.006f, M("wheel hub", Dark), AxisX);
                    }
                }
                Done(b, _wheel);
            }
            {
                var b = new Batch();
                // fork from the axle up into the torso, gyro housings, torso
                foreach (var x in new[] { -1f, 1f })
                {
                    b.Box(new Vector3(x * 0.075f, 0.27f, 0), new Vector3(0.025f, 0.2f, 0.04f), M("gyro stabilizer", Green));
                    b.Cylinder(new Vector3(x * 0.075f, WheelR, 0), 0.025f, 0.03f, M("wheel hub", Dark), AxisX);
                }
                b.Box(new Vector3(0, 0.36f, 0), new Vector3(0.17f, 0.04f, 0.12f), M("gyro stabilizer", Dark));
                b.Box(new Vector3(0, 0.47f, 0), new Vector3(0.24f, 0.2f, 0.18f), M("torso", Green));
                b.Box(new Vector3(0, 0.5f, -0.092f), new Vector3(0.12f, 0.06f, 0.008f), M("torso", Dark));
                for (int k = 0; k < 4; k++) b.Box(new Vector3(0, 0.477f + k * 0.015f, -0.097f), new Vector3(0.11f, 0.005f, 0.005f), M("torso", Brass));
                b.Box(new Vector3(0, 0.415f, -0.092f), new Vector3(0.08f, 0.015f, 0.008f), M("torso", Dark));
                b.Box(new Vector3(0, 0.49f, 0.092f), new Vector3(0.13f, 0.07f, 0.008f), M("torso", Dark));
                b.Cylinder(new Vector3(0, 0.585f, 0), 0.04f, 0.035f, M("torso", Dark));
                Done(b, _leanGeo);
            }
            {
                var b = new Batch();   // brass gyro discs that spin
                foreach (var x in new[] { -1f, 1f }) b.Cylinder(new Vector3(x * 0.1f, 0, 0), 0.04f, 0.018f, M("gyro stabilizer", Brass), AxisX, 12);
                Done(b, _gyro);
            }
            foreach (var (arm, s, part) in new[] { (_armL, -1f, "left arm"), (_armR, 1f, "right arm") })
            {
                var b = new Batch();
                ArmKit(b, s, part, Green, 0.85f);
                Done(b, arm);
            }
            HeadKit(head, Green, new Vector3(0.3f, 0.24f, 0.24f), 1, "head shell", "optical eye", "antenna pair", "antenna pair", "head shell", "head shell", null);
        }

        public override void Animate(RobotAnim a)
        {
            float s = transform.lossyScale.x;
            // the wheel rolls with the distance travelled (true radius at toy scale)
            float v = a.speed01;
            _spin += a.speed * a.dt / Mathf.Max(1e-4f, WheelR * s) * Mathf.Rad2Deg;
            _wheel.localRotation = Quaternion.Euler(_spin, 0, 0);
            // leans into acceleration, wobbles to keep its balance
            float accel = (v - _lastSpeed) / Mathf.Max(a.dt, 1e-4f);
            _lastSpeed = v;
            _lean01 = Mathf.Lerp(_lean01, Mathf.Clamp(v * 8 + accel * 2.5f, -14, 22), a.dt * 4);
            float wobble = Mathf.Sin(a.time * 3.1f) * 2.2f + Mathf.Sin(a.time * 7.3f) * 0.8f;
            _lean.localRotation = Quaternion.Euler(_lean01 + wobble, 0, Mathf.Sin(a.time * 2.3f) * 1.5f + a.turnRate * 0.03f);
            _gyro.localRotation = Quaternion.Euler(a.time * 720f, 0, 0);
            float empty = 1 - Mathf.Max(a.armHold, a.armReach);
            float pitch = -70 * a.armHold - 40 * a.armLift - 45 * a.armReach;
            float flap = Mathf.Sin(a.time * 2.7f) * 8 * empty;   // little balancing arm motions
            _armL.localRotation = Quaternion.Euler(pitch + flap, 0, 8 * a.armHold - 10 * empty);
            _armR.localRotation = Quaternion.Euler(pitch - flap, 0, -8 * a.armHold + 10 * empty);
            LookHead(a);
        }
    }
}
