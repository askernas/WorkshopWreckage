using System.Collections.Generic;
using UnityEngine;

namespace WorkshopWreckage
{
    /// <summary>Material + finish -> PBR numbers (identical table to the Godot version).</summary>
    public static class ItemAppearance
    {
        struct P { public Color albedo; public float metallic, roughness, wear; public P(Color a, float m, float r, float w) { albedo = a; metallic = m; roughness = r; wear = w; } }

        static readonly Dictionary<string, P> Materials = new()
        {
            // workshop parts are oily and worn: metals are only partly mirror-like, so a part on the
            // floor shows the light falling on it instead of reflecting the dark room above
            // without environment reflections metals would look dark: they are mostly diffuse here
            ["steel"] = new P(new Color(0.58f, 0.59f, 0.6f), 0.3f, 0.5f, 0.5f),
            ["stainless"] = new P(new Color(0.7f, 0.71f, 0.73f), 0.35f, 0.38f, 0.15f),
            ["brass"] = new P(new Color(0.86f, 0.66f, 0.30f), 0.35f, 0.4f, 0.40f),
            ["copper"] = new P(new Color(0.88f, 0.50f, 0.33f), 0.35f, 0.42f, 0.45f),
            ["aluminium"] = new P(new Color(0.75f, 0.76f, 0.78f), 0.3f, 0.5f, 0.20f),
            ["painted"] = new P(new Color(0.7f, 0.7f, 0.7f), 0.05f, 0.5f, 0.6f),
            ["oxidized"] = new P(new Color(0.52f, 0.26f, 0.11f), 0.1f, 0.9f, 1.0f),
            ["worn"] = new P(new Color(0.42f, 0.42f, 0.43f), 0.25f, 0.65f, 0.70f),
            ["electronic"] = new P(new Color(1, 1, 1), 0.0f, 0.45f, 0.15f),
        };

        public static bool Known(string material) => Materials.ContainsKey(material);

        /// <summary>(albedo, (metallic, roughness, wear, 0)) — the albedo in sRGB.</summary>
        public static (Color albedo, Vector4 custom) Params(ItemDefinition def)
        {
            var p = Materials.TryGetValue(def.material, out var m) ? m : Materials["steel"];
            if (def.material == "painted" || def.material == "electronic") p.albedo = def.variantColor;
            switch (def.finish)
            {
                case "zinc": p.albedo = new Color(0.6f, 0.64f, 0.68f); p.roughness = 0.30f; break;
                case "black_oxide": p.albedo = new Color(0.2f, 0.2f, 0.21f); p.metallic = 0.25f; p.roughness = 0.5f; break;
                case "polished": p.roughness = Mathf.Max(0.18f, p.roughness - 0.15f); p.wear *= 0.3f; break;
            }
            if (def.subcategory == "led") p.roughness = 0.15f;
            return (p.albedo, new Vector4(p.metallic, p.roughness, p.wear, 0));
        }

        static Shader _shader;
        static Texture2D _noise;
        static readonly Dictionary<string, Material> _cache = new();

        public static Shader Shader => _shader ??= Resources.Load<Shader>("WWItem");

        public static Texture2D Noise
        {
            get
            {
                if (_noise != null) return _noise;
                const int n = 256;
                _noise = new Texture2D(n, n, TextureFormat.R8, true, true) { wrapMode = TextureWrapMode.Repeat, name = "WearNoise" };
                var px = new Color[n * n];
                for (int y = 0; y < n; y++)
                for (int x = 0; x < n; x++)
                {
                    float v = 0, amp = 0.5f, f = 4f / n;
                    for (int o = 0; o < 4; o++)
                    {
                        // tileable Perlin via periodic sampling
                        v += amp * Mathf.PerlinNoise(x * f + o * 17.3f, y * f + o * 9.1f);
                        amp *= 0.5f;
                        f *= 2f;
                    }
                    px[y * n + x] = new Color(v, v, v);
                }
                _noise.SetPixels(px);
                _noise.Apply(true);
                return _noise;
            }
        }

        /// <summary>Per-definition material for non-instanced (inspection/held) models.</summary>
        public static Material For(ItemDefinition def)
        {
            var (albedo, c) = Params(def);
            var key = def.material + "|" + def.finish + "|" + ColorUtility.ToHtmlStringRGB(albedo);
            if (_cache.TryGetValue(key, out var m)) return m;
            m = Make(albedo, c.x, c.y, c.z);
            _cache[key] = m;
            return m;
        }

        public static Material Make(Color albedo, float metallic, float roughness, float wear)
        {
            var m = new Material(Shader);
            m.SetColor("_BaseColor", albedo);
            m.SetFloat("_Metallic", metallic);
            m.SetFloat("_Roughness", roughness);
            m.SetFloat("_Wear", wear);
            m.SetTexture("_WearNoise", Noise);
            m.enableInstancing = true;
            return m;
        }

        static Material _tin, _accent;
        public static Material Tin => _tin ??= Make(new Color(0.78f, 0.78f, 0.76f), 1, 0.35f, 0);
        public static Material Accent => _accent ??= Make(Color.white, 0, 0.6f, 0.1f);

        /// <summary>Materials for each submesh of an entry (slot 0 = part material).</summary>
        public static Material[] SlotMaterials(MeshLibrary.Entry e, Material primary)
        {
            var mats = new Material[e.slots.Length];
            for (int i = 0; i < mats.Length; i++)
                mats[i] = e.slots[i] == 0 ? primary : e.slots[i] == 1 ? Tin : Accent;
            return mats;
        }
    }
}
