using UnityEngine;
using static WorkshopWreckage.Batch;

namespace WorkshopWreckage
{
    /// <summary>The engine lathe on the south wall (furniture "machine", 2.4 x 1.2 x 0.9 footprint; playtest 2026-11-02:
    /// "look a lot more like a lathe, not just boxes and cylinders"). Layout-local, origin on the floor in the middle of
    /// the footprint, the bed along X, the operator's side at -Z (towards the room), the headstock on the operator's
    /// left (+X). Two cabinet stands under a chip pan, the bed with its ways, rack, leadscrew and feed rod, the
    /// headstock with its levers over a quick-change gearbox, the chuck holding a turned bar, the carriage (saddle,
    /// apron and its handwheel, cross slide, compound rest, toolpost and tool), the tailstock with its quill and
    /// handwheel, the end gear guard, the motor at the back, a rear splash guard and a gooseneck work lamp.
    /// Spindle (chuck and bar), apron handwheel and the lamp come alive when the machine is restored (Game).</summary>
    public static class LatheModel
    {
        /// <summary>Top of the bed's ways: what a part lying on the bed rests on.</summary>
        public const float WaysTop = 0.92f;
        /// <summary>The open stretch of bed between the tailstock and the carriage (local x, z): the only place
        /// loose parts lie on the lathe, clear of every housing and under nothing that turns.</summary>
        public static readonly Rect FreeBed = new Rect(-0.60f, -0.17f, 0.66f, 0.34f);
        /// <summary>How high parts may pile on the free bed (well under the spindle axis).</summary>
        public const float FreeBedPile = 0.2f;

        const float Axis = 1.14f;        // spindle centre height
        const float HeadFace = 0.62f;    // the headstock's face towards the work (x)
        const float BedTop = 0.895f;

        static Vector3 V(float x, float y, float z) => new Vector3(x, y, z);
        static readonly Quaternion AlongX = R(Vector3.forward, -Mathf.PI / 2);     // local Y -> +X (Lathe profiles, cylinders)
        static readonly Quaternion AlongNegX = R(Vector3.forward, Mathf.PI / 2);   // local Y -> -X
        static readonly Quaternion AlongZ = R(Vector3.right, Mathf.PI / 2);        // local Y -> Z

