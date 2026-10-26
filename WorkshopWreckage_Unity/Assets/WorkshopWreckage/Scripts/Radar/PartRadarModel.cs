using System.Collections.Generic;
using UnityEngine;

namespace WorkshopWreckage
{
    /// <summary>The Part Radar after resources/part radar (concept sheet, spec v1.2 §3.1): a
    /// two-handed controller cobbled from junk. A chipped cream metal box (≈20 cm, faded blue
    /// patches, rusty screws) with a red ping button and a cream selector dial with a blue knob on
    /// the face the player looks at, one big amber dome LED and four small charge LEDs on top, a
    /// speaker grille and a battery hatch with the robot logo on the back; ribbed rubber grips on
    /// brass and gunmetal pipe brackets either side; two long antennas in a V: the left an old
    /// telescopic rod with brass joints and blue tape, the right a rod wound with copper wire, a
    /// blue lead and a clamped bracket. The face with the controls is the model's front (local +Z,
    /// towards the player when held); "left" is the player's left. Eight parts (spec §4), and the
    /// worn 18650-style cell it runs on (`battery`: that model alone).</summary>
    public class PartRadarModel : RobotModel
    {
        public const string Housing = "radar_housing", HandleL = "radar_left_handle", HandleR = "radar_right_handle", Button = "radar_pulse_button",
            Dial = "radar_selector_dial", Led = "radar_led_module", AntennaL = "radar_left_antenna", AntennaR = "radar_right_antenna";
        public static readonly string[] Parts = { Housing, HandleL, HandleR, Button, Dial, Led, AntennaL, AntennaR };

        /// <summary>Only the battery (its own model).</summary>
        public bool battery;
        /// <summary>The main amber LED and the four charge LEDs (SetLed / SetCharges switch them).</summary>
        public Renderer mainLed;
        /// <summary>The amber light the main LED throws (only on the radar in your hands).</summary>
        public Light ledLight;
        public readonly List<Renderer> chargeLeds = new();
        Transform _antL, _antR, _knob, _button;

        const float W = 0.24f, H = 0.165f, D = 0.085f;   // the housing box
        const float YT = 0.048f, YB = -0.056f;   // the grip brackets
        const float L = 1f;                           // +x is the player's left (the model faces the player)
        public override float ModelHeight => 0.5f;

        static Material _cream, _creamDeep, _blue, _copper, _rustBolt, _wireRed, _wireBlue, _tape, _ledOff, _ledOn, _chargeOff, _chargeOn, _cell;
        static Material Cream => _cream ??= RobotFinish.Paint(new Color(0.8f, 0.74f, 0.6f), 1f, 0.95f, 1f, 1f, 0.85f, 0.25f, 0.16f);
        static Material CreamDeep => _creamDeep ??= RobotFinish.Paint(new Color(0.68f, 0.62f, 0.5f), 1f, 0.95f, 1f, 1f, 0.8f, 0.25f, 0.16f);
        static Material Blue => _blue ??= RobotFinish.Paint(new Color(0.22f, 0.33f, 0.48f), 1f, 0.95f, 0.9f, 1f, 0.8f, 0.6f, 0.16f);   // faded
        static Material Copper => _copper ??= EnvMaterials.Plain(new Color(0.62f, 0.32f, 0.17f), 1f, 0.5f);
        static Material RustBolt => _rustBolt ??= EnvMaterials.Plain(new Color(0.36f, 0.2f, 0.11f), 0.3f, 0.22f);
        static Material WireRed => _wireRed ??= EnvMaterials.Plain(new Color(0.62f, 0.09f, 0.06f), 0f, 0.45f);
        static Material WireBlue => _wireBlue ??= EnvMaterials.Plain(new Color(0.16f, 0.3f, 0.55f), 0f, 0.45f);
        static Material Tape => _tape ??= RobotFinish.Paint(new Color(0.6f, 0.58f, 0.52f), 1f, 0.6f, 0.9f, 0.4f, 0.3f, 0.3f, 0.15f);
        static Material LedOff => _ledOff ??= EnvMaterials.Plain(new Color(0.42f, 0.27f, 0.08f), 0.1f, 0.92f);
        static Material LedOn => _ledOn ??= EnvMaterials.Plain(new Color(1f, 0.667f, 0.133f), 0f, 0.92f, 60f);   // #FFAA22, blazing: it has to read under the workshop lights (playtest 2026-10-26)
        static Material ChargeOff => _chargeOff ??= EnvMaterials.Plain(new Color(0.1f, 0.16f, 0.08f), 0.1f, 0.9f);
        static Material ChargeOn => _chargeOn ??= EnvMaterials.Plain(new Color(0.45f, 1f, 0.3f), 0f, 0.9f, 2.5f);
        static Material Cell => _cell ??= RobotFinish.ShaftSteel;

