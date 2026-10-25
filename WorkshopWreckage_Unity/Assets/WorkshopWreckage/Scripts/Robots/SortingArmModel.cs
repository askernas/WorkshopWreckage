using UnityEngine;

namespace WorkshopWreckage
{
    /// <summary>The Sorting Arm Mk I after resources/robot_arm.png, in the robot family's look:
    /// a mustard toy-enamel industrial arm (bolted panel plates, seams, rust, looping hoses, the
    /// stencilled "A-01"), big red joint caps with bolt rings and brass hubs, a red drum turret
    /// with amber indicators, and a big hooked red / gunmetal pincer; it hangs from a bolted
    /// docking coupler on an overhead cabinet socket. Built at true
    /// size (≈2 m reach from the socket) and shown at 0.42 scale on the bench and while
    /// carried (folded, standing on its coupler). Pose is driven by SortingArm through
    /// analytic two-bone IK in the shoulder plane; the elbow always bends outward (away
    /// from the cabinet). Parts: docking coupler, shoulder turret, control unit, upper arm,
    /// motor cartridge, elbow servo, forearm, wrist rotator, sensor head, left finger,
    /// right finger, cable harness.</summary>
    public class SortingArmModel : RobotModel
    {
        public const float L1 = 0.85f, L2 = 0.8f, ShoulderDrop = 0.42f, GripLen = 0.17f;
        const float UX = 0.2f, FX = 0.07f;   // the upper arm runs beside the turret, the forearm inside it (the reference)
        Transform _shoulder, _upper, _elbow, _wrist, _fingerL, _fingerR;
        public override float ModelHeight => 2.0f;
        protected override Material Skin(Material m) => FamilySkin(m, FamilyOrange);   // industrial orange
        protected override Material BoltSkin(Material m) => FamilyBoltSkin(m);

