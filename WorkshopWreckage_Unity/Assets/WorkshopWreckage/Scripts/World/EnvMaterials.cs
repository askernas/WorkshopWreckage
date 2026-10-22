using System.Collections.Generic;
using UnityEngine;

namespace WorkshopWreckage
{
    /// <summary>Procedural environment textures and materials. The patterns follow
    /// the Godot reference shaders (brick + plaster, planks, wood grain, chipped
    /// paint, blueprint) but are baked into tiling textures sampled triplanar.</summary>
    public static class EnvMaterials
    {
        static readonly Dictionary<string, Material> _cache = new();
        static Shader _env;
        static Shader Env => _env ??= Resources.Load<Shader>("WWEnv");

        /// <summary>GLSL-style smoothstep(edge0, edge1, x). (Unity's Mathf.SmoothStep is an interpolation, not a threshold.)</summary>
        static float SS(float e0, float e1, float x)
        {
            float t = Mathf.Clamp01((x - e0) / (e1 - e0));
            return t * t * (3 - 2 * t);
        }

        static float Hash(float x, float y) => Mathf.Repeat(Mathf.Sin(x * 12.9898f + y * 78.233f) * 43758.5453f, 1f);
        static float Noise(float x, float y, int oct = 4)
        {
            float v = 0, a = 0.5f, f = 1;
            for (int o = 0; o < oct; o++) { v += a * Mathf.PerlinNoise(x * f + o * 31.7f, y * f + o * 11.3f); a *= 0.5f; f *= 2; }
            return v / 0.94f;
        }

        static Texture2D Tex(int size, string name, System.Func<float, float, Color> f, bool linear = false)
        {
            var t = new Texture2D(size, size, TextureFormat.RGBA32, true, linear) { name = name, wrapMode = TextureWrapMode.Repeat, filterMode = FilterMode.Trilinear, anisoLevel = 4 };
            var px = new Color[size * size];
            for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                    px[y * size + x] = f((x + 0.5f) / size, (y + 0.5f) / size);
            t.SetPixels(px);
            t.Apply(true);
            return t;
        }

        static Material Make(string key, Texture2D albedo, Texture2D mask, float scale, float metallic, float smooth, Color tint)
        {
            if (_cache.TryGetValue(key, out var m)) return m;
            m = new Material(Env) { name = key, enableInstancing = true };
            m.SetTexture("_MainTex", albedo);
            if (mask != null) m.SetTexture("_MaskTex", mask);
            m.SetFloat("_Scale", scale);
            m.SetFloat("_Metallic", metallic);
            m.SetFloat("_Smoothness", smooth);
            m.SetColor("_BaseColor", tint);
            _cache[key] = m;
            return m;
        }

        // --- brick: 2 m tile (texture u,v in [0,1) -> metres * 2) -----------------
        public static Material Brick()
        {
            if (_cache.TryGetValue("brick", out var m)) return m;
            var brick = new Color(0.50f, 0.25f, 0.16f);
            var mortar = new Color(0.58f, 0.52f, 0.44f);
            var plaster = new Color(0.74f, 0.66f, 0.54f);
            var tex = Tex(1024, "brick", (u, v) =>
            {
                float wx = u * 2f, wy = v * 2f;
                float bx = wx / 0.25f, by = wy / 0.08f;
                float row = Mathf.Floor(by);
                bx += (row % 2) * 0.5f;
                float fx = bx - Mathf.Floor(bx), fy = by - Mathf.Floor(by);
                float mort = Mathf.Max(1 - SS(0, 0.05f, fx) * SS(1, 0.95f, fx), 1 - SS(0, 0.12f, fy) * SS(1, 0.9f, fy));
                float h = Hash(Mathf.Floor(bx), row);
                float n = Noise(wx * 3f, wy * 3f);
                var c = brick * (0.7f + 0.55f * h) * (0.8f + 0.4f * n);
                c = Color.Lerp(c, mortar * (0.75f + 0.35f * n), mort);
                float p = Noise(u * 4f + 0.3f, v * 4f + 0.1f, 5);
                float pl = SS(0.7f, 0.72f, p);
                c = Color.Lerp(c, plaster * (0.85f + 0.25f * n), pl);
                c.a = 1;
                return c;
            });
            return Make("brick", tex, null, 0.5f, 0, 0.08f, Color.white);
        }

