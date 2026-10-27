using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace WorkshopWreckage
{
    /// <summary>The workshop's art direction (docs/ART_DIRECTION_PASS.md), from shared/data/art_direction.json:
    /// lighting, grading, environment colours and the item shading. The file holds the targets; each call site
    /// passes the value it had before the art pass, and `stylization` blends between the two (0 = the old look,
    /// 1 = the targets), so the whole pass can be dialled back or switched off without code changes. QA:
    /// --artlook=0..1 overrides it. Gameplay colours (category drawers, labels, item colours) never come from here.</summary>
    public static class WorkshopArtDirectionProfile
    {
        public static float Stylization { get; private set; }
        static Dictionary<string, object> _data = new();

        public static void Load(Dictionary<string, string> args)
        {
            var path = Shared.DataPath("art_direction.json");
            _data = File.Exists(path) ? Shared.ReadObject(path) : new Dictionary<string, object>();
            Stylization = Mathf.Clamp01((float)Json.Num(_data, "stylization", File.Exists(path) ? 1 : 0));
            if (args.TryGetValue("artlook", out var a))
                Stylization = Mathf.Clamp01(float.Parse(a, System.Globalization.CultureInfo.InvariantCulture));
            Debug.Log($"[art] stylization {Stylization:0.00}{(File.Exists(path) ? "" : " (art_direction.json missing: the look before the art pass)")}");
            ApplyItemGlobals();
        }

        static object Raw(string section, string key) => Json.Obj(_data.GetValueOrDefault(section))?.GetValueOrDefault(key);

        /// <summary>The value for `section.key`: the old value blended towards the profile's target.</summary>
        public static float F(string section, string key, float legacy) =>
            Raw(section, key) is double d ? Mathf.Lerp(legacy, (float)d, Stylization) : legacy;

        public static Color C(string section, string key, Color legacy)
        {
            if (Raw(section, key) is not List<object> a || a.Count < 3) return legacy;
            return Color.Lerp(legacy, new Color(Json.F(a[0]), Json.F(a[1]), Json.F(a[2])), Stylization);
        }

        /// <summary>A switch: on from half stylization.</summary>
        public static bool B(string section, string key) => Raw(section, key) is bool b && b && Stylization >= 0.5f;

        /// <summary>Item shader globals (WWItem.shader): 0 is the look before the art pass.</summary>
        static void ApplyItemGlobals()
        {
            Shader.SetGlobalFloat("_WWArtSat", F("items", "saturation", 0));
            Shader.SetGlobalFloat("_WWArtRim", F("items", "rim", 0));
            Shader.SetGlobalColor("_WWArtRimColor", C("items", "rim_color", Color.white));
            Shader.SetGlobalFloat("_WWArtAmbient", F("items", "ambient", 0));
            Shader.SetGlobalFloat("_WWArtWear", F("items", "wear_reduction", 0));
            Shader.SetGlobalFloat("_WWArtGloss", F("items", "gloss", 0));
            Shader.SetGlobalFloat("_WWArtWash", F("items", "cavity_wash", 0));
            Shader.SetGlobalColor("_WWArtWashColor", C("items", "wash_color", Color.white));
            Shader.SetGlobalFloat("_WWArtGrime", F("items", "grime", 0));
            Shader.SetGlobalColor("_WWArtGrimeColor", C("items", "grime_color", Color.white));
            Shader.SetGlobalFloat("_WWArtEdgeWear", F("items", "edge_wear", 0));
            Shader.SetGlobalFloat("_WWArtEdge", F("items", "edge_highlight", 0));
            Shader.SetGlobalFloat("_WWArtRough", F("items", "paint_roughness", 0));
            Shader.SetGlobalFloat("_WWArtMetal", F("items", "metallic_response", 0));
            Shader.SetGlobalFloat("_WWArtWashEdge", F("items", "wash_definition", 0));
            Shader.SetGlobalFloat("_WWArtChipSize", F("items", "chip_size", 1));
        }
    }
}