        protected override Material Skin(Material m) => m == StainMat ? m : FamilySkin(m);

        public void SetLed(bool on) { if (mainLed != null) mainLed.sharedMaterial = on ? LedOn : LedOff; }

        /// <summary>Adds the LED's own light (the radar in hand): amber, off until SetLight.</summary>
        public void AddLedLight()
        {
            if (mainLed == null || ledLight != null) return;
            var go = new GameObject("LedLight");
            go.transform.SetParent(mainLed.transform, false);
            go.transform.position = mainLed.bounds.center;
            ledLight = go.AddComponent<Light>();
            ledLight.type = LightType.Point;
            ledLight.color = new Color(1f, 0.667f, 0.133f);
            ledLight.range = 3.5f;
            ledLight.shadows = LightShadows.None;
            ledLight.intensity = 0;
            ledLight.enabled = false;
        }

        public void SetLight(float intensity)
        {
            if (ledLight == null) return;
            ledLight.intensity = intensity;
            ledLight.enabled = intensity > 0.01f;
        }
        public void SetCharges(int n) { for (int i = 0; i < chargeLeds.Count; i++) chargeLeds[i].sharedMaterial = i < n ? ChargeOn : ChargeOff; }
        /// <summary>The knob's turn (degrees) and the ping button pressed in (0..1).</summary>
        public void Pose(float dialDeg, float press)
        {
            if (_knob != null) _knob.localRotation = Quaternion.Euler(0, 0, dialDeg);
            if (_button != null) _button.localPosition = new Vector3(_button.localPosition.x, _button.localPosition.y, D / 2 + 0.012f - press * 0.006f);
        }

        protected override void CreatePivots()
        {
            // antennas splay out from the top corners into a V
            _antL = Pivot("AntennaL", body, new Vector3(L * (W / 2 - 0.026f), H / 2 + 0.012f, 0.004f));
            _antL.localRotation = Quaternion.Euler(0, 0, -L * 21f);
            _antR = Pivot("AntennaR", body, new Vector3(-L * (W / 2 - 0.03f), H / 2 + 0.012f, 0.004f));
            _antR.localRotation = Quaternion.Euler(0, 0, L * 17f);
            _knob = Pivot("Knob", body, new Vector3(-L * 0.042f, -0.014f, D / 2 + 0.008f));
            _button = Pivot("Button", body, new Vector3(L * 0.068f, -0.018f, D / 2 + 0.012f));
            head = body;
        }

        public override void Animate(RobotAnim a) { }

        Vector3 F(float x, float y, float lift = 0) => new Vector3(x, y, -(D / 2 + lift));   // a point on the front face (Godot-local: front is -z)
        Vector3 Bk(float x, float y, float lift = 0) => new Vector3(x, y, D / 2 + lift);   // on the back face

        void RustyScrew(Batch b, string part, Vector3 p, Vector3 n, float r = 0.0045f, float streak = 0.02f)
        {
            Bolt(b, p, n, r, M(part, RustBolt));
            if (streak > 0 && Hash(p, 3) < 0.7f) RustStreak(b, part, p, n, streak * (0.5f + Hash(p, 4)), r * 2.4f);
        }

        protected override void BuildGeometry()
        {
            if (battery) { BuildBattery(); return; }
            chargeLeds.Clear();
            BuildHousing();
            BuildControls();
            BuildHandle(L, HandleL, true);
            BuildHandle(-L, HandleR, false);
            BuildAntennaL();
            BuildAntennaR();
        }