        // --- floor planks: 4 m tile -----------------------------------------------
        public static Material Planks()
        {
            if (_cache.TryGetValue("planks", out var m)) return m;
            var a = new Color(0.37f, 0.27f, 0.19f);
            var b = new Color(0.23f, 0.16f, 0.11f);
            var tex = Tex(1024, "planks", (u, v) =>
            {
                float wx = u * 4f, wz = v * 4f;
                float w = 4f / 21f;
                float row = Mathf.Floor(wz / w);
                float len = 1.6f + Hash(row, 3) * 1.4f;
                float x = wx + Hash(row, 7) * 5f;
                float seg = Mathf.Floor(x / len);
                float fz = wz / w - row;
                float fx = x / len - seg;
                float gap = 1 - SS(0, 0.03f, fz) * SS(1, 0.97f, fz) * SS(0, 0.004f, fx) * SS(1, 0.996f, fx);
                float grain = Noise(x * 0.4f, wz * 8f + seg * 3f, 3);
                float fine = Noise(x * 6f, wz * 40f, 2);
                var c = Color.Lerp(b, a, Hash(row, seg) * 0.7f + grain * 0.5f) * (0.8f + 0.3f * fine);
                float worn = Noise(wx * 0.5f, wz * 0.5f, 3);
                c = Color.Lerp(c, c * new Color(0.7f, 0.62f, 0.55f), SS(0.55f, 0.8f, worn));
                c = Color.Lerp(c, new Color(0.05f, 0.035f, 0.025f), gap * 0.9f);
                c.a = 1;
                return c;
            });
            var mask = Tex(256, "planks_mask", (u, v) => new Color(0, 0.45f - 0.3f * Noise(u * 2, v * 2, 3), 0, 1), true);
            return Make("planks", tex, mask, 0.25f, 0, 1f, Color.white);
        }

        // --- wood (tinted) ---------------------------------------------------------
        static Texture2D _wood;
        public static Material Wood(Color tint)
        {
            _wood ??= Tex(512, "wood", (u, v) =>
            {
                float g = Noise(u * 2f, v * 24f, 3);
                float f = Noise(u * 20f, v * 20f, 2);
                float s = SS(0.75f, 0.9f, Noise(u * 40f + 3.1f, v * 40f, 2));
                float k = (0.7f + 0.5f * g) * (0.85f + 0.3f * f) * (1 + s * 0.1f);
                return new Color(k, k, k, 1);
            });
            return Make("wood" + ColorUtility.ToHtmlStringRGB(tint), _wood, null, 1f, 0, 0.3f, tint);
        }

        // --- painted metal with chips ---------------------------------------------
        static readonly Dictionary<string, Texture2D> _paint = new();
        static Texture2D _paintMask;
        /// <summary>Weathered toy-robot paint, tiled at toy scale (a few cm): rust blooms,
        /// darker grime, fine scratches and chipped edges showing bare metal.</summary>
        public static Material ToyPaint(Color paint)
        {
            var key = "toy" + ColorUtility.ToHtmlStringRGB(paint);
            if (_cache.TryGetValue(key, out var m)) return m;
            if (!_paint.TryGetValue(key, out var tex))
            {
                tex = Tex(512, key, (u, v) =>
                {
                    float grain = Noise(u * 22f + 0.37f, v * 22f, 2);
                    float rust = SS(0.5f, 0.74f, Noise(u * 3f + 1.3f, v * 3f, 4));
                    float grime = SS(0.4f, 0.78f, Noise(u * 1.6f + 7.1f, v * 1.6f, 3));
                    float scratch = SS(0.93f, 0.98f, Noise(u * 40f, v * 3f, 2));
                    float chip = SS(0.78f, 0.84f, Noise(u * 7f + 3.3f, v * 7f, 3) * 0.75f + grain * 0.35f);
                    var c = paint * (0.86f + 0.22f * grain);
                    c = Color.Lerp(c, c * new Color(0.6f, 0.48f, 0.36f), grime * 0.55f);
                    c = Color.Lerp(c, new Color(0.4f, 0.21f, 0.09f), rust * 0.85f);
                    c = Color.Lerp(c, new Color(0.25f, 0.2f, 0.17f), scratch * 0.6f);
                    c = Color.Lerp(c, new Color(0.48f, 0.46f, 0.43f), chip);
                    c.a = 1;
                    return c;
                });
                _paint[key] = tex;
            }
            _paintMask ??= Tex(512, "paint_mask", (u, v) => new Color(0.3f, 0.5f, 0, 1), true);
            m = Make(key, tex, _paintMask, 3f, 1, 1, Color.white);   // object space: tiles per model unit
            m.EnableKeyword("_WW_OBJECT");
            return m;
        }

        public static Material Metal(Color paint, float chip = 0.35f)
        {
            var key = "metal" + ColorUtility.ToHtmlStringRGB(paint) + chip.ToString("0.00");
            if (_cache.TryGetValue(key, out var m)) return m;
            float Chips(float u, float v)
            {
                float n = Noise(u * 3.4f, v * 3.4f, 4);
                float mm = Noise(u * 18f + 0.37f, v * 18f, 2);
                return SS(1 - chip * 0.5f, 1.02f - chip * 0.5f, n * 0.7f + mm * 0.4f);
            }
            if (!_paint.TryGetValue(key, out var tex))
            {
                tex = Tex(512, key, (u, v) =>
                {
                    float mm = Noise(u * 18f + 0.37f, v * 18f, 2);
                    float rust = SS(0.6f, 0.9f, Noise(u * 1.2f + 1.3f, v * 1.2f, 3)) * 0.5f;
                    var c = paint * (0.82f + 0.3f * mm);
                    c = Color.Lerp(c, c * new Color(0.75f, 0.55f, 0.4f), rust * 0.6f);
                    c = Color.Lerp(c, new Color(0.45f, 0.43f, 0.4f), Chips(u, v));
                    c.a = 1;
                    return c;
                });
                _paint[key] = tex;
            }
            _paintMask ??= Tex(512, "paint_mask", (u, v) =>
            {
                float ch = Chips(u, v);
                return new Color(Mathf.Lerp(0.15f, 0.85f, ch), Mathf.Lerp(0.45f, 0.55f, ch), 0, 1);
            }, true);
            return Make(key, tex, _paintMask, 2f, 1, 1, Color.white);
        }

