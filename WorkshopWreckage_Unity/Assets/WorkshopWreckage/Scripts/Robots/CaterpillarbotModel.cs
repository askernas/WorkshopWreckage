using UnityEngine;

namespace WorkshopWreckage
{
    /// <summary>Caterpillarbot after resources/catbot.png: a long, low, weathered
    /// industrial-yellow tracked hauler. Rubber track loops with moving cleats, big red
    /// bolted drive sprockets at both ends, brass road wheels below a yellow side skirt
    /// with a slot and rivets; a wide front block with a slotted plate under the head; the
    /// family head (one bezel eye, mouth grille, red ears, antennas, red siren); two
    /// articulated arms with red claws reaching forward; three open riveted cargo boxes
    /// lengthwise behind the head (brass trims, red slot handles). Treads run with its
    /// speed and counter-rotate when it turns on the spot; parts ride in the boxes.
    /// Parts: head, optical eye, antenna pair, siren light, chassis, left/right track,
    /// left/right drive sprocket, road wheel set A/B, front/middle/rear cargo box,
    /// left/right arm, left/right claw, drive motor, battery pack.</summary>
    public class CaterpillarbotModel : RobotModel
    {
        Transform _treadL, _treadR, _sprocketLF, _sprocketLR, _sprocketRF, _sprocketRR, _armL, _armR, _chassis;
        readonly Transform[] _boxes = new Transform[3];
        float _offL, _offR;
        public override float ModelHeight => 0.9f;
        const float ArmRest = -62f;
        const float TrackX = 0.25f, TrackW = 0.17f, EndZ = 0.47f, EndR = 0.145f, EndY = 0.145f, Cleat = 0.06f, SprocketR = 0.118f;
        static readonly float[] BoxZ = { -0.07f, 0.2f, 0.47f };   // Godot z (front = -z)
        static readonly string[] BoxPart = { "front cargo box", "middle cargo box", "rear cargo box" };

        protected override void CreatePivots()
        {
            _chassis = Pivot("Chassis", body, Vector3.zero);
            _treadL = Pivot("TreadL", body, Vector3.zero);
            _treadR = Pivot("TreadR", body, Vector3.zero);
            _sprocketLF = Pivot("SprLF", body, new Vector3(-TrackX - TrackW / 2, EndY, EndZ));
            _sprocketLR = Pivot("SprLR", body, new Vector3(-TrackX - TrackW / 2, EndY, -EndZ));
            _sprocketRF = Pivot("SprRF", body, new Vector3(TrackX + TrackW / 2, EndY, EndZ));
            _sprocketRR = Pivot("SprRR", body, new Vector3(TrackX + TrackW / 2, EndY, -EndZ));
            head = Pivot("Head", _chassis, new Vector3(0, 0.49f, 0.38f));
            // arms are mounted at the front corners and reach forward, as in the reference
            _armL = Pivot("ArmL", _chassis, new Vector3(-0.27f, 0.43f, 0.55f));
            _armR = Pivot("ArmR", _chassis, new Vector3(0.27f, 0.43f, 0.55f));
            _armL.localRotation = _armR.localRotation = Quaternion.Euler(ArmRest, 0, 0);
            for (int i = 0; i < 3; i++) _boxes[i] = Pivot("Cargo" + i, _chassis, new Vector3(0, 0.335f, -BoxZ[i]));
            carry = _boxes[0];
            Mount(head, "head", Vector3.up, 0.05f);
            Mount(_armL, "left arm", Vector3.up, 0.035f);
            Mount(_armR, "right arm", Vector3.up, 0.035f);
            Mount(_sprocketLF, "left drive sprocket", Vector3.left, 0.04f);
            Mount(_sprocketLR, "left drive sprocket", Vector3.left, 0.04f);
            Mount(_sprocketRF, "right drive sprocket", Vector3.right, 0.04f);
            Mount(_sprocketRR, "right drive sprocket", Vector3.right, 0.04f);
            for (int i = 0; i < 3; i++) Mount(_boxes[i], BoxPart[i], Vector3.up, 0.05f);
        }

        protected override Vector3? ClampPoint => Has("left track") && Has("right track") ? null : new Vector3(0, 0.26f, 0.64f);

