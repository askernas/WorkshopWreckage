using UnityEngine;

namespace WorkshopWreckage
{
    /// <summary>Hoverbot after resources/hoverbot.png: a red riveted flying saucer.
    /// A deep lathed lens of a saucer: a red rim band with four glowing portholes, a
    /// slot window, side arm ports and a red siren; a wide dark slatted skirt with a
    /// glowing plate underneath; a brass gyroscope ring on top; the family head in red with two
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
        public override float ModelHeight => 0.98f;
        const float HeadY = 0.475f, RotorY = 0.935f, RimR = 0.44f;

        protected override void CreatePivots()
        {
            _saucer = Pivot("Saucer", body, Vector3.zero);
            head = Pivot("Head", _saucer, new Vector3(0, HeadY, 0));
            _rotor = Pivot("Rotor", _saucer, new Vector3(0, RotorY, 0));
            _armL = Pivot("ArmL", _saucer, new Vector3(-RimR - 0.02f, 0.27f, 0));
            _armR = Pivot("ArmR", _saucer, new Vector3(RimR + 0.02f, 0.27f, 0));
            carry = Pivot("Carry", _saucer, new Vector3(0, 0.05f, 0.05f));   // held under the saucer
            Mount(head, "head", Vector3.up, 0.05f);
            Mount(_rotor, "rotor hub", Vector3.up, 0.035f);
            Mount(_armL, "left arm", Vector3.left, 0.035f);
            Mount(_armR, "right arm", Vector3.right, 0.035f);
        }

        protected override Vector3? ClampPoint => new Vector3(0, 0.26f, Has("lower saucer shell") ? RimR + 0.02f : 0.2f);

        /// <summary>Without the saucer shells: two crossed spars around the hub ring.</summary>
        protected override void BuildFrame()
        {
            if (Has("lower saucer shell")) return;
            var b = new Batch();
            b.RoundedBox(new Vector3(0, 0.22f, 0), new Vector3(RimR * 1.9f, 0.025f, 0.035f), 0.006f, Gunmetal, 2);
            b.RoundedBox(new Vector3(0, 0.22f, 0), new Vector3(0.035f, 0.025f, RimR * 1.9f), 0.006f, Gunmetal, 2);
            b.Lathe(new Vector3(0, 0.2f, 0), Ring(0.13f, 0.1f, 0.04f), Gunmetal, null, 24);
            b.Lathe(new Vector3(0, 0.2f, 0), Disc(0.06f, 0.06f, 0.008f), Brass, null, 16);
            b.Build(_saucer, "G_frame", true);
        }

