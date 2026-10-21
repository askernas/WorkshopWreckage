using UnityEngine;

namespace WorkshopWreckage
{
    /// <summary>Hoverbot after resources/hoverbot.png: red riveted flying saucer with a
    /// compact two-eyed head, glowing rim lights and thruster plate, two gripping arms
    /// at the rim and a comical two-blade propeller on a mast. It bobs, tilts into its
    /// flight direction and spins the rotor. Parts: head, left/right eye lens,
    /// left/right antenna, siren light, upper/lower saucer shell, rim light 1-4,
    /// thruster ring, thruster glow plate, propeller blade A/B, rotor hub, rotor mast,
    /// rotor motor, left/right arm, left/right claw, gyroscope, flight controller,
    /// battery pack, left/right ear cap.</summary>
    public class HoverbotModel : RobotModel
    {
        Transform _saucer, _rotor, _armL, _armR;
        float _rotorAngle, _tilt;
        public override float ModelHeight => 0.82f;
        static Material RedPaint => Paint(new Color(0.74f, 0.1f, 0.07f), 0.5f);

        protected override void CreatePivots()
        {
            _saucer = Pivot("Saucer", body, Vector3.zero);
            head = Pivot("Head", _saucer, new Vector3(0, 0.41f, 0));
            _rotor = Pivot("Rotor", _saucer, new Vector3(0, 0.76f, 0));
            _armL = Pivot("ArmL", _saucer, new Vector3(-0.37f, 0.3f, 0));
            _armR = Pivot("ArmR", _saucer, new Vector3(0.37f, 0.3f, 0));
            carry = Pivot("Carry", _saucer, new Vector3(0, 0.1f, 0.05f));   // held under the saucer
        }

