using UnityEngine;

namespace WorkshopWreckage
{
    /// <summary>Hoverbot after resources/hoverbot.png: a red riveted flying saucer.
    /// Smooth lathed saucer with a dark rim band carrying four glowing rim lights, a
    /// slotted front vent and a red siren; a dark ribbed thruster ring with a glowing
    /// plate underneath; a brass gyroscope ring on top; the family head in red with two
    /// bezel eyes, red ear caps, antennas and a mouth grille; a stacked dark gear mast
    /// with the rotor motor; a comical two-blade paddle propeller with riveted rounded
    /// tips and a red dome hub; two gripping arms hanging from the rim.
    /// It bobs, tilts into its flight direction and spins the rotor.
    /// Parts: head, left/right eye lens, left/right antenna, siren light, upper/lower
    /// saucer shell, rim light 1-4, thruster ring, thruster glow plate, propeller blade
    /// A/B, rotor hub, rotor mast, rotor motor, left/right arm, left/right claw,
    /// gyroscope, flight controller, battery pack, left/right ear cap.</summary>
    public class HoverbotModel : RobotModel
    {
        Transform _saucer, _rotor, _armL, _armR;
        float _rotorAngle, _tilt;
        public override float ModelHeight => 0.9f;
        const float HeadY = 0.4f, RotorY = 0.86f, RimR = 0.44f;

        protected override void CreatePivots()
        {
            _saucer = Pivot("Saucer", body, Vector3.zero);
            head = Pivot("Head", _saucer, new Vector3(0, HeadY, 0));
            _rotor = Pivot("Rotor", _saucer, new Vector3(0, RotorY, 0));
            _armL = Pivot("ArmL", _saucer, new Vector3(-RimR - 0.02f, 0.27f, 0));
            _armR = Pivot("ArmR", _saucer, new Vector3(RimR + 0.02f, 0.27f, 0));
            carry = Pivot("Carry", _saucer, new Vector3(0, 0.05f, 0.05f));   // held under the saucer
        }

