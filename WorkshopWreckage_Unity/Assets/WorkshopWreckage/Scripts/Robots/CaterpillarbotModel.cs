using UnityEngine;

namespace WorkshopWreckage
{
    /// <summary>Caterpillarbot after resources/catbot.png, upgraded to the family standard (robot family
    /// phase 2, 2026-10-24): a long, low, weathered industrial-yellow tracked hauler in the family finish.
    /// Rubber track loops whose cleats really roll (the bottom run stands still on the floor, the top run
    /// moves forward at twice the speed of the hull), big red drive sprockets with bolted family bearing hubs,
    /// brass road wheels below a bolted yellow skirt; a wide front block under the family head (one big lamp
    /// eye, mouth grille, the canonical ear cones and antennas, the red siren on top); the family arms with
    /// the two-stage telescopic forearms and the articulated red grippers on the front corners; three open
    /// riveted cargo boxes behind the head.
    /// Its own locomotion (Layer B): heavy and planted. The hull rides on the track frames and answers
    /// acceleration and braking with a small, well-damped pitch (no banking, no wobble); a faint engine
    /// rumble when driving. Manipulation and attention come from the family layer (Layer A); the grippers
    /// load the front cargo box over the head (StowsCargo) and take each part out again to deliver it.
    /// Parts: head, optical eye, antenna pair, siren light, chassis, left/right track,
    /// left/right drive sprocket, road wheel set A/B, front/middle/rear cargo box,
    /// left/right arm, left/right claw, drive motor, battery pack.</summary>
    public class CaterpillarbotModel : RobotModel
    {
        Transform _treadL, _treadR, _treadLB, _treadRB, _sprocketLF, _sprocketLR, _sprocketRF, _sprocketRR, _armL, _armR, _chassis;
        readonly Transform[] _boxes = new Transform[3];
        float _offL, _offR;
        public override float ModelHeight => 0.9f;
        const float TrackX = 0.25f, TrackW = 0.17f, EndZ = 0.47f, EndR = 0.145f, EndY = 0.145f, Cleat = 0.06f, SprocketR = 0.118f;
        const float ShoulderX = 0.33f, ShoulderY = 0.43f, ShoulderZ = 0.55f, HullPivotY = 0.2f;
        static readonly float[] BoxZ = { -0.07f, 0.2f, 0.47f };   // Godot z (front = -z)
        static readonly string[] BoxPart = { "front cargo box", "middle cargo box", "rear cargo box" };

        protected override Material Skin(Material m) => FamilySkin(m);
        protected override Material BoltSkin(Material m) => FamilyBoltSkin(m);
        public override bool StowsCargo => true;

        /// <summary>The right gripper is the loader: it alone carries a part over to the front box.</summary>
        public override Vector3? StowGripPoint => clawTipR != null ? clawTipR.position : null;