        protected override void BuildGeometry()
        {
            var b = new Batch();
            b.Cylinder(new Vector3(0, 0.335f, 0), 0.34f, 0.06f, M("upper saucer shell", RedPaint), null, 28);
            b.Cylinder(new Vector3(0, 0.385f, 0), 0.29f, 0.045f, M("upper saucer shell", RedPaint), null, 28);
            b.Cylinder(new Vector3(0, 0.305f, 0), 0.348f, 0.016f, M("upper saucer shell", Dark), null, 28);
            b.Cylinder(new Vector3(0, 0.275f, 0), 0.31f, 0.05f, M("lower saucer shell", RedPaint), null, 28);
            b.Cylinder(new Vector3(0, 0.237f, 0), 0.24f, 0.035f, M("lower saucer shell", RedPaint), null, 24);
            b.Box(new Vector3(0, 0.33f, -0.335f), new Vector3(0.16f, 0.035f, 0.02f), M("upper saucer shell", Dark), false);
            for (int k = 0; k < 3; k++) b.Box(new Vector3(0, 0.322f + k * 0.009f, -0.344f), new Vector3(0.14f, 0.004f, 0.004f), M("upper saucer shell", Brass));
            for (int k = 0; k < 16; k++)
            {
                float a = k * Mathf.PI / 8;
                b.Box(new Vector3(Mathf.Sin(a) * 0.335f, 0.355f, Mathf.Cos(a) * 0.335f), new Vector3(0.012f, 0.008f, 0.012f), M("upper saucer shell", Dark));
            }
            b.Cylinder(new Vector3(0, 0.195f, 0), 0.19f, 0.05f, M("thruster ring", Dark), null, 20);
            for (int k = 0; k < 12; k++)
            {
                float a = k * Mathf.PI / 6;
                b.Box(new Vector3(Mathf.Sin(a) * 0.19f, 0.195f, Mathf.Cos(a) * 0.19f), new Vector3(0.012f, 0.05f, 0.012f), M("thruster ring", Brass));
            }
            b.Cylinder(new Vector3(0, 0.168f, 0), 0.14f, 0.012f, M("thruster glow plate", Glow), null, 20);
            b.Cylinder(new Vector3(0, 0.413f, 0), 0.18f, 0.014f, M("gyroscope", Brass), null, 24);
            b.Box(new Vector3(0, 0.42f, 0.2f), new Vector3(0.1f, 0.03f, 0.06f), M("flight controller", Dark));
            b.Box(new Vector3(0, 0.215f, 0.17f), new Vector3(0.12f, 0.03f, 0.05f), M("battery pack", Dark));
            // rotor mast and motor on top of the head
            b.Cylinder(new Vector3(0, 0.69f, 0), 0.022f, 0.12f, M("rotor mast", Dark));
            b.Cylinder(new Vector3(0, 0.655f, 0), 0.045f, 0.035f, M("rotor motor", Dark), null, 14);
            for (int k = 0; k < 3; k++) b.Cylinder(new Vector3(0, 0.642f + k * 0.012f, 0), 0.048f, 0.004f, M("rotor motor", Brass), null, 14);
            Done(b, _saucer);
            // rim lights and the siren glow
            var rims = new[] { "rim light 1", "rim light 2", "rim light 3", "rim light 4" };
            for (int k = 0; k < 4; k++)
            {
                float a = (k < 2 ? -0.55f : 0.55f) + (k % 2 == 0 ? 0 : Mathf.PI);
                Prim(PrimitiveType.Sphere, _saucer, new Vector3(Mathf.Sin(a) * 0.31f, 0.28f, -Mathf.Cos(a) * 0.31f), Vector3.one * 0.05f, Quaternion.identity, M(rims[k], Glow));
            }
            var siren = Prim(PrimitiveType.Sphere, _saucer, new Vector3(0, 0.3f, -0.345f), new Vector3(0.05f, 0.04f, 0.04f), Quaternion.identity, M("siren light", Red));
            if (Has("siren light")) beacons.Add(siren);
            {
                var r = new Batch();
                r.Cylinder(Vector3.zero, 0.032f, 0.035f, M("rotor hub", Red), null, 14);
                r.Box(new Vector3(0.17f, 0.005f, 0), new Vector3(0.28f, 0.012f, 0.07f), M("propeller blade a", RedPaint), false, Batch.R(Vector3.right, 0.18f));
                r.Box(new Vector3(-0.17f, 0.005f, 0), new Vector3(0.28f, 0.012f, 0.07f), M("propeller blade b", RedPaint), false, Batch.R(Vector3.right, -0.18f));
                foreach (var x in new[] { -0.03f, 0.03f }) r.Box(new Vector3(x, 0, 0), new Vector3(0.03f, 0.016f, 0.02f), M("rotor hub", Dark));
                Done(r, _rotor);
                Prim(PrimitiveType.Sphere, _rotor, new Vector3(0, 0.022f, 0), new Vector3(0.05f, 0.035f, 0.05f), Quaternion.identity, M("rotor hub", Red));
            }
            foreach (var (arm, s, part, claw) in new[] { (_armL, -1f, "left arm", "left claw"), (_armR, 1f, "right arm", "right claw") })
            {
                var a = new Batch();
                ArmKit(a, s, part, RedPaint, 0.7f, claw);
                Done(a, arm);
            }
            HeadKit(head, RedPaint, new Vector3(0.3f, 0.22f, 0.22f), 2, "head", "left eye lens", "left antenna", "right antenna", "left ear cap", "right ear cap", null, true, "right eye lens");
        }

        public override (Transform parent, Vector3 local) CarrySlot(int i, int n)
        {
            float a = i * 2.4f;
            return (carry, new Vector3(Mathf.Sin(a) * 0.08f * Mathf.Min(1, i), -(i / 4) * 0.05f, Mathf.Cos(a) * 0.08f * Mathf.Min(1, i)));
        }

        public override void Animate(RobotAnim a)
        {
            _rotorAngle += a.dt * (a.active ? 1500f : 0);
            _rotor.localRotation = Quaternion.Euler(0, _rotorAngle, 0);
            float bob = Mathf.Sin(a.time * 2.2f) * 0.012f + Mathf.Sin(a.time * 5.3f) * 0.004f;
            _saucer.localPosition = new Vector3(0, bob, 0);
            _tilt = Mathf.Lerp(_tilt, a.speed01 * 12f, a.dt * 3f);
            _saucer.localRotation = Quaternion.Euler(_tilt, 0, -a.turnRate * 0.05f + Mathf.Sin(a.time * 1.7f) * 2f);
            float pitch = -20 * a.armHold - 50 * a.armReach - 60 * a.armLift;
            float wiggle = Mathf.Sin(a.time * 3.3f) * 6 * (1 - a.armHold);
            _armL.localRotation = Quaternion.Euler(pitch + wiggle, 0, -25 + 20 * a.armHold);
            _armR.localRotation = Quaternion.Euler(pitch - wiggle, 0, 25 - 20 * a.armHold);
            LookHead(a, 70);
        }
    }
}