        protected override void BuildGeometry()
        {
            var b = new Batch();
            // saucer shells (smooth lathed profiles)
            b.Lathe(Vector3.zero, new[] { new Vector2(0.19f, 0.17f), new Vector2(0.3f, 0.2f), new Vector2(0.39f, 0.24f), new Vector2(0.43f, 0.27f) }, M("lower saucer shell", ToyRed), null, 40);
            b.Lathe(Vector3.zero, new[] { new Vector2(RimR - 0.008f, 0.268f), new Vector2(RimR, 0.275f), new Vector2(RimR, 0.305f), new Vector2(RimR - 0.008f, 0.312f) }, M("upper saucer shell", Gunmetal), null, 40);
            b.Lathe(Vector3.zero, new[] { new Vector2(0.435f, 0.31f), new Vector2(0.42f, 0.335f), new Vector2(0.37f, 0.365f), new Vector2(0.3f, 0.385f), new Vector2(0.22f, 0.397f), new Vector2(0.1f, 0.402f), new Vector2(0, 0.403f) }, M("upper saucer shell", ToyRed), null, 40);
            for (int k = 0; k < 20; k++)   // rivet ring
            {
                float a = k * Mathf.PI / 10;
                b.Rivet(new Vector3(Mathf.Sin(a) * 0.4f, 0.348f, Mathf.Cos(a) * 0.4f), new Vector3(Mathf.Sin(a), 1.6f, Mathf.Cos(a)), 0.008f, M("upper saucer shell", Gunmetal));
            }
            // front vent and siren on the rim
            Grille(b, "upper saucer shell", new Vector3(0, 0.29f, -RimR - 0.004f), 0.16f, 0.034f, 3, false, -1);
            foreach (var s in new[] { -1f, 1f })
                b.RoundedBox(new Vector3(s * 0.3f, 0.29f, -0.33f), new Vector3(0.07f, 0.026f, 0.012f), 0.006f, M("upper saucer shell", Gunmetal), 1, Batch.R(Vector3.up, s * 0.75f));
            // thruster ring with ribs and the glow plate
            b.Lathe(Vector3.zero, new[] { new Vector2(0.13f, 0.105f), new Vector2(0.17f, 0.105f), new Vector2(0.2f, 0.12f), new Vector2(0.2f, 0.17f) }, M("thruster ring", Gunmetal), null, 32);
            for (int k = 0; k < 16; k++)
            {
                float a = k * Mathf.PI / 8;
                b.RoundedBox(new Vector3(Mathf.Sin(a) * 0.203f, 0.14f, Mathf.Cos(a) * 0.203f), new Vector3(0.012f, 0.05f, 0.012f), 0.003f, M("thruster ring", Brass), 1);
            }
            // top: gyroscope ring, flight controller, battery underneath
            b.Lathe(new Vector3(0, 0.398f, 0), Ring(0.24f, 0.21f, 0.014f), M("gyroscope", Brass), null, 32);
            b.RoundedBox(new Vector3(0, 0.41f, 0.25f), new Vector3(0.12f, 0.03f, 0.07f), 0.01f, M("flight controller", Gunmetal), 2);
            b.RoundedBox(new Vector3(0, 0.155f, 0.24f), new Vector3(0.14f, 0.03f, 0.06f), 0.01f, M("battery pack", Gunmetal), 2);
            // rotor mast: stacked dark gears and the motor, on top of the head
            b.Lathe(new Vector3(0, 0.675f, 0), Disc(0.024f, 0.18f, 0.004f), M("rotor mast", Gunmetal), null, 12);
            for (int k = 0; k < 3; k++)
            {
                float y = 0.712f + k * 0.03f;
                b.Lathe(new Vector3(0, y, 0), Disc(0.055f - k * 0.008f, 0.016f, 0.004f), M("rotor motor", k == 1 ? Brass : Gunmetal), null, 18);
                for (int t = 0; t < 10; t++)
                {
                    float a = t * Mathf.PI / 5;
                    b.Box(new Vector3(Mathf.Sin(a) * (0.055f - k * 0.008f), y + 0.008f, Mathf.Cos(a) * (0.055f - k * 0.008f)), new Vector3(0.01f, 0.014f, 0.01f), M("rotor motor", Gunmetal), false, Batch.R(Vector3.up, a));
                }
            }
            Done(b, _saucer);
            // rim lights and the siren glow
            var rims = new[] { "rim light 1", "rim light 2", "rim light 3", "rim light 4" };
            for (int k = 0; k < 4; k++)
            {
                float a = (k < 2 ? -0.55f : 0.55f) + (k % 2 == 0 ? 0 : Mathf.PI);
                var dir = new Vector3(Mathf.Sin(a), 0, -Mathf.Cos(a));
                var lb = new Batch();
                lb.Lathe(dir * (RimR - 0.002f) + new Vector3(0, 0.29f, 0), Ring(0.032f, 0.022f, 0.012f), M(rims[k], Gunmetal), Quaternion.FromToRotation(Vector3.up, Batch.G(dir)), 18);
                lb.Lathe(dir * (RimR + 0.004f) + new Vector3(0, 0.29f, 0), Dome(0.024f, 0.012f), M(rims[k], Glow), Quaternion.FromToRotation(Vector3.up, Batch.G(dir)), 16);
                Done(lb, _saucer);
            }
            var glow = new Batch();
            glow.Lathe(new Vector3(0, 0.106f, 0), Dome(0.13f, 0.03f, 6), M("thruster glow plate", Glow), DownRot, 28);
            Done(glow, _saucer);
            if (Has("siren light"))
                beacons.Add(Separate(_saucer, "G_siren", lb => lb.Lathe(new Vector3(0, 0.345f, -0.4f), Dome(0.03f, 0.028f, 6), BeaconOffMat, Quaternion.FromToRotation(Vector3.up, new Vector3(0, 0.6f, 0.8f)), 16)));
            {
                var r = new Batch();   // propeller: red dome hub, two riveted paddle blades
                r.Lathe(new Vector3(0, -0.01f, 0), Disc(0.04f, 0.02f, 0.005f), M("rotor hub", Gunmetal), null, 18);
                r.Lathe(new Vector3(0, 0.01f, 0), Dome(0.038f, 0.032f), M("rotor hub", ToyRed), null, 18);
                foreach (var (s, part) in new[] { (1f, "propeller blade a"), (-1f, "propeller blade b") })
                {
                    var twist = Batch.R(Vector3.right, s * 0.16f);
                    r.RoundedBox(new Vector3(s * 0.065f, 0.005f, 0), new Vector3(0.07f, 0.02f, 0.028f), 0.006f, M(part, Gunmetal), 2, twist);   // shank
                    r.RoundedBox(new Vector3(s * 0.25f, 0.005f, 0), new Vector3(0.3f, 0.014f, 0.08f), 0.006f, M(part, ToyRed), 2, twist);
                    r.Lathe(new Vector3(s * 0.4f, -0.002f, 0), Disc(0.04f, 0.014f, 0.004f), M(part, ToyRed), twist, 20);              // rounded tip
                    r.RoundedBox(new Vector3(s * 0.25f, 0.005f, 0), new Vector3(0.3f, 0.016f, 0.008f), 0.003f, M(part, Gunmetal), 1, twist);   // spine
                    foreach (var t in new[] { 0.16f, 0.26f, 0.36f }) r.Rivet(new Vector3(s * t, 0.013f, 0.022f), Vector3.up, 0.005f, M(part, Gunmetal));
                }
                Done(r, _rotor);
            }
            foreach (var (arm, s, part, claw) in new[] { (_armL, -1f, "left arm", "left claw"), (_armR, 1f, "right arm", "right claw") })
            {
                var a = new Batch();
                DetailedArm(a, s, part, claw, ToyRed, ToyRedDeep, ToyRed, 0.7f);
                Done(a, arm);
            }
            DetailedHead(head, ToyRed, new Vector3(0.36f, 0.28f, 0.28f), 2, 0.052f,
                "head", "left eye lens", "right eye lens", "left antenna", "right antenna", "left ear cap", "right ear cap", null, false, true);
        }

        public override (Transform parent, Vector3 local) CarrySlot(int i, int n)
        {
            float a = i * 2.4f;
            return (carry, new Vector3(Mathf.Sin(a) * 0.1f * Mathf.Min(1, i), -(i / 4) * 0.05f, Mathf.Cos(a) * 0.1f * Mathf.Min(1, i)));
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
            _armL.localRotation = Quaternion.Euler(pitch + wiggle, 0, -20 + 15 * a.armHold);
            _armR.localRotation = Quaternion.Euler(pitch - wiggle, 0, 20 - 15 * a.armHold);
            LookHead(a, 70);
            if (a.gesture > 0)
            {
                float w = Mathf.Sin(a.gesture * Mathf.PI);
                _saucer.localRotation *= Quaternion.Euler(0, Mathf.SmoothStep(0, 720, a.gesture), 0);   // a happy spin...
                _saucer.localPosition += new Vector3(0, w * 0.12f, 0);                                     // ...with a little hop
            }
        }
    }
}
