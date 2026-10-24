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
    /// Family standard (docs/ROBOT_FAMILY_PHASE4_HOVERBOT.md): the family finish, the canonical cone ears
    /// and antennas, family arms with articulated grippers. A flight controller banks it into turns from
    /// the real lateral acceleration (speed x turn rate), noses it down to accelerate and up to brake, and
    /// settles it level on a damped spring; no idle sway. The propeller and its mast belong to the chassis:
    /// they sit on a mount at the head's top (HeadTopTaken), never on the head itself.
    /// Parts: head, left/right eye lens, left/right antenna, siren light, upper/lower
    /// saucer shell, rim light 1-4, thruster ring, thruster glow plate, propeller blade
    /// A/B, rotor hub, rotor mast, rotor motor, left/right arm, left/right claw,
    /// gyroscope, flight controller, battery pack, left/right ear cap.</summary>
    public class HoverbotModel : RobotModel
    {
        Transform _saucer, _mast, _rotor, _armL, _armR;
        float _rotorAngle, _rotorRate;
        // flight controller state: roll and pitch (deg) with their rates, the speed last frame, the
        // filtered fore-aft acceleration, the last altitude and the filtered climb rate
        float _roll, _rollV, _pitch, _pitchV, _vPrev, _acc, _altPrev = -1, _climb;
        public override float ModelHeight => 0.98f;
        const float HeadY = 0.475f, HeadH = 0.31f, HeadTop = HeadY + HeadH, RotorY = 0.935f, RimR = 0.44f;
        protected override Material Skin(Material m) => FamilySkin(m);
        protected override Material BoltSkin(Material m) => FamilyBoltSkin(m);

        /// <summary>The paint of the chassis and of each detachable module (Modify Robot spec §4): set before
        /// Init. The three Hoverbots share this one model; only these skins differ (ModuleSkin.HoverbotFactory).</summary>
        public ModuleSkin chassisSkin = ModuleSkin.ClassicRed, headSkin = ModuleSkin.ClassicRed, armLSkin = ModuleSkin.ClassicRed,
            armRSkin = ModuleSkin.ClassicRed, propSkin = ModuleSkin.ClassicRed;
        public void PaintFactory(ModuleSkin s) => chassisSkin = headSkin = armLSkin = armRSkin = propSkin = s;

        ModuleSkin SkinOf(string part) => part switch
        {
            "head" or "left eye lens" or "right eye lens" or "left antenna" or "right antenna" or "left ear cap" or "right ear cap" => headSkin,
            "left arm" or "left claw" => armLSkin,
            "right arm" or "right claw" => armRSkin,
            "propeller blade a" or "propeller blade b" or "rotor hub" => propSkin,
            _ => chassisSkin,
        };

        /// <summary>Each part in its module's skin. The arm segments are built in kit yellow only to tell them
        /// apart from the red grippers, which stay red in every skin (as do the metals, eyes and wear).</summary>
        protected override Material SkinPart(string part, Material m)
        {
            var s = SkinOf(part);
            if (s.Factory || part.EndsWith(" claw")) return FamilySkin(m == ToyYellow ? ToyRed : m == ToyYellowDeep ? ToyRedDeep : m);
            if (m == ToyYellow) return s.Arm;
            if (m == ToyYellowDeep) return s.ArmDeep;
            if (m == ToyRed)
                return part == "rotor hub" ? s.Hub
                     : part.Contains("antenna") || part.Contains("ear cap") || part.EndsWith(" arm") ? s.Accent
                     : part.StartsWith("propeller") ? s.Blade : s.Body;
            if (m == ToyRedDeep) return s.BodyDeep;
            return FamilySkin(m);
        }

        static readonly Vector2[] UpperShell = { new(0.436f, 0.316f), new(0.43f, 0.34f), new(0.39f, 0.375f), new(0.33f, 0.405f), new(0.26f, 0.425f) },
            RimBand = { new(RimR, 0.274f), new(RimR, 0.306f) },
            LowerShell = { new(0.33f, 0.17f), new(0.38f, 0.2f), new(0.42f, 0.235f), new(0.437f, 0.262f) };

        /// <summary>The radius of a lathed shell profile at height y.</summary>
        static float ShellR(Vector2[] p, float y)
        {
            for (int i = 0; i < p.Length - 1; i++)
                if (y <= p[i + 1].y) return Mathf.Lerp(p[i].x, p[i + 1].x, Mathf.InverseLerp(p[i].y, p[i + 1].y, y));
            return p[^1].x;
        }

        /// <summary>A painted pinstripe round a lathed shell between heights y0 and y1, a hair proud of it.</summary>
        void Pinstripe(Batch b, string part, Vector2[] shell, float y0, float y1, Material mat)
        {
            const float lift = 0.0012f;
            float r0 = ShellR(shell, y0) + lift, r1 = ShellR(shell, y1) + lift;
            b.Lathe(Vector3.zero, new[] { new Vector2(r0 - 0.003f, y0 - 0.0004f), new Vector2(r0, y0), new Vector2(r1, y1), new Vector2(r1 - 0.003f, y1 + 0.0004f) }, M(part, mat), null, 48);
        }

        /// <summary>Emblem linework: tongues grown from a root (across, up, in units of the emblem's size), at an angle
        /// (deg), with a length and greatest width; drawn in three overlapping segments tapering to a point (flame),
        /// swelling in the middle (petal) or flat (line).</summary>
        static readonly (float u, float v, float a, float l, float w, char taper)[] LotusStrokes =
        {
            (0, -0.32f, 90, 0.78f, 0.26f, 'p'), (-0.04f, -0.34f, 122, 0.66f, 0.22f, 'p'), (0.04f, -0.34f, 58, 0.66f, 0.22f, 'p'),
            (-0.08f, -0.36f, 152, 0.5f, 0.18f, 'p'), (0.08f, -0.36f, 28, 0.5f, 0.18f, 'p'), (-0.42f, -0.4f, 0, 0.84f, 0.06f, 'l'),
        }, FlameStrokes =
        {
            (0, -0.5f, 90, 1.05f, 0.22f, 'f'), (-0.06f, -0.48f, 112, 0.82f, 0.17f, 'f'), (0.06f, -0.48f, 68, 0.82f, 0.17f, 'f'),
            (-0.12f, -0.46f, 136, 0.56f, 0.14f, 'f'), (0.12f, -0.46f, 44, 0.56f, 0.14f, 'f'),
            (-0.4f, -0.08f, 70, 0.34f, 0.1f, 'f'), (0.4f, -0.08f, 110, 0.34f, 0.1f, 'f'),
        };

        /// <summary>A painted emblem centred at c on a surface with outward normal n (batch coordinates), upright
        /// (or upside down) along the surface.</summary>
        void Emblem(Batch b, string part, string kind, Vector3 c, Vector3 n, float size, Material mat, bool flip = false)
        {
            var nU = new Vector3(n.x, n.y, -n.z).normalized;
            var upU = Vector3.ProjectOnPlane(Vector3.up, nU).normalized * (flip ? -1 : 1);
            var rot = Quaternion.LookRotation(nU, upU);
            var rightU = rot * Vector3.right;
            foreach (var s in kind == "lotus" ? LotusStrokes : FlameStrokes)
            {
                var dir = new Vector2(Mathf.Cos(s.a * Mathf.Deg2Rad), Mathf.Sin(s.a * Mathf.Deg2Rad));
                int segs = s.taper == 'l' ? 1 : 3;
                for (int i = 0; i < segs; i++)
                {
                    float w = s.taper == 'f' ? 1f - 0.32f * i : s.taper == 'p' ? (i == 1 ? 1f : i == 0 ? 0.62f : 0.5f) : 1f;
                    float t = (i + 0.5f) / segs, len = s.l / segs * (segs > 1 ? 1.25f : 1f);
                    var q = new Vector2(s.u, s.v) + dir * (s.l * t);
                    var o = (rightU * q.x + upU * q.y) * size + nU * 0.0012f;
                    b.RoundedBox(c + new Vector3(o.x, o.y, -o.z), new Vector3(len * size, s.w * w * size, 0.0018f), 0.0008f, M(part, mat), 1, rot * Quaternion.Euler(0, 0, s.a));
                }
            }
        }
        protected override bool HeadTopTaken => true;
        /// <summary>The flier keeps its load in the hold under the saucer (no claw grip in the robot logic).</summary>
        public override Vector3? GripPoint => null;

        protected override void CreatePivots()
        {
            _saucer = Pivot("Saucer", body, Vector3.zero);
            head = Pivot("Head", _saucer, new Vector3(0, HeadY, 0));
            // the chassis-owned propeller mount, at the top of whatever head is fitted
            _mast = Pivot("Mast", _saucer, new Vector3(0, HeadTop, 0));
            _rotor = Pivot("Rotor", _mast, new Vector3(0, RotorY - HeadTop, 0));
            _armL = Pivot("ArmL", _saucer, new Vector3(-RimR - 0.06f, 0.3f, 0));
            _armR = Pivot("ArmR", _saucer, new Vector3(RimR + 0.06f, 0.3f, 0));
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
                Bolt(b, new Vector3(Mathf.Sin(a) * 0.405f, 0.368f, Mathf.Cos(a) * 0.405f), new Vector3(Mathf.Sin(a), 1.4f, Mathf.Cos(a)), 0.0075f, M("upper saucer shell", Gunmetal));
                Bolt(b, new Vector3(Mathf.Sin(a) * 0.4f, 0.217f, Mathf.Cos(a) * 0.4f), new Vector3(Mathf.Sin(a), -1.2f, Mathf.Cos(a)), 0.0075f, M("lower saucer shell", Gunmetal));
            }
            // radial panel seams on both shells (between the panels, clear of the front window)
            var upperP = new[] { new Vector2(0.434f, 0.322f), new Vector2(0.43f, 0.34f), new Vector2(0.39f, 0.375f), new Vector2(0.33f, 0.405f), new Vector2(0.29f, 0.418f) };
            var lowerP = new[] { new Vector2(0.335f, 0.173f), new Vector2(0.38f, 0.2f), new Vector2(0.42f, 0.235f), new Vector2(0.434f, 0.258f) };
            for (int k = 0; k < 12; k++)
            {
                float a = (k + 0.5f) * Mathf.PI / 6;
                var dir = new Vector3(Mathf.Sin(a), 0, Mathf.Cos(a));
                foreach (var (prof, part) in new[] { (upperP, "upper saucer shell"), (lowerP, "lower saucer shell") })
                    for (int i = 0; i < prof.Length - 1; i++)
                    {
                        var e = prof[i + 1] - prof[i];
                        var nr = new Vector2(e.y, -e.x).normalized;
                        SeamLine(b, part, dir * prof[i].x + Vector3.up * prof[i].y, dir * prof[i + 1].x + Vector3.up * prof[i + 1].y, dir * nr.x + Vector3.up * nr.y, 0.0042f, 0.0016f);
                    }
            }
            // brass feed pipes along the underside, from the skirt out to the arm ports
            foreach (var sd in new[] { -1f, 1f })
                foreach (var z in new[] { -0.06f, 0.06f })
                    Pipe(b, "lower saucer shell", new[] { new Vector3(sd * 0.338f, 0.158f, z), new Vector3(sd * 0.36f, 0.177f, z), new Vector3(sd * 0.41f, 0.216f, z * 0.9f), new Vector3(sd * 0.434f, 0.248f, z * 0.7f) }, 0.0075f, Brass, Gunmetal);
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
            // the chassis skin's linework: pink pin lines (White / Pink), a gold band with flames (Red / Yellow)
            if (chassisSkin.Stripe is Material cs)
            {
                if (chassisSkin.emblem == "flame")
                {
                    Pinstripe(b, "upper saucer shell", RimBand, 0.2835f, 0.2965f, cs);
                    Pinstripe(b, "upper saucer shell", UpperShell, 0.328f, 0.334f, cs);
                    Pinstripe(b, "lower saucer shell", LowerShell, 0.243f, 0.249f, cs);
                    foreach (var a in new[] { -0.4f, 0.4f, -1.25f, 1.25f })   // big flames either side of the siren and round the front quarters
                    {
                        float sa = Mathf.Sin(a), ca = Mathf.Cos(a);
                        Emblem(b, "upper saucer shell", "flame", new Vector3(sa * ShellR(UpperShell, 0.358f), 0.358f, -ca * ShellR(UpperShell, 0.358f)), new Vector3(sa * 0.66f, 0.75f, -ca * 0.66f), 0.068f, cs);
                        Emblem(b, "lower saucer shell", "flame", new Vector3(sa * ShellR(LowerShell, 0.222f), 0.222f, -ca * ShellR(LowerShell, 0.222f)), new Vector3(sa * 0.66f, -0.75f, -ca * 0.66f), 0.045f, cs, true);
                    }
                }
                else
                {
                    Pinstripe(b, "upper saucer shell", UpperShell, 0.328f, 0.335f, cs);
                    Pinstripe(b, "upper saucer shell", UpperShell, 0.385f, 0.39f, cs);
                    Pinstripe(b, "upper saucer shell", RimBand, 0.279f, 0.285f, cs);
                    Pinstripe(b, "upper saucer shell", RimBand, 0.295f, 0.301f, cs);
                    Pinstripe(b, "lower saucer shell", LowerShell, 0.243f, 0.25f, cs);
                }
            }
            Done(b, _saucer);
            {
                // rotor mast on the chassis mount (coordinates from the head's top): a bolted foot plate, the
                // shaft, stacked dark gears with a brass one, and the motor
                var m = new Batch();
                m.Lathe(new Vector3(0, -0.002f, 0), Disc(0.07f, 0.012f, 0.003f), M("rotor mast", Gunmetal), null, 24);
                for (int k = 0; k < 6; k++)
                {
                    float a = k * Mathf.PI / 3 + 0.3f;
                    Bolt(m, new Vector3(Mathf.Sin(a) * 0.055f, 0.01f, Mathf.Cos(a) * 0.055f), Vector3.up, 0.005f, M("rotor mast", Gunmetal));
                }
                m.Lathe(new Vector3(0, 0.005f, 0), Disc(0.026f, 0.13f, 0.004f), M("rotor mast", Gunmetal), null, 12);
                for (int k = 0; k < 3; k++)
                {
                    float y = 0.025f + k * 0.032f, r = 0.06f - k * 0.009f;
                    m.Lathe(new Vector3(0, y, 0), Disc(r, 0.018f, 0.004f), M("rotor motor", k == 1 ? Brass : Gunmetal), null, 20);
                    for (int t = 0; t < 12; t++)
                    {
                        float a = t * Mathf.PI / 6;
                        m.Box(new Vector3(Mathf.Sin(a) * r, y + 0.009f, Mathf.Cos(a) * r), new Vector3(0.011f, 0.015f, 0.011f), M("rotor motor", Gunmetal), false, Batch.R(Vector3.up, a));
                    }
                }
                GreaseRun(m, "rotor motor", new Vector3(0.05f, 0.03f, -0.02f), X(1), 0.03f, 0.01f);
                Done(m, _mast);
            }
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
                    foreach (var t in new[] { 0.16f, 0.26f, 0.36f }) Bolt(r, new Vector3(s * t, 0.013f, 0.03f), Vector3.up, 0.005f, M(part, Gunmetal));
                    if (propSkin.Stripe is Material ps)   // two diagonal stripes across each blade towards its tip
                        foreach (var t in new[] { 0.31f, 0.35f })
                            r.RoundedBox(new Vector3(s * t, 0.005f, 0), new Vector3(propSkin.emblem == "flame" ? 0.017f : 0.013f, 0.0158f, 0.094f), 0.002f, M(part, ps), 1, twist * Batch.R(Vector3.up, s * 0.45f));
                }
                Done(r, _rotor);
            }
            foreach (var (arm, s, part, claw) in new[] { (_armL, -1f, "left arm", "left claw"), (_armR, 1f, "right arm", "right claw") })
            {
                arm.localScale = Vector3.one * 0.85f;   // short arms out of the rim ports, big grippers (the sheet)
                FamilyArm(arm, s, part, new ArmSpec { paint = ToyYellow, paintDeep = ToyYellowDeep, red = ToyRed, clawScale = 1.3f, twist = 0f, clawPart = claw });   // yellow: the arm's own paint (SkinPart)
            }
            DetailedHead(head, ToyRed, new Vector3(0.44f, HeadH, 0.32f), 2, 0.06f,
                "head", "left eye lens", "right eye lens", "left antenna", "right antenna", "left ear cap", "right ear cap", null, true, true, familyEars: true);
            {
                var w = new Batch();   // the loose wire under the head (it is on the sheet)
                Pipe(w, "head", new[] { new Vector3(0.19f, 0.03f, 0.06f), new Vector3(0.205f, -0.005f, 0.08f), new Vector3(0.212f, -0.03f, 0.07f) }, 0.004f, Rubber, Brass);
                if (headSkin.Stripe is Material hs)
                {
                    // pin lines round the head behind the face and near the back (thin slices of a slightly larger
                    // head box, so they follow its rounded edges), and the emblem on the forehead between the eyes
                    // a painted frame on the flat of the face, just inside its rounded edges (the eye rings sit over it)
                    float bev = Mathf.Min(0.058f, HeadH * 0.19f), wide = headSkin.emblem == "flame" ? 0.007f : 0.0045f;
                    float fx = 0.22f - bev - 0.004f, fy0 = bev + 0.004f, fy1 = HeadH - bev - 0.004f, fz = -0.16f - 0.0012f;
                    foreach (var sx in new[] { -1f, 1f })
                        w.RoundedBox(new Vector3(sx * fx, (fy0 + fy1) / 2, fz), new Vector3(wide, fy1 - fy0 + wide, 0.0018f), 0.0008f, M("head", hs), 1);
                    foreach (var y in new[] { fy0, fy1 })
                        w.RoundedBox(new Vector3(0, y, fz), new Vector3(2 * fx + wide, wide, 0.0018f), 0.0008f, M("head", hs), 1);
                    Emblem(w, "head", headSkin.emblem, new Vector3(0, 0.228f, -0.16f), new Vector3(0, 0, -1), headSkin.emblem == "flame" ? 0.055f : 0.05f, hs);
                }
                Done(w, head);
            }
        }

        public override (Transform parent, Vector3 local) CarrySlot(int i, int n)
        {
            float a = i * 2.4f;
            return (carry, new Vector3(Mathf.Sin(a) * 0.1f * Mathf.Min(1, i), -(i / 4) * 0.05f, Mathf.Cos(a) * 0.1f * Mathf.Min(1, i)));
        }

        /// <summary>Where a carried part rides (saucer space): in the grippers in front of the saucer, below the
        /// rim; and, before the arms have closed on it, the old spot under the saucer.</summary>
        static readonly Vector3 HoldPoint = new(0, 0.1f, 0.68f), HoldUnder = new(0, 0.05f, 0.05f);

        /// <summary>The carry pose, blended over the empty one by w: each upper arm swings forward (a little out
        /// and down, clear of the rim), the forearm turns in and its telescope runs out until the gripper sits
        /// on its side of the load at `hold` (saucer space), `halfGap` off the centre.</summary>
        void HoldArms(float w, Vector3 hold, float halfGap)
        {
            if (w <= 0.001f || !HasGrippers) return;
            float ext = 0;
            foreach (var (arm, tip, s) in new[] { (_armL, clawTipL, -1f), (_armR, clawTipR, 1f) })
            {
                var elbow = arm.Find("Elbow");
                var restA = arm.localRotation;
                var restE = elbow.localRotation;
                var upper = Quaternion.FromToRotation(Vector3.down, new Vector3(s * 0.3f, -0.35f, 1f).normalized);
                arm.localRotation = upper;
                elbow.localRotation = Quaternion.identity;
                // in the elbow's frame: the tip with the forearm home, and the gripper's spot at the load
                var tipE = elbow.InverseTransformPoint(tip.position);
                var toE = elbow.InverseTransformPoint(_saucer.TransformPoint(hold + new Vector3(s * halfGap, 0, 0)));
                // the telescope runs out along the forearm (-Y) until the tip is as far from the elbow as the spot
                float side2 = tipE.x * tipE.x + tipE.z * tipE.z;
                float e = Mathf.Clamp(Mathf.Sqrt(Mathf.Max(0, toE.sqrMagnitude - side2)) + tipE.y, 0, 0.6f);
                ext = Mathf.Max(ext, e);
                var turn = Quaternion.FromToRotation(tipE + Vector3.down * e, toE);
                arm.localRotation = Quaternion.Slerp(restA, upper, w);
                elbow.localRotation = Quaternion.Slerp(restE, turn, w);
            }
            PoseTelescopes(ext * w);
        }

        /// <summary>A damped spring step (semi-implicit, sub-stepped for long frames).</summary>
        static void Spring(ref float x, ref float v, float target, float dt, float omega, float zeta)
        {
            int n = Mathf.CeilToInt(dt / 0.02f);
            float h = dt / n;
            for (int i = 0; i < n; i++)
            {
                v += (omega * omega * (target - x) - 2 * zeta * omega * v) * h;
                x += v * h;
            }
        }

        public override void Animate(RobotAnim a)
        {
            float dt = a.dt;
            bool live = dt > 0 && dt < 0.25f;   // a running robot; studio and QA poses snap (dt 0 or 1)
            // ---- flight controller: what the saucer would do, driven by the real motion
            float latAcc = a.speed * a.turnRate * Mathf.Deg2Rad;                 // m/s^2 towards the inside of the turn
            if (live) _acc = Mathf.Lerp(_acc, (a.speed - _vPrev) / dt, 1 - Mathf.Exp(-dt * 8));
            else _acc = 0;
            _vPrev = a.speed;
            if (live && _altPrev >= 0) _climb = Mathf.Lerp(_climb, (a.altitude - _altPrev) / dt, 1 - Mathf.Exp(-dt * 6));
            else _climb = 0;
            _altPrev = a.altitude;
            // bank into the turn (right turn: right side down), nose down to cruise and to speed up, up to brake
            float rollT = -Mathf.Clamp(Mathf.Atan2(latAcc, 9.81f) * Mathf.Rad2Deg * 1.2f, -22f, 22f);
            float pitchT = Mathf.Clamp(a.speed01 * 7f + Mathf.Atan2(_acc, 9.81f) * Mathf.Rad2Deg * 2.2f, -16f, 22f);
            if (live)
            {
                Spring(ref _roll, ref _rollV, rollT, dt, 6.5f, 0.72f);    // a small overshoot as it levels out
                Spring(ref _pitch, ref _pitchV, pitchT, dt, 6f, 0.8f);
            }
            else { _roll = rollT; _pitch = pitchT; _rollV = _pitchV = 0; }
            // a gentle hover bob (vertical only: no idle sway); it settles while flying fast
            float calm = 1 - 0.6f * Mathf.Clamp01(a.speed01);
            float bob = (Mathf.Sin(a.time * 2.2f) * 0.01f + Mathf.Sin(a.time * 5.3f) * 0.003f) * calm;
            _saucer.localPosition = new Vector3(0, bob, 0);
            _saucer.localRotation = Quaternion.Euler(_pitch, 0, _roll);
            // the rotor: idles when active, works harder climbing, accelerating and banking
            float load = Mathf.Clamp01(Mathf.Abs(_climb) * 0.8f + Mathf.Abs(_acc) * 0.3f + Mathf.Abs(_roll) / 40f);
            float rateT = a.active ? 1300f + 900f * load : 0f;
            _rotorRate = live ? Mathf.MoveTowards(_rotorRate, rateT, dt * 2400f) : rateT;
            _rotorAngle += dt * _rotorRate * (live ? 1 : 0);
            _rotor.localRotation = Quaternion.Euler(0, _rotorAngle, 0);
            // the load in the hold hangs level (the hold is gimballed against the saucer's tilt)
            carry.localRotation = Quaternion.Inverse(_saucer.localRotation);
            // ---- arms: held out of the rim ports with the grippers hanging; they swing towards plumb in a bank
            float pitch = -20 * a.armHold - 50 * a.armReach - 60 * a.armLift - 0.6f * _pitch;
            float empty = 1 - Mathf.Max(a.armHold, a.armReach);
            float hang = -0.6f * _roll * empty;
            _armL.localRotation = Quaternion.Euler(pitch - 4 * empty, 0, -65 * empty + 6 * a.armHold + hang);
            _armR.localRotation = Quaternion.Euler(pitch - 4 * empty, 0, 65 * empty - 6 * a.armHold + hang);
            PoseElbows(a, 14, 62);
            PoseTelescopes(0);
            // carrying: the grippers hold the load out in front of the saucer (raised and pushed forward to deposit)
            float hw = Ease(a.armHold);
            var hold = HoldPoint + new Vector3(0, 0.08f, 0.1f) * a.armLift;
            float halfGap = 0.45f * (a.gripWidth > 0 ? a.gripWidth : 0.05f) / Mathf.Max(1e-4f, _saucer.lossyScale.x);
            HoldArms(hw, hold, halfGap);
            carry.localPosition = Vector3.Lerp(HoldUnder, hold, hw);
            // grippers: open reaching for a part, close on it as it is lifted and carried, relaxed otherwise
            PoseClaws(ClawOpenDeg * Mathf.Max(a.clawOpen, a.armReach * (1 - a.armHold)) - ClawCloseDeg * Mathf.Max(a.clawClose, a.armLift));
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