        protected override void BuildGeometry()
        {
            var b = new Batch();
            // saucer: a deep lens (the concept sheet), red shells either side of a red rim band
            // with dark seams, a wide dark slatted skirt underneath
            b.Lathe(Vector3.zero, new[] { new Vector2(0.33f, 0.17f), new Vector2(0.38f, 0.2f), new Vector2(0.42f, 0.235f), new Vector2(0.437f, 0.262f) }, M("lower saucer shell", ToyRed), null, 48);
            b.Lathe(Vector3.zero, new[] { new Vector2(RimR - 0.006f, 0.262f), new Vector2(RimR + 0.004f, 0.266f), new Vector2(RimR + 0.004f, 0.27f), new Vector2(RimR - 0.004f, 0.272f) }, M("lower saucer shell", Gunmetal), null, 48);
            b.Lathe(Vector3.zero, new[] { new Vector2(RimR - 0.004f, 0.27f), new Vector2(RimR, 0.274f), new Vector2(RimR, 0.306f), new Vector2(RimR - 0.004f, 0.31f) }, M("upper saucer shell", ToyRed), null, 48);
            b.Lathe(Vector3.zero, new[] { new Vector2(RimR - 0.004f, 0.308f), new Vector2(RimR + 0.004f, 0.31f), new Vector2(RimR + 0.004f, 0.314f), new Vector2(RimR - 0.006f, 0.318f) }, M("upper saucer shell", Gunmetal), null, 48);
            b.Lathe(Vector3.zero, new[] { new Vector2(0.436f, 0.316f), new Vector2(0.43f, 0.34f), new Vector2(0.39f, 0.375f), new Vector2(0.33f, 0.405f), new Vector2(0.26f, 0.425f), new Vector2(0.15f, 0.437f), new Vector2(0, 0.44f) }, M("upper saucer shell", ToyRed), null, 48);
            for (int k = 0; k < 24; k++)   // rivet rings on both shells
            {
                float a = k * Mathf.PI / 12;
                b.Rivet(new Vector3(Mathf.Sin(a) * 0.405f, 0.368f, Mathf.Cos(a) * 0.405f), new Vector3(Mathf.Sin(a), 1.4f, Mathf.Cos(a)), 0.0075f, M("upper saucer shell", Gunmetal));
                b.Rivet(new Vector3(Mathf.Sin(a) * 0.4f, 0.217f, Mathf.Cos(a) * 0.4f), new Vector3(Mathf.Sin(a), -1.2f, Mathf.Cos(a)), 0.0075f, M("lower saucer shell", Gunmetal));
            }
            // slot window in the front of the rim band, and the side ports the arms come out of
            Grille(b, "upper saucer shell", new Vector3(0, 0.29f, -RimR - 0.004f), 0.2f, 0.044f, 3, false, -1);
            foreach (var sd in new[] { -1f, 1f })
            {
                b.Lathe(new Vector3(sd * (RimR - 0.01f), 0.29f, 0), Disc(0.05f, 0.03f, 0.008f), M("upper saucer shell", Gunmetal), SideRot(sd), 20);
                b.Lathe(new Vector3(sd * (RimR + 0.02f), 0.29f, 0), Ring(0.046f, 0.03f, 0.01f), M("upper saucer shell", Brass), SideRot(sd), 20);
            }
            // skirt: a wide dark drum with vertical slats, and the battery hatch at the back
            b.Lathe(Vector3.zero, new[] { new Vector2(0.15f, 0.092f), new Vector2(0.3f, 0.094f), new Vector2(0.325f, 0.105f), new Vector2(0.33f, 0.172f) }, M("thruster ring", Gunmetal), null, 40);
            for (int k = 0; k < 32; k++)
            {
                float a = k * Mathf.PI / 16;
                b.RoundedBox(new Vector3(Mathf.Sin(a) * 0.333f, 0.138f, Mathf.Cos(a) * 0.333f), new Vector3(0.016f, 0.052f, 0.01f), 0.003f, M("thruster ring", Dark), 1, Batch.R(Vector3.up, a));
            }
            b.Lathe(new Vector3(0, 0.172f, 0), Ring(0.345f, 0.3f, 0.008f), M("thruster ring", Brass), null, 40);
            b.RoundedBox(new Vector3(0, 0.13f, 0.336f), new Vector3(0.14f, 0.04f, 0.02f), 0.008f, M("battery pack", Gunmetal), 2);
            // top: gyroscope ring, flight controller, neck ring under the head
            b.Lathe(new Vector3(0, 0.405f, 0), Ring(0.29f, 0.262f, 0.016f), M("gyroscope", Brass), null, 40);
            b.RoundedBox(new Vector3(0, 0.43f, 0.21f), new Vector3(0.12f, 0.03f, 0.06f), 0.01f, M("flight controller", Gunmetal), 2);
            b.Lathe(new Vector3(0, 0.432f, 0), Disc(0.13f, 0.016f, 0.004f), M("upper saucer shell", Gunmetal), null, 28);
            b.Lathe(new Vector3(0, 0.446f, 0), Disc(0.1f, 0.03f, 0.006f), M("upper saucer shell", Gunmetal), null, 24);   // neck collar
            b.Lathe(new Vector3(0, 0.456f, 0), Ring(0.106f, 0.09f, 0.008f), M("upper saucer shell", Brass), null, 24);
            // rotor mast: from the top of the head, stacked dark gears with a brass one and the motor
            b.Lathe(new Vector3(0, 0.79f, 0), Disc(0.026f, 0.13f, 0.004f), M("rotor mast", Gunmetal), null, 12);
            for (int k = 0; k < 3; k++)
            {
                float y = 0.81f + k * 0.032f, r = 0.06f - k * 0.009f;
                b.Lathe(new Vector3(0, y, 0), Disc(r, 0.018f, 0.004f), M("rotor motor", k == 1 ? Brass : Gunmetal), null, 20);
                for (int t = 0; t < 12; t++)
                {
                    float a = t * Mathf.PI / 6;
                    b.Box(new Vector3(Mathf.Sin(a) * r, y + 0.009f, Mathf.Cos(a) * r), new Vector3(0.011f, 0.015f, 0.011f), M("rotor motor", Gunmetal), false, Batch.R(Vector3.up, a));
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
                lb.Lathe(dir * (RimR - 0.004f) + new Vector3(0, 0.29f, 0), Ring(0.03f, 0.021f, 0.014f), M(rims[k], Brass), Quaternion.FromToRotation(Vector3.up, Batch.G(dir)), 20);
                lb.Lathe(dir * (RimR + 0.002f) + new Vector3(0, 0.29f, 0), Dome(0.022f, 0.01f), M(rims[k], Glow), Quaternion.FromToRotation(Vector3.up, Batch.G(dir)), 16);
                Done(lb, _saucer);
            }
            var glow = new Batch();
            glow.Lathe(new Vector3(0, 0.094f, 0), Dome(0.16f, 0.032f, 6), M("thruster glow plate", Glow), DownRot, 32);
            Done(glow, _saucer);
            if (Has("siren light"))
                beacons.Add(Separate(_saucer, "G_siren", lb => lb.Lathe(new Vector3(0, 0.37f, -0.39f), Dome(0.03f, 0.028f, 6), BeaconOffMat, Quaternion.FromToRotation(Vector3.up, new Vector3(0, 0.6f, 0.8f)), 16)));
            {
                var r = new Batch();   // propeller: red dome hub, two riveted paddle blades
                r.Lathe(new Vector3(0, -0.01f, 0), Disc(0.04f, 0.02f, 0.005f), M("rotor hub", Gunmetal), null, 18);
                r.Lathe(new Vector3(0, 0.01f, 0), Dome(0.038f, 0.032f), M("rotor hub", ToyRed), null, 18);
                foreach (var (s, part) in new[] { (1f, "propeller blade a"), (-1f, "propeller blade b") })
                {
                    var twist = Batch.R(Vector3.right, s * 0.3f);   // pitched: the paddles read from the front
                    r.RoundedBox(new Vector3(s * 0.07f, 0.005f, 0), new Vector3(0.08f, 0.022f, 0.03f), 0.006f, M(part, Gunmetal), 2, twist);   // shank
                    r.RoundedBox(new Vector3(s * 0.26f, 0.005f, 0), new Vector3(0.3f, 0.014f, 0.1f), 0.007f, M(part, ToyRed), 2, twist);
                    r.Lathe(new Vector3(s * 0.41f, -0.002f, 0), Disc(0.05f, 0.014f, 0.004f), M(part, ToyRed), twist, 24);              // rounded tip
                    r.RoundedBox(new Vector3(s * 0.26f, 0.005f, 0), new Vector3(0.31f, 0.017f, 0.008f), 0.003f, M(part, Gunmetal), 1, twist);   // spine
                    foreach (var t in new[] { 0.16f, 0.26f, 0.36f }) r.Rivet(new Vector3(s * t, 0.013f, 0.03f), Vector3.up, 0.005f, M(part, Gunmetal));
                }
                Done(r, _rotor);
            }
            foreach (var (arm, s, part, claw) in new[] { (_armL, -1f, "left arm", "left claw"), (_armR, 1f, "right arm", "right claw") })
            {
                var a = new Batch();
                DetailedArm(a, s, part, claw, ToyRed, ToyRedDeep, ToyRed, 0.56f);
                Done(a, arm);
            }
            DetailedHead(head, ToyRed, new Vector3(0.44f, 0.31f, 0.32f), 2, 0.06f,
                "head", "left eye lens", "right eye lens", "left antenna", "right antenna", "left ear cap", "right ear cap", null, true, true);
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
            _armL.localRotation = Quaternion.Euler(pitch + wiggle, 0, 4 + 6 * a.armHold);   // tucked under the rim
            _armR.localRotation = Quaternion.Euler(pitch - wiggle, 0, -4 - 6 * a.armHold);
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
