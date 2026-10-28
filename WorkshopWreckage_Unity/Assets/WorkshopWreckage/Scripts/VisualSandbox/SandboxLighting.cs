using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace WorkshopWreckage.Sandbox
{
    /// <summary>The sandbox's lighting presets. They own their lights, ambient, fog and grading volume and never
    /// touch a material. The room is the game's own (WorkshopBuilder.BuildShell, built once): WORKSHOP is its
    /// lighting exactly as in the game (sun through the windows, every ceiling fixture, ambient, fog, shafts and
    /// the grading, with the player's graphics settings); the other presets switch that off and light the same
    /// room with lights of their own, the room's walls and ceiling then casting no shadows.</summary>
    public class SandboxLighting : MonoBehaviour
    {
        public static readonly (string id, string name)[] Presets = { ("neutral_studio", "NEUTRAL STUDIO"), ("workshop", "WORKSHOP"), ("high_contrast", "HIGH CONTRAST") };

        public int current = -1;
        readonly List<GameObject> _owned = new();
        readonly List<(Light light, LightShadows shadows, float strength)> _shadowLights = new();
        ColorAdjustments _grade;
        float _baseContrast, _basePost;
        Color _setSky, _setEq, _setGround;   // the preset's ambient, before the style's light shaping
        Material _skybox;

        // the room's own lighting, as BuildShell and GameSettings.ApplyWorld left it
        WorkshopBuilder _room;
        Material _roomSky;
        float _roomContrast, _roomPost;
        readonly List<(GameObject go, bool active)> _roomLights = new();
        readonly List<(Light light, LightShadows shadows, float strength)> _roomShadowLights = new();
        readonly List<(MeshRenderer r, ShadowCastingMode mode)> _roomRenderers = new();
        AmbientMode _ambMode;
        Color _ambSky, _ambEq, _ambGround, _fogColor;
        bool _fog;
        FogMode _fogMode;
        float _fogDensity, _reflection;

        public string Name => current >= 0 ? Presets[current].name : "";

        /// <summary>`originalSky` is the skybox before the room's lighting tinted a copy of it.</summary>
        public void Init(WorkshopBuilder room, Material originalSky)
        {
            _skybox = originalSky;
            _room = room;
            _roomSky = RenderSettings.skybox;
            _ambMode = RenderSettings.ambientMode;
            _ambSky = RenderSettings.ambientSkyColor; _ambEq = RenderSettings.ambientEquatorColor; _ambGround = RenderSettings.ambientGroundColor;
            _fog = RenderSettings.fog; _fogMode = RenderSettings.fogMode; _fogColor = RenderSettings.fogColor; _fogDensity = RenderSettings.fogDensity;
            _reflection = RenderSettings.reflectionIntensity;
            _roomContrast = room.colorAdjust != null ? room.colorAdjust.contrast.value : 0;
            _roomPost = room.colorAdjust != null ? room.colorAdjust.postExposure.value : 0;
            var lights = new List<GameObject> { room.sun.gameObject, room.transform.Find("PostProcess")?.gameObject };
            foreach (var l in room.lamps) lights.Add(l.gameObject);
            lights.AddRange(room.shafts);
            foreach (var go in lights) if (go != null) _roomLights.Add((go, go.activeSelf));
            foreach (var l in room.GetComponentsInChildren<Light>(true))
                if (l.shadows != LightShadows.None) _roomShadowLights.Add((l, l.shadows, l.shadowStrength));
            if (room.sun.shadows != LightShadows.None) _roomShadowLights.Add((room.sun, room.sun.shadows, room.sun.shadowStrength));   // at the scene root
            foreach (var r in room.GetComponentsInChildren<MeshRenderer>(true)) _roomRenderers.Add((r, r.shadowCastingMode));
        }

        public void Set(int preset)
        {
            Clear();
            current = Mathf.Clamp(preset, 0, Presets.Length - 1);
            bool workshop = Presets[current].id == "workshop";
            foreach (var (go, active) in _roomLights) go.SetActive(workshop && active);
            foreach (var (r, mode) in _roomRenderers) r.shadowCastingMode = workshop ? mode : ShadowCastingMode.Off;
            switch (Presets[current].id)
            {
                case "workshop": Workshop(); break;
                case "high_contrast": HighContrast(); break;
                default: Studio(); break;
            }
            _setSky = RenderSettings.ambientSkyColor; _setEq = RenderSettings.ambientEquatorColor; _setGround = RenderSettings.ambientGroundColor;
        }

        void Clear()
        {
            foreach (var go in _owned) if (go != null) Destroy(go);
            _owned.Clear();
            _shadowLights.Clear();
            _grade = null;
            RenderSettings.skybox = _skybox;
        }

        Light Dir(string name, Color c, float intensity, float elevation, float azimuth, LightShadows shadows, float strength = 1)
        {
            var go = new GameObject(name);
            go.transform.SetParent(transform, false);
            go.transform.rotation = Quaternion.Euler(elevation, azimuth, 0);
            var l = go.AddComponent<Light>();
            l.type = LightType.Directional;
            l.color = c;
            l.intensity = intensity;
            l.shadows = shadows;
            l.shadowStrength = strength;
            _owned.Add(go);
            if (shadows != LightShadows.None) _shadowLights.Add((l, shadows, strength));
            return l;
        }

        /// <summary>A grading volume: tonemapping, exposure and the contrast the style offsets.</summary>
        void Grade(float exposure, float contrast, float saturation, TonemappingMode tm, float bloom)
        {
            var go = new GameObject("SandboxGrade");
            go.transform.SetParent(transform, false);
            var vol = go.AddComponent<Volume>();
            vol.isGlobal = true;
            vol.priority = 10;
            var p = ScriptableObject.CreateInstance<VolumeProfile>();
            p.Add<Tonemapping>(true).mode.Override(tm);
            var ca = p.Add<ColorAdjustments>(true);
            ca.postExposure.Override(exposure);
            ca.contrast.Override(contrast);
            ca.saturation.Override(saturation);
            var b = p.Add<Bloom>(true);
            b.intensity.Override(bloom);
            b.threshold.Override(1f);
            vol.profile = p;
            _grade = ca;
            _baseContrast = contrast;
            _basePost = exposure;
            _owned.Add(go);
        }

        void Studio()
        {
            RenderSettings.sun = Dir("Key", new Color(1f, 0.98f, 0.95f), 2.4f, 48, -35, LightShadows.Soft, 0.85f);   // URP's main (shadowed) light
            Dir("Fill", new Color(0.9f, 0.95f, 1f), 0.7f, 25, 150, LightShadows.None);
            Dir("Top", Color.white, 0.5f, 85, 0, LightShadows.None);
            RenderSettings.ambientMode = AmbientMode.Trilight;
            RenderSettings.ambientSkyColor = new Color(0.42f, 0.43f, 0.45f);
            RenderSettings.ambientEquatorColor = new Color(0.3f, 0.3f, 0.3f);
            RenderSettings.ambientGroundColor = new Color(0.16f, 0.16f, 0.16f);
            RenderSettings.fog = false;
            RenderSettings.reflectionIntensity = 0.45f;
            Grade(0.6f, 0, 0, TonemappingMode.Neutral, 0.2f);
        }

        void HighContrast()
        {
            RenderSettings.sun = Dir("Key", new Color(1f, 0.96f, 0.9f), 4.2f, 32, -60, LightShadows.Hard, 1f);
            Dir("Rim", new Color(0.7f, 0.82f, 1f), 2.6f, 20, 140, LightShadows.None);
            RenderSettings.ambientMode = AmbientMode.Trilight;
            RenderSettings.ambientSkyColor = new Color(0.08f, 0.09f, 0.11f);
            RenderSettings.ambientEquatorColor = new Color(0.04f, 0.04f, 0.04f);
            RenderSettings.ambientGroundColor = new Color(0.01f, 0.01f, 0.01f);
            RenderSettings.fog = false;
            RenderSettings.reflectionIntensity = 0.25f;
            Grade(0.4f, 22, 4, TonemappingMode.ACES, 0.5f);
        }

        /// <summary>The room's own lighting, put back as the game set it.</summary>
        void Workshop()
        {
            RenderSettings.skybox = _roomSky;
            RenderSettings.sun = _room.sun;
            RenderSettings.ambientMode = _ambMode;
            RenderSettings.ambientSkyColor = _ambSky; RenderSettings.ambientEquatorColor = _ambEq; RenderSettings.ambientGroundColor = _ambGround;
            RenderSettings.fog = _fog; RenderSettings.fogMode = _fogMode; RenderSettings.fogColor = _fogColor; RenderSettings.fogDensity = _fogDensity;
            RenderSettings.reflectionIntensity = _reflection;
            _shadowLights.AddRange(_roomShadowLights);
            _grade = _room.colorAdjust;
            _baseContrast = _roomContrast;
            _basePost = _roomPost;
        }

        /// <summary>The style's share: contrast on top of the preset's grading; shadow softness -1 hard .. 0 the
        /// preset's own .. +1 the softest filter and a little lighter; light shaping 0 .. 1 (Variant B) lifts the
        /// ambient's equator and ground (detail in shadows and undersides, same hues, so the warmth stays), lightens
        /// shadows a little and pulls the exposure back so highlights hold.</summary>
        public void ApplyStyle(float contrast, float softness, float shaping = 0)
        {
            shaping = Mathf.Clamp01(shaping);
            if (_grade != null)
            {
                _grade.contrast.Override(Mathf.Clamp(_baseContrast + contrast, -100, 100));
                _grade.postExposure.Override(_basePost - 0.15f * shaping);
            }
            RenderSettings.ambientSkyColor = _setSky;
            RenderSettings.ambientEquatorColor = _setEq * (1 + 0.3f * shaping);
            RenderSettings.ambientGroundColor = _setGround * (1 + 0.6f * shaping);
            foreach (var (l, shadows, strength) in _shadowLights)
            {
                if (l == null) continue;
                l.shadows = softness < -0.5f ? LightShadows.Hard : softness > 0.01f ? LightShadows.Soft : shadows;
                l.shadowStrength = strength * (1 - 0.3f * Mathf.Max(0, softness)) * (1 - 0.12f * shaping);
                var data = l.GetComponent<UniversalAdditionalLightData>() ?? l.gameObject.AddComponent<UniversalAdditionalLightData>();
                data.softShadowQuality = softness > 0.5f ? SoftShadowQuality.High : softness > 0.01f ? SoftShadowQuality.Medium : SoftShadowQuality.UsePipelineSettings;
            }
        }
    }
}
