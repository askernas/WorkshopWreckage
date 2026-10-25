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
            ["steel"] = new P(new Color(0.40f, 0.40f, 0.41f), 0.3f, 0.6f, 0.5f),
            ["stainless"] = new P(new Color(0.46f, 0.47f, 0.48f), 0.35f, 0.5f, 0.15f),
            ["brass"] = new P(new Color(0.62f, 0.47f, 0.22f), 0.35f, 0.55f, 0.40f),
            ["copper"] = new P(new Color(0.62f, 0.36f, 0.24f), 0.35f, 0.55f, 0.45f),
            ["aluminium"] = new P(new Color(0.50f, 0.51f, 0.52f), 0.3f, 0.6f, 0.20f),
            ["painted"] = new P(new Color(0.6f, 0.6f, 0.6f), 0.05f, 0.6f, 0.6f),
            ["oxidized"] = new P(new Color(0.52f, 0.26f, 0.11f), 0.1f, 0.9f, 1.0f),
            ["worn"] = new P(new Color(0.42f, 0.42f, 0.43f), 0.25f, 0.65f, 0.70f),
            ["electronic"] = new P(new Color(1, 1, 1), 0.0f, 0.45f, 0.15f),
        };

        public static bool Known(string material) => Materials.ContainsKey(material);

        // the workshop's palette: vintage industrial enamel instead of toy primaries
        static readonly (Color from, Color to)[] Enamel =
        {
            (new Color(0.72f, 0.12f, 0.09f), new Color(0.56f, 0.17f, 0.11f)),   // red -> oxide red
            (new Color(0.17f, 0.33f, 0.62f), new Color(0.2f, 0.36f, 0.42f)),    // blue -> faded teal
            (new Color(0.24f, 0.45f, 0.25f), new Color(0.34f, 0.39f, 0.24f)),   // green -> olive
            (new Color(0.86f, 0.62f, 0.12f), new Color(0.74f, 0.54f, 0.18f)),   // yellow -> mustard
        };

        /// <summary>Maps a catalog paint to its vintage enamel, keeping the part's own shade.</summary>
        public static Color Vintage(Color c)
        {
            int best = 0; float bd = float.MaxValue;
            for (int k = 0; k < Enamel.Length; k++)
            {
                var f = Enamel[k].from;
                float d = (f.r - c.r) * (f.r - c.r) + (f.g - c.g) * (f.g - c.g) + (f.b - c.b) * (f.b - c.b);
                if (d < bd) { bd = d; best = k; }
            }
            var (from, to) = Enamel[best];
            float shade = (c.r + c.g + c.b) / Mathf.Max(0.05f, from.r + from.g + from.b);
            return to * Mathf.Clamp(shade, 0.6f, 1.4f);
        }

        /// <summary>(albedo, (metallic, roughness, wear, 0)) — the albedo in sRGB.</summary>
        public static (Color albedo, Vector4 custom) Params(ItemDefinition def)
        {
            var p = Materials.TryGetValue(def.material, out var m) ? m : Materials["steel"];
            if (def.material == "painted") p.albedo = ItemSkins.Applies(def) ? ItemSkins.PaintOf(def) : Vintage(def.variantColor) * 0.85f;   // skins: industrial enamel; robot parts: worn enamel
            if (def.material == "electronic") p.albedo = def.variantColor;
            switch (def.finish)
            {
                case "zinc": p.albedo = new Color(0.42f, 0.44f, 0.46f); p.roughness = 0.45f; break;
                case "black_oxide": p.albedo = new Color(0.2f, 0.2f, 0.21f); p.metallic = 0.25f; p.roughness = 0.5f; break;
                case "polished": p.roughness = Mathf.Max(0.35f, p.roughness - 0.15f); p.wear *= 0.3f; p.albedo *= 1.12f; break;
            }
            if (def.subcategory == "led") p.roughness = 0.15f;
            return (p.albedo, new Vector4(p.metallic, p.roughness, p.wear, 0));
        }

        /// <summary>For the shader's wear: 1 bare metal (rusts), 0.5 painted (chips), 0.05 electronics.</summary>
        public static float SurfaceClass(ItemDefinition def) => def.material == "electronic" ? 0.05f : def.material == "painted" ? 0.5f : 1f;

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
            float wear = Mathf.Clamp01(0.45f + c.z * 0.8f);   // held / inspected: as worn as the parts on the floor
            var (sa, sb) = ItemSkins.For(def, ItemSkins.Applies(def) ? ItemSkins.Condition(def) : wear);
            var key = def.material + "|" + def.finish + "|" + ColorUtility.ToHtmlStringRGB(albedo) + "|" + sa + sb;
            if (_cache.TryGetValue(key, out var m)) return m;
            albedo.a = SurfaceClass(def);
            m = Make(albedo, c.x, c.y, wear);
            if (sa.x > 0.5f) { var b = ItemSkins.Base(def, albedo); b.a = albedo.a; m.SetVector("_BaseColor", b); }   // linear, exactly as the instance buffer
            if (sa.x > 0.5f) m.EnableKeyword("_WW_SKIN");
            m.SetVector("_SkinA", sa);
            m.SetVector("_SkinB", sb);
            m.SetFloat("_Seed", (def.id * 0.618f) % 1f);
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
            m.SetTexture("_Decals", Decals);
            m.enableInstancing = true;
            return m;
        }

        static Material _tin, _accent;
        public static Material Tin => _tin ??= Make(new Color(0.5f, 0.5f, 0.49f), 0.4f, 0.6f, 0.3f);   // tinned pins: dull, not chrome
        public static Material Accent => _accent ??= Make(new Color(1, 1, 1, 0.05f), 0, 0.6f, 0.1f);   // accents: no rust or chips

        /// <summary>Materials for each submesh of an entry (slot 0 = part material).</summary>
        public static Material[] SlotMaterials(MeshLibrary.Entry e, Material primary)
        {
            var (tin, accent) = SlotPair(primary);
            var mats = new Material[e.slots.Length];
            for (int i = 0; i < mats.Length; i++)
                mats[i] = e.slots[i] == 0 ? primary : e.slots[i] == 1 ? tin : accent;
            return mats;
        }

        static readonly Dictionary<Material, (Material tin, Material accent)> _slotPairs = new();

        /// <summary>Tin / accent materials that go with a primary: the shared ones for the old look,
        /// copies carrying the primary's skin (or reading the instance buffer) for skinned parts.
        /// Cached per primary, so still one material per skin, never one per object.</summary>
        static (Material, Material) SlotPair(Material primary)
        {
            // the old look keeps the two shared materials: per-primary copies cost ~70 SetPass calls a frame
            // and ~35 MB even with every part unskinned (measured, docs/SKIN_FIRST_PHASE2_REPORT.md)
            if (!primary.IsKeywordEnabled("_WW_SKIN")) return (Tin, Accent);
            bool instanced = primary.IsKeywordEnabled("_WW_INSTANCED");
            if (_slotPairs.TryGetValue(primary, out var pair)) return pair;
            Material Copy(Material from, float slot)
            {
                var m = new Material(from) { enableInstancing = true };
                m.SetFloat("_Slot", slot);
                m.EnableKeyword("_WW_SKIN");
                if (instanced) m.EnableKeyword("_WW_INSTANCED");
                else { m.SetVector("_SkinA", primary.GetVector("_SkinA")); m.SetVector("_SkinB", primary.GetVector("_SkinB")); m.SetFloat("_Seed", primary.GetFloat("_Seed")); }
                return m;
            }
            pair = (Copy(Tin, 1), Copy(Accent, 2));
            _slotPairs[primary] = pair;
            return pair;
        }

        static Texture2D _decals;
        public static Texture2D Decals => _decals ??= Resources.Load<Texture2D>("ItemSkin/item_decals");
    }
}