        protected override void CreatePivots()
        {
            _chassis = Pivot("Chassis", body, Vector3.zero);
            _treadL = Pivot("TreadL", body, Vector3.zero);
            _treadR = Pivot("TreadR", body, Vector3.zero);
            _treadLB = Pivot("TreadLB", body, Vector3.zero);
            _treadRB = Pivot("TreadRB", body, Vector3.zero);
            _sprocketLF = Pivot("SprLF", body, new Vector3(-TrackX - TrackW / 2, EndY, EndZ));
            _sprocketLR = Pivot("SprLR", body, new Vector3(-TrackX - TrackW / 2, EndY, -EndZ));
            _sprocketRF = Pivot("SprRF", body, new Vector3(TrackX + TrackW / 2, EndY, EndZ));
            _sprocketRR = Pivot("SprRR", body, new Vector3(TrackX + TrackW / 2, EndY, -EndZ));
            head = Pivot("Head", _chassis, new Vector3(0, 0.49f, 0.38f));
            // arms are mounted at the front corners and reach forward, as in the reference
            _armL = Pivot("ArmL", _chassis, new Vector3(-ShoulderX, ShoulderY, ShoulderZ));
            _armR = Pivot("ArmR", _chassis, new Vector3(ShoulderX, ShoulderY, ShoulderZ));
            _armL.localScale = _armR.localScale = Vector3.one * 1.1f;   // long loader arms (the sheet)
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
            foreach (var x in new[] { -0.15f, 0.15f }) b.RoundedBox(new Vector3(x, 0.2f, 0), new Vector3(0.04f, 0.05f, EndZ * 2 + 0.1f), 0.008f, Skin(Gunmetal), 2);
            foreach (var z in new[] { -EndZ, -0.2f, 0.1f, EndZ }) b.RoundedBox(new Vector3(0, 0.2f, z), new Vector3(0.34f, 0.035f, 0.035f), 0.008f, Skin(Gunmetal), 2);
            b.RoundedBox(new Vector3(0, 0.25f, 0.25f), new Vector3(0.14f, 0.07f, 0.12f), 0.01f, Skin(Brass), 2);   // the gearbox
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
                // road wheels and the skirt
                for (int k = 0; k < 5; k++)
                {
                    float z = -0.3f + k * 0.15f;
                    string set = k < 3 ? "road wheel set a" : "road wheel set b";
                    b.Lathe(new Vector3(side * (TrackX + TrackW / 2 + 0.002f), 0.065f, z), Disc(0.05f, 0.016f, 0.004f), M(set, Brass), SideRot(side), 18);
                    b.Lathe(new Vector3(side * (TrackX + TrackW / 2 + 0.018f), 0.065f, z), Dome(0.02f, 0.01f), M(set, Gunmetal), SideRot(side), 12);
                }
                float sx = side * (TrackX + TrackW / 2 + 0.008f);
                b.ChamferBox(new Vector3(sx, 0.18f, 0), new Vector3(0.016f, 0.13f, 0.62f), 0.006f, M("chassis", ToyYellow), null, true);
                b.ChamferBox(new Vector3(side * (TrackX + TrackW / 2 + 0.017f), 0.18f, 0), new Vector3(0.006f, 0.02f, 0.16f), 0.002f, M("chassis", Dark));
                foreach (var z in new[] { -0.19f, 0.19f })   // the skirt is three bolted plates
                    SeamLine(b, "chassis", new Vector3(side * (TrackX + TrackW / 2 + 0.016f), 0.122f, z), new Vector3(side * (TrackX + TrackW / 2 + 0.016f), 0.238f, z), new Vector3(side, 0, 0));
                foreach (var z in new[] { -0.28f, -0.1f, 0.1f, 0.28f })
                    foreach (var y in new[] { 0.13f, 0.225f })
                        Bolt(b, new Vector3(side * (TrackX + TrackW / 2 + 0.016f), y, z), new Vector3(side, 0, 0), 0.007f, M("chassis", Gunmetal));
                // the skirt is the most worn paint: grime along its bottom, grease from the bolts
                GrimeBand(b, "chassis", new Vector3(side * (TrackX + TrackW / 2 + 0.0165f), 0.116f, 0), X(side), new Vector3(0, 0, 1), 0.6f, 0.03f);
                foreach (var z in new[] { -0.1f, 0.28f }) GreaseRun(b, "chassis", new Vector3(side * (TrackX + TrackW / 2 + 0.0165f), 0.215f, z), X(side), 0.05f, 0.01f);
                Done(b, body);
                // cleats: the top run moves forward relative to the hull, the bottom run back (it stands on the floor)
                var c = new Batch();
                for (int k = -8; k <= 8; k++)
                    c.RoundedBox(new Vector3(x, EndY + EndR + 0.004f, k * Cleat), new Vector3(TrackW + 0.004f, 0.012f, 0.02f), 0.004f, M(track, Gunmetal), 1);
                Done(c, side < 0 ? _treadL : _treadR);
                var cb = new Batch();
                for (int k = -8; k <= 8; k++)
                    cb.RoundedBox(new Vector3(x, -0.002f, k * Cleat), new Vector3(TrackW + 0.004f, 0.012f, 0.02f), 0.004f, M(track, Gunmetal), 1);
                Done(cb, side < 0 ? _treadLB : _treadRB);
                foreach (var spr in side < 0 ? new[] { _sprocketLF, _sprocketLR } : new[] { _sprocketRF, _sprocketRR })
                {
                    var s = new Batch();
                    s.Lathe(Vector3.zero, Disc(SprocketR, 0.025f, 0.006f), M(sprocket, ToyRed), SideRot(side), 28);
                    s.Lathe(X(side * 0.025f), Disc(0.07f, 0.008f, 0.003f), M(sprocket, Gunmetal), SideRot(side), 24);
                    Hub(s, sprocket, X(side * 0.033f), side, 0.05f, 6, 0.0075f);   // the family bearing hub
                    for (int k = 0; k < 8; k++)   // the drive teeth round the rim (they show the sprocket turning)
                    {
                        float a = k * Mathf.PI / 4;
                        s.ChamferBox(new Vector3(side * 0.0125f, Mathf.Sin(a) * (SprocketR + 0.004f), Mathf.Cos(a) * (SprocketR + 0.004f)), new Vector3(0.02f, 0.014f, 0.022f), 0.003f, M(sprocket, Gunmetal), Batch.R(Vector3.right, -a));
                    }
                    Done(s, spr);
                }
            }
            {
                var b = new Batch();
                // chassis deck between the tracks, front block under the head, motor and battery
                b.ChamferBox(new Vector3(0, 0.2f, 0), new Vector3(0.33f, 0.26f, 1.0f), 0.03f, M("chassis", ToyYellow), null, true);
                b.ChamferBox(new Vector3(0, 0.322f, 0.02f), new Vector3(0.68f, 0.025f, 0.96f), 0.008f, M("chassis", ToyYellowDeep), null, true);
                // the front block: as wide as the head, the arms on its corners (the sheet)
                b.ChamferBox(new Vector3(0, 0.41f, -0.39f), new Vector3(0.48f, 0.16f, 0.36f), 0.03f, M("chassis", ToyYellow), null, true);
                b.ChamferBox(new Vector3(0, 0.41f, -0.573f), new Vector3(0.15f, 0.07f, 0.014f), 0.006f, M("chassis", ToyYellowDeep), null, true);
                b.ChamferBox(new Vector3(0, 0.41f, -0.581f), new Vector3(0.1f, 0.018f, 0.006f), 0.002f, M("chassis", ToyRed));
                b.ChamferBox(new Vector3(0, 0.41f, -0.584f), new Vector3(0.085f, 0.008f, 0.004f), 0.001f, M("chassis", Dark));
                foreach (var px in new[] { -0.062f, 0.062f }) Bolt(b, new Vector3(px, 0.41f, -0.58f), new Vector3(0, 0, -1), 0.0065f, M("chassis", Gunmetal));
                PanelFrame(b, "chassis", new Vector3(0, 0.41f, -0.39f), new Vector3(0.48f, 0.16f, 0.36f), new Vector3(0, 0, -1), 0.03f, 0.007f, 0.08f);
                FramedGrille(b, "chassis", new Vector3(0, 0.2f, -0.501f), 0.16f, 0.08f, 5, false, -1);
                foreach (var n in new[] { Vector3.left, Vector3.right }) PanelFrame(b, "chassis", new Vector3(0, 0.41f, -0.39f), new Vector3(0.48f, 0.16f, 0.36f), n, 0.042f);
                GrimeBand(b, "chassis", new Vector3(0, 0.335f, -0.571f), new Vector3(0, 0, -1), Vector3.right, 0.46f, 0.02f);
                foreach (var s in new[] { -1f, 1f })
                {
                    Pipe(b, "chassis", new[] { new Vector3(s * 0.2f, 0.47f, -0.21f), new Vector3(s * 0.22f, 0.45f, -0.17f), new Vector3(s * 0.24f, 0.37f, -0.12f), new Vector3(s * 0.26f, 0.338f, -0.06f) }, 0.009f, Brass, Gunmetal);
                    Pipe(b, "chassis", new[] { new Vector3(s * 0.15f, 0.47f, -0.21f), new Vector3(s * 0.16f, 0.46f, -0.16f), new Vector3(s * 0.19f, 0.39f, -0.11f), new Vector3(s * 0.2f, 0.338f, -0.05f) }, 0.0075f, Rubber, Brass);
                    // shoulder flanges on the block's corners, bolted; the shoulder drums turn against them
                    // a bolted steel bracket carries the shoulder out beside the block (the arms clear the head)
                    b.ChamferBox(new Vector3(s * 0.262f, ShoulderY, -ShoulderZ + 0.005f), new Vector3(0.044f, 0.1f, 0.1f), 0.008f, M("chassis", Gunmetal));
                    foreach (var y in new[] { -0.034f, 0.034f })
                        foreach (var z in new[] { -0.034f, 0.034f })
                            Bolt(b, new Vector3(s * 0.285f, ShoulderY + y, -ShoulderZ + 0.005f + z), X(s), 0.0055f, M("chassis", Gunmetal));
                    GreaseRun(b, "chassis", new Vector3(s * 0.24f, ShoulderY - 0.05f, -ShoulderZ + 0.03f), X(s), 0.06f, 0.012f);
                    // the "pecs": a bolted housing over each front corner of the block, over the shoulder shaft
                    // (it turns inside them, out of sight); clear of the shoulder drums and the head above
                    var pec = new Vector3(s * 0.172f, 0.4175f, -0.588f);
                    var pecSize = new Vector3(0.18f, 0.125f, 0.06f);
                    b.ChamferBox(pec, pecSize, 0.02f, M("chassis", ToyYellow), null, true);
                    b.ChamferBox(pec + new Vector3(0, 0, -0.031f), new Vector3(0.13f, 0.08f, 0.006f), 0.004f, M("chassis", ToyYellowDeep), null, true);
                    PanelFrame(b, "chassis", pec, pecSize, new Vector3(0, 0, -1), 0.016f, 0.0055f, 0.055f);
                    GrimeBand(b, "chassis", pec + new Vector3(0, -0.0625f + 0.001f, -0.031f), new Vector3(0, 0, -1), Vector3.right, 0.16f, 0.018f);
                }
                b.ChamferBox(new Vector3(0, 0.2f, 0.505f), new Vector3(0.16f, 0.1f, 0.02f), 0.008f, M("battery pack", Gunmetal), null, true);
                FramedGrille(b, "battery pack", new Vector3(0, 0.2f, 0.516f), 0.12f, 0.06f, 5, false, 1);
                b.Lathe(new Vector3(-0.08f, 0.26f, 0.38f), Disc(0.045f, 0.16f, 0.008f), M("drive motor", Gunmetal), RightRot, 16);
                for (int k = 0; k < 3; k++) b.Lathe(new Vector3(0, 0.488f + k * 0.006f, -0.38f), Disc(0.07f - k * 0.006f, 0.006f, 0.002f), M("chassis", Gunmetal), null, 16);
                Done(b, _chassis);
            }
            for (int i = 0; i < 3; i++)
            {
                var b = new Batch();
                string P = BoxPart[i];
                float w = 0.5f, d = 0.25f, h = 0.22f, t = 0.016f;
                b.ChamferBox(new Vector3(0, 0.01f, 0), new Vector3(w, 0.02f, d), 0.005f, M(P, ToyYellowDeep));
                foreach (var s in new[] { -1f, 1f })
                {
                    b.ChamferBox(new Vector3(s * (w / 2 - t / 2), h / 2, 0), new Vector3(t, h, d), 0.006f, M(P, ToyYellow), null, true);
                    b.ChamferBox(new Vector3(0, h / 2, s * (d / 2 - t / 2)), new Vector3(w, h, t), 0.006f, M(P, ToyYellow), null, true);
                    // red slot handle on the outer walls, brass rim trims
                    b.ChamferBox(new Vector3(s * (w / 2 + 0.002f), h * 0.62f, 0), new Vector3(0.006f, 0.035f, 0.1f), 0.003f, M(P, ToyRed));
                    b.ChamferBox(new Vector3(s * (w / 2 + 0.004f), h * 0.62f, 0), new Vector3(0.004f, 0.016f, 0.075f), 0.001f, M(P, Dark));
                    b.RoundedBox(new Vector3(s * (w / 2 - t / 2), h + 0.006f, 0), new Vector3(t + 0.008f, 0.012f, d + 0.008f), 0.004f, M(P, Brass), 1);
                    b.RoundedBox(new Vector3(0, h + 0.006f, s * (d / 2 - t / 2)), new Vector3(w + 0.008f, 0.012f, t + 0.008f), 0.004f, M(P, Brass), 1);
                }
                foreach (var s in new[] { -1f, 1f })   // bolted plates on the walls
                {
                    PanelFrame(b, P, new Vector3(s * (w / 2 - t / 2), h / 2, 0), new Vector3(t, h, d), new Vector3(s, 0, 0), 0.024f, 0.006f, 0.07f);
                    PanelFrame(b, P, new Vector3(0, h / 2, s * (d / 2 - t / 2)), new Vector3(w, h, t), new Vector3(0, 0, s), 0.024f, 0.006f, 0.09f);
                }
                foreach (var x in new[] { -1f, 1f })
                    foreach (var z in new[] { -1f, 1f })
                        b.RoundedBox(new Vector3(x * (w / 2 - 0.004f), h / 2, z * (d / 2 - 0.004f)), new Vector3(0.022f, h + 0.01f, 0.022f), 0.006f, M(P, Brass), 1);   // corner posts
                GrimeBand(b, P, new Vector3(0, 0.001f, -d / 2 - 0.001f), new Vector3(0, 0, -1), Vector3.right, w - 0.04f, 0.025f);
                Done(b, _boxes[i]);
            }
            foreach (var (arm, s, part, claw) in new[] { (_armL, -1f, "left arm", "left claw"), (_armR, 1f, "right arm", "right claw") })
                FamilyArm(arm, s, part, new ArmSpec { paint = ToyYellow, paintDeep = ToyYellowDeep, red = ToyRed, clawScale = 1.08f, twist = 20f, clawPart = claw });
            FamilyHead(head, "head", new HeadSpec
            {
                w = 0.42f, h = 0.28f, d = 0.34f, eyes = 1, eyeScale = 1.55f, eyeY = 0.53f, earY = 0.5f, paint = ToyYellow,
                beacon = true, antennas = true, mouth = true, eyePart = "optical eye", antennaPart = "antenna pair", beaconPart = "siren light",
            });
        }

