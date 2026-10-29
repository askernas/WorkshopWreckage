using System.Collections.Generic;
using System.IO;
using UnityEngine;
using static WorkshopWreckage.Batch;

namespace WorkshopWreckage
{
    /// <summary>The workshop's name sign: the official artwork (resources/Workshop Wreckage Sign.png, cut out as
    /// Resources/SignArt.png) built in depth from Resources/SignShape.txt (both made by tools/sign/make_sign_art.py).
    /// The artwork is split into layers - a steel housing (the frame), the backing panel recessed into it, the
    /// letters standing well proud of the panel, and the decoration round the housing - each a stack of strips at
    /// its own depth with the artwork on its front. Side walls are real faces: rusty steel on the housing, the
    /// artwork itself in shade on the letters and decoration. Seams, straps and bolts on the housing, and chains from
    /// lugs on its top to eyes in the wall. The O's get glass domes with real lights a little apart in strength
    /// and colour, so they read as old bulbs; the one in "SHOP" flickers now and then (SignLamps). A warm,
    /// shadowed spot from above models the letters. Every tunable value is in shared/data/workshop_sign.json (the
    /// defaults here are used when a value is missing). Godot-local, +Z out of the wall, centred on the sign
    /// fixture; the wall is 5 cm behind the fixture's centre.</summary>
    public static class WorkshopSign
    {
        const float Wall = -0.05f;   // the wall surface, sign-local
        static Dictionary<string, object> _d = new();
        static Vector3 _posG;        // where the sign hangs (room space, Godot)
        static float _halfWidth = -1;

        static Dictionary<string, object> Sec(string s) => Json.Obj(_d.GetValueOrDefault(s)) ?? new Dictionary<string, object>();
        static float F(string s, string k, float def) => (float)Json.Num(Sec(s), k, def);
        static float F(Dictionary<string, object> d, string k, float def) => (float)Json.Num(d, k, def);
        static Color C(Dictionary<string, object> d, string k, Color def) =>
            d.GetValueOrDefault(k) is List<object> a && a.Count >= 3 ? new Color(Json.F(a[0]), Json.F(a[1]), Json.F(a[2])) : def;
        static Color C(string s, string k, Color def) => C(Sec(s), k, def);
        static Vector2 Range(string s, string k, Vector2 def) =>
            Sec(s).GetValueOrDefault(k) is List<object> a && a.Count >= 2 ? new Vector2(Json.F(a[0]), Json.F(a[1])) : def;

        /// <summary>A ceiling fixture hanging just in front of the sign moves back into the room by
        /// "ceiling_fixture" move_m, so it neither hides the sign's top from the floor nor floods its face.
        /// Room space, Godot; call after the sign is built.</summary>
        public static Vector3 KeepFixtureClear(Vector3 pG)
        {
            if (_halfWidth < 0) return pG;
            float move = F("ceiling_fixture", "move_m", 1.2f), reach = F("ceiling_fixture", "within_m", 3f);
            bool inFront = Mathf.Abs(pG.x - _posG.x) < _halfWidth + 0.8f && pG.z > _posG.z && pG.z - _posG.z < reach;
            return inFront ? pG + new Vector3(0, 0, move) : pG;
        }