        void BuildHousing()
        {
            var b = new Batch();
            var front = new Vector3(0, 0, -1);
            var back = new Vector3(0, 0, 1);
            b.ChamferBox(Vector3.zero, new Vector3(W, H, D), 0.012f, M(Housing, Cream));
            // a dented, uneven lid: the top plate sits a little proud and skewed
            b.ChamferBox(new Vector3(0.004f, H / 2 + 0.003f, 0.002f), new Vector3(W - 0.03f, 0.007f, D - 0.012f), 0.003f, M(Housing, CreamDeep), Batch.R(Vector3.forward, 0.012f));
            // faded blue patches riveted over old holes (front lower right, front upper left, back, sides)
            foreach (var (c, s, rot) in new[] { (F(-L * 0.078f, -0.052f, 0.0012f), new Vector2(0.075f, 0.052f), -0.04f), (F(L * 0.07f, 0.058f, 0.0012f), new Vector2(0.07f, 0.028f), 0.06f),
                                                 (F(-L * 0.092f, 0.05f, 0.0012f), new Vector2(0.034f, 0.026f), 0.1f), (F(L * 0.02f, -0.07f, 0.0012f), new Vector2(0.05f, 0.016f), 0.02f) })
            {
                b.Box(c, new Vector3(s.x, s.y, 0.0024f), M(Housing, Blue), false, Batch.R(Vector3.forward, rot));
                foreach (var sx in new[] { -1f, 1f })
                    b.Rivet(c + new Vector3(sx * (s.x / 2 - 0.006f), s.y / 2 - 0.006f, -0.0012f), front, 0.0022f, M(Housing, RustBolt));
            }
            b.Box(new Vector3(-L * (W / 2 + 0.0012f), -0.03f, 0.008f), new Vector3(0.0024f, 0.05f, 0.045f), M(Housing, Blue), false, Batch.R(Vector3.right, 0.05f));
            // rusty screws in the corners of both faces, rust bleeding down
            foreach (var sx in new[] { -1f, 1f })
                foreach (var sy in new[] { -1f, 1f })
                {
                    RustyScrew(b, Housing, F(sx * (W / 2 - 0.012f), sy * (H / 2 - 0.012f)), front, 0.0048f, sy > 0 ? 0.03f : 0.008f);
                    RustyScrew(b, Housing, Bk(sx * (W / 2 - 0.012f), sy * (H / 2 - 0.012f)), back, 0.0048f, sy > 0 ? 0.03f : 0.008f);
                }
            // seams: the box is two pressed shells
            SeamLine(b, Housing, new Vector3(W / 2 + 0.0004f, H / 2 - 0.01f, 0), new Vector3(W / 2 + 0.0004f, -H / 2 + 0.01f, 0), Vector3.right);
            SeamLine(b, Housing, new Vector3(-W / 2 - 0.0004f, H / 2 - 0.01f, 0), new Vector3(-W / 2 - 0.0004f, -H / 2 + 0.01f, 0), Vector3.left);
            SeamLine(b, Housing, F(-W / 2 + 0.01f, H / 2 - 0.026f, 0.0004f), F(W / 2 - 0.01f, H / 2 - 0.026f, 0.0004f), front);
            // gunmetal corner brackets holding the shells (top corners, where the antennas clamp)
            foreach (var sx in new[] { -1f, 1f })
            {
                b.ChamferBox(new Vector3(sx * (W / 2 - 0.014f), H / 2 - 0.012f, 0), new Vector3(0.034f, 0.03f, D + 0.008f), 0.004f, M(Housing, Gunmetal));
                foreach (var sz in new[] { -1f, 1f }) Bolt(b, new Vector3(sx * (W / 2 - 0.014f), H / 2 - 0.016f, sz * (D / 2 + 0.004f)), new Vector3(0, 0, sz), 0.0042f, M(Housing, Gunmetal));
            }
            foreach (var sx in new[] { -1f, 1f })
            {
                b.ChamferBox(new Vector3(sx * (W / 2 - 0.012f), -H / 2 + 0.01f, 0), new Vector3(0.028f, 0.024f, D + 0.006f), 0.004f, M(Housing, Gunmetal));
                Bolt(b, new Vector3(sx * (W / 2 - 0.012f), -H / 2 + 0.01f, -(D / 2 + 0.003f)), front, 0.004f, M(Housing, Gunmetal));
            }
            for (int k = 0; k < 5; k++)   // rust bleeding from under the lid and the brackets
            {
                var p = F(Mathf.Lerp(-W / 2 + 0.035f, W / 2 - 0.035f, Hash(new Vector3(k, 1, 0))), H / 2 - 0.022f - 0.03f * Hash(new Vector3(k, 2, 0)), 0.0003f);
                RustStreak(b, Housing, p, front, 0.012f + 0.03f * Hash(new Vector3(k, 3, 0)), 0.004f + 0.004f * Hash(new Vector3(k, 4, 0)));
            }
            // side mounting blocks for the grips
            foreach (var sx in new[] { -1f, 1f })
                foreach (var y in new[] { YT, YB })
                {
                    b.ChamferBox(new Vector3(sx * (W / 2 + 0.008f), y, 0), new Vector3(0.016f, 0.026f, 0.034f), 0.003f, M(Housing, Gunmetal));
                    Bolt(b, new Vector3(sx * (W / 2 + 0.016f), y, 0), new Vector3(sx, 0, 0), 0.0045f, M(Housing, Gunmetal));
                }
            // back: speaker grille (player's right seen from behind = -L... the back face's left), louvres, battery hatch with the robot logo
            var g = Bk(-L * 0.05f, 0.012f, 0.001f);
            b.ChamferBox(g, new Vector3(0.062f, 0.07f, 0.003f), 0.002f, M(Housing, CreamDeep));
            for (int i = 0; i < 5; i++)
                for (int j = 0; j < 5; j++)
                    if (!(i == 4 && j == 0))   // one hole plugged
                        b.Lathe(g + new Vector3((i - 2) * 0.011f, (j - 2) * 0.012f, 0.0012f), Disc(0.0035f, 0.0012f, 0.0004f), M(Housing, Dark), BackRot, 10);
            for (int k = 0; k < 4; k++)
                b.ChamferBox(Bk(L * 0.035f, 0.05f - k * 0.008f, 0.0015f), new Vector3(0.05f, 0.0035f, 0.003f), 0.001f, M(Housing, Dark));
            var hc = Bk(L * 0.042f, -0.03f, 0.004f);
            b.ChamferBox(hc, new Vector3(0.094f, 0.062f, 0.008f), 0.003f, M(Housing, Gunmetal));
            b.ChamferBox(hc + new Vector3(0, 0, 0.0045f), new Vector3(0.084f, 0.052f, 0.003f), 0.002f, M(Housing, CreamDeep));
            var lc = hc + new Vector3(L * 0.006f, 0, 0.0065f);
            b.ChamferBox(lc, new Vector3(0.04f, 0.036f, 0.002f), 0.001f, M(Housing, Blue));
            // the robot logo (the title's little robot): head, eyes, antenna, legs in cream
            var o = lc + new Vector3(0, 0, 0.0012f);
            b.Box(o + new Vector3(0, 0.003f, 0), new Vector3(0.018f, 0.013f, 0.0012f), M(Housing, Cream));
            foreach (var ex in new[] { -1f, 1f }) b.Box(o + new Vector3(ex * 0.0045f, 0.0035f, 0.0005f), new Vector3(0.0045f, 0.0045f, 0.0012f), M(Housing, Blue));
            b.Box(o + new Vector3(0, 0.0125f, 0), new Vector3(0.0022f, 0.006f, 0.0012f), M(Housing, Cream));
            foreach (var lx in new[] { -1f, 1f }) b.Box(o + new Vector3(lx * 0.005f, -0.0085f, 0), new Vector3(0.004f, 0.007f, 0.0012f), M(Housing, Cream));
            foreach (var sx in new[] { -1f, 1f }) foreach (var sy in new[] { -1f, 1f })
                RustyScrew(b, Housing, hc + new Vector3(sx * 0.04f, sy * 0.022f, 0.006f), back, 0.003f, 0);
            b.ChamferBox(hc + new Vector3(-L * 0.051f, 0, 0.002f), new Vector3(0.008f, 0.022f, 0.01f), 0.002f, M(Housing, Brass));   // the retaining clip
            // exposed red and blue wires under the box, looping between the grips
            Pipe(b, Housing, new[] { new Vector3(L * 0.07f, -H / 2 + 0.004f, -0.02f), new Vector3(L * 0.05f, -H / 2 - 0.014f, -0.024f), new Vector3(0, -H / 2 - 0.02f, -0.02f),
                                     new Vector3(-L * 0.05f, -H / 2 - 0.012f, -0.02f), new Vector3(-L * 0.075f, -H / 2 + 0.003f, -0.016f) }, 0.0028f, WireRed, Brass);
            Pipe(b, Housing, new[] { new Vector3(L * 0.03f, -H / 2 + 0.004f, 0.016f), new Vector3(L * 0.01f, -H / 2 - 0.012f, 0.02f), new Vector3(-L * 0.04f, -H / 2 - 0.008f, 0.018f),
                                     new Vector3(-L * 0.06f, -H / 2 + 0.003f, 0.012f) }, 0.0024f, Rubber);
            Done(b, body);
        }

