using System.Collections.Generic;
using System.Threading.Tasks;
using UnityEngine;

namespace WorkshopWreckage.Pegboard
{
    /// <summary>The board's materials, all from workshop_pegboard.json and generated at start-up (WWEnv shader,
    /// like the rest of the workshop): the perforated panel is one non-tiling texture across the panel (mesh UVs)
    /// with its wear, grime and the painted tool silhouettes; the frame is tiling rusty steel (world triplanar).
    /// Few materials, shared by every part of the board.</summary>
    public class PegboardMaterials
    {
        static Shader _env;
        static Shader Env => _env ??= Resources.Load<Shader>("WWEnv");

        public Material panel, holeWall, backing, frame, frameDark, bolt, hook, tube, housing, rack;
        public Texture2D panelTex, outlineTex;

        static float Hash(float x, float y) => Mathf.Repeat(Mathf.Sin(x * 12.9898f + y * 78.233f) * 43758.5453f, 1f);
        static float Noise(float x, float y, int oct = 4)
        {
            float v = 0, a = 0.5f, f = 1;
            for (int o = 0; o < oct; o++) { v += a * Mathf.PerlinNoise(x * f + o * 31.7f, y * f + o * 11.3f); a *= 0.5f; f *= 2; }
            return v / 0.94f;
        }
        static float SS(float e0, float e1, float x) { float t = Mathf.Clamp01((x - e0) / (e1 - e0)); return t * t * (3 - 2 * t); }

        public static Material Make(string name, Texture tex, float scale, float metallic, float smooth, Color tint, bool uv = false, Texture mask = null)
        {
            var m = new Material(Env) { name = name, enableInstancing = true };
            m.SetTexture("_MainTex", tex != null ? tex : Texture2D.whiteTexture);
            if (mask != null) m.SetTexture("_MaskTex", mask);
            m.SetFloat("_Scale", scale);
            m.SetFloat("_Metallic", metallic);
            m.SetFloat("_Smoothness", smooth);
            m.SetColor("_BaseColor", tint);
            if (uv) m.EnableKeyword("_WW_UV");
            return m;
        }

        public PegboardMaterials(PegboardConfig cfg, List<(Vector2[] tri, float shade)> silhouettes)
        {
            panelTex = PanelTexture(cfg, silhouettes, out outlineTex);
            panel = Make("peg_panel", panelTex, 1, 0.15f, 0.32f, Color.white, true);
            panel.SetTexture("_SilTex", outlineTex);
            panel.SetFloat("_SilOn", 1);
            panel.SetFloat("_SilRange", OutlineRange);
            panel.SetFloat("_SilRim", cfg.F("silhouette", "rim_mm", 2.2f) / 1000f);
            panel.SetFloat("_SilChip", cfg.F("silhouette", "chip", 0.76f));
            var silC = cfg.C("silhouette", "color", new Color(0.16f, 0.1f, 0.06f));
            silC.a = cfg.F("silhouette", "alpha", 0.9f);
            panel.SetColor("_SilColor", silC);
            panel.SetColor("_SilRimColor", cfg.C("silhouette", "rim_color", new Color(0.08f, 0.04f, 0.02f)));
            holeWall = Make("peg_hole", null, 1, 0, 0.1f, new Color(0.09f, 0.06f, 0.04f));
            backing = Make("peg_backing", null, 1, 0, 0.05f, cfg.C("panel", "backing_color", new Color(0.07f, 0.05f, 0.03f)));
            var (ft, fm) = RustySteel(cfg.C("frame", "color", new Color(0.29f, 0.23f, 0.17f)), cfg.F("frame", "chip", 0.45f), 512);
            frame = Make("peg_frame", ft, 1.2f, 1, 0.55f, Color.white, false, fm);
            frameDark = Make("peg_frame_dark", ft, 1.2f, 1, 0.45f, new Color(0.62f, 0.6f, 0.58f), false, fm);
            bolt = Make("peg_bolt", ft, 4f, 1, 0.6f, new Color(0.8f, 0.78f, 0.75f), false, fm);
            hook = Make("peg_hook", null, 1, 0.6f, 0.5f, cfg.C("hooks", "color", new Color(0.48f, 0.49f, 0.5f)));
            rack = Make("peg_rack", ft, 3f, 1, 0.5f, new Color(0.85f, 0.85f, 0.86f), false, fm);
            housing = Make("peg_housing", ft, 2f, 1, 0.5f, new Color(0.7f, 0.68f, 0.66f), false, fm);
            tube = Make("peg_tube", null, 1, 0, 0.9f, cfg.C("lights", "tube_color", new Color(1f, 0.84f, 0.6f)));
            tube.SetFloat("_Emission", cfg.F("lights", "tube_emission", 4f));
        }

