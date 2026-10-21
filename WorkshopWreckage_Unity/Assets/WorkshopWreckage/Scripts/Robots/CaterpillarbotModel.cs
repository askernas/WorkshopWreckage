using System.Collections.Generic;
using UnityEngine;

namespace WorkshopWreckage
{
    /// <summary>Caterpillarbot after resources/catbot.png: long, low, weathered industrial
    /// yellow tracked hauler with a one-eyed head and red siren at the front, two
    /// articulated claw arms, and three open cargo boxes lengthwise behind the head (no
    /// tall stack). Treads run with its speed and counter-rotate when it turns on the
    /// spot; carried parts ride in the cargo boxes. Parts: head, optical eye, antenna
    /// pair, siren light, chassis, left/right track, left/right drive sprocket, road
    /// wheel set A/B, front/middle/rear cargo box, left/right arm, left/right claw,
    /// drive motor, battery pack.</summary>
    public class CaterpillarbotModel : RobotModel
    {
        Transform _treadL, _treadR, _sprocketLF, _sprocketLR, _sprocketRF, _sprocketRR, _armL, _armR, _chassis;
        readonly Transform[] _boxes = new Transform[3];
        float _offL, _offR;
        public override float ModelHeight => 0.7f;
        static Material Yellow => Paint(new Color(0.84f, 0.6f, 0.15f), 0.6f);
        const float TrackX = 0.17f, Half = 0.38f, Cleat = 0.06f, SprocketR = 0.09f;
        static readonly float[] BoxZ = { -0.06f, 0.15f, 0.36f };   // Godot z (front = -z)
        static readonly string[] BoxPart = { "front cargo box", "middle cargo box", "rear cargo box" };

        protected override void CreatePivots()
        {
            _chassis = Pivot("Chassis", body, Vector3.zero);
            _treadL = Pivot("TreadL", body, Vector3.zero);
            _treadR = Pivot("TreadR", body, Vector3.zero);
            _sprocketLF = Pivot("SprLF", body, new Vector3(-TrackX - 0.06f, 0.1f, Half));
            _sprocketLR = Pivot("SprLR", body, new Vector3(-TrackX - 0.06f, 0.1f, -Half));
            _sprocketRF = Pivot("SprRF", body, new Vector3(TrackX + 0.06f, 0.1f, Half));
            _sprocketRR = Pivot("SprRR", body, new Vector3(TrackX + 0.06f, 0.1f, -Half));
            head = Pivot("Head", _chassis, new Vector3(0, 0.27f, 0.3f));
            _armL = Pivot("ArmL", _chassis, new Vector3(-0.17f, 0.3f, 0.33f));
            _armR = Pivot("ArmR", _chassis, new Vector3(0.17f, 0.3f, 0.33f));
            for (int i = 0; i < 3; i++) _boxes[i] = Pivot("Cargo" + i, _chassis, new Vector3(0, 0.23f, -BoxZ[i]));
            carry = _boxes[0];
        }