        void BuildControls()
        {
            var front = new Vector3(0, 0, -1);
            {
                // ping button: a bolted gunmetal box with a chunky red cap (the cap moves: its own pivot)
                var b = new Batch();
                var c = F(L * 0.068f, -0.018f, 0.008f);
                b.ChamferBox(c, new Vector3(0.064f, 0.066f, 0.016f), 0.005f, M(Button, Gunmetal));
                foreach (var sx in new[] { -1f, 1f }) foreach (var sy in new[] { -1f, 1f })
                    Bolt(b, c + new Vector3(sx * 0.025f, sy * 0.026f, -0.008f), front, 0.004f, M(Button, Gunmetal));
                b.Lathe(c + new Vector3(0, 0, -0.008f), Disc(0.025f, 0.004f, 0.001f), M(Button, Brass), FwdRot, 24);
                Pipe(b, Button, new[] { c + new Vector3(-L * 0.02f, -0.029f, 0), c + new Vector3(-L * 0.01f, -0.05f, -0.004f), c + new Vector3(L * 0.03f, -0.052f, 0.004f), new Vector3(L * 0.085f, -H / 2 + 0.002f, -0.01f) },
                     0.0026f, WireRed, Brass);
                Done(b, body);
                var bc = new Batch();
                bc.Lathe(Vector3.zero, Disc(0.021f, 0.008f, 0.002f), M(Button, ToyRed), FwdRot, 28);
                bc.Lathe(new Vector3(0, 0, -0.008f), Dome(0.02f, 0.007f), M(Button, ToyRed), FwdRot, 28);
                Done(bc, _button);
            }
            {
                // selector dial: a cream disc with stencilled part icons round its rim; the blue knob turns
                var b = new Batch();
                var c = F(-L * 0.042f, -0.014f, 0);
                b.Lathe(c, Disc(0.058f, 0.006f, 0.0015f), M(Dial, Gunmetal), FwdRot, 40);
                b.Lathe(c + new Vector3(0, 0, -0.006f), Disc(0.055f, 0.003f, 0.0012f), M(Dial, Cream), FwdRot, 40);
                var face = c + new Vector3(0, 0, -0.009f);
                for (int k = 0; k < 7; k++)
                {
                    float a = Mathf.Lerp(-130, 130, k / 6f) * Mathf.Deg2Rad;
                    var p = face + new Vector3(Mathf.Sin(a), Mathf.Cos(a), 0) * 0.043f;
                    if (k % 2 == 0)
                    {
                        // a little gear
                        b.Lathe(p, Ring(0.0052f, 0.002f, 0.0008f), M(Dial, Dark), FwdRot, 12);
                        for (int t = 0; t < 6; t++)
                        {
                            float ta = t * Mathf.PI / 3;
                            b.Box(p + new Vector3(Mathf.Sin(ta), Mathf.Cos(ta), 0) * 0.0058f, new Vector3(0.0022f, 0.0022f, 0.0008f), M(Dial, Dark), false, Batch.R(Vector3.forward, ta));
                        }
                    }
                    else
                    {
                        // a bone-shaped link (an arm part)
                        b.Box(p, new Vector3(0.0022f, 0.009f, 0.0008f), M(Dial, Dark), false, Batch.R(Vector3.forward, a));
                        foreach (var e in new[] { -1f, 1f })
                            b.Lathe(p + new Vector3(Mathf.Sin(a), Mathf.Cos(a), 0) * (e * 0.0048f), Disc(0.0022f, 0.0008f, 0.0002f), M(Dial, Dark), FwdRot, 8);
                    }
                }
                Bolt(b, F(L * 0.016f, 0.05f), front, 0.004f, M(Dial, RustBolt));
                b.Lathe(F(-L * 0.1f, -0.062f, 0), Disc(0.006f, 0.004f, 0.001f), M(Dial, Gunmetal), FwdRot, 14);   // a jack socket
                Done(b, body);
                var bk = new Batch();
                bk.Lathe(Vector3.zero, Disc(0.02f, 0.006f, 0.0015f), M(Dial, Gunmetal), FwdRot, 24);
                bk.RoundedBox(new Vector3(0, 0.004f, -0.014f), new Vector3(0.026f, 0.078f, 0.02f), 0.008f, M(Dial, Blue), 3);
                bk.Box(new Vector3(0, 0.03f, -0.0245f), new Vector3(0.004f, 0.022f, 0.001f), M(Dial, Cream));   // the pointer line
                Done(bk, _knob);
                _knob.localRotation = Quaternion.Euler(0, 0, -L * 35);
            }
            {
                // LED beacon module: a bolted gunmetal block on the lid, brass collar, the amber dome; four charge lamps beside it
                var b = new Batch();
                var top = new Vector3(-L * 0.006f, H / 2 + 0.006f, 0);
                b.ChamferBox(top + new Vector3(0, 0.008f, 0), new Vector3(0.054f, 0.016f, 0.05f), 0.004f, M(Led, Gunmetal));
                foreach (var sx in new[] { -1f, 1f }) foreach (var sz in new[] { -1f, 1f })
                    Bolt(b, top + new Vector3(sx * 0.021f, 0.016f, sz * 0.019f), Vector3.up, 0.0032f, M(Led, Gunmetal));
                b.Lathe(top + new Vector3(0, 0.016f, 0), Disc(0.021f, 0.007f, 0.0015f), M(Led, Brass), null, 28);
                Pipe(b, Led, new[] { top + new Vector3(L * 0.02f, 0.006f, -0.021f), top + new Vector3(L * 0.034f, 0.002f, -0.03f), top + new Vector3(L * 0.05f, -0.004f, -0.022f) }, 0.002f, WireRed);
                // charge strip
                var cs = top + new Vector3(-L * 0.056f, 0.003f, -0.012f);
                b.ChamferBox(cs, new Vector3(0.05f, 0.008f, 0.018f), 0.002f, M(Led, Dark));
                Done(b, body);
                if (Has(Led))
                {
                    mainLed = Separate(body, "MainLed", lb => lb.Lathe(top + new Vector3(0, 0.023f, 0), Dome(0.019f, 0.024f, 8), LedOff, null, 28));
                    for (int i = 0; i < 4; i++)
                    {
                        var p = cs + new Vector3(L * (0.018f - i * 0.012f), 0.004f, 0);
                        chargeLeds.Add(Separate(body, "Charge" + i, lb => lb.Lathe(p, Dome(0.0038f, 0.0032f, 5), ChargeOff, null, 12)));
                    }
                }
            }
        }