        /// <summary>Dark painted steel worn to rust: tiling albedo + mask (R metallic where the paint is gone).</summary>
        static (Texture2D, Texture2D) RustySteel(Color paint, float chip, int size)
        {
            var alb = new Texture2D(size, size, TextureFormat.RGBA32, true) { name = "peg_steel", wrapMode = TextureWrapMode.Repeat, anisoLevel = 4 };
            var msk = new Texture2D(size, size, TextureFormat.RGBA32, true, true) { name = "peg_steel_mask", wrapMode = TextureWrapMode.Repeat };
            var pa = new Color[size * size]; var pm = new Color[size * size];
            for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                {
                    float u = (x + 0.5f) / size, v = (y + 0.5f) / size;
                    // tileable: sample noise on a torus-ish wrap by blending the four shifted copies
                    float N(float fx, float fy, int o)
                    {
                        float a = Noise(u * fx, v * fy, o), b = Noise((u - 1) * fx, v * fy, o), c = Noise(u * fx, (v - 1) * fy, o), d = Noise((u - 1) * fx, (v - 1) * fy, o);
                        return Mathf.Lerp(Mathf.Lerp(a, b, u), Mathf.Lerp(c, d, u), v);
                    }
                    float grain = N(24, 24, 2);
                    float rust = SS(0.5f, 0.78f, N(3.2f, 3.2f, 4) * 0.8f + grain * 0.25f);
                    float streak = SS(0.6f, 0.85f, N(14, 1.6f, 3));
                    float bare = SS(1 - chip * 0.45f, 1.03f - chip * 0.45f, N(7, 7, 3) * 0.75f + grain * 0.35f);
                    float scratch = SS(0.93f, 0.985f, N(46, 4, 2));
                    var c = paint * (0.8f + 0.35f * grain);
                    c = Color.Lerp(c, new Color(0.48f, 0.24f, 0.1f) * (0.8f + 0.4f * grain), rust * 0.85f);
                    c = Color.Lerp(c, new Color(0.24f, 0.12f, 0.06f), streak * 0.35f);
                    c = Color.Lerp(c, new Color(0.56f, 0.53f, 0.49f), bare * 0.9f);
                    c = Color.Lerp(c, new Color(0.62f, 0.6f, 0.56f), scratch * 0.6f);
                    c.a = 1;
                    pa[y * size + x] = c;
                    pm[y * size + x] = new Color(Mathf.Lerp(0.15f, 0.85f, Mathf.Max(bare, scratch)), Mathf.Lerp(0.35f, 0.8f, bare) * (1 - rust * 0.6f), 1, 1);
                }
            alb.SetPixels(pa); alb.Apply(true);
            msk.SetPixels(pm); msk.Apply(true);
            return (alb, msk);
        }

        const float OutlineRange = 0.016f;   // the outline texture's distances span +-8 mm round the edge

        /// <summary>Squared Euclidean distance transform of one line (Felzenszwalb and Huttenlocher): f in, d out.</summary>
        static void Edt1(float[] f, float[] d, int n, int[] v, float[] z)
        {
            float S(int q, int p) => (f[q] + (float)q * q - (f[p] + (float)p * p)) / (2f * (q - p));
            int k = 0; v[0] = 0; z[0] = float.NegativeInfinity; z[1] = float.PositiveInfinity;
            for (int q = 1; q < n; q++)
            {
                float s = S(q, v[k]);
                while (s <= z[k]) { k--; s = S(q, v[k]); }
                k++; v[k] = q; z[k] = s; z[k + 1] = float.PositiveInfinity;
            }
            k = 0;
            for (int q = 0; q < n; q++)
            {
                while (z[k + 1] < q) k++;
                d[q] = (float)(q - v[k]) * (q - v[k]) + f[v[k]];
            }
        }