        public static void Build(Transform g, Batch b, WorkshopBuilder wb)
        {
            var iron = EnvMaterials.Metal(new Color(0.2f, 0.28f, 0.24f), 0.4f);
            var ironDark = EnvMaterials.Metal(new Color(0.13f, 0.17f, 0.15f), 0.3f);
            var steel = EnvMaterials.Plain(new Color(0.6f, 0.6f, 0.62f), 1, 0.7f);
            var steelDark = EnvMaterials.Plain(new Color(0.3f, 0.3f, 0.32f), 1, 0.5f);
            var black = EnvMaterials.Plain(new Color(0.06f, 0.06f, 0.07f), 0.2f, 0.6f);
            var pan = EnvMaterials.Metal(new Color(0.24f, 0.25f, 0.26f), 0.5f);
            var brass = EnvMaterials.Plain(new Color(0.72f, 0.56f, 0.26f), 1, 0.55f);
            var plate = EnvMaterials.Plain(new Color(0.82f, 0.8f, 0.72f), 0, 0.3f);
            var red = EnvMaterials.Plain(new Color(0.62f, 0.08f, 0.06f), 0.1f, 0.55f);
            var yellow = EnvMaterials.Plain(new Color(0.85f, 0.65f, 0.1f), 0.1f, 0.45f);

            void Solid(Vector3 c, Vector3 size) => b.colliders.Add((G(c), size, Quaternion.identity));
            // a lever: a stem from `root` along `dir` ending in a black ball
            void Lever(Vector3 root, Vector3 dir, float len, float ball)
            {
                var tip = root + dir.normalized * len;
                b.Loft(root, tip, V(0.012f, 0.012f), V(0.009f, 0.009f), 0.004f, 0.003f, steel);
                Ball(tip, ball, black);
            }
            void Ball(Vector3 c, float r, Material m) =>
                b.Lathe(c, new[] { V2(0, -r), V2(r * 0.5f, -r * 0.87f), V2(r * 0.87f, -r * 0.5f), V2(r, 0), V2(r * 0.87f, r * 0.5f), V2(r * 0.5f, r * 0.87f), V2(0, r) }, m, null, 10);

            // --- cabinet stands, toe plinths, doors -------------------------------------------------
            foreach (var (cx, w) in new[] { (0.86f, 0.56f), (-0.9f, 0.44f) })
            {
                b.Bevel(V(cx, 0.39f, 0), V(w, 0.66f, 0.46f), 0.02f, iron, true);
                b.Box(V(cx, 0.03f, 0), V(w - 0.04f, 0.06f, 0.42f), ironDark);
                b.ChamferBox(V(cx, 0.4f, -0.234f), V(w - 0.1f, 0.5f, 0.012f), 0.004f, iron);                 // door
                b.ChamferBox(V(cx + (w / 2 - 0.09f) * (cx > 0 ? -1 : 1), 0.46f, -0.245f), V(0.016f, 0.1f, 0.012f), 0.003f, steelDark);   // handle
                for (int k = 0; k < 4; k++)                                                                   // vent slots
                    b.Box(V(cx, 0.2f + k * 0.03f, -0.241f), V(w * 0.45f, 0.008f, 0.004f), black);
            }

            // --- chip pan on the stands -----------------------------------------------------------------
            b.Box(V(0, 0.735f, -0.03f), V(2.32f, 0.03f, 0.6f), pan, true);
            b.Box(V(0, 0.77f, -0.325f), V(2.32f, 0.05f, 0.012f), pan);
            b.Box(V(0, 0.77f, 0.265f), V(2.32f, 0.05f, 0.012f), pan);
            foreach (var sx in new[] { -1f, 1f }) b.Box(V(sx * 1.155f, 0.77f, -0.03f), V(0.012f, 0.05f, 0.6f), pan);

            // --- bed, ways, rack ---------------------------------------------------------------------------
            b.Bevel(V(0, (0.75f + BedTop) / 2, 0), V(2.3f, BedTop - 0.75f, 0.34f), 0.012f, iron, true);
            for (int k = 0; k < 7; k++)                                                                    // cast ribs on the front
                b.Box(V(-1.05f + k * 0.26f, 0.79f, -0.172f), V(0.03f, 0.07f, 0.008f), iron);
            float waysFrom = -1.15f, waysTo = HeadFace, waysLen = waysTo - waysFrom, waysX = (waysFrom + waysTo) / 2;
            float vh = (WaysTop - BedTop) * 1.25f / Mathf.Sqrt(2);   // V-way prism: a square turned 45 degrees, its top edge at WaysTop
            foreach (var z in new[] { -0.15f, 0.06f })
                b.Box(V(waysX, WaysTop - vh / Mathf.Sqrt(2), z), V(waysLen, vh, vh), steel, false, R(Vector3.right, Mathf.PI / 4));
            foreach (var z in new[] { -0.06f, 0.15f })
                b.ChamferBox(V(waysX, (BedTop + WaysTop) / 2, z), V(waysLen, WaysTop - BedTop, 0.045f), 0.004f, steel);
            b.Box(V(waysX - 0.02f, 0.862f, -0.176f), V(waysLen - 0.04f, 0.022f, 0.008f), steelDark);       // rack
            for (float x = waysFrom + 0.03f; x < waysTo - 0.04f; x += 0.016f)
                b.Box(V(x, 0.862f, -0.182f), V(0.007f, 0.022f, 0.006f), steelDark);

            // --- leadscrew, feed rod, end bracket ---------------------------------------------------------
            float rodFrom = -1.12f, rodTo = 0.62f;
            {
                const float pitch = 0.01f, r = 0.012f;
                int n = Mathf.RoundToInt((rodTo - rodFrom) / pitch);
                var prof = new Vector2[2 * n + 1];
                for (int k = 0; k <= 2 * n; k++) prof[k] = V2(k % 2 == 0 ? r * 0.82f : r, k * pitch / 2);
                b.Lathe(V(rodFrom, 0.81f, -0.215f), prof, steelDark, AlongX, 8, true);                     // leadscrew
            }
            b.Cylinder(V((rodFrom + rodTo) / 2, 0.775f, -0.215f), 0.009f, rodTo - rodFrom, steel, AlongX, 10);   // feed rod
            b.ChamferBox(V(-1.13f, 0.795f, -0.21f), V(0.04f, 0.09f, 0.08f), 0.008f, iron);

            // --- quick-change gearbox under the headstock --------------------------------------------------
            b.Bevel(V(0.86f, 0.83f, -0.235f), V(0.5f, 0.15f, 0.13f), 0.015f, iron, true);
            b.Box(V(0.86f, 0.85f, -0.302f), V(0.3f, 0.08f, 0.004f), brass);                                // thread chart
            for (int k = 0; k < 6; k++) b.Box(V(0.74f + k * 0.048f, 0.85f, -0.305f), V(0.03f, 0.05f, 0.002f), plate);
            foreach (var x in new[] { 0.66f, 1.06f }) Lever(V(x, 0.83f, -0.3f), V(0, 0.25f, -1), 0.06f, 0.016f);
            b.Cylinder(V(0.86f, 0.785f, -0.305f), 0.022f, 0.02f, black, AlongZ, 14);                       // feed selector

            // --- headstock -------------------------------------------------------------------------------------
            float hx0 = HeadFace, hx1 = 1.12f, hy1 = 1.36f;
            b.ChamferBox(V((hx0 + hx1) / 2, (BedTop + hy1) / 2, 0), V(hx1 - hx0, hy1 - BedTop, 0.42f), 0.03f, iron);
            b.ChamferBox(V((hx0 + hx1) / 2 - 0.005f, hy1 + 0.015f, 0), V(hx1 - hx0 + 0.02f, 0.03f, 0.44f), 0.01f, iron);   // lid
            Solid(V((hx0 + hx1) / 2, (BedTop + hy1 + 0.03f) / 2, 0), V(hx1 - hx0, hy1 + 0.03f - BedTop, 0.44f));
            b.Box(V(0.86f, 1.04f, -0.212f), V(0.2f, 0.11f, 0.004f), plate);                                 // speed chart
            for (int k = 0; k < 4; k++) b.Box(V(0.86f, 1.07f - k * 0.022f, -0.215f), V(0.16f, 0.006f, 0.002f), black);
            foreach (var (x, y) in new[] { (0.7f, 1.24f), (0.84f, 1.24f), (0.98f, 1.2f) })
            {
                b.Cylinder(V(x, y, -0.215f), 0.025f, 0.012f, steelDark, AlongZ, 14);
                Lever(V(x, y, -0.22f), V(0, 0.6f, -1), 0.09f, 0.02f);
            }
            b.Cylinder(V(1.06f, 1.31f, -0.214f), 0.028f, 0.012f, yellow, AlongZ, 16);                       // stop button
            b.Cylinder(V(1.06f, 1.31f, -0.226f), 0.018f, 0.024f, red, AlongZ, 16);
            b.Cylinder(V(HeadFace - 0.015f, Axis, 0), 0.085f, 0.03f, iron, AlongX, 20);                     // spindle bearing boss

            // --- end gear guard and motor -------------------------------------------------------------------
            b.RoundedBox(V(1.155f, 1.0f, 0), V(0.07f, 0.56f, 0.42f), 0.025f, iron);
            Solid(V(1.155f, 1.0f, 0), V(0.07f, 0.56f, 0.42f));
            b.Cylinder(V(0.86f, 0.52f, 0.33f), 0.095f, 0.24f, ironDark, AlongX, 20);
            for (int k = 0; k < 6; k++) b.Cylinder(V(0.76f + k * 0.04f, 0.52f, 0.33f), 0.1f, 0.008f, ironDark, AlongX, 20);   // cooling fins
            b.Box(V(0.86f, 0.42f, 0.33f), V(0.2f, 0.03f, 0.16f), ironDark);

            // --- rear splash guard -----------------------------------------------------------------------------
            b.Box(V(-0.27f, 1.0f, 0.25f), V(1.76f, 0.5f, 0.01f), iron, true);
            b.Box(V(-0.27f, 1.25f, 0.24f), V(1.76f, 0.012f, 0.03f), iron);

            // --- spindle: chuck and the turned bar (turns once the machine is restored) ---------------------
            wb.machineSpindle = new GameObject("Spindle").transform;
            wb.machineSpindle.SetParent(g, false);
            wb.machineSpindle.localPosition = G(HeadFace - 0.03f, Axis, 0);
            var sb = new Batch();
            sb.Cylinder(V(-0.015f, 0, 0), 0.06f, 0.03f, steel, AlongX, 20);                                  // spindle nose
            sb.Lathe(V(-0.03f, 0, 0), new[] { V2(0.07f, 0), V2(0.112f, 0.006f), V2(0.115f, 0.02f), V2(0.115f, 0.085f), V2(0.106f, 0.098f), V2(0.035f, 0.098f), V2(0, 0.098f) }, steel, AlongNegX, 28, true);
            for (int k = 0; k < 3; k++)
            {
                float a = k * Mathf.PI * 2 / 3;
                var rot = R(Vector3.right, a);
                Vector3 Rad(float x, float r) => V(x, Mathf.Cos(a) * r, Mathf.Sin(a) * r);
                sb.Box(Rad(-0.145f, 0.066f), V(0.035f, 0.05f, 0.024f), steelDark, false, rot);                   // jaw, stepped
                sb.Box(Rad(-0.138f, 0.098f), V(0.02f, 0.025f, 0.024f), steelDark, false, rot);
                sb.Box(Rad(-0.11f, 0.116f), V(0.016f, 0.004f, 0.012f), black, false, R(Vector3.right, a + Mathf.PI / 3));   // key socket
            }
            sb.Lathe(V(0, 0, 0), new[] { V2(0.028f, 0.13f), V2(0.028f, 0.3f), V2(0.023f, 0.304f), V2(0.022f, 0.306f), V2(0.022f, 0.39f), V2(0.018f, 0.4f), V2(0, 0.4f) }, steel, AlongNegX, 20);
            sb.Build(wb.machineSpindle, "spindle");

            // --- carriage: saddle, apron, cross slide, compound, toolpost -------------------------------------
            const float cx0 = 0.1f, cx1 = 0.4f, ccx = (cx0 + cx1) / 2;
            b.ChamferBox(V(ccx, WaysTop + 0.025f, 0), V(cx1 - cx0, 0.05f, 0.4f), 0.01f, iron);
            b.ChamferBox(V(ccx, WaysTop + 0.022f, -0.17f), V(cx1 - cx0 + 0.12f, 0.044f, 0.1f), 0.01f, iron);   // front wings
            b.Bevel(V(ccx, 0.885f, -0.245f), V(0.38f, 0.17f, 0.09f), 0.012f, iron);                          // apron
            Solid(V(ccx, 0.885f, -0.08f), V(0.38f, 0.17f, 0.42f));
            Solid(V(ccx, WaysTop + 0.08f, 0), V(cx1 - cx0, 0.16f, 0.4f));
            Lever(V(0.12f, 0.84f, -0.29f), V(-0.4f, -0.5f, -1), 0.07f, 0.016f);                               // half-nut lever
            b.Cylinder(V(0.2f, 0.93f, -0.296f), 0.018f, 0.014f, black, AlongZ, 14);                           // feed knob
            b.Cylinder(V(0.36f, 0.88f, -0.293f), 0.03f, 0.008f, steelDark, AlongZ, 16);                      // handwheel boss
            b.ChamferBox(V(0.25f, 0.99f, -0.03f), V(0.16f, 0.04f, 0.34f), 0.008f, iron);                       // cross slide
            b.Cylinder(V(0.25f, 0.99f, -0.215f), 0.028f, 0.02f, steel, AlongZ, 18);                            // its dial and crank
            b.Box(V(0.25f, 1.015f, -0.23f), V(0.012f, 0.06f, 0.008f), steel);
            Lever(V(0.25f, 1.04f, -0.234f), V(0, 0, -1), 0.04f, 0.012f);
            b.Cylinder(V(0.27f, 1.02f, -0.05f), 0.06f, 0.02f, iron, null, 20);                                 // compound base
            var compound = R(Vector3.up, 0.5f);
            b.ChamferBox(V(0.27f, 1.048f, -0.05f), V(0.08f, 0.035f, 0.2f), 0.006f, iron, compound);
            var cEnd = V(0.27f, 1.048f, -0.05f) + V(-Mathf.Sin(0.5f), 0, -Mathf.Cos(0.5f)) * 0.11f;
            Ball(cEnd + V(0, 0.012f, 0), 0.012f, black);
            b.ChamferBox(V(0.27f, 1.105f, -0.095f), V(0.065f, 0.08f, 0.065f), 0.006f, steelDark);              // four-way toolpost
            b.Box(V(0.27f, Axis - 0.006f, -0.06f), V(0.014f, 0.014f, 0.075f), steel);                        // tool bit, up to the work
            foreach (var z in new[] { -0.075f, -0.115f }) b.Cylinder(V(0.27f, 1.152f, z), 0.008f, 0.014f, steel, null, 8);
            b.Cylinder(V(0.27f, 1.16f, -0.095f), 0.012f, 0.03f, steel, null, 10);
            Lever(V(0.27f, 1.17f, -0.095f), V(-1, 0.15f, -0.6f), 0.09f, 0.014f);

            // --- apron handwheel (turns while the machine runs; its own axis is the transform's local X) --------
            wb.machineHandwheel = new GameObject("Handwheel").transform;
            wb.machineHandwheel.SetParent(g, false);
            wb.machineHandwheel.localPosition = G(0.36f, 0.88f, -0.297f);
            wb.machineHandwheel.localRotation = Quaternion.Euler(0, -90, 0);   // local +X towards the operator (layout -Z)
            var hb = new Batch();
            var rim = new Vector2[13];
            for (int k = 0; k < 13; k++) { float t = k * Mathf.PI * 2 / 12; rim[k] = V2(0.085f + 0.009f * Mathf.Cos(t), 0.03f + 0.009f * Mathf.Sin(t)); }
            hb.Lathe(V(0, 0, 0), rim, steel, AlongX, 28);
            hb.Cylinder(V(0.02f, 0, 0), 0.022f, 0.04f, steel, AlongX, 14);
            for (int k = 0; k < 3; k++)
            {
                float a = k * Mathf.PI * 2 / 3 + 0.3f;
                hb.Box(V(0.026f, Mathf.Cos(a) * 0.052f, Mathf.Sin(a) * 0.052f), V(0.008f, 0.066f, 0.014f), steel, false, R(Vector3.right, a));
            }
            hb.Cylinder(V(0.07f, 0.085f, 0), 0.009f, 0.06f, black, AlongX, 10);
            hb.Build(wb.machineHandwheel, "handwheel");

            // --- tailstock --------------------------------------------------------------------------------------
            const float tx = -0.91f;
            b.ChamferBox(V(tx, WaysTop + 0.02f, 0), V(0.34f, 0.04f, 0.34f), 0.008f, iron);
            b.Loft(V(tx, WaysTop + 0.04f, 0), V(tx, Axis, 0), V(0.32f, 0.28f), V(0.26f, 0.15f), 0.02f, 0.03f, iron);
            b.Cylinder(V(tx, Axis, 0), 0.065f, 0.34f, iron, AlongX, 20);
            Solid(V(tx, (WaysTop + Axis + 0.065f) / 2, 0), V(0.34f, Axis + 0.065f - WaysTop, 0.34f));
            b.Cylinder(V(-0.71f, Axis, 0), 0.034f, 0.06f, steel, AlongX, 16);                                 // quill
            b.Lathe(V(-0.68f, Axis, 0), new[] { V2(0.024f, 0), V2(0.024f, 0.012f), V2(0, 0.055f) }, steel, AlongNegX, 16, true);   // dead centre
            b.Cylinder(V(-1.095f, Axis, 0), 0.02f, 0.03f, steel, AlongX, 12);                                  // handwheel
            b.Lathe(V(-1.09f, Axis, 0), rim, steel, AlongNegX, 24);
            for (int k = 0; k < 3; k++)
            {
                float a = k * Mathf.PI * 2 / 3;
                b.Box(V(-1.115f, Axis + Mathf.Cos(a) * 0.05f, Mathf.Sin(a) * 0.05f), V(0.008f, 0.066f, 0.012f), steel, false, R(Vector3.right, a));
            }
            b.Cylinder(V(-1.15f, Axis + 0.085f, 0), 0.009f, 0.05f, black, AlongX, 10);
            Lever(V(-0.8f, Axis + 0.065f, 0), V(0.2f, 1, -0.5f), 0.07f, 0.014f);                            // quill lock
            Lever(V(-0.82f, WaysTop + 0.03f, -0.17f), V(0.3f, 0.3f, -1), 0.08f, 0.016f);                    // clamp lever

            // --- gooseneck work lamp on the headstock (lights up once the machine runs) ------------------------
            var foot = V(0.68f, hy1 + 0.03f, 0.17f);
            b.Cylinder(foot + V(0, 0.01f, 0), 0.035f, 0.02f, black, null, 14);
            var p0 = foot + V(0, 0.02f, 0);
            var p1 = V(0.6f, 1.72f, 0.12f);
            var p2 = V(0.36f, 1.56f, 0.0f);
            var prev = p0;
            for (int k = 1; k <= 12; k++)
            {
                float t = k / 12f;
                var q = (1 - t) * (1 - t) * p0 + 2 * (1 - t) * t * p1 + t * t * p2;
                b.Loft(prev, q, V(0.018f, 0.018f), V(0.018f, 0.018f), 0.006f, 0.006f, steelDark);
                prev = q;
            }
            var shade = p2 - V(0, 0.01f, 0);
            var shadeProf = new[] { V2(0.064f, -0.08f), V2(0.06f, -0.075f), V2(0.035f, -0.03f), V2(0.016f, 0), V2(0.012f, 0.01f) };
            b.Lathe(shade, shadeProf, EnvMaterials.Plain(new Color(0.16f, 0.24f, 0.2f), 0.3f, 0.5f), null, 18, false);
            var inside = (Vector2[])shadeProf.Clone(); System.Array.Reverse(inside);
            b.Lathe(shade, inside, EnvMaterials.Plain(new Color(0.85f, 0.83f, 0.78f), 0, 0.3f), null, 18, false);
            Ball(shade + V(0, -0.045f, 0), 0.022f, EnvMaterials.Plain(new Color(1f, 0.93f, 0.78f), 0, 0.6f));
            var lg = new GameObject("WorkLamp");
            lg.transform.SetParent(g, false);
            lg.transform.localPosition = G(shade + V(0, -0.07f, 0));
            wb.machineLamp = lg.AddComponent<Light>();
            wb.machineLamp.type = LightType.Point;
            wb.machineLamp.color = new Color(1f, 0.85f, 0.6f);
            wb.machineLamp.range = 2.5f;
            wb.machineLamp.intensity = 0;
            wb.machineHum = g.gameObject.AddComponent<AudioSource>();
            wb.machineHum.spatialBlend = 1; wb.machineHum.minDistance = 1; wb.machineHum.maxDistance = 12; wb.machineHum.loop = true; wb.machineHum.volume = 0;
        }

        static Vector2 V2(float r, float y) => new Vector2(r, y);
        static Vector2 V(float x, float y) => new Vector2(x, y);
    }
}