        /// <summary>A grip on side s: ribbed rubber wrap on a bar, held off the box by a pipe bracket
        /// at the top and a brass elbow at the bottom; the left one (brass) and the right (gunmetal,
        /// taped) don't match.</summary>
        void BuildHandle(float s, string part, bool brass)
        {
            var b = new Batch();
            float gx = s * (W / 2 + 0.058f), y0 = -0.075f, y1 = 0.058f;
            var pipeMat = brass ? Brass : Gunmetal;
            b.Cylinder(new Vector3(gx, (y0 + y1) / 2, 0), 0.019f, y1 - y0 + 0.02f, M(part, Gunmetal));   // the bar inside
            int ribs = brass ? 8 : 7;
            for (int i = 0; i < ribs; i++)
            {
                float y = Mathf.Lerp(y0 + 0.006f, y1 - 0.016f, i / (float)(ribs - 1));
                b.Lathe(new Vector3(gx, y - 0.007f, 0), Disc(0.031f - 0.002f * Hash(new Vector3(s, i, 0)), 0.015f, 0.006f), M(part, Rubber), null, 18);
            }
            // tape: a wide band near the top (left), two narrow ones (right)
            foreach (var (y, h) in brass ? new[] { (y1 - 0.012f, 0.02f) } : new[] { (y1 - 0.008f, 0.012f), (y0 + 0.03f, 0.01f) })
                b.Lathe(new Vector3(gx, y - h / 2, 0), Disc(0.0325f, h, 0.002f), M(part, Tape), Batch.R(Vector3.forward, 0.08f * s), 18);
            // top bracket: a pipe from the side block, over and down into the grip, with a cap
            Pipe(b, part, new[] { new Vector3(s * (W / 2 + 0.014f), YT, 0), new Vector3(s * (W / 2 + 0.032f), YT + 0.01f, 0), new Vector3(gx - s * 0.008f, y1 + 0.022f, 0), new Vector3(gx, y1 + 0.022f, 0), new Vector3(gx, y1 + 0.004f, 0) },
                 brass ? 0.013f : 0.012f, pipeMat, Gunmetal);
            b.Lathe(new Vector3(gx, y1 + 0.028f, 0), Dome(0.015f, 0.009f), M(part, pipeMat), null, 16);
            // bottom: a brass elbow out of the grip, back under the box
            Pipe(b, part, new[] { new Vector3(gx, y0 + 0.004f, 0), new Vector3(gx, y0 - 0.016f, 0), new Vector3(gx - s * 0.014f, y0 - 0.028f, 0), new Vector3(s * (W / 2 + 0.02f), YB - 0.03f, 0),
                                  new Vector3(s * (W / 2 + 0.014f), YB, 0) }, 0.013f, Brass, Gunmetal);
            if (!brass)   // a hose clamp round the right one's bracket
                b.Lathe(new Vector3(s * (W / 2 + 0.036f), YT + 0.011f, 0), Disc(0.016f, 0.006f, 0.001f), M(part, ToyRed), Batch.R(Vector3.forward, Mathf.PI / 2), 14);
            GreaseRun(b, part, new Vector3(gx + s * 0.0315f, y1 - 0.02f, 0), new Vector3(s, 0, 0), 0.04f, 0.01f);
            Done(b, body);
        }