        /// <summary>Distance (pixels) from every pixel to the nearest pixel where mask == target.</summary>
        static float[] Distance(byte[] mask, int w, int h, byte target)
        {
            const float Inf = 1e9f;   // far beyond any distance on the board, small enough for float precision
            var g = new float[w * h];
            Parallel.For(0, h, y =>
            {
                var f = new float[w]; var d = new float[w]; var v = new int[w]; var z = new float[w + 1];
                for (int x = 0; x < w; x++) f[x] = mask[y * w + x] == target ? 0 : Inf;
                Edt1(f, d, w, v, z);
                for (int x = 0; x < w; x++) g[y * w + x] = d[x];
            });
            Parallel.For(0, w, x =>
            {
                var f = new float[h]; var d = new float[h]; var v = new int[h]; var z = new float[h + 1];
                for (int y = 0; y < h; y++) f[y] = g[y * w + x];
                Edt1(f, d, h, v, z);
                for (int y = 0; y < h; y++) g[y * w + x] = Mathf.Sqrt(d[y]);
            });
            return g;
        }

        /// <summary>Box blur (separable running sums) of a w x h coverage map, radius r pixels.</summary>
        static float[] Blur(float[] src, int w, int h, int r)
        {
            var tmp = new float[src.Length]; var dst = new float[src.Length];
            float k = 1f / (2 * r + 1);
            Parallel.For(0, h, y =>
            {
                int row = y * w; float acc = 0;
                for (int x = -r; x <= r; x++) acc += src[row + Mathf.Clamp(x, 0, w - 1)];
                for (int x = 0; x < w; x++)
                {
                    tmp[row + x] = acc * k;
                    acc += src[row + Mathf.Min(w - 1, x + r + 1)] - src[row + Mathf.Max(0, x - r)];
                }
            });
            Parallel.For(0, w, x =>
            {
                float acc = 0;
                for (int y = -r; y <= r; y++) acc += tmp[Mathf.Clamp(y, 0, h - 1) * w + x];
                for (int y = 0; y < h; y++)
                {
                    dst[y * w + x] = acc * k;
                    acc += tmp[Mathf.Min(h - 1, y + r + 1) * w + x] - tmp[Mathf.Max(0, y - r) * w + x];
                }
            });
            return dst;
        }