        /// <summary>The load rides in the front cargo box (the one the arms reach over the head): three
        /// across, two deep, then a second layer.</summary>
        public override (Transform parent, Vector3 local) CarrySlot(int i, int n)
        {
            int k = i % 6, layer = i / 6;
            return (_boxes[0], new Vector3(((k % 3) - 1) * 0.13f, 0.05f + layer * 0.06f, ((k / 3) - 0.5f) * 0.09f));
        }

        // ------------------------------------------------------------ Layer B: tracks and the heavy hull

        float _pitch, _pitchVel, _lastV, _stowNow;
        float _stowPitch = float.NaN;
        Quaternion _headLook = Quaternion.identity;
        RobotAnim _fa;

        /// <summary>Caterpillarbot's head: slow and steady, looks into a turn and at its work, barely scans.</summary>
        static readonly HeadAttention Attention = new() { aheadMax = 25f, scanDeg = 3f, scanRate = 0.4f, follow = 2.5f, watchWork = true };

        /// <summary>Arms' sideways angle at rest (deg): parallel, slightly in.</summary>
        const float RestInward = 4f;

        /// <summary>The arms' pitch (deg): low and forward at rest (the sheet's loader stance), held up in front
        /// with a part, raised to lift; reaching goes down towards the floor ahead.</summary>
        static float ArmPitch(float hold, float lift, float reach) => Mathf.Lerp(-38 * (1 - hold) - 70 * hold - 35 * lift, -12, reach);