        public static void Build(Transform g, Batch b, Vector3 size)
        {
            var path = Shared.DataPath("workshop_sign.json");
            _d = File.Exists(path) ? Shared.ReadObject(path) : new Dictionary<string, object>();
            var tex = Resources.Load<Texture2D>("SignArt");
            var shape = Resources.Load<TextAsset>("SignShape");
            if (tex == null || shape == null) { Debug.LogWarning("[sign] SignArt/SignShape missing"); return; }
            tex.wrapMode = TextureWrapMode.Clamp;
            var art = EnvMaterials.Atlas(tex);
            art.name = "SignArt";
            art.SetFloat("_Smoothness", F("artwork", "face_smoothness", 0.3f));
            var artSide = new Material(art) { name = "SignArtSide" };   // the letters' side walls: the same paint, in shade
            float shade = F("depth", "side_shade", 0.5f);
            artSide.SetColor("_BaseColor", new Color(shade, shade, shade));
            var housing = Sec("housing");
            var rust = EnvMaterials.Metal(C(housing, "side_color", new Color(0.36f, 0.2f, 0.11f)), F(housing, "side_chips", 0.55f));
            var steel = EnvMaterials.Plain(C(housing, "hardware_color", new Color(0.3f, 0.28f, 0.26f)), 0.85f, 0.45f);
            float up = F("placement", "offset_up_m", 0.14f);

            // the layers, one row of pixels per strip
            var lines = shape.text.Split('\n');
            var dims = lines[0].Split(' ', System.StringSplitOptions.RemoveEmptyEntries);
            int pw = int.Parse(dims[0]), ph = int.Parse(dims[1]), row = dims.Length > 2 ? int.Parse(dims[2]) : 3;
            float s = F("placement", "width_m", 5.25f) / pw;
            Vector2 At(float px, float py) => new((px - pw / 2f) * s, (ph / 2f - py) * s + up);   // artwork pixels -> sign-local metres
            var hb = lines[1].Split(' ', System.StringSplitOptions.RemoveEmptyEntries);
            int hx0 = int.Parse(hb[1]), hy0 = int.Parse(hb[2]), hx1 = int.Parse(hb[3]), hy1 = int.Parse(hb[4]);
            int n = (ph + row - 1) / row;
            var cl = new byte[n + 2, pw + 2];   // layer per strip and pixel column, with an empty border all round
            for (int li = 2; li < lines.Length; li++)
            {
                var f = lines[li].Split(' ', System.StringSplitOptions.RemoveEmptyEntries);
                if (f.Length < 5) continue;
                int r = int.Parse(f[0]) / row + 1;
                for (int k = 2; k + 2 < f.Length; k += 3)
                    for (int x = int.Parse(f[k]), x1 = int.Parse(f[k + 1]); x < x1; x++) cl[r, x + 1] = byte.Parse(f[k + 2]);
            }

            // depths: metres out from the housing's back, which stands back_from_wall_m off the wall. Each letter
            // stands at letters_m plus its own offset, so they read as bolted on one by one, not stamped out.
            var dep = Sec("depth");
            float z0 = Wall + F(dep, "back_from_wall_m", 0.03f), housingD = F(dep, "housing_m", 0.12f), lettersD = F(dep, "letters_m", 0.18f);
            var lvl = new float[LetterCode + 16];
            lvl[1] = housingD; lvl[2] = F(dep, "panel_m", 0.09f); lvl[3] = lettersD; lvl[4] = F(dep, "decoration_m", 0.11f);
            var offs = dep.GetValueOrDefault("letter_offsets_m") as List<object>;
            for (int i = 0; i < 16; i++) lvl[LetterCode + i] = lettersD + (offs != null && i < offs.Count ? Json.F(offs[i]) : 0);
            float Y(int r) => (ph / 2f - (r - 1) * row) * s + up;              // top edge of strip r
            float X(float x) => (x - 1 - pw / 2f) * s;                         // left edge of pixel column x
            float V(float py) => 1 - Mathf.Clamp(py, 0, ph) / ph;               // texture v of an artwork row
            float Z(int c) => z0 + lvl[c];

            // each strip as segments covering its width (layer 0 = nothing). Boundary k of a strip lies between
            // segments k and k+1; where the same boundary carries on in the next strip (same two layers, a few
            // pixels along) the edge slopes through the shared line instead of stepping, so outlines run smooth.
            var segs = new List<(int x0, int x1, int c)>[n + 2];
            var top = new float[n + 2][]; var bot = new float[n + 2][];
            for (int r = 0; r <= n + 1; r++)
            {
                segs[r] = new List<(int, int, int)>();
                for (int x = 1; x <= pw; )
                {
                    int c = cl[r, x], x1 = x;
                    while (x1 <= pw && cl[r, x1] == c) x1++;
                    segs[r].Add((x, x1, c));
                    x = x1;
                }
                top[r] = new float[segs[r].Count - 1]; bot[r] = new float[segs[r].Count - 1];
                for (int k = 0; k < top[r].Length; k++) top[r][k] = bot[r][k] = segs[r][k].x1;
            }
            int reach = 2 * row;
            int Best(List<(int x0, int x1, int c)> a, int k, List<(int x0, int x1, int c)> b)
            {
                int best = -1; float d = reach + 0.5f;
                for (int j = 0; j + 1 < b.Count; j++)
                    if (b[j].c == a[k].c && b[j + 1].c == a[k + 1].c && Mathf.Abs(b[j].x1 - a[k].x1) < d) { d = Mathf.Abs(b[j].x1 - a[k].x1); best = j; }
                return best;
            }
            for (int r = 0; r <= n; r++)
                for (int k = 0; k + 1 < segs[r].Count; k++)
                {
                    int j = Best(segs[r], k, segs[r + 1]);
                    if (j < 0 || Best(segs[r + 1], j, segs[r]) != k) continue;
                    bot[r][k] = top[r + 1][j] = (segs[r][k].x1 + segs[r + 1][j].x1) / 2f;
                }
            for (int r = 0; r <= n + 1; r++)   // keep each strip's edges in order
                for (int k = 1; k < top[r].Length; k++) { top[r][k] = Mathf.Max(top[r][k], top[r][k - 1]); bot[r][k] = Mathf.Max(bot[r][k], bot[r][k - 1]); }

            var front = new Faces(); var sideArt = new Faces(); var sideRust = new Faces();
            Faces SideOf(int c) => c == 1 ? sideRust : sideArt;
            var outN = new Vector3(0, 0, 1);
            for (int r = 1; r <= n; r++)
            {
                float yt = Y(r), yb = Y(r + 1), vt = V((r - 1) * row), vb = V(r * row);
                var sg = segs[r];
                for (int k = 0; k < sg.Count; k++)
                {
                    float lt = k > 0 ? top[r][k - 1] : 1, lb = k > 0 ? bot[r][k - 1] : 1;
                    float rt = k + 1 < sg.Count ? top[r][k] : pw + 1, rb = k + 1 < sg.Count ? bot[r][k] : pw + 1;
                    int c = sg[k].c;
                    if (c != 0)   // the face, the artwork on it
                        front.Quad(new Vector3(X(lt), yt, Z(c)), new Vector3(X(rt), yt, Z(c)), new Vector3(X(rb), yb, Z(c)), new Vector3(X(lb), yb, Z(c)), outN,
                            new Vector2((lt - 1) / pw, vt), new Vector2((rt - 1) / pw, vt), new Vector2((rb - 1) / pw, vb), new Vector2((lb - 1) / pw, vb));
                    if (k + 1 >= sg.Count) continue;
                    int cr = sg[k + 1].c;
                    if (lvl[c] == lvl[cr]) continue;
                    // the side wall on this boundary: owned by the higher side, facing the lower
                    bool ownRight = lvl[cr] > lvl[c];
                    int own = ownRight ? cr : c, low = ownRight ? c : cr;
                    var pt = new Vector2(X(top[r][k]), yt); var pb = new Vector2(X(bot[r][k]), yb);
                    var d = (pb - pt).normalized;
                    var nrm = new Vector3(d.y, -d.x, 0) * (ownRight ? 1 : -1);
                    var ow = ownRight ? sg[k + 1] : sg[k];
                    int inset = Mathf.Min(3, (ow.x1 - ow.x0) / 2);
                    float u = (sg[k].x1 - 1 + (ownRight ? inset : -inset - 1) + 0.5f) / pw;
                    SideOf(own).Quad(new Vector3(pt.x, pt.y, Z(low)), new Vector3(pb.x, pb.y, Z(low)), new Vector3(pb.x, pb.y, Z(own)), new Vector3(pt.x, pt.y, Z(own)), nrm,
                        new Vector2(u, vt), new Vector2(u, vb), new Vector2(u, vb), new Vector2(u, vt));
                }
            }

            // the walls facing up and down: along each line between strips, wherever the layer above and the one
            // below stand at different depths, the higher one shows a step
            for (int r = 0; r <= n; r++)
            {
                var A = segs[r]; var B = segs[r + 1];
                float y = Y(r + 1);
                float EndA(int i) => i + 1 < A.Count ? bot[r][i] : pw + 1;
                float EndB(int j) => j + 1 < B.Count ? top[r + 1][j] : pw + 1;
                int ia = 0, ib = 0, pa = -1, pb = -1;
                float cur = 1, start = 1;
                void Flush(float end)
                {
                    if (pa < 0 || lvl[pa] == lvl[pb] || end - start < 0.01f) return;
                    bool under = lvl[pa] > lvl[pb];   // the strip above stands proud: its underside shows
                    int own = under ? pa : pb, low = under ? pb : pa;
                    float v = V(r * row + (under ? -1.5f : 1.5f)), u0 = (start - 1) / pw, u1 = (end - 1) / pw;
                    SideOf(own).Quad(new Vector3(X(start), y, Z(low)), new Vector3(X(end), y, Z(low)), new Vector3(X(end), y, Z(own)), new Vector3(X(start), y, Z(own)),
                        new Vector3(0, under ? -1 : 1, 0), new Vector2(u0, v), new Vector2(u1, v), new Vector2(u1, v), new Vector2(u0, v));
                }
                while (cur < pw + 1 && ia < A.Count && ib < B.Count)
                {
                    float e = Mathf.Min(EndA(ia), EndB(ib));
                    if (e > cur)
                    {
                        if (A[ia].c != pa || B[ib].c != pb) { Flush(cur); pa = A[ia].c; pb = B[ib].c; start = cur; }
                        cur = e;
                    }
                    if (EndA(ia) <= e) ia++;
                    if (EndB(ib) <= e) ib++;
                }
                Flush(cur);
            }
            front.Build(g, "SignFront", art);
            sideArt.Build(g, "SignSides", artSide);
            sideRust.Build(g, "SignHousing", rust);

            // the housing's hardware: seams with bolts along its exposed top and bottom, straps on its exposed sides
            float zMid = z0 + housingD / 2, bolt = F(housing, "bolt_radius_m", 0.011f), boltStep = F(housing, "bolt_spacing_m", 0.32f);
            int rTop = hy0 / row + 1, rBot = (hy1 - 1) / row + 1;
            void Seam(int rr, int rOut, float y, float dirY)
            {
                bool Open(int xx) => cl[rr, xx] == 1 && cl[rOut, xx] == 0;
                for (int x = hx0 + 1; x <= hx1; )
                {
                    if (!Open(x)) { x++; continue; }
                    int x1 = x;
                    while (x1 <= hx1 && Open(x1)) x1++;
                    float a = X(x) + 0.02f, e = X(x1) - 0.02f;
                    if (e - a > 0.12f)
                    {
                        b.Box(new Vector3((a + e) / 2, y + dirY * 0.002f, zMid), new Vector3(e - a, 0.004f, 0.012f), steel);
                        int k = Mathf.Max(1, Mathf.RoundToInt((e - a) / boltStep));
                        for (int i = 0; i <= k; i++)
                            foreach (var dz in new[] { -housingD * 0.3f, housingD * 0.3f })
                                b.Rivet(new Vector3(Mathf.Lerp(a + 0.03f, e - 0.03f, i / (float)k), y, zMid + dz), new Vector3(0, dirY, 0), bolt, steel);
                    }
                    x = x1;
                }
            }
            Seam(rTop, rTop - 1, Y(rTop), 1);
            Seam(rBot, rBot + 1, Y(rBot + 1), -1);
            foreach (var (col, outCol, dir) in new[] { (hx0 + 1, hx0, -1), (hx1, hx1 + 1, 1) })
            {
                bool Open(int rr) => cl[rr, col] == 1 && cl[rr, outCol] == 0;
                for (int r = rTop; r <= rBot; )
                {
                    if (!Open(r)) { r++; continue; }
                    int r1 = r;
                    while (r1 <= rBot && Open(r1)) r1++;
                    float yTop = Y(r) - 0.02f, yBot = Y(r1) + 0.02f, x = X(dir < 0 ? col : col + 1);
                    if (yTop - yBot > 0.1f)
                    {
                        b.Bevel(new Vector3(x + dir * 0.003f, (yTop + yBot) / 2, zMid), new Vector3(0.006f, yTop - yBot, housingD * 0.75f), 0.002f, steel);
                        foreach (var t in new[] { 0.15f, 0.85f })
                            foreach (var dz in new[] { -housingD * 0.25f, housingD * 0.25f })
                                b.Rivet(new Vector3(x + dir * 0.006f, Mathf.Lerp(yBot, yTop, t), zMid + dz), new Vector3(dir, 0, 0), bolt, steel);
                    }
                    r = r1;
                }
            }

            // chains: from lugs on the housing's top up to eyes in the wall
            var ch = Sec("chains");
            var links = EnvMaterials.Plain(C(ch, "link_color", new Color(0.42f, 0.38f, 0.34f)), 0.85f, 0.45f);
            var plate = EnvMaterials.Plain(C(ch, "plate_color", new Color(0.3f, 0.17f, 0.09f)), 0.5f, 0.3f);
            float rise = F(ch, "rise_m", 0.17f), yTopH = Y(rTop);
            var chainX = ch.GetValueOrDefault("x_px") is List<object> cx ? cx : new List<object> { 372.0, 1560.0 };
            foreach (var px in chainX)
            {
                float x = At(Json.F(px), 0).x;
                b.Bevel(new Vector3(x, yTopH + 0.003f, zMid), new Vector3(0.1f, 0.006f, housingD + 0.004f), 0.002f, steel);   // saddle strap over the top
                b.Bevel(new Vector3(x, yTopH + 0.035f, zMid), new Vector3(0.012f, 0.06f, 0.07f), 0.003f, steel);              // the lug
                foreach (var sx in new[] { -1f, 1f }) b.Rivet(new Vector3(x + sx * 0.035f, yTopH + 0.006f, zMid), Vector3.up, bolt * 0.8f, steel);
                Chain(b, new Vector3(x, yTopH + 0.06f, zMid), new Vector3(x, yTopH + rise, Wall + 0.035f), links, plate);
            }

            // the lamps: a brass bezel and a glass dome, hotter in the middle, over the painted glass; a real light
            var look = Sec("lamp_look");
            float rg = F(look, "glass_radius_px", 30f) * s, domeH = F(look, "dome_height_m", 0.04f);
            var brass = EnvMaterials.Plain(C(look, "bezel_color", new Color(0.6f, 0.46f, 0.24f)), 1f, 0.55f);
            var comp = g.gameObject.AddComponent<SignLamps>();
            var lamps = _d.GetValueOrDefault("lamps") is List<object> la ? la : new List<object>
            {
                new Dictionary<string, object> { ["x_px"] = 658.0, ["y_px"] = 278.0 },
                new Dictionary<string, object> { ["x_px"] = 1515.0, ["y_px"] = 290.0, ["strength"] = 0.84, ["flickers"] = true },
            };
            var outward = Quaternion.FromToRotation(Vector3.up, Vector3.back);   // lathe axis out of the sign (Godot +Z)
            for (int i = 0; i < lamps.Count; i++)
            {
                var lamp = Json.Obj(lamps[i]);
                if (lamp == null) continue;
                var c = At(F(lamp, "x_px", 0), F(lamp, "y_px", 0));
                int under = cl[Mathf.Clamp((int)F(lamp, "y_px", 0) / row + 1, 0, n + 1), Mathf.Clamp((int)F(lamp, "x_px", 0) + 1, 0, pw + 1)];
                float zf = z0 + lvl[under != 0 ? under : 3];   // on the face of its gear
                float strength = F(lamp, "strength", 1f);
                var tint = C(lamp, "tint", new Color(1f, 0.8f, 0.52f));
                b.Lathe(new Vector3(c.x, c.y, zf), new[] { new Vector2(rg + 0.018f, -0.002f), new Vector2(rg + 0.018f, 0.012f), new Vector2(rg + 0.007f, 0.021f), new Vector2(rg - 0.002f, 0.014f) }, brass, outward, 28);
                var rimMat = new Material(EnvMaterials.Plain(tint, 0, 0.85f)) { name = "SignLampGlass" + i };
                var coreMat = new Material(EnvMaterials.Plain(Color.Lerp(tint, Color.white, 0.4f), 0, 0.85f)) { name = "SignLampCore" + i };
                var dome = new List<Vector2>();
                for (int k = 0; k <= 8; k++) { float t = k / 8f * Mathf.PI / 2; dome.Add(new Vector2(Mathf.Cos(t) * rg, 0.004f + Mathf.Sin(t) * domeH)); }
                b.Lathe(new Vector3(c.x, c.y, zf), dome.GetRange(0, 5).ToArray(), rimMat, outward, 28);
                b.Lathe(new Vector3(c.x, c.y, zf), dome.GetRange(4, 5).ToArray(), coreMat, outward, 28);
                var lgo = new GameObject("SignLamp" + i);
                lgo.transform.SetParent(g, false);
                lgo.transform.localPosition = G(c.x, c.y, zf + F(look, "light_distance_out_m", 0.5f));   // out far enough to light the room, not just the gear round it
                var l = lgo.AddComponent<Light>();
                l.type = LightType.Point;
                l.range = F(look, "light_range_m", 2.2f);
                l.color = tint;
                l.shadows = LightShadows.None;
                comp.Add(l, F(look, "light_intensity", 0.6f) * strength, rimMat, F(look, "glass_glow", 2.4f) * strength, coreMat, F(look, "core_glow", 6f) * strength,
                    Json.Bool(lamp, "flickers"));
            }
            comp.firstAfter = Range("flicker", "first_after_s", new Vector2(4, 14));
            comp.every = Range("flicker", "every_s", new Vector2(10, 30));
            comp.burst = Range("flicker", "burst_s", new Vector2(0.2f, 0.9f));
            comp.step = Range("flicker", "step_s", new Vector2(0.03f, 0.12f));
            comp.dim = Range("flicker", "dim_level", new Vector2(0.06f, 0.55f));
            comp.fullOnChance = F("flicker", "full_on_chance", 0.3f);
            comp.drift = F("flicker", "drift", 0.03f);
            comp.still = Game.Instance != null && Game.Instance.Automated;   // tests and screenshots stay steady

            // a warm sign light from above: models the letters (they shadow the panel) and keeps the name readable
            var spot = Sec("spotlight");
            var sgo = new GameObject("SignLight");
            sgo.transform.SetParent(g, false);
            sgo.transform.localPosition = G(0, yTopH + F(spot, "above_m", 0.4f), F(spot, "out_m", 1.1f));
            sgo.transform.localRotation = Quaternion.LookRotation(G(0, up - F(spot, "aim_down_m", 0.2f), z0) - sgo.transform.localPosition);
            var sl = sgo.AddComponent<Light>();
            sl.type = LightType.Spot;
            sl.spotAngle = F(spot, "angle", 120); sl.innerSpotAngle = F(spot, "inner_angle", 80);
            sl.range = F(spot, "range_m", 4f); sl.intensity = F(spot, "intensity", 1.3f);   // the painted artwork is already lit
            sl.color = C(spot, "color", new Color(1f, 0.8f, 0.55f));
            sl.shadows = Json.Bool(spot, "shadows") ? LightShadows.Soft : LightShadows.None;
            sl.shadowStrength = F(spot, "shadow_strength", 0.75f);

            var gu = g.localPosition;
            _posG = new Vector3(gu.x, gu.y, -gu.z);
            _halfWidth = pw * s / 2;
        }