        protected override void BuildGeometry()
        {
            foreach (var (side, track, sprocket) in new[] { (-1f, "left track", "left drive sprocket"), (1f, "right track", "right drive sprocket") })
            {
                var b = new Batch();
                float x = side * (TrackX + 0.04f);
                b.Box(new Vector3(x, 0.2f, 0), new Vector3(0.1f, 0.02f, Half * 2), M(track, Rubber));
                b.Box(new Vector3(x, 0.012f, 0), new Vector3(0.1f, 0.02f, Half * 2), M(track, Rubber));
                foreach (var z in new[] { -Half, Half }) b.Cylinder(new Vector3(x, 0.1f, z), 0.1f, 0.1f, M(track, Rubber), AxisX, 18);
                // road wheels between the sprockets
                for (int k = 0; k < 5; k++)
                    b.Cylinder(new Vector3(side * (TrackX + 0.095f), 0.055f, -0.22f + k * 0.11f), 0.035f, 0.02f, M(k < 3 ? "road wheel set a" : "road wheel set b", Brass), AxisX);
                b.Box(new Vector3(side * (TrackX + 0.1f), 0.12f, 0), new Vector3(0.012f, 0.06f, 0.34f), M("chassis", Yellow));
                b.Box(new Vector3(side * (TrackX + 0.108f), 0.12f, 0), new Vector3(0.006f, 0.014f, 0.1f), M("chassis", Dark));
                // cleats on the top and bottom runs; their pivot slides to make the tread move
                var c = new Batch();
                for (int k = -7; k <= 7; k++)
                {
                    c.Box(new Vector3(x, 0.214f, k * Cleat), new Vector3(0.104f, 0.012f, 0.018f), M(track, Dark));
                    b.Box(new Vector3(x, -0.002f, k * Cleat), new Vector3(0.104f, 0.012f, 0.018f), M(track, Dark));   // bottom run rests on the ground
                }
                Done(c, side < 0 ? _treadL : _treadR);
                Done(b, body);
                foreach (var spr in side < 0 ? new[] { _sprocketLF, _sprocketLR } : new[] { _sprocketRF, _sprocketRR })
                {
                    var s = new Batch();
                    s.Cylinder(Vector3.zero, SprocketR, 0.02f, M(sprocket, Red), AxisX, 18);
                    s.Cylinder(new Vector3(side * 0.012f, 0, 0), 0.03f, 0.012f, M(sprocket, Brass), AxisX);
                    for (int k = 0; k < 4; k++)
                    {
                        float a = k * Mathf.PI / 2;
                        s.Box(new Vector3(side * 0.012f, Mathf.Sin(a) * 0.055f, Mathf.Cos(a) * 0.055f), new Vector3(0.006f, 0.014f, 0.014f), M(sprocket, Dark));
                    }
                    Done(s, spr);
                }
            }
            {
                var b = new Batch();
                b.Box(new Vector3(0, 0.15f, 0.03f), new Vector3(0.26f, 0.12f, 0.86f), M("chassis", Yellow));
                b.Box(new Vector3(0, 0.12f, -0.4f), new Vector3(0.16f, 0.06f, 0.012f), M("chassis", Yellow));
                b.Box(new Vector3(0, 0.12f, -0.407f), new Vector3(0.06f, 0.012f, 0.004f), M("chassis", Dark));
                b.Box(new Vector3(0, 0.12f, 0.36f), new Vector3(0.14f, 0.07f, 0.1f), M("drive motor", Dark));
                b.Box(new Vector3(0, 0.17f, 0.465f), new Vector3(0.16f, 0.06f, 0.03f), M("battery pack", Dark));
                b.Cylinder(new Vector3(0, 0.25f, -0.3f), 0.05f, 0.04f, M("chassis", Dark));   // neck
                Done(b, _chassis);
            }
            for (int i = 0; i < 3; i++)
            {
                var b = new Batch();
                string P = BoxPart[i];
                float w = 0.34f, d = 0.19f, h = 0.13f;
                b.Box(new Vector3(0, 0.005f, 0), new Vector3(w, 0.01f, d), M(P, Yellow));
                b.Box(new Vector3(-w / 2, h / 2, 0), new Vector3(0.012f, h, d), M(P, Yellow));
                b.Box(new Vector3(w / 2, h / 2, 0), new Vector3(0.012f, h, d), M(P, Yellow));
                b.Box(new Vector3(0, h / 2, -d / 2), new Vector3(w, h, 0.012f), M(P, Yellow));
                b.Box(new Vector3(0, h / 2, d / 2), new Vector3(w, h, 0.012f), M(P, Yellow));
                foreach (var s in new[] { -1f, 1f })
                {
                    b.Box(new Vector3(s * (w / 2 + 0.006f), h * 0.6f, 0), new Vector3(0.004f, 0.022f, 0.08f), M(P, Red));
                    b.Box(new Vector3(s * (w / 2 + 0.004f), h, 0), new Vector3(0.014f, 0.012f, d), M(P, Brass));
                }
                Done(b, _boxes[i]);
            }
            foreach (var (arm, s, part, claw) in new[] { (_armL, -1f, "left arm", "left claw"), (_armR, 1f, "right arm", "right claw") })
            {
                var b = new Batch();
                ArmKit(b, s, part, Yellow, 0.75f, claw);
                Done(b, arm);
            }
            HeadKit(head, Yellow, new Vector3(0.28f, 0.22f, 0.24f), 1, "head", "optical eye", "antenna pair", "antenna pair", "head", "head", "siren light");
        }

        public override (Transform parent, Vector3 local) CarrySlot(int i, int n)
        {
            int box = i % 3, k = i / 3;
            float x = ((k % 3) - 1) * 0.09f, z = ((k / 3) % 2 - 0.5f) * 0.07f, y = 0.04f + (k / 6) * 0.05f;
            return (_boxes[box], new Vector3(x, y, z));
        }

        public override void Animate(RobotAnim a)
        {
            float s = Mathf.Max(1e-4f, transform.lossyScale.x);
            // treads: forward speed plus the difference for turning on the spot
            float turn = a.turnRate * Mathf.Deg2Rad * (TrackX + 0.04f) * s;   // turning right: left tread forward, right tread back
            float vl = (a.speed + turn) / s, vr = (a.speed - turn) / s;
            _offL = Mathf.Repeat(_offL + vl * a.dt, Cleat);
            _offR = Mathf.Repeat(_offR + vr * a.dt, Cleat);
            _treadL.localPosition = new Vector3(0, 0, _offL);   // top run moves forward relative to the body
            _treadR.localPosition = new Vector3(0, 0, _offR);
            float rotL = vl * a.dt / SprocketR * Mathf.Rad2Deg, rotR = vr * a.dt / SprocketR * Mathf.Rad2Deg;
            _sprocketLF.localRotation *= Quaternion.Euler(rotL, 0, 0); _sprocketLR.localRotation *= Quaternion.Euler(rotL, 0, 0);
            _sprocketRF.localRotation *= Quaternion.Euler(rotR, 0, 0); _sprocketRR.localRotation *= Quaternion.Euler(rotR, 0, 0);
            // a slow, heavy rumble
            _chassis.localPosition = new Vector3(0, Mathf.Sin(a.time * 23f) * 0.0025f * Mathf.Clamp01(a.speed01 + a.turnRate / 90f), 0);
            float pitch = -25 - 35 * a.armReach - 30 * a.armLift + 10 * a.armHold;
            float sway = Mathf.Sin(a.time * 1.3f) * 4;
            _armL.localRotation = Quaternion.Euler(pitch + sway, 0, 6);
            _armR.localRotation = Quaternion.Euler(pitch - sway, 0, -6);
            LookHead(a, 50);
        }
    }
}
