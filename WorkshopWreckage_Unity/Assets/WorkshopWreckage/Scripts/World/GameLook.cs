using System.Collections.Generic;
using UnityEngine;
using WorkshopWreckage.Sandbox;

namespace WorkshopWreckage
{
    /// <summary>The visual sandbox's VARIANT B (iteration 3, tag visual-bp2) in the real game, for playtesting:
    /// its item shader terms, shape meshes, wear levels and light shaping, taken from styles/variant_b.json.
    /// On by default in this playtest build; --look=baseline gives the game's own look (everything below stays
    /// null / 0, exactly as before). Nothing here touches gameplay, placement, physics or robots.</summary>
    public static class GameLook
    {
        public static bool Active { get; private set; }
        /// <summary>Added to the grading exposure by GameSettings.ApplyWorld (0 with the game's own look).</summary>
        public static float ExposureOffset { get; private set; }
        static VisualStyle _style;

        /// <summary>At boot, after WorkshopArtDirectionProfile.Load and before any item is drawn.</summary>
        public static void Init(Dictionary<string, string> args)
        {
            Active = !(args.TryGetValue("look", out var look) && look == "baseline");
            if (!Active) { Debug.Log("[look] baseline (the game's own look)"); return; }
            var ctl = new VisualStyleController();   // captures the art pass's globals as the zero point
            ctl.current = VisualStyleController.VariantB;
            _style = ctl.Style;
            ctl.Apply(null);                         // the shader terms; the room's light is shaped in ApplyRoom
            int quality = ctl.GeometryQuality, version = ctl.GeometryVersion;
            MeshLibrary.Extra = ShapeMeshes.Build;
            ItemRenderer.WorldLod = (key, size) => VisualStyleController.LodFor(key, size, quality, version);
            if (ctl.WearLevels) ItemRenderer.SkinCondition = (w, id, def, cond) => VisualSandbox.WearLevelCondition(def, cond);
            ExposureOffset = -0.15f * Mathf.Clamp01(_style["light_shaping"]);
            Debug.Log($"[look] {_style.name}: geometry quality {quality}, version {version}, wear levels {ctl.WearLevels}");
        }

        /// <summary>After each WorkshopBuilder.Build (fresh lights and volume, so nothing stacks): the style's light
        /// shaping as SandboxLighting.ApplyStyle does it, on the room's own lighting.</summary>
        public static void ApplyRoom(WorkshopBuilder b)
        {
            if (!Active) return;
            float shaping = Mathf.Clamp01(_style["light_shaping"]);
            if (b.colorAdjust != null) b.colorAdjust.contrast.Override(Mathf.Clamp(b.colorAdjust.contrast.value + _style["contrast"], -100, 100));
            RenderSettings.ambientEquatorColor *= 1 + 0.3f * shaping;
            RenderSettings.ambientGroundColor *= 1 + 0.6f * shaping;
            foreach (var l in b.GetComponentsInChildren<Light>(true))
                if (l.shadows != LightShadows.None) l.shadowStrength *= 1 - 0.12f * shaping;
            if (b.sun != null && b.sun.transform.parent != b.transform && b.sun.shadows != LightShadows.None) b.sun.shadowStrength *= 1 - 0.12f * shaping;
        }
    }
}