        public override void ResetPose() { _pitch = _pitchVel = 0; _lastV = 0; }

        protected override void PoseForDepth(float depth) => PoseBody(_fa, depth);

        /// <summary>The hull and the arms for one frame at a pick-up depth: the hull stays planted (a slight
        /// nod onto the front idlers when reaching), the arms go down towards the part ahead.</summary>
        void PoseBody(RobotAnim a, float depth)
        {
            float reach = Mathf.Min(1, a.armReach * depth), deep = Mathf.Clamp01(a.armReach * depth - 1);
            float nod = 1.2f * Ease(reach) + 1.5f * deep + 0.6f * a.armHold;   // the arms' weight forward
            float rumble = Mathf.Sin(a.time * 23f) * 0.0018f * Mathf.Clamp01(a.speed01 + Mathf.Abs(a.turnRate) / 90f);
            var rot = Quaternion.Euler(_pitch + nod, 0, 0);
            var pivot = new Vector3(0, HullPivotY, 0);
            _chassis.localRotation = rot;
            _chassis.localPosition = pivot - rot * pivot + new Vector3(0, rumble, 0);
            float pitch = ArmPitch(a.armHold, a.armLift, reach) + 10f * deep;
            famArmPitch = pitch;
            float sway = Mathf.Sin(a.time * 1.3f) * 2f * (1 - Mathf.Max(a.armHold, reach));   // the idle arms drift a little
            _armL.localRotation = Quaternion.Euler(pitch + sway, 0, RestInward);
            _armR.localRotation = Quaternion.Euler(pitch - sway, 0, -RestInward);
            PoseElbows(a, 34 * (1 - _stowNow));
        }