        public static Material Plain(Color c, float metallic = 0, float smooth = 0.4f, float emission = 0)
        {
            var key = "plain" + ColorUtility.ToHtmlStringRGBA(c) + metallic + smooth + emission;
            if (_cache.TryGetValue(key, out var m)) return m;
            m = Make(key, Texture2D.whiteTexture, null, 1, metallic, smooth, c);
            m.SetFloat("_Emission", emission);
            return m;
        }

        public static Material Blueprint(int variant)
        {
            var key = "blueprint" + variant;
            if (_cache.TryGetValue(key, out var m)) return m;
            // variant 2: a locked blueprint — the robot drawing faded on grey paper, with a padlock
            var paper = variant == 2 ? new Color(0.2f, 0.22f, 0.26f) : new Color(0.16f, 0.30f, 0.52f);
            float Circle(float u, float v, float cx, float cy, float r) => 1 - SS(0, 0.006f, Mathf.Abs(Vector2.Distance(new Vector2(u, v), new Vector2(cx, cy)) - r));
            float Line(float u, float v, float ax, float ay, float bx, float by)
            {
                var p = new Vector2(u - ax, v - ay);
                var ba = new Vector2(bx - ax, by - ay);
                float h = Mathf.Clamp01(Vector2.Dot(p, ba) / Vector2.Dot(ba, ba));
                return 1 - SS(0, 0.004f, (p - ba * h).magnitude);
            }
            var tex = Tex(512, key, (u, v) =>
            {
                float gx = Mathf.Abs((u * 16) % 1 - 0.5f), gy = Mathf.Abs((v * 16) % 1 - 0.5f);
                float ink = (1 - SS(0, 0.04f, Mathf.Min(gx, gy))) * 0.25f;
                if (variant == 0)
                    ink += Circle(u, v, 0.35f, 0.5f, 0.2f) + Circle(u, v, 0.35f, 0.5f, 0.07f) + Circle(u, v, 0.72f, 0.42f, 0.12f) + Line(u, v, 0.35f, 0.5f, 0.72f, 0.42f) + Line(u, v, 0.1f, 0.85f, 0.9f, 0.85f);
                else
                {
                    ink += (Line(u, v, 0.3f, 0.2f, 0.7f, 0.2f) + Line(u, v, 0.3f, 0.2f, 0.3f, 0.55f) + Line(u, v, 0.7f, 0.2f, 0.7f, 0.55f) + Line(u, v, 0.3f, 0.55f, 0.7f, 0.55f)
                        + Circle(u, v, 0.42f, 0.38f, 0.05f) + Circle(u, v, 0.58f, 0.38f, 0.05f) + Line(u, v, 0.35f, 0.55f, 0.3f, 0.85f) + Line(u, v, 0.65f, 0.55f, 0.7f, 0.85f)) * (variant == 2 ? 0.3f : 1f);
                    if (variant == 2)   // padlock
                        ink += Line(u, v, 0.42f, 0.42f, 0.58f, 0.42f) + Line(u, v, 0.42f, 0.58f, 0.58f, 0.58f) + Line(u, v, 0.42f, 0.42f, 0.42f, 0.58f) + Line(u, v, 0.58f, 0.42f, 0.58f, 0.58f)
                            + Circle(u, v, 0.5f, 0.62f, 0.055f) * (v > 0.6f ? 1 : 0) + Circle(u, v, 0.5f, 0.5f, 0.015f);
                }
                var c = paper * (0.8f + 0.35f * Noise(u * 3, v * 3, 3));
                c = Color.Lerp(c, new Color(0.85f, 0.9f, 0.95f), Mathf.Clamp01(ink) * 0.8f);
                c.a = 1;
                return c;
            });
            m = Make(key, tex, null, 1, 0, 0.05f, Color.white);
            m.EnableKeyword("_WW_UV");
            return m;
        }

        public static Material Atlas(Texture tex)
        {
            var m = new Material(Env) { name = "labels" };
            m.SetTexture("_MainTex", tex);
            m.SetFloat("_Smoothness", 0.25f);
            m.SetColor("_BaseColor", Color.white);
            m.EnableKeyword("_WW_UV");
            return m;
        }

        static Material _outline, _ghost;
        public static Material Outline => _outline ??= new Material(Resources.Load<Shader>("WWOutline"));
        public static Material NewGhost() => new Material(Resources.Load<Shader>("WWGhost"));
    }
}