        /// <summary>Left antenna: an old telescopic rod (three sections, brass joints), blue tape and a
        /// black lead into the box, a little fork at the tip. Built along the pivot's +Y.</summary>
        void BuildAntennaL()
        {
            var b = new Batch();
            string P = AntennaL;
            b.ChamferBox(new Vector3(0, 0.006f, 0), new Vector3(0.026f, 0.02f, 0.026f), 0.003f, M(P, Gunmetal));   // clamp block
            Bolt(b, new Vector3(0, 0.006f, -0.0131f), new Vector3(0, 0, -1), 0.0038f, M(P, Gunmetal));
            b.Lathe(new Vector3(0, 0.016f, 0), Disc(0.0105f, 0.014f, 0.002f), M(P, Gunmetal), null, 16);
            float y = 0.03f;
            foreach (var (len, r) in new[] { (0.12f, 0.0082f), (0.1f, 0.006f), (0.075f, 0.0042f) })
            {
                b.Cylinder(new Vector3(0, y + len / 2, 0), r, len, M(P, len > 0.11f ? Brass : len > 0.09f ? Gunmetal : BrightSteel), null, 12);
                b.Lathe(new Vector3(0, y + len - 0.008f, 0), Disc(r + 0.0042f, 0.014f, 0.0015f), M(P, Brass), null, 14);   // joint collar
                y += len - 0.004f;
            }
            b.Lathe(new Vector3(0, 0.07f, 0), Disc(0.0112f, 0.03f, 0.002f), M(P, Blue), Batch.R(Vector3.forward, 0.05f), 14);   // blue insulation tape
            b.Lathe(new Vector3(0, 0.04f, 0), Disc(0.0125f, 0.02f, 0.002f), M(P, Rubber), null, 14);
            // the tip: a little fork
            b.Lathe(new Vector3(0, y, 0), Disc(0.005f, 0.008f, 0.001f), M(P, Gunmetal), null, 10);
            foreach (var fx in new[] { -1f, 1f })
                b.ChamferBox(new Vector3(fx * 0.0042f, y + 0.014f, 0), new Vector3(0.0028f, 0.014f, 0.004f), 0.0008f, M(P, Gunmetal), Batch.R(Vector3.forward, -fx * 0.25f));
            Pipe(b, P, new[] { new Vector3(0, 0.1f, 0.006f), new Vector3(0.018f, 0.07f, 0.01f), new Vector3(0.02f, 0.02f, 0.012f), new Vector3(0.008f, -0.006f, 0.012f) }, 0.0024f, Rubber, Gunmetal);
            Done(b, _antL);
        }