        /// <summary>Without the chassis: the bare ladder frame.</summary>
        protected override void BuildFrame()
        {
            if (Has("chassis")) return;
            var b = new Batch();
            foreach (var x in new[] { -0.15f, 0.15f }) b.RoundedBox(new Vector3(x, 0.2f, 0), new Vector3(0.04f, 0.05f, EndZ * 2 + 0.1f), 0.008f, Gunmetal, 2);
            foreach (var z in new[] { -EndZ, -0.2f, 0.1f, EndZ }) b.RoundedBox(new Vector3(0, 0.2f, z), new Vector3(0.34f, 0.035f, 0.035f), 0.008f, Gunmetal, 2);
            b.RoundedBox(new Vector3(0, 0.25f, 0.25f), new Vector3(0.14f, 0.07f, 0.12f), 0.01f, Brass, 2);   // the gearbox
            b.Build(_chassis, "G_frame", true);
        }

        protected override void BuildGeometry()
        {
            foreach (var (side, track, sprocket) in new[] { (-1f, "left track", "left drive sprocket"), (1f, "right track", "right drive sprocket") })
            {
                float x = side * TrackX;
                var b = new Batch();
                // rubber loop: top and bottom runs, rounded ends
                b.RoundedBox(new Vector3(x, EndY + EndR - 0.0125f, 0), new Vector3(TrackW, 0.025f, EndZ * 2), 0.008f, M(track, Rubber), 1);
                b.RoundedBox(new Vector3(x, 0.0125f, 0), new Vector3(TrackW, 0.025f, EndZ * 2), 0.008f, M(track, Rubber), 1);
                foreach (var z in new[] { -EndZ, EndZ })
                    b.Lathe(new Vector3(x - TrackW / 2, EndY, z), new[] { new Vector2(EndR - 0.025f, 0), new Vector2(EndR, 0.006f), new Vector2(EndR, TrackW - 0.006f), new Vector2(EndR - 0.025f, TrackW) }, M(track, Rubber), RightRot, 24);
                for (int k = -8; k <= 8; k++)   // bottom-run cleats rest on the ground
                    b.RoundedBox(new Vector3(x, -0.002f, k * Cleat), new Vector3(TrackW + 0.004f, 0.012f, 0.02f), 0.004f, M(track, Gunmetal), 1);
                // road wheels and the skirt
                for (int k = 0; k < 5; k++)
                {
                    float z = -0.3f + k * 0.15f;
                    string set = k < 3 ? "road wheel set a" : "road wheel set b";
                    b.Lathe(new Vector3(side * (TrackX + TrackW / 2 + 0.002f), 0.065f, z), Disc(0.05f, 0.016f, 0.004f), M(set, Brass), SideRot(side), 18);
                    b.Lathe(new Vector3(side * (TrackX + TrackW / 2 + 0.018f), 0.065f, z), Dome(0.02f, 0.01f), M(set, Gunmetal), SideRot(side), 12);
                }
                b.RoundedBox(new Vector3(side * (TrackX + TrackW / 2 + 0.008f), 0.18f, 0), new Vector3(0.016f, 0.13f, 0.62f), 0.006f, M("chassis", ToyYellow), 2);
                b.RoundedBox(new Vector3(side * (TrackX + TrackW / 2 + 0.017f), 0.18f, 0), new Vector3(0.006f, 0.02f, 0.16f), 0.003f, M("chassis", Dark), 1);
                foreach (var z in new[] { -0.28f, -0.1f, 0.1f, 0.28f })
                    foreach (var y in new[] { 0.13f, 0.225f })
                        b.Rivet(new Vector3(side * (TrackX + TrackW / 2 + 0.016f), y, z), new Vector3(side, 0, 0), 0.007f, M("chassis", Gunmetal));
                Done(b, body);
                // top-run cleats: their pivot slides to make the tread move
                var c = new Batch();
                for (int k = -8; k <= 8; k++)
                    c.RoundedBox(new Vector3(x, EndY + EndR + 0.004f, k * Cleat), new Vector3(TrackW + 0.004f, 0.012f, 0.02f), 0.004f, M(track, Gunmetal), 1);
                Done(c, side < 0 ? _treadL : _treadR);
                foreach (var spr in side < 0 ? new[] { _sprocketLF, _sprocketLR } : new[] { _sprocketRF, _sprocketRR })
                {
                    var s = new Batch();
                    s.Lathe(Vector3.zero, Disc(SprocketR, 0.025f, 0.006f), M(sprocket, ToyRed), SideRot(side), 28);
                    s.Lathe(new Vector3(side * 0.025f, 0, 0), Disc(0.04f, 0.012f, 0.003f), M(sprocket, Gunmetal), SideRot(side), 18);
                    s.Lathe(new Vector3(side * 0.037f, 0, 0), Dome(0.022f, 0.012f), M(sprocket, Brass), SideRot(side), 14);
                    for (int k = 0; k < 6; k++)
                    {
                        float a = k * Mathf.PI / 3;
                        s.Rivet(new Vector3(side * 0.025f, Mathf.Sin(a) * 0.065f, Mathf.Cos(a) * 0.065f), new Vector3(side, 0, 0), 0.008f, M(sprocket, Gunmetal));
                    }
                    Done(s, spr);
                }
            }
            {
                var b = new Batch();
                // chassis deck between the tracks, front block under the head, motor and battery
                b.RoundedBox(new Vector3(0, 0.2f, 0), new Vector3(0.33f, 0.26f, 1.0f), 0.03f, M("chassis", ToyYellow), 2);
                b.RoundedBox(new Vector3(0, 0.322f, 0.02f), new Vector3(0.68f, 0.025f, 0.96f), 0.008f, M("chassis", ToyYellowDeep), 2);
                // the front block: as wide as the head, the arms on its corners (the sheet)
                b.RoundedBox(new Vector3(0, 0.41f, -0.39f), new Vector3(0.48f, 0.16f, 0.36f), 0.03f, M("chassis", ToyYellow), 3);
                b.RoundedBox(new Vector3(0, 0.41f, -0.573f), new Vector3(0.24f, 0.07f, 0.014f), 0.006f, M("chassis", ToyYellowDeep), 2);
                b.RoundedBox(new Vector3(0, 0.41f, -0.581f), new Vector3(0.12f, 0.018f, 0.006f), 0.003f, M("chassis", ToyRed), 1);
                b.RoundedBox(new Vector3(0, 0.41f, -0.584f), new Vector3(0.1f, 0.008f, 0.004f), 0.002f, M("chassis", Dark), 1);
                foreach (var x in new[] { -0.21f, -0.07f, 0.07f, 0.21f })
                    foreach (var y in new[] { 0.35f, 0.47f })
                        b.Rivet(new Vector3(x, y, -0.57f), new Vector3(0, 0, -1), 0.0075f, M("chassis", Gunmetal));
                Grille(b, "chassis", new Vector3(0, 0.2f, -0.501f), 0.16f, 0.08f, 5, false, -1);
                b.RoundedBox(new Vector3(0, 0.2f, 0.505f), new Vector3(0.16f, 0.1f, 0.02f), 0.008f, M("battery pack", Gunmetal), 2);
                b.Lathe(new Vector3(-0.08f, 0.26f, 0.38f), Disc(0.045f, 0.16f, 0.008f), M("drive motor", Gunmetal), RightRot, 16);
                b.Lathe(new Vector3(-0.27f, 0.43f, -0.55f), Disc(0.026f, 0.54f, 0.005f), M("chassis", Gunmetal), RightRot, 12);   // shoulder axle across the front
                for (int k = 0; k < 3; k++) b.Lathe(new Vector3(0, 0.488f + k * 0.006f, -0.38f), Disc(0.07f - k * 0.006f, 0.006f, 0.002f), M("chassis", Gunmetal), null, 16);
                Done(b, _chassis);
            }
            for (int i = 0; i < 3; i++)
            {
                var b = new Batch();
                string P = BoxPart[i];
                float w = 0.5f, d = 0.25f, h = 0.22f, t = 0.016f;
                b.RoundedBox(new Vector3(0, 0.01f, 0), new Vector3(w, 0.02f, d), 0.005f, M(P, ToyYellowDeep), 1);
                foreach (var s in new[] { -1f, 1f })
                {
                    b.RoundedBox(new Vector3(s * (w / 2 - t / 2), h / 2, 0), new Vector3(t, h, d), 0.006f, M(P, ToyYellow), 2);
                    b.RoundedBox(new Vector3(0, h / 2, s * (d / 2 - t / 2)), new Vector3(w, h, t), 0.006f, M(P, ToyYellow), 2);
                    // red slot handle on the outer walls, brass rim trims
                    b.RoundedBox(new Vector3(s * (w / 2 + 0.002f), h * 0.62f, 0), new Vector3(0.006f, 0.035f, 0.1f), 0.004f, M(P, ToyRed), 1);
                    b.RoundedBox(new Vector3(s * (w / 2 + 0.004f), h * 0.62f, 0), new Vector3(0.004f, 0.016f, 0.075f), 0.002f, M(P, Dark), 1);
                    b.RoundedBox(new Vector3(s * (w / 2 - t / 2), h + 0.006f, 0), new Vector3(t + 0.008f, 0.012f, d + 0.008f), 0.004f, M(P, Brass), 1);
                    b.RoundedBox(new Vector3(0, h + 0.006f, s * (d / 2 - t / 2)), new Vector3(w + 0.008f, 0.012f, t + 0.008f), 0.004f, M(P, Brass), 1);
                }
                foreach (var x in new[] { -1f, 1f })
                    foreach (var z in new[] { -1f, 1f })
                    {
                        b.RoundedBox(new Vector3(x * (w / 2 - 0.004f), h / 2, z * (d / 2 - 0.004f)), new Vector3(0.022f, h + 0.01f, 0.022f), 0.006f, M(P, Brass), 1);   // corner posts
                        foreach (var y in new[] { 0.05f, h - 0.05f }) b.Rivet(new Vector3(x * (w / 2 + 0.001f), y, z * (d / 2 - 0.035f)), new Vector3(x, 0, 0), 0.006f, M(P, Gunmetal));
                    }
                Done(b, _boxes[i]);
            }
            foreach (var (arm, s, part, claw) in new[] { (_armL, -1f, "left arm", "left claw"), (_armR, 1f, "right arm", "right claw") })
            {
                var b = new Batch();
                DetailedArm(b, s, part, claw, ToyYellow, ToyYellowDeep, ToyRed, 0.95f);
                Done(b, arm);
            }
            DetailedHead(head, ToyYellow, new Vector3(0.42f, 0.28f, 0.34f), 1, 0.075f,
                "head", "optical eye", null, "antenna pair", "antenna pair", "head", "head", "siren light", false, true);
        }