        protected override void CreatePivots()
        {
            _shoulder = Pivot("Shoulder", body, new Vector3(0, -0.06f, 0));
            _upper = Pivot("Upper", _shoulder, new Vector3(0, -(ShoulderDrop - 0.06f), 0));
            _elbow = Pivot("Elbow", _upper, new Vector3(0, -L1, 0));
            _wrist = Pivot("Wrist", _elbow, new Vector3(0, -L2, 0));
            _fingerL = Pivot("FingerL", _wrist, new Vector3(-0.04f, -0.1f, 0));
            _fingerR = Pivot("FingerR", _wrist, new Vector3(0.04f, -0.1f, 0));
            // the pincer opens in the arm's swing plane, so the claws show in profile (the reference)
            _fingerL.localRotation = Quaternion.Euler(0, 90, 0);
            _fingerR.localRotation = Quaternion.Euler(0, 90, 0);
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

        /// <summary>A tapered limb along -Y from y0 to y1 at x = cx (sizes: x across, z depth),
        /// with rounded joint ends, deep-paint bands near both ends, bolts along the edges of
        /// both side faces (a few bleeding short rust drips) and seams across the outer face.</summary>
        void TaperedLimb(Batch b, string part, float cx, float y0, float y1, Vector2 s0, Vector2 s1, float r0, float r1)
        {
            var a = new Vector3(cx, y0, 0);
            var e = new Vector3(cx, y1, 0);
            b.Loft(a, e, s0, s1, 0.022f, 0.018f, M(part, ToyYellow));
            // rounded ends wrapping the joints
            b.Lathe(new Vector3(cx - s0.x / 2, y0, 0), Disc(r0, s0.x, 0.012f), M(part, ToyYellow), RightRot, 32);
            b.Lathe(new Vector3(cx - s1.x / 2, y1, 0), Disc(r1, s1.x, 0.01f), M(part, ToyYellow), RightRot, 28);
            Vector2 S(float t) => Vector2.Lerp(s0, s1, t);
            float Y(float t) => Mathf.Lerp(y0, y1, t);
            foreach (var tb in new[] { 0.14f, 0.86f })   // deep-paint bands
                b.Loft(new Vector3(cx, Y(tb), 0), new Vector3(cx, Y(tb + 0.035f), 0), S(tb) + Vector2.one * 0.008f, S(tb + 0.035f) + Vector2.one * 0.008f, 0.022f, 0.022f, M(part, ToyYellowDeep));
            int nb = Mathf.Max(3, Mathf.RoundToInt(Mathf.Abs(y1 - y0) / 0.11f));
            foreach (var side in new[] { -1f, 1f })
            {
                var nrm = new Vector3(side, 0, 0);
                for (int i = 1; i < nb; i++)
                {
                    float t = i / (float)nb;
                    var sz = S(t);
                    foreach (var zs in new[] { -1f, 1f })
                    {
                        var p = new Vector3(cx + side * (sz.x / 2 + 0.001f), Y(t), zs * (sz.y / 2 - 0.024f));
                        Bolt(b, p, nrm, 0.0105f, M(part, Gunmetal));
                        if (side > 0 && Hash(p) < 0.4f) RustStreak(b, part, p, nrm, 0.04f + 0.04f * Hash(p, 1), 0.026f);
                    }
                }
                foreach (var t in new[] { 0.36f, 0.64f })   // the limb is three bolted plates
                {
                    var sz = S(t);
                    var c = new Vector3(cx + side * (sz.x / 2 + 0.0006f), Y(t), 0);
                    SeamLine(b, part, c + new Vector3(0, 0, -sz.y / 2 + 0.014f), c + new Vector3(0, 0, sz.y / 2 - 0.014f), nrm, 0.006f);
                }
            }
        }

        protected override void BuildGeometry()
        {
            {
                var b = new Batch();   // docking coupler: a wide dark plate under a tapered octagonal plinth, amber slot, red panel
                b.Loft(Vector3.zero, new Vector3(0, -0.026f, 0), new Vector2(0.46f, 0.4f), new Vector2(0.46f, 0.4f), 0.08f, 0.08f, M(Cpl, Gunmetal));
                b.Loft(new Vector3(0, -0.026f, 0), new Vector3(0, -0.1f, 0), new Vector2(0.4f, 0.34f), new Vector2(0.32f, 0.27f), 0.07f, 0.06f, M(Cpl, ToyYellow));
                b.Loft(new Vector3(0, -0.1f, 0), new Vector3(0, -0.112f, 0), new Vector2(0.3f, 0.25f), new Vector2(0.26f, 0.22f), 0.05f, 0.05f, M(Cpl, ToyYellowDeep));
                foreach (var x in new[] { -0.13f, 0.13f })
                    foreach (var z in new[] { -0.1f, 0.1f })
                        Bolt(b, new Vector3(x, -0.1f, z), Vector3.down, 0.012f, M(Cpl, Gunmetal));
                foreach (var x in new[] { -0.14f, 0.14f })
                    b.Cylinder(new Vector3(x, 0.012f, 0.09f), 0.016f, 0.03f, M(Cpl, Dark));   // locking pins
                b.Box(new Vector3(0, -0.062f, -0.158f), new Vector3(0.15f, 0.034f, 0.01f), M(Cpl, Gunmetal), false, Batch.R(Vector3.right, -0.32f));
                b.Box(new Vector3(0, -0.062f, -0.162f), new Vector3(0.12f, 0.016f, 0.006f), M(Cpl, Glow), false, Batch.R(Vector3.right, -0.32f));   // amber slot
                b.Box(new Vector3(0.183f, -0.062f, 0), new Vector3(0.008f, 0.04f, 0.14f), M(Cpl, ToyRed), false, Batch.R(Vector3.forward, 0.38f));     // red panel
                for (int k = 0; k < 3; k++) b.Box(new Vector3(-0.03f + k * 0.03f, -0.06f, 0.155f), new Vector3(0.012f, 0.012f, 0.006f), M(Cpl, Brass));
                Done(b, body);
            }
            if (Has(Cpl)) beacons.Add(Prim(PrimitiveType.Sphere, body, new Vector3(0.12f, -0.07f, -0.15f), Vector3.one * 0.024f, Quaternion.identity, BeaconOffMat));
            {
                // turret (shoulder pivot; the upper arm's axis is 0.36 out): flange, a tall chamfered
                // column, dark collar rings, the big bolted red cap; hoses curl from the cap
                var b = new Batch();
                float axisY = -(ShoulderDrop - 0.06f);
                b.Lathe(Vector3.zero, Disc(0.14f, 0.02f, 0.004f), M(Tur, Gunmetal), DownRot, 32);
                b.Loft(new Vector3(0, -0.02f, 0), new Vector3(0, -0.27f, 0), new Vector2(0.23f, 0.23f), new Vector2(0.19f, 0.19f), 0.06f, 0.05f, M(Tur, ToyYellow));
                foreach (var n in new[] { Vector3.left, new Vector3(0, 0, -1), new Vector3(0, 0, 1) })
                    foreach (var y in new[] { -0.05f, -0.145f, -0.24f })
                        Bolt(b, new Vector3(0, y, 0) + n * (Mathf.Lerp(0.115f, 0.095f, (-y - 0.02f) / 0.25f) + 0.001f), n, 0.01f, M(Tur, Gunmetal));
                foreach (var n in new[] { Vector3.left, new Vector3(0, 0, -1), new Vector3(0, 0, 1) })   // the column is two bolted plates
                    SeamLine(b, Tur, new Vector3(0, -0.145f, 0) + n * 0.106f + Vector3.Cross(n, Vector3.up) * 0.07f, new Vector3(0, -0.145f, 0) + n * 0.106f - Vector3.Cross(n, Vector3.up) * 0.07f, n, 0.006f);
                b.Lathe(new Vector3(0, -0.27f, 0), Disc(0.105f, 0.014f, 0.003f), M(Tur, Gunmetal), DownRot, 28);
                b.Lathe(new Vector3(0, -0.284f, 0), Disc(0.095f, 0.012f, 0.003f), M(Tur, Brass), DownRot, 28);
                b.Lathe(new Vector3(0, -0.296f, 0), Disc(0.11f, 0.014f, 0.003f), M(Tur, Gunmetal), DownRot, 28);
                b.Lathe(new Vector3(0, -0.31f, 0), Disc(0.135f, 0.1f, 0.018f), M(Tur, ToyRed), DownRot, 40);
                b.Lathe(new Vector3(0, -0.41f, 0), Dome(0.11f, 0.025f, 6), M(Tur, ToyRed), DownRot, 36);
                for (int k = 0; k < 12; k++)
                {
                    float a = k * Mathf.PI / 6 + 0.13f;
                    var dir = new Vector3(Mathf.Sin(a), 0, Mathf.Cos(a));
                    Bolt(b, dir * 0.135f + new Vector3(0, -0.325f, 0), dir, 0.01f, M(Tur, Gunmetal));
                    if (Hash(dir, 6) < 0.4f) RustStreak(b, Tur, dir * 0.136f + new Vector3(0, -0.33f, 0), dir, 0.04f + 0.03f * Hash(dir, 7), 0.022f);
                }
                b.RoundedBox(new Vector3(0, -0.365f, -0.134f), new Vector3(0.05f, 0.022f, 0.008f), 0.004f, M(Tur, Glow), 1);
                // control unit on the back of the column
                var cc = new Vector3(-0.15f, -0.13f, 0);
                var cs = new Vector3(0.08f, 0.15f, 0.15f);
                b.RoundedBox(cc, cs, 0.014f, M(Ctl, ToyYellow), 3);
                PanelFrame(b, Ctl, cc, cs, Vector3.left, 0.018f, 0.009f, 0.1f, 0.005f, 0.05f);
                foreach (var y in new[] { -0.1f, -0.15f }) b.RoundedBox(new Vector3(-0.17f, y, -0.076f), new Vector3(0.025f, 0.012f, 0.005f), 0.003f, M(Ctl, Glow), 1);
                // black hoses curling from the cap down the column
                foreach (var (z, r) in new[] { (0.07f, 0.014f), (-0.05f, 0.012f) })
                    Pipe(b, Cab, new[] { new Vector3(-0.1f, -0.37f, z), new Vector3(-0.19f, -0.33f, z * 1.2f), new Vector3(-0.2f, -0.24f, z * 1.3f), new Vector3(-0.13f, -0.1f, z), new Vector3(-0.1f, -0.04f, z * 0.8f) }, r, Rubber, Brass);
                Done(b, _shoulder);
            }
            {
                // upper arm: the biggest limb, beside the turret, tapering towards the elbow
                var b = new Batch();
                b.Lathe(new Vector3(-0.02f, 0, 0), Disc(0.06f, UX + 0.1f, 0.006f), M(Up, Gunmetal), RightRot, 24);   // shoulder axle through the cap
                TaperedLimb(b, Up, UX, 0, -L1, new Vector2(0.17f, 0.24f), new Vector2(0.13f, 0.16f), 0.12f, 0.08f);
                // shoulder: a dark bearing ring carrying the family bearing hub on the outside
                b.Lathe(new Vector3(UX + 0.085f, 0, 0), Disc(0.088f, 0.026f, 0.006f), M(Up, Gunmetal), SideRot(1), 32);
                Hub(b, Up, new Vector3(UX + 0.111f, 0, 0), 1, 0.074f, 8, 0.009f);
                GreaseRun(b, Up, new Vector3(UX + 0.0805f, -0.09f, -0.04f), X(1), 0.1f, 0.02f);
                Stencil(b, Up, new Vector3(UX + 0.0795f, -0.22f, 0.055f), Vector3.down, new Vector3(0, 0, -1), Vector3.right, 0.11f, "A-01");
                // motor cartridge on the inside, and the harness from the cap into the limb
                b.Lathe(new Vector3(UX - 0.115f, -0.12f, 0.02f), Disc(0.036f, 0.17f, 0.006f), M(Mot, Gunmetal), DownRot, 20);
                foreach (var y in new[] { -0.15f, -0.19f, -0.23f }) b.Lathe(new Vector3(UX - 0.115f, y, 0.02f), Disc(0.04f, 0.01f, 0.003f), M(Mot, Brass), DownRot, 20);
                Pipe(b, Cab, new[] { new Vector3(0.1f, 0.06f, 0.09f), new Vector3(0.12f, 0.01f, 0.15f), new Vector3(UX - 0.03f, -0.14f, 0.12f), new Vector3(UX - 0.02f, -0.45f, 0.085f), new Vector3(UX - 0.02f, -0.74f, 0.07f) }, 0.014f, Rubber, Brass);
                // a second, thinner line beside it and a bundle down the back face, held by clamps
                Pipe(b, Cab, new[] { new Vector3(0.08f, 0.05f, 0.1f), new Vector3(0.1f, 0f, 0.155f), new Vector3(UX - 0.055f, -0.15f, 0.13f), new Vector3(UX - 0.05f, -0.45f, 0.095f), new Vector3(UX - 0.045f, -0.72f, 0.075f) }, 0.009f, Rubber, Brass);
                foreach (var zs in new[] { -0.012f, 0.012f })
                    Pipe(b, Cab, new[] { new Vector3(UX + zs, -0.1f, -0.135f), new Vector3(UX + zs, -0.4f, -0.112f), new Vector3(UX + zs, -0.72f, -0.09f) }, 0.008f, Rubber, Brass);
                foreach (var (y, z) in new[] { (-0.3f, 0.108f), (-0.58f, 0.088f) })
                    b.ChamferBox(new Vector3(UX - 0.035f, y, z), new Vector3(0.06f, 0.022f, 0.03f), 0.004f, M(Cab, Gunmetal));
                foreach (var (y, z) in new[] { (-0.25f, -0.128f), (-0.55f, -0.106f) })
                    b.ChamferBox(new Vector3(UX, y, z), new Vector3(0.05f, 0.02f, 0.024f), 0.004f, M(Cab, Gunmetal));
                Done(b, _upper);
            }
            {
                // elbow: the big red outer cap; forearm slimmer, inside the upper arm, to a dark
                // rotary ring and a short wrist segment
                var b = new Batch();
                b.Lathe(new Vector3(FX - 0.06f, 0, 0), Disc(0.05f, UX - FX + 0.13f, 0.006f), M(Elb, Gunmetal), RightRot, 24);
                b.Lathe(new Vector3(UX + 0.062f, 0, 0), Disc(0.105f, 0.08f, 0.018f), M(Elb, ToyRed), SideRot(1), 40);
                Hub(b, Elb, new Vector3(UX + 0.142f, 0, 0), 1, 0.062f, 8, 0.008f);
                for (int k = 0; k < 8; k++)   // the cap's bolt ring
                {
                    float a = k * Mathf.PI / 4 + Mathf.PI / 8;
                    Bolt(b, new Vector3(UX + 0.142f, Mathf.Sin(a) * 0.088f, Mathf.Cos(a) * 0.088f), Vector3.right, 0.008f, M(Elb, Gunmetal));
                }
                Gear(b, Elb, new Vector3(FX - 0.075f, 0, 0), 0.058f, 0.022f, 14);   // the elbow drive, on the inside
                GreaseRun(b, Fore, new Vector3(FX + 0.061f, -0.09f, 0.03f), X(1), 0.09f, 0.016f);
                TaperedLimb(b, Fore, FX, 0, -L2 + 0.13f, new Vector2(0.12f, 0.155f), new Vector2(0.088f, 0.1f), 0.08f, 0.052f);
                b.Lathe(new Vector3(FX, -L2 + 0.13f, 0), Disc(0.058f, 0.035f, 0.006f), M(Fore, Gunmetal), DownRot, 28);    // rotary ring
                b.Lathe(new Vector3(FX, -L2 + 0.145f, 0), Disc(0.062f, 0.008f, 0.002f), M(Fore, Brass), DownRot, 28);
                b.Loft(new Vector3(FX, -L2 + 0.095f, 0), new Vector3(FX, -L2 + 0.035f, 0), new Vector2(0.08f, 0.085f), new Vector2(0.075f, 0.08f), 0.016f, 0.016f, M(Fore, ToyYellow));   // short wrist segment
                Pipe(b, Cab, new[] { new Vector3(FX + 0.01f, -0.06f, 0.068f), new Vector3(FX + 0.015f, -0.35f, 0.07f), new Vector3(FX + 0.01f, -0.6f, 0.058f), new Vector3(FX, -0.66f, 0.04f) }, 0.012f, Rubber, Brass);
                Done(b, _elbow);
            }
            {
                // gripper head (wrist pivot): knuckle bridging to the centre line, a dark housing with
                // bolts and the amber sensor slot
                var b = new Batch();
                b.Lathe(new Vector3(-0.05f, 0, 0), Disc(0.042f, FX + 0.09f, 0.006f), M(Wr, Gunmetal), RightRot, 24);
                foreach (var sd in new[] { -1f, 1f }) b.Lathe(new Vector3(sd < 0 ? -0.05f : FX + 0.04f, 0, 0), Dome(0.03f, 0.014f), M(Wr, ToyRed), SideRot(sd), 18);
                b.Loft(new Vector3(0, -0.02f, 0), new Vector3(0, -0.1f, 0), new Vector2(0.11f, 0.15f), new Vector2(0.12f, 0.19f), 0.02f, 0.024f, M(Wr, Gunmetal));
                foreach (var z in new[] { -0.06f, 0.06f }) Bolt(b, new Vector3(0.059f, -0.06f, z), Vector3.right, 0.008f, M(Wr, Gunmetal));
                b.RoundedBox(new Vector3(0, -0.06f, -0.088f), new Vector3(0.07f, 0.034f, 0.016f), 0.006f, M(Sens, ToyYellow), 2);
                Done(b, _wrist);
            }
            if (Has(Sens)) eyes.Add(Separate(_wrist, "G_sensor", lb => lb.RoundedBox(new Vector3(0, -0.06f, -0.097f), new Vector3(0.05f, 0.016f, 0.006f), 0.004f, EyeOffMat, 1)));
            foreach (var (pivot, part, fs) in new[] { (_fingerL, "left finger", -1f), (_fingerR, "right finger", 1f) })
            {
                // the family gripper finger, at the arm's size: faceted red segments bowing out and hooking
                // in, a flush matte grip strip on the inside, bright steel hinge hubs with brass caps
                var b = new Batch();
                const float F = 1.9f;
                var path = new[] { new Vector2(0.034f, -0.044f), new Vector2(0.074f, -0.078f), new Vector2(0.094f, -0.128f), new Vector2(0.08f, -0.174f), new Vector2(0.04f, -0.2f) };
                float[] wd = { 0.044f, 0.04f, 0.035f, 0.028f, 0.019f }, dp = { 0.056f, 0.054f, 0.049f, 0.043f, 0.036f };
                Vector3 V(Vector2 p) => new Vector3(fs * (p.x - path[0].x), p.y - path[0].y, 0) * F;   // relative to the root hinge
                void HingeHub(Vector3 p, float r)
                {
                    b.Lathe(p + new Vector3(0, 0, -0.033f * F), Disc(r * F, 0.066f * F, 0.003f), M(part, BrightSteel), BackRot, 18);
                    foreach (var zs in new[] { -1f, 1f })
                        b.Lathe(p + new Vector3(0, 0, zs * 0.033f * F), Dome(r * 0.45f * F, 0.006f * F), M(part, Brass), zs < 0 ? FwdRot : BackRot, 10);
                }
                for (int i = 0; i < path.Length - 1; i++)
                {
                    var d = (path[i + 1] - path[i]).normalized;
                    var a0 = i >= 2 ? path[i] - d * (wd[i] * 0.35f) : path[i];
                    var a1 = i >= 1 && i < path.Length - 2 ? path[i + 1] + d * (wd[i + 1] * 0.35f) : path[i + 1];
                    b.Loft(V(a0), V(a1), new Vector2(wd[i], dp[i]) * F, new Vector2(wd[i + 1], dp[i + 1]) * F, 0.011f * F, (i == path.Length - 2 ? 0.006f : 0.01f) * F, M(part, ToyRed), Vector3.right);
                }
                for (int i = 1; i < path.Length - 1; i++)
                {
                    var a = new Vector2(fs * path[i].x, path[i].y); var c = new Vector2(fs * path[i + 1].x, path[i + 1].y);
                    var dir = (c - a).normalized;
                    var inward = new Vector2(dir.y, -dir.x);
                    if (Vector2.Dot(inward, new Vector2(-fs, -0.3f)) < 0) inward = -inward;
                    var a2 = a + inward * (wd[i] / 2 + 0.0008f) + (i == 1 ? dir * 0.01f : Vector2.zero);
                    var c2 = c + inward * (wd[i + 1] / 2 + 0.0008f) - (i == path.Length - 2 ? dir * 0.008f : Vector2.zero);
                    Vector3 W(Vector2 q) => new Vector3(q.x - fs * path[0].x, q.y - path[0].y, 0) * F;
                    b.Loft(W(a2), W(c2), new Vector2(0.0035f, dp[i] * 0.62f) * F, new Vector2(0.0035f, dp[i + 1] * 0.62f) * F, 0.0008f, 0.0008f, M(part, Rubber), Vector3.right);
                }
                HingeHub(V(path[0]), 0.018f);
                HingeHub(V(path[1]), 0.019f);
                GreaseRun(b, part, V(path[1]) + new Vector3(fs * 0.02f, -0.02f, -0.028f * F), new Vector3(0, 0, -1), 0.05f, 0.012f);
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

        /// <summary>Mounted rest: upper arm raised out over the aisle, forearm hanging straight down
        /// in front of the turret, gripper down at shoulder height. The forearm stays off the turret
        /// axis, where the overhead mount's drop tube comes down.</summary>
        public void Park(float yawDeg)
        {
            Reach(ParkWrist(yawDeg), body.TransformDirection(Vector3.down));
            Grip(0.6f);
        }

        const float ParkOut = 0.3f;

        /// <summary>The parked wrist (world): level with the shoulder, ParkOut out of the cabinet
        /// (turned by yawDeg about the turret).</summary>
        public Vector3 ParkWrist(float yawDeg) =>
            body.TransformPoint(new Vector3(0, -ShoulderDrop, 0) + Quaternion.Euler(0, yawDeg, 0) * Vector3.forward * ParkOut);

        /// <summary>QA: the smallest gap (m) between the limbs in the current pose and the overhead
        /// mount: the drop tube (r 0.045) on the turret axis from the coupler (`root`) up to the
        /// beam (`beamY`), and the beam's underside. Limbs are their bounding boxes in their own
        /// pivot frames (joint caps included).</summary>
        public float MountClearance(Vector3 root, float beamY)
        {
            // (pivot, centre, half extents) in the pivot's local frame; limbs run along -Y
            var limbs = new (Transform t, Vector3 c, Vector3 h)[]
            {
                (_upper, new Vector3(UX, -L1 / 2, 0), new Vector3(0.09f, L1 / 2 + 0.12f, 0.12f)),                       // upper arm and its rounded ends
                (_upper, new Vector3(UX + 0.1f, 0, 0), new Vector3(0.04f, 0.09f, 0.09f)),                              // shoulder bearing hub
                (_elbow, new Vector3(FX, -(L2 - 0.13f) / 2, 0), new Vector3(0.065f, (L2 - 0.13f) / 2 + 0.08f, 0.08f)),  // forearm
                (_elbow, new Vector3((FX - 0.08f + UX + 0.15f) / 2, 0, 0), new Vector3((UX + 0.23f - FX) / 2, 0.105f, 0.105f)),   // elbow axle and red cap
                (_wrist, new Vector3(0, -0.16f, 0), new Vector3(0.1f, 0.2f, 0.1f)),                                    // gripper head and fingers
            };
            float best = float.MaxValue;
            for (int i = 0; i <= 40; i++)
            {
                var p = new Vector3(root.x, Mathf.Lerp(root.y + 0.04f, beamY, i / 40f), root.z);
                foreach (var (t, c, h) in limbs)
                {
                    var q = t.InverseTransformPoint(p) - c;
                    var o = new Vector3(Mathf.Max(Mathf.Abs(q.x) - h.x, 0), Mathf.Max(Mathf.Abs(q.y) - h.y, 0), Mathf.Max(Mathf.Abs(q.z) - h.z, 0));
                    best = Mathf.Min(best, o.magnitude - 0.045f);
                }
            }
            foreach (var (t, c, h) in limbs)   // the beam's underside above every box corner
                for (int k = 0; k < 8; k++)
                    best = Mathf.Min(best, beamY - t.TransformPoint(c + Vector3.Scale(h, new Vector3((k & 1) * 2 - 1, (k & 2) - 1, (k & 4) / 2f - 1))).y);
            return best;
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
            _fingerL.localPosition = new Vector3(0, -0.1f, x);
            _fingerR.localPosition = new Vector3(0, -0.1f, -x);
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
