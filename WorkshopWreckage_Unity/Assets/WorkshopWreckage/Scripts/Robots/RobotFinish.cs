using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace WorkshopWreckage
{
    /// <summary>Robot surface finishes on the WWRobot shader: painted metal whose wear follows
    /// the construction (edges rubbed, scratched and chipped to dark steel, grime underneath),
    /// and bare metals told apart by colour, reflectivity and roughness (darkened joint steel,
    /// zinc-plated bolts, bright shafts, brass bearing rings, blackened steel, rubber).
    /// All share one generated tileable noise texture; materials are cached per finish.</summary>
    public static class RobotFinish
    {
        static readonly Dictionary<string, Material> _cache = new();
        static Shader _shader;
        static Texture2D _noise;

        // --- tileable value noise (lattice wraps at the period, so the texture repeats seamlessly)
        static float Lattice(int x, int y, int px, int py, int salt)
        {
            x = ((x % px) + px) % px; y = ((y % py) + py) % py;
            uint h = (uint)(x * 374761393 + y * 668265263 + salt * 1442695041);
            h = (h ^ (h >> 13)) * 1274126177;
            return ((h ^ (h >> 16)) & 0xffff) / 65535f;
        }

        static float Value(float u, float v, int fx, int fy, int salt)
        {
            float x = u * fx, y = v * fy;
            int ix = Mathf.FloorToInt(x), iy = Mathf.FloorToInt(y);
            float tx = x - ix, ty = y - iy;
            tx = tx * tx * (3 - 2 * tx); ty = ty * ty * (3 - 2 * ty);
            float a = Lattice(ix, iy, fx, fy, salt), b = Lattice(ix + 1, iy, fx, fy, salt);
            float c = Lattice(ix, iy + 1, fx, fy, salt), d = Lattice(ix + 1, iy + 1, fx, fy, salt);
            return Mathf.Lerp(Mathf.Lerp(a, b, tx), Mathf.Lerp(c, d, tx), ty);
        }

        static float Fbm(float u, float v, int fx, int fy, int oct, int salt)
        {
            float s = 0, amp = 0.5f, norm = 0;
            for (int o = 0; o < oct; o++, amp *= 0.5f, fx *= 2, fy *= 2) { s += amp * Value(u, v, fx, fy, salt + o * 7); norm += amp; }
            return s / norm;
        }

        /// <summary>R: chip breakup (fbm), G: scratches (sparse thin lines, two directions),
        /// B: fine grain, A: low-frequency blotches. Linear, 512², seamless.</summary>
        static Texture2D Noise
        {
            get
            {
                if (_noise != null) return _noise;
                const int N = 512;
                _noise = new Texture2D(N, N, TextureFormat.RGBA32, true, true) { name = "robot_noise", wrapMode = TextureWrapMode.Repeat, filterMode = FilterMode.Trilinear, anisoLevel = 4 };
                var px = new Color32[N * N];
                for (int y = 0; y < N; y++)
                    for (int x = 0; x < N; x++)
                    {
                        float u = (x + 0.5f) / N, v = (y + 0.5f) / N;
                        float chip = Fbm(u, v, 6, 6, 5, 1);
                        // scratches: long thin streaks, a few along each axis, sparse
                        float s1 = Fbm(u, v, 64, 3, 2, 11), s2 = Fbm(u, v, 4, 48, 2, 23);
                        float gate = Fbm(u, v, 5, 5, 2, 31);
                        float scratch = Mathf.Max(Mathf.InverseLerp(0.72f, 0.86f, s1), Mathf.InverseLerp(0.74f, 0.88f, s2) * 0.8f) * Mathf.InverseLerp(0.45f, 0.65f, gate);
                        float grain = Fbm(u, v, 48, 48, 3, 41);
                        float blotch = Fbm(u, v, 2, 2, 4, 53);
                        px[y * N + x] = new Color32((byte)(chip * 255), (byte)(Mathf.Clamp01(scratch) * 255), (byte)(grain * 255), (byte)(blotch * 255));
                    }
                _noise.SetPixels32(px);
                _noise.Apply(true);
                return _noise;
            }
        }

        static Material Make(string key, Color c, float metallic, float smooth, System.Action<Material> extra = null)
        {
            if (_cache.TryGetValue(key, out var m)) return m;
            _shader ??= Resources.Load<Shader>("WWRobot");
            m = new Material(_shader) { name = key, enableInstancing = true };
            m.SetTexture("_NoiseTex", Noise);
            m.SetColor("_BaseColor", c);
            m.SetFloat("_Metallic", metallic);
            m.SetFloat("_Smoothness", smooth);
            extra?.Invoke(m);
            _cache[key] = m;
            return m;
        }

        /// <summary>Enamel on pressed steel: dielectric, semi-gloss with slight roughness and colour
        /// drift; edges rubbed and chipped through to dark steel; grime underneath.</summary>
        public static Material Paint(Color c, float wear = 0.8f, float chips = 0.2f, float grime = 0.4f, float scratch = 0.55f, float faceChips = 0f, float fade = 0f, float colorVar = 0.06f) =>
            Make("paint" + ColorUtility.ToHtmlStringRGB(c) + string.Join("_", new[] { wear, chips, grime, scratch, faceChips, fade, colorVar }.Select(x => x.ToString("0.00"))), c, 0f, 0.52f, m =>
        {
            m.SetFloat("_SmoothVar", 0.09f);
            m.SetFloat("_ColorVar", colorVar);
            m.SetFloat("_Wear", wear);
            m.SetFloat("_Chips", chips);
            m.SetFloat("_FaceChips", faceChips);
            m.SetFloat("_Fade", fade);
            m.SetFloat("_Scratch", scratch);
            m.SetColor("_ScratchColor", new Color(0.62f, 0.57f, 0.48f));
            m.SetColor("_ChipColor", new Color(0.3f, 0.29f, 0.27f));   // worn-through steel, not black holes
            m.SetFloat("_Grime", grime);
            m.SetVector("_GrimeColor", new Vector4(0.62f, 0.55f, 0.45f, 1));
        });

        /// <summary>Darkened steel of joints, axles and housings: metallic, satin, oily grime, polished rims.</summary>
        public static Material JointSteel => Make("jointsteel", new Color(0.3f, 0.285f, 0.265f), 0.65f, 0.45f, m =>
        {
            m.SetFloat("_SmoothVar", 0.12f); m.SetFloat("_ColorVar", 0.08f);
            m.SetFloat("_EdgePolish", 0.6f); m.SetFloat("_Grime", 0.65f);
            m.SetVector("_GrimeColor", new Vector4(0.42f, 0.38f, 0.32f, 1));
        });

        /// <summary>Zinc-plated bolt heads: lighter and duller than the joint steel, so they read as separate fasteners.</summary>
        public static Material BoltSteel => Make("boltsteel", new Color(0.36f, 0.35f, 0.33f), 0.65f, 0.42f, m =>
        {
            m.SetFloat("_SmoothVar", 0.08f); m.SetFloat("_ColorVar", 0.1f);
            m.SetFloat("_EdgePolish", 0.4f); m.SetFloat("_Grime", 0.5f);
            m.SetVector("_GrimeColor", new Vector4(0.45f, 0.4f, 0.33f, 1));
        });

        /// <summary>Bright steel of shafts and piston rods: light, smooth, little grime.</summary>
        public static Material ShaftSteel => Make("shaftsteel", new Color(0.66f, 0.65f, 0.62f), 0.6f, 0.55f, m =>
        {
            m.SetFloat("_SmoothVar", 0.06f); m.SetFloat("_ColorVar", 0.04f);
            m.SetFloat("_EdgePolish", 0.2f); m.SetFloat("_Grime", 0.25f);
            m.SetVector("_GrimeColor", new Vector4(0.55f, 0.5f, 0.42f, 1));
        });

        /// <summary>Brushed brass bearing rings and hub caps: a darker, slightly tarnished brass, polished where worn.</summary>
        public static Material BearingBrass => Make("bearingbrass", new Color(0.6f, 0.46f, 0.25f), 1f, 0.55f, m =>
        {
            m.SetFloat("_SmoothVar", 0.14f); m.SetFloat("_ColorVar", 0.12f);
            m.SetFloat("_EdgePolish", 0.45f); m.SetFloat("_Grime", 0.45f);
            m.SetVector("_GrimeColor", new Vector4(0.5f, 0.42f, 0.3f, 1));
        });

        /// <summary>Blackened (phosphated) steel: dark, only faintly metallic, satin.</summary>
        public static Material Blackened => Make("blackened", new Color(0.12f, 0.115f, 0.11f), 0.55f, 0.38f, m =>
        {
            m.SetFloat("_SmoothVar", 0.1f); m.SetFloat("_ColorVar", 0.06f);
            m.SetFloat("_EdgePolish", 0.35f); m.SetFloat("_Grime", 0.3f);
        });

        /// <summary>Rubber hoses and soles: dielectric, rough, dusty underneath.</summary>
        public static Material Rubber => Make("rubber", new Color(0.075f, 0.072f, 0.07f), 0f, 0.28f, m =>
        {
            m.SetFloat("_SmoothVar", 0.1f); m.SetFloat("_ColorVar", 0.08f);
            m.SetFloat("_Grime", 0.5f); m.SetVector("_GrimeColor", new Vector4(1.5f, 1.45f, 1.35f, 1));   // dust lightens black rubber
        });

        /// <summary>Grease and dirt runs and bands (WWStain multiply): darker than OilStain, where
        /// joints leak and feet scuff.</summary>
        public static Material Grease
        {
            get
            {
                if (_cache.TryGetValue("grease", out var m)) return m;
                m = new Material(Resources.Load<Shader>("WWStain")) { name = "grease" };
                m.SetColor("_Color", new Color(0.72f, 0.67f, 0.6f));
                return _cache["grease"] = m;
            }
        }

        /// <summary>Grime and oil runs (WWStain multiply): a soft grey-brown, not orange rust.</summary>
        public static Material OilStain
        {
            get
            {
                if (_cache.TryGetValue("oilstain", out var m)) return m;
                m = new Material(Resources.Load<Shader>("WWStain")) { name = "oilstain" };
                m.SetColor("_Color", new Color(0.84f, 0.81f, 0.76f));
                return _cache["oilstain"] = m;
            }
        }
    }
}