        public override (Transform parent, Vector3 local) CarrySlot(int i, int n)
        {
            int box = i % 3, k = i / 3;
            float x = ((k % 3) - 1) * 0.11f, z = ((k / 3) % 2 - 0.5f) * 0.09f, y = 0.05f + (k / 6) * 0.05f;
            return (_boxes[box], new Vector3(x, y, z));
        }

        public override void Animate(RobotAnim a)
        {
            float s = Mathf.Max(1e-4f, transform.lossyScale.x);
            // treads: forward speed plus the difference for turning on the spot
            float turn = a.turnRate * Mathf.Deg2Rad * TrackX * s;   // turning right: left tread forward, right tread back
            float vl = (a.speed + turn) / s, vr = (a.speed - turn) / s;
            _offL = Mathf.Repeat(_offL + vl * a.dt, Cleat);
            _offR = Mathf.Repeat(_offR + vr * a.dt, Cleat);
            _treadL.localPosition = new Vector3(0, 0, _offL);   // top run moves forward relative to the body
            _treadR.localPosition = new Vector3(0, 0, _offR);
            float rotL = vl * a.dt / SprocketR * Mathf.Rad2Deg, rotR = vr * a.dt / SprocketR * Mathf.Rad2Deg;
            _sprocketLF.localRotation *= Quaternion.Euler(rotL, 0, 0); _sprocketLR.localRotation *= Quaternion.Euler(rotL, 0, 0);
            _sprocketRF.localRotation *= Quaternion.Euler(rotR, 0, 0); _sprocketRR.localRotation *= Quaternion.Euler(rotR, 0, 0);
            // a slow, heavy rumble
            _chassis.localPosition = new Vector3(0, Mathf.Sin(a.time * 23f) * 0.0025f * Mathf.Clamp01(a.speed01 + Mathf.Abs(a.turnRate) / 90f), 0);
            float pitch = ArmRest - 15 * a.armReach - 25 * a.armLift + 10 * a.armHold;
            float sway = Mathf.Sin(a.time * 1.3f) * 4;
            _armL.localRotation = Quaternion.Euler(pitch + sway, 0, 6);
            _armR.localRotation = Quaternion.Euler(pitch - sway, 0, -6);
            LookHead(a, 50);
            if (a.gesture > 0)
            {
                float w = Mathf.Sin(a.gesture * Mathf.PI);
                head.localRotation *= Quaternion.Euler(Mathf.Sin(a.gesture * Mathf.PI * 4) * 14 * w, 0, 0);   // a slow, dependable nod
                SetBeacon(Mathf.Repeat(a.gesture * 6, 1) < 0.5f);
            }
        }
    }
}