        const int LetterCode = 10;   // SignShape layers 10-25: the letters one by one, in reading order (make_sign_art.py)

        /// <summary>Loose quads in sign-local Godot space built into one mesh: the sign's sloped outlines need
        /// corners Batch.Quad cannot place. Flat-shaded, wound to face along the given normal.</summary>
        sealed class Faces
        {
            readonly List<Vector3> _v = new(), _n = new();
            readonly List<Vector2> _uv = new();
            readonly List<int> _t = new();

            public void Quad(Vector3 a, Vector3 b, Vector3 c, Vector3 d, Vector3 nrmG, Vector2 ua, Vector2 ub, Vector2 uc, Vector2 ud)
            {
                int i = _v.Count;
                var nu = G(nrmG).normalized;
                _v.Add(G(a)); _v.Add(G(b)); _v.Add(G(c)); _v.Add(G(d));
                for (int k = 0; k < 4; k++) _n.Add(nu);
                _uv.Add(ua); _uv.Add(ub); _uv.Add(uc); _uv.Add(ud);
                var w = Vector3.Cross(_v[i + 1] - _v[i], _v[i + 2] - _v[i]) + Vector3.Cross(_v[i + 2] - _v[i], _v[i + 3] - _v[i]);
                if (Vector3.Dot(w, nu) > 0) _t.AddRange(new[] { i, i + 1, i + 2, i, i + 2, i + 3 });
                else _t.AddRange(new[] { i, i + 2, i + 1, i, i + 3, i + 2 });
            }