        /// <summary>Right antenna: a mismatched rod wound with copper wire, a blue lead looping down
        /// beside it, a clamped bracket at the base and a blue cap.</summary>
        void BuildAntennaR()
        {
            var b = new Batch();
            string P = AntennaR;
            // the clamped bracket: a bent strap bolted to the lid with a hose clamp round the rod
            b.ChamferBox(new Vector3(0, 0.004f, 0), new Vector3(0.03f, 0.008f, 0.028f), 0.002f, M(P, Gunmetal));
            b.ChamferBox(new Vector3(0, 0.022f, 0.009f), new Vector3(0.022f, 0.03f, 0.005f), 0.0015f, M(P, Gunmetal));
            Bolt(b, new Vector3(0, 0.026f, 0.0115f), new Vector3(0, 0, 1), 0.0036f, M(P, Gunmetal));
            b.Lathe(new Vector3(0, 0.016f, 0), Disc(0.0108f, 0.008f, 0.0015f), M(P, BrightSteel), null, 16);
            const float len = 0.29f;
            b.Cylinder(new Vector3(0, 0.01f + len / 2, 0), 0.0068f, len, M(P, Brass), null, 12);
            b.Lathe(new Vector3(0, 0.045f, 0), Disc(0.0115f, 0.03f, 0.002f), M(P, Gunmetal), null, 14);
            b.Lathe(new Vector3(0, 0.075f, 0), Disc(0.009f, 0.018f, 0.002f), M(P, Blue), null, 14);
            // copper winding: a helix round the middle of the rod
            var helix = new List<Vector3>();
            for (int i = 0; i <= 72; i++)
            {
                float t = i / 72f, a = t * Mathf.PI * 2 * 6;
                float rr = 0.0145f + 0.003f * Mathf.Sin(t * 9);   // hand-wound: not quite even
                helix.Add(new Vector3(Mathf.Cos(a) * rr, 0.095f + t * 0.12f, Mathf.Sin(a) * rr));
            }
            Pipe(b, P, helix.ToArray(), 0.0032f, Copper, null, 1);
            // the blue lead: out of the box, up beside the rod, a loop, into the top collar
            Pipe(b, P, new[] { new Vector3(-0.004f, -0.004f, -0.014f), new Vector3(-0.022f, 0.06f, -0.012f), new Vector3(-0.03f, 0.13f, -0.006f), new Vector3(-0.024f, 0.2f, 0f),
                               new Vector3(-0.01f, 0.24f, 0.004f), new Vector3(-0.0065f, 0.255f, 0f) }, 0.0026f, WireBlue, Brass);
            b.Lathe(new Vector3(0, 0.255f, 0), Disc(0.0085f, 0.01f, 0.0015f), M(P, Brass), null, 14);
            b.Lathe(new Vector3(0, 0.01f + len, 0), Disc(0.0072f, 0.012f, 0.002f), M(P, Blue), null, 14);   // blue tip cap
            b.Lathe(new Vector3(0, 0.022f + len, 0), Dome(0.0072f, 0.004f), M(P, Blue), null, 14);
            Done(b, _antR);
        }