        /// <summary>The loader (right) arm's roll inwards (deg) while stowing: enough to bring its gripper
        /// inside the front box's near wall, little enough to pass beside the (turned) head.</summary>
        const float StowRoll = 10f;

        /// <summary>The loader arm's pitch (deg) that puts its gripper over the front cargo box (solved once on
        /// the built model: the arm swings up past the vertical and back beside the head).</summary>
        float StowPitch()
        {
            if (!float.IsNaN(_stowPitch) || !HasGrippers) return float.IsNaN(_stowPitch) ? -220f : _stowPitch;
            SnapshotPose();
            var target = _boxes[0].position;
            float lo = -270f, hi = -170f;   // more negative = further back
            for (int it = 0; it < 12; it++)
            {
                float m = (lo + hi) * 0.5f;
                PoseTelescopes(0);
                _armR.localRotation = Quaternion.Euler(m, 0, -StowRoll);
                var el = _armR.Find("Elbow"); if (el != null) el.localRotation = Quaternion.identity;
                float ahead = Vector3.Dot(clawTipR.position - target, transform.forward);
                if (ahead > 0) hi = m; else lo = m;
            }
            RestorePose();
            _stowPitch = (lo + hi) * 0.5f;
            return _stowPitch;
        }

        /// <summary>The loader arm's pitch (deg) at the top of the throw: up, just short of the vertical, the
        /// arm straight (the part leaves the gripper above the shoulder and sails back over the head).</summary>
        const float TossPitch = -168f;