            public void Build(Transform parent, string name, Material m)
            {
                if (_t.Count == 0) return;
                var mesh = new Mesh { name = name, indexFormat = _v.Count > 65000 ? UnityEngine.Rendering.IndexFormat.UInt32 : UnityEngine.Rendering.IndexFormat.UInt16 };
                mesh.SetVertices(_v);
                mesh.SetNormals(_n);
                mesh.SetUVs(0, _uv);
                mesh.SetTriangles(_t, 0);
                mesh.RecalculateBounds();
                var go = new GameObject(name);
                go.transform.SetParent(parent, false);
                go.AddComponent<MeshFilter>().sharedMesh = mesh;
                var mr = go.AddComponent<MeshRenderer>();
                mr.sharedMaterial = m;
                mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On;
            }
        }

        /// <summary>A chain of round links from a up to top (Godot-local), alternate links turned a quarter; top
        /// hangs from a pin in an eye plate on the wall.</summary>
        static void Chain(Batch b, Vector3 a, Vector3 top, Material steel, Material plate)
        {
            const float R0 = 0.028f, Wire = 0.0075f, Step = 0.042f;
            var ring = new Vector2[9];
            for (int j = 0; j <= 8; j++) { float t = -Mathf.PI / 2 + Mathf.PI * 2 * j / 8; ring[j] = new Vector2(R0 + Wire * Mathf.Cos(t), Wire * Mathf.Sin(t)); }
            var face = Quaternion.FromToRotation(Vector3.up, Vector3.Cross(G(top - a), Vector3.right).normalized);   // link in the chain's plane, facing the room
            var side = Quaternion.FromToRotation(Vector3.up, Vector3.right);
            int n = Mathf.Max(1, Mathf.RoundToInt((top - a).magnitude / Step));
            for (int i = 0; i <= n; i++)
                b.Lathe(Vector3.Lerp(a, top, i / (float)n), ring, steel, i % 2 == 0 ? face : side, 10);
            float zp = Wall + 0.01f;
            b.Bevel(new Vector3(top.x, top.y + 0.03f, zp), new Vector3(0.1f, 0.12f, 0.02f), 0.004f, plate);
            b.Rivet(new Vector3(top.x - 0.03f, top.y + 0.06f, zp + 0.01f), Vector3.forward, 0.009f, steel);
            b.Rivet(new Vector3(top.x + 0.03f, top.y + 0.06f, zp + 0.01f), Vector3.forward, 0.009f, steel);
            b.Cylinder(new Vector3(top.x, top.y, zp + 0.025f), 0.012f, 0.03f, steel, Quaternion.FromToRotation(Vector3.up, Vector3.back), 8);
        }
    }