        /// <summary>A used 18650-style cell (resources/part radar/used_battery.jpg): bare steel can,
        /// a crusty rust rim at the top round the button, rust runs down one side, a dark band at the foot.</summary>
        void BuildBattery()
        {
            var b = new Batch();
            const float r = 0.0092f, h = 0.065f;
            b.Lathe(Vector3.zero, Disc(r, h - 0.004f, 0.0012f), Cell, null, 28);
            b.Lathe(new Vector3(0, 0.002f, 0), Disc(r + 0.0003f, 0.004f, 0.0005f), RobotFinish.Blackened, null, 28);   // the dark foot band
            b.Lathe(new Vector3(0, h - 0.0052f, 0), Disc(r - 0.0004f, 0.0012f, 0.0003f), RobotFinish.Blackened, null, 28);   // crimp groove
            b.Lathe(new Vector3(0, h - 0.004f, 0), Ring(r + 0.0004f, r - 0.0025f, 0.0032f), RustBolt, null, 28);   // the crusty rim
            b.Lathe(new Vector3(0, h - 0.004f, 0), Disc(r - 0.0024f, 0.0015f, 0.0004f), Cell, null, 20);
            b.Lathe(new Vector3(0, h - 0.0025f, 0), Disc(0.0036f, 0.0025f, 0.0006f), Cell, null, 16);   // the + button
            for (int k = 0; k < 6; k++)
            {
                float a = (k * 0.47f + 0.3f) * (k % 2 == 0 ? 1 : -1) + Mathf.PI * 0.25f;
                var n = new Vector3(Mathf.Sin(a), 0, Mathf.Cos(a));
                var p = n * (r + 0.0002f) + Vector3.up * (h - 0.006f - 0.004f * Hash(n));
                RustStreak(b, "", p, n, 0.012f + 0.045f * Hash(n), 0.0012f + 0.0012f * Hash(n, 3));
            }
            for (int k = 0; k < 16; k++)   // crust round the rim
            {
                float a = k * Mathf.PI * 2 / 16 + Hash(new Vector3(k, 0, 1));
                var n = new Vector3(Mathf.Sin(a), 0, Mathf.Cos(a));
                b.ChamferBox(n * (r - 0.0005f) + Vector3.up * (h - 0.002f - 0.004f * Hash(n, 5)), new Vector3(0.003f, 0.0025f + 0.003f * Hash(n, 6), 0.002f), 0.0006f, RustBolt, Quaternion.LookRotation(Batch.G(n)));
            }
            Done(b, body);
        }
    }
}