        public override Vector3? TossReleasePoint()
        {
            if (!HasGrippers) return null;
            SnapshotPose();
            PoseTelescopes(0);
            _armR.localRotation = Quaternion.Euler(TossPitch, 0, 0);
            var el = _armR.Find("Elbow"); if (el != null) el.localRotation = Quaternion.identity;
            var p = clawTipR.position;
            RestorePose();
            return p;
        }

        public override void Animate(RobotAnim a)
        {
            float s = Mathf.Max(1e-4f, transform.lossyScale.x);
            // treads: forward speed plus the difference for turning on the spot
            float turn = a.turnRate * Mathf.Deg2Rad * TrackX * s;   // turning right: left tread forward, right tread back
            float vl = (a.speed + turn) / s, vr = (a.speed - turn) / s;
            _offL = Mathf.Repeat(_offL + vl * a.dt, Cleat);
            _offR = Mathf.Repeat(_offR + vr * a.dt, Cleat);
            _treadL.localPosition = new Vector3(0, 0, _offL);    // the top run moves forward relative to the hull
            _treadR.localPosition = new Vector3(0, 0, _offR);
            _treadLB.localPosition = new Vector3(0, 0, -_offL);  // the bottom run stands on the floor: back relative to the hull
            _treadRB.localPosition = new Vector3(0, 0, -_offR);
            float rotL = vl * a.dt / SprocketR * Mathf.Rad2Deg, rotR = vr * a.dt / SprocketR * Mathf.Rad2Deg;
            _sprocketLF.localRotation *= Quaternion.Euler(rotL, 0, 0); _sprocketLR.localRotation *= Quaternion.Euler(rotL, 0, 0);
            _sprocketRF.localRotation *= Quaternion.Euler(rotR, 0, 0); _sprocketRR.localRotation *= Quaternion.Euler(rotR, 0, 0);
            if (a.dt > 0)
            {
                // the heavy hull: squats back a little when it pulls away, dips when it brakes; a stiff,
                // well-damped spring (no overshoot to speak of, never a wobble)
                float dt = Mathf.Min(a.dt, 0.05f);
                float accel = (a.speed - _lastV) / dt; _lastV = a.speed;
                float target = -Mathf.Clamp(accel * 3f, -1.6f, 1.6f);
                const float k = 90f, damp = 2f * 0.95f * 9.49f;   // stiffness, damping (zeta 0.95)
                _pitchVel += ((target - _pitch) * k - _pitchVel * damp) * dt;
                _pitch += _pitchVel * dt;
            }
            _stowNow = Ease(a.stow);
            _fa = a;
            PoseBody(a, 1);
            FloorGuard(a);
            PoseTelescopes(0);
            FamilyAim(a, ArmPitch(a.armHold, a.armLift, a.armReach), RestInward);
            FamilyGrip(a, RestInward);
            FamilyClaws(a);
            if (_stowNow > 0)
            {
                // loading or unloading the front box: the right (loader) arm alone swings the part up and back
                // beside the head; the left arm lets go and settles at its rest
                float rest = ArmPitch(0, 0, 0);
                _armR.localRotation = Quaternion.Slerp(_armR.localRotation, Quaternion.Euler(StowPitch(), 0, -StowRoll), _stowNow);
                _armL.localRotation = Quaternion.Slerp(_armL.localRotation, Quaternion.Euler(rest, 0, RestInward), Mathf.Clamp01(_stowNow * 2));
                var elL = _armL.Find("Elbow"); if (elL != null) elL.localRotation = Quaternion.Euler(-34f * Mathf.Clamp01(_stowNow * 2), 0, 0);   // it keeps its ready bend
            }
            if (a.toss > 0)
            {
                // the throw: the loader arm swings up straight to above the shoulder; the left arm settles
                float rest = ArmPitch(0, 0, 0);
                _armR.localRotation = Quaternion.Slerp(_armR.localRotation, Quaternion.Euler(TossPitch, 0, 0), a.toss);
                var elR = _armR.Find("Elbow"); if (elR != null) elR.localRotation = Quaternion.Slerp(elR.localRotation, Quaternion.identity, a.toss);
                _armL.localRotation = Quaternion.Slerp(_armL.localRotation, Quaternion.Euler(rest, 0, RestInward), Mathf.Clamp01(a.toss * 2));
            }
            if (a.gesture > 0)
            {
                float w = Mathf.Sin(a.gesture * Mathf.PI);
                head.localRotation = Quaternion.Euler(Mathf.Sin(a.gesture * Mathf.PI * 4) * 14 * w, 0, 0);   // a slow, dependable nod
                SetBeacon(Mathf.Repeat(a.gesture * 6, 1) < 0.5f);
                return;
            }
            head.localRotation = _headLook;   // the attention without the duck below (it eases from frame to frame)
            FamilyLookHead(a, Mathf.Clamp01(a.speed01 * 2), Attention);
            _headLook = head.localRotation;
            // while the loader arm passes beside it the head turns to watch the part go into the box
            if (_stowNow > 0) head.localRotation = Quaternion.Slerp(head.localRotation, Quaternion.Euler(-8f, 90f, 0), Ease(Mathf.Clamp01(_stowNow * 1.6f)));
            if (a.toss > 0) head.localRotation = Quaternion.Slerp(head.localRotation, Quaternion.Euler(-14f, 0, 0), Ease(a.toss));   // it looks up at the throw
        }
    }
}