    /// <summary>The sign's lamps: each glows at its own strength with a slow, faint filament drift; the flickering
    /// ones now and then stutter for a moment, a loose contact (timings from workshop_sign.json "flicker").
    /// Still in automated runs.</summary>
    public class SignLamps : MonoBehaviour
    {
        public bool still;
        public Vector2 firstAfter = new(4, 14), every = new(10, 30), burst = new(0.2f, 0.9f), step = new(0.03f, 0.12f), dim = new(0.06f, 0.55f);
        public float fullOnChance = 0.3f, drift = 0.03f;
        readonly List<(Light light, float lum, Material rim, float rimGlow, Material core, float coreGlow, bool flickers)> _lamps = new();
        float _next, _end, _step, _level = 1;

        public void Add(Light l, float lum, Material rim, float rimGlow, Material core, float coreGlow, bool flickers)
        {
            _lamps.Add((l, lum, rim, rimGlow, core, coreGlow, flickers));
            Set(_lamps.Count - 1, 1);
        }

        void Start() => _next = Time.time + Random.Range(firstAfter.x, firstAfter.y);

        void Set(int i, float f)
        {
            var (l, lum, rim, rimGlow, core, coreGlow, _) = _lamps[i];
            l.intensity = lum * f;
            rim.SetFloat("_Emission", rimGlow * f);
            core.SetFloat("_Emission", coreGlow * Mathf.Lerp(0.6f, 1f, f) * f);   // a dimming filament cools: the hot core drops fastest
        }

        void Update()
        {
            if (still) return;
            float t = Time.time;
            if (_end > 0 && t < _end)
            {
                if (t >= _step)
                {
                    _level = Random.value < fullOnChance ? 1f : Random.Range(dim.x, dim.y);
                    _step = t + Random.Range(step.x, step.y);
                }
            }
            else if (_end > 0) { _end = 0; _level = 1; _next = t + Random.Range(every.x, every.y); }
            else if (t >= _next) { _end = t + Random.Range(burst.x, burst.y); _step = t; }
            for (int i = 0; i < _lamps.Count; i++)
            {
                float wobble = 1 + drift * (Mathf.PerlinNoise(t * 0.6f, i * 7.3f) - 0.5f);
                Set(i, wobble * (_lamps[i].flickers ? _level : 1));
            }
        }
    }
}