        /// <summary>The panel's face: ochre paint over steel, mottled rust, drips, grime towards the frame, worn rings
        /// around the holes, oil stains, scratches, and the painted outline of every tool (silhouettes given as
        /// triangles in panel UV space, 0..1): a brushed dark fill with a crisper, darker rim, worn and chipped, and
        /// a faint halo of grime where the tool has hung for years (it doubles as the hung tool's contact shading).
        /// One texture over the whole panel (panel.texture_width pixels wide), built on all cores, block compressed.</summary>
        static Texture2D PanelTexture(PegboardConfig cfg, List<(Vector2[] tri, float shade)> silhouettes, out Texture2D outlines)
        {
            float pw = cfg.PanelW, ph = cfg.PanelH;
            int w = Mathf.RoundToInt(cfg.F("panel", "texture_width", 4096)) / 4 * 4, h = Mathf.Max(64, Mathf.RoundToInt(w * ph / pw) / 4 * 4);
            // silhouettes: rasterised at 2x for soft, clean edges
            int sw = w * 2, sh = h * 2;
            var sil = new byte[sw * sh];
            foreach (var (t, shade) in silhouettes)
            {
                Vector2 a = new(t[0].x * sw, t[0].y * sh), b = new(t[1].x * sw, t[1].y * sh), c = new(t[2].x * sw, t[2].y * sh);
                int x0 = Mathf.Max(0, Mathf.FloorToInt(Mathf.Min(a.x, Mathf.Min(b.x, c.x)))), x1 = Mathf.Min(sw - 1, Mathf.CeilToInt(Mathf.Max(a.x, Mathf.Max(b.x, c.x))));
                int y0 = Mathf.Max(0, Mathf.FloorToInt(Mathf.Min(a.y, Mathf.Min(b.y, c.y)))), y1 = Mathf.Min(sh - 1, Mathf.CeilToInt(Mathf.Max(a.y, Mathf.Max(b.y, c.y))));
                float area = (b.x - a.x) * (c.y - a.y) - (b.y - a.y) * (c.x - a.x);
                if (Mathf.Abs(area) < 1e-6f) continue;
                for (int y = y0; y <= y1; y++)
                    for (int x = x0; x <= x1; x++)
                    {
                        float px = x + 0.5f, py = y + 0.5f;
                        float w0 = ((b.x - px) * (c.y - py) - (b.y - py) * (c.x - px)) / area;
                        float w1 = ((c.x - px) * (a.y - py) - (c.y - py) * (a.x - px)) / area;
                        float w2 = 1 - w0 - w1;
                        if (w0 >= 0 && w1 >= 0 && w2 >= 0) sil[y * sw + x] = 1;
                    }
            }
            // coverage at texture resolution, a narrow blur for the painted rim and a wide one for the grime halo
            var cov = new float[w * h];
            Parallel.For(0, h, y =>
            {
                int sy = y * 2;
                for (int x = 0; x < w; x++)
                {
                    int sx = x * 2;
                    cov[y * w + x] = (sil[sy * sw + sx] + sil[sy * sw + sx + 1] + sil[(sy + 1) * sw + sx] + sil[(sy + 1) * sw + sx + 1]) * 0.25f;
                }
            });
            float mmPx = pw * 1000f / w;
            // the outlines themselves: signed distance from the 2x mask (sub-pixel exact edges), sampled at texture
            // resolution, with the noise the shader turns into brush streaks, chips and scuffs
            var dIn = Distance(sil, sw, sh, 0);    // inside: distance to the nearest outside pixel
            var dOut = Distance(sil, sw, sh, 1);   // outside: distance to the nearest inside pixel
            var op = new Color32[w * h];
            float mPx2 = pw / sw;   // metres per 2x pixel
            Parallel.For(0, h, y =>
            {
                for (int x = 0; x < w; x++)
                {
                    float sd = 0;
                    for (int k = 0; k < 4; k++)
                    {
                        int j = (y * 2 + (k >> 1)) * sw + x * 2 + (k & 1);
                        sd += sil[j] == 1 ? dIn[j] - 0.5f : -(dOut[j] - 0.5f);
                    }
                    sd = sd * 0.25f * mPx2;
                    float mx = (x + 0.5f) / w * pw, my = (y + 0.5f) / h * ph;
                    op[y * w + x] = new Color(Mathf.Clamp01(0.5f + sd / OutlineRange), Noise(mx * 45 + 5, my * 45, 2),
                        Noise(mx * 2.5f + 11, my * 70, 2), Noise(mx * 6 + 21, my * 22, 3));
                }
            });
            outlines = new Texture2D(w, h, TextureFormat.RGBA32, true, true) { name = "peg_outlines", wrapMode = TextureWrapMode.Clamp, anisoLevel = 8 };
            outlines.SetPixels32(op);
            outlines.Apply(true, true);
            var haloBlur = Blur(cov, w, h, Mathf.Max(2, Mathf.RoundToInt(cfg.F("silhouette", "halo_mm", 9f) / mmPx)));
            float halo = cfg.F("silhouette", "halo", 0.35f);
            var baseC = cfg.C("panel", "color", new Color(0.61f, 0.42f, 0.2f));
            var rustC = cfg.C("panel", "rust_color", new Color(0.42f, 0.24f, 0.11f));
            var grimeC = cfg.C("panel", "grime_color", new Color(0.23f, 0.16f, 0.11f));
            float wear = cfg.F("panel", "wear", 0.55f);
            float pitch = cfg.F("panel", "hole_pitch", 0.052f), hr = cfg.F("panel", "hole_radius", 0.0085f);
            var (ox, oy) = HoleOrigin(cfg);
            var px2 = new Color32[w * h];
            Parallel.For(0, h, y =>
            {
                for (int x = 0; x < w; x++)
                {
                    float u = (x + 0.5f) / w, v = (y + 0.5f) / h;
                    float mx = u * pw, my = v * ph;   // metres on the panel
                    float grain = Noise(mx * 30, my * 30, 2);
                    float mottle = Noise(mx * 1.6f + 3.7f, my * 1.6f, 4);
                    float rust = SS(0.4f, 0.72f, mottle * 0.85f + grain * 0.2f) * wear * 1.25f;
                    float drip = SS(0.62f, 0.86f, Noise(mx * 9f, my * 0.7f + 1.3f, 3)) * SS(0.2f, 0.9f, Noise(mx * 0.8f + 9, my * 0.8f, 2));
                    float stain = SS(0.72f, 0.82f, Noise(mx * 2.6f + 17, my * 2.6f, 3)) * wear;
                    float scratch = SS(0.95f, 0.99f, Noise(mx * 5 + 40, my * 60, 2)) + SS(0.95f, 0.99f, Noise(mx * 55 + 7, my * 6, 2));
                    float edge = Mathf.Min(Mathf.Min(mx, pw - mx), Mathf.Min(my, ph - my));
                    float grime = (1 - SS(0.0f, 0.16f, edge)) * (0.65f + 0.35f * Noise(mx * 6, my * 6, 2));
                    float top = SS(ph - 0.5f, ph, my) * 0.15f;   // under the lights: soot from the old tubes
                    // worn ring round each hole (hooks rubbing)
                    float hx = Mathf.Repeat(mx - ox + pitch / 2, pitch) - pitch / 2, hy = Mathf.Repeat(my - oy + pitch / 2, pitch) - pitch / 2;
                    float hd = Mathf.Sqrt(hx * hx + hy * hy);
                    float ring = (1 - SS(hr, hr * 2.1f, hd)) * 0.55f;
                    var c = baseC * (0.82f + 0.36f * grain) * (0.75f + 0.45f * Noise(mx * 0.5f + 2, my * 0.5f, 3));
                    c = Color.Lerp(c, rustC * (0.85f + 0.3f * grain), Mathf.Clamp01(rust));
                    c = Color.Lerp(c, rustC * 0.7f, drip * 0.45f);
                    c = Color.Lerp(c, grimeC, Mathf.Clamp01(grime * 0.75f + stain * 0.55f + top + ring));
                    c = Color.Lerp(c, new Color(0.78f, 0.62f, 0.4f), Mathf.Clamp01(scratch) * 0.35f);
                    int i = y * w + x;
                    // grime halo round the outline: dirt and hand marks where the tool has hung (outside only)
                    float hal = haloBlur[i] * (1 - cov[i]);
                    if (hal > 0.002f) c = Color.Lerp(c, grimeC * 0.8f, Mathf.Clamp01(hal * 1.6f) * halo * (0.7f + 0.6f * Noise(mx * 14 + 3, my * 14, 2)));
                    px2[i] = c;   // the outlines themselves are painted by the shader (PaintedOutlines, WWEnv.shader)
                }
            });
            var tex = new Texture2D(w, h, TextureFormat.RGBA32, true) { name = "peg_panel", wrapMode = TextureWrapMode.Clamp, anisoLevel = 8 };
            tex.SetPixels32(px2);
            tex.Apply(true, false);
            tex.Compress(true);        // DXT: a quarter of the memory of the 4K RGBA texture
            tex.Apply(false, true);    // no CPU copy kept
            return tex;
        }

        /// <summary>Where the hole grid starts on the panel (metres from its bottom-left): centred, so the margins match.</summary>
        public static (float x, float y) HoleOrigin(PegboardConfig cfg)
        {
            float pitch = cfg.F("panel", "hole_pitch", 0.052f);
            int nx = Mathf.FloorToInt((cfg.PanelW - pitch) / pitch), ny = Mathf.FloorToInt((cfg.PanelH - pitch) / pitch);
            return ((cfg.PanelW - nx * pitch) / 2, (cfg.PanelH - ny * pitch) / 2);
        }
    }
}
