using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace WorkshopWreckage
{
    /// <summary>Graphics and control settings (Settings screen), saved in PlayerPrefs ("ww_gfx_*", "ww_ctl_*") and
    /// applied at boot: rendering when loaded, the camera after the player is made, the look after each workshop is
    /// built. A quality preset sets every graphics value at once; changing one of them makes it "Custom". Automated
    /// runs (tests, screenshots, benchmarks) use the defaults (High) and never write the saved values. Display mode
    /// and resolution are Unity's own (it remembers them itself); volumes are in Sfx.</summary>
    public static class GameSettings
    {
        public static readonly string[] PresetNames = { "Low", "Medium", "High", "Ultra", "Custom" };
        public static readonly string[] AaNames = { "Off", "FXAA", "SMAA", "SMAA + MSAA 2x", "SMAA + MSAA 4x" };
        public static readonly string[] ShadowNames = { "Off", "Low", "Medium", "High", "Ultra" };
        public static readonly int[] FpsCaps = { -1, 30, 60, 120, 144, 240 };
        public const int Custom = 4;

        public static int Preset = 2;
        public static float RenderScale = 1f;
        public static int Aa = 3;
        public static int Shadows = 3;
        public static int PartShadows = 1;   // ItemRenderer.ShadowMode
        public static bool Bloom = true, Grain = true, Vignette = true, Shafts = true;
        public static float Brightness;      // -1..1, added to the look's exposure
        public static bool VSync;
        public static int FpsCap;            // index into FpsCaps
        public static float Fov = 72f, Sensitivity = 0.22f;
        public static bool InvertY;
        static bool _readOnly;

        /// <summary>Preset values: render scale, anti-aliasing, shadows, part shadows, film grain, light shafts.</summary>
        static readonly (float scale, int aa, int shadows, int parts, bool grain, bool shafts)[] Presets =
        {
            (0.75f, 1, 1, 2, false, false),   // Low: older PCs and laptops
            (1f, 2, 2, 1, true, true),        // Medium
            (1f, 3, 3, 1, true, true),        // High: the look as designed
            (1f, 4, 4, 0, true, true),        // Ultra
        };

        public static void Load(bool automated)
        {
            _readOnly = automated;
            if (!automated)
            {
                Preset = PlayerPrefs.GetInt("ww_gfx_preset", 2);
                RenderScale = Mathf.Clamp(PlayerPrefs.GetFloat("ww_gfx_scale", 1f), 0.5f, 1f);
                Aa = Mathf.Clamp(PlayerPrefs.GetInt("ww_gfx_aa", 3), 0, AaNames.Length - 1);
                Shadows = Mathf.Clamp(PlayerPrefs.GetInt("ww_gfx_shadows", 3), 0, ShadowNames.Length - 1);
                PartShadows = Mathf.Clamp(PlayerPrefs.GetInt("ww_part_shadows", 1), 0, 2);
                Bloom = PlayerPrefs.GetInt("ww_gfx_bloom", 1) == 1;
                Grain = PlayerPrefs.GetInt("ww_gfx_grain", 1) == 1;
                Vignette = PlayerPrefs.GetInt("ww_gfx_vignette", 1) == 1;
                Shafts = PlayerPrefs.GetInt("ww_gfx_shafts", 1) == 1;
                Brightness = Mathf.Clamp(PlayerPrefs.GetFloat("ww_gfx_brightness", 0f), -1f, 1f);
                VSync = PlayerPrefs.GetInt("ww_gfx_vsync", 0) == 1;
                FpsCap = Mathf.Clamp(PlayerPrefs.GetInt("ww_gfx_fpscap", 0), 0, FpsCaps.Length - 1);
                Fov = Mathf.Clamp(PlayerPrefs.GetFloat("ww_ctl_fov", 72f), 60f, 100f);
                Sensitivity = Mathf.Clamp(PlayerPrefs.GetFloat("ww_ctl_sensitivity", 0.22f), 0.05f, 0.6f);
                InvertY = PlayerPrefs.GetInt("ww_ctl_inverty", 0) == 1;
            }
            ItemRenderer.ShadowMode = PartShadows;
            Debug.Log($"[settings] {PresetNames[Preset]}: scale {RenderScale:0.00}, AA {AaNames[Aa]}, shadows {ShadowNames[Shadows]}, part shadows {ItemRenderer.ShadowModeNames[PartShadows]}, " +
                      $"bloom {Bloom}, grain {Grain}, vignette {Vignette}, shafts {Shafts}, brightness {Brightness:+0.00;-0.00;0}, vsync {VSync}, cap {FpsCaps[FpsCap]}{(automated ? " (automated run: defaults)" : "")}");
            ApplyRendering();
        }

        /// <summary>Back to the defaults (High, the designed look, standard controls).</summary>
        public static void ResetDefaults(Game g)
        {
            SetPreset(g, 2);
            Brightness = 0; Bloom = true; Vignette = true; VSync = false; FpsCap = 0;
            Fov = 72f; Sensitivity = 0.22f; InvertY = false;
            SaveAll();
            ApplyAll(g);
        }

        public static void SetPreset(Game g, int p)
        {
            Preset = p;
            if (p < Custom)
            {
                var v = Presets[p];
                RenderScale = v.scale; Aa = v.aa; Shadows = v.shadows; PartShadows = v.parts; Grain = v.grain; Shafts = v.shafts;
            }
            SaveAll();
            ApplyAll(g);
        }

        /// <summary>A change to one graphics value: applied and saved; a preset value that no longer matches makes it Custom.</summary>
        public static void Changed(Game g)
        {
            if (Preset < Custom)
            {
                var v = Presets[Preset];
                if (!Mathf.Approximately(v.scale, RenderScale) || v.aa != Aa || v.shadows != Shadows || v.parts != PartShadows || v.grain != Grain || v.shafts != Shafts)
                    Preset = Custom;
            }
            SaveAll();
            ApplyAll(g);
        }

        static void SaveAll()
        {
            if (_readOnly) return;
            PlayerPrefs.SetInt("ww_gfx_preset", Preset);
            PlayerPrefs.SetFloat("ww_gfx_scale", RenderScale);
            PlayerPrefs.SetInt("ww_gfx_aa", Aa);
            PlayerPrefs.SetInt("ww_gfx_shadows", Shadows);
            PlayerPrefs.SetInt("ww_part_shadows", PartShadows);
            PlayerPrefs.SetInt("ww_gfx_bloom", Bloom ? 1 : 0);
            PlayerPrefs.SetInt("ww_gfx_grain", Grain ? 1 : 0);
            PlayerPrefs.SetInt("ww_gfx_vignette", Vignette ? 1 : 0);
            PlayerPrefs.SetInt("ww_gfx_shafts", Shafts ? 1 : 0);
            PlayerPrefs.SetFloat("ww_gfx_brightness", Brightness);
            PlayerPrefs.SetInt("ww_gfx_vsync", VSync ? 1 : 0);
            PlayerPrefs.SetInt("ww_gfx_fpscap", FpsCap);
            PlayerPrefs.SetFloat("ww_ctl_fov", Fov);
            PlayerPrefs.SetFloat("ww_ctl_sensitivity", Sensitivity);
            PlayerPrefs.SetInt("ww_ctl_inverty", InvertY ? 1 : 0);
            PlayerPrefs.Save();
        }

        /// <summary>A control value (field of view, sensitivity, invert Y) changed.</summary>
        public static void ControlsChanged(Game g)
        {
            SaveAll();
            ApplyPlayer(g.player);
        }

        public static void ApplyAll(Game g)
        {
            ApplyRendering();
            if (g.player != null) ApplyPlayer(g.player);
            if (g.builder != null) ApplyWorld(g.builder);
        }

        /// <summary>Pipeline-wide: render scale, MSAA, main-light shadow map, frame pacing.</summary>
        public static void ApplyRendering()
        {
            ItemRenderer.ShadowMode = PartShadows;
            QualitySettings.vSyncCount = VSync ? 1 : 0;
            Application.targetFrameRate = VSync ? -1 : FpsCaps[FpsCap];
            if (GraphicsSettings.currentRenderPipeline is not UniversalRenderPipelineAsset urp) return;
            urp.renderScale = RenderScale;
            urp.msaaSampleCount = Aa == 4 ? 4 : Aa == 3 ? 2 : 1;
            var (res, dist, cascades) = Shadows switch
            {
                1 => (1024, 15f, 1),
                2 => (2048, 22f, 2),
                4 => (8192, 40f, 4),
                _ => (4096, 30f, 4),
            };
            urp.mainLightShadowmapResolution = res;
            urp.additionalLightsShadowmapResolution = Mathf.Min(res, 4096);
            urp.shadowDistance = dist;
            urp.shadowCascadeCount = cascades;
        }

        public static void ApplyPlayer(PlayerController p)
        {
            p.fov = Fov;
            p.cam.fieldOfView = Fov;
            p.sensitivity = Sensitivity;
            p.invertY = InvertY;
            var data = p.cam.GetComponent<UniversalAdditionalCameraData>();
            if (data != null)
                data.antialiasing = Aa == 0 ? AntialiasingMode.None : Aa == 1 ? AntialiasingMode.FastApproximateAntialiasing : AntialiasingMode.SubpixelMorphologicalAntiAliasing;
        }

        /// <summary>The workshop's look: post effects, brightness, light shafts, which lights cast shadows (Low: hard).</summary>
        public static void ApplyWorld(WorkshopBuilder b)
        {
            if (b.bloom != null) b.bloom.active = Bloom;
            if (b.grain != null) b.grain.active = Grain;
            if (b.vignette != null) b.vignette.active = Vignette;
            if (b.colorAdjust != null) b.colorAdjust.postExposure.Override(WorkshopBuilder.Exposure + Brightness);
            foreach (var s in b.shafts) if (s != null) s.SetActive(Shafts);
            b.shadowLights ??= b.GetComponentsInChildren<Light>(true).Where(l => l.shadows != LightShadows.None).ToList();
            foreach (var l in b.shadowLights)
                if (l != null) l.shadows = Shadows == 0 ? LightShadows.None : Shadows == 1 ? LightShadows.Hard : LightShadows.Soft;
        }
    }
}
